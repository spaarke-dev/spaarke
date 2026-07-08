# SpaarkeAi Chat + Summarize — Current Flow (2026-07-03)

> **Purpose**: authoritative code+config trace after R7 Wave 12.3 Phase 12.3a shipped.
> Built by reading `src/**` directly + querying live Azure resources. No stale docs,
> no guessing. Baseline for triaging today's UAT regressions.
>
> Rewritten 2026-07-03 (v2) to (a) replace un-rendering Mermaid with ASCII, (b) add the
> actual step-by-step trace of upload → summarize → response with file:line references
> AND resolved live config, (c) surface the observability that R1+R2 remediation already
> wired up so we stop guessing.

---

## 1. Live Azure config (resolved as of 2026-07-03)

Pulled from `az webapp config appsettings list --name spaarke-bff-dev` + KeyVault reads:

| Setting | Resolved Value | Resource state |
|---|---|---|
| `Redis__Enabled` | `true` | — |
| `Redis__InstanceName` | `spaarke:` (key prefix on-wire) | — |
| `Redis__AllowInMemoryFallback` | `false` (BFF fails hard if Redis unreachable) | — |
| `ConnectionStrings__Redis` | `spaarke-bff-redis-dev.redis.cache.windows.net:6380,password=***,ssl=True,abortConnect=False` | Redis instance exists in RG `spe-infrastructure-westus2` (NOT `rg-spaarke-dev` — different RG from the BFF). VNET-scoped, resolves to `20.69.128.177` |
| `AiSearch__Endpoint` | `https://spaarke-search-dev.search.windows.net` | Search service exists in RG `spe-infrastructure-westus2` |
| `AiSearch__AllowedIndexes__5` | `spaarke-session-files` | This is what `SessionFileTextSource` queries |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `09a9beed-...;westus2-2.in.applicationinsights.azure.com` | App Insights active |

**Key infra observation**: Redis + Search live in a shared RG (`spe-infrastructure-westus2`), NOT the BFF's RG (`rg-spaarke-dev`). Azure resource rebuilds you mentioned would have hit that shared RG.

`abortConnect=False` in the connection string means at the STACKEXCHANGE.REDIS layer, missing Redis doesn't hard-fail — but this is contradicted at the DI layer by `AbortOnConnectFail=true` (see `CacheModule.cs:100`). Startup would still fail, so a running BFF (which we have, `/healthz` = 200) proves Redis is reachable.

---

## 2. What tracing/monitoring is ALREADY wired up (no guessing needed)

R1+R2 remediation projects wired extensive OpenTelemetry + App Insights. Live in
`Infrastructure/DI/TelemetryModule.cs:20-59`:

### 2.1 Custom metric Counters (App Insights → `customMetrics` table)

| Meter | Counter / Histogram | Emitted by | What it tells us |
|---|---|---|---|
| `Sprk.Bff.Api.Cache` | `cache.hits{tier=raw}` / `cache.misses{tier=raw}` | `MetricsDistributedCache` (every IDistributedCache op) | Total Redis hits vs misses at the wire |
| `Sprk.Bff.Api.Cache` | `cache.hits.by_resource{resource=session}` | `TenantCache.GetAsync` | **Session-cache hits specifically** — non-zero means Redis returning data |
| `Sprk.Bff.Api.Cache` | `cache.misses.by_resource{resource=session}` | `TenantCache.GetAsync` | **Session-cache misses** — non-zero means session not found in Redis |
| `Sprk.Bff.Api.Cache` | `cache.failures{outcome=connection\|timeout\|serialization}` | `MetricsDistributedCache` (try/catch) | Redis op errors (R2 FR-01) |
| `Sprk.Bff.Api.Cache` | `cache.duration` histogram | `MetricsDistributedCache` | P50/P95 Redis latency |
| `Sprk.Bff.Api.R5SummarizeTelemetry` | `r5.summarize.invocation` | `SessionSummarizeOrchestrator` | **Every summarize call with outcome tag** |
| `Sprk.Bff.Api.R5SummarizeTelemetry` | `r5.session_files.index_size` | Session upload path | Number of chunks indexed per session |

