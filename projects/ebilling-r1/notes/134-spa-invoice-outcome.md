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
| `src/components/ebilling/InvoiceOutcomeReport.tsx` (new) | `InvoiceOutcomeReport` (one `FirmOutcome`), `OutcomeBody` (report or its state), `SubmissionOutcomeReport` (the result-screen report in a `SectionCard`). Fluent v9, semantic tokens only, print rules scoped to the report. |
| `src/components/record/sections/OutcomeSection.tsx` (new) | The invoice record's section (`RecordSectionCard id="outcome"`); renders nothing, card included, for the disabled state. |
| `src/components/record/recordTemplate.types.ts` | `'outcome'` added to `RecordSectionId` and `RECORD_SECTION_LABELS` (two inserted lines). |
| `src/components/record/sections/index.ts` | Registers `outcome: OutcomeSection` (import and entry). |
| `src/components/record/recordTemplates.ts` | `sprk_invoice.left` becomes `['information', 'outcome', 'files']`; the header table comment row updated. The `headerFacts` lines and the `RecordHeaderFact` type (PR #2929) are untouched. |
| `src/pages/InvoiceSubmitPage.tsx` | Passes `renderReport` (-> `SubmissionOutcomeReport`) to the resume-screen `InvoiceSubmissionOutcome` and to the wizard's result screen. The shared component is not edited. |
| `tests/InvoiceOutcomeReport.test.tsx` (new) | 43 tests (client, report, print, section, helpers). |
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
- No links are built. `reasonCode` and `lineId` are read but not displayed.
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
| 130f K6 | T "130f K6: a Rejected / ReturnedToFirm invoice shows its items but never a per-line approved amount" (x2) plus the approved control |
| D-66 / model 7.4 negative | T "D-66 negative: keys the contract does not name ... never render" and the client "copies only the members of the contract" |
| NFR-13 cards at narrow width | T "NFR-13: items are stacked cards in a list at 360 px and at desktop width, never a table" |
| EB-D-254 print scoping | T "the print rules are scoped to the report ...", "a dark-theme page prints in the light palette ..." |
| Layout (section registered, invoice template only) | T "is registered as a section and is in the sprk_invoice template only, after Information"; the two updated layout assertions |

Fail-before: with `InvoiceSubmitPage.tsx` restored to its pre-task version, the two page tests fail (2 failed, 64 passed); restored afterwards. The other tests cover new files, which do not exist before the change.

## Deviations

1. **EB-D-254: print button instead of PDF.** No Download PDF button, no report route call (task 133 is withdrawn). "Print or save as PDF" calls `window.print()`. The print stylesheet is `@media print` inside the report's own `makeStyles` classes (scoped, no global rule): the summary ends the first page, a card is not split, the button is hidden, and the report re-points its colour custom properties at `webLightTheme`'s values so a dark-mode page prints in light colours. **Limit:** because no global rule is allowed, the SPA's navigation, the record modal's own chrome and the other sections are not hidden by the report's rules; the printout is the page with the report laid out for print. Hiding the surrounding chrome needs a global print rule or an action host that prints only the report, which the amendment forbids or leaves to spa-r4. Not guessed here.
2. **EB-D-202: In Review shows items.** The POML's "Under review only" is superseded by the owner's EB-D-202; the client renders what the server returns (status, billed total, "Under review", plus the decided and added lines with reasons).
3. **K6 handled in the client** (not by nulling `ApprovedAmount` in the projection): `line.approvedAmount` is never rendered for `Rejected` and `ReturnedToFirm`.
4. **The shared slot, no shared-library change.** `InvoiceSubmissionOutcome` and `InvoiceSubmissionWizard` already take `renderReport`; the page passes it. `src/client/shared/**` is untouched. (The wizard's own result screen, not only the resume screen, now shows the report.)
5. **No `RecordDetailModal.tsx`** (deleted by R4 task 005): the report is a section in the record template.
6. A degraded answer (the route answers 503 for it, so only reachable if the contract changes) is treated as `unavailable`, never as a report with an empty list.
7. `reasonCode` is not displayed: the firm-safe text is the finding's `firmReasons` entry, and the shared result screen already shows the submission's reason code.

## ui-tests

Not run. They need a dev deploy of the SPA and a firm sign-in (task 136). The four scripted checks (own-invoice outcome, under review, phone width 360 x 740, dark mode) and the "Print preview shows the same items" replacement for "Download PDF" remain for the 136 smoke. Unit-level stand-ins: items as cards at 360 and 1280, tokens only (no hex in the new files), print rules scoped and light-palette override asserted.

## Checks

- `npx tsc --noEmit -p tsconfig.json` clean; `npm run lint` (eslint src tests) clean; `npm run build` green; `npm test` 66 files, 1867 tests passed (before this task: 1823 tests; +44 net: 43 new in `InvoiceOutcomeReport.test.tsx`, 1 new in `InvoiceSubmitPage.test.tsx`).
- `external-spa` is not a package of the shared client-lint (`scripts/quality/client-lint`, `PACKAGES`), so it has no run there.
- No new npm dependency.

## Component justification (CLAUDE.md section 11)

- **Existing:** `RecordSectionCard` and the record template's section registry; `SectionCard`; the shared `InvoiceSubmissionOutcome.renderReport` slot; `bffApiCall` and `toInvoiceSubmissionError` / `isEbillingDisabledError`. Nothing in the SPA rendered eBilling outcomes (grep "Outcome" in `src/client/external-spa/src` found none before this task).
- **Extension:** the generic record field list cannot render grouped findings with citations, and the same report renders in two places (the invoice record and the result screen), so one component plus one section is the smallest surface; the section reuses the existing card and registry, and the result screen reuses the existing slot. The client file is new because no eBilling outcome client existed (the billing-guidelines client is a different route).
- **Cost of doing nothing:** firms see no reasons for reductions or rejections in the SPA (FR-38 and FR-18 fail; success criterion 6 fails).

## Notes for spa-r4 (reviews the external-spa diff)

- Touches `recordTemplate.types.ts`, `recordTemplates.ts` and `sections/index.ts` in single, additive lines only. PR #2929 edits the `sprk_invoice` `headerFacts` and `RecordHeaderFact`; those lines are not touched here, so a merge should be mechanical (the `sprk_invoice` entry's `left` line is two lines below `headerFacts`).
- The existing layout assertions for `sprk_invoice` in `RecordTemplateModal.test.tsx` and `RecordTemplateSections.test.tsx` changed by one entry each.
