# Task 044 - Invoice submission operation (FR-17, NFR-10)

Status: completed (fix round 2). Rigor FULL; step mode directional. Built on D-87 (the row is created by the allocator with its number) and D-91 (the firm
invoice number is required at upload).

## 1. What was built

| File | Role |
|---|---|
| `Services/Finance/Submission/InvoiceSubmissionService.cs` | The one intake both channels call: validate, read, upload, document, submission (through `ITrackingNumberAllocator`), job; compensation; `GetAsync`, `ListAsync` |
| `Services/Finance/Submission/InvoiceSubmissionContracts.cs` | Request, outcome, receipt, status view, channel/status enums, stable error codes (`InvoiceSubmissionErrors`) |
| `Api/Finance/InvoiceSubmissionEndpoints.cs` | Internal routes; the shared multipart reader and result mapping |
| `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs` | External (SPA) routes, mounted on the `/api/v1/external` group |
| `Api/ExternalAccess/ExternalInvoiceSubmissionAuthorizationFilter.cs` | The external route gate: core record, firm, read scope |
| `Api/Filters/FinanceAuthorizationFilter.cs` | Two constants: `WorkAssignmentEntitySet`, `InvoiceSubmissionEntitySet` |
| `Spaarke.Core/Auth/OperationAccessPolicy.cs` | `finance.submit_invoice` = Read + AppendTo |
| One line each | `ExternalAccessEndpoints.cs` (mount), `EndpointMappingExtensions.cs` (map), `FinanceModule.cs` (scoped registration) |

Files of unified-access-control-r2 touched: `Api/ExternalAccess/ExternalAccessEndpoints.cs` (one mount line). Nothing under
`Infrastructure/ExternalAccess/**` was edited; the filter reuses `CallerPrincipal.RightsOn` (internal) and
`ContactGrantorAuthorizationFilter.ReadMembershipsAsync` (internal static, Api layer), both existing seams.

### Routes

| Route | Authorization (before the handler) | Then |
|---|---|---|
| `POST /api/finance/{matters\|projects\|workassignments}/{id}/invoice-submissions?firmId=` | `AddFinanceAuthorizationFilter` as the caller: Read + AppendTo on the core record (`finance.submit_invoice`), AppendTo on the firm (`finance.attach_invoice`) | `RequireEbillingEnabled` |
| `GET /api/finance/invoice-submissions/{id}` | Read on the submission (`finance.read`, `sprk_invoicesubmissions`), uniform 404 | `RequireEbillingEnabled` |
| `POST /api/v1/external/{matters\|projects\|workassignments}/{id}/invoice-submissions[?firmId=]` | group: ExternalCollaboration + caller principal; filter: Read + Create on the core record, firm from memberships | `RequireEbillingEnabled` |
| `GET /api/v1/external/invoice-submissions/{id}` | the submission's firm is one of the caller's and the caller reads its core record; else uniform 404 | `RequireEbillingEnabled` |
| `GET /api/v1/external/invoice-submissions` | the caller's firms (none -> empty list) | `RequireEbillingEnabled` |

Multipart form (both POSTs): `file` (PDF), `firmInvoiceNumber` (required), `poNumber` (optional, at most 100).

### Service order (`SubmitAsync`)
1. Payload: firm invoice number (blank -> 400 `firm_invoice_number_required`, longer than 89 -> 400 `firm_invoice_number_too_long`), PO length, firm and
   record ids, file present (400 `file_required`), size (413 `too_large`), PDF (declared `application/pdf` AND bytes start `%PDF-`, else 400 `not_pdf`).
   Nothing is read before this step passes (D-91).
2. Firm is an active `sprk_organization` (else 400 `firm_invalid`).
3. Core-record resolver fields through the 013 guard (missing / incomplete -> 422 `record_unavailable`; read fault -> 503).
4. Owner: `IRecordOwnershipResolver` ForParents(core record), RequestedBy = the systemuser (SPA workforce) or the caller oid (internal); refused -> 409
   (`ProblemDetailsHelper.RecordOwnerRefused`). Applied to the row here.
5. Container: `RecordContainerResolver.ResolveForRecordAsync(core)`; Unresolved / FailClosed / empty -> 422 `storage_not_configured`.
5b. (Task 191, 2026-10-31; step 5 is the 092 stage since task 092.) Invoice reader not set up (`ContentUnderstanding:Endpoint` empty) -> 422 `reader_not_configured`, before the stage and so before any write (`191-reader-preflight.md`).
6. Upload `{client stem}-{submissionId:N}.pdf` (SanitizeFileName, `ConflictBehavior.Fail`).
7. Document: SPA via `ExternalDataService.CreateDocumentAsync` (parent bind, team, creator contact), internal via `IDocumentDataverseService`; then type
   Invoice (100000001) and `application/pdf`.
