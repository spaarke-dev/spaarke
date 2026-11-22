# Task 198 — yielding blocking SprkModal leaves its backdrop over the lookup pane

## Cause
`SprkModal` (src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.tsx) set `pointer-events: none` +
`inert` on the SURFACE while `yieldToSidePane`, but a blocking Dialog (`modalType='modal'`, no `nonBlocking`) also renders
`.fui-DialogSurface__backdrop`, a full-window sibling of the surface in the same portal. Nothing touched it. When
`useSidePaneLayering` ends in `dock` (pane page layer has no numeric z-index > 1) or is still `pending`, the modal's portal
layer is not lowered under the pane, so the backdrop (PCF portal z-index 1000000, pointer-events auto) sits above the pane
and eats every click. Only blocking modal that yields today: file preview in SemanticSearchControl
(`yieldToSidePane={wizardLookupOpen || findSimilarLookupOpen}` -> FilePreviewDialog -> RichFilePreviewDialog -> PreviewModal
-> SprkModal). Every existing yield test rendered `nonBlocking`, so the gap was never covered.

## Path taken: PREFERRED (non-modal while yielding)
`modalType = surfaceInert ? 'non-modal' : (alert ? 'alert' : nonBlocking ? 'non-modal' : 'modal')`, where
`surfaceInert = yieldToSidePane && !hidden` (the same condition that already drives `inert`/dim). One-line change.
Why it holds: Fluent react-dialog 9.17.2 `DialogSurface` renders `[backdropMotion(backdrop) | false, root]` as a fixed
array in one Portal, so toggling the backdrop does not move or remount the surface; the test proves it (mount counter 1,
child state kept, same surface and probe DOM nodes after on then off). The fallback (backdrop slot `pointer-events: none`)
was not needed, so the focus-trap-pull-back test was not written.

Side effects, judged acceptable:
- Fluent `useFocusFirstElement(open, modalType)` re-runs when `modalType` changes, so focus moves to the dialog's first
  focusable at the instant yielding starts and again when it ends (before: unchanged). While yielding the surface is `inert`
  and the platform pane takes focus when it opens; after the pick, focus returns to the first focusable instead of body.
- `aria-modal` is false and role is `dialog` (not `alertdialog`) while yielding only.
- Body scroll lock is released while yielding (non-modal), restored after.
- While yielding the (non-inert, un-blocked) page behind the dimmed modal can receive clicks, as with every nonBlocking
  modal today. Not reachable from the pane.
- Alert: an alert modal that yields also goes non-modal (goal: nothing the modal renders may take input from the pane);
  alert behaviour is untouched when not yielding. No host does this today. Covered by a test.

## Tests (sidePaneLayering.test.tsx, new describe "a blocking modal gives up its backdrop (task 198)")
8 tests: control (non-yielding blocking has backdrop; nonBlocking has none), dock, under, pending, surface still
pointer-events none + inert, alert yields then blocks again, `hidden` unchanged (backdrop untouched), on/off no remount.
"Takes pointer events" = a `.fui-DialogSurface__backdrop` exists whose computed pointer-events is not `none`.

Before (unchanged SprkModal.tsx): `Tests: 5 failed, 6 passed, 11 total` (3 pre-existing + 8 new; the 5 new failures):
```
x layering 'dock' (pane layer has no z-index): no backdrop takes pointer events   Expected: false  Received: true
x layering 'under' (pane layer z 1000): no backdrop takes pointer events          Expected: false  Received: true
x layering 'pending' (before the pane appears): no backdrop takes pointer events  Expected: false  Received: true
x an alert modal that yields also stops blocking, and blocks again afterwards     Expected: false  Received: true
x yielding on then off: backdrop returns ...; body and surface NOT remounted      Expected: false  Received: true
```
(control, surface-inert and `hidden` tests pass before and after, as intended.)
After: `src/components/SprkModal` 12 suites, 112 tests pass; with the broader filter (SprkModal, FilePreview,
DocumentEmailWizard, FindSimilarViewer, AccessGrantModal, Wizard, WizardFollowOns, EmailComposer) all suites pass,
no existing test modified.

## Other hosts checked
- FindSimilarViewerDialog: `nonBlocking={yieldToFrameSidePane}` and yields only then: already non-blocking.
- AccessGrantModal: `nonBlocking` + `yieldToSidePane={picking}`: already non-blocking.
- DocumentEmailWizard / SendEmailDialog / WizardShell: yield only when `nonBlocking`.
- BrowseModal / PreviewModal / RichFilePreviewDialog callers other than SemanticSearchControl pass no `yieldToSidePane`.
So SemanticSearchControl is the only PCF bundle hosting a blocking modal that yields.

## PCF
SemanticSearchControl 1.1.89 -> 1.1.90 (ControlManifest.Input.xml version + description-key, in-control footer
"v1.1.90 • Built 2026-11-22", Solution ControlManifest.xml, Solution/solution.xml, Solution/pack.ps1).
`npm run build:prod`: Succeeded (3 pre-existing size warnings); out/controls/SemanticSearchControl/bundle.js contains the new
modalType expression and "v1.1.90". `out/` is gitignored and the tracked Solution/.../bundle.js is only refreshed after a
deploy ("rebuilt bundles" commits), as for task 197, so no bundle file is committed. No other PCF bumped. Not deployed.

## Environment notes
- Four suites (InAppWizardHost x2, AddTodoFollowOn, CreateEventAndAddTodoFollowOn.sidePane) failed to run until
  `Spaarke.Auth` and `Spaarke.SdapClient` had their `npm install` and dist built (`ensure-dist-fresh.js`); then they pass.
- Local typecheck of the UI package (`npx tsc --noEmit -p .`) is clean after those dists exist.
- client-lint: `check --package ui-components` ok, `check --package semantic-search` ok. The full `check` needed per-package
  `install` for the other packages (not done here, none of them changed).

## Files changed
- src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.tsx
- src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.types.ts (yieldToSidePane doc)
- src/client/shared/Spaarke.UI.Components/src/components/SprkModal/__tests__/sidePaneLayering.test.tsx
- src/client/pcf/SemanticSearchControl/{SemanticSearchControl/ControlManifest.Input.xml, SemanticSearchControl/SemanticSearchControl.tsx, Solution/solution.xml, Solution/pack.ps1, Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml}
