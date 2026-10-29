| Logical Name        | Schema Name         | Display Name  | Attribute Type   | Description                            | Custom Attribute | Type   | Additional data                     |
| ------------------- | ------------------- | ------------- | ---------------- | -------------------------------------- | ---------------- | ------ | ----------------------------------- |
| sprk_billingeventid | sprk_BillingEventId | Billing Event | Uniqueidentifier | Unique identifier for entity instances | False            | Simple |                                     |
| sprk_invoice        | sprk_Invoice        | Invoice       | Lookup           |                                        | True             | Simple | Targets:<br><br>sprk_invoice        |
| sprk_invoicename    | sprk_InvoiceName    | N/A           | Text             | N/A                                    | True             | Simple | Format: Text<br><br>Max length: 850 |
| sprk_name           | sprk_Name           | Name          | Text             |                                        | True             | Simple | Format: Text<br><br>Max length: 850 |

## Invoice Line (eBilling R1, task 010, D-18)

`sprk_billingevent` is the **Invoice Line**: display name "Invoice Line" / "Invoice Lines" (logical name unchanged). Columns below were added or repurposed to dev by `scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1` (idempotent, additive; 2026-10-15). The table above is the older generated stub; the full pre-R1 column list is in `docs/data-model/sprk_financial-related-entities.md`.

### Added columns

| Logical name | Type | Display name | Notes |
|---|---|---|---|
| `sprk_resolvedtimekeeper` | Lookup -> `sprk_timekeeper` | Timekeeper | Roster timekeeper. The billed name stays in `sprk_timekeeper` (text). |
| `sprk_approvedrate` | Money (precision 2, 0 .. 1,000,000,000) | Approved Rate | |
| `sprk_approvedamount` | Money (precision 2, 0 .. 1,000,000,000) | Approved Amount | |
| `sprk_adjustmentreason` | Multiline text (2000) | Adjustment Reason | |
| `sprk_reviewedby` | Lookup -> `systemuser` | Reviewed By | |
| `sprk_reviewedon` | Date and time (user local) | Reviewed On | |
| `sprk_linestatus` | Choice (local) | Line Status | Options below. "Pending" is shown as Processing in the review UI (D-58). |
| `sprk_taskcode` | Lookup -> `sprk_billingcode` | Task Code | Created 2026-10-15 by re-running the script after task 018 created `sprk_billingcode` (was deferred until then). See [sprk_billingcode.md](sprk_billingcode.md). |
| `sprk_activitycode` | Lookup -> `sprk_billingcode` | Activity Code | Created with `sprk_taskcode`, as above. |
| `sprk_expensecode` | Lookup -> `sprk_billingcode` | Expense Code | Created with `sprk_taskcode`, as above. |
| `sprk_taskcodeasbilled` | Text (100) | Task Code (as billed) | |
| `sprk_activitycodeasbilled` | Text (100) | Activity Code (as billed) | |
| `sprk_expensecodeasbilled` | Text (100) | Expense Code (as billed) | |
| `sprk_codingconfidence` | Decimal (precision 4, 0 .. 1) | Coding Confidence | |
| `sprk_extractionconfidence` | Multiline text (100000) | Extraction Confidence | JSON: per-field confidence, source page and bounding box (FR-04 / FR-21). |
| `sprk_adjustmentmethod` | Choice (local) | Adjustment Method | Options below. For Adjustment lines. |
| `sprk_adjustmentvalue` | Decimal (precision 4, 0 .. 1,000,000,000) | Adjustment Value | Percent or fixed amount, per method. |
| `sprk_adjustmentrule` | Lookup -> `sprk_policyversion` | Adjustment Rule | Optional (FR-32). |
| `sprk_regardingworkassignment` | Lookup -> `sprk_workassignment` | Regarding Work Assignment | FR-05 core-record lookup (see below). |
| `sprk_regardingrecordtype` | Lookup -> `sprk_recordtype_ref` | Regarding Record Type | ADR-024 resolver field. |
| `sprk_regardingrecordid` | Text (100) | Regarding Record Id | ADR-024 resolver field (length as on `sprk_invoice`). |
| `sprk_regardingrecordname` | Text (100) | Regarding Record Name | ADR-024 resolver field (length as on `sprk_invoice`). |
| `sprk_regardingrecordurl` | URL (250) | Regarding Record URL | ADR-024 resolver field (as on `sprk_invoice`). |
| `sprk_regardingrecordnumber` | Text (1000) | Regarding Record Number | ADR-024 resolver field (as on `sprk_invoice`). |

