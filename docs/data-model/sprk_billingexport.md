# sprk_billingexport (Billing Export)

> **Project**: ebilling-r1, task 023 (FR-13, FR-41, D-21, D-56)
> **Last Updated**: 2026-10-15
> **Script**: `scripts/ebilling/Deploy-EbillingExportSchema.ps1` (idempotent, additive; a second run changes nothing)
> **Solution**: `SpaarkeMaster` (unmanaged, in dev). Solution source, forms and views are synced by task 029.

One AP export batch: the approved invoices a finance user handed to AP in one run, the files produced for them, and the field mapping used. It is the record of "what was sent to AP". Without it an invoice could be exported twice (double payment) and a past batch file could not be reproduced after a mapping change (FR-41). Created additively in dev on 2026-10-15; no plugin, business rule, calculated or rollup column, or flow (ADR-002). The writer is task 150 (batch creation, invoice count, total, files, mapping snapshot, moving invoices to Sent to AP); nothing in Dataverse computes the count or the total.

User-owned, entity set `sprk_billingexports`, primary name `sprk_name` (text 100, the batch id). Created by / created on are the platform columns `createdby` / `createdon`.

## Columns

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (100), primary name, application-required | The batch id (D-56 column `BatchId`). |
| `sprk_invoicecount` | Whole number, min 0, max 1,000,000 | Number of invoices in the batch. Written by task 150. |
| `sprk_totalamount` | Money (precision 2, minimum -922337203685477 after D-129 (4), maximum 1,000,000,000, plus `_base` companion) | Sum of the approved totals of the batch's invoices; an approved credit memo has a negative approved total (D-127), so the sum can be negative. The minimum was 0 and is lowered by `scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1` (idempotent, widen-only; not yet run on dev, owner's OK; 053 adds it to SpaarkeMaster). Written by task 150. |
| `sprk_formats` | Multi-select choice (local) | The formats this batch produced: CSV 100000000, JSON 100000001, XML 100000002. XML is reserved (D-21): nothing writes it in R1. |
| `sprk_exportstatus` | Choice (local), no default | Generating 100000000, Completed 100000001, Failed 100000002. |
| `sprk_mappingsnapshot` | Multiline text (1,048,576) | The field mapping used to produce the files, snapshotted per batch (FR-41): a later mapping change does not alter how a past batch is read. |
| `sprk_failurereason` | Text (200) | Why the batch failed, when `sprk_exportstatus` is Failed. |

The system `statecode` / `statuscode` keep their defaults (Active / Inactive) and carry no export meaning.

## Lookups

All use delete = RemoveLink and NoCascade for assign, merge, reparent, share and unshare. None is required: a batch in Generating or Failed status has no file yet, and `ApplicationRequired` would not be enforced on API writes anyway.

| On | Logical name | Target | Relationship | Notes |
|---|---|---|---|---|
| `sprk_billingexport` | `sprk_csvdocument` | `sprk_document` | `sprk_billingexport_csvdocument_document` | The CSV file, stored as a Document in the E-Billing Output Files container (D-44, D-55). |
| `sprk_billingexport` | `sprk_jsondocument` | `sprk_document` | `sprk_billingexport_jsondocument_document` | The JSON file, same container. |
| `sprk_invoice` | `sprk_billingexport` ("Export Batch") | `sprk_billingexport` | `sprk_invoice_billingexport_billingexport` | See "Invoice in one batch" below. |

FR-13 says "file document" (singular); FR-41 produces a CSV and a JSON file, so there is one document lookup per format and `sprk_formats` records which files a batch produced.

## Invoice in one batch

`sprk_invoice.sprk_billingexport` is a single-valued lookup, so an invoice row references at most one batch; that is what structurally keeps an invoice out of two batches. Metadata alone cannot stop a writer from overwriting the lookup with a second batch: the batch writer (task 150) selects only Approved, not-yet-exported invoices (an empty `sprk_billingexport` is part of "not yet exported") and moves them to Sent to AP in the same operation. The lookup is not a unique key and not required: invoices that are not yet exported have it empty.

The script stops with exit 2 before any write if `sprk_invoice` already carries a column named `sprk_billingexport` that is not this lookup, or any lookup to another export or batch table (the project escalation trigger); in dev neither existed.

## Notes for later tasks

- A `Failed` batch holds no moved invoices; task 150 owns that rule.
- Not in the secure-record family (task 013): its only lookups are the export documents, and a batch spans many core records, so no single secure root owns it and the Secure Record team never owns a row. Whether a batch of secure invoices needs its own read restriction is an open owner / unified-access-control-r2 question (task 013 notes, defer-issues). It does not carry `sprk_createdbyperson`.
- The AP mapping default is the D-56 column set; the mapping itself is configuration (the settings record, D-59), and this table stores only the snapshot used.
