# Task 176: AI retrieval access trim (#1511)

**Status:** implemented on `task/uac-r2-176`, with the PR open against master. The main session runs the two adversarial verifier passes (tags `auth` and `security`).

**History:** the first pass stopped on escalation trigger 1 (latency; measurements in §2). The owner chose **option B** on 2026-10-09: one batch read per page, run as the caller.

## 1. #1511 re-confirmed on master c8a87d818

A probe ran Document Search and Document Discovery over one chunk of a secure-matter document, with the caller set to `UserId = caller-without-read`. On master, both methods put the secret text in `result.Data.content`: the full chunk for SearchDocuments, the 300-character preview for SearchDiscovery. The probe now lives in the regression suite as the `SecureDocument_*` tests (§5).

## 2. Why the task-163 call was not reused (trigger 1, measured)

`IAiAuthorizationService.AuthorizeAsync` checks documents one at a time. On a Redis miss, each document costs four Dataverse round trips: `systemusers`, `RetrievePrincipalAccess`, team memberships and roles. The checks cannot run in parallel because they share one HttpClient header.

Measured on dev App Insights `spe-insights-dev-67e2xz` over the last 7 days, a Web API call takes about 90 ms at p50 and 100–145 ms at p90. So each cold document costs about 360 ms, and 20 documents cost about 7.2 s. The trigger's limit was about 2 s.

**Option B (owner decision):** one caller-scoped read per page. It goes through the existing user-OBO `IDataverseUserClient`, with at most 20 ids per read: `GET sprk_documents?$select=sprk_documentid&$filter=sprk_documentid eq … or …`. Dataverse returns only the rows the caller can Read. No Access walls are enforced by revoking shares, so the native read reflects them.

## 3. What changed, per goal

| Goal | Change |
|---|---|
| 1. One trim seam | New `IRetrievalAccessTrim` / `RetrievalAccessTrim` in `Services/Ai/PublicContracts` (the ADR-013 facade), registered Scoped in `AddPublicContractsFacade`. **Fails closed:** no declared caller, no request principal, or a request principal that is not the declared caller → `NoVerifiedCaller`, no rows. A read error, 429, missing token, OBO failure or malformed body → `CheckFailed`, no rows. A row with no usable GUID id is dropped. An answer naming an id that was not asked is ignored. **Ids:** parsed as GUIDs and written to the filter bare and lowercase (ADR-044), at most 20 per read, larger pages chunked. **Over-fetch:** 2× through `RetrievalAccessTrim.CandidatePoolSize`. Nothing is cached. The internal helper `ReadableRagSearch.SearchReadableAsync` (`Services/Ai`, so CRUD code never sees `IRagService`) does over-fetch → search → trim → cut to page. With no verified caller it does not run the search at all. `TotalCount` is the number of rows returned. |
| 2. Every entry point | See the caller inventory in §4. |
| 3. Caller identity | **Chat:** `ChatInvocationContext.UserId` (the oid). The seam requires it to equal the oid of the ambient request principal, whose bearer token the OBO read uses. **Playbook:** new `NodeExecutionContext.CallerObjectId` ← `PlaybookRunContext.StartedByOid` → new `ToolExecutionContext.CallerObjectId`. **HTTP analysis path:** `AnalysisOrchestrationService` sets it from `httpContext.User`. App-only and scheduled runs have none, so they get no rows. Tool results say so in `message` / `summary` (`RetrievalTrimResult.WithheldMessage`), and the seam logs a warning. |
| 4. Scope | With a host, SearchDocuments is now bound to the host parent, as SearchDiscovery already was. The parent id is canonicalized per ADR-044. **When task 164 drops the host:** both methods search the tenant, and the trim keeps only readable documents. The index never returns an untrimmed tenant-wide page. **Choice recorded:** "trimmed tenant search" rather than "nothing", so standalone chat still finds the user's own documents, and the trim stays the authority. |
| 5. Readable results unchanged | `ReadableDocument_IsReturnedExactlyAsBefore` compares the Data and Metadata JSON with and without the trim and finds them equal. |

