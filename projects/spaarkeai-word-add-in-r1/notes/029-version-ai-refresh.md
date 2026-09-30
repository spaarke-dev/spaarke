# Task 029: re-profile and re-index after a version save

> Trace pass: base `0a8730fd2` (escalated on trigger 2). Implementation pass: base `05c19a784` (owner decisions
> 2026-09-15). Rigor FULL, opus @ high, directional. Symbols are named by symbol; line numbers drift.

## 0. Status: IMPLEMENTED (owner decisions 2026-09-15)

| Trigger | Fired? | Why |
|---|---|---|
| 1. The version key would change another save path's key, retry or skip | **No** | Only `OfficeService.CompleteVersionSaveAsync` passes `isVersionSave: true`. Every other producer leaves the new payload field null, and it is then **omitted from the JSON**, so the payload and every key derived from it are unchanged. Proven by test (§6). The save key (`GenerateIdempotencyKey`, `ResolveIdempotencyKey`, `CheckForExistingJobAsync`, the pane's key) is untouched; that is task 047. |
| 2. The indexing pipeline cannot replace a document's chunks | Fired in the trace pass → **decided: option B** | Upload first, then trim this file's chunks beyond the new chunk count, in the same index the batch went to. Only the version re-index asks for it. One new `IRagService` method (§5.3, placement §5.6). |
| 3. The profile handler's skip logic is shared outside document analysis | **No** | No handler change. `AppOnlyDocumentAnalysisJobHandler` and `RagIndexingJobHandler` already skip on the producer's `job.IdempotencyKey`. Only the producers' keys change, and only for a version. |

Outcome: a version save queues **one** re-profile and **one** re-index of the same `sprk_document`. The index then
holds exactly the new version's chunks, in the index the document routes to. A Service Bus redelivery or retry of
the same save runs nothing extra. **Every new save refreshes, even when its bytes repeat an earlier version
(B, A, B)**, because the discriminator is the save, not the content. A first save and every Email, Attachment,
Compose or manual index path behave exactly as before.

## 1. Trace: finalization → profile → index (trace pass, still accurate)

### 1.1 The two saves reach finalization the same way

- **First save (create)**: `OfficeService.SaveAsync` → `CreateDocumentWithSpePointersAsync` creates the row →
  `OfficeJobQueue.QueueUploadFinalizationAsync(…, documentId: <new id>)`.
- **Version save**: `OfficeService.CompleteVersionSaveAsync` → `OfficeStorageUploader.WriteNewVersionAsync` (OBO PUT by
  item id, the SAME drive item) → `QueueUploadFinalizationAsync(…, documentId: target.DocumentId)`, i.e. the EXISTING
  id, driveId and itemId.
- Before this task, `UploadFinalizationPayload` carried nothing version-specific.

### 1.2 The keys and their skip checks (before this task)

| Layer | Key (producer) | Skip check | Marked | Version save before 029 |
|---|---|---|---|---|
| Finalization worker | `message.IdempotencyKey` = the save's authoritative key | `UploadFinalizationWorker.IsAlreadyProcessedAsync` (tenant cache `office-upload-processed`) | 7 days | Runs |
| Profile | `analysis-{documentId}-documentprofile` (`UploadFinalizationWorker.QueueNextStageAsync`) | `AppOnlyDocumentAnalysisJobHandler` → `IIdempotencyService.IsEventProcessedAsync(job.IdempotencyKey)` | on success, 7 days | **Skipped** |
| Index | `rag-index-{driveId}-{itemId}` (`PostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync`) | `RagIndexingJobHandler` → `IsEventProcessedAsync(job.IdempotencyKey)` | on success, 7 days | **Skipped** |

A second reason the per-document and per-item keys lost versions: `JobSubmissionService` sets the Service Bus
`MessageId` to the idempotency key. With broker duplicate detection enabled, a second save's profile or index
message inside the detection window is dropped at the broker before any handler runs.

Other producers of the same two keys, all unchanged by this task: `analysis-{id}-documentprofile` is also produced by
`UploadFinalizationWorker.EnqueueAiAnalysisForAttachmentAsync`, `DocumentOperationsEndpoints`,
`IncomingCommunicationProcessor` and `CommunicationService`. `rag-index-{driveId}-{itemId}` is also produced by
`PostUploadIndexingEnqueuer` (Office, Email-to-Document, `AnalysisResultPersistence`, `CommunicationEnrichmentService`),
`DeliverToIndexNodeExecutor`, `BulkRagIndexingJobHandler` and `RagEndpoints`.

