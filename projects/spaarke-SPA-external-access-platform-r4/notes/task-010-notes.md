# Task 010 notes — Submit invoice modal over the record + Invoices grid toolbar

## Gate: ebilling-r1 contract review (2026-11-13): agree, with changes

- Build from master: `work/ebilling-r1` has no unmerged changes under `InvoiceSubmission/`.

**1. Hosting.**
- Host the wizard with `embedded` inside the child SprkModal.
- The non-embedded form would nest WizardShell's own dialog inside ours, giving two focus traps and two close buttons.
- Close and Escape go to the wizard's `onClose`, so one component owns the dialog.

**2. `lockedRecord`.** It is additive: when the prop is absent, the wizard behaves exactly as today, and submit is unchanged.
- **(a)** Show the fixed message only when the firm's record list is complete and does not include the locked record. If the read was partial, truncated or threw, show today's read message with Retry, Back and Cancel instead.
- **(b)** Match the locked record on kind plus id.
- **(c)** Re-check on every firm change.
- **(d)** If both are set, `lockedRecord` wins over `initialRecord`; say so in the JSDoc.
- **(e)** Step captions read "Step n of 2", or "Step n of 3" when the firm step is shown.
- **(f)** Tests cover a closed set of cases (copied into the POML acceptance criteria).

**3. Refresh.** Once `onSubmitted` has fired, refresh the record's Invoices section on any close, not only on Done.

**4. Grid toolbar.**
- The DataGrid change is agreed between us and the grid owners.
- The firms-non-empty gate on the button is a convenience only; the server refuses a submission with no firm (EB-D-223).
- Send the diff to ebilling-r1 for review, as in task 007.

## Gate: dataset-grid owners: not needed (2026-11-13)

The shared DataGrid already lets a host add its own commands: command bar config items, the handler registry (`commandBar/registry.ts`), `onCommandInvoke`, and the `create-form` + `onCreateNew` override. Task 010 uses that and adds no DataGrid prop. If that turns out not to work, it stops and reports.

## Implementation (2026-11-13, sonnet/high) - Parts 1 and 2 built; Part 3 NOT built (blocked, see below)

### Files
- Shared lib: `src/client/shared/Spaarke.UI.Components/src/components/InvoiceSubmission/InvoiceSubmissionWizard.tsx` (use `git diff -w`: Prettier re-indented the Record step); new test `.../InvoiceSubmission/__tests__/InvoiceSubmissionWizard.lockedRecord.test.tsx` (23 tests).
- SPA: `components/record/actions/SubmitInvoiceAction.tsx` (now a dialog host), `components/record/recordTemplates.ts` (placement), `recordTemplate.types.ts`, `detail/RecordOpener.tsx`, `sections/InvoicesSection.tsx` (comment-only edits).
- SPA tests: `RecordTemplateSubmitInvoice.test.tsx`, `RecordTemplateModal.test.tsx`, `InvoiceSubmitShell.test.tsx`.
- NOT changed: `src/widgets/InvoicesWidget.tsx`, DataGrid, BFF, `.claude/`.

### lockedRecord contract as built
- Same option type and identity as `initialRecord`: `kind` + `recordId` (id compared case-insensitively). Absent: unchanged.
- Present: no Record step and no picker; submit always receives this record. `loadRecords` still decides (per firm): the record must be in the firm's list.
- COMPLETE list (not truncated/partial) lacking it: fixed message `LOCKED_RECORD_MISSING_MESSAGE` ("This record can't take invoices for the selected firm."), testid `locked-record-missing`, submit never called.
- Thrown read, or truncated/partial list lacking it: today's read message with Retry (locked mode only); Back and Cancel stay in the footer; never the fixed message.
- Re-evaluated on every firm change; a stale firm's list never counts (derived check keyed to the current firm).
- Captions: "Step n of 2", or "Step n of 3" with the firm step (also the phone caption).
- `lockedRecord` wins over `initialRecord` (JSDoc).
- Added `hideTitle?: boolean` (embedded only; default false): suppresses the shell title bar and its x so the SprkModal is the single dialog owner. Not in ebilling's review - flag it.

### Decisions
- Gate lives on the first step after the firm step (the list loads per firm, so with a firm step it cannot be earlier).
- Dialog: `SprkModal` size `lg`, `dismiss="explicit"` (Esc/backdrop close neither dialog nor record), `padded={false}`; x and Cancel go to the wizard's `onClose`; wizard mounted only while open.
- Refresh: after `onSubmitted`, any close calls `refreshSection('invoices')`; a late accept after a mid-flight close refreshes at once. Focus returns via `useChildDialog`.
- Placement: Matter and Project list `submitInvoice` under `sectionActions.invoices`. DEVIATION: Work assignment has no Invoices section (no `invoicesBy`), so it keeps Submit invoice in its header toolbar. Visibility rule unchanged.
- `onNavigate` plumbing (RecordOpener / types) now has no consumer; left in place (removal is a separate cleanup).

