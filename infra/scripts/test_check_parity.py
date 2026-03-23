#!/usr/bin/env python3
"""Unit tests for check_parity.py — Terraform ↔ ARM parity checker."""

import json
import os
import re
import tempfile
import textwrap
import unittest
from pathlib import Path

from check_parity import (
    ARM_INTERMEDIATE_TYPES,
    TF_EXCLUDED_TYPES,
    TF_TO_ARM_TYPE,
    ArmResource,
    ParityIssue,
    ParityReport,
    TerraformResource,
    check_arm_security_hardening,
    check_parity,
    check_version_parity,
    format_report,
    main,
    parse_arm_resources,
    parse_terraform_resources,
    report_to_dict,
)


class TestParseTerraformResources(unittest.TestCase):
    def _write_tf(self, tmpdir: str, filename: str, content: str) -> None:
        Path(tmpdir, filename).write_text(textwrap.dedent(content))

    def test_extracts_resource_blocks(self):
        with tempfile.TemporaryDirectory() as d:
            self._write_tf(
                d,
                "main.tf",
                """\
                resource "azurerm_resource_group" "main" {
                  name     = "rg-test"
                  location = "eastus"
                }

                resource "azurerm_storage_account" "main" {
                  name = "sttest"
                }
                """,
            )
            resources = parse_terraform_resources(d)
            self.assertEqual(len(resources), 2)
            self.assertEqual(resources[0].resource_type, "azurerm_resource_group")
            self.assertEqual(resources[0].name, "main")
            self.assertEqual(resources[1].resource_type, "azurerm_storage_account")

    def test_multiple_files(self):
        with tempfile.TemporaryDirectory() as d:
            self._write_tf(
                d,
                "a.tf",
                'resource "azurerm_key_vault" "main" {\n}\n',
            )
            self._write_tf(
                d,
                "b.tf",
                'resource "azurerm_search_service" "main" {\n}\n',
            )
            resources = parse_terraform_resources(d)
            types = {r.resource_type for r in resources}
            self.assertEqual(types, {"azurerm_key_vault", "azurerm_search_service"})

    def test_line_numbers(self):
        with tempfile.TemporaryDirectory() as d:
            self._write_tf(
                d,
                "main.tf",
                """\
                # comment line 1
                # comment line 2
                resource "azurerm_sql_server" "main" {
                }
                """,
            )
            resources = parse_terraform_resources(d)
            self.assertEqual(resources[0].line, 3)

    def test_ignores_non_tf_files(self):
        with tempfile.TemporaryDirectory() as d:
            Path(d, "readme.md").write_text('resource "azurerm_fake" "x" {}\n')
            self._write_tf(d, "main.tf", 'resource "azurerm_key_vault" "kv" {\n}\n')
            resources = parse_terraform_resources(d)
            self.assertEqual(len(resources), 1)

    def test_empty_directory(self):
        with tempfile.TemporaryDirectory() as d:
            resources = parse_terraform_resources(d)
            self.assertEqual(resources, [])

    def test_missing_directory_raises(self):
        with self.assertRaises(FileNotFoundError):
            parse_terraform_resources("/nonexistent/path")


