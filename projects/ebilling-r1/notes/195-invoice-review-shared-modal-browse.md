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
9. ~~No test for the LegalWorkspace registration (that package has no jest config)~~ WRONG, corrected in fix round 1: the registration shim IS tested, in
   `src/client/shared/Spaarke.DailyBriefing.Components/test/worklistSectionRegistration.test.tsx` (that package imports LegalWorkspace's source, as its header says).
   Round 0 broke that suite (see F1 below).
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

## Fix round 1 (independent review of `fa956e7c76`)

| Item | Fix | Tests |
|---|---|---|
| **F1** broken suite | `DailyBriefing.Components/test/__mocks__/spaarke-ui-components.tsx` now exports the REAL `getClientUrl` (same deep import as `getXrm`); the suite had failed to compile (TS2305), losing every case. Note 9 above corrected. | `worklistSectionRegistration.test.tsx`: with Xrm the shim passes `invoiceReviewClientUrl`; without Xrm `undefined`; an empty client URL gives `undefined`. Whole DailyBriefing.Components suite run. |
| **F2** light page in a dark Console | The shared resolver already reads a direct `theme=dark\|light` URL parameter (`detectDarkModeFromUrl`, tier 2 of `resolveCodePageTheme`, after a stored preference and before the navbar check), and the e-billing page bootstrap already uses `resolveCodePageTheme`, so NO page or `themeStorage.ts` change. The widget runs the shared `getEffectiveDarkMode()` in the Console's own frame (stored preference, URL flag, then the navbar of the frame above, which IS the model-driven app from there) when the modal opens, and `buildInvoiceReviewUrl(clientUrl, target, theme)` adds `&theme=dark\|light` beside `data` (not inside it). Supersedes the "no fix applied" in the theme finding above. | `InvoiceReviewModal.test.tsx` (URL carries the theme beside `data`; every record keeps it; none when no prop), `WorklistInvoiceReviewModal.test.tsx` (stored dark gives dark; stored light and nothing stored give light; the dark URL flag with nothing stored gives dark), `themeStorage.test.ts` (nothing stored and `theme=dark` gives dark, `theme=light` beats a dark navbar, an explicit stored preference beats `theme=`). |
| **F3** history entries | `key={url}` on the iframe: each invoice mounts a FRESH frame, whose first load adds no history entry. The dirty query reads `frameRef.current` at call time, so it targets the current frame. | `InvoiceReviewModal.test.tsx`: each move mounts a NEW iframe element (old one disconnected, one iframe in the document); the dirty check after a move asks the current frame; Cancel keeps the same element. |
| **F4** focus lost at the ends | After a move, if `document.activeElement` is the body or a disabled button, focus goes to the enabled nav button in the direction of travel, else the other one, else Close. `SprkModal.tsx` is untouched. The widget test no longer queries with `hidden: true`: it clicks with `userEvent` (real focus) and asserts focus lands on Previous. | `InvoiceReviewModal.test.tsx`: last record gives Previous, first record gives Next, focus lost to the body mid-set returns to the direction of travel, and focus the person put on Close is left alone. |
| **F5** test claimed more | The filter case now selects a count filter (Do lane, "Coming due") over three billing invoices, activates an item, and asserts M, the order and the neighbour come from the filtered list (a control test with no filter: "3 of 3"; filtered: "2 of 2", Previous skips the filtered-out invoice). The old one-invoice case is renamed to what it checks. | `WorklistInvoiceReviewModal.test.tsx` |
| View decision | A read-only decision view no longer counts as dirty: `useReportDirty(wizard?.open === true && access === 'decide')`. | `dirtyCheckResponder.test.tsx` |
| Firm chosen | A firm chosen in `SetFirmField` but not yet set counts as dirty (`useReportDirty(chosen !== null)`). | `dirtyCheckResponder.test.tsx` |
| `uiScale` | SKIPPED: `WorklistWidget` has no `uiScale` (no Worklist modal passes one; `InvoiceReviewModal` still accepts the prop). | none |
| Cards | Left as is. Five cards need roughly 5 x 100 px, so beside the toolbar there is no room at modal width in any case; the reviewer's figures (about 103 px a card at 1280 px with the 560 px detail pane, a two-digit "Processing" wrapping to about 78-82 px tall, Lines / By rule / Add line on their own row) are the reviewer's computation and are NOT measured here (no browser). The five stay on one row. Measure on dev. | none |

### Fix round 1: results

| Command | Result |
|---|---|
| UI.Components `npm run build` | clean |
| UI.Components jest: Worklist, SprkModal, RecordNavigationModalShell and `utils/__tests__/themeStorage.test.ts` | 31 suites, 560 tests, all pass |
| InvoiceReview `npx jest` (all) | 23 suites, 708 tests, all pass |
| InvoiceReview `npm run build` | green (surface-owned errors 0; 47 existing shared-library errors, deferred) |
| LegalWorkspace `npm run typecheck` | clean, no new errors |
| AI.Widgets jest `WorklistWorkspaceWidget` | 1 suite, 9 tests, all pass |
| DailyBriefing.Components jest, whole package | 28 suites, 194 tests, all pass |

Seen failing before the fix (source reverted to `fa956e7c76`, tests kept):
- F1: the registration suite did not compile (`worklist.registration.ts:70: TS2305 ... no exported member 'getClientUrl'`), 0 tests ran.
- F2/F3/F4 in UI.Components (`InvoiceReviewModal.tsx`, `WorklistWidget.tsx`, `openInvoiceReview.ts` reverted): 9 failed, 31 passed. The 9: the theme helper and modal cases, two widget theme cases, the new-iframe case, the three focus cases, and the header-count case (it now asserts focus without `hidden: true`). The remaining new cases (Cancel keeps the frame, the dirty check asks the current frame, focus left on Close, the widget "stored dark" URL) pass at base by design or because the base URL helper ignores an extra argument.
- View decision and firm chosen (`DecideInvoiceAction.tsx`, `SetFirmField.tsx` reverted): 2 failed, 10 passed.
- The `themeStorage.test.ts` cases pin existing resolver behaviour that F2 relies on; they pass at base, by design.
- F5 is a test-honesty fix: the filtered-list case passes at base too (the widget already built the set from the filtered list), which is the point; it now proves it.

Test-run notes: (1) DailyBriefing.Components' jest config builds `testMatch` from the absolute path, and a worktree under `.claude\worktrees\...` breaks the glob (`\.claude` is read as an escape), so here it was run with `--testMatch '**/test/**/*.test.ts' '**/test/**/*.test.tsx'`; CI, whose checkout has no dot directory, uses the config as is. (2) A first whole-package run alongside two other jest runs failed 2 cases in `DailyBriefingApp.openRecord` / `itemEmail` (timeouts under load, each passes alone); the run recorded above is the package alone. (3) `npm install` for that package changed its lock file; that change was reverted, not committed.

### Not verified (added in round 1)
- The theme in a real dark app: that the Console frame's `getEffectiveDarkMode()` resolves dark from the model-driven app's navbar one frame up, and that the page then honours `theme=dark`. Check on dev with no stored theme, once with the dark app theme and once with the dark URL flag.
- No history entry per move (Back leaves the modal's page alone), and focus after the end of the set, in a real browser.
- Count-card height and the toolbar row at 1280 wide.