All new lookups: relationship delete = RemoveLink, assign / share / unshare / reparent / merge = NoCascade. All new columns are optional (no required level); resolver fields on `sprk_billingevent` are not business-required (unlike `sprk_regardingrecordtype` on `sprk_invoice`) so legacy rows and non-eBilling creates stay valid; the server writes them (task 013).

### Repurposed / relabelled columns

| Logical name | Change |
|---|---|
| `sprk_costtype` (global choice) | Option **Adjustment = 100000002** added; Fee 100000000 and Expense 100000001 unchanged. This is the line type; there is no `sprk_linetype` column. |
| `sprk_timekeeper` (text, 100) | Display name changed to "Timekeeper (as billed)"; logical name and type unchanged. Holds the name as extracted from the invoice. |
| `sprk_matter`, `sprk_project` (typed lookups) | Reused as the line's Matter and Project core lookups. No `sprk_regardingmatter` / `sprk_regardingproject` is added to this table. |
| `sprk_amount` | **Minimum lowered 2026-10-19 (D-121, task 049 part A): 0 -> -922337203685477**, the minimum `sprk_rate` already has, by `scripts/ebilling/Deploy-EbillingBillingEventAmountRange.ps1` (idempotent, widen-only; the maximum 1,000,000,000 and precision 2 are unchanged). A credit line (a reversal, D-105) keeps its negative amount as billed; `InvoiceTotalsCalculator` sums it as a signed value. Not yet run on dev (owner's OK); 053 adds it to SpaarkeMaster. |
| `sprk_quantity`, `sprk_rate`, `sprk_eventdate`, `sprk_description`, `sprk_invoice`, `sprk_linesequence` | Unchanged; they are the line's billed values (FR-04). `sprk_quantity` and `sprk_rate` already accepted negatives. |

**Credit lines and the stored totals.** `sprk_amount` was widened by D-121, and the invoice's six stored totals (`sprk_totalamount`, `sprk_feestotal`, `sprk_expensestotal`, `sprk_lineadjustmentstotal`, `sprk_invoiceadjustmentstotal`, `sprk_approvedtotal`) by D-127 (`scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1`; see `sprk_invoice.md`). `InvoiceTotalsCalculator` sums credits signed, so an all-credit invoice (a credit memo) has negative totals that are stored as computed; there is no `invoice-totals.total-below-storable-minimum` refusal. A reviewer may disallow a credit line (D-128); it raises the payable total. The automatic NonBillableActivity suggestion still never proposes disallowing a negative amount (D-105).

### Option values

| Column | Options |
|---|---|
| `sprk_linestatus` | 100000000 Pending, 100000001 Clean, 100000002 Flagged, 100000003 Accepted, 100000004 Reduced, 100000005 Disallowed |
| `sprk_adjustmentmethod` | 100000000 Percent, 100000001 Fixed |
| `sprk_costtype` (global) | 100000000 Fee, 100000001 Expense, 100000002 Adjustment |

### Core record on a line (FR-05, D-75)

A line's core record is read from the line's **own** lookups, never through `sprk_invoice`: exactly one of `sprk_matter`, `sprk_project`, `sprk_regardingworkassignment` is set; the five resolver fields describe it. Derivation and the one-lookup guard are task 013. `sprk_regardingworkassignment` is a filing lookup here, not an access stamp: `sprk_billingevent` is not in `CoreAncestorResolver.StampSourceColumns` and no writer asks the resolver to stamp it (task 010 notes, "ApplyStamps analysis").
