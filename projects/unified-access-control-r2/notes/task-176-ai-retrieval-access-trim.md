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
| 1 | `DocumentSearchHandler` SearchDocuments: chat, and playbook via `ExecuteAsync` | chat tool "SYS-Document Search", always offered | **Trimmed**; bound to the host parent when there is one |
| 2 | `DocumentSearchHandler` SearchDiscovery: chat and playbook | chat tool "SYS-Document Discovery" | **Trimmed**; host parent as before; no host → trimmed tenant search |
| 3 | `KnowledgeRetrievalHandler` SearchKnowledgeBase | chat tool "SYS-Knowledge Base Search", playbooks | **Trimmed** |
| 4 | `KnowledgeRetrievalHandler` GetKnowledgeSource | chat tool "SYS-Knowledge Source Retrieval", playbooks | **Trimmed** (a knowledge-source chunk with no document id is dropped; see §7) |
| 5 | `AiAnalysisNodeExecutor.RetrieveDocumentContextAsync` (L2) | playbook nodes with `includeDocumentContext` | **Trimmed** with the run principal; app-only run → none. A parent named in ConfigJson that the caller cannot read now yields nothing. |
| 6 | `SemanticSearchToolHandler` (2 calls, `ISemanticSearchService`) | analysis/playbook tool "Search Documents" | **Trimmed** (2× pool, page cut, counts = rows returned); no caller → `RESULTS_WITHHELD` warning |
| 7 | `DocumentClassifierHandler.GetRagExamplesAsync` | analysis tool "Document Classifier", including app-only document profiling | **Trimmed.** The examples are other documents' text placed in the prompt. App-only profiling has no caller, so it classifies zero-shot. |
| 8 | `AnalysisRagProcessor.ProcessRagKnowledgeAsync` (knowledge sources, after its tenant RAG cache) | HTTP analysis (`AnalysisOrchestrationService.ExecutePlaybookAsync`) | **Trimmed** after the cache, with the HTTP caller. The cache holds untrimmed rows and is never returned as is. With no caller, RAG sources contribute nothing. |
| 9 | `InsightsOrchestrator.SearchAsync`, the only RAG call behind `/api/insights/search`, `/api/insights/assistant/query` and `AssistantToolCallHandler` | user routes (task-163 subject gate) | **Trimmed** with `request.CallerPrincipal`. The subject Read gate does not cover a restricted document under that subject (round 87: a child may be stricter). |
| 10 | `CommunicationTriageAi.RetrieveMatterCorrespondenceGroundingAsync` | unattended communication enrichment; output persisted on the communication record | **Trimmed with NO caller → always withheld** (search not run); triage runs context-free. There is no single reader whose access could bound it: the output is read by every reader of the communication. **Behaviour change**, listed in §8. |
| 11 | `SemanticScopeProvider.GetSemanticContextAsync` | no consumer is wired yet (DI only) | **Trimmed** with `request.CallerPrincipal`; lifetime changed Singleton → Scoped (no singleton resolves it) |
| 12 | `RecallSessionFileHandler` (3 calls) | chat tool "SYS-Recall Session File" | **Exempt, with evidence.** It searches `spaarke-session-files` with `tenantId` + `sessionId = context.ChatSessionId`, which is server-built from the caller's session. Every chat session route carries `AddSessionOwnershipFilter`. The index has two writers, both indexing bytes the session owner uploaded: `ChatDocumentEndpoints.UploadDocumentAsync` (`POST …/sessions/{id}/documents`) and `SessionFileRehydrationService`, which re-indexes that upload's durable copy. The by-reference `…/documents/from-document` ingest is Read-gated (`AddAiAuthorizationFilter`) and does not index (`SearchDocumentIdsCsv = ""`). The rows are not sprk_documents, so the trim could not evaluate them. |
| 13 | `SessionFileTextSource.FetchAsync` | chat summarize of the session's uploaded files | **Exempt**, same evidence as #12 (same index, same session filter, the caller's own uploads) |
| 14 | `RagEndpoints.Search` (`/api/ai/rag/search`) | HTTP | **Already trimmed (task 163)**, left on `IAiAuthorizationService` (§6) |
| 15 | `SemanticSearchEndpoints` (`/api/ai/search`), `RecordSearchEndpoints`, visualization "related" | HTTP | Already per-row checked (out of scope, unchanged) |
| 16 | `FileIndexingService`, `RagIndexingPipeline`, `PostUploadIndexingEnqueuer`, `RelocatedFileIndexing`, `KnowledgeBaseEndpoints` (health counts) | indexing / counts | Do not return document content to a user; no change |
| 17 | `ReferenceRetrievalService` (L1) | playbooks | Not `IRagService`: it reads the shared `spaarke-rag-references` corpus (`tenantId = "system"`, curated reference material, not customer documents). Outside the trim, as the issue says. |

The search covered every `IRagService` injection (`grep -rn "IRagService"`) and every `.SearchAsync(` in `Sprk.Bff.Api`.

## 5. Tests

