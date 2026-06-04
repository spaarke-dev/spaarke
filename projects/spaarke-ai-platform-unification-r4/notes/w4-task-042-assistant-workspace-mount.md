# R4 Task 042 (W-4) — Assistant → Workspace mount source

> **Status**: Code complete — deploy deferred to Phase 7 wrap-up
> **FR**: FR-02 (Assistant pane → Workspace `widget_load` dispatch)
> **OC**: OC-R4-07 (operator confirmation of demo scenario)
> **Rigor**: FULL
> **Date**: 2026-05-26

---

## Demo scenario chosen

**PDF upload in chat → DocumentViewerWidget mounts as workspace tab** (the recommended path per OC-R4-07).

Behaviour:
1. User attaches one or more files via SprkChat's `+` button (existing FR-07 affordance — unchanged).
2. SprkChat's internal `useChatFileAttachment` hook extracts text client-side (PDF.js for PDFs, mammoth for DOCX, native `File.text()` for txt/md).
3. When a file transitions from `extracting` → `ready` state, SprkChat fires the new `onAttachmentReady(attachment)` host callback.
4. ConversationPane's handler dispatches a typed `widget_load` event on the `workspace` PaneEventBus channel with:
   - `widgetType: 'document-viewer'` (symbolic constant `DOCUMENT_VIEWER_WIDGET_TYPE` from `@spaarke/ai-widgets`)
   - `widgetData: { filename, contentType, textContent }` (typed `DocumentViewerWidgetData`)
   - `displayName: <filename>` (operator-visible tab label)
5. WorkspacePane's existing `usePaneEvent('workspace', ...)` subscriber (R2 origin) resolves the widget via `WorkspaceWidgetRegistry` and mounts it as a new tab.
6. The tab is selectable, closable (existing tab semantics — no new closable handling needed).

The widget is a SHIM viewer per Risk R-7: it renders the pre-extracted text content as a scrollable monospace preview. PDF binary render, image preview, RecordViewer integration are explicitly out of scope and deferred to a follow-up.

---

## Why this scenario over alternatives

The recommended PDF-upload path is the lowest-risk and most operator-visible end-to-end demo because:

- The chat attachment flow already exists end-to-end (FR-07; SprkChat `+` button, text extraction, chip lifecycle, BFF payload wiring). No new chat affordance is required.
- The Workspace pane already subscribes to `workspace.widget_load` (R2 origin). No new receiver logic.
- The dispatch payload type is reusable for W-5 (task 043 — Context pane mount source), satisfying the ADR-030 invariant that new event-type discriminants are additive.

Alternative scenarios (image upload, record link, entity mention) were considered but all would require new chat affordances — broader scope than R-7 permits.

---

## Files changed

### Shared lib (`@spaarke/ai-widgets`)

| File | Change |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | Added `WorkspaceWidgetLoadEvent` interface — typed payload contract for `workspace.widget_load` dispatches from mount-source panes. ADR-030 compliant (no `any`). |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentViewerWidget.tsx` | NEW. Workspace widget renderer for chat attachments. Fluent v9 tokens; React 19 functional component; defensive payload narrowing. |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-document-viewer-widget.ts` | NEW. Side-effect registration file. Exports the symbolic `DOCUMENT_VIEWER_WIDGET_TYPE` constant. |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/DocumentViewerWidget.test.tsx` | NEW. 9 tests — header, content, truncation, loading, error, defensive narrowing. PASS. |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/register-document-viewer-widget.test.ts` | NEW. 5 tests — registration, metadata, resolution, constant stability. PASS. |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | Wired side-effect import + named exports (`DocumentViewerWidget`, `DocumentViewerWidgetData`, `DOCUMENT_VIEWER_WIDGET_TYPE`, `WorkspaceWidgetLoadEvent`). |

### Shared UI components (`@spaarke/ui-components`)

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` | Added optional `onAttachmentReady?: (attachment: ChatAttachment) => void` to `ISprkChatProps`. Type-only import of `ChatAttachment` (no runtime coupling). |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | Destructured new `onAttachmentReady` prop; added effect watching `attachmentFiles` for `ready`-state transitions, firing the host callback once per ready file (deduplicated via ref-set). Host callback errors are swallowed to keep SprkChat lifecycle intact. |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/SprkChat.onAttachmentReady.test.tsx` | NEW. 6 tests — covers no-fire when extracting/error/empty, fires once per ready chip, no double-fire on re-render, host-throw resilience. **Pre-existing test-env issue prevents execution** (see Deviations §). |

### SpaarkeAi solution

| File | Change |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | Added named imports `DOCUMENT_VIEWER_WIDGET_TYPE` + `DocumentViewerWidgetData` from `@spaarke/ai-widgets`. New `handleAttachmentReady` callback constructs typed `DocumentViewerWidgetData` payload and dispatches `widget_load` on the workspace PaneEventBus channel. Wired through to `<SprkChat onAttachmentReady={...} />`. |

---

## PaneEventTypes.ts changes (ADR-030 review)

