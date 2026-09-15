# Task 029: re-profile and re-index after a version save (trace + escalation)

> Base: `0a8730fd2` (project branch head). Rigor FULL, opus @ high, directional.
> Symbols are named by symbol; line numbers are as of `0a8730fd2` and may drift.

## 0. Status: STOPPED at step 1. Escalation trigger 2 fired

| Trigger | Fired? | Why |
|---|---|---|
| 1. The version key would change another save path's key, retry or skip | **No** (by design, §5) | The discriminator rides a payload field that only `OfficeService.CompleteVersionSaveAsync` sets. Every other producer passes null, which gives today's key string unchanged. |
| 2. The indexing pipeline cannot replace a document's chunks (re-index would duplicate or orphan) | **YES** (§3) | The path the RAG job takes overwrites chunks by id and never deletes. A version with fewer chunks leaves the old tail behind, and Find can read it. The only delete-by-document primitive targets the tenant-default index, not the routed index, and nothing on this path calls it. Making re-index orphan-free means adding a delete step, which is exactly what the trigger reserves for a decision. |
| 3. The profile handler's skip logic is shared outside document analysis | **No** | No handler change is needed. `AppOnlyDocumentAnalysisJobHandler` already honours the producer's `job.IdempotencyKey`, and it serves only `AppOnlyDocumentAnalysis`. |

**No production code, no tests, no POML status change.** This note is the only file written. The key design (§5) is
ready to implement once §4 is decided.

## 1. Trace: finalization → profile → index (step 1)

### 1.1 The two saves reach finalization the same way

- **First save (create)**: `OfficeService.SaveAsync` → `CreateDocumentWithSpePointersAsync` creates the row →
  `OfficeJobQueue.QueueUploadFinalizationAsync(…, documentId: <new id>)`.
- **Version save**: `OfficeService.CompleteVersionSaveAsync` → `OfficeStorageUploader.WriteNewVersionAsync` (OBO PUT by
  item id, the SAME drive item) → `QueueUploadFinalizationAsync(…, documentId: target.DocumentId)`, i.e. the EXISTING
  id, driveId and itemId.
- `UploadFinalizationPayload` carries `DocumentId`, `TempFileLocation = spe://{driveId}/{itemId}`, `FileName`,
  `AiOptions`. **It carries no content hash and no version marker.** The two saves produce payloads that differ only
  in size/name, never in anything version-specific.

### 1.2 The keys and their skip checks

| Layer | Key (producer) | Skip check | Marked | Version save today |
|---|---|---|---|---|
| Finalization worker | `message.IdempotencyKey` = the save's authoritative key (`OfficeService.ResolveIdempotencyKey`) | `UploadFinalizationWorker.IsAlreadyProcessedAsync` (tenant cache `office-upload-processed`) | 7 days | Runs: the server key is content-aware for a version (task 023/039) |
| Profile | `analysis-{documentId}-documentprofile`, built in `UploadFinalizationWorker.QueueNextStageAsync` | `AppOnlyDocumentAnalysisJobHandler.ProcessAsync` → `IIdempotencyService.IsEventProcessedAsync(job.IdempotencyKey)` | on success, 7 days | **Skipped**: same document id as the first save |
| Index | `rag-index-{driveId}-{itemId}`, built in the shared `PostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync` | `RagIndexingJobHandler.ProcessAsync` → `IsEventProcessedAsync(job.IdempotencyKey)` | on success, 7 days | **Skipped**: same drive item as the first save |

So the gap is wider than 023 §13 recorded. The **index key is per-item**, and a version keeps its item id. Both
the profile and the index are skipped for 7 days after the previous successful run. After that they run again only
if something re-queues them.

Other producers of the same two keys (all must stay byte-identical, per the task constraint):
- `analysis-{id}-documentprofile`: `UploadFinalizationWorker.EnqueueAiAnalysisForAttachmentAsync` (email attachment
  children), `DocumentOperationsEndpoints`, `IncomingCommunicationProcessor`, `CommunicationService` (email-r2).