8. Submission via `CreateSubmissionAsync(submissionId, firm, number, fields)`: name, Received, channel, document, received-on, PO, correlation id,
   submitter contact (SPA), core lookup, the five resolver fields, owner + creator person. Refused -> 503 `ebilling.tracking.unavailable`.
9. Job `InvoiceSubmissionProcessing`, payload `{ "submissionId": ... }`, `SubjectId` = `IdempotencyKey` = submission id.

**Idempotency is per submission, not per request.** The submission id is minted once per request; it de-duplicates the processing job of that
submission and nothing else. A client that retries after losing the 202 sends a new request, gets a new submission id and creates a second
submission with its own tracking number (INV.2). That is by design under D-37 / D-88; nothing here recognises a repeated upload.

**Writes ignore the caller's token** (fix round 2). From the upload to the enqueue every call runs under `CancellationToken.None`: Graph, Dataverse
and Service Bus finish a request they received even when the caller abandons it, and the abandoned call's reply (the id compensation needs) is lost.
A client that disconnects mid-write therefore gets a complete submission it never hears about, not orphans. A caller already gone before the upload
writes nothing (`OperationCanceledException`, nothing to undo).

Any failure after step 6 compensates in reverse order with its own token (submission row, document row, file); a failed compensation is logged and
never masks the original. No path creates `sprk_invoice`. Each write's compensation works without the write's reply:
- File: by the id the upload returned; with no reply, by its path (`SpeFileStore.GetDriveItemByPathAsync`). The name carries the submission id, so
  whatever sits at that path is this request's own upload.
- Document, internal: the id is chosen before the create and sent with it (`CreateDocumentRequest.Id`); deleted by it, not-found ignored.
- Document, SPA: the external create mints the id; with no reply, the row is found by `sprk_graphitemid` = the uploaded item and deleted.
- Document typing update failing: the row id is already held, so the row is deleted.
- Submission: treated as possibly existing from the moment the allocator is asked; deleted by its id, not-found ignored. An allocator refusal
  creates nothing and deletes nothing.
- 409 on the upload (fix round 2, choice: **delete and refuse**, not "look up and continue"): the name is unique to this submission, so the
  conflict is the Graph handler's retried PUT colliding with its own committed first PUT. The file is found by its path and deleted, and the
  request is refused 409 `failed` ("Nothing was saved; try again"). Continuing with the found item would trust bytes this request never saw
  acknowledged; a retry is cheap and lands on a fresh name.

Read faults (fix round 2): a firm read that faults is 503 `firm_unverifiable`; a submission read or list that faults is 503 `record_unavailable`
(`InvoiceSubmissionService.ReadUnavailable`, rendered by the internal GET, the external read-one filter and the external list). A fault is never a
500, a "not found" or "no firm".

## 2. Response contract (tasks 049 / 051 / 052)

- 202 `Location: {statusUrl}`, body `{ submissionId, jobId, trackingNumber, statusUrl }`. `trackingNumber` is never null (D-87 / D-91). Internal
  `statusUrl` = `/api/finance/invoice-submissions/{id}`, external = `/api/v1/external/invoice-submissions/{id}`.
- Status view (both GETs; the list returns an array of it, newest first, at most 50): `submissionId, trackingNumber, firmInvoiceNumber, poNumber,
  status` (Received / Processing / Accepted / Rejected / Failed), `channel` (SPA / Internal / ...), `receivedOn, outcomeReasonCode, documentId, firmId,
  coreRecord { kind, recordId }, createdOn`.
- Refusals: ProblemDetails with `errorCode` from `InvoiceSubmissionErrors` (`ebilling.submission.*`), `ebilling.disabled` (503),
  `ebilling.tracking.unavailable` (503). The internal 403s come from FinanceAuthorizationFilter (its `reasonCode`), not `record_forbidden`.
- 049: the job payload is `{submissionId}`; the row carries the document, the core lookup and the five resolver fields. A row the job reads may be
  missing if an enqueue reported failure after Service Bus accepted the message (compensation deleted the row): treat a missing submission as done.

## 3. Decisions and deviations

