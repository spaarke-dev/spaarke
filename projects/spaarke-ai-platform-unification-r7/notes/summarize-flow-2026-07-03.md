# Chat Summarize — Successful Flow

> **Date**: 2026-07-03 (final state after Wave 12.3 Phase 12.3a UAT closure).
>
> **Scope**: end-to-end review of the file-summarize path from client upload
> through structured output rendering, as it runs in production `spaarke-bff-dev`
> right now. Built from direct code trace + App Insights KQL evidence + curl-driven
> API test (session `9d466fd406b54e5d8777642849cd90f3` produced a valid
> `DocumentAnalysisResult` with populated `tldr`, `summary`, `keywords`, `entities`
> for `testdoc.txt` on 2026-07-03 ~22:31 UTC).
>
> **What "successful" means here**: user uploads a file, types "summarize this
> document" (or `/summarize`), Summary tab installs in the workspace, and the
> TL;DR / Summary / Keywords / Entities sections render populated content.
> Independently verified in the browser AND via curl.

---

## 1. Synopsis

Chat summarize is a **two-endpoint dance** driven by an intermediary SSE event:

1. **`POST /messages`** carries the user's turn. The server does a whole-word
   keyword match on the message text (`\b(summarize|summary|summarise)\b`,
   case-insensitive). On match with attachments present, the server emits a
   `linear_dispatch` SSE event that includes a pre-composed
   `dispatchUrl = "/api/ai/chat/sessions/{id}/summarize"` and `requestBody =
   {"fileIds":[...]}`, then closes with `done`. **No LLM call is made on this
   request** — the server's role is purely intent-recognition.
2. **`POST /summarize`** is the actual work. The client, on receiving
   `linear_dispatch`, immediately POSTs to the dispatched URL with the pre-composed
   body. The server reads the file's already-extracted text INLINE from the
   session-state record (no Azure AI Search hop), calls Azure OpenAI structured
   completion against the chat-summarize consumer's Action schema, and returns
   ONE `complete` SSE chunk carrying the full `DocumentAnalysisResult` payload.
3. **Client renders**. The client's SSE bridge synthesizes one
   `workspace.field_delta` event per top-level property of the result
   (`tldr` / `summary` / `keywords` / `entities`), then emits
   `workspace.streaming_complete`. The `StructuredOutputStreamWidget` mounted
   into the workspace's Summary tab receives the deltas over the `PaneEventBus`
   and populates its sections.

Text extraction lives ENTIRELY on the upload path — never at summarize time. The
Azure AI Search `spaarke-session-files` index is populated in parallel during
upload (for future recall / retrieval tools) but is NOT read during the
summarize flow at all after 2026-07-03 today.

---

## 2. Component Model

### 2.1 Client — SpaarkeAi code page (React 19, Vite bundle)

