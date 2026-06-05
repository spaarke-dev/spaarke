# Post-Wave-1.3 Authoritative Baseline — 2026-05-31

> **Source TRX**: [`post-019-verify-2026-05-31.trx`](post-019-verify-2026-05-31.trx) (captured 2026-05-31 by task 019 as the formal verification gate)
> **Confirms**: [`post-018-passing-delta-2026-05-31.md`](post-018-passing-delta-2026-05-31.md) (task 018's reported post-edit measurement)
> **Authority**: Per §6.3 binding rule, this file is the new authoritative measurement that Phase 2+3 tasks MUST cite as their starting state.
> **Scope**: `tests/unit/Sprk.Bff.Api.Tests/` ONLY (integration project `Spe.Integration.Tests` is still compile-broken; see [`integration-build-errors-2026-05-31.txt`](integration-build-errors-2026-05-31.txt) — task 024 absorbs that fix).

---

## Headline result

| Metric | Value | Source |
|---|---:|---|
| Total tests | **6,034** | `post-019-verify-2026-05-31.trx` |
| **Passed** | **5,753 (95.3%)** | same |
| **Failed** | **172 (2.85%)** | same |
| Skipped | **109 (1.81%)** | same |
| Build errors | **0** | `dotnet build -c Release` |
| Build warnings | **17 (pre-existing)** | same — Kiota CVE + obsolete API + CS1998 unchanged |
| Duration | **1m 13s** | same — on RalphSchroeder Windows dev box (Release) |

---

## Verification chain — Phase 0 → Wave 1.1a → Wave 1.3

Per §6.3, all Phase 1+ tasks MUST cite measured numbers from this chain (NOT design.md §3 stale figures):

| Checkpoint | TRX file | Total | Passed | Failed | Skipped | Δ Failed vs. Phase 0 |
|---|---|---:|---:|---:|---:|---:|
| **Phase 0 baseline** (task 001) | `test-baseline-2026-05-31.trx` | 6,021 | 5,572 | 342 | 107 | — (anchor) |
| **Post-Wave-1.1a** (task 014) | `post-wave1.1a-runtime-2026-05-31.trx` | 6,020 | 5,627 | 284 | 109 | **−58 (−17.0%)** |
| **Pre-018** (task 018 pre-edit) | `pre-018-baseline.trx` | 6,034 | 5,641 | 284 | 109 | −58 (statistical drift +14 total) |
| **Post-018 / Post-Wave-1.3** (task 018 post-edit) | `post-018-measure.trx` | 6,034 | 5,753 | 172 | 109 | **−170 (−49.7%)** |
| **Verification (task 019)** | `post-019-verify-2026-05-31.trx` | **6,034** | **5,753** | **172** | **109** | **−170 (−49.7%)** ✅ |

**Cumulative Phase 0 → Wave 1.3 reduction: 342 → 172 = −170 / −49.7%.** Half the original failure surface has been cleared; remaining 172 are all post-host-build (assertion-level) failures absorbed by Phase 2+3 tier tasks per [`post-018-passing-delta-2026-05-31.md`](post-018-passing-delta-2026-05-31.md) §B.

---

## Verification gate decision (per task 019 POML)

Per task 019 `<goal>` SUCCESS-path criteria:

| Criterion | Required | Measured (task 019) | Result |
|---|---|---|---|
| Passed ≥ 4,844 (Phase 0 design baseline) | ≥ 4,844 | **5,753** | ✅ exceeded by +909 |
| Passed ≥ 5,627 (post-Wave-1.1a actual baseline) | ≥ 5,627 | **5,753** | ✅ exceeded by +126 |
| Total ≥ post-compile total (6,020) | ≥ 6,020 | **6,034** | ✅ exceeded by +14 |
| Zero passing-test regressions | 0 | **0** | ✅ confirmed |
| 7 inventory keys present in factory | 7 / 7 | **7 / 7** | ✅ all verified by grep |
| Build clean | 0 errors | **0 errors / 17 pre-existing warnings** | ✅ |
| No `src/`/`power-platform/`/`infra/`/`scripts/` modifications | 0 | **0** | ✅ confirmed via `git status` |

**Verdict: SUCCESS — isolation envelope CLOSED; repair tracks (Phase 2+3 + Phase 4) CLEARED to resume.**

---

## Regression analysis — zero regressions confirmed

The skipped count is unchanged (109 in all three measurements). With pre-018 at 5,641 passed and post-018/019 at 5,753 passed, the +112 delta consists entirely of `Failed → Passed` transitions. Per [`post-018-passing-delta-2026-05-31.md`](post-018-passing-delta-2026-05-31.md) §C, an explicit cluster-level regression check (post ∖ pre clusters) returned ∅ — no cluster has any newly-failing tests post-018.

The `services.RemoveAll<IHostedService>()` guard at line 162 of `CustomWebAppFactory.cs` absorbed any new background services that the newly-present config keys may have unlocked (per task 017 §D analysis). Task 018's additive changes did NOT inadvertently activate any new code paths.

**No partial revert required.**

---

## §4.5 additive-only compliance (re-confirmed by task 019)

```
$ git diff --stat tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs
 tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs | 19 ++++++++++++++++++-
 1 file changed, 18 insertions(+), 1 deletion(-)
```

- **18 insertions** = 7 dict entries + 11 lines of section comments/blank lines
- **1 deletion** = the existing `["ManagedIdentity:ClientId"]` line was rewritten only to append `,` (no semantic change). Single smallest-possible additive growth point in C# dictionary-initializer syntax.
- **0 method-signature changes**, **0 existing dict entries removed/modified**, **0 logic restructuring**
- **Net growth**: +17 LOC (+9.9%) — below design.md §3.4 estimate of "~30 LOC growth to ~200 LOC".

The 7 keys verified present in the working tree:

| # | Key | File:Line | Value |
|---:|---|---|---|
| 1 | `CosmosPersistence:Endpoint` | `CustomWebAppFactory.cs:112` | `"https://test.documents.azure.com:443/"` |
| 2 | `CosmosPersistence:DatabaseName` | `CustomWebAppFactory.cs:113` | `"spaarke-ai-test"` |
| 3 | `AgentService:Enabled` | `CustomWebAppFactory.cs:119` | `"false"` (ADR-018 kill-switch OFF) |
| 4 | `AgentService:Endpoint` | `CustomWebAppFactory.cs:120` | `"https://test.services.ai.azure.com/api/projects/test-project"` |
| 5 | `AgentService:AgentId` | `CustomWebAppFactory.cs:121` | `"test-agent-id"` |
| 6 | `AgentService:MaxConcurrency` | `CustomWebAppFactory.cs:122` | `"4"` |
| 7 | `AgentService:ThreadCacheExpiryMinutes` | `CustomWebAppFactory.cs:123` | `"60"` |

---

## NFR compliance proof

| NFR | Requirement | Verification | Status |
|---|---|---|---|
| **NFR-01** | No `src/`/`power-platform/`/`infra/`/`scripts/` changes | `git status --short` shows only `tests/unit/...` + `projects/...` | ✅ |
| **NFR-02** | Measurement only (no test edits in this task) | This task wrote only baseline + project docs | ✅ |
| **NFR-03** | No BFF DI registration count increase | Task 018 added config keys only; zero `services.AddXxx(...)` | ✅ |
| **NFR-07** | Anti-parallelism (ISOLATED Wave 1-C-isolated) | Orchestrator brief confirms task 019 ran ISOLATED after task 018 | ✅ |
| **NFR-09** | `<repair-not-rewrite>true</repair-not-rewrite>` | Task 019 POML metadata + verbatim verification only | ✅ |
| **§4.5** | Factory extended additively only | 18 insertions / 1 trailing-comma deletion via `git diff --stat` | ✅ |
| **§6.3** | Cite measured numbers, not design.md §3 stale figures | This file is the new authoritative measurement | ✅ |
| **§6.4** | Full suite run after factory change | Task 019 IS the post-factory full suite (this run) | ✅ |
| **ADR-010** | DI minimalism preserved | No new DI registrations from factory edit | ✅ |
| **ADR-018** | Kill switches | `AgentService:Enabled=false` keeps Foundry agent disabled in tests | ✅ |

---

## P1.C track exit declaration

**The P1.C (factory extension) track is COMPLETE.** Tasks 017 + 018 + 019 are all fully executed and verified:

- **Task 017** (inventory): produced `notes/spikes/factory-config-gaps.md` identifying 7 missing keys
- **Task 018** (factory edit): applied the 7 additive entries; measured −112 failures (5,641 → 5,753 passing)
- **Task 019** (verification gate, this task): re-measured 5,753 / 172 / 109 — matches task 018's report exactly; zero regressions confirmed

**Isolation envelope (NFR-07) CLOSED 2026-05-31** by this task. All subsequent Phase 1 (P1.A/B/D/E) verification gates + Phase 2+3 tier-batch tasks + Phase 4 governance tasks are CLEARED to resume parallel execution per the orchestrator's wave plan.

---

## Forward reference — what Phase 2+3 measures against

Phase 2+3 tier-batch tasks (030–074) MUST cite this baseline:

> **Sprk.Bff.Api.Tests starting state (post-Wave-1.3, 2026-05-31): 6,034 total / 5,753 passing / 172 failing / 109 skipped.**

The 172 remaining failures are clustered as per [`post-018-passing-delta-2026-05-31.md`](post-018-passing-delta-2026-05-31.md) §B (Integration.Workspace.* 54, Services.Ai.Safety 11, Api.Office.* 10, Integration.CommunicationIntegrationTests 9, etc. — all distributed across Phase 2+3 absorbing tasks already in the TASK-INDEX).

The cross-cluster `CosmosPersistence:Endpoint` symptom in `Spe.Integration.Tests.IntegrationTestFixture.cs` (~97 integration failures) is **out of scope for task 019** and absorbed by Phase 2+3 P23.I task 062 per [`notes/spikes/factory-config-gaps.md`](../notes/spikes/factory-config-gaps.md) §E.

---

## Verification checklist

- [x] `dotnet build -c Release` returned 0 errors / 17 pre-existing warnings
- [x] Full test suite ran in 1m 13s (well under the 30-min timebox)
- [x] TRX captured: `baseline/post-019-verify-2026-05-31.trx`
- [x] Counts match task 018's post-edit report exactly (5,753 / 172 / 109)
- [x] Zero passing-test regressions (post ∖ pre cluster set is ∅ per §C)
- [x] 7 inventory keys present in `CustomWebAppFactory.cs` (verified by grep)
- [x] `git diff --stat` confirms additive-only (18 insertions / 1 trailing-comma deletion)
- [x] `git status` shows no `src/`/`power-platform/`/`infra/`/`scripts/` changes
- [x] Authoritative baseline document written for Phase 2+3 to cite
- [x] P1.C track exit declared; isolation envelope (NFR-07) CLOSED
