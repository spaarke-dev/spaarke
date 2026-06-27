# Wave 7 — KnowledgeRetrievalTools migration — STOP-AND-SURFACE

**Status**: ⛔ **HALTED before implementation** per the "ADRs are defaults — challenge when warranted" binding principle (projects/spaarke-ai-platform-unification-r6/CLAUDE.md §97-138).
**Date**: 2026-06-08
**Agent**: Wave 7 sub-agent (pattern-validating role)

## Why halted

The Wave 7 sub-agent prompt explicitly identified three stop-and-report triggers, ALL of which fire in the current code state. The pattern-validating migration that Wave 8 will reuse can NOT be sketched cleanly until the user decides which of three viable paths to take. Proceeding silently with any of them would either (a) build a fragile workaround, (b) require Wave 8 to undo+redo, or (c) silently expand the IToolHandler contract without surfacing.

## The three triggers, verified against current source

### Trigger 1 — `ToolResult` has no `Metadata` dictionary

`src/server/api/Sprk.Bff.Api/Services/Ai/ToolResult.cs` is a fixed-shape record (HandlerId, ToolId, ToolName, Success, ErrorMessage, ErrorCode, Data (JsonElement?), Summary, Confidence, ItemConfidences, Execution, Warnings). There is no extensibility hatch for "citations" or "widget" payloads. Adding `IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }` is an additive interface change (no breaking renames) — but it IS a change to a public-facing record that the 8 typed handlers already consume.

### Trigger 2 — `ToolHandlerToAIFunctionAdapter` does not accumulate citations

The adapter (`Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs`) calls `_handler.ExecuteChatAsync(...)`, returns the `ToolResult` to Microsoft.Extensions.AI, and emits ADR-015 telemetry (handler name + decision id + outcome + duration). It does NOT touch citations and has no `CitationContext` reference. Adding citation accumulation means:
- Extending the adapter's constructor to receive a `CitationContext` (per-message accumulator)
- OR injecting it via the `ChatInvocationContext` factory closure
- AND adding logic to read `ToolResult.Metadata["citations"]` and forward each into `CitationContext.AddCitation(...)`

### Trigger 3 — No SSE/widget post-processing path in the adapter

The current `KnowledgeRetrievalTools.GetKnowledgeSourceAsync` imperatively calls `_sseWriter(...)` with a `ChatSseEventFactory.CreateSourcePaneEvent("DocumentViewer", ...)`. The adapter has no `sseWriter` plumbing. Adding it means:
- Extending the adapter or `ChatInvocationContext` to carry an SSE-writer delegate
- Adding logic to read `ToolResult.Metadata["widget"]` and emit the `source_pane` event

## Why this is bigger than "a quick add"

Per the task's own stop-and-report instructions, if adapter accumulation/SSE-emission is "significant" — surface. This is significant because:

1. **Three shared affordances need to land together**: ToolResult.Metadata field, adapter accumulation, adapter SSE post-processing. Each one in isolation is small (~30 LOC), but together they form the new "citations-in-metadata + widget-in-metadata" contract.

2. **The 4 other Wave 7 / Wave 8 tools all depend on the same contract**:
   - Wave 8: DocumentSearchTools, WebSearchTools, CodeInterpreterTools, LegalResearchTools, VerifyCitationsTool — every one uses `CitationContext.AddCitation(...)` AND most use `_sseWriter` for `source_pane` or `output_pane` events.
   - Wave 7 sibling: TextRefinementTools (no citations, no SSE — safe).
   - Wave 7 sibling: AnalysisQueryTools (no citations, no SSE — safe).

3. **The pattern decided here is the template Wave 8 will copy**: this is why the Wave 7 prompt explicitly says "Quality here matters disproportionately."

## Three viable paths

### Path A — Add `Metadata` to `ToolResult` + accumulate in adapter (RECOMMENDED)

**Change set**:
- Add `IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }` to `ToolResult` (additive; 8 existing handlers unaffected).
- Extend `ToolHandlerToAIFunctionAdapter` constructor with optional `CitationContext? citationAccumulator` + optional `Func<ChatSseEvent, CancellationToken, Task>? sseWriter`.
- After `ExecuteChatAsync` returns, inspect `result.Metadata["citations"]` → forward into accumulator; `result.Metadata["widget"]` → emit `source_pane` / `output_pane` SSE.
- `SprkChatAgentFactory.ResolveTools()` passes its existing `citationContext` + `sseWriter` to the adapter constructor when creating chat-available tool adapters.

