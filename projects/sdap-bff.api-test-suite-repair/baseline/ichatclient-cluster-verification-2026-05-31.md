# IChatClient Streaming Cluster — Verification Gate Report (Task 032)

> **Wave**: Phase 2+3 Wave 2.4 verification gate
> **Date**: 2026-05-31
> **Author**: Task 032 (P23.A3 IChatClient cluster verification gate)
> **Rigor**: STANDARD (verification-only; NFR-02 binds — no test edits in this task)
> **Source TRX**: [`ichatclient-cluster-2026-05-31.trx`](ichatclient-cluster-2026-05-31.trx)

---

## Verdict

**IChatClient streaming cluster (tasks 015 + 016 + 030 + 031) is CONVERGED — zero failures in the cluster's authoritative scope.**

P23.A is **CLOSED** for the IChatClient-cluster outcome. The 8 surface-level failures observed in the broader `--filter "FullyQualifiedName~Streaming|FullyQualifiedName~ChatClient"` sweep are **out-of-cluster** — they belong to `Integration.SseStreamingIntegrationTests` (a separate SSE-streaming cluster owned by Phase 2+3 Integration tier tasks; last-touched by `ai-sprk-chat-r2`, not by 030/031).

---

## Cluster member verification

| Task | Asset | Wave | TASK-INDEX status | Verified state (this task) |
|---|---|---|---|---|
| **015** | `Mocks/AsyncEnumerableHelpers.cs` (363 LOC, 9 public members incl. `FakeChatClient`) | 1-A | ✅ Completed 2026-05-31 | ✅ File intact; `git status` clean; not touched by any Wave 2.x agent |
| **016** | `Mocks/AsyncEnumerableHelpersTests.cs` (14 `[Fact]` tests) | 1-B | ✅ Completed 2026-05-31 | ✅ **14/14 Pass** (re-verified this run; 102 ms) |
| **030** | `Services/Ai/Chat/StreamingWriteIntegrationTests.cs` (28 tests; replaced 21 LOC obsolete helpers with canonical `AsyncEnumerableHelpers`) | 23-A | ✅ Completed 2026-05-31 | ✅ **28/28 Pass** (re-verified this run; 100 ms) |
| **031** | (originally targeted `Services/Ai/Capabilities/Streaming*`) | 23-A | ✅ NO-OP 2026-05-31 — scope mismatch, no such files exist; CapabilityRouter cluster owned by task 053 | ✅ NO-OP confirmed by absence of the file path; escalation already filed |

**Cluster scope total**: 42 tests across 015/016/030 — **42/42 Pass / 0 Failed / 0 Skipped**.

---

## Full streaming-surface filter result

`dotnet test --filter "FullyQualifiedName~Streaming|FullyQualifiedName~ChatClient" -c Release --no-build`

| Metric | Value |
|---|---:|
| Total tests matched | **112** |
| Passed | **104 (92.9%)** |
| Failed | **8 (7.1%)** |
| Skipped | 0 |
| Duration | ~1m |

### Failure inventory (all 8 OUT-OF-CLUSTER)

All 8 failures are in **`Sprk.Bff.Api.Tests.Integration.SseStreamingIntegrationTests`** — a separate SSE-streaming integration cluster, NOT the IChatClient streaming cluster of tasks 030/031:

1. `Cancellation_CleansUpBffStream_NoEventsAfterCancel`
2. `ConcurrencyLimit_Returns429WhenExceeded_AiStreamPolicy`
3. `ErrorEvent_NotEmitted_WhenClientCancels`
4. `ErrorEvent_PropagatesCorrectly_WhenModelThrowsMidStream`
5. `FirstToken_WithinLatencyBudget_P95Under500ms`
6. `HighVolumeStreaming_MaintainsConsistentLatency`
7. `SseEventSequence_FollowsCorrectOrder_TypingStartTokensTypingEndDone`
8. `StreamingTokens_NotCachedInRedis_DuringStreaming`