### 1.3 Profile write semantics: overwrite, no own skip

`AppOnlyAnalysisService.AnalyzeDocumentAsync` has no "already profiled" check; a run overwrites the profile fields.
A per-version key is sufficient for the profile half. Whether that run succeeds is the existing profile pipeline's
business (`DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` Part 4); this task only makes sure it is queued and not
skipped.

### 1.4 Index write semantics before this task: overwrite by id, never delete

`RagIndexingJobHandler` → `FileIndexingService.IndexFileAppOnlyAsync` → `IndexTextInternalAsync` →
`RagService.IndexDocumentsBatchAsync` (`MergeOrUploadDocumentsAsync`). Chunk ids are `{speFileId}_{index}`, and a
version keeps the item id, so chunks `0..N-1` are replaced in place. Nothing deleted `N..M-1` when the new version was
shorter. Those orphans are user-visible: `VisualizationService.GetSourceDocumentAsync` (Find) reads the source vector
from one unordered chunk filtered by `documentId`. The only delete-by-document primitive,
`DeleteBySourceDocumentAsync`, targets the tenant-default index, not the routed index this path writes to.

## 2. Why a key change alone was not enough (trace pass)

Per-version keys without a replace would re-index a shrunk version into chunks `0..N-1` and leave a stale tail, which
fails AC4. That is why the trace pass stopped on trigger 2 rather than shipping half the fix.

## 3. Pre-existing and NOT fixed here: the same orphan tail on Compose and manual re-index

Compose save-back (`ComposeService` → `PostUploadIndexingEnqueuer.EnqueueIfApplicableAsync`, OBO) and
`POST /api/ai/rag/send-to-index` both re-index onto the same item id with no trim. A Compose document that shrinks
already leaves stale tail chunks. The owner decision keeps those paths byte-for-byte, so this remains open.
**Recommend a follow-up task for option C** (make the trim the default in `IndexTextInternalAsync` for every re-index
path). Not filed from this sub-agent.

## 4. Escalation on trigger 2: DECIDED 2026-09-15

The trace pass offered A (delete-then-upload, opt-in), **B (upload-then-trim, opt-in)**, C (A or B for every path),
D (accept orphans) and E (profile only); see `a24f99c9d` for the full table. Owner chose **B**, **no backfill** of
stale dev chunks, and **close the revert case in this task**. That last decision rules out the content-hash
discriminator that §5 of the trace pass proposed.

The owner also asked to check first whether the save itself drops a revert. The main session checked, and it does: the
version save key hashes content, and `CheckForExistingJobAsync` has no time window, so a third save of B after A is
answered Duplicate and never written. That is now **task 047** (main session). This task does not touch the save key
and is designed to refresh for every save 047 lets through (§5.5).

## 5. As-built design

### 5.1 Discriminator: the save's ProcessingJob id

`UploadFinalizationPayload.VersionSaveJobId` (`Guid?`, `[JsonIgnore(WhenWritingNull)]`) is stamped with the save's job
id by `OfficeJobQueue.QueueUploadFinalizationAsync(…, isVersionSave: true, …)`. Only `CompleteVersionSaveAsync` passes
`true`; the create path passes `false`.

| Candidate | Verdict |
|---|---|
| Content hash (trace-pass plan) | **Rejected.** Identical bytes give identical keys, so B, A, B skips the third refresh inside the 7-day marks. The owner closed that case. |
| SPE version id / eTag from the write | Workable, but `OfficeStorageUploader.VersionWriteResult` returns neither today. Using it would change the facade result and add a null-eTag fallback, and it would add nothing: one save = one job = one version write. |
| **Save's ProcessingJob id** | **Chosen.** Always present. A Service Bus redelivery repeats it, and so does the Office worker's own retry (`RetryJobAsync` copies the message), so both still skip. A client retry of the same save is answered Duplicate by the save spine with the SAME job, so nothing is re-queued. Every new save that is written has a new job, and after task 047 that includes reverts. |
| `message.JobId` without a payload field | Rejected. The first save's message also has a job id, and it must stay unversioned. An explicit version-only field is the marker and the discriminator in one. |

