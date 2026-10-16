# Task 044 - Invoice submission operation (FR-17, NFR-10)

Status: completed (fix round 1). Rigor FULL; step mode directional. Built on D-87 (the row is created by the allocator with its number) and D-91 (the firm
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
6. Upload `{client stem}-{submissionId:N}.pdf` (SanitizeFileName, `ConflictBehavior.Fail`).
7. Document: SPA via `ExternalDataService.CreateDocumentAsync` (parent bind, team, creator contact), internal via `IDocumentDataverseService`; then type
   Invoice (100000001) and `application/pdf`.
8. Submission via `CreateSubmissionAsync(submissionId, firm, number, fields)`: name, Received, channel, document, received-on, PO, correlation id,
   submitter contact (SPA), core lookup, the five resolver fields, owner + creator person. Refused -> 503 `ebilling.tracking.unavailable`.
9. Job `InvoiceSubmissionProcessing`, payload `{ "submissionId": ... }`, `SubjectId` = `IdempotencyKey` = submission id.

Any failure after step 6 compensates in reverse order with its own token (submission row, document row, file); a failed compensation is logged and
never masks the original. Cancellation compensates and rethrows. No path creates `sprk_invoice`. The document id is held as soon as the row exists
(create, then a separate typing update), so a failed update removes the row. The submission is treated as possibly existing from the moment the
allocator is asked (a lost reply may have committed it) and is deleted by its id; a not-found on that delete is ignored. An allocator refusal
creates nothing and deletes nothing.

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
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionServiceTests.cs`: 28 passed (25 + 3 fix-round tests).
- `tests/integration/contract/Api/Finance/InvoiceSubmissionEndpointsContractTests.cs`: 33 passed (real mappers, filters, kill switch and service;
  + kill switch after authorization, + internal GET 200).
- Fix-round before/after: the round-0 service against the new tests = 3 failed / 58 passed; the fixed service = 61 passed.
- Full `Sprk.Bff.Api.Tests` run (stopped after about 74 failures): every failure was `TaskCanceledException` in WebApplicationFactory hosts
  unrelated to this task (Documents, Office, Ai, Rag ...), under machine load from parallel agents. Six of those classes re-run alone: 70 passed.
- `OperationAccessPolicyCompletenessTests`: 32 passed (finance.submit_invoice pinned in three theories).
- `tests/Spaarke.ArchTests`: 1052 passed (RouteAuthorizationGuard 126 -> 128 files, credited form; RecordOwnerAssignmentCensus Routed entry +
  PersonBearingWriter; SpeWriteSinkContainerProvenance two ServerDerivedRecord sites; SpeUploadPathIsFlat; ADR010 unchanged).
- Beyond the stated contract (one line each): the two-firm and no-firm cases (escalation trigger 2's proposal); unreadable memberships 503 (fault
  is never "no firm"); cancellation compensation (acceptance criterion 7 under a client disconnect).

### Publish size
Framework-dependent linux-x64 `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal`: fresh master `709101aa6` 192 files 37.149 MB;
task base `9db5d265e` 192 files 37.290 MB; branch 192 files 37.321 MB. Delta +0.03 MB vs base (+0.17 MB vs master, the rest is other branch content).
No `.csproj` change, so no new package and no new CVE.

### Conflict check
Open PRs overlapping: #2019 (ontology-104) on `EndpointMappingExtensions.cs` and `RouteAuthorizationGuardTests.Ledger.cs` (the
`ExpectedEndpointFileCount` line will need the sum on merge); #2017 (uac-1363) on `SpeWriteSinkContainerProvenanceGuardTests.cs` (append-only list).

### Adversarial review (one pass)
- F2 fixed: a failed document update orphaned the document row (the id was assigned only after create + update both returned).
- F2 fixed: a submission create whose reply was lost (timeout after commit) left a Received row with no job.
- K2 closed by tests: kill-switch ordering after authorization; internal GET 200.
- K3: the list maps a row whose core lookups name two records to `coreRecord: null`. Reachable only if a writer bypasses FR-05 (one core
  lookup per row); this service always writes exactly one.
- K4: Service Bus can accept the message while the enqueue call reports failure; compensation then deletes the row and the job later finds no
  submission. 049 must treat a missing submission as done (stated in section 2).

## 6. Known limits
- Until task 049 lands, `InvoiceSubmissionProcessing` has no handler; the flag (`Ebilling:Enabled`, default off) keeps the routes closed.
- The PDF check is the declared type plus the `%PDF-` header; a malformed PDF body is 049's to reject.
- The external read and list are scoped to the caller's CONFERRING firms; a contact whose membership lapsed no longer sees their firm's submissions.
- The list returns at most 50 rows across all root types and has no paging.
