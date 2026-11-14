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

## ebilling review fixes (2026-11-13, sonnet) - ebilling-r1 PASS with changes: F1-F4 applied

**F1 (200-row cap dead end).** `SubmitInvoiceAction` no longer passes `listInvoiceCoreRecords` (caps each kind at 200) to the locked wizard. Its `loadRecords` reads `readInvoiceFirmRecords(firmId)` (the button's own source, uncapped) and returns `[lockedRecord]` when that firm's list names it (kind + id, case-insensitive), else `[]`, carrying the answer's `truncated` / `partial`; so the wizard shows the fixed message only for a complete answer and the read message (with Retry) for a cut-short one. No firm in the query (firms unknown) falls back to `listInvoiceCoreRecords({})`. Still per firm; every call reads afresh so Retry can recover (the opening's cached read is for the button only). The kind-to-list map is a 3-line local copy of `recordAccess.ts`'s (not exported; that file belongs to task 011 now). Tests (`RecordTemplateSubmitInvoice.test.tsx`): record past the cap (core list truncated and lacking it) but in the firm records goes on, core list never called; a record the wizard's firm answer lacks gives the fixed message; truncated / partial answers lacking it give the read message with Retry and no fixed message; a truncated answer that lists it is enough; unknown firms fall back to the core list with `{}`. Fail-before: 5 of those tests fail with `loadRecords={listInvoiceCoreRecords}`. The old fixed-message test faked the core list; it now fakes the wizard-time firm answer (`openWizard({ wizardAnswer })`).

**F2 (title-bar x during submit, page path).** `InvoiceSubmissionWizard` keeps a `submittingRef` (true before `submit`, false in `finally`) and gives WizardShell `onClose={handleShellClose}`, which ignores the close while it is set. The success screen's Done keeps the raw `onClose`. WizardShell untouched. Tests in `InvoiceSubmissionWizard.test.tsx`: x during a pending submit does not call `onClose`, the following refusal shows, and the x works once settled (fails with `onClose={onClose}`); twin: Done after success still closes.

**F3.** `recordsFirmKey` kept as defence in depth, with a comment saying a mutation removing its guard is unobservable (the records effect already clears the held answer on a firm change) and that no test is claimed to cover it; the earlier note that the firm-change tests cover "2c" through it is withdrawn: they cover the behaviour, not that guard. The "phone width" test is deleted (it asserted the same caption as the no-firm test; jsdom cannot see the CSS).

**F4 (focus after Retry).** The locked-record gate now renders in an always-mounted container (`tabIndex=-1`, testid `locked-record-gate`, outline off); the Retry click focuses it before the button is replaced by "Checking this record", so focus stays inside the step through the check and its answer (it does not fall to `body`). Test: focus is on the container while checking and after the answer; fails without the focus call. Side effect: in locked mode an empty container adds one flex gap of space when there is nothing to show; not browser-checked.

## ebilling-r1 re-check (2026-11-13): PASS on 9b10699efe

ebilling-r1 re-checked the fix lines and found nothing new:
- **F1:** both reads use the same uncapped source; the record matches on kind plus a case-insensitive id; `truncated`/`partial` pass through; with no firm, falling back to the capped list is acceptable.
- **F2:** `submittingRef` is cleared before `onSubmitted`.
- **F3, F4:** OK.

**Merge.** Allowed on our gates. Send ebilling-r1 the master merge SHA when it lands; they record it against their task 095 notes.

**Open.** Part 3 (the Invoices grid toolbar command) is still waiting on the owner's choice.

## Part 3 — grid host command (2026-11-13, sonnet/high): BUILT, owner approved option A

Owner approved (2026-11-13, on behalf of the dataset-grid owners, who have no active session) a small generic additive DataGrid option for host-supplied toolbar commands. This replaces the earlier "dataset-grid owners: not needed" gate note above (the existing mechanism could not add a command without a live config edit).