- **Operation key** (step 1): new `finance.submit_invoice` = Read + AppendTo. `finance.attach_invoice` (AppendTo only) names attaching an invoice and
  omits Read, which the receipt and status route need. The firm check reuses `finance.attach_invoice` (the confirm route's vendor-org reasoning).
- **External core-record check**: the POML names `IsOperationPermittedAsync`; it takes a `WorkforcePrincipal` and cannot answer for a CIAM contact.
  The filter uses the principal's post-veto rights (`RightsOn`), which are that service's composed answer for both planes and the expression the
  external document upload already uses. Required: Read + Create (Collaborate or Full Access). Escalation trigger 1 did not fire (no private seam, no
  edit of `Infrastructure/ExternalAccess/**`).
- **Firm**: external = the contact's CONFERRING memberships. A named `firmId` must be one of them (403 `firm_forbidden`); none named and one firm -> it;
  several -> 400 `firm_required` (the POML's proposal, the server never picks); none -> 403 `firm_forbidden`; unreadable -> 503 `firm_unverifiable`.
  Internal = the query `firmId`, an active organization, AppendTo as the caller.
- **firmId is a query parameter** on both POSTs (the Finance doctrine: the filter and the handler read the same source).
- **Literal route chains**: the three POSTs per channel are written out (not a loop) because the route-authorization scanner reads each chain.
- **Body cap**: `RequestSizeLimit` 101 MB (the option ceiling + envelope); the configured limit is the handler's 413.
- **No interface** (ADR-010): the service is concrete; ADR-010 ceiling delta 0.

## 4. §11 component justification

| Component | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceSubmissionService` | `RateCardImportService` (same upload/document/compensate shape, other table); external document upload | Neither creates a submission or enqueues a job; FR-17 needs one owner of the submission create (ADR-002) | No invoice can be received; 049 has nothing to process |
| Internal + external routes | Finance and external upload routes | They act on other resources | The SPA (051) and the internal channel have no endpoint |
| `ExternalInvoiceSubmissionAuthorizationFilter` | `ContactGrantorAuthorizationFilter` (same principal + membership reads) | It decides grant eligibility, not a firm-scoped submit/read | NFR-10 would be enforced in handlers the route guard cannot see |
| `finance.submit_invoice` | `finance.attach_invoice` | Lacks Read; reusing it misnames the act in deny logs | The internal submit has no least-privilege key |
| Two entity-set constants | Same values elsewhere (`DecisionActionExecutors`, `SecureChildLineage`) | Finance filter constants are per-route by design | The routes would pluralize or inline set names |
| `SpeFileStore.GetDriveItemByPathAsync` / `DriveItemOperations.GetDriveItemByPathAsync` (fix round 2) | `GetDriveItemAsync` (by item id), `ListChildrenAsync` | The by-id lookup builds `items/{id}`, which cannot carry a path; listing the root is unbounded. Same ownership gate and 404 handling, copied | An upload whose reply is lost, or a retried PUT answered 409, leaves a file in the matter's container with no row and no way to find it |

Placement: BFF (the only backend; the write path and auth seams live here). No package, no background service, no new config key.

## 5. Evidence

### Seeding proofs (acceptance criterion 6)
- Firm check removed (`ExternalInvoiceSubmissionAuthorizationFilter` submit: `firms.Contains(firmId)` short-circuited): 1 failed / 30 passed —
  `External_caller_naming_another_firm_is_403_firm_forbidden_and_nothing_is_written`. Read-one firm check removed: 1 failed —
  `External_get_of_another_firms_submission_is_the_same_404_as_a_missing_one`.
- Core-record check removed (submit `RightsOn(...).HasFlag(Read|Create)`): 3 failed / 28 passed — `External_caller_cannot_submit_on_a_record_outside_their_set`
  and both cases of `External_caller_without_collaborate_on_the_record_is_403_record_forbidden_and_nothing_is_written`.
- Restored; the file matches the commit (`git status` clean).

### Tests
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionServiceTests.cs`: 36 passed. The write doubles commit first, then
  fail or honour the token, so a "failure" can leave a committed write whose reply never arrived.
- `tests/integration/contract/Api/Finance/InvoiceSubmissionEndpointsContractTests.cs`: 37 passed (real mappers, filters, kill switch and service).
  The membership double answers only for the test contact (fix round 2, F3).
- Fix round 1 before/after: the round-0 service against the new tests = 3 failed / 58 passed; the fixed service = 61 passed.
- Fix round 2 before/after: the round-1 service, filter and routes against the new tests = 12 failed / 61 passed; fixed = 73 passed. The
  contact-blind membership double against `External_firms_are_those_of_the_callers_own_contact` = 1 failed / 72 passed. The round-1
  `A_cancelled_request_is_compensated_and_rethrown` is replaced: a mid-write cancellation no longer compensates, it completes.
- Fix round 2 runs: InvoiceSubmission + OperationAccessPolicyCompleteness 105 passed; `tests/Spaarke.ArchTests` 1056 passed; BFF build (-m:1) clean.
- Full `Sprk.Bff.Api.Tests` run (stopped after about 74 failures): every failure was `TaskCanceledException` in WebApplicationFactory hosts
  unrelated to this task (Documents, Office, Ai, Rag ...), under machine load from parallel agents. Six of those classes re-run alone: 70 passed.
- `OperationAccessPolicyCompletenessTests`: 32 passed (finance.submit_invoice pinned in three theories).
- `tests/Spaarke.ArchTests`: 1052 passed (RouteAuthorizationGuard 126 -> 128 files, credited form; RecordOwnerAssignmentCensus Routed entry +
  PersonBearingWriter; SpeWriteSinkContainerProvenance two ServerDerivedRecord sites; SpeUploadPathIsFlat; ADR010 unchanged).
- Beyond the stated contract (one line each): the two-firm and no-firm cases (escalation trigger 2's proposal); unreadable memberships 503 (fault
  is never "no firm"); a client disconnect mid-write completes the submission (acceptance criterion 7: no orphan, no Received row without a job).

### Publish size
Framework-dependent linux-x64 `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal`: fresh master `709101aa6` 192 files 37.149 MB;
task base `9db5d265e` 192 files 37.290 MB; branch 192 files 37.321 MB. Delta +0.03 MB vs base (+0.17 MB vs master, the rest is other branch content).
No `.csproj` change, so no new package and no new CVE.

### Conflict check
Open PRs overlapping: #2019 (ontology-104) on `EndpointMappingExtensions.cs` and `RouteAuthorizationGuardTests.Ledger.cs` (the
`ExpectedEndpointFileCount` line will need the sum on merge); #2017 (uac-1363) on `SpeWriteSinkContainerProvenanceGuardTests.cs` (append-only list).

### Adversarial review, second pass (fix round 2)
No authorization defect. Fixed:
- F2: orphans after a client disconnect or a lost reply (document row with no file; Received submission with no job; uploaded file with no
  id). Writes now ignore the caller's token; compensation finds each write without its reply (section 1). This also closes the first pass's K4
  for the disconnect case.
- F2: the 409 branch left the file its own retried PUT committed. Now deleted by path, then refused.
- F1: firm and submission read faults escaped as 500. Now 503.
- F3: the membership double ignored the contact id.

Known limits recorded:
- K2 (residual race): a slow write whose reply times out (an HttpClient or SDK timeout, not the caller) can still commit after compensation has
  looked for it: a submission create landing after its delete leaves a Received row with no job, a document create after the sweep leaves a row,
  an upload after the path lookup leaves a file. The window is the gap between the client-side timeout and the server's commit.
- K1, K3, K4 of the second review: their text did not reach this task (the fix brief named them without content); to be added from the review report.

### Adversarial review, first pass
- F2 fixed: a failed document update orphaned the document row (the id was assigned only after create + update both returned).
- F2 fixed: a submission create whose reply was lost (timeout after commit) left a Received row with no job.
- K2 closed by tests: kill-switch ordering after authorization; internal GET 200.
- K3: the list maps a row whose core lookups name two records to `coreRecord: null`. Reachable only if a writer bypasses FR-05 (one core
  lookup per row); this service always writes exactly one.
- K4: Service Bus can accept the message while the enqueue call reports failure; compensation then deletes the row and the job later finds no
  submission. 049 must treat a missing submission as done (stated in section 2). Since fix round 2 a caller disconnect no longer triggers it;
  a send that times out after the broker accepted it still can.

## 6. Known limits
- Until task 049 lands, `InvoiceSubmissionProcessing` has no handler; the flag (`Ebilling:Enabled`, default off) keeps the routes closed.
- The PDF check is the declared type plus the `%PDF-` header; a malformed PDF body is 049's to reject.
- The external read and list are scoped to the caller's CONFERRING firms; a contact whose membership lapsed no longer sees their firm's submissions.
- The list returns at most 50 rows across all root types and has no paging.

## Second adversarial review: known limits (added by the main session, 2026-10-16)

- K1: membership-unreadable -> 503 is tested on submit only, not on the external read-one and list; the code path is shared and correct.
- K3: the whole file is buffered in memory (copy plus ToArray, about twice the file size per request), and the internal POST has no rate limit. Revisit with NFR-02 load measurement (task 112/160).
- K4: the file-name stem is cut at 100 characters, which can split a UTF-16 surrogate pair; the GUID suffix still makes the name unique and safe.
