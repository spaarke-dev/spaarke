# Current Task State — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-19 (Batch G-2C task 125 H4 SDK port COMPLETE + pushed; task 126 H4 real-values next, SEQUENTIAL)
> **Working directory**: `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` — in sync with `origin/work/customer-provisioning-orchestration-r1` (see git log for latest commit)
> **PR**: https://github.com/spaarke-dev/spaarke/pull/779 (DRAFT — DO NOT MERGE — Phase C'' incomplete; Waves G-2..G-7 remain)

## Batch G-2C progress (2026-08-19)

- **Task 125 (H4 SDK port) ✅ COMPLETE**: `SecretClientKvWriter` / `ArmAppServiceIdentityPatcher` / `ArmSlotIdentityRoleGranter` replace the 3 retired AzCli* collaborators. T1 both-slots PATCH unconditional (completeness-tested). T1 VERIFY reuses task 123's `ArmKeyVaultRefProbe` unmodified. T5 idempotency redesigned (deterministic role-assignment name). New `Azure.ResourceManager.Authorization` 1.1.4 package. FR-39 pluggability satisfied (no BFF-API-ClientSecret special-casing — test-verified). 16 new tests; L2 suite 867 → 883, zero regressions. Zero code-review/adr-check findings. Full detail in the POML's `<notes>` COMPLETION NOTES block.
- **Task 126 (H4 real-values correctness gate) — NEXT, SEQUENTIAL**: replaces `ResolveValueForEntry`'s literal `{name}-interim-placeholder-{customerId}` (left INTACT by task 125, as directed) with real secret generation. Both tasks touch the same H4 handler + KV writer file family, so no parallelism.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Phase C'' — Execution-Engine Build (delivers r1's stated goal: E2E provisioning per FR-18 / SC #5) |
| **Wave G-1 status** | ✅ 100% COMPLETE — 17 tasks + governance + auth-v4 coord = 20 substantive commits landed and pushed |
| **Next decision** | **Owner call**: (A) run Wave G-1 LIVE CEREMONY first (owner-in-the-loop, ~5 discrete Azure operations), then Wave G-2; OR (B) start Wave G-2 dispatch in parallel with live ceremony (independent tracks); OR (C) Wave G-2 now, ceremony later |
| **Live-ceremony backlog** | 7 items — see § Live Ceremony Backlog below |
| **Wave G-2 dispatch plan** | 7 POMLs (120-126); H0/H1/H0.5/H2a/H2b SDK ports + H4 split (125 SDK-port + 126 real-values-correctness-gate). Tasks 125/126/130/142 amended for auth-v4 FIC pluggability per commit `8edc72d66` |
| **Status** | Fully clean checkpoint — working tree empty, all commits pushed |

---

## What was accomplished this session (in commit order)

### Foundation: gap analysis → design studies → decisions → amendments → task decomposition

| Commit | Content |
|---|---|
| `57003b1b0` | DS-6 Batch 1 amendments (spec.md FR-22 + design.md §4.2 restructured with §4.2a runtime topology + §4.2b dispatcher design + S-12 MUST rules; the "BFF IJobHandler infra" root muddle fixed) + 10 evidence design-study notes |
| `a33d719d5` | DS-6 Batch 2 amendments (runtime fact base + retry envelope + serializer contract + Path X cluster + H9 artifact) |
| `3ee508628` | DS-6 Batch 3 amendments (summary/scope/dispositions/ADRs/SC + v3.4 version bump) |
| `5cdc26c0e` | DS-6 addendum sweep (IProvisioningHandler terminology consistency across spec/design/plan/README/CLAUDE — 25 edits) |
| `b0f535ef0` | **Phase C'' task decomposition — 58 POMLs across Waves G-1..G-7** |

### Wave G-1 build (17 tasks) + governance + auth-v4 coord

