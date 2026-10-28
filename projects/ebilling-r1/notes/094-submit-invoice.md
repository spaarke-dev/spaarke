# Task 094 — Submit Invoice from a source Document

> Project: ebilling-r1 (model `procedure-and-file-model.md` §5, §9.1; owner K, EB-D-158). Branch `task/ebill-094-submit-invoice` (merged `origin/work/ebilling-r1` @ `ecb534d03e`, 098 @ `2f50cddec9` and @ `f2446e638e`, 095 @ `85464826b6`). Rigor FULL, opus @ high. Built and tested only: **no deploy, no live Dataverse or Azure write, no Key Vault change.** `StoredFileReconcileJob` stays disabled. Core `5a5ffabfa8`; fix round (uac review + adversarial review addendum) on top, §9.

## 1. What was built

| Piece | File | Change |
|---|---|---|
| Route | `Api/Finance/InvoiceSubmissionEndpoints.cs` | `POST /api/finance/invoice-submissions/from-document` (`SubmitInvoiceFromDocument`). The body is validated first (every required field, the firm invoice number included: 400 before any rights query or read). The finance filter checks Read + AppendTo on the core record and AppendTo on the firm; step 0 refuses a flagged-external caller before any record read. Then `PrepareSourceAsync` (step 1, read-only), then `SubmitExistingDocumentAsync` (050a's intake) with step A handed to it as a hook. |
| Step 1 | `InvoiceSubmissionEndpoints.PrepareSourceAsync` | READ-ONLY. The caller's own Read on the document and on its communication, through the gate variant of `CallerRecordAccessProbe` (an unknown record and an unreadable one give the same 403 `source_forbidden`). A communication that is not the document's own is 422 `source_communication_mismatch`. Returns `SourcePreparation` (a refusal, or step A as `SourceFiling`). |
| Step A | `InvoiceSubmissionEndpoints.SourceFiling`, `InvoiceSubmissionRequest.FileSourceBeforeWrite` | Run by the intake after every read-only refusal (earlier submission, ended, other details, D-140 chain, isolation, firm link, owner) and before the create; on the earlier-submission path only once the answer is that submission itself. Step A decides "unfiled" from the D-140 chain (`InvoiceSubmissionService.IsCommunicationFiledAsync`); it refuses an isolated communication (`ExistingDocumentIsolationCheck.CheckCommunicationForFilingAsync`: 409 `document_core_record_mismatch`, a fault 503); then Write on the communication (else 403 `COMMUNICATION_REFILE_FORBIDDEN`) and the real re-file core (`CommunicationEndpoints.RefileCommunicationAsync`) AS THE CALLER, its own answers otherwise. A is the one write Submit Invoice makes as the caller and the only one it never undoes. A refusal after A filed says "No submission was saved" and "The communication was filed under the record first; that filing is kept." |
| Confirm route | `Api/Finance/FinanceEndpoints.cs` | `ConfirmInvoiceReview` runs the same read-only `PrepareSourceAsync`, with the document's Read checked there (its filter requires Write and Write\|Append, not Read), and hands the same step A to the intake. |
| Job (step 2) | `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` | A source-document submission (`SubmissionSnapshot.FileSource == SourceDocument`) reads the document's version, then stages a COPY of its bytes in the Finance file store under `StoredFileIds.ForSourceVersion(submission, version)` (Invoice PDF, Firm-visible No, source document, source communication, version, SHA), named by the submission's tracking number. An email archive document is Failed `document_unavailable`. The staged copy is discarded on every path except Committed, Undone, a Retry that links the invoice and the outcome-unknown Retry (a try/finally; the discard is row-guarded). `SourceChanged` with the same bytes re-stages at the new version and persists once; other bytes, or a second change, are transient. Step 0: a Failed submission with an undo code runs the held compensation; on a held undo at the final delivery the admin note says "undo held — operator action". |
| Snapshot / persister (steps 3–4) | `SubmissionEvaluationOutcome.cs`, `InvoiceSubmissionPersister.cs` | A source-document submission must be the id derived from its document (`ExistingDocumentSubmissionIdFor`), else `document_unavailable` (snapshot) / Refused (persister). Step 3 re-reads the live row (version, link, review status) and runs `CheckSourceFilingAsync` (filing chain + isolation walk). A version differing from the staged one is `SourceChanged`. Step 4: ONE transaction: invoice, the copy's Stored File row, lines, approval, the guard (`UpdateRequest` of the document with `IfRowVersionMatches` at the staged version, writing back only `sprk_invoicereviewstatus` as read), the submission. No document link, no owner write, no share write. A transaction failure deletes the staged blob. |
| Commit gate (steps 5–6) | `Services/Finance/Submission/SubmissionCommitGate.cs` (new) | Shared by the persister (after its commit), the after-steps' resume (first) and the handler (held undo). Step 5: re-runs the source check for every source of the invoice's files and checks the source's secure roots ⊆ the copy's; any fault is a failure (uac-r2 condition 1). Pass: flip every file to Servable. Fail: **undo** (§9.2). Step 6: if any file is already Servable, step 5 is skipped. Idempotent; a fault is "held" and completed by a later delivery. Every channel (uploads too: 092 review item 2). |
| Delete pre-check | `Services/DocumentCheckoutService.cs`, `Api/DataverseDocumentsEndpoints.cs`, `Api/DocumentOperationsEndpoints.cs`, `Api/ExternalAccess/ExternalProjectDataEndpoints.cs`, `StoredFileService.CountCopiesOfDocumentAsync` | `sprk_storedfile.sprk_sourcedocument` is Delete = Restrict, so a document a Stored File was copied from is refused BEFORE its SPE file is touched, on both delete paths: internal 409 `document_is_invoice_source` ("An invoice was submitted from this document, so it can't be deleted."), external plane 409 `document_in_use` (generic, no count; EB-D-166). Children are checked first (their count is kept). A count read fault fails closed. |
| Retired | `InvoiceDocumentRefile.cs` (deleted), `FinanceModule.cs`, `InvoiceSubmissionAfterSteps.cs` | 050a's re-file of the confirmed document under the invoice, its after-step, and the indexing after-step (owner K; DEF-047). `commitGate` is a required argument of `InvoiceSubmissionAfterSteps`. |
| Admin text, codes | `AdminAttentionText.cs`, `SubmissionReasonCodes.cs`, `InvoiceSubmissionContracts.cs`, `ProblemDetailsHelper.cs` | `UndoPendingNote`, `UndoneNote`, `UndoHeldNote` (one undone slot); `source_check_failed`; `source_forbidden`, `source_communication_mismatch`, `document_rejected`; `document_is_invoice_source`, `document_in_use`. |

## 2. Failure outcomes (model 9.1)

| Step | Condition | Outcome |
|---|---|---|
| validation | missing field or blank firm invoice number | 400 (field errors), nothing read |
| 0 | flagged-external / no systemuser / flag fault | 403 before any record read |
| filter | no Read+AppendTo on core, no AppendTo on firm | 403, nothing read |
| 1 | no Read on document or communication (or unknown) | 403 `ebilling.submission.source_forbidden` |
| 1 | communication not the document's | 422 `source_communication_mismatch` |
| intake | earlier submission ended / other details / filed under another core record (D-140 chain) / isolated / firm not linked / owner | its refusal; step A has not run |
| A | isolated communication | 409 `document_core_record_mismatch`; nothing written |
| A | no Write on an unfiled communication | 403 `COMMUNICATION_REFILE_FORBIDDEN` (re-file route's own) |
| A | re-file core refuses | its answer as is (e.g. 404 on a core record without AppendTo) |
| after A | allocator conflict, queue failure | its refusal; "No submission was saved" + the filing is kept |
| intake | chain or document read fault | 503 `record_unavailable` |
| 2 | email archive document | Failed `document_unavailable` |
| 2/3 | submission not the document's derived id | Failed `document_unavailable` / Refused |
| 3 | live version ≠ staged | `SourceChanged` → re-stage once (same bytes) or transient |
| 4 | guard faults (source written since staging) | rolled back; same as step 3 |
| 4 | transaction failure | staged blob deleted; Retry |
| 5 | re-filed elsewhere / roots not covered / re-run fault / no or unflippable file | undone; Failed `source_check_failed` or `file_unservable`; job Poisoned |
| 5 | undo fault | held: Failed, "removal pending" note, file Not yet servable; next delivery completes it; final delivery: "undo held — operator action" |

## 3. §11 component justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Route `POST .../invoice-submissions/from-document` | `POST /api/finance/invoice-review/confirm` (050a) | The confirm is the review page's action on an already-reviewed document with its own filter; Submit Invoice needs Read on the source and its communication plus A, and is the contract 118 and 095 call | Submit Invoice has no entry point; 118's command has nothing to call. |
| `PrepareSourceAsync`, `SourcePreparation`, `SourceFiling`, `SourceFilingRefusedException` + `SourceServices` | `CommunicationEndpoints.RefileCommunicationAsync` (reused, not copied) | It composes the re-file core; the confirm route reuses it | Step 1's source checks and A would be duplicated per route, or A would run before the intake's refusals. |
| `InvoiceSubmissionRequest.FileSourceBeforeWrite`, `InvoiceReviewConfirmRequest.FileSourceBeforeWrite` | the intake's own order of checks | A hook the intake runs at the one point after its read-only refusals | A refused submission leaves a filing behind (fix round F1-b). |
| `InvoiceSubmissionService.IsCommunicationFiledAsync` | `ResolveFilingAsync` (the D-140 chain), `ReconciledEmailFiling.FilingColumns`, `RecordThreadFiling.ParentsOf` | Composes them; no new rule | Step A's "unfiled" would differ from the D-140 chain (fix round F1-a). |
| `ExistingDocumentIsolationCheck.CheckCommunicationForFilingAsync` | `CheckAsync` (the document's walk) | Same walk, over the communication row, without a core record | Step A's check would differ from the intake's (fix round F1-a). |
| `SubmissionCommitGate` | 092's `FlipUploadFileAsync` in the persister | Moved and generalized (one gate, every channel) instead of a second flip | No step 5 re-run, no undo: a source re-filed after the commit leaves a Servable copy under the wrong record (uac-r2 conditions 1, 3). |
| `SubmissionCommitGate.NoteUndoHeldAsync` | `AdminAttentionText.Merge` | One note write | A dead-lettered held undo carries no operator signal (F2-a). |
| `StagedSourceCopy`, `SubmissionFileSource`, `SubmissionSnapshot.FileSource`, `StagedFiles` | `SubmissionSnapshot.DocumentId` | A null document no longer decides the channel (092 review item 1) | The job cannot tell a copy from an upload. |
| `StoredFileService.WithdrawForUndoAsync`, `DeleteBlobForUndoAsync`, `ReadRowsForInvoiceAsync` | `DeleteStagedAsync` (refuses when a row exists) | The undo must withdraw a row that exists, then delete its blob, before the one transaction that deletes the rows and the invoice | The undo cannot remove the file, or leaves the invoice without its file row (§9.2). |
| `StoredFileService.CountCopiesOfDocumentAsync`, `DocumentCheckoutService.CountStoredFileCopiesAsync`, `InvoiceSourceDeleteResult`, `ProblemDetailsHelper.DocumentIsInvoiceSource` / `DocumentInUse` | `CountChildDocumentsAsync`, `DeleteResult`, `HasChildDocuments` | Same pre-check slot, own answer | Restrict refuses the document row after its SPE file was deleted (ontology-r1 088 gate F1); the user is told about "children" that do not exist (F1-c). |
| Codes and notes in §1 | `SubmissionReasonCodes`, `InvoiceSubmissionErrors`, `AdminAttentionText`, `DocumentCheckoutService` | Extended in place | The outcomes have no stable code (118, 095 contracts pin them). |

DI: `SubmissionCommitGate` replaces `InvoiceDocumentRefile` (one registration out, one in). No package, no Dataverse column. BFF placement: Finance submission services in `Sprk.Bff.Api`, same as 092 (no new module).

## 4. Seeding proofs (POML)

| # | Seed | Result | Restored |
|---|---|---|---|
| 1 | `SubmissionCommitGate.SourcesStillCoveredAsync` returns true unconditionally | `Source_RefiledAfterTheCommit_IsUndone` fails (with the roots, re-run-fault and idempotence undo tests and the happy path's read count): 5 failed | yes, file identical to before |
| 2 | `CompensateAsync` skips the file rows' delete (then `DeleteForUndoAsync`; since round 4 the rows go in the rows-and-invoice transaction) | the refiled-source undo test fails, with roots, re-run fault, idempotence, upload flip-failure undo and undo-held: 6 failed | yes |

Guard controls: `SubmitInvoiceSourceUntouchedGuardTests.Detector_MustFireAndMustNotFire`. Delete pre-check: must-fire (external 409, internal 409, Dataverse route 409, read fault 503) and must-not-fire (unreferenced document deletes; another document's copies do not refuse).

## 5. Tests and gate (fix round head)

- Builds: Sprk.Bff.Api.Tests, Spaarke.ArchTests, Spaarke.Core.Tests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests: all succeed.
- Unit broad filter (Finance, Billing, Signals, RuleSet, Invoice, Compliance, Rejection, Decision, AccessTable, ParentLineage, Ebilling, Procedure, SecureChild, Submission): 9675 passed, 7 skipped, 0 failed.
- Document delete suites (DocumentDestroy, ExternalDeleteOwn, ExternalRecordDocumentRoutes, DataverseDocumentsEndpoints, DocumentCheckout, DocumentOperations, ExternalDocumentNarrowing, Delete): 1051 passed, 1 skipped. Access-control suites (DocumentRouteExternalNarrowing, UnifiedAccessControl, AccessControl): 4108 passed.
- ArchTests: 1567 passed, 0 failed (full).
- Sprk.Bff.Api.IntegrationTests: 199 passed, 8 skipped. Spe.Integration.Tests: 353 passed, 23 skipped.
- Copy rule end to end: `CopyRule_EndToEnd_LosingReadOnTheSource_RefusesTheCopy` serves the committed copy through 090's real `MapStoredFileEndpoints` over the same store, then 404 once the caller loses Read on the source.

## 6. Publish size (Compress-Archive, short paths, 192 files each)

| Build | Zip bytes |
|---|---|
| master `90f3cab765` (C:\wt094m) | 39,725,046 |
| branch before 094 (`584adec17e`, C:\wt094h) | 40,531,161 |
| branch with 094 core (`5a5ffabfa8`, C:\wt094b) | 40,553,752 |
| fix round (C:\wt094f) | 40,561,899 |

094's own delta: **+30,738 bytes** (core +22,591, fix round +8,147). Cumulative ebilling-r1 vs master: +836,853 bytes. No package added (csproj and `Directory.Packages.props` unchanged), so no new CVE.

## 7. For 118 (command) and 095 (from email)

- 118: `POST /api/finance/invoice-submissions/from-document`, body `{communicationId, documentId, coreRecord: {kind: matter|project|workassignment, id}, firmId, firmInvoiceNumber, poNumber?}`. **`communicationId` is required** (the route is "an email attachment, a document of a communication"; a document with no communication is not submitted here; K7). 202 `{submissionId, jobId, trackingNumber, statusUrl}`; refusals per §2 (ProblemDetails with `errorCode`; a blank number is a 400 field error `firmInvoiceNumber`). A refusal after A filed the communication carries "that filing is kept".
- 095: the gate already reads its sources from the file rows (`sprk_sourcedocument`), so a from-email copy is re-run and undone like a document copy. The `ISubmitInvoiceFromEmail` implementation is built (§10.6), with the uac-r2 095 K5 hand-offs: `sprk_createdbyperson` = caller on everything it creates, A on the duplicate path (as the hook now runs on the earlier-submission path), Read on the `.eml` and its communication; no reliance on `IdempotencyFilter`'s old double call; no assumption that a generation's creator matches earlier ones; bytes staged from the `.eml` as read back (base64 parts).

## 8. Dependencies, open questions, known limits

- **086 (live gate):** until `sprk_storedfile` is a `SecureChildLineage` child, the copy's roots are its invoice's; the gate switches to the file rows' own roots when 086 lands (`FileRootsAsync`). No change to `SecureChildLineage` or the role JSON here.
- **Open — indexing (owner K, DEF-047/#2351):** no submission is indexed since 094; the confirm path lost its indexing.
- **Open — D-139:** an unfiled document (nothing in its chain) may still be submitted to any core record; A files only the communication.
- **Open — A and the undo:** A survives a later undo and a later refusal by design (the answer and the note say so). A refusal by A is returned as the re-file route's answer, not a submission code.
- **Open — confirm route:** it now requires Read on the document and on its communication; a reviewer who could confirm before may be refused.
- **Open — Delete:** deleting a document with a copy is 409 until the Stored File is removed; there is no user path to remove a Stored File.
- **Closed — ISS-012 (#2374):** owner decision EB-D-167, built in `1091efa81b` (§10.4).
- **Follow-up — DEF-048 (#2372, owner OK; F1-1):** field-secure `sprk_invoicesubmission.sprk_document` so users cannot re-point a submission (the BFF already fails such a row closed).
- **Known limit (uac K2 = addendum K3) — DEF-049 (#2373):** step 6 and the undo read "the invoice's files"; before O-14 upload-to-invoice or 150 export files under an invoice, both must key to the submission's own file ids.
- **Known limit (uac K1):** the copy's Stored File row (name, size, SHA, source ids) is readable by the invoice's readers. The name is now the tracking number, never the source's file name (one line, done); the source ids and hash stay visible to them.
- **Known limit (uac K3 / K4):** recorded in the uac review: the source's Restricted / internal-only flags are read and carried, never a refusal ground (owner K); the copy follows the source's Read only on 090's download route.
- **Known limit (addendum K4, orphan blobs):** an evaluation or persister fault now discards the staged copy (F2-b), and so does a Resumed run (another delivery committed). Left: ISS-013 (#2375), the ContentConflict re-stage can delete a concurrent delivery's not-yet-committed blob (fails closed: the flip's hash check refuses it, then undo); and an undo that meets a file row another writer already deleted leaves its blob for the reconcile job (disabled; never servable without its row).
- **Known limit (addendum K5):** A is check-then-act: the communication can be filed elsewhere between A's read and its write (the re-file core's own checks apply); step 5 re-runs the chain after the commit and undoes a mismatch.
- **Known limit (Restrict fallback):** a copy committed after the delete pre-check's count still makes Dataverse refuse the document row (Restrict), which the delete paths map to the has-children 409 with no count.
- **Known limit:** `Source_RefiledAfterTheCommit` and the roots test exercise step 5 through the isolation fake; the live walk is 098/086's.

### Deploy checklist (before this reaches an environment with 050a submissions)

1. **uac K5:** list the 050a submissions committed (invoice linked) but not through their after-steps (`sprk_processingstatus` Processing with `sprk_invoice` set); drain them (let their jobs resume) or list them for the owner. Their resume now runs the commit gate first.
2. **Addendum K1 / O-13:** a pre-094 committed submission has NO Stored File row (its PDF stayed a document). The gate's step 5 treats "no file" as a failure, so such a submission resumed after deploy is UNDONE (`file_unservable`). Drain item 1 before deploy, or the owner decides to exempt pre-094 rows (none exempted in code).

## 9. Fix round (uac review + adversarial review addendum)

### 9.1 Items

| Item | Change | Tests (fail before, pass after) |
|---|---|---|
| F1-a (uac) = F1-3 | Step A decides "unfiled" from the D-140 chain and refuses an isolated communication. | `SecureChildOwnershipAiToolTests.SubmitInvoice_StepA_UnfiledFromTheD140Chain_Case1_NoWrite`, `_Case2_NoWrite`, `_Case3_NoWrite`; `SubmitInvoice_StepA_IsolatedCommunication_Case1_Is409_NoWrite`, `_Case2_Is409_NoWrite`; `SubmitInvoice_UnfiledCommunication_IsolationUnreadable_Is503_NotFiled`; controls `SubmitInvoice_CommunicationOnADirectThread_IsUnfiled_AndFiled`, `SubmitInvoice_UnfiledCommunication_IsFiledAsTheCaller_WhenStepARuns`. Service level (409, A not run): `InvoiceSubmissionExistingDocumentTests.StepA_DoesNotRun_BeforeARefusal(d140-chain-case-1 / isolation-case-1)`. |
| F1-b (uac) = F1-4 | Step 1 is read-only; A is a hook the intake runs after every read-only refusal and before the create (also on the earlier-submission path). Post-A refusals: "No submission was saved" + "that filing is kept". | `InvoiceSubmissionExistingDocumentTests.StepA_RunsOnce_AfterTheChecks_BeforeTheCreate`, `StepA_DoesNotRun_BeforeARefusal` (9 cases), `StepA_RunsOnTheEarlierSubmissionsRetry`, `StepA_Filed_ThenARefusal_SaysTheFilingIsKept` (queue, allocator), `StepA_FiledNothing_ThenARefusal_SaysNothingOfAFiling`; `FinanceEndpointsAuthorizationContractTests.Confirm_DocumentsCommunicationReadable_HandsStepAToTheIntake_AndFilesNothingFirst`, `Confirm_StepARefusedInsideTheIntake_IsAsGiven`. |
| F1-c (uac) = F1-5 | Delete of a document with a copy: internal 409 `document_is_invoice_source` with its own message; external plane generic 409 `document_in_use`, no count. Children checked first. | `ExternalDeleteOwnTests.Document_WithAStoredFileCopy_Is409_InUse_AndNothingIsDeleted`, `InternalDelete_WithStoredFileCopies_IsRefused_AsAnInvoiceSource_BeforeTheFile`, `InternalDelete_WithChildrenAndStoredFileCopies_IsRefusedForTheChildren_WithTheirCount`; `DocumentDestroyAuthorizationTests.DeleteDataverseDocument_WithStoredFileCopies_Is409_AsAnInvoiceSource_AndDestroysNothing`, `DeleteDataverseDocument_WithChildrenAndStoredFileCopies_Is409_ForTheChildren`. |
| F1-1 (security) | A source-document submission must be the id derived from its document (snapshot and persister). Field-securing the column: DEF-048. | `InvoiceSubmissionProcessorTests.InternalUploadSubmission_PatchedToNameADocument_FailsClosed_DocumentUnavailable` (092's restored), `DerivedSubmission_RepointedAtAnotherDocument_FailsClosed_DocumentUnavailable`; `InvoiceSubmissionPersisterTests.Source_DocumentItsSubmissionWasNotCreatedFor_IsRefused_NothingWritten`. |
| F2-a (uac) = F2 | "Removal pending" note first; "removed" only after `CompensateAsync` returned true; "undo held — operator action" on a held undo at the final delivery; the approval is deleted before the file step. | `InvoiceSubmissionPersisterTests.Undo_FaultInTheRowsAndInvoiceTransaction_LeavesTheInvoiceWithItsFileRow_ThenCompletes`, `Undo_FaultBeforeTheFileStep_LeavesTheInvoiceWithItsFileRow` (approval, lines); `InvoiceSubmissionProcessingJobHandlerTests.PersistUndone_MapsCompleteOrHeld`, `FailedWithAnUndoCode_RunsTheCompensation` (held notes asserted). |
| F2-b (uac) | try/finally in the job's pass loop: the staged copy is discarded unless Committed, Undone, a linked Retry or the outcome-unknown Retry. | `InvoiceSubmissionProcessingJobHandlerTests.Source_EvaluationThrowsAfterTheStage_DiscardsTheStagedCopy`, `Source_PersisterThrows_DiscardsTheStagedCopy`, `Source_Resumed_DiscardsThisRunsCopy`; control `Source_Committed_KeepsTheStagedCopy`. |
| Undo order (098 K1) = F1-2 | §9.2. | `InvoiceSubmissionPersisterTests` `ShouldBeUndone` (one transaction for rows + invoice, after approval and lines; the file row present while they are deleted) over every undo test, plus the two `Undo_Fault...` tests above. |
| F4 | The confirm route checks the document's Read itself (`documentReadChecked: false`). | `FinanceEndpointsAuthorizationContractTests.Confirm_DocumentWithoutRead_Is403SourceForbidden_TheIntakeIsNotCalled`, `Confirm_DocumentsCommunicationWithoutRead_Is403SourceForbidden_TheIntakeIsNotCalled`. |
| Validation order | The firm invoice number is validated with the body (400 before any rights query or read). | `InvoiceSubmissionEndpointsContractTests.FromDocument_invalid_body_is_400(number)` (no source rights call; `errors.firmInvoiceNumber`). |
| F3 tests | Step-A harness split and extended. | `SubmitInvoice_NoReadOnTheDocument_IsForbidden`, `SubmitInvoice_NoReadOnTheCommunication_IsForbidden` (split of the old theory); the A-not-before-refusal and confirm-route tests above; the confirm host's probe answers record rights from the filter's table (`RecordingPrivilegeProbe.RecordRights`). |
| K6 | `commitGate` is required in `InvoiceSubmissionAfterSteps`. | `InvoiceSubmissionAfterStepsTests.Constructor_WithoutACommitGate_Throws`. |
| K7 | Route summary "Submit an email attachment (a document of a communication) as an invoice"; the description says `communicationId` is required. Noted for 118 (§7). | — (OpenAPI text) |
| uac K1 | The copy is named by the tracking number. | `InvoiceSubmissionProcessingJobHandlerTests.Source_IsStagedAsItsCopy_AndHandedToThePersister` (name asserted). |

### 9.2 Undo order (deviation from model 9.1, stated)

Model 9.1 orders the undo "Stored File blob and row, approval, lines, invoice". The fix round deletes: the approval; the lines; the file blobs; then the Stored File rows AND the invoice in ONE `ExecuteTransactionAsync` (all or nothing); then the four submission columns and the "removed" note. Rationale: `sprk_storedfile.sprk_invoice` is RemoveLink, and the 098 external predicate (`ExternalInvoiceVisibility`) hides an invoice when any of its sources is hidden and shows an invoice with no source file. Deleting the file row first (as 9.1 orders it) leaves, on any fault after it, an invoice with no file row: visible on the external plane although its source is restricted. In the fix round's order no partial state leaves the invoice without its file row; a blob deleted before a rolled-back transaction leaves a Not yet servable row with no blob (never served), finished by the next delivery. More than 50 file rows (only a data fault) hold the undo with nothing deleted (uac-r2 K2, §10.2). Since §10.2 the rows are also withdrawn (Removed) before any blob goes; the model's §9.1 now states this order (amended 2026-10-28).

### 9.3 Issues filed (project-defer-issue-tracking)

DEF-048 #2372, DEF-049 #2373, ISS-012 #2374, ISS-013 #2375 (`notes/defer-issues.md`; ids provisional, renumber on merge).

## 10. Round 4 (2026-10-28): 095 fold-in, uac-r2 conditions, EB-D-167, from-email operation

### 10.1 Merges

- 095 @ `85464826b6` (`718ee42b5d`); 098 @ `f2446e638e` (`cdff7a9ece`, kept on the coordinator's word). The 098 round-4 head is merged over it when the coordinator sends it; no other 098 head is merged. The persister's content-team change waits for uac's R4b sha (EB-D-168).

### 10.2 uac-r2 conditions

| Item | Change | Tests |
|---|---|---|
| K2 | The undo reads the invoice's file rows FIRST; more than `MaxFiles` holds the undo with nothing deleted (the read is unordered, so dropping a "surplus" could keep a sourceless row). | `InvoiceSubmissionPersisterTests.Undo_MoreRowsThanOneTransaction_HoldsWithNothingDeleted`, `Undo_ExactlyMaxFilesRows_Completes` |
| (b) | Every row is withdrawn (Removed, conditional on the version read) before any blob goes: a held undo never leaves a Servable row without its blob. | `Undo_HeldAfterTheBlobDelete_LeavesTheServableRowWithdrawn_NeverServed`, `Undo_HeldBeforeTheFileStep_WithdrawsNothing` |
| (c) | `procedure-and-file-model.md` §9.1 step 5 amended to the code's undo order (path B): file rows read with the cap hold, approval, lines, rows withdrawn, blobs, one transaction (rows + invoice), columns. | — (notes) |
| K1 | Step A's "filed" covers the D-140 hop, over the shape set. | `StepAFilingCoversTheHopTests.WhenTheHopReportsFiled_StepAReadsFiled`, `TheShapeSet_CoversBothHopsAndAnUnfiledCase` |
| K3 (known limit, recorded here only; #2372 not edited) | Only the BFF creates submissions and writes their document column. A submission's id is derived deterministically, so a user who holds direct Create on `sprk_invoicesubmission` could create that row first and make the confirm answer 409; they gain no read by it. Tied to UAC 191 (the census of users' direct table rights, model §5.2). | — |

### 10.3 InvoicePdf writer guard

An Invoice PDF Stored File row is created only inside the persister's commit, with its invoice (`StoredFileService` refuses one outside it). ArchTest `InvoicePdfWriterGuardTests.AnInvoicePdfIsCreatedOnlyWithItsInvoice` with `Detector_MustFireAndMustNotFire`; unit `StoredFileServiceTests.InvoicePdf_OutsideTheCommit_IsRefused_NothingWritten`, control `SupportingFile_OntoTheInvoice_IsCreated`. End to end with 098's predicate: `InvoiceSubmissionPersisterTests.UploadInvoice_CommittedByThePersister_IsVisibleToTheFirm(Spa/Internal)`, control `InternalUpload_WithoutItsStoredFileRow_IsHidden`.

### 10.4 EB-D-167 (closes ISS-012, #2374)

After a **Failed** attempt the confirm (and Submit Invoice from a document) starts a new attempt: generation g's derived id (`ExistingDocumentSubmissionIdFor(doc, g)`, at most `MaxExistingDocumentGeneration` = 50), created by the caller, every check run again; **Rejected** stays 409 (D-139). Interpretation recorded: "the upload routes" in the decision are the document-keyed internal entries (confirm, from-document); a file upload mints a fresh id per request and needs no generation. The allocator skips tracking numbers held by undone attempts (`44c3ca9bc5`). Tests: `InvoiceSubmissionExistingDocumentTests.Retry_AfterFailed_IsANewAttempt`, `AnotherUsersFailedAttempt_GivesANewAttemptByThisCaller`, `Retry_AfterTwoFailedAttempts_IsAttemptThree`, `Retry_AfterFailed_RunsEveryCheck`, `TwoConcurrentRetries_AfterFailed_MakeExactlyOneNewAttempt`, `GenerationIds_KeepGenerationOne_AndAreTheDocumentsOwn`; `InvoiceReviewWritePathTests.Confirm_RetryAfterTheSubmissionFailed_IsANewAttempt_AndMarks`, `Confirm_RetryAfterTheSubmissionWasRejected_Is409_AndDoesNotMark`; `TrackingNumberAllocatorTests.ANumberHeldByAnUndoneAttempt_IsSkipped`, `MoreUndoneAttemptsThanMaxAttempts_AreAllSkipped`, `UndoneAttemptsBeyondTheSkipBound_AreRefused`, `ANumberHeldByANumberedRow_IsALostRace_NotASkip`.

### 10.5 What a from-email submission writes (sent to the coordinator for 098 round 4)

- The submission: id `FromEmailSubmissionKey.SubmissionIdFor(K, generation)`, created through the allocator; Channel **Internal**; `sprk_createdbyperson` = the caller (`CallerSystemUserId`) on every generation; **`sprk_document` empty**.
- The Invoice part: an Invoice PDF Stored File `StoredFileIds.ForUpload(submissionId)` under the invoice (`InvoiceIdFor`), Firm-visible No, `sprk_sourcedocument` = the `.eml`, `sprk_sourcecommunication` = its communication, `sprk_sourceattachmentidentity` = `eml-part-{ordinal}`, `sprk_sourceversion`, `sprk_sourcesha256`; named `{tracking}.pdf`.
- Each Supporting part: an Invoice supporting file `StoredFileIds.ForAttachment(submissionId, "eml-part-{n}")` with the same source columns; named `{tracking}-{n}{ext}` (never the attachment's name).
- Every row is written only in the persister's commit transaction, created by the submission's creator person.

### 10.6 The from-email operation (`SubmitInvoiceFromEmailOperation`)

Replaces 095's not-yet-available stand-in (one scoped registration; the ADR-010 single-implementation count stays 171). In order, nothing written before the first stage:

1. Step 1, read-only (`PrepareSourceAsync` with `documentReadChecked: false`): the caller's Read on the `.eml` and on its communication; the `.eml` must be that communication's.
2. The intake's checks (`InvoiceSubmissionService.DecideForFromEmailAsync`): the number, the firm active and linked, the core record, the owner, with `RequestedBy` = the caller's systemuser, so the creator person is the caller on every generation; the seam reads nothing of an earlier generation's creator.
3. Step A, after those refusals: on every path that creates an attempt, including 095's duplicate path (the earlier `.eml` of the same email is the source) and a concurrent retry that then loses the create race (`AlreadyCreated`). 095's replay of an existing attempt creates nothing and runs no step A (the attempt that created the row ran it).
4. The marked parts, read out of the saved `.eml` by `EmlMarkedPartReader` (ordinal, name, SHA-256; its `versionnumber` is the source version).
5. Each part staged under its Stored File id with its source written into the blob's metadata (`sourcedocument`, `sourcecommunication`, `sourceversion`, `sourceattachment`; the Invoice part also lists the Supporting ordinals, `emlparts`; a Supporting part its `extension`). `StoredFileService.ReadStagedSourceAsync` reads them back. ContentConflict 409 `document_already_submitted`; NotConfigured 422 `storage_not_configured`.
6. The row with exactly the attempt's id (`AlreadyCreated` is the race answer; Refused 503 `ebilling.tracking.unavailable`), then the job. No compensation: the ids are deterministic, so a loser that deleted "its" blobs or row would delete the winner's; a row whose job was not queued is re-queued by the retry's replay (`RequeueAsync`). `ReadAttemptSourceAsync` answers from the Invoice part's stage metadata (the `.eml` and the part's SHA), null for an upload.

Refusals after step A filed end with "The communication was filed under the record first; that filing is kept." A step-1 or step-A answer that carries `errorCode` becomes the operation's refusal; any other (the re-file route's own, the owner refusal's ProblemDetails) is returned by the route as it is (`FromEmailRouteAnswerException`, caught in `InvoiceSubmissionFromEmailHttp`).

**The persister.** It recognises a from-email submission from the Invoice part's stage metadata (a recorded `.eml`); it requires Channel Internal and a recorded version (else Refused `document_unavailable`), runs step 3 (`CheckExistingDocumentAsync`) on the `.eml`, builds every part's row in the one transaction (a listed Supporting part that is missing or records another source: Refused `file_unservable`), and adds the guard on the `.eml`. The commit gate re-runs every file's source and flips every file.

**Version guard decision (amendment to model §9.1 step 4, stated):** the `.eml`'s guard is at the version step 3 read in this delivery, not the version the parts were read at. The in-process save's finalization writes the `.eml` row after the intake, so a staging-version pin would never commit. The bytes are pinned instead by the SHA-256 recorded at intake from the caller's marks; the guard pins the filing and isolation step 3 judged. A guard failure is `SourceChanged`; with no job-staged copy, the job retries the delivery and step 3 reads the `.eml` again.

Tests: `SubmitInvoiceFromEmailOperationTests` (20): `Submit_StagesEveryPartWithItsSource_AndCreatesThisIdWithNoDocument_ChannelInternal_CreatedByTheCaller`, `Submit_GenerationTwo_IsCreatedByThisCaller_WhoeverMadeTheEarlierAttempt` (both callers), `Submit_StepOneRefuses_IsThatRefusal_BeforeTheChecks_AndWritesNothing`, `Submit_StepOneAnswersWithoutAnErrorCode_IsTheRoutesAnswer`, `Submit_TheFirmIsNotLinked_IsRefusedByTheIntake_AndStepAIsNotRun`, `Submit_StepARuns_AfterTheReadOnlyChecks_AndBeforeAnyWrite`, `Submit_StepARefusesTheFiling_IsTheRefileRoutesAnswer_AndWritesNothing`, `Submit_StepARefusesWithASubmissionCode_IsThatRefusal`, `Submit_TheEmlCannotBeReadAfterFiling_Is503_SayingTheFilingIsKept_AndWritesNothing`, `Submit_APartDiffersFromItsMark_Is503_AndWritesNothing`, `Submit_TheRowAlreadyExists_IsAlreadyCreated_NoJob_NothingDeleted`, `Submit_TheJobCannotBeQueued_Is503_AndKeepsTheRowAndTheBlobs`, `Submit_NoTrackingNumber_Is503TrackingUnavailable`, `Submit_OtherBytesAreStagedUnderThisAttempt_Is409DocumentAlreadySubmitted_NoRow`, `Submit_TheStoreIsNotConfigured_Is422StorageNotConfigured`, `Submit_AnInconsistentInput_IsAWiringDefect`, `ReadAttemptSource_IsTheEmlAndTheInvoicePartsSha_FromWhatTheStageRecorded`, `ReadAttemptSource_AnUploadOrNothingStaged_IsNull`, `Requeue_QueuesTheJob_AndIsFalseWhenTheQueueRefuses`. Persister: `InvoiceSubmissionPersisterTests.FromEmail_CommitsEveryPartWithItsSource_GuardsTheEml_AndFlipsEveryPart`, `FromEmail_TheEmlMovedOnSinceTheIntake_GuardsTheLiveVersion_AndCommits`, `FromEmail_AWriteBetweenTheCheckAndTheCommit_IsSourceChanged`, `FromEmail_TheEmlIsLinkedToAnotherInvoice_IsRefused_NoTransaction`, `FromEmail_ASupportingPartIsMissingOrNotThisEmails_IsRefused_NoTransaction` (missing, another source), `FromEmail_ASourcedStageOnAnSpaSubmission_IsRefused`. 095's contract suite (§9 of `095-from-email-contract.md`) is unchanged and green; its §6 and §9 gained the operation's codes (marked "Added 2026-10-28 (094)").

### 10.7 Finding: from-email invoices and 098 at `f2446e638e`

`InvoiceSubmissionPersisterTests.FromEmailInvoice_CommittedByThePersister_IsVisibleToTheFirm` commits a from-email invoice through the real persister and asks 098's predicate: at `f2446e638e` it is **hidden** from its own firm (`source_link_unreadable`). The coordinator has the shape (§10.5) and 098 round 4 reworks rule 3; the test is skipped with that reason and is unskipped when the round-4 head is merged. No 098 file was changed here.

### 10.8 Gate (round 4 head, `c35493a1ec`)

- Builds: Sprk.Bff.Api, Sprk.Bff.Api.Tests, Spaarke.ArchTests, Spaarke.Core.Tests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests: all succeed.
- Unit broad filter (Finance, Billing, Signals, RuleSet, Invoice, Compliance, Rejection, Decision, AccessTable, ParentLineage, Ebilling, Procedure, SecureChild, Submission, Office, FromEmail, Idempotency, External): 15735 passed, 15 skipped, 0 failed.
- Document delete suites: 1054 passed, 1 skipped. Access-control suites: 4108 passed.
- ArchTests (full, InvoicePdfWriterGuard and ADR010 included): 1576 passed, 0 failed.
- Sprk.Bff.Api.IntegrationTests: 199 passed, 8 skipped. Spe.Integration.Tests: 353 passed, 23 skipped.
- Publish size (Compress-Archive, short paths, 192 files each): fresh master `b445886fed` (C:\wt094n) 39,732,663 bytes; this head (C:\wt094g) 40,625,990 bytes: cumulative ebilling-r1 +893,327; since the fix round (C:\wt094f, 40,561,899) +64,091, which includes the 095 and 098 merges. No package or csproj change against master, so no new CVE.
