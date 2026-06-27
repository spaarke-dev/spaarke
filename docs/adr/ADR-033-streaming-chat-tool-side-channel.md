# ADR-033: Streaming chat-tool side channel

> **Status**: Accepted
> **Domain**: BFF AI / chat-tool framework
> **Date Accepted**: 2026-06-08
> **Concise version**: [`.claude/adr/ADR-033-streaming-chat-tool-side-channel.md`](../../.claude/adr/ADR-033-streaming-chat-tool-side-channel.md)
> **Source project**: `spaarke-ai-platform-unification-r6` Wave 9 (Q9 chat-tool migration finalization — closes the migration of 10/10 pre-R5 hardcoded chat tools to typed `IToolHandler` implementations)
> **Cross-references**:
> - Extends [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) (AI as BFF extension + facade boundary) — adds the side-channel emission contract for chat-tool handlers
> - Reinforces [ADR-015](../../.claude/adr/ADR-015-ai-data-governance.md) (data governance) — streaming tokens MUST NOT appear in logs above Debug
> - Reinforces [ADR-014](../../.claude/adr/ADR-014-ai-caching.md) (caching) — streaming tokens are transient; only the per-call SHA-256 content hash is cacheable
> - Independent of [ADR-030](../../.claude/adr/ADR-030-pane-event-bus.md) (4-channel PaneEventBus) — document-stream SSE is a SEPARATE SSE pipe from PaneEventBus; this ADR does not add a 5th channel
> - Pairs with the Wave 7b citations/widget side-channel pattern (`ToolHandlerToAIFunctionAdapter.PostProcessMetadataAsync`) — see § 5.2 for the binding two-channel table

---

## 1. Context

### 1.1 The Q9 chat-tool migration

R6 Pillar 2 (Tool Registry + 8 Typed Handlers) consolidates the conversational chat-agent's tool surface onto the same data-driven `IToolHandler` framework used by the playbook-execution path. Ten pre-R5 hardcoded chat-tool classes (`DocumentSearchTools`, `WebSearchTools`, ...) need to migrate to typed `IToolHandler` implementations registered via `sprk_analysistool` Dataverse rows.

Waves 1–8 of the project successfully migrated 9 of the 10 tools using two complementary patterns:
- **Direct migration** (Waves 1, 7): trivial classes (`AnalysisQueryTools`, `TextRefinementTools`) — translate directly to `IToolHandler` with no contract gap.
- **Wave 7b infrastructure** (Waves 7c, 8): tools that emit **citations** or one-shot **widget events** (DocumentSearch, KnowledgeRetrieval, VerifyCitations, WebSearch, CodeInterpreter, LegalResearch) — `ToolResult.Metadata["citations"]` + `ToolResult.Metadata["widget"]` envelope returned by the handler; `ToolHandlerToAIFunctionAdapter.PostProcessMetadataAsync` performs the side effects after the handler returns.

`WorkingDocumentTools` is the last pre-R5 class to migrate. It is qualitatively different from all 9 already-migrated tools: it makes inner **streaming** LLM calls (via `IChatClient.GetStreamingResponseAsync`) and emits **document-stream SSE events** — `DocumentStreamStartEvent` → N × `DocumentStreamTokenEvent` → `DocumentStreamEndEvent` — over a separate SSE pipe from the chat-output stream. The frontend renders these tokens into the in-place document editor surface as they arrive. Buffering the tokens until the handler returns would add the full LLM round-trip latency to perceived edit speed and defeat the streaming UX.

### 1.2 Pre-Wave-9 framing (over-engineered, corrected)

The Wave 9 pre-dispatch analysis initially framed this as:

> *"The `IToolHandler` contract assumes single-value return (`Task<ToolResult>`) but `WorkingDocumentTools` needs streaming. We need to extend `IToolHandler` with a streaming overload (`ExecuteChatStreamingAsync` returning `IAsyncEnumerable<TokenChunk>`). That requires NFR-03 revision (no new ADRs in R6) and an interface change touching all existing handlers."*

On inspection, this framing was incorrect:

