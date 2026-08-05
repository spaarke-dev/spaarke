# As-Built Evidence — R2 Design (traces 2026-08-04)

Source code traces backing [`../design.md`](../design.md). Re-verify at spec intake (code moves).

## 1. Cross-pane context / Assistant tab-awareness (Workstreams A, B, C)

- **PaneEventBus**: `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts:59-158`; types `PaneEventTypes.ts` (workspace channel `:193-953`, discriminants `:300-386`). 4 channels: workspace/context/conversation/safety.
- **`active_widget_changed` producer (live, unconsumed by Assistant)**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx:540-568` (`broadcastActiveTabChange`) — comment: "NO consumers wired… signal infrastructure only."
- **Only consumers today**: `components/shell/ReviewCompleteToast.tsx:138-141` (is-Compose-active); `Spaarke.Compose.Components/.../ComposeWorkspace.tsx:2726-2727` (re-register active doc).
- **Assistant subscriptions (do NOT read active tab)**: `ConversationPane.tsx:2265` (conversation session_switch/open_quick_start), `:2297` (workspace widget_load compose seed only); `useSelectionChip.ts:52`.
- **Chat request body seam**: `SprkChat.tsx:1456-1487` — base `{message, documentId}`, optional `attachments`, host `onDecorateOutboundBody(body)` at `:1470-1485`. SpaarkeAi wires `onDecorateOutboundBody` at `ConversationPane.tsx:2682` but passes **no** documentId. **This is the focus-stamp injection point (A).**
- **Pillar 9 visible-to-assistant (server)**: `Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — `BuildWorkspaceStateBlock:1454` (filters `VisibleToAssistant` && derivable state), `TryDeriveVisibleState:1547` (closed union → Summary/DocumentViewer/Dashboard/Table), `FormatVisibleStateFields:1679`. Active tab labeled by most-recent `UpdatedAt` (`:1440-1441`) — **the wrong signal A replaces.**
- **Visible-state contract (client)**: `Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts:127` (`getVisibleState?` — OPTIONAL, FR-56 not retrofitted). Models: `Sprk.Bff.Api/Models/Workspace/WorkspaceTabVisibleState.cs`, `WorkspaceTab.cs`.
- **Email widget gap**: `getAgentVisibleState` NOT implemented in `Spaarke.Communication.Components` (grep: 0 hits). SpaarkeAi "Email" = stub `src/solutions/SpaarkeAi/src/components/workspace/EmailStubWidget.tsx:88-136` ("coming soon"); real `CommunicationsWorkspaceWidget` lives in `Spaarke.Communication.Components` (email-r5), not wired into SpaarkeAi WorkspacePane on master.
- **Privacy**: ADR-015 (deterministic-metadata-only); `selectionText` capped 200 chars.

## 2. Session model (Workstream A, D reframe)

- **ONE session, tabs live under it — not per-tab.** `AiSessionProvider.tsx:451` single `chatSessionId`; localStorage key `sprk_ai2_chatSessionId[__{entityType}:{entityId}]` (`:98-105`) — scoped per host-record, not per tab. Single `<ConversationPane>`/`<SprkChat key=…>` (`ThreePaneShell.tsx:860`, `ConversationPane.tsx:2624`).
- **sessionId minted lazily**: `useChatSession.ts:71-80` (`POST /api/ai/chat/sessions`).
- **`session_switch`** = adopt existing session + reload transcript. Subscribed `ConversationPane.tsx:2265` → `handleSelectHistorySession` → `setChatSessionId` + `startNewSession`. Dispatched only by analysis reopen `WorkspacePane.tsx:975-978` / hub grid. **Tab clicks do NOT dispatch it** (only `tab_change`/`tab_count_change`).
- **Tabs are session-scoped (durable)**: `IWorkspaceStateService.GetTabsAsync(tenantId, sessionId)`; Redis key `tenant:{t}:workspace-state:{sessionId}:v1`; `PATCH/GET /api/ai/chat/sessions/{id}/tabs` (`WorkspacePane.tsx:453,640`). Restore effect depends on `chatSessionId` (`:619-705`) → session change re-fetches tabs. Per-entity localStorage fallback `:158-187`.
- **Chat NOT tagged per-tab** — association session-level only. (No `tabId`/`activeTab` on chat message models.)

## 3. History UX (Workstream D)

