# Task 118b: Submit Invoice from a document carries supporting documents

> Project: ebilling-r1. POML `tasks/118b-from-document-supporting-files.poml` (FULL, opus @ high). Escalation resolved 2026-11-03 by the main session: option A (stage the supporting copies at intake through one shared document read), split 118b-1 / 118b-2. Built and tested only: no deploy, no live Dataverse or Azure write, nothing sent to other sessions.

## 1. Part 118b-1: the read, the contract, the access check, the sameness rule

| Piece | File | Change |
|---|---|---|
| Shared document read | `Services/Finance/Submission/SourceDocumentFileRead.cs` (new, static) | The job's read of a source document's file, MOVED from `InvoiceSubmissionProcessingJobHandler.LoadSourceDocumentAsync`: one retrieve (`Columns`, the job's former `SourceDocumentColumns`), no pointer or an email archive is `NoFile`, the version (`InvoiceSubmissionPersister.VersionOf`, throws when none), the pointer's container verified first (`StorageUnverified`), the download (`NotDownloaded` when no stream), the bytes. Returns the raw `sprk_mimetype` (null when blank) and the communication. Faults propagate. Logs nothing. |
| Job | `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` | Calls the shared read and maps each outcome to the SAME `SubmissionStepFailure` code, permanence and log line as before (Gone and NoFile: `document_unavailable` permanent; StorageUnverified: `document_storage_unverified` permanent; NotDownloaded: `processing_failed` transient). The PDF content-type fallback stays the job's. Staging unchanged. |
| Contract | `Api/Finance/InvoiceSubmissionEndpoints.cs` (`SubmitInvoiceFromDocumentRequest`) | `supportingDocumentIds?` (array of GUIDs). `SupportingDocuments`: de-duplicated, sorted. `Validate()`: an empty id, the invoice document listed, or `distinct + 1 > SubmissionCommitGate.MaxFiles` is the 400 field error `supportingDocumentIds` (stable key; validated with the body, so before any rights query, read or stage). |
| Access check | `InvoiceSubmissionEndpoints.CheckSupportingDocumentsAsync` | After step 1 (`PrepareSourceAsync`) passed for the invoice document, before the intake: per id, the caller's own Read through `CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync` (gate variant, the invoice document's check), then the row app-only must link the body's `communicationId`. Unreadable, unknown, another communication, none: the uniform 404 (`FinanceAuthorizationFilter.UniformRecordNotFound`); an unreadable row is never read. Read fault: 503 `record_unavailable`. |
| Intake | `Services/Finance/Submission/InvoiceSubmissionService.cs`, `InvoiceSubmissionContracts.cs` | `InvoiceSubmissionRequest.SupportingDocumentIds` (default empty). `AnswerWithEarlierAsync`: the earlier attempt's set (`ReadAttemptSupportingDocumentsAsync`) must equal the request's (`SameSupportingDocuments`, set equality) for "same details"; else 409 `document_already_submitted`. A read fault of the earlier set is 503 `record_unavailable`, before step A. Step 2 is unchanged, so after a Failed attempt any set is a new attempt. Step 4a-supporting (interim): a NEW attempt naming supporting documents is refused 503 `ebilling.submission.failed` (`SupportingNotYetCopiedDetail`), read-only, after every check and before step A. |

### The key (POML constraint "idempotency")

