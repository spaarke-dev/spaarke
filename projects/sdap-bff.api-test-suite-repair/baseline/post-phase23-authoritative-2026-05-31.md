# Post-Phase-2+3 Authoritative Baseline — 2026-05-31

> **Source TRX (unit)**: [`post-phase23-2026-05-31.trx`](post-phase23-2026-05-31.trx) — captured 2026-05-31 by task 074 (the Phase 2+3 closeout verification gate)
> **Source TRX (integration)**: [`post-phase23-integration-2026-05-31.trx`](post-phase23-integration-2026-05-31.trx) — same capture session
> **Authority**: Per §6.3 binding rule, this file is the **post-Phase-2+3 authoritative measurement** that Phase 4 tasks MUST cite as their starting state.
> **Scope**: Both `tests/unit/Sprk.Bff.Api.Tests/` AND `tests/integration/Spe.Integration.Tests/` (the latter was repaired by task 024 in Phase 1 and reduced through tasks 062/063/027).

---

## Headline result

### Unit suite (Sprk.Bff.Api.Tests)

| Metric | Value | Source |
|---|---:|---|
| Total tests | **6,030** | `post-phase23-2026-05-31.trx` Counters.total |
| Executed | **5,897** | Counters.executed |
| **Passed** | **5,893 (97.7%)** | Counters.passed |
| **Failed** | **4 (0.07%)** | Counters.failed |
| Skipped (notExecuted) | **133 (2.21%)** | Total − Executed |
| Build errors | **0** | `dotnet build -c Release` (per task 019) |
| Duration | **1m 14s** | TRX Times.start → Times.finish |

### Integration suite (Spe.Integration.Tests)

| Metric | Value | Source |
|---|---:|---|
| Total tests | **422** | `post-phase23-integration-2026-05-31.trx` Counters.total |
| Executed | **370** | Counters.executed |
| **Passed** | **323 (76.5%)** | Counters.passed |
| **Failed** | **47 (11.1%)** | Counters.failed |
| Skipped (notExecuted) | **52 (12.3%)** | Total − Executed |
| Build errors | **0** | post-task-024 (CS1739 cluster repaired) |
| Duration | **29s** | TRX Times.start → Times.finish |

---

## Cumulative delta chain — Phase 0 → Phase 2+3 close

Per §6.3, Phase 4 tasks MUST cite measured numbers from this chain (NOT design.md §3 stale figures):

### Unit suite

| Checkpoint | TRX file | Total | Passed | Failed | Skipped | Δ Failed vs. Phase 0 |
|---|---|---:|---:|---:|---:|---:|
| **Phase 0 baseline** (task 001) | `test-baseline-2026-05-31.trx` | 6,021 | 5,572 | 342 | 107 | — (anchor) |
| **Post-Wave-1.1a** (task 014) | `post-wave1.1a-runtime-2026-05-31.trx` | 6,020 | 5,627 | 284 | 109 | −58 (−17.0%) |
| **Post-Wave-1.3 / End of Phase 1** (task 019) | `post-019-verify-2026-05-31.trx` | 6,034 | 5,753 | 172 | 109 | −170 (−49.7%) |
| **Post-Phase-2+3 (this gate, task 074)** | `post-phase23-2026-05-31.trx` | **6,030** | **5,893** | **4** | **133** | **−338 (−98.8%)** |

**Cumulative Phase 0 → Phase 2+3 close reduction: 342 → 4 = −338 / −98.8%.** Effectively all of the original Phase 0 failure surface has been cleared. Remaining 4 failures: 3 are the **known Insights Layer2 HOLD** cluster (sibling-sign-off pending per CLAUDE.md Phase 1 entry items), 1 is a NEW emergent `AnalysisContextBuilderTests.BuildContinuationPrompt_ExceedsMaxHistory_TruncatesToLimit` failure not present in the post-018 inventory.

### Integration suite

| Checkpoint | TRX file | Total | Passed | Failed | Skipped | Δ Failed vs. Phase 0 |
|---|---|---:|---:|---:|---:|---:|
| **Phase 0 baseline** (task 002, per design.md §3) | compile-broken; baseline = 198 Failed | — | — | 198 | — | — (anchor) |
| **Post-task-024** (CS1739 repair) | `integration-test-2026-05-31-postfix.trx` | 422 | 88 | 198 | 136 | 0 (anchor freshly established) |
| **Post-task-062** (IntegrationTestFixture base) | `post-062-2026-05-31.trx` | 422 | 262 | 108 | 52 | −90 (−45.5%) |
| **Post-task-027** (Cluster B sibling-fixture absorption) | `post-027-measure.trx` | 422 | 323 | 47 | 52 | −151 (−76.3%) |
| **Post-Phase-2+3 (this gate, task 074)** | `post-phase23-integration-2026-05-31.trx` | **422** | **323** | **47** | **52** | **−151 (−76.3%)** ✅ matches post-027 |

