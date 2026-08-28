# task-031-topology-decision.md

> **Task**: 031 — Rebuild `platform.bicep` (shrink to control-plane-only per D12)
> **Wave**: C2 (parallel-dispatch batch 2A, siblings: 025 / 029 / 037)
> **Date**: 2026-08-17
> **Rigor**: FULL @ opus/high
> **Baseline commit**: `0cfe0c614` (50 ahead of origin at kick-off; sibling agents held uncommitted changes in tree)

## Files modified

1. `infrastructure/bicep/platform.bicep` — shrunk from 316 lines to ~135 lines (includes comprehensive header rationale)
2. `infrastructure/bicep/parameters/platform-prod.bicepparam` — aligned to shrunk schema (removed obsolete AI + App Service Plan parameters)
3. `projects/customer-provisioning-orchestration-r1/notes/task-031-topology-decision.md` — this file

## Files NOT touched (deliberately)

- `infrastructure/bicep/platform-controlplane.bicep` — sibling task 033's file; zero overlap with task 031 per parent orchestrator directive.
- `infrastructure/bicep/customer.bicep` — sibling task 027's file (extensions already landed at `0ca76777a`).
- `infrastructure/bicep/stacks/*.bicep` — Model 1 / Model 2 first-class stacks; consumers do not import platform.bicep (verified via grep).
- `scripts/Deploy-Platform.ps1` — header comments will read as stale ("Azure OpenAI, AI Search, Doc Intelligence, App Service...") but the script itself only invokes `az deployment sub create` with the (now shrunk) template + bicepparam. Flagged for follow-on Wave D deploy tooling; explicitly out of scope for this Bicep-only task.
- Any `.claude/**` — sub-agent write boundary (root CLAUDE.md §3); also nothing in the task scope required it.

## Topology decision (per CLAUDE.md §6.5)

### Path C — Comply with D12 (pivot fully to spec design intent)

**Pre-shrink `platform.bicep` composed** (316 lines):

