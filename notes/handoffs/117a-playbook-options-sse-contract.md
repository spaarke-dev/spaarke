# Task 117a — `playbook_options` SSE Event Contract — Handoff

**Date**: 2026-06-25
**Status**: ✅ Complete
**Project**: spaarke-ai-platform-chat-routing-redesign-r1
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
**Wave**: 5-D
**Spec**: FR-49 (SSE shape lock), FR-48 (no auto-execute), FR-51 (library CTA always on)
**ADRs**: ADR-013 (boundary), ADR-015 tier-1 (audit safety), ADR-029 (publish-size), ADR-019 (SSE error handling parity)

## What shipped

### New files

| File | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/PlaybookOptionsSseEvent.cs` | Event type constant (`"playbook_options"`) + `PlaybookOptionsSseEventData` envelope record (5 fields) + `PlaybookOptionCandidate` record (5 fields, locked by spec FR-49). JSON property names locked via `[JsonPropertyName]` attributes — camelCase wire shape. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/PlaybookOptionsEventBuilder.cs` | Scoped DI service. Orchestrates `IPlaybookCandidateSelector.Select` (task 113R) → optional `IIntentRerankerService.RerankAsync` (task 111R) → projection to locked `PlaybookOptionsSseEventData` shape. Owns the FR-51 invariant (`LibraryModalCta = true`). Owns the ADR-015 tier-1 telemetry contract (counts + controlled-vocabulary tags + latency only). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookOptionsEventBuilderTests.cs` | 14 tests (12 [Fact] + 1 [Theory] with 3 InlineData). Covers happy path, rerank path, graceful-degrade, empty input, multi-file ID passthrough, FR-51 invariant, ADR-015 telemetry assertion, latency tag, cancellation propagation, reranker-returns-zero fallback, FR-49 shape lock (envelope + candidate), FR-48 no-`AutoExecute` reflection assertion. |

### Modified files

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/ChatSseEventFactory.cs` | Added `CreatePlaybookOptionsEvent(PlaybookOptionsSseEventData)` factory parallel to existing pane-event factories. Wraps payload in `ChatSseEvent("playbook_options", null, data)`. Includes ADR-019 serialization-failure error event parity. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiChatModule.cs` | Registered `PlaybookOptionsEventBuilder` as `AddScoped<>` (matches selector/reranker lifetime composition). DI count: 5 → 6, well within ADR-010 ≤15 ceiling. Comment block documents lifetime + ADR boundaries. |

### Tracking updates

- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/117a-playbook-options-sse-event.poml` — status: `not-started` → `completed`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/TASK-INDEX.md` — 117a row: 🔲📄 → ✅; payload note expanded with two additional emitted envelope fields (`rerankInvoked`, `rerankReason`)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/current-task.md` — Last Updated + Recovery banner

## Locked wire format (binding — do not deviate)

```json
{
  "candidates": [
    {
      "playbookId": "<guid string>",
      "playbookCode": "<short code>",
      "displayName": "<sprk_name>",
      "confidence": 0.92,
      "reason": "<controlled-vocabulary tag>"
    }
  ],
  "libraryModalCta": true,
  "sessionAttachmentIds": ["<file id 1>", "<file id 2>"],
  "rerankInvoked": true,
  "rerankReason": "ambiguous-top-2-within-margin"
}
```

Test `BuildAsync_SerializedPayload_HasExactlyTheLockedFieldSet` enforces both the envelope field set (5) and the candidate field set (5). Adding a field requires an explicit FR amendment and a test update.

## Test results

- **New tests**: 14 / 14 passing (`PlaybookOptionsEventBuilderTests`)
- **`Services.Ai.Chat` slice regression**: 782 passed, 0 failed, 4 skipped (pre-existing, unrelated)
- **Build warnings**: 17 (baseline preserved — no new warnings introduced)

## Publish-size

- **Compressed (tar.gz)**: 47.92 MB
- **Delta vs current-task.md 47.91 MB baseline (task 115)**: +0.01 MB
- **Delta vs ~45.65 MB POML reference baseline**: +2.27 MB (continuation of the cumulative project drift; well under the 60 MB NFR-01 ceiling)
- **NFR-01 status**: ✅ within ceiling

## ADR-015 tier-1 audit posture

The payload + the builder's log line contain ONLY:

- Deterministic identifiers (playbook GUIDs, session attachment IDs)
- Counts (candidate count, attachment count)
- Admin-facing display names (`sprk_name` — configuration content, not user content)
- Controlled-vocabulary reason tags (mirrored from selector + reranker contracts)
- Latency milliseconds (wall-clock telemetry)

