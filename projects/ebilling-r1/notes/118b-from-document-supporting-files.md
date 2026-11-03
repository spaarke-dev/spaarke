# Task 118b: Submit Invoice from a document carries supporting documents

> Project: ebilling-r1. POML `tasks/118b-from-document-supporting-files.poml` (FULL, opus @ high). Escalation resolved 2026-11-03 by the main session: option A (stage the supporting copies at intake through one shared document read), split 118b-1 / 118b-2. Built and tested only: no deploy, no live Dataverse or Azure write, nothing sent to other sessions.

## 1. Part 118b-1 (this part): the read, the contract, the access check, the sameness rule

| Piece | File | Change |
|---|---|---|
| Shared document read | `Services/Finance/Submission/SourceDocumentFileRead.cs` (new, static) | The job's read of a source document's file, MOVED from `InvoiceSubmissionProcessingJobHandler.LoadSourceDocumentAsync`: one retrieve (`Columns`, the job's former `SourceDocumentColumns`), no pointer or an email archive is `NoFile`, the version (`InvoiceSubmissionPersister.VersionOf`, throws when none), the pointer's container verified first (`StorageUnverified`), the download (`NotDownloaded` when no stream), the bytes. Returns the raw `sprk_mimetype` (null when blank) and the communication. Faults propagate. Logs nothing. |
| Job | `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` | Calls the shared read and maps each outcome to the SAME `SubmissionStepFailure` code, permanence and log line as before (Gone and NoFile: `document_unavailable` permanent; StorageUnverified: `document_storage_unverified` permanent; NotDownloaded: `processing_failed` transient). The PDF content-type fallback stays the job's. Staging unchanged. |
| Contract | `Api/Finance/InvoiceSubmissionEndpoints.cs` (`SubmitInvoiceFromDocumentRequest`) | `supportingDocumentIds?` (array of GUIDs). `SupportingDocuments`: de-duplicated, sorted. `Validate()`: an empty id, the invoice document listed, or `distinct + 1 > SubmissionCommitGate.MaxFiles` is the 400 field error `supportingDocumentIds` (stable key; validated with the body, so before any rights query, read or stage). |
| Access check | `InvoiceSubmissionEndpoints.CheckSupportingDocumentsAsync` | After step 1 (`PrepareSourceAsync`) passed for the invoice document, before the intake: per id, the caller's own Read through `CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync` (gate variant, the invoice document's check), then the row app-only must link the body's `communicationId`. Unreadable, unknown, another communication, none: the uniform 404 (`FinanceAuthorizationFilter.UniformRecordNotFound`); an unreadable row is never read. Read fault: 503 `record_unavailable`. |
| Intake | `Services/Finance/Submission/InvoiceSubmissionService.cs`, `InvoiceSubmissionContracts.cs` | `InvoiceSubmissionRequest.SupportingDocumentIds` (default empty). `AnswerWithEarlierAsync`: the earlier attempt's set (`ReadAttemptSupportingDocumentsAsync`) must equal the request's (`SameSupportingDocuments`, set equality) for "same details"; else 409 `document_already_submitted`. A read fault of the earlier set is 503 `record_unavailable`, before step A. Step 2 is unchanged, so after a Failed attempt any set is a new attempt. Step 4a-supporting (interim): a NEW attempt naming supporting documents is refused 503 `ebilling.submission.failed` (`SupportingNotYetCopiedDetail`), read-only, after every check and before step A. |

### The key (POML constraint "idempotency")

The supporting set never joins the submission id. `ExistingDocumentSubmissionIdFor(document, generation)` is unchanged, so every existing key and every existing row's id are reproduced exactly, with or without the field (test `Retry_WithTheSameSet_ReplaysTheEarlierSubmission_WithTheDocumentsId`). Putting the set in the id would let one document make two submissions, so two invoices (D-134), and would fail the persister's derived-id check (`IsExistingDocumentSubmissionIdOf`). The set joins the replay comparison instead: same firm, record, number, correction target **and supporting set** replays; any difference is 409 `document_already_submitted` (the route's existing "other details" rule); after Failed, a new attempt (EB-D-167).

**The earlier set until 118b-2.** `ReadAttemptSupportingDocumentsAsync` answers the EMPTY set without a read: nothing stages document parts yet, and step 4a-supporting refuses every new attempt that names any, so no attempt can have a stored set. A request without the field therefore compares exactly as before; one with the field against an earlier attempt is 409.

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
| K1 | Until 118b-2, a new attempt that names supporting documents is refused 503 `failed` (interim). | Not shipped alone: 118 (the only caller that sends the field) waits for 118b. The refusal keeps the field from being dropped silently. |
| K2 | The supporting checks run one document at a time (up to 49 rights calls and 49 reads per request). | Read-only and bounded by the 400 limit; a batch probe is not available on the gate variant. |
| K3 | A supporting document's row is read app-only only for its communication link here; its file, email-archive flag and container are judged by the shared read when 118b-2 stages it. | 118b-2 owns the read and its refusals (before step A). |

## 6. Hand-off to 118b-2

1. **Stage at step 4a-supporting** (`InvoiceSubmissionService.SubmitExistingDocumentAsync`), replacing the interim refusal and `SupportingNotYetCopiedDetail`: after every read-only check; the shared read (`SourceDocumentFileRead.ReadAsync`) for each supporting document BEFORE step A (its refusals file nothing); then step A; then stage each under `StoredFileIds.ForAttachment(submissionId, "document-part-{i}")`, i = 1..N in `request.SupportingDocumentIds` order (already sorted), purpose `InvoiceSupportingFile`, parent the invoice (`InvoiceIdFor`), Firm-visible No, source = that document, its communication, its version and SHA-256; the first part's manifest lists the parts. The intake has no `ISpeFileOperations` or `RecordContainerResolver` today: resolve them in the route (as `SourceServices` does) or add them to the service's constructor (the test harness's `Build()` and `WithStoredSets` construct it).
2. **The earlier set:** implement `ReadAttemptSupportingDocumentsAsync` over those stages (part 1's manifest, each part's `SourceDocumentId`); a fault throws (the caller already answers 503); no part 1 is the empty set. Delete the "until 118b-2" paragraph in its doc comment and the test `UntilTheParts_AreStaged_AnEarlierAttemptHasTheEmptySet`; replace the interim tests (`NewAttempt_WithSupportingDocuments_IsRefusedBeforeStepA_UntilTheCopiesAreBuilt`, the contract test `FromDocument_readable_supporting_documents_pass_the_checks_as_the_caller` and `Retry_AfterFailed_WithAnotherSet_IsANewAttempt_Not409`'s last assertion) with the Accepted cases.
3. **SameSource parts list** (`StoredFileBlobs.SameSource`) and the **persister's document-parts branch** (POML constraint "escalation resolved"), and the scan rule.
4. Contract text: replace the "Interim until 118b-2" sentence in `094-submit-invoice.md` section 7.
