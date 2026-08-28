# r1 Gap Analysis — Current State vs Required State (2026-08-18)

> **Produced by**: Fable-model gap-analysis session per owner directive (2026-08-18 session close).
> **Deliverable framing (owner, 2026-08-18, verbatim)**: *"the deliverable of this project is E2E customer provisioning — full stop."* This report is an ACCOUNTING of every piece that must be built/wired/tested for that deliverable to be met. It proposes no fixes and advocates no architectural direction.
> **Acceptance target** (spec FR-18 + SC #5 + design.md §15): a fresh customer environment is provisioned end-to-end via the new pipeline, reaching `sprk_dataverseenvironment.sprk_setupstatus = Ready`.
> **Method**: every claim below is grep/read-verified against the working tree (`work/customer-provisioning-orchestration-r1`), not inferred from task status. File paths cited throughout.

---

## Reading guide

- §A — Required state: every runtime component the stated goal needs, with spec/design cites.
- §B — Actual state: what exists in code today, grep-verified, including a per-handler placeholder audit.
- §C — Gap catalog, categorized 1–6.
- §D — Delta accounting: what closing each gap entails, plus summary tallies.

**One required-state ambiguity must be stated up front** (factually — no direction advocated): the required-state authority contradicts itself on WHERE the queue consumer lives.
- spec.md **FR-22** (line 160): "handlers run in **BFF's existing `IJobHandler` infrastructure** (ADR-004)".
- design.md **§4.2 step 2** (line 282): "Handler execution happens in the BFF's existing `IJobHandler` infrastructure (ADR-004) — **a dedicated worker consumes the Service Bus queue**".
- spec.md MUST rule + D3/D8/D12 (mirrored in project CLAUDE.md): "**MUST** register provisioning handlers in **L2 control-plane service, not the BFF**".
- Implementation reality: all 19 handlers were built into L2 (`src/server/services/Sprk.Provisioning.ControlPlane/Handlers/**`); the BFF has no reference to them and its `ServiceBusJobProcessor` drains a different queue (its own `options.QueueName`, not `sprk-provisioning-jobs`).
- Owner clarification (2026-08-18): BFF is the operational customer-facing API, not the provisioning executor; handlers are in L2; BFF has no role in the provisioning execution path.
- Net effect: the "dedicated worker [that] consumes the Service Bus queue" was specified in a location (BFF) that the MUST rules and the implementation both contradict, and **no task in the 78-task WBS ever owned building it anywhere**. This is the root of the load-bearing gap in §C-1.1. FR-22/design §4.2 step 2 text does not match the delivered architecture and will need reconciliation as part of closing the gap (documentation-side accounting appears in §D).

---

## (A) Required State — runtime execution model for E2E provisioning

Every component that MUST exist and be wired for `POST /api/runs` → … → `sprk_setupstatus = Ready` to complete. Cites are spec.md (FR-xx, §4B/§4C/§4D, NFR-xx) and design.md (§4.1 DAG, §4.2, D1–D20, §14A).

### A.1 Orchestration spine

| # | Component | Purpose | Spec/design cite | Must be wired in |
|---|---|---|---|---|
| A1 | L2 REST API (8 endpoints) | Intake + status + gates + resume + cancel + clear-quarantine + logs | spec FR-20, FR-21; design §4.2 API table | L2 `Program.cs` endpoint mapping |
| A2 | BFF `POST /api/onboarding/consent-callback` (9th endpoint) | H0.5 Model-2 consent hop (Anonymous + HMAC) | spec FR-02, FR-21; design D18, §4.3a.2 | BFF `Program.cs` / OnboardingModule |
| A3 | Cosmos state store (`spaarke-provisioning/runs`, partition `/customerId`) | ProvisioningRun documents; ETag concurrency | spec FR-27, FR-30 (I3) | L2 CosmosModule + Azure account |
| A4 | Service Bus fleet queue `sprk-provisioning-jobs` | Work queue between enqueue side and execution side | spec FR-22, §4.2; design §4.2 steps 1–2 | Azure SB namespace + IaC |
| A5 | Handler enqueuer (deterministic MessageId per (HandlerId, RunId, CustomerId, paramHash)) | FR-22 Level-1 idempotency at the wire | spec FR-22, NFR-10 | L2 ServiceBusModule |
| A6 | **Queue consumer / dispatcher** — the "dedicated worker [that] consumes the Service Bus queue, runs the handler (which may take 30+ min), updates Cosmos on completion/failure" | THE execution engine. Without it no handler ever runs | spec FR-22; design §4.2 step 2 (verbatim above); `Handlers/IProvisioningHandler.cs` header steps 1–3 (pull envelope → resolve handler by HandlerId → invoke + interpret HandlerResult against §4C) | Never assigned a home (see reading-guide ambiguity). Owner-clarified: NOT BFF |
| A7 | Handler resolution by `HandlerId` | Dispatcher must map envelope `HandlerId` ("H0"…"H14") to the correct handler instance | `IProvisioningHandler.cs` header step 2 ("keyed-services registration … is one option") | L2 DI (all 19 handlers resolvable by id) |
| A8 | State-reconciler `BackgroundService` (5s Cosmos poll → DAG advance → enqueue ready handlers) | Advances the pipeline between handler completions | spec FR-22, §4.2 step 3; design §4.2 step 3 | L2 `AddHostedService` |
| A9 | DAG definition (H0→H1→H2a→{H2b,H4→H3→{H8,H9},H5→H6→H7→H10→H11→{H12a,H12b}}→H12c→H14→H13) | Ready-set computation | design §4.1 | L2 (consumed by A8) |
| A10 | Handler-outcome application (HandlerResult → §4C classification → Cosmos state transition → re-enqueue/quarantine) | Failure taxonomy takes effect at runtime | spec FR-24 (§4C); design §4C | Invoked by A6 on every handler completion |
| A11 | `CustomerRunGuard` (I5 optimistic upsert on `sprk_currentrunid`; 409 on conflict; Quarantined block) | Same-customer serialization | spec FR-23, FR-24; design §4.2 I5 | L2 POST /api/runs + cancel + completion release |
| A12 | `CrashRecoveryStartupService` (I6 boot scan for orphaned Running/WaitingOnGate; re-enqueue `currentPhase`) | Crash recovery | spec FR-23; design §4.2 I6 | L2 `AddHostedService` |
| A13 | Rollback surface (`FailureClassifier`, `RollbackTransitions`, `QuarantineClearService` + clear-quarantine endpoint) | 4-class §4C taxonomy round-trips through Cosmos | spec FR-24 | L2 DI + endpoint + A10 |
| A14 | 3-level idempotency: (1) SB MessageId dedup, (2) Redis idempotency lock, (3) durable dedup (CompletedPhases / Dataverse alt-key) | Safe under duplicate dispatch, crash-resume, concurrent reconcilers | spec FR-22, NFR-10; ADR-036 | (1) queue must have duplicate detection ON; (2) a Redis-backed lock on the dequeue side; (3) each handler body |
| A15 | Registry `sprk_setupstatus → Ready` writer | THE acceptance-target transition; H13 is sole authority | spec FR-18; design §7 | L2 (H13's `IRegistrySetupStatusUpdater` — real Dataverse PATCH) |
| A16 | Registry read client (lookup `sprk_dataverseenvironment` by tenantId/environmentId) | H0.5 re-consent semantics; H13 idempotency short-circuit | spec FR-02, FR-26 | L2 (`IDataverseEnvironmentRegistryClient` — real impl) |
| A17 | Handler execution environment: `pwsh`, `az` CLI, `pac` CLI, repo `scripts/**`, `infrastructure/bicep/**`, solution ZIPs — available WHERE handlers execute, with a working auth chain | ~11 of 19 handlers shell out to PS scripts / az / pac (see B.3) | design §4.1 per-handler rows (H2a Provision-Customer.ps1, H5 pac admin, H6 Deploy-DataverseSolutions.ps1, etc.); FR-04/08/09/12 acceptance criteria name the scripts | The dispatcher host's runtime image + publish payload |
| A18 | L2 gate management (`gateStates`; H0.5 admin-consent, H3 consent, H8 SPE 24h, H11 B2B consent; `POST …/gates/{gateId}/advance`) | Manual/external gates pause + resume the DAG | spec FR-21, FR-02; design §4.1 gate rows | L2 endpoints + handlers + reconciler |
| A19 | Upgrade-mode behavior per handler (registry `sprk_provisionedon` non-null → §14A semantics) | Second run against same customer must not destroy state | spec FR-34; design §14A | Handler bodies (largely delivered; H2a what-if, H4 rotation-safe) |

### A.2 Handler catalog H0–H14 (the 19 executables)

| Handler | Purpose (one line) | Spec cite |
|---|---|---|
| H0 | Preflight: OpenAI TPM + Dataverse env-rate + subscription vCPU + SPE cert-bootstrap quota probes; blocks before H1 | FR-01, NFR-12 |
| H0.5 | Model-2 consent capture: HMAC callback → `tid` → seed params → kick pipeline; re-consent semantics via registry lookup | FR-02 |
| H1 | Subscription readiness: ARM reachability + (CustomerOwned) Lighthouse | FR-03 |
| H2a | Per-customer Bicep infra deploy (15 resources; T1 keyVaultReferenceIdentity PATCH owner; what-if drift in upgrade mode) | FR-04, FR-33 T1 |
| H2b | 7 canonical AI Search indexes (Model 2 create / Model 1 verify + per-tenant filter template = I2 provisioning half) | FR-05, FR-29 |
| H3 | Entra app-reg, 14 grants, admin-consent gate verified via Graph | FR-06 |
| H4 | KV secrets population from canonical manifest; T1 PATCH both slots; T5 interim grants; BINDING never-delete list | FR-07, FR-33 T1/T5, FR-35/36 |
| H5 | Dataverse environment creation (interim `pac admin`) + reachability gate | FR-08 |
| H6 | 8 managed solutions via Package Deployer, dependency-ordered | FR-09 |
| H7 | 7 per-customer Dataverse env-var values | FR-10 |
| H8 | SPE container-type + root container via confidential-client cert (T6); 24h replication gate | FR-11, FR-33 T6 |
| H9 | BFF deploy blue-green slot swap + r3 gates + NFR-01 size gate | FR-12 |
| H10 | 2 Dataverse App Users + UAMI Graph app-role parity (T2 + T3 owners) | FR-13, FR-33 T2/T3 |
| H11 | User provisioning per identity preset (NativeAccount / B2BGuest + consent gate) | FR-14 |
| H12a | AI seed chain (type-lookups → … → playbook consumers) | FR-15 |
| H12b | App-config seed (DataGrid, field-mapping, workspace layouts, chart defs) — DAG-parallel with H12a | FR-16 |
| H12c | `sprk_aimodeldeployment` runtime references (3-way join H12a+H12b+H2a) | FR-17 |
| H13 | E2E acceptance gate: extended validate + ALL 6 traps T1–T6 + ALL 5 invariants I1–I5 + naming-conformance + cost envelope → **sole authority for `Ready`** | FR-18, FR-33, FR-28..32 |
| H14 | Post-deploy integration wiring: H14a Exchange ApplicationAccessPolicy (T4), H14b Graph webhooks, H14c Dataverse service-endpoint webhooks | FR-19, FR-33 T4 |

### A.3 Infrastructure / config / RBAC / operator surfaces

| # | Component | Cite |
|---|---|---|
| A20 | `platform-controlplane.bicep` L2 stamp (App Service + slot, Plan, Cosmos, KV, App Insights, Log Analytics, UAMI) with app settings whose keys MATCH the L2 code's configuration reads | spec FR-20; design §4.2 hosting |
| A21 | L2 AAD app-reg (audience per tenant policy, Operator/Reader roles, operator assignment, az-CLI SP consent) | spec FR-20, NFR-11; design §4.3a.2 |
| A22 | L2 UAMI RBAC: Cosmos Data Contributor, KV Secrets User, SB **Data Sender** (enqueue) AND SB **Data Receiver** (the consumer side) on the fleet namespace | ADR-028; spec FR-22 |
| A23 | KV secrets the L2 code binds (`ServiceBus-ConnectionString` or MI equivalent, `Dataverse-ClientSecret` or its post-r3 replacement for registry/guard auth) | spec FR-07; r3 handoff (S2S dropped) |
| A24 | Fail-fast config validation on every Tier-1 option (Cosmos, SB, audience, guard, handler options incl. per-handler ClientSecret KV refs) | NFR-05 |
| A25 | `/provision-environment` L3 skill matching the ACTUAL L2 contract (URL, audience, `environmentId`, profile enum `spaarke-hosted-model1-trial` / `spaarke-hosted-model2` / `customer-owned-model2`, prerequisite `sprk_dataverseenvironment` record creation) + fallback matrix + handoff report + registry update | spec FR-25; design §4.3a |
| A26 | Operator runbook + verification harness aligned to same contract | plan Phase F; task 089 scaffolding |
| A27 | Tests: unit + integration seam for dispatcher, reconciler, guard, crash recovery, rollback; load test (FR-22 acceptance: ≥30-min handler completes; no duplicate enqueue under N reconcilers); ArchTests I1–I5 | spec FR-22/23/24 acceptance, NFR-08; ADR-038 |
| A28 | L2 deploy path (repeatable publish + deploy of the L2 App Service itself) | implied by FR-20 + §14A (L2 is fleet infrastructure); currently absent from WBS |

---

## (B) Actual State — grep-verified

### B.1 Orchestration spine vs A.1

| # | Component | Exists? | Path | Impl vs placeholder | Wired? | Grep evidence |
|---|---|---|---|---|---|---|
| A1 | L2 REST API | ✅ YES | `Api/RunsEndpoints.cs`, `Api/RunLogsEndpoints.cs`, `Endpoints/HealthEndpoints.cs` | Real (8 endpoints; live-verified 202 + auth chain on 2026-08-18) | ✅ `Program.cs:1130-1169` | `MapRunsEndpoints` / `MapRunLogsEndpoints` present |
| A2 | BFF consent-callback | ✅ YES | `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/` (7 files: `ConsentCallbackEndpoint.cs`, `HmacSignatureVerifier.cs`, `ServiceBusProvisioningEnqueuer.cs`, …) | Real; 7/7 E2E tests (task 078) | ✅ BFF Program.cs | Enqueues H0.5 envelope to `sprk-provisioning-jobs` (`ServiceBusProvisioningEnqueuer.cs:60`) — same unconsumed queue |
| A3 | Cosmos state store | ✅ YES | `Modules/CosmosModule.cs`, `Repositories/CosmosProvisioningRunRepository.cs`, `Models/ProvisioningRun.cs` | Real; live doc written 2026-08-18 (post bugs #19/#20) | ✅ `Program.cs:90` | — |
| A4 | SB queue | ⚠️ EXISTS LIVE ONLY | — | Created manually via `az servicebus queue create` (bug #22); **in no Bicep**; duplicate detection NOT enabled | ❌ not in IaC | `grep sprk-provisioning-jobs infrastructure/` → 0 hits; `service-bus.bicep:39` `requiresDuplicateDetection: false` (and that module doesn't create this queue) |
| A5 | Enqueuer | ✅ YES | `Enqueue/ServiceBusHandlerEnqueuer.cs` | Real (deterministic MessageId; IDisposable fix #17 in worktree) | ✅ `Program.cs:96` | — |
| A6 | **Queue consumer / dispatcher** | ❌ **NO — never built** | — | — | — | `grep -rn "ServiceBusProcessor\|ProcessMessageAsync\|CreateProcessor\|ServiceBusReceiver\|ReceiveMessage" src/server/services/Sprk.Provisioning.ControlPlane/ --include=*.cs` → **0 matches**. `IProvisioningHandler.cs:16-30` documents it as "planned wave C5". No POML task ever created it |
| A7 | HandlerId resolution | ❌ NO | — | Only H0 is registered AS `IProvisioningHandler` (`HandlersModule.cs:103`). The other 18 handlers are concrete-type registrations only (`AddScoped<H1SubscriptionReadinessHandler>()` etc., `Program.cs`) — not resolvable by id or interface | — | `grep "AddScoped<IProvisioningHandler" ` → 1 hit (H0); no keyed-services registration anywhere |
| A8 | State reconciler | ✅ YES — **and IS wired** (corrects session-close "bug #23") | `Reconciler/StateReconcilerService.cs` | Real; `Enabled` defaults **true** (`ReconcilerOptions.cs:40`) | ✅ `Program.cs:986` → `ReconcilerModule.cs:82` `AddHostedService<StateReconcilerService>()` | See B.4 for why it still cannot advance anything |
| A9 | DAG definition | ✅ YES | `Reconciler/DagAdvancer.cs` | Real; encodes full §4.1 DAG; H0/H0.5 excluded as entry points | ✅ via ReconcilerModule | Header lines 6–47 |
| A10 | Outcome application | ⚠️ BUILT, NO PRODUCTION CALLER | `StateReconcilerService.ApplyHandlerOutcomeAsync` (lines 365–476) | Real §4C mapping + quarantine + re-enqueue | ❌ "Exposed as internal … the wiring hook lives here" — **zero callers outside tests** | `grep ApplyHandlerOutcomeAsync src/` → definition + doc-ref only |
| A11 | CustomerRunGuard | ✅ YES, wired into endpoints | `Concurrency/CustomerRunGuard.cs` + `DataverseRegistryConcurrencyStore.cs` | Real, but `Enabled` defaults **false** (`CustomerRunGuardOptions.cs:92`) → Success-always; requires TargetDataverseUrl/TenantId/ClientId/**ClientSecret** config that is not provisioned (and the historical `Dataverse-ClientSecret` was dropped by r3) | ✅ `Program.cs:1023`; consumed in `RunsEndpoints.cs:341` | I5 currently NOT enforced at runtime |
| A12 | CrashRecoveryStartupService | ✅ YES | `Reconciler/CrashRecoveryStartupService.cs` | Real | ✅ `Program.cs:1065` | Only hosted service besides reconciler |
| A13 | Rollback surface | ✅ YES | `Rollback/*` (FailureClassifier, RollbackTransitions, QuarantineClearService) | Real; exhaustive-switch guarded | ✅ `Program.cs:1098`; clear-quarantine endpoint live | Effective only when A6/A10 exist to feed it |
| A14 | 3-level idempotency | ⚠️ PARTIAL | — | L1: enqueuer computes MessageId, **but the live queue has duplicate detection off → L1 inert**. L2 (Redis): **absent by design in L2** — "the level-2 gate is currently a no-op" (`IProvisioningHandler.cs:38-40`); no Redis package/client in L2 csproj. L3: implemented per handler (CompletedPhases scans present) | — | `grep -i redis src/server/services/Sprk.Provisioning.ControlPlane/` → comments only |
| A15 | Registry Ready writer | ❌ PLACEHOLDER | `Handlers/E2EAcceptance/DataverseRegistrySetupStatusUpdater.cs` | "Returns Success **WITHOUT issuing any Dataverse call**" (file header lines 10-18; LogWarning at line 49) | ✅ registered (E2EAcceptanceModule) | **The acceptance-target transition `sprk_setupstatus = Ready` is a no-op in code** |
| A16 | Registry read client | ❌ PLACEHOLDER | `Registry/NullDataverseEnvironmentRegistryClient.cs` | "Every lookup returns null" (line 9/64) | ✅ registered `Program.cs:125` | H0.5 re-consent + H13 short-circuit degrade to "always fresh run" |
| A17 | Handler execution environment | ❌ NOT PROVISIONED / NEVER TASKED | — | csproj publishes code only (no `scripts/**`, no `infrastructure/bicep/**` content items; `Sprk.Provisioning.ControlPlane.csproj` has zero script includes); default script path is `AppContext.BaseDirectory/scripts/...` (`PowerShellPreflightProbe.cs:291-292`); L2 publish was 6.57 MB; Linux App Service default image has no pwsh/az/pac; no `az login` identity in-process | — | `grep -c "pwsh\|az \|pac \|ProcessStartInfo" Handlers/` → 420 occurrences across 96 files |
| A18 | Gate management | ✅ YES (code level) | `Models/GateState.cs`, gates endpoint, H3/H8/H11 WaitingOnGate paths | Real | ✅ | Untestable E2E until handlers execute |
| A19 | Upgrade mode | ✅ largely | H2a what-if / H4 rotation-safe / H12* additive per file headers | Real code paths | ✅ | Untested live |

### B.2 Handler-by-handler audit (all 19) — real vs placeholder, and runtime executability

Legend — **Core**: the handler's own orchestration/Cosmos/idempotency logic. **Collab**: its injected collaborator seams. **Runtime**: can it actually execute inside the deployed L2 App Service as-is (independent of the missing dispatcher)?

| Handler | File | Core | Collaborator seams (real / placeholder) | Runtime executability in deployed L2 |
|---|---|---|---|---|
| H0 | `Handlers/Preflight/H0PreflightHandler.cs` | Real (incl. temp bridge: on success enqueues H0.5 directly, line 391-405) | 4× `PowerShellPreflightProbe` — real code, shells `pwsh` + `scripts/preflight/*.ps1` | ❌ scripts not in publish; pwsh absent |
| H0.5 | `Handlers/ConsentCapture/H05ConsentCaptureHandler.cs` | Real | `IDataverseEnvironmentRegistryClient` = **Null placeholder** (always null) | ⚠️ runs in-process, but re-consent semantics (FR-02) inert |
| H1 | `Handlers/SubscriptionReadiness/H1SubscriptionReadinessHandler.cs` | Real | `ISubscriptionReadinessProbe` = **NullSubscriptionReadinessProbe placeholder** (no ARM call) | ⚠️ runs, but FR-03 verification is fictional |
| H2a | `Handlers/BicepInfraDeploy/H2aBicepInfraDeployHandler.cs` | Real (T1 probe, §4C mapping) | `ProvisionCustomerScriptBicepDeployRunner` (pwsh `Provision-Customer.ps1`), `AzCliArmKeyVaultRefProbe`, `AzCliUpgradeDriftDetector` (az CLI), `FileBicepTemplateInspector` (reads `infrastructure/bicep/` on disk) — all real code | ❌ pwsh/az/bicep-dir absent from runtime |
| H2b | `Handlers/AiSearchIndex/H2bAiSearchIndexHandler.cs` | Real | Provisioner = pwsh `Deploy-AllIndexes.ps1`; Verifier = real REST (HttpClient); tenant-filter template = **StubAiSearchTenantFilterTemplateProvisioner** (logs + Success; I2 provisioning-half not real) | ❌ provisioner needs pwsh+script; verifier OK |
| H3 | `Handlers/EntraAppReg/H3EntraAppRegHandler.cs` | Real | Provisioner = pwsh `Register-EntraAppRegistrations.ps1`; `IAdminConsentVerifier` = **NullAdminConsentVerifier (always Verified)** — consent gate not actually verified | ❌ pwsh/script; consent check fictional |
| H4 | `Handlers/KvSecretsPopulation/H4KvSecretsPopulationHandler.cs` | Real (BINDING never-delete guard, T1/T5) | Manifest = **StaticKvSecretManifest (interim)** — task 084's canonical manifest generator landed but the DI swap to it never happened (`Program.cs:328` still binds Static); writers/patchers = az CLI shell-outs | ❌ az CLI absent |
| H5 | `Handlers/DataverseEnvCreation/H5DataverseEnvCreationHandler.cs` | Real | `PacAdminDataverseEnvCreator` = pac CLI shell-out; health probe = real HttpClient | ❌ pac CLI absent |
| H6 | `Handlers/SolutionImport/H6SolutionImportHandler.cs` | Real | Importer = pwsh `Deploy-DataverseSolutions.ps1`; verifier = pac CLI; `SolutionImportOptions:ClientSecret` KV wiring deferred ("Wave C5 wires the option-binding to a Key Vault reference" — `Program.cs:440-443`) | ❌ pwsh/pac/solution-ZIPs absent; secret unconfigured |
| H7 | `Handlers/EnvVarValues/H7DataverseEnvVarValuesHandler.cs` | Real | Writer = real Dataverse Web API via HttpClient; `EnvVarValuesOptions:ClientSecret` KV wiring deferred (same wave-C5 note) | ⚠️ in-process capable IF credential config provisioned |
| H8 | `Handlers/SpeContainerType/H8SpeContainerTypeHandler.cs` | Real (T6 both stages) | Provisioner/verifier = pwsh scripts (`Create-NewContainerType.ps1`, `Get-SpeContainerMetadata-AppOnly.ps1`); KV writer = az CLI | ❌ pwsh/az/scripts absent |
| H9 | `Handlers/BffDeploy/H9BffDeployHandler.cs` | Real (NFR-01 gate, rollback re-swap) | `DotnetR3GateVerifier` (dotnet+pwsh; missing gate artifacts report **Skipped** — interim), `DeployBffApiScriptRunner` (pwsh `Deploy-BffApi.ps1` — needs full repo + dotnet SDK to BUILD the BFF), slot swapper = az CLI | ❌ needs dotnet SDK + repo + az; heaviest environment dependency of all |
| H10 | `Handlers/DataverseAppUserGraphParity/H10…Handler.cs` | Real (T2+T3 independent re-queries) | All 5 seams real REST via HttpClient + DefaultAzureCredential | ⚠️ in-process capable IF UAMI has Graph/Dataverse permissions (never granted to L2 UAMI) |
| H11 | `Handlers/UserProvisioning/H11UserProvisioningHandler.cs` | Real (B2B gate) | 3 seams real Graph REST | ⚠️ same caveat as H10 |
| H12a | `Handlers/AiSeedChain/H12aAiSeedChainHandler.cs` | Real | Runner = pwsh `Invoke-SeedManifest.ps1 -Live`; reader = on-disk manifest | ❌ pwsh/scripts/manifest absent |
| H12b | `Handlers/AppConfigSeed/H12bAppConfigSeedHandler.cs` | Real | DataGrid + workspace-layout = pwsh script seeders; field-mapping + chart-def = **DeferredAppConfigSeeder (intentional no-op interim; mirrors never authored in "Wave C5")** | ❌ pwsh/scripts; 2 of 4 scopes no-op regardless |
| H12c | `Handlers/RuntimeReferences/H12cRuntimeReferencesHandler.cs` | Real | Writer = real Dataverse Web API (HttpClient); pinned 3-model catalog | ⚠️ in-process capable IF credential provisioned |
| H13 | `Handlers/E2EAcceptance/H13E2EAcceptanceGateHandler.cs` | Real aggregation + sole `Ready` authority (`run.Status = Completed` line 687) | Trap verifier = **PlaceholderTrapVerifier → InfraFault for ALL T1–T6** (lines 60-70); invariant verifier = **PlaceholderInvariantVerifier → InfraFault for ALL I1–I5**; registry updater = **placeholder Success without Dataverse write** (A15); validate/naming/cost = pwsh + az shell-outs | ❌ triple-blocked: placeholders guarantee Resumable (never green) + Ready write is a no-op + shell-outs impossible. **H13 can never produce `Ready` as coded** |
| H14 | `Handlers/IntegrationWiring/H14IntegrationWiringHandler.cs` (+ H14a/b/c) | Real (parent-owns-Cosmos) | Exchange policy = pwsh `Set-ExchangeApplicationAccessPolicy.ps1`; Graph subscriptions + Dataverse service-endpoints = real REST; KV reader = az CLI | ❌ pwsh/az for H14a; H14b/c in-process capable |

**Placeholder tally inside "✅ completed" handler tasks**: 9 distinct placeholder/interim collaborators across 8 handlers (H0.5, H1, H2b, H3, H4, H12b, H13×3), plus 2 deferred credential wirings (H6, H7) and 1 interim gate-verifier posture (H9).

### B.3 The execution-environment fact base (A17)

- 420 `pwsh`/`az `/`pac `/`ProcessStartInfo` occurrences across 96 handler files (grep count, `Handlers/**`).
- `Sprk.Provisioning.ControlPlane.csproj`: no `<Content>`/`<None>` items copying `scripts/**`, `infrastructure/bicep/**`, solution ZIPs, or seed manifests to publish. The only content rule EXCLUDES `appsettings.template.json`.
- Default script roots resolve to `AppContext.BaseDirectory` (e.g. `PreflightModuleOptions.ScriptsDirectory` = `{base}/scripts/preflight`), i.e. inside the publish folder that doesn't contain them.
- The L2 App Service is Linux framework-dependent publish (csproj lines 24-27); pwsh, az CLI, pac CLI are not present in the default runtime image, and there is no `az login` session or interactive auth chain in an App Service process (multiple collaborators are documented as using "the operator az CLI auth chain").
- No task in TASK-INDEX owns "make handler collaborators executable in the L2 runtime" — the collaborators were built and unit-tested with fakes; the runtime feasibility question was never assigned.

### B.4 Why the live run sits at `NotStarted` forever (mechanics, fully traced)

1. `POST /api/runs` (`RunsEndpoints.cs:389`) writes the run with `Status = NotStarted` and enqueues the H0 envelope. ✅ happened live (1 message on queue).
2. Nothing consumes the queue (A6 missing) → H0 never executes → `run.Status = Running` (set inside `H0PreflightHandler.cs:353`) never happens.
3. The reconciler IS running, but `CosmosActiveRunScanner.cs:46` scans `WHERE c.status IN ('Running','WaitingOnGate')` — a `NotStarted` run is invisible to it **by design** (entry-point handlers are transport-dispatched, `DagAdvancer.cs:43-47`).
4. Even for a future `Running` run, the scanner has a **latent serialization defect** (see C-4.5): the Cosmos SDK's default (Newtonsoft-based) serializer ignores the STJ `JsonStringEnumConverter` on `RunStatus` and writes `status` as an **integer**, while the scan compares **strings** — the same defect family as fixed bugs #19/#20, one query further downstream.
5. Even if handlers executed and the DAG advanced to H13, H13's trap/invariant verifiers return InfraFault (Resumable forever) and its registry updater performs no Dataverse write — `sprk_setupstatus = Ready` is structurally unreachable in the current code.

### B.5 Skill / operator surfaces vs A25–A26

| Item | State | Evidence |
|---|---|---|
| SKILL.md URL + audience | Fixed (uncommitted worktree edit) | phase-f log |
| SKILL.md `profile` values | ❌ still `dev`/`trial` style (`SKILL.md:124,199,210,235,283`) — L2 requires `spaarke-hosted-model1-trial` / `spaarke-hosted-model2` / `customer-owned-model2` (`ProvisioningRun.cs:120-124`) | grep |
| SKILL.md `environmentId` intake | ❌ absent — L2 `POST /api/runs` 400s without it (live-verified) | grep `environmentId` → 0 intake hits |
| Prerequisite `sprk_dataverseenvironment` record creation step | ❌ absent from skill | phase-f drift table |
| Operator runbook / report skeleton | ❌ same drift family (old URL at `phase-f-e2e-acceptance-2026-08-18.md:109`, profile `trial`) | read |
| Task 089 POML | Amended for Model 2 but carries the same pre-drift contract | TASK-INDEX row 089 |

### B.6 Infra / config vs A20–A24

| Item | State | Evidence |
|---|---|---|
| `platform-controlplane.bicep` applied | ✅ live (rg-spaarke-platform-dev, 7 resources) | phase-f L2-1 |
| Bicep app-setting keys vs code reads | ❌ 4 mismatches in SOURCE (`controlplane-app-service.bicep:122-130` emits `Cosmos__Endpoint`/`Cosmos__Database`/`Cosmos__RunsContainer`/`ServiceBus__ConnectionString`; code reads `Cosmos:AccountEndpoint`/`DatabaseName`/`ContainerName`/`ServiceBus:FullyQualifiedNamespace`) — patched LIVE via manual aliases only | grep both sides |
| Bicep `jwtAudience` | ❌ source var `api://spaarke-provisioning-controlplane-${env}` (`platform-controlplane.bicep:151`) vs tenant-policy-forced actual `api://spaarke.com/provisioning-controlplane-{env}`; live override only | grep |
| `dataverseClientSecretName` default | ❌ binds `Dataverse-ClientSecret` (`platform-controlplane.bicep:105`) which r3 dropped; live dummy value seeded (bug #18 documented, unfixed at source) | grep |
| SB queue in IaC | ❌ nowhere; live queue manual, `requiresDuplicateDetection` off → FR-22 L1 idempotency inert | B.1 A4 |
| L2 UAMI SB RBAC | ⚠️ Data Sender granted manually (live only, not Bicep); **no Data Receiver grant exists anywhere** (needed by any consumer) | phase-f bug #21 |
| CustomerRunGuard config (4 keys + secret) | ❌ not provisioned anywhere; Enabled=false default | B.1 A11 |
| Per-handler ClientSecret KV refs (H6/H7 options; guard) | ❌ deferred to "Wave C5"; never provisioned | Program.cs comments 440-443, 483-486 |
| KV retentionPolicy Bicep fix | ✅ committed `1d9a89a4e` | phase-f |
| L2 deploy script / CI workflow | ❌ none (`grep -i controlplane scripts/ .github/workflows/` → 0); live deploy was ad-hoc `az webapp deploy` | grep |

### B.7 Tests vs A27

- L2 test project exists: `src/server/services/Sprk.Provisioning.ControlPlane.Tests/` (Api/, Concurrency/, Handlers/, Reconciler/, Rollback/ + Cosmos/SB smoke tests); ~524 tests green per Wave-4C record. Note it lives under `src/`, not the ADR-038 `tests/**` KEEP paths.
- Reconciler tests drive `RunTickAsync`/`ApplyHandlerOutcomeAsync` directly — they prove the logic, not the wiring (the missing production caller is invisible to them).
- **No dispatcher tests exist** (nothing to test). No end-to-end seam test covers "SB message in → handler executed → Cosmos transitioned". The FR-22 load test (task 062 ✅) tested enqueue-and-202 + reconciler DAG advancement — not consumption.
- ArchTests I1–I5: 65/65 green (Wave 4C/4D records) — these verify code patterns, not runtime provisioning.

---

## (C) Gap Catalog

### CATEGORY 1 — Missing entirely (never built; no task owned it)

| ID | Gap | Evidence |
|---|---|---|
| C1.1 | **Wave-C5 Service Bus dispatcher/consumer** — the execution engine (receive envelope → resolve handler by HandlerId → invoke → apply outcome → complete/abandon/deadletter). Root cause traceable to the FR-22/design-§4.2-step-2 vs D3/D8/D12 contradiction (reading guide): the worker was specified into the BFF's infrastructure on paper while handlers were mandated into L2; no POML was ever generated for it in either home | B.1 A6; `IProvisioningHandler.cs:16-30`; grep 0 matches |
| C1.2 | Handler-resolution surface: 18 of 19 handlers not resolvable by HandlerId/interface (concrete-type DI only) | B.1 A7 |
| C1.3 | Handler execution environment (pwsh + az + pac + repo scripts + bicep dir + solution ZIPs + seed manifests + auth chain, wherever handlers execute) — affects ≥11 of 19 handlers | B.3 |
| C1.4 | Real L2 Dataverse registry client (read + Ready-write against admin env `sprk_dataverseenvironment`) — referenced by tasks 042/055 comments as "Wave C5 … once the L2 Dataverse client wiring lands"; that wiring task never existed | B.1 A15/A16 |
| C1.5 | Level-2 (Redis) idempotency on the dequeue side — documented no-op; no Redis dependency in L2 | B.1 A14 |
| C1.6 | Dead-letter / poison-message policy for `sprk-provisioning-jobs` | implied by C1.1 |
| C1.7 | L2 deploy path (script or workflow) — the L2 service itself has no repeatable deployment mechanism | B.6 |
| C1.8 | Dispatcher/consumption test surface (unit + integration seam + the "30-min handler completes" half of FR-22 acceptance) | B.7 |

### CATEGORY 2 — Built but unwired

| ID | Gap | Evidence |
|---|---|---|
| C2.1 | `ApplyHandlerOutcomeAsync` (§4C outcome application) — implemented + tested, zero production callers; the §4C taxonomy currently never executes at runtime | grep: definition only |
| C2.2 | Canonical secret-catalog manifest (task 084, ✅) never DI-swapped into H4 — `Program.cs:328` still binds interim `StaticKvSecretManifest` | B.2 H4 |
| C2.3 | ~~StateReconcilerService unregistered~~ — **FINDING REVERSED**: it IS registered and enabled (`ReconcilerModule.cs:82`, `Program.cs:986`, `Enabled=true` default). Session-close "bug #23" was incorrect. The reconciler's real blockers are C1.1 (nothing reaches Running) and C4.5 (scanner query vs enum serialization) | B.1 A8, B.4 |

### CATEGORY 3 — Placeholder implementations (inside ✅-marked tasks)

| ID | Component (handler) | Placeholder behavior | Consequence for E2E |
|---|---|---|---|
| C3.1 | `PlaceholderTrapVerifier` (H13) | InfraFault for all T1–T6 | H13 loops Resumable forever; FR-33 unverified |
| C3.2 | `PlaceholderInvariantVerifier` (H13) | InfraFault for all I1–I5 | same; FR-28..32 runtime-half unverified |
| C3.3 | `DataverseRegistrySetupStatusUpdater` (H13) | Success with **no Dataverse write** | `sprk_setupstatus = Ready` never written — the acceptance target itself |
| C3.4 | `NullDataverseEnvironmentRegistryClient` (H0.5, H13) | all lookups null | FR-02 re-consent semantics inert; H13 short-circuit inert |
| C3.5 | `NullSubscriptionReadinessProbe` (H1) | no ARM call | FR-03 fictional |
| C3.6 | `NullAdminConsentVerifier` (H3) | always Verified | admin-consent gate not actually checked (FR-06) |
| C3.7 | `StubAiSearchTenantFilterTemplateProvisioner` (H2b) | logs + Success | I2 provisioning-half (FR-29/Model 1) not real |
| C3.8 | `DeferredAppConfigSeeder` (H12b, 2 of 4 scopes) | no-op | field-mapping + chart-def never seeded (FR-16 partial) |
| C3.9 | `DotnetR3GateVerifier` interim posture (H9) | missing gate artifacts → Skipped | acceptable-by-design interim; noted for completeness |
| C3.10 | H6/H7 `ClientSecret` option KV wiring deferred | options unbound in deployed config | H6/H7 cannot authenticate even in-process |

### CATEGORY 4 — Real impl, broken (bugs)

| ID | Bug | Status |
|---|---|---|
| C4.1 | #17 enqueuer IAsyncDisposable-only → DI dispose failure → 500s | Fixed, in worktree (uncommitted) |
| C4.2 | #19 `Ttl` null serialization rejected by Cosmos | Fixed, in worktree (uncommitted) |
| C4.3 | #20 `RunId` STJ `[JsonPropertyName("id")]` ignored by Cosmos Newtonsoft path | Fixed (Newtonsoft attr added), in worktree (uncommitted) |
| C4.4 | #21 UAMI missing SB Data Sender / #22 queue nonexistent / #18 dropped `Dataverse-ClientSecret` | Live workarounds only; sources unfixed (counted in Cat 5) |
| C4.5 | **NEW (this analysis)**: `RunStatus` enum serialization mismatch — Cosmos default serializer (Newtonsoft-based; proven by #19/#20) ignores the STJ `JsonStringEnumConverter` (`ProvisioningRun.cs:177`) and writes `status` as an integer; `CosmosActiveRunScanner.cs:46` queries `c.status IN ('Running','WaitingOnGate')` as strings → the reconciler and I6 crash-recovery scans would return **zero rows even for genuinely Running runs**. High confidence; verifiable in one read of the live Cosmos doc (`runs/65109e91-…`). Same defect class threatens any future string-comparison SQL on enum-typed fields (`GateState`, `QuarantineState`) | Not fixed; not in the 23-gap log |
| C4.6 | **NEW (this analysis)**: FR-22 Level-1 idempotency inert — deterministic MessageId only dedups if the queue has duplicate detection enabled; the live queue was created with `az` defaults (off) and `service-bus.bicep:39` sets `requiresDuplicateDetection: false` for the queues it does manage | Not fixed; not in the 23-gap log |

### CATEGORY 5 — Config / infra gaps (all part of the deliverable)

| ID | Gap |
|---|---|
| C5.1 | 4 Bicep↔code config-key mismatches fixed live-only; `controlplane-app-service.bicep` source still emits wrong keys — next Bicep apply reverts the fix |
| C5.2 | `jwtAudience` Bicep var wrong vs tenant-policy URI; live override only |
| C5.3 | `dataverseClientSecretName` default binds a dropped secret; dummy value seeded live; no defined post-r3 credential for L2's registry/guard auth |
| C5.4 | `sprk-provisioning-jobs` queue absent from all IaC; created manually; no duplicate detection (C4.6) |
| C5.5 | L2 UAMI SB **Data Sender** granted live-only (not IaC); SB **Data Receiver** granted nowhere (required by any consumer) |
| C5.6 | CustomerRunGuard config (TargetDataverseUrl/TenantId/ClientId/ClientSecret + Enabled=true) not provisioned → I5 serialization OFF at runtime |
| C5.7 | H6/H7 handler ClientSecret app-settings/KV refs not provisioned (pairs with C3.10) |
| C5.8 | L2 UAMI has no Graph or Dataverse permissions — even the in-process-capable handlers (H7/H10/H11/H12c) cannot authenticate to their targets |
| C5.9 | No repeatable L2 deploy mechanism (C1.7 restated as infra debt: live binary is ahead of committed source until worktree fixes land) |

### CATEGORY 6 — Skill / operator gaps (all part of the deliverable)

| ID | Gap |
|---|---|
| C6.1 | SKILL.md profile enum drift (`dev`/`trial` vs `spaarke-hosted-model1-trial`/`spaarke-hosted-model2`/`customer-owned-model2`) |
| C6.2 | SKILL.md missing `environmentId` intake (request 400s without it) |
| C6.3 | SKILL.md missing prerequisite step: create placeholder `sprk_dataverseenvironment` record and capture its GUID |
| C6.4 | URL/audience fix exists only as an uncommitted worktree edit |
| C6.5 | Operator runbook + Phase-F report skeleton + task-089 POML carry the same pre-drift contract (old URL, old profile values, no environmentId) |
| C6.6 | Skill Step-6 completion handoff (registry update via Dataverse MCP) presumes a run can complete — blocked on Cats 1–3; also `sprk_setupstatus` write duplication semantics between skill (operator-side) and H13 (pipeline-side, currently no-op) are undefined in the skill text |

---

## (D) Delta — what closing each gap entails (accounting only; the deliverable is fixed)

Per owner directive: E2E customer provisioning is the deliverable — full stop. The following is the complete accounting of what must be built/wired/tested/configured for the acceptance target to be met. It is not a menu and not a plan; sequencing and design decisions belong to the owner-approved follow-on.

| Gap | What closing it entails | Size class |
|---|---|---|
| C1.1 dispatcher | A queue-consuming worker: `ServiceBusProcessor`/receiver over `sprk-provisioning-jobs`; deserialize `HandlerEnvelope`; resolve handler by `HandlerId`; invoke `HandleAsync` with long-running-lock/renewal semantics (handlers run 10–30 min; SB lock max ~5 min → renewal or receive-and-delete-with-state design decision); apply outcome via the existing §4C path (C2.1); complete/abandon/deadletter; concurrency limits; per-customer ordering (enqueuer sets SessionId → session-aware receiver or a decision to drop sessions). Comparable in scope to BFF's `ServiceBusJobProcessor` (~500 LOC) plus the outcome-application wiring, plus tests (C1.8). Where it RUNS is bound to the C1.3 decision, since the handlers it invokes need the execution environment | **Substantial — the runtime executor** |
| C1.2 handler resolution | Registration refactor so all 19 handlers resolve by id (keyed services or `IEnumerable<IProvisioningHandler>` + HandlerId match, per the option already noted in `IProvisioningHandler.cs`); touch = Program.cs/modules + a registration-completeness test | Small-medium |
| C1.3 execution environment | For each of the ~11 script/CLI-dependent handlers, either (a) the runtime where the dispatcher executes gains pwsh + az + pac + repo scripts + bicep + ZIPs + manifests + a non-interactive auth chain (custom container / self-hosted worker / build pipeline agent — a design decision), or (b) the collaborator is re-implemented against SDK/REST in-process (per-handler rewrite). Either way this is per-handler work multiplied across H0, H2a, H2b(provisioner), H3, H4(writers), H5, H6, H8, H9, H12a, H12b, H13(scripts), H14a. No task ever scoped this; it is the second-largest block after C1.1 | **Substantial — dominates with C1.1 and C3** |
| C1.4 registry client | Real Dataverse Web API client against the admin env (read by tenantId/environmentId + PATCH `sprk_setupstatus`), an authentication decision (post-r3 S2S drop: UAMI App-User vs new secret), DI swap of the two placeholders (C3.3/C3.4), tests | Medium |
| C1.5 Redis L2 idempotency | Either a Redis client + idempotency lock in the dispatcher dequeue path (per ADR-036/NFR-10 3-level contract), or a documented, reviewed exception narrowing FR-22 to 2-level for L2 | Small-medium (+ decision) |
| C1.6 dead-letter policy | Dispatcher dead-letter handling + operator visibility (runbook/logs endpoint) | Small |
| C1.7/C5.9 L2 deploy path | Publish+deploy script or workflow for L2 (parity with `Deploy-BffApi.ps1`), so committed source and live binary converge | Small |
| C1.8 tests | Dispatcher unit tests; integration seam test (message in → handler invoked → Cosmos transitioned); complete FR-22 load-test acceptance (30-min handler, no-duplicate under N reconcilers) | Medium |
| C2.1 outcome wiring | Consumed by C1.1 (call `ApplyHandlerOutcomeAsync` on every completion) — no new logic | Trivial once C1.1 exists |
| C2.2 manifest swap | One DI line + verifying H4 against the task-084 canonical manifest output | Trivial-small |
| C3.1/C3.2 trap+invariant verifiers | Real per-trap/per-invariant probes (T1 ARM read, T2/T3 Dataverse+Graph queries, T4 Exchange policy read, T5 role-assignment read, T6 app-only SPE GET; I1–I5 sample verifications) — each is an independent probe impl + tests; the task-055 header says "swap to real per-trap live-probe impl in Phase F task 089", which never did | **Substantial (11 probes)** |
| C3.3 Ready writer | Real Dataverse PATCH (rides on C1.4) | Small once C1.4 exists |
| C3.4 registry lookups | Rides on C1.4 | — |
| C3.5 H1 probe | Real ARM reachability (+ Lighthouse branch for CustomerOwned) impl + tests | Small-medium |
| C3.6 H3 consent verifier | Real Graph `oauth2PermissionGrants` query impl + tests | Small |
| C3.7 H2b filter template | Real per-tenant filter-template provisioning (I2 provisioning half) + tests | Small-medium |
| C3.8 H12b deferred scopes | Author field-mapping + chart-def seed mirrors + flip 2 DI lines | Medium |
| C3.10/C5.7 handler credentials | Define + provision the credential model for H6/H7 (KV refs + app settings + NFR-05 validation) | Small |
| C4.1–C4.3 | Commit the 3 worktree fixes | Trivial |
| C4.5 status serialization | Align Cosmos write format with scanner query (serializer registration, query change, or converter) + regression test + verify live doc | Small but load-bearing |
| C4.6 queue dedup | Recreate/configure queue with duplicate detection (ties to C5.4 IaC) or document L1 as inert and lean on L2/L3 | Small (+ decision) |
| C5.1–C5.3 Bicep source | Fix app-setting keys, audience value, secret-name default in Bicep source so the live aliases stop being load-bearing | Small |
| C5.4 queue IaC | Add queue (with dedup + sessions decision) to Bicep | Small |
| C5.5 RBAC IaC | Data Sender + Data Receiver grants for L2 UAMI in Bicep | Small |
| C5.6 guard config | Provision 4 config values + secret; flip Enabled=true; verify 409 behavior live | Small (depends on C1.4 credential decision) |
| C5.8 UAMI permissions | Grant L2 UAMI the Graph/Dataverse permissions the in-process handlers need (Graph app roles, Dataverse App User on targets) — itself a mini-H10 for the control plane | Medium |
| C6.1–C6.5 skill/runbook | Rewrite intake (environmentId + real profile enum + prerequisite record step), commit URL/audience fix, align runbook/report/POML | Small-medium (main-session-only for `.claude/**`) |
| C6.6 completion semantics | Define skill-vs-H13 registry-write ownership in skill text once C3.3 is real | Small |
| Doc reconciliation | FR-22 + design §4.2 step 2 text ("BFF's IJobHandler infrastructure") vs the owner-clarified L2-executor reality — the spec/design must be corrected to match whatever C1.1's approved design states, via the §6.5 protocol | Small |

### Summary tallies

- **CATEGORY 1 (missing entirely): 8 components.** Aggregate: **the dominant block** — C1.1 (dispatcher) + C1.3 (execution environment) are each substantial and coupled; C1.4 medium; rest small-medium.
- **CATEGORY 2 (built but unwired): 2 real items** (outcome application; manifest swap) — trivial once Cat 1 lands. Third item reversed: the reconciler IS wired (session-close bug #23 was incorrect).
- **CATEGORY 3 (placeholders): 10 items across 8 handlers** (plus 2 credential deferrals). Aggregate: **substantial** — dominated by the 11 real trap/invariant probes (C3.1/C3.2) and the registry client family (C3.3/C3.4 via C1.4).
- **CATEGORY 4 (bugs): 3 fixed-uncommitted + 2 NEW latent defects found by this analysis** (status-enum serialization breaking the reconciler scan; inert L1 dedup) + 3 live-workaround items counted under Cat 5.
- **CATEGORY 5 (config/infra): 9 gaps** — individually small; collectively they are the difference between "works once, hand-patched" and "provisionable"; per the owner's framing they are part of the deliverable, not polish.
- **CATEGORY 6 (skill/operator): 6 gaps** — small-medium; the skill cannot drive a successful run today independent of everything above.

**Bottom line**: ~75/78 tasks ✅ delivered the intake/persistence/enqueue spine, the reconciler+rollback logic (wired but starved), 19 handler cores, and the endpoint/auth surface. What stands between that and `sprk_setupstatus = Ready` is: no executor (C1.1–C1.3), no real acceptance-gate probes or registry writes (C3.1–C3.4), a reconciler-blinding serialization defect (C4.5), and the config/RBAC/skill alignment (C5/C6) — every one of which is required by the fixed deliverable.

---

*Analysis-only artifact. No code, config, Azure state, or skill files were modified. Live Azure state preserved as of session start (1 Cosmos run doc at NotStarted; 1 SB message unclaimed).*