Deviation from the POML constraint "deterministic for identical content (content hash or SPE version id), never …
random": the job id is a GUID minted once per save and deterministic across every retry of that save. The owner's
2026-09-15 decision explicitly allows "the save job's id" and requires that identical content from two saves NOT
collapse, which a content-deterministic key cannot satisfy.

### 5.2 Keys after this task

| Save | Profile key | Index key | Index payload |
|---|---|---|---|
| Version save (job J) | `analysis-{documentId}-documentprofile-version-{J:N}` | `rag-index-{driveId}-{itemId}-version-{J:N}` | `ReplaceStaleChunks: true` |
| First save, Email, Attachment, attachment children, every other producer | `analysis-{documentId}-documentprofile` (unchanged) | `rag-index-{driveId}-{itemId}` (unchanged) | property omitted (unchanged JSON) |

Plumbing: `UploadFinalizationWorker.QueueNextStageAsync` builds the profile key and passes
`VersionDiscriminator: J:N` on the new optional trailing `PostUploadIndexingRequest.VersionDiscriminator` (default
null, so every existing call site compiles unchanged). `PostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync`
appends the suffix and sets `RagIndexingJobPayload.ReplaceStaleChunks` (`[JsonIgnore(WhenWritingDefault)]`). The
OBO path (`EnqueueIfApplicableAsync`, used by Compose and the wizards) does not read the field. Attachment children
pass `versionSaveJobId: null` explicitly.

The version index key exceeds 128 characters for real drive and item ids; `JobSubmissionService.ToSafeMessageId`
already SHA-256-hashes such keys for the Service Bus `MessageId`, which is deterministic, so broker dedup still
holds. Redis keys have no such limit.

### 5.3 Replace = upload, then trim (option B)

- `RagIndexingJobHandler` maps `payload.ReplaceStaleChunks` onto the new `FileIndexRequest.ReplaceStaleChunks`. The
  allow-list fallback (`request with { SearchIndexName = null }`) keeps the flag, so the trim follows the write to
  the default index when that fallback fires.
- `FileIndexingService.IndexTextInternalAsync` is the one convergence point. **Only after every new chunk is
  confirmed** (`allSucceeded`) and only when the flag is set, it calls
  `IRagService.DeleteChunksBeyondCountAsync(tenantId, speFileId, chunks.Count, searchIndexName)`, passing the same
  `searchIndexName` the batch used. `IndexContentAsync` passes `false`; the OBO `IndexFileAsync` forwards the request
  flag, which no caller sets.
- `RagService.DeleteChunksBeyondCountAsync` is the **new facade method**:
  - Routing is identical to `IndexDocumentsBatchAsync`: a non-empty name goes through the allow-listed 3-argument
    `GetSearchClientAsync(tenant, index)`; null goes to the tenant default. This makes it **the per-record index,
    not the tenant default**, whenever the document routes elsewhere.
  - Filter: `tenantId eq '{t}' and speFileId eq '{s}' and chunkIndex ge {N}` (all three fields are `filterable` in
    `infrastructure/ai-search/spaarke-files-index.json`). It pages with `Size=1000` and `Skip`, collects every id
    first, then deletes in batches of 1000.
  - It deletes **only ids of this pipeline's shape**, `{speFileId}_{chunkIndex}`. `RagIndexingPipeline`
    (`{documentId}_{suffix}_{i}`) and the reference indexer (`{sourceId}_ref_{i}`) can store chunks carrying the same
    `speFileId`; those are never touched.
  - It refuses `keepChunkCount < 1` (`ArgumentOutOfRangeException`), so it can never empty a file. With
    upload-before-trim, **the document is never without chunks**.
  - It throws `InvalidOperationException` if any leftover fails to delete.
- **Trim failure** is caught in `IndexTextInternalAsync` and returned as `Success=false` with a message naming only
  the exception *type*. That keeps content out of the logs (ADR-015) and avoids the handler's permanent-failure words.
  The job therefore returns **Failure (retryable)**, the key is not marked, and Service Bus retries. The new chunks
  are already in place throughout; only the leftover tail survives until the retry. I chose this over "log and
  succeed", which would have left orphans silently and failed AC4.
- `NullRagService` throws `FeatureDisabledException` like its siblings.

### 5.4 Unchanged paths (trigger 1 evidence)

