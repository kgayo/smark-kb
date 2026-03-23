#!/usr/bin/env python3
"""
Terraform ↔ ARM template parity checker.

Compares resource inventories between Terraform .tf files and ARM main.json
to detect drift between the two IaC definitions. Designed to run in CI.

Exit codes:
  0 — parity OK (warnings may be present)
  1 — parity errors detected
  2 — file/parse error
"""

import json
import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Terraform → ARM resource type mapping
# ---------------------------------------------------------------------------
TF_TO_ARM_TYPE: dict[str, str] = {
    "azurerm_resource_group": "",  # not in ARM (deployment scope)
    "azurerm_service_plan": "Microsoft.Web/serverfarms",
    "azurerm_linux_web_app": "Microsoft.Web/sites",
    "azurerm_mssql_server": "Microsoft.Sql/servers",
    "azurerm_mssql_database": "Microsoft.Sql/servers/databases",
    "azurerm_mssql_firewall_rule": "Microsoft.Sql/servers/firewallRules",
    "azurerm_storage_account": "Microsoft.Storage/storageAccounts",
    "azurerm_storage_container": "Microsoft.Storage/storageAccounts/blobServices/containers",
    "azurerm_servicebus_namespace": "Microsoft.ServiceBus/namespaces",
    "azurerm_servicebus_queue": "Microsoft.ServiceBus/namespaces/queues",
    "azurerm_key_vault": "Microsoft.KeyVault/vaults",
    "azurerm_search_service": "Microsoft.Search/searchServices",
    "azurerm_log_analytics_workspace": "Microsoft.OperationalInsights/workspaces",
    "azurerm_application_insights": "Microsoft.Insights/components",
    "azurerm_monitor_action_group": "Microsoft.Insights/actionGroups",
    "azurerm_monitor_metric_alert": "Microsoft.Insights/metricAlerts",
    "azurerm_role_assignment": "Microsoft.Authorization/roleAssignments",
    "azurerm_static_web_app": "Microsoft.Web/staticSites",
    "azurerm_user_assigned_identity": "Microsoft.ManagedIdentity/userAssignedIdentities",
}

# ARM types that exist as intermediate plumbing (no direct TF equivalent needed)
ARM_INTERMEDIATE_TYPES: set[str] = {
    "Microsoft.Storage/storageAccounts/blobServices",
    "Microsoft.Sql/servers/administrators",
}

# Terraform resource types intentionally excluded from ARM (deployment scope)
TF_EXCLUDED_TYPES: set[str] = {
    "azurerm_resource_group",
}


# ---------------------------------------------------------------------------
# Data structures
# ---------------------------------------------------------------------------
@dataclass
class TerraformResource:
    resource_type: str
    name: str
    file: str
    line: int


@dataclass
class ArmResource:
    resource_type: str
    name_expr: str
    is_child: bool = False


@dataclass
class ParityIssue:
    severity: str  # "error" or "warning"
    category: str
    message: str


@dataclass
class ParityReport:
    terraform_resources: list[TerraformResource] = field(default_factory=list)
    arm_resources: list[ArmResource] = field(default_factory=list)
    issues: list[ParityIssue] = field(default_factory=list)
    tf_type_counts: dict[str, int] = field(default_factory=dict)
    arm_type_counts: dict[str, int] = field(default_factory=dict)

    @property
    def has_errors(self) -> bool:
        return any(i.severity == "error" for i in self.issues)

    @property
    def error_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == "error")

    @property
    def warning_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == "warning")


# ---------------------------------------------------------------------------
# Parsers
# ---------------------------------------------------------------------------
_TF_RESOURCE_RE = re.compile(
    r'^resource\s+"([^"]+)"\s+"([^"]+)"\s*\{', re.MULTILINE
)

_TF_INFRA_VERSION_RE = re.compile(
    r'variable\s+"infra_version"\s*\{[^}]*default\s*=\s*"([^"]+)"', re.DOTALL
)


def parse_terraform_resources(tf_dir: str | Path) -> list[TerraformResource]:
    """Extract resource blocks from all .tf files in a directory."""
    resources: list[TerraformResource] = []
    tf_path = Path(tf_dir)
    if not tf_path.is_dir():
        raise FileNotFoundError(f"Terraform directory not found: {tf_dir}")

    for tf_file in sorted(tf_path.glob("*.tf")):
        content = tf_file.read_text(encoding="utf-8")
        for match in _TF_RESOURCE_RE.finditer(content):
            line_num = content[: match.start()].count("\n") + 1
            resources.append(
                TerraformResource(
                    resource_type=match.group(1),
                    name=match.group(2),
                    file=tf_file.name,
                    line=line_num,
                )
            )
    return resources


