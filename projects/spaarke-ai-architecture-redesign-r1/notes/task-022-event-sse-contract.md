# Task 022 → 023 handoff — Event-path server contract (FR-P1-03)

> **Date**: 2026-07-05 · **Owner of this contract**: task 022 (server). Task 023 consumes it
> (client `dispatchConsumer` + chips UI). Canonical source in code:
> `src/server/api/Sprk.Bff.Api/Services/Ai/EventRules/EventRuleSseContract.cs`.

## 1. When the client calls the Event endpoint

`POST /api/ai/chat/sessions/{sessionId}/events/document-uploaded`

- Call ONCE when an upload **batch** completes (after the last per-file
  `POST /sessions/{id}/documents` 202). Per-file upload responses are unchanged.
- Body: `{ "fileIds": ["<documentId>", ...], "typedCommand": "<text or null>" }`
  - `fileIds` in upload order — index 0 is the deterministic **top-1** for the bulk bound.
  - `typedCommand`: pass the user's composer text when the upload was accompanied by a
    typed command; the server enforces explicit-command supersede regardless, but only
    the client knows the composer state. When superseded the client should dispatch the
    typed command on its normal Text path — the event stream returns only a notice.
- Auth: standard `@spaarke/auth` bearer; rate limit `ai-context`.
- Errors before the stream: ProblemDetails with `errorCode` ∈
  `sessionId.invalid | auth.tid-missing | auth.oid-missing | event-rules.session-not-found |
  event-rules.invalid-request | event-rules.internal-error` + 503 feature-disabled shape
  (`ai.event-rules.disabled`) when the compound AI gate is off.

## 2. SSE stream (envelope = ChatSseEvent `{type, content?, data?}`, camelCase, `data: {json}\n\n`)

| `type` | `data` shape | Notes |
|---|---|---|
| `event_classification` | `{ fileId, fileName, docType, confidence, bindingId, ucid, ledgerKey }` | Classify member result. Also persisted to `ChatSessionFile.ClassifiedDocType/Confidence`. |
| `event_output` | `{ bindingId, ucid, ledgerKey, disposition, payload }` | A member's STORED ledger output (ADR-040 render-follows-store). For summarize, `payload` is the SUM-CHAT@v1 `DocumentAnalysisResult`-shaped JSON (`tldr/summary/keywords/entities`). |
| `event_confirmation` | `{ fileId, docType, confidence, threshold, message, chips: EventChip[] }` | M4 gate — classify confidence below `EventRules:ClassifyConfidenceThreshold` (default 0.85). Summarize did NOT run; the confirm chip resumes it via the Click path. |
| `event_notice` | `{ reason, message, chips?: EventChip[] }` | Graceful non-execution. `reason` ∈ `superseded \| opted-out \| daily-cap \| no-attachments \| no-rule`. `daily-cap` and `opted-out` carry a manual-run chip. |
| `chips` | `{ sourceBindingId, chips: EventChip[] }` | Next-step chips after the rule completes: the last member's `sprk_chiptransitions` + the bulk `"Summarize all N files?"` chip when the batch had >1 file. |
| `error` | — (`content` = safe message) | Standard chat error shape. |
| `done` | — | Terminal (matches chat stream convention). |

**EventChip** = `{ targetBindingId: string, label: string, args?: object }` —
`targetBindingId` IS the routing decision (ADR-039); the client calls
`dispatchConsumer(targetBindingId, args)` verbatim. `args` today:
`{ fileIds: string[] }` (manual-run / summarize-all) and
`{ fileIds: [fileId], confirmedDocType: string }` (M4 confirm).

Happy path event order: `event_classification` → `event_output` → `chips` → `done`.

## 3. Opt-out routes (for the settings toggle UI)

- `GET /api/ai/chat/event-rules/opt-out` → `{ "optedOut": boolean }`
- `PUT /api/ai/chat/event-rules/opt-out` body `{ "optedOut": boolean }` → `{ "optedOut": boolean }`

## 4. Bounds live server-side

Daily cap (`EventRules:DailyExecutionCap`, default 50 executions/user/UTC-day), opt-out,
bulk top-1, supersede, and the empty-attachments precondition are ALL enforced in
`EventRulesService` regardless of client behavior — the client only improves UX by
passing accurate `fileIds` + `typedCommand`.
