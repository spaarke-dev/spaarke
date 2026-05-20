# SprkChat SSE Regression Test Report

> **Date**: 2026-05-17
> **Platform**: Spaarke AI Platform R2 (AIPU2)
> **Scope**: Complete SSE event type catalog and regression test matrix

---

## 1. Overview

SprkChat uses Server-Sent Events (SSE) for streaming AI responses from the BFF API
(`POST /api/ai/chat/sessions/{id}/messages`). The R2 ConversationPane wraps SprkChat
and routes SSE pane events to the PaneEventBus for cross-pane communication.

This report catalogs all **23 SSE event types** across two discriminator families:

- **Chat events** (discriminator: `type` field) -- parsed by `parseSseEvent()` in `useSseStream.ts`
- **Pane events** (discriminator: `event` field) -- parsed by `parsePaneEvent()` in `useSseStream.ts`

---

## 2. SSE Wire Format

All events share the standard SSE framing:

```
data: {"type":"<event_type>","content":"...","data":{...}}\n\n
```

Pane events use `event` as discriminator instead of `type`:

```
data: {"event":"output_pane","widgetType":"AnalysisEditor","payload":{...}}\n\n
```

---

## 3. Complete SSE Event Catalog

### 3.1 Chat Events (type-discriminated, 20 types)

These are parsed by `parseSseEvent()` and processed by `processEvent()` in `useSseStream.ts`.

#### 3.1.1 token

| Field | Value |
|-------|-------|
| **Type string** | `token` |
| **Category** | Streaming core |
| **JSON payload** | `{"type":"token","content":"Hello "}` |
| **Trigger condition** | Each AI-generated text token arrives from the OpenAI streaming response |
| **SprkChat UI behavior** | Hides typing indicator on first token; appends `content` to accumulated response text; updates `content` state |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus (handled internally by useSseStream) |
| **R1 vs R2** | Identical behavior |

#### 3.1.2 done

| Field | Value |
|-------|-------|
| **Type string** | `done` |
| **Category** | Streaming lifecycle |
| **JSON payload** | `{"type":"done","content":null}` |
| **Trigger condition** | AI response stream completes (all tokens emitted, post-processing done) |
| **SprkChat UI behavior** | Sets `isDone=true`, `isStreaming=false`, `isTyping=false`; message finalized |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Identical behavior |

#### 3.1.3 error

| Field | Value |
|-------|-------|
| **Type string** | `error` |
| **Category** | Streaming lifecycle |
| **JSON payload** | `{"type":"error","content":"Rate limit exceeded"}` |
| **Trigger condition** | Backend error during streaming (OpenAI timeout, safety block, serialization failure) |
| **SprkChat UI behavior** | Sets error state; displays error message to user; stops typing indicator |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Identical behavior |

#### 3.1.4 typing_start

| Field | Value |
|-------|-------|
| **Type string** | `typing_start` |
| **Category** | Streaming lifecycle |
| **JSON payload** | `{"type":"typing_start","content":null}` |
| **Trigger condition** | BFF begins processing user message; emitted before first token |
| **SprkChat UI behavior** | Sets `isTyping=true`; displays SprkChatTypingIndicator animation |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Identical behavior |

#### 3.1.5 typing_end

| Field | Value |
|-------|-------|
| **Type string** | `typing_end` |
| **Category** | Streaming lifecycle |
| **JSON payload** | `{"type":"typing_end","content":null}` |
| **Trigger condition** | Typing phase ends (either tokens start flowing or processing completes without tokens) |
| **SprkChat UI behavior** | Sets `isTyping=false`; hides typing indicator |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Identical behavior |

#### 3.1.6 suggestions

| Field | Value |
|-------|-------|
| **Type string** | `suggestions` |
| **Category** | Post-response |
| **JSON payload** | `{"type":"suggestions","content":null,"data":{"suggestions":["Follow-up 1","Follow-up 2","Follow-up 3"]}}` |
| **Trigger condition** | AI generates follow-up suggestion prompts after response completes |
| **SprkChat UI behavior** | Displays SprkChatSuggestions chips (max 3); clicking sends suggestion as new message |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus directly; SprkChat handles internally |
| **R1 vs R2** | R1: string array. R2: ChatSseR2EventTypes.Suggestions adds confidence/category (backward compatible) |

#### 3.1.7 citations