- `WorkingDocumentTools` itself **already** returns a single `Task<string>` summary to the LLM. The streaming is a **side effect** — an inner streaming LLM call whose tokens are piped to a per-request SSE writer delegate already passed by `SprkChatAgentFactory` at tool construction.
- The "streaming" is not the AIFunction's return value; it is a side-channel emission via a callback.
- The existing `IToolHandler.ExecuteChatAsync(ChatInvocationContext, AnalysisTool, CancellationToken) → Task<ToolResult>` signature is sufficient.
- The change needed is therefore **not an interface extension** but a small **context-field addition**: pass the writer delegate via `ChatInvocationContext` instead of via constructor.

### 1.3 Why this still needs an ADR

The corrected framing is so small (one nullable field on a context record) that one might argue it doesn't merit an ADR. But:

1. **Future-proofing**: future chat-tool authors will encounter similar streaming use cases. Without this ADR, they will likely re-derive the over-engineered framing (interface extension) by analogy with non-streaming SSE side-channels handled at the adapter (Wave 7b citations/widget pattern). The ADR explicitly documents which side-channel pattern applies when.

2. **NFR-03 revision is real**: R6 spec.md NFR-03 says "no new ADRs in R6." Even if the change to the codebase is small, the **commitment to invariance** is what NFR-03 protects, and revising it requires documentation. Per the "ADRs Are Defaults — Challenge When Warranted" operating principle (project CLAUDE.md), surfacing the trade-off is binding when the optimal answer requires modifying a project rule.

3. **Operating-principle exemplar**: this ADR is the first explicit invocation of the ADRs-Are-Defaults principle in R6. Documenting how the principle was applied here (analysis → enumeration → user confirmation → smaller-scope answer) establishes the worked example future tasks consult.

### 1.4 NFR-03 revision

R6 spec.md NFR-03 declared "no new ADRs in R6." The cost of NOT writing this ADR is materially worse than the cost of writing it:

- **No-ADR option A**: defer `WorkingDocumentTools` migration to R7 → leaves the Q9 chat-tool migration incomplete (9/10) → closeout-known-limit for R6, future onboarding friction, conversation-thread overhead.
- **No-ADR option B**: re-implement `WorkingDocumentTools` as a parallel non-IToolHandler class kept outside the data-driven registry → duplicates the capability-gate logic, fragments the chat-tool framework, defeats the Q9 migration's structural goal.
- **One-ADR option**: this ADR + one focused context-field addition → 10/10 chat tools migrated; no closeout-known-limit; one well-documented side-channel pattern for future authors.

The one-ADR option is unambiguously the best technical outcome. NFR-03 is revised per-case for Wave 9. The exception is documented here; NFR-03's remaining scope-discipline force ("be deliberate about new ADRs") is intact.

---

## 2. Decision

Chat-tool handlers that emit **document-stream SSE side-channel events** MUST emit those events via a writer delegate carried on `ChatInvocationContext`, NOT via constructor injection AND NOT via a new method on `IToolHandler`.

### 2.1 Three-part decision

1. **`ChatInvocationContext` gains an optional `DocumentStreamWriter` field** of type `Func<DocumentStreamEvent, CancellationToken, Task>?`. The field is `init`-only (per the immutable-record pattern used for the existing fields).

2. **The wiring point** is `ChatEndpoints` (or whichever chat-session entry point constructs the per-request SSE response writers). When a write-back-capable playbook is active and the request has a valid `HttpContext`, `ChatEndpoints` binds the field to `ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)` (the existing R2-023 helper). When `HttpContext` is unavailable (background processing, replay, unit tests), the field is left null.

3. **The `IToolHandler` interface is unchanged**. The existing `ExecuteChatAsync` signature is sufficient — handlers that need streaming read `context.DocumentStreamWriter` and emit directly during their work; handlers that don't need streaming ignore the field.

### 2.2 Why this fits the existing contract

The Wave 7b infrastructure introduced `ToolResult.Metadata["citations"]` + `Metadata["widget"]` for one-shot side-channel data emitted **after** the handler returns. The adapter consumes the Metadata and emits SSE events.

The document-stream side-channel emits events **during** the handler's execution, while the inner LLM streaming call is in flight. The handler still returns a single `ToolResult` to the LLM at the end (a summary string + a metadata block). The streaming is purely a side-channel emission to a different SSE pipe.

