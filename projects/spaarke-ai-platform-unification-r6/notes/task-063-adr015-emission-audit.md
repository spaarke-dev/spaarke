# Task 063 — ADR-015 + NFR-08 per-site emission audit

**Pillar / Spec ref**: R6 Pillar 6c / FR-37 — `context.*` events emitted from BFF
telemetry sites to feed the ExecutionTraceWidget (task 061).
**Date**: 2026-06-18.

## Telemetry transport (Step 1 dependency-verification result)

- **Event-type definitions (task 059)**: ✅ ON DISK at
  `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` (six
  discriminants on the `ContextPaneEvent.type` union: `tool_call_started`,
  `tool_call_completed`, `knowledge_retrieved`, `playbook_node_executing`,
  `playbook_node_completed`, `decision_made`). See
  `notes/task-059-context-pane-events-evidence.md` for the audit trace.
- **Existing SSE pipe**: `Services/Ai/Chat/SseEvent.cs` + `SseEventTypes/ChatSseEventFactory.cs`
  already carry `source_pane` / `output_pane` / `source_highlight` events from
  the BFF to the chat frontend via the SSE writer delegate threaded through
  `ToolHandlerToAIFunctionAdapter` (R6 Wave 7b infrastructure). The chat SSE
  pipe is the eventual frontend transport; the wire-up to PaneEventBus dispatch
  is downstream subscription work (out of scope for 063).
- **R5 task 008 telemetry baseline**: `Sprk.Bff.Api/Telemetry/AiTelemetry.cs`
  with `Meter` name `Sprk.Bff.Api.Ai` for OTel exporters; sibling instruments
  for ai.summarize / ai.rag / ai.tool / ai.export. R5 task 008 established
  the structured-log + Meter pattern this task extends.

## Chosen emission mechanism

A new **`IContextEventEmitter`** interface plus a singleton
**`ContextEventEmitter`** implementation that emits via:
1. A new `Meter` named `Sprk.Bff.Api.Ai.ContextEvents` with one
   `Counter<long>` per event type (six counters total).
2. A structured `ILogger` log entry per emission, with the prefix `[ADR-015]`
   and a discriminant matching the PaneEventTypes.ts contract (e.g.,
   `[context.tool_call_started]`).

Both surfaces carry deterministic IDs + numeric metrics + enum-like short
strings ONLY. The interface signatures are structurally constrained to enforce
this — no `object` / `JsonElement` / free-form `string content` parameters.

The Meter name + counter names are public constants
(`ContextEventEmitter.MeterName`, `.ToolCallStartedCounter`, etc.) so test
`MeterListener` subscribers can match by exact name.

## Per-site audit (4 emission categories)

### Site 1 — `SprkChatAgent` chat-tool invocation

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs`

This is the actual chat-tool dispatch path. `SprkChatAgent` invokes the
function-invocation-enabled IChatClient which, on each LLM-issued function call,
calls `ToolHandlerToAIFunctionAdapter.InvokeCoreAsync`. The adapter wraps every
chat-tool execution — it is THE chokepoint for chat-tool emission.

**Emissions wired**:
- `context.tool_call_started` — BEFORE handler dispatch (after the context
  factory builds the per-call `ChatInvocationContext` but before
  `_handler.ValidateChat` is called).
- `context.tool_call_completed` — at FOUR exit paths (validation_failed,
  handler-dispatch success/error, cancelled, exception). Each path emits the
  matching enum-like `outcome` value and the wall-clock `durationMs` from
  the same `Stopwatch` that already times the ADR-015 outcome log.

**Payload audit**:
- `toolName` ← `_tool.Name` (config identifier from `sprk_analysistool.sprk_name`; Tier 1 safe per ADR-015).
- `decisionId` ← `context.DecisionId` (freshly-generated GUID per invocation, no semantic content).
- `sessionId` ← `context.ChatSessionId` (deterministic GUID).
- `tenantId` ← `context.TenantId` (opaque tenant identifier).
- `outcome` ← enum-like short string from `{ "ok", "error", "validation_failed", "cancelled", "exception" }`.
- `durationMs` ← `stopwatch.ElapsedMilliseconds` (numeric metric).

**NOT carried** (verified via code-review of the adapter):
- ❌ `arguments` (the `AIFunctionArguments` LLM-supplied payload) — already
  ADR-015-blacklisted at lines 854-856 of the existing adapter; reused here.
- ❌ `result.Data` (tool output body) — never passed to the emitter.
- ❌ `context.ToolArgumentsJson` (raw JSON) — never passed.
- ❌ Any user message text or LLM response body.

**ADR-015 conclusion**: ✅ PASS. The emission site uses only the four
deterministic identifiers + enum outcome + numeric duration. The
`IContextEventEmitter.ToolCallStarted` / `ToolCallCompleted` interface
signatures are structurally constrained — there is no `object` /
`JsonElement` parameter that could carry user content.

### Site 2 — `CapabilityRouter` knowledge retrieval (delegated to RagService)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs`