| Field | Value |
|-------|-------|
| **Type string** | `citations` |
| **Category** | Post-response |
| **JSON payload** | `{"type":"citations","content":null,"data":{"citations":[{"id":1,"sourceName":"Agreement.pdf","page":3,"excerpt":"The tenant shall...","chunkId":"chunk-42","sourceType":"document"}]}}` |
| **Trigger condition** | RAG pipeline resolves document citations for the AI response |
| **SprkChat UI behavior** | Maps SSE citation items to ICitation format; renders [N] markers as interactive CitationMarker components with popover details |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | R2 adds `sourceType` (document/web) and `url`/`snippet` for web citations |

#### 3.1.8 plan_preview

| Field | Value |
|-------|-------|
| **Type string** | `plan_preview` |
| **Category** | Compound intent (Phase 2F) |
| **JSON payload** | `{"type":"plan_preview","content":null,"data":{"planId":"plan-abc","planTitle":"Document Analysis Plan","steps":[{"id":"step-1","description":"Extract clauses","status":"pending"}]}}` |
| **Trigger condition** | CompoundIntentDetector identifies multi-step intent; BFF emits plan for user approval |
| **SprkChat UI behavior** | Sets `pendingPlanId` and `pendingPlanData`; renders PlanPreviewCard with Proceed/Cancel buttons |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | New in R1 Phase 2F; unchanged in R2 |

#### 3.1.9 plan_step_start

| Field | Value |
|-------|-------|
| **Type string** | `plan_step_start` |
| **Category** | Plan execution (Phase 2F) |
| **JSON payload** | `{"type":"plan_step_start","content":null,"data":{"stepId":"step-1","stepIndex":0}}` |
| **Trigger condition** | Plan execution begins a new step (emitted by /plan/approve endpoint) |
| **SprkChat UI behavior** | No direct state update in processEvent; handled by dedicated approval stream in handlePlanProceed |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | New in R1 Phase 2F; unchanged in R2 |

#### 3.1.10 plan_step_complete

| Field | Value |
|-------|-------|
| **Type string** | `plan_step_complete` |
| **Category** | Plan execution (Phase 2F) |
| **JSON payload** | `{"type":"plan_step_complete","content":null,"data":{"stepId":"step-1","status":"completed","result":"Found 3 clauses"}}` |
| **Trigger condition** | Plan step finishes executing (success or failure) |
| **SprkChat UI behavior** | No direct state update in processEvent; handled by dedicated approval stream |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | New in R1 Phase 2F; unchanged in R2 |

#### 3.1.11 action_confirmation

| Field | Value |
|-------|-------|
| **Type string** | `action_confirmation` |
| **Category** | HITL actions (R2-039) |
| **JSON payload** | `{"type":"action_confirmation","content":null,"data":{"actionId":"act-1","actionName":"Send Email","summary":"Send summary to counsel","parameters":{"recipient":"john@example.com"}}}` |
| **Trigger condition** | Playbook action with `requiresConfirmation=true`; user must approve before execution |
| **SprkChat UI behavior** | Sets `pendingActionEvent`; SprkChat displays ActionConfirmationDialog |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Introduced in R2 (task R2-039) |

#### 3.1.12 action_success

| Field | Value |
|-------|-------|
| **Type string** | `action_success` |
| **Category** | Autonomous actions (R2-039) |
| **JSON payload** | `{"type":"action_success","content":null,"data":{"actionId":"act-1","message":"Email sent successfully"}}` |
| **Trigger condition** | Playbook action with `requiresConfirmation=false` completes successfully |
| **SprkChat UI behavior** | Sets `pendingActionEvent`; SprkChat shows success notification |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Introduced in R2 (task R2-039) |

#### 3.1.13 action_error

| Field | Value |
|-------|-------|
| **Type string** | `action_error` |
| **Category** | Action failure (R2-039) |
| **JSON payload** | `{"type":"action_error","content":null,"data":{"actionId":"act-1","message":"Failed to send email: recipient not found"}}` |
| **Trigger condition** | Playbook action execution fails |
| **SprkChat UI behavior** | Sets `pendingActionEvent`; SprkChat shows error notification |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Introduced in R2 (task R2-039) |

#### 3.1.14 dialog_open

