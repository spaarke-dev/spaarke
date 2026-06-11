# ADR-033: Streaming chat-tool side channel (Concise)

> **Status**: Accepted
> **Domain**: BFF AI / chat-tool framework
> **Last Updated**: 2026-06-08
> **Source project**: `spaarke-ai-platform-unification-r6` Wave 9 (Q9 chat-tool migration finalization)
> **Cross-references**: extends ADR-013 (AI architecture + facade boundary); reinforces ADR-015 (data governance — no token content in logs); reinforces ADR-014 (transient streaming content not cached); satisfies the binding "ADRs Are Defaults — Challenge When Warranted" operating principle in project CLAUDE.md.
> **NFR-03 revision (2026-06-08)**: R6 spec.md NFR-03 declared "no new ADRs in R6." Per the ADRs-Are-Defaults principle, that scope-discipline rule is revised per-case for Wave 9. Without this ADR, `WorkingDocumentTools` would either (a) be deferred to R7 — leaving one of ten chat tools unmigrated and Q9 incomplete — or (b) re-implemented as a parallel non-IToolHandler class — duplicating the registration/discovery surface. Both costs are materially worse than authoring one focused ADR.

---

## Decision

Chat-tool handlers that emit **document-stream SSE side-channel events** (a separate SSE channel from the chat output channel — used for streaming AI-generated document content into in-place editor surfaces) MUST emit those events via a writer delegate carried on `ChatInvocationContext`, NOT via constructor injection AND NOT via a new method on `IToolHandler`.

Concretely:
1. `ChatInvocationContext` gains an optional `DocumentStreamWriter` field of type `Func<DocumentStreamEvent, CancellationToken, Task>?`.
2. `ChatEndpoints.SendMessageAsync` (or the chat-session entry point) injects the field by binding it to the per-request `DocumentStreamSseWriter` it already constructs for the in-flight SSE response.
3. Chat-tool handlers that need to emit document-stream events (currently `WorkingDocumentHandler`) read `context.DocumentStreamWriter` at the start of `ExecuteChatAsync` and call it directly during streaming.
4. The `IToolHandler` interface itself is **NOT modified**. The existing `ExecuteChatAsync(ChatInvocationContext, AnalysisTool, CancellationToken) → Task<ToolResult>` signature is sufficient — streaming is a side-effect via context-injected delegate, not a change to the handler return type.

---

## Why this works (and why the original framing was over-engineered)

Wave 9 pre-analysis initially framed this as "the `IToolHandler` contract assumes single-value return but `WorkingDocumentTools` needs streaming — requires a streaming overload on the interface." That framing was incorrect on inspection: `WorkingDocumentTools` itself already returns a single `Task<string>` summary to the LLM. The streaming is a **side effect** — an inner streaming LLM call whose tokens are piped to a per-request SSE writer delegate already passed by `SprkChatAgentFactory` at tool construction. The "streaming" is not the AIFunction's return value; it's a side-channel emission via a callback.

This pattern fits the existing `ExecuteChatAsync` contract cleanly:
- The handler returns `ToolResult.Success("Edited document. Streamed N tokens.")` (single value)
- During execution, it consumes `IChatClient.GetStreamingResponseAsync` and emits via the context-side writer
- No change to `IToolHandler`; no new "streaming variant"; no parallel handler class

Per the **ADRs Are Defaults — Challenge When Warranted** operating principle (project CLAUDE.md), the right answer here was the smaller-scope change. Going through the principle's checklist:
1. **STOP** before implementing the streaming overload (we did)
2. **NAME the specific rule** — IToolHandler contract; ChatInvocationContext surface; NFR-03 "no new ADRs"
3. **VERIFY** — walked through what each contract requires; discovered the streaming overload was unnecessary
4. **ENUMERATE** — interface extension vs context-field; chose the smaller change
5. **WAIT** for explicit user direction (user confirmed Wave 9 + ADR change)

---

## Why a NEW ADR (not just a context-field addition)

Three reasons:
1. **Codifies a side-channel pattern** for future chat-tool handlers. Without this ADR, future authors might re-derive the wrong framing (interface extension) by analogy with non-streaming SSE side-channels handled at the adapter (Wave 7b citations/widgets via `ToolHandlerToAIFunctionAdapter.PostProcessMetadataAsync`).
2. **Explicitly resolves the NFR-03 revision question** — R6 promised "no new ADRs"; this ADR documents the operating principle was invoked, the alternatives were considered, and the cost of NOT writing it was higher than writing it.
3. **Distinguishes two side-channel patterns** (see Two-channel summary below). Without explicit documentation, the citations/widget side-channel (handler returns Metadata + adapter emits) and the document-stream side-channel (handler emits directly via context-side writer) could be conflated. The two channels exist for different reasons; this ADR makes that distinction binding.

