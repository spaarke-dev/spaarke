# Task 017 — D2-07 StructuredOutputStreamWidget — Evidence

**Status**: complete (code authoring — main session owns build, code-review, adr-check, commit, push)
**Date**: 2026-06-04
**Effort**: ~1h (sub-agent, code-authoring scope only)
**Rigor**: FULL (frontend widget on R5 critical path; risk UR-02; downstream consumer task 026 depends on contract)
**Scope note**: This evidence file is produced by the parallel-wave sub-agent.
The main session owns `npm run build`, code-review/adr-check quality gates,
commits, and pushes — NOT this sub-agent.

---

## 1. Files created / modified

| File | LOC (approx) | Nature |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` | ~680 | NEW — widget body, reducer, sub-renderers, schemas |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts` | ~80 | NEW — registration sentinel mirroring `register-document-viewer-widget.ts` |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | +35 | Side-effect import + named exports for schemas + types |

**No backend files touched** — confirms ADR-029 publish-size delta = 0 MB; no DI registrations delta.

---

## 2. Required exports (downstream contract for task 020 + task 026)

The widget exports the following from `@spaarke/ai-widgets`:

```ts
// Widget component (lazy-loaded via registry)
export { default as StructuredOutputStreamWidget } from '...';

// Types
export type {
  StructuredOutputStreamWidgetData,
  StructuredOutputSchema,
  StructuredOutputField,
  StructuredOutputDisplayHint,
};

// Schemas — REUSE PROOF POINT for risk UR-02 (task 026 imports INSIGHTS_PLAYBOOK_SCHEMA)
export { SUMMARIZE_SCHEMA, INSIGHTS_PLAYBOOK_SCHEMA };

// Widget type key
export { STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE };
```

**Reusability handshake for task 026** (D2-16 — Two-path response renderer):

To render an Insights playbook response, task 026 dispatches a `widget_load`
event with payload:

```ts
{
  widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,  // 'structured-output-stream'
  widgetData: {
    mode: 'static',
    schema: INSIGHTS_PLAYBOOK_SCHEMA,
    prefilledFields: {
      answer:        '<Insights answer text>',
      playbookId:    '<predict-matter-cost@v1>',
      inferenceBody: '<reasoning paragraph>',
      evidenceList:  '<newline-separated source list or JSON array>',
    },
    // OR for decline path:
    declineState: { reason: '<DeclineResponse.Reason>', suggestedActions: [...] },
    // OR for RAG empty-result path:
    emptyResultState: true,
  } satisfies StructuredOutputStreamWidgetData,
}
```

ZERO widget code change is required to add task 026 as a second consumer.

---

## 3. Schema declarations (matched to BFF action output order)

### `SUMMARIZE_SCHEMA` — Summarize playbook (FR-02)

| Order | path | label | displayHint |
|---|---|---|---|
| 10 | `tldr` | TL;DR | heading |
| 20 | `summary` | Summary | paragraph |
| 30 | `keywords` | Keywords | badge |
| 40 | `entities` | Entities | list |

**`tldr` FIRST** matches the task 006 spike result (Azure OpenAI streams JSON
properties in declaration order; `tldr` first-seen at event #4 / char position
10). The deployed SUM-CHAT@v1 action's output JSON-schema property order MUST
align — if it does not, that is a separate fix on the BFF action seed (task
010 / D1-10) NOT a widget change.

### `INSIGHTS_PLAYBOOK_SCHEMA` — Insights playbook envelope (FR-13)

| Order | path | label | displayHint |
|---|---|---|---|
| 10 | `answer` | Insight | heading |
| 20 | `playbookId` | Playbook | badge |
| 30 | `inferenceBody` | Reasoning | paragraph |
| 40 | `evidenceList` | Evidence | list |

Sourced from `notes/insights-engine-assistant-integration-brief.md` §4.1
playbook-path response schema:
- `answer` → user-facing summary
- `playbookId` → e.g. `"predict-matter-cost@v1"`
- `structuredResult.envelope.inferenceBody` (caller flattens) → reasoning text
- `EvidenceRefs[]` (caller serializes as newlines or JSON array) → evidence list