def parse_arm_resources(
    arm_path: str | Path,
) -> list[ArmResource]:
    """Extract resources (including nested children) from ARM template."""
    arm_file = Path(arm_path)
    if not arm_file.is_file():
        raise FileNotFoundError(f"ARM template not found: {arm_path}")

    with open(arm_file, encoding="utf-8") as f:
        template = json.load(f)

    resources: list[ArmResource] = []
    _walk_arm_resources(template.get("resources", []), resources, is_child=False)
    return resources


def _walk_arm_resources(
    resource_list: list[dict],
    out: list[ArmResource],
    is_child: bool,
) -> None:
    for res in resource_list:
        res_type = res.get("type", "")
        name_expr = res.get("name", "")
        out.append(ArmResource(resource_type=res_type, name_expr=name_expr, is_child=is_child))
        # Recurse into nested resources
        for child in res.get("resources", []):
            child_type = child.get("type", "")
            # ARM child types are relative; qualify them
            if "/" not in child_type or not child_type.startswith("Microsoft."):
                qualified = f"{res_type}/{child_type}"
            else:
                qualified = child_type
            child_copy = dict(child)
            child_copy["type"] = qualified
            _walk_arm_resources([child_copy], out, is_child=True)


# ---------------------------------------------------------------------------
# Parity comparison
# ---------------------------------------------------------------------------
def check_parity(
    tf_resources: list[TerraformResource],
    arm_resources: list[ArmResource],
) -> ParityReport:
    """Compare Terraform and ARM resource inventories and return a report."""
    report = ParityReport(
        terraform_resources=tf_resources,
        arm_resources=arm_resources,
    )

    # Build type → count maps
    tf_counts: dict[str, int] = {}
    for r in tf_resources:
        tf_counts[r.resource_type] = tf_counts.get(r.resource_type, 0) + 1
    report.tf_type_counts = tf_counts

    arm_counts: dict[str, int] = {}
    for r in arm_resources:
        arm_counts[r.resource_type] = arm_counts.get(r.resource_type, 0) + 1
    report.arm_type_counts = arm_counts

    # Map TF types to expected ARM types and compare counts
    tf_arm_expected: dict[str, int] = {}  # ARM type → expected count from TF
    unmapped_tf_types: list[str] = []

    for tf_type, count in tf_counts.items():
        if tf_type in TF_EXCLUDED_TYPES:
            continue
        arm_type = TF_TO_ARM_TYPE.get(tf_type)
        if arm_type is None:
            unmapped_tf_types.append(tf_type)
            continue
        if arm_type == "":
            continue  # explicitly excluded
        tf_arm_expected[arm_type] = tf_arm_expected.get(arm_type, 0) + count

    # Check for unmapped TF types
    for tf_type in unmapped_tf_types:
        report.issues.append(
            ParityIssue(
                severity="warning",
                category="unmapped_type",
                message=f"Terraform type '{tf_type}' has no known ARM mapping — add to TF_TO_ARM_TYPE",
            )
        )

    # Compare expected (from TF) vs actual (from ARM)
    all_arm_types = set(arm_counts.keys()) | set(tf_arm_expected.keys())

    for arm_type in sorted(all_arm_types):
        if arm_type in ARM_INTERMEDIATE_TYPES:
            continue  # skip plumbing types

        expected = tf_arm_expected.get(arm_type, 0)
        actual = arm_counts.get(arm_type, 0)

        if expected > 0 and actual == 0:
            report.issues.append(
                ParityIssue(
                    severity="error",
                    category="missing_in_arm",
                    message=f"ARM template missing {expected} '{arm_type}' resource(s) that exist in Terraform",
                )
            )
        elif actual > 0 and expected == 0:
            # ARM has resources not tracked in TF
            if arm_type not in ARM_INTERMEDIATE_TYPES:
                report.issues.append(
                    ParityIssue(
                        severity="error",
                        category="missing_in_terraform",
                        message=f"Terraform missing {actual} '{arm_type}' resource(s) that exist in ARM template",
                    )
                )
        elif expected != actual:
            report.issues.append(
                ParityIssue(
                    severity="error",
                    category="count_mismatch",
                    message=f"'{arm_type}' count mismatch: Terraform expects {expected}, ARM has {actual}",
                )
            )

    # Check parameter parity (output/variable counts as informational)
    return report


