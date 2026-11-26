# Task 195 - Invoice Review on the shared modal, with browse; one-row count cards

Branch `task/ebilling-195` (base `9581114231`). Contract: `projects/ebilling-r1/tasks/195-invoice-review-shared-modal-browse.poml`.
Nothing deployed. Browser behaviour is not verified here (see "Not verified").

## What was built, per design item

- **A. In-app modal, not `navigateTo`.** New `InvoiceReviewModal` (`Spaarke.UI.Components/src/components/Worklist/InvoiceReviewModal.tsx`) composes `SprkModal`
  directly (size `xl`, `padded={false}`) around an `<iframe>` whose `src` is the Invoice Review page URL. `WorklistWidget` takes a new optional prop
  `invoiceReviewClientUrl` (the HOST supplies it; the shared library has no Xrm, ADR-012). With it, activating a billing item opens the modal; without it the
  existing `openInvoiceReview` / `navigateTo` path runs unchanged. The AI.Widgets `WorklistWorkspaceWidget` and the LegalWorkspace
  `worklist.registration.ts` pass `getClientUrl()` through.
- **B. URL helper.** `buildInvoiceReviewUrl` in `openInvoiceReview.ts`: cleans GUID braces and case, omits `lineId` when absent, encodes `data` once, no double slash.
  The existing `buildInvoiceReviewLaunch` now shares it.
- **C. Browse set.** `buildInvoiceBrowseSet` (`invoiceReviewBrowse.ts`): the lane/filter in force, billing items only, one entry per invoice (first occurrence wins,
  so display order is kept), start index at the activated invoice, the activated line kept for that first entry. The set is captured when the modal opens.
- **D. "N of M" browse.** The modal uses the shell's own `nav` prop (ADR-050): header "N of M", previous/next, the end disables the matching button, a
  one-invoice set shows "1 of 1". Moving changes the iframe `src`.
- **E. Dirty check.** `nav.onBeforeNavigate` runs the cross-frame round trip. The parent half was a closure inside `RecordNavigationModalShell`; it is now the
  exported `queryDirtyState` (`RecordNavigationModalShell/queryDirtyState.ts`), and the shell calls it (shell behaviour unchanged, its own suite is untouched and
  green). The one addition is `requireSourceMatch` (off by default): an answer counts only from the frame's own window. The modal passes
  `requireSourceMatch: true`, the frame's origin as target, and a same-origin-only trust test. Dirty raises the shared `ConfirmModal` ("Discard and continue" /
  Cancel); clean, or no answer in 1000 ms, moves with no prompt. Page side: `useDirtyCheck.ts` in InvoiceReview answers `request-dirty-check` only for
  same-origin requests, replying to the requester pinned to its origin. It is dirty for a non-blank note, an unsaved reduce entry (amount or reason), the
  decision wizard open, or a write in flight; false on a fresh load. Wired into `InvoiceReviewPage`, `LineReviewPane`, `Dialogs`, `DecideInvoiceAction`,
  `BlockedBanner` and `SubmitCorrectedInvoiceAction`.
- **F. Close.** The × closes the modal and reloads the lanes once.
- **G. Count cards on one row.** `InvoiceReviewPage` passes a page-local class (`cardsRow`) to `MetricCardRow` (`layout="wide"`): five equal columns, each card
  turned into a short rectangle (number and label on one line, tighter padding, smaller number). The cards area has a 480px basis so the tabs and "Add line" wrap below.
  `MetricCard`, `MetricCardRow` and `SprkModal` are unchanged (verified: empty `git diff`).
- **H. Version.** `@spaarke/ui-components` 3.1.0 to 3.2.0; the lock files of the five consumers that pin it are bumped (UI.Components, AI.Widgets, InvoiceReview,
  LegalWorkspace, SpaarkeAi).

## Judgement calls

1. Extracted `queryDirtyState` from the shell rather than copying it (one owner of the protocol). `requireSourceMatch` is the single behavioural addition and defaults off.
2. `dismiss="explicit"`: the frame may hold unsaved work, so a backdrop click or Esc does not close it.
3. No `uiScale` passed from the widget.
4. The × is not dirty-guarded (the spec asks only for browse to be guarded).
5. "View decision" (the read-only wizard) counts as dirty, because the spec says an open wizard is dirty.
6. Cards are sized with descendant selectors on the page-local class because `MetricCardRow` passes no class down to its cards.
7. `flex: '1 1 480px'` is a layout basis, not a colour or spacing token; no colour literals or inline `style` were added.
8. `InvoiceReviewModal` is exported to the Worklist barrel as a TYPE only (the barrel guard in `Worklist.test.tsx` forbids a second row/value component there); the
   widget imports the file directly. `laneCountFilters.test.tsx`'s file list gained `InvoiceReviewModal.tsx`.
9. No test for the LegalWorkspace registration (that package has no jest config); it is covered by the typecheck and by the AI.Widgets test for the same wiring.
10. "The set is not reshuffled when the lanes reload under the open modal" holds by construction (the set is state captured at open) and has no test.

## Theme finding (design note 2)

`resolveCodePageTheme` (`themeStorage.ts`) resolves in three tiers: (1) localStorage `spaarke-theme`; (2) a `flags` / `theme` query parameter; (3) the
`[data-id="navbar-container"]` element in the current document or ONE parent frame.
- Tier 1 is identical in the old dialog and the new nested frame (the frame is same-origin with the Console).
- Tier 3 differs. In the `navigateTo` dialog the parent was the model-driven app window, which has the navbar. In the new nested frame the parent is the Console
  document, which has none, so tier 3 yields nothing and the page falls back to light.