- **Menu**: `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` (`HistoryMenu`). Trigger icon `HistoryRegular:559-572`. Fetch `GET /api/ai/chat/sessions?limit=50` (`:366-369`). `mapSession:291-301` reads only `{sessionId,title,lastMessageAt}` — **drops summary + message count.** Rows `:609-644`.
- **Load path WIRED (R5-5 fix)**: row `onClick handleSelect` (`:617`) → `onSelectSession` → `ConversationPane.tsx:2249-2255` (`setChatSessionId`+`startNewSession`) → remount (`sprkChatRemountKey` `useCommandRouting.ts:247-249`) → `SprkChat.tsx:1020-1065` (`resumeSession`+`loadHistory`) → `useChatSession.ts:150-189` (`GET …/history`, `setMessages`).
- **ROOT CAUSE of "does nothing" = data, not wiring**: transcript + title both derive from persisted `messages`. `BuildSessionTitle` (`SessionPersistenceService.cs:811-821`): seed `FirstMessage`(=Cosmos `messages[0].content`, `:762-765,854-855`)→`ConversationSummary`→`entity.DisplayName`→ fallback `"Conversation · {ts} UTC"`. **Timestamp title ⇔ empty `messages[0]` ⇔ empty transcript on load.** `ConversationSummary` = placeholder, gated 15 msgs (`ChatHistoryManager.cs:194-210`).
- **Secondary bug — 200-empty vs 404**: `GetHistoryAsync` returns empty array (`ChatHistoryManager.cs:172-179`) wrapped `Results.Ok` (`ChatEndpoints.cs:1164`); client recovery only on 404 (`useChatSession.ts:161-163`) → silent blank pane.
- **Up-arrow = "Promote to Analysis"** (not load): `HistoryOverlay.tsx:632-642` (`ArrowUpRegular`, stopPropagation, `POST /api/ai/analysis/promote`). Two row actions, uncommunicated.
- **Rename: absent** — only `PATCH …/context`,`…/tabs` (`ChatEndpoints.cs:135,185`); `Title` read-time-computed, no writable field.
- **Delete: endpoint exists, no UI** — `DELETE /api/ai/chat/sessions/{id}` (`ChatEndpoints.cs:147`, `DeleteSessionAsync:1231-1261`, archives in Dataverse), `useChatSession.deleteSession:251-277`; no control in HistoryOverlay.
- **List endpoint**: `ChatEndpoints.cs:71,1813-1842` `ListRecentSessionsAsync` → `RecentSessionDto{Id,Title,EntityType,EntityName,PlaybookName,UpdatedAt}`.

## 3b. Session-restore fidelity (Workstream D — two paths)

- **History-menu path restores CHAT ONLY.** `handleSelectHistorySession` → `setChatSessionId`+`startNewSession` (remount) → `resumeSession`+`loadHistory` `GET /history`. Never calls `/restore`, never reads `widgetStates`, never rehydrates tabs. (`ConversationPane.tsx:2283-2289`, `useCommandRouting.ts:73-75`, `SprkChat.tsx:1020-1065`.)
- **Deep-link path is RICH.** `?sessionId=` → `SessionRestoreManager` (`ThreePaneShell.tsx:582-685`) → `GET /…/restore` → `SessionRestoreSpec{playbookId,widgetStates,conversationSummary,recentMessages}` → dispatches `widget_load` per widgetState, restores tabs. **Opening an Analysis from the Hub uses THIS path** (`openSpaarkeAi({analysisId})`).
- **Tab restore no-op/corruption**: effect re-fires (dep `chatSessionId` `WorkspacePane.tsx:713`) but `restoreFromPersistence` no-ops if a widget tab exists (`WorkspaceTabManager.ts:756-757`); `startNewSession` doesn't clear prior tabs → dropped + PATCH-overwrite hazard (`WorkspacePane.tsx:604-615`).
- **Widget work = pointer, not content**: `widgetData` opaque shell (`WorkspaceTabManager.ts:74-91`); compose = `ComposeWidgetSeed` door (`composeWidgetData.ts:19-85`); Summary/Table serialized state is content-minimized agent projection (`SerializedWidgetState.ts:113-299`), dispatched deep-link only.
- **Attached files**: server assoc durable (`ChatSession.DocumentId`/`UploadedFiles` `ChatSession.cs:47-83,208-213`); client chip LOST on reopen (no files in `SessionRestoreSpec` `useSessionRestore.ts:27-36`).
- **AnalysisHubWidget** = plain grid; row-click → `open_analysis_headless` → `openSpaarkeAi({analysisId},2)` new headless modal (deep-link/rich path) (`AnalysisHubWidget.tsx:139-152`, `WorkspacePane.tsx:1276-1278`).