The POML names "CapabilityRouter OR RAG path" as the emission site for
`context.knowledge_retrieved`. CapabilityRouter Layer 1 / Layer 2 do intent
classification, not knowledge retrieval — the actual retrieval happens in
`RagService.SearchAsync` (Layer 1 keyword classifier doesn't even touch
Azure Search). So the RAG path is the correct site.

**Emission wired**: After the results loop completes inside the success path
of `RagService.SearchAsync` (right after `LogRetrievalResults` is called),
the emitter iterates the `results` list and emits one
`context.knowledge_retrieved` event per result.

**Payload audit (per emission)**:
- `knowledgeSourceId` ← `r.DocumentId ?? r.Id ?? string.Empty` (deterministic
  chunk identifier; matches the per-document `documentIds` already in the
  existing `LogRetrievalResults` structured-log event — same Tier 1 safe identifier).
- `relevanceScore` ← `r.Score` (effective score after `MinScore` filter;
  numeric metric — already in the existing structured log).
- `resultCount` ← `results.Count` (numeric metric).
- `sessionId` ← `null` today (RagService.SearchAsync doesn't receive chat
  session id today; trace correlation by `tenantId + timestamp ordering`
  per PaneEventTypes.ts `correlationId?` optional semantics).
- `tenantId` ← `options.TenantId` (opaque tenant identifier — also in existing log).

**NOT carried** (verified via code-review of the emission site):
- ❌ `r.Content` (chunk text) — never passed to the emitter.
- ❌ `r.Highlights` (excerpt text) — never passed.
- ❌ `r.Metadata` (free-form fields) — never passed.
- ❌ `r.DocumentName` / `r.KnowledgeSourceName` (filename surface) —
  intentionally excluded to keep this event Tier 1 only. The PaneEventTypes
  TS contract describes the `knowledgeSourceId` as "deterministic ID enough
  for the trace widget to render a link" — names are looked up at render
  time, not carried in the event.

**ADR-015 conclusion**: ✅ PASS. Source IDs + numeric scores + tenant ID
only. The `IContextEventEmitter.KnowledgeRetrieved` signature is
structurally constrained — no `string content` / `object` parameter.

