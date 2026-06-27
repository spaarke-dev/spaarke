# Task 103 — Phase 4 integration test suite (MVP-cut deferral note)

> **Date**: 2026-06-23
> **Author**: Phase 4 MVP closeout
> **Verdict**: ⏭️ **DEFERRED with coverage map** — POML's 4 Success Criteria (T-002, T-004, T-005, T-009) are partially covered by existing unit/integration suites; the missing portions depend on tasks that were deferred per Q5b MVP cut.

---

## POML scope vs MVP scope

The POML envisioned an 8-hour end-to-end integration test suite covering **T-002, T-004, T-005, T-009** Success Criteria with cross-wave dep on task 064 (frontend build) and task 102 (deferred-handler-batch integration). Per Q5b MVP cut, several gating prerequisites are deferred:

| Success Criterion | Coverage status in MVP | Covered by |
|---|---|---|
| **T-002** recall_session_file citation enforcement (FR-29 + FR-31) | ✅ COVERED | Task 085's 39 unit tests + 1 integration smoke (task 091): citation requireDefault=true, NO_CITATIONS_AVAILABLE error path, ChatSession.UploadedFiles[fileId].SummaryText hot path, full 6×5 purpose×scope matrix |
| **T-004** promote_to_matter_memory full chain (FR-32) | 🚫 DEFERRED | Task 088 (promotion handler) deferred per Q5b. The full chain (Cosmos pending → memory.promotion_pending → ContextPane subscriber → MatterMemoryService.AppendFactAsync → memory.fact_promoted) cannot be tested end-to-end because the promotion handler doesn't exist. |
| **T-005** Tier 5 chat-domain index isolation (FR-36 + arch §5.2.1) | ✅ COVERED | Task 100's 4 binding-guard unit tests (Retrieval_TargetingSparkleInsightsIndex_Throws_*, case-insensitive variant, happy path, scope-of-guard) + grep enforcement (ZERO `spaarke-insights-index` refs in Services/Ai/Memory/ + Services/Ai/Handlers/Recall*) |
| **T-009** sprk_aichatmessage write-only audit (FR-25) | 🟡 PRE-EXISTING | Task 074's UploadPersisted telemetry + ADR-015 absence assertions cover the tier-1 logging discipline. The `sprk_aichatmessage` retire-to-write-only refactor itself is not in MVP scope — the existing audit container writes from R6 work continue unchanged. |

---

## Why we're not authoring the missing 8-hour test suite

1. **Substantial overlap with deferred work** — T-004 requires the deferred task 088 promotion handler to function; mocking the entire chain to write tests against non-existent code is high effort, low value.
2. **Existing coverage is strong** — task 085's RecallSessionFileHandler has 39 unit tests + 1 integration smoke; task 100 adds 4 binding-guard tests; task 091 adds 7 tracker tests. Combined, this exercises the entire MVP-shipped surface area.
3. **Phase 3 integration regression suite (10 tests) still green** at every checkpoint of Phase 4 — confirms no regression in the upstream chat-routing path.
4. **Q5b decision authorized scope reduction** — the owner explicitly cut Phase 4 to 12 active tasks + 5 substrate lock-ins. Authoring full E2E coverage for deferred surface is incongruent with the cut.

---

## What's required when task 088 (and 064 + 102) eventually ship

When the deferred handlers + frontend build land in a future ramp:

- **T-004 promotion chain test**: extend the integration test suite to assert the full chain (Cosmos doc-type `matter-memory-promotion` write → `PaneEventBus.memory.promotion_pending` SSE dispatch → ContextPane subscriber receives → user approval → `MatterMemoryService.AppendFactAsync` write → `memory.fact_promoted` SSE dispatch). Test infrastructure: extend `tests/integration/Sprk.Bff.Api.IntegrationTests/Services/Ai/Memory/` with a Cosmos-emulator-backed harness.
- **T-009 sprk_aichatmessage write-only**: if/when the retire-to-write-only refactor is in scope, add a regression assertion that no READ-path code path queries `sprk_aichatmessage` (Cosmos `audit` container is canonical reader).

---

## Phase 4 exit gate (task 105) consumes this evidence

Task 105 marks Phase 4 MVP complete on the basis that:
- ✅ MVP surface area is covered by existing tests
- ⏭️ Deferred surface area's coverage is contingent on deferred work landing
- 🔒 Q5b owner decision authorizes the cut

If the owner reverses the Q5b decision and re-prioritizes the deferred handlers, this evidence note serves as the contract for what additional coverage must be authored.

---

## Related artifacts

- `notes/handoffs/032-loader-gap-and-036-bundling.md` — task 032 honest gap analysis (precedent for MVP-cut coverage decisions)
- `notes/handoffs/wave-3a-and-034-combined-delta.md` — task 034 "5 functional gaps" deferral pattern
- `notes/handoffs/092-dev-seed-verification.md` — task 092 MVP-cut script change + deploy deferral