- Create save: `isVersionSave: false`, so the payload has no `VersionSaveJobId` and the keys are identical.
  Pinned by the data-mutation test through the real route, and by the seam test's pinned strings.
- Email save: same; pinned by both test files.
- Attachment children, `DocumentOperationsEndpoints`, `IncomingCommunicationProcessor`, `CommunicationService`,
  `CommunicationEnrichmentService`, `AnalysisResultPersistence`, `DeliverToIndexNodeExecutor`,
  `BulkRagIndexingJobHandler`, `RagEndpoints`, Compose: not edited, or edited only to pass `null` explicitly; the
  record default is `null`.
- The save spine: `GenerateIdempotencyKey`, `ResolveIdempotencyKey`, `CheckForExistingJobAsync` and the pane key are
  not touched (task 047). No `src/client/office-addins/**` change (task 036).

### 5.5 Dependency on task 047

Until 047 lands, a version save whose bytes repeat an earlier save of the same document (B, A, B) is answered
Duplicate by the save spine and never written. For that save nothing reaches this pipeline, and SPE, the profile and
the index all still describe A: the edit is lost, but nothing is stale. Once 047 makes every genuinely new save
create its own ProcessingJob, that job's id becomes `VersionSaveJobId` and this pipeline refreshes for it without any
further change. The seam test `RevertedContent_BThenAThenB_RefreshesForEveryNewVersion` drives finalization with the
job 047 will create.

### 5.6 Placement statement (CLAUDE.md §10 / §11) for `IRagService.DeleteChunksBeyondCountAsync`

- **Placement:** in the BFF, on the existing `IRagService` facade in `Services/Ai/`, beside
  `DeleteBySourceDocumentAsync`. Its only caller is `FileIndexingService` (also `Services/Ai`). No CRUD→AI dependency
  is added: the Office worker still only enqueues jobs, and nothing new is injected anywhere. It is not out-of-band
  work (ADR-001) and it meets none of the refined ADR-013 extraction criteria.
