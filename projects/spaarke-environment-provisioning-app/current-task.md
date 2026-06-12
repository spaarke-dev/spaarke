# Current Task State

> **Project**: spaarke-environment-provisioning-app
> **Status**: complete
> **Current Task**: none
> **Phase**: —
> **Last Updated**: 2026-06-12

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | none |
| **Step** | — |
| **Status** | Project complete (all 22 tasks ✅) |
| **Next Action** | Successor program: environment-factory-r1 (see projects/spaarke-environment-factory-r1/design.md) |

## Project Outcome

All 22 tasks complete. E2E results: notes/e2e-test-results.md (7/11 criteria verified, 4 manual
sign-off items, criterion 10 carried to r2). Lessons: notes/lessons-learned.md.

Defects fixed during E2E (2026-06-12): FR-11 per-environment license wiring (4 BFF files +
LicenseResolutionTests, 6/6 pass) and seed-record license SKU population (Dataverse data fix).

## Carry-overs → environment-factory-r1

- DemoExpirationService migration off obsolete Environments config (unblocks deleting
  DemoProvisioning__Environments__* from Azure)
- Live provisioning sign-off round (criteria 5, 8, 9, 11)
- auth-azure-resources.md App Service name drift (spe-api-dev-67e2xz → spaarke-bff-dev/rg-spaarke-dev)
- spec.md/design.md column names → deployed reality (sprk_envaccountdomain, sprk_mdaappid)
- Lookup filtering by environment type
- Azure resource provisioning automation (Entra groups / Conditional Access / SPE containers)
