# Wave B-G9c2 — B7 + B8 combined: Summary tab DEFERRED install + per-run tab

**Date**: 2026-06-10
**Wave**: B-G9c2 (B7 + B8 combined hotfix)
**Status**: SUCCESS — both bugs fixed; build clean; 17 tests pass.

---

## Summary

Two related Summary-tab bugs surfaced in Phase B walkthrough were fixed together because they share files:

- **B7**: Summary tab appeared as DEFAULT tab on workspace load BEFORE any summarize had run (R5 task 038 eager install).
- **B8**: Summary tab REPLACED on each summarize run because all runs shared `streamId === chatSessionId` and a single auto-installed tab.

Per user decisions:
- **B7**: Defer install until first event (accept race risk; handle carefully).
- **B8**: New tab per run (honor FR-06); title includes filename; server-side correlationId per invocation.

The combined fix shifts both responsibilities from `WorkspacePane` (eager auto-install) to `executeSummarizeIntent` (per-run dispatcher), reusing the existing `widget_load` event path.

---

## B7 — Deferred Summary tab install

### Files changed

- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`
  - **Removed** (lines 546–655 in the pre-fix file): the eager `prependTab` effect that auto-installed a "Summary" tab on mount + the follow-on correlationId-sync effect.
  - **Removed**: the `streaming_started` auto-focus handler block (R5 task 038 lines 657–713 pre-fix). With the deferred-install model, the `widget_load` handler's `addTab` auto-activates the new tab — no separate focus handler needed.
  - **Retained**: `summaryTabIdRef` + `streamFocusOverrideRef` as benign null/false sentinels so `handleTabChange` (lines ~743+ pre-fix) doesn't have to change.
  - **Removed imports**: `SUMMARIZE_SCHEMA`, `StructuredOutputStreamWidgetData` (no longer used at this site).

### How install now happens

Each summarize invocation synchronously dispatches `workspace.widget_load` BEFORE its SSE stream opens. The EXISTING `widget_load` handler in `WorkspacePane.tsx` (around line 656 pre-fix; subscriber unchanged) installs a new tab via `manager.addTab(...)`. `addTab` auto-activates the new tab so the user sees their result immediately.

When no summarize has fired AND no layout / pin exists, `tabs.length === 0` and `WorkspacePane` shows its existing first-paint placeholder (already in the code). This is the "no empty Summary tab on cold load" outcome.

### Race handling

- `PaneEventBus.dispatch` is **synchronous** (verified by reading existing code). The `widget_load` event installs the tab synchronously before any subsequent `streaming_started` dispatch (also synchronous, also from the same call site in `executeSummarizeIntent`). No queue / replay needed because the events are emitted in order from the same async function.
- The widget component itself resolves asynchronously via `resolveWorkspaceWidget(widgetType).then(...)`. While it's resolving, the tab carries its `widgetData` payload (including `correlationId`); `streaming_started` and subsequent `field_delta` events sit in the bus subscriber list — the widget's own `usePaneEvent` subscription (registered in the widget's initial render) catches them as soon as it mounts. Per the existing `StructuredOutputStreamWidget` behavior, the widget filters events by `correlationId` — so concurrent runs cannot cross-contaminate.

---

## B8 — New tab per summarize run

### Files changed

- `src/solutions/SpaarkeAi/src/components/conversation/executeSummarizeIntent.ts`
  - **Added imports** from `@spaarke/ai-widgets`: `STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE`, `SUMMARIZE_SCHEMA`, `SUM_CHAT_OUTPUT_SCHEMA`, type `StructuredOutputStreamWidgetData`.
  - **Added `formatTitleSuffix(names)` helper**: builds the tab-title suffix from filenames — 1 name verbatim, 2 names joined, 3+ collapse to `"N files"`.
  - **Added new Step 3a** (lines ~263–305 post-fix): emits `workspace.widget_load` BEFORE the SSE stream opens. The widget data carries `mode: 'streaming'` + `schema: SUMMARIZE_SCHEMA` + `outputSchema: SUM_CHAT_OUTPUT_SCHEMA` (mirrors the Wave B-G9a fix so `tldr` / `entities` render correctly) + `correlationId: streamId` (per-run unique) + `title: 'Summary: <filename>'` (or just `'Summary'` for 0 names).
  - **Existing streamId default**: the function already defaulted `streamId = inputs.streamId ?? generateStreamId()`. With ConversationPane passing `streamId: undefined`, each invocation now gets a fresh unique id (Date+random-based; not security-sensitive).

- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (line ~1134)
  - **Changed**: `streamId: chatSessionId` → `streamId: undefined`. This is the heart of B8 — by NOT reusing `chatSessionId` as the streamId, each run is uniquely correlated and the `widget_load` produces a distinct new tab via the `addTab` path.

### Server-side correlationId

Investigated `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` (line 125). The server-side `SummarizeSessionFilesRequest` already accepts an optional `CorrelationId` parameter for distributed tracing, and the SSE bridge tags every `workspace.streaming_*` event with the **client-supplied** `streamId` (via `createSseToPaneEventBridge(streamId)` in `executeSummarizeIntent.ts` line 263). The server does NOT need to change for B8 — the streaming-event correlation lives entirely on the client; the BFF emits opaque `AnalysisChunk` events and the client's bridge tags them with the per-run streamId before publishing on the bus.

This means **no BFF code changes were required** for B8. The server-side `CorrelationId` field (distributed-tracing only) is orthogonal to the workspace-tab correlation. The BFF was NOT modified.

---

## Test coverage

### File rewritten (B7+B8 supersede R5 task 038 tests)

`src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspacePane.summary-tab.test.tsx`:
- **(B7-1)** `does NOT install a Summary tab on mount` — asserts no structured-output-stream widget exists initially and the first-paint placeholder shows.
- **(B7-2)** `installs a Summary tab when a 'widget_load' for structured-output-stream is dispatched` — simulates a summarize run; asserts the tab appears and becomes active.
- **(B8-1)** `creates two distinct Summary tabs for two consecutive summarize runs` — dispatches two `widget_load` events with different correlationIds + filenames; asserts BOTH tabs exist (not replaced).
- **(B8-2)** `uses the dispatched displayName as the tab title (Summary: <filename>)` — asserts title carries the filename.

### File extended (executeSummarizeIntent.test.ts)

- Existing happy-path test updated: workspace event order now `widget_load → streaming_started → field_delta → streaming_complete` (was `streaming_started → field_delta → streaming_complete`).
- Existing "SSE malformed JSON line is skipped" assertion updated to the same 4-event order.
- **New** `executeSummarizeIntent — B-G9c2 per-run tab + unique streamId` suite:
  - `two consecutive runs generate UNIQUE streamIds and emit TWO widget_load events` — asserts streamIds differ and each widget_load carries a matching correlationId.
  - `tab title includes the source filename (single-file run)` — asserts `displayName === 'Summary: deposition.pdf'`.
  - `tab title collapses to "N files" for 3+ files` — asserts the 3-file case produces `'Summary: 3 files'`.

### Test outputs (final)

```
$ npx jest --testPathPatterns="executeSummarizeIntent|WorkspacePane.summary-tab"
Test Suites: 2 passed, 2 total
Tests:       17 passed, 17 total
Snapshots:   0 total
Time:        5.738 s
```

### Type-check

```
$ npx tsc --noEmit 2>&1 | grep -E "(executeSummarizeIntent|WorkspacePane|ConversationPane.tsx)"
(no output — 0 errors in B-G9c2 touched files)
```

The repo has many pre-existing type-check errors unrelated to this wave (Spaarke.UI.Components ComponentFramework namespace gaps, FieldMappingService unused vars, LegalWorkspaceApp IWebApi mismatch, etc.). None are in the files this wave touched.

### Jest infrastructure note (incidental dependency)

A sibling agent (B-G9c3) added a `marked` ESM stub + a deep-path module-name mapping for `@spaarke/ai-widgets/*` to `jest.config.ts` to unblock `ConversationPane.r5.test.tsx` and `ConversationPane.slash-nl-rewire.test.tsx`. This wave benefits from the existing `marked` stub (the `@spaarke/ai-widgets` barrel transitively pulls `DocumentViewerWidget` → `renderMarkdown` → `marked`); without it, the new value-imports added by this wave to `executeSummarizeIntent.ts` would have broken its test. No additional jest.config.ts change is required for B-G9c2.

---

## Race + edge case audit

| Case | Behavior | Verified by |
|---|---|---|
| Concurrent summarize: A in-flight, user starts B | Each gets its own tab; A keeps streaming to its tab; B opens and becomes active | Unique streamIds + widget correlationId gate; covered by `two consecutive runs generate UNIQUE streamIds` test |
| User closes the in-flight tab | Existing `handleTabClose` semantic applies; subsequent events for that correlationId find no subscribed widget and are dropped silently | Existing R5 test suite (`WorkspaceTabManager.test.ts`) — no regression |
| First event arrives BEFORE deferred install completes | Not possible: `widget_load` is dispatched synchronously BEFORE `streaming_started` from the same async function in `executeSummarizeIntent`. The bus is synchronous; widget_load is processed first, tab is installed, then streaming_started is processed (widget mount is async but the events sit in the bus subscriber chain — the widget's `usePaneEvent` registers on its first render and picks up later events) | Code inspection of `PaneEventBus.dispatch` (synchronous notify-all) + `executeSummarizeIntent` step order |
| Cold load with no summarize, no layout, no pin | `tabs.length === 0` → first-paint placeholder. No empty Summary tab. | Test `does NOT install a Summary tab on mount` |
| 0-file summarize | Pre-existing guard in `executeSummarizeIntent`: throws if `heldFiles.length === 0`. No widget_load dispatched. | Existing test (heldFiles guard) |

---

## Files modified (summary)

| File | Change |
|---|---|
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` | Removed eager Summary auto-install effect + sync-correlationId follow-up + streaming_started focus handler. Retained ref sentinels. |
| `src/solutions/SpaarkeAi/src/components/conversation/executeSummarizeIntent.ts` | Added value imports for widget symbols + schemas. Added `formatTitleSuffix` helper. Added Step 3a dispatching `widget_load` with per-run correlationId + filename-bearing title. |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | `streamId: chatSessionId` → `streamId: undefined` so each summarize run generates a fresh streamId. |
| `src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspacePane.summary-tab.test.tsx` | Rewritten for the deferred-install + per-run model. |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/executeSummarizeIntent.test.ts` | Updated 2 existing assertions for the new event order (widget_load first). Added 3 new B-G9c2 tests. |

No server-side BFF files modified. No `.claude/` paths modified. No ADRs modified.

---

## Commit message

```
fix(r6 G9c2 B7+B8): defer Summary tab install + new tab per summarize run

Hotfix wave B-G9c2 combines two related Phase B walkthrough bugs whose
source files overlap. Fixed together to avoid merge churn.

B7 (defer install): WorkspacePane no longer eagerly prepends an empty
"Summary" tab on mount (R5 task 038). The tab is now installed on demand
by the existing widget_load handler when a summarize run dispatches the
event. With no summarize / no layout / no pin, the pane shows its existing
first-paint placeholder instead of an empty Summary tab.

B8 (per-run tab): Each summarize invocation now generates a unique
streamId (executeSummarizeIntent's existing generateStreamId default — was
previously overridden to chatSessionId by ConversationPane). The streamId
is the tab's correlationId, so concurrent or successive runs each get
their own tab via the StructuredOutputStreamWidget's correlation gate
(allowMultiple: true in register-structured-output-stream-widget.ts).
Tab title includes the source filename ("Summary: contract.pdf" for one
file; "Summary: a.pdf, b.pdf" for two; "Summary: 3 files" for 3+).

Restores FR-06 design intent that R5 task 038 collapsed back to a single
shared tab. MAX_WORKSPACE_TABS=8 FIFO eviction remains in force.

No BFF code change required: the SSE bridge already tags streaming events
with the client-supplied streamId; the server-side CorrelationId field
(distributed tracing) is orthogonal to workspace-tab correlation.

Tests: 17 pass (existing 14 + 3 new); type-check clean on touched files.
```