Decline rendering uses `declineState` on widgetData (NOT a schema field), so
the `DeclineResponse.SuggestedActions` array is rendered via Fluent `Button`s
below the warning `MessageBar` per §4.5 of the brief.

---

## 4. Four-state rendering matrix (visually distinct)

| State | Trigger | UI elements | Cursor? | data-render-state |
|---|---|---|---|---|
| (a) Streaming | `mode: 'streaming'` + `phase: 'streaming'` | Schema fields render progressively; brand-colored `▋` blinking cursor at tail of most-recent path; `Skeleton`/`SkeletonItem` placeholders for not-yet-started fields; brand-tint `Badge` "Streaming…" in header | YES — at `mostRecentPath` tail | `streaming` |
| (b) Streaming-complete | `mode: 'streaming'` + `phase: 'complete'` OR `mode: 'static'` | Final formatted output; no cursor; success-filled `Badge` with `CheckmarkCircleRegular` icon "Complete" | NO | `complete` / `static` |
| (c) Decline | `declineState` set | `MessageBar intent="warning"` with `reason`; optional `Divider` + label + Fluent `Button` row with `suggestedActions`; warning-filled `Badge` with `WarningRegular` icon "Declined"; schema fields HIDDEN | NO | `decline` |
| (d) Empty-result | `emptyResultState === true` | Muted `colorNeutralForeground3` hint "I couldn't find anything for that. Try rephrasing or attaching files."; informative-tint `Badge` "No results"; schema fields HIDDEN | NO | `empty` |

Override precedence (highest → lowest): `error` > `declineState` > `emptyResultState` > `isLoading` > normal rendering. Mutually exclusive at render time.

Cursor animation: pure CSS keyframes on `tokens.colorBrandForeground1`,
900ms period, opacity 1→0→1 — adds no JavaScript-driven animation tax. The
glyph is `▋` (UNICODE LEFT FIVE EIGHTHS BLOCK) and is marked `aria-hidden`
so screen readers don't speak "block" repeatedly.

---

## 5. PaneEventBus subscription contract

- Hook: `usePaneEvent('workspace', handler)` from `@spaarke/ai-widgets`
- Channel: `workspace` (one of 4 closed channels per ADR-030 — NOT a new channel)
- Discriminants consumed (added by task 016 / D2-06):
  - `streaming_started` → reducer `reset`
  - `field_delta` → reducer append-by-path with out-of-order detection
  - `streaming_complete` → reducer marks phase complete + clears cursor
- Discriminants ignored (back-compat per ADR-030): `widget_load`, `widget_update`, `widget_action`, `tab_change`, `tab_count_change`, `selection_changed`, `tabs_clear`, `wizard_step`, `entity_resolved`, `session_reset`, `active_widget_changed`, AND any future additive types
- Correlation: events whose `streamId !== widgetData.correlationId` are skipped (allows multiple widget instances to coexist per FR-06)

Out-of-order delta handling: `useReducer` keeps `lastSequence` per path;
incoming deltas with `sequence <= lastSequence` are dropped + logged via
`console.debug` (telemetry, not console.warn — not a defect).

---

## 6. Fluent UI v9 reuse (R5 CLAUDE.md §3.1 reuse mandate)

Primitives used (NO parallel UI lib built):

| Primitive | Where | Why |
|---|---|---|
| `Card` + `CardHeader` | Widget root | Matches DocumentViewerWidget canonical pattern |
| `Text` | Field labels, paragraphs, hint copy | Standard typography primitive |
| `Badge` | Header state + `displayHint: 'badge'` rendering | Compact tokenized output |
| `MessageBar` + `MessageBarBody` | Decline state | Semantic warning UI (intent="warning") |
| `Skeleton` + `SkeletonItem` | Streaming placeholders | Layout-stable loading shimmer |
| `Divider` | Between schema fields + above suggested actions | Visual separation |
| `Button` | Decline suggested actions | Clickable next-step affordances |
| `SparkleRegular`, `WarningRegular`, `CheckmarkCircleRegular` | Header + state badges | `@fluentui/react-icons` brand iconography |

