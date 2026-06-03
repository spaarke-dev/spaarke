# Post-028 Residual Classification — 2026-05-31

> **Source TRX (unit)**: [`post-028-unit-2026-05-31.trx`](post-028-unit-2026-05-31.trx) — captured 2026-05-31 by task 028 (residual classification + Phase 2+3 final disposition)
> **Source TRX (integration)**: [`post-028-integration-2026-05-31.trx`](post-028-integration-2026-05-31.trx) — same capture session
> **Authority**: Per §6.3 binding rule, this file is the **post-residual-classification authoritative measurement**. Phase 4 tasks MUST cite this as the starting state (it supersedes [`post-phase23-authoritative-2026-05-31.md`](post-phase23-authoritative-2026-05-31.md) for `Failed`/`Skipped` counts).
> **Scope**: §6.2 final-end-state assignment for the 51 residual `Failed` tests carried out of Phase 2+3 close, satisfying §4.3 / NFR-10.

---

## Headline result

### Unit suite (Sprk.Bff.Api.Tests)

| Metric | Pre-028 (post-phase23) | Post-028 (this task) | Δ |
|---|---:|---:|---:|
| Total tests | 6,030 | 6,030 | 0 |
| **Passed** | 5,893 (97.7%) | 5,893 (97.7%) | 0 |
| **Failed** | **4** | **0** | **−4 (100%)** |
| Skipped (notExecuted) | 133 | 137 | +4 |
| Build errors | 0 | 0 | 0 |

### Integration suite (Spe.Integration.Tests)

| Metric | Pre-028 (post-phase23) | Post-028 (this task) | Δ |
|---|---:|---:|---:|
| Total tests | 422 | 421 | -1 (test inventory reduction from Theory collapse during edits)¹ |
| **Passed** | 323 (76.5%) | 323 (76.7%) | 0 |
| **Failed** | **47** | **0** | **−47 (100%)** |
| Skipped (notExecuted) | 52 | 98 | +46 |
| Build errors | 0 | 0 | 0 |

¹ Theory `Authorization_ChecksDifferentPolicies_PerEndpoint` was 2 InlineData rows pre-edit; receiving a `[Theory(Skip=...)]` collapses both InlineData rows into a single Skipped test entry — net reduction of 1 in the test inventory.

**Bottom line**: BOTH suites now have **`Failed: 0`**. §4.3 / NFR-10 satisfied.

---

## Per-cluster disposition table

The 51 residual `Failed` tests were classified to §6.2 final end-states as follows:

| # | Cluster | Failures | Disposition | Ledger Entry | Severity | Fix-by | Rationale |
|---|---|---:|---|---|---|---|---|
| 1 | `AnalysisContextBuilderTests.BuildContinuationPrompt_ExceedsMaxHistory_TruncatesToLimit` (unit, 1) | 1 | `real-bug-pending-fix` | RB-T028-01 | MEDIUM | 2026-07-31 | `OrderByDescending(m => m.Timestamp)` is non-deterministic on tied ticks — drops Msg-11, reorders (19,20) pair. Production bug observable when concurrent messages share a tick. |
| 2 | `Layer2OutcomeExtractionTests` (unit, 3) | 3 | `real-bug-pending-fix` | RB-T028-02 | MEDIUM | 2026-09-30 | HOLD per task 008 Insights sibling sign-off. Fixture text drifted from production prompt; resolution requires `ai-spaarke-insights-engine-r1` owner coordination. |
| 3 | `Api.Ai.KnowledgeBaseEndpointsTests` (integration, 13) | 13 | `real-bug-pending-fix` | RB-T028-03 | HIGH | 2026-07-31 | DI binding gap: endpoint takes `INotificationService` which is unregistered when `Analysis:Enabled=false`. Production startup fails if feature flags ever disable AI. |
| 4 | `Api.Ai.ChatEndpointsTests` (integration, 11) | 11 | `real-bug-pending-fix` | RB-T028-04 | HIGH | 2026-07-31 | Same root cause as RB-T028-03 (Chat endpoint family). |
| 5 | `Api.Ai.ReAnalysisFlowTests` (integration, 8) | 8 | `real-bug-pending-fix` | RB-T028-05 | HIGH | 2026-07-31 | Same root cause as RB-T028-03 (ReAnalysis endpoint family). |
| 6 | `AuthorizationIntegrationTests` (integration, 5) | 5 | `real-bug-pending-fix` | RB-T028-06 | HIGH | 2026-07-31 | Authorization endpoints don't directly need `INotificationService`, but ASP.NET endpoint metadata generation aborts on the FIRST unresolvable handler, taking down the entire pipeline. Fix-by binds to RB-T028-03/04/05. |
| 7 | `Api.Ai.UploadIntegrationTests` (integration, 9) | 9 | `real-bug-pending-fix` | RB-T028-07 | MEDIUM | 2026-07-31 | Upload endpoint returns 500 instead of documented 422/200. Production exception not isolated to 422 path; storage seam missing for tests. |
| 8 | `Api.Insights.PrecedentAdminEndpointsTests.PostPrecedent_AsAdmin_Returns_201_WithTentativeStatus` (integration, 1) | 1 | `real-bug-pending-fix` | RB-T028-08 | LOW | 2026-09-30 | `CreateTentativeAsync` Moq expected once but was 0 times. Possibly signature drift or production short-circuit. 5 of 6 sibling tests pass. |

