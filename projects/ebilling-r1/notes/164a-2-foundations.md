# 164a-2: the foundations for Add line

Task 164 (Add line), part 164a-2. Branch `task/ebilling-164a2`, from `work/ebilling-r1` at `2377cf643a`. Rigor FULL. No Add line service, route or persister edit is in this change; those come later.

Decisions this implements: EB-D-192 (totals, coding), EB-D-193 (the marker), EB-D-198 (the `add-line` code, no link code), EB-D-199 (the approved-total rule). Brief: `add-line-brief.md` sections 3, 4 (Q1-Q3) and 5.

## What changed

| Area | Change |
|---|---|
| `DecisionActionCatalog` (`BillingActions` only) | New `add-line`: required `reason` (TextArea), Judgement, Tier2b (`Reversible`, `RecordOfTruthImpact`), `Excludes` empty, plan lane Decide, no Next step. Appended after `assign-firm`. Not in `All`, so the Signals commit route cannot take it. |
| `DecisionRecordWriter` | One literal: `BillingSubjectByCode` maps `add-line` to `sprk_billingevent` (the line, D-75) beside the three line codes. The doc comment names it. Nothing else in the writer changed. |
| `InvoiceTotalsCalculator` | `InvoiceLineInput.ReviewerAdded` (trailing, default false). Rule below. |
| `ExtractionProvenance` | `SourceMember`, `ReviewerSource`, `DecisionIdMember`, `ForReviewerAddedLine(decisionId)` (the write), `AddedLineIdFor(decisionId)` (the row id) and `IsReviewerAdded(Entity)` (the only read; fix round F1). |
| `InvoiceTotalsService` | `ReviewerAddedOf(Entity)` (delegates to `ExtractionProvenance.IsReviewerAdded(Entity)`), `ToRow` sets the flag, and the lines query now also reads `sprk_extractionconfidence`. |
| `InvoiceReviewReadService` | `ReviewLineDto.ReviewerAdded` (bool, last member), set from `ExtractionProvenance.IsReviewerAdded(row)` in `ToLine`. |
| `ApExportService.ReadLinesAsync` (fix round F2) | A reviewer-added line is exported with `billed = 0` (as an Adjustment line); `approved` stays its contribution. The lines query reads `sprk_extractionconfidence`. |

## The approved-total rule (EB-D-199, test T8)

approved = billed + the sum of added-line contributions - firm-line adjustments - invoice adjustments.

A reviewer-added Fee or Expense line contributes its amount while undecided or Accepted, its approved amount when Reduced, and 0 when Disallowed. It is outside billed, fees, expenses, the firm-line adjustments and the percent base. A Reduced added line is range-checked like any Reduced line (fail closed). The flag is ignored on an Adjustment line. The FR-32 floor and the credit-memo rule are unchanged. The code derives the contribution as `amount - lineAdjustment`, so there is one definition of "the part not approved".

Tests (T8): accept, reduce (including to 0), disallow, undecided (Flagged, Pending, Clean), Fee and Expense, a credit, the percent base, the FR-32 floor, a refused Reduced; billed, fees, expenses and line adjustments are asserted unchanged. Must-not-fire: an intake line (`ReviewerAdded` false) behaves exactly as before, at the calculator and through the real line decision service.

## `InvoiceLineInput` builders (every caller)

| Site | Sets `ReviewerAdded` from | Notes |
|---|---|---|
| `InvoiceTotalsService.ToRow` (`RecalculateAsync`, the stored totals) | `ReviewerAddedOf(line)` | Query now includes `sprk_extractionconfidence`. |
| `InvoiceAdjustmentService.ToInput` (adjustment add and remove dry runs) | `ReviewerAddedOf(row)` | `LineQuery` already reads the column. |
| `InvoiceCorrectionService.DryRunTotalsAsync` | through `InvoiceAdjustmentService.ToInput` | Same rows, no change needed. |
| `InvoiceLineDecisionService` dry run (two constructions, the decided and the undecided branch) | `ReviewerAddedOf(row)` | `LineQuery` already reads the column. |
| `ApExportService.ReadLinesAsync` (not an `InvoiceLineInput`; same read) | `ExtractionProvenance.IsReviewerAdded(line)` | F2. |
| `InvoiceReviewReadService.ToLine` (not an `InvoiceLineInput`; same read) | `ExtractionProvenance.IsReviewerAdded(row)` | `ReviewLineDto.ReviewerAdded`. |
| `InvoiceAdjustmentService` line 542, the new Adjustment line | default false | An Adjustment line is never reviewer-added; the flag is ignored for it. |
| Tests only | `ApExportReviewFixTests`, `InvoiceTotalsCalculatorTests` | Positional, default false; unchanged. |

