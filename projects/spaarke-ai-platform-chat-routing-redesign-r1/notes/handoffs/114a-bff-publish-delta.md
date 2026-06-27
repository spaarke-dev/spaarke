# Task 114a — BFF publish-delta + ADR-033 non-collision evidence

> **Date**: 2026-06-25
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **Task**: 114a per-section SSE streaming (FR-53)

## Publish-size delta (NFR-01 / ADR-029)

| Measurement | Value |
|---|---|
| Pre-114a baseline (from `current-task.md` per task 117b handoff) | 47.92 MB compressed |
| Post-114a compressed (this task) | 47.93 MB compressed (50,267,474 bytes / 1024 / 1024) |
| **Delta** | **+0.01 MB** (essentially noise — within measurement precision) |
| NFR-01 ceiling | 60 MB compressed |
| Headroom remaining | 12.07 MB |

**Measurement method**: `dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o deploy/api-publish/` then `tar -czf api-publish.tar.gz api-publish/` followed by `ls -la api-publish.tar.gz`. Repeats the procedure used by every BFF-touching task in this project.

**No new package references** — the implementation is pure code addition inside existing modules:
- 1 new file: `Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs` (~180 LOC including XML-doc)
- 3 edits: `ChatSseEventFactory.cs` (+3 factory methods), `IPlaybookOrchestrationService.cs` (+3 enum values + `SectionStreamPayload` + 3 factory methods + nav property), `PlaybookOrchestrationService.cs` (+`EmitDeliverCompositeSectionEventsAsync` helper + 1 conditional dispatch in the node-completion path)

## ADR-033 non-collision evidence (binding)

Per ADR-033 (streaming-chat-tool side-channel preservation), Path 3 chat-summarize streaming MUST remain functional and the new section events MUST NOT collide on event-type names with the existing tool-side-channel surface.

### Path 3 chat-summarize emission surface

The chat-summarize streaming path lives in `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` and emits `AnalysisChunk` records (NOT `ChatSseEvent` / NOT `PlaybookStreamEvent`). The `AnalysisChunk` envelope uses a `Type` discriminator with these values:

```
"text"      // streaming partial content
"complete"  // final chunk with full result
"error"     // error chunk
"delta"     // structured-field token delta (FieldDelta payload)  ← R5 additive
```

Source: `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisChunk.cs`.

### Task 114a's new event-type names

The three new SSE event types in `Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs` use these wire strings:

```
"section_started"
"section_data"
"section_completed"
```

These flow on the `ChatSseEvent` envelope (a different envelope from `AnalysisChunk`), via `PlaybookStreamEvent.SectionStarted` / `SectionData` / `SectionCompleted` factories in the orchestration layer.

### Existing `ChatSseEvent` event-type strings (full inventory, grep evidence)

Grepped `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/`:

```
output_pane             OutputPaneSseEvent.cs
source_pane             SourcePaneSseEvent.cs
source_highlight        SourceHighlightSseEvent.cs
playbook_options        PlaybookOptionsSseEvent.cs
section_started         SectionStreamSseEvents.cs   ← new (114a)
section_data            SectionStreamSseEvents.cs   ← new (114a)
section_completed       SectionStreamSseEvents.cs   ← new (114a)
```

**Conclusion**: ZERO collision. `section_*` event names occupy a distinct namespace from `delta` (chat-summarize), `output_pane`, `source_pane`, `source_highlight`, `playbook_options`.

### Path 3 streaming surface unchanged

The `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` method body was NOT modified by this task. The `IncrementalJsonParser` + `FieldDelta` + `AnalysisChunk.FromDelta` flow is byte-identical to its pre-114a behavior. The `delta`-typed `AnalysisChunk` events emitted on `/api/ai/chat/sessions/{id}/summarize` continue to flow exactly as before.

### Backward-compat invariant test

`PlaybookOrchestrationServiceSectionStreamingTests.SchemaPositionPlaybook_DeliverOutputPath_EmitsZeroSectionEvents`

This test sets up a `NodeType.Output` node dispatched as `ActionType.DeliverOutput` (the legacy schema-position path) and asserts that ZERO `section_*` events are emitted on the orchestrator's stream surface. Task 118R should re-run this test after migrating playbooks to `DeliverComposite` to confirm the migration boundary.

## Build + test summary

