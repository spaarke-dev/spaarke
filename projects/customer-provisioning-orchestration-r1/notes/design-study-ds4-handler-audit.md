# Design Study DS-4 — Per-Handler Build Backlog (complete Phase G handler audit)

> **Produced**: 2026-08-18 by design-study sub-agent. Research + analysis only — no source edits, no `.claude/**` writes.
> **Inputs**: [`r1-gap-analysis-2026-08-18.md`](./r1-gap-analysis-2026-08-18.md) (§B.2 handler audit, §C gap catalog), [`design-study-ds1b-option-d-hybrid-deep-dive.md`](./design-study-ds1b-option-d-hybrid-deep-dive.md) (§1 SDK matrix, §5 effort), [`design-study-ds2b-concurrency-safety-deep-dive.md`](./design-study-ds2b-concurrency-safety-deep-dive.md) (session dispatch, §1.2 wall-clock), [`design-study-ds8-uami-dv-appuser-maturity.md`](./design-study-ds8-uami-dv-appuser-maturity.md) (Path X).
> **Locked Wave A decisions honored**: Option D hybrid (12 pure-.NET handlers + H14a Exchange-only sidecar) · session-serialized dispatch with the 39 `ReplaceRunAsync` Conflict arms preserved · L2 Dataverse creds = Path X (UAMI-App-User).
> **Every classification below is grep-verified** against the working tree; POML claims cross-checked against `tasks/*.poml` acceptance criteria and `tasks/TASK-INDEX.md`.

---

## 1. Handler inventory

22 handler classes in `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/` — 19 top-level DAG handlers + 3 H14 sub-handlers (H14a/b/c execute in-process inside the H14 parent, never independently enqueued — `H14IntegrationWiringHandler.cs:45` "ONE ReplaceRunAsync call"). All expose `HandlerId => HandlerIdentifier`. Only H0 is registered as `IProvisioningHandler` (`HandlersModule.cs:103`); the other 18 are concrete-type DI registrations — gap C1.2.

| HandlerId | Class (path under `Handlers/`) | LOC (handler) | Spec / design ref | Task POML |
|---|---|---|---|---|
| H0 | `Preflight/H0PreflightHandler.cs` | 424 | FR-01, NFR-12 / design §4.1 H0 | 041 ✅ |
| H0.5 | `ConsentCapture/H05ConsentCaptureHandler.cs` | 332 | FR-02 / design §4.3a, D18 | 042 ✅ |
| H1 | `SubscriptionReadiness/H1SubscriptionReadinessHandler.cs` | 570 | FR-03 | 043 ✅ |
| H2a | `BicepInfraDeploy/H2aBicepInfraDeployHandler.cs` | 618 | FR-04, FR-33 T1 | 044 ✅ |
| H2b | `AiSearchIndex/H2bAiSearchIndexHandler.cs` | 663 | FR-05, FR-29 (I2) | 045 ✅ |
| H3 | `EntraAppReg/H3EntraAppRegHandler.cs` | 622 | FR-06 | 046 ✅ |
| H4 | `KvSecretsPopulation/H4KvSecretsPopulationHandler.cs` | 806 | FR-07, FR-33 T1/T5, FR-35/36 | 047 ✅ |
| H5 | `DataverseEnvCreation/H5DataverseEnvCreationHandler.cs` | 618 | FR-08 | 048 ✅ |
| H6 | `SolutionImport/H6SolutionImportHandler.cs` | 594 | FR-09 | 049 ✅ |
| H7 | `EnvVarValues/H7DataverseEnvVarValuesHandler.cs` | 590 | FR-10 | 050 ✅ |
| H8 | `SpeContainerType/H8SpeContainerTypeHandler.cs` | 595 | FR-11, FR-33 T6 | 051 ✅ |
| H9 | `BffDeploy/H9BffDeployHandler.cs` | 803 | FR-12, NFR-01 | 052 ✅ |
| H10 | `DataverseAppUserGraphParity/H10DataverseAppUserGraphParityHandler.cs` | 499 | FR-13, FR-33 T2/T3 | 053 ✅ |
| H11 | `UserProvisioning/H11UserProvisioningHandler.cs` | 648 | FR-14 | 054 ✅ |
| H12a | `AiSeedChain/H12aAiSeedChainHandler.cs` | 435 | FR-15 | 070 ✅ |
| H12b | `AppConfigSeed/H12bAppConfigSeedHandler.cs` | 531 | FR-16 | 071 ✅ |
| H12c | `RuntimeReferences/H12cRuntimeReferencesHandler.cs` | 545 | FR-17 | 072 ✅ |
| H13 | `E2EAcceptance/H13E2EAcceptanceGateHandler.cs` | 755 | FR-18, FR-33 (T1–T6), FR-28..32 (I1–I5) | 055 ✅ |
| H14 | `IntegrationWiring/H14IntegrationWiringHandler.cs` | 565 | FR-19, FR-33 T4 | 073 ✅ |
| H14a | `IntegrationWiring/H14aExchangePolicySubHandler.cs` | 199 | FR-19 / T4 | 073 ✅ |
| H14b | `IntegrationWiring/H14bGraphWebhookSubHandler.cs` | 235 | FR-19 | 073 ✅ |
| H14c | `IntegrationWiring/H14cDataverseWebhookSubHandler.cs` | 198 | FR-19 | 073 ✅ |