# ---------------------------------------------------------------------------
# ARM security hardening validation
# ---------------------------------------------------------------------------

# Required security properties for ARM resource types.
# Each entry: (json_path_parts, expected_value, description)
_ARM_SECURITY_RULES: list[tuple[str, list[tuple[list[str], object, str]]]] = [
    (
        "Microsoft.Web/sites",
        [
            (["properties", "httpsOnly"], True, "httpsOnly must be true"),
            (["identity", "type"], "SystemAssigned", "must use SystemAssigned identity"),
        ],
    ),
    (
        "Microsoft.Storage/storageAccounts",
        [
            (["properties", "minimumTlsVersion"], "TLS1_2", "must enforce TLS 1.2"),
            (["properties", "supportsHttpsTrafficOnly"], True, "must require HTTPS-only traffic"),
        ],
    ),
    (
        "Microsoft.KeyVault/vaults",
        [
            (["properties", "enableRbacAuthorization"], True, "must use RBAC authorization"),
            (["properties", "enableSoftDelete"], True, "must have soft-delete enabled"),
        ],
    ),
    (
        "Microsoft.Search/searchServices",
        [
            (["identity", "type"], "SystemAssigned", "must use SystemAssigned identity"),
        ],
    ),
]


def _get_nested(obj: dict, keys: list[str]) -> object:
    """Walk a dict by key path, returning None on missing keys.

    If an intermediate value is an ARM expression string (starts with '['),
    returns that string so callers can do substring matching.
    """
    current = obj
    for k in keys:
        if not isinstance(current, dict) or k not in current:
            return None
        current = current[k]
        # ARM conditional expressions are strings like "[if(...)]"
        if isinstance(current, str) and current.startswith("[") and keys[-1] != k:
            return current  # return the expression for caller to inspect
    return current


def check_arm_security_hardening(arm_path: str | Path) -> list[ParityIssue]:
    """Validate ARM template resources meet security hardening requirements."""
    arm_file = Path(arm_path)
    if not arm_file.is_file():
        raise FileNotFoundError(f"ARM template not found: {arm_path}")

    with open(arm_file, encoding="utf-8") as f:
        template = json.load(f)

    issues: list[ParityIssue] = []
    resources = template.get("resources", [])

    for rule_type, checks in _ARM_SECURITY_RULES:
        matching = [r for r in resources if r.get("type") == rule_type]
        for res in matching:
            name = res.get("name", "<unnamed>")
            for path_keys, expected, desc in checks:
                actual = _get_nested(res, path_keys)
                # Handle ARM conditional expressions (e.g. "[if(...)]" strings).
                # When the value is an ARM expression string, check if it contains
                # the expected value (the property is expression-driven, not static).
                if actual != expected:
                    if isinstance(actual, str) and actual.startswith("[") and isinstance(expected, str) and expected in actual:
                        continue  # ARM expression contains the expected value
                    issues.append(
                        ParityIssue(
                            severity="error",
                            category="security_hardening",
                            message=(
                                f"{rule_type} '{name}': {desc} "
                                f"(expected={expected!r}, actual={actual!r})"
                            ),
                        )
                    )

    # Validate blob containers have no public access
    for res in resources:
        if res.get("type") == "Microsoft.Storage/storageAccounts/blobServices/containers":
            access = _get_nested(res, ["properties", "publicAccess"])
            if access != "None":
                issues.append(
                    ParityIssue(
                        severity="error",
                        category="security_hardening",
                        message=f"Blob container '{res.get('name', '')}': publicAccess must be 'None' (actual={access!r})",
                    )
                )

    # Validate Service Bus queue has dead-lettering enabled
    for res in resources:
        if res.get("type") == "Microsoft.ServiceBus/namespaces/queues":
            dlq = _get_nested(res, ["properties", "deadLetteringOnMessageExpiration"])
            if dlq is not True:
                issues.append(
                    ParityIssue(
                        severity="error",
                        category="security_hardening",
                        message=f"Queue '{res.get('name', '')}': deadLetteringOnMessageExpiration must be true",
                    )
                )

    return issues


# ---------------------------------------------------------------------------
# CMK parity check (P3-030)
# ---------------------------------------------------------------------------

_TF_CMK_ENABLE_RE = re.compile(
    r'variable\s+"enable_cmk"\s*\{[^}]*default\s*=\s*(true|false)', re.DOTALL
)

_TF_CMK_KEY_RE = re.compile(
    r'variable\s+"cmk_key_vault_key_id"\s*\{', re.DOTALL
)


