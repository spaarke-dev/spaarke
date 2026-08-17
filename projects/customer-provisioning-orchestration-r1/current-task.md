# Current Task State — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-17 (Wave 3 COMPLETE — all 19 handler tasks landed)
> **Working directory**: `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` (unpushed; owner directive "no push, no PR yet" active)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project phase** | **Wave 3 COMPLETE (53/78 tasks = 68%)** — Wave 4 dispatch pending owner go-ahead |
| **Task** | none (wave boundary) |
| **Status** | wave-boundary |
| **Next Action** | Present Wave 4 dispatch plan to owner. Wave 4 = Phase D + Wave C5 + Wave C6 + Phase H + Phase E cleanup + Phase F acceptance + wrap-up (25 tasks). |
| **Blocker** | None external. **Pre-Wave-4 backlog** (see below): (a) ArchTest debt from tasks 046/047 — Path A extension of `CosmosProvisioningSecretGuardTests.ExcludedTypeFullNames` OR refactor `KeyVaultName`/`KvSecretsPopulation.*` to `KeyVaultSecretRef` type; (b) master divergence at ~103 commits behind; (c) owner decision on push/PR — 60+ local commits accumulated |
| **L2 project state** | 428/428 tests pass · `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/` 0/0 · TreatWarningsAsErrors=true · CVE clean · Program.cs at ADR-010 target |

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

**Status**: None external

**Pre-Wave-4 owner decisions**:
1. **ArchTest debt** — extend exclusions (path A) or refactor (path B)? Recommend refactor to `KeyVaultSecretRef`.
2. **Master divergence** — ~103 commits behind origin/master. Merge planning: (a) merge master into feature branch now (safe — overlapping files: GraphAppRoles.cs, customer.bicep, 3 Bicep modules); (b) defer to end-of-Wave-4; (c) push feature branch as-is + rebase later.
3. **Push / PR** — 60+ local commits since Wave 0 start. Owner directive still "no push, no PR yet"; owner may want to reconsider now that Wave 3 is a clean milestone.

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
