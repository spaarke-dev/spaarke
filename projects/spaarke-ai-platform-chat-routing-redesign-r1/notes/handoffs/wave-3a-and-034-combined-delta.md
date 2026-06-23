# Wave 3-A + Task 034 — combined verification & delta

> **Date**: 2026-06-22
> **Author**: Main session (3 parallel code-only sub-agents + main session verify + ship)
> **Verdict**: ✅ **PASS** — 3 tasks ship; 1 test failure caught + fixed inline; 5 FR-13 functional gaps tracked for follow-up

---

## Summary

Three sub-agents executed in parallel (per the E2 pattern proven in task 036 bundle):
- **045** NodeDestination.Both — enum extension + JSON converter cases
- **046** DispatchResult extension — new properties with defaults (binary-compat)
- **034** Drift detection job — scaffolding + hash calculator + tests + DI

All three returned clean reports. One test failure (`ProcessAsync_PropagatesCancellation_WhenTokenIsCancelled`) was caught by the verification suite — eager `ct.ThrowIfCancellationRequested()` was inside the enumeration loop instead of at the top of `ProcessAsync`. Main session fixed inline; re-ran; all 52 tests now pass.

---

## Files in this bundle

### Task 045 — NodeDestination.Both

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs` | `Both` enum value added (line 64-74); converter Read+Write cases extended (lines 256, 269) |
| `tests/unit/Sprk.Bff.Api.Tests/Models/Ai/NodeRoutingConfigTests.cs` (EXTENDED) | 4 new tests (Both roundtrip; 4-existing roundtrip via Theory; null→Chat per FR-14f; unknown-fallback regression) |

### Task 046 — DispatchResult extension

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/DispatchResult.cs` | Record extended with `NodeDestination` (default Chat) + `WidgetType` (default null). Fully-qualified type names used because parameter name `NodeDestination` shadows the type inside parameter list (avoids CS0119) |
| `tests/unit/Sprk.Bff.Api.Tests/Models/Ai/DispatchResultTests.cs` (NEW) | 5 tests covering default construction, NoMatch static, explicit values, regression for existing dispatcher-style construction, `with`-expression semantics |

### Task 034 — Drift detection scaffolding

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/IPlaybookEmbeddingHashCalculator.cs` (NEW, 42 lines) | Interface; binding contract — indexer AND drift job consume |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingHashCalculator.cs` (NEW, 47 lines) | `internal sealed`; calls `PlaybookEmbeddingService.ComposeContentText(document)`; SHA-256 hex lowercase |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexDriftDetectionJob.cs` (NEW, ~250 lines) | `IJobHandler` (mirrors `InvoiceExtractionJobHandler` pattern); `JobTypeName = "PlaybookIndexDriftDetection"`; ADR-015 telemetry (counts + durationMs + tenantId; NO content) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` | Registered hash calculator (Singleton) + drift job (Scoped, as `IJobHandler`) — both UNCONDITIONAL per CLAUDE.md §10 F.1 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingHashCalculatorTests.cs` (NEW, ~115 lines) | 6 tests pinning determinism, content-sensitivity, vector/id ignorance, format |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookEmbedding/PlaybookIndexDriftDetectionJobTests.cs` (NEW, ~210 lines) | 7 active + 3 Skip'd tests (each Skip carries the contract a future PR closing the gap must satisfy) |

### Main session inline fix (post-agent verification)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexDriftDetectionJob.cs` line 103 | Added `ct.ThrowIfCancellationRequested();` immediately after `ArgumentNullException.ThrowIfNull(job);` — defensive eager cancellation check before any enumeration begins. Fix for the empty-enumeration cancellation test case |

---

## Verification results

| Check | Result |
|---|---|
| BFF build | ✅ 0 errors, 16 pre-existing warnings (unchanged baseline) |
| New tests (NodeRoutingConfig + DispatchResult + PlaybookEmbeddingHashCalculator + PlaybookIndexDriftDetectionJob) | ✅ 52/55 pass + 3 Skipped (documented FR-13 contract gaps) — Duration 51 ms |
| Phase 1 regression suite | ✅ 10/10 pass in 187 ms |
| BFF publish (compressed) | ✅ 46.09 MB (+0.01 MB delta vs task 036; +1.34 MB cumulative since task 032 baseline; 13.91 MB under NFR-01 ceiling) |
| ADR-013 / 014 / 015 / 029 / 010 | ✅ no violations (sub-agent reports + main session review) |
| CLAUDE.md §10 F.1 asymmetric-registration | ✅ all 3 new registrations unconditional (hash calculator, drift job, validator from task 036) |
| CLAUDE.md §11 Component Justification | ✅ each new component (hash calculator interface, drift job, validator) extends or fills a stated FR — articulated in agent reports |

---

## Task 034 — 5 FR-13 functional gaps flagged for follow-up

The drift-detection sub-agent honestly reported that the JOB SCAFFOLDING is complete but the FUNCTIONAL DATA PATH is gated by 5 missing pieces in OTHER services. Each Skip'd test captures the contract the follow-up must satisfy.

| # | Gap | Contract for follow-up |
|---|---|---|
| 1 | `IPlaybookService` has no tenant-wide enumeration | Add `IAsyncEnumerable<PlaybookResponse> ListAllActivePlaybooksAsync(CancellationToken)` (or similar) |
| 2 | `PlaybookResponse` doesn't expose `sprk_indexstatus`, `sprk_indexhash`, `sprk_lastindexedat` | Extend like task 036 did for `JpsMatchingMetadata` — add 3 properties; extend $select strings (lines 176, 366); extend projections (195, 418) |
| 3 | No tracking-field write path on `IPlaybookService` | Add `UpdateIndexStatusAsync(Guid id, int statusCode, string? lastError, CancellationToken)` — mirror `UpdatePlaybookAsync` OData PATCH pattern |
| 4 | Per-tenant scoping flows via `JobContract.SubjectId` — producer is expected to enqueue per-tenant | Producer implementation deferred (no nightly producer infrastructure exists yet) |
| 5 | `PlaybookEmbeddingService.IndexPlaybookAsync` doesn't write `sprk_indexhash` at index time → drift compares against `null` and skips everything | Route `IPlaybookEmbeddingHashCalculator.ComputeHash(document)` through to `UpdateIndexStatusAsync` at successful index completion |

Net effect right now: drift job will run, scan playbooks, but skip 100% of them because `GetStoredIndexHash` returns `null` (placeholder behavior). This is **deliberately fail-safe** — no false-positive `Stale` flips can occur until the data path is complete.

Recommended follow-up task (file as **034b** or "Wire FR-13 functional data path"): bundle gaps 1+2+3+5 (~3-5h) into a single PR like the task 032+036 bundle. Gap 4 (producer) is separate and likely belongs to infra/operations.

---

## Sibling-task status

- Tasks 045 + 046 + 034: ✅ landed; tested; ADR-clean
- Task 047 (PlaybookDispatcher populate): can be dispatched now (depends on 045 + 046 being in code, both ✅)
- Task 033 / 035: remain 🟡 PARTIAL-BLOCKED (Power Apps source-layout direction needed; orthogonal)
- Wave 2-D (tasks 037-040): unblocked; can run after Wave 3-A closes

---

## Related artifacts

- `notes/handoffs/036-validation-gate-and-032-loader-fix.md` — previous bundle (FR-10/FR-12)
- `notes/handoffs/032-loader-gap-and-036-bundling.md` — earlier honest-gap pattern this work followed
- POMLs: `tasks/045-*.poml`, `tasks/046-*.poml`, `tasks/034-*.poml`, `tasks/047-*.poml`
