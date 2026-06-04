# Task 018 (D2-09) — FilePreviewContextWidget — Evidence

> **Wave**: P2-G4 (parallel sub-agent with task 017 `StructuredOutputStreamWidget`)
> **Date**: 2026-06-04
> **Status**: ✅ Code authoring complete (build/commit/push handled by main session)
> **Rigor**: FULL (frontend widget creation; new event-type wiring; reuses extracted renderer + canonical 3-dot menu)

---

## 1. Scope summary

Built the `FilePreviewContextWidget` — a NEW Context-pane widget that renders inline (non-modal) file previews for the R5 chat-driven Summarize vertical slice (spec FR-08, NFR-02).

- **Single-file mode**: renders the file inline via the extracted `RichFilePreview` renderer.
- **Multi-file mode**: vertical list of file cards above the active preview; clicking a card swaps the active file AND dispatches `context.file_selected`.
- **3-dot menu**: reuses the canonical `DocumentRowMenu` (12 actions in FR-DOC-01 order) — both at the per-card surface and as the active-file renderer's title-bar menu.
- **Loading / Empty / Error**: Skeleton / "No files attached" / Fluent v9 `MessageBar` — never a blank pane.

---

## 2. Files created / modified

| File | Status | Purpose |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` | NEW | Widget component (~600 LOC; default export + named type exports) |
| `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` | EXTEND | Added `'file-preview'` registration with type-erasure cast mirroring `playbook-gallery` / `findings` |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | EXTEND | Inline registration + typed named exports (`FilePreviewContextData`, `FilePreviewContextFile`, `FilePreviewContextRenderProps`, `FilePreviewContextWidgetProps`, `FilePreviewFileActionHandler`, `FILE_PREVIEW_CONTEXT_WIDGET_TYPE`) — mirrors the dual-registration pattern used for `playbook-gallery` + `findings` |
| `projects/spaarke-ai-platform-unification-r5/tasks/018-file-preview-context-widget.poml` | EDIT | Status `not-started` → `complete`; started/completed timestamps set |
| `projects/spaarke-ai-platform-unification-r5/notes/task-018-widget-evidence.md` | NEW | This evidence file |

NO BFF code change. Publish-size delta = 0 MB.

---

## 3. Reuse confirmation (R5 CLAUDE.md §3.1)

### 3.1 `RichFilePreview` reuse (extracted renderer from task 013 / D2-08)

The active-file preview slot mounts the extracted renderer directly:

```tsx
import { RichFilePreview, DocumentRowMenu, type DocumentRowAction } from '@spaarke/ui-components';

// ...

<RichFilePreview
  documentId={activeFile.fileId}
  documentName={activeFile.fileName}
  documentType={activeFile.documentType}
  // ...
  fetchPreviewUrl={fetchActivePreviewUrl}
  onOpenFile={handleActiveOpenFile}
  // ...
/>
```

- **No iframe re-implementation**: the renderer's existing iframe + sandbox attributes + retry-on-error flow is reused.
- **No metadata pane re-implementation**: the renderer's existing 2-column body grid + Details section is reused.
- **No prev/next re-implementation**: the renderer's title-bar nav IS available via its `navigationTotal` + `currentIndex` + `onNavigate` props (left opt-in for future hosts; widget uses the card-list selection instead since the card list IS the navigation chrome).
- **No menu re-implementation**: the renderer's title-bar `DocumentRowMenu` is reused; the widget passes its own `disabledActions` set so the menu's behavior is consistent with the per-card surface.

### 3.2 `DocumentRowMenu` reuse (12 actions, FR-DOC-01 canonical order)

Per-card menu in multi-file mode:

```tsx
<DocumentRowMenu
  document={{ id: file.fileId, name: file.fileName, documentType: file.documentType }}
  onAction={handleAction}
  disabledActions={disabledActions}
