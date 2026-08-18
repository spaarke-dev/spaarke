# task-028-deviations.md

> **Task**: 028 — Author NEW `infrastructure/bicep/modules/uami.bicep`
> **Wave**: C2 (parallel-dispatch batch 1 of 6)
> **Date**: 2026-08-17
> **Rigor**: FULL @ opus/high

## Deviations from POML

### D1 — Output naming: `id` (POML said `resourceId`)

**POML step 2** specified outputs `(resourceId = uami.id, principalId = uami.properties.principalId, clientId = uami.properties.clientId)`.

**Shipped**: outputs are `id`, `name`, `principalId`, `clientId` — four outputs, not three.

**Rationale**:
- Dispatch context (parent-agent report contract) explicitly asked for `id`, `name`, `principalId`, `clientId`.
- Sibling modules (`app-service.bicep`, `key-vault.bicep`) prefix outputs with the resource kind (`appServiceId`, `keyVaultId`). In a **module-per-resource-kind** design like `uami.bicep`, unprefixed names read cleaner at the call site: `uami.outputs.id` vs. `uami.outputs.resourceId` are equivalent in intent; `id` is shorter + is the Azure Verified Modules convention.
- Added `name` output so downstream Dataverse env-var writes + logging don't need to reference the caller's `param name`.

**Impact**: Downstream tasks (029, 030, H4, H10) must consume `uami.outputs.id` (not `.resourceId`). Because task 028 is the FIRST landing of this module + tasks 029/030 are same-wave siblings, no callers exist to break; sibling tasks will consume the shipped names on first author.

### D2 — API version selection: `2023-01-31`

POML step 2 allowed `2023-01-31 (or current stable API version)`. Shipped `2023-01-31`.

**Rationale**: Latest non-preview stable GA release of `Microsoft.ManagedIdentity/userAssignedIdentities` at 2026-08-17. Used by Azure Verified Modules. No known breaking changes vs. later preview versions. Compiles cleanly (0 errors, 0 warnings).

### D3 — Scratch consumer removed after verification (not kept in-tree)

POML step 5 said "Author a minimal consumer test (a temporary .bicepparam or scratch consumer .bicep) confirming the outputs are typed strings + accessible."

**Shipped**: Wrote `uami.consumer-scratch.bicep`, ran `az bicep build --file uami.consumer-scratch.bicep` (exit 0, 0 errors, 0 warnings), then **deleted the scratch file + both compiled `.json` artifacts**.

**Rationale**: The POML wording ("temporary") + the acceptance criterion "Negative: this task adds NO other resources" reads as "verify then remove". Sibling `modules/*.bicep` compile-check artifacts are not checked into the tree (verified `infrastructure/bicep/modules/*.json` shows no per-module compiled ARM). Keeping the scratch file in-tree would create a maintenance burden (someone would eventually try to deploy it) with no test-harness value (task 029 will exercise the module for real).

## Verification

- `az bicep build --file infrastructure/bicep/modules/uami.bicep` → exit 0, 0 errors, 0 warnings.
- Scratch consumer (deleted post-verification) resolved all four outputs as typed `string`: `uami.outputs.id`, `uami.outputs.name`, `uami.outputs.principalId`, `uami.outputs.clientId`.
- Bicep CLI version: 0.46.1.

## Acceptance criteria mapping (POML)

| Criterion | Status |
|---|---|
| `az bicep build` succeeds with 0 errors + 0 warnings | ✅ verified (exit 0, no output) |
| Outputs `resourceId` + `principalId` + `clientId` typed string, resolve from consumer | ✅ verified via deleted scratch consumer (see D1: `id` not `resourceId`) |
| `tags` is parameter (not hardcoded), `location` defaults to `resourceGroup().location` | ✅ verified in source |
| Negative: no other resources beyond the UAMI itself | ✅ verified — module declares only one `resource` block |
| Negative: no changes to `modules/app-service.bicep` | ✅ verified — file untouched |

## ADR-028 compliance

- **MUST** use `DefaultAzureCredential` (managed identity) for all server outbound: this module IS the identity that later enables ADR-028's server-outbound MUST rule. UAMI object here → task 029 binds it to App Service → task 030 grants roles → BFF/handlers use `DefaultAzureCredential` at runtime. No violation.
- Amendments A1 / A2 / A3 (external-portal, Teams host, module-host platform): unaffected. UAMI is a per-customer server identity; the amendments govern client-side auth surfaces.

## Escalation not triggered

POML escalation trigger ("If UAMI resource cannot bind to both App Service slots in a downstream configuration test (task 029), STOP and escalate") is downstream (task 029). This task authors the module only; binding is task 029's scope.

## Sibling coordination

Wave C2 batch 1 siblings (per dispatch context):
- Task 027 (`customer.bicep` extension): will import `modules/uami.bicep` and pass through `name`/`location`/`tags` params. If task 027's PR lands first with placeholder module invocation, this task's PR wraps up authoring; task 027 will rebase cleanly since we only ADD a new file.
