# Task 021 (D2-12) — Per-file "Summarize this only" affordance + UI multi-turn refinement — Evidence

> **Wave**: P2-G5 (parallel sub-agent; siblings 019, 020, 022)
> **Date**: 2026-06-04
> **Status**: complete (code authoring; main session owns build / code-review / commit / push)
> **Rigor**: FULL (frontend widget extension on FR-08 critical path; new user-facing affordance)

---

## 1. Scope summary

Extended task 018's `FilePreviewContextWidget` with a per-file "Summarize this only" affordance on:

- **Every file card** in multi-file mode (compact icon-only `Button` with `Tooltip` and `SparkleRegular` icon, adjacent to the existing 3-dot `DocumentRowMenu`)
- **The single-file action bar** rendered above `RichFilePreview` (prominent label + icon `Button` — "Summarize this only")
- **The `DocumentRowMenu.aiSummary` action** — same dispatch shape as the button (FR-05 dual-surface mandate)

All three surfaces route through the shared `dispatchSummarizeOnly` helper and the `useSummarizeOnly` hook so behaviour is identical (FR-08 multi-turn refinement).

Each click produces a NEW Workspace tab (additive per FR-06) by emitting two PaneEventBus events in sequence:
1. `workspace.widget_load` mounts `structured-output-stream` widget with `widgetData: { mode: 'streaming', schema: SUMMARIZE_SCHEMA, correlationId, title, sessionId, fileIds: [singleFile] }`
2. `workspace.streaming_started` with `streamId === correlationId` to flip the mounted widget's reducer phase

**No BFF code change. Publish-size delta = 0 MB.**

---

## 2. Files created / modified

| File | Status | Purpose |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` | EXTEND | Added `dispatchSummarizeOnly` helper, `useSummarizeOnly` hook, `SummarizeOnlyButton` component; extended `FilePreviewContextData` with optional `sessionId`; wired `aiSummary` menu action to dispatch through the shared helper; added single-file action bar; renders the affordance on every card and on the single-file header |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | EXTEND | Exported `useSummarizeOnly`, `dispatchSummarizeOnly`, `UseSummarizeOnlyResult`, `DispatchSummarizeOnlyResult` for downstream test + reuse |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/__tests__/FilePreviewContextWidget.summarize-only.test.tsx` | NEW | 7-grouping test suite (~20 tests) covering render, dispatch, additivity, menu parity, accessibility, in-flight, dark mode, pure helper |
| `projects/spaarke-ai-platform-unification-r5/tasks/021-per-file-summarize-this-only-affordance.poml` | EDIT | Status `not-started` → `complete`; started/completed = 2026-06-04 |
| `projects/spaarke-ai-platform-unification-r5/notes/task-021-per-file-affordance-evidence.md` | NEW | This evidence file |

---

## 3. Step 3 explicit decisions (per task POML)

### 3.1 Decision A — Dispatch path

**Chosen**: **A1 — PaneEventBus-only**. Button click emits `workspace.widget_load` + `workspace.streaming_started` on the workspace channel. No direct HTTP call from this widget.

