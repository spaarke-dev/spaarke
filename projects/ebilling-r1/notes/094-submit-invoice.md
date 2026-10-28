# Task 094 — Submit Invoice from a source Document

> Project: ebilling-r1 (model `procedure-and-file-model.md` §5, §9.1; owner K, EB-D-158). Branch `task/ebill-094-submit-invoice` (merged `origin/work/ebilling-r1` @ `ecb534d03e` and 098 @ `2f50cddec9`). Rigor FULL, opus @ high. Built and tested only: **no deploy, no live Dataverse or Azure write, no Key Vault change.** `StoredFileReconcileJob` stays disabled.

## 1. What was built

| Piece | File | Change |
|---|---|---|
| Route | `Api/Finance/InvoiceSubmissionEndpoints.cs` | `POST /api/finance/invoice-submissions/from-document` (`SubmitInvoiceFromDocument`). The finance filter checks Read + AppendTo on the core record and AppendTo on the firm; step 0 refuses a flagged-external caller before any record read. Then `PrepareSourceAsync` (step 1, then A), then `SubmitExistingDocumentAsync` (050a's intake, unchanged checks: D-140 chain, firm link, review status, duplicate). |
| Step 1 + A | `InvoiceSubmissionEndpoints.PrepareSourceAsync` | The caller's own Read on the document and on its communication, through the gate variant of `CallerRecordAccessProbe` (an unknown record and an unreadable one give the same 403 `source_forbidden`). A communication that is not the document's own is 422 `source_communication_mismatch`. **A:** an unfiled communication is filed to the core record AS THE CALLER through the real re-file core (`CommunicationEndpoints.RefileCommunicationAsync`): Write on the communication (else its 403 `COMMUNICATION_REFILE_FORBIDDEN`), its own answers otherwise. A is the one write Submit Invoice makes as the caller and the only one it never undoes. Source services resolve from `http.RequestServices` after the filters, so the filters still deny first. |
| Confirm route | `Api/Finance/FinanceEndpoints.cs` | `ConfirmInvoiceReview` runs the same `PrepareSourceAsync` (document Read already checked by its filter): it now checks Read on the communication and runs A too. |
| Job (step 2) | `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` | A source-document submission (`SubmissionSnapshot.FileSource == SourceDocument`) reads the document's version, then stages a COPY of its bytes in the Finance file store under `StoredFileIds.ForSourceVersion(submission, version)` (Invoice PDF, Firm-visible No, source document, source communication, version, SHA). An email archive document is Failed `document_unavailable`. No commit discards the staged copy. `SourceChanged` with the same bytes re-stages at the new version and persists once; other bytes, or a second change, are transient. Step 0: a Failed submission with an undo code runs the held compensation. |
| Persister (steps 3–4) | `Services/Finance/Submission/InvoiceSubmissionPersister.cs` | Step 3 re-reads the live row (version, link, review status) and runs `CheckSourceFilingAsync` (filing chain + isolation walk). A version differing from the staged one is `SourceChanged`. Step 4: ONE transaction: invoice, the copy's Stored File row, lines, approval, the guard (`UpdateRequest` of the document with `IfRowVersionMatches` at the staged version, writing back only `sprk_invoicereviewstatus` as read), the submission. No document link, no owner write, no share write. A transaction failure deletes the staged blob. |
| Commit gate (steps 5–6) | `Services/Finance/Submission/SubmissionCommitGate.cs` (new) | Shared by the persister (after its commit), the after-steps' resume (first) and the handler (held undo). Step 5: re-runs the source check for every source of the invoice's files and checks the source's secure roots ⊆ the copy's; any fault is a failure (uac-r2 condition 1). Pass: flip every file to Servable. Fail: **undo** — Failed + reason + admin note first, then delete Stored File blob and row, approval, lines, invoice in that order, then clear the four submission columns (only when something was left). Step 6: if any file is already Servable, step 5 is skipped. Idempotent; a delete fault is "held" (Failed, file Not yet servable) and completed by a later delivery. Every channel (uploads too: 092 review item 2). |
| Delete pre-check | `Services/DocumentCheckoutService.cs`, `Api/DataverseDocumentsEndpoints.cs`, `StoredFileService.CountCopiesOfDocumentAsync` | `sprk_storedfile.sprk_sourcedocument` is Delete = Restrict, so a document a Stored File was copied from is refused with the existing 409 has-children answer BEFORE its SPE file is touched, on both delete paths. A count read fault fails closed (503 / 500, nothing deleted). |
| Retired | `InvoiceDocumentRefile.cs` (deleted), `FinanceModule.cs`, `InvoiceSubmissionAfterSteps.cs` | 050a's re-file of the confirmed document under the invoice, its after-step, and the indexing after-step (owner K; DEF-047). |
| Admin text, codes | `AdminAttentionText.cs`, `SubmissionReasonCodes.cs`, `InvoiceSubmissionContracts.cs` | `UndoneNote`; `source_check_failed`; `source_forbidden`, `source_communication_mismatch`, `document_rejected`. |

## 2. Failure outcomes (model 9.1)

| Step | Condition | Outcome |
|---|---|---|
| 0 | flagged-external / no systemuser / flag fault | 403 before any record read |
| filter | no Read+AppendTo on core, no AppendTo on firm | 403, nothing read |
| 1 | no Read on document or communication (or unknown) | 403 `ebilling.submission.source_forbidden` |
| 1 | communication not the document's | 422 `source_communication_mismatch` |
| A | no Write on an unfiled communication | 403 `COMMUNICATION_REFILE_FORBIDDEN` (re-file route's own) |
| A | re-file core refuses | its answer as is (e.g. 404 on a core record without AppendTo) |
| intake | filed under another core record (D-140 chain) | 409 `document_core_record_mismatch` |
| intake | chain or document read fault | 503 `record_unavailable` |
| 2 | email archive document | Failed `document_unavailable` |
| 3 | live version ≠ staged | `SourceChanged` → re-stage once (same bytes) or transient |
| 4 | guard faults (source written since staging) | rolled back; same as step 3 |
| 4 | transaction failure | staged blob deleted; Retry |
| 5 | re-filed elsewhere / roots not covered / re-run fault / no or unflippable file | undone; Failed `source_check_failed` or `file_unservable`; job Poisoned |
| 5 | undo delete fault | held: Failed, file Not yet servable; next delivery completes it |

## 3. §11 component justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Route `POST .../invoice-submissions/from-document` | `POST /api/finance/invoice-review/confirm` (050a) | The confirm is the review page's action on an already-reviewed document with its own filter; Submit Invoice needs Read on the source and its communication plus A before the intake, and is the contract 118 and 095 call | Submit Invoice has no entry point; 118's command has nothing to call. |
| `PrepareSourceAsync` + `SourceServices` | `CommunicationEndpoints.RefileCommunicationAsync` (reused, not copied) | It composes the re-file core; the confirm route reuses it | Step 1's source checks and A would be duplicated per route, or absent. |
| `SubmissionCommitGate` | 092's `FlipUploadFileAsync` in the persister | Moved and generalized (one gate, every channel) instead of a second flip | No step 5 re-run, no undo: a source re-filed after the commit leaves a Servable copy under the wrong record (uac-r2 conditions 1, 3). |
| `StagedSourceCopy`, `SubmissionFileSource`, `SubmissionSnapshot.FileSource`, `StagedFiles` | `SubmissionSnapshot.DocumentId` | A null document no longer decides the channel (092 review item 1) | The job cannot tell a copy from an upload; decided per channel and pinned per channel by tests. |
| `StoredFileService.DeleteForUndoAsync`, `ReadRowsForInvoiceAsync`, `StoredFileCompensation` | `DeleteStagedAsync` (refuses when a row exists) | The undo must delete a row that exists | The undo cannot remove the file. |
| `StoredFileService.CountCopiesOfDocumentAsync`, `DocumentCheckoutService.CountStoredFileCopiesAsync` | `CountChildDocumentsAsync` / `GetDocumentsByParentAsync` | Added to the same count, the same 409 | Restrict refuses the document row after its SPE file was deleted (ontology-r1 088 gate F1). |
| `SourceFilingVerdict`, `SourceFilingCheck`, `SourceIsolationFlags`, `ExistingDocumentIsolation.Roots` | `ExistingDocumentIsolationCheck` | Extended in place | Step 5 cannot compare roots. |
| Codes `source_check_failed`, `source_forbidden`, `source_communication_mismatch`, `document_rejected`; `UndoHeld`; `UndoneNote` | `SubmissionReasonCodes`, `InvoiceSubmissionErrors`, `AdminAttentionText` | Extended in place | The outcomes have no stable code (118, 095 contracts pin them). |

DI: `SubmissionCommitGate` replaces `InvoiceDocumentRefile` (one registration out, one in). No package, no Dataverse column. BFF placement: Finance submission services in `Sprk.Bff.Api`, same as 092 (no new module).

## 4. Seeding proofs (POML)

| # | Seed | Result | Restored |
|---|---|---|---|
| 1 | `SubmissionCommitGate.SourcesStillCoveredAsync` returns true unconditionally | `Source_RefiledAfterTheCommit_IsUndone` fails (with the roots, re-run-fault and idempotence undo tests and the happy path's read count): 5 failed | yes, file identical to before |
| 2 | `CompensateAsync` skips `DeleteForUndoAsync` | the refiled-source undo test fails, with roots, re-run fault, idempotence, upload flip-failure undo and undo-held: 6 failed | yes |

Guard controls: `SubmitInvoiceSourceUntouchedGuardTests.Detector_MustFireAndMustNotFire`. Delete pre-check: must-fire (external 409, internal count, Dataverse route 409, read fault 503) and must-not-fire (unreferenced document deletes; another document's copies do not refuse).

## 5. Tests and gate

- Builds: Sprk.Bff.Api.Tests, Spaarke.ArchTests, Spaarke.Core.Tests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests: all succeed.
- Unit broad filter: 9637 passed, 7 skipped, 0 failed.
- Document delete and access-control suites (not in the broad filter; the pre-check touches them): 1049 + 4107 passed, 0 failed.
- ArchTests: 1567 passed, 0 failed (full).
- Sprk.Bff.Api.IntegrationTests: 199 passed, 8 skipped. Spe.Integration.Tests: 353 passed, 23 skipped.
- Copy rule end to end: `CopyRule_EndToEnd_LosingReadOnTheSource_RefusesTheCopy` serves the committed copy through 090's real `MapStoredFileEndpoints` over the same store, then 404 once the caller loses Read on the source.
- Retired: `SubmitInvoiceSourceUntouchedGuardTests` (scan of Submission services, route, handler) plus the happy path (guard writes only `sprk_invoicereviewstatus`; invoice has no document link; no owner write) plus `RefileWriterReconcilerRegistrationTests` (type gone).

## 6. Publish size (Compress-Archive, short paths, 192 files each)

| Build | Zip bytes |
|---|---|
| master `90f3cab765` (C:\wt094m) | 39,725,046 |
| branch before 094 (`584adec17e`, C:\wt094h) | 40,531,161 |
| branch with 094 (C:\wt094b) | 40,553,752 |

094's own delta: **+22,591 bytes**. Cumulative ebilling-r1 vs master: +828,706 bytes. No package added (csproj and `Directory.Packages.props` unchanged), so no new CVE.

## 7. For 118 (command) and 095 (from email)

- 118: `POST /api/finance/invoice-submissions/from-document`, body `{communicationId, documentId, coreRecord: {kind: matter|project|workassignment, id}, firmId, firmInvoiceNumber, poNumber?}`; 202 `{submissionId, jobId, trackingNumber, statusUrl}`; refusals per §2 (ProblemDetails with `errorCode`).
- 095: the gate already reads its sources from the file rows (`sprk_sourcedocument`), so a from-email copy is re-run and undone like a document copy. The `ISubmitInvoiceFromEmail` implementation is folded in once 095's fix head is pushed (coordinator), with the uac-r2 095 K5 hand-offs: `sprk_createdbyperson` = caller on everything it creates, A on the duplicate path, Read on the `.eml` and its communication; no reliance on `IdempotencyFilter`'s old double call; no assumption that a generation's creator matches earlier ones; bytes staged from the `.eml` as read back (base64 parts).

## 8. Dependencies, open questions, known limits

- **086 (live gate):** until `sprk_storedfile` is a `SecureChildLineage` child, the copy's roots are its invoice's; the gate switches to the file rows' own roots when 086 lands (`FileRootsAsync`). No change to `SecureChildLineage` or the role JSON here.
- **Open — indexing (owner K, DEF-047/#2351):** no submission is indexed since 094; the confirm path lost its indexing.
- **Open — D-139:** an unfiled document (nothing in its chain) may still be submitted to any core record; A files only the communication.
- **Open — A and the undo:** A survives a later undo by design (the note says so). A refusal by A is returned as the re-file route's answer, not a submission code.
- **Open — confirm route:** it now requires Read on the document's communication and runs A; a reviewer who could confirm before may be refused if they cannot read the communication.
- **Open — Delete privilege:** deleting a document with a copy is now 409 until the Stored File is removed; there is no user path to remove a Stored File.
- **Known limit:** when the undo meets a file row another writer already deleted, its blob is left for the reconcile job (disabled); never servable (no row).
- **Known limit:** `Source_RefiledAfterTheCommit` and the roots test exercise step 5 through the isolation fake; the live walk is 098/086's.
