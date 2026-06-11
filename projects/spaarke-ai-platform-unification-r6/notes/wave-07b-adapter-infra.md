# Wave 7b — Citations + Widget Post-Processing Infrastructure

**Status**: COMPLETE
**Date**: 2026-06-08
**Predecessor**: `wave-07-knowledge-retrieval-migration.md` (stop-and-surface analysis)
**Successor (unblocks)**: Wave 7c (KnowledgeRetrieval + VerifyCitations re-attempt), Wave 8 (4 citation/SSE-state tools)

---

## Summary

The pre-R5 chat tools mutated a shared `CitationContext` accumulator and called an `SseWriter` delegate captured at construction. The R6 `IToolHandler` contract returns a synchronous `ToolResult`. Wave 7b bridges this gap: handlers now declare citations + widget events as `ToolResult.Metadata` (pure-input/pure-output) and the `ToolHandlerToAIFunctionAdapter` performs the side effects (accumulation, SSE emission). Handlers stay testable in isolation; the adapter owns cross-cutting concerns.

This was Path A from the surfacing analysis. No new ADR required (additive change within ADR-013 + ADR-015 envelope).

## Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/ToolResult.cs` | Added `Metadata` init-only property, `ToolResultMetadataKeys` static class (Citations / Widget constants), `ToolResultCitation` record, `ToolResultWidget` record |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | Constructor gained 2 optional params (`citationAccumulator`, `sseWriter`); new private methods `PostProcessMetadataAsync`, `TryAccumulateCitations`, `TryEmitWidgetAsync` + 2 small helpers; called after `ExecuteChatAsync` returns |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Data-driven block (~line 1387) now passes `citationContext` + `sseWriter` to adapter ctor |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` | New section "Returning Citations + Widget Events from Chat-side Handlers (R6 Wave 7b)" with worked example + envelope table + ADR-015 binding |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ToolHandlerToAIFunctionAdapterTests.cs` | Added 13 new tests (citation + widget metadata, malformed handling, ADR-015 sentinel) |

## Pattern documented (worked example from doc)

```csharp
public async Task<ToolResult> ExecuteChatAsync(
    ChatInvocationContext context, AnalysisTool tool, CancellationToken ct)
{
    var hits = await _ragService.SearchAsync(query, ct: ct);

    var citations = hits.Select(h => new ToolResultCitation(
        ChunkId: h.ChunkId, SourceName: h.DocumentName,
        PageNumber: h.PageNumber, Excerpt: h.Snippet)).ToArray();

    var widget = new ToolResultWidget(
        PaneType: "source_pane", WidgetType: "DocumentViewer",
        Data: new { filename = top.DocumentName, page = top.PageNumber },
        CitationId: "1");

    return ToolResult.Ok(HandlerId, tool.Id, tool.Name, new { hits },
        summary: $"Found {hits.Count} results.") with
    {
        Metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations,
            [ToolResultMetadataKeys.Widget]    = widget,
        }
    };
}
```

## Design decisions

- **Constants class over XML docs only** — `ToolResultMetadataKeys` provides discoverability + compile-safety. Wave 7c / Wave 8 handlers reference `ToolResultMetadataKeys.Citations` instead of stringly-typed keys.
- **Envelope records (`ToolResultCitation`, `ToolResultWidget`)** — strongly-typed handler-side shapes that round-trip through the adapter's JSON-normalization layer. Handlers MAY also supply raw `JsonElement` / dictionaries / anonymous objects if they prefer; the adapter normalizes via serialize-then-parse.
- **PaneType-driven SSE dispatch** — adapter routes `"source_pane"` → `ChatSseEventFactory.CreateSourcePaneEvent`, `"output_pane"` → `CreateOutputPaneEvent`. Unknown pane types are logged + skipped (non-fatal).
- **Citation accumulator vs widget writer are independent** — handlers may emit either, both, or neither. Adapter checks each metadata key independently.
- **Resilience contract** — citation parsing failures + SSE writer faults are non-fatal. The handler's `ToolResult` is always returned to the LLM regardless. Errors are logged with type-only + decisionId per ADR-015.
- **Telemetry** — ADR-015 binding: counts + outcome + decisionId only. The sentinel test (`PostProcessing_Telemetry_DoesNotLogCitationOrWidgetContent_Adr015`) places unique sentinel strings into BOTH citation excerpt and widget data, then walks every Mock<ILogger> invocation and asserts neither sentinel appears anywhere in formatted args.
- **Factory wiring** — the data-driven block at `SprkChatAgentFactory.ResolveTools` ~line 1387 already had both `citationContext` and `sseWriter` in scope as method parameters. Forwarding them required only changing the ctor invocation.

