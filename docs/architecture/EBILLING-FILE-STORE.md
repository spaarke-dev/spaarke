# E-Billing File Store Architecture

> **Last Updated**: 2026-11-25
> **Purpose**: How the e-billing feature stores, protects, serves and removes files (Procedure source files, invoice PDFs and supporting files, export files): the one table, the one service, the blob layout, the upload and download sequences, the copy rule, Submit Invoice, the firm file route and the known limits. Describes what is in code; where the code and the model note differ, the code is described and the model note is the rationale.
> **Rationale and decisions**: [procedure-and-file-model.md](../../projects/ebilling-r1/notes/procedure-and-file-model.md) (EB-D-148), sections 4, 9, 10. This page restates no decision log.
> **Data model**: [sprk_storedfile.md](../data-model/sprk_storedfile.md), [sprk_procedure.md](../data-model/sprk_procedure.md)

## Overview

Before EB-D-148 an e-billing file was a `sprk_document` in a SharePoint Embedded container. Procedure source files, invoice PDFs, supporting files and AP export files are now rows in one Dataverse table, `sprk_storedfile`, with their bytes in Azure Blob Storage. Nothing about the feature goes through SharePoint Embedded or the Document table any more, except that a Submit Invoice reads its bytes from an existing Document (the source) and a Stored File may point at it.

What the design guarantees:

- **A file is served only when its row is Servable**, its blob path is its own, and its scan verdict is clean. Every reader refuses anything else.
- **No storage URL or SAS ever reaches a client.** Every download goes through a BFF route that checks the caller first.
- **A file never outlives or outruns its parent by accident.** Rows and blobs are created in an order that leaves no servable file without both, and a scheduled job removes what a failed or out-of-band delete leaves behind.

## Component structure

One table, one service, one control.

| Piece | What it is | Code |
|---|---|---|
| Table | `sprk_storedfile`. One row per file; exactly one parent lookup (Procedure Version, invoice or billing export); status Not yet servable, Servable or Removed. | [sprk_storedfile.md](../data-model/sprk_storedfile.md) |
| Service | `StoredFileService` is the one owner of rows and blobs: stage, create the row, flip to Servable, serve, replace, withdraw. `StoredFileBlobs` is the blob half (managed identity, no SAS, scan tags). | `src/server/api/Sprk.Bff.Api/Services/Finance/Files/StoredFileService.cs`, `StoredFileBlobs.cs`, `StoredFileContracts.cs` |
| Reconcile | One hourly scheduled job (ADR-036) deletes orphans and reports rows stuck Not yet servable. | `Services/Finance/Files/StoredFileReconcileJob.cs` |
| Internal routes | Download, list and supporting-file upload under `/api/finance`. | `Api/Finance/StoredFileEndpoints.cs` |
| Firm routes | List and download of the firm-visible files of an invoice. | `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs`, `ExternalInvoiceSubmissionAuthorizationFilter.cs` |
| Control | The Invoice Files code page, embedded in the invoice form. It lists files from the list route and opens them through the content route; it never reads or writes Dataverse rows itself. | `src/solutions/InvoiceFiles/src/main.tsx` |

Other writers use the service and never touch the table or the container directly: the Procedure routes (`Api/Finance/ProcedureEndpoints.cs`, the version file), the submission persister (`Services/Finance/Submission/InvoiceSubmissionPersister.cs`) and the AP export (`Services/Finance/ApExportService.cs`).

### Ids

File ids are deterministic where a retry must land on the same row (`StoredFileIds` in `StoredFileContracts.cs`): `ForSourceVersion` (a submission and the source document's version), `ForAttachment` (a submission and an attachment's identity, for example an .eml part) and `ForUpload` (an upload submission). A caller's own upload takes a new random id (`StoredFileIds.New`).

## Blob layout and protection