| # | Resource | Post-shrink disposition |
|---|---|---|
| 1 | Resource Group `rg-spaarke-platform-{env}` | **KEEP** — remains the platform RG (shared with `platform-controlplane.bicep`) |
| 2 | Monitoring (`sprk-platform-{env}-insights` + `sprk-platform-{env}-logs`) | **KEEP** — env-scope operational visibility (distinct from L2's `sprk-controlplane-{env}-*`) |
| 3 | Platform KV (`sprk-{env}-kv`, param default) | **KEEP** — env-scope shared vault per §7.9 canonical naming |
| 4 | App Service Plan (`spaarke-bff-{env}-plan`) | **REMOVE** — BFF Plan now lives per-tier in `stacks/*.bicep` |
| 5 | App Service `spaarke-bff-{env}` (BFF) + config override + staging slot | **REMOVE** — BFF now lives per-tier in `stacks/model1-shared.bicep` (multi-tenant) or `stacks/model2-full.bicep` (dedicated) |
| 6 | Azure OpenAI (`spaarke-openai-{env}`) | **REMOVE** — per D12; per-customer AI belongs in `customer.bicep` / `stacks/*.bicep` |
| 7 | AI Search (`spaarke-search-{env}`) | **REMOVE** — per D12 (same rationale) |
| 8 | Document Intelligence (`spaarke-docintel-{env}`) | **REMOVE** — per D12 (same rationale) |
| 9 | Cosmos DB (`spaarke-cosmos-{env}`, `spaarke-ai` DB) | **REMOVE** — per D12 (per-customer Cosmos already in `customer.bicep` at line 213–223 since task 014; L2 provisioning-run Cosmos is in `platform-controlplane.bicep`) |

### Why the shrink is more aggressive than "just remove AI"

The POML step 3 says *"Remove per-customer AI module invocations from platform.bicep (OpenAI, AI Search, Doc Intelligence, per-customer Cosmos, per-customer App Insights). Preserve control-plane KV + monitoring + env-scope service-bus / VNet."* — which literally would leave the BFF App Service in place.

Two forcing functions pushed the shrink past that literal reading:

1. **`modules/app-service.bicep` was refactored by Wave C2 sibling task 029** (verified in the modified files list at kick-off: `M infrastructure/bicep/modules/app-service.bicep`, `M infrastructure/bicep/modules/app-service-slot.bicep`, `M infrastructure/bicep/modules/deployment-slot.bicep`). The refactor removed `keyVaultName` + `enableManagedIdentity` params, made `userAssignedIdentityResourceId` REQUIRED, and dropped the `appServicePrincipalId` output — a UAMI-first structural signal.

   Running `az bicep build platform.bicep` at kick-off confirmed the structural break — 6 errors, all in the BFF App Service + slot module invocations:

   ```
   BCP035 (169,3): missing "userAssignedIdentityResourceId" on bffApi params
   BCP037 (173,5): "keyVaultName" not allowed on bffApi params
   BCP037 (174,5): "enableManagedIdentity" not allowed on bffApi params
   BCP035 (207,3): missing "userAssignedIdentityResourceId" on bffApiStagingSlot params
   BCP053 (276,43): bffApi.outputs.appServicePrincipalId does not exist
   BCP053 (293,47): bffApi.outputs.appServicePrincipalId does not exist
   ```

   Options considered:
   - **(a) Keep BFF, pass a UAMI**: requires adding a UAMI here (belongs in `platform-controlplane.bicep` for L2 per task 033, or per-customer per task 028; neither maps naturally to a shared env-scope BFF).
   - **(b) Fork yet another App Service module**: duplicates task 033's `controlplane-app-service.bicep` pattern for no functional gain.
   - **(c) Remove the BFF App Service chain from platform.bicep entirely (SHIPPED)**: aligns with D12 (per-customer AI move) + the stacks that already provision their own BFF (Model 1 shared / Model 2 dedicated).

2. **Redundancy with `stacks/model1-shared.bicep`** (task 032, shipped at `f7c5a69c4`) — that stack already provisions its own `${sharedBaseName}-api` App Service (line 400) with the correct multi-tenant BFF wiring (MULTI_TENANT_MODE=true + per-tenant AI resolution per §4D I5). Keeping a parallel BFF in `platform.bicep` would create two ways to deploy the same tier, plus a naming collision (`spaarke-bff-{env}` here vs `${sharedBaseName}-api` there in the same RG family).

3. **Redundancy with `stacks/model2-full.bicep`** — that stack provisions a dedicated per-customer BFF (`${baseName}-api`) with per-customer AI dependencies. The pre-shrink `platform.bicep` BFF has no analogous customer to serve after D12's per-customer AI move.

### Explicit consideration of paths A / B / C per CLAUDE.md §6.5

- **Path A (project-scoped exception)**: Would allow platform.bicep to keep a "legacy shared BFF" as-is. Rejected — this preserves the pre-D12 topology that the whole r1 project is designed to unwind. The exception would be indefinite scope creep.
- **Path B (ADR amendment)**: Would propose amending D12 to permit a shared platform-scope BFF. Rejected — D12 is the design intent driving Wave C2 in the first place; amending it undoes the project.
- **Path C (comply with D12, SHIPPED)**: Full shrink to env-scope shared infrastructure only. Selected.

## Coordination notes (Wave C2 batch 2A siblings)

| Sibling | File touched | Overlap with this task | Notes |
|---|---|---|---|
| **025** (ArchTest) | `tests/Spaarke.ArchTests/**` | None | Not Bicep-adjacent. |
| **029** (app-service.bicep refactor) | `modules/app-service.bicep`, `modules/app-service-slot.bicep`, `modules/deployment-slot.bicep` | INDIRECT (via BCP errors) — task 031 shrink RESOLVES the errors task 029 exposed in platform.bicep. Consumers (stacks/*.bicep) are expected to pass UAMI via their own tier composition; this task simply stops platform.bicep from being a consumer. | Confirmed at kick-off by inspecting the modified files list on the shared work tree. |
| **037** (L2 Cosmos wiring) | `src/server/services/Sprk.Provisioning.ControlPlane/**`, `tests/**` | None (C# only). | |
| **033** (already committed at `d3b994434`) | `platform-controlplane.bicep`, `modules/controlplane-app-service.bicep` | None — task 033 created NEW file; task 031 shrinks EXISTING file. Different files, coordinated topology (see `platform.bicep` header COORDINATION section). | See `task-033-deviations.md` line 154 which explicitly says task 031 "does NOT need to invoke this task's file" — coordinated at design time. |

## Verification

### `az bicep build`

```
$ az bicep build --file infrastructure/bicep/platform.bicep --stdout | head -5
{
  "$schema": "https://schema.management.azure.com/schemas/2018-05-01/subscriptionDeploymentTemplate.json#",
  "contentVersion": "1.0.0.0",
  "metadata": {
    "_generator": {
```

**Result**: 0 errors, 0 warnings on final build. (Pre-shrink: 6 errors, 0 warnings.)

### Downstream stacks — build integrity check

Verified via `az bicep build` on all three stacks:
- ✅ `stacks/model1-shared.bicep` — 0 errors (no dependency on `platform.bicep`)
- ✅ `stacks/model2-full.bicep` — see note below
- ✅ `stacks/model1-customer.bicep` — 0 errors (RG-scope; consumes shared KV name string, not platform.bicep outputs)

`model2-full.bicep` build result: has pre-existing warnings + errors from an unrelated Wave C2 in-flight refactor (references removed `disableSharedKeyAccess`, `allowedSubnetId` etc.). NOT caused by this task 031 shrink — grep confirms `model2-full.bicep` does not import or reference `platform.bicep` outputs. Cross-checked by attempting to build both pre- and post-shrink; the same errors surface either way.

### `az deployment sub what-if`

Deferred per parent orchestrator directive (no dev subscription context available in the parallel-dispatch runtime). `az bicep build` is sufficient for structural verification.

### Acceptance criteria (POML)

| Criterion | Result |
|---|---|
| `az bicep build` succeeds with 0 errors + 0 warnings | ✅ (pre-shrink was BROKEN — task fixed 6 pre-existing errors) |
| `what-if` shows only control-plane / env-scope resources | ⏭️ Deferred (no sub context); replaced by build-integrity + template inspection |
| No reference to modules/openai.bicep / ai-search.bicep / doc-intelligence.bicep for per-customer contexts | ✅ (all four AI module invocations removed) |
| Negative: stacks/*.bicep still builds (no upstream break) | ✅ (Model 1 shared / customer + Model 2 full; Model 2 pre-existing errors independent of this task) |

## Publish-size impact

**Zero** — Bicep-only task; no BFF C# code touched. `dotnet publish` size unchanged from 44.96 MB baseline (2026-08-13, `dotnet-10-upgrade-r1` task 031, .NET 10 framework-dependent linux-x64).

## CVE impact

**Zero** — no NuGet package changes. `dotnet list package --vulnerable --include-transitive` result unchanged.

## Follow-on work (out of scope; not blocked)

1. **`scripts/Deploy-Platform.ps1` header comments** — describe pre-shrink resources; will read as stale. Update in Wave D deploy tooling.
2. **SPAARKE-INTERNAL dev/staging/prod BFF re-deployment** — the live `spaarke-bff-{env}` App Services deployed by pre-shrink `platform.bicep` continue to exist in Azure (this is IaC-only; nothing gets destroyed). Future SPAARKE-INTERNAL BFF deployments should use `stacks/model1-shared.bicep`. Ops decision; not a code concern.
3. **`docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md`** — references `platform.bicep` composition; may need refresh. Flagged for the Phase A doc consolidation task.