Test `BuildAsync_LogMessages_NeverContainUserTextOrAttachmentNames` asserts that the verbatim user message string AND attachment filename / MIME do NOT appear in any captured log line. This is the test that prevents future drift on the tier-1 boundary.

Rerank free-form text (`RankedPlaybookCandidate.RerankReason` — raw LLM output) is INTENTIONALLY DISCARDED in the projection. The projected candidate's `Reason` field uses the selector's controlled-vocabulary tag; the envelope's `RerankReason` field uses the reranker's controlled-vocabulary outcome tag. Free-form LLM text never reaches the wire.

## What was NOT done (by design, per POML scope)

1. **Wiring into `ChatEndpoints.cs`** — task 117a defines the contract + projection; the actual SSE emit point in the chat streaming flow is a future orchestration task (or part of 117b's BE companion). The builder is registered in DI and ready to consume.
2. **`PlaybookCode` enrichment** — the upstream `PlaybookCandidate` / `RankedPlaybookCandidate` records do not surface `PlaybookCode`; the builder emits empty string today. The future orchestrator task may enrich via `IPlaybookLookupService` before calling `ChatSseEventFactory.CreatePlaybookOptionsEvent`, OR the upstream selector/reranker contracts may be extended to carry the code field. Either approach is compatible with the locked SSE shape.
3. **Phase B → builder wiring** — `PlaybookDispatcher.RunPhaseBVectorMatchAsync` returns `IReadOnlyList<PhaseBPerFileResult>`; the orchestrator must pass these results plus the session attachment metadata + IDs to `BuildAsync`. The contract is in place; the wiring is the next task.
4. **Integration test that hits the SSE wire** — covered indirectly by the FR-49 shape-lock unit test (`BuildAsync_SerializedPayload_HasExactlyTheLockedFieldSet`); the full HTTP-level integration test belongs to the orchestrator task that owns the emit point.

## Open follow-ups (for main session)

1. **Choose the SSE emit point** in the chat streaming flow. Two candidates surfaced by Grep: (a) after `PlaybookDispatcher.DispatchAsync` returns when the file-aware path produced no high-confidence single — emit `playbook_options` and SHORT-CIRCUIT the auto-execute path; (b) as a non-blocking pre-emit before the LLM stream when attachments are present. Decision belongs to the orchestrator task.
2. **`PlaybookCode` strategy** — either enrich in the orchestrator (additional `IPlaybookLookupService` call per candidate; ~5–10ms each, cacheable) OR extend `PlaybookCandidate` / `PlaybookSearchResult` upstream to surface code from the Dataverse projection. Recommend the upstream extension — single change, no per-emit cost. Out of scope for 117a.
3. **117b** (FE consumer) — unblocked by this task. The locked TypeScript shape mirrors `PlaybookOptionsSseEventData`. The frontend SSE client matches on `event: playbook_options` and renders the candidate list as inline link buttons with a permanent "Open Library" CTA per FR-51.

## Pivots from POML

The POML's step 4 ("Emit the event via the existing SSE writer with event name `playbook_options`") was intentionally NOT executed in this task. The POML's `<step>` list was authored before the W0 design conversation introduced the explicit BE/FE split (117a = BE contract + builder; 117b = FE consumer). The main session's task description for 117a explicitly directs: "DO NOT wire this into ChatEndpoints YET — the actual SSE emit at the right point in the chat-streaming flow is a future orchestration task". The builder is ready to wire; the emit point is a downstream decision.

The shape was extended beyond the POML's literal 3-field envelope (`candidates / libraryModalCta / sessionAttachmentIds`) to include `rerankInvoked` + `rerankReason`, which are explicit in the main-session task description AND are required to surface the 113R → 111R outcome telemetry to the frontend. These two fields are also ADR-015 tier-1 safe (boolean + controlled-vocabulary string). The TASK-INDEX row note was updated to reflect this.

## Coordination with parallel task 116

Task 116 (remove `SoftSlashIntentToCapabilityName` dict) is running in a parallel sub-agent. It modifies:
- BE: `Services/Ai/Capabilities/CapabilityRouter.cs` (or similar)
- FE: `src/solutions/SpaarkeAi/` or `src/client/shared/` SoftSlashRouter

Task 117a touches none of those files. The two tasks should commit + push cleanly to the same branch in any order; rebase should be conflict-free.
