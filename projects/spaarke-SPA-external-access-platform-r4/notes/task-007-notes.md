# Task 007 — "Submit invoice" record action (FR-10)

- **Rigor:** FULL (modifies .tsx). **Model:** Sonnet @ high (POML `<model-tier>`).

## Step 1 — coordination gate (record preselect)

**Proposal** (spa-r4 → ebilling-r1, 2026-11-11): an additive optional `?record={kind}:{id}` on `/invoices/submit`
(kind `matter` | `project` | `workassignment`, id a GUID). `?submissionId=` keeps winning; malformed input is ignored.
`InvoiceSubmitPage` takes the option from its own `loadRecords` result (matching kind + recordId) and passes the
wizard's existing optional `initialRecord` prop (ebilling task 052, `InvoiceSubmissionWizard.tsx:148-152`). No name
travels in the URL; a record the caller can't submit for is simply not preselected.

**ebilling-r1's answer (2026-11-11):**
- (a) Agreed, matching stays in the page, not the wizard. Addition: for a caller with several firms the "Choose firm"
  step (their task 051) comes BEFORE the Record step and the record options are per firm (their D-106). Never
  auto-pick a firm from the deep link; preselect the record only once the chosen firm's `loadRecords` result contains
  it. If the caller picks a firm whose list doesn't contain it, nothing is preselected and the Record step starts
  empty. A single-firm caller goes straight to the preselected record.
- (b) **R4 implements** the page change (`InvoiceSubmitPage.tsx` + `InvoiceSubmitPage.test.tsx`) under ebilling-r1's
  review; R4 sends them the diff. Tests they asked for: malformed input, not-in-list, `submissionId` winning,
  multi-firm.
- (c) No conflict with their open work (116 PR to master, task 084 RecordProcedures code page, task 134's
  `record/sections/OutcomeSection.tsx` + `sprk_invoice` template entry). They confirmed the template visibility rule
  (`/me` firms non-empty AND the firm-records answer lists this record; partial/truncated without it = hidden).

**Owner confirmation (2026-11-12):** approved — R4 builds the page change under ebilling-r1's review, then the action.

> **Rename note (fix round 1, 2026-11-12):** the parameter agreed above as `?record=` is now `?invoiceRecord=` (see "Fix round 1", F1). The rename was pending ebilling-r1's re-confirmation when this note was first written; ebilling-r1 has since confirmed it (relayed by the main session): approved, and their approval carries over on condition of the shell-level test and the both-parameters twin (both added, `tests/InvoiceSubmitShell.test.tsx`). Wherever the sections below say `?record=` for the invoice page, read `?invoiceRecord=`.

## Part 1 — `InvoiceSubmitPage` `?record=` (ebilling-owned file; for ebilling-r1's review)

Files: `src/pages/InvoiceSubmitPage.tsx`, `tests/InvoiceSubmitPage.test.tsx` (additive; the wizard, the shared lib, routes and
server calls are untouched). Diff for review: `git diff -- src/client/external-spa/src/pages/InvoiceSubmitPage.tsx src/client/external-spa/tests/InvoiceSubmitPage.test.tsx`.