- **Existing:** `DeleteBySourceDocumentAsync(documentId, tenantId)` (tenant-default index only; deletes ALL of a
  document's chunks), `DeleteIndexedDocumentAsync(indexName, …)` (knowledge/discovery only, admin), and
  `DeleteDocumentAsync(chunkId, tenantId)` (one id, default index). Found by grep over `IRagService`.
- **Extension:** extending `DeleteBySourceDocumentAsync` in place was rejected. Changing its routing would change
  the admin endpoint's behaviour, and it deletes everything, which option B exists to avoid. An overload with a
  `keepChunkCount` would still be a new member, so the named method is clearer than an overload with different
  semantics.
- **Cost of doing nothing:** a re-saved document that got shorter keeps its previous version's tail chunks, with the
  old text and the old document vector, in the index Find reads. `VisualizationService.GetSourceDocumentAsync` can
  pick one and rank "related documents" by content that no longer exists.
- **No new DI registration** (ADR-010), no new package, no new endpoint, no new job type.

## 6. Tests (ADR-038 KEEP paths; new files only; no existing test modified)

| File | KEEP category | Proves |
|---|---|---|
| `tests/integration/seam/Office/VersionSaveAiRefreshSeamTests.cs` | vertical-slice seam | Production `OfficeJobQueue` → `UploadFinalizationWorker` → `PostUploadIndexingEnqueuer` → `RagIndexingJobHandler` → `FileIndexingService`, plus `AppOnlyDocumentAnalysisJobHandler`. Doubled only at Service Bus, the Redis stores, SPE bytes, extraction/chunking, the AI call and the AI Search index. (a) A differing version queues one profile + one index job with version keys, runs one re-profile and one re-index, and leaves exactly the new chunks in the routed index, with nothing in the default index. (b) A redelivery to the same worker, a re-run on a fresh-cache worker (same keys) and a plain redelivery of the jobs run nothing. (c) B, A, B refreshes three times with unique keys, and the index matches each version after each save. (d) A first save and an Email save keep the pinned master keys, have no `VersionSaveJobId` or `ReplaceStaleChunks` in their JSON, and never trim. (e) A failed trim returns Failed (not Poisoned) with the new chunks already in place and the key unmarked; the retry completes the replace. |
| `tests/unit/domain/Ai/RagServiceChunkTrimTests.cs` | domain | The real `RagService.DeleteChunksBeyondCountAsync` against a doubled `SearchClient`: it targets the routed index (the 2-argument default overload is never called), falls back to the default index when no name is given, uses the exact tenant+file+`chunkIndex ge N` filter, deletes only `{speFileId}_{i}` ids (a same-file chunk under another id scheme survives), issues no delete when there is no tail, refuses `keepChunkCount < 1` without touching the index, and throws when a delete is rejected. |
| `tests/integration/data-mutation/OfficeVersionSave/OfficeVersionSaveAiRefreshPayloadTests.cs` | data-mutation | Through the real `POST /api/office/save` over `OfficeVersionSaveWorld`: a version save stamps its own response `jobId` as `VersionSaveJobId`; two successive versions stamp two distinct ids that match their responses; a first document save and an Email save carry no such property. |

**Mutation checks (tests fail without the fix):**
- M1: the worker ignores the discriminator (pre-fix keys). 4 of 5 seam tests fail (refresh, redelivery, revert,
  trim-retry). The first-save/Email seam test and all 4 payload tests pass, as they should: they pin today's keys
  and the save side.
- M2: keys intact, trim disabled. The same 4 seam tests fail on the leftover tail; the first-save/Email test passes.
- Both mutations were reverted; the final build and test runs are on the restored code.

## 7. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| New tests (3 classes) | 14 passed / 0 failed |
| ArchTests | 191 / 191 passed |
| Full `Sprk.Bff.Api.Tests`, before (`05c19a784`) | 12,273 passed / 0 failed / 56 skipped (12,329) |
| Full `Sprk.Bff.Api.Tests`, after | **12,287 passed / 0 failed / 56 skipped (12,343)** = baseline + the 14 new tests. Run once, in 4 disjoint foreground chunks (outside Services+Api 3,930 · Services.Ai 4,677 · Services non-Ai 2,246 · Api 1,490) because the single run exceeds the tool's 10-minute limit. The chunk totals sum exactly to 12,343. |
| Targeted areas (filter `Office \| Rag \| FileIndexing \| PostUploadIndexing \| Analysis \| Documents \| Finalization`) | 1,511 passed / 0 failed / 20 skipped (1,531) |
| `dotnet list package --vulnerable --include-transitive` (BFF) | "no vulnerable packages" |

**Publish size** (root CLAUDE.md §10). `dotnet publish -c Release -o C:\t\…`, zipped with PowerShell
`Compress-Archive -CompressionLevel Optimal` (the method `scripts/Deploy-BffApi.ps1` uses), PDBs included:

| Build | Commit | Zip bytes |
|---|---|---|
| Fresh master | `e0a6f87c4` | 47,555,948 |
| Branch before task 029 | `05c19a784` | 47,609,367 |
| Branch with task 029 | working tree on `05c19a784` | 47,612,370 |
| **Task 029 contribution** | | **+3,003 B (+0.003 MB)** |
| Branch vs fresh master (all unmerged project work) | | +56,422 B (+0.054 MB) |

Far below the +5 MB justification threshold and the 60 MB ceiling. Master re-measured at 31 B less than the
recorded 47,555,979 at the same commit (zip timestamps).

## 8. Step 9.5: code review and ADR check (self-run, foreground, over the full diff)

**Code review.** Coverage-first; severity and confidence are noted. No Critical or Warning findings needed a fix.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| CR-1 | Info (pre-existing, high confidence) | `RagIndexingJobHandler`'s `catch (SdapProblemException) when (ex.Code == "INDEX_NOT_ALLOWED")` fallback is **unreachable**: `FileIndexingService.IndexFileAppOnlyAsync` catches every exception and returns `Success=false`. So a disallowed routed index retries as transient and never falls back to the default index. | Not introduced or changed by 029; the trim inherits the same routing either way. Recommend a follow-up. |
| CR-2 | Low | `FileIndexRequest.ReplaceStaleChunks` is also forwarded by the OBO `IndexFileAsync`. No OBO caller sets it. | Documented on the property; no behaviour change. |
| CR-3 | Info | `IRagService` gained a member. Implementers are `RagService`, `NullRagService` and the new seam double; Moq mocks are unaffected. | Build green. |
| CR-4 | Info | The seam test's extractor lambda does not dispose its `StreamReader`. The underlying stream is disposed by the code under test. | Test-only; left as is. |
| CR-5 | Low | The trim needs `speFileId` and `chunkIndex` to be filterable in every allow-listed index. | Residual 5; fails safe (retry, new chunks kept). |

Security: the delete filter is tenant-scoped, with escaped string values and an integer bound. There is no auth-surface change and no secrets. Performance: one search plus one delete batch, and only on a version re-index.

**ADR check.**

| ADR | Result |
|---|---|
| ADR-001 | ✅ No new endpoint, Function or hosted service. |
| ADR-004 | ✅ Job Contract unchanged. Keys are deterministic per save (every retry or redelivery of a save reproduces them). Handlers are idempotent. The >128-character version index key is SHA-256-hashed for `MessageId` by `JobSubmissionService`. The ADR's own example key, `doc-{docId}-v{rowVersion}`, is per-version. |
| ADR-007 | ✅ No SPE access change. |
| ADR-008 | ✅ Not applicable (no endpoint). |
| ADR-010 | ✅ No DI registration. |
| ADR-013 | ✅ No CRUD→AI injection. The new member is on the AI facade in `Services/Ai` and is called only from `Services/Ai`. |
| ADR-014 / ADR-016 | ✅ Tenant-scoped delete filter. |
| ADR-015 | ✅ Logs carry ids, counts and exception type names only. |
| ADR-017 | ✅ Job status handling unchanged. |
| ADR-032 | ✅ `NullRagService` follows P3 (`FeatureDisabledException`); no new interface. |
| ADR-038 | ✅ New tests only, at KEEP paths. No `Mock<HttpMessageHandler>`, DI-registration or ctor-null tests. `KeepCountBelowOne_Refuses…` guards a business invariant ("never without chunks"), not a constructor null check. |

**Challenge path: none needed.** The only tension is between the POML constraint "never a … random value" and the owner's revert decision. It was settled by the owner on 2026-09-15 and is recorded in §5.1; no ADR is involved.

## 9. Residuals and what remains unverified

1. **Task 047 dependency (§5.5).** Revert saves are dropped by the save spine until 047 lands; this task refreshes for
   them afterwards.
2. **Out-of-order version jobs.** An index or profile job reads the item's CURRENT version at run time, so two saves
   in quick succession normally converge on the latest bytes. There is a narrow race: job N downloads before save N+1
   writes and finishes after job N+1, and the index then reflects N until the next save. The same race exists today
   for any concurrent re-index of one item; it is not introduced here.
3. **Compose and manual Run Index orphan tail (§3).** Pre-existing; option C follow-up recommended.
4. **The playbook Index node** (`DeliverToIndexNodeExecutor`) and **`AnalysisResultPersistence`** (called only from
   the user-initiated `AnalysisOrchestrationService`, not from the app-only profile job) still enqueue the per-item
   key with no trim. If one fires after a version, it is either skipped (within 7 days of the item's first index) or
   re-writes the same current `N` chunks: harmless, but not "exactly one".
5. **Other allow-listed indexes must share the canonical schema** (`speFileId`, `chunkIndex` filterable). This holds
   for indexes deployed through `Deploy-AllIndexes.ps1`. If a routed index lacked it, the trim query would fail, the
   job would retry and then poison, the current chunks would stay, and the tail would remain (logged).
6. **Not exercised live:** Service Bus delivery and dedup of the >128-character hashed `MessageId`; Azure AI Search's
   response to the trim filter and paging on the real service; and whether the app-only index and profile jobs can
   read a version written OBO (the writer-identity question open since 023). No dev environment was used.
7. **No backfill** of stale chunks already in dev (owner decision).
8. **Insights ingest** (`insights-ingest-{documentId}-{matterId}`) is not versioned; it is default-off and out of scope.

## 10. Deviations

- Discriminator is the save's job id, not a content hash or SPE version id (§5.1; owner-sanctioned, and forced by
  the revert decision).
- A trim failure fails the index job (retryable) rather than logging and succeeding (§5.3), so AC4 is not silently
  violated.
- Per the dispatch, this sub-agent did not edit `TASK-INDEX.md`, `current-task.md`, the project `CLAUDE.md` or
  `ci-gated-suites.txt`. The context-handoff checkpoints `task-execute` asks for were not written to
  `current-task.md` for the same reason; this note is the recovery record.
