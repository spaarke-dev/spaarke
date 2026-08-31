# Task 203b execution results — Class-A punch list bicep hardening

> **Task**: 203b — Apply Class-A punch list rows A13, A14, A17, A18, A19, A20, A21, A22, A23, A25, A26, A27
> **Executed**: 2026-08-24 (background parallel sub-agent)
> **Executor**: Claude Opus 4.7
> **Rigor**: FULL (per POML — deploy tag)
> **Commits landed on** `work/customer-provisioning-orchestration-r1`:
> - `3b4f400c5` — batch 1 (A20 + A21 Model 1 + A25)
> - `9eee99de6` — batch 2 (A21 Model 2 + A27)
>
> **Do NOT amend `notes/task-202-punch-list.md` from this file** — the master
> punch list is amended by the main session at end of parallel wave. This file
> is the per-row execution record for that amendment.

## Row-by-row execution table

| row_id | verified_state (before) | action_taken | commit_sha | notes |
|---|---|---|---|---|
| A13 | **already-applied** — `modules/controlplane-sb-rbac.bicep` grants both SB Data Sender + SB Data Receiver to `principalId` param at namespace scope (task 110 landed Wave G-1). | skip | n/a | Grep: `serviceBusDataReceiverRoleId = '4f6d3b9b-...'` in `controlplane-sb-rbac.bicep:89`; module wired in `platform-controlplane.bicep:585-592`. |
| A14 | **already-applied** — `Cosmos__AccountEndpoint` + `ServiceBus__FullyQualifiedNamespace` + `ManagedIdentity__ClientId` emitted in both `controlplane-app-service.bicep` (line 136-163) and `controlplane-worker-app-service.bicep` (line 194-334). Zero live `Cosmos__Endpoint` or `ServiceBus__ConnectionString` app-setting emissions (only in header comments describing the fix). | skip | n/a | Task 110's DS-5 C5.1 follow-on fix already landed both modules. |
| A17 | **already-applied** — `modules/controlplane-artifacts-storage.bicep` exists (181 LOC) with Wave G-8 Batch 2 header, wired from `platform-controlplane.bicep:615-626` module block 9. Blob URI output threaded into worker app-settings. | skip | n/a | Was landed by Wave G-8 Batch 2 per file header. |
| A18 | **already-applied** — `modules/controlplane-acr.bicep` exists (120 LOC) with Wave G-8 Batch 2 header, wired from `platform-controlplane.bicep:640-651` module block 10. `acrImageTag` + `sidecarAuthType` params surfaced from platform stack (line 174-178). | skip | n/a | Was landed by Wave G-8 Batch 2. |
| A19 | **already-applied** — Sub Contributor via `modules/controlplane-subscription-rbac.bicep:62-70` (Wave G-8 Batch 2 audit defect #2). Storage Blob Data Reader inside `controlplane-artifacts-storage.bicep:147-156`. AcrPull inside `controlplane-acr.bicep:88-97`. All three take the L2 UAMI principalId from `uami.outputs.principalId`. | skip | n/a | Was landed by Wave G-8 Batch 2 audit defects #2/#3/#4. |
| **A20** | **open** — H4-shared source-service RBAC entirely missing per `task-200-completion-notes.md` "Deferred #1". | **applied** | `3b4f400c5` | NEW `modules/model1-shared-l2-rbac.bicep` grants 6 roles at the shared-tier resources on the L2 UAMI: Cognitive Services User × 2 (OpenAI + DocIntel), Search Service Contributor (AI Search), SB Data Owner (SB namespace), Storage Contributor (SA), Redis Cache Contributor (Redis). Wired from `stacks/model1-shared.bicep` via new `controlPlaneUamiPrincipalId` param. Split into child module because `model1-shared.bicep` is sub-scope and role assignments are RG-scoped (BCP139). |
| **A21** | **open** — Website Contributor grant on either shared BFF or per-customer BFF for the L2 UAMI missing everywhere. Grep for `de139f84-1756-...` (Website Contributor role id) or literal "Website Contributor" returned zero matches before. | **applied** | `3b4f400c5` (Model 1) + `9eee99de6` (Model 2) | For Model 1: same NEW `modules/model1-shared-l2-rbac.bicep` includes the Website Contributor grant on `sharedBffAppServiceName`. For Model 2 dedicated: NEW `modules/customer-l2-bff-rbac.bicep` grants Website Contributor on the per-customer BFF App Service; wired from `customer.bicep` via new `controlPlaneUamiPrincipalId` param. |
| **A22** | **partial (Model 2 wired; Model 1 arch-boundary chose different path)** — `customer.bicep:655-665` already invokes `../../scripts/canonical-secret-catalog/generated/kv-secrets.generated.bicep` with a `kvSecretValues` map of 10 resolvable sibling-module outputs (task 129, Wave G-2). `stacks/model1-shared.bicep` does NOT invoke it BY DESIGN per §"DELIBERATE ARCHITECTURAL BOUNDARY" (lines 638-654). | **noted (no additional Bicep change needed)** | n/a | **A22 approach chosen (per CLAUDE.md §6.5):** Model 2 uses approach (i) — wire `kv-secrets.generated.bicep` into `customer.bicep` (already landed by task 129). Model 1 uses a DIFFERENT approach: H4-shared handler (task 200) populates per-tenant KVs at RUNTIME from source-service reads (which now works after A20 grants land) — this is architecturally cleaner than mixing Bicep-write + runtime-write into the same KV. **Rejected alternatives (per §6.5):** (ii) extending `InterStepState` with H2a outputs would require changing the `IKvSecretValueResolver` contract and adds cross-handler state coupling; rejected. (iii) pre-seeding via a generated seeder is inferior to (i)'s Bicep-managed declarative pattern. Regression check: `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Core/` succeeds; H4's `KvSecretValueResolver` will succeed on Model 2 fresh customers (kvSecrets writes values before H4 reads); H4-shared handles Model 1 shared secrets independently at runtime. |
| A23 | **already-applied** — `scripts/canonical-secret-catalog/generated/kv-secrets.generated.bicep` uses `if (contains(secretValues, '<name>'))` skip-if-absent guard on every KV secret resource (Wave G-8 Batch 1 defect #15). BINDING never-delete secrets Dataverse-ClientSecret + BFF-API-ClientSecret are NOT declared as writable ARM resources AT ALL — file header lines 25-30 + line 187-190 make this explicit. Verified: re-deploying this module cannot touch either BINDING secret's live value. | skip | n/a | Was landed by Wave G-8 Batch 1 defect #15. |
| A24 | **already-applied** — `healthCheckPath` set to `/healthz` in EVERY bicep module that declares it: `app-service.bicep:82`, `app-service-slot.bicep:54`, `app-service-config.bicep:9`, `controlplane-app-service.bicep:118,202`, `controlplane-worker-app-service.bicep:178`, `deployment-slot.bicep:57,91,123`. Punch list claim of asymmetric `/health` vs `/healthz` is stale (fix already landed). | skip | n/a | Not scoped in 203b's POML (POML step 3 says "recheck if not applied in 203a") — verified as landed. |
| **A25** | **open** — `stacks/model1-shared.bicep:247-256` did not pass `userAssignedIdentityPrincipalId` to `sharedKeyVault`; comment on line 494-497 explicitly said "KV Secrets User grant to the shared BFF UAMI is a follow-on concern." | **applied** | `3b4f400c5` | Added `userAssignedIdentityPrincipalId: sharedBffUami.outputs.principalId` to `sharedKeyVault` module invocation in `model1-shared.bicep`. `key-vault.bicep:182-190` already emits the KV Secrets User role assignment when this param is non-empty (`uamiSecretsRole` resource). Updated the outdated comment in the `sharedBffApi` module block. Bicep resolves the topological dependency automatically (`sharedBffUami` module has no dependency on `sharedKeyVault`). |
| **A26** | **open — deferred (not executable in sub-agent scope)** — `sprk-provisioning-jobs` queue on `spaarke-servicebus-dev` still has `requiresSession=false` + `requiresDuplicateDetection=false` per `queue-recreate-runbook-2026-08.md` §3 live snapshot. Bicep declaration for the desired end-state landed via `modules/controlplane-sb-queue.bicep` (task 108) but the LIVE recreate ceremony has not been executed. | **deferred** | n/a | Requires **live Azure destructive operation** (`az servicebus queue delete` against dev L2 stamp per runbook §4 step 2) — beyond the safe scope of a background non-interactive sub-agent. Runbook explicitly says (§7): "task 108 does NOT execute §4 of this runbook against live Azure — that is a separate, human-run (or explicitly separately-dispatched) ceremony". Must be executed by a human operator per runbook §7, sequenced AFTER task 107 (`attempt` field in `ReconcilerEnqueuePayload`) has shipped to L2 per runbook §5. Bicep declaration + runbook are already complete + committed; only the live execution remains. |
| **A27** | **open** — `CustomerRunGuard__*` app settings entirely missing from `controlplane-worker-app-service.bicep`; grep for `CustomerRunGuard` in bicep returned zero matches. But `Sprk.Provisioning.ControlPlane.Worker/Program.cs:909` invokes `AddCustomerRunGuard(Configuration)` which fails-fast at boot on missing config when `Enabled=true`. | **applied** | `9eee99de6` | Added five app-settings to `controlplane-worker-app-service.bicep` app-settings block: `CustomerRunGuard__TargetDataverseUrl` (= `adminDataverseEnvironmentUrl`), `CustomerRunGuard__TenantId` (new param `customerRunGuardTenantId`), `CustomerRunGuard__ClientId` (new param `customerRunGuardClientId`), `CustomerRunGuard__ClientSecret` (KV ref to `bffApiClientSecretName` — the SAME BFF-API-ClientSecret H6/H7 use), `CustomerRunGuard__Enabled` (new param `customerRunGuardEnabled`, default **false** per ADR-032 null-object kill-switch). Threaded through `platform-controlplane.bicep`: `customerRunGuardTenantId` defaults to `effectiveJwtTenantId`; `customerRunGuardClientId` + `customerRunGuardEnabled` are top-level params. **Enabled=false** keeps a fresh L2 deploy boot-safe; flip to `true` after supplying the BFF client-id + confirming the KV secret. Per r1-gap-analysis c5-6 this closes the I5 same-customer serialization guard config gap. |

## Summary

| Metric | Count |
|---|---|
| Rows in scope | 12 |
| Rows already-applied (verified skip) | 7 (A13, A14, A17, A18, A19, A23, A24) |
| Rows applied this task | 4 (A20, A21, A25, A27) |
| Rows deferred | 1 (A26 — live-Azure destructive op; requires human operator per runbook §7) |
| Rows partial / architectural note | 1 (A22 — Model 2 already applied via customer.bicep task 129; Model 1 chose H4-shared runtime pattern per architectural boundary, alternatives documented per §6.5) |

## Files created

- `infrastructure/bicep/modules/model1-shared-l2-rbac.bicep` (7 role assignments — Model 1 shared tier L2 UAMI grants)
- `infrastructure/bicep/modules/customer-l2-bff-rbac.bicep` (1 role assignment — per-customer BFF Website Contributor)
- `projects/customer-provisioning-orchestration-r1/notes/task-203b-execution-results.md` (this file)

## Files modified

- `infrastructure/bicep/customer.bicep` — new `controlPlaneUamiPrincipalId` param + `customerL2BffRbac` module invocation (A21 Model 2)
- `infrastructure/bicep/stacks/model1-shared.bicep` — new `controlPlaneUamiPrincipalId` param + `sharedKeyVault` gets `userAssignedIdentityPrincipalId` (A25) + `model1SharedL2Rbac` module invocation (A20 + A21 Model 1) + comment fix in `sharedBffApi` block
- `infrastructure/bicep/platform-controlplane.bicep` — 2 new params (`customerRunGuardClientId`, `customerRunGuardEnabled`) + 3 new args to `workerAppService` module (A27)
- `infrastructure/bicep/modules/controlplane-worker-app-service.bicep` — 3 new params (`customerRunGuardTenantId`, `customerRunGuardClientId`, `customerRunGuardEnabled`) + 5 new CustomerRunGuard app-settings (A27)

## Build verification

- `az bicep build infrastructure/bicep/stacks/model1-shared.bicep` — **PASS**
- `az bicep build infrastructure/bicep/modules/model1-shared-l2-rbac.bicep` — **PASS**
- `az bicep build infrastructure/bicep/customer.bicep` — **PASS** (pre-existing warnings only; none introduced)
- `az bicep build infrastructure/bicep/modules/customer-l2-bff-rbac.bicep` — **PASS**
- `az bicep build infrastructure/bicep/platform-controlplane.bicep` — **PASS**
- `az bicep build infrastructure/bicep/modules/controlplane-worker-app-service.bicep` — **PASS**
- `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Core/` — **PASS** (0 warnings, 0 errors, 9.01s)

## NFR-01 publish-size verification

**Non-applicable.** Task 203b touches only `infrastructure/bicep/**` (Bicep templates) and `projects/**/notes/` (documentation) — zero touch to `src/server/api/Sprk.Bff.Api/**`. Per POML §constraints and CLAUDE.md §10, no publish-size delta report is needed for infrastructure-only tasks.

## Downstream unblocks (for main-session amendment of the master punch list)

- Task 186 E2E can now succeed against Model 1 stamp: H4-shared can read source-service keys (A20) + H4b Kudu logs + H9 zip-deploy work (A21) + shared BFF resolves KV refs at boot (A25) + I5 concurrency guard is config-provisioned (A27, but Enabled=false until operator flips it).
- Task 186 E2E can now succeed against Model 2 stamp: per-customer BFF supports L2 handler operations (A21 Model 2 path).
- **Still blocking task 186**: A26 live queue-recreate ceremony (must be human-run; sequenced after task 107 attempts field is live in L2). This is a live-Azure operator task, not a Bicep gap.
