| Logical Name    | Schema Name     | Display Name | Attribute Type   | Description                            | Custom Attribute | Type   | Additional data                     |
| --------------- | --------------- | ------------ | ---------------- | -------------------------------------- | ---------------- | ------ | ----------------------------------- |
| sprk_invoiceid  | sprk_InvoiceId  | Invoice      | Uniqueidentifier | Unique identifier for entity instances | False            | Simple |                                     |
| sprk_matter     | sprk_Matter     | Matter       | Lookup           |                                        | True             | Simple | Targets:<br><br>sprk_matter         |
| sprk_mattername | sprk_MatterName | N/A          | Text             | N/A                                    | True             | Simple | Format: Text<br><br>Max length: 100 |
| sprk_name       | sprk_Name       | Name         | Text             |                                        | True             | Simple | Format: Text<br><br>Max length: 850 |
## Invoice header (eBilling R1, task 012)

Columns and statuscode values below were added to dev by `scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1` (idempotent, additive; 2026-10-15). The table above is the older generated stub; the full pre-R1 column list is in `docs/data-model/sprk_invoice-matter-project.md`. All new columns are optional plain columns: no calculated, rollup or formula column and no business rule (ADR-002). Totals are written only by the server-side calculator (FR-07, task 016); status only by the lifecycle service (FR-06, task 014).

### Added columns

| Logical name | Type | Display name | Notes |
|---|---|---|---|
| `sprk_regardingworkassignment` | Lookup -> `sprk_workassignment` (relationship `sprk_sprk_workassignment_sprk_invoice_sprk_regardingworkassignment`, delete RemoveLink, no other cascade) | Regarding Work Assignment | FR-05 work-assignment core record. The typed `sprk_matter` / `sprk_project` lookups are the Matter / Project lookups (no `sprk_regardingmatter` / `sprk_regardingproject`, no migration). Exactly-one-lookup guard and resolver-field completeness are server-side (task 013). The resolver does not read this column until task 013 adds it to `IntermediateRootColumns["sprk_invoice"]` and its TypeScript mirror. |
| `sprk_feestotal` | Money (precision 2, 0 .. 1,000,000,000) | Fees Total | FR-07. Sum of fee lines as billed. |
| `sprk_expensestotal` | Money (same) | Expenses Total | FR-07. Sum of expense lines as billed. |
| `sprk_lineadjustmentstotal` | Money (same) | Line Adjustments Total | FR-07. Billed minus approved on reduced / disallowed lines. |
| `sprk_invoiceadjustmentstotal` | Money (same) | Invoice Adjustments Total | FR-07 / D-42. Total of invoice-level Adjustment lines. |
| `sprk_approvedtotal` | Money (same) | Approved Total | FR-07 / D-42. Billed total minus line and invoice adjustments. Minimum 0, so a negative approved total is refused by the platform as well as by the calculator (FR-32). |
| `sprk_supersedesinvoice` | Lookup -> `sprk_invoice` (self; relationship `sprk_sprk_invoice_sprk_invoice_sprk_supersedesinvoice`, delete RemoveLink) | Supersedes Invoice | D-43: the resubmission points at the invoice that was returned to the firm. |
| `sprk_billingperiodstart` | Date only | Billing Period Start | FR-21 header. |
| `sprk_billingperiodend` | Date only | Billing Period End | FR-21 header. |
| `sprk_billingexport` | Lookup -> `sprk_billingexport` (relationship `sprk_invoice_billingexport_billingexport`, delete RemoveLink, no other cascade; not required) | Export Batch | FR-13 / FR-41 (task 023). The AP export batch the invoice was sent in. Single-valued, so an invoice is in at most one batch; the batch writer (task 150) sets it and moves the invoice to Sent to AP. Columns of the batch: `docs/data-model/sprk_billingexport.md`. |
| `sprk_trackingnumber` | Text (100), optional | Tracking Number | D-124 (task 049 part A). The D-87 tracking number `{firm invoice number}.{n}`, copied from the submission's `sprk_trackingnumber` when 049 creates the invoice and never changed afterwards. The firm's own number stays in `sprk_invoicenumber`. Length 100 = the submission's: the allocator caps the firm invoice number at 89 characters and adds a dot and up to 10 digits. Optional because invoices created before eBilling have none; not a key (the (firm, tracking number) key is on `sprk_invoicesubmission`). Created by `scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1` (idempotent; not yet run on dev, owner's OK; 053 adds it to SpaarkeMaster). Logical name constant: `InvoiceReviewService.InvTrackingNumber`. |

`sprk_totalamount` (existing, money) stays the **billed total**. Each new money column also gets a system `_base` companion (base-currency value) from Dataverse. The old invoice-status choice column and its global option set were retired by task 015 (D-41): the status lives only on `statecode` / `statuscode` (below).