## 3c. `sprk_analysis` record + "Set related record" (was Promote) (Workstream D)

- **`sprk_analysis`** = named durable "AI work session" anchor (Dataverse). Fields: name, worktype, status, playbook, agreementtype, **source doc** `_sprk_documentid_value`, **output doc** `_sprk_outputfileid_value`, `sprk_sessionid`, **regarding** (ADR-024 polymorphic: matter/project/document/…). (`Spaarke.UI.Components/src/types/sprkAnalysis.ts:124-192`; server `AnalysisEntity` `Spaarke.Dataverse/Models.cs:393-405`.)
- **Matter link** = `sprk_analysis_RegardingMatter_sprk_matter` (ref attr `sprk_regardingmatter`), shown as Analyses subgrid tab (`matter-analyses-tab.xml`). One Analysis → many sessions (FK `sprk_aichatsummary.sprk_analysis`, `ChatDataverseRepository.cs:24-29,64-67`).
- **Open an analysis** (Hub row OR matter ribbon) → both → `openSpaarkeAi({analysisId},2)` 80%×80% modal, deep-link RICH restore; rehydrates playbook/capabilities/source-file/scopes (`AnalysisChatContextResolver.cs:241-468`, `launch-resolver.ts:323-350`, `AnalysisRecordLaunch.ts:157-167`, `sprk_analysis_commands.js:152-209`).
- **Two distinct saves** (CONFIRMED): (1) **Promote/"Set related record"** = `POST /api/ai/analysis/promote` creates named `sprk_analysis` + binds existing session FK, NO file, document-anchored, one-time (`AnalysisEndpoints.cs:1285-1404`, `ChatDataverseRepository.cs:238-296`); "casual chat NEVER auto-promoted" (`:72-77`). (2) **Save redline as Document** = `WorkingDocumentService.SaveToSpeAsync` → SPE `/analysis-outputs/{analysisId}/{file}`, linked `_sprk_outputfileid_value` (`WorkingDocumentService.cs:91-200`). Independent.
- **Redline reload into Compose** works: render-follows-store (ADR-040) `GET /…/compose-outputs`, comments re-projected on Load (`compose-contracts.ts:359-367,610-625`).

## 3d. Persistence tiers (CONFIRMED — Cosmos store-of-record; ADR-040/D-06)

- **Transcript/messages**: Redis hot `session:{s}` → **Cosmos `sessions` `StoredSession.Messages` (store-of-record)** → Dataverse cold path DEAD (`GetMessagesAsync` returns empty, `ChatDataverseRepository.cs:325-351`). `sprk_aichatmessage` write-only never read; `sprk_aichatsummary` = metadata+summary only. `sprk_chathistory` session write REMOVED (task 064). (`ChatSessionManager.cs:11-16,171-223`.)
- **Write path fire-and-forget**: `SessionPersistenceService.PersistMessageAsync` Redis then f-a-f Cosmos upsert (`:84-104`) — **the reliability gap** behind blank-on-reopen.
- **Session state** (DocumentId/UploadedFiles/ActiveDocument): **Cosmos `StoredSession`** (`StoredSession.cs:121-234`); Dataverse written only at create/archive/promote. ADR-040 round-trip persist/restore `ChatSessionManager.cs:602-663,676-749`.
- **Tabs**: Redis + **Cosmos `memory` container** (`WorkspaceStateService.cs:11-30,57-63`), mirrored into `StoredSession.Tabs`. No Dataverse.
- **Compose ledger** (annotations/reference map/redline): **Cosmos `StoredSession` keyed by sessionId** (`StoredSession.cs:201-234`, ADR-040). No Dataverse.
- **Analysis work products**: Dataverse `sprk_analysisoutput` + Review Memo JSON (survives DELETE /sessions) + `sprk_workingdocument`; files in SPE (`AnalysisResultPersistence.cs:99-328`).
- **Memory** (ADR-042): Cosmos `memory-items` PK `/subjectId` (`MemoryItemStore.cs:34-37`).
- **Net**: resume data (transcript+tabs+doc refs+uploaded files+ledger) ALL already in Cosmos `StoredSession` keyed by sessionId → resume = READ/WIRING problem, not storage.

## 4. Notifications banner (Workstream E)

- Banner "You have N new notifications" at top of Assistant pane (screenshot). Underlying spine = ADR-047 notification/action spine (`@spaarke/notifications`, `OutboxService`). **Confirm exact banner component + that removal doesn't regress Daily-Briefing / suggestion-card consumers** (open item in design §8).