Note on POML `<status>`: many handler POMLs still carry `<status>not-started</status>` while TASK-INDEX marks them ✅ — the POML status fields were not maintained. TASK-INDEX + code are the authority used here.

---

## 2. Per-handler classification

Legend: Col-1 impl state (✅ REAL / ⚠️ SHELL-OUT REAL / 🟡 PARTIAL / ❌ PLACEHOLDER) · Col-2 post-Option-D destination (A pure .NET / B sidecar / C mixed) · Col-3 migration delta · Col-4 blast radius. Packages abbreviated: **ARM.\*** = `Azure.ResourceManager.*`, **KV** = `Azure.Security.KeyVault.Secrets.SecretClient`, **Graph** = `Microsoft.Graph` 6.x, **DV-REST** = raw `HttpClient` + `DefaultAzureCredential` against Dataverse Web API (the shipped H7/H10/H11/H12c idiom), **BAP-REST** = `api.bap.microsoft.com` admin API.

| H | Col 1 — current state | Col 2 | Col 3 — migration work (LOC delta · SDKs · placeholders to build · tests) | Col 4 — deps / blockers |
|---|---|---|---|---|
| **H0** | ⚠️ SHELL-OUT REAL — core real; 4× `PowerShellPreflightProbe` shell `pwsh scripts/preflight/*.ps1` | A | ~600–800 LOC: 4 probe ports — `ARM.CognitiveServices` `GetUsagesAsync` (TPM), BAP-REST env list (env-rate), `ARM.Compute` `GetUsagesAsync` (vCPU), KV (cert-bootstrap). No placeholders. Tests: 4 probe fakes exist; add per-probe threshold unit tests | Entry handler — blocks everything. Needs dispatcher + UAMI ARM Reader on target sub. Safe to build in isolation |
| **H0.5** | 🟡 PARTIAL — core real (idempotency, restart-vs-no-op); `NullDataverseEnvironmentRegistryClient` always returns null (C3.4) → FR-02 re-consent semantics inert | A | ~50 LOC handler-side: DI-swap onto the real C1.4 registry client (Path X, DS-8 §8). Tests: re-consent no-op vs restart branches against a fake registry with data | Blocked by C1.4 registry client. BFF consent-callback side already real (task 078, 7/7 E2E) |
| **H1** | ❌ PLACEHOLDER — core real but its ONLY functional collaborator `NullSubscriptionReadinessProbe` returns Passed with no ARM call (`NullSubscriptionReadinessProbe.cs:71,94`) — FR-03 verification fictional | A | ~150–250 LOC: real probe — `ARM.Resources` subscription GET (reachability) + `Microsoft.ManagedServices/registrationAssignments` list (Lighthouse, CustomerOwned branch). `SubscriptionReadinessRejectionCodes.cs:84` already reserves the real-impl code. Tests: reachable/unreachable/delegation-missing | Blocks H2a per DAG. Safe in isolation once dispatcher exists |
| **H2a** | ⚠️ SHELL-OUT REAL — `ProvisionCustomerScriptBicepDeployRunner` (pwsh `Provision-Customer.ps1`), `AzCliArmKeyVaultRefProbe`, `AzCliUpgradeDriftDetector` (az) | A | ~600–800 LOC: `ARM.Resources` `ArmDeployment.CreateOrUpdateAsync` + RG ensure + `WhatIfAtSubscriptionScopeAsync` (typed `WhatIfChange[]`) + `ARM.AppService` KV-ref read. Effective port = script steps 1–3 only (~450 script L; steps 4–10 duplicate H4/H5/H6/H7/H8 — DS-1b #5). Requires CI Bicep→ARM-JSON pre-compile artifact. Tests: deploy-runner fake matrix + what-if drift classification | **Gates the entire resource fan-out** (H2b, H4-chain, H5-chain). Needs Bicep artifact scheme + UAMI Contributor on customer sub |
| **H2b** | 🟡 PARTIAL — provisioner = pwsh `Deploy-AllIndexes.ps1`; verifier = real REST; `StubAiSearchTenantFilterTemplateProvisioner` logs+Success (C3.7 — I2 provisioning half not real) | A | ~400–600 LOC: `Azure.Search.Documents` `SearchIndexClient` (RBAC auth — deletes admin-key handling); index JSONs become content files; REAL filter-template provisioner. Tests: 7-index create/verify + template idempotency | Needs H2a (Search service exists). Verifier already real — port is contained |
| **H3** | 🟡 PARTIAL — provisioner = pwsh `Register-EntraAppRegistrations.ps1` (982 L, real); `NullAdminConsentVerifier` always Verified (`NullAdminConsentVerifier.cs:79`) — consent gate never actually checked (C3.6) | A | ~900–1,300 LOC: **heaviest port** — Graph `Applications`/`ServicePrincipals`/`AppRoleAssignedTo`/`Oauth2PermissionGrants` + KV; the lone `pac admin assign-app-to-environment` reuses H10's DV-REST app-user idiom verbatim. Real consent verifier = one `oauth2PermissionGrants` query (~80 LOC). Tests: parity acceptance vs recorded script outputs (DS-1b §6 mitigation) | Needs H4 (KV populated) per DAG H2a→H4→H3. Admin-consent manual gate (WaitingOnGate) is an external blocker at runtime, not build time |
| **H4** | 🟡 PARTIAL — core real (never-delete guard, T1/T5) BUT (a) `StaticKvSecretManifest` interim still bound (`Program.cs:328`; task-084 canonical manifest never DI-swapped — C2.2) and (b) `AzCliKvSecretsWriter.ResolveValueForEntry` writes literal `{name}-interim-placeholder-{customerId}` **values** (`AzCliKvSecretsWriter.cs:223`) — even with az present, KV receives non-functional secrets | A | ~350–500 LOC: KV `SetSecretAsync` family; `ARM.AppService` `SitePatchInfo.KeyVaultReferenceIdentity` both slots (T1); `ARM.Authorization` role assignment (T5). Plus: 1-line manifest DI swap + **real value-sourcing per `KvSecretValueSource`** (generate/copy/reference — currently all placeholder). Tests: T1/T5 + never-delete guard against SDK fakes | **Gates H3→{H8,H9} and every downstream KV consumer (H6/H7/H14 secret reads)**. Placeholder VALUES silently break downstream handlers — top-priority correctness fix |
| **H5** | ⚠️ SHELL-OUT REAL — `PacAdminDataverseEnvCreator` (pac CLI); health probe real HttpClient | A | ~250–350 LOC: BAP-REST env create + async operation polling — the repo's own `Provision-Customer.ps1` STEP 5 (line 589) already abandoned pac for this REST sequence; port it. Tests: create/poll/timeout/duplicate-domain | Gates the whole Dataverse chain (H6→H7→H10→H11→H12x). Dataverse env-creation rate limits are the external throttle |
| **H6** | ⚠️ SHELL-OUT REAL — pwsh `Deploy-DataverseSolutions.ps1` (847 L) + `PacCliSolutionVerifier`; `SolutionImport:ClientSecret` KV wiring deferred (`Program.cs:440-443`) — cannot authenticate even in-process (C3.10) | A | ~450–650 LOC: DV-REST `ImportSolution`/`StageAndUpgrade` + `ImportJob` polling; verifier = trivial `GET /solutions?$select=uniquename,version`. Dependency-ordered 8-solution sequence as C# control flow. **Solution ZIPs must become versioned publish/content artifacts** (owed under every option). Credential: customer-env auth model (BFF app-reg secret from H4-populated KV until NG1). Tests: order/retry/partial-failure + parity vs recorded imports | Needs H5 (env exists) + H4 (real secret values). Longest-running handler (30–60 min, DS-2b §1.2) — dispatcher lock-renewal sizing case |
| **H7** | ✅ REAL — DV-REST writer; only credential config unprovisioned (C5.7) | A (native) | ~0 code. Provision `EnvVarValues:ClientSecret` KV ref (or fold into the H4 real-value work) + NFR-05 validation. Tests exist | Needs H6 (solutions define the env-var definitions). Config-only |
| **H8** | ⚠️ SHELL-OUT REAL — pwsh `Create-NewContainerType.ps1` + `Get-SpeContainerMetadata-AppOnly.ps1`; az KV writer | A | ~350–450 LOC: Graph `POST /storage/fileStorage/containerTypes` (v1.0) under `ClientCertificateCredential` (T6 cert from KV) + Graph GET verify + KV writer swap. Tests: T6 cert-path + created/verify/24h-pending | Needs H3 (app-reg) + H4 (cert secret real). **24 h SPE replication gate** is the run-level external blocker (WaitingOnGate; holds no session) |
| **H9** | ⚠️ SHELL-OUT REAL, broken-by-design — `DeployBffApiScriptRunner` → `Deploy-BffApi.ps1` runs `dotnet publish` at provision time (line 221); needs full repo + dotnet SDK in the runtime. Re-scope to artifact-based (§5 below) | A (post-E3) | ~300–450 LOC handler-side + CI workflow delta (§5). `ARM.AppService` `SwapSlotAsync` replaces `AzCliAppServiceSlotSwapper`; `DotnetR3GateVerifier` degrades to artifact-metadata check (gates run in CI). Tests: artifact-resolve/deploy/health/swap/rollback-re-swap | Needs H2a (App Service) + H4 (KV refs). CI side is a **coordinated PR** (ci-cd worktree owns `.github/workflows/**`) |
| **H10** | ✅ REAL — all 5 seams DV-REST/Graph-REST via `DefaultAzureCredential` | A (native) | ~0 code. Blocker is C5.8: grant L2 UAMI Graph app roles + Dataverse admin rights on target envs (`Grant-ControlPlaneIdentity.ps1`, DS-8 §4). Tests exist | Needs H5 (env) + H3 (BFF app-reg id). Blocks H11 |
| **H11** | ✅ REAL — Graph REST provisioner + B2B invitation + consent verifier | A (native) | ~0 code. C5.8 grants (`User.ReadWrite.All`, `User.Invite.All` etc.). Tests exist | Needs H10. B2B consent gate external at runtime |
| **H12a** | ⚠️ SHELL-OUT REAL — pwsh `Invoke-SeedManifest.ps1 -Live` (+ `powershell-yaml` module) | A | ~400–550 LOC: YamlDotNet manifest engine + DV-REST seed writes (H12c's exact idiom); seed manifests become content files. Tests: manifest-hash idempotency + per-step outcomes | Needs H6 (solutions/tables exist). Parallel with H12b |
| **H12b** | 🟡 PARTIAL — DataGrid + workspace-layout scopes = real pwsh seeders; field-mapping + chart-def scopes = `DeferredAppConfigSeeder` no-op (C3.8 — FR-16 half-delivered) | A | ~450–600 LOC: 2 near-mechanical DV-REST ports (~40-line az-token + Invoke-RestMethod scripts) + **2 greenfield seeders** (field-mapping, chart-def — never authored anywhere). Tests: per-scope seed/verify | Needs H6. Parallel with H12a |
| **H12c** | ✅ REAL — DV-REST writer + pinned 3-model catalog | A (native) | ~0 code; credential config only | Needs H12a+H12b+H2a (3-way join). Blocks H14 |
| **H13** | ❌ PLACEHOLDER — the acceptance gate cannot go green as coded: `PlaceholderTrapVerifier` → InfraFault for ALL T1–T6 (`:56`), `PlaceholderInvariantVerifier` → InfraFault for ALL I1–I5, `DataverseRegistrySetupStatusUpdater` returns Success **without any Dataverse write** (`:49` LogWarning) — the `Ready` transition is a no-op; plus 3 shell-out runners (validate/naming/cost) | A | ~1,200–1,600 LOC — see §6. 11 real probes + 3 runner ports + Ready writer (rides C1.4/Path X). Tests: per-probe Pass/Fail/InfraFault ×11 + gate aggregation | **Terminal gate — blocked by every other handler being real**; buildable earlier against fakes (§6) |
| **H14** | 🟡 mixed — parent orchestration real; H14b/H14c REST real; `AzCliKvSecretReader` az shell-out; H14a = pwsh EXO | **C** | ~200–300 LOC: KV reader → `SecretClient`; H14a → sidecar HTTP client (`ExchangePolicySidecarClient : IExchangePolicyApplier`, DS-1b §3) + sidecar image/CI/Bicep. Tests: sidecar client outcome mapping | Needs H3 (app ids), H4 (cert), H12c upstream. Sidecar build is fully parallelizable |
| **H14a** | ⚠️ SHELL-OUT REAL — `ExchangePolicyScriptApplier` → `Set-ExchangeApplicationAccessPolicy.ps1` (best-behaved script in fleet: idempotent, headless, JSON envelope) | **B** | Script moves INTO the sidecar unchanged (+~10-line `-Certificate` amendment for Linux); no C# rewrite — verified PS-only surface (DS-1b §0, App-RBAC successor also PS-only) | — |
| **H14b** | ✅ REAL — Graph REST subscriptions | A (native) | 0 | — |
| **H14c** | ✅ REAL — DV-REST service-endpoint webhooks | A (native) | 0 | — |

**Tally (19 top-level)**: ✅ REAL 4 (H7, H10, H11, H12c) · ⚠️ SHELL-OUT REAL 6 (H0, H2a, H5, H6, H8, H9) · 🟡 PARTIAL 7 (H0.5, H2b, H3, H4, H12b, H14, — ) · ❌ PLACEHOLDER 2 (H1, H13). Sub-handlers: H14b/c REAL, H14a shell-out→sidecar. Destination: 12 Class A + 6 native-already + 1 Class C (H14, containing the sole Class B residual H14a) — matches DS-1b §2 exactly.

---

## 3. Handler-by-handler deep-dive (delta vs POML claims)

Only material divergences narrated; mechanical shell-out→SDK swaps are fully covered by the table + DS-1b §1.

- **H0** — POML 041 claimed 4 quota probes; delivered as real *pwsh wrappers* that cannot run in the L2 App Service (no pwsh, scripts not in publish — gap B.3). Recipe: replace each `PowerShellPreflightProbe` instance behind `IPreflightQuotaProbe` with an SDK probe; threshold logic ports from the scripts' own comparison blocks.
- **H1** — POML 043 acceptance criteria **required real ARM behavior** ("Given … the target subscriptionId is reachable via ARM … returns success"; "Lighthouse delegation is MISSING … returns `LighthouseDelegationMissing`"). Delivered: `NullSubscriptionReadinessProbe` returns Passed unconditionally with a LogWarning. Criteria were satisfied only via injected test fakes — **the POML materially overstates delivery**. Recipe: `ArmClient.GetSubscriptionResource(...).GetAsync()` for reachability; `GET /subscriptions/{id}/providers/Microsoft.ManagedServices/registrationAssignments` for Lighthouse; map to the two reserved rejection codes.
- **H2a** — POML 044 delivered what it claimed (script-runner architecture). The Option-D delta is the locked migration: ARM SDK deploy of CI-precompiled ARM JSON, typed what-if. Keep the T1 KV-ref probe post-condition (now `ARM.AppService` read).
- **H3** — POML 046 disclosed the Null consent verifier in scaffold headers, but the handler *behaves* as if consent is verified (gate can advance on fiction). Recipe order: Graph app ensure → 14 `AppRoleAssignedTo` grants (ids from `GraphAppRoles.cs` — 11 null GUIDs MUST be completed first per project CLAUDE.md) → consent gate → real `Oauth2PermissionGrants` verify → KV writes → Dataverse app-user assign via H10 idiom.
- **H4** — POML 047 honestly disclosed the interim manifest (Path A per §11 in TASK-INDEX). What it did NOT surface: the writer's **placeholder secret values** (`AzCliKvSecretsWriter.cs:223`) mean a "successful" H4 leaves every downstream secret consumer broken. Under Option D: SecretClient + real value-sourcing + task-084 canonical manifest swap (C2.2, 1 line) land together.
- **H6** — POML 049 delivered the script-importer architecture; the credential was knowingly deferred ("Wave C5 wires the option-binding" — `Program.cs:440-443`) but Wave C5 never existed. Port to `ImportSolution`/`StageAndUpgrade` with `ImportJob` polling; ZIP artifact packaging is the hidden prerequisite.
- **H9** — see §5.
- **H12b** — POML 071 claimed FR-16's 4 scopes; 2 of 4 are `DeferredAppConfigSeeder` no-ops whose mirrors were "never authored in Wave C5". Half of FR-16 is undelivered — second-strongest overstatement after H1/H13.
- **H13** — POML 055 acceptance criteria required "`sprk_dataverseenvironment.SetupStatus` transitions to Ready" and independent re-verification of all 11 checks. Delivered: all three seams placeholder (traps InfraFault, invariants InfraFault, Ready writer no-op). Criteria pass only against fakes. The POML's own escape hatch ("swap to real per-trap live-probe impl in Phase F task 089") pointed at a task that never did it. **The strongest overstatement in the WBS** — the acceptance target itself is a logged no-op.
- **H14** — POML 073 delivered honestly (incl. the ADR-028 Path A for Exchange app-only PS). Option-D delta is confined to the sidecar client + KV reader swap.

---

## 4. Build-wave sequencing (Phase G)

Ordering constraints honored: Wave A blockers first (dispatcher, C4.5 RunStatus serialization fix, queue recreate with sessions+dedup); H0/H0.5 gate entry; H2a gates resources; H4 gates KV consumers; H5 gates the Dataverse chain; H10 gates H11; H12x gate H13/H14; sidecar parallelizes freely.

| Wave | Content | Rationale |
|---|---|---|
| **G-1 Foundation** | Dispatcher (`ServiceBusSessionProcessor`, `MaxConcurrentCallsPerSession=1`, calls `ApplyHandlerOutcomeAsync`) + keyed-service handler registration (C1.2) + C4.5 RunStatus serialization fix + queue recreate in IaC (sessions ON, dup-detection ON) + Bicep source config-key/audience fixes (C5.1–C5.3, DS-5) + **C1.4 registry client Path X** (`Grant-ControlPlaneIdentity.ps1` incl. C5.8 Graph grants) + L2 deploy script (C1.7). Sidecar image build starts here in parallel (independent). CI coordination PR for Bicep→ARM-JSON + BFF artifact publish (E3) opens here | Nothing executes until this lands; registry client unblocks H0.5/H13/guard; grants unblock every native handler |
| **G-2 Entry + resources** | H0 SDK probes · H1 real probe · H0.5 registry swap · H2a ARM port · H2b Search port + real filter template · H4 SDK port + real values + manifest swap | First runnable prefix H0→H1→H2a→{H2b, H4}; H4 correctness (real secret values) is prerequisite for everything after |
| **G-3 Identity + deploy** | H3 Graph port + real consent verifier (heaviest — start early, finish here with parity tests) · H8 Graph containerTypes port · H9 artifact-based rebuild (handler side; CI side from G-1) | DAG H4→H3→{H8, H9}; H8's 24 h gate starts ticking as early as possible |
| **G-4 Dataverse chain** | H5 BAP-REST port · H6 Web-API import port + ZIP artifacts · H7 credential provisioning · H10/H11 grant verification (code done) | H5→H6→H7→H10→H11 is the long chain (DS-2b: H6 30–60 min) |
| **G-5 Seed** | H12a YamlDotNet engine · H12b 2 ports + 2 greenfield seeders · H12c config | H12a ∥ H12b then H12c join |
| **G-6 Integration wiring** | H14 KV-reader swap + sidecar client wiring + sidecar live verification (H14b/c already real) | Needs H3/H4/H12c outputs; sidecar itself finished since G-1/G-2 |
| **G-7 Acceptance** | H13: 11 real probes + validate/naming/cost ports + Ready writer swap · then Phase F E2E acceptance rerun (task 089 for real) | Terminal. Probes buildable from G-3 onward against fakes (§6); LIVE green requires all waves |

H13 probe development should be **pipelined, not serialized**: each probe's target handler completion (T1↔H2a/H4, T2/T3↔H10, T4↔H14a, T5↔H4, T6↔H8; I2↔H2b) unblocks that probe's live test — start each probe in the same wave its subject lands.

---

## 5. H9 special case — artifact-based re-scope

**Where artifacts come from today**: `.github/workflows/deploy-bff-api.yml` already builds the exact artifact H9 needs — `dotnet publish` (line 82) → `actions/upload-artifact` `bff-api-build` (lines 84–89, **retention 7 days**) → zip → `az webapp deploy --type zip` (lines 174–182). Two problems for H9 reuse: (1) GitHub Actions artifacts are short-lived and need a GitHub token to fetch — wrong store for a fleet control plane; (2) the workflow is `workflow_dispatch`-only and targets the platform BFF, not customer stamps.

**Re-scope design**:
1. **CI publishes to a well-known blob** (coordinated PR): extend the BFF build job to also push `bff-api-{version}.zip` + a `latest.json` manifest (version, sha, size, r3-gate results) to a `provisioning-artifacts` container on a platform storage account. The r3 gates + NFR-01 size check run in CI against the artifact — their results ride in the manifest.
2. **H9 handler post-rescope**: resolve desired version (run parameter or `latest.json`) → verify gate/size metadata from manifest (replaces `DotnetR3GateVerifier` shell-outs — becomes a pure-C# metadata check) → download blob (`Azure.Storage.Blobs`, UAMI RBAC) → deploy to staging slot via Kudu zip-deploy (`POST https://{app}-{slot}.scm.azurewebsites.net/api/zipdeploy`, MI token) or `ARM.AppService` extension → health-probe staging (existing `HttpHealthProbe` — already real) → swap via `WebSiteSlotResource.SwapSlotSlotAsync` (LRO) → verify production → rollback re-swap on failure (existing handler logic preserved).
3. **SDK coverage**: `Azure.ResourceManager.AppService` covers slot swap (`SwapSlotAsync` on `WebSiteSlotResource`, [learn.microsoft.com/dotnet/api/azure.resourcemanager.appservice](https://learn.microsoft.com/en-us/dotnet/api/azure.resourcemanager.appservice.websiteslotresource)) + stop/start; zip-deploy itself is best done via the documented Kudu `/api/zipdeploy` REST (App Service's own recommended automation path) since the ARM SDK has no first-class zip-deploy primitive — one authenticated POST.
4. **Runbook change**: releases that customer provisioning may consume MUST run the artifact-publish workflow (or a `workflow_run` follow-on) so the blob is current; `sprk_bffversion` registry column records the deployed artifact version; H9 refuses to deploy if the manifest's gate results are missing/red.

**Deletion dividend**: `DeployBffApiScriptRunner`, `DotnetR3GateVerifier` shell-outs, and the repo+dotnet-SDK runtime requirement (H9 was "the heaviest environment dependency of all" — gap B.2) all disappear; H9 becomes an ordinary Class-A handler.

---

## 6. H13 placeholder deep-dive

Seam enumeration (all registered in `E2EAcceptanceModule.cs`):

| Seam | Current impl | Real impl | Blocked by |
|---|---|---|---|
| `IE2ETrapVerifier` **T1** keyVaultReferenceIdentity both slots | `PlaceholderTrapVerifier` → InfraFault | `ARM.AppService` read `Data.KeyVaultReferenceIdentity` on site + slot; compare to UAMI resource id | H2a/H4 real (T1 owner) |
| — **T2** Dataverse App User pair | same | DV-REST `systemusers?$filter=applicationid eq …` ×2 (UAMI + BFF app-reg) | H10 (done) + C5.8 grants |
| — **T3** Graph app-role parity (14) | same | Graph `/servicePrincipals/{id}/appRoleAssignments` vs `GraphAppRoles.cs` (11 null GUIDs must be completed) | H10 + GUID completion (task 005 follow-through) |
| — **T4** Exchange policy count | same | **Sidecar call** — extend the H14a sidecar with a read-only `GET /policies` route wrapping `Get-ApplicationAccessPolicy` (the ONE H13 probe that is not pure .NET; keep it in the same sidecar, same envelope) | H14a sidecar + H14 run |
| — **T5** slot-MI KV RBAC / UAMI structural | same | `ARM.Authorization` role-assignment list at KV scope, or structural UAMI check post-Phase-C | H4 real (T5 owner) |
| — **T6** SPE confidential-client creation | same | KV secret presence + Graph containerType GET under `ClientCertificateCredential` (app-only proof) | H8 real + 24 h gate elapsed |
| `IE2EInvariantVerifier` **I1** no hardcoded tenant | `PlaceholderInvariantVerifier` → InfraFault | On-disk grep of packaged scripts/content for tenant-shaped GUID defaults — pure C#, zero live deps (easiest first probe) | none |
| — **I2** AI Search tenant filter | same | Sample query against customer Search endpoint asserting unconditional `tenantId eq` | H2b real (incl. filter template) |
| — **I3** Cosmos partition-key | same | Sample query against customer Cosmos with pk predicate | H2a |
| — **I4** SPE container resolver | same | BFF diagnostic endpoint call (`ITenantContainerResolver` path) | H9 deployed BFF |
| — **I5** Graph token tenant scope | same | Assert token-acquisition path carries explicit tenantId (probe against Graph with per-tenant authority) | C5.8 |
| `IE2EValidationRunner` | pwsh `Validate-DeployedEnvironment.ps1` (real script, unrunnable in L2) | C# HttpClient effect probes (BFF /health, sample analysis, doc upload+index, layout render, wizard field-map) — **same work as the probe families, done once** (DS-1b #25 convergence) | H9 + H6/H7 |
| `INamingConformanceChecker` | pwsh `naming-conformance-check.ps1` (0 az calls — pure string checks) | Trivial C# port | none |
| `ICostEnvelopeChecker` | `AzCliCostEnvelopeChecker` (az costmanagement) | `ARM.CostManagement` query or one REST POST | H2a (resources to cost) |
| `IRegistrySetupStatusUpdater` | **logged no-op** (`DataverseRegistrySetupStatusUpdater.cs:49`) — Ready never written | DV-REST PATCH `sprk_setupstatus` + clear `sprk_currentrunid` via the C1.4 Path X registry client | C1.4 (G-1) |

**Can H13 be built in one wave?** The *code* can: every probe is independently unit-testable with canned outcomes, and I1/naming/Ready-writer have no live dependencies at all. But H13 cannot go *live-green* until every subject handler is real — so the correct shape is: author probes pipelined alongside their subject handlers (G-3..G-6), assemble + live-verify in G-7. Building all of H13 in a single late wave would serialize ~2 weeks of probe work behind everything else for no reason.

---

## 7. Total build effort — tally + reconciliation with DS-1b

| Block | LOC (net new/changed) | Person-days |
|---|---|---|
| Handler SDK ports (Class A: H0, H1, H2a, H2b, H3, H4, H5, H6, H8, H9, H12a, H12b incl. 2 greenfield seeders + filter template + consent verifier + H14 swaps) | ~5,500–7,500 | 47–62 (matches DS-1b §5 Class-A subtotal) |
| H13: 11 probes + 3 runner ports + Ready writer | ~1,200–1,600 | 8–12 |
| Sidecar (image, CI, sitecontainer Bicep, HTTP client/listener, cert amendment, + T4 read route) | ~500 + infra | 5–8 |
| Dispatcher + keyed registration + outcome wiring + Redis-L2 decision + DLQ policy + C4.5 fix (DS-2/DS-2b scope) | ~800–1,100 | 6–9 |
| C1.4 registry client (Path X) + `Grant-ControlPlaneIdentity.ps1` + guard refactor (DS-8 §6) | ~400–600 | 3–5 |
| IaC/config alignment: queue+RBAC in Bicep, config-key/audience/secret-name source fixes, H6/H7 creds, L2 deploy script (DS-5 Cat-5) | ~small ×9 | 4–6 |
| Skill/runbook alignment (Cat-6, main-session-only for `.claude/**`) | — | 2–3 |
| Tests beyond per-block (integration seam: message→handler→Cosmos; FR-22 30-min/load; probe live suite) | — | 5–7 |
| Real Phase F E2E acceptance (task 089 rerun, incl. 24 h-gate calendar span) | — | 3–5 |
| **Phase G total** | **~8,500–11,500** | **≈ 83–117** |

**Reconciliation with DS-1b's 55–75 pd**: consistent — no contradiction, different scopes. DS-1b §5 priced **Option D's migration surface only** (Class-A ports 47–62 + sidecar 5–8 + payload 2–3) and explicitly excluded, as "owed under every option": the 11 H13 probes, the 2 H12b seeders, E3/H9 re-scope CI work, dispatcher (DS-2's scope), registry client (C1.4), Cat-5/6 config+skill, and acceptance. This audit's total = DS-1b's 55–75 **+ ~28–42 pd of owed-under-every-option work** the gap analysis catalogued. The complete-deliverable number the owner should plan against is **~85–115 person-days** (≈ 2 engineers × 8–11 weeks, with the sidecar, CI coordination, and probe pipeline as the parallelizable slack) — plus the fixed ~24 h SPE calendar gate inside the final acceptance run.

---

*Analysis-only artifact. No code, config, Azure state, or `.claude/**` files modified.*
