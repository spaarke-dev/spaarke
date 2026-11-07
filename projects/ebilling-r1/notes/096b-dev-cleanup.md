# Task 096b scope 4: dev invoices created outside a submission (read-only list)

Taken 2026-11-07 from dev Dataverse by the main session (read-only queries). Nothing has been changed. Every step on dev waits for the owner's go in the ebilling session, with uac-r2 told before and after (EB-D-201).

## Method

An invoice was created by a submission when its id equals `InvoiceSubmissionPersister.InvoiceIdFor(submissionId)` for some `sprk_invoicesubmission` row (SHA-256 of `ebilling-invoice-{id}`, first 16 bytes). Dev has 17 invoices and 10 submissions. 6 invoices derive from a submission. The 11 below do not, and all 11 are In Review. No Inactive invoice is outside a submission.

## The 11 invoices and their dependents

| Invoice id | Name | Created | Lines | Documents | Communications | Decision Records | Signals |
|---|---|---|---|---|---|---|---|
| 0857647b-61cd-f111-a05d-0022482913fc | New Invoice Created 10-21-2026 from Outlook Add-in | 2026-10-21 | 0 | 0 | 0 | 0 | 0 |
| 05c8ef8d-8e5f-f111-a825-70a8a59455f4 | JX | 2026-06-03 | 0 | 0 | 0 | 0 | 0 |
| b09d85f7-2f06-f111-8407-7c1e520aa4df | INV-PAT-556322.01 | 2026-02-09 | 0 | 0 | 0 | 0 | 0 |
| 6f9641d0-0f09-f111-8406-7c1e525abd8b | INV-002 Commercial Real Estate | 2026-02-13 | 0 | 0 | 0 | 0 | 0 |
| bcf81d78-1f0c-f111-8342-7ced8d1dc988 | Invoice Number 3 Test Record | 2026-02-17 | 0 | 0 | 0 | 0 | 0 |
| d6e0a25f-e10c-f111-8342-7ced8d1dc988 | Invoice Number 4 Test Record | 2026-02-18 | 0 | 0 | 0 | 0 | 0 |
| f749a11e-2a7b-f111-ab0e-7ced8ddc4cc6 | Invoice Wizard | 2026-07-08 | 0 | 1 | 0 | 0 | 0 |
| a1652ca5-437b-f111-ab0e-7ced8ddc4cc6 | Invoice 7/8/2026 | 2026-07-08 | 0 | 0 | 0 | 0 | 0 |
| 9050d1b8-467b-f111-ab0e-7ced8ddc4cc6 | New invoice 7/9/2026 | 2026-07-08 | 0 | 0 | 0 | 0 | 0 |
| 55328b00-477b-f111-ab0e-7ced8ddc4cc6 | New invoice 3 7/9/2026 | 2026-07-08 | 0 | 1 | 1 | 0 | 0 |
| 4c0f4d12-9e9c-f111-b8de-7ced8ddc4cc6 | INV-005 | 2026-08-20 | 1 | 0 | 0 | 0 | 0 |

The checks covered:
- lines (`sprk_billingevent.sprk_invoice`);
- documents (`sprk_document.sprk_invoice`);
- communications (`sprk_communication.sprk_regardingrecordid`);
- events and To Dos (`sprk_event.sprk_regardingrecordid`: none);
- Decision Records and Signals (`sprk_regardinginvoice`: none).

## Options per record (the owner chooses)

- **Cancel** through the lifecycle (FR-06 allows In Review to Cancelled). The record and its dependents stay, closed. This is the only option that keeps the three records with dependents intact.
- **Delete** the invoice. The 8 with no dependents can be deleted outright. For the other 3, the dependents (1 line, 2 documents with their stored files, 1 communication) would need deleting or detaching first.

**Recommendation:** delete the 8 with no dependents, and Cancel the 3 with dependents ("Invoice Wizard", "New invoice 3 7/9/2026", "INV-005").

## Done 2026-11-07 (owner go in the ebilling session; uac-r2 agreed to delete instead of Cancel: no BFF route cancels an invoice)

Run as ralph.schroeder@spaarke.com on SPAARKE DEV 1. uac-r2 was told before and after.

1. 20:00:51Z and 20:00:59Z: the two documents `d36e37a2-3ccb-f111-a05d-0022482913fc` and `58328b00-477b-f111-ab0e-7ced8ddc4cc6` were deleted through the dev BFF `DELETE /api/documents/{id}`. Both answered 200, and the file and row went together.
2. About 20:01–20:02Z: line `5ea5ad2a-9e9c-f111-b8de-7ced8ddc4cc6` and communication `a62a5f02-477b-f111-ab0e-7ced8ddc4a05` were deleted in Dataverse.
3. 20:03Z: the 11 invoices listed above were deleted in Dataverse (11/11).

Afterwards, dev has 6 invoices, all derived from their submission, and 0 non-derived. This is the 165c baseline. The membership junction rows of the two documents go stale until the nightly reconciliation; uac-r2 checks them.
