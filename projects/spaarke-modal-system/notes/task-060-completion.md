# Task 060 Completion — P4 RichFilePreviewDialog → PreviewModal/BrowseModal; retire @deprecated FilePreviewDialog (FR-15)

> RIGOR: FULL. Executed per task-execute protocol. Dependency (007 — PreviewModal + BrowseModal presets) confirmed ✅ in TASK-INDEX.md before starting.

## Path-routing design (single-document vs record-set)

`RichFilePreviewDialog.tsx` computes the SAME `navEnabled` boolean it always has
(`navigationTotal > 0 && typeof currentIndex === 'number' && typeof onNavigate === 'function'`)
and now branches on it to pick the preset, instead of branching on whether to
mount `<RecordNavigationModalShell>`:

- `navEnabled === false` → `<PreviewModal>` (single document).
- `navEnabled === true` → `<BrowseModal>` with `nav={{index, total, onNavigate: handleNavigate}}`,
  where `handleNavigate` adapts `BrowseModal`'s direction-based
  (`'prev'|'next'`) callback to the wrapper's legacy index-based
  `onNavigate(nextIndex)` public contract (same adapter shape as before, just
  driven by the preset's `nav` prop instead of the shell's `onNavigate(direction)`).

Both branches mount the SAME `stage` element (`<RichFilePreview
showTitle={false} showMetadataPane={false} .../>`) as `children` — the
renderer's fetch/loading/error/iframe logic and its 3-dot `DocumentRowMenu`
are 100% preserved, unchanged control flow.

`RichFilePreviewDialog` no longer owns ANY local UI state (no `Dialog`, no
`DialogSurface`, no `isMaximized` `useState`, no `RecordNavigationModalShell`,
no `ModalWindowControls`) — all of that is now owned by `SprkModal` (via the
two presets), which is exactly the "renderer/envelope split... formalized by
the presets" the spec background section describes. Net effect: the file
shrank from 382 lines to ~245 lines (see diff stat below) while gaining a
composed preset instead of hand-rolled chrome.

## Layout-2 sizing evidence (content-driven, NOT 85%×85%)

`PreviewModal`/`BrowseModal` both fix `size="lg"` (from `SprkModal/sizes.ts`):

```
lg: { cap: 1280, widthVw: 94, height: '85vh', heightMax: 880, layout: 'landscape', ... }
→ width:  min(1280px, 94vw)
→ height: min(85vh, 880px)
```

This is a **strict improvement** on the pre-task-060 hand-rolled dialog, which
had `width:'100%', maxWidth:'1280px', height:'85vh'` (no height cap — grows
unbounded/square-ish on a 2560×1440 monitor, `72vh`→ ~1037px). The `lg` preset
adds the `heightMax:880` cap that holds the landscape rectangle on tall
monitors, per `record-modal-selection.md`'s own citation of this exact
component as the canonical Layout-2 reference case.

This is categorically different from Layout 1's OOB `record` size (`85%×85%`
of the viewport, used for `Xrm.Navigation.navigateTo` full-form opens) — the
`lg` preset's caps are FIXED px values with a `vw`/`vh` clamp, not a raw
percentage-of-viewport rectangle. Verified by a new assertion in
`RichFilePreviewDialog.test.tsx`:

```ts
expect(screen.getByRole('dialog')).toHaveStyle({
  width: 'min(1280px, 94vw)',
  height: 'min(85vh, 880px)',
});
```

## Renderer decomposition (`RichFilePreview.tsx`) — additive only

Two additive changes, both backward-compatible for existing non-modal
consumers (`FilePreviewContextWidget.tsx`, `DocumentViewerWidget.tsx` — grepped,
confirmed neither passes the removed props nor needs the new one to default
differently):

1. **New optional `showMetadataPane?: boolean` prop (default `true`)**. When
   `false`, the renderer's own 2-column body collapses to just the stage
   (iframe/loading/error), full width, no grid split — because the MODAL
   wrapper now supplies its OWN 320px meta column via
   `PreviewModal`/`BrowseModal`'s `metadata` prop (`PreviewGridBody`).
   Mounting the unmodified 2-column `RichFilePreview` body INSIDE the
   preset's stage cell would have nested a SECOND 320px panel inside the
   preset's own 320px panel — this prop avoids that duplication.
   `RichFilePreviewDialog` computes its `metadata` array by reusing
   `RichFilePreview`'s own (now-exported) `formatDate`/`formatFileSize`/
   `nonEmpty` helpers — composed, not duplicated (CLAUDE.md §11).
