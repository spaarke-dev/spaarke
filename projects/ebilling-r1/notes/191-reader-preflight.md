# Task 191: invoice reader preflight at intake

Branch `task/ebill-191-reader-preflight`, from `origin/work/ebilling-r1` @ `8b49cb2432`. Owner-approved 2026-10-31, relayed through word-add-in-r1 after the first dev UAT. Wire contract: `notes/095-from-email-contract.md` section 6 (new 422) and section 9 (the `ai.invoice.*` outcome codes).

## Problem

With `ContentUnderstanding:Endpoint` empty the reader refuses with `ai.invoice.read.not_configured`, but only in the background job. By then the intake had answered 202, allocated a tracking number, created a submission row and (from-email, from-document) filed the communication, and the submission ended Failed. The file store already does the right thing: `storage_not_configured` is a synchronous 422, checked before step A, with nothing written.

## Change

New synchronous 422 `ebilling.submission.reader_not_configured` (`InvoiceSubmissionErrors.ReaderNotConfigured`), answered when the invoice reader is not set up, before anything is written or filed, on all three intake routes. Same status, ProblemDetails shape (`InvoiceSubmissionOutcome.Refused` / `FromEmailSubmitResult` refusal) and "Nothing was submitted." wording as the storage check.

| Route | Entry point | Where the check sits |
|---|---|---|
| from-email | `SubmitInvoiceFromEmailOperation.SubmitAsync` | step 2c, directly after the store's check (2b) and before the marked-part read (3) and step A (4) |
| from-document (confirm) | `InvoiceSubmissionService.SubmitExistingDocumentAsync` | step 4-reader, after `DecideAsync` (the intake's checks) and before a Correction's checks (4a), step A (4b) and the create (5). The route stages no file, so there is no store check beside it |
| upload (SPA and internal) | `InvoiceSubmissionService.SubmitAsync` | step 2b, after `DecideAsync` and before `WriteAsync` (the first stage / write). The store's check is the stage itself, so when the reader is not set up the store is asked first (`CanStageAsync`, read-only) and its code wins if it is missing too |

Order rule kept: storage first. Where both are missing, all routes that have a store answer `storage_not_configured`. The common path (reader set up) adds no read to the upload route.

### One test of "configured"

`InvoiceSubmissionService` takes `IOptions<ContentUnderstandingOptions>` and exposes `ReaderIsConfigured => options.IsConfigured` and `ReaderNotConfiguredRefusal()`. `ContentUnderstandingOptions.IsConfigured` (`!string.IsNullOrWhiteSpace(Endpoint)`) is what `ContentUnderstandingClient.IsConfigured` returns and so what `InvoiceAi.ReadInvoiceAsync` tests. The intake reads the same bound options object the client is built with (`IOptions`, singleton binding), so no second parser exists and the two cannot disagree. A test pins it for empty, null, blank and set endpoints.

### The job's backstop is unchanged

If the setting is removed between intake and job, `InvoiceAi.ReadInvoiceAsync` still throws `FeatureDisabledException(ai.invoice.read.not_configured)`, the processor returns Failed with that code, and the handler stores it as a permanent outcome (`InvoiceSubmissionProcessingJobHandlerTests.FailedOutcome_IsFailedWithItsCode` already covers the stored code). No job code changed.

### Contract: `ai.invoice.*` inventory (section 9)

Inventoried from the code (`Services/Ai`, `Services/Finance`, `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler`). A code reaches `sprk_outcomereasoncode` three ways: a `FeatureDisabledException` from the read or resolution step (processor returns Failed with its `ErrorCode`; handler `FailAsync`, permanent), an `InvoiceAiException` from line coding (handler catch uses `ReasonCode`; `TransientAsync` retries and writes Failed on the final delivery), or a code the job names itself.

| Code | Source | Permanent or retried |
|---|---|---|
| `ai.invoice.read.not_configured` | `InvoiceAi.ReadNotConfiguredCode`, `ReadInvoiceAsync` | permanent |
| `ai.invoice.disabled` | `NullInvoiceAi` kill switch | permanent |
| `ai.invoice.binding_missing` | `InvoiceAiException`, line coding | retried, stored on the final delivery |
| `ai.invoice.completion_failed` | `InvoiceAiException`, line coding | retried, stored on the final delivery |
| `ai.invoice.output_invalid` | `InvoiceAiException`, line coding | retried, stored on the final delivery |

Not listed on purpose: `ai.linear-consumers.disabled` (not `ai.invoice.*`; line coding catches it and holds the invoice for review as `coding_unavailable`, it never becomes a submission outcome) and the `invoice.read.*` codes (already in section 9, no `ai.` prefix).

## CLAUDE.md §11: the new error code and check

1. **Existing.** Overlaps the `storage_not_configured` preflight (same shape, same place) and the job's `ai.invoice.read.not_configured` outcome (same condition, found too late). It reuses `ContentUnderstandingOptions.IsConfigured` and `InvoiceSubmissionOutcome.Refused`.
2. **Extension.** The job's code cannot be extended: it runs after the 202, the tracking number, the row and the filing, which is the defect. The check is a method and a constructor option on the existing `InvoiceSubmissionService` (no new type, interface, endpoint, DI registration or package; `IOptions<ContentUnderstandingOptions>` is already registered by `AnalysisServicesModule`).
3. **Cost of doing nothing.** In an environment where the reader is not set up, every submit answers 202 with a tracking number and files the email, then the submission ends Failed; the user finds out only by polling, and the filing and the burned tracking number stay behind (first dev UAT, word-add-in-r1).

New surface: one error-code constant and one check. No new endpoint, service or DI registration.

## BFF §10

Placement: `Services/Finance/Submission` beside the intake it extends; in-request work; no background work, package, endpoint or DI registration. One added constructor dependency on an existing singleton options registration. Publish-size delta and the CVE check: see the task report.

## Tests

All on the existing doubles (`InvoiceSubmissionServiceTests.Harness`, `FromEmailWorld`); the harness gets `Reader` options, configured by default so every other test takes the normal path.

| Test | Pins |
|---|---|
| `Upload_TheReaderIsNotConfigured_Is422ReaderNotConfigured_NothingStagedTrackedOrCreated` (2 rows) | upload: 422, no stage, allocation, job or write; configured then takes the normal path; store also missing gives the storage code |
| `InvoiceSubmissionExistingDocumentTests.ExistingDocument_TheReaderIsNotConfigured_...` | from-document: 422 before step A, create and job; configured then takes the normal path |
| `Submit_TheReaderIsNotConfigured_Is422ReaderNotConfigured_BeforeStepA_NothingFiledOrWritten` (2 rows) | from-email: 422, step A not run, no stage, allocation, job or write; configured then takes the normal path; store also missing gives the storage code |
| `The_intakes_reader_check_and_the_readers_own_agree` (4 rows) | the intake check equals `ContentUnderstandingClient.IsConfigured` for empty, null, blank and set endpoints |

Nothing beyond the brief. The "configured gives the normal path" criterion is asserted inside each route's test (second submit after setting the endpoint), not as separate tests.

Fail-before: with `ReaderIsConfigured` forced to `true` (the pre-change behaviour), 6 of the 9 new rows fail; the 3 that pass by design are the two "store also missing" rows (the store's code is the pre-change answer) and the "set endpoint" agreement row.

## Known limits

- **Replays are not refused.** A retry of a from-email or from-document submission that already exists (the earlier-submission answer) returns that submission as before, even if the reader has since been removed; if it is still Received it is re-queued and the job's backstop code applies. The check guards new attempts, which are the ones that would allocate, create and file.
- **Options are read as the reader reads them.** `IOptions` is a startup snapshot, as the reader's is, so a setting changed at runtime is seen by neither until restart. That is the agreement the task asks for.
