# Task 063 — External upload is queued for indexing and document profile

## Owner decision (2026-10-13)

> "yes files should be indexed and document profile"

Files uploaded by external users through the SPA are indexed in AI Search AND get a document profile, both app-only.

## Rigor / tier

- Rigor: FULL (POML-authored; tags bff-api, ai, auth, testing).
- Model tier: opus @ high. Step mode: directional.

## Step 1 — how the profile job is queued server-side (trace)

The document profile is queued as an ADR-004 job, not run inline:

1. A producer submits a `JobContract` with `JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName` ("AppOnlyDocumentAnalysis"),
   `SubjectId = documentId`, `IdempotencyKey = AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId)`
   and a payload `{ DocumentId, Source, EnqueuedAt }` through `JobSubmissionService.SubmitJobAsync` (Service Bus `sdap-jobs`).
   Producers that do exactly this today: `UploadFinalizationWorker.EnqueueAiAnalysisForAttachmentAsync` (:1313),
   `IncomingCommunicationProcessor.EnqueueDocumentAnalysisAsync` (:1361), `DocumentOperationsEndpoints.TriggerDocumentAnalysis` (:584),
   `OfficeProfileQueue.QueueAsync` (per-request key variant).
2. `AppOnlyDocumentAnalysisJobHandler` (registered unconditionally, `JobProcessingModule.cs:38`) checks the idempotency key,
   takes a processing lock and calls `IAppOnlyAnalysisService.AnalyzeDocumentAsync(documentId, playbookName ?? "Document Profile")`.
3. `AppOnlyAnalysisService.AnalyzeDocumentAsync` (:232) reads the row, checks the pointer container
   (`IsDocumentPointerContainerAllowedAsync`, fail closed), downloads the file with `ISpeFileOperations.DownloadFileAsync`
   (app-only, MI) and, since #919 Fix 2, routes the "Document Profile" consumer onto the direct-Action spine (ACT-011 +
   `DocumentProfileOutputMapper`), stamping `sprk_filesummarystatus` Pending -> Completed/Failed.

**No user token anywhere on this path.** The escalation trigger (profile needs OBO) does NOT fire.

## Identity of the uploaded file (auth.md:78 vs the interface comment)