| Field | Value |
|-------|-------|
| **Type string** | `dialog_open` |
| **Category** | Navigation (R2-039) |
| **JSON payload** | `{"type":"dialog_open","content":null,"data":{"targetPage":"sprk_emailcomposer","prePopulateFields":{"recipient":"john@example.com"},"width":85,"height":85}}` |
| **Trigger condition** | AI instructs frontend to open a Code Page dialog via Xrm.Navigation.navigateTo |
| **SprkChat UI behavior** | Sets `pendingActionEvent` with type `dialog_open`; host workspace opens dialog |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Introduced in R2 (task R2-039) |

#### 3.1.15 navigate

| Field | Value |
|-------|-------|
| **Type string** | `navigate` |
| **Category** | Navigation (R2-052) |
| **JSON payload** | `{"type":"navigate","content":null,"data":{"url":"https://org.crm.dynamics.com/main.aspx?...","targetPage":"sprk_matterdetail","parameters":{"matterId":"..."}}}` |
| **Trigger condition** | AI instructs frontend to navigate to a Dataverse record or Code Page |
| **SprkChat UI behavior** | Sets `pendingActionEvent` with type `navigate`; host workspace performs navigation |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Introduced in R2 (task R2-052) |

#### 3.1.16 document_stream_start

| Field | Value |
|-------|-------|
| **Type string** | `document_stream_start` |
| **Category** | Document streaming (R2-051) |
| **JSON payload** | `{"type":"document_stream_start","operationId":"op-1","targetPosition":"cursor","operationType":"insert"}` |
| **Trigger condition** | BFF begins streaming AI-generated content for insertion into a document editor |
| **SprkChat UI behavior** | Forwarded via `onDocumentStreamEventRef` callback to SprkChatBridge for cross-pane delivery to Lexical editor |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus (uses direct callback ref) |
| **R1 vs R2** | Introduced in R2 (task R2-051) |

#### 3.1.17 document_stream_token

| Field | Value |
|-------|-------|
| **Type string** | `document_stream_token` |
| **Category** | Document streaming (R2-051) |
| **JSON payload** | `{"type":"document_stream_token","operationId":"op-1","token":"The contract","index":0}` |
| **Trigger condition** | Each AI-generated token for document insertion |
| **SprkChat UI behavior** | Forwarded synchronously via callback ref to avoid React state batching token loss |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus (uses direct callback ref) |
| **R1 vs R2** | Introduced in R2 (task R2-051) |

#### 3.1.18 document_stream_end

| Field | Value |
|-------|-------|
| **Type string** | `document_stream_end` |
| **Category** | Document streaming (R2-051) |
| **JSON payload** | `{"type":"document_stream_end","operationId":"op-1","cancelled":false,"totalTokens":142}` |
| **Trigger condition** | Document streaming operation completes or is cancelled |
| **SprkChat UI behavior** | Forwarded via callback ref; editor finalizes insertion |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus (uses direct callback ref) |
| **R1 vs R2** | Introduced in R2 (task R2-051) |

#### 3.1.19 document_processing_start

| Field | Value |
|-------|-------|
| **Type string** | `document_processing_start` |
| **Category** | Document upload (Phase 3E) |
| **JSON payload** | `{"type":"document_processing_start","content":null}` |
| **Trigger condition** | Document Intelligence begins extracting text from an uploaded document |
| **SprkChat UI behavior** | No direct handling in processEvent; managed by document upload flow |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Present in ChatSseEventType union; upload flow-specific |

#### 3.1.20 document_processing_complete

| Field | Value |
|-------|-------|
| **Type string** | `document_processing_complete` |
| **Category** | Document upload (Phase 3E) |
| **JSON payload** | `{"type":"document_processing_complete","content":null}` |
| **Trigger condition** | Document Intelligence extraction finishes, document added to context |
| **SprkChat UI behavior** | No direct handling in processEvent; managed by document upload flow |
| **ConversationPane/PaneEventBus** | Not routed to PaneEventBus |
| **R1 vs R2** | Present in ChatSseEventType union; upload flow-specific |

### 3.2 Pane Events (event-discriminated, 3 types)

These are parsed by `parsePaneEvent()` in `useSseStream.ts` and forwarded via the
`onPaneEventRef` callback. In R2, AiSessionProvider routes them to PaneEventBus channels.

#### 3.2.1 output_pane

