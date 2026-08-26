---
name: dataverse-env-provisioning-e2e-2026-08-22
description: End-to-end programmatic Dataverse env provisioning surface for Spaarke r1; 8 API surfaces, TF provider status, F12 root cause, gap list
metadata:
  type: reference
---

# Dataverse env provisioning end-to-end (Spaarke r1 foundation, 2026-08-22)

**Deep-dive** at `projects/customer-provisioning-orchestration-r1/notes/research-dataverse-env-provisioning-2026-08-22.md`. Summary here for future-session recall.

## The 8 API surfaces (composed, not one API)

1. **Env create** — `pac admin create` (interim) / BAP REST `api.bap.microsoft.com/scopes/admin/environments` (target) / TF `powerplatform_environment` (D14). Types: Production, Sandbox, Trial, Developer (SP-blocked), Teams, SubscriptionBasedTrial. Only Production + Sandbox support PAYG. `pac admin create` params: `--type --currency --domain --language --name --region --security-group-id --templates --user --async --input-file --json`. NO managed-env / env-group / PAYG at create time on pac — those are separate follow-ups.
2. **Managed Environments enable** — `pac admin set-governance-config --environment X --protection-level Standard [--solution-checker-mode warn ...]` OR `Set-AdminPowerAppEnvironmentGovernanceConfiguration` PS OR TF `powerplatform_managed_environment`. NOW INCLUDED in ALL Power Apps + D365 licenses (no separate paid add-on). Required for Env Groups + Pipelines.
3. **Environment Groups** — REST `https://api.powerplatform.com/environmentmanagement/environmentGroups?api-version=2024-10-01` (audience `api.powerplatform.com/.default`, NOT api.bap.microsoft.com). Full CRUD + add/remove env + LRO. `pac admin add-group / list-groups` (no create-group in pac). TF `powerplatform_environment_group` (create/get/delete but no direct env-assign — use `powerplatform_tenant_settings.environment_routing_target_environment_group_id` or REST). Env-Group envs MUST be Managed. Each env in ≤1 group.
4. **PAYG billing plan** — PPAC UI primary / BAP REST `billingPolicies?api-version=2022-03-01-preview` (under-documented) / TF `powerplatform_billing_policy` + `powerplatform_billing_policy_environment`. Creates a `Microsoft.PowerPlatform/accounts` Azure resource. Prereq: `az provider register --namespace Microsoft.PowerPlatform`. 1 GB DB + 1 GB file per PAYG env free, rest metered.
5. **Solution import** — Web API `ImportSolutionAsync` + AsyncOperation polling (recommended) / `StageSolution` → `ImportSolutionAsync` w/ StageSolutionUploadId (upgrades) / `pac solution import` (CLI wrapper). Missing-deps is a **packaging-time problem NOT an import-API problem** — categories: D365 first-party (install via `pac application install`), another managed solution (order dep chain first), unmanaged customizations (re-export). Always export MANAGED from a CLEAN managed source.
6. **Application User (MI-BFF-API)** — BAP REST `POST /addAppUser` body `{"servicePrincipalAppId": "<mi-or-sp-client-id>"}` always System Administrator (single 200 OK). To scope tighter, use `pac admin assign-user --user <app-id> --role "Scoped Role" --application-user`. Verify via Dataverse Web API `systemusers?$filter=applicationid eq {app-id}`. MI variant works identically — pass UAMI clientId.
7. **B2B guest / customer users** — CRITICAL: `restrictGuestUserAccess` defaults **TRUE (blocked)** for all NEW envs since Mar 2026. Must flip: `pac env update-settings --environment X --name restrictGuestUserAccess --value false`. Then B2B invite via Graph → assign via `pac admin assign-user` → security role. Copilot Studio Graph connectors may still leak to guests even when restricted.
8. **Per-env config** — `pac env update-settings` for post-provisioning per-env knobs (audit toggles, guest access, feature flags).

## TF Power Platform provider status (Jan 2026, v4.1.0)

