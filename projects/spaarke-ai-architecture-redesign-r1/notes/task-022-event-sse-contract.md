# Task 022 → 023 handoff — Event-path server contract (FR-P1-03)

> **Date**: 2026-07-05 · **Revised 2026-07-06** by the G-P1 UAT round-1 fix wave (operator
> ruling "auto-classify, chip-offered summarize" + Defects 1–3 — see
> `notes/g-p1-uat-round1-findings.md`). · **Owner of this contract**: task 022 (server).
> Task 023 consumes it (client `dispatchConsumer` + chips UI). Canonical source in code:
> `src/server/api/Sprk.Bff.Api/Services/Ai/EventRules/EventRuleSseContract.cs`.

## 1. When the client calls the Event endpoint

`POST /api/ai/chat/sessions/{sessionId}/events/document-uploaded`

- Call ONCE per **attach gesture**, when EVERY file queued in the gesture has received
  its per-file `POST /sessions/{id}/documents` 202 (**count-complete batching** — G-P1
  Defect-2 fix; a 30 s stuck-promotion fallback fires the batch with whatever settled).
  Per-file upload responses are unchanged. Client promotions run sequentially.
- Body: `{ "fileIds": ["<documentId>", ...], "typedCommand": "<text or null>" }`
  - `fileIds` in upload order. Since the 2026-07-05 ruling the rule runs its members for
    EVERY file of the batch (classify-only launch rule) — top-1 is retired.
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
| `chips` | `{ sourceBindingId, chips: EventChip[] }` | Next-step chips after the rule completes — the last member's `sprk_chiptransitions`. Post-ruling these ARE the summarize entry: single-file → each transition with `args {fileIds}`; multi-file → the bulk `"<label> all N files?"` chip (first transition target, full-batch args) + per-file chips when the batch is ≤ 3. |
| `error` | — (`content` = safe message) | Standard chat error shape. Per-FILE failures emit an error line and CONTINUE the batch (2026-07-06); catalog errors stay batch-fatal. |
| `done` | — | Terminal (matches chat stream convention). |

**EventChip** = `{ targetBindingId: string, label: string, args?: object, requiresAttachments?: boolean }`
— `targetBindingId` IS the routing decision (ADR-039); the client calls
`dispatchConsumer(targetBindingId, args)` verbatim. `requiresAttachments` (added 2026-07-06)
drives the client's disabled-chip precondition and is carried through from the authored
`sprk_chiptransitions` entry (`requires_attachments`). `args` today:
`{ fileIds: string[] }` (transition / manual-run / bulk / per-file chips) and
`{ fileIds: [fileId], confirmedDocType: string }` (M4 confirm).

Happy path event order (classify-only launch rule): `event_classification` (× N files) →
`chips` → `done`. Multi-member rules retain `event_output` between classification and chips.

**Click-dispatch chips (2026-07-06, unified vocabulary)**: the Click path
(`POST /sessions/{id}/dispatch`, AnalysisChunk stream) now emits a `{"type":"chips",
"chips": EventChip-wire[]}` chunk AFTER its terminal `complete` chunk, carrying the
dispatched Binding's own `sprk_chiptransitions` (e.g. summarize → "Summarize again").
`dispatchConsumer` returns `{result, chips}`; the host renders the STORED result in the
conversation and re-arms the chip strip. ONE client parser (`parseConsumerChips`) reads
both streams' chips.

**Manifest readiness probe (Defect 3, 2026-07-06)**: when the event arrives before every
requested fileId is visible in the session manifest, the server re-reads the session up to
`EventRules:ReadinessProbeAttempts` (default 5) × `ReadinessProbeDelayMs` (default 1000 ms)
before degrading — it proceeds with whatever resolved; `no-attachments` only at zero.

## 3. Opt-out routes (for the settings toggle UI)

- `GET /api/ai/chat/event-rules/opt-out` → `{ "optedOut": boolean }`
- `PUT /api/ai/chat/event-rules/opt-out` body `{ "optedOut": boolean }` → `{ "optedOut": boolean }`

## 4. Bounds live server-side

Daily cap (`EventRules:DailyExecutionCap`, default 50 executions/user/UTC-day — counted
as members × batch files since 2026-07-06), opt-out, supersede, and the empty-attachments
precondition (with its bounded readiness probe) are ALL enforced in `EventRulesService`
regardless of client behavior — the client only improves UX by passing accurate
`fileIds` + `typedCommand`. The bulk top-1 bound is RETIRED by the 2026-07-05 ruling
(classify-only rule runs every file; summarize is chip-offered).