### 2.2 Distributed traces (App Insights → `dependencies` + `requests` tables)

- `AddRedisInstrumentation()` (line 58, R1 task 040) — every StackExchange.Redis SET / GET / EXPIRE becomes a `dependencies` row with `type=Redis`, `target=spaarke-bff-redis-dev.redis.cache.windows.net`, `success`, `duration`.
- `AddSource("Sprk.Bff.Api.R5SummarizeTelemetry")` — every summarize call = an operation with child spans.
- `AddSource("Sprk.Bff.Api.Rag")` — every RAG search = a dependency span on `spaarke-search-dev`.

### 2.3 Health check

`/healthz` writes a test key `_health_check_` to Redis and reads it back. If Redis is dead, `/healthz` fails. Our `/healthz` currently returns 200, meaning Redis roundtrips are working.

### 2.4 KQL queries we can run RIGHT NOW to diagnose

```kql
// Q1: Did the last chat summarize call actually reach the server?
requests
| where timestamp > ago(30m)
| where url contains "/summarize" or url contains "/messages" or url contains "/documents"
| project timestamp, url, resultCode, duration, operation_Id
| order by timestamp desc

// Q2: Are session-cache GETs finding data?
customMetrics
| where timestamp > ago(30m)
| where name in ("cache.hits.by_resource","cache.misses.by_resource")
| where customDimensions.resource == "session"
| summarize count() by name, bin(timestamp, 1m)
| render timechart

// Q3: Are there Redis failures (connection / timeout / serialization)?
customMetrics
| where timestamp > ago(30m)
| where name == "cache.failures"
| summarize count() by tostring(customDimensions.outcome), bin(timestamp, 1m)

// Q4: What happened to the last summarize invocation? Success or fail? Which step?
customMetrics
| where timestamp > ago(30m)
| where name == "r5.summarize.invocation"
| project timestamp, valueSum, customDimensions

// Q5: Are Redis SETs actually persisting? (dependency trace by operation_Id)
dependencies
| where timestamp > ago(30m)
| where type == "Redis"
| where target contains "spaarke-bff-redis-dev"
| summarize count(), avg(duration), sumif(1, success == false) by name, bin(timestamp, 1m)

// Q6: For the failing summarize turn, follow the operation_Id end-to-end
requests
| where timestamp > ago(30m) and url contains "summarize"
| project operation_Id, timestamp, url
| join kind=inner (
    dependencies
    | where timestamp > ago(30m)
    | project operation_Id, timestamp, name, target, type, success, duration
) on operation_Id
| order by timestamp asc
```

**These queries answer "where did it fail" without guessing.** We have not been running them today — every fix has been from static code analysis.

---

## 3. The actual upload → summarize → response flow (step by step)

### Client: file upload

```
User drops "Engagement Letter.docx" onto SprkChatUploadZone
    │
    │  SprkChatUploadZone.tsx:472 — handleDrop
    │  useChatFileAttachment.ts:315 — client-side extract
    │      (PDF: pdfjs-dist / DOCX: mammoth / TXT: File.text())
    │
    ▼
POST /api/ai/chat/sessions/{sessionId}/documents
     multipart/form-data with binary file + Bearer <token>
     — Auth v2 D-AUTH-7: token fetched at XHR.open() time, never snapshotted
```

### Server: upload endpoint  `ChatDocumentEndpoints.UploadDocumentAsync (line 151)`