| Item | Behaviour | Code |
|---|---|---|
| Containers | `procedures`, `invoice-files`, `billing-exports`, chosen by the file's purpose. | `StoredFileSchema.ContainerOf` |
| Blob name | `{organization id}/{parent table}/{parent id}/{file id}`. The first segment is the Dataverse environment's organization id, read from the `organization` row. The download checks that the parent id in the stored path equals the row's parent. | `StoredFileBlobName` in `StoredFileContracts.cs` |
| Write-once per content | A blob is created only if none of that name exists. A retry of the same file id with the same bytes and source is accepted and rewrites the identical verified bytes under the existing blob's ETag; different bytes or another source are refused and the blob is left unchanged. The rewrite moves the ETag, which stops a cleanup that judged the blob earlier from deleting it, and it makes Defender scan the file again. | `StoredFileBlobs.StageAsync` |
| Identity | The BFF managed identity only. No account key, connection string or SAS. | `StoredFileBlobs.cs` |
| Malware scan | Defender for Storage writes blob index tags (`Malware Scanning scan result`, `Malware Scanning scan time UTC`). A reader classifies them on demand: **Clean** only when every result tag says "No threats found" and exactly one scan-time tag is no earlier than the blob's last modification; **Malicious** when any says Malicious; **Pending** for everything else, including no tag. Nothing is queued and no Event Grid receiver exists. "Not scanned" is served only when `Finance:FileStore:MalwareScanningEnabled` is explicitly false. | `StoredFileBlobs.ClassifyScan` |
| Retention | A file lives as long as its parent row. There is no WORM policy, and storage soft delete is on. | |
| Configuration | `Finance:FileStore:BlobEndpoint`, `TenantId`, `MalwareScanningEnabled`, `OrphanGraceMinutes`, `NotServableAlertMinutes` (validated at start-up; the orphan grace may not be shorter than the submission job's longest retry horizon). | `Configuration/FinanceOptions.cs`, `FinanceOptionsValidator.cs` |

### Row lifecycle

1. **Stage.** Compute the SHA-256, write the blob with its metadata.
2. **Row.** Create the Stored File row as Not yet servable. If the row write fails, delete the staged blob.
3. **Flip.** Set Servable only after checking that the blob exists and its SHA-256 matches the row's, with an If-Match write on the row version (`StoredFileService.FlipToServableAsync`). A flip that finds the row changed is skipped, not forced.
4. **Withdraw.** Set Removed, never delete first, so a held undo cannot leave a Servable row without its blob.

### Orphan reconcile

`StoredFileReconcileJob` runs hourly under the scheduler lease. Every delete needs a successful not-found answer; a fault skips the unit and the next run revisits it.

- **A.** A row with no parent (the parent was deleted outside the BFF; the lookups are RemoveLink) or whose parent reads as not found: delete its blob, then the row.
- **B.** A blob with no row, older than `OrphanGraceMinutes`: delete it, unless its metadata names a submission that is not yet terminal.
- **C.** Rows still Not yet servable after `NotServableAlertMinutes`: one warning event with the count and ids, no repair.

The job is behind the dedicated `Ebilling:StoredFileReconcileEnabled` switch (`Configuration/EbillingOptions.cs`), a temporary guard that defaults to off; check the environment setting before relying on it.

## Upload and download

### Upload (caller)

The supporting-file route (`POST /api/finance/invoices/{id}/files`) and the Procedure version file (`PUT /api/finance/procedures/{procedureId}/versions/{versionId}/file`) both end in `StoredFileService.UploadForCallerAsync`:

```
caller
  -> step 0: refuse an external caller (a fault reading the flag refuses)
  -> the caller's own Write on the parent (gate variant)
  -> stage the blob under the new file id
  -> create the row, Not yet servable
  -> flip to Servable (SHA-256 verified)
```

Internal uploads are never Firm-visible and carry no source. The invoice route also refuses an Inactive invoice (`ebilling.invoice.inactive`), answers 409 `ebilling.invoice.files_pending` while the invoice's own submission is still storing its files, and refuses past the per-invoice file limit (`ebilling.files.limit_reached`). A Procedure version holds one Servable source file: `ReplaceForCallerAsync` makes the new file Servable first, then withdraws the earlier ones, and withdraws the new one if the earlier ones cannot be withdrawn.

### Download (internal route)

`GET /api/finance/files/{fileId}/content` runs its checks in an endpoint filter (`StoredFileAccessFilter`), in this order:

1. Step 0: an external caller is refused with 403 before any record is read.
2. Read the row app-only: unknown id 404, a read fault 503.
3. The caller's own Read on the parent (gate variant): otherwise 404.
4. The copy rule, for a Submit Invoice copy (next section): otherwise 404.
5. The row is Servable, its path is its own, and the scan verdict is Clean: otherwise 404; Pending 409 `file_scan_pending`; a store fault 503 `file_store_unavailable`.

Every 404 is the same uniform not-found response as an unknown id. The handler then opens the blob pinned to the ETag the verdict was read at (a blob changed since answers 409), writes one download audit event (`StoredFileAudit.Downloaded`), and streams with the row's content type, `nosniff`, `no-store` and a sandboxing content security policy.

## The copy rule

A Submit Invoice copy is the bytes of an attachment, copied out of an existing Document (the source) into the invoice's files. The copy keeps the source's ids (`sprk_sourcedocument`, `sprk_sourcecommunication`, `sprk_sourceattachmentidentity`).

**Rule (B):** on **every** download of the copy, the internal route applies the caller's own Read on the source document, gate variant. A later refile, Restricted flag, internal-only flag or Open-to-Private change of the source is therefore honoured for the bytes, without anything being stored on the copy. A source that is referenced cannot be deleted (Restrict on both source lookups). External callers never get a copy (it is never Firm-visible).

The source's own isolation is carried forward, not refused: the copy is Firm-visible No, is not served to the firm, and stays out of search and indexing. The invoice's own external visibility is the read-time predicate described in [sprk_invoice.md](../data-model/sprk_invoice.md).

## Submit Invoice, all or nothing

`POST /api/finance/invoice-submissions/from-document` and `/from-email` (`Api/Finance/InvoiceSubmissionEndpoints.cs`, `Services/Finance/Submission/FromEmailSubmissionService.cs`, `SubmitInvoiceFromEmailOperation.cs`) start the submission; a job completes it. The sequence, with the code that owns each step:

```mermaid
sequenceDiagram
    participant C as Caller
    participant A as BFF route
    participant J as Submission job
    participant B as Blob store
    participant D as Dataverse

    C->>A: Submit Invoice (source, marks)
    A->>A: 1 Confirm: caller's Read on source and communication, filing chain, isolation check
    A->>D: create the submission
    A->>J: start the job
    J->>B: 2 Stage the copy under a deterministic id
    J->>J: 3 Pre-check: re-run filing chain and isolation, read source state
    J->>D: 4 One transaction: invoice, lines, approval, submission update, Stored File (Not yet servable), source guard (IfRowVersionMatches)
    J->>J: 5 Re-run the gate immediately after commit
    alt gate passes
        J->>D: flip the Stored File to Servable (blob and SHA-256 checked)
        J->>J: after-steps (totals, Signals, indexing, notification)
    else gate fails or faults
        J->>D: withdraw rows, delete approval, lines, blobs, then rows and invoice in one transaction, clear the submission's commit columns
    end
    J->>J: 6 A resume re-runs step 5 and the flip; a detector reports rows still Not yet servable
```

1. **Confirm.** The caller must Read the source document and its communication. If the communication is unfiled it is filed to the target core record first (with the caller's rights), and its thread is resolved before the pre-check. Then the filing chain and `ExistingDocumentIsolationCheck`. Refusals: 409 `document_core_record_mismatch`; a read fault is 503.
2. **Stage.** Copy the bytes to a blob under the deterministic file id and record the source's version and SHA-256 (`StoredFileIds.ForSourceVersion` or `ForAttachment`). The blob is unreachable without a Servable row.
3. **Pre-check.** Re-run the filing chain and the isolation check and read the source's Restricted, internal-only and privacy state; an unreadable flag fails the submission.
4. **Commit, one transaction** (`InvoiceSubmissionPersister`). The invoice, its lines, the approval, the submission update and the Stored File as Not yet servable, plus the guard on the source row (`IfRowVersionMatches`). A guard failure retries the delivery; a failed transaction deletes the staged blob.
5. **Re-run the gate, before any after-step** (`Services/Finance/Submission/SubmissionCommitGate.cs`). It re-runs the filing chain and compares the roots above the source with the committed copy's roots. **Pass:** flip the Stored File to Servable (blob present and SHA-256 equal), then run the after-steps. **Fail or any fault:** undo everything, in this order: read the invoice's Stored File rows and withdraw them all at once; then the approval; the lines; the rows' blobs; then one transaction deleting the Stored File rows and the invoice together; then clear the commit's columns on the submission, leaving status Failed, the admin note and the correlation. The communication's filing from step 1 survives the undo. If any delete fails the submission stays Failed and its rows stay unservable (Not yet servable or Removed) until compensation finishes.
6. **Resume.** The resume path runs step 5 and the flip. A detector (reconcile job, part C) reports rows still Not yet servable after `NotServableAlertMinutes`.

**What cannot be withdrawn** once the after-steps run: Signal history, the automatic-rejection Decision Record, the indexing job and its search entry, and notifications. Step 5 runs before all of them. A change to the source after step 5 passes does not undo the invoice; the bytes stay correct through the copy rule.

The Outlook add-in `from-email` route saves the email as its .eml Document (own-Restricted, with the submission marker `sprk_document.sprk_restrictedbysubmission`, see [child-access-permission.md](../data-model/child-access-permission.md)), then copies the marked attachments' bytes out of the .eml. A correction is a new submission linked to the old invoice, which closes as Superseded at the new commit.

## The firm file route

Firms see an invoice's PDF only through two routes (`Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs`): `GET /api/v1/external/invoices/{id}/files` and `GET /api/v1/external/invoices/{id}/files/{fileId}/content`. There is no upload route; a firm's upload goes through its submission (Invoice PDF, Firm-visible Yes). The internal download route refuses external callers (step 0).

The one file gate for list and content (`ExternalInvoiceSubmissionAuthorizationFilter.IsFirmFile`, then `StoredFileService.JudgeServableAsync`):

- the file's own parent is this invoice;
- purpose Invoice PDF;
- Firm-visible Yes;
- no source document and no source communication (a Submit Invoice copy never passes);
- Servable and the path is its own;
- scan: Clean is served, Pending is listed as "Checking" and its content answers 409 `file_scan_pending`, Malicious is not listed and its content answers 404.

Before the gate the caller must see the invoice: the invoice's firm must match the caller's own firm (the vendor organization), the caller must have Read on the invoice and its core record, and the invoice's external-visibility predicate must pass. An invisible invoice or file gets the same 404 after the same work; a read fault gets 503.

## Export files

`ApExportService` (`Services/Finance/ApExportService.cs`) writes the CSV and JSON of a billing export as Stored Files: purpose Export file, parent `sprk_billingexport`, Firm-visible No, container `billing-exports`, created app-only through `StoredFileService.CreateForAppAsync`. They are internal: the only reader is the internal download route, and no firm route lists them. A batch that fails withdraws its export files (`WithdrawExportFileAsync`). An export is not a secure child of anything.

## Procedure source files

A Procedure Version's source file is a Stored File (purpose Procedure source, parent the version, container `procedures`). The version holds one Servable file; replacing it is `ReplaceForCallerAsync`. Rule drafting reads it through `OpenForProcessingAsync` (`Services/Jobs/Handlers/BillingRuleDraftJobHandler.cs`), which serves only a Servable row with a clean scan. See [sprk_procedure.md](../data-model/sprk_procedure.md).

## Known limits

- **Compensated bytes live 7 days.** Soft delete keeps a deleted blob for 7 days; there is no WORM policy.
- **A change to the source after the gate passes** does not hide the extracted fields from the core record's internal readers; the copy rule still governs the bytes.
- **Reused .eml.** The only reuse is the same user's own duplicate save of the same email to the same record. If that .eml lacks the invoice part, the SHA check fails the submission.
- **Cloud links and large attachments.** An invoice sent as a cloud link, or a PDF over 20 MB, cannot be submitted from the add-in.
- **A normal email save first.** If the email was first saved normally with the invoice ticked, an invoice-PDF Document already exists beside the new Stored File and is not moved.
- **Reconcile is guarded by a switch that defaults to off** (`Ebilling:StoredFileReconcileEnabled`), so orphans accumulate in an environment where it is not enabled.
- **The scan verdict is read on demand.** A file shows Checking until Defender writes its tags, and a rewrite of the same bytes (a retry) triggers a new scan.

## Constraints

- No Dataverse plugin and no business rule: the invariants (one parent, one Servable source file per version, the status flip) are BFF code (ADR-002).
- No new package: the blob gateway extends the existing storage access.
- Logs carry ids only, never file names or subjects.

## Related

- [sprk_storedfile.md](../data-model/sprk_storedfile.md), [sprk_procedure.md](../data-model/sprk_procedure.md), [sprk_invoice.md](../data-model/sprk_invoice.md), [sprk_billingexport.md](../data-model/sprk_billingexport.md), [child-access-permission.md](../data-model/child-access-permission.md)
- [procedure-and-file-model.md](../../projects/ebilling-r1/notes/procedure-and-file-model.md): rationale, decisions and review record
- [DATAVERSE-WRITE-PATH-ARCHITECTURE.md](DATAVERSE-WRITE-PATH-ARCHITECTURE.md): the write-path rules the file service follows
