# Current Task State — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-17 (Wave 4 Batch 4B COMPLETE — 4 parallel subagents all landed clean)
> **Working directory**: `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` @ `40b09f837` (draft PR #779 open; last remote push at `9e936e911` — 4 new commits unpushed: b8dcdfaeb + 111773ffc + 67e8830ba + 40b09f837)
> **PR**: https://github.com/spaarke-dev/spaarke/pull/779 (DRAFT — DO NOT MERGE)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project phase** | **Wave 4 Batch 4B COMPLETE (59/78 tasks = 75.6%)** — Batch 4C dispatch pending owner go-ahead |
| **Task** | none (batch boundary) |
| **Status** | batch-boundary — READY for Batch 4C |
| **Next Action** | Push 4 new commits to origin. Then on owner go-ahead: dispatch **Wave 4 Batch 4C** (4 parallel: 058 state-reconciler BackgroundService opus/xhigh · 065 audit sweep BFF services for I2–I5 (fixes 12 baseline violations from task 064) · 066 verify Register-EntraAppRegistrations.ps1:63 fix · 085 alias collapse — BINDING pre-check required). |
| **Blocker** | None active. Task 065 (Batch 4C) is designed to fix the 12 baseline violations surfaced by task 064's new ArchTests — until 065 lands, CI on PR #779 will show 4 failing ArchTests (I1/I2/I3/I5). This is INTENTIONAL per POML acceptance criterion — task 064 shipped the strong guards; task 065 fixes the drift. |
| **L2 project state** | **486/486 tests pass** (was 428; +35 H9 + 23 endpoint tests) · `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/` 0/0 · TreatWarningsAsErrors=true · CVE clean · +85-line Program.cs DI additions from 052 landed cleanly beside 057's endpoint mapping (2 parallel L2 writers, zero destructive resets) |
| **BFF state** | **10,477/0 BFF tests pass** (was 10,457; +20 metering tests) · 0 warnings · publish size **44.96 MB (Δ 0.00** vs baseline; app-level metering added zero size) · CVE clean · Model 1 429-gating for over-budget tenants (SC #13 met) |
| **ArchTest state** | 5 new TenantIsolation tests added (I1-I5). **4 baseline violations FAIL by design** — 12 pre-existing violations catalogued for task 065 fix. Do NOT weaken guards per POML step 7 + CLAUDE.md §6.5. |
| **Master sync** | Feature branch is now 86 commits ahead of master (82 previous + 4 Batch 4B). |

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