The supporting set never joins the submission id. `ExistingDocumentSubmissionIdFor(document, generation)` is unchanged, so every existing key and every existing row's id are reproduced exactly, with or without the field (test `Retry_WithTheSameSet_ReplaysTheEarlierSubmission_WithTheDocumentsId`). Putting the set in the id would let one document make two submissions, so two invoices (D-134), and would fail the persister's derived-id check (`IsExistingDocumentSubmissionIdOf`). The set joins the replay comparison instead: same firm, record, number, correction target **and supporting set** replays; any difference is 409 `document_already_submitted` (the route's existing "other details" rule); after Failed, a new attempt (EB-D-167).

**The earlier set until 118b-2** (superseded by section 7). `ReadAttemptSupportingDocumentsAsync` answered the EMPTY set without a read: nothing staged document parts yet, and step 4a-supporting refused every new attempt that named any, so no attempt could have a stored set. From 118b-2 it reads the part stages (section 7).

### Section 11 justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Body field `supportingDocumentIds` | 094's from-document body; 095's `marks` (from-email) | Added to the existing body (optional, additive); the from-email body is another route's contract | 118 cannot send the Supporting attachments the user marked (118a), so they are dropped from the invoice. |
| `SourceDocumentFileRead` (static, no DI) | `InvoiceSubmissionProcessingJobHandler.LoadSourceDocumentAsync` (the code moved), `EmlMarkedPartReader` (a `.eml`'s MIME parts, no container check) | It is the job's code, moved; the reader is a different source | 118b-2 would need a second copy of the container-verified read (escalation option C, rejected). |
| `CheckSupportingDocumentsAsync` | `PrepareSourceAsync` (step 1 for the invoice document) | Same probe, same app-only read; its answers differ (the uniform 404 for every refusal, by the POML), so it is a sibling, not a parameter of step 1 | The caller could name a document they cannot Read and have the app copy it. |
| `InvoiceSubmissionRequest.SupportingDocumentIds`, `SameSupportingDocuments`, `ReadAttemptSupportingDocumentsAsync` | `AnswerWithEarlierAsync`'s other details, `SameCorrectionTarget` | Extended in place | A retry with another set would be answered with the first request's receipt. |

No package, no Dataverse column, no DI registration, no new storage path. BFF placement: in `Sprk.Bff.Api` beside the intake it serves (bff-extensions placement: same module, no new background work). No package was added, so no publish-size delta was measured and no CVE check was needed (POML: measure only when a package is added).

## 2. Seeding proof (the Read guard only, POML)

| Seed | Result | Restored |
|---|---|---|
| `CheckSupportingDocumentsAsync`: the Read refusal disabled (`if (false && !rights.HasFlag(AccessRights.Read))`) | `FromDocument_supporting_document_refusals_are_the_uniform_404` fails in all 3 cases (unreadable: the row is read and the request goes on; and the unknown id the harness answers is then readable): 3 failed | yes, file byte-identical to before |

Before/after for the other cases (the comparison, the interim refusal and the shape checks turned off together): 13 of the 53 tests in `InvoiceSubmissionSupportingDocumentsTests` + `InvoiceSubmissionEndpointsContractTests.FromDocument*` failed (every must-fire), all 53 pass after.

## 3. Tests

`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionSupportingDocumentsTests.cs` (8 tests, 15 cases): same set (order, repeats) replays with the document's id; another set 409 (5 cases); until 118b-2 the earlier set is empty (409 with a set, replay without); an earlier set that cannot be read is 503; after Failed another set is a new attempt (generation 2), not 409; the interim refusal before step A; a new attempt without the field is unchanged; set equality (4 cases).

`tests/integration/contract/Api/Finance/InvoiceSubmissionEndpointsContractTests.cs` (+5 tests, 9 cases): readable supporting documents pass, each checked once as the caller (OBO); the uniform 404 (unreadable, another communication, none), byte-identical to an unknown id, an unreadable row never read; the 400s (invoice document, empty id, 50 supporting); 49 supporting plus repeats pass the limit; a retry naming a set is 409 and the same request without it replays.

Job tests: unchanged and passing (the move keeps every outcome).

Test scope beyond the POML cases: the set-equality theory pins the comparison helper the 409 rests on; the 503 on an unreadable earlier set pins the fail-closed answer 118b-2's stage read must keep; the 49-document control is the must-not-fire side of the limit.

## 4. Gate (118b-1 head)

- Builds: Sprk.Bff.Api, Sprk.Bff.Api.Tests, Spaarke.ArchTests, Spaarke.Core.Tests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests: all succeed, 0 warnings.
- ArchTests: 1931 passed, 0 failed.
- Unit filter (Finance, Submission, Invoice, StoredFile, Idempotency, Jobs): 7024 passed, 5 skipped, 2 failed. The 2 are `InvoiceSubmissionByEmailContractTests.Step0Refusal_IsTheUniform404` (FlagTrue, NoSystemUserRow): they fail the same way on the base `834234768c` without 118b-1 (checked); not caused here (095a's by-email route; reported to the main session).

## 5. Known limits (118b-1)

| # | Limit | Why accepted |
|---|---|---|
| K1 | Until 118b-2, a new attempt that names supporting documents is refused 503 `failed` (interim). **Closed by 118b-2** (section 7). | Not shipped alone: 118 (the only caller that sends the field) waits for 118b. The refusal keeps the field from being dropped silently. |
| K2 | The supporting checks run one document at a time (up to 49 rights calls and 49 reads per request). | Read-only and bounded by the 400 limit; a batch probe is not available on the gate variant. |
| K3 | A supporting document's row is read app-only only for its communication link here; its file, email-archive flag and container are judged by the shared read when 118b-2 stages it. | 118b-2 owns the read and its refusals (before step A). |

## 6. Hand-off to 118b-2

1. **Stage at step 4a-supporting** (`InvoiceSubmissionService.SubmitExistingDocumentAsync`), replacing the interim refusal and `SupportingNotYetCopiedDetail`: after every read-only check; the shared read (`SourceDocumentFileRead.ReadAsync`) for each supporting document BEFORE step A (its refusals file nothing); then step A; then stage each under `StoredFileIds.ForAttachment(submissionId, "document-part-{i}")`, i = 1..N in `request.SupportingDocumentIds` order (already sorted), purpose `InvoiceSupportingFile`, parent the invoice (`InvoiceIdFor`), Firm-visible No, source = that document, its communication, its version and SHA-256; the first part's manifest lists the parts. The intake has no `ISpeFileOperations` or `RecordContainerResolver` today: resolve them in the route (as `SourceServices` does) or add them to the service's constructor (the test harness's `Build()` and `WithStoredSets` construct it).
2. **The earlier set:** implement `ReadAttemptSupportingDocumentsAsync` over those stages (part 1's manifest, each part's `SourceDocumentId`); a fault throws (the caller already answers 503); no part 1 is the empty set. Delete the "until 118b-2" paragraph in its doc comment and the test `UntilTheParts_AreStaged_AnEarlierAttemptHasTheEmptySet`; replace the interim tests (`NewAttempt_WithSupportingDocuments_IsRefusedBeforeStepA_UntilTheCopiesAreBuilt`, the contract test `FromDocument_readable_supporting_documents_pass_the_checks_as_the_caller` and `Retry_AfterFailed_WithAnotherSet_IsANewAttempt_Not409`'s last assertion) with the Accepted cases.
3. **SameSource parts list** (`StoredFileBlobs.SameSource`) and the **persister's document-parts branch** (POML constraint "escalation resolved"), and the scan rule.
4. Contract text: replace the "Interim until 118b-2" sentence in `094-submit-invoice.md` section 7.

All four were done in 118b-2 (section 7).

## 7. Part 118b-2: staging, the parts list, the reuse check, the persister branch

Commit `ab0ae5a0cc` (code and tests). The escalation trigger did not fire: the copies go through the from-email path's own pieces (`StoredFileService.StageAsync` with a `StagedSourceManifest`, `ReadStagedSourceAsync`, the persister's Supporting-row builder, `BuildRow`, the commit gate); no second copy of the staging rule or of the document read was made.

| Piece | File | Change |
|---|---|---|
| Step 4a-supporting (replaces the interim refusal) | `InvoiceSubmissionService.CheckSupportingDocumentsAsync` | Read-only, after every other read-only refusal, before step A. With supporting documents: the file store (`CanStageAsync`; fault 503 `failed`, not configured 422 `storage_not_configured`), then each document in sorted id order through the route's binding of the shared read. Gone, no file or an email archive, storage unverified, or a file whose `sprk_relatedcommunication` is no longer the route's communication: 422 `document_unavailable`. Not downloaded or a fault: 503 `record_unavailable`. No answer names a document. Without supporting documents: any part 1 staged under this attempt's id (readable or not) is 409 `document_already_submitted`; a read fault 503. |
| Step 4c (stage) | `InvoiceSubmissionService.StageSupportingDocumentsAsync`, `DocumentPartSpecOf` | After step A, on `CancellationToken.None`: part `i` under `StoredFileIds.ForAttachment(submissionId, "document-part-{i}")`, purpose Supporting, parent `InvoiceIdFor`, Firm-visible No, source = that document, its communication, its version, the SHA-256 of the bytes read; no attachment identity (a document is copied whole). Part 1's manifest lists `1..N`; each part records its extension. ContentConflict 409 `document_already_submitted`, NotConfigured 422, anything else 503 `failed` (all after filing: "that filing is kept"). No compensation (from-email's rule: deterministic ids). |
| The dependencies (hand-off item 1) | `InvoiceSubmissionRequest.ReadSupportingDocument` (internal), `InvoiceSubmissionEndpoints.SubmitFromDocumentAsync` | Resolved in the route: the route binds `SourceDocumentFileRead.ReadAsync` to `IGenericEntityService`, `ISpeFileOperations` and `RecordContainerResolver` from `RequestServices`, only when the body names supporting documents. Not the constructor: `InvoiceSubmissionServiceTests.The_intake_has_no_way_to_reach_SharePoint_Embedded_or_create_a_document` pins (task 092) that the intake takes no SPE or `RecordContainerResolver` seam. The step-A hook (`FileSourceBeforeWrite`) is the same pattern. No DI registration added. |
| The earlier set | `InvoiceSubmissionService.ReadAttemptSupportingDocumentsAsync` | Over the part stages through `InvoiceSubmissionPersister.ReadDocumentPartStagesAsync`, the ONE reader the persister commits with. No part 1: the empty set. A store that cannot stage (not configured, or another environment's segment) holds none: the empty set, so a request without the field is unchanged there (test). Parts that do not read back as part 1 lists them, or a fault: throws (the 503 118b-1 built). |
| The reader | `InvoiceSubmissionPersister.ReadDocumentPartStagesAsync`, `DocumentPartIdentity`, `DocumentPartFileId` | Part 1's list must be exactly `1..N` with `N < MaxFiles`; every part present, its source readable, a source document and version, no attachment identity; the documents distinct. Otherwise null. |
| The reuse check | `StoredFileBlobs.SameSource` | `MetadataEmlParts` joins `SourceIdentityKeys`. A part 1 left by a request of the same attempt with another set (no row created) no longer reuses: 409, never extra files. From-email: the list is fixed by the key its ids derive from, so its retries carry the same list; its suites pass unchanged (`SubmitInvoiceFromEmailOperationTests`, the from-email contract and persister tests, inside the gate filter). The key keeps its name (it is on staged blobs already); the doc comment says it now also carries the document parts. |
| The persister | `InvoiceSubmissionPersister.PersistCoreAsync` (source-document branch), `SupportingPartSpec` | A from-document submission reads its parts; each becomes a Supporting row in the one transaction (`sprk_invoice` the new invoice through the parent, `sprk_sourcedocument` that document, Firm-visible No, named `{tracking}-{i}{ext}`, the creator person). Parts that do not read back: Refused `file_unservable`, nothing written. The row spec builder is now shared with the from-email parts (`SupportingPartSpec`, the same expression moved out of the from-email loop). Scan state: `BuildRow`, the existing rule (Not yet servable until the gate flips). The commit gate re-runs each file's own `sprk_sourcedocument` and enforces `MaxFiles` (unchanged). |
| The shared read | `SourceDocumentFileRead` | Also reads `sprk_filename` (returned as `FileName`), used only for the copy's extension (the from-email parts record theirs). The job ignores it; its outcomes, codes and logs are unchanged. |

### Section 11 justification (118b-2's new surface)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceSubmissionRequest.ReadSupportingDocument` (internal) | `FileSourceBeforeWrite` (the route's step-A hook); the job's constructor seams | The hook pattern, extended with one more route-bound delegate; the constructor is pinned SPE-free (task 092) | The intake cannot read a supporting document's file, so nothing can be copied. |
| `ReadDocumentPartStagesAsync`, `DocumentPartIdentity`, `DocumentPartFileId` | `ReadFromEmailSupportingAsync`, `EmlPartIdentity` | The from-email reader keys on the Invoice part's `.eml`; these key on part 1. One reader serves the intake and the persister | The replay and the commit would each need their own reading of the parts list (two copies of the rule). |
| `CheckSupportingDocumentsAsync`, `StageSupportingDocumentsAsync`, `DocumentPartSpecOf` | `SubmitInvoiceFromEmailOperation` steps 2b, 3, 5 and `SpecOf` | They are private steps of the existing intake (the operation's are another source's); `DocumentPartSpecOf` is `SpecOf`'s twin so tests stage exactly as the intake does | 118b's goal fails: the supporting documents are not copied. |
| `sprk_filename` in the shared read | `SourceDocumentFileRead.Columns` | One more column of the same retrieve | A Supporting copy has no extension, unlike a from-email one. |
| `MetadataEmlParts` in `SameSource` | `SameSource` | One key added | A leftover part 1 of another set is reused and the commit copies files the request did not name. |

No package, no Dataverse column, no DI registration, no new storage path, no new endpoint. No package was added, so no publish-size delta was measured and no CVE check was needed (as 118b-1).

### Seeding proof (the SameSource parts check, per the brief)

| Seed | Result | Restored |
|---|---|---|
| `StoredFileBlobs.SourceIdentityKeys` without `MetadataEmlParts` | `NewAttempt_ALeftoverStageOfAnotherSet_Is409_NeverExtraFiles` fails: the request with fewer documents is 202 over the leftover parts, not 409 (1 failed of 85 in the supporting, from-email and blob suites) | yes, byte-identical (`cmp`) |

Fail before: the 118b-2 tests use members 118b-2 adds (`ReadSupportingDocument`, `DocumentPartFileId`, `DocumentPartSpecOf`), so they do not compile against `08ec7ee1dc`; on that base the interim refusal answered every new attempt with supporting documents 503 (the tests they replace).

### Tests (118b-2)

`InvoiceSubmissionSupportingDocumentsTests` (13 tests, 26 cases; replaced the interim ones named in section 6):
- `NewAttempt_WithTwoSupportingDocuments_ReadsBeforeStepA_StagesEachCopyWithItsSource` (acceptance; reads before step A, sorted part numbers, each part's source and version, part 1's list, the extension, no attachment identity, the bytes, writes on `None`).
- `Retry_ComparesWithTheSetTheEarlierAttemptStaged` (a replay returns the same submission; fewer or none 409; no parts is the empty set).
- `Retry_AfterFailed_WithAnotherSet_IsANewAttempt_Not409` (now Received at generation 2, parts under the new id).
- `NewAttempt_ALeftoverStageOfAnotherSet_Is409_NeverExtraFiles` (seed-proofed; fewer, more, none 409; the same set reuses).
- `NewAttempt_WithoutSupportingDocuments_AnIncompleteLeftover_Is409`; `Retry_TheEarlierPartsDoNotReadBack_Is503`.
- `NewAttempt_ASupportingDocumentCannotBeRead_IsRefusedBeforeStepA` (7 cases).
- `NewAttempt_WithoutSupportingDocuments_AStoreNotConfigured_IsUnchanged`.

`InvoiceSubmissionPersisterTests` (+2 tests, 3 cases): `Source_WithTwoSupportingDocuments_CommitsOneInvoicePdfAndTwoSupporting_EachWithItsSourceDocument` (acceptance, end to end: the REAL intake stages into the store the REAL persister and commit gate read; 1 Invoice PDF, 2 Supporting rows with their own `sprk_sourcedocument`, all Servable); `Source_SupportingPartsDoNotReadBack_IsRefused_NoTransaction` (missing part, list not 1..N).

`InvoiceSubmissionEndpointsContractTests`: `FromDocument_two_readable_supporting_documents_are_202_and_each_copy_is_staged_with_its_source` replaces the interim must-not-fire test (the route's binding: each document checked once as the caller, downloaded once through the REAL container check, staged with its source; one row, one job). The harness registers `ISpeFileOperations` (mock) and `RecordContainerResolver` (`TestRecordContainerResolver.ForBusinessUnitContainers`). The retry-409 test's name no longer says "until 118b-2".

Beyond the POML's cases, with one line each: the read refusals pin that every supporting refusal happens before step A (the POML's "nothing staged" rule for the new failure modes); the incomplete-leftover case pins that a stuck answer is the 409, not a retried 503; the unconfigured-store case pins "absent field behaves as today"; the parts-do-not-read-back cases pin fail-closed reads on both readers.

### Gate (118b-2 head)

- Builds: Spe.Integration.Tests, Sprk.Bff.Api.IntegrationTests, Spaarke.ArchTests, Spaarke.Core.Tests, Sprk.Bff.Api.Tests (with Sprk.Bff.Api): all succeed, 0 warnings.
- ArchTests: 1950 passed, 0 failed.
- Unit filter (Finance, Submission, Invoice, StoredFile, Idempotency, Jobs): 7048 passed, 5 skipped, 0 failed. `Step0Refusal_IsTheUniform404` passes (fixed on work at `82786ce7f3`).

### Known limits (118b-2)

| # | Limit | Why accepted |
|---|---|---|
| K2 | A part 1 left by a refused request of this attempt (no row) gives a stuck 409 to any request of the same attempt with another set, until the stage is removed (as from-email, 094 section 11.3). | POML-accepted K2. It fails closed (no extra files, nothing committed); the same request as the leftover's completes it. |
| K2-b | Two concurrent requests of the same attempt, one with a set and one without, can race: the one without reads no part 1, the other stages and loses the create to it, then replays its receipt (the same set as its stages); the submission then commits the parts the other caller named. | A same-instant race of two different requests for one document; the parts were checked against their caller's Read and communication; the gate re-runs every file's source. |
| K4 | The intake holds every supporting document's bytes in memory between the reads (before step A) and the stages (after it): up to 49 files, each bounded only by the store's size limit. | The order (read before step A, stage after) is the escalation's resolution; from-email holds the whole `.eml` the same way. A per-file size cap is not in the POML; reported to the main session for a follow-up decision. |
| K3 | A supporting document's version is not re-checked at the commit (the invoice document's is, with the guard); its bytes are pinned by the SHA-256 recorded at stage, and the gate re-runs its filing and isolation after the commit. | The from-email parts' rule (094 section 10.6, "version guard decision"). |
