# Task 092 — Invoice submission on the Finance file store

> **⚠ Merge / deploy precondition**: 091 (storage account, containers, role) and 079 (the `Finance:FileStore` settings) must be live before 092 is deployed to any environment. Without them every upload answers **422 `storage_not_configured`** (SPA and internal channels alike).

> Project: ebilling-r1 (model `procedure-and-file-model.md` §4.1–4.3, 9.1, 9.2, 12; EB-D-148). Branch `task/ebill-092-submission-files` from `work/ebilling-r1` @ `786d1ea17a`. Rigor FULL, opus @ high, directional steps. Built and tested only: **no deploy, no live Dataverse or Azure write.** `StoredFileReconcileJob` stays disabled everywhere; no environment config gained file-store settings.

## 1. What was built

| Piece | File | Change |
|---|---|---|
| Intake (SPA + internal uploads) | `Services/Finance/Submission/InvoiceSubmissionService.cs` | The PDF is staged through 090 (`StageAsync`) under `StoredFileIds.ForUpload(submissionId)`, parent `sprk_invoice` / `InvoiceIdFor(submissionId)`, metadata `submissionid`, Firm-visible only for SPA. The existing file checks stay first; a refusal stages nothing. No `SpeFileStore` upload, no `sprk_document` create; the submission row names no document. Compensation (submission create or job enqueue failed) deletes the staged blob through `DeleteStagedAsync`. A store that is not configured answers **422 `storage_not_configured`**, the code 044 answered when no SPE container resolved, so the route contract and 051's message are unchanged. `UploadFileSpec` / `UploadName` are the one spec both the intake and the job build. |
| Receipt | `InvoiceSubmissionContracts.cs` | `InvoiceSubmissionReceipt.DocumentId` is `Guid?`: `null` for an upload, the confirmed document for 050a. The HTTP body (`InvoiceSubmissionAccepted`) never carried it; no client reads it. |
| Snapshot | `SubmissionEvaluationOutcome.cs` | `SubmissionSnapshot.DocumentId` is nullable; `IsUpload`. A document that is not a 050a confirm (`InvoiceDocumentRefile.AppliesTo`) is the retired intake shape and fails closed with `document_unavailable` (O-13: dev data is recreated, not migrated). |
| Job read | `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` | An upload's bytes come from `OpenStagedForProcessingAsync`: Pending → transient (Failure, retried); Malicious → Failed `file_malicious`, Poisoned; Missing / hash mismatch → Failed `document_unavailable`, Poisoned. Bytes go inline to the extractor as before. The SPE branch remains only for 050a existing-document submissions. `FileUnservable` with a linked invoice (persister or resume) marks the submission Failed — the one exception to "a linked submission is never Failed". |
| Persister | `InvoiceSubmissionPersister.cs` | Before the transaction: `GetStagedAsync` (no usable blob → Refused `file_unservable`, nothing written). In the one `ExecuteTransaction`: `BuildRow` (Invoice PDF, Not yet servable, owner = the invoice's ForParents resolution, creator person = the submitter; none for a contact, K6). Right after the commit and before the totals: `FlipUploadFileAsync` (Concurrency retried once; Definitive → Refused `file_unservable` **with** the invoice id, no totals, no after-step, the row stays Not yet servable for 090's detector; a fault throws). The upload path links no document. |
| Resume | `InvoiceSubmissionAfterSteps.cs` | `ResumeAsync` runs the flip gate **first** when the submission has no `sprk_document`, before totals and every after-step (see §4 D1). The indexing after-step is skipped for an upload (§5 K1). |
| Reason codes | `SubmissionReasonCodes.cs` | `FileUnservable = "ebilling.submission.file_unservable"`, `FileMalicious = "ebilling.submission.file_malicious"`, `FileScanTimeout = "ebilling.submission.file_scan_timeout"` (review F1, §4a). `DocumentMissing` is no longer written (kept so stored values still read). |
| Review read model | `Services/Finance/InvoiceReviewReadService.cs` | `InvoiceReviewDto.InvoicePdfFileId` (nullable): the newest Servable Invoice PDF Stored File of the invoice, one app-only query (`TopCount 1`, ids only) after the route's checks. The page (110) loads the bytes through `GET /api/finance/files/{fileId}/content`. |
| 090 primitives | `Services/Finance/Files/StoredFileService.cs` | `OpenStagedForProcessingAsync`, `GetStagedAsync`, `DeleteStagedAsync` (§4 E4). |
| 028 retirement | `FinanceOptions.cs`, `FinanceOptionsValidator.cs`, `GraphModule.cs` (`StampContainerSettingKeys`), `RecordContainerResolver.DocumentPointer.cs` (declared unfiled homes), `FileIndexingService.cs` (skip), `FinanceModule.cs` (comment), `appsettings.template.json` | **`Finance:EbillingOutputContainerId` only** is retired. `Finance:ProcedureLibraryContainerId` stays (escalation §6). A value left under the retired key admits nothing (must-not-fire tests). |
| Docs | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` | The output key is described as retired by 092. |

## 2. Component justification (§11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `FileUnservable`, `FileMalicious` reason codes | `SubmissionReasonCodes` (extended in place) | Added to the existing class | A missing or malicious file fails as a generic error the admin cannot act on. |
| `InvoiceReviewDto.InvoicePdfFileId` | `InvoiceReviewReadService` (extended in place) | One nullable field and one query | The review page (110) cannot load the invoice PDF once no Document exists. |
| `StoredFileService.OpenStagedForProcessingAsync` | `OpenForProcessingAsync` (needs a Servable row) | Shares its verdict-gated, ETag-pinned, hash-checked read (`ReadVerifiedAsync`); only the hash source differs | The job runs before the row exists (the row is written in the job's own commit, model 9.1 step 4), so without it the job cannot read the upload at all. |
| `StoredFileService.GetStagedAsync` | `StageAsync` returns a `StagedBlob`, but in the intake process | Reads the same facts back by name | The persister runs in another process than the intake; without the staged facts it cannot call `BuildRow`. |
| `StoredFileService.DeleteStagedAsync` | `StoredFileBlobs.DeleteStagedBlobAsync` (blob only) | Adds the "no row exists" check before the delete | A failed intake would leave an orphan blob until the reconcile job — which is disabled and must stay so. |
| `InvoiceSubmissionAfterSteps` `makeUploadServable` seam | The existing test-seam constructor (`refileDocument`) | One more optional delegate, production default = `FlipUploadFileAsync` | The resume gate could not be tested without a real file store. |

| `FileScanTimeout` reason code, `ScanWaits` / `ScanWaitBudget` / `ScanDelay` on the job handler (review F1) | The handler's transient path (`TransientAsync`) and its reason codes | Extended in place; `ScanDelay` is an `init` test seam defaulting to `Task.Delay` | A fresh upload fails `processing_failed` in seconds on every scanning-on environment (review F1). |

No new service, route, DI registration, package or Dataverse column.

## 3. Ordering contract (model 9.1 steps 4–6)

1. Intake: checks → stage blob → create submission (no document) → enqueue the job. Failure after the stage → compensation deletes the blob (only when no row exists).
2. Job: lock → re-read → processor (reads the staged blob; verdict and hash gate) → persister.
3. Persister: `GetStagedAsync` → one transaction (invoice, Stored File row, lines, approval, submission link) → **flip** → totals → after-steps.
4. A crash or fault between commit and flip: the submission links its invoice, so the redelivery is a resume, and `ResumeAsync` flips first.
5. Definitive flip failure (first run or resume): Refused `file_unservable` with the invoice id → the handler marks the submission Failed, Poisons the job, runs no after-step; the row stays Not yet servable (090's detector reports it).

## 4. Deviations and decisions

- **D1 — The resume gate lives in `InvoiceSubmissionAfterSteps.ResumeAsync`, not the persister's `ResumeAsync`.** The handler's linked-submission path (a redelivery after commit) calls the after-steps' resume directly and never reaches the persister, so a gate in the persister would be skipped on exactly the path that needs it. The persister's `Resumed` result runs no totals (049 R2), so the order holds either way.
- **D2 — Row name is `{TrackingNumber}.pdf`.** The client file name is not in the blob name or metadata, and the job never sees it; the tracking number is the stable, non-client name. The intake's spec uses the client name only for the stage call (the name is not part of the blob path).
- **D3 — Legacy intake-document submissions fail closed** with `document_unavailable` (any `sprk_document` that is not a 050a confirm). O-13: no migration.
- **D4 — `NotConfigured` maps to 422 `storage_not_configured`**, not 503, to keep the 044 route contract and 051's message.
- **E4 — 090 API gap (escalation 4 of the POML).** 090's API had no read of a staged file before its row exists, no read-back of staged facts, and no row-guarded delete. The three primitives were added **inside `StoredFileService`** (no direct blob writes from this task; all names still go through `StoredFileBlobs.RequireOwnBlob`). **090's owner should accept or revert them**; if reverted, 092 needs an equivalent API.
- **Firm-visible**: Yes for the SPA channel, No for internal (`spec.FirmVisible = channel == Spa`).

## 4a. Review fixes (adversarial review of `bbf9cd04e8`)

- **F1 — a Pending scan no longer burns every delivery in seconds.** Before: Pending threw a transient failure, the processor abandoned the message with no delay, and the 5th delivery (`JobContract.MaxDeliveryCount`) failed the submission `processing_failed` within ~3–10 s, long before Defender could tag a fresh upload. Now `LoadUploadAsync` polls the scan within the delivery with backoff (`InvoiceSubmissionProcessingJobHandler.ScanWaits`: 5, 10, 15, 20 s then 30 s ×6 = **3 min 50 s**, `ScanWaitBudget`), inside the 10-minute message-lock renewal and the 20-minute submission lock. Still Pending after the budget: transient with **`ebilling.submission.file_scan_timeout`**; on the final delivery the submission is Failed with that code (≈19 minutes of scan patience in all). A verdict that arrives during the wait is read on the same delivery. `ScanDelay` is a test seam (`Task.Delay` in production).
- **K6 hardening.** `StoredFileService.OpenStagedForProcessingAsync`: when a row already exists under the id, its purpose and parent must equal the request's, or the read is Missing (fail closed, no bytes read). A test now proves the PRODUCTION `InvoiceSubmissionAfterSteps` constructor wires the resume gate to `FlipUploadFileAsync` over the real file store.

### Outcome codes this task adds (contract for 051 / 095 / word-add-in-r1)

| Code | When | Submission | Invoice |
|---|---|---|---|
| `ebilling.submission.file_malicious` | Defender tagged the staged PDF Malicious | Failed (permanent, first delivery) | none |
| `ebilling.submission.file_scan_timeout` | no scan verdict after every delivery's wait (~19 min) | Failed on the final delivery | none |
| `ebilling.submission.file_unservable` | before commit: staged blob gone/unusable; after commit: definitive flip failure | Failed | none before commit; **kept** after commit (K2) |
| `ebilling.submission.document_unavailable` (widened) | staged blob missing / hash mismatch; or a `sprk_document` that is not a 050a confirm | Failed | none |
| `storage_not_configured` (HTTP 422, existing 044 error code) | file store not configured at intake | not created | none |

## 5. Known limits

- **K1 — Uploaded invoices are not indexed.** `InvoiceIndexingJobHandler` is keyed by a `sprk_document`; an upload has none, so the after-step skips indexing (logged). Per D-44 (second half) the indexing step takes no bytes from a Stored File. Filed as DEF-047 (provisional id) / #2351. 050a confirms are still indexed.
- **K2 — D-44: invoice PDFs leave the core record's Documents and the files index by design.** An internal user no longer sees the uploaded invoice PDF in the matter's Documents grid or in document search; the invoice review page is its surface (110). **The firm also loses its uploaded PDF from the SPA document list until 093's firm file route ships** (review K5).
- **K3 — DEVIATION from model §9.1 step 5: a definitive post-commit flip failure keeps the invoice.** The contract says undo; 092 does not implement the undo. The invoice stays committed (In Review or Rejected) with no totals, Signals or Decision Record, the submission is Failed `file_unservable`, and the row stays Not yet servable for the detector. **Task 094 implements the §9.1 undo and has been told to apply it to the upload channel too.** Until then two consumers assume a Failed submission has no invoice and are wrong for this case:
  - `src/server/api/Sprk.Bff.Api/Services/Finance/SubmissionOverrideService.cs:969` answers "nothing to decide" for the Failed submission although its invoice exists;
  - `src/server/api/Sprk.Bff.Api/Services/Signals/Billing/BillingFactLoader.cs:512` leaves Failed submissions out of the duplicate priors, so the kept invoice is not a prior for later duplicate checks.
- **K4 — A staged blob with no row** (e.g. Malicious, or a job that was never processed) stays until `StoredFileReconcileJob` runs, which is disabled and stays disabled (hard rule).
- **K5 — A Stored File create under a Secure core record is refused by Dataverse until 086's role row is live**; 079 deploys both.

## 6. Escalation — Procedure Library readers (🔔 Human Input Required)

The POML's first escalation trigger fired: `Finance:ProcedureLibraryContainerId` is still read by three production paths and no merged task has moved them to Procedure Version Stored Files:

- `src/server/api/Sprk.Bff.Api/Services/Finance/RuleSetService.cs:406` (`CreateAsync` upload)
- `src/server/api/Sprk.Bff.Api/Services/Finance/RuleSetService.cs:655` (draft source)
- `src/server/api/Sprk.Bff.Api/Services/Finance/RateCardImportService.cs:158` (rate-card workbook)

So only `EbillingOutputContainerId` was retired; the Procedure Library key, its validator check, its stamp-container entry, its declared-home entry, the template line and these readers are untouched. No task in the re-plan owns their move: the owner should name one (or fold it into 093/094). DEF-013 / #1967 is updated: the E-Billing Output Files half is superseded by 091's storage hand-off; the Procedure Library half stays open.

Also outside this task's write boundary: `.claude/constraints/provisioning.md:269` still names `Finance__EbillingOutputContainerId` (sub-agents cannot write under `.claude/`; main session to update).

## 7. Tests

Unit / contract / seam (Sprk.Bff.Api.Tests): new and rewritten tests in `InvoiceSubmissionServiceTests` (stage once under ForUpload, no SPE / document, compensation, refusals stage nothing, 422 not configured), `InvoiceSubmissionProcessorTests`, `InvoiceSubmissionPersisterTests` (one Stored File create, Firm-visible by channel, no staged blob, definitive flip failure, flip fault then resume, concurrency retry, non-confirm document refused, confirm has no file, transaction failure leaves no row), `InvoiceSubmissionAfterStepsTests` (resume flips first, unservable runs nothing, fault throws, no gate for a document or on `RunAsync`), `InvoiceSubmissionProcessingJobHandlerTests` (file-store read, pending, malicious, missing / changed, FileUnservable with and without the invoice, resume unservable), `InvoiceSubmissionProcessingFlowTests` (end to end: Servable file, no document, no indexing), `InvoiceReviewReadServiceTests` (file id set / null, query shape, read count 6), `StoredFileServiceTests` (staged read, staged facts, delete guarded by the row), `FinanceContainerIdOptionsTests`, `FileIndexingServiceTests`, `SpeAppOnlyContainerIsolationTests` and `RefileFileRelocationTests` (retired key admits nothing / declares no home), `VersionSaveAiRefreshSeamTests`, `InvoiceReviewWritePathTests`, `InvoiceSubmissionEndpointsContractTests`, `InvoiceSubmissionExistingDocumentTests`. ArchTests: the two stale `InvoiceSubmissionService` SPE sink sites removed; census texts updated.

Results (tip `7db7517c17`): all five test projects build; ArchTests 1553/1553; Sprk.Bff.Api.IntegrationTests 199 passed / 8 skipped; Spe.Integration.Tests 353 passed / 23 skipped; unit broad filter plus touched classes 9873 passed / 7 skipped / 0 failed; adjacent areas (tenant SPE, Office, indexing, container resolver, document container, options validation, startup, Finance contract) 1805 passed / 7 skipped.

Seeding proofs (each defect applied, the unit project rebuilt, the guarding tests run, then reverted): (S1) no post-commit flip → 6 tests fail (ordering `FileStatusAtTotals`, one Stored File create, definitive failure, fault-then-resume, concurrency, flow end-to-end); (S2) no resume gate → 3 fail (resume-first, resume-unservable, resume fault); (S3) Firm-visible for every channel → spec and transaction tests fail; (S4) compensation keeps the staged blob → 4 intake compensation tests fail; (S5) the retired key re-added to `StampContainerSettingKeys` → the retired-key must-not-fire fails. 16 failures in total; all green after the revert.

Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, deflate-compressed): master `1243eee69a` 36.86 MB; base `786d1ea17a` 37.68 MB; tip 37.68 MB (**+4.7 KB vs base**; +0.82 MB vs master is the rest of ebilling-r1). No package added.

Review round (F1 / K6): new tests `Upload_PendingOnEveryDelivery_WaitsEachDelivery_ThenFailsFileScanTimeout` (all `MaxDeliveryCount` deliveries Pending), `Upload_ScanArrivesDuringTheWait_IsReadOnTheSameDelivery`, `Upload_AlreadyScanned_DoesNotWait`, `Upload_PendingScan_IsTransient` (tightened: full wait, `file_scan_timeout`), `OpenStaged_RowWithAnotherParentOrPurpose_IsMissing`, `OpenStaged_MatchingRow_ReturnsBytes`, `ProductionConstructor_WiresTheFlipGateToTheFileStore`. Seeding proofs: (SF1) the pre-fix behaviour restored (no wait, `processing_failed`) → 3 F1 tests fail, including the all-deliveries test; (SK6a) the purpose/parent check removed → the must-fire fails; (SK6b) the production constructor passes no gate → the wiring test fails. 5 failures, green after the revert. Gate: 5 test projects build; ArchTests 1553/1553; broad filter + StoredFile + job processor 9471 passed / 7 skipped / 0 failed.

## 9. Coordination

- **095 (from-email, in progress on `task/ebill-095-from-email`)**: no textual conflict (merge-tree clean), but `SubmissionSnapshot.Read` now refuses any `sprk_document` that is not a 050a confirm with `document_unavailable`. If 095's submissions set `sprk_document` (e.g. the `.eml`), 095 must stage its marked PDF under the file store and leave `sprk_document` empty, or extend the snapshot rule.
- **098** edits `InvoiceSubmissionContracts.cs` and `InvoiceSubmissionService.cs` (status view `InvoiceId`): merge-tree clean.
- `work/ebilling-r1` has moved since the base (master merge, 118a): merge-tree clean.

## 8. Live steps (none done here)

- 079: remove the `Finance__EbillingOutputContainerId` app setting from dev (the owner-created dev containers are not touched); set the `Finance__FileStore__*` settings after 091.
- 086's role row live before any Stored File create under a Secure core record.
- 091: storage account, containers and role; Defender scanning (or `MalwareScanningEnabled=false` on dev only).