`UploadDocumentForParent` writes the bytes with `ISpeFileOperations.UploadSmallAsync` — the app-only (MI) member. The
external file is therefore MI-written, which is exactly the precondition auth.md:78 and the interface place on
`EnqueueAppOnlyIfApplicableAsync`. auth.md:80 ("MUST NOT wire new post-upload pipelines through
`JobSubmissionService.SubmitJobAsync(RagIndexingJobPayload)` from request-scoped endpoints") is respected: indexing goes
through the helper, never a raw `RagIndexingJobPayload`. The helper's OBO method is not applicable: the external plane has
no OBO token, and the file was not written under one.

`IPostUploadIndexingEnqueuer.cs` remarks on `EnqueueAppOnlyIfApplicableAsync` updated to match auth.md:78 (MI-written
files, including external uploads written app-only by the BFF; since task 171 app-only jobs read user-uploaded files too).

## ADR-013 / bff-hygiene item 3 — Path C (comply)

The endpoint gains dependencies on `IPostUploadIndexingEnqueuer` (Services/Ai) and `AppOnlyDocumentAnalysisJobHandler`
constants (Services/Ai/Jobs). Neither is a forbidden AI-internal type (`ADR013_AiBoundaryTests` forbids `IOpenAiClient`,
`IPlaybookService`); both are the established producer seams already used by CRUD code: `ComposeService` (enqueuer, called
"the always-registered, ADR-013-safe seam" there), `IncomingCommunicationProcessor` / `CommunicationEnrichmentService`
(enqueuer + profile job), `DocumentOperationsEndpoints` (profile job). The endpoint runs no AI; it submits two Service Bus
jobs. Note for the operator: `IRelocatedFileIndexing`'s remarks take the stricter reading (enqueuer = AI-internal); the
repo is not consistent on this point. Not escalated: the POML names the enqueuer call explicitly, and every other
upload producer follows the same shape.

## §11 component justification (new surface inside an existing file)

- Existing: `IPostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync` (indexing) and the `AppOnlyDocumentAnalysis` job (profile).
- Extension: call them from `UploadDocumentForParent`; one private helper `QueueIndexingAndProfileAsync` in the same file,
  no new class, interface, DI registration or package.
- Cost of doing nothing: external uploads are never searchable and never profiled (owner check-2 issue 5).

## Design choices

- Both enqueues run after `CreateDocumentAsync` returns, side by side (`Task.WhenAll`), each in its own try/catch: an
  indexing failure does not stop the profile, and neither changes the 201.
- Not the request token: the bytes and the row are committed, so a client that disconnects must not leave the document
  un-indexed and un-profiled (`OfficeProfileQueue.QueueAsync` reasoning). But not unbounded either (adversarial pass F3):
  the Service Bus client's default retry budget (3 retries, 60 s try-timeout) can hold a send for minutes on a hung
  namespace, the SPA would time out on an upload that DID land, and its retry would meet the route's 409. Both queues
  share one 15 s budget (`PostUploadQueueBudget`); a timeout is logged like any other queue failure.
- The indexing request carries the SAME drive/item pointers that were persisted on the row (one `pointers` local feeds both).
- `ParentEntity: null` — `ParentEntityContext` needs a display name the route does not have, and passing one would be a
  second derivation of the parent; the app-only handler resolves the parent from the document row
  (`DocumentIndexParentResolver`), as for every other app-only producer.
- Tenant id from `TENANT_ID` / `AzureAd:TenantId`, as the other app-only producers. Never the caller's token `tid`
  (a CIAM or workforce home-directory id, not the Spaarke tenant).
- Profile key `ProfileIdempotencyKey(documentId)` (no discriminator): a new document, profiled once.
- Logs carry identifiers only (document id, parent kind/id, exception type); no content (ADR-015).

## Adversarial pass (1 full pass; change adds no authorization decision, so the second `auth` pass is left to the caller)

| # | Class | Finding | Outcome |
|---|---|---|---|
| 1 | F3 | Both sends sequential on `CancellationToken.None` with the SDK default retry budget: a hung namespace holds the 201 for minutes after the row exists; the SPA times out, retries, gets 409 | Fixed: one 15 s budget, both queues side by side; test asserts both calls get a bounded token |
| 2 | K2 (out of scope — reported) | `DocumentIndexParentResolver` excludes `sprk_relatedservicerequest` and has no service-request hop, so an external service-request upload indexes with NO parent fields: the document carries no parent. Cross-record search (`scope=all`) drops rows with no parent (`SemanticSearchEndpoints.cs:592-598`) and `scope=entity` fails closed (`:503-505`), so such a document is NOT found by record-scoped or cross-record search. It is reachable only through the per-document, caller-trimmed surfaces (RAG `/search`, `ReadableRagSearch`, `scope=documentIds`), each gated by Read on the `sprk_document` row. So: no exposure, but a findability gap (filed as an issue by the main session). Pre-existing; affects every service-request document, not only external ones | Reported to the operator; fix belongs in the resolver (`IntermediateTypes`), outside this task |
| 3 | K3 | The index-throws test pins the route's defensive catch (the real enqueuer catches internally) | Comment added on the test |
| 3a | K3 (pass 2) | The bounded-token assertion also passed for `HttpContext.RequestAborted` (cancellable, not yet cancelled) | Fixed: the test doubles read the served request's `RequestAborted` through `IHttpContextAccessor` and assert the queue token differs from it. Bite check: temporarily passed `HttpContext.RequestAborted` to both queues; the theory failed for all 5 parents at `c.Queue != c.Request.Value`. Restored; passes |
| 3b | K4 (pass 2) | A `PostUploadIndexingResult.Failed` (the production enqueuer's failure shape) was discarded: no route log | Fixed: the route logs `[EXT-UPLOAD]` with document id, parent kind and id and the failure reason; still 201. New test `Upload_WhenTheIndexEnqueueReturnsFailed_...`; bite check: with the log disabled it fails; restored; passes |
| 4 | K4 | `PostUploadIndexingEnqueuer.cs` comment listed the old caller set | Updated |
| 5 | K4 | Catches dropped the exception object | Now `LogWarning(ex, …)` (Service Bus exceptions carry no content) |
| 6 | K4 | Notes gave a wrong reason for `ParentEntity: null` | Corrected |

Pre-existing inconsistency noticed (not changed, `.claude/` is outside a sub-agent's write boundary): `.claude/constraints/auth.md:78`
("an app-only job can read user-uploaded files since task 171") and `:79` ("they cannot read user-OBO-uploaded files") contradict
each other; the interface remark follows :78.

## Test scope

`ExternalRecordDocumentRoutesTests` (through the REAL routes): success on every parent type queues one index request
with the row's id/drive/item and one profile job; an index enqueue fault and a profile submit fault are each logged and
still 201; an SPE collision (409), a null upload handle (502) and a document-create fault enqueue nothing. Nothing beyond
the POML's stated contract and edge cases.