```
 [1] tenant id from JWT `tid` claim  (line 174)
 [2] session load: sessionManager.GetSessionAsync(tenantId, sessionId)  (line 187)
       ├─ Redis hit (cache.hits.by_resource{resource=session}++)
       ├─ Redis miss → Cosmos fallback → MapStoredSessionToChatSession → LOSES UploadedFiles
       └─ Cosmos miss → Dataverse fallback → LOSES UploadedFiles
 [3] check UploadedFiles cap (≥20 → 400)  (line 201)
 [4] file bytes → memory  (line 299)
 [5] text extraction: ITextExtractor.ExtractAsync
        → Azure Document Intelligence REST call
        → returns TextExtractionResult{ Text, EstimatedTokenCount }
 [6] Redis stores extracted text @ tenant:X:doc-upload-text:*  (line 374, 4h TTL)
 [7] Redis stores binary       @ tenant:X:doc-upload-binary:* (line 399, non-fatal)
 [8] Redis stores metadata     @ tenant:X:doc-upload-meta:*   (line 419)
 [9] RAG indexing pipeline:  RagIndexingPipeline.IndexSessionFileAsync
        → chunks (2048 chars, 200 overlap)
        → Azure OpenAI embeddings
        → Azure AI Search UPSERT into `spaarke-session-files` index
            (partition keys: tenantId + sessionId, chunk id `{documentId}_s_{index}`)
[10] build new ChatSessionFile record incl. SearchDocumentIdsCsv
     AND (2026-07-03 fix, commit 5ab21578b) ExtractedText  (line 530-546)
[11] append newFile → session.UploadedFiles  (line 542-545)
[12] sessionManager.UpdateSessionCacheAsync(updatedSession):
       ├─ CacheSessionAsync → Redis SET  spaarke:tenant:X:session:Y:v1 (24h sliding TTL)
       │       ↑ THIS carries the full session INCLUDING UploadedFiles
       └─ FireAndForgetCosmosPersist → MapChatSessionToStoredSession → *** DROPS UploadedFiles ***
                → Cosmos SET (async, no error surfacing)
[13] emit App Insights context event UploadCompleted
[14] return 202 Accepted DocumentUploadResponse
```

**Where the file information "lives" after this:**

| Storage | UploadedFiles present? | Text present? | Chunks present? |
|---|---|---|---|
| Redis (session key) | ✅ full manifest incl. ExtractedText | ✅ inline on ChatSessionFile.ExtractedText (2026-07-03) | Not here |
| Redis (`doc-upload-text:*`, 4h TTL) | — | ✅ raw text | — |
| Cosmos `sessions` | ❌ mapping drops UploadedFiles per contract | ❌ | — |
| Dataverse `sprk_aichatsummary` | ❌ manifest omitted per contract | ❌ | — |
| Azure AI Search `spaarke-session-files` | — | — | ✅ indexed as chunks, partitioned by session |

---

### Client: user types "summarize this document"

```
SprkChat.handleSend (line 1092)
    │  build body { message, documentId, attachments: [{filename, contentType, textContent}] }
    │      attachments come from client-side chatAttachments (extracted at upload)
    │
    ▼
POST /api/ai/chat/sessions/{sessionId}/messages   (SSE response)
```

### Server: message endpoint  `ChatEndpoints.SendMessageAsync (line 340)`

```
 [1] sessionManager.GetSessionAsync                       (line 367)
       ⚠ if Redis missed & Cosmos hit → session.UploadedFiles = NULL
 [2] validate request.Attachments                        (line 380)
 [3] compose effective message: user text + inline extraction of each attachment.textContent
 [4] DISPATCH DECISION TREE:
     IF request.Attachments.Count > 0:
       ├─ TryDetectExplicitConsumerType(message)  (line 648)
       │      regex \b(summarize|summary|summarise)\b  case-insensitive
       │      → returns "chat-summarize" (or null)
       │
       ├─ IF match → build sessionAttachmentIdsForDispatch by pairing
       │            request.Attachments[i] ↔ session.UploadedFiles[i].FileId
       │      ⚠ IF session.UploadedFiles is empty:
       │           sessionAttachmentIdsForDispatch = []
       │           2026-07-03 GUARD (line 679): skip emit → fall through
       │      ELSE: emit `linear_dispatch` SSE + done + persist user msg + RETURN
       │
       └─ else → dispatcher.RunPhaseBVectorMatchAsync (line 726)
                 build playbook_options payload
                 emit `playbook_options` SSE + done + persist user msg + RETURN
     ELSE:
       normal LLM chat via SprkChatAgentFactory.SendMessageAsync
       (agent tools include RAG, DocumentSearch, KnowledgeRetrieval)
```

