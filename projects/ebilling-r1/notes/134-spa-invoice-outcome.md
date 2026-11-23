# Task 134: SPA invoice outcome report

Branch `task/ebilling-134`, from `19866f6606` (work/ebilling-r1 tip). Rigor STANDARD. Model: Sonnet @ high. Client-only: no server, schema or `@spaarke/ui-components` change.

## What was built

The firm's on-screen outcome report (FR-38, FR-18, FR-40 as amended by EB-D-254) in the external SPA, over 130's two read routes:

- `GET /api/v1/external/ebilling/invoices/{id}/outcome`: the invoice record's new **Outcome** section.
- `GET /api/v1/external/ebilling/submissions/{id}/outcome`: the rejected-submission result screen, through the shared `InvoiceSubmissionOutcome`'s existing `renderReport` slot.

## Files changed (all under `src/client/external-spa/`)

| File | Purpose |
|---|---|
| `src/api/ebilling-client.ts` (new) | Typed `getInvoiceOutcome` / `getSubmissionOutcome` through `bffApiCall` only (ADR-028). Resolves to an explicit state: `ok`, `noAccess` (403), `notFound` (404), `disabled` (503 `ebilling.disabled`), `unavailable` (any other 503, or `degraded`, or kind `Unavailable`), `error`. Reads the body through its own allow-list (`normalizeOutcome`): unknown keys never leave the client layer. |
| `src/components/ebilling/useFirmOutcome.ts` (new) | The read hook (subject, refresh signal, reload, stale-answer drop). |
| `src/components/ebilling/InvoiceOutcomeReport.tsx` (new) | `InvoiceOutcomeReport` (one `FirmOutcome`), `OutcomeBody` (report or its state), `SubmissionOutcomeReport` (the result-screen report in a `SectionCard`). Fluent v9, semantic tokens only; the report's own print rules are on its classes. |
| `src/components/ebilling/outcomePrint.tsx` (new, fix round 1) | The print surface: `useOutcomePrint` appends `#sprk-outcome-print-root` to `body`, portals a copy of the whole report into it inside a LIGHT-theme `FluentProvider`, sets `html.sprk-outcome-printing`, calls `window.print()`, and removes all of it on `afterprint`, on a fallback timer, and on unmount. Holds the one global print rule (`PRINT_GLOBAL_CSS`, `makeStaticStyles`). |
| `src/components/record/sections/OutcomeSection.tsx` (new) | The invoice record's section (`RecordSectionCard id="outcome"`); renders nothing, card included, for the disabled state. |
| `src/components/record/recordTemplate.types.ts` | `'outcome'` added to `RecordSectionId` and `RECORD_SECTION_LABELS` (two inserted lines). |
| `src/components/record/sections/index.ts` | Registers `outcome: OutcomeSection` (import and entry). |
| `src/components/record/recordTemplates.ts` | `sprk_invoice.left` becomes `['information', 'outcome', 'files']`; the header table comment row updated. The `headerFacts` lines and the `RecordHeaderFact` type (PR #2929) are untouched. |
| `src/pages/InvoiceSubmitPage.tsx` | Passes `renderReport` (-> `SubmissionOutcomeReport`) to the resume-screen `InvoiceSubmissionOutcome` and to the wizard's result screen. The shared component is not edited. |
| `tests/InvoiceOutcomeReport.test.tsx` (new) | 57 tests (client, report, print surface, section, helpers). |
| `tests/InvoiceSubmitPage.test.tsx` | The old "report placeholder" test now asserts the report (and the submission route call); one new resume-screen test (report shown; kill switch shows nothing). |
| `tests/RecordTemplateModal.test.tsx`, `tests/RecordTemplateSections.test.tsx` | The expected `sprk_invoice` layout gains `'outcome'` (the old expectation is the very thing this task changes). |

## Content rules (as built)

- Summary: tracking number, status, billed total; approved total only when non-null. Items are stacked `<li>` cards in a `<ul>` at every width (never a table), lines first (line number, then date, then server order), then "Whole invoice" items (adjustments and findings with no line). Each card: decision badge, line/title, description, amounts, values read, reason, firm reasons, and a citation block per citation (firm-facing `label`, rule name, version, section reference, quoted clause). No citation: the reason only.
- `InvoiceInReview`: "Under review", billed total, items (deviation 2).
- `SubmissionInProgress`, `Closed`: status (and tracking, billed when present) only.
- `Rejected`, `ReturnedToFirm`: items; the line's `approvedAmount` is never rendered (130f K6, client side).
- `noticeProse`: a plain React text node (`white-space: pre-wrap`), never HTML; absent or blank: no reason line.
- `notShownCount` > 0: a warning bar ("N more findings could not be shown here.").
- Approved kinds (`Approved`, `ApprovedWithAdjustments`, `SentToAp`, `Paid`) with both totals: a reconciliation (billed, reduction or increase = billed - approved in whole cents, approved).
- A finding's `reasonCode`, when present, shows as a small "Reason code: ..." line beside its firm-safe description (EB-D-210 / FR-28, fix round 1 F4).
- No links are built. `lineId` is read but not displayed.
- `Closed` check: the server sends the status label and tracking number only (130b fix round 1); the report renders exactly that.

## Acceptance criteria to tests

(`T` = `tests/InvoiceOutcomeReport.test.tsx`; `P` = `tests/InvoiceSubmitPage.test.tsx`)

| Criterion | Test |
|---|---|
| AC1: every item renders subject, decision, reason and (when present) label, version, section, quote; no citation = reason only | T "AC1: every item renders ...", "AC1: an item with no citation shows its reason only", "shows the firm-facing label ... never a rule set name", "a citation with no label, section or quote ..." |
| AC2: In Review shows status, billed total and "Under review" (items per EB-D-202) | T "EB-D-202: an In Review invoice ..." |
| AC3: Download PDF only when a report exists | Withdrawn (EB-D-254). Replaced by: T "the Print button calls window.print" and "there is no download control and no report route call" |
| AC4 (130g): noticeProse plain text; absent shows no reason line | T "130g: Rejected/ReturnedToFirm shows noticeProse ... markup ... shown literally" (x2), "130g: an absent noticeProse (or a blank one) shows no reason line" |
| AC5: 403 no-access; 503 `ebilling.disabled` hides the section | T "403 shows the no-access state", "503 ebilling.disabled renders nothing at all", "another 503 shows the unavailable state with Try again", client state table (403, 404, 503 x3, 500, network) |
| AC6: 051's rejected-submission result screen shows the same component | P "a Rejected result shows the reason code and the outcome report, not the placeholder" (wizard result screen) and "a Rejected submission on the resume screen shows the same outcome report ..., and nothing of it when eBilling is off" |
| AC7: build and test pass; no new dependency | `npm run build`, full `npm test`; `package.json` untouched |
| AC8: ui-tests | Not run (see below) |
| 130f K6 | T "130f K6: a Rejected / ReturnedToFirm invoice shows its items but never a per-line approved amount" (x2) plus the approved control; and on the print path (N1): "the PRINT copy of a Rejected / ReturnedToFirm invoice shows the line but never the per-line approved amount" (x2), with an Approved print copy as the control that does show 650.00 |
| D-66 / model 7.4 negative | T "D-66 negative: keys the contract does not name ... never render" and the client "copies only the members of the contract" |
| NFR-13 cards at narrow width | T "NFR-13: items are stacked cards in a list at 360 px and at desktop width, never a table" |
| EB-D-254 print (fix round 1 F1) | T "F1: with no print in progress the html class is absent ...", "clicking Print mounts the print root with the FULL item list, sets the html class, then calls window.print", "afterprint removes ...", "when afterprint never arrives the fallback timer ...", "a window.print that throws still ends in the fallback clean-up", "unmounting during a print ...", "a second click ...", "a report whose host clips it still prints in full", "the print copy is rendered in the LIGHT theme even when the page is dark", "every print rule is either one of the report's own classes or the single rule gated on the html printing class", "the global rule is gated on the html class, only under @media print, ..." |
| F4 reason code | T "F4 (EB-D-210, FR-28): a finding shows its reason code ...; none when the code is absent" |
| K4 cheap fixes | T "no 'Reduction 0.00' row when nothing was reduced", "the summary carries the print page break only when items follow"; the amount formatter is now the SPA's own money formatter (`formatDetailValue`, kind `money`) |
| Layout (section registered, invoice template only) | T "is registered as a section and is in the sprk_invoice template only, after Information"; the two updated layout assertions |

Fail-before: with `InvoiceSubmitPage.tsx` restored to its pre-task version, the two page tests fail (2 failed, 64 passed); restored afterwards. The other tests cover new files, which do not exist before the change.

## Deviations

1. **EB-D-254: print button instead of PDF.** No Download PDF button, no report route call (task 133 is withdrawn). "Print or save as PDF" prints a COPY of the report (fix round 1 F1: the hosts clip the on-screen report, because the record modal's content scrolls and the wizard frame has a fixed height and `overflow: hidden`, so printing the page printed only the visible slice). The copy is portaled into a dedicated root on `document.body`, inside a LIGHT-theme `FluentProvider` (so dark mode prints in light colours; the earlier token re-pointing in the report's CSS is gone), `html.sprk-outcome-printing` is set, then `window.print()` is called; `afterprint`, a 10 s fallback timer and unmount each remove the class, the portal and the root. **The whole report prints, and the SPA chrome is hidden only during a report print.** The one global rule (`PRINT_GLOBAL_CSS` in `outcomePrint.tsx`, `makeStaticStyles`) is under `@media print` and gated on that html class: it hides every `body > *` except the print root and lifts the page's `html, body { height: 100%; overflow: hidden }` (from `index.html`) so the root is unconstrained; the root is `display: none` on screen. Outside a report print it matches nothing, so it never reaches the record modal or the grids. For spa-r4 to review. The report's own `@media print` classes lay the copy out (page break after the summary only when items follow, cards not split). Note: Griffel's `makeStaticStyles` is a no-op when `NODE_ENV` is `test`, so the unit tests parse the exported rule text (`PRINT_GLOBAL_CSS`) rather than observing the injection; the wiring is not visible to vitest and needs the 136 print-preview smoke.
2. **EB-D-202: In Review shows items.** The POML's "Under review only" is superseded by the owner's EB-D-202; the client renders what the server returns (status, billed total, "Under review", plus the decided and added lines with reasons).
3. **K6 handled in the client** (not by nulling `ApprovedAmount` in the projection): `line.approvedAmount` is never rendered for `Rejected` and `ReturnedToFirm`.
4. **The shared slot, no shared-library change.** `InvoiceSubmissionOutcome` and `InvoiceSubmissionWizard` already take `renderReport`; the page passes it. `src/client/shared/**` is untouched. (The wizard's own result screen, not only the resume screen, now shows the report.)
5. **No `RecordDetailModal.tsx`** (deleted by R4 task 005): the report is a section in the record template.
6. A degraded answer (the route answers 503 for it, so only reachable if the contract changes) is treated as `unavailable`, never as a report with an empty list.
7. (Fix round 1) `reasonCode` is now shown (F4). Amounts use the SPA's money formatter (browser locale, as the record header's total does); a zero reduction row is not shown.

## ui-tests

Not run. They need a dev deploy of the SPA and a firm sign-in (task 136). The four scripted checks (own-invoice outcome, under review, phone width 360 x 740, dark mode) and the "Print preview shows the same items" replacement for "Download PDF" remain for the 136 smoke. Unit-level stand-ins: items as cards at 360 and 1280, tokens only (no hex in the new files), print surface, its gate and its clean-up asserted. The 136 print-preview smoke must also check, in a real browser, that a report with many lines prints in full from the record modal and from the submission result screen, and that the page is back to normal after the print dialog closes.

## Checks

- `npx tsc --noEmit -p tsconfig.json` clean; `npm run lint` (eslint src tests) clean; `npm run build` green; `npm test` 66 files, 1881 tests passed (before this task: 1823 tests; +58 net: 57 new in `InvoiceOutcomeReport.test.tsx`, 1 new in `InvoiceSubmitPage.test.tsx`).
- `external-spa` is not a package of the shared client-lint (`scripts/quality/client-lint`, `PACKAGES`), so it has no run there.
- No new npm dependency.

## Component justification (CLAUDE.md section 11)

- **Existing:** `RecordSectionCard` and the record template's section registry; `SectionCard`; the shared `InvoiceSubmissionOutcome.renderReport` slot; `bffApiCall` and `toInvoiceSubmissionError` / `isEbillingDisabledError`. Nothing in the SPA rendered eBilling outcomes (grep "Outcome" in `src/client/external-spa/src` found none before this task).
- **Extension:** the generic record field list cannot render grouped findings with citations, and the same report renders in two places (the invoice record and the result screen), so one component plus one section is the smallest surface; the section reuses the existing card and registry, and the result screen reuses the existing slot. The client file is new because no eBilling outcome client existed (the billing-guidelines client is a different route).
- **Cost of doing nothing:** firms see no reasons for reductions or rejections in the SPA (FR-38 and FR-18 fail; success criterion 6 fails).

## Notes for spa-r4 (reviews the external-spa diff)

- Touches `recordTemplate.types.ts`, `recordTemplates.ts` and `sections/index.ts` in single, additive lines only. PR #2929 edits the `sprk_invoice` `headerFacts` and `RecordHeaderFact`; those lines are not touched here, so a merge should be mechanical (the `sprk_invoice` entry's `left` line is two lines below `headerFacts`).
- The existing layout assertions for `sprk_invoice` in `RecordTemplateModal.test.tsx` and `RecordTemplateSections.test.tsx` changed by one entry each.

## spa-r4 review (2026-11-23) and fix round

Branch `task/ebilling-134-fix1`, from `f70380b196` (work/ebilling-r1 tip). Client-only, `src/client/external-spa`.

### Fixed

- **F1 (Outcome section called the route for callers the route refuses).** `OutcomeSection` now makes no call, and renders nothing (card included), unless `/me` positively says the caller is a firm member: `callerFirmIdsOf(callerContext).length > 0` (the `InvoicesWidget` "Submit invoice" rule, one definition; on this branch it is the same as origin/master's) and `systemUserId` is not set (the BFF filter refuses a systemuser before anything else, so a systemuser is never asked even if `/me` listed firms). A caller context that is not in yet, failed, or has `firms` null/absent (unknown) asks nothing. The read hook sits in an inner component, so a gated caller never starts it.
  - **Firm of THIS invoice not used.** The invoice record read (`RECORD_DETAIL_ENTITIES.sprk_invoice`) does not carry `sprk_vendororg`, so the section cannot name the invoice's firm; adding the column to the shared read is a contract change outside this fix. So a member of ANOTHER firm on a shared matter still passes the gate and gets the server's 403; that case is the backstop below, and there is no "caller of another firm" must-not-call test (it cannot be expressed). Residual cost: one refused read for that caller, with nothing shown. To close it, add the firm lookup to the invoice read.
  - **Backstop (b).** A 403 that still happens renders nothing (`return null`, as the disabled state does), not the no-access card. `OutcomeBody` keeps the no-access card for the submission result screen (the submitter is the firm).
  - Commit `977cd2df47`. Tests (`InvoiceOutcomeReport.test.tsx`): "403 shows the no-access state" is flipped into "spa-r4 F1 backstop: a 403 that still happens renders nothing"; new "who is asked" group: workforce caller (firms `[]` and systemUserId), systemuser with firms listed, caller with no firms, firms null and absent, caller context null, caller context that arrives later (read starts then), and the must-fire (one firm and several firms still get the section and the call). Fail before / pass after: 7 failed with the old section (the flipped 403 test and six must-not-call tests), 64 passed with the new; the must-fire control passes both ways.
  - Follow-on test fix: `RecordTemplateSections.test.tsx` "FR-01 template sections" for `sprk_invoice` used a `/me` with no firms and so lost its Outcome section; its mock `/me` now takes `firms`, the invoice case sets one and waits for `/me`, and a new test asserts a no-firm caller gets `['information', 'files']` and no `/outcome` request.
- **F4 (modal wizard showed the placeholder).** `renderSubmissionReport` moved next to `SubmissionOutcomeReport` in `components/ebilling/InvoiceOutcomeReport.tsx` (exported); `InvoiceSubmitPage` and `SubmitInvoiceAction` (`<InvoiceSubmissionWizard renderReport={renderSubmissionReport}>`) both use it. The result screen's own outcome call is unchanged. Commit `5a59385b5b`. Tests: `RecordTemplateSubmitInvoice.test.tsx` "a Rejected result in the modal wizard" (the modal wizard renders `submission-outcome-report` around `invoice-outcome-report`, reads the submission route, no `invoice-report-placeholder`): fails with `SubmitInvoiceAction` restored to its old version, passes after; `InvoiceSubmitPage.test.tsx` "a Rejected result shows the reason code and the outcome report" now also asserts the page draws `submission-outcome-report` around the report.

### Known limits recorded by spa-r4 (no change)

- **K1** No list-empty case.
- **K2** `outcomePrint.tsx`: the fallback timer is not cleared if `afterprint` fires inside `print()` (no real path in Chrome).
- **K3** No test mounts the report inside `RecordTemplateModal` / `SprkModal`.
- **K4** Refresh replaces the report with a spinner.

### Checks

`npx tsc --noEmit -p tsconfig.json` clean; `npx eslint src tests` clean; `npx vitest run` 66 files, 1890 tests passed.
