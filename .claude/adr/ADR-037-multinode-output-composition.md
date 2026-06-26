# ADR-037: Multi-Node Output Composition (Concise)

> **Status**: Accepted
> **Domain**: BFF AI / playbook execution + FE widget rendering
> **Last Updated**: 2026-06-25
> **Source project**: `chat-routing-redesign-r1` Wave 5-C (Phase 5R FR-52 through FR-55)
> **Cross-references**: extends [ADR-033](ADR-033-streaming-chat-tool-side-channel.md) (preserves Path 3 streaming invariant); reinforces [ADR-013](ADR-013-ai-architecture.md) (playbook execution stays in `Services/Ai/`); reinforces [ADR-015](ADR-015-ai-data-governance.md) (section names are deterministic identifiers; content NOT logged separately from SSE payload); reinforces [ADR-021](ADR-021-fluent-design-system.md) (FE widget uses Fluent v9 semantic tokens).

---

## Decision

Add a new playbook execution node type — **`NodeType.DeliverComposite`** — that composes N upstream Action node outputs into a single section-name-keyed output payload, and stream those sections to the FE via three new SSE event types (`section_started`, `section_data`, `section_completed`) keyed by section NAME (not schema position).

**Rationale**: The legacy `NodeType.Output` + `StructuredOutputStreamWidget` model had 5 brittle coordination points (action's `outputSchema`, output node's `outputSchema`, widget's hardcoded field positions, SSE `FieldDelta` indexing, and the implicit "schema ordinal == field ordinal" assumption). Renaming any field or reordering the schema broke rendering silently. Section-name-keyed composition reduces coordination to **2** points (section name + section state), eliminates the schema-position fragility, and makes each section independently composable.

---

## When Composite vs Single-Action Output

| Use composite | Use single-action Output |
|---|---|
| Workspace widget that renders multiple distinct sections (Summary + Key Terms + Action Items) | Chat-side text response — one streamed paragraph |
| Per-section authoring of prompts (each Action node has its own focused prompt) | Single LLM call produces all content in one structured output |
| Future per-section regeneration (user edits one section → rerun only its Action node) | All-or-nothing replacement |
| **Workspace rendering** (`destination=workspace`) | **Chat rendering** (`destination=chat`) — chat sibling stays single-action; no benefit from composition |

The `summarize-document-for-workspace@v1` playbook is the canonical first migration (task 118R). The chat sibling `summarize-document-for-chat@v1` stays single-action — chat doesn't benefit from per-section streaming.

---

## Constraints

### ✅ MUST

- **MUST** preserve binary-compat for the existing `NodeType.Output` enum ordinal — append new value, never renumber (Dataverse option-set persistence)
- **MUST** preserve `FieldDelta` SSE emission for unmigrated playbooks — the FE widget detects event type at runtime and routes to legacy or section-keyed renderer
- **MUST** emit `section_started` → `section_data` → `section_completed` in that order, per section
- **MUST** key SSE events by section NAME (not index, not schema position)
- **MUST** log only `(sectionCount, sectionNames=[...], totalLatencyMs, perSectionLatencyMs=[...])` — section names are deterministic identifiers
- **MUST** preserve [ADR-033](ADR-033-streaming-chat-tool-side-channel.md) Path 3 streaming — chat-summarize `StreamStructuredCompletionAsync` + `FieldDelta` events unchanged

### ❌ MUST NOT

- **MUST NOT** migrate a playbook to `DeliverComposite` without re-authoring its widget contract — section names become the API surface
- **MUST NOT** inject `Func<ChatSseEvent, ...>` into `DeliverCompositeNodeExecutor` — the executor returns the composite payload; the orchestrator owns SSE emission (per [ADR-013](ADR-013-ai-architecture.md) separation of concerns)
- **MUST NOT** delete legacy `FieldDelta` emission until ALL production-bound playbooks have been migrated (currently: only `summarize-document-for-workspace@v1` is queued; others continue legacy)

---

## Reference Implementation (chat-routing-redesign-r1 Phase 5R)

| Task | Commit | Surface |
|---|---|---|
| 114R | `f8cb5f365` | `Services/Ai/Nodes/DeliverCompositeNodeExecutor.cs` + `NodeType.DeliverComposite = 100_000_004` + `ActionType.DeliverComposite = 42` |
| 114a | (current wave) | `Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs` + `PlaybookOrchestrationService` emits per-section events |
| 114b | (current wave) | `StructuredOutputStreamWidget` reworked to consume section-keyed events; legacy `FieldDelta` path preserved |
| 118R (future) | (queued) | Dataverse data update: migrate `summarize-document-for-workspace@v1` to `DeliverComposite` |

---

## Backward-Compat Invariants

| Invariant | Enforced By |
|---|---|
| Single-action `NodeType.Output` playbooks emit `FieldDelta` unchanged | `DeliverCompositeNodeExecutor` only handles `ActionType.DeliverComposite`; the `DeliverOutputNodeExecutor` path is untouched |
| Existing playbook canvas-config persistence works | New `NodeType` value APPENDED at ordinal 100_000_004; existing ordinals preserved |
| FE renders legacy playbooks correctly | Widget detects event type at runtime; `FieldDelta` → legacy renderer; `section_*` → new renderer |
| `summarize-document-for-chat@v1` (chat sibling) NOT migrated | Per "When Composite vs Single-Action Output" decision — chat doesn't need composition |

Regression tests guarding these:
- `DeliverCompositeNodeExecutorTests.ExistingSingleActionOutputNode_BackwardCompat_DeliverCompositeExecutorDoesNotHandleDeliverOutput` (114R)
- `PlaybookOrchestrationServiceSectionStreamingTests` legacy-FieldDelta cases (114a)
- `StructuredOutputStreamWidget` legacy `FieldDelta` test (114b)

---

## Migration Path

1. **114R landed** (foundational): engine knows `DeliverComposite`; executor returns sections; orchestrator + FE not yet wired.
2. **114a landed** (BE streaming): orchestrator emits `section_*` events when terminal node is `DeliverComposite`.
3. **114b landed** (FE rendering): widget consumes `section_*` events; legacy `FieldDelta` preserved.
4. **118R queued** (data): migrate `summarize-document-for-workspace@v1` playbook canvas to use `DeliverComposite` + 3 Action node sections.
5. **Per-playbook follow-up**: any future workspace playbook authored after Phase 5R uses `DeliverComposite` by default. Legacy `NodeType.Output` deprecated for workspace destinations (chat destinations continue legacy indefinitely).

---

## Telemetry (ADR-015 Tier-1)

| Logged | Not Logged |
|---|---|
| `sectionCount`, `sectionNames=[summary, keyTerms, actionItems]`, `totalLatencyMs`, `perSectionLatencyMs=[...]` | Section content text (lives in SSE payload, not duplicated to logs) |
| `actionType=DeliverComposite`, `playbookId`, `runId` | User message, file content, LLM intermediate output |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-037-multinode-output-composition.md](../../docs/adr/ADR-037-multinode-output-composition.md)

For detailed context including:
- The 5-coordination-point fragility this replaces (with examples)
- Section-name-keyed routing rationale
- Per-playbook migration runbook
- When NOT to use composite (chat sibling, single-LLM-call extraction)
- Section-name versioning policy for backward-compat across playbook revisions

---

**Lines**: ~115