### Totals calculation (FR-07, I-1, D-42; task 016)

The five totals and every Adjustment line's `sprk_amount` are written only by `InvoiceTotalsService.RecalculateAsync(invoiceId)` (`Services/Finance/InvoiceTotalsService.cs`), which applies `InvoiceTotalsCalculator` (`Services/Finance/InvoiceTotalsCalculator.cs`). No API takes a total from a caller. Definitions:

- fees total = sum of `sprk_amount` of Fee lines; expenses total = the same for Expense lines (both as billed);
- a Fee or Expense line's adjustment = `sprk_amount - sprk_approvedamount` when its status is Reduced, `sprk_amount` when Disallowed, 0 otherwise; line-adjustments total = the sum;
- a Percent Adjustment line = `value% x (fees total - sum of the Fee lines' adjustments)`, expenses never in the base, every percent line on the same base (they do not compound); a Fixed Adjustment line = its value; invoice-adjustments total = the sum of the Adjustment-line amounts;
- approved total = `sprk_totalamount` (billed) - line-adjustments total - invoice-adjustments total.

**Rounding.** `decimal` arithmetic; each computed adjustment and each total is rounded to 2 decimals with `MidpointRounding.AwayFromZero` (half a cent rounds up, not to even). USD only in R1; no currency conversion.

**Refusals (FR-32), stable codes, nothing written.** `invoice-totals.approved-total-negative`; and, fail-closed, `invoice-totals.line-cost-type-unknown` (a line with no Fee / Expense / Adjustment cost type), `invoice-totals.line-approved-amount-invalid` (Reduced line with no approved amount, or one outside 0 .. billed amount), `invoice-totals.adjustment-invalid` (Adjustment line with no method / value, a negative value, or a percent above 100), `invoice-totals.billed-total-missing` (`sprk_totalamount` empty), `invoice-totals.total-below-storable-minimum` (a fees, expenses or adjustments total that credit lines (D-121) would take below 0, which the column minimum of 0 cannot store; this replaced the earlier refusal of any negative Fee or Expense line, `invoice-totals.line-amount-negative`). A Fee or Expense line may be a credit: it keeps its negative `sprk_amount` and is summed as a signed value. An Adjustment line's own `sprk_linestatus` is never read; only Fee and Expense lines are decided.

### Status (`statecode` / `statuscode`, FR-06, D-41)

| statuscode | Label | statecode | Origin |
|---|---|---|---|
| 1 | In Review | 0 (Active) | Existing value 1, relabelled from "Active". The default status of statecode 0. Existing invoices (10 in dev, all at 1) become In Review with no data change. |
| 659490000 | Approved | 0 (Active) | Inserted |
| 659490001 | Sent to AP | 0 (Active) | Inserted |
| 659490002 | Paid | 1 (Inactive) | Inserted |
| 659490003 | Rejected | 1 (Inactive) | Inserted |
| 659490004 | Returned to Firm | 1 (Inactive) | Inserted |
| 2 | Cancelled | 1 (Inactive) | Existing value 2, relabelled from "Inactive" (dev held zero invoices at statecode 1 when relabelled). The default status of statecode 1. |

Values are in the publisher option-value range (prefix 65949), as for `sprk_todo`. The statecode labels stay "Active" / "Inactive". Allowed transitions (FR-06): In Review -> Approved | Rejected | Returned to Firm | Cancelled; Approved -> Sent to AP | Cancelled; Sent to AP -> Paid. They are enforced by the lifecycle service (task 014), not by metadata. **Creation (D-101, task 049 part A):** an invoice is created directly In Review or Rejected (an automatic rejection) and in no other status; `IInvoiceLifecycleService.BuildCreateRequest` builds the `CreateRequest` carrying the paired statecode + statuscode, and refuses any other status, or a row that already carries a status column, so the lifecycle service stays the only owner of status. The request is returned, not sent, so the 049 processor can put it in one `ExecuteTransactionAsync` with the lines. Creating directly in Rejected (statecode 1) is the shape to verify on the first live run (task 079); if Dataverse refuses it, the builder changes to Create In Review plus a state change in the same transaction.

**Spend (D-103, supersedes D-98; ontology D-166).** `FinanceBaseValues.CountedInvoiceStatuses` = Approved, Sent to AP and Paid, filtered on `statuscode` (never `statecode`: Paid is Inactive and still counts). In Review (pending), Rejected, Returned to Firm and Cancelled are not counted. The matter / project rollup, the Signal evaluator's Threshold sums and the FinancialCalculation tool all use this set. A new invoice is created without a status value and takes the `statuscode` default, In Review.