def check_cmk_parity(
    tf_dir: str | Path,
    arm_path: str | Path,
) -> list[ParityIssue]:
    """Verify CMK configuration parity between Terraform and ARM."""
    issues: list[ParityIssue] = []

    # Check Terraform has enable_cmk variable
    tf_path = Path(tf_dir)
    variables_file = tf_path / "variables.tf"
    tf_has_cmk_var = False
    tf_has_cmk_key_var = False
    if variables_file.is_file():
        content = variables_file.read_text(encoding="utf-8")
        tf_has_cmk_var = bool(_TF_CMK_ENABLE_RE.search(content))
        tf_has_cmk_key_var = bool(_TF_CMK_KEY_RE.search(content))

    # Check ARM has enableCmk parameter
    arm_file = Path(arm_path)
    arm_has_cmk_param = False
    arm_has_cmk_key_param = False
    if arm_file.is_file():
        with open(arm_file, encoding="utf-8") as f:
            template = json.load(f)
        params = template.get("parameters", {})
        arm_has_cmk_param = "enableCmk" in params
        arm_has_cmk_key_param = "cmkKeyVaultKeyId" in params

    # Both must have CMK variables or neither
    if tf_has_cmk_var and not arm_has_cmk_param:
        issues.append(
            ParityIssue(
                severity="error",
                category="cmk_parity",
                message="Terraform has enable_cmk variable but ARM template missing enableCmk parameter",
            )
        )
    elif arm_has_cmk_param and not tf_has_cmk_var:
        issues.append(
            ParityIssue(
                severity="error",
                category="cmk_parity",
                message="ARM template has enableCmk parameter but Terraform missing enable_cmk variable",
            )
        )

    if tf_has_cmk_key_var and not arm_has_cmk_key_param:
        issues.append(
            ParityIssue(
                severity="error",
                category="cmk_parity",
                message="Terraform has cmk_key_vault_key_id variable but ARM template missing cmkKeyVaultKeyId parameter",
            )
        )
    elif arm_has_cmk_key_param and not tf_has_cmk_key_var:
        issues.append(
            ParityIssue(
                severity="error",
                category="cmk_parity",
                message="ARM template has cmkKeyVaultKeyId parameter but Terraform missing cmk_key_vault_key_id variable",
            )
        )

    # Check that CMK identity resource exists in both when CMK vars are present
    if tf_has_cmk_var:
        cmk_tf = tf_path / "cmk.tf"
        if not cmk_tf.is_file():
            issues.append(
                ParityIssue(
                    severity="error",
                    category="cmk_parity",
                    message="Terraform enable_cmk variable exists but cmk.tf file not found",
                )
            )

    return issues


# ---------------------------------------------------------------------------
# Version parity check
# ---------------------------------------------------------------------------
def check_version_parity(
    tf_dir: str | Path,
    arm_path: str | Path,
) -> list[ParityIssue]:
    """Verify that infra_version in Terraform matches metadata.infraVersion in ARM."""
    issues: list[ParityIssue] = []
    tf_version: str | None = None
    arm_version: str | None = None

    # Extract Terraform infra_version default
    tf_path = Path(tf_dir)
    variables_file = tf_path / "variables.tf"
    if variables_file.is_file():
        content = variables_file.read_text(encoding="utf-8")
        match = _TF_INFRA_VERSION_RE.search(content)
        if match:
            tf_version = match.group(1)
        else:
            issues.append(
                ParityIssue(
                    severity="warning",
                    category="version_missing",
                    message="Terraform variables.tf missing infra_version variable with default",
                )
            )
    else:
        issues.append(
            ParityIssue(
                severity="warning",
                category="version_missing",
                message="Terraform variables.tf not found",
            )
        )

    # Extract ARM metadata.infraVersion
    arm_file = Path(arm_path)
    if arm_file.is_file():
        with open(arm_file, encoding="utf-8") as f:
            template = json.load(f)
        metadata = template.get("metadata", {})
        arm_version = metadata.get("infraVersion")
        if arm_version is None:
            issues.append(
                ParityIssue(
                    severity="warning",
                    category="version_missing",
                    message="ARM template missing metadata.infraVersion field",
                )
            )

    # Compare
    if tf_version and arm_version and tf_version != arm_version:
        issues.append(
            ParityIssue(
                severity="error",
                category="version_mismatch",
                message=(
                    f"Infrastructure version mismatch: "
                    f"Terraform infra_version={tf_version!r}, "
                    f"ARM metadata.infraVersion={arm_version!r}"
                ),
            )
        )

    return issues


