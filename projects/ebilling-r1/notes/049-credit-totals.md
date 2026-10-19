# 049 credit totals - D-127 and D-128 (task 016 code)

Owner decisions: D-127 (invoice totals may be negative; credit memos) and D-128 (a reviewer may disallow a credit line). Extends D-121. Not run on any environment.

## 1. Stored invoice totals widened (D-127)

`scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1` (idempotent, widen-only, copied from `Deploy-EbillingBillingEventAmountRange.ps1`; parse-checked, **not run**) lowers MinValue to -922337203685477 on six Money columns of `sprk_invoice` (precision 2 and maximum 1,000,000,000 untouched; the `_base` companions already accept the range):

| Column | Role |
|---|---|
| `sprk_totalamount` | billed total (read by `InvoiceTotalsService`, written by 049's invoice create) |
| `sprk_feestotal` | written by `InvoiceTotalsService.RecalculateAsync` |
| `sprk_expensestotal` | same |
| `sprk_lineadjustmentstotal` | same |
| `sprk_invoiceadjustmentstotal` | same |
| `sprk_approvedtotal` | same |

The five written columns are exactly the five in the `UpdateRecordFieldsIfUnchangedAsync` call; `sprk_totalamount` is the sixth (part A's notes said "plus four others"; the five written totals plus the billed total make six). The script checks every column's type first (escalation exit 2 on a non-Money), then widens each, then publishes `sprk_invoice` only if it wrote. Dev ordering: run it before the first live 049 run that can create a credit memo.

## 2. Calculator changes (`InvoiceTotalsCalculator`)

- `invoice-totals.total-below-storable-minimum` (`TotalBelowStorableMinimum`) is **removed**: constant, check and docs. Grep of src, tests and client code found no caller other than the calculator's own tests and the totals-service test (both rewritten); no SPA or client code reads it.
- A disallowed credit is summed signed (its adjustment is the negative amount), so disallowing a credit **raises** the approved total (D-128). The "refused when nothing offsets it" rule is gone.
- **Needs owner confirmation.** `invoice-totals.approved-total-negative` (FR-32) would have refused every credit memo, because its approved total is negative by nature. It now applies only when the billed total is not negative (`approved < 0 && billed >= 0`): adjustments that exceed a non-negative bill are still refused; a credit memo is never refused for a negative approved total. On a negative-billed invoice no check limits how far adjustments push the approved total.
- A Reduced credit is still refused (`line-approved-amount-invalid`: 0 .. a negative billed amount is empty). D-128 only names Disallow, so this was left.

## 3. Percent adjustments on a net-negative fee base

Rule (stated in the class remarks and `sprk_invoice.md`): a percent line is `value% x` the signed fee base (fees minus the Fee lines' adjustments), never clamped at zero. On a net-negative base the line is negative (a negative Adjustment-line `sprk_amount`, which the column accepts since D-121) and lessens the credit: 10% of -700 = -70, approved total -630. This equals taking the percentage of each fee line separately (distributive), and a disallowed credit leaves the base like any disallowed fee line (fees 300 and -1000, credit disallowed: base 300). The alternative (clamp the base at 0, so no percent adjustment on a net credit) is not taken; say if the owner prefers it.

## 4. Reviewer disallow (D-128)

No reviewer-disallow or adjustment code exists yet in `src` (tasks 101 and 102 are not built): nothing refuses a negative line, and the only gate was the calculator's. The line-status write in 101 and 102 must not add one. `NonBillableActivityHandler` is unchanged (D-105: finding stays, no suggested disallow for a negative amount); a new test pins it next to D-128.

## 5. Tests

Calculator (`InvoiceTotalsCalculatorTests`), totals service (`InvoiceTotalsServiceTests`), `NonBillableActivityHandlerTests`. Run against the old calculator: 10 new tests fail (all-credit, fee and expense credits, credits beyond their type, mixed net-negative, disallowed credit alone and on a credit memo, three percent-base cases, service write of negative totals). Guards that pass before and after by design: refusal still raised on a non-negative invoice, disallowed credit offset by a disallowed charge, the D-105 pin.

## 6. Reported, not changed

- `sprk_billingexport.sprk_totalamount` (min 0, "sum of the approved totals of the batch's invoices", task 150) can go negative if a batch holds credit memos. Not in the D-127 list; owner decision needed before 150.
- Other readers of `sprk_approvedtotal` / `sprk_totalamount` (rollups, summary, search, analysis, spend D-103, `FinanceBaseValues`) were not audited for an assumption of non-negative totals.
- The generated stubs `sprk_invoice-matter-project.md` and `sprk_financial-related-entities.md` still show minimum 0 until the script runs and they are regenerated (noted in `sprk_invoice.md`).