**Totals**:
- 4 unit residuals → 2 ledger entries (RB-T028-01, RB-T028-02) → all `real-bug-pending-fix`
- 47 integration residuals → 6 ledger entries (RB-T028-03..08) → all `real-bug-pending-fix`
- **0 archived, 0 flaky-quarantined, 0 repaired** (no quick-fix opportunity emerged; AnalysisContextBuilder bug was a real production sort-stability issue, not test-stale)

---

## §6.2 final-end-state distribution

After task 028, the 51 residuals have:

| §6.2 end-state | Count | % of 51 |
|---|---:|---:|
| `repaired` | 0 | 0.0% |
| `real-bug-pending-fix` | 51 | **100.0%** |
| `flaky-quarantined` | 0 | 0.0% |
| archived (rename to `.cs.archived-YYYY-MM-DD`) | 0 | 0.0% |

**Verdict**: 100% of residuals classified to `real-bug-pending-fix`. NFR-04 archive ceiling (≤10 per phase) **trivially satisfied** (0 archives in this task).

---

## Ledger entries created

Appended to [`projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md`](../ledgers/real-bug-ledger.md):

| ID | Title | Severity | Fix-by | Tests Skip'd |
|---|---|---|---|---:|
| **RB-T028-01** | `AnalysisContextBuilder.BuildContinuationPrompt` OrderByDescending non-deterministic | MEDIUM | 2026-07-31 | 1 |
| **RB-T028-02** | Layer 2 outcome-extraction LLM-mock fixture drift (HOLD pending sibling) | MEDIUM | 2026-09-30 | 3 |
| **RB-T028-03** | `KnowledgeBaseEndpoints` DI binding gap (notificationService UNKNOWN) | HIGH | 2026-07-31 | 13 |
| **RB-T028-04** | `ChatEndpoints` DI binding gap (same root cause as -03) | HIGH | 2026-07-31 | 11 |
| **RB-T028-05** | `ReAnalysisFlowEndpoints` DI binding gap (same root cause as -03) | HIGH | 2026-07-31 | 8 |
| **RB-T028-06** | `AuthorizationEndpoints` DI binding gap (cascade from -03/-04/-05) | HIGH | 2026-07-31 | 5 |
| **RB-T028-07** | `UploadEndpoint` returns 500 instead of documented status codes | MEDIUM | 2026-07-31 | 9 |
| **RB-T028-08** | `PrecedentAdmin.CreateTentativeAsync` Moq verification gap | LOW | 2026-09-30 | 1 |

**Total**: 8 new entries (RB-T028-01..08) → 51 tests Skip'd.

**Real-bug-ledger.md cumulative count**: 12 (pre-028) + 8 (this task) = **20 entries**.

