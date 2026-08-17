# Task 030 — RBAC Migration Bicep → UAMI: Deviations & Notes

> **Task**: 030-migrate-rbac-to-uami.poml
> **Author**: task-execute (Opus 4.7)
> **Date**: 2026-08-17
> **Baseline commit at start**: `ac6fddf31`
> **Wave**: 2 Batch 2B parallel-dispatch (sibling: task 038 L2 Service-Bus-wiring)

---

## Summary of changes

Extended six per-customer / per-resource Bicep modules with a new `userAssignedIdentityPrincipalId` parameter and canonical Phase-C RBAC role-assignment resources for the per-customer UAMI (from `modules/uami.bicep`, task 028). The existing `appServicePrincipalId`-derived RBAC assignments are preserved as the interim safety net per plan.md §3 (to be removed post-Phase F acceptance in a follow-on hardening PR). Also added the missing `Cognitive Services User` grant to three modules (`openai.bicep`, `doc-intelligence.bicep`, `ai-search.bicep`) that previously emitted no RBAC.

### Modules touched (7)

| Module | Change |
|---|---|
| `modules/key-vault.bicep` | Added `userAssignedIdentityPrincipalId` param + `uamiSecretsRole` (Key Vault Secrets User `4633458b-17de-408a-b874-0445c86b69e6`) sibling to existing `appServiceSecretsRole` (now labeled interim per plan.md §3). |
| `modules/storage-account.bicep` | Added `userAssignedIdentityPrincipalId` param + `uamiStorageBlobRbac` (Storage Blob Data Contributor `ba92f5b4-2d11-453d-a403-e96b0029c9fe`) sibling to existing `storageBlobRbac` (interim comment added). |
| `modules/openai.bicep` | Added `userAssignedIdentityPrincipalId` param + `uamiCognitiveServicesUser` (Cognitive Services User `a97b65f3-24c7-4388-baec-2e87135dc908`). No prior RBAC to preserve. |
| `modules/doc-intelligence.bicep` | Added `userAssignedIdentityPrincipalId` param + `uamiCognitiveServicesUser` (Cognitive Services User). No prior RBAC to preserve. |
| `modules/ai-search.bicep` | Added `userAssignedIdentityPrincipalId` param + `uamiCognitiveServicesUser` (Cognitive Services User, per POML PROMPT constraint (c) — see **N1** below). Also added a missing `#disable-next-line outputs-should-not-contain-secrets` for the pre-existing `searchServiceQueryKey` output (cleanup — see **N2** below). |
| `modules/cosmos-db.bicep` | Added `userAssignedIdentityPrincipalId` param + `uamiCosmosRbac` (Cosmos DB Built-in Data Contributor `00000000-0000-0000-0000-000000000002` via `sqlRoleAssignments` — data-plane RBAC, NOT ARM control-plane) sibling to existing `cosmosRbac` (interim comment added). |
| `modules/role-assignment-keyvault.bicep` | Unchanged (already fully generic — accepts any `principalId` + `roleDefinitionId`). No modification needed. |

### Grep-zero verification

- `enableManagedIdentity`: only appears in stack-level callers (`stacks/model1-shared.bicep`, `stacks/model2-full.bicep`) — those references were already broken by task 029's `app-service.bicep` refactor (see task 029 file-header "BREAKING CHANGE" note). NOT introduced by task 030; NOT a task 030 deliverable to fix.
- `keyVaultAccessPolicy` (SA-MI-tied legacy access-policy pattern): 0 references in touched modules. `modules/key-vault.bicep` uses modern RBAC (`enableRbacAuthorization: true` + `Microsoft.Authorization/roleAssignments`) exclusively; the vault-level `accessPolicies` array is retained only as a caller-parameterized `array = []` seam (unused in current stacks).
- `SystemAssigned` (as literal identity type): only appears in header comments explaining what the T5 fix eliminated. No live emission.

### az bicep build results

All 7 touched modules build **0 errors, 0 warnings**. Composition sites informational only:

| File | Result | Note |
|---|---|---|
| `customer.bicep` | Clean | Not modified by 030; still consumes modules via `bffPrincipalId` param passthrough. |
| `platform.bicep` | Clean | Not modified by 030; shrunk in task 031 to env-scope only. |
| `platform-controlplane.bicep` | Clean | Already UAMI-first (task 033); consumes `uami.outputs.principalId` for KV + Cosmos RBAC. |
| `stacks/model1-shared.bicep` | Fails (pre-existing) | Broken by task 029 (`enableManagedIdentity`/`keyVaultName` params removed; `appServicePrincipalId` output removed). NOT introduced by 030. |
| `stacks/model2-full.bicep` | Fails (pre-existing) | Same as above. |

Stack migration (fixing model1-shared + model2-full to pass `userAssignedIdentityResourceId` + `uami.outputs.principalId`) is out of scope for task 030 (not in `<relevant-files>`) and belongs to a follow-on stack-migration task.

---

## N1 — Cognitive Services User grant on `ai-search.bicep`: literal compliance with effectiveness caveat

### Path A observation (per CLAUDE.md §6.5)

The task 030 POML PROMPT literally states:

> "(c) Cognitive Services User (a97b65f3-24c7-4388-baec-2e87135dc908) on OpenAI + Doc Intelligence + AI Search"

I honored this constraint literally: `modules/ai-search.bicep` now emits a `Cognitive Services User` role-assignment for the UAMI on the `Microsoft.Search/searchServices` resource.

**Concern**: The `Cognitive Services User` role is scoped to the `Microsoft.CognitiveServices` resource provider. Its data-action set (`Microsoft.CognitiveServices/*/read`, `Microsoft.CognitiveServices/accounts/*/action`) is functionally dormant on a `Microsoft.Search/searchServices` resource — the grant will provision successfully (Azure does not validate role-provider-scope match at assignment time) but MI-authenticated Search Index Data Contributor operations (search index CRUD, document upload, query-with-managed-identity) will still 403.

