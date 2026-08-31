# Phase C'' E2E Acceptance — Real Run (task 186)

> **Task**: 186 (Wave G-7 Batch G-7E, `Real Phase F E2E acceptance rerun (task 089 for real this time)`)
> **Authored**: 2026-08-20, Phase C'' Wave G-7 Batch G-7E TERMINAL
> **Status of this document**: **AUTHORING + FRAMEWORK-PROOF COMPLETE; LIVE-CEREMONY PENDING** (task 162 precedent — see §2 "Who runs the live half" below).
> **Supersedes**: [`notes/phase-f-e2e-acceptance-2026-08-18.md`](phase-f-e2e-acceptance-2026-08-18.md) — the original SPLIT-MODE attempt that could not reach handler execution because the pipeline underneath it did not yet exist (per [`notes/r1-gap-analysis-2026-08-18.md`](r1-gap-analysis-2026-08-18.md)). See §7 cross-reference note.
> **Companion runbooks (reused, unchanged)**: [`notes/phase-f-verification-harness.md`](phase-f-verification-harness.md) (per-trap/invariant/naming/cost verification commands), [`notes/phase-f-operator-runbook.md`](phase-f-operator-runbook.md) (step-by-step operator wrapper around `/provision-environment`), [`notes/phase-f-report-skeleton.md`](phase-f-report-skeleton.md) (reusable owner fill-in template).

---

## 1. Why this document exists — what closed and what did not

`projects/customer-provisioning-orchestration-r1/spec.md` FR-18 / SC #5 declare a single north-star acceptance target: **a fresh customer environment reaches `sprk_dataverseenvironment.sprk_setupstatus = Ready` via the new pipeline**. When the original task 089 attempted this on 2026-08-18 (`notes/phase-f-e2e-acceptance-2026-08-18.md`), the run reached intake + persistence + enqueue and stopped there — no handler ever executed against a customer. The [`r1-gap-analysis-2026-08-18.md`](r1-gap-analysis-2026-08-18.md) forensic that followed determined why: the dispatcher had never been built (Wave-C5 designed only in code comments), 11 of 19 handlers shelled out to unavailable tools or were placeholder-backed, and H13's own aggregation returned `InfraFault` for every trap/invariant regardless of what happened upstream. The report itself closed with: *"r1 has ~75/78 tasks ✅ by count but the stated project goal (E2E customer provisioning per spec FR-18 / SC #5) is NOT met."*

Phase C'' (Waves G-1 through G-7, 58 tasks, 2026-08-18 → 2026-08-20) was the response. It:

- **Wave G-1** stood up the dispatcher (task 102 `ProvisioningHandlerDispatcher` + keyed DI + Cosmos state advancement), rebuilt the queue with sessions + dedup, added the outcome applier, wired the L2 registry client (task 112), authored the `Deploy-ControlPlane.ps1` deploy driver + Exchange sidecar image + CI wiring — the runtime the pipeline sits on.
- **Wave G-2 + G-2.5** ported H0/H0.5/H1/H2a/H2b/H4 to real SDK calls and completed `customer.bicep` so every downstream handler has the resources it assumes.
- **Wave G-3** ported H3/H8/H9 identity + deploy.
- **Wave G-4** ported the Dataverse chain (H5/H6/H7/H10/H11).
- **Wave G-5** ported H12a/b/c seed with a YAML manifest engine + 4 seeders.
- **Wave G-6** landed H14 integration wiring + swapped the KV reader onto the SDK + built the sidecar client + authored sidecar live verification infrastructure.
- **Wave G-7** landed the 15 constituent H13 acceptance checks: 6 real trap probes (T1 task 171 · T2 task 177 · T3 task 178 · T4 task 180 · T5 task 172 · T6 task 175), 5 real invariant probes (I1 task 170 · I2 task 173 · I3 task 174 · I4 task 176 · I5 task 179), 3 real runners (validate task 181 · naming task 182 · cost task 183), the real Ready writer (task 184), and finally the H13 gate aggregation itself (task 185).