| Field | Value |
|-------|-------|
| **Type string** | `output_pane` (event discriminator) |
| **Category** | Cross-pane (R2 three-pane layout) |
| **JSON payload** | `{"event":"output_pane","widgetType":"AnalysisEditor","payload":{"documentId":"doc-42","content":"..."}}` |
| **Trigger condition** | AI tool produces output that should render as a workspace widget (via ChatSseEventFactory.CreateOutputPaneEvent) |
| **SprkChat UI behavior** | Not rendered in chat; forwarded via `onPaneEventRef` callback |
| **ConversationPane/PaneEventBus** | Routed to `workspace` channel as `WorkspacePaneEvent { type: 'widget_load' }` |
| **R1 vs R2** | R1: single-subscriber ref (last-call-wins). R2: multi-subscriber PaneEventBus |

#### 3.2.2 source_pane

| Field | Value |
|-------|-------|
| **Type string** | `source_pane` (event discriminator) |
| **Category** | Cross-pane (R2 three-pane layout) |
| **JSON payload** | `{"event":"source_pane","widgetType":"DocumentViewer","payload":{"documentId":"doc-42","documentName":"Agreement.pdf"},"citationId":"cit-1"}` |
| **Trigger condition** | AI references a source document (via ChatSseEventFactory.CreateSourcePaneEvent) |
| **SprkChat UI behavior** | Not rendered in chat; forwarded via `onPaneEventRef` callback |
| **ConversationPane/PaneEventBus** | Routed to `context` channel as `ContextPaneEvent { type: 'context_update' }` |
| **R1 vs R2** | R1: single-subscriber ref. R2: multi-subscriber PaneEventBus |

#### 3.2.3 source_highlight

| Field | Value |
|-------|-------|
| **Type string** | `source_highlight` (event discriminator) |
| **Category** | Cross-pane (R2 three-pane layout) |
| **JSON payload** | `{"event":"source_highlight","sourceRef":"citation-7","selectionRef":"char:1024-1200"}` |
| **Trigger condition** | AI response references a specific excerpt that should be highlighted in-document (via ChatSseEventFactory.CreateSourceHighlightEvent) |
| **SprkChat UI behavior** | Not rendered in chat; forwarded via `onPaneEventRef` callback |
| **ConversationPane/PaneEventBus** | Routed to `context` channel as `ContextPaneEvent { type: 'context_highlight' }` |
| **R1 vs R2** | R1: single-subscriber ref. R2: multi-subscriber PaneEventBus |

---

## 4. Test Matrix

| # | Event Type | Category | Discriminator | Trigger | Expected UI | R1 Compat | PaneEventBus Channel |
|---|-----------|----------|---------------|---------|-------------|-----------|---------------------|
| 1 | `token` | Streaming core | `type` | AI token generated | Append to response text | Yes | -- |
| 2 | `done` | Lifecycle | `type` | Stream completes | Finalize message | Yes | -- |
| 3 | `error` | Lifecycle | `type` | Backend error | Show error | Yes | -- |
| 4 | `typing_start` | Lifecycle | `type` | Processing begins | Show typing indicator | Yes | -- |
| 5 | `typing_end` | Lifecycle | `type` | Processing ends | Hide typing indicator | Yes | -- |
| 6 | `suggestions` | Post-response | `type` | AI generates follow-ups | Show suggestion chips | Yes | -- |
| 7 | `citations` | Post-response | `type` | RAG resolves citations | Render [N] markers | Yes | -- |
| 8 | `plan_preview` | Compound intent | `type` | Multi-step intent detected | Show PlanPreviewCard | Yes | -- |
| 9 | `plan_step_start` | Plan execution | `type` | Step begins | Update step status | Yes | -- |
| 10 | `plan_step_complete` | Plan execution | `type` | Step finishes | Update step result | Yes | -- |
| 11 | `action_confirmation` | HITL action | `type` | Action needs approval | Show confirmation dialog | No (R2) | -- |
| 12 | `action_success` | Autonomous action | `type` | Action succeeded | Show success toast | No (R2) | -- |
| 13 | `action_error` | Action failure | `type` | Action failed | Show error toast | No (R2) | -- |
| 14 | `dialog_open` | Navigation | `type` | Open Code Page dialog | Open Xrm dialog | No (R2) | -- |
| 15 | `navigate` | Navigation | `type` | Navigate to record | Perform navigation | No (R2) | -- |
| 16 | `document_stream_start` | Doc streaming | `type` | Editor insert begins | Forward to bridge | No (R2) | -- |
| 17 | `document_stream_token` | Doc streaming | `type` | Editor token | Forward to bridge | No (R2) | -- |
| 18 | `document_stream_end` | Doc streaming | `type` | Editor insert ends | Forward to bridge | No (R2) | -- |
| 19 | `document_processing_start` | Doc upload | `type` | Extraction begins | Show processing status | Yes | -- |
| 20 | `document_processing_complete` | Doc upload | `type` | Extraction finishes | Show complete status | Yes | -- |
| 21 | `output_pane` | Cross-pane | `event` | Widget output produced | Forward to bus | No (R2) | workspace |
| 22 | `source_pane` | Cross-pane | `event` | Source doc referenced | Forward to bus | No (R2) | context |
| 23 | `source_highlight` | Cross-pane | `event` | Citation highlight | Forward to bus | No (R2) | context |

