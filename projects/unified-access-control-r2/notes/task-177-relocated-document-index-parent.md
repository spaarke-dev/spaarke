# Task 177: indexing recovers a document's parent when the request has none (#1510)

Owner decision 2026-10-09 ("fix now in uac-r2"). Branch `task/uac-r2-177`, from `origin/master` `c8a87d818`.

## The defect, re-confirmed

Two probe tests, written first and run against unchanged master code, both FAILED with `ParentEntity` null:

- `RelocatedFileIndexingTests.Issue1510_TheRelocatedFile_IsIndexedByTheJobHandler_UnderTheDocumentsParent` runs the
  REAL `RelocatedFileIndexing` into the REAL `PostUploadIndexingEnqueuer`, captures the job where Service Bus would take
  it, and runs it through the REAL `RagIndexingJobHandler`. The `FileIndexRequest` the handler builds is asserted, so
  the parent has to come from the handler, not from the enqueue.
- `RagIndexingJobHandlerTests.Issue1510_APayloadWithNoParent_IsIndexedUnderTheParentTheDocumentRowNames`.

The issue text did not carry the probe code, so these two were written fresh against the issue's Suspect 2 path. Both
are kept as regression tests and pass now.

## What changed, per goal

1. **One helper.** `Services/Ai/DocumentIndexParentResolver.cs`. Send-to-Index's inline matter / project / invoice block
   moved into it. Owner decision 2026-10-09 (Q1, below): **the most specific record wins**. The order is work assignment,
   then project, then matter, then invoice (the invoice keeps its place after matter and project). Only when none of
   those is named does the document's `sprk_relatedevent` give a parent: the event's core ancestor, read through the
   existing `CoreAncestorResolver.ResolveStampsAsync("sprk_event", …)` (one hop) and named matter, then project, then
   work assignment. Types use the names `scope=entity` uses (`SemanticSearchAuthorizationFilter.AuthorizableEntitySets`):
   `matter`, `project`, `invoice`, `workassignment`. That map has no `event`, so a chunk filed under the event itself
   could never be searched. The event inherits its access from its core record, so naming that record matches access
   (owner Q2: keep it).
2. **Job handler.** `RagIndexingJobHandler` calls `ResolveAsync(payload.DocumentId)` when `payload.ParentEntity` is null
   and a document id is set. That is one read of five columns. The recovered parent feeds both the index request and the
   `ISearchIndexNameResolver` routing. An unreadable row, an unreadable event, or no link at all logs and returns no
   parent, and the job still indexes. Only cancellation propagates. A parent the payload already carries is used as is.
3. **Send-to-Index.** It calls `ResolveAsync(DocumentEntity)`, which makes one read of the two links that type lacks
   (work assignment, related event) and applies the same order. **Behaviour change (owner Q1):** a document that names
   more than one of work assignment / project / matter used to take the matter and now takes the most specific record.
   Dev has 0 such rows (matter+project 0, work assignment + any of matter/project/invoice 0, 2026-10-09). A document that
   names only one of these takes the same parent as before. A document that names none and used to get no parent may now
   get one from its event. If that extra read fails, the answer is no parent rather than the entity's links: a work
   assignment the read would have found outranks them, so the derivation fails closed. Send-to-Index's existing tests
   pass unchanged.
4. **Producers.** Every listed producer already passes `DocumentId`, so none needed a change (table below). Compose
   create-on-save indexes INLINE on the OBO path (`PostUploadIndexingEnqueuer.EnqueueIfApplicableAsync`), not through the
   job handler. The issue says "that one change fixes every producer", but Compose would not have been fixed by the
   handler alone. So the enqueuer's OBO path applies the same `ResolveAsync(documentId)` when the caller sent no parent.

| Producer | Path | DocumentId | Fixed by |
|---|---|---|---|
| `RelocatedFileIndexing` (relocator: Make Secure, inherit, reconciliation, migration, move-along, re-copy) | app-only job | `request.DocumentId` ✓ | handler |
| `UploadFinalizationWorker.EnqueueRagIndexingAsync` (Office new item + version save) | app-only job | `documentId` ✓ | handler |
| `ComposeService` create-on-save STEP 4 | OBO inline | `promotion.DocumentRecordId`, set on both the create and the existing-row branch ✓ | enqueuer OBO path |
| `AnalysisResultPersistence.EnqueueRagIndexingJobAsync` | app-only job | `documentId` (always set by `AnalysisOrchestrationService`) ✓ | handler |

