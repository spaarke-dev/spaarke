# Wave 9 Stage 2 — ChatInvocationContext.DocumentStreamWriter wiring

> **Date**: 2026-06-08
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **ADR**: ADR-033 (Streaming chat-tool side channel)
> **Stage**: 2 of 4 — infrastructure prerequisite for Stage 3 `WorkingDocumentHandler`

## Scope

Stage 2 lifts the per-request document-stream SSE writer delegate (already constructed inside the legacy `WorkingDocumentTools` block since R2-023) to the chat-tool framework boundary so the upcoming typed `WorkingDocumentHandler` (Stage 3) can receive it via `ChatInvocationContext` without taking a separate DI dependency or constructor parameter.

## Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` | Added nullable `DocumentStreamWriter` field per ADR-033 §4.1 verbatim (type signature, XML doc, `init` accessor). |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | Added optional `documentStreamWriter` ctor parameter (4th optional, after `logger`/`citationAccumulator`/`sseWriter`); stored on private field; forwarded onto each per-call `ChatInvocationContext` via the `with`-expression inside `InvokeCoreAsync`. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Hoisted `documentStreamWriter` (nullable) to the top of `ResolveTools` (~line 745). Reused inside the legacy `WorkingDocumentTools` block (coalesced to no-op for the legacy class's non-nullable ctor contract) and passed to the adapter (NULL when `httpContext` is unavailable, per ADR-033 §3.1). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ToolHandlerToAIFunctionAdapterTests.cs` | Added 5 new tests covering: non-null writer forwarded to context (round-trip via captured delegate); null writer leaves context field null; default-omitted parameter is null; existing context fields unaffected; same writer reference across invocations. |

## Hoist location

`SprkChatAgentFactory.cs` lines ~744–763:

```csharp
var tools = new List<AIFunction>();

// ADR-033 (R6 Wave 9): hoisted document-stream SSE writer. Built ONCE per ResolveTools
// call and consumed in two places:
//   1. The legacy WorkingDocumentTools block below (which requires a non-null delegate,
//      so we coalesce to a no-op when httpContext is unavailable). This block exits
//      in Wave 9 Stage 4 once the typed WorkingDocumentHandler is the sole emitter.
//   2. The data-driven adapter construction (FR-11 block ~line 1290) where the writer
//      is passed to ToolHandlerToAIFunctionAdapter and forwarded onto each per-call
//      ChatInvocationContext.DocumentStreamWriter so the typed WorkingDocumentHandler
//      can emit Start → N×Token → End events directly during streaming.
//
// The adapter receives the NULLABLE variant (null when httpContext is unavailable)
// per ADR-033 §3.1 — the typed handler checks for null and degrades gracefully via
// ToolResult.Failure with a clear "no stream writer wired" message. The no-op
// fallback below is specific to the LEGACY WorkingDocumentTools class which
// requires a non-null delegate by ctor contract.
var documentStreamWriter = httpContext != null
    ? Api.Ai.ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)
    : null;
```

Consumers:
- Legacy WorkingDocumentTools block (~line 829): `documentStreamWriter ?? ((_, _) => Task.CompletedTask)` — keeps the no-op fallback for the legacy class's non-nullable ctor.
- Adapter construction (~line 1302): `documentStreamWriter: documentStreamWriter` — passed as nullable per ADR-033.

## ADR-033 conformance

- §4.1 (ChatInvocationContext field shape) — matches verbatim: `Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task>?`, `init` accessor, XML doc including ADR-015 sub-rule.
- §4.3 (wiring point) — implemented in `SprkChatAgentFactory.ResolveTools` (lines 744+ for the construction, 1302 for the pass-through into the adapter). The adapter's `InvokeCoreAsync` sets the field on the per-call `ChatInvocationContext` via the `with`-expression.
- §3.1 MUST — null-propagation preserved (adapter does NOT coalesce); per-call context field stays null when writer not wired; the typed handler is responsible for null-check + degrade.
- IToolHandler interface: UNCHANGED (confirmed). Only the context type and the adapter were modified.

## Deviations from ADR-033

NONE. All MUST/MUST NOT clauses honoured.

## Stop-and-surface items

NONE. The hoist required ~20 lines total (1 add + 1 modification of the existing no-op fallback + 1 line in the adapter ctor signature + 1 line in the per-call `with`-expression + 1 line at the adapter call-site). No `ChatEndpoints` handlers touched. No cascade to other adapter instantiation sites — the `SprkChatAgentFactory.ResolveTools` block (line 1302) is the SOLE adapter construction site in the repository (verified via `Grep` for `new ToolHandlerToAIFunctionAdapter`).

## Build + test verification

```
dotnet build src/server/api/Sprk.Bff.Api/
→ 0 errors, 16 baseline warnings ✓

dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ToolHandlerToAIFunctionAdapter"
→ Passed: 45 (was 40 pre-change; 5 new wiring tests), Failed: 0, Skipped: 0

dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Services.Ai"
→ Passed: 3554 (was 3549 baseline; +5 from this change), Failed: 0, Skipped: 22 (baseline)
```

## New test coverage (per ADR-033 §7.2 + Stage 2 spec)

1. `InvokeAsync_DocumentStreamWriter_NonNull_ForwardedToContext` — non-null path with round-trip emit
2. `InvokeAsync_DocumentStreamWriter_Null_LeavesContextFieldNull` — explicit null preserved (no coalesce)
3. `InvokeAsync_DocumentStreamWriter_DefaultsToNull_WhenOmitted` — default-param backward compatibility
4. `InvokeAsync_DocumentStreamWriter_DoesNotAffectExistingContextFields` — regression guard for ChatSessionId / TenantId / DecisionId / KnowledgeScope / RequestedToolName / ToolArgumentsJson
5. `InvokeAsync_DocumentStreamWriter_FreshlyAttachedPerInvocation` — adapter-scoped writer is shared across per-call contexts

## Next stages

- **Stage 3** (parallel agent): implements `Services/Ai/Handlers/WorkingDocumentHandler.cs` consuming `context.DocumentStreamWriter` per ADR-033 §4.2 exemplar. Tests for the handler itself land in Stage 3.
- **Stage 4** (main session, post-Stage-3): removes the legacy `WorkingDocumentTools` hardcoded block from `SprkChatAgentFactory.cs` once the seed row for the handler is verified working end-to-end. The hoisted `documentStreamWriter` and the coalesce-to-no-op fallback inside that block are both removed in Stage 4; only the adapter pass-through remains.