## Test coverage

40 adapter tests pass (was 27 pre-Wave-7b; +13 new):

| Test | Asserts |
|---|---|
| `PostProcessing_NoMetadata_PreservesExistingBehavior` | Baseline preserved when handler doesn't set Metadata |
| `PostProcessing_CitationsMetadata_WithAccumulator_AddsToContext` | Citations forwarded into `CitationContext.AddCitation` |
| `PostProcessing_CitationsMetadata_NullAccumulator_DropsSilently` | Null accumulator = drop, no throw |
| `PostProcessing_WidgetMetadata_WithSseWriter_EmitsSourcePane` | `source_pane` SSE event emitted |
| `PostProcessing_WidgetMetadata_OutputPane_EmitsOutputPane` | `output_pane` SSE event emitted |
| `PostProcessing_WidgetMetadata_NullSseWriter_DropsSilently` | Null sseWriter = drop, no throw |
| `PostProcessing_MalformedCitations_LogsWarning_DoesNotThrow` | Malformed citation value logged + skipped |
| `PostProcessing_UnknownPaneType_LogsWarning_DoesNotEmit` | Unknown pane type skipped, no emission |
| `PostProcessing_Telemetry_DoesNotLogCitationOrWidgetContent_Adr015` | ADR-015 sentinel — no excerpt / widget-data text in logs |
| `PostProcessing_SseWriterThrows_NonFatal_HandlerResultStillReturned` | SSE writer fault doesn't bubble up; `ToolResult` flows to LLM |

Existing 27 adapter tests + 359 handler tests in the broader `Handlers` filter remain green. No regressions.

## BFF publish-size delta

| Baseline | Wave 7b publish | Delta |
|---|---|---|
| 45.65 MB | 45.90 MB | +0.25 MB |

Well under the +5 MB single-task threshold (NFR-02) and the 60 MB hard ceiling (ADR-029). No new NuGet packages — purely internal class extensions.

## Constraint check confirmations

| Constraint | Result |
|---|---|
| ADR-013 (PublicContracts boundary) | PASS — `ToolResult` is `Services/Ai/`, NOT `Services/Ai/PublicContracts/`. AnalysisTool DTO unchanged. CRUD-side code does NOT consume `ToolResult.Metadata`. |
| ADR-010 (DI minimalism) | PASS — zero new top-level DI registrations. Factory already had both `citationContext` and `sseWriter` in scope. |
| ADR-014 (per-tenant caching) | PASS — citation accumulator is per-chat-session = per-tenant scope (factory creates it fresh per `CreateAgentAsync` call). |
| ADR-015 (data governance) | PASS — citations + widget metadata carry deterministic source IDs + display metadata only; sentinel-string test verifies no content leakage through telemetry. |
| ADR-029 (publish size) | PASS — +0.25 MB delta. |
| NFR-04 (no Agent Framework) | PASS — no `Microsoft.Agents.*` references introduced. |

## Confirmation existing tests pass unchanged

```
Adapter tests: 27 pre-existing + 13 new = 40 passing
Handlers filter (handlers + adapter + dispatch): 359 passing
Build: 0 errors, 16 warnings (baseline)
```

## Wave 7c + Wave 8 unblocked

The infrastructure now supports the surfacing scenario:

- **Wave 7c (KnowledgeRetrieval re-attempt)**: handler returns `ToolResultMetadataKeys.Citations` + `ToolResultMetadataKeys.Widget` (source_pane / DocumentViewer); adapter performs accumulation + SSE emission.
- **Wave 7c (VerifyCitations)**: handler returns citation envelopes; adapter accumulates into per-turn `CitationContext`.
- **Wave 8 (DocumentSearch, WebSearch, CodeInterpreter, LegalResearch)**: same pattern; the worked example in `HandlerRegistrationConventions.md` is the template.

Each of these handlers now needs ~30 LOC + a handful of tests — the cross-cutting plumbing is solved once here.