- So a divergence appears only for a user with no stored `spaarke-theme`, no flag, who relies on a dark model-driven navbar. The Console itself uses the same
  resolver, so it agrees with the frame in that case.
- No fix applied (out of scope, and unconfirmed in a browser). Mitigations if the owner sees it: pass `theme=` in the frame query, or widen the navbar walk by one frame.

## Tests and builds (all run in this worktree)

| Command | Result |
|---|---|
| `npm run build` in `src/client/shared/Spaarke.UI.Components` | clean (tsc) |
| `npx jest src/components/Worklist src/components/SprkModal src/components/RecordNavigationModalShell` (UI.Components) | 30 suites, 484 tests, all pass |
| `npx jest WorklistWorkspaceWidget` (AI.Widgets) | 1 suite, 9 tests, all pass |
| `npx jest` in `src/solutions/InvoiceReview` | 23 suites, 706 tests, all pass |
| `npm run build` in `src/solutions/InvoiceReview` | green (surface-owned tsc errors 0; 47 pre-existing shared-lib errors, deferred) |
| `npm run typecheck` in `src/solutions/LegalWorkspace` | clean: `src/**` clean, 44 baselined errors outside `src/**`, no new ones |
| `npx tsc --noEmit` in AI.Widgets, filtered to `WorklistWorkspaceWidget` | no errors in the changed file (other packages' missing-dependency noise is environmental) |

New tests: `queryDirtyState.test.ts` (7), `InvoiceReviewModal.test.tsx` (18), `WorklistInvoiceReviewModal.test.tsx` (8), `dirtyCheckResponder.test.tsx` (10),
plus one added case in the AI.Widgets `WorklistWorkspaceWidget.test.tsx`.

### Seen failing before the change

- `WorklistInvoiceReviewModal.test.tsx` with `WorklistWidget.tsx` reverted to base: 5 failed, 3 passed (the 3 are the unchanged-path and negative cases).
- `dirtyCheckResponder.test.tsx` with the six page files reverted to base: 7 failed (fresh load, note, spaces-only note, reduce entry, write in flight, wizard open, and the
  cards one-row CSS test), 3 passed (the negatives: other origin, not a request, stops after unmount).
- AI.Widgets `WorklistWorkspaceWidget` with the widget reverted: 1 failed (the client-URL hand-over case), 8 passed.
- `queryDirtyState.test.ts` and `InvoiceReviewModal.test.tsx` fail trivially at base because the modules do not exist; I did not re-prove them separately.

### Test note

In `WorklistInvoiceReviewModal.test.tsx` the assertions after moving to "2 of 2" query buttons with `hidden: true`. In jsdom the Fluent/tabster modalizer, on a
timer, marks the dialog surface `aria-hidden` when no element in it has focus (`fireEvent` does not focus). The buttons are present and enabled/disabled as
asserted. The standalone `InvoiceReviewModal.test.tsx` does not hit it. Worth a look in a real browser (see below).

## Files changed

Under `src/client/shared/Spaarke.UI.Components/` (`src/components/`): `Worklist/InvoiceReviewModal.tsx` (new), `Worklist/invoiceReviewBrowse.ts` (new),
`Worklist/WorklistWidget.tsx`, `Worklist/openInvoiceReview.ts`, `Worklist/index.ts`, `RecordNavigationModalShell/queryDirtyState.ts` (new),
`RecordNavigationModalShell/RecordNavigationModalShell.tsx`, `RecordNavigationModalShell/index.ts`, `RecordNavigationModalShell/README.md`, `package.json`,
`package-lock.json`; tests `Worklist/__tests__/InvoiceReviewModal.test.tsx`, `Worklist/__tests__/WorklistInvoiceReviewModal.test.tsx`,
`Worklist/__tests__/laneCountFilters.test.tsx`, `RecordNavigationModalShell/__tests__/queryDirtyState.test.ts`.

`src/client/shared/Spaarke.AI.Widgets/`: `src/widgets/workspace/WorklistWorkspaceWidget.tsx`, its test, `package-lock.json`.

`src/solutions/InvoiceReview/`: `src/components/useDirtyCheck.ts` (new), `InvoiceReviewPage.tsx`, `LineReviewPane.tsx`, `Dialogs.tsx`,
`decision/DecideInvoiceAction.tsx`, `decision/BlockedBanner.tsx`, `decision/SubmitCorrectedInvoiceAction.tsx`, `src/__tests__/uiComponentsTestSeam.ts`,
`src/__tests__/dirtyCheckResponder.test.tsx` (new), `package-lock.json`.

`src/solutions/LegalWorkspace/`: `src/sections/worklist.registration.ts`, `package-lock.json`. `src/solutions/SpaarkeAi/package-lock.json`.

## Not verified (browser checks for the owner on dev)

1. Open an invoice from the Console worklist: in-app modal titled "Invoice Review", and the theme in dark and light (especially with no stored preference).
2. "N of M" counter; previous/next swap the invoice; the ends disable; the dirty prompt appears with a typed note, an open wizard or a write in flight; Cancel keeps the invoice.
3. After moving to the last record, the dialog is still reachable by keyboard and a screen reader (the jsdom aria-hidden note above).
4. Same-origin assumption: Console and page on the same org origin. If they differ, the dirty check silently treats the frame as clean.
5. Count cards on one row at 1280 wide, readable, about 62-72px tall.
6. The lanes refresh after the modal closes; the set stays put if they reload under the open modal.
7. The Navigator side pane still registers inside the iframe.
