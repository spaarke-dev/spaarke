# Task 176: AI retrieval access trim (#1511), escalation before implementation

Status: **STOPPED on escalation trigger 1 (latency).** No production code is changed. The branch `task/uac-r2-176` holds the POML (in-progress), this note and the #1511 probe test.

## 1. #1511 re-confirmed on master c8a87d818

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/Issue1511ProbeTests.cs`: one chunk of a secure-matter document is returned by `IRagService`, and the chat call (`ExecuteChatAsync`, caller `UserId = caller-without-read`) is checked for the secret text.

| Method | Result on master |
|---|---|
| SearchDocuments | **FAIL**: `result.Data.content` contains the full chunk text |
| SearchDiscovery | **FAIL**: `result.Data.content` contains the 300-char preview |

The issue had no test code attached, so this probe was written from its description. It becomes the regression test once the trim lands.

## 2. Escalation trigger 1 FIRED: the reused check costs about 360 ms per distinct document

Trigger: "STOP … if the reused authorization call cannot check a row without a Dataverse round trip per row that makes chat unusably slow (more than about 2 s added for 20 rows)."

**What the reused call does.** `IAiAuthorizationService.AuthorizeAsync` (the task-163 trim in `RagEndpoints.TrimToReadableDocumentsAsync`) loops over the distinct document ids **sequentially**. For each document, `CachedAccessDataSource` either hits Redis (key per user and document, TTL 60 s) or calls `DataverseAccessDataSource.GetUserAccessAsync`. That method makes four Dataverse Web API round trips per document: a `systemusers` lookup, `RetrievePrincipalAccess`, `teammembership_association` and `systemuserroles_association`. The user, team and role reads are per-user, but they repeat for every document. The checks cannot run in parallel: `DataverseAccessDataSource` sets `_httpClient.DefaultRequestHeaders.Authorization` on a client shared within the request scope (`SemanticSearchEndpoints` documents the same constraint).

**Measured** (dev App Insights `spe-insights-dev-67e2xz`, `dependencies`, target `spaarkedev1.crm.dynamics.com`, last 7 days):

| Call | n | p50 | p90 |
|---|---|---|---|
| `GET systemusers` | 266 | 90 ms | 100 ms |
| `GET systemusers({id})/RetrievePrincipalAccess` | 36 | 93 ms | 144 ms |
| Web API calls overall | 3077 | 92 ms | 115 ms |
| `GET sprk_documents` (filtered reads) | 21 | 303 ms | 371 ms |

**Projected cost of the reused call on a Redis miss** (4 × ~90 ms per distinct document, run one after another):

| Distinct documents in the page | Added latency, p50 | Added latency, p90 |
|---|---|---|
| 5 (SearchDocuments default topK) | ~1.8 s | ~2.4 s |
| 10 (SearchDiscovery default topK) | ~3.6 s | ~4.8 s |
| 20 (MaxTopK) | ~7.2 s | ~9.6 s |
| 3× over-fetch of 20 rows | worse, until a budget stops it | |

A Redis hit costs under 10 ms per document, but the TTL is 60 s, so a new query in a chat usually lands on cold documents. Rows are chunks, so 20 rows can come from fewer than 20 documents. The worst case is still 20 distinct documents, and over-fetching (goal 1) raises the count.

## 3. Options

| | Option | Latency for 20 documents | Same decision as task 163? | New surface | Cost |
|---|---|---|---|---|---|
| **A** | Reuse `AuthorizeAsync` as is. Check documents lazily in rank order and stop when the page is full or a budget of N documents is spent. Rows not examined are dropped and a "partial" note is added (the `SemanticSearchEndpoints` pattern) | about N × 360 ms; N = 5 gives ~1.8 s | Yes, the identical call | Seam only | Recall: a caller who can read few of the top documents gets short or empty pages. Still about 1.8 s per tool call on a Redis miss |
| **B** | ONE caller-scoped (OBO) read per page through the existing `IDataverseUserClient`: `GET sprk_documents?$select=sprk_documentid&$filter=Microsoft.Dynamics.CRM.In(PropertyName='sprk_documentid',PropertyValues=[…])`. Dataverse returns only the rows the caller can Read | one round trip, ~0.1–0.4 s, for the whole over-fetched page | The same Dataverse Read evaluation as RetrievePrincipalAccess (No Access walls are enforced by revoking shares, so native read reflects them). It is not the same code path as task 163 | Seam only; `IDataverseUserClient` already exists and is user-OBO only, fails closed with no user context, and is general BFF infrastructure since task 126 | Two trims in the BFF (163: per-document RPA; 176: batch read) unless task 163's trim is moved onto the same seam, which would also be one call per page there |
| **C** | Make `DataverseAccessDataSource` cache the per-user reads (systemuser, teams, roles) for the request, so a document costs about one RPA round trip | ~20 × 90 ms ≈ 1.8 s | Yes | None, but it changes shared `Spaarke.Dataverse` code that every authorization path uses | Wider blast radius than this task; still over 1 s per 20 cold documents |
| **D** | Run the RPA checks in parallel | — | Yes | — | Blocked by the shared HttpClient header (above); needs a per-check client |

**Recommendation: B**, with goal 2's over-fetch (3× topK, capped) and the fail-closed rules unchanged: no document id drops the row; a fault, a missing caller or no `HttpContext` returns no rows. It is the only option that meets the 2 s bar at 20 documents without giving up recall. It also reuses an existing caller-scoped boundary, and the owner can choose to move task 163's `/api/ai/rag/search` trim onto the same seam so that there is one trim. If the owner wants the task-163 call itself, A with N = 5 is the fallback. It is about 1.8 s worst case per tool call, and short pages are announced.

## 4. Escalation trigger 2 (tenant-wide legitimate corpus): evaluated, not fired

- The reference knowledge base (`spaarke-rag-references`) holds chunks with `tenantId = "system"` and no `documentId`. It is fed only by `scripts/ai-search/Add-ReferenceToIndex.ps1`. `IRagService.SearchAsync` always filters on `tenantId eq '<caller tenant>'` against the tenant's files index, so no `IRagService` caller can reach it. `ReferenceRetrievalService` (L1) reads it directly, not through `IRagService`, and is outside the trim.
- Knowledge-source chunks inside a tenant's files index can come only from `POST /api/ai/rag/enqueue-indexing` (RagApiKey; `/index-file` refuses knowledge-source fields since task 163). No producer of that route exists in the repo (only docs). If a deployment has such chunks WITHOUT a `documentId`, the trim drops them from `KnowledgeRetrievalHandler.GetKnowledgeSource`/`SearchKnowledgeBase` and from `AnalysisRagProcessor` (knowledge-source-scoped). This is the same rule task 163 applied to `/api/ai/rag/search` (its trigger 4). **Live-data check recommended before deploy**: count chunks in `spaarke-files-index` with `knowledgeSourceId ne null and documentId eq null`.

## 5. Planned caller inventory (from code reading; dispositions are proposed, not implemented)

| # | Caller | Reachable from chat/playbook | Proposed disposition |
|---|---|---|---|
| 1 | `DocumentSearchHandler` SearchDocuments (chat + playbook) | yes | trim; bind to host parent when the chat has one |
| 2 | `DocumentSearchHandler` SearchDiscovery (chat + playbook) | yes | trim; host parent as today; no host → trimmed tenant search, never untrimmed |
| 3 | `KnowledgeRetrievalHandler` SearchKnowledgeBase / GetKnowledgeSource | yes | trim |
| 4 | `AiAnalysisNodeExecutor.RetrieveDocumentContextAsync` (L2) | playbook | trim with the run principal; app-only run → no rows |
| 5 | `SemanticSearchToolHandler` (`IAnalysisToolHandler`, `ISemanticSearchService`, 2 calls) | analysis/playbook tool path (not a chat `IToolHandler`) | trim the rows (per-row document check) |
| 6 | `AnalysisRagProcessor` (knowledge sources, analysis path) | playbook legacy mode | trim (see §4) |
| 7 | `RecallSessionFileHandler`, `SessionFileTextSource` | chat | session-files index, `tenantId + sessionId` filter, session ownership owned by the session — **review**; no sprk_document rows |
| 8 | `DocumentClassifierHandler` | playbook | **review** |
| 9 | `InsightsOrchestrator` / `AssistantToolCallHandler` | `/api/insights/*` (task 163 subject gate) | **review**: rows reach the model after a subject Read gate only |
| 10 | `CommunicationTriageAi`, `SemanticScopeProvider` | non-chat | **review** |
| 11 | `RagEndpoints` `/search` | HTTP | already trimmed (task 163) |
| 12 | `SemanticSearchEndpoints`, `RecordSearchEndpoints`, visualization | HTTP | already per-row checked (out of scope) |

Rows 7–10 still need disposition reading. That work waits for the owner's option choice, because the seam's shape decides how each one is wired.