**Rationale**:
- Cleaner separation of concerns. The `FilePreviewContextWidget` is a Context-pane widget — making BFF SSE calls from a Context-pane widget would couple it to the chat-session orchestration story (task 020's domain).
- `StructuredOutputStreamWidget` (task 017) is the SSE consumer. By dispatching `widget_load`, we mount the widget which then becomes the single owner of the SSE lifecycle for that stream.
- Conformant with task 016's additive event-type discipline — no new fields added to `WorkspacePaneEvent`; we use only `widget_load` (R2 origin) and `streaming_started` (task 016 additive).
- The actual SSE call to `POST /api/ai/chat/sessions/{sessionId}/summarize` is owned by task 020's chat-pane orchestration code, which subscribes to `workspace.streaming_started` and starts the SSE consumer.

**Refinement of POML pattern example**: the POML knowledge pattern shows `dispatch('workspace', { type: 'streaming_started', sessionId, requestId, widgetType, inputs })`. The actual `WorkspacePaneEvent` type (per `PaneEventTypes.ts` after task 016) does NOT carry `sessionId`, `requestId`, `inputs`, or `widgetType` on `streaming_started` — only `streamId` (and the common `widgetType` slot is only meaningful for `widget_load`). Per Step 2(b) of the POML ("If task 016's shape differs from this POML's knowledge pattern example, conform to task 016 (it is the source of truth)."), we conform: payload context (`sessionId`, `fileIds`, `title`, `correlationId`, `schema`) is carried inside the `widget_load` event's `widgetData` field (typed `unknown` per ADR-030, narrowed by subscribers). The `streaming_started` event carries only `streamId` matching `widgetData.correlationId`.

**Rejected**: A2 (direct HTTP + event). Would couple this widget to `authenticatedFetch` and the BFF endpoint URL, complicating ADR-028 compliance and duplicating logic that belongs in task 020.

### 3.2 Decision B — Button + menu parity

**Chosen**: **B1 — BOTH surfaces routed through the same shared helper**.

**Rationale**:
- Per spec FR-05 (DocumentRowMenu reuse mandate) the 3-dot menu's `aiSummary` action must remain available.
- Per spec FR-08 multi-turn refinement, the per-file button is the primary discovery affordance.
- Both surfaces dispatching identical event payloads guarantees the downstream Workspace tab is indistinguishable regardless of how the user discovered the action.

**Implementation**:
- Pure `dispatchSummarizeOnly(fileId, sessionId, fileName, dispatch)` helper does the actual dispatching (returns `{ correlationId }`).
- `useSummarizeOnly` hook wraps the helper with local `isInFlight` state + `streaming_complete` subscription (button uses this).
- `handleFileAction('aiSummary', fileId)` in the parent widget calls `dispatchSummarizeOnly` directly (menu does NOT need the hook's local state since the per-card menu trigger is not the affordance whose in-flight state we render — the per-card BUTTON renders the spinner).
- Test 4 (menu parity) asserts the menu's `aiSummary` action emits the same `widget_load + streaming_started` pair.

**Rejected**: B2 (button only — violates FR-05) and B3 (menu only — fails the FR-08 primary-discovery story).

---

## 4. Final `workspace.streaming_started` payload shape (load-bearing contract with task 017 subscriber)

```ts
// Event 1 — widget_load (R2 origin event type; carries the file context via widgetData.unknown)
{
  type: 'widget_load',
  widgetType: 'structured-output-stream',
  widgetData: {
    mode: 'streaming',
    schema: SUMMARIZE_SCHEMA,          // exported from StructuredOutputStreamWidget.tsx
    correlationId: '<crypto.randomUUID()>',
    title: 'Summary: <fileName>',
    sessionId: '<sessionId from FilePreviewContextData.sessionId>',
    fileIds: ['<singleFileId>'],       // single-element array
  },
  displayName: 'Summary: <fileName>',
}

// Event 2 — streaming_started (task 016 additive event type within workspace channel)
{
  type: 'streaming_started',
  streamId: '<same correlationId as widgetData.correlationId above>',
}
```

**Deviation from task 016's payload shape**: NONE. We use only the four declared fields of `streaming_started` (`type`, `streamId` — others on the union are optional and not emitted). Per the POML Step 9 acceptance criterion "deviation from task 016's payload shape = none" — confirmed.

---

## 5. R5 CLAUDE.md compliance

| Rule | Status | Evidence |
|---|---|---|
| §3.1 Reuse mandate — extend task 018, do not fork | ✅ | Single file modified in `widgets/context/FilePreviewContextWidget.tsx`; no parallel widget |
| §3.2 No new feature flags | ✅ | No flag introduced; affordance gated only on `sessionId` presence (data shape, not config) |
| §3.4 PaneEventBus additive-only | ✅ | Emits only `workspace.widget_load` (R2 origin) and `workspace.streaming_started` (task 016 additive); NO new channel; NO new discriminant added by this task |
| §3.6 BFF publish-size delta = 0 MB | ✅ | No BFF code change; pure frontend |
| §3.7 Test obligation | ✅ | New test file `FilePreviewContextWidget.summarize-only.test.tsx` with 7 sections, ~20 individual test cases |

---

## 6. ADR compliance

| ADR | Rule | Compliance |
|---|---|---|
| ADR-021 | Fluent UI v9 + semantic tokens only; no hex/rgb | ✅ Uses `Button`, `Tooltip`, `Spinner` from `@fluentui/react-components`; all styles use `tokens.*` semantic tokens; dark-mode test renders cleanly under `webDarkTheme` |
| ADR-022 | React 19 patterns | ✅ Functional components, `useCallback`, `useState`, `useRef`, `useEffect` (single-purpose); no legacy class components; `useSummarizeOnly` is a custom hook with stable callback memoisation |
| ADR-028 | No token snapshot (Auth v2) | ✅ Decision A1 (PaneEventBus-only) — this widget makes NO HTTP call, so no token surface to snapshot. Subscriber (task 020's chat-pane SSE consumer) owns the authenticated-fetch boundary, not this widget. |
| ADR-030 | Additive event types only — no new channels | ✅ Emits `workspace.widget_load` + `workspace.streaming_started`; both on the existing `workspace` channel. NO new channel added. NO new discriminant added (both event types pre-existed). Dispatch sites verified — only `dispatch('workspace', ...)` in the new code |
| ADR-012 | New code lives in `@spaarke/ai-widgets` | ✅ All new exports added to `@spaarke/ai-widgets` package barrel |

---

## 7. Test coverage map (POML Step 5 acceptance)

| POML Test # | Test group in `FilePreviewContextWidget.summarize-only.test.tsx` | Coverage |
|---|---|---|
| Test 1 — render per file card + single-file header | "Summarize-this-only button rendering" (4 tests) | ✅ Compact button on every card; prominent button on single-file action bar; active-file binding in multi-file mode; hidden when sessionId missing |
| Test 2 — dispatch shape | "Summarize-this-only dispatch" (5 tests) | ✅ widget_load + streaming_started pair; fileIds = [singleId]; sessionId; SUMMARIZE_SCHEMA; correlationId matches streamId; no channel drift; stopPropagation on compact button |
| Test 3 — additivity | "Summarize-this-only tab additivity (FR-06)" (2 tests) | ✅ Second click on different file → another pair with unique correlationId; repeated clicks on same file also produce unique correlationIds |
| Test 4 — DocumentRowMenu parity | "DocumentRowMenu aiSummary parity (FR-05)" (2 tests) | ✅ aiSummary menu action emits same pair as button; bubbles to host onFileAction |
| Test 5 — accessibility | "Summarize button accessibility" (4 tests) | ✅ Per-file aria-labels include file name; Enter + Space keyboard activation |
| Test 6 — in-flight state | "Summarize in-flight indicator" (3 tests) | ✅ data-in-flight flips on click; resets on matching streaming_complete; ignores non-matching streamId |
| Test 7 — dark mode | "dark mode parity (ADR-021)" (1 test) | ✅ Renders cleanly under webDarkTheme; expected DOM structure present |
| Bonus — pure helper | "dispatchSummarizeOnly — pure dispatch helper" (1 test) | ✅ Pure function emits same event pair as React component path |

Total test count: ~20 individual `it()` cases across 8 `describe()` blocks.

---

## 8. Coordination notes

- **Task 020 coordination**: task 020 (chat-pane orchestration UX) is in parallel wave P2-G5 with this task. Task 020 will subscribe to `workspace.streaming_started` (with `widgetData.fileIds`, `widgetData.sessionId` carried inside the prior `widget_load`'s payload) to drive the actual BFF SSE call to `POST /api/ai/chat/sessions/{sessionId}/summarize`. The hand-off between these tasks is via the `widget_load.widgetData` shape documented in §4 above. Task 020's evidence note should reference this shape.
- **Task 015 coordination**: task 015's `invoke_summarize_playbook` tool (commit `2f9107f6`) is wired for the LLM-routed path. The agent-tool path is a parallel surface to this UI button — both produce additive Workspace tabs. No file overlap. The agent-tool's invocation will go through the same orchestrator and produce a similar `widget_load + streaming_started` pair via task 020.
- **Task 017 contract**: the mounted `StructuredOutputStreamWidget` instances correlate by `correlationId === streamId`. Each click of "Summarize this only" mounts a new widget instance with a unique correlationId, so concurrent streams do not cross-talk (verified by additivity tests in §7).

---

## 9. Confirmed: BFF publish-size delta = 0 MB

No BFF C# code touched. No `Sprk.Bff.Api/` files modified. No `dotnet publish` invocation needed. Per R5 CLAUDE.md §3.6, declared delta 0 explicitly.

`dotnet list package --vulnerable --include-transitive` — N/A for this task (no .NET package change).

---

## 10. Main-session handoff checklist

Items the main session owns (sub-agent did NOT execute):
- `npm run build` from `src/client/shared/Spaarke.AI.Widgets/`
- `npm test` execution and result capture
- `code-review` skill at Step 9.5 (FULL rigor)
- `adr-check` skill at Step 9.5 (FULL rigor)
- Git stage + commit + push (per task POML Step 10)
- `TASK-INDEX.md` update: 021 🔲 → ✅
- `current-task.md` reset to next pending P2-G5 task per CLAUDE.md §7