### Severity distribution (cumulative ledger)
- HIGH: 1 (RB-T044-01) + 4 (RB-T028-03/04/05/06) = **5**
- MEDIUM: 4 (RB-T044-02/04, RB-T053-01, RB-T070-03) + 3 (RB-T028-01/02/07) = **7**
- LOW: 7 (RB-T012-01, RB-T034-01, RB-T044-03/05, RB-T050-01, RB-T070-01/02) + 1 (RB-T028-08) = **8**
- **Cumulative total**: 20 entries

### Fix-by date distribution (cumulative ledger)
- 2026-07-31 (60-day target — 11 entries pre-028 + 6 new) = **17**
- 2026-09-30 (90-day target — 1 entry pre-028 + 2 new) = **3**
- **Cumulative total**: 20 entries

---

## Flaky-quarantined ledger

Created [`projects/sdap-bff.api-test-suite-repair/ledgers/flaky-ledger.md`](../ledgers/flaky-ledger.md) with **zero entries** (no non-deterministic failures surfaced in the 51 residuals). The file documents the schema for future use and notes the 2026-05-31 task 028 close state.

---

## Files modified (test edits — Skip + Trait additions only)

| File | Modifications | Diff impact |
|---|---|---|
| [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisContextBuilderTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisContextBuilderTests.cs) | 1 `[Fact(Skip=...)]` + 1 `[Trait(...)]` | <1% |
| [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Layer2/Layer2OutcomeExtractionTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Layer2/Layer2OutcomeExtractionTests.cs) | 3 `[Fact(Skip=...)]` + 3 `[Trait(...)]` | <1% |
| [`tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs`](../../../tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs) | 13 `[Fact(Skip=...)]` + 13 `[Trait(...)]` | ~5% |
| [`tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs`](../../../tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs) | 11 `[Fact(Skip=...)]` + 11 `[Trait(...)]` | ~4% |
| [`tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs`](../../../tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs) | 8 `[Fact(Skip=...)]` + 8 `[Trait(...)]` | ~3% |
| [`tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs`](../../../tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs) | 3 `[Fact(Skip=...)]` + 1 `[Theory(Skip=...)]` + 4 `[Trait(...)]` | ~3% |
| [`tests/integration/Spe.Integration.Tests/Api/Ai/UploadIntegrationTests.cs`](../../../tests/integration/Spe.Integration.Tests/Api/Ai/UploadIntegrationTests.cs) | 9 `[Fact(Skip=...)]` + 9 `[Trait(...)]` | ~3% |
| [`tests/integration/Spe.Integration.Tests/Api/Insights/PrecedentAdminEndpointsTests.cs`](../../../tests/integration/Spe.Integration.Tests/Api/Insights/PrecedentAdminEndpointsTests.cs) | 1 `[Fact(Skip=...)]` + 1 `[Trait(...)]` | <1% |

**Total**: 8 files touched. ALL diffs <50% line replacement → **NFR-02 trivially satisfied** (no `escalations/rewrite-request-*.md` needed).

**Ledger files modified**:
- [`projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md`](../ledgers/real-bug-ledger.md): 8 new entries appended (RB-T028-01..08)
- [`projects/sdap-bff.api-test-suite-repair/ledgers/flaky-ledger.md`](../ledgers/flaky-ledger.md): NEW file (zero-entries canonical schema doc)

---

## NFR compliance proof (task 028)

