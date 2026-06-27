# ADR-037: Multi-Node Output Composition

## Status

Accepted (2026-06-25)

## Domain

BFF AI playbook execution + FE widget rendering pipeline

## Context

### The 5-coordination-point fragility we are replacing

Prior to Phase 5R, structured playbook output was rendered via a single `NodeType.Output` node whose `sprk_configjson` declared an `outputSchema` array. The streaming pipeline emitted incremental `FieldDelta` SSE events keyed by FIELD POSITION (ordinal index in the schema). The FE `StructuredOutputStreamWidget` had hardcoded field positions for each playbook's output shape.

This created **five distinct coordination points** that had to stay in lockstep:

1. **Action node's `outputSchema`** — the structured output schema the LLM was asked to produce (used in the `json_schema` response format constraint)
2. **Output node's `outputSchema`** — repeated declaration, sometimes drifting from Action node's
3. **FE widget's hardcoded field positions** — the renderer assumed `output[0] == "summary"`, `output[1] == "keyTerms"`, etc.
4. **SSE `FieldDelta` event's `fieldIndex`** — the streaming layer indexed by integer ordinal, propagating Action node's schema position into the wire format
5. **Implicit "schema ordinal == widget field ordinal" assumption** — the linkage was never explicit; it lived in the AUTHOR's head

**What broke**:

- Renaming a field in the Action node's `outputSchema` without updating the widget broke rendering silently — fields appeared blank or under wrong labels.
- Adding a new field in the middle of the schema shifted every subsequent ordinal, breaking all downstream playbooks that shared the widget contract.
- Re-ordering fields for UX reasons (e.g., promoting `redFlags` above `summary`) required widget code changes.
- Action node prompts couldn't be authored or regenerated independently — they all fed one single Output node's schema.

The user (project owner) identified this fragility during the 2026-06-24 design conversation:

> "The 5-coordination-point `StructuredOutputStreamWidget` model is brittle. Each section/field should have its own Action node feeding into an Output Node that composes for the consumer."

### The architectural goal

Reduce coordination points to **2**:

- **Section name** (semantic string, e.g., `"summary"`, `"keyTerms"`, `"actionItems"`)
- **Section state** (status + accumulated content + structured data + citations)

The Action node's prompt is authored independently. The composite Output node enumerates which Action nodes feed which sections. The SSE wire format carries section NAMES. The widget renders by name.

Adding a new section → add a new Action node + add a new section binding to the composite Output node's `sprk_configjson`. No widget code change. No prompt-side coordination. No ordinal drift.

## Decision

Introduce a new playbook execution node type `NodeType.DeliverComposite` (ordinal `100_000_004`) with `ActionType.DeliverComposite = 42`. The corresponding executor `DeliverCompositeNodeExecutor` reads the composite node's `sprk_configjson`:

```json
{
  "sections": [
    { "sectionName": "summary",     "inputNodeId": "<action-node-guid-1>", "displayLabel": "Summary" },
    { "sectionName": "keyTerms",    "inputNodeId": "<action-node-guid-2>", "displayLabel": "Key Terms" },
    { "sectionName": "actionItems", "inputNodeId": "<action-node-guid-3>", "displayLabel": "Action Items" }
  ],
  "destination": "workspace",
  "widgetType": "structured-output-stream"
}
```

resolves each `inputNodeId` to its execution result from the run context, and produces a `CompositeOutputPayload` keyed by section name.

The orchestration layer (`PlaybookOrchestrationService`) detects `ActionType.DeliverComposite` in the node-output yield loop and emits three SSE events per section:

- `section_started { sectionName, displayLabel?, sectionIndex, totalSections }`
- `section_data { sectionName, textDelta?, structuredData? }` — possibly multiple per section as content streams
- `section_completed { sectionName, finalText?, finalStructuredData?, citations[] }`

The FE `StructuredOutputStreamWidget` maintains a `sections: Record<string, SectionState>` map keyed by section name. The widget detects event type at runtime; `section_*` events route to the new path, `FieldDelta` events route to the legacy path for unmigrated playbooks.

## Alternatives Considered

### Alternative 1: Stay with `FieldDelta` but make the widget schema-driven

Make the widget read the playbook's `outputSchema` at runtime and dynamically render fields based on that schema. This would eliminate the hardcoded field positions but preserves the schema-ordinal contract.

**Rejected** because:
- Still 4 coordination points (Action node schema, Output node schema, FE schema-loader, FE renderer mapping)
- Schema migration story is awful (any field rename breaks runtime parsers)
- Per-section streaming and per-section regeneration are NOT supported (still one schema, one LLM call, all-or-nothing)
- Doesn't unlock per-section authoring

### Alternative 2: Use a generic JSON streaming widget without composite node

Skip the composite node; have a single Action node produce all sections in one structured output, and have the FE widget render a generic JSON tree.

