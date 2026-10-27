# Task 095: Submit Invoice from the Outlook add-in (from-email): notes

> Branch `task/ebill-095-from-email`. Binding wire contract: [`095-from-email-contract.md`](095-from-email-contract.md) sections 1-9 (agreed with word-add-in-r1; EB-D-160 and `invoice_not_pdf` included). Rigor FULL. Early start under EB-D-158: everything that does not need task 094's internals. Task 094 replaces one DI registration.

## 1. What is built

| Piece | Where |
|---|---|
| `POST /api/finance/invoice-submissions/from-email`, with filters in the constrained order: step 0, Finance filter (body-shape 400; Read+AppendTo on the core record, AppendTo on the firm), OfficeAuth, EntityAccessFilter (same core record), Office Save rate limit, version-save gate (no-op), kill switch, `X-Idempotency-Key` cache bound to K | `Api/Finance/InvoiceSubmissionEndpoints.cs`, `Api/Finance/InvoiceSubmissionFromEmailHttp.cs` |
| `GET /api/finance/core-records/{entityType}/{entityId}/firms` (contract section 8) | same files |
| Orchestration: marks (400), bytes (422), number, Correction `not_available` (OP-7), firm (D-106), seam availability, K replay (EB-D-160), communication filing (409), in-process save, `.eml` resolution, then the seam | `Services/Finance/Submission/FromEmailSubmissionService.cs` |
| Marks rules, K, ids and generations, `invoice_not_pdf`, codes, Stored File id derivation | `Services/Finance/Submission/FromEmailSubmissionContracts.cs` |
| The seam onto 094 and its stand-in | `Services/Finance/Submission/ISubmitInvoiceFromEmail.cs` |
| The job's byte read of the `.eml` (094 calls it at staging) | `Services/Finance/Submission/EmlMarkedPartReader.cs` |
| The in-process email save: `SelectedAttachmentFileNames=[]`, no body key, refused before any write when no communication resolves, `.eml` created own-Restricted with the marker in the same create, parts recorded | `Services/Office/OfficeService.cs` (`SaveCoreAsync`), `OfficeSubmissionEmailSave.cs`, `OfficeDocumentPersistence.cs` |
| MIME part reader (ordinal, name, SHA-256) | `Services/Office/EmlMimeParts.cs` |
| Marker staging in the create only | `Spaarke.Dataverse/DataverseServiceClientImpl.cs` (`StageDocumentCommunicationFields`), `Models.cs` (`CreateDocumentRequest.RestrictedBySubmission`) |

### Refusal table: what the route produces

Marks 400 (`marks_invalid` plus `reason` and `fileName`), then bytes 422 (`marked_file_missing` plus `fileName` and `mark`), then the rest, in this order:

1. 400 `firm_invoice_number_required`.
2. 409 `correction_not_allowed` reason `not_available`.
3. 400 `firm_invalid` for an empty, missing or inactive firm.
4. 403 `firm_not_linked`.
5. 503 `record_unavailable` for a firm or link read fault. See interpretation 6.
6. 503 `failed` when the seam is not available.
7. The replay rows.
8. 503 `record_unavailable` when an attempt read faults.
9. 409 `document_core_record_mismatch`.
10. The save:
    - 422 `email_unidentifiable`, with reason `no_communication`, `earlier_save_unlinked` or `eml_ambiguous`;
    - 503 `record_unavailable`;
    - the Office save's own error passed through as `/office/save` renders it.
11. The seam's own refusal, passed through.

None of these writes anything, except a refusal after the save; that case is recorded as a known limit.

### Interpretations