/>
```

- All 12 actions in FR-DOC-01 order are available (`preview`, `aiSummary`, `openFile`, `findSimilar`, `download`, `copyLink`, `email`, `openRecord`, `toggleWorkspace`, `pinToTop`, `rename`, `delete`).
- `'preview'` is ALWAYS hidden via `disabledActions` (the widget IS the preview surface).
- `'rename'` is hidden defensively when no host `onFileAction` is wired (no destination).
- When `onFileAction` is absent, additional actions that need host routing (`aiSummary`, `findSimilar`) are also hidden so the menu doesn't show dead entries.
- When `onFileAction` IS wired, those actions bubble to the host. `toggleWorkspace` is special-cased: the widget dispatches `workspace.widget_load` itself AND bubbles to the host (so the host can update local UI state like a pin icon).

---

## 4. PaneEventBus dispatch confirmation (ADR-030 additive event types)

### 4.1 `context.file_selected` — additive type added by task 016 (D2-06)

Dispatched on multi-file mode card clicks:

```tsx
const handleCardSelect = useCallback(
  (fileId: string) => {
    if (fileId === selectedFileId) return;
    setSelectedFileId(fileId);
    const file = files.find(f => f.fileId === fileId);
    dispatch('context', {
      type: 'file_selected',
      selectedFileId: fileId,
      selectionSource: 'context-card',
      contextType: 'chat-session-file',
      contextData: file ? { fileId: file.fileId, fileName: file.fileName } : { fileId },
    });
  },
  [dispatch, files, selectedFileId]
);
```

- Channel: `'context'` (existing — no 5th channel added per ADR-030).
- Discriminant: `'file_selected'` (additive, added by task 016).
- Payload fields:
  - `selectedFileId` — required per task 016's type definition.
  - `selectionSource: 'context-card'` — UX hint per `ContextPaneEvent.selectionSource` union.
  - `contextType: 'chat-session-file'` — narrow classifier so subscribers can filter.
  - `contextData` — minimal `{ fileId, fileName }` shape for subscribers that need the name without a session-manifest lookup.
- Existing `context` channel subscribers (entity-info, findings, citation-highlight) ignore unknown discriminants per the ADR-030 additive-types contract. No breakage expected.

### 4.2 `workspace.widget_load` — reuses R4 task 042 (W-4) demo plumbing

Dispatched when the user invokes the `toggleWorkspace` per-file action:

```tsx
dispatch('workspace', {
  type: 'widget_load',
  widgetType: 'document-viewer',
  displayName: file?.fileName ?? 'Document Viewer',
  widgetData: file
    ? { fileId: file.fileId, filename: file.fileName, contentType: file.contentType ?? '', sizeBytes: file.sizeBytes ?? undefined }
    : { fileId },
});
```

- Reuses the existing `'document-viewer'` workspace widget type (registered by R4 task 042 / `register-document-viewer-widget.ts`).
- No new channel, no new event type — pure reuse of existing Assistant → Workspace mount-source path.

---

## 5. Registration confirmation (ADR-018 — no new feature flags)

Dual registration mirroring the `playbook-gallery` + `findings` precedent:

1. **Aggregation file** `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts`:
   ```ts
   registerContextWidget('file-preview', {
     factory: () =>
       import('../widgets/context/FilePreviewContextWidget').then(m => ({
         default: m.default as unknown as ContextWidgetComponent,
       })),
   });
   ```
2. **Package barrel** `src/client/shared/Spaarke.AI.Widgets/src/index.ts`:
   - Same `registerContextWidget('file-preview', ...)` call inline.
   - Plus named exports for typed consumers: `FilePreviewContextWidget` (component), `FilePreviewContextData` (payload), `FilePreviewContextFile` (per-file shape), `FilePreviewContextRenderProps` (host-binding callbacks), `FilePreviewContextWidgetProps` (combined props), `FilePreviewFileActionHandler` (action callback), `FILE_PREVIEW_CONTEXT_WIDGET_TYPE` (string constant `'file-preview'`).

- **Unconditional registration** — no `if (flag)` block, no new feature flag per R5 CLAUDE.md §3.2 / ADR-018 Flag Scope Discipline.
- Registry's first-wins behavior makes dual registration safe (consistent with `playbook-gallery` / `findings`).

---

## 6. ADR compliance check

| ADR | Compliance |
|---|---|
| **ADR-012** (shared component library boundaries) | Widget lives in `@spaarke/ai-widgets`; consumes `RichFilePreview` + `DocumentRowMenu` from `@spaarke/ui-components` via the package barrel. No cross-cutting imports. |
| **ADR-018** (no new feature flags in R5) | Registration is unconditional; no `if (flag)` block. |
| **ADR-021** (Fluent v9 semantic tokens + dark-mode parity) | All styles via `makeStyles` + `tokens.*`. Regex audit shows no `#[0-9a-fA-F]` or `rgb(`/`rgba(` outside Fluent token references. Dark-mode parity by construction (tokens adapt to host theme). |
| **ADR-022** (React 19 functional components + hooks only) | Component is a React.FC; only `useState`, `useMemo`, `useCallback`, `useEffect`. No class components. No React 18-only APIs. |
| **ADR-028** (no token snapshots; host owns auth) | Widget never calls a BFF endpoint directly. The host provides `onFetchPreviewUrl(fileId)` so SAS-token / OBO concerns stay host-owned. |
| **ADR-030** (closed at 4 channels; additive event types only) | Uses ONLY `context` (additive `file_selected` type) + `workspace` (existing `widget_load` type). NO new channel. |