### Client: SSE handling of `linear_dispatch`

```
useSseStream parses "linear_dispatch" event, invokes handler ref
   ↓
ConversationPane.handleLinearDispatch (line 1687)
   [1] guard: if payload.sessionAttachmentIds.length === 0 → drop, return (2026-07-03)
   [2] setPendingInjection("I'll summarize that file for you.")
   [3] fire-and-forget: executeLinearDispatch(payload)
```

### Client: `executeLinearDispatch` — the second POST

```
executeLinearDispatch.ts
   [1] emit workspace.widget_load onto PaneEventBus → WorkspacePane installs Summary tab
   [2] POST payload.dispatchUrl (== /api/ai/chat/sessions/{id}/summarize)
       body = payload.requestBody  (already JSON: {"fileIds":["..."],"style":null})
   [3] read SSE response as AnalysisChunk stream
       [3a] each delta → sseToPaneEventBridge → workspace.field_delta events
       [3b] complete → workspace.streaming_complete
       [3c] error chunk → throw → catch handler injects "I couldn't complete that…"
```

### Server: `/summarize`  `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync (line 174)`

```
 [1] sessionManager.GetSessionAsync                       (line 195)
       ⚠ same Redis-first / Cosmos-drops-UploadedFiles risk as above
 [2] resolvedFileIds = request.FileIds OR fall back to session.UploadedFiles
       ⚠ if session.UploadedFiles is empty AND request.FileIds is empty → resolvedFileIds = []
 [3] consumerRouting.ResolveActionAsync(ChatSummarize)  (line 214)
       reads sprk_playbookconsumer.sprk_action from Dataverse
       cached 5min per ADR-014
       returns non-null Guid → Linear path
       returns null → Playbook Engine path
 [4] Linear path: ExecuteLinearAsync (line 440)
       filter uploadedFiles by resolvedFileIds → targetFiles
       ⚠ if targetFiles.Count == 0 → emit AnalysisChunk.FromError(
             "No session files were available to summarize.")
             → yield break
       SessionFileTextSource.FetchAsync:
          ├─ IF all files have inline ExtractedText (2026-07-03 fix):
          │     concat + return (NO Azure AI Search hit)
          └─ ELSE fall through to RAG:
             Azure AI Search query on `spaarke-session-files`
             filter: tenantId + sessionId, allowed chunk ids
             ⚠ empty result → emit "session files contained no text" error
       FileSummarizeService.ExecuteAsync(text, filename, ChatSummarize):
          → Azure OpenAI structured completion via IOpenAiClient
          → stream AnalysisStreamChunk events
       translate to AnalysisChunk envelope (SSE wire contract preserved)
```

---

## 4. Where things can silently fail (with the metric/trace that would surface it)

| Failure | Silent to user? | Metric / trace that would show it |
|---|---|---|
| Redis SET succeeds but Redis GC evicts before next read | ✅ silent | `cache.misses.by_resource{resource=session}` non-zero + `cache.hits.by_resource{resource=session}` zero |
| Redis SET fails silently | ❌ raised by MetricsDistributedCache | `cache.failures{outcome=connection}` non-zero + App Insights dependency error on Redis |
| Cosmos write drops UploadedFiles → next Redis miss loses files | ✅ **silent, by design (`ChatSession.cs:63-66`)** | Not directly. See #1 above. |
| Session read from Cosmos instead of Redis | ✅ silent | Redis dependency trace count / Cosmos dependency trace count |
| `SessionFileTextSource` RAG search returns 0 chunks | ⚠ surfaces as AnalysisChunk.FromError to client | AI Search dependency trace with `count == 0` results + `r5.summarize.invocation{outcome=error}` |
| Consumer routing returns null (falls back to engine) | ✅ silent, goes to engine path | Add custom log; not currently metered separately |
| Two /messages requests for one send (double-fire hypothesis) | ✅ silent to server | `requests | where url contains "messages"` count vs expected 1 per turn |
| BFF pointed at old Redis after rebuild | ✅ silent (writes/reads succeed against wrong instance) | Redis dependency `target` matches expected FQDN |