### Site 3 — `CapabilityRouter` Layer 1 / Layer 2 / Layer 3 decisions

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs`

The router has three decision points: Layer 1 keyword classifier (sync),
Layer 2 LLM-mini classifier (async, optional), Layer 3 broad superset
fallback. Each produces a `CapabilityRoutingResult` with the resolved
capability name and confidence — these become the `decision_made` events.

**Emissions wired**:
- Layer 1 (in `RouteSync`, after the confidence threshold is evaluated):
  `decision_made` with `layer="layer1"`, `decision="confident"|"uncertain"`,
  `capabilityName=result.SelectedCapabilities[0]?`.
- Layer 2 confident (in `Layer2ClassifyAsync` success path):
  `decision_made` with `layer="layer2"`, `decision="confident"`,
  `capabilityName=top.Name`.
- Layer 2 timeout / rate_limit (in the exception handlers of
  `Layer2ClassifyAsync`):
  `decision_made` with `layer="layer2"`, `decision="timeout"|"rate_limited"`,
  `capabilityName=null`.
- Layer 3 fallback (in `Layer3Fallback`):
  `decision_made` with `layer="layer3"`, `decision="fallback"`,
  `capabilityName=null`.

**Payload audit**:
- `layer` ← `"layer1"|"layer2"|"layer3"` (enum-like short string).
- `decision` ← `"confident"|"uncertain"|"timeout"|"rate_limited"|"fallback"` (enum-like).
- `capabilityName` ← config identifier from
  `sprk_capability.sprk_name` / manifest entry (Tier 1 safe per ADR-015
  amendment — config names are deterministic identifiers, not user content).
- `sessionId` ← `null` (CapabilityRouter is stateless; session is handled at
  the chat-orchestration layer).
- `tenantId` ← `null` (same — CapabilityRouter doesn't carry tenant context
  in its routing API today).

**NOT carried** (verified via code-review at every emission site):
- ❌ `userMessage` — the entire reason `userMessage` is sanitized at the
  emission boundary. The existing OTEL spans inside `RouteSync` / `Layer2ClassifyAsync`
  already document this contract (lines 50, 545 of CapabilityRouter.cs:
  *"ADR-015: user message content is NEVER logged or recorded in spans"*).
- ❌ `result.Confidence` (numeric metric — could be added but isn't carried
  on this event surface; the existing `Layer1LatencyHistogram` already records it).
- ❌ `responseText` (Layer 2 LLM response body) — explicitly excluded at the
  existing line 684 in `Layer2ClassifyAsync`: *"Log the exception
  type/message — NOT the userMessage (ADR-015)"*.

**ADR-015 conclusion**: ✅ PASS. The `IContextEventEmitter.DecisionMade`
signature accepts only enum-like strings + the capability name (Tier 1 safe
config identifier).

### Site 4 — `PlaybookOrchestrationService.ExecuteNodeAsync` (per-node wrapper)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`

**NFR-08 BINDING**: emission is AT THE WRAPPER level (`ExecuteNodeAsync`,
which dispatches to `executor.ExecuteAsync` at line ~1216), NOT inside any
of the 11 production node executors.

**Emissions wired**:
- `context.playbook_node_executing` — emitted ONCE at the very top of
  `ExecuteNodeAsync` (immediately after the existing `PlaybookStreamEvent.NodeStarted`
  channel write). Paired with starting a local `Stopwatch` for duration measurement.
- `context.playbook_node_completed` — emitted via a local helper
  `EmitNodeCompleted(string decision)` called at EVERY return path inside the
  function:
  - Branch-not-selected skip (line ~975): `decision="skipped"`
  - Dependency-failed skip (line ~998): `decision="skipped"`
  - Start node passthrough (line ~1025): `decision="skipped"`
  - Action-not-found error (line ~1073): `decision="failed"`
  - AI-node-without-ActionId error (line ~1088): `decision="failed"`
  - No-executor-registered error (line ~1126): `decision="failed"`
  - Insights L2 gate-fail skip (line ~1179): `decision="skipped"`
  - Validation-failed error (line ~1214): `decision="failed"`
  - Executor success/error (line ~1245): `decision="success"|"failed"` based
    on `output.Success`.
  - Cancellation (line ~1252): `decision="cancelled"`.
  - Catch-all exception (line ~1272): `decision="failed"`.

**Payload audit**:
- `playbookId` ← `runContext.PlaybookId` (deterministic GUID).
- `nodeId` ← `node.Id` (deterministic GUID, matches the existing
  `PlaybookStreamEvent.NodeStarted` / `NodeCompleted` correlation surface).
- `nodeType` ← `node.NodeType.ToString()` (enum-like — `AIAnalysis`, `Output`,
  `Control`, `Workflow`, etc.).
- `decision` ← enum-like short string from
  `{ "success", "failed", "skipped", "cancelled" }`.
- `durationMs` ← `nodeStopwatch.ElapsedMilliseconds` (numeric metric).
- `sessionId` ← `null` (chat session id is not threaded into
  `PlaybookOrchestrationService` today; downstream Pillar 6c trace widget
  correlates by `playbookId` + `runId` + timestamp ordering).
- `tenantId` ← `null` (same).

**NOT carried** (verified via code-review at every emission site):
- ❌ `node.Name` — even the human-readable node name is excluded from the
  Meter tags (only `nodeId` GUID + `nodeType` enum). The existing
  `PlaybookStreamEvent` channel writes carry `node.Name` for end-user UI
  surfacing, but the `context.*` trace event surface is deterministic-ID-only.
- ❌ `node.ConfigJson` — never touched by the emitter.
- ❌ `output.Data` / `output.ErrorMessage` — never passed to the emitter.
  (The existing `_logger.LogWarning("Node {NodeName} failed: {Error}", ...)`
  at line ~1238 is a SEPARATE ADR-015-compliant log under existing
  governance — not part of the new context.* event.)
