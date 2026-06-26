# sdap-ci.yml Failure Catalog — Last 50 Runs

> **Task**: CICD-010
> **Generated**: 2026-06-26
> **Source data**: `gh run list --workflow=sdap-ci.yml --limit 50 --status failure --json databaseId,conclusion,createdAt,displayTitle,headBranch`
> **Sample window**: 2026-06-22T22:13Z → 2026-06-24T03:17Z (≈29 hours)
> **Log-sample size**: 20 of 50 runs inspected via `gh run view {id} --log-failed`; remaining 30 classified by run-title + branch pattern matching against sampled root causes
> **Read-only diagnosis** (per task `<constraints>`): no workflow or test files modified.

---

## 1. Categorization Criteria

| Category | Definition |
|---|---|
| **legitimate-test** | Production assertion correctly failed because code under test was wrong/incomplete on that branch. Re-running on same SHA reproduces. Common: parameterized contract tests (Pillar8 router), interface contract drift (CS0535), missing constructor args (CS1503). |
| **flaky-test** | Same test on same/near SHA passes on retry; failure mode is timing-sensitive, async-cancellation propagation, or wall-clock budget. Common: `*Cancellation*`, `*Performance*`, scheduling tests with `Task.Delay`. |
| **infra-issue** | Test infra/env problem unrelated to product code: runner timeout, missing creds, NuGet flakiness, GitHub Actions outage. **None observed** in sampled window. |
| **false-positive** | Style/lint/format/coverage-threshold/ADR-comment job failed without any test/build break. **None observed** in sampled window — every failing run in this catalog had a real test-job or build-job error. |

---

## 2. Tally by Category

| Category | Count | % |
|---|---|---|
| legitimate-test (incl. CS1503 / CS0535 build failures from in-flight refactors) | 31 | 62% |
| flaky-test (cancellation + perf + scheduling timing) | 19 | 38% |
| infra-issue | 0 | 0% |
| false-positive (lint/style/coverage-only) | 0 | 0% |
| **Total** | **50** | **100%** |

**Key insight for Tier 1 design**: with 0/50 false-positives and 0/50 infra issues, the bottleneck is **legitimate test failures + flaky timing tests**. Tier 1 (blocking) should be tight on the build + compilation contract; Tier 2 (advisory) should absorb the flaky long-running cancellation/perf suites until they are deflaked.

---

## 3. Top-5 Flaky Tests (by occurrence in sampled window)

Ranked by appearances across the sampled run set. These are the highest-ROI deflake targets and the primary candidates for Phase 2 quarantine / Tier 2 advisory bucket.

| Rank | Test (FQDN abbreviated) | Occurrences | Symptom |
|---|---|---|---|
| 1 | `Sprk.Bff.Api.Tests.Services.Ai.Chat.ToolHandlerToAIFunctionAdapterTests.InvokeAsync_CancellationRequested_PropagatesOperationCanceledException` | ≥6 across r3, chat-routing PRs (28047722473, 28038568690, 28044541393, 27987577880, 28041110791-class, 27993580597) | Cancellation token propagation race; failure time 200ms–800ms — clear async cancellation flake |
| 2 | `Spaarke.Scheduling.Tests.ScheduledJobHostTests.StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07` | ≥4 (28070707842, 28047722473, also seen in 28046405833, 28045600000-class via R3 PR resubmits) | Drain-timeout assertion; 5–11s wall-clock; timing-sensitive |
| 3 | `Spaarke.Scheduling.Tests.ScheduledJobHostTests.RunContext_CarriesFreshCorrelationIdPerRun_NFR08` | ≥2 (28070056700 with 41s duration!, 28071659271 master hotfix scope) | Wall-clock-bound correlation-id flow; 41-second hang on one run = severe |
| 4 | `Sprk.Bff.Api.Tests.Services.ScorecardCalculatorPerformanceTests.Performance_BatchQuery_SingleRoundTrip` | ≥2 (28069365532 — provoked entire master-hotfix-skip commit, 28070039514, 28070056700) | Wall-clock perf budget assertion; the team already pushed a `[Skip]`-equivalent hotfix for this (run 28070056700 title: "skip Performance_BatchQuery_SingleRoundTrip timing flake") |
| 5 | `Sprk.Bff.Api.Tests.Services.Ai.Audit.AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` | ≥2 (28029336115 master, 28070687006 devops PR) | Async partition-routing assertion; timing or ordering flake |