- `rag-index-{driveId}-{itemId}`: `PostUploadIndexingEnqueuer` (Office, Email-to-Document, post-analysis re-index),
  `DeliverToIndexNodeExecutor` (the "Document Profile" playbook's Index node), `BulkRagIndexingJobHandler`,
  `RagEndpoints` (enqueue-indexing).

### 1.3 Profile write semantics: overwrite, no own skip

`AppOnlyAnalysisService.AnalyzeDocumentAsync` has no "already profiled" check. A run overwrites the profile fields on
`sprk_document` (via the node playbook, Path C of `DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`, still subject to its
Part 4 Update Record defect). **A per-version profile key alone is sufficient for the profile half.**

### 1.4 Index write semantics: overwrite by id, never delete (the trigger)

`RagIndexingJobHandler` → `FileIndexingService.IndexFileAppOnlyAsync` → `IndexTextInternalAsync` →
`RagService.IndexDocumentsBatchAsync`:
- Chunk ids are `{speFileId}_{chunk.Index}`, where `speFileId` is the item id. A version keeps the item id, so its
  chunk ids collide with the previous version's.
- The write is `MergeOrUploadDocumentsAsync`. Chunks `0..N-1` are replaced in place: **no duplicates**.
- **Nothing deletes chunks `N..M-1`** when the new version yields fewer chunks (`N`) than the old one (`M`). They
  remain with the old content, the old `ChunkCount`, and the old `DocumentVector` (`IndexDocumentsBatchAsync` averages
  a vector per batch and stamps it only on the chunks in that batch). The code comment in `IndexTextInternalAsync`
  ("re-indexing … overwrites old chunks (instead of … stale orphans)") covers only the case where the new version has
  at least as many chunks as the old.
- **The orphans are user-visible.** `VisualizationService.GetSourceDocumentAsync` (the Find route task 033/034 uses)
  reads the source vector from `Size = 1` chunk filtered by `documentId`, with no ordering. It can pick an orphan and
  rank "related" documents by the OLD content.

Delete primitives that exist (`IRagService`):

| Method | Targets | Fit for this path |
|---|---|---|
| `DeleteBySourceDocumentAsync(documentId, tenantId)` | the tenant **deployment default** index (`GetSearchClientAsync(tenantId)`) | Partial. The handler writes to the **resolved** `SearchIndexName` (payload → `ISearchIndexNameResolver` chain document → parent → BU → default). When a record routes to a non-default allow-listed index, or under the Dedicated deployment model (`{tenant}-knowledge`), this delete misses the index that was written to. Also not called anywhere on this path. |
| `DeleteIndexedDocumentAsync(indexName, …)` | knowledge or discovery **only** (`EnsureKnownIndex`); admin endpoint | No: rejects any other allow-listed index |
| `DeleteDocumentAsync(chunkId, tenantId)` | one chunk id, default index | No |
| `RagIndexingPipeline.IndexDocumentAsync` (delete-then-upload, ADR-004) | knowledge + discovery, different chunk sizes; DocumentIntelligence-gated | No: a different pipeline; switching to it changes the indexes and the chunking |

**Conclusion:** the pipeline this path uses has no replace. Its existing delete reaches the right index only in the
default-routing case. An orphan-free re-index needs a new delete step, and possibly a routed-delete overload on the
`IRagService` facade. Trigger 2 reserves that for a decision.

## 2. Why the key change alone is not enough

Shipping only per-version keys (§5) would make a version save re-profile (correct) and re-index (partly correct).
Chunks `0..N-1` would be current; a shrunk version would keep a stale tail that Find can read. That is an
improvement on today, where every chunk is stale, but it violates the task constraint ("duplicate or orphan chunks
are a defect") and AC4 ("the index holds chunks for the current version only … proven by a test"). So I did not
implement the key half before the decision either.

## 3. Pre-existing: the same orphan tail already affects Compose and manual re-index

Compose save-back (`ComposeService` → `PostUploadIndexingEnqueuer.EnqueueIfApplicableAsync`, OBO →
`FileIndexingService.IndexFileAsync` → the same `IndexTextInternalAsync`) re-indexes on **every** save, with no
idempotency gate, onto the same item id. So a Compose document that shrinks already leaves stale tail chunks. The same
holds for `POST /api/ai/rag/send-to-index` after an edit. This task's constraint forbids changing Compose behaviour, so
it is not fixed here. It should be tracked (recommend `/defer`; not filed from this sub-agent).

## 4. 🔔 Human Input Required: escalation trigger 2

**Situation.** A per-version key makes a version save re-index, but the RAG path cannot replace a document's chunks.
It overwrites ids `0..N-1` and orphans the tail when the new version is shorter, and Find can read the orphan's stale
vector. The one delete-by-document primitive (`IRagService.DeleteBySourceDocumentAsync`) targets only the tenant
default index, not the per-record routed index this path may write to, and it is not wired in.

**Options.**

| # | Option | Blast radius | Trade-off |
|---|---|---|---|
| **A** | **Opt-in delete-then-upload for version re-index only.** Carry a flag (the version hash) on `RagIndexingJobPayload` → `FileIndexRequest`. When set, delete every chunk for `documentId` in the **same resolved index**, then upload. Needs one new routed overload `IRagService.DeleteBySourceDocumentAsync(documentId, tenantId, searchIndexName)` (+ `NullRagService`), mirroring the existing routed `GetSearchClientAsync(tenantId, indexName)` and allow-list check. | The facade gains one overload. Every non-version path is unchanged (flag null). | This is the documented ADR-004 pattern `RagIndexingPipeline` already uses. Between delete and upload the document has **no** chunks. If the upload fails, the job returns Failure, Service Bus retries, and the key is not marked, so it self-heals. |
| **B** | **Opt-in post-upload tail purge for version re-index only.** After a successful upload of `N` chunks, delete chunks with `speFileId eq X and chunkIndex ge N` (tenant-scoped) in the same resolved index. | Same as A (one new facade method) | No unsearchable window, and current content is never at risk. If the purge fails, the tail survives (logged) but current content is present. Slightly more bespoke query than A. |
| **C** | **A or B applied to every re-index** (fix `IndexTextInternalAsync` itself) | All index paths: Office, Email, Compose, send-to-index, playbook Index node | Also fixes the pre-existing Compose defect (§3). Violates this task's "every other path byte-for-byte" constraint, so it needs owner sign-off and should be its own task. |
| D | Accept orphan tails on shrink for now (ship §5 only) | Office version saves only | Fails AC4 and the constraint. A stale vector can mis-rank Find. |
| E | Profile only: version the analysis key, leave the index key per-item | Office version saves only | Find stays stale after every re-save. Fails the goal. |

**Recommendation: B, scoped opt-in to version re-index, plus a follow-up task for C.** B never removes current
content (A's zero-chunk window can surface as "no related documents" if the upload stalls), and the only extra work
it adds is a bounded tail delete in the index the chunks were actually written to. The routed-delete method is new
surface (CLAUDE.md §11): existing = `DeleteBySourceDocumentAsync`; extension = add the routed/tail variant beside it
rather than a parallel helper; cost of doing nothing = Find ranks re-saved documents by content that no longer exists.
C is the structurally right end state because Compose has the same defect, but it belongs in a task that owns every
indexing caller.

**Decision needed:** A, B or C (or D/E with the stated failures accepted). Also: may `IRagService` gain one delete
overload in this project?

## 5. The key design, ready once §4 is decided (triggers 1 and 3 checked)

- **Discriminator: the task 023/039 content hash** (`OfficeService.HashContent(request.Document.ContentBase64)`,
  SHA-256 hex). It does NOT reach the finalization payload today, and the payload's `message.IdempotencyKey` is not a
  substitute: when the client sends a body `idempotencyKey`, that key is the client's own and may be random per attempt
  (023 §10), which the constraint forbids. Plan:
  - compute the hash **once** in `SaveAsync` and feed both the save key and the payload from it (an internal
    `ResolveIdempotencyKey(request, contentHash)` overload behind the public one, so the 039-pinned key strings are
    unchanged);
  - pass it only from `CompleteVersionSaveAsync` via a new optional `QueueUploadFinalizationAsync` argument.
- **Payload:** `UploadFinalizationPayload.VersionContentHash` (string?, `[JsonIgnore(WhenWritingNull)]`), so a create's
  payload JSON stays byte-identical.
- **Profile key:** `analysis-{documentId}-documentprofile` when the hash is null (unchanged), else
  `analysis-{documentId}-documentprofile-version-{hash}`. Built in `QueueNextStageAsync` only. The attachment-child
  producer is untouched.
- **Index key:** a new optional `PostUploadIndexingRequest.ContentVersion` (trailing, default null; every existing call
  site compiles unchanged). `EnqueueAppOnlyIfApplicableAsync` appends `-version-{hash}` only when set. That also becomes
  the §4 opt-in flag on `RagIndexingJobPayload`.
- **No handler change** (trigger 3). **No new DI registration** (ADR-010). **No AI type in CRUD code** (ADR-013): the
  worker keeps enqueueing jobs; nothing new is injected.
- **Tests planned (ADR-038):**
  - (a) a data-mutation test on `OfficeVersionSaveWorld.FinalizationPayloads` + a worker-level test with an in-memory
    `IIdempotencyService`: a differing version queues one profile + one index job with version keys;
  - (b) the same payload redelivered produces the same keys, and the handlers skip it;
  - (c) a create and an Email save produce keys and payload JSON identical to master (pinned strings);
  - (d) an index-replace test per the §4 option chosen.

## 6. Residuals to know regardless of the option chosen

1. **ABA within the 7-day TTL.** A content-hash key is deterministic for identical content, which the constraint
   requires. So a document saved as version B, then A, then B again within 7 days skips the third run, and the profile
   and index show A. A simple revert to the ORIGINAL create content is safe (the create used the unversioned key). A
   fully ABA-safe discriminator needs the previous version's hash (a column or a Graph read), which is a scope change.
   Recommend accepting and documenting it.
2. **A version with identical bytes re-profiles once** (a new version key the first time). This is harmless. A second
   identical version save is deduplicated by the save spine.
3. **The playbook's Index node** (`DeliverToIndexNodeExecutor`) enqueues the per-item key. For a version it is skipped
   within 7 days of the item's last index. After that it would index the current content a second time: harmless
   with replace semantics, but not "exactly one". It is unreachable today, because the playbook fails first at Update
   Record (Part 4 of the profile architecture doc).
4. **Insights ingest** (`insights-ingest-{documentId}-{matterId}`) is not versioned. It is default-off and out of scope.
5. **Writer identity (from 023):** the version is written OBO, while profile and index read it app-only. Not verified
   live; no live environment was used here.

## 7. Gates

Not run: no production or test code changed (`git status` shows only this note), so build, tests, ArchTests, publish
delta and CVE results would be identical to the branch head. `/conflict-check` is deferred to the implementation pass.
Recommended TASK-INDEX status: **🔔 blocked (escalated, trigger 2)**. The POML `<status>` is left unchanged.