**Public preview, NOT GA.** 13 resources incl. environment, managed_environment, environment_group, billing_policy, billing_policy_environment, solution, user, application_package_install, tenant_settings, data_loss_prevention_policy, connection, connection_share, rest, data_record. 24 data sources. Auth: SP+secret / SP+OIDC / Azure CLI. **NO managed identity auth.** SP cannot create Developer envs. Env-group direct-assign missing.

## F12 root cause (SpaarkeMaster 240 missing deps)

Not a `pac solution import` bug. It's SpaarkeMaster.zip being exported from a source env with 240+ components (first-party D365, other managed solutions, or unmanaged) that the fresh Model 1 Prod env doesn't have. Fix: extract solution.xml, enumerate `<MissingDependencies>`, categorize each, then install first-party prereqs / order dep chain / re-export from clean managed source. r1 doesn't have this diagnostic step wired anywhere.

## Spaarke r1 gap list

- **ABSENT** in design.md: Managed Environments enable (High — required for Env Groups + Pipelines + Sharing Limits governance); Environment Groups (Low for M1 single-env, Medium for M2 tier-based); PAYG billing plan (High for M2 dedicated); `restrictGuestUserAccess = false` flip after H5 (Medium — silent-fail risk for B2B onboarding).
- **PRESENT + aligned**: env create H5 (interim pac, target TF D14 — good), solution import H6 (Web API port planned Wave D-2 — good), app user H10 (TF `powerplatform_user` target, interim `pac admin assign-user --application-user` — good), user provisioning H11 (existing GraphUserService — good).
- **Doc staleness**: design.md doesn't cite Env Groups (2024 GA), PAYG for Dataverse, guest-access default flip (Mar 2026).

## 2026 shifts affecting Spaarke

- Fresh Azure subs = 0 App Service Plan quota in East US (Sp canonical = WestUS2+WestUS3).
- `restrictGuestUserAccess` DEFAULT TRUE new envs (Mar 2026).
- Solution-aware components reclassified to file storage (Apr 2026) — DB pressure ↓.
- Managed Envs included in ALL Power Apps + D365 licenses (was add-on).
- Environment Groups GA'd 2024 — pre-dates r1 design.
- Copilot Studio PAYG (Dec 2024).
- Dataverse Agents (Entra Agent ID) preview May 2026.

## Recommended plan (ordered)

Immediate: R1 diagnose SpaarkeMaster deps upstream → R2 fix packaging → R3 enable Managed Env on M1 Prod → R4 flip guest access. Short-term: R5 add H6a handler → R6 doc Env Group decision → R7 doc SpaarkeMaster packaging discipline → R8 doc PAYG deferral M1/adoption M2. Medium (Phase D-E): R9 H5→BAP REST → R10 H6→ImportSolutionAsync port → R11 add H11a guest-access flip. Long (M2 first customer): R12 H2c PAYG billing plan → R13 TF migration H5+H6+H10 → R14 H1a Env Group. Roadmap: R15 Package Deployer → R16 AppSource.

## Open questions for owner

Managed Envs for M1 yes/no · Env Groups scope (per-stamp vs deferred) · PAYG for M1 confirm no · `restrictGuestUserAccess = false` default at H5 · Package Deployer investment now vs later · M2 first-customer commit trigger · `sprk_dataverseenvironment` extend for managed-env/env-group/payg columns now vs later.

## Related memories

Sibling to [[ciam-user-provisioning-graph-2026-07-19]] (B2B invite Graph flow, `oid` link key), [[dataverse-webapi-create-rows-2026-07]] (systemuser POST pattern), [[dataverse-mcp-refresh-2026-07-05]] (Dataverse MCP GA — delegated-only, not for app-user create), [[graph-spe-standards-2026-08-16]] (SPE confidential-client + 24h replication). Complements existing r1 memory on Azure sub gotchas (see `feedback_spe_container_timing.md`, `feedback_fix_drift_at_discovery.md`).
