# task-033-deviations.md

> **Task**: 033 — Author NEW `infrastructure/bicep/platform-controlplane.bicep` — L2 orchestrator infrastructure
> **Wave**: C2 (parallel-dispatch batch 2 of 6, sibling of task 032)
> **Date**: 2026-08-17
> **Rigor**: FULL @ opus/high
> **Baseline commit**: `ebbd8d8e0` (46 ahead of origin, tree clean at batch 2 kick-off)

## Files created

1. `infrastructure/bicep/platform-controlplane.bicep` (subscription-scope composition, ~380 lines with comments)
2. `infrastructure/bicep/modules/controlplane-app-service.bicep` (RG-scope App Service + slot module, ~180 lines with comments)

Two files rather than one because Bicep disallows RG-scope resource declarations inside a subscription-scope file (BCP037/BCP139) — a NEW dedicated module was needed for the UAMI-only App Service. See D2 below.

## Deviations from POML

### D1 — POML step 4 said "invoke `modules/app-service.bicep` (task 029 UAMI-refactored)" — NOT invoked; used a new dedicated `controlplane-app-service.bicep` module instead

**POML text**: *"Invoke `modules/app-service.bicep` (task 029 UAMI-refactored) — .NET 10 stack, ≥ P1v3 SKU, appSettings including audience `api://spaarke-provisioning-controlplane-{env}`."*

**Shipped**: created NEW `modules/controlplane-app-service.bicep` and invoked THAT.

**Rationale (CLAUDE.md §6.5 path A — project-scoped exception)**:
- Task 029 has NOT shipped at Wave C2 batch 2 kick-off (git log confirms last touch of `modules/app-service.bicep` is `2b4e...` from a prior date). The module still emits `SystemAssigned` MI ONLY and carries no UAMI parameter.
- ADR-028 MUST-rule: *"MUST use `DefaultAzureCredential` (managed identity) for all server outbound"* + T1/T5 structural fix requires ONE UAMI binding BOTH slots (so KV RBAC + Dataverse App User + Graph app-roles do not drift on slot swap).
- Options considered:
  - **(a) Wait for task 029**: blocks Wave C2 batch 2; couples parallel-safe tasks.
  - **(b) Invoke unchanged `modules/app-service.bicep`**: emits SystemAssigned MI; either co-mixed with a subsequent PATCH (anti-pattern per ADR-028) or requires a follow-up delete-and-recreate step.
  - **(c) Fork a dedicated module (SHIPPED)**: emits UAMI-only from birth; keeps `modules/app-service.bicep` untouched for BFF consumers; task 029 refactor can OPTIONALLY retire the fork later (topology equivalent).
- Option (c) is the smallest correct step: it preserves the parallel-safe contract, honors the ADR, and creates zero blast-radius on BFF.

**Impact if kept**: two App Service modules coexist in `modules/`. Small acceptable duplication (~180 LOC), well-documented in both file headers, easy to consolidate post-task-029 landing.

**Explicit consideration of paths A/B/C per CLAUDE.md §6.5**:
- Path C (comply): would require synchronously coordinating with task 029, or shipping a non-compliant SystemAssigned identity. Rejected.
- Path B (ADR amendment): not warranted — ADR-028 is correct as written.
- Path A (documented exception scoped to THIS task): SELECTED. The exception is scoped: the new module exists ONLY for the L2 control plane; general-purpose App Service composition still routes through the shared module (once task 029 refactors it).

### D2 — L2 App Service composed via module (not inline in `platform-controlplane.bicep`)

**Rationale (technical, not procedural)**: Bicep subscription-scope files cannot declare RG-scope resources inline — attempted first, hit BCP037/BCP139/BCP120. The idiomatic pattern (see `platform.bicep` + `customer.bicep`) is to `scope: rg` on `module` blocks; resources go inside modules. The intent (UAMI-only from birth) is fully preserved.

### D3 — POML step 6 said vault name `spaarke-controlplane-{env}-kv` (or per canonical Phase G)