---

## Two-channel summary (binding pattern)

| Side-channel | Mechanism | Why this mechanism | Example handler |
|---|---|---|---|
| **Citations + chat-widget events** (Wave 7b) | Handler returns via `ToolResult.Metadata["citations"]` + `ToolResult.Metadata["widget"]`; adapter (`ToolHandlerToAIFunctionAdapter.PostProcessMetadataAsync`) emits side effects after the handler returns. | Citations + widget events are **discrete data emitted once per tool call** after the handler completes its work. Returning via Metadata keeps the handler synchronous (1 yield to caller); adapter centralizes the side-effect logic. | DocumentSearch, KnowledgeRetrieval, VerifyCitations, WebSearch, CodeInterpreter, LegalResearch |
| **Document-stream events** (Wave 9, this ADR) | Handler receives `DocumentStreamWriter` via `ChatInvocationContext`; handler emits Start → N×Token → End events directly during streaming, inside the body of `ExecuteChatAsync`. | Token events are **emitted as the work happens** (during a streaming sub-LLM call), not after the handler returns. Buffering all tokens in `ToolResult.Metadata` would delay perceived latency by the full LLM round-trip duration and defeat the purpose of streaming. Direct emission keeps token latency at the network-token boundary. | WorkingDocument |

Future chat-tool handlers consult this table:
- **One-time data side-channels** (citations, single widget snapshot, source-pane footnotes) → Wave 7b Metadata-envelope pattern.
- **Streaming token emission during execution** → context-side writer delegate (this ADR).

---

## Constraints

### ✅ MUST

- **MUST** declare `DocumentStreamWriter` on `ChatInvocationContext` as **optional / nullable** (`Func<...>?`). Handlers that need it check for null and emit a `ToolResult.Failure` with a clear "no stream writer wired" error when nil. This preserves the existing zero-DI handler invocation surface for handlers that don't stream.
- **MUST** wire `DocumentStreamWriter` from `ChatEndpoints` (or the chat-session entry point that constructs SSE response writers) into the `ChatInvocationContext` at construction time. The factory + adapter MUST NOT lose this binding.
- **MUST** emit a terminal `DocumentStreamEndEvent` in EVERY exit path (success, cancellation, error) so the frontend can finalize the editor UI state. Per ADR-019 / spec FR-12 — no half-open document streams.
- **MUST** preserve ADR-015 governance: log token COUNT + operation IDs only, NEVER log token content or full prompts.
- **MUST** preserve ADR-014: streaming tokens are transient — MUST NOT be persisted to Redis / Cosmos. Tokens may live only in-flight in the SSE pipe + the accumulating `StringBuilder` used to compute the SHA-256 content hash returned in `DocumentStreamEndEvent`.
- **MUST** keep the `IChatClient` injection inside the handler's DI ctor (not via context) — `IChatClient` is a singleton DI surface; passing it via context would couple per-request state to per-process state.

### ❌ MUST NOT

- **MUST NOT** add a streaming-variant method to `IToolHandler` (e.g., `ExecuteChatStreamingAsync` returning `IAsyncEnumerable<TokenChunk>`). The existing contract is sufficient; adding the variant would either (a) duplicate the dispatch surface or (b) require migrating every existing handler to the streaming form (~800 LOC of avoidable churn).
- **MUST NOT** reuse the Wave 7b citations/widget pattern (return tokens via `ToolResult.Metadata["stream-tokens"]`) for streaming. Buffering tokens in the return value defeats streaming latency.
- **MUST NOT** carry `HttpResponse` or `HttpContext` on `ChatInvocationContext`. The delegate signature `Func<DocumentStreamEvent, CancellationToken, Task>` deliberately hides the HTTP transport; handlers stay testable without web infrastructure.
- **MUST NOT** introduce a new SSE channel (5th pane / event-bus channel). The document-stream channel is the **existing** SSE plumbing wired through `ChatEndpoints.WriteDocumentStreamSSEAsync` (R2-023); this ADR codifies how chat-tool handlers reach it, not a new channel. Pane-event-bus channels remain at 4 (ADR-030).
- **MUST NOT** weaken the chat-tool capability gate. `WorkingDocumentHandler` ships with `sprk_requiredcapability = "write_back"` per Wave 7b infrastructure, replacing the hardcoded `if (capabilities.Contains(PlaybookCapabilities.WriteBack))` gate.