---

## 7. Disabled actions decision matrix

Per `effectiveDisabledActions` logic:

| Scenario | Hidden actions | Rationale |
|---|---|---|
| `disabledActions` prop supplied (any value) | Caller's list + `'preview'` (always) | Caller controls; widget always hides `'preview'` because the widget IS the preview surface. |
| `onFileAction` NOT supplied (no host handler) | `'preview'`, `'aiSummary'`, `'findSimilar'`, `'rename'` | Defensive — these actions have no destination without host routing. `toggleWorkspace` stays visible because the widget handles it internally. |
| `onFileAction` supplied (host handler present) | `'preview'`, `'rename'` | `'rename'` is commonly host-unimplemented; hosts override via prop if needed. All other actions bubble to the host. |

For the renderer's active-file menu (`activeRendererDisabledActions`):
- Same as card-level, EXCEPT `'toggleWorkspace'` is forced VISIBLE at the active surface (toggling makes sense from the active-file context).

---

## 8. Composability for task 021 (D2-12 "Summarize this only")

The `FilePreviewCard` sub-component is intentionally kept simple + composable:
- Compact card shape with icon + filename + type/size + 3-dot menu.
- Task 021 can extend the card with a "Summarize this only" affordance (e.g. a button beside the menu trigger) WITHOUT rewriting the existing markup — just append a new node to `styles.card`'s flex row.
- Alternative path: task 021 wires the existing `aiSummary` menu item to dispatch to the Summarize orchestrator with `fileIds: [singleFile]` — no widget change needed at all, just host-side `onFileAction` routing.

---

## 9. Open integration points (for downstream tasks)

| Open point | Owning task | Notes |
|---|---|---|
| Host wires `onFetchPreviewUrl(fileId)` to a per-file SAS-URL or `DocumentCheckoutService.GetPreviewUrlAsync` flow | Task 020 (D2-11) chat-pane orchestration UX | The widget is currently inert without this callback (renders empty preview area). |
| Host wires `onFileAction('aiSummary', fileId)` to the Summarize orchestrator dispatch | Task 021 (D2-12) "Summarize this only" affordance | Per-file Summarize routing; widget already bubbles the action. |
| Host wires `onFileAction('findSimilar', fileId)` to the RAG flow | Task 020 (D2-11) chat-pane orchestration UX | Existing R5 RAG flow — host callback only. |
| BFF widget payload key alignment | Phase 2 integration | Widget registers under type string `'file-preview'`. If the BFF dispatches a different key during Phase 2 integration, surface the mismatch — do NOT silently rename. |

---

## 10. Acceptance criteria checklist

| Criterion | Status | Evidence |
|---|---|---|
| (1) Widget file exists, exports `ContextWidgetComponent<FilePreviewContextData>` default | ✅ | `widgets/context/FilePreviewContextWidget.tsx` lines 1+ |
| (2) Single-file mode renders extracted `RichFilePreview` inline | ✅ | Same renderer used in both modes; multi-file mode just adds the card list above |
| (3) Multi-file mode card list + `context.file_selected` dispatch | ✅ | `FilePreviewCard` sub-component + `handleCardSelect` dispatch |
| (4) Per-file 3-dot menu uses `DocumentRowMenu` with documented dispatch routing | ✅ | `FilePreviewCard` line render + `handleFileAction` switch |
| (5) Widget registered with type string `'file-preview'` using lazy-factory pattern | ✅ | Dual registration in `register-context-widgets.ts` + `index.ts` |
| (6) Loading / empty / error states render gracefully | ✅ | `FilePreviewSkeleton`, `FilePreviewEmpty`, inline `MessageBar` |
| (7) Existing `context` subscribers ignore the new event type | ✅ | Additive-types contract (task 016) — no breakage |
| (8) `code-review` + `adr-check` at Step 9.5 | ⏭️ | Sub-agent scope is CODE AUTHORING only; quality gates run by main session per task instructions |

---

## 11. Build / publish-size note

- **Frontend-only change**: NO `.cs` files modified.
- **BFF publish-size delta**: 0 MB (no BFF code changed).
- **Per R5 CLAUDE.md §3.6**: `dotnet publish` re-measurement not required for this task.

---

## 12. Sub-agent scope guard

Per task 018 sub-agent instructions:
- ✅ Code authoring only (widget + registration + POML status + this evidence file)
- ❌ NO `git commit` / `git push`
- ❌ NO `npm run build`
- ❌ NO `dotnet build`
- ✅ Main session executes build, runs quality gates (code-review + adr-check), commits, and updates `TASK-INDEX.md`.

---

*End of evidence file.*