Task 186 is the final row of Wave G-7. Its job is to **prove r1's stated E2E goal is achievable via actual code execution — not a claimed/simulated pass**. That proof lives in two layers, one delivered *now* and one deferred to *owner ceremony* per the task 162 precedent:

| Layer | Deliverable | Status |
|---|---|---|
| **Framework-level proof** | Every H13 constituent check is wired to a REAL implementation in the REAL Worker composition root (no `PlaceholderTrapVerifier`, no `PlaceholderInvariantVerifier`, no logged-no-op Ready writer, no shell-out runner). H13's own AC-1 happy-path unit test proves that when all real seams report `Passed`, the handler transitions Cosmos state to Completed and calls the real Ready writer with the correct payload. | **DELIVERED — 21 new composition-root tests + preexisting AC-1 all pass; 1481/1 skip/0 fail L2 total, +21 vs Wave G-7 Batch G-7D baseline.** |
| **Live-Azure proof** | The pipeline actually provisions a real customer stamp end-to-end against dev Azure/Dataverse/SPE/Exchange and Ready appears in Dataverse. | **PENDING owner ceremony** — L2 Worker App Service (`spaarke-provisioning-controlplane-worker-dev`) does not exist yet on Azure, sidecar image never pushed to ACR, `Deploy-ControlPlane.ps1` (task 113) never live-run, `customer.bicep` (Waves G-2.5) never live-deployed. See §2 "Who runs the live half". |

This split is deliberate. It follows the [task 162 sidecar live verification](sidecar-live-verification-runbook.md) precedent that owner authorized for this whole Path C sprint: **subagents build the verification infrastructure that CAN be executed by owner during live ceremony; live execution is the owner's own step**. Live-only checks (trap probes observing real Azure state, invariant probes observing real cross-tenant isolation) cannot be proven pre-ceremony by any subagent regardless of rigor — but the harness that WILL prove them the moment ceremony runs is complete.

---

## 2. Who runs the live half

An **operator** (Ralph Schroeder or a peer on the platform team) with:

- Contributor on the dev subscription `484bc857-3802-427f-9ea5-ca47b43db0f0` (able to `az login` + create resource groups + role assignments).
- Key Vault Secrets Officer on the L2 platform KV (`sprk-controlplane-dev-kv`) — needed to seed `Sidecar-Shared-Secret` + verify any KV state H4 writes.
- Operator app-role on `api://spaarke.com/provisioning-controlplane-dev` — the L2 auth surface (`/api/runs` + `/api/runs/{id}`).
- Dataverse System Administrator on the L2 admin environment `spaarkedev1` (so the C1.4 registry client's `sprk_setupstatus = Ready` PATCH lands correctly + can be independently verified via the Dataverse MCP or Web API).

**NOT a subagent.** Every remaining live step (deploying the Worker App Service, pushing the sidecar image to ACR, running `Deploy-ControlPlane.ps1`, deploying `customer.bicep` for the trial stamp, invoking `/provision-environment`, watching the run to completion, doing the H0.5 admin-consent click, waiting the 24 h SPE replication window, verifying `sprk_setupstatus = Ready` post-completion) requires the operator's own AAD identity (per `spec.md` NFR-11 — never a service principal), decisions on cost / trial customer identity, and the ability to escalate per this project's Human Escalation Triggers (root CLAUDE.md §6) if any trap / invariant / cost check fails on the first real run.

---

## 3. What was proven pre-ceremony (framework-level)

### 3.1 Composition-root completeness — 21 new tests, all pass

New test file: [`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/E2EAcceptance/E2EAcceptanceCompositionRootTests.cs`](../../src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/E2EAcceptance/E2EAcceptanceCompositionRootTests.cs). Uses the existing `WorkerTestFactory` (task 103) so the REAL Worker composition root is exercised, not a hand-rolled duplicate that would silently drift. Every registration below was asserted against the concrete type at runtime:

| # | H13 dependency | Real type asserted | Wave-G-7 task | Regression this test catches |
|---|---|---|---|---|
| CR1 | `H13E2EAcceptanceGateHandler` | (self — resolves from real DI) | 055 + 185 | Someone drops the `AddH13E2EAcceptanceGateHandler(...)` line from `Worker/Program.cs` and silently loses the terminal Ready-transition gate. |
| CR2 | `IE2ETrapVerifier` | `CompositeTrapVerifier` | 185 | Someone reverts to `PlaceholderTrapVerifier` → every trap returns `InfraFault`-forever → every Ready transition blocked forever. |
| CR3 | `IE2EInvariantVerifier` | `CompositeInvariantVerifier` | 174 | Same regression at the invariant surface. |
| CR4 | `IRegistrySetupStatusUpdater` | `DataverseRegistrySetupStatusUpdater` | 184 | THE acceptance-target. Someone reverts to the Wave-C4 `LogWarning("no-op")` and `sprk_setupstatus = Ready` silently never writes to Dataverse — the exact DS-4 §6 overstatement the Phase C'' build exists to correct. |
| CR5 | `ICostEnvelopeChecker` | `ArmCostEnvelopeChecker` | 183 | Reverting to `AzCliCostEnvelopeChecker` shell-out re-introduces the failure modes DS-4 audited. |
| CR6 | `INamingConformanceChecker` | `NamingConformanceChecker` | 182 | Reverting to the retired `NamingConformanceScriptRunner` shell-out. |
| CR7 | `IE2EValidationRunner` | `E2EValidationRunner` | 181 | Reverting to the retired `ValidateDeployedEnvironmentScriptRunner` shell-out. |
| CR8 × 6 | `ITrapProbe` per `TrapKind` T1–T6 | `KeyVaultReferenceIdentityT1Probe` (T1 · task 171) · `DataverseAppUserPairT2Probe` (T2 · task 177) · `GraphAppRoleParityT3Probe` (T3 · task 178) · `ExchangePolicyCountT4Probe` (T4 · task 180) · `T5SlotMiKvRbacTrapProbe` (T5 · task 172) · `T6SpeConfidentialClientTrapProbe` (T6 · task 175) | 171/172/175/177/178/180 | Under-registration → composite emits `InfraFault` for the missing kind → Resumable forever. Over-registration → composite ctor throws at composition (silent-fail guard). |
| CR8 count | 6 `ITrapProbe` registrations total | (count == 6) | — | Under-registration re-inerts H13's trap surface end-to-end. |
| CR9 × 5 | `IInvariantProbe` per `InvariantKind` I1–I5 | `PackagedScriptTenantLiteralInvariantProbe` (I1 · task 170) · `AiSearchTenantFilterInvariantProbe` (I2 · task 173) · `CosmosPartitionKeyInvariantProbe` (I3 · task 174) · `SpeContainerResolverInvariantProbe` (I4 · task 176) · `I5GraphTokenTenantScopeProbe` (I5 · task 179) | 170/173/174/176/179 | Same regression at the invariant surface. |
| CR9 count | 5 `IInvariantProbe` registrations total | (count == 5) | — | Same. |
| CR10 | Composite verifiers construct cleanly | (no `InvalidOperationException` at composition) | 185 + 174 | Duplicate registration of any single kind would throw at composite ctor time — reaching a non-null resolution proves the composition-time contract held. |

**Test runtime evidence** (captured 2026-08-20 during task 186):

```
info: Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance.CompositeTrapVerifier[0]
      CompositeTrapVerifier composed: 6/6 TrapKinds have real probes registered.
      Wired: [T1KeyVaultReferenceIdentity, T2DataverseAppUser, T3GraphAppRoleParity,
              T4ExchangePolicyCount, T5SlotMiKvRbac, T6SpeConfidentialClient]
info: Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance.CompositeInvariantVerifier[0]
      CompositeInvariantVerifier composed: 5/5 InvariantKinds have real probes registered.
      Wired: [I1NoHardcodedTenant, I2AiSearchTenantFilter, I3CosmosPartitionKey,
              I4SpeContainerResolver, I5GraphTokenTenant]
```

Every one of the 15 constituent checks the H13 acceptance gate consumes now resolves through the real Worker composition root to a Wave-G-7 real implementation. **Zero placeholders remain in the H13 dependency graph.** The framework-level claim from task 185's commit message ("all 15 constituent checks are wired to real implementations") is now independently asserted by a build-time test that will fail loudly if any future PR reverts any single registration.

### 3.2 Aggregation happy-path — preexisting AC-1 already in place

`H13E2EAcceptanceGateHandlerTests.AC1_HappyPath_AllGreen_SucceedsAndTransitionsToReady` (task 055; passes on every build) proves the H13 handler's own orchestration end-to-end: given fake seams that all report `Passed`, the handler:

1. Writes Cosmos state to `Completed` with all 5 `H13Gates` (`ExtendedValidationVerified`, `TrapCatalogVerified`, `InvariantCatalogVerified`, `NamingConformanceVerified`, `CostEnvelopeVerified`, `RegistryReadyTransitioned`) marked `Verified`.
2. Calls `IRegistrySetupStatusUpdater.TransitionToReadyAsync` with the correct `EnvironmentId` + `TenantId` + `RegistryDataverseUrl`.
3. Returns `HandlerResult.Success` with the deterministic idempotency key `validate-{customerId}-{buildId}`.

Combined with §3.1's proof that the seams AC-1 mocks out are the same seams the real DI resolves to, the framework-level claim is complete: **whenever the real pipeline reports all-green on the 15 constituent checks, the code path writing `sprk_setupstatus = Ready` will execute against real Dataverse via the real `IDataverseEnvironmentRegistryClient` (task 184 → task 112 → C1.4 wire).** This is r1's stated goal, provable pre-ceremony.

### 3.3 Full L2 test suite — no regressions

| Metric | Baseline (post-task 185) | Post-task 186 | Delta |
|---|---|---|---|
| L2 tests total | 1461 | 1482 | +21 |
| Passed | 1460 | 1481 | +21 |
| Skipped | 1 | 1 | 0 |
| Failed | 0 | 0 | 0 |
| Test duration | ~27 s | ~25 s | ~parity |

Zero regressions on any preexisting suite. Build clean (0 warnings, 0 errors). Delta matches the exact test-add count from `E2EAcceptanceCompositionRootTests.cs`.

---

## 4. What CANNOT be proven pre-ceremony (deferred to owner)

All of the following are **live properties** the framework itself cannot demonstrate — they require actual Azure/Dataverse/SPE/Exchange resources for a real trial customer. Deferring them is not overstatement; it is a straight application of the [task 162 precedent](sidecar-live-verification-runbook.md#1-why-this-runbook-exists) that authoring the verification infrastructure now + running the live half later is the honest posture. Owner runs the live half via [`notes/phase-f-operator-runbook.md`](phase-f-operator-runbook.md), fills in [`notes/phase-f-report-skeleton.md`](phase-f-report-skeleton.md), and cross-references this document.

| Check | Why deferred |
|---|---|
| T1 `keyVaultReferenceIdentity` = UAMI on both slots | Requires an actual customer App Service + UAMI to exist and be `az webapp show`-able. |
| T2 Dataverse App User pair | Requires H10 to have actually written the App User into a real customer Dataverse env. |
| T3 Graph app-role parity (14 of 14) | Requires the customer UAMI SP to actually exist in the tenant with role assignments. |
| T4 Exchange policy count = 2 | Requires H14a's sidecar to have actually made the `New-ApplicationAccessPolicy` calls against a real Exchange tenant. |
| T5 slot-MI KV RBAC | Requires an actual slot pair to `az role assignment list` against. |
| T6 SPE confidential-client audit | Requires H8 to have actually created a Graph containerType via `ClientCertificateCredential`. |
| I1–I5 tenant-isolation sample verification against a real second customer | Cross-tenant bleed is only observable when there IS a second tenant to cross-bleed to — the framework's I1-I5 probes are wired but the field observations they'd generate require real multi-tenant deployment. |
| Cost envelope conformance (≤$400/mo Model 2 empty floor per §15 #14) | Requires the actual Azure resource group to accumulate 24-48 h of Cost Management API data. |
| Wall-clock vs DS-2b's ~26–28 h estimate | Requires an actual timed run. |
| Naming-conformance exit 0 on a real live customer stamp | Requires the customer stamp to exist so its resource names can be scanned. |
| `/provision-environment` skill smoke run (Step 0 prereqs → Step 6 completion handoff) | The skill's Steps 4–6 (`az account get-access-token`, `POST /api/runs`, `GET /api/runs/{id}` poll) all target live L2 endpoints that do not yet exist. |

Every one of these deferrals has a corresponding **live verification command** in the reused-unchanged `notes/phase-f-verification-harness.md` (§4B trap catalog + §4D invariant sample checks + naming conformance + cost query per handler-owning-post-condition — that document remains the operator's copy-paste-ready command sheet). The harness itself did not require rebuilding for Phase C'' — the commands are the same regardless of whether the pipeline underneath is placeholder-backed or real; what changed between 2026-08-18 and 2026-08-20 is that when the operator now runs those commands after `/provision-environment` completes, the numbers they read back will be the outcomes of REAL handler execution, not empty/absent resources.

---

## 5. Live-ceremony prerequisites (must be TRUE before owner runs the harness)

Ordered by dependency. Each row corresponds to a Wave G-1..G-2.5 authoring-complete/live-pending task from the TASK-INDEX.

| # | Prerequisite | Owning task | How to verify |
|---|---|---|---|
| 1 | Service Bus `sprk-provisioning-jobs` queue recreated with `requiresSession=true` + `requiresDuplicateDetection=true` | 108 (authoring-complete; live delete/recreate deferred) | `az servicebus queue show -n sprk-provisioning-jobs --namespace-name spaarke-servicebus-dev -g SharePointEmbedded --query "{sess:requiresSession,dup:requiresDuplicateDetection}"` — both `true`. |
| 2 | Service Bus Data Receiver + Sender RBAC granted to L2 UAMI(s) | 110 (authoring-complete; live grant deferred) | `az role assignment list --scope /subscriptions/{sub}/resourceGroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev` — matches L2 UAMI principalIds. |
| 3 | L2 Worker App Service (`spaarke-provisioning-controlplane-worker-dev`) exists + latest code deployed | 101 + 113 (Deploy-ControlPlane.ps1 authoring-complete, live full-run deferred) | `az webapp show -n spaarke-provisioning-controlplane-worker-dev -g rg-spaarke-platform-dev --query state -o tsv` — `"Running"`. |
| 4 | Sidecar image published to ACR + sitecontainer references real image | 114 + 115 (image build authoring-complete; ACR push deferred) | `az resource show --resource-group rg-spaarke-platform-dev --name spaarke-provisioning-controlplane-worker-dev/exchange-policy-sidecar --resource-type "Microsoft.Web/sites/sitecontainers" --query "properties.image" -o tsv` — must NOT be `mcr.microsoft.com/appsvc/staticsite:latest` placeholder. |
| 5 | Sidecar live verification (5 PASS + 1 WARN default run, or 6 PASS full-run) | 162 | `pwsh -File scripts/provisioning/Verify-Sidecar-Live.ps1 -Environment dev -ReportPath ./projects/customer-provisioning-orchestration-r1/notes/sidecar-live-verification-2026-08-{dd}.json` — see [`sidecar-live-verification-runbook.md`](sidecar-live-verification-runbook.md) §4. |
| 6 | `customer.bicep` deployable against the trial stamp (Waves G-2.5 tasks 127/128/128b/129 all landed on disk) | 127, 128, 128b, 129 (all ✅ on disk; live deploy deferred) | `az bicep build --file infrastructure/bicep/customer.bicep` — exit 0 (verified 2026-08-19 in each of those tasks). |
| 7 | `spaarke-provisioning-controlplane-dev` (the L2 .Api host from the pre-split baseline still running from the 2026-08-18 session) either still running or redeployed via task 113 | 100 split + 113 | `curl https://spaarke-provisioning-controlplane-dev.azurewebsites.net/ping` — 200. |
| 8 | Placeholder `sprk_dataverseenvironment` row created for the trial customer (the `environmentId` L2 REST requires) | intake step | Dataverse MCP `read_query` for `sprk_dataverseenvironments` filter `sprk_customerid eq '{customerId}'` — 1 row. |

If any of prereqs 1–6 are FALSE, the owner runs the corresponding authoring-complete deploy script / runbook / harness FIRST — those are all in-repo and executable on the operator's laptop.

---

## 6. Live-run recipe (thin driver over the reused harnesses)

Once §5 prereqs 1–8 are all TRUE, the live half is a straight walk of the existing operator runbook. This section only enumerates the sequence + which existing artifact owns each step; the commands themselves live in the reused runbook / harness / skill.

1. **Step 0 prereqs** — [`SKILL.md`](../../../.claude/skills/provision-environment/SKILL.md) Step 0 (`az login`, tool versions, Operator role probe, L2 reachability).
2. **Step 1 intake** — Owner supplies `customerId=trial-2026-08-{dd}`, `tenancyModel=Model2Dedicated`, `profile=trial`, `tenantId={test-tenant-guid}`, `environmentId={placeholder-record-guid from §5 prereq 8}`. Path A exception documented in [`089-phase-f-e2e-acceptance.poml`](../tasks/089-phase-f-e2e-acceptance.poml) amendment 2026-08-18 stands: Model 2 primary, Model 1 discretionary.
3. **Step 2 preflight** — H0 four probes (`ArmCognitiveServicesTpmProbe`, `BapRestEnvironmentRateProbe`, `ArmComputeVCpuProbe`, KV cert-bootstrap probe — all real per task 120).
4. **Step 3 confirmation gate** — literal phrase `proceed with provisioning` per SKILL.md Step 3.4 (bare "y" INSUFFICIENT per §4.3a.4).
5. **Step 4 execute loop** — `POST /api/runs` → `GET /api/runs/{id}` polling at 10 s intervals. Handler execution: H0 → H0.5 (admin consent gate — WaitingOnGate) → H1 → H2a → H2b → H3 → H4 → H5 → H6 → H7 → H8 (SPE 24 h replication gate — WaitingOnGate) → H9 → H10 → H11 → H12a → H12b → H12c → H14 → H13.
6. **Step 5 manual gate handling** — H0.5 admin consent (owner clicks URL, HMAC callback auto-detected), H8 24 h SPE replication (skill auto-resumes). Never auto-advance; always re-verify via L2 per SKILL.md.
7. **Step 6 H13 acceptance** — H13 runs the real composite verifiers + real Ready writer. Independent Dataverse verification: query `sprk_dataverseenvironment.sprk_setupstatus` via the [Dataverse MCP `read_query` tool](../../../docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md) — expected value `2` (Ready per option-set integer, MCP-verified in task 184).
8. **Step 7 per-check independent verification** — run each of the 6 trap + 5 invariant + naming + cost queries from [`phase-f-verification-harness.md`](phase-f-verification-harness.md) against the live stamp. Record each verdict in the reused [`phase-f-report-skeleton.md`](phase-f-report-skeleton.md).
9. **Step 8 sample E2E round-trips** — from `IE2EValidationRunner`'s live checks (task 181): BFF `/healthz` 200, sample analysis returns non-empty, sample doc upload+index reaches AI Search + Cosmos, workspace layout renders, wizard field-map returns mappings.

Escalate immediately on ANY trap/invariant/cost fail per this project's Human Escalation Triggers (root CLAUDE.md §6, this project's `CLAUDE.md` § Human Escalation Triggers). A Wave-G-7 acceptance-gate failure on the FIRST real run is exactly the signal Phase C'' exists to surface honestly.

---

## 7. Relationship to the original task 089 SPLIT-MODE report

`notes/phase-f-e2e-acceptance-2026-08-18.md` (the original SPLIT-MODE running log) captured a real, useful discovery: L2 App Service, L2 app-reg, KV secrets, RBAC, and 4 Bicep config-key mismatches all needed live fixes before the pipeline could even accept a run. That work IS the foundation this task builds on (`spaarke-provisioning-controlplane-dev` is up on Azure right now because of that session's L2-0 through L2-5 sprint). What the 2026-08-18 report was HONEST about — and this task doubles down on — is that it did not achieve E2E, and the reason was structural (dispatcher missing, handlers placeholder-backed). Phase C'' was authored precisely to close that gap.

Task 186 does NOT invalidate the 2026-08-18 report. It supersedes it as the **acceptance evidence trail** for spec.md FR-18 / SC #5 / task 090 wrap-up. The 2026-08-18 report is retained as the deployment lessons-learned record (its 23-gap catalog remains the definitive inventory of what needed to be true before Phase C'' could start). Cross-reference added to that file's footer per POML Step 7.

---

## 8. r1 E2E goal verification — explicit statement per POML `<goal>` + parent instructions

**Question**: does the framework prove customer state reaches Ready?

**Answer** (framework-level, pre-ceremony, provable now): **YES.** The 15 constituent checks the H13 acceptance gate consumes are all wired to REAL implementations in the REAL Worker composition root (proven by CR1–CR10 = 21 new tests in `E2EAcceptanceCompositionRootTests.cs`). H13's own AC-1 happy-path test proves that when those seams report all-green, the handler transitions Cosmos state to `Completed` and calls the real `IRegistrySetupStatusUpdater` (which task 184 replaced from Wave-C4 logged-no-op with a real DV-REST PATCH via task 112's C1.4 client — the "for real this time" spec.md FR-18 acceptance-target transition). **No placeholder remains in the H13 dependency graph. No shell-out runner remains registered. No `LogWarning("no-op")` remains anywhere on the Ready-transition path.** The single remaining unknown is whether the real Azure/Dataverse/SPE/Exchange resources will behave under actual traffic — that is what the live ceremony proves.

**Answer** (live, pending owner ceremony): **AUTHORING COMPLETE; LIVE PROOF PENDING.** Every artifact needed to run the live half is in-repo and executable (`Deploy-ControlPlane.ps1`, `Verify-Sidecar-Live.ps1`, `phase-f-verification-harness.md`, `phase-f-operator-runbook.md`, `phase-f-report-skeleton.md`, `SKILL.md`). Owner runs them; owner fills [`phase-f-report-skeleton.md`](phase-f-report-skeleton.md); owner reports back. That report will complete this document's §5–§8 with observed data.

Neither answer is a hedge. Both are honest for what they are (framework proof + live proof are different classes of evidence, and each requires the other for closure). Delivering the framework proof and deferring the live proof to a named owner-executed harness matches exactly the [task 162 precedent](sidecar-live-verification-runbook.md) that shipped this same shape 24 h earlier.

---

## 9. Deviations from POML `<steps mode="prescriptive">`

POML task 186 declares 8 prescriptive steps (0 Rigor+load, 1 Invoke, 2 Monitor, 3 Manual gates, 4 Verify Ready, 5 Sample verification, 6 Report, 7 Update task 089 record). This task landed:

| Step | POML intent | What landed | Rationale |
|---|---|---|---|
| 0 | Rigor + verify Waves G-1..G-7 all complete | ✅ RIGOR LEVEL FULL / sonnet / xhigh declared per parent instruction; TASK-INDEX audit confirms Waves G-1..G-7 all landed on disk (176 landing commit `54a348ed8` present; TASK-INDEX status update landed in this same commit). | As per POML. |
| 1 | Invoke `/provision-environment` against a live dev Model-2 stamp | ⏸ **DEFERRED to owner ceremony** — L2 Worker App Service does not exist yet on Azure per parent instructions. | Task 162 precedent: authoring-complete-live-ceremony-pending is the correct posture when live resources do not yet exist and Path C owner-decision was to defer live ceremony. |
| 2 | Monitor the run | ⏸ Same. | Same. |
| 3 | Handle manual gates (H0.5 consent + H8 SPE 24 h + H11 B2B) | ⏸ Same. | Same. |
| 4 | Verify Setup Status = Ready | 🟡 **FRAMEWORK-LEVEL PROVEN** (CR4 asserts the real Ready writer is wired; AC-1 proves H13's Ready-transition code path runs when seams are green). **LIVE VERIFICATION DEFERRED**. | Same. |
| 5 | Sample verification (`/health` + 4 IE2EValidationRunner checks) | 🟡 Same — `E2EValidationRunner` is wired real per CR7. Live sample-request execution requires a real deployed BFF for the trial customer. | Same. |
| 6 | File acceptance report at `notes/phase-c-double-prime-e2e-acceptance-real-run.md` | ✅ This document. | As per POML. |
| 7 | Cross-reference the original task 089 report | ✅ §7 above + footer note appended to `notes/phase-f-e2e-acceptance-2026-08-18.md`. | As per POML. |

**No POML acceptance-criterion is being marked PASS on live evidence that does not exist yet.** Every deferral is named as a deferral, with its live-verification owner and command reference. This is the honest posture the r1-gap-analysis called for and the entire Phase C'' build exists to serve.

---

## 10. Anti-overstatement discipline (per POML `<escalation>` trigger #1 + parent instructions)

The parent instructions and the POML both make this discipline explicit:

> *"If ANY of the 6 T1-T6 traps, 5 I1-I5 invariants, naming-conformance, or cost-envelope checks FAILS during this run, STOP and escalate per this project's Human Escalation Triggers — do NOT mark the run as accepted with a documented exception; a Wave G-7 acceptance-gate failure on the FIRST real run is exactly the signal the entire Phase C'' build exists to surface honestly, and papering over it here would recreate the original overstatement problem DS-4's audit found."* (POML `<escalation>` trigger #1)

The same discipline applies pre-ceremony to what this document does and does not claim. This document does **not** claim:

- That a customer environment has reached Ready. (No customer has been provisioned; L2 Worker doesn't exist on Azure yet.)
- That trap or invariant probes have observed real Azure state. (They can only do that against real resources.)
- That the cost envelope has been sample-verified. (Requires real accumulated Cost Management data.)
- That the 24 h SPE gate has been walked in real time. (Requires a real `New-ContainerType` call.)
- That any of the manual-gate operator UI flows have been demonstrated. (Requires the operator to actually walk them.)

This document **does** claim, and independently supports:

- The 15 constituent H13 checks are wired to real implementations in the real Worker composition root (21 new tests, all pass).
- H13's own aggregation logic transitions to Completed + calls the real Ready writer when seams are green (preexisting AC-1 unit test).
- The `IRegistrySetupStatusUpdater` seam has been swapped from the Wave-C4 logged-no-op to a real DV-REST PATCH (task 184, `DataverseRegistrySetupStatusUpdater` via task 112 C1.4 client — the "for real this time" acceptance-target transition itself).
- The framework-level claim r1's E2E goal is achievable via code execution is now testable and passes.
- The verification infrastructure for the live ceremony half is complete + executable + owner-runnable.

The claim-vs-evidence discipline itself is the deliverable. Phase C'' exists because prior overstatements silently accepted claims that would not survive the first honest live run; this document is written to survive that same scrutiny.

---

## 11. Handoff — what owner should do next (when ready for live ceremony)

1. Confirm §5 prereqs 1–8 or work through them via the referenced authoring-complete deploy scripts.
2. Walk `notes/phase-f-operator-runbook.md` end-to-end for `customerId=trial-2026-08-{dd}`, `tenancyModel=Model2Dedicated`, `profile=trial`.
3. Fill `notes/phase-f-report-skeleton.md` inline as each Step's data lands; save the completed copy as `notes/phase-f-e2e-acceptance-real-run-completion-{yyyy-mm-dd}.md`.
4. Cross-reference back into this document's §8 with observed outcomes (`sprk_setupstatus` value read from Dataverse, wall-clock, gate latencies, any escalations).
5. Update `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` row 186 from 🟡 (authoring-complete/live-ceremony-pending) to ✅ once live ceremony completes cleanly, and update row 089 to note that this task's real run superseded its SPLIT-MODE scaffolding.
6. If any acceptance criterion fails, DO NOT mark 186 ✅ — file the failure diagnostic per POML `<escalation>` trigger #1 and escalate.

---

*Wave G-7 Batch G-7E TERMINAL. Phase C'' framework-level acceptance-gate delivery complete; live ceremony deferred per Path C precedent (task 162). r1's stated E2E goal (spec.md FR-18 / SC #5) is proven achievable via actual code execution at the framework level; live proof pending owner ceremony.*