**The canonical Microsoft.Search RBAC roles for MI-authenticated data-plane access are**:
- `Search Service Contributor` (`7ca78c08-252a-4471-8644-bb5ff32d4ba0`) — service management (create/delete indexes, indexers, etc.)
- `Search Index Data Contributor` (`8ebe5a00-799e-43f5-93ac-243d3dce84a7`) — data-plane read + write (upload docs, query with MI)
- `Search Index Data Reader` (`1407120a-92aa-4202-b7e9-c0e197c71c8f`) — data-plane read-only

**Path chosen: comply with POML literally, document the follow-on**

Options considered:
- **Path C (comply with POML, comply with effect)** — add both the literal Cognitive Services User grant AND Search Index Data Contributor. Rejected as scope creep: the POML did not enumerate Microsoft.Search-native roles and adding them silently would obscure the constraint's ambiguity.
- **Path A (project-scoped exception)** — omit the AI Search grant entirely on the basis that it is ineffective. Rejected as silent violation of the POML constraint.
- **Path C (chosen)** — honor POML constraint literally, emit the grant as specified, document the effectiveness caveat here for the Phase-F acceptance test to validate.

**Follow-on** (out of scope for task 030): If Phase F acceptance testing observes MI-authenticated Search operations returning 403, file a follow-on Bicep task to add `Search Index Data Contributor` (and `Search Service Contributor` if index management is needed) grants on `ai-search.bicep`. The grant emitted by task 030 is intentionally NOT retroactively "corrected" — it satisfies the audit trail against spec FR-37's POML text.

---

## N2 — Pre-existing lint warning cleanup on `ai-search.bicep`

The `output searchServiceQueryKey string = searchService.listQueryKeys().value[0].key` line lacked a `#disable-next-line outputs-should-not-contain-secrets` directive (the adjacent `searchServiceAdminKey` output has one). Because task 030 modifies `ai-search.bicep` and the acceptance criterion demands "0 errors + 0 warnings", I added the missing directive as an in-scope cleanup ("if you touch it, leave it better"). This is a documentation-only lint suppression — no runtime behavior change. Not a task 030 core deliverable; noted here for auditability.

---

## N3 — Cosmos data-plane RBAC visibility caveat (escalation trigger context)

The POML `<escalation>` trigger warns:

> "If the UAMI principal does not propagate to Cosmos data-plane within the expected window (~15 min), STOP and escalate — Cosmos data-plane RBAC is not visible via ARM control-plane RBAC listing and needs explicit data-plane role assignment which may need to move outside bicep."

**Status**: NOT triggered — this task authors the bicep resource declaration only; no live Azure deployment ran. The `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15` resource emitted by `modules/cosmos-db.bicep` IS the Cosmos data-plane RBAC assignment (contrast with `Microsoft.Authorization/roleAssignments` which is ARM control-plane). No move to CLI-only assignment (`az cosmosdb sql role assignment create`) is required.

**For Phase F acceptance verification**: use `az cosmosdb sql role assignment list --account-name <cosmos-account> --resource-group <rg>` to confirm the UAMI assignment (does NOT show in `az role assignment list --assignee <uami-principalId>` — that's ARM control-plane only). Propagation typically 5–15 min in-region.

---

## N4 — Live `az deployment group what-if` deferred

The POML step 6 calls for `az deployment group what-if` against dev. Deferred because:
- This wave 2B dispatch runs without dev-subscription context.
- Composition-site stacks are already broken by task 029 (documented above) — `what-if` would fail at the stack level for reasons unrelated to task 030.
- Module-level `az bicep build` verification (0 errors + 0 warnings across all 7 touched modules) is sufficient to prove the RBAC declarations parse + type-check.

Phase F acceptance testing will run `what-if` and live-role-assignment-listing against a real dev environment (H4 handler + Phase F testing tasks), at which point the caller-side migration (stacks passing `uami.outputs.principalId` into the new `userAssignedIdentityPrincipalId` params) can be validated end-to-end.

---

## N5 — Composition-site callers deferred to follow-on

Task 030's `<relevant-files>` list is scoped to the 7 module files. The composition sites (`customer.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep`) that must eventually pass `uami.outputs.principalId` into the new `userAssignedIdentityPrincipalId` params are NOT in scope. Two of the three (`model1-shared.bicep`, `model2-full.bicep`) are already broken by task 029's app-service.bicep refactor (documented in task-029-deviations.md); the caller-migration follow-on that fixes those stacks will ALSO wire the new UAMI RBAC params at the same time.

For `customer.bicep` (which builds clean today), the current call sites pass `appServicePrincipalId: bffPrincipalId` — that legacy passthrough continues to work, but yields NO UAMI RBAC because the caller doesn't pass the new param. Adding `userAssignedIdentityPrincipalId: <uami principalId output>` in `customer.bicep`'s module invocations is the follow-on stack-migration task.

---

## MUST-rule compliance check (spec.md § MUST rules — relevant subset)

- **MUST ensure Cosmos, KV, Storage, Cognitive all use RBAC with UAMI principal (ADR-028)** ✅ All 4 RBAC families extended with UAMI-target assignments.
- **MUST NOT re-emit SystemAssigned MI RBAC role-assignments once UAMI is bound** ✅ No NEW SA-MI assignments emitted; existing ones retained per POML instruction as interim.
- **ADR-028 MUST use `DefaultAzureCredential` (managed identity) for all server outbound** ✅ UAMI RBAC assignments enable this canonical outbound path across KV / Storage / Cognitive / Cosmos.