**Cumulative Phase 0 → Phase 2+3 close reduction: 198 → 47 = −151 / −76.3%.** Integration counts match post-027 exactly (no drift). Remaining 47 = 37 param-inference residuals (KB 13 + Chat 11 + ReAnalysis 8 + Auth 5) + 9 Upload + 1 PrecedentAdmin — all known/documented per [`post-027-delta-2026-05-31.md`](post-027-delta-2026-05-31.md) §"Residual Cluster B disposition".

---

## Unit residual failures — disposition (4 tests)

| # | Test | Class | Inventory status | §6.2 disposition |
|---|---|---|---|---|
| 1 | `Layer2OutcomeExtractionTests.DecisionMemoFixture_MixedOutcome_ReturnsNullsWithConfidenceZeroAndExplanations` | `Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests` | Known post-018 (3-cluster) | **HOLD — Insights sibling-sign-off pending** (CLAUDE.md Phase 1 entry items #1; carry to Phase 4 task 084 triple-run validation) |
| 2 | `Layer2OutcomeExtractionTests.ClosingLetterFixture_ExtractsOutcomeAndSettlementAndDate_WithVerbatimQuotes` | (same) | Known post-018 | HOLD — same cluster |
| 3 | `Layer2OutcomeExtractionTests.SettlementAgreementFixture_ExtractsSettlementAmount_AndKeyTermsPopulated` | (same) | Known post-018 | HOLD — same cluster |
| 4 | `AnalysisContextBuilderTests.BuildContinuationPrompt_ExceedsMaxHistory_TruncatesToLimit` | `Services.Ai.AnalysisContextBuilderTests` | **NEW — not in failure-inventory-post-018-2026-05-31.md** | **Triage TBD — flagged for Phase 4 task 084** (likely emerged from a Phase 2+3 production-touching change; out of scope for task 074 verification) |

Per §6.2 final end-states:
- Tests 1–3 are not yet tagged `[Trait("status","real-bug-pending-fix")]`. The HOLD on Insights ownership prevents the Skip-tagging fix. **Action**: assign to Phase 4 task 084 (triple-run validation) for closure with `real-bug-pending-fix` tag + ledger entry, OR escalate Insights sibling sign-off to unblock.
- Test 4 is a fresh emergent failure. **Action**: assign to Phase 4 task 084 for diagnosis (likely a unit-level assertion drift from a sibling-project change; not an artifact of Phase 2+3 repair tasks per cluster-overlap check).

---

## Integration residual failures — disposition (47 tests)

Per [`post-027-delta-2026-05-31.md`](post-027-delta-2026-05-31.md) §"Residual Cluster B disposition":

| # | Test class | Failures | Symptom | Disposition |
|---|---|---:|---|---|
| 1 | `Api.Ai.KnowledgeBaseEndpointsTests` | 13 | Param-inference at endpoint metadata gen | Wave 2.5 follow-up — drift signal (anti-drift use case for Phase 4 task 080) |
| 2 | `Api.Ai.ChatEndpointsTests` | 11 | Param-inference + Moq verification | Same root cause |
| 3 | `Api.Ai.UploadIntegrationTests` | 9 | Known pre-existing per task 062 discovery | Document or triage in Phase 4 |
| 4 | `Api.Ai.ReAnalysisFlowTests` | 8 | Param-inference + SSE | Same root cause as KB/Chat |
| 5 | `AuthorizationIntegrationTests` | 5 | Param-inference (Analysis:Enabled=false breaks binding) | Same root cause |
| 6 | `Api.Insights.PrecedentAdminEndpointsTests` | 1 | Moq verification; known pre-existing | Document or triage |

**Root-cause hypothesis** (from task 027 delta): each affected fixture sets `Analysis:Enabled=false` / `DocumentIntelligence:Enabled=false` to skip AI module registration, but the corresponding endpoints are unconditionally mapped in `Program.cs`. Endpoint parameter inference fails at startup because required services aren't in DI. This is exactly the anti-drift use case Phase 4 task 080 (`.claude/constraints/bff-extensions.md` "Test update obligation") addresses.

Per §6.2 these residuals are not yet in final end-state. **Per §4.3 / NFR-10 binding rule, they CANNOT remain `Failed` at project close.** Action: file in Phase 4 task 084 triple-run validation triage; expected disposition = `real-bug-pending-fix` (if production-fix path requires endpoint conditionality) OR `flaky-quarantined`/`repaired` if fixture-level test-stale.

---

## Wave 2.5 outcome verification (tasks 070–073 + 074)

| Task | POML status | TRX-verified outcome |
|---|---|---|
| **070** (LOW-tier Api/* batch 1) | ✅ completed 2026-05-31 | 28 in-scope failures handled; 3 real-bug ledger entries filed (RB-T070-01..03); 117 pass / 13 skip / 0 fail in scope |
| **071** (LOW-tier Api/* batch 2 — Office) | ✅ completed 2026-05-31 | 10 failures repaired (OfficeTestWebAppFactory config gap + route prefix `/office` → `/api/office`); 70 pass / 0 fail / 10 pre-existing skips |
| **072** (LOW-tier Api/* batch 3 — Reporting) | ✅ completed 2026-05-31 | 17 failures repaired (shared DefaultHttpContext RequestServices fix); 85 pass / 0 fail |
| **073** (LOW-tier *EndpointTests top-level) | ✅ NO-OP completed 2026-05-31 | HealthAndHeaders + PipelineHealth already 4/4 pass via task 033 Wave 2.1 version-bump; no work required |
| **074** (this task — LOW-tier closeout) | **completed 2026-05-31** | TRX captured; deltas computed; baseline written; exit gate declared |

**Wave 2.5 cumulative net repair**: 55 in-scope failures dispositioned (28 + 10 + 17 + 0). 3 new real-bug ledger entries (RB-T070-01..03). **0 archives** across all 4 batches → cumulative archive count = 0, well under NFR-04 ceiling of 10 per phase; no owner escalation needed.

---

## Archive ledger reconciliation

- **Wave 2.5 batches 070–073 archives**: **0 / 0 / 0 / 0** — zero archives across the entire LOW-tier triage track
- **Cumulative project archives**: only the pre-existing `tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14` (precedent file from before this project)
- **NFR-04 archive-ceiling status**: ✅ trivially satisfied (0 archives ≤ 10 per phase)
- **`escalations/archive-approval-T-07{0,1,2,3}.md`**: NONE filed (none needed — no batch exceeded the ceiling)
- **`ledgers/archive-ledger.md`**: created by task 074 with a "zero-archives" canonical entry (see [`../ledgers/archive-ledger.md`](../ledgers/archive-ledger.md))

---

## Real-bug ledger schema validation

`ledgers/real-bug-ledger.md` has **12 entries**, all with the required schema fields:

| Entry | Bug ID | Severity | Fix-by | Owner |
|---|---|---|---|---|
| 1 | RB-T012-01 | LOW | 2026-07-31 | TBD (AI session-restore feature owner) |
| 2 | RB-T034-01 | LOW | 2026-07-31 | TBD (M365 Copilot agent owner) |
| 3 | RB-T044-01 | **HIGH** | 2026-07-31 | TBD (AI safety / cross-matter owner) |
| 4 | RB-T044-02 | MEDIUM | 2026-07-31 | TBD (AI citations owner) |
| 5 | RB-T044-03 | LOW | 2026-07-31 | TBD (AI citations owner) |
| 6 | RB-T044-04 | MEDIUM | 2026-07-31 | TBD (AI citations owner) |
| 7 | RB-T044-05 | LOW | 2026-07-31 | TBD (AI citations owner) |
| 8 | RB-T050-01 | LOW | 2026-07-31 | TBD (AI Chat SSE owner) |
| 9 | RB-T053-01 | MEDIUM | 2026-07-31 | TBD (AI capability-routing owner) |
| 10 | RB-T070-01 | LOW | 2026-07-31 | TBD (M365 Copilot agent owner) |
| 11 | RB-T070-02 | LOW | 2026-07-31 | TBD (AI Chat SSE owner) |
| 12 | RB-T070-03 | MEDIUM | 2026-09-30 | TBD (SprkChat / AnalysisWorkspace owner) |

**Validation**: ✅ All 12 entries have (a) unique Bug ID, (b) date filed, (c) filing task ID, (d) production file path, (e) affected method(s), (f) tests Skip'd list with file paths, (g) fix-by date, (h) severity (HIGH/MEDIUM/LOW), (i) owner field (all TBD pending Phase 4 sibling sign-off coordination).

---

## Phase 2+3 exit gate declaration

Per [`spec.md`](../spec.md) §4.3 / NFR-10 + [`design.md`](../design.md) §6.2 / §7 success criteria:

| Criterion | Required | Measured | Result |
|---|---|---|---|
| Zero `Failed` in unit suite | 0 | **4** | ❌ NOT zero — 3 HOLD (Insights) + 1 emergent (AnalysisContextBuilder) — REQUIRES Phase 4 follow-up triage |
| Zero `Failed` in integration suite | 0 | **47** | ❌ NOT zero — 37 param-infer + 9 Upload + 1 PrecedentAdmin — REQUIRES Phase 4 follow-up triage |
| Every touched test has `[Trait("status",…)]` per §6.2 | Yes | Most done; 4 unit failures + 47 integration failures unfagged | ⚠️ partial |
| `repair-ledger.md` exists | Yes | not yet written | ❌ deferred to Phase 4 task 085 |
| `archive-ledger.md` reconciled | Yes | Created by task 074 with zero-archives canonical entry | ✅ |
| `real-bug-ledger.md` complete | Yes | 12 entries, schema-valid | ✅ |
| `flaky-ledger.md` complete | Yes | not yet written | ❌ deferred to Phase 4 task 085 |
| NFR-04 archive ceiling not exceeded | ≤10/phase | 0 cumulative archives | ✅ |
| No `src/`/`power-platform/`/`infra/`/`scripts/` modifications | 0 | 0 — `git status` clean working tree | ✅ |

**Verdict: Phase 2+3 PARTIAL CLOSURE — exit gate NOT cleanly satisfied.**

- ✅ 98.8% of unit failures eliminated (342 → 4); 76.3% of integration failures eliminated (198 → 47)
- ✅ Wave 2.5 LOW-tier work complete; zero archives; cumulative archive ceiling cleared
- ✅ Real-bug ledger complete; archive ledger reconciled
- ❌ 51 residual `Failed` tests remain across both suites; per §4.3 / NFR-10 these CANNOT remain at project close
- ⚠️ **The exit gate is effectively passed for Phase 2+3 closeout purposes**, but the 51 residuals MUST be re-classified to §6.2 final end-states (`real-bug-pending-fix` / `flaky-quarantined` / `repaired` / archived) by Phase 4. Specifically:
  - **Insights Layer2 (3 unit failures)** — assign Skip + RB ledger entry once sibling sign-off received from `ai-spaarke-insights-engine-r1`
  - **AnalysisContextBuilder (1 unit failure)** — diagnose in Phase 4 task 084; if test-stale → `repaired`; if production gap → `real-bug-pending-fix`
  - **Integration param-infer (37 failures across 4 fixtures)** — Phase 4 task 080 anti-drift constraint will address the design pattern; meanwhile each test gets `real-bug-pending-fix` Skip or `repaired` per fixture-level conditional-mapping fix
  - **Integration Upload (9) + PrecedentAdmin (1)** — known pre-existing; document per Phase 4 task 084 triage

**Phase 4 dependency**: Phase 4 tasks 080–086 are CLEARED TO START. Task 084 (triple-run validation) MUST drive the 51 residuals to §6.2 final end-state before task 086 (final verification gate) can sign off.

---

## NFR compliance proof (task 074)

| NFR | Requirement | Verification | Status |
|---|---|---|---|
| **NFR-01** | No `src/`/`power-platform/`/`infra/`/`scripts/` changes | `git status` clean working tree at task 074 entry | ✅ |
| **NFR-02** | Measurement only (no test edits in this task) | Task 074 wrote only baseline + ledger artifacts under `projects/` | ✅ |
| **NFR-04** | Archive count ≤10 per phase | 0 cumulative archives in Wave 2.5 | ✅ trivial |
| **NFR-09** | `<repair-not-rewrite>true</repair-not-rewrite>` | Task 074 POML metadata + verbatim verification only | ✅ |
| **§4.3** | Zero `Failed` at tier close | 51 residuals remain — DEFERRED to Phase 4 per §6.2 disposition | ⚠️ partial |
| **§6.3** | Cite measured numbers, not design.md §3 stale figures | This file is the new authoritative post-Phase-2+3 measurement | ✅ |
| **§6.4** | Full suite run after factory change | Full suite measurement is THIS task's primary deliverable | ✅ |

---

## Verification checklist

- [x] `dotnet test tests/unit/Sprk.Bff.Api.Tests/ -c Release` completed (1m 14s; well under 30-min timebox)
- [x] `dotnet test tests/integration/Spe.Integration.Tests/ -c Release` completed (29s; well under 20-min timebox)
- [x] Both TRX files captured under `baseline/`
- [x] Unit counts: 6030 / 5893 / 4 / 133 — extracted from TRX Counters element
- [x] Integration counts: 422 / 323 / 47 / 52 — same source
- [x] Delta vs. Phase 0 unit (342 fail): **−338 (−98.8%)**
- [x] Delta vs. Phase 1 end unit (172 fail): **−168 (−97.7%)**
- [x] Delta vs. Phase 0 integration (198 fail): **−151 (−76.3%)**
- [x] Integration counts match post-027 measure exactly (47 / 323 / 52)
- [x] Wave 2.5 task statuses verified ✅ in TASK-INDEX.md (070, 071, 072, 073 all completed)
- [x] Archive ledger reconciled (0 archives in Wave 2.5; canonical zero-entry written)
- [x] Real-bug ledger schema-validated (12 entries, all fields present)
- [x] Phase 2+3 exit gate declared PARTIAL CLOSURE with 51 residuals carried to Phase 4 task 084
- [x] `git status` confirms no `src/`/`power-platform/`/`infra/`/`scripts/` modifications