class TestParseArmResources(unittest.TestCase):
    def _write_arm(self, tmpdir: str, template: dict) -> str:
        path = os.path.join(tmpdir, "main.json")
        with open(path, "w") as f:
            json.dump(template, f)
        return path

    def test_extracts_top_level_resources(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(
                d,
                {
                    "resources": [
                        {
                            "type": "Microsoft.Web/serverfarms",
                            "apiVersion": "2022-03-01",
                            "name": "plan-test",
                        },
                        {
                            "type": "Microsoft.KeyVault/vaults",
                            "apiVersion": "2023-02-01",
                            "name": "kv-test",
                        },
                    ]
                },
            )
            resources = parse_arm_resources(path)
            self.assertEqual(len(resources), 2)
            types = {r.resource_type for r in resources}
            self.assertEqual(
                types,
                {"Microsoft.Web/serverfarms", "Microsoft.KeyVault/vaults"},
            )

    def test_extracts_nested_children(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(
                d,
                {
                    "resources": [
                        {
                            "type": "Microsoft.Sql/servers",
                            "name": "sql-test",
                            "apiVersion": "2022-05-01",
                            "resources": [
                                {
                                    "type": "databases",
                                    "name": "db-test",
                                    "apiVersion": "2022-05-01",
                                },
                                {
                                    "type": "firewallRules",
                                    "name": "fw-test",
                                    "apiVersion": "2022-05-01",
                                },
                            ],
                        }
                    ]
                },
            )
            resources = parse_arm_resources(path)
            self.assertEqual(len(resources), 3)
            types = {r.resource_type for r in resources}
            self.assertIn("Microsoft.Sql/servers", types)
            self.assertIn("Microsoft.Sql/servers/databases", types)
            self.assertIn("Microsoft.Sql/servers/firewallRules", types)
            # Children marked as is_child
            children = [r for r in resources if r.is_child]
            self.assertEqual(len(children), 2)

    def test_missing_file_raises(self):
        with self.assertRaises(FileNotFoundError):
            parse_arm_resources("/nonexistent/main.json")

    def test_empty_resources(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {"resources": []})
            resources = parse_arm_resources(path)
            self.assertEqual(resources, [])


class TestCheckParity(unittest.TestCase):
    def test_matching_resources_no_issues(self):
        tf = [
            TerraformResource("azurerm_key_vault", "main", "keyvault.tf", 1),
            TerraformResource("azurerm_search_service", "main", "search.tf", 1),
        ]
        arm = [
            ArmResource("Microsoft.KeyVault/vaults", "kv-test"),
            ArmResource("Microsoft.Search/searchServices", "srch-test"),
        ]
        report = check_parity(tf, arm)
        self.assertFalse(report.has_errors)
        self.assertEqual(report.error_count, 0)

    def test_missing_in_arm_detected(self):
        tf = [
            TerraformResource("azurerm_key_vault", "main", "keyvault.tf", 1),
            TerraformResource("azurerm_search_service", "main", "search.tf", 1),
        ]
        arm = [
            ArmResource("Microsoft.KeyVault/vaults", "kv-test"),
            # search service missing from ARM
        ]
        report = check_parity(tf, arm)
        self.assertTrue(report.has_errors)
        errors = [i for i in report.issues if i.severity == "error"]
        self.assertEqual(len(errors), 1)
        self.assertEqual(errors[0].category, "missing_in_arm")
        self.assertIn("Microsoft.Search/searchServices", errors[0].message)

    def test_missing_in_terraform_detected(self):
        tf = [
            TerraformResource("azurerm_key_vault", "main", "keyvault.tf", 1),
        ]
        arm = [
            ArmResource("Microsoft.KeyVault/vaults", "kv-test"),
            ArmResource("Microsoft.Insights/actionGroups", "ag-test"),
        ]
        report = check_parity(tf, arm)
        self.assertTrue(report.has_errors)
        errors = [i for i in report.issues if i.severity == "error"]
        self.assertEqual(len(errors), 1)
        self.assertEqual(errors[0].category, "missing_in_terraform")

    def test_count_mismatch_detected(self):
        tf = [
            TerraformResource("azurerm_linux_web_app", "api", "app.tf", 1),
            TerraformResource("azurerm_linux_web_app", "ingestion", "app.tf", 10),
        ]
        arm = [
            ArmResource("Microsoft.Web/sites", "app-api"),
            ArmResource("Microsoft.Web/sites", "app-ingestion"),
            ArmResource("Microsoft.Web/sites", "app-extra"),  # extra one
        ]
        report = check_parity(tf, arm)
        self.assertTrue(report.has_errors)
        errors = [i for i in report.issues if i.severity == "error"]
        self.assertEqual(len(errors), 1)
        self.assertEqual(errors[0].category, "count_mismatch")

    def test_resource_group_excluded(self):
        tf = [
            TerraformResource("azurerm_resource_group", "main", "main.tf", 1),
        ]
        arm: list[ArmResource] = []
        report = check_parity(tf, arm)
        self.assertFalse(report.has_errors)

    def test_arm_intermediate_types_excluded(self):
        tf: list[TerraformResource] = []
        arm = [
            ArmResource("Microsoft.Storage/storageAccounts/blobServices", "default", is_child=True),
            ArmResource("Microsoft.Sql/servers/administrators", "ActiveDirectory", is_child=True),
        ]
        report = check_parity(tf, arm)
        self.assertFalse(report.has_errors)

    def test_unmapped_terraform_type_warning(self):
        tf = [
            TerraformResource("azurerm_unknown_thing", "x", "test.tf", 1),
        ]
        arm: list[ArmResource] = []
        report = check_parity(tf, arm)
        self.assertFalse(report.has_errors)
        warnings = [i for i in report.issues if i.severity == "warning"]
        self.assertEqual(len(warnings), 1)
        self.assertEqual(warnings[0].category, "unmapped_type")

    def test_role_assignment_count_parity(self):
        tf = [
            TerraformResource("azurerm_role_assignment", f"r{i}", "rbac.tf", i)
            for i in range(5)
        ]
        arm = [
            ArmResource("Microsoft.Authorization/roleAssignments", f"role-{i}")
            for i in range(5)
        ]
        report = check_parity(tf, arm)
        self.assertFalse(report.has_errors)

    def test_role_assignment_count_mismatch(self):
        tf = [
            TerraformResource("azurerm_role_assignment", f"r{i}", "rbac.tf", i)
            for i in range(10)
        ]
        arm = [
            ArmResource("Microsoft.Authorization/roleAssignments", f"role-{i}")
            for i in range(8)
        ]
        report = check_parity(tf, arm)
        self.assertTrue(report.has_errors)
        self.assertIn("count_mismatch", report.issues[0].category)


class TestFormatReport(unittest.TestCase):
    def test_pass_report_contains_pass(self):
        report = ParityReport()
        output = format_report(report)
        self.assertIn("PASS", output)

    def test_fail_report_contains_fail(self):
        report = ParityReport(
            issues=[ParityIssue("error", "missing_in_arm", "test error")]
        )
        output = format_report(report)
        self.assertIn("FAIL", output)
        self.assertIn("test error", output)

    def test_verbose_shows_type_counts(self):
        report = ParityReport(
            tf_type_counts={"azurerm_key_vault": 1},
            arm_type_counts={"Microsoft.KeyVault/vaults": 1},
        )
        output = format_report(report, verbose=True)
        self.assertIn("azurerm_key_vault", output)
        self.assertIn("Microsoft.KeyVault/vaults", output)

    def test_warnings_only_shows_pass_with_warnings(self):
        report = ParityReport(
            issues=[ParityIssue("warning", "unmapped_type", "test warning")]
        )
        output = format_report(report)
        self.assertIn("PASS (with warnings)", output)


class TestReportToDict(unittest.TestCase):
    def test_json_structure(self):
        report = ParityReport(
            terraform_resources=[
                TerraformResource("azurerm_key_vault", "main", "kv.tf", 1)
            ],
            arm_resources=[ArmResource("Microsoft.KeyVault/vaults", "kv-test")],
            issues=[ParityIssue("error", "missing_in_arm", "test")],
        )
        d = report_to_dict(report)
        self.assertEqual(d["terraform_resource_count"], 1)
        self.assertEqual(d["arm_resource_count"], 1)
        self.assertTrue(d["has_errors"])
        self.assertEqual(d["error_count"], 1)
        self.assertEqual(len(d["issues"]), 1)
        self.assertEqual(d["issues"][0]["severity"], "error")

    def test_json_serializable(self):
        report = ParityReport()
        d = report_to_dict(report)
        # Should not raise
        json.dumps(d)


class TestMappingCompleteness(unittest.TestCase):
    """Verify the TF→ARM mapping covers all types in the actual project."""

    def test_all_project_tf_types_mapped(self):
        """Every Terraform type in infra/terraform/ must have a mapping."""
        tf_dir = Path(__file__).parent.parent / "terraform"
        if not tf_dir.is_dir():
            self.skipTest("Terraform directory not found (running outside repo)")
        resources = parse_terraform_resources(tf_dir)
        unmapped = set()
        for r in resources:
            if r.resource_type not in TF_TO_ARM_TYPE:
                unmapped.add(r.resource_type)
        self.assertEqual(
            unmapped,
            set(),
            f"Unmapped Terraform types: {unmapped}",
        )

    def test_project_parity_passes(self):
        """The actual project IaC should pass parity (no errors)."""
        tf_dir = Path(__file__).parent.parent / "terraform"
        arm_path = Path(__file__).parent.parent / "arm" / "main.json"
        if not tf_dir.is_dir() or not arm_path.is_file():
            self.skipTest("IaC files not found (running outside repo)")
        tf_resources = parse_terraform_resources(tf_dir)
        arm_resources = parse_arm_resources(arm_path)
        report = check_parity(tf_resources, arm_resources)
        if report.has_errors:
            self.fail(
                f"IaC parity check failed:\n{format_report(report, verbose=True)}"
            )


class TestTfstateBackendTemplate(unittest.TestCase):
    """Validate the tfstate-backend.json ARM template structure."""

    def setUp(self):
        self.template_path = Path(__file__).parent.parent / "arm" / "tfstate-backend.json"
        if not self.template_path.is_file():
            self.skipTest("tfstate-backend.json not found (running outside repo)")
        with open(self.template_path) as f:
            self.template = json.load(f)

    def test_valid_arm_schema(self):
        self.assertEqual(
            self.template["$schema"],
            "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
        )

    def test_has_required_top_level_keys(self):
        for key in ["$schema", "contentVersion", "parameters", "resources"]:
            self.assertIn(key, self.template, f"Missing top-level key: {key}")

    def test_has_storage_account_resource(self):
        types = [r["type"] for r in self.template["resources"]]
        self.assertIn("Microsoft.Storage/storageAccounts", types)

    def test_has_blob_container_resource(self):
        types = [r["type"] for r in self.template["resources"]]
        self.assertIn("Microsoft.Storage/storageAccounts/blobServices/containers", types)

    def test_has_blob_services_with_versioning(self):
        blob_services = [
            r for r in self.template["resources"]
            if r["type"] == "Microsoft.Storage/storageAccounts/blobServices"
        ]
        self.assertEqual(len(blob_services), 1)
        self.assertTrue(blob_services[0]["properties"]["isVersioningEnabled"])

    def test_storage_account_enforces_tls12(self):
        storage = [
            r for r in self.template["resources"]
            if r["type"] == "Microsoft.Storage/storageAccounts"
        ]
        self.assertEqual(len(storage), 1)
        self.assertEqual(storage[0]["properties"]["minimumTlsVersion"], "TLS1_2")

    def test_storage_account_disallows_public_access(self):
        storage = [
            r for r in self.template["resources"]
            if r["type"] == "Microsoft.Storage/storageAccounts"
        ]
        self.assertFalse(storage[0]["properties"]["allowBlobPublicAccess"])

    def test_has_outputs(self):
        self.assertIn("outputs", self.template)
        self.assertIn("storageAccountName", self.template["outputs"])
        self.assertIn("containerName", self.template["outputs"])

    def test_all_resources_have_api_version(self):
        for i, resource in enumerate(self.template["resources"]):
            self.assertIn("apiVersion", resource, f"Resource [{i}] missing apiVersion")
            self.assertIn("name", resource, f"Resource [{i}] missing name")
            self.assertIn("type", resource, f"Resource [{i}] missing type")

    def test_soft_delete_retention_configured(self):
        blob_services = [
            r for r in self.template["resources"]
            if r["type"] == "Microsoft.Storage/storageAccounts/blobServices"
        ]
        policy = blob_services[0]["properties"]["deleteRetentionPolicy"]
        self.assertTrue(policy["enabled"])
        # days may be an ARM expression string or integer
        self.assertIn("days", policy)


class TestBootstrapScriptExists(unittest.TestCase):
    """Verify bootstrap script is present and executable."""

    def test_bootstrap_script_exists(self):
        script_path = Path(__file__).parent / "bootstrap-tfstate.sh"
        if not script_path.is_file():
            self.skipTest("bootstrap-tfstate.sh not found (running outside repo)")
        self.assertTrue(script_path.is_file())

    def test_bootstrap_script_is_executable(self):
        script_path = Path(__file__).parent / "bootstrap-tfstate.sh"
        if not script_path.is_file():
            self.skipTest("bootstrap-tfstate.sh not found (running outside repo)")
        self.assertTrue(os.access(script_path, os.X_OK))

    def test_bootstrap_script_has_shebang(self):
        script_path = Path(__file__).parent / "bootstrap-tfstate.sh"
        if not script_path.is_file():
            self.skipTest("bootstrap-tfstate.sh not found (running outside repo)")
        content = script_path.read_text()
        self.assertTrue(content.startswith("#!/"))

    def test_bootstrap_script_uses_strict_mode(self):
        script_path = Path(__file__).parent / "bootstrap-tfstate.sh"
        if not script_path.is_file():
            self.skipTest("bootstrap-tfstate.sh not found (running outside repo)")
        content = script_path.read_text()
        self.assertIn("set -euo pipefail", content)


class TestBackendConfigFiles(unittest.TestCase):
    """Verify backend config .hcl files exist for each environment."""

    def test_backend_configs_exist_for_all_environments(self):
        tf_dir = Path(__file__).parent.parent / "terraform"
        if not tf_dir.is_dir():
            self.skipTest("Terraform directory not found (running outside repo)")
        for env in ["dev", "staging", "prod"]:
            hcl_path = tf_dir / f"backend.{env}.hcl"
            self.assertTrue(
                hcl_path.is_file(),
                f"Missing backend config: backend.{env}.hcl",
            )

    def test_backend_configs_reference_tfstate_container(self):
        tf_dir = Path(__file__).parent.parent / "terraform"
        if not tf_dir.is_dir():
            self.skipTest("Terraform directory not found (running outside repo)")
        for env in ["dev", "staging", "prod"]:
            content = (tf_dir / f"backend.{env}.hcl").read_text()
            self.assertIn('container_name', content)
            self.assertIn('tfstate', content)
            self.assertIn(f'smartkb-{env}.tfstate', content)

    def test_backend_configs_use_consistent_storage_account(self):
        tf_dir = Path(__file__).parent.parent / "terraform"
        if not tf_dir.is_dir():
            self.skipTest("Terraform directory not found (running outside repo)")
        for env in ["dev", "staging", "prod"]:
            content = (tf_dir / f"backend.{env}.hcl").read_text()
            self.assertIn('storage_account_name = "stsmartkbtfstate"', content)


class TestCliEntryPoint(unittest.TestCase):
    def test_pass_returns_zero(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            Path(tf_dir, "main.tf").write_text(
                'resource "azurerm_key_vault" "main" {\n}\n'
            )
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump(
                    {
                        "resources": [
                            {
                                "type": "Microsoft.KeyVault/vaults",
                                "apiVersion": "2023-02-01",
                                "name": "kv-test",
                                "properties": {
                                    "enableRbacAuthorization": True,
                                    "enableSoftDelete": True,
                                },
                            }
                        ]
                    },
                    f,
                )
            rc = main(["--tf-dir", tf_dir, "--arm-template", arm_path])
            self.assertEqual(rc, 0)

    def test_fail_returns_one(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            Path(tf_dir, "main.tf").write_text(
                'resource "azurerm_key_vault" "main" {\n}\n'
            )
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({"resources": []}, f)
            rc = main(["--tf-dir", tf_dir, "--arm-template", arm_path])
            self.assertEqual(rc, 1)

    def test_missing_dir_returns_two(self):
        rc = main(["--tf-dir", "/nonexistent", "--arm-template", "/nonexistent.json"])
        self.assertEqual(rc, 2)

    def test_json_output(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            Path(tf_dir, "main.tf").write_text("")
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({"resources": []}, f)
            rc = main(["--tf-dir", tf_dir, "--arm-template", arm_path, "--json"])
            self.assertEqual(rc, 0)


class TestCheckArmSecurityHardening(unittest.TestCase):
    """Unit tests for check_arm_security_hardening function."""

    def _write_arm(self, tmpdir: str, template: dict) -> str:
        path = os.path.join(tmpdir, "main.json")
        with open(path, "w") as f:
            json.dump(template, f)
        return path

    def test_passes_for_compliant_web_app(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.Web/sites",
                    "name": "app-test",
                    "apiVersion": "2023-12-01",
                    "identity": {"type": "SystemAssigned"},
                    "properties": {"httpsOnly": True},
                }]
            })
            issues = check_arm_security_hardening(path)
            self.assertEqual(len(issues), 0)

    def test_fails_when_https_only_missing(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.Web/sites",
                    "name": "app-test",
                    "apiVersion": "2023-12-01",
                    "identity": {"type": "SystemAssigned"},
                    "properties": {"httpsOnly": False},
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "httpsOnly" in i.message]
            self.assertEqual(len(errors), 1)
            self.assertEqual(errors[0].category, "security_hardening")

    def test_fails_when_identity_missing(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.Web/sites",
                    "name": "app-test",
                    "apiVersion": "2023-12-01",
                    "properties": {"httpsOnly": True},
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "SystemAssigned" in i.message]
            self.assertEqual(len(errors), 1)

    def test_fails_when_storage_tls_wrong(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.Storage/storageAccounts",
                    "name": "st-test",
                    "apiVersion": "2023-05-01",
                    "properties": {
                        "minimumTlsVersion": "TLS1_0",
                        "supportsHttpsTrafficOnly": True,
                    },
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "TLS" in i.message]
            self.assertEqual(len(errors), 1)

    def test_fails_when_keyvault_rbac_disabled(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.KeyVault/vaults",
                    "name": "kv-test",
                    "apiVersion": "2023-07-01",
                    "properties": {
                        "enableRbacAuthorization": False,
                        "enableSoftDelete": True,
                    },
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "RBAC" in i.message]
            self.assertEqual(len(errors), 1)

    def test_fails_when_blob_container_public(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.Storage/storageAccounts/blobServices/containers",
                    "name": "container-test",
                    "apiVersion": "2023-05-01",
                    "properties": {"publicAccess": "Blob"},
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "publicAccess" in i.message]
            self.assertEqual(len(errors), 1)

    def test_fails_when_queue_no_dlq(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.ServiceBus/namespaces/queues",
                    "name": "queue-test",
                    "apiVersion": "2022-10-01-preview",
                    "properties": {
                        "maxDeliveryCount": 10,
                        "deadLetteringOnMessageExpiration": False,
                    },
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "deadLettering" in i.message]
            self.assertEqual(len(errors), 1)

    def test_multiple_web_apps_each_checked(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [
                    {
                        "type": "Microsoft.Web/sites", "name": "app-1",
                        "apiVersion": "2023-12-01",
                        "identity": {"type": "SystemAssigned"},
                        "properties": {"httpsOnly": False},
                    },
                    {
                        "type": "Microsoft.Web/sites", "name": "app-2",
                        "apiVersion": "2023-12-01",
                        "identity": {"type": "SystemAssigned"},
                        "properties": {"httpsOnly": False},
                    },
                ]
            })
            issues = check_arm_security_hardening(path)
            https_errors = [i for i in issues if "httpsOnly" in i.message]
            self.assertEqual(len(https_errors), 2)

    def test_search_service_identity_required(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._write_arm(d, {
                "resources": [{
                    "type": "Microsoft.Search/searchServices",
                    "name": "srch-test",
                    "apiVersion": "2024-03-01-preview",
                    "properties": {},
                }]
            })
            issues = check_arm_security_hardening(path)
            errors = [i for i in issues if "SystemAssigned" in i.message]
            self.assertEqual(len(errors), 1)

    def test_missing_file_raises(self):
        with self.assertRaises(FileNotFoundError):
            check_arm_security_hardening("/nonexistent/main.json")


class TestProjectArmSecurityHardening(unittest.TestCase):
    """Validate security hardening of the actual project ARM template."""

    def setUp(self):
        self.arm_path = Path(__file__).parent.parent / "arm" / "main.json"
        if not self.arm_path.is_file():
            self.skipTest("ARM template not found (running outside repo)")
        with open(self.arm_path) as f:
            self.template = json.load(f)
        self.resources = self.template.get("resources", [])

    def test_all_web_apps_enforce_https(self):
        sites = [r for r in self.resources if r.get("type") == "Microsoft.Web/sites"]
        self.assertGreaterEqual(len(sites), 2, "Expected at least 2 web apps")
        for site in sites:
            self.assertTrue(
                site["properties"].get("httpsOnly"),
                f"Web app '{site['name']}' must have httpsOnly=true",
            )

    def test_all_web_apps_have_system_identity(self):
        sites = [r for r in self.resources if r.get("type") == "Microsoft.Web/sites"]
        for site in sites:
            self.assertEqual(
                site.get("identity", {}).get("type"), "SystemAssigned",
                f"Web app '{site['name']}' must use SystemAssigned identity",
            )

    def test_storage_account_tls12(self):
        storage = [r for r in self.resources if r.get("type") == "Microsoft.Storage/storageAccounts"]
        self.assertEqual(len(storage), 1)
        self.assertEqual(storage[0]["properties"]["minimumTlsVersion"], "TLS1_2")

    def test_storage_account_https_only(self):
        storage = [r for r in self.resources if r.get("type") == "Microsoft.Storage/storageAccounts"]
        self.assertTrue(storage[0]["properties"]["supportsHttpsTrafficOnly"])

    def test_blob_container_private(self):
        containers = [
            r for r in self.resources
            if r.get("type") == "Microsoft.Storage/storageAccounts/blobServices/containers"
        ]
        self.assertGreaterEqual(len(containers), 1)
        for c in containers:
            self.assertEqual(c["properties"]["publicAccess"], "None")

    def test_key_vault_rbac_enabled(self):
        vaults = [r for r in self.resources if r.get("type") == "Microsoft.KeyVault/vaults"]
        self.assertEqual(len(vaults), 1)
        self.assertTrue(vaults[0]["properties"]["enableRbacAuthorization"])

    def test_key_vault_soft_delete_enabled(self):
        vaults = [r for r in self.resources if r.get("type") == "Microsoft.KeyVault/vaults"]
        self.assertTrue(vaults[0]["properties"]["enableSoftDelete"])

    def test_key_vault_soft_delete_retention_90_days(self):
        vaults = [r for r in self.resources if r.get("type") == "Microsoft.KeyVault/vaults"]
        self.assertEqual(vaults[0]["properties"]["softDeleteRetentionInDays"], 90)

    def test_search_service_has_system_identity(self):
        search = [r for r in self.resources if r.get("type") == "Microsoft.Search/searchServices"]
        self.assertEqual(len(search), 1)
        self.assertEqual(search[0]["identity"]["type"], "SystemAssigned")

    def test_sql_server_version_12(self):
        sql = [r for r in self.resources if r.get("type") == "Microsoft.Sql/servers"]
        self.assertEqual(len(sql), 1)
        self.assertEqual(sql[0]["properties"]["version"], "12.0")

    def test_sql_firewall_allows_azure_services_only(self):
        fw_rules = [
            r for r in self.resources
            if r.get("type") == "Microsoft.Sql/servers/firewallRules"
        ]
        self.assertGreaterEqual(len(fw_rules), 1)
        for rule in fw_rules:
            self.assertEqual(rule["properties"]["startIpAddress"], "0.0.0.0")
            self.assertEqual(rule["properties"]["endIpAddress"], "0.0.0.0")

    def test_queue_dead_lettering_enabled(self):
        queues = [
            r for r in self.resources
            if r.get("type") == "Microsoft.ServiceBus/namespaces/queues"
        ]
        self.assertGreaterEqual(len(queues), 1)
        for q in queues:
            self.assertTrue(q["properties"]["deadLetteringOnMessageExpiration"])

    def test_queue_max_delivery_count(self):
        queues = [
            r for r in self.resources
            if r.get("type") == "Microsoft.ServiceBus/namespaces/queues"
        ]
        for q in queues:
            self.assertEqual(q["properties"]["maxDeliveryCount"], 10)

    def test_app_service_plan_linux(self):
        plans = [r for r in self.resources if r.get("type") == "Microsoft.Web/serverfarms"]
        self.assertEqual(len(plans), 1)
        self.assertEqual(plans[0]["kind"], "linux")
        self.assertTrue(plans[0]["properties"]["reserved"])

    def test_web_apps_have_required_app_settings(self):
        """Both web apps must configure all infrastructure connection settings."""
        required_settings = {
            "APPLICATIONINSIGHTS_CONNECTION_STRING",
            "KeyVault__VaultUri",
            "ServiceBus__FullyQualifiedNamespace",
            "BlobStorage__ServiceUri",
            "SearchService__Endpoint",
        }
        sites = [r for r in self.resources if r.get("type") == "Microsoft.Web/sites"]
        for site in sites:
            settings = site["properties"]["siteConfig"].get("appSettings", [])
            names = {s["name"] for s in settings}
            missing = required_settings - names
            self.assertEqual(
                missing, set(),
                f"Web app '{site['name']}' missing app settings: {missing}",
            )

    def test_web_apps_have_sql_connection_string(self):
        sites = [r for r in self.resources if r.get("type") == "Microsoft.Web/sites"]
        for site in sites:
            conn_strings = site["properties"]["siteConfig"].get("connectionStrings", [])
            names = {cs["name"] for cs in conn_strings}
            self.assertIn("SmartKbDb", names, f"Web app '{site['name']}' missing SmartKbDb connection string")

    def test_security_hardening_function_passes(self):
        """The project ARM template passes automated security hardening checks."""
        issues = check_arm_security_hardening(self.arm_path)
        if issues:
            msgs = "\n".join(f"  [{i.category}] {i.message}" for i in issues)
            self.fail(f"Security hardening issues found:\n{msgs}")


class TestPropertyLevelParity(unittest.TestCase):
    """Cross-validate key configuration properties between Terraform and ARM."""

    def setUp(self):
        self.tf_dir = Path(__file__).parent.parent / "terraform"
        self.arm_path = Path(__file__).parent.parent / "arm" / "main.json"
        if not self.tf_dir.is_dir() or not self.arm_path.is_file():
            self.skipTest("IaC files not found (running outside repo)")
        with open(self.arm_path) as f:
            self.arm = json.load(f)
        self.arm_resources = self.arm.get("resources", [])

    def _read_tf(self, filename: str) -> str:
        return (self.tf_dir / filename).read_text()

    def test_web_apps_https_only_in_both(self):
        """Both TF and ARM enforce httpsOnly on web apps."""
        tf_content = self._read_tf("app-service.tf")
        self.assertIn("https_only", tf_content)
        self.assertEqual(tf_content.count("https_only          = true"), 2)
        sites = [r for r in self.arm_resources if r.get("type") == "Microsoft.Web/sites"]
        for site in sites:
            self.assertTrue(site["properties"]["httpsOnly"])

    def test_storage_tls_version_matches(self):
        """TF min_tls_version = TLS1_2 matches ARM minimumTlsVersion = TLS1_2."""
        tf_content = self._read_tf("storage.tf")
        self.assertIn('min_tls_version          = "TLS1_2"', tf_content)
        storage = [r for r in self.arm_resources if r.get("type") == "Microsoft.Storage/storageAccounts"]
        self.assertEqual(storage[0]["properties"]["minimumTlsVersion"], "TLS1_2")

    def test_key_vault_rbac_matches(self):
        """TF enable_rbac_authorization = true matches ARM enableRbacAuthorization = true."""
        tf_content = self._read_tf("keyvault.tf")
        self.assertIn("enable_rbac_authorization = true", tf_content)
        vaults = [r for r in self.arm_resources if r.get("type") == "Microsoft.KeyVault/vaults"]
        self.assertTrue(vaults[0]["properties"]["enableRbacAuthorization"])

    def test_key_vault_soft_delete_retention_matches(self):
        """TF soft_delete_retention_days = 90 matches ARM softDeleteRetentionInDays = 90."""
        tf_content = self._read_tf("keyvault.tf")
        self.assertIn("soft_delete_retention_days = 90", tf_content)
        vaults = [r for r in self.arm_resources if r.get("type") == "Microsoft.KeyVault/vaults"]
        self.assertEqual(vaults[0]["properties"]["softDeleteRetentionInDays"], 90)

    def test_queue_max_delivery_count_matches(self):
        """TF max_delivery_count = 10 matches ARM maxDeliveryCount = 10."""
        tf_content = self._read_tf("servicebus.tf")
        self.assertIn("max_delivery_count                   = 10", tf_content)
        queues = [r for r in self.arm_resources if r.get("type") == "Microsoft.ServiceBus/namespaces/queues"]
        self.assertEqual(queues[0]["properties"]["maxDeliveryCount"], 10)

    def test_queue_dead_lettering_matches(self):
        """TF dead_lettering_on_message_expiration = true matches ARM."""
        tf_content = self._read_tf("servicebus.tf")
        self.assertIn("dead_lettering_on_message_expiration = true", tf_content)
        queues = [r for r in self.arm_resources if r.get("type") == "Microsoft.ServiceBus/namespaces/queues"]
        self.assertTrue(queues[0]["properties"]["deadLetteringOnMessageExpiration"])

    def test_sql_server_version_matches(self):
        """TF version = 12.0 matches ARM version = 12.0."""
        tf_content = self._read_tf("sql.tf")
        self.assertIn('version                      = "12.0"', tf_content)
        sql = [r for r in self.arm_resources if r.get("type") == "Microsoft.Sql/servers"]
        self.assertEqual(sql[0]["properties"]["version"], "12.0")

    def test_blob_retention_days_match(self):
        """TF delete_retention_policy days = 7 matches ARM deleteRetentionPolicy days = 7."""
        tf_content = self._read_tf("storage.tf")
        self.assertIn("days = 7", tf_content)
        blob_svc = [
            r for r in self.arm_resources
            if r.get("type") == "Microsoft.Storage/storageAccounts/blobServices"
        ]
        self.assertEqual(
            blob_svc[0]["properties"]["deleteRetentionPolicy"]["days"], 7
        )

    def test_app_settings_keys_match(self):
        """Both TF web apps and ARM web apps define the same app setting keys."""
        tf_content = self._read_tf("app-service.tf")
        tf_settings = set(re.findall(r'"([A-Z][A-Za-z_]+)"', tf_content))
        # Filter to only actual app setting keys (not connection string types)
        expected_settings = {
            "APPLICATIONINSIGHTS_CONNECTION_STRING",
            "KeyVault__VaultUri",
            "ServiceBus__FullyQualifiedNamespace",
            "BlobStorage__ServiceUri",
            "SearchService__Endpoint",
        }
        self.assertTrue(expected_settings.issubset(tf_settings))

        sites = [r for r in self.arm_resources if r.get("type") == "Microsoft.Web/sites"]
        for site in sites:
            arm_settings = {
                s["name"]
                for s in site["properties"]["siteConfig"].get("appSettings", [])
            }
            missing = expected_settings - arm_settings
            self.assertEqual(missing, set(), f"ARM '{site['name']}' missing: {missing}")

    def test_search_identity_matches(self):
        """Both TF and ARM define SystemAssigned identity on search service."""
        tf_content = self._read_tf("search.tf")
        self.assertIn('type = "SystemAssigned"', tf_content)
        search = [r for r in self.arm_resources if r.get("type") == "Microsoft.Search/searchServices"]
        self.assertEqual(search[0]["identity"]["type"], "SystemAssigned")


class TestArmParameterFileConsistency(unittest.TestCase):
    """Validate ARM parameter files are consistent across environments."""

    def setUp(self):
        self.arm_dir = Path(__file__).parent.parent / "arm"
        if not self.arm_dir.is_dir():
            self.skipTest("ARM directory not found (running outside repo)")
        self.envs = ["dev", "staging", "prod"]
        self.param_files = {}
        for env in self.envs:
            path = self.arm_dir / f"parameters.{env}.json"
            if not path.is_file():
                self.skipTest(f"parameters.{env}.json not found")
            with open(path) as f:
                self.param_files[env] = json.load(f)

    def test_all_param_files_have_valid_schema(self):
        for env, data in self.param_files.items():
            self.assertEqual(
                data["$schema"],
                "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
                f"parameters.{env}.json has invalid schema",
            )

    def test_all_param_files_have_same_parameter_keys(self):
        key_sets = {
            env: set(data["parameters"].keys())
            for env, data in self.param_files.items()
        }
        dev_keys = key_sets["dev"]
        for env in ["staging", "prod"]:
            self.assertEqual(
                dev_keys, key_sets[env],
                f"parameters.{env}.json keys differ from dev: "
                f"missing={dev_keys - key_sets[env]}, extra={key_sets[env] - dev_keys}",
            )

    def test_environment_value_matches_filename(self):
        for env, data in self.param_files.items():
            self.assertEqual(
                data["parameters"]["environment"]["value"], env,
                f"parameters.{env}.json environment value mismatch",
            )

    def test_all_param_files_have_entra_tenant_id(self):
        for env, data in self.param_files.items():
            self.assertIn("entraTenantId", data["parameters"])

    def test_all_param_files_have_sql_admin_password(self):
        for env, data in self.param_files.items():
            self.assertIn("sqlAdminPassword", data["parameters"])

    def test_param_files_reference_main_template_params(self):
        """All param file keys must exist in the main ARM template parameters."""
        main_path = self.arm_dir / "main.json"
        if not main_path.is_file():
            self.skipTest("main.json not found")
        with open(main_path) as f:
            main_template = json.load(f)
        template_params = set(main_template["parameters"].keys())
        for env, data in self.param_files.items():
            file_params = set(data["parameters"].keys())
            unknown = file_params - template_params
            self.assertEqual(
                unknown, set(),
                f"parameters.{env}.json references unknown params: {unknown}",
            )


class TestTfvarsFileConsistency(unittest.TestCase):
    """Validate Terraform tfvars files are consistent across environments."""

    def setUp(self):
        self.tf_dir = Path(__file__).parent.parent / "terraform"
        if not self.tf_dir.is_dir():
            self.skipTest("Terraform directory not found (running outside repo)")
        self.envs = ["dev", "staging", "prod"]
        self.tfvars = {}
        for env in self.envs:
            path = self.tf_dir / f"{env}.tfvars"
            if not path.is_file():
                self.skipTest(f"{env}.tfvars not found")
            self.tfvars[env] = path.read_text()

    def _parse_keys(self, content: str) -> set:
        return set(re.findall(r'^(\w+)\s*=', content, re.MULTILINE))

    def test_all_tfvars_have_same_variable_keys(self):
        key_sets = {env: self._parse_keys(c) for env, c in self.tfvars.items()}
        dev_keys = key_sets["dev"]
        for env in ["staging", "prod"]:
            self.assertEqual(
                dev_keys, key_sets[env],
                f"{env}.tfvars keys differ from dev: "
                f"missing={dev_keys - key_sets[env]}, extra={key_sets[env] - dev_keys}",
            )

    def test_environment_value_matches_filename(self):
        for env, content in self.tfvars.items():
            self.assertIn(
                f'environment     = "{env}"', content,
                f"{env}.tfvars environment value mismatch",
            )

    def test_tfvars_keys_reference_declared_variables(self):
        """All tfvars keys must correspond to declared variables in variables.tf."""
        vars_path = self.tf_dir / "variables.tf"
        if not vars_path.is_file():
            self.skipTest("variables.tf not found")
        vars_content = vars_path.read_text()
        declared = set(re.findall(r'^variable\s+"(\w+)"', vars_content, re.MULTILINE))
        for env, content in self.tfvars.items():
            used = self._parse_keys(content)
            unknown = used - declared
            self.assertEqual(
                unknown, set(),
                f"{env}.tfvars references undeclared variables: {unknown}",
            )

    def test_sku_values_provided_for_all_envs(self):
        """All environments must specify SKU variables."""
        sku_vars = {"app_service_sku", "sql_sku", "search_sku", "servicebus_sku"}
        for env, content in self.tfvars.items():
            keys = self._parse_keys(content)
            missing = sku_vars - keys
            self.assertEqual(
                missing, set(),
                f"{env}.tfvars missing SKU variables: {missing}",
            )

    def test_arm_and_tf_sku_defaults_match(self):
        """ARM default SKU parameter values match TF variable defaults for dev."""
        arm_path = Path(__file__).parent.parent / "arm" / "main.json"
        if not arm_path.is_file():
            self.skipTest("main.json not found")
        with open(arm_path) as f:
            arm = json.load(f)
        arm_params = arm["parameters"]
        vars_path = self.tf_dir / "variables.tf"
        vars_content = vars_path.read_text()

        # Check that ARM default values match TF variable defaults
        arm_tf_map = {
            "appServiceSku": "app_service_sku",
            "sqlSku": "sql_sku",
            "searchSku": "search_sku",
            "serviceBusSku": "servicebus_sku",
        }
        for arm_name, tf_name in arm_tf_map.items():
            arm_default = arm_params[arm_name].get("defaultValue")
            # Extract TF default from variables.tf
            pattern = rf'variable\s+"{tf_name}".*?default\s*=\s*"([^"]+)"'
            match = re.search(pattern, vars_content, re.DOTALL)
            if match and arm_default:
                self.assertEqual(
                    arm_default, match.group(1),
                    f"Default SKU mismatch for {arm_name}: ARM={arm_default}, TF={match.group(1)}",
                )


class TestCliIncludesSecurityHardening(unittest.TestCase):
    """Verify the CLI entry point also runs security hardening checks."""

    def test_cli_fails_on_security_hardening_violation(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            Path(tf_dir, "main.tf").write_text(
                'resource "azurerm_key_vault" "main" {\n}\n'
            )
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "resources": [
                        {
                            "type": "Microsoft.KeyVault/vaults",
                            "apiVersion": "2023-07-01",
                            "name": "kv-test",
                            "properties": {
                                "enableRbacAuthorization": False,
                                "enableSoftDelete": False,
                            },
                        }
                    ]
                }, f)
            rc = main(["--tf-dir", tf_dir, "--arm-template", arm_path])
            self.assertEqual(rc, 1, "CLI should fail on security hardening violations")

    def test_cli_passes_when_all_compliant(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            Path(tf_dir, "main.tf").write_text(
                'resource "azurerm_key_vault" "main" {\n}\n'
            )
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "resources": [
                        {
                            "type": "Microsoft.KeyVault/vaults",
                            "apiVersion": "2023-07-01",
                            "name": "kv-test",
                            "properties": {
                                "enableRbacAuthorization": True,
                                "enableSoftDelete": True,
                            },
                        }
                    ]
                }, f)
            rc = main(["--tf-dir", tf_dir, "--arm-template", arm_path])
            self.assertEqual(rc, 0)


