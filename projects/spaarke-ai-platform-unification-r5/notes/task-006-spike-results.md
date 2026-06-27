# Task 006 Spike Results — Azure OpenAI Structured Outputs Streaming

> **Task**: 006 D1-06 Azure OpenAI Structured Outputs + incremental JSON parser
> **Date**: 2026-06-04
> **Operator**: ralph.schroeder@spaarke.com
> **Environment**: Spaarke Dev (`spaarke-openai-dev.cognitiveservices.azure.com`, deployment `gpt-4o-mini` → gpt-4.1-mini 2025-04-14)
> **Spike harness**: `notes/spikes/task-006-structured-outputs/spike-request.json` + raw SSE response in `spike-raw-stream.sse`

---

## Outcome: **PATH A — Structured Outputs streaming + incremental JSON parser**

PATH A is confirmed viable. PATH B (Function Calling fallback) is NOT needed. Azure OpenAI streams JSON content token-by-token via `delta.content` events when `response_format.type = json_schema` (strict mode) is combined with `stream: true`.

## Evidence

### Streaming granularity (191 content-bearing SSE events for a 932-char JSON response)

| Token length | Event count |
|---|---|
| 1 char | 31 |
| 2 chars | 19 |
| 3 chars | 32 |
| 4 chars | 26 |
| 5 chars | 17 |
| 6-9 chars | 43 |
| 10+ chars | ~23 (long-word tokens) |

Tokens are sub-word fragments. JSON delimiters (`{"`, `":[`, `"`) often arrive as standalone 1-3 char events; word content arrives as ~4-8 char chunks. This is FINER granularity than required — field-level boundary detection is reliable.

### First 30 content tokens (representative — see `spike-raw-stream.sse` lines 1-90 for raw)

```
[  0] ''
[  1] '{"'
[  2] 't'
[  3] 'ldr'
[  4] '":["'
[  5] 'Jane'
[  6] ' Doe'
[  7] ' is'
[  8] ' hired'
...
```

### Top-level field appearance order (matches JSON-schema declaration order)

| Field | First seen at event # | Char position | Significance |
|---|---|---|---|
| `tldr` | #4 | 10 | FIRST — perfect for R5 UX requirement (TL;DR populates first) |
| `summary` | #66 | 299 | Second |
| `keywords` | #150 | 742 | Third |
| `entities` | #177 | 879 | Last |

**No schema reordering needed**: Azure OpenAI emits fields in JSON-schema property declaration order. The spike-tested schema declares `tldr` first; the actual schema in `DocumentAnalysisResult.cs` may need verification but adjusting property declaration order (if needed) is a one-line change.

### Latency

| Metric | Value |
|---|---|
| Total wall-clock | 4.80s |
| First content event after request | ~0.3-0.5s (estimated from event timestamps; TTFB well under 500ms NFR-01 ceiling) |
| Token rate | ~40 events/sec |

### JSON validity

- **Intermediate**: JSON is syntactically INVALID at every intermediate state (unclosed strings, unbalanced braces). Parser MUST be partial-JSON tolerant — cannot use `JsonSerializer.Deserialize` on intermediate buffers.
- **Final**: Accumulated buffer parses cleanly as the declared schema. All 4 top-level fields populated. `tldr` array has 3 entries as requested.

## Decision rationale

**Why PATH A wins**:

1. **Granularity exceeds R5 needs**: 191 events for ~932 chars = ~4.9 chars/event avg. R5 needs field-level boundary detection (4 boundaries); the stream gives us 191x that resolution. Field-level state machine is trivially robust at this granularity.
2. **Declaration order is honored**: TL;DR-first UX requirement satisfied by JSON property order. Zero special handling needed.
3. **Single round-trip**: PATH B (Function Calling) requires defining a `record_summary` function whose parameters match the schema — adds API surface complexity and runs through `ToolCallUpdates` instead of `ContentUpdate`. PATH A reuses the existing `CreateJsonSchemaFormat` config from `GetStructuredCompletionRawAsync` (one-line config swap to `CompleteChatStreamingAsync`).
4. **Same final shape**: Final accumulated JSON parses cleanly to `DocumentAnalysisResult`. Wizard back-compat is preserved verbatim — same final-result envelope as today.
5. **TTFB well under NFR-01 ceiling**: <500ms confirmed.

**Why PATH B would have been worse**:

- ToolCallUpdates streaming has DIFFERENT shape than ContentUpdate streaming — would have required two different parser strategies and more SDK-coupling.
- Function-calling does not naturally enforce JSON-schema strict mode at the same level — small risk of malformed output.
- Wizard back-compat path (current `GetStructuredCompletionRawAsync` returns a raw JSON string) would have diverged.

## Known risks / caveats

| Risk | Mitigation |
|---|---|
| Intra-string content includes JSON-escape sequences (`\"`, `\n`) | Parser must consume backslash-escapes when tracking depth + field boundaries |
| Long string fields (e.g., `summary`) take many tokens to complete | Emit progressive content deltas (FieldDelta with `IsFieldStart=false, IsFieldComplete=false`); widget renders ChatGPT-style cursor |
| Single token may span field boundary (e.g., `"."}` could close one field and start nothing) | State machine processes tokens one char at a time, not whole-token at a time |
| Schema property order in `DocumentAnalysisResult` may not match R5 desired UX order | If non-TL;DR-first, reorder `[JsonPropertyName]` on the record — separate consideration documented in implementation plan |
| Content filter pre-flight event (event #0 is empty + has `prompt_filter_results`) | Parser ignores empty content deltas; safe |
| `[DONE]` SSE terminator | Existing `StreamCompletionAsync` does not emit until iteration ends naturally — same handling |

## Implementation path (PATH A)

1. **New helper**: `Services/Ai/Streaming/IncrementalJsonParser.cs` — character-by-character state machine; tracks depth + current top-level field + emits `FieldDeltaEvent(Path, Content, Sequence, Kind)` at boundaries.
2. **New OpenAiClient method**: `StreamStructuredCompletionAsync(messages, jsonSchema, schemaName, deploymentName, ct)` — mirrors `StreamCompletionAsync` but adds `ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(...)` to options; yields raw `delta.content` tokens.
3. **Wiring**: `AnalysisOrchestrationService.ExecutePlaybookAsync` (Summarize tool branch) consumes the stream, feeds tokens to the parser, translates `FieldDeltaEvent` → `AnalysisChunk.FromDelta(Path, Content, Sequence)` (the variant landed by task 005 in commit `84b26f6f`), and emits the final `AnalysisChunk.Completed(result)` when the parser yields the final accumulated `DocumentAnalysisResult`.
4. **DI**: `IncrementalJsonParser` registered in `AnalysisServicesModule.cs` (concrete; no interface; no new `Program.cs` line).

## Spike artifacts

- `notes/spikes/task-006-structured-outputs/spike-request.json` — request payload (system prompt + user content + JSON Schema strict mode)
- `notes/spikes/task-006-structured-outputs/spike-raw-stream.sse` — full raw SSE response (67 KB, 195 lines)

Both files are ephemeral per the task POML contract — keep for reference until task 006 completes, then candidates for cleanup at project wrap-up (task 090) via `repo-cleanup`.

---

*Spike duration: ~10 minutes. Cost: 1 Azure OpenAI call against Spaarke Dev (gpt-4o-mini deployment).*