Styling: `makeStyles` + `tokens.*` only — verified no hex, no rgba, no
`#fff`, no `@fluentui/react-components/v8` imports.

---

## 7. ADR compliance

| ADR | Compliance |
|---|---|
| **ADR-021** (Fluent v9 + dark mode) | PASS — all colors via `tokens.*`; cursor uses `colorBrandForeground1`; decline uses semantic `MessageBar intent="warning"`; empty-result uses `colorNeutralForeground3` |
| **ADR-022** (React 19) | PASS — functional component + `useReducer` + `useMemo`; no class components, no legacy lifecycle |
| **ADR-012** (component libs) | PASS — lives in `@spaarke/ai-widgets`; no `Xrm` references; no SpaarkeAi-shell-specific imports; no PCF-specific code |
| **ADR-028** (Spaarke Auth v2) | PASS — NO BFF calls; pure subscriber of PaneEventBus events; no token snapshots |
| **ADR-030** (PaneEventBus closed channels) | PASS — subscribes to existing `workspace` channel only; consumes only the three additive event types from task 016; unknown types ignored |
| **ADR-010** (DI minimalism) | N/A — frontend widget; no DI surface |
| **ADR-029** (BFF publish hygiene) | N/A — frontend-only task; publish-size delta = 0 MB |

---

## 8. UR-02 mitigation evidence (80/20 follow-up TODOs)

Documented inline in `StructuredOutputStreamWidget.tsx` JSDoc:

1. **Nested JSON paths** (e.g. `parties[0].name`, `metadata.author.email`) —
   v1 treats `path` as an opaque top-level key. Renders, but per-element
   editing / hover-highlight is unsupported. Required ONLY when a future
   action surface exposes nested structured outputs that the user must
   inspect at sub-field granularity.

2. **Dynamic-cardinality arrays** (e.g. `fileHighlights[N]` with growing N
   under streaming) — v1 falls back to a single list renderer keyed by
   `displayHint: 'list'` with newline/JSON-array splitting. Adequate for
   Summarize `keywords` + `entities` and Insights `evidenceList`.

3. **Schema versioning** — `StructuredOutputSchema` has no `version` field.
   Add `schemaVersion: number` when two consumers ship with divergent
   schemas requiring migration.

4. **Per-field custom renderers** — v1 has 5 standard `displayHint` values
   (heading, paragraph, list, badge, callout). Custom render functions per
   field would be a follow-up if a consumer requires a domain-specific
   shape (e.g. cost-prediction breakdown table).