---

## 5. PaneEventBus Routing Table

| SSE Event (`event` field) | PaneEventBus Channel | Dispatched Event Type | Key Fields Mapped |
|---------------------------|---------------------|-----------------------|-------------------|
| `output_pane` | `workspace` | `widget_load` | `widgetType` -> `widgetType`, `payload` -> `widgetData` |
| `source_pane` | `context` | `context_update` | `widgetType` -> `contextType`, `payload` -> `contextData` |
| `source_highlight` | `context` | `context_highlight` | `sourceRef` -> `citationId`, `selectionRef` -> `selectionRef` |
| Unknown event type | (none -- dropped) | (none) | Warning logged, no dispatch |

---

## 6. Mock SSE Stream Examples

### 6.1 Standard Chat Response

```
data: {"type":"typing_start","content":null}

data: {"type":"token","content":"Based on "}

data: {"type":"token","content":"the agreement, "}

data: {"type":"token","content":"Section 3.1 states..."}

data: {"type":"typing_end","content":null}

data: {"type":"citations","content":null,"data":{"citations":[{"id":1,"sourceName":"Agreement.pdf","page":3,"excerpt":"The tenant shall maintain...","chunkId":"chunk-42","sourceType":"document"}]}}

data: {"type":"suggestions","content":null,"data":{"suggestions":["What are the key obligations?","Summarize termination clauses","Compare with standard terms"]}}

data: {"type":"done","content":null}

```

### 6.2 Plan Preview + Execution

```
data: {"type":"typing_start","content":null}

data: {"type":"plan_preview","content":null,"data":{"planId":"plan-abc","planTitle":"Document Analysis Plan","steps":[{"id":"step-1","description":"Extract key clauses","status":"pending"},{"id":"step-2","description":"Identify risks","status":"pending"}]}}

data: {"type":"typing_end","content":null}

data: {"type":"done","content":null}

```

After user approves (on /plan/approve stream):

```
data: {"type":"plan_step_start","content":null,"data":{"stepId":"step-1","stepIndex":0}}

data: {"type":"token","content":"Extracting clauses..."}

data: {"type":"plan_step_complete","content":null,"data":{"stepId":"step-1","status":"completed","result":"Found 5 key clauses"}}

data: {"type":"plan_step_start","content":null,"data":{"stepId":"step-2","stepIndex":1}}

data: {"type":"token","content":"Analyzing risks..."}

data: {"type":"plan_step_complete","content":null,"data":{"stepId":"step-2","status":"completed","result":"3 medium-risk clauses identified"}}

data: {"type":"done","content":null}

```

### 6.3 Cross-Pane Events (Three-Pane Layout)

```
data: {"type":"typing_start","content":null}

data: {"event":"output_pane","widgetType":"AnalysisEditor","payload":{"documentId":"doc-42","content":"<analysis results>"}}

data: {"event":"source_pane","widgetType":"DocumentViewer","payload":{"documentId":"doc-42","documentName":"Agreement.pdf"}}

data: {"type":"token","content":"I've analyzed the document..."}

data: {"event":"source_highlight","sourceRef":"citation-1","selectionRef":"char:1024-1200"}

data: {"type":"typing_end","content":null}

data: {"type":"done","content":null}

```

### 6.4 Action Confirmation Flow

```
data: {"type":"typing_start","content":null}

data: {"type":"token","content":"I'll send the summary email to counsel."}

data: {"type":"action_confirmation","content":null,"data":{"actionId":"act-email-1","actionName":"Send Email","summary":"Send analysis summary to john@example.com","parameters":{"recipient":"john@example.com","subject":"Analysis Summary - NDA Review"}}}

data: {"type":"typing_end","content":null}

data: {"type":"done","content":null}

```