## Component justification (CLAUDE.md §11)

`DocumentIndexParentResolver` (new class, one singleton registration in `AnalysisServicesModule`):

1. **Existing.** Send-to-Index's inline block (`RagEndpoints` Step 3), `RagEndpoints.HoldIndexFileRequestToRow` (index-file:
   the same three columns, but a body-vs-row CHECK), `BulkRagIndexingJobHandler` (matter only, from its own OData query),
   and `CoreAncestorResolver` (core-ancestor stamps).
2. **Extension.** `CoreAncestorResolver` derives ACCESS stamps for a write. It covers core types only, gives no names,
   fails closed, and a document's invoice is not one of its roots. A search parent must also name an invoice and must
   never fail an index, so it cannot be that method. The new class reuses the resolver for the event hop instead. The
   Send-to-Index block moved here; it was not copied. `HoldIndexFileRequestToRow` answers a different question (does the
   body match the row) and was left alone.
3. **Cost of doing nothing.** A file moved by a secure transition, or saved by Office or Compose, is indexed with no
   `parentEntityType` / `parentEntityId`. `scope=entity` and `scope=all` then never return it, and parent-scoped RAG
   never grounds on it, until someone runs Send-to-Index by hand.

Registration: `TryAddSingleton`, unconditional, beside the enqueuer that consumes it (§10 F.1). It also calls the
TryAdd-only `AddCoreAncestorResolver`, so no composition has the enqueuer without the event hop's resolver. No package,
no endpoint, no column.

## Placement (CLAUDE.md §10)

It stays in the BFF: a synchronous Dataverse read on the existing indexing paths (the job handler and the OBO request),
with nothing to place elsewhere under ADR-052. It sits in `Services/Ai/` because every consumer is on the indexing side
(`RagEndpoints`, `RagIndexingJobHandler`, `PostUploadIndexingEnqueuer`). It adds no CRUD→AI dependency.

## Tests (the AC's closed set, plus three justified extras)

- The two #1510 probes above. The relocator one asserts the handler's `FileIndexRequest` and the routing call.
- `DocumentIndexParentResolverTests`: matter, project, work assignment, invoice (one theory); no parent link; an
  unreadable row.
- `RagIndexingJobHandlerTests.Issue1510_AnUnreadableDocumentRow_IndexesWithoutAParent_AndTheJobSucceeds`: an unreadable
  row gives no parent, and the job completes.
- Extra 1: `ADocumentUnderAnEvent_IsFiledUnderTheEventsCoreRecord`. Goal 1 names the event, and it is the only branch
  that goes through `CoreAncestorResolver`.
- Extra 2: `SendToIndexsPath_TakesTheMostSpecificRecord_IncludingTheWorkAssignmentTheEntityCannotCarry`. It pins
  that Send-to-Index takes the same answer as the job path.
- Owner Q1: `ADocumentNamingTwoRecords_IsFiledUnderTheMoreSpecificOne` (three ties).
- Extra 3: `PostUploadIndexingEnqueuerTests.EnqueueIfApplicableAsync_NoParentButADocument_IndexesUnderTheParentTheRowNames`.
  Compose indexes on this path, so without this test the Compose producer is untested.
- Construction sites updated for the new constructor parameter: `RagIndexingJobHandlerTests`,
  `PostUploadIndexingEnqueuerTests`, `PostUploadIndexingEnqueuerReplaceStaleChunksTests`,
  `VersionSaveAiRefreshSeamTests`. They use the shared `TestDocumentIndexParentResolver.Over()`, which is the real
  class over a Dataverse double that answers "not found", so their behaviour is unchanged.

## Known limits (K-class, one line each)

- K2: the related twins (`sprk_relatedmatter` / `sprk_relatedproject` / `sprk_relatedworkassignment`) are not read. 0 live
  rows set them (CoreAncestorResolver note, 2026-10-02), and no BFF writer writes them. Adding one is a single line.