| File | What |
|---|---|
| `tests/integration/regression/Ai/Issue1511_AiRetrievalAccessTrimTests.cs` (new, KEEP path) | Real trim plus the simulated user-OBO boundary. `SecureDocument_DoesNotReachDocumentSearch` (SearchDocuments and SearchDiscovery), `…KnowledgeRetrieval` (SearchKnowledgeBase and GetKnowledgeSource), `…PlaybookNodeDocumentContext` (through `AiAnalysisNodeExecutor.ExecuteAsync`), `…SemanticSearchTool`; `ReadableDocument_IsReturnedExactlyAsBefore`; `NoCallerIdentity_ReturnsNoRows_SaysSo_AndDoesNotSearch`; `UnattendedPlaybookRun_WithNoRunPrincipal_ReturnsNoRows`; `AFailedOrThrottledAccessCheck_ReturnsNoRows` (429); `HostDropped_DiscoverySearchesTheTenant_ButReturnsOnlyWhatTheCallerCanRead`; `WithAHost_SearchDocumentsIsBoundToTheHostParent`. 12 cases. |
| `tests/unit/.../Services/Ai/PublicContracts/RetrievalAccessTrimTests.cs` (new) | The owner's batch-read rules: ADR-044 canonicalization, chunks of 20 (45 ids → 20/20/5) with order kept, unusable ids dropped, an unasked id in the answer ignored, caller ≠ request principal → nothing and no read, malformed answer → nothing. 6 cases. |
| `tests/unit/.../Insights/InsightsOrchestratorTests.cs` (+1) | `SearchAsync_DocumentTheCallerCannotRead_IsNotReturnedOrSummarized` (caller #9) |
| Shared helpers (new) | `tests/integration/Shared/ReadableDocumentsUserClient.cs` (simulated `IDataverseUserClient`, the documented mock boundary); `PermitAllRetrievalAccessTrim.cs` (for suites that test what callers do with permitted rows) |
| Updated | `DocumentSearchHandlerTests`, `KnowledgeRetrievalHandlerTests`, `DocumentClassifierHandlerTests`, `InsightsOrchestratorTests`, `PredictMatterCostPlaybookTests`, `AnalysisOrchestrationServiceTests`, `SemanticScopeProviderSeamTests`: constructors take the trim (permit-all). `ExecuteChatAsync_SearchDiscovery_DefaultTopK_IsTen` now asserts the 2× pool (page size still 10). |

**Beyond the stated contract (one line each):**
- "unasked id ignored" guards the line that keeps a Dataverse answer from widening the result.
- "malformed answer" covers the `CheckFailed` branch that a non-error status does not reach.
- The Insights test covers caller #9, which a user reaches and which none of the AC tests cover.
- Callers #7, #8, #10 and #11 use the same `SearchReadableAsync` path the AC tests prove, so no per-caller test was added (K3).

## 6. Seeding proof and the task-163 swap

**Seeding proof:** `RetrievalAccessTrim.TrimAsync` was changed to `var kept = rows.ToList();` (trim disabled). Seven tests failed: `SecureDocument_DoesNotReachDocumentSearch` ×2, `…KnowledgeRetrieval` ×2, `…PlaybookNodeDocumentContext`, `…SemanticSearchTool` and `HostDropped_…`. The change was reverted, and all pass.

**The task-163 trim stays as is.** `RagEndpoints.TrimToReadableDocumentsAsync` is not moved onto the seam. Its tests (`RagEndpoints*`, contract and auth suites) drive the real `AiAuthorizationService` through `CallerAccessSeam` (`GetUserAccessAsync`), so a swap to `IDataverseUserClient` would change those tests. Per the owner's condition ("only if mechanical with its tests unchanged"), it is not done. `/api/ai/rag/search` keeps its per-document cost; that is recorded, not fixed.

## 7. Knowledge-source chunks without a document id (escalation trigger 2): count

Read-only, with the dev **query** key, against `spaarke-search-dev` / `spaarke-files-index` (1,347 chunks):

| Filter | Count |
|---|---|
| `knowledgeSourceId ne null and documentId eq null` | **0** |
| `knowledgeSourceId ne null` | 0 |
| `documentId eq null` (any orphan) | 2 (dropped by the trim, as task 163 drops them) |

Trigger 2 was not fired on dev. The reference KB is in `spaarke-rag-references` (`tenantId = "system"`), which no `IRagService` caller can reach. **Before a customer deploy**, repeat the first count per environment: a non-zero count means GetKnowledgeSource or knowledge-source-scoped search for those sources returns nothing after this change.

## 8. Known behaviour changes (for the owner)

1. **Unattended runs get no document results** (fail closed, with a warning logged by the seam). This covers app-only and scheduled playbook runs (`ExecuteAppOnlyAsync`, `PlaybookSchedulerJob`, Service Bus analysis) and app-only document profiling.
   - **Dev survey:** no `sprk_playbooknode` has `includeDocumentContext`, and no playbook node is linked to Document Search, Document Discovery, Knowledge Base Search, Knowledge Source Retrieval, Search Documents or Document Classifier. So no scheduled playbook on dev uses document retrieval today.
   - The Daily Briefing scheduler composite and the notification playbooks use Dataverse queries, not RAG.
2. **Communication triage runs context-free** (caller #10): matter-correspondence grounding is withheld for every email.
3. **Document Classifier RAG examples:** examples under app-only profiling are gone (zero-shot).
4. **Chat search pages:** they may be shorter than topK when the caller can read few of the top 2× candidates. No "partial" flag is added, because the chat text already states the count.

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