**Common root cause**: `NSubstitute.Exceptions.CouldNotSetReturnDueToMissingInfoAboutLastCallException` — the test calls `.Returns(...)` on what NSubstitute classifies as a non-virtual/non-abstract member. This is an **NSubstitute mocking-pattern bug** in the test setup (`SetupChatClientTokens`, `GenerateTokensThenError`), not a contract drift from tasks 030/031's canonical helpers.

**Ownership**: file `tests/unit/Sprk.Bff.Api.Tests/Integration/SseStreamingIntegrationTests.cs` was last touched by commit `32fa2aa6` (project `ai-sprk-chat-r2`). It is **NOT** in tasks 030/031's `<relevant-files>`. It belongs to the broader Phase 2+3 Integration tier scope (absorbed by an Integration-cluster task per TASK-INDEX).

---

## Cross-cluster regression check (FR-21)

Per FR-21 (cross-track regression check), this gate must verify the IChatClient cluster did not introduce regressions elsewhere.

- **Within cluster (015/016/030)**: 42/42 pass — no regressions.
- **Adjacent SseStreaming cluster**: the 8 SseStreamingIntegrationTests failures **pre-date** Wave 2.x repair work — they have the same root cause (`NSubstitute.SubstituteExtensions.Returns` against non-virtual member) which is consistent with the file being authored before tasks 015's canonical helpers existed. These failures were NOT introduced by 030's canonical-helpers swap; they have always existed in the post-Wave-1.3 baseline (172 failures), they were merely surfaced by this gate's broader filter.

**Conclusion**: zero NEW failures attributable to the IChatClient cluster work. The 8 surface-level failures are pre-existing, separately-owned, and absorbed by a different Phase 2+3 task.

---

## NFR compliance

| NFR | Requirement | Verification | Status |
|---|---|---|---|
| **NFR-01** | No `src/`/`power-platform/`/`infra/`/`scripts/` changes | `git status` clean across this task | ✅ |
| **NFR-02** | Measurement only (no test edits) | This task wrote only this report + appended to current-task.md | ✅ |
| **NFR-09** | `<repair-not-rewrite>true</repair-not-rewrite>` | Task 032 POML metadata + verification-only | ✅ |
| **§4.3** | No test in `Failed` state at cluster close | 42/42 cluster tests Pass; 8 surface-failures are out-of-cluster (separately owned) | ✅ for IChatClient cluster |
| **FR-14** | All IChatClient streaming cluster tests in §6.2 final end-state | All 42 cluster tests are `repaired` (Pass) | ✅ |
| **FR-21** | Cross-track regression check | Zero new failures attributable to 030's canonical-helpers swap | ✅ |

---

## Acceptance criteria checklist (task 032 POML)

- [x] Tasks 030 + 031 status=completed in TASK-INDEX.md (030 ✅; 031 ✅ NO-OP)
- [x] Cluster filter run and TRX captured (`ichatclient-cluster-2026-05-31.trx`)
- [x] Zero failures *within the IChatClient cluster scope* (42/42 pass)
- [x] Out-of-cluster failures (8 in `SseStreamingIntegrationTests`) identified, ownership traced, NOT attributable to 030/031 work
- [x] Cluster metrics tallied (see "Cluster member verification" + "Full streaming-surface filter result")
- [x] This verification report written at the parent's specified path
- [x] No production code changes (`git status` clean)

---

## Forward references

- The 8 `SseStreamingIntegrationTests` failures remain in the 172 post-Wave-1.3 residual failure inventory and are absorbed by Phase 2+3 Integration tier tasks. They are NOT P23.A's responsibility.
- Task 015's canonical `AsyncEnumerableHelpers` + `FakeChatClient` are the project's MANDATED helpers for all NEW IChatClient mocking (per §6.5). Future tasks repairing `SseStreamingIntegrationTests` SHOULD migrate from raw `IChatClient` NSubstitute substitutes to `FakeChatClient` from task 015, which would simultaneously fix the NSubstitute `Returns` bug AND consolidate on the canonical pattern.

---

*Cluster declared CLOSED — P23.A IChatClient streaming cluster outcome converged 2026-05-31.*