### Tests
- Shared lib Jest, InvoiceSubmission: 6 suites, 108 passed. New wizard file fails against the old wizard (20 failed, 1 control passed of the 21 then written).
- SPA `npx vitest run`: 55 files, 1494 passed, 0 failed. Old SubmitInvoiceAction + recordTemplates against the new tests: 13 failed / 72 passed (2 files).
- SPA typecheck, lint, Prettier clean. Pre-existing, not mine: a readonly-tuple `it.each` tsc error in `RecordTemplateSubmitInvoice.test.tsx` appears only under a tsconfig that includes tests.
- Not verified: phone-width layout (jsdom), any browser check.

### Part 3 (Invoices grid toolbar command) - BLOCKED, reported, nothing changed
- The command bar's items come only from the Dataverse `sprk_gridconfiguration` `commandBar.primary/secondary` (live data; Invoices grid `3ff4102c-1092-f111-b8dc-7ced8ddc4a05`, last known `showDefaultCommands.newRecord:false`) plus built-in defaults gated by `showDefaultCommands`.
- A `custom` item needs a `customHandlerId` registered via `registerCommandHandler`, but only an item that exists in the config renders; the SPA cannot add one without editing the live config. `create-form` + `onCreateNew` overrides the built-in New, which this config hides.
- So the existing mechanism cannot add a host command without a live config change (outside the SPA) or a DataGrid change (the brief forbids a new prop/slot).
- Options: A) generic additive DataGrid prop for host commands (needs dataset-grid owner agreement; recommended); B) edit the Invoices grid config in Dataverse to add a custom command plus a handler registered by the SPA/widget; C) leave the widget's own button.
- `InvoicesWidget` keeps its own Submit invoice button; the page path (`InvoiceSubmitPage`, `?invoiceRecord=`) is untouched and still works.

### For ebilling-r1's diff review
`git diff -w` on the wizard; points to check: `hideTitle`, the Retry button, the derived `lockedCheck`, the case-insensitive id match, `LOCKED_RECORD_MISSING_MESSAGE` export.

## Review round 1 (2026-11-13, sonnet) - F1 x2, F2 fixed; part 3 still waits on the owner (not built)

**F1 (wizard): a `firm_required` refusal's notice was invisible with `lockedRecord`.** With no Record step, the server's `notice` (set when the firms reload finds no choice to make) had no step to render in. `InvoiceSubmissionWizard` now renders it in the File step, above the gate/upload, when `lockedRecord && notice && !needsFirmStep` (MessageBar `warning`, `role="alert"`, testid `locked-record-notice`). With several firms the notice stays on the firm step as before; without `lockedRecord` nothing changed. Tests (3, in `InvoiceSubmissionWizard.lockedRecord.test.tsx`): the notice shows after a locked `firm_required`; must-not-fire twins: no refusal means no bar, and another refusal (403) stays on Details and never shows the File-step notice. Fail-before: the show test fails with the block disabled.

**F1 (SPA host): the SprkModal x closed the dialog while submit was in flight, hiding a 4xx/5xx.** `SubmitInvoiceAction` wraps `submit` (`trackedSubmit`): an `inFlight` ref plus `onBusyChange(true/false)` (the record's existing hold: its Close, browsing and Refresh wait too). `handleClose` ignores every close while in flight (x and the wizard's Cancel); the host clears busy on unmount. The late-accept refresh in `handleSubmitted` stays as a safety net. Not reachable through the x any more, so the old "closed mid-flight, accepted later" test is replaced by two: x in flight does not close, and a 400 after it is shown in the wizard (still open, no refresh, x closes it once settled); x in flight, then accepted, shows the result and Done closes and refreshes once. Fail-before: both fail with the old `submit={submitInvoice}`. WizardShell is untouched (its own title-bar x has the same gap on the page path; the coordinator raises it with ebilling).

**F2: dead navigation plumbing removed.** `RecordActionHostContext.onNavigate` / `requestClose` (types and construction), the `RecordTemplateModal` `onNavigate` prop, `RecordOpenerProvider`'s `onNavigate`, `GridWidgetBody`'s pass-through (and its now-unused destructure), `RouterRecordOpener.tsx` (deleted with `git rm`: it had no other job; App.tsx mounts `RecordOpenerProvider` directly, as before task 007), and `invoiceSubmitPathFor` (production-unused). Kept: `INVOICE_SUBMIT_PATH`, `INVOICE_RECORD_PARAM`, `parseInvoiceRecordParam`, the page and `?invoiceRecord=` (a saved address still works); `InvoicesWidget` navigates with its own `onNavigate` prop from `WorkspaceHomePage`, a different prop that is untouched. Tests adjusted: shell test mounts `RecordOpenerProvider`; the App.tsx source pin now asserts the plain provider and no `RouterRecordOpener`; the link-format test parses a literal address; the "no onNavigate still offered" test and the `onNavigate` mocks are gone. Stale comments fixed in `RecordTemplateModal.tsx`, `recordTemplate.types.ts`, `invoiceSubmitLink.ts`, `InvoiceSubmitPage.tsx`, `RecordTemplateAccess.test.tsx`. No router imports added; `BrowserRouter` stays only in App.tsx.