---

## Key pattern

### Adding `DocumentStreamWriter` to ChatInvocationContext

```csharp
public record ChatInvocationContext : ToolInvocationContextBase
{
    // ... existing fields (ChatSessionId, DecisionId, MatterId, KnowledgeScope, ...) ...

    /// <summary>
    /// Optional per-request writer for document-stream SSE side-channel events.
    /// Bound by ChatEndpoints when the active session/playbook is write-back-capable.
    /// Handlers that emit DocumentStreamEvent (currently WorkingDocumentHandler) read this
    /// field and emit directly during streaming. Null when document streaming is not wired
    /// for the current request — handlers MUST check for null and degrade gracefully.
    /// </summary>
    /// <remarks>
    /// ADR-033 binding pattern. ADR-015: this delegate is a side-effect emitter; it MUST
    /// NOT be invoked from a logging context (the delegate writes content to the SSE pipe,
    /// not to the structured-log sink).
    /// </remarks>
    public Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task>? DocumentStreamWriter { get; init; }
}
```

### Handler emit pattern (WorkingDocumentHandler exemplar)

```csharp
public async Task<ToolResult> ExecuteChatAsync(
    ChatInvocationContext context,
    AnalysisTool tool,
    CancellationToken cancellationToken)
{
    var streamWriter = context.DocumentStreamWriter
        ?? return ToolResult.Failure("DocumentStreamWriter not wired; document edit unavailable for this session.");

    var operationId = Guid.NewGuid();
    var tokenIndex = 0;
    var contentBuilder = new StringBuilder();

    await streamWriter(new DocumentStreamStartEvent(operationId, TargetPosition: "document", OperationType: "replace"), cancellationToken);
    try
    {
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            var tokenText = update.Text;
            if (!string.IsNullOrEmpty(tokenText))
            {
                contentBuilder.Append(tokenText);
                await streamWriter(new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex), cancellationToken);
                tokenIndex++;
            }
        }
        var contentHash = ComputeContentHash(contentBuilder.ToString());
        await streamWriter(new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex, ContentHash: contentHash), cancellationToken);
        return ToolResult.Success($"Working document edited. Streamed {tokenIndex} tokens. OperationId={operationId}.");
    }
    catch (OperationCanceledException)
    {
        await streamWriter(new DocumentStreamEndEvent(operationId, Cancelled: true, TotalTokens: tokenIndex), cancellationToken);
        return ToolResult.Success($"Document edit cancelled. {tokenIndex} tokens emitted before cancellation.");
    }
    catch (Exception ex)
    {
        await streamWriter(new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex,
            ErrorCode: "LLM_STREAM_FAILED", ErrorMessage: "Document editing failed."), cancellationToken);
        return ToolResult.Failure($"Edit failed. Operation: {operationId}. Error: {ex.GetType().Name}.");
    }
}
```

### Wiring point (ChatEndpoints / SprkChatAgentFactory)

The factory (or adapter) constructs `ChatInvocationContext` per LLM tool-call. Within that construction site, when an `httpContext` is available, set `DocumentStreamWriter = ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)` (existing helper from R2-023). When `httpContext` is unavailable (background processing, replay, tests), leave the field null.

---

## Rationale

The pre-Wave-9 hardcoded `WorkingDocumentTools` already proved this pattern works in production (R2-023): it accepts the SSE writer via constructor and emits events during streaming. Migrating to `IToolHandler` is therefore not "adding streaming support" but **lifting the writer-delegate plumbing from ctor injection (per-tool) to context injection (per-call)**.

The Wave 9 deep analysis (project notes `wave-09-streaming-analysis.md` from the prior session) reviewed three alternatives:

| Alternative | Cost | Verdict |
|---|---|---|
| **A: Streaming method on IToolHandler** (`ExecuteChatStreamingAsync` returning `IAsyncEnumerable<TokenChunk>`) | Touches every handler; duplicates dispatch in adapter; pulls IChatClient streaming semantics into the framework boundary | REJECTED — over-engineered |
| **B: Parallel non-IToolHandler streaming-handler class** | Keeps `WorkingDocumentTools` outside the data-driven registry; duplicates capability-gate logic; perpetuates Q9 known-limit | REJECTED — fragments the chat-tool framework |
| **C: Context-side writer delegate** (this ADR) | Adds 1 optional field to `ChatInvocationContext`; preserves `IToolHandler` contract; handler implementation mirrors `WorkingDocumentTools` 1-for-1 | CHOSEN |

