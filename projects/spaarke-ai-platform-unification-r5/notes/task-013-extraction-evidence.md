# Task 013 (D2-08) — RichFilePreview renderer extraction evidence

> **Status**: Code authoring complete (sub-agent wave P2-G2). Build / test execution + dark-mode visual check + commit deferred to main session.
> **Date**: 2026-06-04
> **Task**: `tasks/013-extract-richfilepreview-renderer.poml`
> **Rigor**: FULL

---

## 1. Summary

Extracted the renderer core from `RichFilePreviewDialog.tsx` into a new shared primitive `RichFilePreview.tsx` in `@spaarke/ui-components`. Refactored the existing dialog into a thin wrapper that composes the extracted renderer inside Fluent v9 modal chrome.

**Per R5 CLAUDE.md §3.1**: this is an EXTRACTION (lines moved, not duplicated). Authoring a parallel preview component was explicitly prohibited and is NOT what happened here — the dialog wrapper's body now renders `<RichFilePreview {...props} />` inside `<DialogSurface>`. No parallel preview component was created.

---

## 2. Files created / modified

| Path | Status | LOC delta (approx) |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` | NEW | +585 |
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` | REFACTORED | 895 → 215 (-680) |
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/index.ts` | EXTENDED | +9 (additive exports) |
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/__tests__/RichFilePreview.test.tsx` | NEW | +281 |
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/__tests__/RichFilePreviewDialog.test.tsx` | NEW | +85 |
| `projects/spaarke-ai-platform-unification-r5/notes/task-013-extraction-evidence.md` | NEW (this file) | — |

**Net source delta**: ~+5 LOC across the two production files combined (renderer + dialog). The shared library gained one new file but the dialog file shrank by the same logic that the renderer file now hosts. The slight + is for explanatory comments + the new prop-API doc + the small wrapper boilerplate the dialog now needs.

---

## 3. Extraction-boundary decisions

| Concern | Lives in | Rationale |
|---|---|---|
| `Dialog`/`DialogSurface`/`DialogActions` envelope | `RichFilePreviewDialog.tsx` | Modal-only chrome; non-modal consumers (task 018 / task 022) must not pay for it. |
| `surface` style (max 1280px × 85vh) | `RichFilePreviewDialog.tsx` | Dialog-only surface clamp. Non-modal hosts size their own outer container. |
| `footer` style (Close button bar) | `RichFilePreviewDialog.tsx` | Modal-only Close affordance. |
| Title bar (title + Prev/Next + 3-dot menu) | `RichFilePreview.tsx` | Renderer-internal — every consumer of the renderer wants this chrome. |
| 2-column body grid (iframe + metadata pane) | `RichFilePreview.tsx` | Renderer core. |
| Preview-URL fetch effect | `RichFilePreview.tsx` | Renderer lifecycle; reset-on-open semantic preserved (effect re-fires on `documentId` change). |
| Keyboard listener (`ArrowLeft` / `ArrowRight`) | `RichFilePreview.tsx` | Renderer-internal; INPUT-focus guard preserved verbatim. |
| `dialogDisabledActions` → `DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS` | `RichFilePreview.tsx` | Exposed as a frozen constant + overridable via `disabledActions?` prop so non-modal consumers can override (e.g. Context-pane widget may need to expose `toggleWorkspace`). |
| `IFilePreviewDialogSummary` type | `RichFilePreview.tsx` (source) + re-exported from `RichFilePreviewDialog.tsx` | Preserves both import paths — `'./RichFilePreview'` (new) and `'./RichFilePreviewDialog'` (existing). |

**Reset-on-close semantic**: pre-extraction, the dialog's `useEffect` cleared `previewUrl` + `error` when `open` flipped false. After extraction, the renderer no longer knows about `open` — instead, the dialog wrapper conditionally mounts the renderer (`{open && <RichFilePreview ... />}`). When `open` flips false, the renderer unmounts entirely, which discards its state. Net effect: identical from the consumer's perspective. When `open` flips back to true, the renderer remounts with fresh state and the fetch effect fires.

**Title rendering**: the dialog wrapper previously used `<DialogTitle action={null}>` around the document name. The extracted renderer uses a plain `<Text as="h2">` because non-modal consumers may not want Fluent's `DialogTitle` semantics (which assume modal-dialog accessibility roles). Visually identical (semibold, `colorNeutralForeground1`, ellipsis-on-overflow). Reviewer flag if this needs revisiting.

---

## 4. Back-compat verification — existing consumers

The dialog's public prop API (`IFilePreviewDialogProps`) is UNCHANGED. Confirmed against the four known consumers:

| Consumer | Path | Import | Compatibility |
|---|---|---|---|
| LegalWorkspace `FilePreviewDialog` | `src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx` | `import { RichFilePreviewDialog } from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog';` | ✅ Same named export, same prop shape. Renders `<RichFilePreviewDialog open documentId documentName onClose fetchPreviewUrl onOpenFile onOpenRecord onEmailDocument onCopyLink onToggleWorkspace isInWorkspace />` — all props still present. |
| DocumentRelationshipViewer `FilePreviewDialog` | `src/client/code-pages/DocumentRelationshipViewer/src/components/FilePreviewDialog.tsx` | `export { RichFilePreviewDialog as FilePreviewDialog } from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog'; export type { IFilePreviewDialogProps, IFilePreviewDialogSummary } from ...;` | ✅ Pure re-export shim. Both named exports + both types still present at the same path. |
| SemanticSearchControl PCF `FilePreviewDialog` | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx` | `export { RichFilePreviewDialog as FilePreviewDialog } from '@spaarke/ui-components/dist/components/FilePreview/RichFilePreviewDialog'; export type { IFilePreviewDialogProps, IFilePreviewDialogSummary } from ...;` | ✅ Pure re-export shim (from compiled `dist/` path; after the next library publish the compiled output exposes the same named exports + types). |
| Office Add-ins | None found via `grep -r "RichFilePreviewDialog" src/client/office-addins` | n/a | ✅ No Office Add-in consumer was discovered in the current repo state; the task POML's enumeration mentions "any Office Add-in consumer" as a hypothetical. The other three consumers are confirmed unaffected. |

**No additional consumers discovered**: full repository search for `RichFilePreviewDialog` (excluding the project task POMLs, design / plan / spec / index docs, and the bundled `bundle.js` in the PCF solution package) found only the four files above.

---

## 5. Sub-agent execution scope

Per the wave-P2-G2 sub-agent contract:
- ✅ Code authoring complete (renderer extracted, dialog refactored, barrel updated, tests added, evidence file written).
- ⏭️ `npm run build` — deferred to main session.
- ⏭️ `npm test` — deferred to main session.
- ⏭️ `tsc --noEmit` cross-consumer check — deferred to main session.
- ⏭️ Dark-mode visual parity check (light + dark theme rendering) — deferred to main session.
- ⏭️ `code-review` + `adr-check` skills (Step 9.5) — deferred to main session.
- ⏭️ Commit + push — explicitly deferred per task launch instructions (sub-agent must NOT commit/push).

---

## 6. ADR compliance check (sub-agent self-review)

| ADR | Constraint | Verification |
|---|---|---|
| ADR-006 | No PCF control change | ✅ Zero changes to `src/client/pcf/`. The PCF's own `FilePreviewDialog.tsx` shim is a re-export — untouched. |
| ADR-012 | Shared-library boundary | ✅ Extracted renderer lives in `@spaarke/ui-components` (`src/client/shared/Spaarke.UI.Components/`). Both `@spaarke/ui-components` consumers AND future `@spaarke/ai-widgets` consumers (task 018, 022) can import. |
| ADR-021 | Fluent v9 semantic tokens; dark-mode parity | ✅ No hard-coded color literals introduced. Every color (`colorNeutralForeground1/2/3`, `colorNeutralStroke2`, `colorNeutralBackground2`) flows through `tokens.*`. Spacing, border widths, radii all use semantic tokens. Dark-mode visual check deferred to main session. |
| ADR-022 | React 19 (no React 18-only APIs) | ✅ Component uses `React.FC` + `useState` + `useEffect` + `useCallback` + `useMemo` only. No React 18-only APIs (`use`, `useTransition`, etc.) introduced. |
| ADR-029 (R5 NFR-01) | BFF publish-size delta | ✅ Zero BFF code changed. Publish-size delta = 0 MB (frontend-only task). |
| ADR-030 (PaneEventBus) | Zero new channels | ✅ Not applicable — this task does not touch the PaneEventBus. |

---

## 7. Out-of-scope confirmation (negative criteria)

- ✅ `DocumentViewerWidget.tsx` (task 022) NOT modified.
- ✅ `FilePreviewContextWidget.tsx` (task 018) NOT created.
- ✅ No parallel preview component created (per R5 CLAUDE.md §3.1).
- ✅ No new feature flag (per R5 CLAUDE.md §3.2; ADR-018).
- ✅ No new DI registration (frontend-only task).
- ✅ No new PaneEventBus channel (per R5 CLAUDE.md §3.4; ADR-030).

---

## 8. Surprises / decisions

1. **Title element**: switched the renderer's title from Fluent's `<DialogTitle>` to a plain `<Text as="h2">` because the renderer is no longer inside a modal dialog. The dialog wrapper does not re-wrap it in `<DialogTitle>` either, since the title is now part of the renderer subtree. Visual output identical (same color/weight/ellipsis); accessibility-wise the renderer presents a level-2 heading instead of a dialog title. Reviewer should sanity-check that screen-reader behavior in the modal case is still acceptable (the `Dialog` itself still provides modal-dialog ARIA roles).

2. **Reset-on-close behavior**: pre-extraction the renderer-internal effect cleared `previewUrl` + `error` when `open` flipped to false. Post-extraction the renderer doesn't know about `open`; the dialog conditionally mounts/unmounts the renderer instead. This produces an identical external behavior (next time the dialog opens, the renderer mounts fresh and re-fetches) but it is a structural change worth flagging for code review.

3. **`disabledActions` override seam**: exposed as an OPTIONAL renderer prop. When the dialog wrapper omits it, the renderer applies `DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS` + auto-hides `findSimilar` when no callback — exactly matching the pre-extraction dialog's `dialogDisabledActions` computation. Non-modal consumers (task 018 / task 022) get the same defaults unless they explicitly override.

4. **`DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS` exported as a frozen array**: lets non-modal consumers spread + remove from it (e.g. `[...DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS].filter(a => a !== 'preview')` if Context-pane wants to surface preview). Avoids forcing consumers to re-author the default list.

5. **No new Office Add-in consumer found** in the repo — only the three consumers (LegalWorkspace, DocumentRelationshipViewer, SemanticSearchControl PCF) were confirmed. The task POML's "any Office Add-in consumer" is a hypothetical; the back-compat verification is complete.

---

## 9. Next steps (for main session)

1. Run `npm run build` from `src/client/shared/Spaarke.UI.Components/` and confirm clean compile.
2. Run `npm test -- RichFilePreview` and confirm both new test files pass.
3. Run `tsc --noEmit` (or the workspace cross-consumer check) against the three consumer paths to confirm no API drift.
4. Render `RichFilePreviewDialog` in light + dark themes in LegalWorkspace dev page (or another live consumer) and confirm visual parity with the pre-extraction baseline.
5. Run `code-review` + `adr-check` skills at task-execute Step 9.5.
6. Mark task 013 ✅ in `TASK-INDEX.md`, reset `current-task.md` to next pending P2-G2 / P2-G3 task.
7. Note unblocking of tasks 018 (D2-09) + 022 (D2-08 follow-on) in the wave-progress log.