| Commit | Task | Content |
|---|---|---|
| `b5508eebb` | 100 | Split L2 project → `.Core` + `.Api` + `.Worker` (234 files renamed, all 6 project builds green, 670 tests unchanged) |
| `382478f63` | 106 | C4.5 serializer fix — Newtonsoft `StringEnumConverter` on `RunStatus`/`GateState`/`QuarantineState` + serializer contract test + Cosmos scanner seam test (regression-proof verified) |
| `eefad966e` | 111 | `Grant-ControlPlaneIdentity.ps1` — Path X UAMI-App-User + C5.8 Graph app-role grants (idempotent, `-DryRun`/`-WhatIf`, delegates Section B to sibling script per §11) |
| `762fe50e8` | 108 + 112 + 114 | 108 🟡 (Bicep queue module + drain-verify runbook — live recreate deferred) · 112 ✅ (C1.4 registry client, MI-native, `DefaultAzureCredential`) · 114 ✅ (Exchange sidecar image ~200-230 MB, pwsh 7.4-mariner + ExchangeOnlineManagement 3.5.1) |
| `8c20179bd`+`23de4e096` | 101 | Worker App Service Bicep module + wiring into `platform-controlplane.bicep` (sitecontainer for sidecar, slotless per DS-3) |
| `29e9e2396` | 107 | `attempt` field in `HandlerEnvelope` + MessageId hash + `HandlerRetryAttempts` dict on ProvisioningRun (defeats L1-dedup-kills-§4C-retry latent defect) |
| `4302c45df` | 115 | Sidecar CI workflow YAML drafted as coord-note (escalated closed coordination window — window was CLOSED, worktree DORMANT) |
| `8281045052` | **Governance** | ci-cd-r1 coord window declared expired by owner — r1 formally owns `.github/workflows/**` for Phase C'' scope. 3 queued coord-notes applied (067/088/115): sdap-ci.yml tenant-isolation job + naming-conformance + nightly-health.yml graph parity + NEW `build-provisioning-sidecar.yml`. r1 CLAUDE.md + `projects/INDEX.md` updated |
| `87da541f3` | 116 | BFF artifact publish CI extension — `deploy-bff-api.yml` extended with 11 new steps (buildId + 3 r3 gates + artifact zip + OIDC ACR login + blob push + manifest + red-gate check) + JSON schema for `latest.json` |
| `f1763c489` | 117 | Bicep→ARM-JSON pre-compile CI — new `publish-provisioning-arm-artifacts.yml` (compiles `customer.bicep` + `stacks/model1-shared.bicep`, blob-publishes with manifest). **actionlint downloaded and used** |
| `8edc72d66` | **auth-v4 coord** | Response to auth-v4's ADR-028 A4+E-3 change request (FIC adoption for BFF-OBO). Applied split: spec.md:236 MUST scoped to Model 1 (shared multitenant app-reg) vs Model 2 (per-customer app-reg + FIC). New FR-39 (pluggable secret/FIC) + FR-40 (invariant I6 Model-1-only, ArchTest-enforced). R23 closed with corrected MI-as-issuer vs MI-as-recipient cap analysis. design.md v3.4 → v3.5. POMLs 125/126/130/142 amended for FIC pluggability. Response coord-note at `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` |
| `a0b1f9d26` | 103 | Keyed DI catalog for 20 dispatchable HandlerIds + `HandlerRegistrationCompletenessTests` (WebApplicationFactory<Worker.Program> asserts all 19 resolve). **Caught 2 real DI bugs** (H5 + H7) that unit tests missed. 729/729 tests pass |
| `66b1911e8` | 104 | Extract `IHandlerOutcomeApplier` from `StateReconcilerService` + shared `ReconcilerEnvelopeBuilder`. **Bonus**: DS-2 §5 guard-release gap closed (wires `ICustomerRunGuard.ReleaseAsync`). 736 tests pass (+7) |
| `8e5163dfa` | 109 | Bicep C5.1-C5.3 config-key drift fixes for Api side (`Cosmos__AccountEndpoint`, `ServiceBus__FullyQualifiedNamespace`, etc.); `dev.bicepparam` B1→P1v3 per auth-v4 §8 item 4; ARM JSON regenerated (was stale since task 033) |
| `1a9d23914` | 110 | 🟡 SB Data Sender + Receiver RBAC role assignments in Bicep (cross-RG pattern from task 108) + folded-in Worker-Bicep C5.1 fix. Live grant deferred to Deploy-ControlPlane.ps1 |
| `6a54e09d2` | 113 | 🟡 `Deploy-ControlPlane.ps1` (1074 lines, PSScriptAnalyzer clean, `-WhatIf`-exercised, real `dotnet publish` produced 10.01 MB zip). **Bonus fix-at-discovery**: `.Api` had no `/healthz` route (platform health probe 404-ing since first deploy per task-033 Bicep) — added + tested |
| `2b6250aa6` | 102 | **`ProvisioningHandlerDispatcher`** — the load-bearing execution engine (748 lines). `ServiceBusSessionProcessor` (NOT plain), `SessionId=CustomerId`, `MaxConcurrentCallsPerSession=1` freeze-tested TWICE. Contract test uses reflection on `assembly.GetReferencedAssemblies()` (stronger than grep). New `Microsoft.Extensions.Caching.StackExchangeRedis 10.0.9` NuGet (Wave G-1's sole new dep). 740/740 tests pass |
| `c56be622d`+`220ec3023` | 118 | Dispatch spine integration seam test — arrange in-memory Worker DI, register canary handler under REAL "H1" HandlerId (so `DagAdvancer` genuinely computes H2a as next), assert Cosmos test-double receives outcome. Regression coverage explicitly maps to tasks 102/103/104/106. 742/742 tests pass (+2) |
| `89f2d60fa` | 105 | Real Redis `DispatchIdempotencyService` (swaps NoOp) + 4 new test files (36 new tests). **Self-corrected 2 ADR-038 violations during Step 9.5** + **caught silent-fail design gap** (Redis fallback silently degraded to same-instance-only in prod; fixed via `IHostEnvironment` throw). **787/787 tests pass** (+47). Dispatch-namespace test count: 76 |

### Wave G-1 tally

- **17 of 17 tasks ✅** (14 fully-complete ✅ + 3 authoring-complete-live-pending 🟡: 108, 110, 113)
- **20 substantive commits** landed on PR #779
- **787/787 tests pass** on the L2 test suite (started at ~618 pre-Wave-G; net +169 new tests, zero regressions)
- **Zero code-review criticals**, **zero ADR violations** across all quality gates
- **The execution engine is in the tree**: dispatcher + reconciler + crash recovery + outcome applier + keyed handler resolution + Redis L2 idempotency + Path X registry client + sidecar image + queue Bicep + config-key alignment + deploy script + integration seam test + freeze tests

---

## 🎯 CRITICAL FRAMING FOR NEXT SESSION

Phase C'' delivers r1's stated E2E goal. Wave G-1 (foundation + execution engine) is now done. Two things remain:

1. **Wave G-1 LIVE CEREMONY** — 7 owner-in-the-loop operations against live Azure/Dataverse that Wave G-1 tasks correctly refused to execute unilaterally. Enumerated below.
2. **Waves G-2 through G-7** — 39 tasks remaining. Wave G-2 is 7 tasks (SDK ports for H0/H1/H0.5/H2a/H2b + H4 split). See `notes/design-study-ds4-handler-audit.md` §4 for full wave sequencing.

**These two tracks are LARGELY INDEPENDENT**. Wave G-2's handler SDK-port work doesn't require the live ceremony to have completed — the ports author + test code against SDK contracts, not live Azure. So next session can:

- **Path A (safest)**: Live ceremony first, then Wave G-2
- **Path B (parallel-friendly)**: Dispatch Wave G-2 in parallel with owner running the live ceremony
- **Path C (recommended if owner isn't ready to babysit)**: Dispatch Wave G-2 now; queue the live ceremony for when owner is available

Owner call at session start.

---

## Wave G-1 LIVE CEREMONY Backlog (owner-in-the-loop, ordered)

### 1. Queue delete-and-recreate (per task 108's runbook)

**Prerequisite** (already met): task 107's `attempt` field is committed (`29e9e2396`); will deploy via task 113's `Deploy-ControlPlane.ps1`. Turning on dedup without `attempt` in the MessageId hash would silently swallow every §4C retry within PT1H.

**Runbook**: [`notes/queue-recreate-runbook-2026-08.md`](notes/queue-recreate-runbook-2026-08.md)

**Steps**:
1. Peek queue: `az servicebus queue show -n sprk-provisioning-jobs --namespace-name spaarke-servicebus-dev -g SharePointEmbedded --query "{active:countDetails.activeMessageCount,dlq:countDetails.deadLetterMessageCount}"`
2. Task 108 verified 0/0 on 2026-08-19. If non-zero when you run, drain first per runbook §3.
3. Delete: `az servicebus queue delete -n sprk-provisioning-jobs --namespace-name spaarke-servicebus-dev -g SharePointEmbedded`
4. Recreate via Bicep (**note**: `platform-controlplane.bicep` has `targetScope='subscription'`, so use `az deployment sub create`, NOT `group create`; use the new `parameters/platform-controlplane-dev.bicepparam` file created 2026-08-19 fix-at-discovery — NOT `stacks/dev.bicepparam` which targets `model2-full.bicep`): `az deployment sub create --location westus2 --template-file infrastructure/bicep/platform-controlplane.bicep --parameters infrastructure/bicep/parameters/platform-controlplane-dev.bicepparam`
5. Verify: `az servicebus queue show ... --query "{sess:requiresSession,dup:requiresDuplicateDetection}"` → both `true`
6. Flip TASK-INDEX row 108 🟡 → ✅

### 2. Pre-Grant-ControlPlaneIdentity fix (small code fix)

**Discovered by task 113**: `az ... | Select-Object -First 1` leaves `$LASTEXITCODE` stale/null → false "Azure CLI not found" preflight failures. Same pattern exists **unfixed** in `Grant-ControlPlaneIdentity.ps1` (task 111). Back-port task 113's fix pattern before step 3.

**Fix pattern**: set `$LASTEXITCODE = 0` before every `az` call, OR capture output to a variable then check `$LASTEXITCODE` before piping.

### 3. Grant-ControlPlaneIdentity.ps1 exec against spaarkedev1

**Script**: [`scripts/provisioning/Grant-ControlPlaneIdentity.ps1`](scripts/provisioning/Grant-ControlPlaneIdentity.ps1) (task 111 `eefad966e`)

**Prerequisite**: operator has Dataverse System Administrator role on `spaarkedev1.crm.dynamics.com` (bootstrap is not self-serviceable).

**Steps**:
1. `az login` with account having Dataverse SysAdmin on `spaarkedev1`
2. Dry run: `.\scripts\provisioning\Grant-ControlPlaneIdentity.ps1 -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2 -AdminEnvUrl "https://spaarkedev1.crm.dynamics.com" -L2UamiClientId "965a4a01-01e1-442b-97a6-6a98308018b3" -DryRun`
3. Review dry-run output
4. Execute: same command minus `-DryRun`
5. Verify: `WhoAmI` returns the new systemuser; role association returns 200

### 4. Provisioning-artifacts storage account bootstrap (for tasks 116 + 117 blob push)

**Escalation from tasks 116 + 117**: workflows can't push artifacts to `provisioning-artifacts` blob container because storage account doesn't exist in Bicep. Workflows fail cleanly today (don't break existing deploy).

**Steps**:
1. Author Bicep for a platform storage account (probably new `modules/provisioning-artifacts-storage.bicep` invoked from `platform-controlplane.bicep`)
   - Resource: `Microsoft.Storage/storageAccounts` with `provisioning-artifacts` container
   - RBAC: grant `Storage Blob Data Contributor` to the OIDC SP used by `deploy-bff-api.yml` + `publish-provisioning-arm-artifacts.yml` (grep `.github/workflows/` for the SP name/OIDC federation config)
2. Deploy the Bicep
3. Set repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` to the new storage account name
4. Trigger workflows (dispatch) to confirm end-to-end artifact publish

**Note**: this is effectively a new small task not on the DS-4 wave plan. Could be numbered 118.5 or folded into Wave G-3's H9 (task 132) prerequisites.

### 5. Deploy-ControlPlane.ps1 live execution (deploys Wave G-1 code + Bicep)

**Script**: [`scripts/provisioning/Deploy-ControlPlane.ps1`](scripts/provisioning/Deploy-ControlPlane.ps1) (task 113 `6a54e09d2`)

**This is the meat of the live ceremony** — deploys the entire Wave G-1 platform to `rg-spaarke-platform-dev`.

**Steps**:
1. `.\scripts\provisioning\Deploy-ControlPlane.ps1 -Environment dev -Target All -WhatIf`
2. Review what-if output — last chance to catch anything wrong before mutation
3. `.\scripts\provisioning\Deploy-ControlPlane.ps1 -Environment dev -Target All`
4. Verify: `/healthz` returns 200 on both Api + Worker; `az servicebus queue show` returns sessions+dedup both true; `az role assignment list --assignee <L2-UAMI-principalId>` returns Sender + Receiver

### 6. Post-deploy verification (closes 108/110/113 outstanding acceptance criteria)

Once step 5 completes cleanly:
- Flip TASK-INDEX row 108 🟡 → ✅ (queue live-verified)
- Flip TASK-INDEX row 110 🟡 → ✅ (RBAC live-verified)
- Flip TASK-INDEX row 113 🟡 → ✅ (deploy script live-exercised)

### 7. auth-v4 phase-5 secret retirement coordination (deferred, ongoing)

auth-v4 owns the schedule. r1's obligation: honor the pluggability contract (POMLs 125/126/130/142 amended per commit `8edc72d66`). Not a live ceremony action this session — reminder for Wave-G-3 dispatch: check auth-v4's rollout state before starting task 130 (H3 heavy Graph port with FIC branch).

---

## Wave G-2 Dispatch Plan (independent of live ceremony)

Per DS-4 §4 + auth-v4 amendments per commit `8edc72d66`, Wave G-2 = 7 tasks:

| Task | Model | Rigor | Scope | Notes |
|---|---|---|---|---|
| 120 | Sonnet | FULL | H0 SDK ports (4 `PowerShellPreflightProbe` → `ARM.CognitiveServices` + BAP-REST + `ARM.Compute` + KV) | ~600-800 LOC |
| 121 | Sonnet | FULL | H1 real ARM probe (replaces `NullSubscriptionReadinessProbe` which returns Passed unconditionally) | ~150-250 LOC |
| 122 | Sonnet | STANDARD | H0.5 registry swap (DI-swap onto task 112's real `IDataverseEnvironmentRegistryClient`) | ~50 LOC handler-side |
| 123 | Sonnet | FULL | H2a ARM port (`ArmDeployment.CreateOrUpdateAsync` + `WhatIfAtSubscriptionScopeAsync` + T1 KV-ref read; consumes task 117's ARM JSON artifacts) | ~600-800 LOC |
| 124 | Sonnet | FULL | H2b `SearchIndexClient` port + REAL AI Search tenant-filter template provisioner (replaces `Stub*Provisioner`) | ~400-600 LOC |
| 125 | Sonnet | FULL | H4 SDK port (`SecretClient` + `ARM.AppService` `KeyVaultReferenceIdentity` PATCH + `ARM.Authorization` role assignment) — **AMENDED for auth-v4 FIC pluggability per commit `8edc72d66`** | ~350-500 LOC |
| 126 | Sonnet | FULL | **H4 real-values correctness gate** — replaces `AzCliKvSecretsWriter.ResolveValueForEntry`'s literal `{name}-interim-placeholder-{customerId}` values with real secret generation. **AMENDED for auth-v4 FIC (BFF-API-ClientSecret path may go away per phase 5)** | ~200-400 LOC |

**Dispatch batching** (learned from Wave G-1 coordination):
- **Batch G-2A** (parallel, isolated trees): 120 (H0) + 121 (H1) + 122 (H0.5) — each in own handler folder
- **Batch G-2B** (after G-2A): 123 (H2a heavy) + 124 (H2b) — different handlers, safe parallel
- **Batch G-2C** (after G-2B, or in parallel with G-2A/B if isolated enough): 125 (H4 SDK port) then 126 (H4 real values) — SEQUENTIAL (both touch H4 handler + KV writer)

Rough estimate: Wave G-2 total ~3-5 days of parallel-agent work at Wave G-1's cadence.

---

## Live Azure state (unchanged this session — Wave G-1 was code + IaC only)

### `rg-spaarke-platform-dev` (L2 control plane)

- `spaarke-provisioning-controlplane-dev` App Service — currently running the pre-Wave-G-1 code (Wave G-1 code committed but NOT deployed; live ceremony step 5 deploys it)
- `spaarke-provisioning-controlplane-worker-dev` — DOES NOT EXIST YET (task 101 authored the Bicep; deployed by live ceremony step 5)
- `cosmos-spaarke-platform-dev/spaarke-provisioning/runs` — still has the 1 orphaned ProvisioningRun doc (`65109e91-5968-4300-933e-9e79dea4109c`, customerId `trial-2026-08-18`) from failed test 2026-08-18. Will be dropped when reconciler starts + advances it, or when queue is recreated.
- `sprk-controlplane-dev-kv` — unchanged
- `sprk-controlplane-dev-uami` — UAMI needs live grants per step 3 (Path X App User on `spaarkedev1`)

### `spaarke-servicebus-dev` (in `SharePointEmbedded` RG)

- Queue `sprk-provisioning-jobs` still exists with sessions + dedup OFF (task 108 verified 0/0 messages 2026-08-19). Live ceremony step 1 recreates it.
- L2 UAMI has Sender role (manually granted 2026-08-18); Receiver role NOT YET granted (task 110's Bicep adds it; live ceremony step 5 applies)

### `spaarkedev1.crm.dynamics.com` (admin Dataverse)

- `sprk_dataverseenvironment` record from 2026-08-18: id `87d7b4a7-399b-f111-b8de-7ced8ddc4a05`, sprk_name `trial-2026-08-18`. Still there.
- L2 UAMI NOT YET registered as systemuser (Path X). Live ceremony step 3 registers it.

### Cost

~$110-120/mo on `rg-spaarke-platform-dev` baseline (PremiumV3 App Service Plan). Will grow slightly (~$5/mo for storage account) after live ceremony steps 4 + 5.

---

## Systemic coordination context (governance decisions this session)

### 1. ci-cd-r1 declared expired (commit `8281045052`)

The `ci-cd-unit-test-remediation-r1` worktree owned `.github/workflows/**` for a 28-day coordination window that closed 2026-07-23. Worktree dormant since 2026-06-28 (~52 days). Owner declared window expired 2026-08-19; **r1 formally took ownership of `.github/workflows/**` for Phase C'' scope**. If ci-cd-r1 reactivates, merge-conflict resolution proceeds normally.

Applied: 3 queued r1 coord-notes (067 Graph parity, 088 CI-gate wiring, 115 sidecar build) landed directly to workflows. r1 CLAUDE.md + `projects/INDEX.md` updated.

### 2. auth-v4 coordination (commit `8edc72d66`)

auth-v4 (via `notes/PROVISIONING-CHANGE-REQUEST.md`) is moving BFF confidential credential from client-secret to Federated Identity Credential (FIC) per ADR-028 Amendment A4 + Exception E-3.

**Owner-signed-off split** (2026-08-19):
- Model 1 (shared/SMB): single shared multitenant BFF app-reg (matches live state `AzureADMultipleOrgs`). No per-customer FIC creation.
- Model 2 (dedicated): per-customer BFF app-reg + FIC pointing at shared BFF UAMI.
- **New invariant I6 (FR-40, Model 1 only)**: OBO exchange app-reg MUST be per-tenant-request-context-derived. ArchTest-enforced (same pattern as I1-I5).
- **R23 closed** with corrected MI-as-issuer vs MI-as-recipient cap analysis. r1's Q5 spike was overly conservative.
- **Pluggability contract accepted**: POMLs 125/126/130/142 amended for secret-OR-FIC swappable creation during auth-v4's phased rollout.

Response coord-note at [`notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md).

**Wave G-3 timing**: check auth-v4's rollout state before starting task 130 (H3 heavy Graph port). If FIC creation script has landed in `Register-EntraAppRegistrations.ps1` (auth-v4's §3.2 primary home), task 130 invokes it rather than duplicating logic.

---

## Quality patterns proven this session (worth capturing for future waves)

### 1. Completeness/forcing-function ArchTests catch real defects

- **Task 103's HandlerRegistrationCompletenessTests** (WebApplicationFactory<Worker.Program> constructs real DI): caught H5 + H7 DI-registration defects that unit tests missed. Task 050's H7 agent auto-resumed and fixed it via inter-agent coordination.
- **Task 113's discovery**: `.Api` had no `/healthz` route despite Bicep declaring `healthCheckPath: '/healthz'` since task 033 → platform health probe was 404-ing since first deploy. Fixed at discovery (rigor bumped STANDARD → FULL).
- **Task 102's reflection-based contract test** (`assembly.GetReferencedAssemblies()` vs file-grep): stronger than grep (no false positives from 22 prose-only doc comments referencing IJobHandler by name).
- **Task 118's seam test regression coverage** explicitly mapped: fires on any regression in tasks 102/103/104/106.
- **Task 105's self-correction during Step 9.5 quality gates**: caught 2 ADR-038 violations + 1 silent-fail Redis-fallback design gap (originally would have silently degraded to same-instance-only in prod → fixed via `IHostEnvironment` throw at startup).

Every one of these was worth the engineering cost of building the ArchTests.

### 2. Parallel-agent git-index-race handling

Multiple subagents share the git index. When agent A commits, it can sweep agent B's staged files. Working pattern (proven across ~30 subagents this session):

- Every agent instructed: `git commit --only <specific paths>`, never `git add -A` / `git add .`
- When race happens: sibling agent's file sweep is caught in commit + explicitly cited in commit message. Coordination-only note sent via SendMessage between agents.
- No data loss across the entire session despite ~5 documented sweeps.
- Alternative option available if conflicts worsen: `isolation="worktree"` in Agent tool (creates isolated worktree per agent; ~200-500ms setup + disk cost).

### 3. Live-ceremony vs authoring separation

Consistently applied pattern (task 089 established, tasks 108/110/113 followed):
- Authoring-half completes as ✅ OR 🟡 (if acceptance criteria have live-only checks)
- Live-ceremony half deferred to grouped operator run
- POML `<status>` = `authoring-complete-live-*-pending`
- TASK-INDEX row = 🟡 with inline note pointing at runbook

Keeps subagents safe from destructive live mutations while operator retains full control.

### 4. Coord-note application → direct-authoring shift

Governance discovered mid-Wave-G-1: coord-notes queued for months against a dormant worktree = blocked E2E. Owner declared expiry → r1 took ownership of the surface. Pattern replicable if similar ownership impasses arise on other cross-worktree surfaces.

---

## What's NOT changing (reassurance)

- Handler catalog H0-H14 (19 top-level + 3 sub-handlers = 22 classes total): unchanged
- DAG dependencies + gates + §4B trap catalog + §4C rollback + §4D invariants I1-I5: unchanged (I6 added Model-1-only)
- Tenancy model (D3): unchanged
- The 7 hard-required H7 env vars: unchanged
- The 8 authoritative solutions: unchanged
- Canonical naming (FR-35/36): unchanged
- Path X for L2's own Dataverse creds (FR-38, DS-8): unchanged and distinct from auth-v4's BFF-OBO scope
- Wave G-1 acceptance criteria remaining (108/110/113's live checks): unchanged — pending owner-in-the-loop ceremony

---

## Recovery for a fresh session

If context is refreshed and next session picks up:

1. **Read this file first** (`current-task.md`) — the Quick Recovery table + Wave G-1 tally + owner decision are the essential recovery
2. **Read [`notes/design-study-ds4-handler-audit.md`](notes/design-study-ds4-handler-audit.md)** — the authoritative wave sequencing for Waves G-2..G-7
3. **Check [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)** for row statuses across the 58 Phase C'' tasks (100-186)
4. **Verify git state** — `git status --short` should be clean; `git log --oneline -5` should show `89f2d60fa` at HEAD (or later if new commits have landed)
5. **Ask owner**: Path A (live ceremony first), Path B (Wave G-2 parallel), or Path C (Wave G-2 now, ceremony later)?

Do NOT re-derive Wave A/B decisions — they are locked and committed. Do NOT re-open R23 — closed per auth-v4 §4 analysis.

---

## Files preserved for full context

| File | Purpose |
|---|---|
| `spec.md` v3.5 (post-Batch-1/2/3/addendum/auth-v4 amendments) | Authoritative FR/NFR reference |
| `design.md` v3.5 | Authoritative design reference |
| `plan.md` + `README.md` + this project's `CLAUDE.md` (post-terminology sweep) | IProvisioningHandler-consistent |
| `tasks/TASK-INDEX.md` | 136 tasks (78 original + 58 Phase C''), status current |
| `tasks/100-186-*.poml` | Phase C'' task files (POMLs 125/126/130/142 amended for auth-v4 FIC pluggability) |
| `notes/design-study-ds*.md` (9 files) | Wave A + Wave B design studies |
| `notes/r1-gap-analysis-2026-08-18.md` | Original gap analysis |
| `notes/design-study-ds6-spec-design-amendment-text.md` | Ready-to-apply amendment text (already applied) |
| `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` | r1's response to auth-v4's change request |
| `notes/PROVISIONING-CHANGE-REQUEST.md` | auth-v4's change request (APPLIED banner added) |
| `notes/queue-recreate-runbook-2026-08.md` | Task 108's live queue recreate runbook |
| `notes/h9-artifact-publish-ci-coord-pr.md` | Task 116's design record |
| `notes/h2a-bicep-precompile-ci-coord-pr.md` | Task 117's design record |
| `notes/sidecar-ci-workflow-coord-pr.md` | Task 115's coord-note (APPLIED banner added) |
| `notes/graph-app-role-parity-coord-pr.md` | Task 067's coord-note (APPLIED) |
| `notes/phase-h-ci-wiring-coord-pr.md` | Task 088's coord-note (APPLIED) |
| `notes/task-*-deviations.md` (multiple) | Per-task deviation records |
| `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` | Path X grant script (task 111) |
| `scripts/provisioning/Deploy-ControlPlane.ps1` | L2 deploy script (task 113) |
| `src/server/services/Sprk.Provisioning.ControlPlane.*` | L2 code split into `.Core` + `.Api` + `.Worker` + `.Sidecar` + `.Tests` |
| `.github/workflows/sdap-ci.yml` (updated) + `nightly-health.yml` (updated) + `deploy-bff-api.yml` (updated) + NEW `build-provisioning-sidecar.yml` + NEW `publish-provisioning-arm-artifacts.yml` | r1-owned CI workflows per governance shift |

---

*Wave G-1 complete. Ready for owner direction on live ceremony vs Wave G-2 dispatch (or both in parallel).*
