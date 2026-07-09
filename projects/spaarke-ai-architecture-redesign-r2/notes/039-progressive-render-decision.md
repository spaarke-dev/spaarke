# Task 039 — Progressive render of dispatched outputs: mechanism decision

> **Task**: `tasks/039-progressive-render.poml` (FR-A1-10 / D-F5, Wave J-parallel)
> **Author**: task-execute (Sonnet 5, effort high)
> **Status**: Implemented

## Requirement recap

D-F5 wants long dispatched capability outputs to render progressively (≥2 visible steps)
rather than as one terminal blob, WITHOUT weakening the ADR-040 storage-precedes-rendering
invariant. The spec explicitly deferred the mechanism choice to implementation time:
"section-keyed streaming preferred; client-reveal fallback — chosen at implementation time."

## What already existed before this task

Investigation found the section-name-keyed rendering *contract* (ADR-037 as amended
2026-07-05) was already fully built end-to-end for the Click dispatch path, from an earlier
project (`ai-architecture-redesign-r1` task 046 / FR-P3-07):

- **Server**: `SessionDispatchOrchestrator.DispatchAsync` emits ONE terminal `complete`
  `AnalysisChunk` carrying the full, already-STORED `SessionOutput.Payload` — built strictly
  AFTER `IOutputRouter.RouteAsync` completes the ledger write (`Services/Ai/Chat/
  SessionDispatchOrchestrator.cs`).
- **Client bridge**: `dispatchConsumer.ts`'s `consumeChunk` already synthesized one
  `section_started` / `section_completed` pair per top-level key of the terminal `result`,
  in declaration order, and published them to the `workspace` PaneEventBus channel.
- **Widget**: `StructuredOutputStreamWidget.tsx` already renders a `sections: Map<string,
  SectionState>` reducer driven by exactly those `section_*` events, with skeletons, a
  streaming cursor, and per-section completion state — i.e. the RENDER surface for
  progressive, section-keyed output already ships.

**The actual gap**: `dispatchConsumer.ts` published all of a result's section events in a
single **synchronous** `for` loop. React 18/19 batches synchronous state updates from the
same call stack into one paint, so — despite the wire-level section-keyed events already
existing — the widget still visually rendered everything in one frame. This reproduces
exactly the anti-pattern D-F5 asks to eliminate ("appearing as one terminal blob"), even
though the underlying event vocabulary was already section-keyed.

## Mechanism chosen: client-side progressive reveal, riding the EXISTING section-keyed contract

This is deliberately a hybrid of the two options named in the POML, not a pick-one:

- It is the **client-reveal fallback** in spirit — no new BFF wire vocabulary, no new SSE
  event types, the terminal `complete` `AnalysisChunk` is unchanged.
- It reuses the **section-name-keyed contract** ADR-037 (as amended) declares binding for
  "ANY composite executor — coded workflow or frozen engine node" — the `section_started`
  / `section_completed` PaneEventBus events dispatchConsumer already emitted.

The only new behavior is **pacing**: a small delay (default 120ms, configurable) between
each section's reveal, so N sections produce a real, perceptible sequence of renders
instead of one batched paint.

### Why not full server-side section-keyed streaming (`DeliverComposite`-style)?

The `NodeType.DeliverComposite` / `ActionType.DeliverComposite` machinery is the FROZEN
playbook-engine path (ADR-037 amendment 2026-07-05: "no new capability lands on it").
The Click dispatch path this task touches (`SessionDispatchOrchestrator` / `Binding` /
`ActionRunner`) is the CURRENT, non-frozen R2 core-stack path and does not go through that
engine at all — there is no composite node to extend, and building a NEW server-side
per-Action-field incremental-streaming mechanism would mean:

1. `ActionRunner`'s structured-completion call is a single non-streaming LLM call producing
   the whole JSON object at once (no incremental per-field generation exists to stream).
2. Introducing NEW SSE event types on the `AnalysisChunk` wire (distinct from the existing
   PaneEventBus `section_*` contract) would be a second render vocabulary for the same
   concept — contrary to ADR-039's "no second dispatch protocol" and CLAUDE.md §11
   (component justification / reuse-first).

Given the existing section-keyed PaneEventBus contract + widget already do exactly the
rendering D-F5 wants, extending them with real pacing is the minimal, ADR-compliant path
that satisfies every acceptance criterion without inventing new machinery.

## How store-before-render is preserved

- **Server**: `Services/Ai/Chat/ProgressiveRenderGuard.cs` (new) — `EnsureStored(SessionOutput)`
  is now called at the render boundary in `SessionDispatchOrchestrator.DispatchAsync`,
  immediately before the stored entry's payload is turned into the terminal chunk. It
  throws `InvalidOperationException` if the entry has no evidence of having gone through
  `IOutputRouter.RouteAsync`'s ledger write (`SessionOutput.CreatedAt == default`). This
  makes the ADR-040 invariant an executable assertion, not just prose — covered by
  `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ProgressiveRenderGuardTests.cs` (positive:
  a genuinely-stored entry passes through unchanged; negative: an entry with no write
  timestamp throws — the acceptance-criteria-mandated "render-ahead attempt fails" test).
- **Client**: `progressiveSectionReveal.ts` (new) only ever operates on the `sections`
  array the caller derives from `chunk.result` — and `chunk.result` is populated
  exclusively by the `'complete'` `AnalysisChunk` case, which the BFF only emits after the
  ledger write completes. There is no code path by which the client module can see
  pre-store content; a dedicated test
  (`dispatchConsumer.test.ts` → "never reveals a section for a non-'complete' chunk")
  proves an adversarial non-`complete` chunk carrying a `result`-shaped payload produces
  ZERO section events.

## Files touched

| File | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ProgressiveRenderGuard.cs` (new) | ADR-040 render-boundary assertion |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` (edit) | Wires the guard in before building the terminal chunk |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ProgressiveRenderGuardTests.cs` (new) | Positive + negative (throws) guard tests |
| `src/client/shared/Spaarke.UI.Components/src/services/progressiveSectionReveal.ts` (new) | Pure section extraction + paced async reveal |
| `src/client/shared/Spaarke.UI.Components/src/services/dispatchConsumer.ts` (edit) | Wires the paced reveal into the existing section-keyed bridge |
| `src/client/shared/Spaarke.UI.Components/src/services/__tests__/progressiveSectionReveal.test.ts` (new) | Extraction + pacing unit tests |
| `src/client/shared/Spaarke.UI.Components/src/services/__tests__/dispatchConsumer.test.ts` (edit) | Pacing + negative render-ahead tests added; existing tests set `sectionRevealDelayMs: 0` to stay fast |

## No second dispatch protocol (ADR-039)

Zero new SSE event types, zero new endpoints, zero new wire shapes. The `AnalysisChunk`
terminal `complete` chunk is byte-for-byte unchanged; the PaneEventBus `section_started`
/ `section_completed` events already existed. This change is pacing-only.