2. **Removed `onClose`/`isMaximized`/`onToggleMaximize`** (+ the
   `ModalWindowControls` import/render). These were added by P1 task 030 as
   an INTERIM measure (wired into BOTH `RichFilePreviewDialog`'s non-nav path
   AND `RichFilePreview` itself) before the `SprkModal` presets existed.
   Grepped confirmed: `RichFilePreviewDialog` was their ONLY caller anywhere
   in the repo (task-030's own completion notes confirm the same). Since
   BOTH paths (single-doc and browse) now render via a preset whose
   `SprkModal` header owns `ModalWindowControls` unconditionally, these 3
   props were fully dead the moment `RichFilePreviewDialog` stopped passing
   them — removed rather than left as an unused shim (repo convention: "no
   shims, complete or delete", per `RecordNavigationModalShell/README.md`).
   Zero existing test in `RichFilePreview.test.tsx` referenced these 3 props
   (confirmed by reading the full file before editing) — zero test breakage.

The 3-dot `DocumentRowMenu` is UNCHANGED and keeps rendering inside the stage
cell's (now much thinner) title-bar row — with `showTitle=false` and no
nav/window-controls props, that row collapses to just the menu button,
right-aligned (pre-existing `marginInlineStart:'auto'` on `titleActions`
already handled this before my change).

## Migration mapping: `FindSimilarResultsStep.tsx` off the deprecated dialog

The deprecated `FilePreviewDialog`'s `services: IFilePreviewServices` bundle
prop is replaced by `RichFilePreviewDialog`'s per-action callback props. The
injected `filePreviewServices: IFilePreviewServices` prop on
`IFindSimilarResultsStepProps` is **UNCHANGED** (so `FindSimilarDialog.tsx`,
which passes it in, needs zero edits — confirmed by reading that file; it is
also out of scope, owned by sibling task 061 which is concurrently editing it
for unrelated reasons in this same worktree).

| Old (deprecated dialog, internal) | New (`RichFilePreviewDialog` callback prop) | Mapping |
|---|---|---|
| `services.getDocumentPreviewUrl(documentId)` (on open + retry) | `fetchPreviewUrl: () => Promise<string\|null>` | `() => filePreviewServices.getDocumentPreviewUrl(previewItem.id)` — direct reuse, function-passed per ADR-028 |
| `services.getDocumentOpenLinks` cascade (desktop → web → previewUrl fallback), single "Open File" button | `onOpenFile: (mode:'desktop'\|'web') => void` | Re-implemented the IDENTICAL 3-step cascade in the new callback, using the same `filePreviewServices` methods (desktop wins if present; else web; else fetch+open the preview URL). `RichFilePreview`'s `DocumentRowMenu` always dispatches `mode='desktop'`, but the handler honors `'web'` too for defensiveness. |
| `services.navigateToEntity({action:'openRecord', entityName:'sprk_document', entityId, openInNewWindow:true})` | `onOpenRecord: () => void` | Direct 1:1 call, same shape |
| `services.copyDocumentLink(documentId)` | `onCopyLink: () => void` | Direct 1:1 call |
| *(no email action existed)* | `onEmailDocument: () => void` (**required** on the new contract) | Documented no-op — mirrors the EXACT precedent already shipped in `EmailReadingAttachments.tsx` (`onEmailDocument={() => { /* out of scope for this surface */ }}`), a sibling `RichFilePreviewDialog` consumer with the identical no-send-email-capability characteristic. **Not** a behavior regression: the deprecated dialog never had an email affordance either; the new dialog's menu now SHOWS an inert "Email" item (an additive, precedented UX delta — `email` is not in `RichFilePreview`'s default-hidden action set, unlike `toggleWorkspace`/`aiSummary`/`preview`/`rename`). |
| *(no metadata pane existed at all)* | `documentType?`, `createdAt?` (optional) | `mapDocumentResults` extended (2 new passthrough fields, additive — no existing grid column reads them) to carry `IDocumentResult.documentType`/`.createdAt` through to the grid row, so the migrated preview's Details pane shows real values instead of always "—". `createdBy`/`fileSize` are not present in the Find-Similar search-result payload (`IDocumentResult` has no such fields) — both gracefully render "—" via the existing, tested placeholder behavior. |
| 3 separate `useState` (`previewOpen`/`previewDocId`/`previewDocName`) | 1 `useState<GridItem \| null>` (`previewItem`) | Simplified to a single "whole clicked row" state, mirroring the identical `previewItem` pattern already shipped in `EmailReadingAttachments.tsx` — avoids 2 more parallel state variables to carry `documentType`/`createdAt` through. |

**Escalation trigger assessed and NOT fired.** The POML's escalation trigger
("if the old service-callback contract has no PreviewModal-equivalent without
behavior change → STOP") does not apply here: every mapping above is either a
direct 1:1 reuse of the SAME `filePreviewServices` methods (open-record,
copy-link, preview-url fetch), a faithful port of the deprecated dialog's own
cascade logic (open-file), or a documented no-op that mirrors an
ALREADY-SHIPPED, reviewed precedent in this exact codebase
(`EmailReadingAttachments.tsx`'s `onEmailDocument`). No capability is lost;
the only deltas are additive (metadata pane appears, mostly with real values
where the search-result payload has them).

## Retirement grep proof (zero dangling imports in scope)

```
grep -rn "FilePreview/FilePreviewDialog" src/            → 0 matches (deep-import module path — nobody imports the deleted file anywhere)
grep -rn "export \{ FilePreviewDialog \}" Spaarke.UI.Components/  → 0 matches (barrel no longer re-exports it)
test -f .../FilePreview/FilePreviewDialog.tsx            → file does not exist (deleted)
```

Remaining hits for the bare string `FilePreviewDialog` are ALL out-of-scope,
same-named, independent local files per the POML's own notes (verified each
one's import specifier resolves to ITS OWN local file, not the deleted
shared-lib one):
- `src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx` (LegalWorkspace's own adapter — imports `RichFilePreviewDialog`, not the deprecated one)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx` (re-exports `RichFilePreviewDialog as FilePreviewDialog` from a deep import path)
- `src/client/code-pages/DocumentRelationshipViewer/src/components/FilePreviewDialog.tsx` (same re-export pattern)

`src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/FindSimilarResultsStep.tsx`
now imports `RichFilePreviewDialog` (not `FilePreviewDialog`) from `'../FilePreview'` —
this is the CORRECT, migrated import, not a dangling reference.

## Wording note: BrowseModal vs. literal `RecordNavigationModalShell` composition

The POML's AC text says BrowseModal renders "`‹ N of M ›` navigation composing
`RecordNavigationModalShell`". Per explicit guidance given at task dispatch,
this phrasing predates task 007's actual shipped decision (recorded in
`notes/wave-b-completion.md`): `BrowseModal` = `PreviewModal` + `SprkModal`'s
OWN built-in header `nav` prop (click Prev/Next + "N of M" counter), NOT a
nested `<RecordNavigationModalShell>` — nesting the shell's own `Dialog`/header
envelope inside `SprkModal` would render two headers/counters (the exact
anti-pattern the original task 007 POML's own escalation trigger named). This
was a deliberate, already-reviewed architecture call, not something task 060
should re-litigate.

`BrowseModal` instead exposes `onBeforeNavigate?: (dir) => boolean |
Promise<boolean>` as the composition seam for delegating a cross-frame
dirty-check (e.g. to `RecordNavigationModalShell`'s protocol) WITHOUT nesting
its chrome. **This task does not wire `onBeforeNavigate`** — file preview is
read-only with no unsaved-state concept (the pre-task-060 code's own
`dirtyCheckTargetWindow={undefined}` already establishes this — dirty-check
was never active for file preview even under the old shell-based
implementation). The seam remains available, unused, for a future consumer
that has real dirty state to guard.

Net result: `RecordNavigationModalShell` itself is untouched by this task
(not forked, not modified) — `RichFilePreviewDialog` simply no longer
imports it, because `BrowseModal` (a previously-reviewed, already-shipped
preset) already generalizes the "N of M" concept at the `SprkModal` level.
This satisfies the AC's INTENT (compose the canonical browse-nav
abstraction, don't hand-roll a parallel one) even though the literal
component named in the AC text is not instantiated by this particular
consumer.

**Keyboard arrow-key nav — confirmed NOT a regression.** The PRE-task-060 code
already omitted nav props when mounting `RichFilePreview` inside
`RecordNavigationModalShell` (comment in the old code: "nav props
deliberately omitted — shell owns nav chrome"), so `RichFilePreview`'s own
ArrowLeft/ArrowRight keyboard listener was ALREADY inert in the browse path
before this task (only the shell's/now-preset's click-driven Prev/Next
worked). `BrowseModal`/`SprkModal` do not add document-level keyboard-arrow
listeners either — this task preserves the EXACT SAME (click-only)
characteristic, it does not remove a previously-working keyboard feature.

## Preset-gap report (per ADR-012 Step 9.5 instruction — reported, not worked around)

`PreviewModal`/`BrowseModal` do not currently expose a `headerActions`
passthrough to `SprkModal`'s own `headerActions` prop (which DOES exist on
`SprkModal` itself — `PreviewModalProps`/`BrowseModalProps` simply don't
forward it). The 3-dot `DocumentRowMenu` therefore cannot be relocated into
the shell's header-right slot (next to `ModalWindowControls`) without either
forking the shell (forbidden by ADR-012) or extending the two presets (out of
this task's hard boundary — "Do NOT modify SprkModal/presets (gaps =
report)"). Interim resolution: the menu stays inside the stage cell, in
`RichFilePreview`'s own (now much thinner) title-bar row — visually it now
sits at the top of the LEFT stage column rather than the modal's own
top-right header, a minor placement change, not a capability loss. Flagging
for the main session / a future preset-hardening task to consider adding
`headerActions?: ReactNode` to both presets (thin passthrough to
`SprkModal`), which would let a future task relocate the menu to the header
proper.

## Build/verify results

| Check | Command | Result |
|---|---|---|
| TypeScript | `npx tsc --noEmit` (shared-lib) | **PASS** — exit 0, zero errors |
| ESLint | `npx eslint <6 touched files>` | **PASS** — zero errors/warnings |
| Scoped jest | `npx jest src/components/FilePreview src/components/SprkModal/presets/__tests__/PreviewModal src/components/SprkModal/presets/__tests__/BrowseModal src/components/FindSimilar` | **54 passed / 2 failed** of 56, across 4 suites (3 suites 100% green: `RichFilePreviewDialog.test.tsx`, `PreviewModal.test.tsx`, `BrowseModal.test.tsx`; `FindSimilar` contributes 0 suites — no test file exists there). The 2 failures are BOTH in `RichFilePreview.test.tsx` and are **PRE-EXISTING BASELINE**, confirmed by hunk-level diff inspection: (1) `keyboard nav › does NOT dispatch when keydown target is contentEditable` — an event-dispatch call-count mismatch in a `useEffect`/keyboard-listener code path my diff never touches (confirmed: zero diff hunks anywhere near that logic); (2) `metadata pane › renders Tags section with the documentType chip` — a pre-existing "multiple elements with text NDA" ambiguity between the Tags chip and the Details "Type" row, both rendered by `renderTagSection()`/`renderDetailsSection()` — functions my diff does not modify (confirmed via hunk inspection). Both failures are independently named, verbatim, in task-030's OWN completion notes (`notes/task-030-completion.md`) as pre-existing and unrelated to that task's diff either — same conclusion applies here since neither function was touched by either task. **Zero NEW failures introduced.** Added 5 new tests (2 in `RichFilePreviewDialog.test.tsx` replacing 2 stale ones — net even; 3 new in `RichFilePreview.test.tsx` for `showMetadataPane`) — all 5 pass. |
| Retirement grep | see above | **PASS** — zero dangling imports in scope |
| Hex/`'1px'`/inline-color diff-gate | `git diff \| grep -iE "#[0-9a-f]{3,8}\|'1px'\|inline color"` on added lines | **PASS** — zero matches |

Not re-run this task (not listed in the task's Build/verify discipline
section, unlike task 030): a PCF consumer build (`@types/react` 18) and a
Code Page consumer build. No React-19-only API was introduced anywhere in
this diff (only `React.useState`/`useCallback`/`useMemo`/`React.FC`, all
React-16-safe); the public prop contract for every PCF/Code-Page consumer of
`RichFilePreviewDialog` (`CommunicationAttachmentsApp.tsx`,
`SemanticSearchControl`'s `FilePreviewDialog.tsx` re-export, LegalWorkspace's
adapter, etc.) is byte-identical to before this task, so no new
cross-version-typing risk is introduced by this change specifically.

## Step 9.5 gates (FULL rigor)

- **Self code-review**: renderer fetch/loading/error/iframe/keyboard-nav/
  row-action logic byte-preserved (confirmed via hunk-level diff inspection —
  zero touch to those functions); interim P1 `ModalWindowControls` wiring
  removed as dead code (zero other callers, confirmed by grep + task-030's
  own notes); retirement complete (file deleted, barrel cleaned, sole
  consumer migrated, zero dangling imports).
- **adr-check**:
  - ADR-012 — composed `PreviewModal`/`BrowseModal` as shipped; did not fork
    or modify `SprkModal`/presets/`RecordNavigationModalShell`; one preset
    gap (`headerActions` passthrough) reported above rather than worked
    around.
  - ADR-021 — zero hex/`'1px'`/inline-color added (grep-verified); the one
    new style (`bodyStageOnly`) mirrors the immediately-adjacent, pre-existing
    `body` style's `shorthands.padding('0px')` convention (a dimension
    zero-reset, not a hairline-border `'1px'` literal).
  - ADR-028 — `fetchPreviewUrl`/`onOpenFile`/`onOpenRecord`/`onCopyLink`/
    `onEmailDocument` all passed as functions, never snapshotted.
  - NFR-04 — no React-18/19-only API used; public contract unchanged for all
    PCF/Code-Page consumers (see caveat above re: no fresh PCF/Code-Page
    build this round).
  - NFR-05 — client-only, zero BFF touch.

## POML acceptance-criteria checklist

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | Single document → `PreviewModal`; record-set → `BrowseModal` with `‹ N of M ›` nav | **PASS** (composes `SprkModal`'s own nav group via `BrowseModal`, not a nested `RecordNavigationModalShell` — see wording note above; satisfies AC intent per explicit task-dispatch guidance) |
| 2 | Preview size is content-driven (Layout 2), NOT 85%×85% | **PASS** — `lg` = `min(1280px,94vw) × min(85vh,880px)`, verified by test assertion |
| 3 | `@deprecated FilePreview/FilePreviewDialog.tsx` deleted; `FindSimilarResultsStep.tsx` migrated | **PASS** |
| 4 | Negative: zero dangling `FilePreviewDialog` imports in scope | **PASS** — grep-proven (see above) |
| 5 | `RecordNavigationModalShell` composed not forked; no hex/`'1px'`/inline color; dark parity; shared-lib build green under `@types/react` 18 + React 19 | **PASS** — shell untouched/not forked (superseded by the already-reviewed `BrowseModal` nav-prop architecture); zero token violations; dark parity inherited from untouched preset/renderer styling (`PreviewModal.test.tsx`'s dark-theme test still passes); `tsc --noEmit` green under React 19 typing (PCF/React-18 build not independently re-run this task — see caveat) |

## Files modified

- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` (re-based)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` (additive: `showMetadataPane` prop, exported formatters; removed dead P1 window-controls props)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/index.ts` (barrel cleanup — removed deprecated exports, dropped now-unnecessary type alias)
- `src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/FindSimilarResultsStep.tsx` (migrated off the deprecated dialog)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/__tests__/RichFilePreviewDialog.test.tsx` (updated for preset composition)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/__tests__/RichFilePreview.test.tsx` (added `showMetadataPane` coverage)

## Files deleted

- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/FilePreviewDialog.tsx`

## Deviations / escalations

- **No escalation fired** (see "Escalation trigger assessed and NOT fired" above).
- **Preset gap reported** (`headerActions` passthrough missing on `PreviewModal`/`BrowseModal`) — not worked around, per instruction; candidate for a future preset-hardening task.
- **BrowseModal-vs-RecordNavigationModalShell wording delta** — documented above; resolved per explicit task-dispatch guidance, not a fresh decision made unilaterally by this task.
- Concurrent sibling agent (task 061) is editing `FindSimilar/FindSimilarDialog.tsx` in this same worktree during this task's execution (confirmed via `git diff --stat`, 30 unrelated inserted lines) — not touched by this task; `tsc --noEmit` remained clean throughout, confirming no interference between the two concurrent changesets at the time of this task's verification.