### Prop API as built
- `DataGrid` prop `hostCommands?: ReadonlyArray<DataGridHostCommand>`, threaded DataGrid -> `CommandBar` (`hostCommands` prop on `CommandBarProps` too).
- `DataGridHostCommand` = `{ id: string; label: string; icon?: React.ReactElement; onInvoke: () => void | Promise<void>; disabled?: boolean }`. Exported from `components/DataGrid/index.ts` (and so from the components barrel). Defined in `commandBar/CommandBar.tsx`.
- Generic: no entity names, no Xrm, no data access. Absent or `[]`: `buildEffectiveItems` returns today's list untouched.

### Placement and collision rules (fixed, documented in the type's JSDoc and in the architecture doc)
- Host commands come FIRST in the effective list, in the order supplied; then the ordinary `inlineLimit` rule (the grid mounts the bar with `inlineLimit={3}`; the rest go to the `...` menu).
- A host `id` equal to a configured or built-in item's `id` REPLACES that item (host behaviour wins; never two buttons for one id). Duplicate host ids: first wins.
- Subtle buttons only (the "no primary CTA" convention in CommandBar.tsx is kept; the doc does not let hosts override it). The widget's old button was `primary`; the new one is subtle, like every grid command.
- `onCommandInvoke` fires first as an observer; no built-in handler runs; a throwing `onInvoke` is logged, not rethrown. `disabled` honoured.