```
┌───────────────────────────────────────────────────────────┐
│ chat-tool handler execution                               │
│                                                            │
│  1. read context.DocumentStreamWriter (delegate)          │
│  2. emit DocumentStreamStartEvent  ───────────────► SSE   │
│  3. iterate IChatClient streaming response                │
│       ├─ emit DocumentStreamTokenEvent ─────────► SSE     │
│       ├─ emit DocumentStreamTokenEvent ─────────► SSE     │
│       └─ … N times …                                       │
│  4. compute SHA-256 hash                                  │
│  5. emit DocumentStreamEndEvent  ─────────────────► SSE   │
│  6. return ToolResult.Success("…summary…")  ──► LLM       │
└───────────────────────────────────────────────────────────┘
```

The LLM sees the single `ToolResult.Success`. The frontend sees the SSE event sequence. Both are correct for their respective consumers.

---

## 3. Constraints

### 3.1 MUST

- **MUST** declare `DocumentStreamWriter` on `ChatInvocationContext` as **nullable** (`Func<...>?`). Handlers that need it MUST check for null and emit `ToolResult.Failure` with a clear "no stream writer wired" diagnostic when nil. This preserves the existing zero-DI handler invocation surface for handlers that don't stream.

- **MUST** wire `DocumentStreamWriter` from `ChatEndpoints` (or the chat-session entry point that constructs SSE response writers) into the `ChatInvocationContext` at construction time. The factory + adapter MUST NOT lose this binding through the lifetime of a single chat turn.

- **MUST** emit a terminal `DocumentStreamEndEvent` in EVERY exit path of the handler (success, cancellation, error). Per ADR-019 + spec FR-12 — no half-open document streams. Frontend editor state depends on the terminal event to finalize the UI; missing it leaves the editor in a "streaming…" indeterminate state.

- **MUST** preserve ADR-015 governance: handlers MUST log token COUNT + operation IDs only. Tokens, prompts, document content MUST NEVER appear in log entries above Debug level. Telemetry capture MUST assert no token content via sentinel-string scan (matches Wave 7c/8 test pattern).

- **MUST** preserve ADR-014 caching rules: streaming tokens are **transient** and MUST NOT be persisted to Redis / Cosmos. The accumulating `StringBuilder` used to compute the SHA-256 content hash MUST live only for the duration of the handler invocation; the hash is the cacheable artifact, not the tokens.

- **MUST** keep `IChatClient` injection inside the handler's DI ctor (NOT via context). `IChatClient` is a singleton DI surface; passing it via per-request context would couple per-request state to per-process state and break the auto-discovery pattern (per ADR-010).

- **MUST** preserve the existing `sprk_requiredcapability = "write_back"` gate. `WorkingDocumentHandler` ships with this capability requirement on its seed row, replacing the hardcoded `if (capabilities.Contains(PlaybookCapabilities.WriteBack))` check in the factory.

### 3.2 MUST NOT

- **MUST NOT** add a streaming-variant method to `IToolHandler` (e.g., `ExecuteChatStreamingAsync` returning `IAsyncEnumerable<TokenChunk>`). The existing contract is sufficient. Adding the variant would either (a) duplicate the dispatch surface in the adapter or (b) require migrating every existing handler to a streaming form (~800 LOC of avoidable churn).

- **MUST NOT** reuse the Wave 7b citations/widget Metadata pattern for streaming. Buffering tokens in `ToolResult.Metadata["stream-tokens"]` would defeat the streaming latency goal — perceived edit speed would equal the full LLM round-trip.

- **MUST NOT** carry `HttpResponse` or `HttpContext` directly on `ChatInvocationContext`. The delegate signature `Func<DocumentStreamEvent, CancellationToken, Task>` deliberately hides the HTTP transport — handlers stay testable without web infrastructure. Tests inject a fake delegate that records the event sequence.

- **MUST NOT** introduce a new SSE channel (5th pane / event-bus channel). The document-stream SSE pipe is **existing** plumbing from R2-023 (`ChatEndpoints.WriteDocumentStreamSSEAsync`). This ADR codifies how chat-tool handlers reach that pipe, not a new channel. Pane-event-bus channels remain at 4 per ADR-030.

- **MUST NOT** weaken the chat-tool capability gate. `WorkingDocumentHandler`'s `sprk_requiredcapability = "write_back"` row data MUST gate the handler's exposure to the LLM. Without the gate, document mutation tools would appear in read-only playbooks.

