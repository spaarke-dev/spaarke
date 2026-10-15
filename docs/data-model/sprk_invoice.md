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

`sprk_totalamount` (existing, money) stays the **billed total**. Each new money column also gets a system `_base` companion (base-currency value) from Dataverse. `sprk_invoicestatus` is unchanged here (task 015 retires it).

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

Values are in the publisher option-value range (prefix 65949), as for `sprk_todo`. The statecode labels stay "Active" / "Inactive". Allowed transitions (FR-06): In Review -> Approved | Rejected | Returned to Firm | Cancelled; Approved -> Sent to AP | Cancelled; Sent to AP -> Paid. They are enforced by the lifecycle service (task 014), not by metadata.
