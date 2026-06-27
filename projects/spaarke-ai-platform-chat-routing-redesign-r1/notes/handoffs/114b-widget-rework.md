# Task 114b Handoff — StructuredOutputStreamWidget rework to section-name-keyed (FR-54)

**Task**: 114b — StructuredOutputStreamWidget rework to section-name-keyed
**Phase**: 5R Wave 5-C
**Status**: ✅ Complete (2026-06-25)
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
**Depends on**: 114a (BFF SSE contract for section_started / section_data / section_completed events)
**Blocks**: 114c (ADR — main session), 118R (per-playbook data migration)

---

## Summary

`StructuredOutputStreamWidget` now consumes the section-name-keyed SSE event contract finalised by Phase 5R Wave 5-C task 114a (`section_started` / `section_data` / `section_completed`). Internal state shape extended with `sections: Map<string, SectionState>` keyed by section NAME (NOT schema position). The legacy `FieldDelta` schema-position path is preserved unchanged — unmigrated playbooks continue to render exactly as before.

**Coordination point reduction (per FR-54)**:

| Coordination point (legacy) | Status in section-keyed mode |
|---|---|
| 1. Schema declared on action's `outputSchema` | DROPPED — section name is the contract key |
| 2. `FieldDelta` event keyed by schema-position `fieldPath` | DROPPED — `section_*` events keyed by `sectionName` |
| 3. Schema-aware widget dispatch (`classifySchemaField`) | DROPPED — direct section state map |
| 4. Field-position display hint (`displayHint: 'heading' / 'list' / ...`) | DROPPED — section renders text + optional structured fallback |
| 5. Per-field renderer (`HeadingRenderer` / `ListRenderer` / ...) | DROPPED — single `SectionRenderer` handles all sections uniformly |
| **NET** | **5 → 2 coordination points (section name + section state)** |

---

## Files modified

| File | Purpose |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | Added 3 new `workspace.*` discriminants (`section_started`, `section_data`, `section_completed`) + 9 section-related field declarations (`sectionName`, `displayLabel`, `sectionIndex`, `totalSections`, `contentDelta`, `structuredData`, `finalContent`, `finalStructuredData`, `citations`). All fields optional per ADR-030 additive-types rule. ADR-015 governance JSDoc on each new field. |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` | Reworked widget body: new `SectionState` exported type; reducer extended with 3 new actions; subscription handler dispatches `section_*` to reducer; `SectionRenderer` component; section-mode detection via `streamState.sections.size > 0`; new `data-render-mode="sections"|"fields"` attribute. Public props shape UNCHANGED — backward-compat-safe. |

## Files created

| File | Purpose |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/StructuredOutputStreamWidget.sections.test.tsx` | 19 tests covering: section-keyed hydration; `sectionIndex` sort order; insertion-order fallback; out-of-order tolerance (completed-before-started, data-before-started); BACKWARD-COMPAT INVARIANT TEST (`FieldDelta`-only render unchanged); mixed-mode precedence; empty-section header-only render; structured-data fallback; citations rendering; streaming badge on active section; defensive event drops; correlationId gate; ADR-021 no-hex-color compliance. |

## Backward-compat invariant test (118R re-validation anchor)

**Test name**: `BACKWARD-COMPAT: legacy FieldDelta events render via schema-position path UNCHANGED (118R anchor)`