Option C minimizes the surface area of the change while completing the Q9 migration (10 of 10 chat tools migrated to typed handlers).

---

## Test obligations

Per CLAUDE.md §10 (BFF Hygiene § F.3 test update obligation), the Wave 9 PR MUST add:

1. **WorkingDocumentHandlerTests** — exercising all three methods (`EditWorkingDocument`, `AppendSection`, `WriteBackToWorkingDocument`) with mocked `IChatClient`, mocked `IWorkingDocumentService`, and a captured `DocumentStreamWriter` delegate that records the event sequence. Assertions:
   - Correct event sequence (Start → N×Token → End) per method
   - Cancellation path emits terminal End with `Cancelled: true`
   - Error path emits terminal End with `ErrorCode` populated
   - ADR-015 telemetry: log capture asserts no token content above Debug
   - SHA-256 hash matches the assembled content
   - Null `DocumentStreamWriter` returns `ToolResult.Failure` (degrade gracefully)
2. **ChatInvocationContext wiring test** — assert `ChatEndpoints` (or factory) sets `DocumentStreamWriter` correctly when httpContext is present and leaves null otherwise.
3. **No regression**: existing 6 chat-tool handler tests (DocumentSearch, KnowledgeRetrieval, VerifyCitations, WebSearch, CodeInterpreter, LegalResearch) MUST continue to pass — none of them read `DocumentStreamWriter`, so the new field MUST not affect their behavior.

---

## Integration with other ADRs

| ADR | Relationship |
|---|---|
| ADR-013 (AI as BFF extension + facade boundary) | Handler is an AI-internal type — does not surface through `Services/Ai/PublicContracts/`. Capability gate via `sprk_requiredcapability` preserved per ADR-013 routing rules. |
| ADR-014 (AI caching) | Streaming tokens are transient — MUST NOT be cached. Per-call `ContentHash` is the cacheable artifact, not the token stream. |
| ADR-015 (AI data governance) | Document content + prompts + tokens MUST NOT appear in logs above Debug. Telemetry from this handler emits operation IDs + token counts + decision IDs only. |
| ADR-016 (rate limits) | Inner streaming LLM calls inherit the outer chat-session's concurrency slot; no separate semaphore. |
| ADR-018 (kill switches) | `WorkingDocumentHandler` requires `IChatClient` + `IWorkingDocumentService` + `IAnalysisOrchestrationService` — all gated by `Analysis:Enabled` + `DocumentIntelligence:Enabled` per ADR-018. When kill-switched off, the handler is simply not auto-discovered (its DI deps don't resolve), matching the hardcoded path's pre-R6 behavior. |
| ADR-019 (ProblemDetails) | Streaming failures emit terminal `DocumentStreamEndEvent` with error code; the LLM-visible `ToolResult.Failure` carries the user-readable summary. |
| ADR-029 (BFF publish hygiene) | Net additions: `WorkingDocumentHandler.cs` (~600 LOC), `WorkingDocumentHandlerTests.cs` (~500 LOC), 1 seed row JSON. BFF publish size impact: <0.1 MB compressed (estimated). Well within R6 ≤+5 MB budget. |
| ADR-030 (PaneEventBus 4-channel) | Document-stream SSE is a SEPARATE SSE pipe from PaneEventBus. The 4 pane channels (workspace / context / conversation / safety) are unchanged. |

---

## What this ADR is NOT

- **Not** a generalized "streaming chat-tool" framework. The pattern is specific to side-channel SSE events emitted DURING handler execution. The Wave 7b citation/widget Metadata pattern remains correct for one-shot data side-channels.
- **Not** an extension of the `IToolHandler` interface. The interface is unchanged.
- **Not** a license for chat-tool handlers to assume `DocumentStreamWriter` is always non-null. Handlers that need it MUST check + degrade gracefully.

---

## References

- Wave 9 deep analysis: `projects/spaarke-ai-platform-unification-r6/notes/wave-09-streaming-analysis.md` (prior session — see git log for the wave-08 + wave-09 dispatch notes)
- Pre-R6 hardcoded behavior reference: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WorkingDocumentTools.cs` (the class being migrated)
- Wave 7b infra reference: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` (citations/widget post-processing — sibling side-channel pattern)
- "ADRs Are Defaults" operating principle: project CLAUDE.md § 🚨 ADRs Are Defaults — Challenge When Warranted
- Full ADR (rationale history, migration log): `docs/adr/ADR-033-streaming-chat-tool-side-channel.md`