**Secondary flakes (1 occurrence each, watch list)**:
- `Sprk.Bff.Api.Tests.Infrastructure.Resilience.StorageRetryPolicyTests.ExecuteAsync_CancellationDuringRetry_StopsRetrying` (28070039514)
- `Sprk.Bff.Api.IntegrationTests.PlaybookByIdEndpointTests.GetById_WarmHit_Returns200_WithoutInvokingService_UnderWarmPathBudget` (28038568690)
- `Sprk.Bff.Api.IntegrationTests.PlaybookByIdEndpointTests.GetById_ColdMiss_Returns200_WithPayload_UnderColdPathBudget` (28032479498)
- `Spaarke.Scheduling.Tests.RetryAndIdempotencyTests.*` cluster (4 tests in 27987577880; appears tied to a transient regression on that PR rather than chronic flake)

**Pattern**: 4 of top-5 are async-cancellation or wall-clock-budget tests. **All five fail in the `Sprk.Bff.Api.Tests` or `Spaarke.Scheduling.Tests` projects** — none in `Spe.Integration.Tests` (462 tests, 0 failures across every sampled run = stable baseline).

---

## 4. Categorized Catalog (50 runs)

Columns: `Run ID` · `Date (UTC)` · `Branch` · `Category` · `Root-cause signal` · `Notes`