**Shipped**: vault name is `sprk-controlplane-{env}-kv` — canonical `sprk-` prefix per `AZURE-RESOURCE-NAMING-CONVENTION.md` § R3 (also matches `platform.bicep`'s adopted `sprk-{env}-kv` for the BFF platform vault, task 018).

**Rationale**: POML explicitly allowed the canonical form as an alternative. Chose canonical to avoid seeding a new naming exception (Phase H remediation would just remove it later). `controlplane` remains in the scope segment because L2 has its OWN secrets (audience/app-reg/Service-Bus/Dataverse-orchestration credentials) distinct from BFF's `sprk-{env}-kv`.

### D4 — POML step 3 said "invoke uami.bicep IF UAMI should be created here ... otherwise consume the passed-in `userAssignedIdentityResourceId`"

**Shipped**: created UAMI HERE (invoked `modules/uami.bicep`); did NOT accept a passed-in resource ID.

**Rationale**:
- The control-plane UAMI is fleet-scoped (one per env), distinct from per-customer UAMIs (task 028's module is intended for `sprk-{env}-{customerId}-uami`).
- Creating it inside this stack means the RBAC grants (Cosmos data-contributor, KV secrets user) can be atomic in the SAME deployment — no cross-stack ordering.
- The UAMI resource ID + client ID + principal ID all flow OUT as stack outputs, so external consumers (H4/H10 handlers, ops scripts) still reference them by name/ID after deployment.

### D5 — Deployment `slot-sticky` settings not declared

**POML did NOT explicitly require slot-sticky settings.** Deferred to ops scripts post-deploy.

**Rationale**: Slot-sticky config (`Microsoft.Web/sites/config` with `slotConfigNames` under `properties`) is operational — the correct set depends on what env-vars must NOT swap (e.g. staging-only feature flags). Declaring an empty set is fine; leaving to Phase F ops scripts avoids over-committing on ops semantics from Bicep.

## Not deviations (explicit design choices worth calling out)

### N1 — Service Bus is NOT created here (ADR-036 reuse)

Per ADR-036 (background-job infrastructure), the L2 control plane REUSES the environment-scope Service Bus provisioned elsewhere (`modules/service-bus.bicep` for the SB namespace + `modules/membership-topic.bicep` for the SB topic). This stack takes `serviceBusKeyVaultSecretName` as a parameter and wires an `@Microsoft.KeyVault(...)` reference into App Service `ServiceBus__ConnectionString`. Zero new SB resources.

### N2 — `keyVaultReferenceIdentity` PATCH is NOT applied here

Per spec.md MUST rule + design.md T1, `keyVaultReferenceIdentity` MUST be PATCHed to the UAMI resource ID on BOTH slots. This is done by handler **H4 post-deploy** — not by Bicep. Setting it at CREATE-time may race with the KV role assignment (App Service may boot before the RBAC grant lands). Bicep provisions the resource shape; H4 applies the PATCH deterministically after RBAC settles.

### N3 — No per-customer AI resources declared (matches POML negative acceptance)

Grep of both files confirms: only reference to `openai` / `aisearch` / `docintel` is a comment in the file header saying "MUST NOT be declared here" (design intent per D3/D12). Those live in `customer.bicep`.

### N4 — Cosmos DB composed via task 024's `modules/cosmos-provisioning.bicep` (NOT re-declared)

Task 024's module fully provisions: account (`EnableServerless`, `disableLocalAuth:true`, `Continuous7Days` backup, `Session` consistency) + `spaarke-provisioning` database + `runs` container (`/customerId` partition, composite indexes for reconciler + fleet-view queries, 365-day TTL) + Cosmos DB Built-in Data Contributor RBAC to L2 MI. Invoked directly here with `controlPlanePrincipalId = uami.outputs.principalId` (fleet-scoped grant to the control-plane UAMI). Duplication would be a bug.

### N5 — App Insights + Log Analytics dedicated to control-plane (NOT shared with BFF platform)

Chose `sprk-controlplane-{env}-insights` / `sprk-controlplane-{env}-logs` instead of consuming BFF's `sprk-platform-{env}-insights`. Rationale: L2 telemetry is fleet-orchestration audit trail (NFR-11); mixing with BFF's per-customer telemetry would complicate querying + retention policy tuning. Both live in the same RG (`rg-spaarke-platform-{env}`) so cross-workspace queries remain trivial when needed.

## Verification

### `az bicep build`

```
$ az bicep build --file infrastructure/bicep/platform-controlplane.bicep
$ echo $?
0
```

**Result**: 0 errors, 0 warnings on final build. Initial build had:
- 5 errors (fixed by extracting App Service to a dedicated module — see D2).
- 1 no-hardcoded-env-urls warning (fixed by switching `AzureAd__Instance` from hardcoded `https://login.microsoftonline.com/` to `environment().authentication.loginEndpoint`).

### `az deployment sub what-if`

Executed against Spaarke Development Environment (`484bc857-3802-427f-9ea5-ca47b43db0f0`):

```
$ az deployment sub what-if \
    --location westus2 \
    --template-file infrastructure/bicep/platform-controlplane.bicep \
    --parameters environmentName=dev
```

**Status**: `Succeeded`, `error: null`.

**Resources to be CREATED** (14 top-level resources):

| # | Resource | Type |
|---|---|---|
| 1 | `rg-spaarke-platform-dev` | `Microsoft.Resources/resourceGroups` |
| 2 | `sprk-controlplane-dev-logs` | `Microsoft.OperationalInsights/workspaces` |
| 3 | `sprk-controlplane-dev-insights` | `Microsoft.Insights/components` |
| 4 | `sprk-controlplane-dev-uami` | `Microsoft.ManagedIdentity/userAssignedIdentities` |
| 5 | `sprk-controlplane-dev-kv` | `Microsoft.KeyVault/vaults` |
| 6 | KV → LA diagnostic settings | `Microsoft.Insights/diagnosticSettings` |
| 7 | KV → UAMI Secrets User RBAC | `Microsoft.Authorization/roleAssignments` (Unsupported changeType due to chained-ref limitation) |
| 8 | `cosmos-spaarke-platform-dev` | `Microsoft.DocumentDB/databaseAccounts` |
| 9 | `spaarke-provisioning` | `Microsoft.DocumentDB/databaseAccounts/sqlDatabases` |
| 10 | `runs` | `Microsoft.DocumentDB/databaseAccounts/.../containers` |
| 11 | Cosmos → UAMI Data Contributor SQL role assignment | `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments` (Unsupported changeType — same reason) |
| 12 | `spaarke-controlplane-dev-plan` | `Microsoft.Web/serverfarms` |
| 13 | `spaarke-provisioning-controlplane-dev` | `Microsoft.Web/sites` |
| 14 | `staging` slot | `Microsoft.Web/sites/slots` |

Two `Unsupported` change-types (#7 and #11) are the known Bicep what-if limitation for role-assignment names computed from `reference(...)` at deploy time. They are NOT authoring errors — they will land at deploy time. `az deployment sub what-if` returned status `Succeeded`.

### Acceptance criteria (POML)

- ✅ `az bicep build` succeeds with 0 errors + 0 warnings.
- ✅ `what-if` against dev shows all 5 resource families (App Service + slot, Cosmos DB + database + container + RBAC, KV + RBAC + diagnostics, App Insights, Log Analytics + UAMI) added.
- ✅ App Service identity is `UserAssigned` bound to the control-plane UAMI (verified in what-if `identity.userAssignedIdentities` map + resource ID chain).
- ✅ App Service `AzureAd__Audience` is `api://spaarke-provisioning-controlplane-dev` (via `jwtAudience` variable interpolation).
- ✅ Cosmos account is serverless — `EnableServerless` capability inherited from `modules/cosmos-provisioning.bicep`.
- ✅ Negative: grep confirms NO per-customer AI resources (OpenAI / AI Search / Doc Intelligence) declared — only mentioned in header comments as "out of scope".

## Publish-size impact

**Zero** — Bicep-only task; no BFF code touched. `dotnet publish` size unchanged from 44.96 MB baseline (2026-08-13, `dotnet-10-upgrade-r1` task 031).

## Coordination notes

- **Task 032 (sibling)**: refactors `stacks/model1-shared.bicep`. Zero file overlap with this task.
- **Task 024 (prerequisite, complete at `ebbd8d8e0`)**: authored `modules/cosmos-provisioning.bicep` — consumed via module invocation here (per N4 above). No duplication.
- **Task 028 (prerequisite, complete at `b17a146ca`)**: authored `modules/uami.bicep` — consumed for the control-plane UAMI here (per D4 above).
- **Task 029 (upcoming, not shipped)**: will refactor `modules/app-service.bicep` for UAMI. When it lands, `modules/controlplane-app-service.bicep` MAY be retired in favor of two invocations of the refactored shared module. Retirement is optional (topology equivalent); post-task-029 cleanup is best decided by the maintainer of the shared module.
- **Task 031 (upcoming, not shipped)**: rebuilds `platform.bicep`. This task's file (`platform-controlplane.bicep`) is a NEW parallel stack — it does NOT replace `platform.bicep`, and task 031 does NOT need to invoke this task's file (design coordination: `platform.bicep` may delegate to this stack OR ops scripts invoke both). No overlap.
- **Handler H4 (later)**: PATCHes `keyVaultReferenceIdentity` to `outputs.controlPlaneUamiId` on BOTH slots. This stack exports the ID via output.
- **Handler H10 (later)**: registers UAMI as Dataverse App User using `outputs.controlPlaneUamiClientId`; grants Graph app-roles onto UAMI's SP.
