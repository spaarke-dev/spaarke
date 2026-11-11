# 049 credit totals - D-127, D-128 and D-129 (task 016 code)

Owner decisions: D-127 (invoice totals may be negative; credit memos), D-128 (a reviewer may disallow a credit line), D-129 (the owner's answers to the four open questions of the first pass). Extends D-121. Nothing was run on any environment.

## 1. Stored totals widened (D-127, D-129 (4))

`scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1` (idempotent, widen-only, copied from `Deploy-EbillingBillingEventAmountRange.ps1`; parse-checked, **not run**) lowers MinValue to -922337203685477 on seven Money columns (precision 2 and maximum 1,000,000,000 untouched; the `_base` companions already accept the range):

| Column | Role |
|---|---|
| `sprk_invoice.sprk_totalamount` | billed total (read by `InvoiceTotalsService`, written by 049's invoice create) |
| `sprk_invoice.sprk_feestotal` | written by `InvoiceTotalsService.RecalculateAsync` |
| `sprk_invoice.sprk_expensestotal` | same |
| `sprk_invoice.sprk_lineadjustmentstotal` | same |
| `sprk_invoice.sprk_invoiceadjustmentstotal` | same |
| `sprk_invoice.sprk_approvedtotal` | same |
| `sprk_billingexport.sprk_totalamount` | batch total, the sum of the batch's approved invoice totals (task 150; D-129 (4)) |

The five columns the service writes are exactly the five in its `UpdateRecordFieldsIfUnchangedAsync` call. The script checks every column's table and type first (exit 2 on a non-Money, nothing written), widens each, and publishes `sprk_invoice` and `sprk_billingexport` only if it wrote to them. Dev ordering: run it before the first live 049 run that can create a credit memo.

## 2. Calculator rules (`InvoiceTotalsCalculator`)

- `invoice-totals.total-below-storable-minimum` is removed (constant, check, docs); no caller outside the calculator's own tests used it, and no client code reads it.
- **D-129 (1).** `invoice-totals.approved-total-negative` (FR-32) applies only when the billed total is zero or more (`approved < 0 && billed >= 0`). A credit memo (negative billed total) has no extra limit, even if adjustments push it further negative. Tests: `D129_1_*`.
- **D-128.** A Disallowed credit is summed signed (adjustment = the negative amount), so disallowing a credit raises the approved total; the "refused when nothing offsets it" rule is gone.
- **D-129 (3).** A Reduced credit is allowed: the approved amount must lie between the billed credit and 0 (`min(0, billed) <= approved <= max(0, billed)`). -100 reduced to -40 gives an adjustment of -60, so the payable rises by 60. More credit than billed (-150) or the opposite sign (+10) is still refused with `line-approved-amount-invalid`, as is a missing approved amount. A charge keeps its range 0 .. billed. Tests: `D129_3_*`.
- **D-129 (2), percent base.** A percent adjustment is `value% x max(0, fees - fee-line adjustments)`. On a net-negative fee base it adjusts nothing: 10% of -700 is 0 and the invoice stays -700; a fixed adjustment is unaffected. On a positive base nothing changes. A disallowed credit leaves the base like any disallowed fee line (fees 300 and -1000, credit disallowed: base 300, 10% = 30). Expenses never enter the base. Tests: `D129_2_*`.

## 3. Reviewer disallow (D-128)

No reviewer-disallow or adjustment code exists yet in `src` (tasks 101 and 102 are not built): nothing refuses a negative line, and the only gate was the calculator's. 101 and 102 must not add one, and must allow Reduced on a credit within the range above. `NonBillableActivityHandler` is unchanged (D-105: the finding stays, no suggested disallow for a negative amount); a test pins it next to D-128.

## 4. Tests

`InvoiceTotalsCalculatorTests`, `InvoiceTotalsServiceTests`, `NonBillableActivityHandlerTests`. Each behaviour change has a test that fails on the previous calculator; guards that pass before and after by design are the refusal on a non-negative invoice, the offsetting disallow case and the D-105 pin.

## 5. Audit of readers of `sprk_approvedtotal` and `sprk_totalamount` (read-only; D-129 point 5)

No reader found that breaks on a negative total.

- `FinanceBaseValues.cs:25,39,239,246` spend = grouped SUM of `sprk_invoice.sprk_totalamount` (the billed total, not the approved total) over counted statuses: signed, so a credit memo reduces spend as D-103 intends. Utilization = spend / budget x 100 can go negative; `sprk_budgetutilizationpercent` accepts down to -100000000000.
- `FinanceRollupService.cs:56,104` stores spend, remaining budget, monthly spend and pending spend on matter and project: `sprk_totalspendtodate`, `sprk_remainingbudget`, `sprk_monthlyspendcurrent`, `sprk_pendingspend` all have minimum -922337203685477 in `docs/data-model/sprk_financial-related-entities.md`.
- `FinanceSummaryService.cs:391,414` reads `TotalAmount` signed (`?? 0m`) into a DTO; no division, filter or non-negative check.
- `Tools/FinancialCalculationToolHandler.cs:155,204` adds `sprk_totalamount` signed into total spend; no check.
- `InvoiceReviewService.cs:945-947` writes a caller's `TotalAmount` with no non-negative validation, so a credit memo can be created.
- `InvoiceLiveFactResolver.cs:66`, `InvoiceIndexingJobHandler.cs:266,367`, `PolicyVersionValidator.cs:151` (doc comment), `ExternalAccessModule.cs:462` (a column list) and the external SPA `RecordDetailFields.tsx:145` (money display) only read or display the value.
- Not audited at row level: Dataverse-side views, charts or forms that may filter on a total above 0, and the `sprk_invoice` `_base` columns (already accept the range).
- Observation, not changed: the spend base is the billed total, not `sprk_approvedtotal`, so a disallowed line or reduced credit changes the approved total but not counted spend. That is pre-existing and was not part of D-127 to D-129.
- Inside the totals service nothing needed fixing.