No other production code builds an `InvoiceLineInput` or calls `InvoiceTotalsCalculator.Calculate`. Every production read of the flag goes through the entity overload; the JSON-only reads are private.

## The marker (EB-D-193) and what authenticates it (fix round F1)

`sprk_extractionconfidence` of an added line is `{"provenance":{"source":"reviewer","decisionId":"<guid>"}}`: a source word and an id, no name, text or value, no new column.

The column is not field-secured and no guard covers it, so the marker alone must never decide. `ExtractionProvenance.IsReviewerAdded(Entity)` is true ONLY when all three hold: the marker says `reviewer` (ordinal), its `decisionId` parses (and is not empty), and `line.Id == AddedLineIdFor(decisionId)`. A copy of the marker on an intake line, or on any row with another id, reads false, so it takes nothing out of billed, fees, expenses or the percent base. The read is exact: absent, another source or case, a non-string, a missing or unparsable decision id, a damaged or empty column, and a top-level `source` all give false. There is no public JSON-only read; the JSON helpers are private.

**Placement of `AddedLineIdFor`:** in `ExtractionProvenance`, beside the marker it authenticates, and not next to `InvoiceSubmissionPersister.LineIdFor`. The persister is not edited by this task (it overlaps 082d and 095d-2a), and the `ebilling-added-line-` prefix keeps these ids apart from every intake line id. It uses the persister's existing `IdOf`.

An added line has no analyzer pair, so `HasExtraction` is false and an extracted-field correction of it is refused (brief K1).

**For 164a-4a (the Add line service):** create the row with id `ExtractionProvenance.AddedLineIdFor(decisionId)` and write `ExtractionProvenance.ForReviewerAddedLine(decisionId)` into its `sprk_extractionconfidence`, with the same `decisionId` that goes to the Decision Record. A row created with any other id will not read as reviewer-added. The deterministic id also gives the Q3 idempotency: a retry addresses the same row, so there is one line and one record.

**Known limit:** the marker can still be removed from a genuine added line by anyone who may write the line. The line then reads as an intake line. Effects, checked against the calculator (`AnAddedLine_WithItsMarkerRemoved_IsCountedAsBilled`, all four statuses): the stored billed total (`sprk_totalamount`) is an input and does not move; the fees total (or expenses total) is overstated by the line's amount; and the approved total is UNDERSTATED by the line's amount, not unchanged: its contribution is lost, and if it was Reduced or Disallowed the intake rule also charges a firm-line adjustment against money that was never billed. Spend follows the approved total, so spend is understated by the same amount. The AP export would emit the line with `billed` equal to its amount. Removing the marker is a loss of the line's payment, not a gain.

## Idempotency on the decision id (EB-D-198)

No writer change was needed. `WriteBillingDecisionAsync` keys on (subject, decision id) and hashes the code, subject, confirmer, role, values, outcome, reason and resolved set (not the facts). Tested for `add-line`: a repeat gives `AlreadyRecorded` with one create; the same id with another reason is refused (`ReviewInvalid`, "other content"); a blank reason is refused before any I/O. The line id derivation (a deterministic line id from invoice and `requestId`) belongs to the service task.