## 4. Caller inventory: every `IRagService.SearchAsync` and semantic-search caller

| # | Caller | Reach | Disposition |
|---|---|---|---|
| 1 | `DocumentSearchHandler` SearchDocuments: chat, and playbook via `ExecuteAsync` | chat tool "SYS-Document Search", always offered | **Trimmed**; tenant-wide, **never bound to the host** (fix round 2: supersedes POML goal 4 for this method, see §12) |
| 2 | `DocumentSearchHandler` SearchDiscovery: chat and playbook | chat tool "SYS-Document Discovery" | **Trimmed**; bound to the host only for matter / project / invoice (the types the index holds today); any other host or none → trimmed tenant search |
| 3 | `KnowledgeRetrievalHandler` SearchKnowledgeBase | chat tool "SYS-Knowledge Base Search", playbooks | **Trimmed** |
| 4 | `KnowledgeRetrievalHandler` GetKnowledgeSource | chat tool "SYS-Knowledge Source Retrieval", playbooks | **Trimmed** (a knowledge-source chunk with no document id is dropped; see §7) |
| 5 | `AiAnalysisNodeExecutor.RetrieveDocumentContextAsync` (L2) | playbook nodes with `includeDocumentContext` | **Trimmed** with the run principal; app-only run → none. A parent named in ConfigJson that the caller cannot read now yields nothing. |
| 5b | `AiAnalysisNodeExecutor.RetrieveEntityContextAsync` (L3, records index, app-only `IRecordSearchService`) | playbook nodes with `includeEntityContext` | **Gated (fix round 1, F1).** One caller-scoped read of the ConfigJson parent (`TrimByRecordAsync`). No principal, an unreadable parent or a failed check → no L3, logged. Also new: the records-index hit must BE that parent (`RecordId` = `parentEntityId`); the keyword search's top hit was not guaranteed to be. |
| 6 | `SemanticSearchToolHandler` (2 calls, `ISemanticSearchService`) | analysis/playbook tool "Search Documents" | **Trimmed** (2× pool, page cut, counts = rows returned); no caller → `RESULTS_WITHHELD` warning |
| 7 | `DocumentClassifierHandler.GetRagExamplesAsync` | analysis tool "Document Classifier", including app-only document profiling | **Trimmed.** The examples are other documents' text placed in the prompt. App-only profiling has no caller, so it classifies zero-shot. |
| 8 | `AnalysisRagProcessor.ProcessRagKnowledgeAsync` (knowledge sources, after its tenant RAG cache) | HTTP analysis (`AnalysisOrchestrationService.ExecutePlaybookAsync`) | **Trimmed** after the cache, with the HTTP caller. The cache holds untrimmed rows and is never returned as is. With no caller, RAG sources contribute nothing. |
| 9 | `InsightsOrchestrator.SearchAsync`, the only RAG call behind `/api/insights/search`, `/api/insights/assistant/query` and `AssistantToolCallHandler` | user routes (task-163 subject gate) | **Trimmed** with `request.CallerPrincipal`. The subject Read gate does not cover a restricted document under that subject (round 87: a child may be stricter). |
| 10 | `CommunicationTriageAi.RetrieveMatterCorrespondenceGroundingAsync` | unattended communication enrichment; output persisted on the communication record | **Trimmed with NO caller → always withheld** (search not run); triage runs context-free. There is no single reader whose access could bound it: the output is read by every reader of the communication. **Behaviour change**, listed in §8. |
| 11 | `SemanticScopeProvider.GetSemanticContextAsync` | no consumer is wired yet (DI only) | **Trimmed** with `request.CallerPrincipal`; lifetime changed Singleton → Scoped (no singleton resolves it) |
| 12 | `RecallSessionFileHandler` (3 calls) | chat tool "SYS-Recall Session File" | **Exempt, with evidence.** It searches `spaarke-session-files` with `tenantId` + `sessionId = context.ChatSessionId`, which is server-built from the caller's session. Every chat session route carries `AddSessionOwnershipFilter`. The index has two writers, both indexing bytes the session owner uploaded: `ChatDocumentEndpoints.UploadDocumentAsync` (`POST …/sessions/{id}/documents`) and `SessionFileRehydrationService`, which re-indexes that upload's durable copy. The by-reference `…/documents/from-document` ingest is Read-gated (`AddAiAuthorizationFilter`) and does not index (`SearchDocumentIdsCsv = ""`). The rows are not sprk_documents, so the trim could not evaluate them. |
| 13 | `SessionFileTextSource.FetchAsync` | chat summarize of the session's uploaded files | **Exempt**, same evidence as #12 (same index, same session filter, the caller's own uploads) |
| 14 | `RagEndpoints.Search` (`/api/ai/rag/search`) | HTTP | **Already trimmed (task 163)**, left on `IAiAuthorizationService` (§6) |
| 15 | `SemanticSearchEndpoints` (`/api/ai/search`) | HTTP | **Corrected in fix round 2.** It checked only the PARENT record (`IsPermitted`, `AuthorizeRowsByParentAsync`), so a restricted document under a readable matter came back with name, highlights, summary and TL;DR for `scope=entity` and `scope=all`. It is now also trimmed to the caller's readable documents after the parent step (`TrimToReadableDocumentsAsync`), and the counts are recomputed. |
| 15b | `RecordSearchEndpoints`, visualization "related" | HTTP | Per-row checked (unchanged). `/api/ai/search/count` returns counts only and is unchanged (K). |
| 16 | `FileIndexingService`, `RagIndexingPipeline`, `PostUploadIndexingEnqueuer`, `RelocatedFileIndexing`, `KnowledgeBaseEndpoints` (health counts) | indexing / counts | Do not return document content to a user; no change |
| 17 | `ReferenceRetrievalService` (L1) | playbooks | Not `IRagService`: it reads the shared `spaarke-rag-references` corpus (`tenantId = "system"`, curated reference material, not customer documents). Outside the trim, as the issue says. |
| 18 | `IndexRetrieveNode` (`SearchAsync<SearchDocument>` on `spaarke-insights-index`) | predict-matter-cost@v1 (`retrieveCohortObservations`, `retrievePrecedents`), matter-health-single (`retrieveObservations`), reaching `POST /api/insights/ask` | **Trimmed (fix round 1, F1)** through `TrimByRecordAsync` as the run principal, from a 2× pool. The deciding record is the row's **source document** when its `document` evidence names one: a bare `sprk_document` id, or `spe://drive/{d}/item/{i}`, checked as "the caller can read a `sprk_document` row on that `sprk_driveitemid`" (the dominant emission shape). Otherwise its **scope record**: `scope.entityType` + `entityId` (matter, project, invoice, work assignment), or the legacy `scope.matterId`. A row with neither is dropped. `totalCount` is now the returned count. The `/ask` result cache key already includes the caller oid (`AccessibleScopeHash`), so trimmed results are not shared between callers. **Precedents** are kept only when the caller can read every supporting matter (owner option b, §11). |
| 19 | `InternalIndexProvider` (citation verification, `spaarke-rag-references`) | VerifyCitations tool | Reference corpus (`tenantId = "system"`, script-fed); returns a verification verdict and a score. Not customer documents; outside the trim. |
| 20 | `FilesIndexIngestDocumentSource` | Insights ingest (app-only producer) | Reads one named document's chunks to EXTRACT observations; nothing returns to a user here. Its output is trimmed at retrieval (#18). |
| 21 | `InvoiceSearchService` (`/api/finance/invoices/search`) | HTTP finance route | Matter-Read-gated route (task 130); invoice rows of that matter, not AI document retrieval. Unchanged. |
| 22 | `RecordSearchService`, `RecordMatchService`, `DataverseIndexSyncService` (records index) | record search / matching | Record metadata, not document content. `/search/records` is per-row checked (issue table). Matching runs in background or through its own routes. Out of scope; listed under out-of-scope defects only if it returns a record the caller cannot read (not established here). |
| 23 | `SessionFilesCleanupJob`, `SessionFilesHotIndexAccess`, `EmbeddingMigrationService`, `PrecedentProjectionSync`, `ObservationIndexUpserter`, `InvoiceIndexingJobHandler`, `RagIndexingPipeline`, `KnowledgeDeploymentService`, `SearchClientFactory`, `ResilientSearchClient`, `VisualizationService`, `SemanticSearchService` | jobs, writers, factories, and services whose callers are rows 6/15 | No user-facing read of their own |