- **MUST NOT** allow `DocumentStreamWriter` to be invoked from a logging callback or background task that survives the chat-turn boundary. The writer holds a reference to the HTTP response stream; invoking it after the response is complete will throw `ObjectDisposedException`. Handlers MUST emit all events inline during their `ExecuteChatAsync` body, before returning.

---

## 4. Implementation pattern (binding)

### 4.1 ChatInvocationContext field addition

```csharp
public record ChatInvocationContext : ToolInvocationContextBase
{
    // ... existing fields preserved ...

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

### 4.2 Handler emit pattern (WorkingDocumentHandler exemplar)

```csharp
public async Task<ToolResult> ExecuteChatAsync(
    ChatInvocationContext context,
    AnalysisTool tool,
    CancellationToken cancellationToken)
{
    var streamWriter = context.DocumentStreamWriter;
    if (streamWriter is null)
    {
        return ToolResult.Failure(
            "DocumentStreamWriter not wired; document streaming unavailable for this session.");
    }

    var operationId = Guid.NewGuid();
    var tokenIndex = 0;
    var contentBuilder = new StringBuilder();

    await streamWriter(
        new DocumentStreamStartEvent(operationId, TargetPosition: "document", OperationType: "replace"),
        cancellationToken);

    try
    {
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            var tokenText = update.Text;
            if (!string.IsNullOrEmpty(tokenText))
            {
                contentBuilder.Append(tokenText);
                await streamWriter(
                    new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                    cancellationToken);
                tokenIndex++;
            }
        }

        var contentHash = ComputeContentHash(contentBuilder.ToString());
        await streamWriter(
            new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex, ContentHash: contentHash),
            cancellationToken);

        return ToolResult.Success(
            $"Working document edited. Streamed {tokenIndex} tokens. OperationId={operationId}.");
    }
    catch (OperationCanceledException)
    {
        await streamWriter(
            new DocumentStreamEndEvent(operationId, Cancelled: true, TotalTokens: tokenIndex),
            cancellationToken);
        return ToolResult.Success(
            $"Document edit cancelled. {tokenIndex} tokens emitted before cancellation.");
    }
    catch (Exception ex)
    {
        await streamWriter(
            new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex,
                ErrorCode: "LLM_STREAM_FAILED", ErrorMessage: "Document editing failed."),
            cancellationToken);
        return ToolResult.Failure(
            $"Edit failed. Operation: {operationId}. Error: {ex.GetType().Name}.");
    }
}
```

### 4.3 Wiring point (ChatEndpoints / context construction)

```csharp
// At the per-LLM-tool-call context construction site (inside ChatEndpoints.SendMessageAsync
// or wherever ChatInvocationContext is built):

Func<DocumentStreamEvent, CancellationToken, Task>? documentSSE =
    httpContext != null
        ? Api.Ai.ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)
        : null;  // background processing / replay / tests — leave null