1. An over-length `firmInvoiceNumber` (over 100 characters) or `poNumber` is the body-shape 400. A blank number is `firm_invoice_number_required`.
2. A missing `firmId` is the body-shape 400. An empty GUID is `firm_invalid`, so no firm rights check runs.
3. Bad base64, or a missing `contentBase64`, on a saved mark is 422 `marked_file_missing`.
4. K upper-cases the mark's content type. Marks match attachments ignoring case, so K must not split on case.
5. The generation cap is 50. Past it, the answer is 409 `submission_already_ended`.
6. A firm or firm-link read fault answers 503 `ebilling.submission.record_unavailable`, the contract's one transient-Dataverse code. The internal intake's `firm_unverifiable` is not in the add-in's closed set (section 6).
7. A save the Office pipeline resolved as a content duplicate of another document answers `eml_ambiguous`.
8. The duplicate path (step 1a) requires every marked SHA-256 to be among the candidate `.eml`'s parts.
9. OP-5: a different creator answers 409 `document_already_submitted` for any status, including Failed. EB-D-160's new attempt is for the same caller only. See open question Q1.
10. **The response cache serves only requests that carry `X-Idempotency-Key`.** The route passes `mayReplayResponse` for this.
    - Without the header, `IdempotencyFilter` keys its 24-hour cache on the body. An identical retry after a Failed attempt would then get the failed attempt's cached 202, not the new attempt EB-D-160 grants.
    - The handler's K replay answers a repeat either way, and the in-flight lock still applies.
    - Proof S3 is in section 5 below.
    - A cached replay has no `Location` header; that is existing filter behaviour. The body is identical and carries `statusUrl`.

## 2. The seam: what task 094 must supply

```csharp
internal interface ISubmitInvoiceFromEmail
{
    bool IsAvailable { get; }
    Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct);
    Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct);
    Task<FromEmailAttemptSource?> ReadAttemptSourceAsync(Guid submissionId, CancellationToken ct);
}
```

- `FromEmailSubmitInput` carries:
  - `SourceEmlDocumentId` and `CommunicationId`;
  - `CoreRecord`, `FirmId`, `FirmInvoiceNumber` (trimmed) and `PoNumber`;
  - `MarkedParts`: ordinal, file name, SHA-256, mark and **`StoredFileId`**. `InvoiceStoredFileId` is the Invoice part's id;
  - `SubmissionId`, `JobId` and `Generation`;
  - `Mode` and `InvoiceId`;
  - `CallerSystemUserId`, `CallerObjectId` and `CorrelationId`.