- K2: an event whose only core ancestor is a service request gives no parent, because search cannot authorize one.
- K4: Send-to-Index's response `ParentEntityId` can now name an event's core record to a caller who holds Write on the
  document. It is the id of the record the document's family is filed under; no content is exposed.

## Backfill (owner Q3: no new code; a post-deploy Send-to-Index repair, steps in PR #1517)

The constraint rules out a backfill job. Chunks written before this fix keep no parent until the document is re-indexed
(Send-to-Index, a save, or another relocation). The index could not be counted: `az` holds control-plane access to
`spaarke-search-dev` but data-plane queries return 403, and a query key was deliberately not fetched. Dataverse proxy
(spaarkedev1, read-only, 2026-10-09): 574 documents. 144 carry a matter / project / invoice, 1 carries only a work
assignment, 0 carry only an event, and 429 carry no parent link at all (no fix can give those a parent). The documents at
risk are the relocated ones among the 145 with a link. If the owner wants them repaired at once, the cheapest path is a
one-off Send-to-Index over the documents that carry a link and were relocated (`sprk_searchindexname` stamped after a
move). That needs no new code.

## Owner decisions (main session, 2026-10-09)

1. **Several core records named:** the MOST SPECIFIC wins (work assignment over project over matter; invoice keeps its
   place). This is applied in the shared helper, so Send-to-Index changes for that case too. Tests: one per tie
   (`ADocumentNamingTwoRecords_IsFiledUnderTheMoreSpecificOne`: matter+project gives project, matter+work assignment
   and project+work assignment give work assignment) and the Send-to-Index path (a work assignment found by the extra
   read beats the entity's matter and project).
2. **Event:** naming the event's core record is correct. Kept.
3. **Backfill:** no new code. A one-off Send-to-Index repair runs as a post-deploy main-session step (in the PR body):
   documents that were relocated and carry a parent link.

## Out-of-scope defects found (filed as #1516)

- `BulkRagIndexingJobHandler.QueryDocumentsAsync` selects and filters `_sprk_matterid_value` and `sprk_ragindexedon`.
  Neither column exists on `sprk_document` (live check 2026-10-09), so every bulk / scheduled RAG sweep's Dataverse query
  fails with 400. It also derives a matter-only parent, so a re-indexed project document would lose its parent. #1516.
- `DataverseServiceClientImpl.MapToDocumentEntityWithLookups` never sets `MatterName` / `ProjectName` / `InvoiceName`, so
  Send-to-Index (and the index-file row parent) always writes "Unknown Matter" / "Unknown Project" / "Unknown Invoice" as
  `parentEntityName`. #1516.

## Verification

- Probes: both FAILED on master `c8a87d818` (`ParentEntity` null) and PASS after the fix.
- Targeted: 276 / 276 across `RagIndexingJobHandler*`, `PostUploadIndexingEnqueuer*`, `RelocatedFileIndexing*`,
  `DocumentContainerRelocator*`, `*SendToIndex*`, `RagEndpoints*`, `VersionSaveAiRefresh*` and
  `DocumentIndexParentResolverTests`.
- Full `Sprk.Bff.Api.Tests`: 19,068 passed, 54 skipped, 18 failed. All 18 failures were host-startup timeouts (3-7
  minutes each, "The client aborted the request") on the shared machine, in suites this change does not touch. Re-run in
  isolation, all 32 cases in those 18 tests pass.
- `Spaarke.ArchTests`: 841 / 841. `Spe.Integration.Tests` and `Sprk.Bff.Api.IntegrationTests` build (both compile the
  new shared helper).
- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: clean.
- Publish size (`dotnet publish -c Release`, fresh master `c8a87d818` vs this branch, the same machine): 127,552,292 →
  127,562,576 bytes uncompressed (+10,284 B); zipped 37,199,312 → 37,203,854 bytes (+4,542 B, about 35.48 MiB). No package
  reference changed, so no new CVE is possible.
- After the owner's Q1 decision: the targeted run is 279 / 279 and the `-warnaserror` build is clean.
- Self-review (task-execute Step 9.5): no F-class finding remains. The K-class items are listed above. The one full
  verifier pass is the main session's.