**Rejected** because:
- Loses per-section prompt authoring (one big LLM call instead of N focused ones)
- Loses per-section regeneration (user can't edit just one section)
- Generic JSON tree UI is poor UX — domain-specific labels and ordering still need to be authored somewhere

### Alternative 3: Inject the SSE emitter into the executor

Pass `Func<ChatSseEvent, CancellationToken, Task>? emitSseEvent` into `DeliverCompositeNodeExecutor` so it emits per-section events directly as it composes.

**Rejected** because:
- Couples node execution to streaming concern — violates ADR-013 separation of concerns
- Makes the executor harder to test (need to mock the emit callback)
- Orchestrator already owns SSE emission for other event types; consistency favors keeping it there

### Alternative 4: Replace `NodeType.Output` outright

Deprecate `NodeType.Output` immediately and force all playbooks to migrate to `DeliverComposite`.

**Rejected** because:
- 5+ production-bound playbooks would break overnight
- Chat-destination playbooks don't benefit from composition (single streamed paragraph)
- Migration must be playbook-by-playbook with operator coordination
- Backward-compat via runtime event-type detection costs ~50 LOC; full migration is YAGNI for chat siblings

## Consequences

### Positive

- **Coordination point count drops from 5 to 2** — section name + section state
- **Per-section authoring** — each Action node has its own focused prompt; can be tuned independently
- **Per-section regeneration** — future feature: user edits one section's prompt or input, only that Action node reruns
- **Schema evolution is safer** — renaming sections requires updating ONE place (the playbook's composite config + the widget's section-name renderer mapping); ordinal drift is impossible
- **Better LLM prompt focus** — small per-section prompts produce more reliable structured output than one giant prompt asking for 5 sections
- **FE backward compat** — widget runtime-detects event type; legacy `FieldDelta` playbooks render unchanged

### Negative

- **More nodes per playbook** — `summarize-document-for-workspace@v1` goes from 1 Output node to N Action nodes + 1 Composite node (e.g., 4 nodes instead of 1)
- **Author cognitive load** — section names become an API surface; renaming requires care
- **Two streaming paths in FE** — legacy `FieldDelta` + new `section_*` until migration is complete
- **Engine surface +1 node type** — `DeliverComposite` adds to the executor registry; one more thing to know about

### Neutral

- BFF publish size: +~10 KB (executor + records); negligible vs ~46 MB baseline
- LLM cost may go UP per playbook (N small calls vs 1 large call), but quality goes UP too — the per-section prompts focus the LLM; net cost/quality trade-off is favorable for legal content where each section has different domain language

## Implementation

### Phase 5R Wave 5-C tasks

| Task | Commit | Scope |
|---|---|---|
| 114R | `f8cb5f365` | `DeliverCompositeNodeExecutor`, `NodeType.DeliverComposite = 100_000_004`, `ActionType.DeliverComposite = 42`, DI registration, 23 unit tests |
| 114a | (this wave) | 3 new BFF SSE event types in `Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs`, orchestrator emit wiring, regression test asserting legacy `FieldDelta` path unchanged |
| 114b | (this wave) | `StructuredOutputStreamWidget` rework with `sections: Record<string, SectionState>` map, runtime event-type detection for backward compat |
| 114c | (this ADR) | This document + concise `.claude/adr/ADR-037` + INDEX + CHANGELOG |
| 118R | (queued) | Dataverse data migration: `summarize-document-for-workspace@v1` → multi-node composite (3 Action nodes + 1 Composite); chat sibling `summarize-document-for-chat@v1` STAYS single-action |

### Reference: 114R DeliverCompositeNodeExecutor

The executor is keyed in the `INodeExecutorRegistry` by `ActionType.DeliverComposite`. It receives the run context (with all prior node outputs accessible by node ID) and the current node's `sprk_configjson`. It:

1. Parses the JSON config into `CompositeNodeConfig { Sections, Destination, WidgetType }`
2. For each `CompositeSectionSpec { SectionName, InputNodeId, DisplayLabel }`:
   - Resolves `InputNodeId` to the upstream Action node's `NodeOutput`
   - Wraps its content into a `CompositeSectionResult { SectionName, DisplayLabel, TextContent, StructuredData, Citations }`
3. Returns a single `NodeOutput` whose `StructuredData` is the `CompositeOutputPayload { Destination, WidgetType, Sections }`

The executor is **purely deterministic** — given the same upstream outputs, it produces the same composite. No LLM call. No external state.

### Reference: 114a per-section SSE emission

In `PlaybookOrchestrationService`'s node-output yield loop, after the composite executor produces its `NodeOutput`:

```csharp
if (nodeOutput.ActionType == ActionType.DeliverComposite)
{
    var payload = JsonSerializer.Deserialize<CompositeOutputPayload>(nodeOutput.StructuredData);
    for (int i = 0; i < payload.Sections.Count; i++)
    {
        var section = payload.Sections[i];

        await emitSseEvent(
            ChatSseEventFactory.CreateSectionStartedEvent(
                new SectionStartedSseEventData {
                    SectionName = section.SectionName,
                    DisplayLabel = section.DisplayLabel,
                    SectionIndex = i,
                    TotalSections = payload.Sections.Count,
                }),
            cancellationToken);

        await emitSseEvent(
            ChatSseEventFactory.CreateSectionDataEvent(
                new SectionDataSseEventData {
                    SectionName = section.SectionName,
                    TextDelta = section.TextContent,
                    StructuredData = section.StructuredData,
                }),
            cancellationToken);

        await emitSseEvent(
            ChatSseEventFactory.CreateSectionCompletedEvent(
                new SectionCompletedSseEventData {
                    SectionName = section.SectionName,
                    FinalText = section.TextContent,
                    FinalStructuredData = section.StructuredData,
                    Citations = section.Citations,
                }),
            cancellationToken);
    }
}
else
{
    // Existing FieldDelta emission path — UNCHANGED
}
```

Phase A composite is non-streaming (one consolidated `section_data` per section). A future enhancement could split section content into multiple incremental `section_data` events as the underlying Action node produces tokens — this is forward-compatible with the current event shape.

### Reference: 114b widget rework

The widget maintains:

```typescript
type SectionState = {
  sectionName: string;
  displayLabel?: string;
  sectionIndex?: number;
  totalSections?: number;
  status: 'idle' | 'streaming' | 'completed';
  accumulatedText: string;
  structuredData?: unknown;
  citations?: Citation[];
};

const [sections, setSections] = useState<Record<string, SectionState>>({});
```

`useEffect` subscribes to the SSE event stream. Event-type detection routes:

- `section_started` → upsert section with `status='streaming'`
- `section_data` → append `textDelta` to `accumulatedText`, merge `structuredData`
- `section_completed` → replace with final values, set `status='completed'`
- `FieldDelta` → legacy renderer (unchanged code path)

Renderer iterates section state by `sectionIndex` (when present) or by insertion order. Each section renders header (`displayLabel ?? sectionName`) + content + citations + streaming indicator while `status === 'streaming'`.

## ADR-013 / ADR-015 / ADR-021 / ADR-033 alignment

- **ADR-013** AI architecture: composite executor stays in `Services/Ai/Nodes/`; FE consumer in `src/client/shared/Spaarke.UI.Components/`. Orchestrator owns SSE emission (separation of concerns).
- **ADR-015** Data governance tier-1: section names are deterministic identifiers logged in telemetry; section CONTENT lives in the SSE payload but is NOT duplicated to logs (the SSE wire is the canonical record sent to the client; telemetry doesn't repeat it).
- **ADR-021** Fluent design system: widget uses Fluent v9 semantic tokens (`tokens.colorBrandForeground1`, `tokens.colorNeutralBackground2`) — dark mode auto via theme provider.
- **ADR-033** Streaming preservation: Path 3 chat-summarize (`StreamStructuredCompletionAsync` + `FieldDelta`) is UNCHANGED. Composite path is additive; chat-destination playbooks continue to use Path 3 indefinitely.

## Open Questions

- **Section-name versioning**: when a playbook revision renames a section (e.g., `keyTerms` → `keyClauses`), what's the upgrade path for in-flight chat sessions that have the OLD section state cached on the FE? **Proposed**: section names are immutable for v1; revision = new section name AND new playbook code. v2 of a playbook starts a fresh section map. Not enforced by the engine; operator discipline.
- **Per-section regeneration UX**: the architecture supports it (run only ONE Action node when the user edits one section), but the UX surface (edit-this-section button, regenerate-with-prompt-edit dialog) is out of scope for Phase 5R. Future project.

## Migration Runbook (Per-Playbook)

When migrating a workspace-destination playbook from `NodeType.Output` to `DeliverComposite`:

1. Identify the current `outputSchema` array on the existing Output node (e.g., 5 fields).
2. For each field, author a new Action node with a focused prompt that produces JUST that field's content.
3. Create a new `DeliverComposite` node whose `sprk_configjson` enumerates the 5 sections, each pointing to its corresponding Action node by `inputNodeId`.
4. Delete the legacy Output node.
5. Validate via `Deploy-Playbook.ps1` — JSON-schema gate enforces section-config shape.
6. Smoke-test against the FE — verify each section renders correctly under its `displayLabel`.
7. (Optional) Backfill the playbook's documentation describing the new section contract for downstream consumers.

If the playbook has chat-destination siblings (e.g., `summarize-document-for-chat@v1`), DO NOT migrate them — they continue to use `NodeType.Output` + `FieldDelta` streaming per the "When Composite vs Single-Action Output" decision.

## Status / Rollout

- 114R landed 2026-06-25 (foundational executor + DI + 23 tests; engine knows the new node type but nothing emits/renders it yet)
- 114a + 114b landing in current wave (BFF SSE + FE widget consumption)
- 114c (this ADR) landing alongside
- 118R queued (first playbook migration; proves the end-to-end path)

After 118R lands, the architecture is production-ready. Subsequent workspace playbook authoring uses `DeliverComposite` by default. Chat-destination playbooks continue legacy.