- `FromEmailSubmitResult` is one of `Received(tracking)`, `AlreadyCreated` (the allocator's race) or `Refused(status, code, detail)`.
- Stand-in: `SubmitInvoiceFromEmailNotYetAvailable` (`IsAvailable=false`). The route answers 503 `ebilling.submission.failed` before any write. Task 094 replaces the one registration in `FinanceModule`.

**Built around task 092's file-store intake (coordinator, 2026-10-27). There is no wire change.** Task 094 must do the following, in order:

1. Read the marked parts from the saved `.eml` with `EmlMarkedPartReader`. It does one row read, gives the version to pin, and checks each part's ordinal, name and SHA-256.
2. Stage each part with `StoredFileService.StageAsync` under its `StoredFileId`:
   - the Invoice under `StoredFileIds.ForUpload(submissionId)`, purpose Invoice PDF;
   - each Supporting part under `StoredFileIds.ForAttachment(submissionId, "eml-part-{ordinal}")`, purpose Invoice supporting file;
   - parent `InvoiceIdFor(submissionId)`, Firm-visible No;
   - the source recorded on the Stored File: `sprk_sourcedocument` = the `.eml`, `sprk_sourcecommunication` = the communication.
3. Create the submission through the allocator with **`sprk_document` EMPTY**. Since 092, `SubmissionSnapshot.Read` refuses any submission whose document is not a 050a confirm with `document_unavailable`. Channel Internal (OP-6), and `sprk_createdbyperson` = the caller's systemuser.
4. Queue the job. The job reads the bytes with `OpenStagedForProcessingAsync`.
5. Run step A and the model's steps 1-6, including the step 5 re-check (`source_check_failed`).
6. Answer `ReadAttemptSourceAsync` for the replay: the `.eml` id and the Invoice part's SHA-256 that the attempt recorded, for example from the Invoice Stored File's `sprk_sourcedocument` and hash. Null means "not recorded", and the replay answers 503 `failed`.

Where 094 runs `EmlMarkedPartReader`, at intake or in the job, decides how its outcomes surface:

| Reader outcome | Before the row exists | In the job |
|---|---|---|
| `EmailSaveFailed` | refusal | Failed `email_save_failed` |
| `DocumentUnavailable` | refusal | Failed `document_unavailable` |
| `Transient` | 503 | retry |

The section 9 code `email_save_failed` exists as `FromEmailSubmissionErrors.EmailSaveFailed`.

## 3. Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `POST .../from-email` | `/office/save` and the internal multipart submit | Neither can carry one email with marks, a firm and K: the save files one artifact, and the submit takes one PDF. A nested SaveRequest would bring a second target and a client duplicate key (contract section 2) | The add-in has no route for Submit Invoice from an email |
| `GET .../core-records/{type}/{id}/firms` | `CheckFirmLinkAsync` (the D-106 rule), `ReadActiveFirmNamesAsync` (the 050a picker's names) | It is built on both: one link read, one name query. No internal route listed a record's firms | The add-in cannot offer only firms the submit accepts (G1). Every guess risks a 403 `firm_not_linked` |
| `ISubmitInvoiceFromEmail` and its stand-in | `InvoiceSubmissionService.SubmitAsync` (multipart PDF) | 094 is the extension point. The brief requires one seam so 095 ships first | 095 cannot be built or tested before 094 |
| `IOfficeSubmissionEmailSave` (implemented by `OfficeService`) | `IOfficeService.SaveAsync` | The same pipeline (`SaveCoreAsync`); the interface only exposes the submission state: the `.eml`, its communication and its parts | The Finance service would need to reach into Office internals, or run a second save pipeline |
| `EmlMimeParts` | MimeKit in `OfficeEmailEnricher` (build only) | Nothing read parts back with ordinals and hashes | K's marks could not be tied to the bytes in the `.eml` (F2-c) |
| `EmlMarkedPartReader` | `SpeFileStore.DownloadFileAsync` | A thin reader over it, with the outcome mapping | 094 would re-derive the part check |
| `ApplyEmailCommunicationOutcome(..., submissionNarrowing)` | The I-19 single owner of an email `.eml`'s own narrowing | An optional parameter on that owner, not a second writer (the task 130 guard holds) | The `.eml` would get task 130's provisional narrowing and no marker |
| `CreateDocumentRequest.RestrictedBySubmission` | `sprk_restrictedbysubmission` (task 088 column) | A create-only property. The stager refuses it without own Restricted plus the record | The marker could not be written in the create (model 9.1) |
| `EntityAccessFilter` branch for `FromEmailSubmissionBody` | the `SaveRequest` branch | A concrete type check, not an interface (ADR-010 Path C) | The filter would pass the route through, credited as a gate while checking nothing |
| DI: `FromEmailSubmissionService`, `ISubmitInvoiceFromEmail`, `EmlMarkedPartReader`, `IOfficeSubmissionEmailSave` | — | Scoped like their dependencies. The Office save uses the same `OfficeService` instance | The route cannot resolve |

ADR-010: the ceiling goes 169 → 171, for `ISubmitInvoiceFromEmail` and `IOfficeSubmissionEmailSave`; each entry names its falsifier. A third interface, `IEntityAccessTarget`, was removed in favour of the concrete check.

## 4. Guards and census changes

- **New: `SubmissionMarkerWriterGuardTests`** (ArchTest; must-fire and must-not-fire controls inline). Its rules:
  - the marker is written by the two approved writers only;
  - it is never in an update payload;
  - every writer narrows in the same member;
  - the writers are called only from the approved callers;
  - only the Office persistence passes `submissionNarrowing`;
  - only `CreateDocumentRequest` has the property.
- `RecordOwnerAssignmentCensusTests`: `OfficeService.SaveAsync` → `SaveCoreAsync`. The body moved and still names the person.
- The `InvoiceSubmissionService` filing probe is a `new Entity { ... }` with no logical name, never written, so it is outside the task 146 create census.
- `RouteAuthorizationGuardTests.Ledger`: the reason for `InvoiceSubmissionEndpoints.cs` now names both routes; the count is unchanged.
- `DocumentAccessPermissionWriterGuardTests`: the reason for the stager names task 095.

## 5. Seeding proofs (each reverted; `git status` clean after)

| # | Seed | Fired |
|---|---|---|
| S1 | `document[DocumentSubmissionMarkerColumn] = false;` in `UpdateDocumentAsync` | `SubmissionMarkerWriterGuardTests`: "UPDATE PAYLOAD: DataverseServiceClientImpl.cs:938 in UpdateDocumentAsync" |
| S2 | The upload worker passes `submissionNarrowing: true` | "SUBMISSION FLAG from UploadFinalizationWorker.cs:1283 ProcessSingleAttachmentAsync" |
| S3 | Drop `mayReplayResponse` from the route | `Replay_AnIdenticalRetryWithoutTheHeader_AfterFailed_IsANewAttempt_NotTheCachedFailedOne` and `Replay_SameCallerSameK_LatestNotEnded_Is202WithTheIdenticalBody(Received)` (the cached answer skipped the re-queue) |
| S4 | Disable the `FromEmailSubmissionBody` branch of `EntityAccessFilter` | `EntityAccessFilter_GatesTheBodysCoreRecord_403Office009_WithReasonCodeAndEntityType` |

## 6. Contract items not implementable as written (reported, not changed)

- **503 `email_unavailable`** has no producer. The route never asks Graph:
  - OP-2 requires the Message-ID in the body, and step 0 requires the bytes;
  - the in-process save's Graph enrichment swallows its own faults.
  The code constant exists for task 094 and later.
- **422 `email_unidentifiable` reason `email_not_found`** (Graph 403/404) has no producer, for the same reason.
- **Reason `no_message_id`** is unreachable. A missing or non-RFC Message-ID is the body-shape 400 (OP-2).
- **404 `record_unavailable` for a Correction `invoiceId`** has no producer until task 097: Correction is `not_available` (OP-7).
- **409 RecordOwnerRefused** belongs to task 094 (the row's owner). The seam's `Refused` passes it through.

## 7. Known limits (K-class)

- K1: two concurrent *first* attempts carrying different headers can both run the save. The allocator lets only one create the row; the other answers as a replay. A second `.eml` can be left (no submission names it).
- K2: a seam failure after the save leaves the `.eml` saved, own-Restricted and marked. A retry reuses it through step 1a.
- K3: a capture fault that the save swallows looks like no communication. The Message-ID lookup decides, and a fault there is a 503.
- K4: a save still in flight for the same email answers `eml_ambiguous` (retry later).
- K5: an update failure after the create orphans the `.eml`, as `/office/save` does today.
- K6: a repeat that carries the SAME `X-Idempotency-Key` within 24 hours gets the cached 202. That is contract section 5, row 2. The cached answer:
  - does not re-queue a Received row;
  - would replay the first attempt even after it Failed.
  The contract has the client send a new value per user action, so a re-attempt after Failed is a new action with a new key.

## 8. Open questions

- Q1 (OP-5, owner): should a Failed attempt by another user let this user re-attempt? Today the answer is 409 `document_already_submitted`.
- Q2 (094): confirm the Supporting Stored File id derivation, `ForAttachment(submissionId, "eml-part-{ordinal}")`.
- Q3 (094): where `EmlMarkedPartReader` runs (intake or job), and so how its outcomes surface.

## 9. Gates (2026-10-27)

- Builds: all 5 test projects succeed (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spaarke.Core.Tests, Spe.Integration.Tests).
- ArchTests 1562/1562 passed. Integration 199 passed, 8 skipped.
- The broad unit filter (the brief's filter plus EmlMarkedPart, DocumentCommunicationFieldsStaging, EntityAccess and Idempotency): 10687 passed and 14 skipped, out of 10702.
  - 1 failed: `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_FailsLoudlyAndLogsTheContainerId`. It was not touched by 095; it timed out while two Release publishes were running.
  - Re-run alone, its class passed 55/55.
- The route contract suite passed 65/65, 5 consecutive runs (including the concurrent-retry test).
- **Publish size.** `dotnet publish -c Release`, framework-dependent linux-x64, PDBs included, `Compress-Archive -CompressionLevel Optimal`. Each side is a fresh short-path worktree, 192 files.

  | Build | Size |
  |---|---|
  | master 68048b83d5 | 37.79 MB |
  | merge-base with work/ebilling-r1 (8c076f972d) | 38.63 MB |
  | branch 4d78a0545c | 38.68 MB |

  - 095's own delta: **+0.05 MB**.
  - Delta against master: +0.89 MB. That is all ebilling-r1 work not yet on master.
- No package or csproj change, so no new CVE surface.