- ❌ Anything from the `executor.ExecuteAsync(nodeContext, ...)` return value
  other than the boolean `output.Success`.

**ADR-015 conclusion**: ✅ PASS. The `IContextEventEmitter.PlaybookNodeExecuting`
/ `PlaybookNodeCompleted` signatures accept GUIDs + enum-like strings +
numeric duration only.

**NFR-08 verification** (binding):
```
$ git diff --stat src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/
(empty)
```

The 11 production node executors under `Services/Ai/Nodes/` are
UNMODIFIED — verified post-implementation via `git diff --stat`. The emission
is exclusively at the wrapper level (`ExecuteNodeAsync`), not inside any
executor's `ExecuteAsync(NodeExecutionContext, CancellationToken)` body.

## Cross-cutting ADR conclusions

- **ADR-013** (AI architecture, facade boundary): The emitter is registered
  unconditionally in `AnalysisServicesModule.cs` at the top of the module
  (next to `R5SummarizeTelemetry`) — singleton lifetime. Sibling AI services
  (CapabilityRouter, PlaybookOrchestrationService, RagService,
  ToolHandlerToAIFunctionAdapter) inject `IContextEventEmitter?` (optional)
  so existing test fixtures and AI-OFF paths continue to construct cleanly.
  The interface lives inside `Services/Ai/Telemetry/` (AI-internal subtree) —
  NOT surfaced through `Services/Ai/PublicContracts/`. CRUD-side callers
  never inject it.
- **ADR-015** (data governance, BINDING): per-site audits documented above.
  The structural constraint (interface signatures accept no `object` /
  `JsonElement` / `string content` parameters) makes user-content smuggling
  IMPOSSIBLE at the type-system layer — only deterministic identifiers,
  numeric metrics, enum-like short strings, and ISO-8601 timestamps fit.
- **ADR-029** (BFF publish hygiene): zero new NuGet dependencies. The
  emitter uses `System.Diagnostics.Metrics` (BCL) and
  `Microsoft.Extensions.Logging` (already a project reference). Compressed
  publish size measured at 44.68 MB — UNDER the prior 45.65 MB baseline
  (delta: -0.97 MB; well within the NFR-02 ≤+5 MB R6 budget).
- **ADR-030** (PaneEventBus 4-channel): events fit on the existing `context`
  channel using the additive discriminants defined in task 059. No 5th
  channel is introduced.
- **NFR-08** (11 production node executors UNMODIFIED): verified — see
  Site 4 audit above.

## Summary

| Site | File | Status | ADR-015 conclusion |
|---|---|---|---|
| `tool_call_started` + `tool_call_completed` | `Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | ✅ Wired (4 paths) | PASS — deterministic IDs + enum outcome + numeric duration only |
| `knowledge_retrieved` | `Services/Ai/RagService.cs` (per-result emission) | ✅ Wired | PASS — source IDs + scores + counts only; no chunk text |
| `decision_made` | `Services/Ai/Capabilities/CapabilityRouter.cs` (Layer 1 / 2 / 3 outcomes) | ✅ Wired (5 paths) | PASS — enum layer + decision + capability NAME only |
| `playbook_node_executing` + `playbook_node_completed` | `Services/Ai/PlaybookOrchestrationService.cs` (wrapper level, NFR-08) | ✅ Wired (11 paths via helper) | PASS — GUIDs + enum decision + numeric duration only |

| Cross-cut | Result |
|---|---|
| **NFR-08** (`git diff Services/Ai/Nodes/`) | EMPTY (verified) |
| **ADR-029** publish-size delta | -0.97 MB (44.68 MB compressed vs 45.65 baseline) |
| **Build** | 0 errors / 16 warnings (all pre-existing) |
| **Unit tests** | 11 / 11 passing (`ContextEventEmissionTests.cs`) |

R6 Pillar 6c emission surface (FR-37) is functional end-to-end at the BFF
side. Frontend subscription (chat SSE → PaneEventBus dispatch → trace widget
render) is downstream wiring (tasks 061 / 062 already landed for the widget +
registry; subscribing them to the meter / log surface is a deployment-time
config step, not a code task).
