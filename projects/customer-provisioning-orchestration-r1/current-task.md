# Current Task State — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-18 evening (Phase F acceptance sprint session ended with **incomplete E2E**; next session = fresh Fable-model current-state-vs-required-state gap analysis)
> **Working directory**: `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` — commits landed today: `78a50edf3`, `17063669b`, `5b908591a`, `d39dd926d`, `b70e83504`, `1d9a89a4e` (all pushed). 4 more uncommitted L2-code-fix files in worktree pending handoff commit.
> **PR**: https://github.com/spaarke-dev/spaarke/pull/779 (DRAFT — DO NOT MERGE)

---

## Active task: 102 — ✅ COMPLETE (2026-08-19)

| Field | Value |
|-------|-------|
| **Task** | 102 — Implement ProvisioningHandlerDispatcher BackgroundService (ServiceBusSessionProcessor) in .Worker |
| **Rigor** | FULL (opus @ xhigh, POML-declared) |
| **Status** | ✅ completed |
| **Phase** | C'' Wave G-1 — Foundation |
| **Consumers already committed** | task 103 keyed DI `a0b1f9d26` · task 104 IHandlerOutcomeApplier extract `66b1911e8` · task 107 HandlerEnvelope.Attempt `29e9e2396` |
| **Files authored** | `.Core/Dispatch/{DispatcherOptions,DispatchDecision,DispatchModule,IDispatchIdempotencyService,NoOpDispatchIdempotencyService}.cs` (5) · `.Worker/Dispatch/ProvisioningHandlerDispatcher.cs` · `.Worker/Program.cs` (+2 lines block) · `.Core.csproj` (+Redis NuGet) · `.Worker.csproj` (+InternalsVisibleTo) · `.Tests/Dispatch/ProvisioningHandlerDispatcherInvariantTests.cs` |
| **New NuGet** | `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.9 on `.Core.csproj` — Wave G-1 sole new dep |
| **Freeze test 1 (MaxConcurrentCallsPerSession == 1)** | ✅ PASS — `built.MaxConcurrentCallsPerSession.Should().Be(1)` at test line 101 |
| **Contract test (zero .Core/.Worker → BFF ref)** | ✅ PASS — reflection on `assembly.GetReferencedAssemblies()` for both assemblies |
| **Build state** | dotnet build 0 warnings / 0 errors · dotnet format compliant on task-102 files |
| **Test state** | 740/740 L2 tests pass · 4/4 new invariant tests pass · 0 regressions |
| **Quality Gates (Step 9.5)** | code-review ✅ (0 critical, 0 warn, 3 non-blocking suggestions) · adr-check ✅ (0 violations) |
| **TASK-INDEX row 102** | ✅ (flipped from 🔲) |
| **Push status** | NOT PUSHED per parent instructions |
| **Next action** | Commit + report to parent |

---

## 🎯 CRITICAL FRAMING FOR NEXT SESSION (READ THIS BEFORE ANY WORK)

**The r1 project's stated goal is E2E customer provisioning (spec FR-18, SC #5, design.md §15 north-star). Today's Phase F acceptance attempt REVEALED THAT THIS GOAL IS NOT MET.** ~75/78 tasks are ✅ by task count but a load-bearing runtime component is missing.

**Owner directive at session close** (verbatim): *"my concern is that we have not fully fleshed out what is required to meet the project objectives, or to introduce suggestions such as extending the bff.api. we need to do a full current-state v required state using Fable-model to determine what is actually required to complete this project"*

**Next session's ONLY job**: fresh, rigorous **current-state vs required-state gap analysis using Fable-model**. Do NOT do reactive fixes. Do NOT suggest architectural changes (like "extend BFF") without grounding them in that analysis. Do NOT invoke the skill or run more provisioning attempts until the required-state has been rigorously mapped.

**What this session got WRONG**:
- Suggested extending BFF.API to consume the Service Bus queue as a fix for the dispatcher gap. This was reactive — **BFF is the operational customer-facing API, NOT the provisioning executor. Handlers ARE in L2. BFF has no role here.**
- Framed "wrap-up as acceptance win" when actually r1 didn't deliver the stated E2E goal. The dispatcher gap is the acceptance finding; wrapping without addressing it = accepting the project failed to meet stated objectives.
- Made 4 real L2 code fixes today but they were fixes to parts that DO exist. The bigger gap is a whole subsystem (wave C5 dispatcher) that was DESIGNED but never BUILT.

---

## Quick Recovery (READ THIS AFTER FRAMING)

| Field | Value |
|-------|-------|
| **r1 stated goal** | E2E provisioning: fresh customer stamp reaches `Setup Status = Ready` via new pipeline (spec FR-18 / SC #5 / plan.md Phase F). |
| **Actual delivered state** | Machinery **up to** the queue works (POST /api/runs → Cosmos persist → SB enqueue). **Nothing consumes the queue.** No handlers execute. No customer environment gets provisioned. |
| **The load-bearing gap** | **Wave C5 dispatcher is missing.** Per L2 code comment (`Handlers/IProvisioningHandler.cs`): *"In wave C4, no dispatcher exists yet — planned wave C5 [will pull] a HandlerEnvelope off the fleet-scoped Service Bus queue via a BackgroundService drain loop."* Wave C5 was never built. `grep -rn "ServiceBusProcessor\|MessageReceived\|ProcessMessageAsync" src/server/services/Sprk.Provisioning.ControlPlane/ --include=*.cs` returns ZERO .cs matches. |
| **Additional gap** | Handler CLASSES exist in L2 (`Sprk.Provisioning.ControlPlane/Handlers/*/H*Handler.cs`) but per task 055 comments, trap/invariant verifiers ship as PLACEHOLDERS returning `InfraFault`. Same may apply to some other handler impls — needs per-handler audit. |
| **Second gap** | `StateReconcilerService` (BackgroundService that polls Cosmos + advances DAG) was authored (task 058) but never registered in `Program.cs`. Only `CrashRecoveryStartupService` gets `AddHostedService`. |

---

## What was actually accomplished this session (in order)

### 1. Wave 4 finalization + push (BEFORE dispatcher-gap discovery)

- Task 075 (`/provision-environment` L3 skill scaffolding) landed 78a50edf3
- Task 076 (fallback matrix) landed 78a50edf3
- Task 081.5 (RegistrationDataverseService ctor refactor + 4-config-gap fix on spaarke-bff-dev) landed 17063669b
- Task 082 (delete `DemoProvisioning__Environments__*` from BFF Azure config) landed 5b908591a
- 089 harness scaffolding subagent output (POML amendment + verification harness + report skeleton + operator runbook) landed b70e83504
- Handoff commit d39dd926d
- Bicep fix + running-notes commit 1d9a89a4e

### 2. L2 deployment sprint (attempted E2E) — MULTI-STEP RECOVERY

- **L2-0**: Created L2 AAD app-reg `spaarke-provisioning-controlplane-dev` (appId `70ba7b19-8969-47e5-a508-efe621dea1a4`); identifier URI `api://spaarke.com/provisioning-controlplane-dev` (tenant policy forced verified domain); app-roles Operator + Reader; SP objectId `583b34f7-6992-4a93-83d1-99296531325c`; Azure CLI SP created + delegated user_impersonation consent granted; Operator role assigned to owner (oid `c74ac1af-ff3b-46fb-83e7-3063616e959c`).
- **L2-1**: Applied `platform-controlplane.bicep` to new `rg-spaarke-platform-dev`; created 7 resources (UAMI `sprk-controlplane-dev-uami` principalId `38f7693f-e6e2-4a3e-9acf-7f9e29dd4044` clientId `965a4a01-01e1-442b-97a6-6a98308018b3`; App Service Plan PremiumV3 `spaarke-controlplane-dev-plan`; Log Analytics `sprk-controlplane-dev-logs`; Cosmos `cosmos-spaarke-platform-dev`; App Insights `sprk-controlplane-dev-insights`; KV `sprk-controlplane-dev-kv`; App Service `spaarke-provisioning-controlplane-dev` + staging slot). Bicep fix needed: deprecated `retentionPolicy` on `diagnosticSettings` (fix in commit 1d9a89a4e).
- **L2-1.5**: Seeded L2 KV with SB conn (copied from `spaarke-spekvcert`) + dummy `Dataverse-ClientSecret` (r3 dropped the real secret). Required owner-granted Key Vault Secrets Officer role (role assignment `e11d5f92-0cbd-4913-aca9-c62d7d3d7aa3`).
- **L2-2**: Published + deployed `Sprk.Provisioning.ControlPlane` (6.57 MB).
- **L2-3**: 4 config-key naming mismatches between Bicep + L2 code discovered via `LogFiles/StartupLogs/*_failure.log`; fixed with app-setting aliases (`Cosmos__AccountEndpoint`, `Cosmos__DatabaseName`, `Cosmos__ContainerName`, `ServiceBus__FullyQualifiedNamespace`, `ManagedIdentity__ClientId`).
- **L2-4/5**: `/ping` = 200; role-probe `POST /api/runs` = 400 (not 403) → auth chain complete.

### 3. Skill drift discovery + fixes

- Skill/POML/runbook authored based on spec (`customerId + tenantId + tenancyModel + profile`) — but actual L2 code requires `environmentId` + uses different `profile` enum values (`spaarke-hosted-model2` etc.) + different URLs (`spaarke-provisioning-controlplane-dev` not `spaarke-provisioning-dev`) + different audience (`api://spaarke.com/provisioning-controlplane-dev` per tenant policy).
- Minimum-viable skill fix applied: URL + audience replaced in `.claude/skills/provision-environment/SKILL.md` (**uncommitted**).
- Other skill drift NOT yet fixed: profile enum values, environmentId collection, prerequisite `sprk_dataverseenvironment` record creation.

### 4. Live invocation attempt — 4 real L2 code bugs discovered + fixed

Each fix required rebuild + redeploy cycle (~10-15 min each).

1. **Bug #17**: `ServiceBusHandlerEnqueuer` implements `IAsyncDisposable` only, not `IDisposable`. DI container can't dispose in sync scope teardown → 500 on every POST. **Fix committed to working tree** in `src/server/services/Sprk.Provisioning.ControlPlane/Enqueue/ServiceBusHandlerEnqueuer.cs` (added `IDisposable` interface + `Dispose()` bridging to `DisposeAsync()`) — **UNCOMMITTED**.
2. **Bug #18** (documented; not yet fixed as code): `dataverseClientSecretName` Bicep default `Dataverse-ClientSecret` binds a secret that r3 task 060 dropped. Worked around by seeding a dummy KV value.
3. **Bug #19**: `ProvisioningRun.Ttl` field serializes as `"ttl": null` on Cosmos write; Cosmos rejects (only accepts positive int or -1). **Fix in worktree** in `Models/ProvisioningRun.cs` — TTL property removed with comment explaining why (container-level TTL applies globally); **UNCOMMITTED**.
4. **Bug #20**: `ProvisioningRun.RunId` uses `[JsonPropertyName("id")]` (System.Text.Json) but Cosmos SDK uses Newtonsoft.Json serializer by default — STJ attribute ignored on Cosmos write path. **Fix in worktree** in `Models/ProvisioningRun.cs` — added `[Newtonsoft.Json.JsonProperty("id")]` alongside; **UNCOMMITTED**.
5. **Bug #21**: L2 UAMI missing `Azure Service Bus Data Sender` role on `spaarke-servicebus-dev`. Bicep created L2's own resources but didn't grant RBAC on the existing env-scoped SB namespace. Owner-granted (role assignment `2efad74b-cc0b-4ffc-920e-6b39c161adcd`).
6. **Bug #22**: Service Bus queue `sprk-provisioning-jobs` doesn't exist. Created via `az servicebus queue create`.
7. **Bug #23** (documented; not yet fixed as code): `StateReconcilerService` (task 058) never registered in `Program.cs` — only `CrashRecoveryStartupService` gets `AddHostedService`.
8. **THE BIG ONE — architectural gap**: Wave C5 dispatcher (`ServiceBusProcessor` that drains `sprk-provisioning-jobs` + invokes handlers by HandlerId) **DOES NOT EXIST IN L2**. Was DESIGNED per code comments in `Handlers/IProvisioningHandler.cs` but NEVER BUILT.

**Post-all-fixes state**: `POST /api/runs` returns 202 with a runId; ProvisioningRun document written to Cosmos; H0 envelope enqueued to `sprk-provisioning-jobs` (verified via `az servicebus queue show` — 1 active message). Run polls at `status: NotStarted` indefinitely. Reconciler doesn't advance (not registered). No consumer drains queue (dispatcher missing).

---

## Current live Azure state (NOT in git; all reversible)

### `spaarke-bff-dev` — BFF (from earlier today's work)

Post-task 081.5 + 082:
- New app settings: `DATAVERSE_URL`, `PublicConfig__BffUrl`, `PublicConfig__MsalClientId`, `PublicConfig__TenantId`, `Onboarding__EnableDevBypass=true` (5 total)
- Deleted app settings: 9 legacy `DemoProvisioning__Environments__*` + `DemoProvisioning__DefaultEnvironment` keys
- Live BFF: refactored (no `[Obsolete]` fallback), 44.96 MB, `/healthz` = 200
- Reversibility: snapshot preserved at `projects/customer-provisioning-orchestration-r1/notes/phase-e-config-snapshot.json`

### `rg-spaarke-platform-dev` — L2 (new today; ~$110-120/mo baseline running)

- `spaarke-provisioning-controlplane-dev` App Service (PremiumV3 plan) — L2 code deployed; `/ping` = 200; `POST /api/runs` accepts + persists but downstream consumer missing
- `cosmos-spaarke-platform-dev/spaarke-provisioning/runs` — has 1 ProvisioningRun doc (id `65109e91-5968-4300-933e-9e79dea4109c`, customerId `trial-2026-08-18`) sitting at NotStarted
- `sprk-controlplane-dev-kv` — has `ServiceBus-ConnectionString` (copied from `spaarke-spekvcert`) + dummy `Dataverse-ClientSecret`
- `sprk-controlplane-dev-uami` — UAMI with KV Secrets User, Cosmos DB Built-in Data Contributor (via Bicep) + Azure Service Bus Data Sender on `spaarke-servicebus-dev` (owner-granted this session)
- App Insights + Log Analytics collecting L2 traces
- **Teardown command** (if project cancelled): `az group delete --resource-group rg-spaarke-platform-dev --yes --no-wait` (removes ~$110/mo). Also `az ad app delete --id 70ba7b19-8969-47e5-a508-efe621dea1a4` for the app-reg.

### `spaarke-servicebus-dev` (in `SharePointEmbedded` RG)

- New queue `sprk-provisioning-jobs` created (1 active message from today's test run, will grow if any more POSTs happen)
- New role assignment: L2 UAMI granted `Azure Service Bus Data Sender` (role assignment `2efad74b-cc0b-4ffc-920e-6b39c161adcd`)
- To reverse: `az servicebus queue delete --namespace-name spaarke-servicebus-dev --name sprk-provisioning-jobs --resource-group SharePointEmbedded`; delete the role assignment.

### `spaarkedev1.crm.dynamics.com` — Admin Dataverse

- New `sprk_dataverseenvironment` record: id `87d7b4a7-399b-f111-b8de-7ced8ddc4a05`, sprk_name `trial-2026-08-18`
- To reverse: delete that record via `pac data` or Web API DELETE.

---

## What's UNCOMMITTED in the worktree (will be committed by handoff)

1. `.claude/skills/provision-environment/SKILL.md` — URL/audience minimum-viable fix
2. `src/server/services/Sprk.Provisioning.ControlPlane/Enqueue/ServiceBusHandlerEnqueuer.cs` — added `IDisposable` (bug #17 fix)
3. `src/server/services/Sprk.Provisioning.ControlPlane/Models/ProvisioningRun.cs` — TTL removed + Newtonsoft `[JsonProperty("id")]` added (bugs #19, #20 fixes)
4. `projects/customer-provisioning-orchestration-r1/notes/phase-f-e2e-acceptance-2026-08-18.md` — running-log entries from today

---

## Key evidence files for next session's analysis

| File | Purpose |
|---|---|
| `projects/customer-provisioning-orchestration-r1/notes/phase-f-e2e-acceptance-2026-08-18.md` | Full running log of today's Phase F attempt — every gap discovered, every fix, every decision |
| `projects/customer-provisioning-orchestration-r1/notes/task-081.5-rollback.md` | Earlier today's BFF story (Wave 4 config-gap fix + StartupLogs diagnostic pattern) |
| `projects/customer-provisioning-orchestration-r1/spec.md` | Authoritative spec — FR-18, SC #5, §4.2 handler execution model, §15 north-star |
| `projects/customer-provisioning-orchestration-r1/design.md` v3.3 | Design decisions D1-D20, handler catalog H0-H14, §4B trap catalog, §4C rollback, §4D tenant isolation |
| `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` | Task status — ~75/78 ✅ but the delta between "task done" and "goal met" IS the analysis |
| `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/IProvisioningHandler.cs` | **CRITICAL** — comment header enumerates the wave-C5-dispatcher gap in the author's own words: "no dispatcher exists yet — planned wave C5..." |
| `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` | L2 composition root — check which services actually get `AddHostedService` (StateReconciler is NOT one; only CrashRecoveryStartupService) |
| `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/*/H*Handler.cs` | 15+ handler classes — each needs audit: real impl vs placeholder returning InfraFault |
| `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/StateReconcilerService.cs` | The BackgroundService that polls Cosmos + advances DAG — authored + tested but NOT REGISTERED in Program.cs |

---

## Next session — Fable-model gap analysis brief

**Prompt to fresh session** (approximate — refine as needed):
> Do a rigorous current-state vs required-state gap analysis for the r1 project's stated goal: "fresh customer environment provisioned end-to-end via the new pipeline, reaching sprk_dataverseenvironment.sprk_setupstatus = Ready" (spec FR-18 / SC #5). Do NOT propose fixes yet. Do NOT assume any architectural direction (like "extend BFF" — that was a reactive mistake last session). Just map: (a) what the required state looks like — every runtime component needed for E2E provisioning; (b) what actually exists in code today (grep-verified, not just "task marked ✅"); (c) the gap between them — every missing piece; (d) the delta of what would ACTUALLY need to be built/wired/tested. Use spec.md + design.md as authoritative for required state. Use the L2 code (`src/server/services/Sprk.Provisioning.ControlPlane/`) as authoritative for actual state. Use `notes/phase-f-e2e-acceptance-2026-08-18.md` as evidence of the gaps discovered in today's live invocation attempt. Produce a gap catalog + then let the owner decide direction (build the missing pieces, re-scope, or defer).

**Non-negotiable guardrails for the analysis**:
- Do NOT re-run the invocation. Analyze from artifacts.
- Do NOT propose "extend BFF" or any similar reactive fix without grounding in the required-state map.
- Do NOT invoke the `/provision-environment` skill.
- Do NOT tear down L2 (owner may want state preserved for analysis).
- DO grep the L2 code for every runtime concern (SB consumer, handler dispatcher, state advancement, handler impl real-vs-placeholder).
- DO cross-reference each handler class against its POML task to verify what was scoped vs delivered.

---

## Remaining work (post-Fable analysis, TBD by owner)

- Complete r1's stated E2E goal (dispatcher + placeholder→real handlers + reconciler wiring + tests)
- OR formally re-scope r1 as "control-plane machinery" (accepting E2E is r2 or later)
- OR defer + start fresh project for the dispatcher work
- Task 090 wrap-up follows from that decision
- L2 infrastructure teardown decision (~$110/mo running)

---

## Original context (retained for continuity — pre-Phase-F sections below)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project progress** | ~75/78 tasks ✅ · Wave 4 100% landed |
| **Current phase** | F E2E Acceptance — task 089 in split-approach (scaffolding half ✅ COMPLETE; owner invocation half pending) |
| **Scaffolding subagent** | `aab31dc9d86933b0c` COMPLETE — landed POML amendment + 3 notes files + TASK-INDEX row 089 → 🟡 |
| **Next owner action** | Follow `notes/phase-f-operator-runbook.md`: `az login` → `/provision-environment trial-2026-08-18 --tenancyModel Model2Dedicated --profile trial` → run `notes/phase-f-verification-harness.md` checks against the live stamp → fill `notes/phase-f-report-skeleton.md` into `notes/phase-f-e2e-acceptance-2026-08-18.md` → flip TASK-INDEX row 089 to ✅ → dispatch task 090 |
| **Status** | Ready for owner interactive invocation of `/provision-environment` |

### What just happened (last few hours)

1. Wave 4 tasks 075/076/081.5/082 all landed today across 3 commits (`78a50edf3`, `17063669b`, `5b908591a`) + pushed to PR #779
2. Task 081.5 (`RegistrationDataverseService` ctor refactor) had a dramatic recovery: subagent's deploy failed with container exit 134, misdiagnosed as deploy-script flakiness; main-session took over, found actual cause via `LogFiles/StartupLogs/*_failure.log` (4 missing Tier-1 IOptions from Wave 4 tasks 042/087 never rolled out to dev), fixed the 4 config gaps, redeployed successfully
3. Task 082 (Azure config delete + [Obsolete] class cleanup) — subagent completed core work then crashed with API error mid-run; main-session recovered + landed the commit
4. Owner decided Model 2 dedicated as primary acceptance path (Path A exception per CLAUDE.md §6.5 — POML originally had Model 1 mandatory)
5. Scaffolding-half subagent (`aab31dc9d86933b0c`) landed the Path A exception into `089-phase-f-e2e-acceptance.poml`, authored `notes/phase-f-verification-harness.md` (per-trap T1-T6 + per-invariant I1-I5 + naming + cost verification commands), `notes/phase-f-report-skeleton.md` (owner fill-in template), and `notes/phase-f-operator-runbook.md` (step-by-step `/provision-environment` wrapper). No live Azure/Dataverse mutation performed by the subagent (scope-compliant). TASK-INDEX row 089 → 🟡.

### Azure state (spaarke-bff-dev) — NOT in git

- 5 new app settings added: `DATAVERSE_URL`, `PublicConfig__BffUrl`, `PublicConfig__MsalClientId`, `PublicConfig__TenantId`, `Onboarding__EnableDevBypass=true`
- 9 legacy `DemoProvisioning__Environments__*` + `DemoProvisioning__DefaultEnvironment` app settings deleted
- Live BFF: refactored (no `[Obsolete]` fallback), 44.96 MB, `/healthz` = 200
- Rollback snapshot preserved at [`notes/phase-e-config-snapshot.json`](notes/phase-e-config-snapshot.json)

### Lessons learned to capture in task 090 wrap-up

- **Governance-gap**: root CLAUDE.md §10 BINDING pre-check protects KV secret RENAMES but NOT new app-setting REQUIREMENTS. When code adds `services.AddOptions<X>().ValidateOnStart()`, no CI check verifies dev/staging/prod App Services have the corresponding settings. Add "Tier-1 IOptions deploy checklist" to `.claude/constraints/bff-extensions.md`.
- **Diagnostic pattern**: BFF container exit 134 → ALWAYS pull `LogFiles/StartupLogs/{yyyy_mm_dd}_{instance-id}_failure.log`, NEVER `LogFiles/{date}_docker.log`. Docker log only shows platform events, not app stdout.
- **Fix-drift-at-discovery reinforced**: the 4-config-gap discovery could easily have been deferred ("run 082 with the workaround; fix config later") — that path leads to accumulated drift. Owner reinforced principle by choosing "Recommended" fix on the spot.
- **Split-approach for Phase F acceptance** is worthwhile — subagent handles the machinery-authoring; owner in-the-loop for real Azure mutations.

### Remaining work (after 089 subagent + owner invocation)

- Task 090 wrap-up: `/test-diet` + lessons-learned (governance-gap, diagnostic pattern, fix-drift reinforcement) + README status flip + INDEX.md row update + `repo-cleanup` archive
- Optional: teardown Model 2 trial stamp after acceptance verified (`az group delete --resource-group rg-trial-2026-08-18-*` + Dataverse env decommission)

---

---

## ✅ TASK 081.5 RESOLVED (2026-08-18, supersedes escalation below)

**Task 081.5 is COMPLETE.** The subagent's escalation-record below is preserved for the postmortem but is **superseded** — main-session diagnosed + fixed + retried successfully.

**What actually happened**: the subagent's root-cause hypothesis ("deploy script's fallback path is flaky") was wrong. Real cause: 4 missing Tier-1 IOptions on `spaarke-bff-dev` from Wave 4 tasks 042 (Onboarding) and 087 (PublicConfig) that were never rolled out to dev App Service — the subagent's deploy was the first Wave 4 BFF deploy to live dev and surfaced all latent config gaps at once. The subagent missed the actual `OptionsValidationException` because it only pulled `LogFiles/docker.log` (container-orchestration events) not `LogFiles/StartupLogs/{date}_failure.log` (actual .NET stdout with the stack trace).

**Main-session recovery** (2026-08-18 ~15:50-16:04 UTC):
1. Diagnosed: pulled `StartupLogs/*_failure.log` via SCM VFS → found `OptionsValidationException` enumerating 4 missing settings
2. Applied 4 config values on `spaarke-bff-dev`: `PublicConfig__BffUrl`, `PublicConfig__MsalClientId`, `PublicConfig__TenantId`, `Onboarding__EnableDevBypass=true`
3. `az webapp restart` → `/healthz` recovered to 200 in 53s
4. Republished (44.96 MB) + deployed refactored code via direct `az webapp deploy` → `RuntimeSuccessful`, 0 failed
5. Post-deploy `/healthz` = 200 on first poll → confirmed refactor works end-to-end

**Task 082 is now UNBLOCKED.** The `[Obsolete]` fallback branch is no longer in production; grep of live source confirms zero remaining consumers of `_options.Environments` / `_options.DefaultEnvironment`.

**Full postmortem**: [`notes/task-081.5-rollback.md`](notes/task-081.5-rollback.md) POSTMORTEM section (main-session appended after root-cause fix).

**Governance-gap follow-up** identified for task 090 wrap-up: root CLAUDE.md §10 BINDING pre-check protects KV secret RENAMES but has no equivalent gate for new app-setting REQUIREMENTS introduced by new Tier-1 `IOptions`. When code adds `services.AddOptions<X>().ValidateOnStart()`, no CI check verifies the corresponding App Service settings are populated. Recommend adding a "Tier-1 IOptions deploy checklist" to `.claude/constraints/bff-extensions.md`.

**Also learned**: when a BFF container fails startup with exit 134, ALWAYS pull `LogFiles/StartupLogs/{date}_failure.log` (not `LogFiles/{date}_docker.log`) — the StartupLogs directory has the actual .NET stdout with the boot exception stack trace.

TASK-INDEX.md row 081.5 = ✅.

---

## ⚠ TASK 081.5 ESCALATION (2026-08-18 — subagent record, superseded by RESOLVED section above)

**Task 081.5** (`RegistrationDataverseService` ctor refactor, unblocks 082) is **NOT complete**. The code refactor is done and fully validated (build 0/0, 10,484/0 BFF unit tests pass, zero HIGH CVEs, publish size unchanged at 44.96 MB — reapplied to the working tree, **uncommitted**), but the **deploy of the refactored code failed** at Step 8: the App Service container crashed on startup (`exit code 134`, ~12-13s) via `scripts/Deploy-BffApi.ps1`'s `stop → Kudu zipdeploy → start` auto-recovery path.

**Critical evidence this is NOT a code defect**: redeploying the ORIGINAL (pre-refactor) code via the identical path failed the exact same way. The site only recovered via a plain `az webapp restart` (not a redeploy). This points to a flake/regression in the deploy script's fallback path itself, not either version of the source.

> **NOTE FROM MAIN-SESSION**: this hypothesis was WRONG — see RESOLVED section above. Actual cause was 4 missing Tier-1 IOptions from Wave 4 tasks 042/087 rolled out for the first time by this deploy. Preserved verbatim below for postmortem trail.

**Current live state (AT TIME OF SUBAGENT ESCALATION — since superseded)**: `spaarke-bff-dev` `/healthz` = 200, running the **original (pre-refactor, still-[Obsolete]) code**. `DATAVERSE_URL` app-setting remains correctly set (harmless either way; required for the eventual retry). Task 082 remains blocked (⏸) — 081.5 did not unblock it.

**Full record**: [`notes/task-081.5-rollback.md`](notes/task-081.5-rollback.md) — timeline, root-cause assessment, recommended next steps (retry-with-live-log-capture vs. investigate the deploy script's fallback path first). Per the POML's "do NOT retry blindly" instruction, no further deploy attempts were made — this needs owner/main-session judgment on which path to take.

TASK-INDEX.md row 081.5 set to 🔄 (needs-retry, not ✅ — per parallel-execution failure-isolation convention).

> **[SUPERSEDED — see RESOLVED section above; row 081.5 is now ✅]**

---

## ⚠ IN-FLIGHT AT HANDOFF (READ FIRST NEXT SESSION)

**Task 082 subagent (`a4e4137017e220ab8`) was DISPATCHED just before this handoff and is still running in background.**

Scope: delete `DemoProvisioning__Environments__*` + `DemoProvisioning__DefaultEnvironment` from dev App Service `spaarke-bff-dev` config; optionally remove `DemoProvisioningOptions` class if grep-safe; verify BFF `/healthz` 200 post-delete. Subagent has BINDING pre-check safety valve.

**On next session start**:
1. Check the agent's completion notification via task ID `a4e4137017e220ab8` (its transcript is under `C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-customer-provisioning-orchestration-r1\5aa5d91f-cd2d-4c24-88ae-ae9649a3fe2f\tasks\a4e4137017e220ab8.output` — DO NOT read it via shell; use SendMessage or check for task-notification).
2. If it completed cleanly, git log will show a new commit — verify + update TASK-INDEX row 082 status if not already done.
3. If it failed or stopped mid-work, review its report + main-session recovers.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project phase** | **Wave 4 Batches 4A-4E: 72-73/78 (92-94%)** depending on task 082 subagent outcome. 5-6 tasks remain for next session. |
| **Task** | 082 in-flight (subagent); 075 + 076 + 089 + 090 pending main-session |
| **Status** | context-handoff — READY for session refresh |
| **Next Action** | (1) Check task 082 subagent outcome. (2) Main-session author task 075 (`/provision-environment` L3 skill) — 6-10 hrs estimated, MAIN-SESSION-ONLY per Sub-Agent Write Boundary. (3) Main-session author task 076 (fallback matrix — small extension of 075). (4) Dispatch task 089 with split-approach (subagent scaffolds report + verification harness; owner runs actual `/provision-environment` invocation). (5) Dispatch task 090 (wrap-up + /test-diet). (6) Final push. |
| **Blocker** | None active. All prior gates GREEN. |
| **L2 project state** | **662/662 tests pass** (was 618; +44 H13 tests) · `dotnet build` 0/0 · TreatWarningsAsErrors=true · CVE clean · State-reconciler + Crash-recovery + I5 guard + §4C rollback + H13 gate all live |
| **BFF state** | **10,491/0 BFF tests pass** (10,477 baseline + 7 ConsentCallback E2E + 7 ConfigContractTests) · 0 warnings · publish size **43.67 MB (Δ +0.03 MB** vs 43.64 baseline; task 086 shrink held through 4E) · CVE clean |
| **ArchTest state** | **65/65 full suite** · I5 broadened to Infrastructure/Auth/** · nightly Graph parity test project added (task 067) · CI coord spec ready for ci-cd-r1 (task 088) |
| **L3 skill state** | **NOT AUTHORED YET** — task 075 is the next main-session priority |
| **KV state (dev)** | Alias collapse complete (task 085 + 086); if task 082 completes cleanly, `DemoProvisioning__*` legacy App Service settings also removed |
| **Master sync** | Feature branch is now **101-103 commits ahead of master** depending on 082 outcome |

---

## Wave 4 Batch 4E — 5 of 8 COMPLETE (2026-08-18) + 1 IN-FLIGHT at handoff

### Landed (7 commits total)

| Sub-task | Commit | Summary |
|---|---|---|
| **4E/pre** Drift-5 Bicep BCP cleanup | `6ff75702a` | 5 Bicep files + platform.json regen. Root cause: task 029 UAMI refactor never migrated stack callers; task 032 refactored around stale call site. Fixed w/ minimal UAMI wiring (`bffUamiName` param + module invocation + 5 `appServicePrincipalId` repoints). Both stacks `az bicep build` exit 0. |
| **4E/1** Task 087 `/config.json` | `adbb11fa2` | BFF endpoint Anonymous + short-cache + strong ETag + `If-None-Match` → 304. Tier-1 IOptions w/ env-aware custom validator (r3 task 061 pattern) — avoided 30-fixture sweep. 2 code-pages migrated (SpaarkeAi + LegalWorkspace); external-spa deferred (coord recipe in D2). BFF 10,484/0. Publish Δ +0.03 MB. |
| **4E/1.5** Task 067 nightly Graph parity | `e8f300b4d` | New NightlyTests project. `GraphAppRoleParityTest.cs` reads GraphAppRoles.cs reflectively + queries UAMI SP appRoleAssignments via Graph SDK v6 with explicit tenantId per §4D I5. Also L2 mirror drift-guard as bonus 2nd Fact. 4 GH Actions secrets contract. Coord spec preserved as detail source. |
| **4E/2** Task 088 CI coord PR spec | `d98d64ec3` | Comprehensive coord-PR spec: 3 sections (naming-conformance step in code-quality + new dedicated tenant-isolation top-level job avoiding continue-on-error swallow + nightly Graph parity in nightly-health.yml). 14 referenced paths verified. Zero workflow-file edits. 067's spec preserved as detail source (Option B). |
| **4E/3** Task 062 load test | `e1959426b` | dotnet custom harness (xUnit + `Parallel.ForEachAsync` + `WebApplicationFactory`) — zero package footprint. 3 scenarios PASS: enqueue p95=9ms (target <100ms), 30-min compressed to 3s (202 in 60ms + 13 polls), reconciler concurrency N=3/5/3 × M=1/10/5 = 70 enqueue calls → 14 distinct MessageIds (dedup ratio 5). 5/5 tests in 29s. |
| **4E/4** Task 055 H13 acceptance gate | `63f843273` | Handler + 6 seams (E2EValidationRunner + TrapVerifier + InvariantVerifier + NamingConformanceChecker + CostEnvelopeChecker + RegistrySetupStatusUpdater). Trap/invariant verifiers ship as PLACEHOLDERS returning InfraFault → live-probes deferred to task 089 (Phase F E2E has live customer stamp access). Wave-C4 placeholder pattern. Cost-drift = advisory-warn by default. Deterministic aggregation priority. L2 662/662 (was 618, +44). |
| **4E/5** Task 078 consent-callback E2E | `e3919a0ef` | Synthetic signed HMAC payload approach (avoids live-tenant + ADR-038 compliant). 4 paths: happy + re-consent no-op + restart from H0 + invalid HMAC 401. Idempotency via `ServiceBusProvisioningEnqueuer.ComputeMessageId` (internal via existing `InternalsVisibleTo`). Fixture-scoped Mock<IDataverseService> mirrors PlaybookByIdIntegrationTestFixture pattern. 7/7 tests in 2s. ADR-clean across 7 ADRs. |

### In-flight at handoff (1)

- **4E/6-alt** Task 082 legacy config delete — subagent `a4e4137017e220ab8` running. Deletes `DemoProvisioning__Environments__*` + `__DefaultEnvironment` from dev App Service; optionally removes `DemoProvisioningOptions` class if grep-safe; verifies BFF /healthz 200. See ⚠ IN-FLIGHT AT HANDOFF section above for recovery instructions.

### Remaining for next session (5 tasks)

- **075** `/provision-environment` L3 operator skill — MAIN-SESSION-ONLY (Sub-Agent Write Boundary; writes to `.claude/skills/`). 6-10 hrs estimated. Reference model: `.claude/skills/deploy-new-release/SKILL.md`. Owner authorized full authoring next session.
- **076** Fallback matrix impl in `/provision-environment` skill (MCP disconnect handling) — MAIN-SESSION-ONLY, small extension of 075.
- **089** Phase F E2E Model 1 acceptance — owner authorized SPLIT approach: subagent authors verification harness + report scaffold + operator runbook; owner (interactive main-session) runs actual `/provision-environment` skill invocation against dev + fills observed results. Preserves E2E integrity + doesn't fake live provisioning.
- **090** Project wrap-up + `/test-diet` — the final task. Marks project Complete, runs `/test-diet` reconciliation per ADR-038, updates README, writes lessons-learned.
- **Final push** — 17+ commits accumulated (last push at `ccd858b7c`). Owner's Decision 3 posture: push at each batch boundary. This handoff would be a valid push point; deferred so owner reviews first if desired.

---

## Wave 4 Batch 4D — COMPLETE (2026-08-18)

### Commit map (10 commits total)

| Sub-task | Commit | Summary |
|---|---|---|
| **4D/1** Task 059 I5 concurrency | `3964bba4c` | 8 new files. `CustomerRunGuard` w/ optimistic Dataverse upsert on `sprk_currentrunid`. 18 new tests. **Fully wired Quarantined reason-code integration** (better than POML minimum). Created `IQuarantineClearService` stub to unblock 061. 5 Path C deviations. |
| **4D/2** Task 060 I6 crash recovery | `869b650ab` | 3 new files + 698-line test suite. `CrashRecoveryStartupService` at boot; scans Cosmos for orphaned runs > `max(2× median, 5min floor)`. **MessageId byte-identity to reconciler** — SB level-1 dedup collapses startup-scan + first 5s reconciler tick to ONE msg. 24 new tests, 65 Reconciler-scope pass. Absorbed 059's Program.cs DI (Wave 3 pattern). |
| **4D/3** Task 061 §4C rollback | `22ad121a8` | 8 new files. `FailureClassifier` + `QuarantineClearService` + `RollbackTransitions` (pure-static §4C table via exhaustive switch). Wired reconciler dispatch + clear-quarantine endpoint. Replaced 059's `IQuarantineClearService` stub with real impl. 4-class taxonomy tests all pass. 6 Path C deviations. |
| **4D/4** Task 086 IaC alignment | `fa121e534` | 5 Bicep files + platform.json regen. Zero orphans to delete (all already absent — no-op). Publish **43.64 MB (Δ −1.32 MB)** — Bicep-only changes shrank compressed output. Also auto-fixed drift-2's platform.json follow-on. |
| **4D/5a** Drift 1 (MI factory + I5 broaden) | `ed4cdee42` | Set explicit `TenantId` on `ManagedIdentityCredentialFactory.cs` (was flagged by task 065 §7.2 as outside I5 scope). Broadened I5 ArchTest to scan `Infrastructure/Graph/**` AND `Infrastructure/Auth/**`. 5/5 I5 pass on broadened scope. |
| **4D/5b** Drift 1 SHA fill | `cf6de1d3b` | doc-only: fill commit SHA in TASK-INDEX + notes. |
| **4D/6** Drift 2 (prod PS scripts) | `55997daf3` | 2 lines in 2 PS scripts: `ai-search-key` → `AiSearch--AdminKey`. Source-only (no live prod). PSScriptAnalyzer clean. |
| **4D/7** Drift 3 (RecordMatchServiceTests) | `1d204667e` | Repaired compile break introduced by task 065 (added missing `IConfiguration` ctor arg in 2 test methods). Unblocked BFF unit test verification for downstream work. |
| **4D/8** Drift 4 (CustomerRunGuardOptions Path A) | `a70c4bf54` | Extended `CosmosProvisioningSecretGuardTests.ExcludedTypeFullNames` for `CustomerRunGuardOptions` (task 059's IOptions ClientSecret — same pattern as SolutionImportOptions + EnvVarValuesOptions precedent). FR-27 restored to 5/5. |
| **4D/9** Dispatcher wrap-up | (this commit) | TASK-INDEX + current-task.md update. |

### Cross-agent coordination achievements

- **6 parallel subagents dispatched at once** — highest concurrency of the project. Zero destructive resets. Absorbed shared-file writes (Program.cs, RunsEndpoints.cs) via Wave 3 read-late + `git commit --only` pattern.
- **Coordination behavior**: 060 absorbed 059's Program.cs DI + fixed a case-sensitivity typo in 059's uncommitted file (etagCounter → EtagCounter), left in tree for 059's commit. 061's clear-quarantine handler edits had been partially staged into 059's snapshot; final consolidation into 061 was clean.
- **059 exceeded POML** by fully wiring Quarantined reason-code integration (POML only required a hook).
- **061 avoided 57-file churn** by keeping FailureClass in Handlers namespace (per CLAUDE.md §11 minimalism) instead of moving to Rollback/.
- **086 discovered orphan flat keys were already absent** — dry-run pre-check saved a no-op live-mutation cycle.
- **Task 059 created + task 061 replaced** the `IQuarantineClearService` stub in-flight — cross-agent stub coordination worked without message-passing.

### Discovered drift resolved during 4D (per fix-at-discovery principle)

- ✅ **ManagedIdentityCredentialFactory gap** — fixed by drift 1
- ✅ **Prod-side AI Search alias in PS scripts** — fixed by drift 2 (source only; prod deploy is a future gesture)
- ✅ **RecordMatchServiceTests compile break** — fixed by drift 3 (regression from task 065)
- ✅ **CustomerRunGuardOptions.ClientSecret ArchTest** — fixed by drift 4 (Path A per SolutionImport precedent)

### Follow-on flag (for Wave 4 wrap-up / task 090)

Pre-existing Bicep errors in `infrastructure/bicep/stacks/model1-shared.bicep` + `model2-full.bicep` (BCP035/037/053) inherited from task 032 refactor. NOT introduced by any Wave 4 task. Task 086 subagent explicitly noted this. Should be a small cleanup task in Batch 4E or a separate follow-up.

---

## Wave 4 Plan — remaining 11 tasks (Batch 4E, serial)

| Task | Focus | Deps |
|---|---|---|
| **087** | `/config.json` runtime endpoint | 086 (canonical config surface) |
| **088** | CI coord PR to `ci-cd-unit-test-remediation-r1` (wire the 5 new ArchTests + config-drift check) | 064-067 + 084-087 |
| **062** | Load test (reconciler under concurrent runs) | 058-061 |
| **055** | H13 E2E acceptance-gate handler | all C4 + C6 + C' |
| **078** | E2E consent-callback verification | 042 + 057 |
| **089** | E2E Model 1 `trial-{yyyymmdd}` acceptance | ALL |
| **090** | Wrap-up + `/test-diet` + `README → Complete` + lessons-learned | 089 |

Plus optional pre-4E: small Bicep cleanup task for `stacks/model{1,2}-*.bicep` BCP errors.

**Serial dispatch rationale**: 087 must land config surface before 088 wires CI; E2E acceptance tests need all handlers in place; wrap-up is terminal.

---

## Wave 4 Batch 4C — COMPLETE (2026-08-17)

### Commit map

| Sub-task | Commit | Summary |
|---|---|---|
| **4C/1** Task 058 state-reconciler | `1b0297c7b` | 10 new files. `BackgroundService` with `TimeProvider`-based `PeriodicTimer` polling Cosmos every 5s (configurable). `DagAdvancer` pure-function encoding all 20 handlers (H0-H14 per §4.1). Cross-partition read waived via same-named local `[AllowCrossPartitionScan]` attribute (avoids Spaarke.Core transitive Dataverse SDK pull). N=5 concurrent instances → 1 distinct MessageId per handler verified. Cosmos-unreachable resilient (warn + retry, no crash). +38 new tests. 5 Path C deviations documented. |
| **4C/2** Task 066 verify + I1 | `e54cfb6e5` | Verified `1834b77bc` fix present in current branch (Register-EntraAppRegistrations.ps1:122-124 has `[Parameter(Mandatory = $true)][string]$TenantId`). Task 064's I1 test was already generalized (scans all `.ps1` via `Directory.EnumerateFiles`). Added regression seed test — creates temp `.ps1`, verifies scanner reports file/line, cleans up. Extracted scanner into reusable helper. |
| **4C/3** Task 085 alias collapse | `4ab4fbeda` + `06db97468` | Pre-check PROCEED (all 5 STOP conditions clear). 4 Bicep source templates migrated to canonical `AiSearch--AdminKey`. **2 aliases deleted** in dev KV (`AzureAISearchApiKey` + `ai-search-key`; `aisearch-admin-key` was Bicep write-only alias, never seeded). BFF `/healthz` HTTP 200 after every step. Soft-delete recovery active until 2026-11-16. Prod side deferred (documented as follow-on). |
| **4C/4** Task 065 audit sweep + fix | `f66a6add7` | 47-site comprehensive audit. 10 code files modified (3 PS + GraphClientFactory + 6 BFF services). Fix strategies: I1 remove GUID defaults + Mandatory, I2 add `tenantId eq` via `AzureAd:TenantId`, I3 explicit PartitionKey or `[AllowCrossPartitionScan]`, I5 explicit `credentialOptions.TenantId`. **All 5 §4D ArchTests PASS 22/22** (was 4 failing / 12 baseline violations). BFF 10,477 tests / L2 524 tests preserved. Δ 0.00 MB publish. |

### Follow-on drift surfaced during 4C (per fix-at-discovery principle)

1. **ManagedIdentityCredentialFactory.cs:34-40** (from task 065 §7.2) — same "no TenantId on options bag" gap as GraphClientFactory but sits OUTSIDE I5 ArchTest scope (`Infrastructure/Graph/**` only). Options: (a) broaden I5 scope + fix, (b) targeted PR fix. Both are ~30-minute work.
2. **Prod-side AI Search alias references** (from task 085 §out-of-scope) — `Seed-ProductionKeyVault.ps1` + `Configure-ProductionAppSettings.ps1` still reference `ai-search-key`; stale `infrastructure/bicep/platform.json` compiled artifact still contains alias literal (`platform.bicep` source already migrated in task 031). Options: (a) fix in a Batch 4D-adjacent small task, (b) fold into Batch 4E or wrap-up.

Both align with the "fix drift at discovery" feedback principle (memory: `feedback_fix_drift_at_discovery.md`). Suggest raising with owner alongside Batch 4D dispatch decision.

### Cross-agent coordination achievements

- **4 parallel subagents** (opus + 3× sonnet) all landed clean. Zero destructive resets.
- **File-tree partitioning worked**: 058 = L2 Reconciler+Program.cs, 065 = BFF Services+PS, 066 = TenantIsolation ArchTest, 085 = Bicep+PS+live KV. No overlaps.
- **Task 085 subagent correctly held out task 065's WIP** files (BFF Services + PS + GraphClientFactory) from its commit — clean git commit --only discipline.
- **Task 065 waited for task 066's test-file changes** (didn't touch I1 test) — coordination worked without message-passing.
- **CI-friendly commit ordering**: 058/066 landed quickly; 085/065 finished later. When pushed, PR #779 CI should go GREEN in one atomic post-push run since I1-I5 all now pass.

---

## Wave 4 Plan — remaining 15 tasks

Batch 4D (4 parallel): 059 I5 concurrency guard · 060 I6 crash recovery · 061 §4C rollback semantics · 086 IaC alignment.

Batch 4E (serial): 087 `/config.json` runtime endpoint · 088 CI coord PR to ci-cd-unit-test-remediation-r1 · 062 load test · 055 H13 E2E gate · 078 E2E consent-callback · 089 E2E Model 1 acceptance · 090 wrap-up + /test-diet.

Plus 2 optional discovered-drift fixes (ManagedIdentityCredentialFactory + prod AI Search aliases).

---

## Wave 4 Batch 4B — COMPLETE (2026-08-17)

### Commit map

| Sub-task | Commit | Summary |
|---|---|---|
| **4B/1** Task 057 L2 REST | `b8dcdfaeb` | 4 new files + 3 modified. 8 L2 REST endpoints under `/api/runs` + logs. 23 new tests (WebApplicationFactory-based). Audit-log via `ILogger` (parity with `AuditLogMiddleware`, D-057-4). `?customerId=` required on `/{id}` endpoints (§4D I3 enforcement, D-057-3). 6 documented deviations. |
| **4B/2** Task 064 ArchTests | `40b09f837` | 5 tests I1-I5 + `AllowCrossPartitionScanAttribute` in `Spaarke.Core.Attributes`. **4 of 5 tests fail on baseline by design** — 12 pre-existing violations filed to task 065 audit sweep (I4 passes cleanly). Suite runtime <1s for TenantIsolation subset. |
| **4B/3** Task 052 H9 BFF deploy | `67e8830ba` | 14 new files + 2 modified. Handler w/ 6 seams (PowerShellRunner, SlotSwapper, HealthProbe, R3GateVerifier, PublishSizeReporter). 35 new tests. Rollback re-swap verified; both-slots-bad escalation code. `Deploy-Release.ps1` already hardened (task 013 done). 2 Path C deviations. |
| **4B/4** Task 077 metering | `111773ffc` | 7 new production + 2 new tests + 3 modified. **Phase A: app-level custom App Insights metric** (rejected APIM). ONLY the enforcement delta on top of existing `AiTelemetry.RecordMeteredTokens` observability (from ai-architecture-redesign-r1 task 054). SC #13 met: Model 1 → 429; Model 2 → observation-only. |

### Cross-agent coordination achievements
- **2 parallel L2 Program.cs writers** (057 + 052): read-late narrow-hunk pattern held again. 057 committed first (endpoint mapping), 052 second (85-line DI hunk landed beside). Zero destructive resets — Wave 3's pattern scaled trivially.
- **Task 077's scope discipline**: subagent recognized POML `<justification><existing>` was wrong (observability layer already existed from a merged sibling project), reshaped scope to enforcement delta only per CLAUDE.md §11 no-duplication rule. Documented as D1.
- **Task 064's guard integrity**: subagent chose NOT to weaken guards to make baseline pass; correctly filed 12 violations to task 065. This is the FORCING FUNCTION working as designed.

### Notes files
- `notes/task-057-deviations.md` — 6 deviations (D-057-1..6).
- `notes/task-064-deviations.md` — 12 baseline violations catalogued for task 065.
- `notes/task-052-deviations.md` — 2 Path C deviations.
- `notes/task-077-deviations.md` + Phase A design doc (`per-tenant-metering-impl-2026-08-17.md`).

---

## Wave 4 Plan — remaining 19 tasks

Batches 4C/4D/4E to be dispatched sequentially with owner approval at each batch boundary.

---

## Wave 4 Batch 4A — COMPLETE (2026-08-17)

### Commit map

| Sub-task | Commit | Summary |
|---|---|---|
| **4A/1-5** ArchTest debt refactor | `3b67a7b8d` | 17 files (156+/36−). Mixed Path C (7 vault-identifier renames: KeyVaultName→VaultName, KeyVaultResourceId→VaultResourceId, KeyVaultSecretsUserRoleId→KvSecretsUserRoleId) + Path A extension (EnvVarValuesOptions + EnvVarValuesWriteRequest added to `ExcludedTypeFullNames` — mirrors SolutionImport precedent from task 049). ArchTest 4/5 → **5/5**. |
| **4A/6a** Task 081 BFF refactor | `0b8ca53ba` | 4 files. `SubmitDemoRequest` migrated off `[Obsolete] DemoProvisioningOptions.Environments/DefaultEnvironment` to `DataverseEnvironmentService`. Extracted `BuildRegistrationRecordUrl` pure helper + 6 new tests at KEEP path #6. Endpoint contract unchanged. **Unblocks task 082** (Azure App Service config removal). |
| **4A/6b** Task 084 Phase H manifest | `70abd9992` | 9 files, 3,767 lines. `scripts/canonical-secret-catalog/` — manifest.yaml (32 canonical secrets across 8 categories + 12 aliases), Invoke-CatalogGenerator.ps1 (deterministic, --dry-run + --verify, BINDING never-delete guard + dev-vault-exception guard), README, 4 generated outputs (seeder, Configure, tokens.md, Bicep). PSScriptAnalyzer clean. **Unblocks 085/086/088**. |

### Notes files
- `notes/wave-4-batch-4a-archtest-debt.md` — Mixed remediation rationale (why not pure Path A, why not pure Path C, follow-on Wave-C5 refactor after task 084's KV resolver seam lands).
- `notes/task-081-deviations.md` — no deviations.
- `notes/task-084-deviations.md` — no material deviations (StaticKvSecretManifest DI swap deferred per POML `<notes>`).

---

## Wave 4 Plan — remaining 23 tasks

Original plan preserved in Wave 3 wrap-up. Batches 4B/4C/4D/4E to be dispatched sequentially with owner approval at each batch boundary.

---

## Wave 3 Wrap-Up (2026-08-17)

### All Handlers Implemented (13 handlers + H0.5 BFF endpoint)

| Handler | Task | Commit | Tier / Focus |
|---|---|---|---|
| H0 preflight | 041 | Batch 3A | Quota checks (OpenAI TPM + DV rate + sub vCPU + SPE cert) |
| H0.5 consent | 042 | Batch 3A | BFF `POST /api/onboarding/consent-callback` + L2 dual-service |
| H1 subscription | 043 | Batch 3B | ARM + Lighthouse |
| H2a Bicep | 044 | Batch 3B | T1 owner: `keyVaultReferenceIdentity` PATCH |
| H2b AI Search | 045 | Batch 3C | 7 canonical indexes |
| H3 Entra | 046 | Batch 3C | 14 grants; S2S dropped |
| H4 KV Secrets | 047 | `230e78e0e` | T1 + T5 owner; interim `StaticKvSecretManifest`; DI swap path for task 084 |
| H5 DV env | 048 | Batch 3C | Interim `pac admin` |
| H6 Solution Import | 049 | `6b8698461` | Shell-out to task 012 Deploy-DataverseSolutions.ps1 |
| H7 env-vars | 050 | `60d13d7e3` | 5 hard-required per §10.2 (POML/prose reconciliation) |
| H8 SPE | 051 | `5096329ea` | T6 confidential-client; canonical `SPE-ContainerTypeId` KV |
| H10 App User + Graph parity | 053 | `b7dd49d6f` | T2 + T3 owner; 2 App Users (incl. BFF app-reg system user) |
| H11 users | 054 | `7cbc30d8b` | NativeAccount + B2BGuest as alternative branches |
| H12a AI seed | 070 | Batch 3C | Playbook consumers per ADR-039 |
| H12b app-config | 071 | Batch 3C | DAG-parallel with H12a |
| H12c runtime refs | 072 | `9d49c74f...` | `sprk_aimodeldeployment`; live-schema Path C |
| H14 post-deploy | 073 | `7dcc82fd7` | Parent + 3 sub-handlers (H14a/b/c); 1 new ADR-028 Path A |

### Test Growth
- Baseline before Wave 3: 244 tests
- After Wave 3D (task 047): 288
- After Batch 3E: 352 (26 H7 + 21 H8 + 17 H10 = 64 new + 244 baseline; some overlap)
- After Batch 3F: **428** (20 H12c + 16 H11 + 28 H14 = 64 new + 352 baseline; H12a/b tests picked up)

### Cross-Agent Coordination Achievements
Parallel Program.cs writes across 15 sibling agents produced ZERO data loss via:
- **Read-late narrow-hunk pattern** (established by task 049; refined by 070; robust by Batches 3E/3F)
- **git commit --only** discipline (never `git add .`)
- **Filesystem `mv` (not git) isolation** of broken sibling folders during in-flight compile verification — new technique documented by task 054 for future batches
- **InterStepState controlled schema extensions** as agent-coordination points (SpeContainerId · BffAppRegSystemUserId · ProvisionedUsers)

### Deviations Registry (all documented at `notes/task-XXX-deviations.md`)
- **Path A × 2 new** (in addition to spec's 2 baseline): task 073 ADR-028 Exchange app-only PS (formally added to spec.md ADR Tensions section); task 047 Phase H dep deferred (interim `StaticKvSecretManifest`, DI swap path documented for task 084)
- **Path C × ~15**: mostly reconciling POML literal prose vs acceptance-criteria vs design.md prose (test file location, param counts, auth choice, idempotency key format, seam count, InterStepState extensions)
- **Zero Path B** (no ADR amendments needed)

### ArchTest Debt (Wave 4 backlog item)
- `CosmosProvisioningSecretGuardTests` (from task 025) flags:
  - Task 046: `EntraAppRegRequest.KeyVaultName` (string)
  - Task 047: `KvSecretsPopulation.*` types
- **Options**: (a) extend `ExcludedTypeFullNames` via documented Path A (owner sign-off required — task 025's whole purpose is to prevent this drift; exclusions should be rare and rationale-heavy), OR (b) refactor to structured `KeyVaultSecretRef` type (task 025 has this as intended type). Recommend option (b) — small refactor task at Wave 4 start.
- **Not blocking Wave 4** — these are pre-existing before Wave 4 kicks off.

---

## Wave 4 Plan (25 tasks — pending owner go-ahead)

### Phase C Wave C5 — L2 REST + State Reconciler (6 tasks)
- **057** L2 REST endpoints (9 per §4.2) — opus/high — dep 036, 042
- **058** State-reconciler BackgroundService — opus/xhigh — dep 037, 038, 057
- **059** I5 concurrency guard — sonnet/xhigh — dep 023, 058
- **060** I6 crash recovery — sonnet/xhigh — dep 058, 059
- **061** §4C rollback semantics (4-class + Quarantined) — sonnet/xhigh — dep 057, 058
- **062** Load test — sonnet/high — dep all prior

### Phase C Wave C6 — Tenant-Isolation ArchTests (4 tasks)
- **064** 5 ArchTests for §4D I1–I5 — sonnet/xhigh — dep 042
- **065** Audit sweep BFF services for I2–I5 — sonnet/xhigh — dep 064
- **066** Verify `Register-EntraAppRegistrations.ps1:63` fix — sonnet/high — dep 064
- **067** Nightly Graph parity ArchTest — sonnet/high — dep 005, 053, 064, 088

### Phase D — L3 Skill + Metering (4 tasks)
- **075** `/provision-environment` skill (MAIN-SESSION-ONLY — Sub-Agent Write Boundary) — opus/high — dep 057
- **076** Fallback matrix in skill — sonnet/medium — dep 075 (MAIN-SESSION)
- **077** Token-metering layer (D19 — APIM vs custom App Insights metric) — opus/high
- **078** E2E consent-callback verification — sonnet/high — dep 042, 057

### Phase E cleanup (2 tasks; 080 done in Wave 2)
- **081** Refactor `RegistrationEndpoints.cs` (remove 4 `[Obsolete]`) — sonnet/high — dep 080
- **082** Delete `DemoProvisioning__Environments__*` from Azure config — sonnet/high — dep 080, 081

### H9 + H13 (2 tasks)
- **052** H9 BFF deploy (blue-green slot swap) — sonnet/xhigh — dep 013, 047
- **055** H13 E2E acceptance-gate handler — sonnet/xhigh — dep all C4 + C6 + C'

### Phase H — KV Federation (5 tasks)
- **084** Canonical secret-catalog manifest generator (r3 Phase 3b) — opus/xhigh — dep 018-020
- **085** Alias collapse for AI Search key — sonnet/xhigh — dep 084 · **BINDING pre-check applies**
- **086** IaC alignment — sonnet/high — dep 084, 085
- **087** `/config.json` runtime endpoint — sonnet/xhigh — dep 086 · **parallel-safe:false**
- **088** Coord PR to `ci-cd-unit-test-remediation-r1` (`.github/workflows/**`) — sonnet/medium — dep 064-067 + 084-087 · **parallel-safe:false**

### Phase F + wrap-up (2 tasks)
- **089** E2E Model 1 `trial-{yyyymmdd}` acceptance — sonnet/xhigh — dep ALL
- **090** Wrap-up (README status → Complete; lessons-learned.md; INDEX.md update; `/test-diet`) — sonnet/high — dep 089 · **parallel-safe:false**

### Suggested Wave 4 dispatch order
1. **Batch 4A** (parallel, 3): ArchTest debt refactor (small task 049.5) · 081 · 084 (Phase H manifest gen — opus/xhigh)
2. **Batch 4B** (parallel, 4): 057 opus/high · 064 · 052 H9 · 077 metering
3. **Batch 4C** (parallel, 4): 058 opus/xhigh · 065 · 066 · 085 (BINDING pre-check!)
4. **Batch 4D** (parallel, 4): 059 · 060 · 061 · 086
5. **Batch 4E** (serial: 087 → 088 → 062 → 055 → 078 → 089 → 090)

Estimated: ~40-50 more agent hours across 5 sessions with proactive checkpointing.

---

## Blockers

**Status**: None. All Wave 3 wrap-up decisions closed.

**Pre-Wave-4 owner decisions (all resolved 2026-08-17)**:
1. ✅ **ArchTest debt** — owner chose: refactor to structured `KeyVaultSecretRef` type (small task, executed as Wave 4 Batch 4A opener).
2. ✅ **Master divergence** — owner chose: merge master NOW. Executed via `9ec26ffe0` (112 commits absorbed; conflict on researcher `MEMORY.md` resolved as append-only union merge; L2 build 0/0 + 428/428 tests still pass post-merge).
3. ✅ **Push / PR** — owner chose: push branch + open draft PR at Wave 3 milestone. Executed: branch pushed to `origin/work/customer-provisioning-orchestration-r1`; draft PR https://github.com/spaarke-dev/spaarke/pull/779 opened with comprehensive body (3-layer architecture summary, L2 verification, `§4D` invariants status, deviations registry, Wave 4 plan).

---

## Session Notes

### Wave 3 Session (2026-08-17)
- **Started**: 2026-08-17 (context-recovered from prior compacted session)
- **Focus**: Wave 3 handler dispatch — 6 batches (3A/3B/3C/3D/3E/3F), 19 handler implementations
- **Duration**: ~10-12 hours of parallel agent work
- **Outcome**: 100% success (0 failed tasks, 0 destructive Program.cs resets after task 071's precedent-setting mistake)

### Key Learnings
- Read-late narrow-hunk Program.cs pattern scales to at least 5 concurrent sibling writers per batch (Batch 3E)
- Filesystem `mv` (not git) for isolating broken sibling folders during compile verification — new technique from task 054
- InterStepState as agent-coordination surface — 3 controlled extensions added in Wave 3 (SpeContainerId, BffAppRegSystemUserId, ProvisionedUsers) — matches design intent for handler state exchange
- MCP `mcp__dataverse__describe` used by task 072 to catch live-schema divergence from POML literal (`sprk_aimodeldeployment.tenantId` doesn't exist) — pattern for future handlers
- Handler-chain enqueue bridges (H0→H0.5, H12a/b→H12c, H12c→H14) established as Wave-Cp temporary pattern; refactor to reconciler-driven DAG-advancement at Wave C5 task 058

---

## Recovery Instructions

**To recover context after compaction:**
1. Read "Quick Recovery" section above (< 30 seconds)
2. Read "Wave 3 Wrap-Up" for handler-commit map
3. Read "Wave 4 Plan" for next dispatch
4. Consult `tasks/TASK-INDEX.md` for full task status
5. To resume: `/project-continue` or "start Wave 4 Batch 4A"

**Commands**:
- `/project-continue` — Full project context reload + master sync
- `/context-handoff` — Save current state before compaction (this file is that state)

---

*This file is the primary source of truth for active work state. Updated at Wave 3 boundary — safe compaction point.*