- **New interface**: `WorkspaceWidgetLoadEvent` — convenience contract for DISPATCHERS (W-4 / W-5) that narrows the existing `WorkspacePaneEvent` union for the `widget_load` discriminant. No new discriminant added — `widget_load` already exists on the union (R2 origin).
- **`widgetData` typed as `unknown`** (NOT `any`) per ADR-030. Dispatchers cast to the widget's declared data shape at the dispatch boundary; subscribers narrow before use.
- **Backward compatible**: existing subscribers of `WorkspacePaneEvent` continue to compile unchanged. The new interface is additive — exported but not required by any consumer.

---

## Build verification

| Package | Command | Result |
|---|---|---|
| `@spaarke/ai-widgets` | `npx tsc --noEmit` | Pre-existing rootDir / module-resolution errors (B-2 / task 061) — no errors in new files. |
| `@spaarke/ai-widgets` (new files only) | `npx tsc --noEmit DocumentViewerWidget.tsx register-document-viewer-widget.ts ...` | ✅ Clean. |
| `@spaarke/ui-components` | `npx tsc --noEmit` | No errors in SprkChat.tsx or types.ts. |
| SpaarkeAi Vite build | `npx vite build` | ✅ Success — 920.83 KB gzip. |

**Bundle delta**: 918 KB R3 baseline → 920.83 KB R4 = **+2.83 KB gzip**. Well within NFR-08 ≤+50 KB budget.

---

## UI tests (defined, not executed in this task)

The POML's `<ui-tests>` section defines three operator-runnable scenarios:

1. **Assistant → Workspace end-to-end demo** (FR-02 acceptance): upload PDF → tab opens with filename label → tab selectable → tab closable → 0 console errors.
2. **Dark Mode Compliance** (ADR-021): toggle dark mode → widget renders cleanly → all colors via Fluent v9 semantic tokens.
3. **PaneEventBus type safety** (ADR-030): inspect dispatched event → payload matches declared TypeScript type → no `any` casts in the dispatch path.

Execution deferred to deploy time per parent agent guardrails (NO deploy in this task).

---

## Deviations

- **SprkChat test execution blocked by pre-existing environment issue**. The new `SprkChat.onAttachmentReady.test.tsx` uses the same import pattern as the existing `SprkChat.attachments.test.tsx`; both fail with `Cannot find module 'react-dom/client'` due to the `@spaarke/ui-components` package being PCF-paired (older React) while `@testing-library/react` expects React 18+ test runtime. This is a pre-existing limitation tracked separately (and would be uncovered by task 061 / B-2 tsc rootDir fix). The DocumentViewerWidget + registration tests in `@spaarke/ai-widgets` (React 19 native) pass cleanly: **14/14**.

- **Naming-collision note** (informational): the existing R1-origin `Spaarke.AI.Outputs/src/source-widgets/DocumentViewerWidget.tsx` shares the name `DocumentViewerWidget` but is a Context-pane source widget (consumes `ContextWidgetProps`). My new widget lives in `Spaarke.AI.Widgets/src/widgets/workspace/DocumentViewerWidget.tsx` (consumes `WorkspaceWidgetProps`). Different packages, different files, different widget contracts. No conflict — the registry resolves them via distinct keys.

- **No deploy performed** per parent agent guardrails. Code + build verify only.

- **Did NOT touch**: TASK-INDEX.md, current-task.md, root CLAUDE.md, any `.claude/` paths — per parent agent guardrails (other sub-agents running in parallel; main session reconciles).

---

## Risk R-7 scope adherence

Per plan.original.md §8 Risk R-7: **ship dispatch + ONE viewer widget end-to-end; defer broader coverage to follow-up.**

This task delivered exactly that: the Assistant-side dispatcher + ONE registered receiving widget (DocumentViewer). Broader widget coverage (image preview, RecordViewer, ContextDataWidget) is OUT OF SCOPE and will surface as a follow-up if FR-02 acceptance asks for it after the demo.

---

## Acceptance criteria (POML — referenced for traceability)

| # | Criterion | Status |
|---|---|---|
| 1 | Demo trigger dispatches typed `widget_load` on workspace channel | ✅ Code complete |
| 2 | Workspace pane resolves via registry + mounts as new tab | ✅ Existing WorkspacePane logic — no change needed |
| 3 | Tab is selectable + closable | ✅ Existing tab semantics — no change needed |
| 4 | No `any` in dispatch path; `widget_load` typed in `PaneEventTypes.ts` | ✅ `WorkspaceWidgetLoadEvent` added; `DocumentViewerWidgetData` typed |
| 5 | `authenticatedFetch` for any BFF call | ✅ N/A — no BFF calls in v1 viewer |
| 6 | Fluent v9 tokens only; no hex/rgba | ✅ All styles use `tokens.*` |
| 7 | Bundle delta ≤+50 KB | ✅ +2.83 KB gzip |
| 8 | TASK-INDEX shows task 042 ✅ | ⏸ Deferred to main session per parent agent guardrails |