### 6.5 Document Streaming (Insert to Editor)

```
data: {"type":"typing_start","content":null}

data: {"type":"document_stream_start","operationId":"op-1","targetPosition":"cursor","operationType":"insert"}

data: {"type":"document_stream_token","operationId":"op-1","token":"The parties ","index":0}

data: {"type":"document_stream_token","operationId":"op-1","token":"hereby agree ","index":1}

data: {"type":"document_stream_token","operationId":"op-1","token":"to the following terms.","index":2}

data: {"type":"document_stream_end","operationId":"op-1","cancelled":false,"totalTokens":3}

data: {"type":"typing_end","content":null}

data: {"type":"done","content":null}

```

### 6.6 Error Stream

```
data: {"type":"typing_start","content":null}

data: {"type":"error","content":"The AI service is temporarily unavailable. Please try again in a few moments."}

```

---

## 7. Test Execution Procedure

### 7.1 Unit Tests

Run the SSE event routing tests:

```bash
cd src/client/shared/Spaarke.AI.Widgets
npx jest --testPathPattern="sse-event-routing" --verbose
```

Run the existing AiSessionProvider tests:

```bash
npx jest --testPathPattern="AiSessionProvider" --verbose
```

Run the existing PaneEventBus tests:

```bash
npx jest --testPathPattern="PaneEventBus" --verbose
```

Run useSseStream tests:

```bash
cd src/client/shared/Spaarke.UI.Components
npx jest --testPathPattern="useSseStream" --verbose
```

### 7.2 Manual SSE Stream Testing

1. Open the LegalWorkspace or AnalysisWorkspace Code Page
2. Open browser DevTools > Network tab, filter by EventStream
3. Send a chat message and observe the SSE stream
4. Verify each event type in the stream matches the expected payload schema
5. For cross-pane events, verify the workspace and context panes react correctly

### 7.3 Regression Checklist

- [ ] All 20 chat event types parsed correctly by parseSseEvent()
- [ ] All 3 pane event types parsed correctly by parsePaneEvent()
- [ ] output_pane routed to workspace channel as widget_load
- [ ] source_pane routed to context channel as context_update
- [ ] source_highlight routed to context channel as context_highlight
- [ ] Unknown pane event types logged and dropped (no crash)
- [ ] Multi-subscriber independence: two workspace subscribers both receive output_pane
- [ ] Unsubscribing one pane does not affect the other
- [ ] Token accumulation correct across stream lifecycle
- [ ] Typing indicator shows/hides at correct boundaries
- [ ] Suggestions display after response completes
- [ ] Citations render as interactive [N] markers
- [ ] Action confirmation dialog opens for HITL events
- [ ] Document stream tokens forwarded without loss (callback ref, not state)
- [ ] Plan preview card renders with Proceed/Cancel buttons

---

## 8. Source Code References

### Backend (C#)

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | R1 SSE event emission, ChatSseEvent record definition |
| `src/server/api/Sprk.Bff.Api/Api/Ai/R2SseEventEmitter.cs` | R2 event emitter (workspace_widget, context_update, etc.) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEvent.cs` | Provider-agnostic SSE event envelope (R2 agent pipeline) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/ChatSseEventFactory.cs` | Factory for output_pane, source_pane, source_highlight |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/OutputPaneSseEvent.cs` | output_pane type constant and payload record |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/SourcePaneSseEvent.cs` | source_pane type constant and payload record |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/SourceHighlightSseEvent.cs` | source_highlight type constant and payload record |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Sse/SseEventSchemaValidator.cs` | ChatSseR2EventTypes constants, schema validation |

### Frontend (TypeScript)

| File | Purpose |
|------|---------|
| `src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` | Canonical SSE stream hook (parseSseEvent, parsePaneEvent, processEvent) |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` | ChatSseEventType union, IChatSseEvent, IAiPaneEvent, IDocumentStreamSseEvent |
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | PaneChannel, WorkspacePaneEvent, ContextPaneEvent, SafetyPaneEvent |
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` | Multi-subscriber typed event bus |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | routePaneEvent: SSE -> PaneEventBus routing |
| `src/client/shared/Spaarke.AI.Context/src/types/index.ts` | AiPaneEvent, StreamingCallbacks interfaces |