# ---------------------------------------------------------------------------
# Output formatting
# ---------------------------------------------------------------------------
def format_report(report: ParityReport, verbose: bool = False) -> str:
    lines: list[str] = []
    lines.append("=" * 60)
    lines.append("  Terraform ↔ ARM Parity Report")
    lines.append("=" * 60)
    lines.append("")

    # Summary counts
    tf_core = sum(
        c for t, c in report.tf_type_counts.items() if t not in TF_EXCLUDED_TYPES
    )
    arm_core = sum(
        c for t, c in report.arm_type_counts.items() if t not in ARM_INTERMEDIATE_TYPES
    )
    lines.append(f"  Terraform resources: {len(report.terraform_resources)} ({tf_core} mapped)")
    lines.append(f"  ARM resources:       {len(report.arm_resources)} ({arm_core} core)")
    lines.append("")

    if verbose:
        lines.append("  Terraform type counts:")
        for t in sorted(report.tf_type_counts):
            lines.append(f"    {t}: {report.tf_type_counts[t]}")
        lines.append("")
        lines.append("  ARM type counts:")
        for t in sorted(report.arm_type_counts):
            lines.append(f"    {t}: {report.arm_type_counts[t]}")
        lines.append("")

    # Issues
    errors = [i for i in report.issues if i.severity == "error"]
    warnings = [i for i in report.issues if i.severity == "warning"]

    if errors:
        lines.append(f"  ERRORS ({len(errors)}):")
        for issue in errors:
            lines.append(f"    [{issue.category}] {issue.message}")
        lines.append("")

    if warnings:
        lines.append(f"  WARNINGS ({len(warnings)}):")
        for issue in warnings:
            lines.append(f"    [{issue.category}] {issue.message}")
        lines.append("")

    if not report.issues:
        lines.append("  RESULT: PASS — Terraform and ARM templates are in parity")
    elif report.has_errors:
        lines.append(f"  RESULT: FAIL — {report.error_count} error(s), {report.warning_count} warning(s)")
    else:
        lines.append(f"  RESULT: PASS (with warnings) — {report.warning_count} warning(s)")

    lines.append("=" * 60)
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# JSON output for programmatic consumption
# ---------------------------------------------------------------------------
def report_to_dict(report: ParityReport) -> dict:
    return {
        "terraform_resource_count": len(report.terraform_resources),
        "arm_resource_count": len(report.arm_resources),
        "tf_type_counts": report.tf_type_counts,
        "arm_type_counts": report.arm_type_counts,
        "issues": [
            {
                "severity": i.severity,
                "category": i.category,
                "message": i.message,
            }
            for i in report.issues
        ],
        "has_errors": report.has_errors,
        "error_count": report.error_count,
        "warning_count": report.warning_count,
    }


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------
def main(argv: Optional[list[str]] = None) -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Check Terraform ↔ ARM template resource parity"
    )
    parser.add_argument(
        "--tf-dir",
        default="infra/terraform",
        help="Path to Terraform directory (default: infra/terraform)",
    )
    parser.add_argument(
        "--arm-template",
        default="infra/arm/main.json",
        help="Path to ARM template (default: infra/arm/main.json)",
    )
    parser.add_argument(
        "--verbose", "-v", action="store_true", help="Show detailed type counts"
    )
    parser.add_argument(
        "--json", action="store_true", help="Output JSON report"
    )
    args = parser.parse_args(argv)

    try:
        tf_resources = parse_terraform_resources(args.tf_dir)
        arm_resources = parse_arm_resources(args.arm_template)
    except (FileNotFoundError, json.JSONDecodeError) as e:
        print(f"Error: {e}", file=sys.stderr)
        return 2

    report = check_parity(tf_resources, arm_resources)

    # Also run security hardening checks
    hardening_issues = check_arm_security_hardening(args.arm_template)
    report.issues.extend(hardening_issues)

    # Also run version parity check
    version_issues = check_version_parity(args.tf_dir, args.arm_template)
    report.issues.extend(version_issues)

    # Also run CMK parity check
    cmk_issues = check_cmk_parity(args.tf_dir, args.arm_template)
    report.issues.extend(cmk_issues)

    if args.json:
        print(json.dumps(report_to_dict(report), indent=2))
    else:
        print(format_report(report, verbose=args.verbose))

    return 1 if report.has_errors else 0


if __name__ == "__main__":
    sys.exit(main())