var ctx = new ChatInvocationContext
{
    ChatSessionId = chatSessionId,
    DecisionId = Guid.NewGuid(),
    RequestedToolName = toolName,
    ToolArgumentsJson = argumentsJson,
    MatterId = matterId,
    KnowledgeScope = knowledgeScope,
    DocumentStreamWriter = documentSSE  // ← new field, ADR-033
};
```

---

## 5. Two-channel side-channel pattern (binding reference)

### 5.1 Pattern table

| Side-channel | Mechanism | Why this mechanism | Example handlers |
|---|---|---|---|
| **Citations + chat-widget events** (Wave 7b) | Handler returns via `ToolResult.Metadata["citations"]` + `ToolResult.Metadata["widget"]`; adapter (`ToolHandlerToAIFunctionAdapter.PostProcessMetadataAsync`) emits side effects after the handler returns. | Citations + widget events are **discrete data emitted once per tool call** after the handler completes its work. Returning via Metadata keeps the handler synchronous (1 yield to caller); adapter centralizes the side-effect logic; handlers stay simple. | DocumentSearch (citations + SearchResults widget), KnowledgeRetrieval (citations + source_pane), VerifyCitations, WebSearch, CodeInterpreter (citations + ChartViewer widget), LegalResearch |
| **Document-stream events** (Wave 9, this ADR) | Handler receives `DocumentStreamWriter` via `ChatInvocationContext`; handler emits Start → N×Token → End events directly during streaming, inside the body of `ExecuteChatAsync`. | Token events are **emitted as the work happens** (during a streaming sub-LLM call), not after the handler returns. Buffering all tokens in `ToolResult.Metadata` would delay perceived latency by the full LLM round-trip duration. Direct emission keeps token latency at the network-token boundary. | WorkingDocument (EditWorkingDocument, AppendSection, WriteBackToWorkingDocument) |

### 5.2 Decision rule for future authors

When adding a new chat-tool handler that needs to emit non-return side-channel data:

1. **Is the data a discrete artifact emitted once per tool call (after the work is done)?** → Wave 7b Metadata-envelope pattern. Return `ToolResult.Metadata["..."]`; let the adapter emit.

2. **Is the data emitted continuously during the tool's work (typically tokens streamed from an inner LLM call)?** → ADR-033 context-side writer pattern. Add a writer delegate to `ChatInvocationContext` if a new event type is needed; emit directly from the handler.

Authors faced with the rare case where BOTH apply (e.g., a chat tool that streams document tokens AND emits a final citation list) can use both mechanisms in the same handler — they are orthogonal.

---

## 6. Migration plan (Wave 9 execution)

Wave 9 dispatches in 3 stages:

### Stage 1 (main session, sequential)
Write ADR-033 (this document + the concise version in `.claude/adr/`). Define the `DocumentStreamWriter` field signature and the wiring point. **Output**: contract locked in.

### Stage 2 (parallel sub-agent A)
Extend `ChatInvocationContext` with the `DocumentStreamWriter` field. Wire it from `ChatEndpoints` (or the chat-session entry point) into the context construction site. **Constraint**: do not modify `SprkChatAgentFactory.cs`'s hardcoded `WorkingDocumentTools` block — main session removes that in Stage 4.

### Stage 3 (parallel sub-agent B)
Create `WorkingDocumentHandler.cs` (implementing `IToolHandler` with `ExecuteChatAsync`); create 1 seed JSON (`infra/dataverse/sprk_analysistool-working-document-row.json` with `sprk_requiredcapability = "write_back"`); create test file (`WorkingDocumentHandlerTests.cs`) following the Wave 7c/8 patterns; create bookkeeping note. **Constraint**: do not modify `SprkChatAgentFactory.cs` or `Seed-TypedHandlers.ps1` — main session merges those in Stage 4.

### Stage 4 (main session, sequential)
- Remove the hardcoded `WorkingDocumentTools` registration block from `SprkChatAgentFactory.cs` (replace with REMOVED comment matching Wave 7/7c/8 pattern).
- Add the new `$RowFiles` entry to `scripts/Seed-TypedHandlers.ps1`.
- Build verify (0 errors expected).
- Test verify (`WorkingDocumentHandlerTests` + broader handler/adapter sweep — no regressions expected).
- Deploy the seed row to Spaarke Dev.
- Commit + push as one Wave 9 commit.
- Update `current-task.md` to Wave 9 complete; Q9 migration finishes 10/10.

### Stage 5 (cleanup, optional)
Delete the legacy `WorkingDocumentTools.cs` class (no remaining consumers after Stage 4). This is OPTIONAL for Wave 9 — Wave 10 may bundle this with the AnalysisExecutionTools + InvokeSummarize/InvokeInsightsQuery bridge deletions.

---

## 7. Test obligations

Per CLAUDE.md §10 § F.3 (test update obligation), Wave 9 PR MUST add:

### 7.1 WorkingDocumentHandlerTests (new)

Coverage targets:
- **Method dispatch**: tests for each of the 3 methods (EditWorkingDocument, AppendSection, WriteBackToWorkingDocument) via the `sprk_configuration.method` discriminator (matches Wave 7c/8 multi-method-handler pattern).
- **Event sequence**: per method, assert Start → N × Token → End sequence with a captured `DocumentStreamWriter` mock delegate. N depends on the mocked `IChatClient` streaming response.
- **Cancellation path**: when `OperationCanceledException` propagates, assert terminal End event with `Cancelled: true` AND no rethrow (the catch handler converts cancellation to a successful summary).
- **Error path**: when inner LLM call throws, assert terminal End event with `ErrorCode = "LLM_STREAM_FAILED"` AND `ToolResult.Failure` returned.
- **Null writer**: when `DocumentStreamWriter` is null on the context, assert `ToolResult.Failure` with the "not wired" diagnostic, AND no `IChatClient` call attempted.
- **SHA-256 hash**: assemble token text into a known string, assert returned hash matches the expected hash.
- **ADR-015 telemetry**: sentinel-string scan over captured log output asserts no token content above Debug.
- **Chat-context dispatch**: integration test via `ToolHandlerToAIFunctionAdapter` confirms the handler dispatches correctly from chat-mode invocation.

### 7.2 ChatInvocationContext wiring test

Add to existing `ChatInvocationContextTests` (or create if doesn't exist):
- When `HttpContext` is present, `DocumentStreamWriter` is set to a non-null delegate.
- When `HttpContext` is null (background processing), `DocumentStreamWriter` is null.
- Existing fields (ChatSessionId, MatterId, KnowledgeScope, etc.) unaffected.

### 7.3 Regression suite

Existing 6 chat-tool handler tests (DocumentSearch 34, KnowledgeRetrieval N, VerifyCitations N, WebSearch 28, CodeInterpreter 26, LegalResearch 27 = 115+ tests from Waves 7c+8) MUST continue to pass. None of them read `DocumentStreamWriter`, so the new field MUST not affect their behavior.

Broader test surface (handler + adapter + factory sweep) MUST stay at the post-Wave-8 baseline of 3549/3571 (0 fail, 22 skip).

---

## 8. Alternatives considered

| # | Alternative | Cost / risk | Verdict |
|---|---|---|---|
| A | **Streaming method on `IToolHandler`** — `ExecuteChatStreamingAsync(context, tool, ct) → IAsyncEnumerable<TokenChunk>` | Touches every existing handler (must add default no-op override); duplicates dispatch in the adapter; pulls `IChatClient` streaming semantics into the framework boundary | REJECTED — over-engineered; doesn't fit non-streaming handlers naturally |
| B | **Parallel non-IToolHandler streaming-handler class** — keep `WorkingDocumentTools` outside the data-driven registry; register it via a separate path | Duplicates the capability-gate logic; fragments the chat-tool framework; perpetuates Q9 known-limit; future onboarding burden | REJECTED — defeats the structural goal of Q9 |
| C | **Context-side writer delegate** (this ADR) | Adds 1 optional field to `ChatInvocationContext`; preserves `IToolHandler` contract; handler implementation mirrors `WorkingDocumentTools` 1-for-1; tests trivially mock the delegate | CHOSEN |
| D | **Buffer tokens in `ToolResult.Metadata["stream-tokens"]`** — reuse Wave 7b pattern | Defeats streaming latency (perceived edit speed = full LLM round-trip); user-visible regression vs pre-R6 behavior | REJECTED — wrong pattern for continuous data |
| E | **Pass `IChatClient` through `ChatInvocationContext`** — make `IChatClient` per-request | Couples per-process state (singleton DI surface) to per-request state; breaks auto-discovery DI pattern (ADR-010) | REJECTED — fights existing DI architecture |
| F | **Return `IAsyncEnumerable<DocumentStreamEvent>` from `ExecuteChatAsync`** | Changes the `IToolHandler` return type; same problems as alternative A but with a different mechanism | REJECTED — same problems as A |

Alternative C minimizes the surface area of the change while completing the Q9 migration. The next-best alternative (A) would touch every existing handler with a default no-op override, doubling the contract surface for one handler's benefit.

---

## 9. Open questions

### 9.1 Does Stage 5 (delete legacy `WorkingDocumentTools.cs`) ship in Wave 9 or Wave 10?

Wave 9 dispatch leaves the legacy class in place to minimize Wave 9 risk. Wave 10 (cleanup wave) bundles:
- Delete `AnalysisExecutionTools.cs` (replaced by Pillar 3 `invoke_playbook`)
- Delete `InvokeSummarizePlaybookTool.cs` (replaced by Pillar 3 `invoke_playbook`)
- Delete `InvokeInsightsQueryTool.cs` (replaced by Pillar 3 `invoke_playbook`)
- Delete `WorkingDocumentTools.cs` (replaced by Wave 9 `WorkingDocumentHandler`)
- Delete other migrated legacy classes if no remaining non-LLM consumers exist (audit per class)

This is documented as a Wave 10 task at Wave 9 commit time.

### 9.2 Does the new field need a corresponding adapter parameter?

`ToolHandlerToAIFunctionAdapter` does not need extension. The adapter constructs `ChatInvocationContext` per LLM tool-call inside its `InvokeAsync` body; the construction site receives `DocumentStreamWriter` from its constructor (passed through by `SprkChatAgentFactory` when wrapping the handler as an `AIFunction`). Adapter signature change: 1 new optional ctor param `documentStreamWriter`.

---

## 10. Consequences

### 10.1 Positive

- **Q9 migration complete**: 10/10 chat tools migrated to typed `IToolHandler`; data-driven registry surface is the single source of truth for chat tools; admins can add/remove/gate tools via Dataverse rows.
- **Side-channel pattern codified**: future chat-tool authors have an explicit two-channel decision table (§ 5.2) to consult. Reduces future re-derivation cost.
- **ADRs-Are-Defaults principle validated**: documents one fully-walked invocation of the operating principle (stop → name → verify → enumerate → wait → decide). Establishes the precedent for future ADR-vs-NFR-03 trade-off cases.
- **`IToolHandler` contract preserved**: 8 existing typed handlers (Wave 1-2 + 7-8) unaffected; no migration churn for non-streaming handlers.

### 10.2 Negative / costs

- **One new ADR** in a project that planned for zero. Mitigated by: clear documentation of the NFR-03 revision rationale; this is the only ADR R6 plans to write.
- **`ChatInvocationContext` surface grows** by one field. Mitigated by: nullable default; existing handler tests unaffected (none read the new field); the field is co-located with `KnowledgeScope` (added in Wave 7c) so the pattern of "context-injected handler-specific delegates" is consistent.
- **Operational dependency**: Stage 2 must land before Stage 3 can run end-to-end. Mitigated by: Stage 1 (this ADR) locks in the contract; Stage 2 and Stage 3 can run in parallel against the locked contract — Stage 3's handler code compiles against the field signature documented here, even if Stage 2's wiring hasn't landed yet.

### 10.3 Neutral / observational

- **Adapter signature grows by 1 optional ctor param** (`documentStreamWriter`). The 9 existing chat-tool handlers don't read it; no behavior change for them.
- **No new SSE channel**. The document-stream SSE pipe is the existing R2-023 plumbing; this ADR codifies the entry path, not new infrastructure.

---

## 11. References

- **R6 spec**: `projects/spaarke-ai-platform-unification-r6/spec.md` § Q9 (chat-tool migration); NFR-03 (no new ADRs — revised per-case for this ADR)
- **Project CLAUDE.md**: `projects/spaarke-ai-platform-unification-r6/CLAUDE.md` § "🚨 ADRs Are Defaults — Challenge When Warranted (binding operating principle)" — the operating principle this ADR documents the first invocation of
- **Wave 7b infra reference**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` — citations/widget post-processing (sibling side-channel pattern)
- **Pre-R6 hardcoded behavior**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WorkingDocumentTools.cs` — the class being migrated
- **R2-023 SSE plumbing**: `ChatEndpoints.WriteDocumentStreamSSEAsync` + `ChatEndpoints.CreateDocumentStreamSseWriter` — existing document-stream SSE pipe (not added by this ADR)
- **Concise version (binding)**: `.claude/adr/ADR-033-streaming-chat-tool-side-channel.md`
- **Q9 migration history**:
  - Wave 7 (trivial): commit `9e3d4f93` (AnalysisQuery, TextRefinement migrated)
  - Wave 7b (infra): commit `66da08ca` (ToolResult.Metadata + adapter post-process + sprk_requiredcapability)
  - Wave 7c (citations): commit `52201189` + `b7c089cc` (KnowledgeRetrieval, VerifyCitations)
  - Wave 8 (citation/SSE-state): commit `3eb7d17d` (DocumentSearch, WebSearch, CodeInterpreter, LegalResearch — 115 new tests)
  - Wave 9 (streaming, this ADR): forthcoming commit (WorkingDocumentHandler)
  - Wave 10 (cleanup): forthcoming commit (delete AnalysisExecutionTools + InvokeSummarize + InvokeInsightsQuery + optional WorkingDocumentTools)