The search covered every `IRagService` injection (`grep -rn "IRagService"`) and every `.SearchAsync(`. Fix round 1 re-grepped every `SearchAsync<`, `.GetSearchClient(` and `new SearchClient(` in `Sprk.Bff.Api` (rows 18–23), which is where `IndexRetrieveNode` had been missed.

## 5. Tests

| File | What |
|---|---|
| `tests/integration/regression/Ai/Issue1511_AiRetrievalAccessTrimTests.cs` (new, KEEP path) | Real trim plus the simulated user-OBO boundary. `SecureDocument_DoesNotReachDocumentSearch` (SearchDocuments and SearchDiscovery), `…KnowledgeRetrieval` (SearchKnowledgeBase and GetKnowledgeSource), `…PlaybookNodeDocumentContext` (through `AiAnalysisNodeExecutor.ExecuteAsync`), `…SemanticSearchTool`; `ReadableDocument_IsReturnedExactlyAsBefore`; `NoCallerIdentity_ReturnsNoRows_SaysSo_AndDoesNotSearch`; `UnattendedPlaybookRun_WithNoRunPrincipal_ReturnsNoRows`; `AFailedOrThrottledAccessCheck_ReturnsNoRows` (429); `HostDropped_DiscoverySearchesTheTenant_ButReturnsOnlyWhatTheCallerCanRead`; `WithAHost_SearchDocumentsIsBoundToTheHostParent`. 12 cases. |
| `tests/unit/.../Services/Ai/PublicContracts/RetrievalAccessTrimTests.cs` (new) | The owner's batch-read rules: ADR-044 canonicalization, chunks of 20 (45 ids → 20/20/5) with order kept, unusable ids dropped, an unasked id in the answer ignored, caller ≠ request principal → nothing and no read, malformed answer → nothing. 6 cases. |
| Regression suite, fix round 1 additions | `WithAnIndexedHost_BothMethodsAreBoundToTheHostParent` (`sprk_matter` → `matter` for both methods, `sprk_workassignment` → `workassignment`, through production `ChatHostContext` normalization); `WithAHostTheIndexDoesNotStore_SearchIsTheTrimmedTenantSearch_NotEmpty` (`sprk_analysisoutput` and `sprk_document` hosts × both methods: no parent filter, the readable document still returned, the secure one trimmed); `SecureDocument_AndSecureMatter_Observations_DoNotReachIndexRetrieve` (restricted document under a readable matter, unreadable `spe://` item, secure matter with no document evidence → dropped; readable item and readable matter → kept); `L3EntityContext_IsAddedOnlyForAParentTheCallerCanRead` (unreadable → none; readable → still added). The earlier `WithAHost_SearchDocumentsIsBoundToTheHostParent` used `"sprk_matter"` raw (F3) and is replaced. Total 21 cases. |
| `tests/unit/.../Insights/InsightsOrchestratorTests.cs` (+1) | `SearchAsync_DocumentTheCallerCannotRead_IsNotReturnedOrSummarized` (caller #9) |
| Shared helpers (new) | `tests/integration/Shared/ReadableDocumentsUserClient.cs` (simulated `IDataverseUserClient`, the documented mock boundary); `PermitAllRetrievalAccessTrim.cs` (for suites that test what callers do with permitted rows) |
| Updated (fix round 1) | `DocumentSearchHandlerTests.ExecuteChatAsync_SearchDiscovery_ForwardsParentEntity_FromKnowledgeScope` now expects `matter`; it pinned the F2 bug (`sprk_matter` matched no chunk). `IndexRetrieveNode*Tests` and `InsightsNodesIntegrationTests` pass an `IServiceScopeFactory`. `ReadableDocumentsUserClient` now answers any (entity set, key) read. |
| Updated | `DocumentSearchHandlerTests`, `KnowledgeRetrievalHandlerTests`, `DocumentClassifierHandlerTests`, `InsightsOrchestratorTests`, `PredictMatterCostPlaybookTests`, `AnalysisOrchestrationServiceTests`, `SemanticScopeProviderSeamTests`: constructors take the trim (permit-all). `ExecuteChatAsync_SearchDiscovery_DefaultTopK_IsTen` now asserts the 2× pool (page size still 10). |

**Beyond the stated contract (one line each):**
- "unasked id ignored" guards the line that keeps a Dataverse answer from widening the result.
- "malformed answer" covers the `CheckFailed` branch that a non-error status does not reach.
- The Insights test covers caller #9, which a user reaches and which none of the AC tests cover.
- Callers #7, #8, #10 and #11 use the same `SearchReadableAsync` path the AC tests prove, so no per-caller test was added (K3).

## 6. Seeding proof and the task-163 swap

**Seeding proof:** `RetrievalAccessTrim.TrimAsync` was changed to `var kept = rows.ToList();` (trim disabled). Seven tests failed: `SecureDocument_DoesNotReachDocumentSearch` ×2, `…KnowledgeRetrieval` ×2, `…PlaybookNodeDocumentContext`, `…SemanticSearchTool` and `HostDropped_…`. The change was reverted, and all pass.

**Seeding proof, fix round 1:**
- `IndexRetrieveNode` was changed to take `candidates` instead of `trim.Rows`.
- The L3 gate was changed to `parentCheck.Rows.Count < 0`.
- Two tests failed: `SecureDocument_AndSecureMatter_Observations_DoNotReachIndexRetrieve` and `L3EntityContext_IsAddedOnlyForAParentTheCallerCanRead(callerCanRead: False)`.
- Both changes were reverted, and all 21 pass.

**The task-163 trim stays as is.** `RagEndpoints.TrimToReadableDocumentsAsync` is not moved onto the seam. Its tests (`RagEndpoints*`, contract and auth suites) drive the real `AiAuthorizationService` through `CallerAccessSeam` (`GetUserAccessAsync`), so a swap to `IDataverseUserClient` would change those tests. Per the owner's condition ("only if mechanical with its tests unchanged"), it is not done. `/api/ai/rag/search` keeps its per-document cost; that is recorded, not fixed.

## 7. Knowledge-source chunks without a document id (escalation trigger 2): count

Read-only, with the dev **query** key, against `spaarke-search-dev` / `spaarke-files-index` (1,347 chunks):

| Filter | Count |
|---|---|
| `knowledgeSourceId ne null and documentId eq null` | **0** |
| `knowledgeSourceId ne null` | 0 |
| `documentId eq null` (any orphan) | 2 (dropped by the trim, as task 163 drops them) |

Trigger 2 was not fired on dev. The reference KB is in `spaarke-rag-references` (`tenantId = "system"`), which no `IRagService` caller can reach.

**Before a customer deploy (K2), per environment:**
1. Repeat the first count. A non-zero count means GetKnowledgeSource or knowledge-source-scoped search returns nothing for those sources after this change.
2. Knowledge-source chunks that DO carry a document id are now trimmed like any other document. A user who cannot read a knowledge source's underlying `sprk_document` rows (for example, a curated library filed on a restricted matter) gets nothing from that source. Check that knowledge-source documents sit on records their audience can read.

## 8. Known behaviour changes (for the owner)

1. **Unattended runs get no document results** (fail closed, with a warning logged by the seam). This covers app-only and scheduled playbook runs (`ExecuteAppOnlyAsync`, `PlaybookSchedulerJob`, Service Bus analysis) and app-only document profiling.
   - **Dev survey:** no `sprk_playbooknode` has `includeDocumentContext`, and no playbook node is linked to Document Search, Document Discovery, Knowledge Base Search, Knowledge Source Retrieval, Search Documents or Document Classifier. So no scheduled playbook on dev uses document retrieval today.
   - The Daily Briefing scheduler composite and the notification playbooks use Dataverse queries, not RAG.
2. **Communication triage runs context-free** (caller #10): matter-correspondence grounding is withheld for every email.
3. **Document Classifier RAG examples:** examples under app-only profiling are gone (zero-shot).
4. **Chat search pages:** they may be shorter than topK when the caller can read few of the top 2× candidates. No "partial" flag is added, because the chat text already states the count.
5. **Insights cohort retrieval (`IndexRetrieveNode`)** returns only observations the caller can read. The matter-cost cohort shrinks for a caller who cannot read many matters, and EvidenceSufficiency may decline sooner. With no run principal (any app-only run), it returns nothing.
   - On dev, `IndexRetrieve` nodes exist only in predict-matter-cost@v1 (`retrieveCohortObservations`, `retrievePrecedents`) and matter-health-single (`retrieveObservations`).
   - `/ask` runs predict-matter-cost with the HTTP caller (`ExecuteAsync`), so the principal is present.
   - The dev insights index is empty (0 documents).
6. **Precedents** reach a caller only when they can read every supporting matter (§11).
7. **Chat hosted on a type the index does not store** (an analysis, a document, an event) now gets the trimmed tenant search. Before, it was always empty, because `sprk_*` types never matched the index's `matter`/`project`/… values. That also affected a `sprk_matter` host on SearchDiscovery before this task.

## 9. §10 / §11 placement and justification

**Placement:** `Services/Ai/PublicContracts` (the ADR-013 facade), so a future CRUD consumer can trim without touching `IRagService`. Registered in `AddPublicContractsFacade` (compound-ON), because every consumer is compound-ON. Its dependencies (`IDataverseUserClient`, `IHttpContextAccessor`) are unconditional.

**§11 justification:**
1. **Existing:** it overlaps the task-163 `RagEndpoints.TrimToReadableDocumentsAsync` (per-document `IAiAuthorizationService`) and the semantic-search per-parent loop. Both are private to their endpoints, and both cost one Dataverse round trip or more per document.
2. **Extension:** the task-163 helper could not be extended to meet the owner's latency bar. It is a per-document RPA loop (about 360 ms per cold document), and it takes an `HttpContext` that chat tool handlers do not have. The batch read is a different mechanism on an existing client.
3. **Cost of doing nothing:** chat, playbook and Insights retrieval keep returning the text of secure, Restricted and No Access documents to users who cannot read them (#1511, F1).

**New surface:** `ToolExecutionContext.CallerObjectId` and `NodeExecutionContext.CallerObjectId` (needed to carry the run principal, goal 3; `UserId` there is a systemuserid, not an oid), and `ReadableRagSearch` (internal helper, one copy of over-fetch/trim/cut instead of eight).

**Publish size:** see §10.

## 10. Publish-size delta, quality gates and test runs

**Publish size.** `dotnet publish -c Release` of master `8a9ecaac1` (fresh detached worktree) against this branch merged on it; both outputs zipped with Deflate.

| | master | branch | delta |
|---|---|---|---|
| Compressed publish | 37,199,213 B (37.20 MB) | 37,208,675 B (37.21 MB) | **+9,462 B (+0.01 MB)** |
| Raw publish folder | 127,552,920 B | 127,577,160 B | +24,240 B |
| `Sprk.Bff.Api.dll` | 16,801,792 B | 16,822,784 B | +20,992 B |

No package was added, so there is no new CVE surface.

**ADR-010 ratchet.** `IRetrievalAccessTrim → RetrievalAccessTrim` is a new 1:1 interface. The ceiling was raised 157 → 158 in `tests/Spaarke.ArchTests/ADR010_DITests.cs`, with the seam justification: a PublicContracts facade plus a real test seam. The ArchTests suite passes: 889/889.

**Test runs.**

| Run | Result |
|---|---|
| Full `Sprk.Bff.Api.Tests` (before merging master) | 19,080 passed, 13 failed, 54 skipped |
| The 13 failing tests, re-run alone | 165/165 passed |
| Affected AI suites + new tests, after merging master | 883 passed, 2 skipped |
| `Spe.Integration.Tests` `ToolFrameworkIntegrationTests` (real DI resolves the handlers with the trim) | 16/16 |

The 13 failures each took 3–5 minutes. They are Office save, document identity, Insights endpoint and spend-limit contract tests that start `WebApplicationFactory`, and they timed out under machine load.

**Step 9.5 self-review.** Findings fixed in the task:
- F2: a stale comment in `PlaybookChatContextProvider` still said SearchDocuments runs tenant-wide.
- F2: `AnalysisRagProcessor` still set `TotalCount` to the untrimmed count.

Known limits (K):
- K4: the `SemanticScopeProviderSeamTests` "no caller" test uses the permit-all double, so it still describes `RagService`'s public-only filter, not the provider's new no-caller behaviour. The provider has no consumer yet.
- K2: chat pages may come back short (§8.4).

## 11. Fix round 1 (verifier pass 1 on PR #1520)

| Item | Fix |
|---|---|
| **F2** host binding emptied both methods | `DocumentSearchHandler.CanonicalParentScope` normalizes the host type (strips `sprk_`) and binds ONLY to a type the index stores: matter, project, invoice, servicerequest, account, contact, and `workassignment` (the task-177 parent resolver, PR #1517, writes `workassignment`). Anything else → the trimmed tenant search (the host-dropped decision). A non-GUID host id → unbound. **What the dev index holds today** (`spaarke-files-index`, facet on `parentEntityType`, 1,347 chunks): matter 205, event 6, invoice 1, project 1. `event` is deliberately NOT bound: task 177 files an event's documents under the event's parent, so an `event` filter would find only 6 legacy chunks and miss the rest. |
| **F3** test used `"sprk_matter"` raw | Scopes are built through `ChatHostContext` (production `EntityTypeNormalizer`). Added an analysis-host case and a document-host case for both methods: no parent filter, readable document returned, secure one trimmed. |
| **F1** `IndexRetrieveNode` untrimmed | Trimmed (row #18). New seam method `IRetrievalAccessTrim.TrimByRecordAsync` with `RetrievalRecordKey` (factories: `Document`, `DocumentByDriveItem`, `ParentRecord`). Only those (entity set, column) pairs ever reach a filter. Same rules: 20 keys per read per kind, ADR-044 GUIDs, unasked keys ignored, fail closed. `IndexRetrieveNode` resolves the scoped trim through `IServiceScopeFactory` (it is a singleton, like `LiveFactNode`). |
| **F1** L3 entity context unchecked | Gated (row #5b), plus the hit-must-be-the-parent check. |
| **K3** triage logged a warning per email | The seam logs a no-DECLARED-caller withhold at **Information** (an expected unattended path). A declared caller that is not the request principal still logs a **Warning** (an anomaly). User-visible unattended paths log their own Warning: Document Search, Knowledge Retrieval, L2, `AnalysisRagProcessor`, `IndexRetrieveNode`, and now also `SemanticSearchToolHandler` and the Document Classifier examples. |

**Precedents (trigger 2), owner decision 2026-10-09: option (b).** A Precedent is kept only when the caller can read EVERY one of its supporting matters (`supporting-matter` evidence, `matter://{id}`, as written by `PrecedentProjectionMapper`). The check is one batched `TrimByRecordAsync` pass per page, shared with the observations: every row's records are checked together and a row is kept only when all of them pass. A Precedent with no supporting-matter list, or with any unreadable or unparseable one, is dropped (fail closed). Test: `Precedent_IsKeptOnlyWhenEverySupportingMatterIsReadable` (all readable → kept, in one read of 2 ids; one unreadable → dropped; list missing → dropped). Option (c), treating confirmed Precedents as firm-level curated knowledge outside the trim, remains an owner policy choice for later.

**Known limits kept (verifier K1, K2, K4, K5):** left as known limits, no code change, per the coordinator. The verifier's full K-text is in the verifier report, not repeated here. K2 (knowledge-source documents) has its pre-deploy check extended in §7. This note's own K items are in §8 and §10.

**Test runs after fix round 1:**
- Affected AI suites: 1,320 passed, 3 skipped, after the F2 assertion fix.
- ArchTests 889/889; no new interface, so the ADR-010 ceiling is unchanged.
- `Spe.Integration.Tests` `ToolFrameworkIntegrationTests` 16/16.

## 12. Fix round 2 (verifier pass 2 on `26ab76242`)

| Item | Fix |
|---|---|
| **F1** `/api/ai/search` parent-only check | After the parent step, every scope's rows go through `IRetrievalAccessTrim.TrimAsync(r => r.DocumentId)` as the caller. `TotalResults` drops by the number of rows removed, `ReturnedResults` is the row count, and a failed check returns no rows. Regression test: `Search_ARestrictedDocumentUnderAReadableMatter_IsNotReturned` (`scope=entity` and `scope=all`) in `Spe.Integration.Tests/SemanticSearch/SemanticSearchAuthorizationTests.cs`, through the real route. The fixture's `SwitchableRetrievalAccessTrim` (new shared helper) is permit-all for the existing parent-record tests and the real `RetrievalAccessTrim` + `ReadableDocumentsUserClient` for this one. **Seeding check:** with the route's trim call removed, both cases fail; reverted. |
| **F2 + K4** host binding (main-session decision) | **SearchDocuments is no longer bound to the host at all**: tenant-wide, always trimmed. **This supersedes POML goal 4 for SearchDocuments.** Reason: the trim is the access control, and binding only cost findability. A work-assignment host got empty results, and after #1517 a matter-hosted chat would miss documents filed under the matter's work assignments. **SearchDiscovery** keeps its pre-PR binding, limited to host types the index holds TODAY: matter, project, invoice. `workassignment` stays out until existing chunks are backfilled. Any other host → trimmed tenant search. Tests: `WithAMatterHost_SearchDiscoveryIsBoundToTheMatter`; `UnboundHosts_GetTheTrimmedTenantSearch_NotAnEmptyOne` (SearchDocuments with a matter host, both methods with a work-assignment host, analysis and document hosts: no parent filter, readable document returned, secure one trimmed). |
| **K2 hardening** `IndexRetrieveNode` first document ref only | A non-Precedent row now needs EVERY `document` evidence ref readable (AND), as Precedents do. A document ref that is not a checkable document drops the row. The ingest fallback `file://{sprk_document id}` is now recognised as a document id. Test: `Observation_WithAReadableAndARestrictedDocumentRef_IsDropped`. |

**Known limits kept:** K1 and K3–K7 as documented by the verifier. **K5 (for the owner):** excerpts that reached chats before this fix (stored chat histories, citation records) are not purged; the trim stops new disclosure only.

**Test runs:**
- Affected AI and contract suites: 1,532 passed, 3 skipped.
- `Spe.Integration.Tests`: SemanticSearch + ToolFramework 104/104.
- ArchTests 929/929.