class TestCheckVersionParity(unittest.TestCase):
    """Tests for check_version_parity()."""

    def test_matching_versions_no_issues(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            with open(os.path.join(tf_dir, "variables.tf"), "w") as f:
                f.write(textwrap.dedent('''\
                    variable "infra_version" {
                      default = "1.6.0"
                    }
                '''))
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "metadata": {"infraVersion": "1.6.0"},
                    "resources": []
                }, f)

            issues = check_version_parity(tf_dir, arm_path)
            self.assertEqual(len(issues), 0)

    def test_mismatched_versions_returns_error(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            with open(os.path.join(tf_dir, "variables.tf"), "w") as f:
                f.write(textwrap.dedent('''\
                    variable "infra_version" {
                      default = "1.6.0"
                    }
                '''))
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "metadata": {"infraVersion": "1.5.0"},
                    "resources": []
                }, f)

            issues = check_version_parity(tf_dir, arm_path)
            errors = [i for i in issues if i.severity == "error"]
            self.assertEqual(len(errors), 1)
            self.assertEqual(errors[0].category, "version_mismatch")
            self.assertIn("1.6.0", errors[0].message)
            self.assertIn("1.5.0", errors[0].message)

    def test_missing_tf_version_returns_warning(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            with open(os.path.join(tf_dir, "variables.tf"), "w") as f:
                f.write('variable "environment" { default = "dev" }\n')
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "metadata": {"infraVersion": "1.6.0"},
                    "resources": []
                }, f)

            issues = check_version_parity(tf_dir, arm_path)
            warnings = [i for i in issues if i.severity == "warning"]
            self.assertEqual(len(warnings), 1)
            self.assertEqual(warnings[0].category, "version_missing")

    def test_missing_arm_metadata_returns_warning(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            with open(os.path.join(tf_dir, "variables.tf"), "w") as f:
                f.write(textwrap.dedent('''\
                    variable "infra_version" {
                      default = "1.6.0"
                    }
                '''))
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({"resources": []}, f)

            issues = check_version_parity(tf_dir, arm_path)
            warnings = [i for i in issues if i.severity == "warning"]
            self.assertEqual(len(warnings), 1)
            self.assertIn("ARM", warnings[0].message)

    def test_missing_variables_tf_returns_warning(self):
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            # No variables.tf created
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "metadata": {"infraVersion": "1.6.0"},
                    "resources": []
                }, f)

            issues = check_version_parity(tf_dir, arm_path)
            warnings = [i for i in issues if i.severity == "warning"]
            self.assertEqual(len(warnings), 1)
            self.assertIn("variables.tf", warnings[0].message)

    def test_version_check_in_main_cli(self):
        """Ensure main() runs version check (matching versions → no error from version)."""
        with tempfile.TemporaryDirectory() as d:
            tf_dir = os.path.join(d, "tf")
            os.makedirs(tf_dir)
            with open(os.path.join(tf_dir, "variables.tf"), "w") as f:
                f.write(textwrap.dedent('''\
                    variable "infra_version" {
                      default = "2.0.0"
                    }
                '''))
            with open(os.path.join(tf_dir, "main.tf"), "w") as f:
                f.write("")
            arm_path = os.path.join(d, "main.json")
            with open(arm_path, "w") as f:
                json.dump({
                    "metadata": {"infraVersion": "2.0.0"},
                    "resources": []
                }, f)
            # No resource mismatch, no security issues, matching versions → exit 0
            rc = main(["--tf-dir", tf_dir, "--arm-template", arm_path])
            self.assertEqual(rc, 0)


if __name__ == "__main__":
    unittest.main()