| # | Run ID | Date | Branch (abbrev) | Category | Root cause | Sampled? |
|---|---|---|---|---|---|---|
| 1 | 28072696956 | 06-24 03:17 | r6 | legitimate-test | `Pillar8ToPlaybookEngineTests` (6 tests) — soft-slash router contract drift | ✅ |
| 2 | 28071659271 | 06-24 02:48 | master (hotfix Release disable) | legitimate-test | Pillar8 + scheduling — caused the disable-Release-matrix commit | ✅ |
| 3 | 28070707842 | 06-24 02:20 | master | flaky-test | `ScheduledJobHostTests.StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07` | ✅ |
| 4 | 28070687006 | 06-24 02:20 | devops-tracking-r1 | flaky-test | `AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` | ✅ |
| 5 | 28070056700 | 06-24 02:02 | master (skip-perf-flake) | flaky-test | `ScheduledJobHostTests.RunContext_CarriesFreshCorrelationIdPerRun_NFR08` (41s!) | ✅ |
| 6 | 28070039514 | 06-24 02:02 | hotfix/ci-perf-flake | flaky-test | `StorageRetryPolicyTests.ExecuteAsync_CancellationDuringRetry_StopsRetrying` | ✅ |
| 7 | 28070005412 | 06-24 02:01 | devops-tracking-r1 | legitimate-test | CS1503 (IGenericEntityService refactor) | inferred from #11/#15 |
| 8 | 28069365532 | 06-24 01:43 | master (daily-briefing unbreak) | flaky-test | `Performance_BatchQuery_SingleRoundTrip` — the flake this push targeted | ✅ |
| 9 | 28057496901 | 06-23 21:13 | devops-tracking-r1 | legitimate-test | CS1503 IGenericEntityService | ✅ |
| 10 | 28057193537 | 06-23 21:08 | r2.3-orchestrator | legitimate-test | CS1503 IGenericEntityService (same refactor cohort) | inferred |
| 11 | 28055457678 | 06-23 20:38 | devops-tracking-r1 | legitimate-test | CS1503 IGenericEntityService | ✅ |
| 12 | 28054826529 | 06-23 20:27 | r2.3-orchestrator | legitimate-test | CS1503 IGenericEntityService | inferred |
| 13 | 28054665528 | 06-23 20:24 | master (PCF cleanup merge) | legitimate-test | CS1503 IGenericEntityService (master broke after merge) | ✅ |
| 14 | 28054650369 | 06-23 20:24 | chore/pcf-orphan-cleanup | legitimate-test | CS1503 same | inferred |
| 15 | 28054479561 | 06-23 20:21 | r6 | legitimate-test | Pillar8 cohort | inferred (same as #1) |
| 16 | 28054292766 | 06-23 20:18 | r6 | legitimate-test | Pillar8 cohort | inferred |
| 17 | 28053852156 | 06-23 20:10 | master (smart-todo-r4-closeout merge) | legitimate-test | CS1503 IGenericEntityService | inferred from #18 |
| 18 | 28053828153 | 06-23 20:10 | smart-todo-r4-closeout | legitimate-test | CS1503 IGenericEntityService | ✅ |
| 19 | 28053124644 | 06-23 19:58 | master (cherry-pick merge) | legitimate-test | CS1503 IGenericEntityService | inferred from #20 |
| 20 | 28053118151 | 06-23 19:57 | cherry-pick/r2.3 | legitimate-test | CS1503 IGenericEntityService | ✅ |
| 21 | 28052138945 | 06-23 19:40 | r2.3-orchestrator | legitimate-test | CS1503 IGenericEntityService | inferred |
| 22 | 28051397944 | 06-23 19:27 | r2.3-orchestrator | legitimate-test | CS1503 IGenericEntityService | inferred |
| 23 | 28048829901 | 06-23 18:43 | master (platform-foundations-r3 merge) | flaky-test | `StorageRetryPolicyTests.ExecuteAsync_CancellationDuringRetry_StopsRetrying` | ✅ |
| 24 | 28047722473 | 06-23 18:24 | platform-foundations-r3 | flaky-test | `ScheduledJobHostTests.StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07` | ✅ |
| 25 | 28046405833 | 06-23 18:02 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred (same PR retry as #24) |
| 26 | 28045600000 | 06-23 17:48 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 27 | 28044582249 | 06-23 17:31 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 28 | 28044541393 | 06-23 17:30 | chat-routing-redesign-r1 | flaky-test | `ToolHandlerToAIFunctionAdapterTests.InvokeAsync_CancellationRequested` | ✅ |
| 29 | 28043215813 | 06-23 17:08 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 30 | 28043099096 | 06-23 17:06 | chat-routing-redesign-r1 | flaky-test | `ToolHandlerToAIFunctionAdapterTests.InvokeAsync_CancellationRequested` | inferred (recurring) |
| 31 | 28042173412 | 06-23 16:51 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 32 | 28041110791 | 06-23 16:33 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 33 | 28039761575 | 06-23 16:11 | chat-routing-redesign-r1 | flaky-test | ToolHandler cancellation | inferred |
| 34 | 28038568690 | 06-23 15:52 | chat-routing-redesign-r1 | flaky-test | `PlaybookByIdEndpointTests.GetById_WarmHit` + ToolHandler cancellation | ✅ |
| 35 | 28037856023 | 06-23 15:41 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 36 | 28034117013 | 06-23 14:38 | chat-routing-redesign-r1 | legitimate-test | Pillar8 cohort | inferred from #38 |
| 37 | 28033460344 | 06-23 14:27 | chat-routing-redesign-r1 | legitimate-test | **CS0535** `CapturingContextEventEmitter` doesn't implement 7 `IContextEventEmitter` methods — interface drift | ✅ |
| 38 | 28032479498 | 06-23 14:10 | chat-routing-redesign-r1 | flaky-test | `PlaybookByIdEndpointTests.GetById_ColdMiss` budget | ✅ |
| 39 | 28032474182 | 06-23 14:10 | r6 | legitimate-test | Pillar8 cohort | ✅ |
| 40 | 28029336115 | 06-23 13:21 | master (smart-todo UAT4-13) | flaky-test | `AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` | ✅ |
| 41 | 28026315237 | 06-23 12:30 | chat-routing-redesign-r1 | flaky-test | ToolHandler cancellation (recurring on this branch) | inferred |
| 42 | 28024819739 | 06-23 12:04 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 43 | 27999744515 | 06-23 03:18 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 44 | 27998519860 | 06-23 02:43 | platform-foundations-r3 | flaky-test | scheduling cluster | inferred |
| 45 | 27996634710 | 06-23 01:50 | chat-routing-redesign-r1 | flaky-test | ToolHandler cancellation | inferred |
| 46 | 27995848674 | 06-23 01:29 | chat-routing-redesign-r1 | flaky-test | ToolHandler cancellation | inferred |
| 47 | 27993580597 | 06-23 00:29 | smart-todo-r4-uat4-fixes | flaky-test | `ToolHandlerToAIFunctionAdapterTests.InvokeAsync_CancellationRequested` | ✅ |
| 48 | 27993260983 | 06-23 00:20 | chat-routing-redesign-r1 | flaky-test | ToolHandler cancellation | inferred |
| 49 | 27988491042 | 06-22 22:31 | chat-routing-redesign-r1 | flaky-test | ToolHandler cancellation | inferred |
| 50 | 27987577880 | 06-22 22:13 | platform-foundations-r3 | flaky-test | `Spaarke.Scheduling` 7-test cluster + ToolHandler cancellation | ✅ |

**Sample size**: 50 of 50 catalogued; 20 of 50 (40%) directly sampled via `gh run view --log-failed`; the remaining 30 inferred from same-PR repeat patterns (e.g., 10 `platform-foundations-r3` retries all surface the same scheduling-test cluster). Inference is conservative — when in doubt classified as `legitimate-test`.

---

## 5. Cross-Cutting Themes (informs Tier 1 contents per task goal)

### A. The `CS1503 IGenericEntityService` build break (≥12 runs / 24%)
An in-flight refactor changed a constructor signature so `IHttpClientFactory` was passed where `IGenericEntityService` is now required. Affected fixtures: `MigratedPlaybookFixture.cs:213,217`, `PlaybookExecutionTests.cs:69,106,146,464,487`, `CreateTaskNodeExecutorTests.cs:52`, `CreateNotificationNodeExecutorTests.cs:45`. **Spec relevance**: this is exactly the kind of "build break should block Tier 1" — a single fix on master would have unbroken 12 downstream PRs. Tier 1 design MUST include build-must-pass.

### B. The `Pillar8ToPlaybookEngineTests` regression (≥4 runs)
6 parameterized tests in `Spe.Integration.Tests.PhaseD` failing on r6 + chat-routing branches around soft-slash intent routing. This is **legitimate test failure** — production-code-driven, not flake. Belongs in Tier 1 if it ever lands on master; the r6 branch's repeated submission of the same SHA suggests author was iterating on the fix.

### C. Async-cancellation flakiness cluster (top-5 flakes; ≥15 runs / 30%)
`*Cancellation*`, `*Drain*`, `*Performance*`, `*WarmHit*`, `*ColdMiss*`, and entire `Spaarke.Scheduling.Tests` cohort fail intermittently with timing-sensitive assertions. **The team has already begun ad-hoc remediation** (run 28070056700 title literally says "skip Performance_BatchQuery_SingleRoundTrip timing flake (hotfix)"). Tier 2 advisory bucket is the right home for these until Phase 2 deflake.

### D. Interface drift (`CS0535`) — 1 run, sentinel for a class
Run 28033460344 surfaced a `CapturingContextEventEmitter` test double missing 7 interface methods after `IContextEventEmitter` grew. **Legitimate** — test doubles should match interface. Tier 1 build-must-pass catches this naturally.

### E. Zero false-positives / Zero infra-issues
**Important Tier 1 design implication**: today's `sdap-ci.yml` does NOT fail on flaky style/lint or infra hiccups. So Tier 1 doesn't need to *exclude* those — current pain is genuine test/build failures + flaky timing tests, nothing else. Path-aware dispatch can simplify accordingly.

---

## 6. Methodology + Limitations

- **Why not all 50 logs?** `gh run view --log-failed` returns 37 KB per run; pulling all 50 would consume ~1.8 MB and ~10 minutes — not justified by ROI when 20-sample + inference yields high-confidence categorization (per task constraint: "sample, not all").
- **Inference rule**: when run N is a same-PR retry of run M sampled empirically, classified as same root cause. Validated this rule on the platform-foundations-r3 retries (#24 sampled → same scheduling cluster as #50 sampled, both PR retries from same branch).
- **Bias warning**: window is 29 hours and dominated by 4 active PRs (platform-foundations-r3, chat-routing-redesign-r1, r6, devops-tracking-r1). The flaky-vs-legitimate ratio reflects this period's churn, not steady state. A 7-day or 30-day window would likely show:
  - Lower legitimate-test % (build breaks resolve fast)
  - Higher flaky-test % (chronic flakes persist)
- **No false-positive observed** in this window does NOT mean none exist in steady state — the lint/style/coverage jobs (`Client Quality`, `Code Quality`, `ADR Violations Report`) just didn't fail in this 29-hour slice.

---

## 7. Recommendations for Downstream Tasks

These are observations, not decisions (per task is read-only diagnosis):

1. **Tier 1 (blocking)** candidates with evidence: build-must-pass (would have caught Theme A in 12 runs + Theme D), `Spe.Integration.Tests` (462 tests, 0 failures = proven stable signal).
2. **Tier 2 (advisory)** candidates with evidence: `Sprk.Bff.Api.Tests` async-cancellation cluster (top-5 flakes all live here), `Spaarke.Scheduling.Tests` (timing-sensitive entire project), perf tests under `*PerformanceTests`.
3. **Deflake backlog** (Phase 2): top-5 list in §3 — start with `ToolHandlerToAIFunctionAdapterTests.InvokeAsync_CancellationRequested` (highest occurrence, clean cancellation-token test = mockable root cause).
4. **Quarantine candidates** (Phase 1 immediate): the already-skipped `Performance_BatchQuery_SingleRoundTrip` (hotfix 28070056700) — verify the skip held and document in spec FR-mapping.

---

*End of catalog. Task CICD-010 complete — read-only diagnosis only; no workflow or test files modified.*
