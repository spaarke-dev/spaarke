# 164a-2: the foundations for Add line

Task 164 (Add line), part 164a-2. Branch `task/ebilling-164a2`, from `work/ebilling-r1` at `2377cf643a`. Rigor FULL. No Add line service, route or persister edit is in this change; those come later.

Decisions this implements: EB-D-192 (totals, coding), EB-D-193 (the marker), EB-D-198 (the `add-line` code, no link code), EB-D-199 (the approved-total rule). Brief: `add-line-brief.md` sections 3, 4 (Q1-Q3) and 5.

## What changed

| Area | Change |
|---|---|
| `DecisionActionCatalog` (`BillingActions` only) | New `add-line`: required `reason` (TextArea), Judgement, Tier2b (`Reversible`, `RecordOfTruthImpact`), `Excludes` empty, plan lane Decide, no Next step. Appended after `assign-firm`. Not in `All`, so the Signals commit route cannot take it. |
| `DecisionRecordWriter` | One literal: `BillingSubjectByCode` maps `add-line` to `sprk_billingevent` (the line, D-75) beside the three line codes. The doc comment names it. Nothing else in the writer changed. |
| `InvoiceTotalsCalculator` | `InvoiceLineInput.ReviewerAdded` (trailing, default false). Rule below. |
| `ExtractionProvenance` | `SourceMember`, `ReviewerSource`, `DecisionIdMember`, `ForReviewerAddedLine(decisionId)` (the write), `IsReviewerAdded(string?)` and `IsReviewerAdded(JsonElement)` (the read). |
| `InvoiceTotalsService` | `ReviewerAddedOf(Entity)` (the one definition of the read), `ToRow` sets the flag, and the lines query now also reads `sprk_extractionconfidence`. |
| `InvoiceReviewReadService` | `ReviewLineDto.ReviewerAdded` (bool, last member), set from `LineExtraction.ReviewerAdded` (an optional trailing member, set from the same parse). |

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
| `InvoiceAdjustmentService` line 542, the new Adjustment line | default false | An Adjustment line is never reviewer-added; the flag is ignored for it. |
| Tests only | `ApExportReviewFixTests`, `InvoiceTotalsCalculatorTests` | Positional, default false; unchanged. |

No other production code builds an `InvoiceLineInput` or calls `InvoiceTotalsCalculator.Calculate`.

## The marker (EB-D-193)

`sprk_extractionconfidence` of an added line is `{"provenance":{"source":"reviewer","decisionId":"<guid>"}}`: a source word and an id, no name, text or value, no new column. The read is exact: ordinal `"reviewer"` in `provenance.source`. Absent, another source, another case, a non-string, a damaged or empty column, and a top-level `source` all give false. An added line has no analyzer pair, so `HasExtraction` is false and an extracted-field correction of it is refused (brief K1).

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

- **AP export lines.** `ApExportService.ReadLinesAsync` (about line 1063) emits `line.billed = sprk_amount` for every Fee and Expense line, including a reviewer-added one. For an added line the lines' `billed` then sums to more than the invoice's `BilledTotal`, while the lines' `approved` still sums to the `ApprovedTotal`. The export has no way to tell the line apart (brief K3). The owner of the export should decide before Add line ships: emit `billed = 0` for an added line (as for an Adjustment line), or accept the difference.
- **Totals read cost.** Every recalculation now reads `sprk_extractionconfidence` for each line (a JSON column up to 100,000 characters; typically a few thousand). It is the only way to read the marker without a new column. A narrower read (a second query that filters on the marker) was not used because Dataverse cannot filter on JSON content reliably. Re-measure if an invoice has thousands of lines.

## Verification

Run on this branch; see the commit and the final report for counts. Mutation checks (each reverted): ignoring the flag in the calculator, forcing `ReviewerAddedOf` and the provenance read to false, removing `add-line` from the writer's line literal, and forcing the review read's marker to false. Each turned the new or extended tests red (24 failures for the first three together, 11 for the last two).