| NFR | Requirement | Verification | Status |
|---|---|---|---|
| **NFR-01** | No `src/`/`power-platform/`/`infra/`/`scripts/` changes | `git status` shows only `tests/` + `projects/` modifications | ✅ |
| **NFR-02** | ≤50% line replacement per file | All diffs are additive attribute injections; max ~5% per file | ✅ |
| **NFR-03** | No new DI registrations in tests | Pure attribute additions; no DI changes | ✅ |
| **NFR-04** | Archive count ≤10 per phase | 0 archives in this task; cumulative still 0 | ✅ trivial |
| **NFR-06** | No silent deletions | No deletions; only Skip + Trait additions | ✅ |
| **NFR-09** | `<repair-not-rewrite>true</repair-not-rewrite>` | Declared in task 028 POML metadata | ✅ |
| **NFR-10 / §4.3** | Zero `Failed` end-state at project close | **0 Failed in both suites** post-028 | ✅ |
| **NFR-11** | Compile-broken files compile cleanly | Both projects build 0 errors / unchanged warnings | ✅ |
| **§4.5** | No `CustomWebAppFactory.cs` rewrite | File untouched | ✅ |
| **§6.2** | Every touched test has `[Trait("status", …)]` per taxonomy | 51 tests tagged `real-bug-pending-fix` | ✅ |
| **§6.3** | Cite measured numbers, not design.md §3 stale figures | This file extends [`post-phase23-authoritative-2026-05-31.md`](post-phase23-authoritative-2026-05-31.md) measurement chain | ✅ |

---

## Phase 2+3 close — REVISED exit gate declaration

Per [`post-phase23-authoritative-2026-05-31.md`](post-phase23-authoritative-2026-05-31.md), task 074 declared Phase 2+3 as PARTIAL CLOSURE because 51 residuals remained in `Failed` state. Task 028 satisfies the §6.2 final-end-state requirement for all 51 — the exit gate is now **FULLY SATISFIED** for §4.3 / NFR-10:

| Criterion | Required | Pre-028 | Post-028 (this task) | Result |
|---|---|---|---|---|
| Zero `Failed` in unit suite | 0 | 4 | **0** | ✅ |
| Zero `Failed` in integration suite | 0 | 47 | **0** | ✅ |
| Every touched test has `[Trait("status",…)]` per §6.2 | Yes | 51 untagged | **all 51 tagged `real-bug-pending-fix`** | ✅ |
| `real-bug-ledger.md` complete | Yes | 12 entries | **20 entries (12 + 8 new)** | ✅ |
| `flaky-ledger.md` complete | Yes | not yet written | **created (zero-entries canonical)** | ✅ |
| NFR-04 archive ceiling not exceeded | ≤10/phase | 0 cumulative | **0 cumulative** | ✅ |
| No `src/`/`power-platform/`/`infra/`/`scripts/` modifications | 0 | 0 | **0** | ✅ |

**Verdict**: Phase 2+3 exit gate is now **FULLY SATISFIED**. Phase 4 tasks 080–086 are CLEARED TO START with no carry-over `Failed`-state debt.

---

## What Phase 4 tasks should do next

- **Task 080** (`.claude/constraints/bff-extensions.md` "Test update obligation"): This task's RB-T028-03/04/05/06 are the canonical anti-drift use case. Cite them when describing the constraint.
- **Task 084** (triple-run validation): re-run both full suites three times; verify 0 Failed each time. The 8 new ledger entries (RB-T028-01..08) should appear as Skipped on every run — confirm Skip + Trait persistence.
- **Task 085** (`repair-ledger.md` final write-up): document the 8 new ledger entries' classification rationale in the repair narrative.
- **Task 086** (final verification gate): the §4.3 / NFR-10 check should now pass without exception.

---

## Verification checklist

- [x] Both TRX files captured under `baseline/` (`post-028-unit-2026-05-31.trx`, `post-028-integration-2026-05-31.trx`)
- [x] Unit suite: 0 Failed, all 4 residuals now Skipped with `real-bug-pending-fix` trait
- [x] Integration suite: 0 Failed, all 47 residuals now Skipped with `real-bug-pending-fix` trait
- [x] 8 new RB-T028-NN entries appended to real-bug-ledger.md with schema-compliant fields
- [x] flaky-ledger.md created with zero-entries canonical schema doc
- [x] 0 archives (NFR-04 trivially satisfied)
- [x] 0 `src/`/`power-platform/`/`infra/`/`scripts/` modifications (NFR-01 satisfied)
- [x] Per-file diff <5% across all 8 test files (NFR-02 trivially satisfied)
- [x] Both projects build 0 errors with `-warnaserror` equivalent (NFR-11 satisfied)
- [x] Phase 2+3 exit gate revised from PARTIAL CLOSURE → FULL CLOSURE

---

*End of post-028 classification report.*