| Metric | Value |
|---|---|
| Pre-implementation build warnings | 17 |
| Post-implementation build warnings | 17 (no new warnings introduced) |
| Build errors | 0 |
| New tests added | 9 (`PlaybookOrchestrationServiceSectionStreamingTests`) |
| New tests passing | 9 / 9 |
| Related regression test scope (PlaybookOrchestration + DeliverComposite + ChatSseEventFactory + PlaybookExecutionEngine) | 119 / 119 passing |
| Full BFF unit-test suite | 7918 / 7918 passing (137 pre-existing skips unchanged) |

## Approach chosen

**Approach A (orchestrator-emits)** per ADR-013 separation of concerns:

- Executor (`DeliverCompositeNodeExecutor`) remains PURE — returns structured data via `NodeOutput.StructuredData` (the `CompositeOutputPayload` shape unchanged from task 114R)
- Orchestrator (`PlaybookOrchestrationService.ExecuteNodeAsync`) owns the SSE emission: when a node completes with `actionType == ActionType.DeliverComposite`, it calls `EmitDeliverCompositeSectionEventsAsync` which re-iterates the section list and emits 3 events per section
- Emission is APPENDED to (not REPLACING) the standard `NodeCompleted` event — all existing consumers see unchanged behavior

Approach B (executor-emits via injected delegate) was rejected because it would couple the executor to the streaming concern and require a delegate injection that the existing `INodeExecutor` interface does not support.

## Files touched

### New files
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookOrchestrationServiceSectionStreamingTests.cs`
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/114a-bff-publish-delta.md` (this file)

### Modified files
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/ChatSseEventFactory.cs` (added `CreateSectionStartedEvent` / `CreateSectionDataEvent` / `CreateSectionCompletedEvent`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs` (added `SectionStreamPayload`, 3 `PlaybookEventType` enum values, 3 factory methods, `SectionPayload` property on `PlaybookStreamEvent`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` (added `EmitDeliverCompositeSectionEventsAsync` + conditional dispatch from node-completion path; added `System.Diagnostics` + `Services.Ai.Chat.SseEventTypes` usings)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/114a-per-section-sse-streaming.poml` (status → completed)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/TASK-INDEX.md` (114a → ✅)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/current-task.md` (Last Updated + Recovery update)

## Open follow-ups for the main session

1. **117a orchestrator emit-point** (carried over from task 117b handoff, NOT 114a's territory): the `PlaybookOptionsEventBuilder` is registered in DI but the SSE emit-point in `ChatEndpoints.cs` is still pending the future orchestrator task. 114a did NOT touch this surface.

2. **114b FE widget rework** is now UNBLOCKED — the SSE contract is locked (event-type names + payload shapes + ordering invariant). 114b consumes `section_started` / `section_data` / `section_completed` and renames `StructuredOutputStreamWidget` accordingly.

3. **114c ADR** is now UNBLOCKED — main session can author the ADR for the multi-node Output composition pattern.

4. **118R playbook migrations** — when 118R migrates legacy schema-position playbooks to `DeliverComposite`, the test `SchemaPositionPlaybook_DeliverOutputPath_EmitsZeroSectionEvents` should be re-run against the migrated playbooks to confirm the migration boundary.

## ADR / NFR conformance summary

| Item | Status |
|---|---|
| ADR-013 boundary (`Services/Ai/`) | ✅ All new code lives in `Services/Ai/Chat/SseEventTypes/` + `Services/Ai/PlaybookOrchestrationService.cs` |
| ADR-015 tier-1 logging | ✅ Logged: `sectionCount`, `sectionNames=[...]`, `totalLatencyMs`. NOT logged: section content (canonical record is the SSE wire) |
| ADR-029 publish-size (per-task) | ✅ +0.01 MB delta vs 47.92 MB baseline; 12.07 MB headroom under 60 MB ceiling |
| ADR-033 streaming preservation | ✅ Path 3 chat-summarize `delta` flow untouched; new `section_*` event names don't collide with any existing event-type string |
| FR-53 (section events keyed by section name) | ✅ All three events carry `sectionName` as the load-bearing correlation key |
| Backward-compat invariant (`FieldDelta` unchanged for unmigrated playbooks) | ✅ Asserted by `SchemaPositionPlaybook_DeliverOutputPath_EmitsZeroSectionEvents` |