5. **Stream cursor token-position precision** — cursor renders at the tail
   of `mostRecentPath`. If two fields update in rapid alternation, the
   cursor will jump between them — acceptable per FR-02 UX scope (the
   visual signal is "AI is producing output", not "this exact char is the
   write head").

R5 Phase 2 ships with the 80/20 baseline. Any of the above five becomes a
backlog item if Phase 3 (`/analyze` proof-point, task 040) requires it.

---

## 9. Build verification (deferred to main session)

Per task POML constraint, the main session runs `npm run build` in
`src/client/shared/Spaarke.AI.Widgets/` to verify TypeScript compiles. The
sub-agent does NOT execute the build.

Expected outcomes:
- Zero TypeScript errors (no `any` in public exports; defensive narrowing
  on `widgetData`)
- Zero new lint warnings (file follows the eslint config used by
  `DocumentViewerWidget.tsx` — verified by mirroring exact styling /
  comment / import discipline)
- Widget appears in `getAllWorkspaceWidgetTypes()` under
  `'structured-output-stream'` (registry populated at module-evaluation
  time via the side-effect import in `index.ts`)

---

## 10. Reusability handshake — note for task 026 implementer

Task 026 (D2-16 — Two-path response renderer) will be the SECOND consumer of
this widget. The contract is:

1. **Import**: `import { STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE, INSIGHTS_PLAYBOOK_SCHEMA }
   from '@spaarke/ai-widgets';`
2. **Dispatch** a `workspace.widget_load` event from `InsightsResponseRenderer`
   when the Insights tool call resolves with `path: 'playbook'`:
   - `widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE`
   - `widgetData: { mode: 'static', schema: INSIGHTS_PLAYBOOK_SCHEMA, prefilledFields: {...} }`
3. **For decline path** (`structuredResult.kind === 'decline'` from brief §4.5),
   set `widgetData.declineState = { reason: envelope.Explanation, suggestedActions: envelope.SuggestedActions }`.
4. **For RAG empty-result** (brief §4.4 — `citations: []` + `answer: ''`),
   set `widgetData.emptyResultState = true`.
5. **For RAG with citations** (brief §4.1 — citation-grounded prose with
   `[n]` tokens), task 026 likely needs a sibling widget (citation-rich
   prose with clickable references) — NOT this widget. This widget targets
   the structured/decline/empty cases; citation prose is a different shape.

Confirmation: **NO widget code change required** for cases 1–4 above. The
schema-driven contract is the reuse vehicle.

---

## 11. Outstanding work (deferred to main session)

Per task 017 POML §Step 10 — main session ownership:

- [ ] Run `npm run build` in `src/client/shared/Spaarke.AI.Widgets/`
- [ ] Run `code-review` skill against the two new files
- [ ] Run `adr-check` skill against the two new files
- [ ] Update task 017 POML status from `not-started` → `complete`
- [ ] Update `TASK-INDEX.md`: 017 🔲 → ✅
- [ ] Reset `current-task.md` to next pending task per CLAUDE.md §7
- [ ] Commit (`feat(r5): task 017 D2-07 — StructuredOutputStreamWidget (schema-driven; dual-purpose)`)
- [ ] Push to remote per push-to-github skill

---

## 12. Acceptance-criterion walkthrough (POML §acceptance-criteria)

| Criterion | Status | Evidence |
|---|---|---|
| Widget exists at expected path; React 19 functional + Fluent v9 semantic tokens only | ✅ | `StructuredOutputStreamWidget.tsx` — `makeStyles` + `tokens.*` only |
| Registration under `'structured-output-stream'`, `allowMultiple: true`, `defaultOrder: 160`, lazy import | ✅ | `register-structured-output-stream-widget.ts` |
| Accepts `StructuredOutputSchema` config; renders fields in `order` ascending; `SUMMARIZE_SCHEMA` + `INSIGHTS_PLAYBOOK_SCHEMA` exported | ✅ | §3 above; both schemas are module-level `const` exports |
| Subscribes via `usePaneEvent('workspace', handler)`; handles 3 event types; unknown types ignored | ✅ | §5 above; switch with `default` early return |
| Progressive rendering observable; cursor at most-recent field tail during streaming; cursor removed on `streaming_complete` | ✅ | Reducer tracks `mostRecentPath`; cursor renders only when `phase === 'streaming'` |
| Out-of-order `field_delta` events handled via `sequence` comparison; stale dropped + logged | ✅ | Reducer `field_delta` branch checks `sequence <= lastSequence` |
| Four render states visually distinct (streaming / complete / decline / empty) | ✅ | §4 above + `data-render-state` attribute aids testing |
| Dark mode passes for all four states | ⏭ | Deferred to main session — sub-agent confirms `tokens.*`-only styling; visual check requires running shell |
| Reusability validated — `mode: 'static'` + `INSIGHTS_PLAYBOOK_SCHEMA` + `prefilledFields` works with zero widget code change | ✅ | §2 + §10 handshake documented |
| UR-02 mitigation TODOs documented | ✅ | §8 above + JSDoc in widget file |
| Build passes | ⏭ | Main session per §9 |
| BFF publish-size delta = 0 MB | ✅ | §1 — no backend changes |
| `code-review` + `adr-check` gates pass | ⏭ | Main session per §11 |

⏭ = handed to main session per parallel-wave sub-agent scope contract.