| Component | File | Role in summarize |
|---|---|---|
| `SprkChat` (shared lib) | `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | Chat UI shell; owns the SSE stream via `useSseStream`. Now honors `initialSessionId` and calls `resumeSession(id)` instead of always POSTing a fresh session. Emits `onSessionStale(id)` when a resumed id is 404 on the server. |
| `useChatSession` (shared lib) | `.../hooks/useChatSession.ts` | Session lifecycle. `createSession()` (POST `/sessions`), `resumeSession(id)` (state seed only, new), `loadHistory()` (returns `{ok, staleSession?}`, new). |
| `useChatFileAttachment` (shared lib) | `.../hooks/useChatFileAttachment.ts` | Client-side text extraction for PDF (pdfjs-dist) / DOCX (mammoth) / TXT/MD (native). Sets chip `status = 'ready'` with `textContent`. Does NOT POST to server. |
| `useSseStream` (shared lib) | `src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` | Parses SSE stream; dispatches by event type via callback refs. New: `onLinearDispatch` callback ref for the `linear_dispatch` event. |
| `AiSessionProvider` (shared lib) | `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | Cross-widget session state (`chatSessionId` in localStorage). New: `clearChatSession()` for stale-session cleanup. |
| `ConversationPane` (SpaarkeAi) | `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | Host that wraps `SprkChat`. **New**: auto-promotion `useEffect` that POSTs any `ready` un-promoted chip to `/documents`. **New**: `handleLinearDispatch` that fires `executeLinearDispatch`. `handleSessionStale` calls `clearChatSession()`. |
| `executeLinearDispatch` (SpaarkeAi) | `src/solutions/SpaarkeAi/src/components/conversation/executeLinearDispatch.ts` | Companion helper: emits `workspace.widget_load` for Summary tab, POSTs to `dispatchUrl` with `requestBody`, consumes SSE via `sseToPaneEventBridge`. |
| `sseToPaneEventBridge` (SpaarkeAi) | `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts` | Translates `AnalysisChunk` events to `PaneEventBus` workspace events. **New**: `complete` case now synthesizes `field_delta` per top-level `chunk.result` property BEFORE emitting `streaming_complete`. |
| `PaneEventBus` (`@spaarke/ai-widgets`) | `src/client/shared/Spaarke.AI.Widgets/src/events/*` | Type-safe pub/sub across the workspace / context / source / output channels. Closed at 4 channels per ADR-030. |
| `WorkspacePane` (SpaarkeAi) | `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` | Subscribes to `workspace` channel; on `widget_load` installs a new tab via `WorkspaceTabManager.addTab` (auto-activates). |
| `StructuredOutputStreamWidget` (`@spaarke/ai-widgets`) | shared library widget | Mounted into the Summary tab. Subscribes to `field_delta` events per `correlationId`, accumulates per-fieldPath content, and renders TL;DR / Summary / Keywords / Entities sections against `SUMMARIZE_SCHEMA`. |

### 2.2 Server — BFF (`Sprk.Bff.Api`, .NET 8 Minimal API)

| Component | File | Role in summarize |
|---|---|---|
| `ChatDocumentEndpoints` | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs:151` | `POST /sessions/{id}/documents`. Extracts text via Document Intelligence, indexes chunks to Azure AI Search, **and now (2026-07-03) persists the extracted text inline on `ChatSessionFile.ExtractedText`** so summarize can read it without hitting Search. |
| `ChatEndpoints.SendMessageAsync` | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:340` | `POST /sessions/{id}/messages`. Runs the dispatch decision tree. Now has the **linear_dispatch fast-path** (lines 633-724) that pre-empts the LLM when the message matches a summarize keyword and files are present. |
| `TryDetectExplicitConsumerType` | `ChatEndpoints.cs:2549` | Whole-word case-insensitive regex `\b(summarize|summary|summarise)\b`. Returns `"chat-summarize"` on match. Data-driven vocabulary in Phase 12.4 (via `sprk_intenttriggers`). |
| `SessionSummarizeOrchestrator` | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs:174` | `POST /sessions/{id}/summarize`. Reads session state; checks `IConsumerRoutingService.ResolveActionAsync("chat-summarize")` for a Linear `sprk_action`. If populated → `ExecuteLinearAsync` (line 440). Fall-through → Playbook Engine path. |
| `SessionFileTextSource` | `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/SessionFileTextSource.cs` | Resolves file text. **New**: reads `ChatSessionFile.ExtractedText` inline if all target files have it (no Search hop). Falls back to Azure AI Search RAG when any file lacks inline text. |
| `FileSummarizeService` | `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/FileSummarizeService.cs` | The Linear consumer's execution unit. Loads Action config from Dataverse, builds prompt, calls `IOpenAiClient.GetStructuredCompletionRawAsync`, yields one `AnalysisStreamChunk.Result` chunk with the entire JSON output. |
| `IConsumerRoutingService.ResolveActionAsync` | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs` | Queries `sprk_playbookconsumer` where `consumertype='chat-summarize'`; returns the `sprk_action` GUID. Cached 5 min per ADR-014. |
| `TranslateStreamChunkToChunk` | `SessionSummarizeOrchestrator.cs:543` | Adapts `AnalysisStreamChunk` (Linear consumer envelope) → `AnalysisChunk` (chat-endpoint wire contract). `"result"` chunk with JSON becomes `AnalysisChunk.Completed(DocumentAnalysisResult)`. |
| `IOpenAiClient.GetStructuredCompletionRawAsync` | `src/server/api/Sprk.Bff.Api/Infrastructure/OpenAi/*` | Wrapper around Azure OpenAI. Non-streaming structured-outputs mode against the Action's `sprk_outputschemajson`. |
| `IDocumentTextExtractor` | Document Intelligence wrapper | Called during upload; NOT summarize. |
| `RagIndexingPipeline` | `src/server/api/Sprk.Bff.Api/Services/Ai/Rag/RagIndexingPipeline.cs` | Called during upload; NOT summarize. Chunks + embeds + Azure AI Search UPSERT for future recall/query use. |
| `ChatSessionManager` | `.../Services/Ai/Chat/ChatSessionManager.cs` | Redis-first session cache (24 h sliding TTL). Cosmos write-through drops `UploadedFiles` per the aggressive cleanup-on-session-end contract. |

### 2.3 Azure services

| Service | Role in summarize | Used during |
|---|---|---|
| **Azure Document Intelligence** | Extract text from PDF / DOCX / TXT / MD | Upload only |
| **Azure OpenAI** — text-embedding-3-large | Chunk embeddings | Upload only |
| **Azure OpenAI** — gpt-4o-mini (structured completions) | Summarize the extracted text | Summarize only |
| **Azure AI Search `spaarke-session-files`** | Persistent chunk index (partitioned by tenantId + sessionId) | Upload only. Read only if `ExtractedText` inline is missing (fallback). |
| **Azure Cache for Redis** — `spaarke-bff-redis-dev` | Session state, upload-text cache (4 h), upload-binary cache (4 h) | Both |
| **Azure Cosmos DB** — `sessions` container | Warm-tier session persistence (drops UploadedFiles by design) | Both (background write-through) |
| **Application Insights** — `sprkspaarkedev-aif-insights` | Traces + metrics + Redis / Search / Dataverse dependency spans | Both |
| **Dataverse** (`spaarkedev1`) | Reads: `sprk_playbookconsumer` routing row, `sprk_analysisaction` for prompt + schema. Writes: message history (`sprk_aichatmessage`) | Both |

---

## 3. Interactions (end-to-end sequence)

### 3.1 Phase A — File upload (chip → server session state)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER                                                                     │
│                                                                              │
│   [1] user drops "Engagement Letter.docx" onto SprkChatUploadZone            │
│         └─ useChatFileAttachment: PDF/DOCX text extraction (client-side)     │
│              status: 'extracting' → 'ready' + textContent stored             │
│              onAttachmentReady(chip) fired                                   │
│                                                                              │
│   [2] ConversationPane.handleAttachmentReady                                 │
│         └─ captures the original File in heldFilesRef by filename            │
│                                                                              │
│   [3] ConversationPane.useEffect (auto-promote, NEW 2026-07-03)              │
│         └─ trigger: chatSessionId available + chip.status='ready'            │
│                    + heldFilesRef.get(filename)                              │
│                    + not in promotedChipIds + not in pendingPromotionIdsRef  │
│         └─ pendingPromotionIdsRef.add(chip.id)                               │
│         └─ POST /api/ai/chat/sessions/{sessionId}/documents                  │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ multipart/form-data
                                    │ Authorization: Bearer {jwt}
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BFF — ChatDocumentEndpoints.UploadDocumentAsync                             │
│                                                                              │
│   [4] validate tenant + session + size + MIME                                │
│   [5] Azure Document Intelligence → extractionResult.Text (2223 chars)       │
│   [6] Redis SET `tenant:X:doc-upload-text:*`   (4 h TTL)                     │
│   [7] Redis SET `tenant:X:doc-upload-binary:*` (4 h TTL)                     │
│   [8] RagIndexingPipeline.IndexSessionFileAsync                              │
│         ├─ chunk (2048 chars, 200 overlap) → 3 chunks                        │
│         ├─ Azure OpenAI text-embedding-3-large (3072 dims × 3)               │
│         └─ Azure AI Search UPSERT `spaarke-session-files`                    │
│                partition keys: tenantId + sessionId                          │
│                chunk id pattern: {documentId}_s_{index}                      │
│   [9] build ChatSessionFile record (NEW 2026-07-03: includes ExtractedText)  │
│  [10] session with { UploadedFiles = old.Append(newFile) }                   │
│  [11] ChatSessionManager.UpdateSessionCacheAsync                             │
│         ├─ Redis SET `spaarke:tenant:X:session:Y:v1` (24 h sliding TTL)      │
│         │      (full session INCLUDING UploadedFiles + ExtractedText)        │
│         └─ FIRE-AND-FORGET Cosmos PersistSessionAsync                        │
│                (mapping DROPS UploadedFiles per design contract)             │
│  [12] Return 202 { documentId, status:"ready", tokenEstimate, ... }          │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ 202 Accepted
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER — ConversationPane                                                  │
│                                                                              │
│  [13] setPromotedChipIds(new Set([...prev, chip.id]))                        │
│  [14] pendingPromotionIdsRef.delete(chip.id)                                 │
│  [15] chip remains 'ready' visually; file is now server-session-known        │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Phase B — Message send + dispatch

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER — user types "summarize this document" + hits Enter                 │
│                                                                              │
│  [1] SprkChat.handleSend                                                     │
│        body = { message, documentId, attachments: chatAttachments            │
│                  .filter(c => c.status === 'ready')                          │
│                  .map(c => ({filename, contentType, textContent})) }         │
│        onBeforeSendMessage(text)   ← informational hook                      │
│        onDecorateOutboundBody(body) ← SoftSlashRouter (may add intentHint)   │
│        useSseStream.startStream(url, body, getAccessToken)                   │
│                                                                              │
│  [2] POST /api/ai/chat/sessions/{sessionId}/messages                         │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ application/json
                                    │ text/event-stream response
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BFF — ChatEndpoints.SendMessageAsync                                        │
│                                                                              │
│  [3] tenant + session load                                                   │
│        ChatSessionManager.GetSessionAsync (Redis HIT ⇒ 24h TTL refresh)      │
│        session.UploadedFiles.Count == 1 ✓                                    │
│                                                                              │
│  [4] Cross-Matter Conversation Safety check (no pivot ⇒ skip)                │
│                                                                              │
│  [5] SprkChatAgentFactory.CreateAgentAsync                                   │
│        ├─ resolve persona (SYS-DEFAULT)                                      │
│        ├─ append Session Files manifest to system prompt (fileCount=1)       │
│        ├─ init PlaybookService + ToolHandlerRegistry                         │
│        └─ agent ready (~500-800ms; tool metadata + Dataverse fetches)        │
│                                                                              │
│  [6] agent.DetectToolCallsAsync ⇒ (pre-inspection LLM call)                  │
│  [7] CompoundIntentDetector.IsCompoundIntent(toolCalls) ⇒ false (skip)       │
│                                                                              │
│  [8] Build PlaybookDispatcher (agentFactory)                                 │
│                                                                              │
│  [9] IF request.Attachments.Count > 0:                                       │
│                                                                              │
│      ┌─ NEW 2026-07-03: R7 Wave 12.3 explicit-keyword auto-dispatch ─┐       │
│      │                                                                       │
│      │  var explicitConsumerType = TryDetectExplicitConsumerType(msg)        │
│      │      regex \b(summarize|summary|summarise)\b case-insensitive         │
│      │      ⇒ "chat-summarize"                                                │
│      │                                                                       │
│      │  build sessionAttachmentIdsForDispatch =                              │
│      │      map request.Attachments[i] ↔ session.UploadedFiles[i].FileId    │
│      │      count == 1 ✓                                                     │
│      │                                                                       │
│      │  dispatchUrl = "/api/ai/chat/sessions/{sessionId}/summarize"           │
│      │                                                                       │
│      │  emit SSE:                                                            │
│      │    data: {"type":"linear_dispatch","content":null,"data":{            │
│      │      "consumerType":"chat-summarize",                                 │
│      │      "dispatchUrl":"/api/ai/chat/sessions/{sid}/summarize",           │
│      │      "requestBody":"{\"fileIds\":[\"{fileId}\"]}",                    │
│      │      "reason":"explicit-keyword-match:chat-summarize",                │
│      │      "sessionAttachmentIds":["{fileId}"]}}                            │
│      │                                                                       │
│      │  emit SSE: {"type":"done","content":null,"data":null}                 │
│      │  persist user message to session history                              │
│      │  RETURN (no LLM call was made on this request)                        │
│      └───────────────────────────────────────────────────────────────┘       │
│                                                                              │
│      // fall-through (would run Phase B vector match + playbook_options)     │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ SSE frames flow back
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER — useSseStream in SprkChat                                          │
│                                                                              │
│ [10] parseSseEvent for each frame                                            │
│      processEvent branch: type === 'linear_dispatch'                         │
│      handlers.onLinearDispatch(payload)                                      │
│           └─ callback ref → SprkChat effect → ConversationPane prop          │
│                                                                              │
│ [11] ConversationPane.handleLinearDispatch(payload)                          │
│      guard: if payload.sessionAttachmentIds.length === 0 ⇒ drop              │
│      inject "I'll summarize that file for you." (chat interstitial)          │
│      void executeLinearDispatch(payload)                                     │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 3.3 Phase C — Summarize + render

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER — executeLinearDispatch                                             │
│                                                                              │
│  [1] widget config for consumerType 'chat-summarize' ⇒ {                     │
│         widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,                    │
│         schema:      SUMMARIZE_SCHEMA,                                       │
│         outputSchema:SUM_CHAT_OUTPUT_SCHEMA,                                 │
│         defaultDisplayName: "Summary" }                                      │
│                                                                              │
│  [2] streamId = generateStreamId('chat-summarize')                           │
│      widgetData = { mode:'streaming', schema, outputSchema,                  │
│                     correlationId: streamId, title, fileIds }                │
│                                                                              │
│  [3] dispatch('workspace', {                                                 │
│         type: 'widget_load',                                                 │
│         widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,                    │
│         widgetData, displayName: "Summary" })                                │
│                                                                              │
│  [4] fresh token = await getAccessToken()                                    │
│  [5] fetch(bffBaseUrl + dispatchUrl, {                                       │
│         method:'POST', body: payload.requestBody })                          │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ POST /summarize
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BFF — SessionSummarizeOrchestrator.SummarizeSessionFilesAsync               │
│                                                                              │
│  [6] session load (Redis HIT)                                                │
│  [7] resolvedFileIds = request.FileIds (client-supplied)                     │
│  [8] linearActionId = ConsumerRoutingService                                 │
│         .ResolveActionAsync("chat-summarize", cache 5 min)                   │
│         ⇒ Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4")                 │
│                                                                              │
│  [9] ExecuteLinearAsync (line 440):                                          │
│      ┌─ SessionFileTextSource.FetchAsync ────────────────────────────┐       │
│      │  all target files have ExtractedText inline ⇒                 │       │
│      │    return { ExtractedText: concat, DisplayName, ChunkCount:0 }│       │
│      │  (NO Azure AI Search call — 2026-07-03 fix)                   │       │
│      └──────────────────────────────────────────────────────────────┘       │
│                                                                              │
│      ┌─ FileSummarizeService.ExecuteAsync ──────────────────────────┐       │
│      │  yield Progress("resolving_action")                            │       │
│      │  TryResolveActionAsync: Dataverse GET sprk_analysisactions    │       │
│      │      ⇒ "Summarize Document for Chat" with SystemPrompt +      │       │
│      │        strict OutputSchema (SUM-CHAT@v1: tldr, summary,       │       │
│      │        keywords, entities)                                    │       │
│      │  yield Progress("calling_llm")                                 │       │
│      │  IOpenAiClient.GetStructuredCompletionRawAsync                 │       │
│      │      Model=gpt-4o-mini  Schema=Summarize_Document_for_Chat     │       │
│      │      promptLen=4855                                            │       │
│      │  ⇒ ResponseLength=1420 (JSON)                                  │       │
│      │  yield Result(aiOutput.GetRawText())                           │       │
│      └──────────────────────────────────────────────────────────────┘       │
│                                                                              │
│ [10] TranslateStreamChunkToChunk ("result" ⇒ DeserializeResultChunk):        │
│      JsonSerializer.Deserialize<DocumentAnalysisResult>(jsonContent)         │
│      ⇒ AnalysisChunk.Completed(doc) with all fields populated                │
│                                                                              │
│ [11] emit SSE:                                                               │
│      data: {"type":"complete","content":"","done":true,                      │
│              "summary":"...",                                                │
│              "result":{"summary":"...",                                      │
│                        "tldr":["...","...","..."],                           │
│                        "keywords":"...",                                     │
│                        "entities":{"organizations":[...],...},               │
│                        "parsedSuccessfully":true},                           │
│              "error":null}                                                   │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ SSE frames
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER — executeLinearDispatch stream loop                                 │
│                                                                              │
│ [12] parse SSE, each "data: {...}" line ⇒ AnalysisChunk                      │
│                                                                              │
│ [13] sseToPaneEventBridge.consume(chunk) for the 'complete' chunk:           │
│      ┌─ NEW 2026-07-03: field_delta synthesis ────────────────────┐          │
│      │  if !started: emit workspace.streaming_started              │          │
│      │  for each [key,value] of chunk.result:                      │          │
│      │    skip parsedSuccessfully, rawResponse, null/undefined     │          │
│      │    content = typeof value==='string' ? value                │          │
│      │            : JSON.stringify(value)                          │          │
│      │    events.push({ type:'field_delta', streamId,              │          │
│      │                  fieldPath:key, fieldContent:content,       │          │
│      │                  sequence: seq++ })                         │          │
│      └──────────────────────────────────────────────────────────────┘          │
│      then push workspace.streaming_complete                                  │
│                                                                              │
│ [14] dispatch('workspace', ...) each event onto PaneEventBus                 │
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ pub/sub via PaneEventBus
                                    ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  BROWSER — StructuredOutputStreamWidget (in the Summary tab)                 │
│                                                                              │
│ [15] on 'streaming_started' (correlationId === widget's own): reset state    │
│ [16] on 'field_delta' (matching correlationId + registered fieldPath):       │
│         accumulate content per fieldPath, render section                     │
│      Widget receives:                                                        │
│        tldr     ⇒ '["Engagement letter dated...","Legal...","Fees..."]'      │
│        summary  ⇒ 'The document titled testdoc.txt is an engagement...'      │
│        keywords ⇒ 'Acme Corporation, Jane Smith, Smith Legal Group, ...'     │
│        entities ⇒ '{"organizations":["Acme..."],"people":[],...}'            │
│      Widget parses arrays / objects from string per SUMMARIZE_SCHEMA         │
│      TL;DR bullets render, Summary paragraph renders, Keywords chips render, │
│      Entities → Organizations list renders.                                  │
│ [17] on 'streaming_complete': widget toggles "Complete" pill (green)         │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. What each fix on 2026-07-03 contributed

| Commit | Component | Concrete gap it closed |
|---|---|---|
| `139014adc` | `ChatEndpoints.cs` | server-side keyword-match + `linear_dispatch` SSE emit — the whole triangulation feature |
| `7f0e42b30` | `useSseStream.ts`, `SprkChat.tsx`, `ConversationPane.tsx`, `executeLinearDispatch.ts` | client wiring for the `linear_dispatch` event; the initial companion helper |
| `a9bdd2f88` | `ConversationPane.tsx` | retired the old NL `executeSummarizeIntent` branch (avoids double-dispatch race) |
| `5ab21578b` | `ChatSession.cs`, `ChatDocumentEndpoints.cs`, `SessionFileTextSource.cs` | persist `ChatSessionFile.ExtractedText` at upload; `SessionFileTextSource` reads it inline first → **no RAG-catchup race** |
| `ab8ab68a8` | `useChatSession.ts`, `SprkChat.tsx`, `AiSessionProvider.tsx`, `ConversationPane.tsx` | resume, don't recreate persisted chat sessions → **single session id** for uploads and messages |
| `68e8b96f1` | `ConversationPane.tsx` (new useEffect) | auto-promote `ready` chips to `/documents` → the server session actually gets the file (regression when `executeSummarizeIntent` was retired) |
| `2d4e0c8d8` | `sseToPaneEventBridge.ts` | synthesize `field_delta` events from the terminal `complete` chunk's `result` payload → widget populates instead of showing blank template |

---

## 5. Observability — where to look when it fails again

R1 + R2 remediation wired substantial observability. Concrete KQL queries for
future triage (App Insights AppId `6a76b012-46d9-412f-b4ab-4905658a9559`):

```kql
// A. Was the linear_dispatch check reached and what did it decide?
traces
| where timestamp > ago(30m)
| where message startswith "Wave 12.3 keyword-check"
| project timestamp, message

// B. Did the summarize call complete? What was the LLM response length?
traces
| where timestamp > ago(30m)
| where message contains "Structured raw completion finished"
     or message contains "SessionFileTextSource: read"
     or message contains "[SUMMARIZE-SESSION] Stream complete"
| project timestamp, message
| order by timestamp asc

// C. Was the RAG hop taken or the inline text used?
traces
| where timestamp > ago(30m)
| where message startswith "SessionFileTextSource:"

// D. Any Redis cache failures?
customMetrics
| where timestamp > ago(30m)
| where name == "cache.failures"
| summarize count() by tostring(customDimensions.outcome), bin(timestamp, 1m)

// E. What request URLs hit /messages / /summarize / /documents and with what result?
requests
| where timestamp > ago(30m)
| where url contains "/documents" or url contains "/messages" or url contains "/summarize"
| project timestamp, url=tostring(split(url,"?")[0]), resultCode, duration, operation_Id
| order by timestamp desc

// F. End-to-end trace for one summarize turn (get operation_Id from E)
traces
| where timestamp > ago(30m)
| where operation_Id == "<op-id>"
| project timestamp, severityLevel, message
| order by timestamp asc
```

## 6. Curl-driven repro (bypasses browser cache)

```bash
TOKEN=$(az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" --query "accessToken" -o tsv)
TID=a221a95e-6abc-4434-aecc-e48338a1b2f2

# 1) create session
SID=$(curl -sS -X POST \
    -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TID" \
    -H "Content-Type: application/json" \
    -d '{"documentId":null,"playbookId":null,"hostContext":null}' \
    https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions \
    | python -c "import sys,json;print(json.load(sys.stdin)['sessionId'])")

# 2) upload a text file (see /c/tmp/testdoc.txt)
UP=$(curl -sS -X POST \
    -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TID" \
    -F "file=@c:/tmp/testdoc.txt;type=text/plain" \
    "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/$SID/documents")
FID=$(echo "$UP" | python -c "import sys,json;print(json.load(sys.stdin)['documentId'])")

# 3) POST /messages — server emits linear_dispatch
TEXT=$(cat /tmp/testdoc.txt | tr '\n' ' ')
BODY=$(printf '{"message":"summarize this document","attachments":[{"filename":"testdoc.txt","contentType":"text/plain","textContent":%s}]}' \
    "$(printf '%s' "$TEXT" | python -c 'import json,sys;print(json.dumps(sys.stdin.read()))')")

curl -sN -X POST \
    -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TID" \
    -H "Content-Type: application/json" --data "$BODY" \
    "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/$SID/messages"

# → data: {"type":"linear_dispatch",...}
# → data: {"type":"done",...}

# 4) POST /summarize — server returns complete chunk with full DocumentAnalysisResult
curl -sN -X POST \
    -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TID" \
    -H "Content-Type: application/json" --data "{\"fileIds\":[\"$FID\"]}" \
    "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/$SID/summarize"

# → data: {"type":"complete","content":"","done":true,
#          "summary":"...","result":{"summary":"...","tldr":[...],
#                                    "keywords":"...","entities":{...},
#                                    "parsedSuccessfully":true},"error":null}
```

Verified passing 2026-07-03 22:31 UTC.

---

## 7. Known adjacent behavior (for the record)

- **Follow-on question path** (e.g. "who is the letter addressed to") does NOT
  use the linear_dispatch flow — it goes through the standard LLM agent path
  with tool calls. When the agent proposes ≥2 tool calls / a write-back /
  external action, `CompoundIntentDetector.IsCompoundIntent` fires the
  `plan_preview` SSE event and halts execution (spec constraint FR-11).
  Whether that UX is appropriate for read-only recall tools like
  `SYS-Recall_Session_File` is a separate discussion.
- **`/summarize` slash command** and NL "summarize" both funnel through the
  same server-side keyword-match. The client's soft-slash decoration adds an
  `intentHint` field but the linear_dispatch check itself only reads
  `request.Message` — so both entry paths converge.
- **`session.UploadedFiles` intentionally lives ONLY in Redis** per
  `ChatSession.cs:63-66`. On Redis TTL miss + Cosmos fallback, UploadedFiles is
  lost. Design contract from the session-files cleanup story (files are
  aggressively cleaned on session end). This is durable for 24h of active use;
  BFF restarts or extended idle can lose it. When session state is stale,
  `handleSessionStale` clears localStorage and creates fresh, and the
  auto-promote effect re-uploads any chips still visible.