### Files
- Shared lib: `DataGrid/commandBar/CommandBar.tsx` (type, prop, merge in `buildEffectiveItems`, invoke/disabled/icon branches), `DataGrid/DataGrid.tsx` (prop + pass-through; no `props.x` access, the external-host guard's count is unchanged), `DataGrid/index.ts` (type export).
- Tests (new): `DataGrid/commandBar/__tests__/CommandBar.hostCommands.test.tsx` (12), `DataGrid/__tests__/DataGrid.hostCommands.test.tsx` (2).
- Docs: `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` new "Host commands" subsection after "Host filters".
- SPA: `src/widgets/GridWidgetBody.tsx` (`createGridWidgetBody` now returns `ComponentType<GridWidgetBodyProps>`; optional `hostCommands` forwarded to `<ExternalDataGrid>`; types `GridHostCommands` / `GridWidgetBodyProps` read off the wrapper's props, so no import of the shared grid), `src/widgets/InvoicesWidget.tsx` (standalone button, its styles and the `Button` import removed; a memoised `hostCommands` of one command `submit-invoice` / "Submit invoice" / `AddRegular`, only when `canSubmit` = `onNavigate` present and `me.firms` non-empty; invoking calls `onNavigate(INVOICE_SUBMIT_PATH)` as before). `tests/InvoiceSubmitPage.test.tsx` (grid stub now renders the host commands it is handed inside the grid element; tests updated).
- `ExternalDataGrid.tsx` NOT touched (its text is pinned by `ExternalSpaGridViewSelectorGuardTests`; its `Omit<DataGridProps,'showViewSelector'>` picks the new prop up).
- Not touched: `components/record/**`, `recordAccess.ts`, EventsCalendar, TasksEventsSection, ProjectPage (task 011), BFF, `.claude/`.

### Gating
Unchanged: shown only with `onNavigate` and a non-empty `/me` `firms`. Convenience only; the server refuses a no-firm submission (EB-D-223). Still one `/me` read on mount, no list read.

### Verification
- Shared lib Jest: `src/components/DataGrid` + `InvoiceSubmission` + `RecordCommandBar`: 20 suites, 249 passed. New tests against the old CommandBar/DataGrid: 11 failed, 3 passed (the 3 are the absent-prop controls). Shared lib `tsc --noEmit`: clean; eslint 0 errors on the changed files; Prettier clean.
- SPA: `npm run typecheck` clean, `npm run lint` clean, `npx vitest run` 56 files, 1626 tests, all pass (one `DocumentUploadPage.externalUpload` test failed once while a dotnet build ran at the same time, passed alone: 25/25; unrelated). The widget "toolbar command" test fails against the old `InvoicesWidget` (standalone button outside the grid); the "no command when firms null/absent/empty/failed /me, no onNavigate" tests are controls.
- Arch guard `dotnet test --filter ExternalSpaGridViewSelectorGuard` (Spaarke.ArchTests): passed (DataGrid.tsx pins unchanged).
- Not verified: real-browser look of the toolbar (inline order with the live grid config's built-ins, icon size next to the 20px built-in icons); jsdom only. Live config `showDefaultCommands` for the Invoices grid not re-read.

### Other DataGrid consumers checked (additive optional prop; no change needed)
`src/solutions/{EventsPage,LegalWorkspace (matters/projects/documents/invoices/workAssignments registrations),RuleReview,SpeAdminApp,sprk_communicationspage,sprk_invoicespage,sprk_kpiassessmentspage}`, `src/client/pcf/SemanticSearchControl`, `src/client/shared/{Spaarke.AI.Widgets,Spaarke.Communication.Components,Spaarke.Events.Components}`: none pass `hostCommands`; the bar is built exactly as before for them. Other `createGridWidgetBody` callers (Documents, Matters, Projects, ServiceRequests widgets, registry) are unchanged: the returned type is assignable to `WidgetBodyComponent`.

### For ebilling-r1 / dataset-grid owners (FYI diff review)
`git diff` on `DataGrid/commandBar/CommandBar.tsx` and `DataGrid/DataGrid.tsx`; points: the replace-on-collision rule, host-first placement, `hostCommand` carried on an internal `EffectiveItem` (the config schema `CommandBarItem` is unchanged).

## Part 3 review round (2026-11-13, sonnet)

Scoped review of part 3 found two items; both fixed in the working tree (uncommitted).

**F2: the `GridWidgetBody` -> `<ExternalDataGrid hostCommands>` forward was untested** (`InvoiceSubmitPage.test` mocks `createGridWidgetBody` wholesale). New `tests/GridWidgetBody.hostCommands.test.tsx` (2 tests) renders the REAL `createGridWidgetBody(id)` with `../src/widgets/ExternalDataGrid` mocked (RecordTemplateSections pattern): the mocked grid receives the same `hostCommands` array when passed (and the config id and a row-open callback), and none when not passed (control). Fail-before: deleting the `hostCommands={hostCommands}` line fails the forward test (1 failed, 1 passed).

**K1 decided: a host command never replaces a configured or built-in item** (supersedes the "replace on collision" rule written in the Part 3 section above; a built-in such as `delete-selected` would lose its confirm + selection gating). `CommandBar.tsx` `buildEffectiveItems`: a host command whose `id` equals a configured item's id, or ANY built-in action id (`refresh`, `delete-selected`, `create-form`, `export-excel`, `edit-columns`, `edit-filters`, whether or not that built-in is shown, so a host cannot impersonate a hidden one either), is SKIPPED (not rendered) and `console.warn` names the id. Duplicate host ids: first wins (kept). Placement (host first) unchanged. JSDoc (`DataGridHostCommand`, `DataGrid.hostCommands`) and the "Host commands" section of `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` updated.
- Tests (`CommandBar.hostCommands.test.tsx`, now 14): configured-id collision keeps the configured item, which still runs, host skipped, warn names the id; built-in `refresh` collision keeps Refresh, which still calls the grid refresh; collision with a HIDDEN built-in (`delete-selected` with delete off) is skipped; a colliding command does not hide a valid one beside it, and a no-collision bar does not warn; duplicate ids first wins. The 4 collision tests fail against the previous replace-on-collision `CommandBar` (4 failed, 36 passed).

### Results
- Shared lib `npx jest src/components/DataGrid`: 13 suites, 124 passed (was 122). `tsc --noEmit` clean; eslint 0 errors; Prettier clean on the changed files.
- SPA: `npm run typecheck` clean, `npm run lint` clean, `npx vitest run` 57 files, 1628 tests, all pass (no load timeouts this run). Arch guard (`ExternalSpaGridViewSelectorGuard`) passed on the round-1 DataGrid.tsx; this round changed only a comment there.
