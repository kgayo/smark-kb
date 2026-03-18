#!/usr/bin/env python3
"""Unit tests for check_parity.py — Terraform ↔ ARM parity checker."""

import json
import os
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
    check_parity,
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


if __name__ == "__main__":
    unittest.main()