- **Parser.** `parseInvoiceRecordParam('{kind}:{guid}')`: kind exactly `matter` | `project` | `workassignment`, id a GUID
  (case-insensitive, the page's `GUID_PATTERN`); anything else is `null` = ignored. `invoiceSubmitPathFor(kind, id)` builds
  `/invoices/submit?record=` + `encodeURIComponent('{kind}:{id}')` (the colon is `%3A`); both live in the page so the token
  and its reader stay together. `?submissionId=` (valid or not) wins: the preselect is not even started then.
- **Why the page reads ahead.** The wizard seeds `useState(initialRecord ?? null)` once, at mount, and a full option (name,
  number) exists only in a `loadRecords` result. So for a valid link `usePreselectedRecord` reads first and mounts the wizard
  after, with a "Loading your records" spinner meanwhile. No wizard change was needed.
- **How the multi-firm rule holds.**
  1. `listInvoiceFirms()` (the wizard's own `loadFirms`), then `listInvoiceCoreRecords` for the one firm of a single-firm caller
     (unnarrowed `{}` when the firms are unknown/none, exactly as the wizard asks), or for EVERY firm of a several-firm caller.
  2. The option is the first one in those lists with the link's kind + id (names/numbers do not depend on the firm).
  3. The wizard is mounted with that `initialRecord`. Its own Record effect drops a chosen record that the loaded list does not
     offer (`setSelected(prev => offered ? prev : null)`), and does nothing while a several-firm caller has not chosen a firm. So:
     the link never picks a firm (the firm step stays first, nothing in it is selected); choosing a firm whose list has the record
     shows it chosen; choosing a firm whose list lacks it starts the Record step empty. A single-firm caller lands on the Record
     step with it chosen.
  4. Each read is handed to the wizard's FIRST identical call and dropped (one-shot map keyed by firm), so no list is read twice;
     a failed read is not kept (the wizard asks again live and shows the failure itself). Later calls (a firm change, the
     `firm_required` reload) are live.
- **Known limit (no wizard change allowed).** Reading every firm's list costs N record reads up front for a several-firm
  caller (typically 2-3; each is the firm-records route plus the three record lists). And because the wizard drops a
  non-offered choice for good, a caller who picks firm B (record absent) and then goes Back to pick firm A (record present)
  gets no preselect the second time. Both are inside the agreed contract ("nothing preselected when the chosen firm's list
  doesn't contain it"); a wizard-side `initialRecord` that re-applies per firm would remove the second.
- **`web-api-client.ts`.** The one inline firm-records call in `listInvoiceCoreRecords` is now `readInvoiceFirmRecords(firmId)`
  (exported, with `InvoiceFirmRecordsResponse`) so the record view reads the same route the same way; behaviour of
  `listInvoiceCoreRecords` is unchanged (same GUID check, same error mapping; the page tests pass unchanged).
- **Tests added** (`InvoiceSubmitPage.test.tsx`, "?record= preselect"): single firm chosen + no list read twice; GUID case +
  the submit really targets the preselected record's route; project link; 7 malformed links ignored (each with the unchanged
  empty start); parser table (3 kinds, 12 rejects, builder round trip); record not on the list / unknown GUID; failed read-ahead
  is not kept; `submissionId` wins (no wizard, no reads); multi-firm: firm step first and nothing selected, chosen firm
  lists it -> chosen, chosen firm lacks it -> empty, only the second firm lists it -> chosen for that firm only.

## Part 2 — the record-view action

- **Where.** `record/actions/SubmitInvoiceAction.tsx` is an action host (registered in `actions/index.ts` under `submitInvoice`),
  as Download/Delete are, because it needs a read and a visibility decision. It renders nothing and offers a RecordCommandBar
  descriptor through `onActionChange` (icon `RECORD_ACTION_ICONS.submitInvoice`, label/tooltip from `RECORD_ACTION_TEXT`).
  `recordTemplates.ts` needed no change: task 003 already lists `submitInvoice` in the shared `ROOT_TOOLBAR` of exactly Project,
  Matter and Work assignment (a test pins that no other template lists it). The `RecordTemplateModal` header comment is updated.
- **Rule** (`recordAccess.ts`: `invoiceKindOfEntity`, `callerFirmIdsOf`, `submitInvoiceRightOf`): `/me` `firms` a non-empty
  array AND for at least one firm the firm-records answer lists the record id under its kind (case-insensitive). Absent/null/
  empty/failed `/me`, no `onNavigate`, an unanswered read, a failed read, or a non-listing answer = hidden.
- **`partial` / `truncated` (escalation 2).** The flags are NOT consulted. A record that IS listed is linked, so it is offered
  even if the answer is flagged; a flagged answer that does NOT list it is hidden (never shown on `firms` alone). Both are tested.
  A failed firm hides only itself (another firm that lists the record still offers it).
- **Cache choice.** One `GET firm-records` per firm (the route is one bounded read, not paged, so paging steps do not apply),
  cached for the opening of the record modal: the promise map is a `WeakMap` keyed by the opening's `/me` object
  (`useCallerContext` reads it once per modal instance, and both openers mount a new instance per opening). Browsing to the next
  record in the same opening reads nothing more (host remounts per record, reuses the cached promises); reopening reads again.
  A failed read stays failed for the opening. Chosen over a prop-drilled cache so `RecordViewContext` and the modal gain nothing
  for it. Reads start only when the config lists the action, `/me` has firms and `onNavigate` is present.
- **Navigation.** `RecordTemplateModal` gets an optional `onNavigate` and hands it (with a guarded `requestClose`) to action hosts.
  `RecordOpenerProvider` takes `onNavigate`; `App.tsx` has a tiny `RouterRecordOpener` (inside `BrowserRouter`, outside `export
  const App`'s JSX shape the arch guard reads) giving it the router's `navigate`. Click: `requestClose()` (refused while an
  operation holds the view) then `navigate(invoiceSubmitPathFor(kind, id))` on the next task, so the dialog is closed before a
  route change can unmount a grid widget that mounted it (#2658). No template/opener/grid file imports `react-router*` (grep test).
- **Deviation: `GridWidgetBody.tsx` (not in the POML's file list).** Records opened from a grid row (the main way to open a
  matter/project/WA) use `GridWidgetBody`'s own `RecordTemplateModal`, not the opener's, so without passing the widget's
  `onNavigate` (already handed by `WorkspaceHomePage`) to it the action would never appear for a grid-opened record. One prop.

## Verification

`npm run typecheck` clean, `npm run lint` clean, full `npx vitest run` 53 files / 1501 tests pass (2026-11-12; the machine was
loaded: an earlier run hit 60 s load timeouts in the page file while sources were being edited, and the one pre-existing test
that failed there passed alone and in the later full run). New: 34 tests in `RecordTemplateSubmitInvoice.test.tsx`, 26 added to
`InvoiceSubmitPage.test.tsx` (52 -> 78 incl. the parser table). Mutation checks (each applied, the named tests failed, then
restored; no marker left in the tree): rule treats a failed read as listed (failed-read test); action ignores `onNavigate`
(no-onNavigate test); hold guard removed from `requestClose` (busy test); per-opening cache removed (reads tests); rule
ignores the listing (not-listed, truncated/partial, browse-neighbour tests); `submitInvoice` added to the Invoice toolbar
(invoice-absent + config-listing tests); `some` -> `every` over the firms (all four /me-hidden tests + multi-firm); page:
`submissionId` guard removed (submissionId-wins test); only the first firm read (both multi-firm tests); `initialRecord` not
passed (5 preselect tests). Not run: the POML's browser UI tests (spa-dev, dark mode) and the C#
arch tests (`ExternalSpaGridViewSelectorGuardTests` reads `App.tsx`; the change keeps the `App` return shape).

## ebilling-r1 review of Part 1 (2026-11-12)

**APPROVED as diffed.** Checked: parser/builder a closed set (three kinds, GUID only, `submissionId` wins); no name in
the URL and the option only from the caller's own `loadRecords` (server re-checks Read/Create on submit); read-ahead
keyed on `firmId` alone (`InvoiceRecordQuery` has only `firmId`); rejected reads removed before `ready`, so the wizard
never receives a rejected promise and no unhandled rejection; spinner branch after the availability and `submissionId`
returns; `readInvoiceFirmRecords` a pure extraction; the 26 tests cover the four requested cases with not-preselected
twins and no-double-read counts.

- Known limits **accepted**: (1) one record read per firm up front for a multi-firm caller (fine at 2-3 firms; if a caller
  can ever have many firms, consider skipping the read-ahead above a small number); (2) firm B then Back to A loses the
  preselect (wizard `initialRecord` contract not reopened).
- K2 (their note, not blocking): read-ahead entries for firms the wizard never asks about stay in the ref; a later Back +
  pick of such a firm gets the page-load list, not a fresh read. At most minutes old; the server re-checks on submit, so
  it fails closed. Left as is.

> **Rename note (fix round 1, 2026-11-12):** the parameter agreed above as `?record=` is now `?invoiceRecord=` (see "Fix round 1", F1). The rename was pending ebilling-r1's re-confirmation when this note was first written; ebilling-r1 has since confirmed it (relayed by the main session): approved, and their approval carries over on condition of the shell-level test and the both-parameters twin (both added, `tests/InvoiceSubmitShell.test.tsx`). Wherever the sections below say `?record=` for the invoice page, read `?invoiceRecord=`.

## Fix round 1 (2026-11-12)

All paths under `src/client/external-spa/`. Review findings F1, F3, #2658 and one K note.

**F1: param collision.** `INVOICE_RECORD_PARAM` was `record`, the app-wide deep link `RecordDeepLink` reads on every route: landing on
`/invoices/submit?record=matter%3A{id}` reopened the matter modal over the page, then `strip()` dropped the param and the preselect was
lost. Renamed to `invoiceRecord` (`/invoices/submit?invoiceRecord={kind}:{id}`). `RecordDeepLink` / `record-targets.ts` untouched. All
page tests, the action's expected path and the comments follow the new name. Re-confirmation: ebilling-r1 approved the rename on
condition of the shell-level test and the both-parameters twin (below).

**F3: router-free link module.** New `src/pages/invoiceSubmitLink.ts` (its only import is an `import type` of the kind union; no router,
no wizard at runtime): `GUID_PATTERN`, `INVOICE_SUBMIT_PATH`, `INVOICE_RECORD_PARAM`, `InvoiceRecordRef`, `parseInvoiceRecordParam`,
`invoiceSubmitPathFor`. `InvoiceSubmitPage.tsx`, `App.tsx` (route path), `InvoicesWidget.tsx` and `SubmitInvoiceAction.tsx` import from it;
no re-exports from the page (the tests' imports were updated instead). Before this the action and the widget imported the page, which
pulled in react-router and the shared wizard. Tests (`RecordTemplateSubmitInvoice.test.tsx`, "imports no router"): `SubmitInvoiceAction.tsx`
imports `../../../pages/invoiceSubmitLink` and none of its imports names `pages/InvoiceSubmitPage` or
`ui-components/components/InvoiceSubmission`; the link module has only `import type` imports (so no router and nothing at runtime).

**F3: shell-level tests** (`tests/InvoiceSubmitShell.test.tsx`, 3 tests). One MemoryRouter holds the real `RecordDeepLink`, the real
`RecordOpenerProvider` given the router's `navigate` (a test-local copy of App.tsx's `RouterRecordOpener`, which App.tsx does not export;
the copy avoids touching App.tsx, which an arch guard reads), the real `RecordTemplateModal`, and the `/invoices/submit` route with the
real `InvoiceSubmitPage` + shared wizard. Network seam is the same as `InvoiceSubmitPage.test.tsx`: a URL-routed fake `fetch` plus a fake
token acquirer.
1. `/invoices/submit?invoiceRecord=matter:{id}`: no dialog, the matter radio chosen and Next enabled, the link still in the address, and
   the record view made no single-record read.
2. **Twin (ebilling-r1's ask): both params**, `?record=matter:{A}&invoiceRecord=matter:{B}` (A is a matter that is not on the firm's
   list). `record` does its own job: A's record dialog opens (heading "Deep-linked matter") and `record=` is stripped from the address
   once it settles. `invoiceRecord` does its own: still in the address after the strip, the wizard has B chosen (queried through the
   dialog's aria-hidden with `hidden: true`), and after closing the dialog B is still chosen with Next enabled. The page's read-ahead ran
   once (one `projects` read, one wizard matter-list read), so the strip neither dropped `invoiceRecord` nor cancelled or redid the
   read-ahead. Finding: nothing to fix on the page side. `withoutRecordParam` re-serialises the remaining params with `URLSearchParams`
   (the colon becomes `%3A`), which the page's parser reads back identically; the page's read-ahead effect depends on `linkKind` /
   `linkId` / `enabled`, not on `location.search`, so the strip does not restart it.
3. Full click path: from a launcher, open the matter record view (real modal), click "Submit invoice" (shown once `/me` firms and the
   firm-records read list the matter), land on `/invoices/submit?invoiceRecord=...` (not `record=`) with the matter chosen and no dialog
   open (also after a settle: the deep link opened nothing).

Mutation checks (each applied, the named tests failed, then restored; no marker left in the tree):
- `INVOICE_RECORD_PARAM` back to `'record'`: all three shell tests fail (matter not chosen / address has no `invoiceRecord`), i.e. the
  original F1 collision is caught.
- `withoutRecordParam` also deletes `invoiceRecord` (temporary edit to `record-targets.ts`, restored): the twin fails
  (`expected '/invoices/submit' to contain 'invoiceRecord='`); the other two pass, as they should.
- `SubmitInvoiceAction` imports `pages/InvoiceSubmitPage` alongside the link import: the new action-import test fails on that specifier;
  replacing the link import with the page import fails the same test on its `toContain` line.
- The link module gains `import { useNavigate } from 'react-router-dom'`: the link-module test fails.

While writing the shell test, the first version's single-record-read filter matched on the JSON body without unescaping it, so it could
never match; fixed (`fetchXmlOf` parses the body first) and that assertion is now a real check.

**#2658: aria-hidden after navigating away from a grid-opened record. Left as is; unverified in a browser.** The repo only has the
keep-mounted pattern (`open={false}`, never unmounted while open: `RecordOpener.tsx`, `GridWidgetBody.tsx:101`, `QuickStartPane.tsx`, and
the ArchTest `ModalUnmountedWhileOpenGuardTests`). There is no "navigate after the dialog has closed" helper, and `SprkModal` (the shared
envelope) does not expose Fluent's `surfaceMotion` / motion-finish callback, so waiting for the real end of the exit motion means a change
in the shared library or a new polling mechanism, which is not small. Current behaviour: `requestClose()` commits `open=false`, then
`setTimeout(navigate, 0)`, so the route change can unmount `GridWidgetBody`'s modal while the exit motion is still running.
**Task 018 (browser check on spa-dev) must verify:** open a matter, project and work assignment from a grid row (not from a deep link),
click Submit invoice, and on the invoice page confirm (a) the page is interactive: no element under `body` carries a stuck
`aria-hidden="true"` (inspect `document.body.children` and `#root`), focus is not trapped, Tab and click reach the wizard; (b) the same
after Back to the workspace; (c) the same when the record was opened from the deep link and from a notification. If (a) fails, the fix is
to navigate from the modal's close-complete signal (needs `SprkModal` to forward Fluent `surfaceMotion.onMotionFinish`) or to keep the
modal mounted across the route change.

**K note.** Under `React.StrictMode` (dev builds only) effects run twice, so the invoice page's read-ahead (`usePreselectedRecord`) runs
twice in development: the first run is cancelled and its result dropped. Production reads once. The tests do not mount under StrictMode.

**Verification (fix round 1).** `npm run typecheck` clean, `npm run lint` clean, full `npx vitest run` 54 files / 1506 tests pass (was
53 / 1501). Not run: the C# arch tests (only the import line in `App.tsx` changed; the `App` return shape is untouched) and the browser
checks above. The ebilling review diff was regenerated at
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\28c5934d31e00b51\a41deb45-98e1-456a-a086-b75eba475e4f\scratchpad\task007-invoice-submit-page.diff`.

## Reviews (main session)

- **Adversarial pass (Opus):** F1 `?record=` collided with the app-wide `RecordDeepLink` (the matter reopened over the page
  and the strip dropped the preselect) → renamed to `?invoiceRecord=` with ebilling-r1's re-confirmation (fix round 1);
  F3 no shell-level test → `tests/InvoiceSubmitShell.test.tsx` (3 tests incl. ebilling-r1's both-params twin);
  F3 action pulled the page module (router) into the template graph → router-free `pages/invoiceSubmitLink.ts`;
  K (unverified) #2658 aria-hidden after navigating away from a grid-opened record → browser check in task 018 (fix
  round 1 lists the steps); K StrictMode double read (dev only).
- **Scoped re-check of fix round 1 (Sonnet):** F3 the shell test ran a COPY of App's `RouterRecordOpener`, and no test
  renders `App`, so reverting App's opener to a bare `RecordOpenerProvider` (action hidden everywhere) passed every test →
  fixed in the main session: the bridge moved to `components/detail/RouterRecordOpener.tsx`, imported by `App.tsx` and
  the shell test; a source pin in `RecordTemplateSubmitInvoice.test.tsx` asserts App mounts it and has no bare
  `<RecordOpenerProvider` (mutation: bare provider in App → the pin fails; restored). K1 the import scans are regex-only
  (dynamic `import()` / `export … from` would slip; none exist); K2 the shell tests' "no dialog after settle" uses a
  fixed 150 ms settle (false pass only; mutation 1 fails three ways independently).
- **ebilling-r1:** approved Part 1 (incl. both known limits) and the rename, conditional on the shell test + twin (done).

rework: fix-round-1 task 007

## ebilling-r1 verification at b2b519acff (2026-11-12)

Approval stands. They ran `InvoiceSubmitShell` + `InvoiceSubmitPage` at the head (68/68). Confirmed: `parseRecordLink`
reads the exact key `record` (`getAll('record')`), so `invoiceRecord` is never a deep link; the link survives sign-in
(`captureReturnTo` keeps pathname+search); one producer (`SubmitInvoiceAction`). Known limit (no change): inside Teams
only `record=` deep links are restored, so a Teams sub-page link to `/invoices/submit?invoiceRecord=` would not be — no
producer emits one; an emailed/Teams "submit an invoice for X" link would need that restore.