---

## 5. R7 Wave 12.3 changes summary (post-13.6a, all deployed)

| Commit | File | Change |
|---|---|---|
| 139014adc | `Api/Ai/ChatEndpoints.cs` | server keyword-match `linear_dispatch` bypass + `TryDetectExplicitConsumerType` |
| 7f0e42b30 | shared UI + `ConversationPane.tsx` | client-side `linear_dispatch` SSE wiring + `executeLinearDispatch` helper |
| a9bdd2f88 | `ConversationPane.tsx` | retire client NL `executeSummarizeIntent` branch (avoid double dispatch) |
| 5ab21578b | 4 server files + guards | persist `ChatSessionFile.ExtractedText` at upload; `SessionFileTextSource` reads it inline before RAG fallback; empty-attachment guards on both sides |

Chat-summarize routing row `sprk_playbookconsumer WHERE consumertype='chat-summarize'` had `sprk_action` populated with the target Action GUID — this is what activates the Linear path in `SessionSummarizeOrchestrator`. Clearing that field reverts to the pre-R7 playbook engine path with zero code change.

---

## 6. What we don't know yet (and how to find out — WITHOUT guessing)

1. **Is the double linear_dispatch fire actually two `/messages` calls?**
   Run KQL Q1: filter `requests` for the user's session over the last 5 min. Two rows for the same message = confirmed double-call at server. One row = client-side event doubling in `useSseStream`.

2. **Is the "please upload document" symptom caused by `session.UploadedFiles` being empty at message-read time?**
   Run KQL Q2 + Q5: correlate `cache.hits.by_resource{resource=session}` count vs `dependencies` Redis SET count for the user's session. If SETs happened but subsequent GETs returned misses OR successfully returned an empty `UploadedFiles`, we know exactly where the state was lost.

3. **Is BFF talking to the correct Redis instance?**
   Run KQL Q5 and inspect `dependencies.target`. Should be `spaarke-bff-redis-dev.redis.cache.windows.net`. If it's anything else → old connection string cached.

4. **What is the actual summarize call outcome?**
   Run KQL Q4: `r5.summarize.invocation` last hour should show every call + tagged outcome. If none appear → never reached the endpoint. If tagged `outcome=empty_session_files` → session state issue. If tagged `outcome=rag_empty` → index issue.

---

## 7. Recommendation

**Before ANY more code changes**, run the KQL queries above from the App Insights portal
(instrumentation key `09a9beed-0dcd-4aad-84bb-3696372ed5d1`,
`westus2-2.in.applicationinsights.azure.com`).

That answers definitively:
- Is Redis wired to the right resource? → `dependencies.target`
- Are session state writes happening? → Redis SET count / cache.set metrics
- Are subsequent reads finding them? → `cache.hits.by_resource{resource=session}` vs misses
- What's the summarize call outcome? → `r5.summarize.invocation` counter dimensions
- Is the double-fire a server or client issue? → count of `requests | url ~ messages`

Once those data points are in hand, the fix is targeted, not layered. My best hypothesis right now — given the intentional "UploadedFiles only in Redis" contract + your Redis+Search rebuild + our repeated BFF restarts today evicting Redis — is that stale Redis was the source of R-1 in earlier revisions of this document, but the LATEST fresh test with fresh uploads should have worked. If it did not, the diagnostic queries will pinpoint which stage failed.