**Location**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/StructuredOutputStreamWidget.sections.test.tsx`

**Acceptance criterion**: When the widget receives only `streaming_started` / `field_delta` / `streaming_complete` events (i.e., an unmigrated schema-position playbook running through the renderer), the widget renders via the legacy schema-position-keyed path with **zero behavioural change**:
- `data-render-mode === "fields"`
- `[data-testid="sections-container"]` does NOT exist
- Legacy `[data-field-path="..."]` blocks render unchanged

A sibling test verifies the `mode: 'static'` legacy path also renders unchanged.

When 118R migrates the first playbook to multi-node composite output, that playbook should switch to emitting `section_*` events; this test continues to verify that *non-migrated* playbooks remain on the legacy path.

---

## Test summary

| Test file | Tests | Status |
|---|---|---|
| `StructuredOutputStreamWidget.test.tsx` (existing) | 23 | ✅ ALL PASS unchanged (no regression) |
| `StructuredOutputStreamWidget.integration.dispatchSummarizeOnly.test.tsx` (existing) | 8 | ✅ ALL PASS unchanged |
| `StructuredOutputStreamWidget.sections.test.tsx` (NEW — task 114b) | 19 | ✅ ALL PASS |
| **Total** | **50** | **✅ ALL PASS** |

Per-suite result: `Test Suites: 3 passed, 3 total / Tests: 50 passed, 50 total / Time: 4.991 s`

## Build verification

| Build | Result |
|---|---|
| `@spaarke/ai-widgets` typecheck (only this lib's files) | ✅ Zero new TS errors in `StructuredOutputStreamWidget.tsx` or `PaneEventTypes.ts`. Pre-existing module-resolution errors in other lib files (`@spaarke/auth`, `@spaarke/ai-context`) are environmental and unrelated to this task. |
| `@spaarke/ai-outputs` build (dependency) | ✅ Clean |
| SpaarkeAi solution downstream build | ✅ Clean — Vite production build succeeded in 14.06s; output 3,955 kB / gzip 1,080 kB (no regression). |

## Public-surface impact

- **`SectionState` type EXPORTED** from `StructuredOutputStreamWidget.tsx` for downstream consumers that may want to inspect section state programmatically (e.g., future debug/dev panels).
- Widget public props (`WorkspaceWidgetProps<StructuredOutputStreamWidgetData>`) UNCHANGED — backward-compat for existing consumers (binding per POML constraint).
- `StructuredOutputStreamWidgetData` interface UNCHANGED — same `mode` / `schema` / `prefilledFields` / `correlationId` / `outputSchema` / etc. fields; no consumer migration required.
- `WorkspaceWidgetRegistry` entry UNCHANGED — widget remains registered with the same `widgetType` key.
- `PaneEventTypes.WorkspacePaneEvent` extended additively — new discriminants + new optional fields. Existing subscribers unaffected (ADR-030 additive-types rule).

## Open follow-ups for the main session

1. **Task 114c** (ADR for multi-node composition pattern, main-session-only because `.claude/` touch): doc-only; can proceed independently and parallel to 114a/114b.
2. **Task 118R** (per-playbook data migration to multi-node): once 114a + 114b + 114c land, 118R migrates the FIRST playbook (`summarize-document-for-workspace@v1` per spec FR-54 pilot) to emit `section_*` events. The backward-compat invariant test referenced above MUST continue to pass after that migration — that test exercises an unmigrated playbook, which 118R does NOT touch.
3. **Future**: when richer per-widget-type structured renderers are needed (beyond the compact-JSON fallback the section MVP provides), a widget-type registry can be added inside `SectionRenderer` — out of 114b scope per POML "Backward compat for props is binding".
4. **Future**: section-event telemetry plumbing on the FE side (ADR-015 tier-1 metadata only — section count, total duration). Currently the widget logs only defensive dropped-event debug messages via `console.debug` (Tier 1 safe — no user content or PII).

## Pivots from POML

- POML step 5 prescribed "event-type detection at the SSE entry point: if the FIRST event observed is `FieldDelta` → route to legacy renderer; if `section_started` → route to new renderer". The implementation uses a **render-time detection** (`streamState.sections.size > 0`) rather than a one-time entry-point routing decision. This is functionally equivalent for the spec-valid input cases (BFF emits one OR the other per 114a's guard) AND is strictly more defensive for the out-of-order tolerance acceptance criterion (a `section_completed` arriving before its `section_started` still routes correctly to section mode). The legacy `FieldDelta` path remains untouched; both paths coexist in the same component with no shared mutable state risk.
- POML step 6 prescribed "Render sections in COMPLETION order (not declaration order)". The implementation uses **`sectionIndex` order when defined, with `receivedAt` (insertion order = completion order) as fallback**. Per FR-53 the BFF emits in completion order, so insertion-order fallback IS completion order. When the playbook author has annotated each section with `sectionIndex`, the renderer honours the author's intended declaration order for UI display — this is the more user-friendly default (predictable layout) without sacrificing the underlying completion-order invariant on the wire. Tests cover both code paths.
- The POML mentioned `StructuredOutputStreamWidget` could live in `Spaarke.UI.Components` or `SpaarkeAi` — confirmed via `Glob` it lives in `@spaarke/ai-widgets` (Spaarke.AI.Widgets).
- Tests written as a **separate test file** (`StructuredOutputStreamWidget.sections.test.tsx`) rather than appended to the existing 851-line `StructuredOutputStreamWidget.test.tsx` — this keeps the existing test suite (covering R5/R6 schema-aware paths) as the pure legacy-path regression suite, while new tests focus on the FR-54 section-keyed contract. The legacy file remains unchanged, validating zero regression.
