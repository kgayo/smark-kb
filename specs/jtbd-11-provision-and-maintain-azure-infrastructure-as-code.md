# JTBD 11: Provision and Maintain Azure Infrastructure as Code

## Job to Be Done
As a platform owner, I need Azure resources defined and frequently updated using IaC so environments can be provisioned, audited, and recovered consistently.

## Scope
- Terraform templates for all Azure resources.
- Azure ARM templates for the same resource topology.
- Environment parameterization (dev/staging/prod).
- IaC validation and drift-prevention checks in CI.

## Requirements
- Every Azure resource used by the app must exist in both Terraform and ARM templates.
- Any infrastructure change must update Terraform and ARM templates in the same PR/iteration.
- Keep templates modular and environment-parameterized.
- Validate templates on every infra change (`terraform fmt/validate`, ARM validate command).
- Track template versions and changelog notes for breaking infra changes.
- Prevent manual portal-only changes from becoming source of truth.

## Acceptance Criteria
- [x] New environment can be provisioned from IaC artifacts without manual portal steps.
- [x] Terraform and ARM definitions stay functionally aligned for core resources.
- [x] CI fails infra changes when Terraform or ARM validation fails.
- [x] Drift checks/reporting are available and reviewed regularly.
- [x] IaC updates accompany Azure resource modifications in code review.

## Non-Goals
- Supporting every IaC framework beyond Terraform and ARM in current scope.
- Manual-only infrastructure management.

## Phase Mapping
- Phase 1: baseline Terraform + ARM for core services.
- Phase 2: IaC hardening, drift checks, and modular expansion.