**Pros**: Clean separation. Handlers are pure functions; adapter owns cross-cutting concerns. Wave 8 tools copy this template trivially. Citations + SSE are TESTABLE in isolation.
**Cons**: Touches `ToolResult` (record evolution; binary-compat fine since additive). +~70 LOC across 2 files.
**Cost**: ~2-3 hours including tests. **No new ADR needed** — this is implementation refinement within ADR-013 + ADR-015.
**Wave 8 impact**: Migration template ready. Each of 4 Wave 8 tools is ~2 hours of work.

### Path B — Keep handler imperative; let it receive `CitationContext` via context

**Change set**:
- Extend `ChatInvocationContext` to optionally carry a `CitationContext?` reference + `Func<ChatSseEvent, CancellationToken, Task>?` sseWriter.
- Handler's `ExecuteChatAsync` reads them from context and mutates exactly as `KnowledgeRetrievalTools` does today.

**Pros**: Smaller diff. No `ToolResult` change.
**Cons**: Violates the "pure handler" design intent — handlers carry chat-specific I/O concerns. The whole point of `IToolHandler` is symmetric playbook+chat dispatch; pushing chat-only delegates into `ChatInvocationContext` blurs the boundary. ADR-015 telemetry harder to enforce (handler can leak via SSE writer; today the adapter is the choke point). **Anti-pattern relative to typed-handler design.**
**Wave 8 impact**: Same anti-pattern propagates 4× more.

### Path C — Defer to Wave 8 (or later); keep KnowledgeRetrievalTools hardcoded for now

**Change set**: None to BFF. Skip Wave 7 KnowledgeRetrieval migration. Migrate TextRefinement + AnalysisQuery only in Wave 7. Decide on citations-in-metadata pattern in a dedicated infrastructure task BEFORE Wave 8.

**Pros**: Avoids deciding under time pressure. Wave 8 still has 4 tools that depend on the same answer.
**Cons**: Wave 7 fails to validate the "harder case" pattern. The "pattern-validating" purpose of Wave 7 is undermined.
**Wave 8 impact**: Wave 8 cannot start until the infrastructure task lands.

## Recommendation — Path A

Path A is the optimal technical answer per the same reasoning ADR-013 / ADR-015 use: cross-cutting concerns (citations, SSE) belong in the adapter (the architectural boundary already responsible for chat ↔ handler translation + telemetry hygiene), NOT in the handler (the unit of business logic). Handlers stay pure-input-pure-output. Wave 8 inherits a clean template.

Path A does NOT require a new ADR. The `ToolResult.Metadata` addition is additive to an existing AI-internal record (per ADR-013 it lives in `Services/Ai/` not `PublicContracts/`). The adapter changes are within the file `ToolHandlerToAIFunctionAdapter.cs` already audited under task 010. ADR-015 telemetry invariant is PRESERVED (the adapter still emits IDs + outcome + duration only; the Metadata dictionary carries citations + widget keys with deterministic identifiers, NOT user-message content).

## What the agent has NOT done

To keep the work reversible and the decision in the user's hands:
- ❌ No code changes to `Sprk.Bff.Api`
- ❌ No `KnowledgeRetrievalHandler.cs` written
- ❌ No row-JSON files created
- ❌ No edits to `Seed-TypedHandlers.ps1`
- ❌ No edits to `SprkChatAgentFactory.cs`

All findings above are READ-ONLY analysis.

## Files referenced

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/KnowledgeRetrievalTools.cs` (current; lines 30-194)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` (target for accumulation logic)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolResult.cs` (target for Metadata field)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` (extensible for KnowledgeScope)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` lines 43, 58, 137, 309 (citation context lifecycle — Reset per message, exposed via `Citations` getter to endpoint)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` lines 407, 421, 448, 763, 825, 1107, 1139, 1192 (citation context flows through 5 tool constructors + agent)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/CitationContext.cs` (thread-safe accumulator; `AddCitation` returns 1-based id)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatContext.cs` (`ChatKnowledgeScope` record — would extend `ChatInvocationContext` to carry it)

## Awaiting decision

Pick A, B, or C. Path A is the recommended pattern-validator template for Wave 8.