## Section 11 answers

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `add-line` catalog entry and its writer subject | Nine billing codes; none means "a line was added" (accept, reduce, disallow, approve, reject, return, override, assign-firm). | The catalog and the writer's line literal are extended in place; no new entry point or writer. | The record would carry a wrong code (a decision that did not happen) or none, and the Add line record could not be filed under the line. |
| `InvoiceLineInput.ReviewerAdded` and the calculator branch | `InvoiceTotalsCalculator` is the one definition of I-1; its rule is billed minus adjustments. | One trailing field and one branch in the same method; no second calculator. | An Accepted added line pays nothing and a Disallowed one subtracts money that was never billed (EB-D-199). |
| `ExtractionProvenance` members and `ForReviewerAddedLine` | The class already owns the `provenance` member of the line column (EB-D-170). | Three constants and two readers beside the existing ones; no column. | The line cannot be told apart from an intake line (EB-D-193), so totals, the badge and the K1 refusal have nothing to read. |
| `InvoiceTotalsService.ReviewerAddedOf` | `InvoiceAdjustmentService.ToInput`, `InvoiceTotalsService.ToRow` and the decision dry run each build an input. | A single helper on the type that already owns the line column names. | Each builder would parse the marker itself, and one missed builder counts an added line as billed in a dry run. |
| `ReviewLineDto.ReviewerAdded` | The DTO is the read model of Invoice Review. | A bool member set from the parse the read already does; no new query. | The page (task 164b) cannot show or filter the badge. |

## For ontology-r1 (they own `Services/Signals/**` and `DecisionActionCatalog`)

Their review set, additive and minimal:

- `Services/Signals/Actions/DecisionActionCatalog.cs`: one entry, `add-line`, in `DeclaredBilling` (11 lines with comments).
- `Services/Signals/DecisionRecordWriter.cs`: one expression (`or "add-line"`) in `BillingSubjectByCode` and one comment sentence. No request shape, validation, lookup or hash changed.
- Tests in the same change: `Billing_C2` (the exact-set test and the table theory), two `add-line` writer tests, `DecisionActionCatalogTests` (the code list, class and tier, required parameter, a dedicated `add-line` test) and the commit-route refusal theory (`ABillingCode_IsRefusedByTheCommitRoute`).
- `ResolvesSignal` stays at its default (true) for `add-line`, as for `assign-firm`; the existing exact-set test requires it for every billing action. Add line writes an empty `ResolvedSignalIds`, so the flag has no effect on its record.

**I-1 changes, so ontology-r1 must be told (spend, D-130).** The approved total can now exceed the billed total on an invoice with a reviewer-added line. Spend follows the approved total (`FinanceBaseValues.SpendAmountColumns`, `InvoiceTotalsService`), so spend on such an invoice rises by the added amount (undecided, Accepted) or its approved amount (Reduced), and does not move for a Disallowed line. Billed, fees and expenses do not change.

## Findings outside this change (reported, not fixed)

- **AP export lines (fixed in the fix round, F2).** `ApExportService.ReadLinesAsync` now emits `billed = 0` for a reviewer-added line (as for an Adjustment line, EB-D-192: the billed total stays as billed) and its contribution as `approved`; `lines[].billed` sums to `BilledTotal` and `lines[].approved` to `ApprovedTotal` (tests for accepted, reduced, disallowed and undecided). `ApExportService` was in scope for that round only.
- **Totals read cost.** Every recalculation now reads `sprk_extractionconfidence` for each line (a JSON column up to 100,000 characters; typically a few thousand). It is the only way to read the marker without a new column. A narrower read (a second query that filters on the marker) was not used because Dataverse cannot filter on JSON content reliably. Re-measure if an invoice has thousands of lines.

## Verification

Run on this branch; see the commit and the final report for counts. Mutation checks (each reverted): ignoring the flag in the calculator, forcing `ReviewerAddedOf` and the provenance read to false, removing `add-line` from the writer's line literal, and forcing the review read's marker to false. Fix round seeds: dropping the id comparison from `IsReviewerAdded(Entity)` turns the F1 must-fire tests red (10 failures: totals service, line decision, review read, AP export, ToInput, provenance); making the AP export ignore the marker turns the F2 reconciliation red (4). Each turned the new or extended tests red (24 failures for the first three together, 11 for the last two).
