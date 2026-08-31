# Wave 4 Batch 4E drift-5 — Bicep BCP035 / BCP037 / BCP053 cleanup

**Date**: 2026-08-18
**Trigger**: Discovered by Wave 4C task 086 subagent while verifying `az bicep build` after IaC alignment to canonical KV secret names. Errors are **not new** — they are debt from the task 029 refactor of `modules/app-service.bicep` (UAMI-only, T5 structural fix) which explicitly deferred stack-caller migration to a follow-on task that never landed. Owner authorized cleaning them up as pre-Batch-4E work under fix-at-discovery so task 089 (E2E Model 1 acceptance) can run the end-to-end Bicep dry-run unblocked.

**Rigor**: STANDARD (small cleanup; no new architectural work).

**Scope guardrails**:
- Do NOT redesign the stacks — just resolve the compile errors.
- Do NOT touch `.claude/`, `src/server/**`, `scripts/canonical-secret-catalog/**`.
- Do NOT undo task 032's semantic refactor of `model1-shared.bicep`.

---

## Reproduction (pre-fix)

```
$ az bicep build --file infrastructure/bicep/stacks/model1-shared.bicep
ERROR: model1-shared.bicep(403,3) : BCP035: The specified "object" declaration is missing the following required properties: "userAssignedIdentityResourceId".
       model1-shared.bicep(407,5) : BCP037: The property "keyVaultName" is not allowed on objects of type "params". Permissible properties include "runtimeStack", "userAssignedIdentityResourceId", "vnetIntegrationSubnetId".
       model1-shared.bicep(408,5) : BCP037: The property "enableManagedIdentity" is not allowed on objects of type "params". Permissible properties include "runtimeStack", "userAssignedIdentityResourceId", "vnetIntegrationSubnetId".
       model1-shared.bicep(576,59) : BCP053: The type "outputs" does not contain property "appServicePrincipalId". Available properties include "appServiceDefaultHostName", "appServiceId", "appServiceName", "appServiceUrl".

$ az bicep build --file infrastructure/bicep/stacks/model2-full.bicep
ERROR: model2-full.bicep(156,43) : BCP053: The type "outputs" does not contain property "appServicePrincipalId". ...
       model2-full.bicep(180,3)  : BCP035: The specified "object" declaration is missing the following required properties: "userAssignedIdentityResourceId".
       model2-full.bicep(184,5)  : BCP037: The property "keyVaultName" is not allowed on objects of type "params". ...
       model2-full.bicep(185,5)  : BCP037: The property "enableManagedIdentity" is not allowed on objects of type "params". ...
       model2-full.bicep(244,33) : BCP053: The type "outputs" does not contain property "appServicePrincipalId". ...
       model2-full.bicep(262,36) : BCP053: The type "outputs" does not contain property "appServicePrincipalId". ...
       model2-full.bicep(471,47) : BCP053: The type "outputs" does not contain property "appServicePrincipalId". ...
```

Pre-existing WARNINGS unchanged by this cleanup (out of scope):
- `modules/dashboard.bicep` — 17× BCP036 (MonitorChartPart / MarkdownPart type mismatch)
- `stacks/model2-full.bicep` — ~17× BCP318 (conditional-module null-safety on `enableVnet` / `enableAiFoundry` / `enableMonitoringDashboard` accesses)

---

## Root cause

Task 029 (`029-refactor-app-service-bicep-uami.poml`) rewrote `modules/app-service.bicep` to be UAMI-only per ADR-028 + T5 (slot-swap identity rotation) structural fix. That refactor:

- **REMOVED** `keyVaultName` param (SA-MI-tied KV access policy no longer emitted here — task 030 owns UAMI-based RBAC via `role-assignment-keyvault.bicep`).
- **REMOVED** `enableManagedIdentity` param (SA-MI toggle no longer meaningful — module always emits `identity.type = 'UserAssigned'`).
- **ADDED** required `userAssignedIdentityResourceId` param (the sole identity now bound to the App Service, mirrored to staging slot).
- **REMOVED** `output appServicePrincipalId` (the App Service resource no longer has a SystemAssigned principalId; downstream consumers MUST read the UAMI's `principalId` at the caller level).

Task 029's own header explicitly documents this:

> **BREAKING CHANGE (caller migration required)**: This refactor REMOVES `enableManagedIdentity` and `keyVaultName` parameters. Existing callers (`platform.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep`) still pass these params and will FAIL `az bicep build` at the stack level until they are migrated to pass `userAssignedIdentityResourceId` instead. Follow-on task will migrate the callers atomically.

That follow-on task never landed. Task 032 refactored `model1-shared.bicep` into first-class shape but did not migrate the `app-service.bicep` call site. `model2-full.bicep` was never touched. Task 086 IaC alignment (canonical KV secret names) didn't affect the app-service call surface, so it surfaced but didn't fix these errors either.

Note: `customer.bicep` and `platform.bicep` accept the UAMI as a PASS-THROUGH parameter but do NOT themselves invoke `app-service.bicep`, so they compile clean. The stacks under `stacks/**` are the only callers with the broken shape.

---

## Fix — minimal caller migration (matches task 029's intended contract)

Same pattern in both stacks:

1. **Introduce a stable UAMI** for the BFF App Service via `modules/uami.bicep` — one per stack, deployed to the same RG as the BFF App Service.
2. **Wire `userAssignedIdentityResourceId: <bffUami>.outputs.id`** into the `app-service.bicep` invocation; drop the removed `keyVaultName` + `enableManagedIdentity` params.
3. **Repoint every `<bffApi>.outputs.appServicePrincipalId` read to `<bffUami>.outputs.principalId`** — the stable UAMI principal (which survives slot swaps + App Service delete/recreate per T5 structural fix) is the canonical identity for all downstream RBAC / Dataverse App User / Graph app-role grants.

### `stacks/model2-full.bicep` — 5 changes

- Added param `bffUamiName` (defaults to `sprk-${environment}-${customerId}-uami` per uami.bicep header convention).
- Added `module bffUami '../modules/uami.bicep'` invocation before `storage` (which reads its `principalId`).
- Migrated `bffApi` params: removed `keyVaultName`, `enableManagedIdentity`; added `userAssignedIdentityResourceId: bffUami.outputs.id`.
- Repointed 4 `bffApi.outputs.appServicePrincipalId` reads to `bffUami.outputs.principalId`:
  - `storage` module → `appServicePrincipalId` param
  - `kvRbacAppService` (role-assignment-keyvault) → `principalId` param
  - `membershipTopic` → `bffPrincipalId` param
  - `apiPrincipalId` output
- Added 2 new outputs: `apiUamiResourceId`, `apiUamiClientId` (downstream consumers — Dataverse App User registration, Graph client — need these).

### `stacks/model1-shared.bicep` — 3 changes

- Added param `sharedBffUamiName` (defaults to `sprk-${environment}-shared-bff-uami`).
- Added `module sharedBffUami '../modules/uami.bicep'` invocation in `sharedRg` (this is the SHARED-BFF UAMI, distinct from `perTenantUami` which stays for per-tenant KV / Graph / Dataverse App User parity).
- Migrated `sharedBffApi` params: removed `keyVaultName`, `enableManagedIdentity`; added `userAssignedIdentityResourceId: sharedBffUami.outputs.id`.
- Repointed 1 `sharedBffApi.outputs.appServicePrincipalId` read (the `sharedApiPrincipalId` output) to `sharedBffUami.outputs.principalId`; added `sharedApiUamiResourceId` + `sharedApiUamiClientId` outputs.

**Intentionally NOT changed** (documented as future work in comments):
- KV Secrets User grant to the shared BFF UAMI in `model1-shared.bicep` — a follow-on concern parallel to the per-tenant Data-plane RBAC deferral that task 032 already documented at the Cosmos module. `model1-shared.bicep` never had a `role-assignment-keyvault.bicep` invocation for the shared BFF, so this is a pre-existing gap, not a regression of this cleanup.
- Migrating the various `appServicePrincipalId` param names on downstream modules (`storage-account.bicep`, `key-vault.bicep`, `cosmos-db.bicep`, `doc-intelligence.bicep`) to the canonical `userAssignedIdentityPrincipalId`. Those modules kept both param surfaces per task 030's plan (`appServicePrincipalId` is "retained for legacy callers as interim safety net per plan.md §3 — remove post-Phase F acceptance"). Passing the UAMI's `principalId` into `appServicePrincipalId` is semantically correct (both slots grant the same role to that principal) and keeps this diff minimal.

---

## Verification

```
$ az bicep build --file infrastructure/bicep/stacks/model1-shared.bicep
(exit 0; only pre-existing dashboard BCP036 warnings remain)

$ az bicep build --file infrastructure/bicep/stacks/model2-full.bicep
(exit 0; only pre-existing dashboard BCP036 + conditional-module BCP318 warnings remain)

$ pwsh -File scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
Manifest shape:    OK (32 secrets)
BINDING never-delete guard: OK (Dataverse-ClientSecret, BFF-API-ClientSecret)
Dev exception guard:        OK (spaarke-spekvcert)
VERIFY: OK - generated/ is in sync with manifest.yaml.
(exit 0)
```

Task 086's canonical secret-catalog verification is unchanged by this cleanup — this drift touched only the App Service identity wiring, not any secret name.

---

## Files modified

- `infrastructure/bicep/stacks/model1-shared.bicep` (+21 -3)
- `infrastructure/bicep/stacks/model2-full.bicep` (+30 -6)
- `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` (+1 drift-5 row)
- `projects/customer-provisioning-orchestration-r1/notes/wave-4-drift-5-bicep-bcp-cleanup.md` (this file)

**Not modified** — `modules/app-service.bicep` (already correct — the stacks were the drifted callers), `customer.bicep`, `platform.bicep` (accept UAMI as pass-through, don't invoke app-service.bicep themselves), `scripts/canonical-secret-catalog/**` (task 084/086 owned).

---

## Follow-on (not this drift's scope)

- **Grant KV Secrets User on `sharedKeyVault` to `sharedBffUami.outputs.principalId`** in `model1-shared.bicep` — the shared multi-tenant BFF needs KV secret reads to resolve the `@Microsoft.KeyVault(...)` references in `appSettings`. Currently no `role-assignment-keyvault.bicep` invocation exists for the shared BFF; add one when Model 1 tier moves toward acceptance.
- **Dashboard BCP036 warnings** (17×) — MonitorChartPart type mismatch in `modules/dashboard.bicep`. Separate cleanup; not caller-migration debt.
- **Conditional-module BCP318 warnings** (17×) in `model2-full.bicep` — accesses like `vnet.outputs.snetAppId` in expressions where `vnet` may be null (guarded by `enableVnet` boolean, but the compiler can't prove that). Fix pattern: `enableVnet ? vnet!.outputs.snetAppId : ''` (null-forgiving) or `vnet.?outputs.snetAppId ?? ''` (null-coalescing). Not this drift's scope.
