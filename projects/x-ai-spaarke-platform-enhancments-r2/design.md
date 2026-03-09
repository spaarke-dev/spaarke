# SprkChat Interactive Collaboration — Design Document

> **Project**: ai-spaarke-platform-enhancements-r2
> **Author**: Ralph Schroeder / Claude Code
> **Date**: 2026-02-25
> **Status**: Draft — Pending Review
> **Revision**: 2 — Major architectural revision (platform-wide side pane, Code Page migration, streaming writes)

---

## 1. Executive Summary

SprkChat today is a **read-only AI assistant** embedded as a child component inside the Analysis Workspace PCF control. It can answer questions about documents, retrieve analysis results, search knowledge bases, and refine text passages — but it cannot write back to the working document, re-execute analysis with user context, or provide an action-oriented command interface. It is also locked to React 16 and tightly coupled to the Analysis Workspace layout.

This project transforms SprkChat into a **platform-wide AI collaborator** deployed as a **standalone side pane** accessible from any Dataverse form — Matters, Projects, Invoices, Analysis records. The Analysis Workspace becomes the first "context" use case, not the only one. The project also migrates the Analysis Workspace from a PCF control to a **Code Page** (React 19, full viewport control) to enable streaming write sessions, modern rendering, and clean separation from the SprkChat side pane.

### Driving User Expectations

Users across the platform expect an AI collaborator that:

1. **Is always available** — A persistent side pane on any form, not buried inside one workspace
2. **Edits documents interactively** — Streaming write sessions where the AI types into the editor in real time (like Claude/ChatGPT artifacts), not bulk file replacement
3. **Re-processes on demand** — "Rerun the analysis focusing on financial risks" triggers full document reprocessing through the analysis pipeline
4. **Offers structured actions** — A `/` command palette for playbook switching, web search, export, and other operations
5. **Shows its work** — Diff compare view to review AI-proposed changes before accepting
6. **Revises selected text** — Select text in the editor, ask AI to rewrite/expand/simplify in-place
7. **Works within the playbook model** — Playbooks govern what capabilities SprkChat has in each context

### Scope

Nine work packages organized for **maximum parallel execution** by agent teams:

| Priority | Package | Feature | Size | Parallelizable |
|----------|---------|---------|------|----------------|
| P0 | A | SprkChat Side Pane (Code Page) | L | Yes — independent |
| P0 | B | Streaming Write Engine | L | Yes — independent |
| P0 | C | Analysis Workspace Code Page Migration | L | Depends on A |
| P0 | D | Action Menu / Command Palette | M | Yes — independent |
| P0 | E | Re-Analysis Pipeline | M | Depends on B |
| P1 | F | Diff Compare View | M | Depends on B |
| P1 | G | Selection-Based Revision | M | Depends on B, C |
| P1 | H | Suggested Follow-Ups + Citations | M | Depends on A |
| P2 | I | Web Search + Multi-Document | M | Depends on A |

**Estimated task count**: 100–130 POML tasks across all packages.

---

## 2. Current Architecture (Baseline)

### 2.1 Chat Implementation Inventory

The codebase contains **three distinct chat implementations**. Understanding each is critical for reconciliation.

#### 2.1.1 Legacy Analysis Chat (DEPRECATED)

| Aspect | Detail |
|--------|--------|
| **Location** | Inline in `AnalysisWorkspaceApp.tsx` (lines 1404-1489) |
| **Status** | Deprecated — behind `useLegacyChat` flag (default: `false`) |
| **Backend** | `POST /api/ai/analysis/{analysisId}/continue` — marked DEPRECATED in AnalysisEndpoints.cs |
| **Session** | Manual resume via `POST /api/ai/analysis/{id}/resume`; in-memory only |
| **Persistence** | Chat history saved to Dataverse `sprk_chathistory` field |
| **UI** | Custom SSE hook (`useSseStream.ts`, 294 lines), `ResumeSessionDialog` component |
| **Features** | Basic streaming chat, session resume dialog |

This is the original chat — it talks directly to the analysis orchestration service, not the agent framework. **SprkChat already replaced it** (the flag defaults to `false`). This project will complete the deprecation by removing the legacy code and endpoints.

#### 2.1.2 SprkChat (CURRENT — Unified Standard)

| Aspect | Detail |
|--------|--------|
| **Location** | `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` (~1,600 lines) |
| **Status** | Active, production — the system this project enhances |
| **Backend** | `/api/ai/chat/sessions/` endpoints (agent framework with tools) |
| **Session** | Redis hot cache + Dataverse fallback |
| **UI** | `SprkChatInput`, `SprkChatMessage`, `SprkChatContextSelector`, `SprkChatPredefinedPrompts`, `SprkChatHighlightRefine` |
| **Hooks** | `useChatSession`, `useSseStream`, `useChatPlaybooks` |
| **Features** | Context switching, highlight-refine, playbook discovery, predefined prompts, `hostContext` entity scoping |

SprkChat is currently embedded as a child component inside the Analysis Workspace PCF. This project promotes it to a **standalone side pane deployable on any Dataverse form**.

#### 2.1.3 PlaybookBuilder AI Assistant (SEPARATE — Not In Scope)

| Aspect | Detail |
|--------|--------|
| **Location** | `src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/` (~3,000+ lines) |
| **Status** | Active — fully custom implementation, NOT using SprkChat |
| **Backend** | `POST /api/ai/playbook-builder/process` (52K bytes endpoint file) |
| **State** | Zustand store (`aiAssistantStore.ts`, 1,089 lines) |
| **UI** | Floating draggable modal with: ChatHistory, ChatInput, CommandPalette, ClarificationOptions, SuggestionBar, OperationFeedback, TestProgressView |
| **SSE Events** | `thinking`, `canvas_patch`, `dataverse_operation`, `message`, `clarification`, `plan_preview`, `done`, `error` |
| **Features** | Canvas patch application, Dataverse record creation, model selection, intent classification, clarification flow, test execution |

The PlaybookBuilder AI Assistant is a **domain-specific system** for building playbooks on a visual canvas. Its needs (canvas patches, Dataverse operations, clarification flows) are fundamentally different from document analysis chat. **It remains separate in R2.** However, its `CommandPalette` and `SuggestionBar` components serve as reference implementations for similar SprkChat features (Packages D and H).

**Future convergence (R3+)**: Extend SprkChat with a plugin/event system that the PlaybookBuilder could adopt, centralizing shared infrastructure (SSE streaming, session management, action menus) while preserving domain-specific tools.

### 2.2 Analysis Workspace (PCF — Current)

The Analysis Workspace is currently a **PCF control** with a 3-column layout:

```
┌─────────────────────┬──────────────────┬──────────────────┐
│   Analysis Output   │  Source Document  │   Conversation   │
│   (RichTextEditor)  │  (Viewer)         │   (SprkChat)     │
│                     │                   │                  │
│  ← editable panel   │  ← read-only      │  ← AI chat       │
│  setHtml() / getHtml│  collapsible      │  350px min width  │
└─────────────────────┴──────────────────┴──────────────────┘
```

**Constraints of the PCF model:**
- React 16/17 only (platform-provided, cannot upgrade)
- No concurrent rendering — streaming writes cause jank
- Layout constrained by Dataverse form column
- SprkChat is embedded as a child — cannot be deployed independently
- No `startTransition`, `useDeferredValue`, or Suspense for smooth streaming UX

**In the revised architecture**, the Analysis Workspace becomes a **2-panel Code Page** (output editor + source viewer) and SprkChat becomes a **separate side pane Code Page**. See Section 3.

### 2.3 Backend — BFF API

**Chat Endpoints** (`/api/ai/chat/`):

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/sessions` | Create session |
| POST | `/sessions/{id}/messages` | Send message (SSE stream) |
| POST | `/sessions/{id}/refine` | Refine selected text (SSE stream) |
| PATCH | `/sessions/{id}/context` | Switch document/playbook |
| GET | `/sessions/{id}/history` | Load message history |
| DELETE | `/sessions/{id}` | Delete session |
| GET | `/playbooks` | Discover available playbooks |

**Deprecated Endpoints** (to be removed):

| Method | Path | Status |
|--------|------|--------|
| POST | `/api/ai/analysis/{id}/continue` | DEPRECATED — replaced by `/chat/sessions/{id}/messages` |
| POST | `/api/ai/analysis/{id}/resume` | DEPRECATED — replaced by session management in ChatSessionManager |

**SSE Event Types** (current):
```
{ type: "token", content: "..." }    // streaming text chunk
{ type: "done", content: null }       // stream complete
{ type: "error", content: "..." }     // error message
```

**Registered AI Tools** (via `SprkChatAgentFactory.ResolveTools`):

| Tool Class | Methods | Purpose |
|------------|---------|---------|
| `DocumentSearchTools` | `SearchDocuments`, `SearchDiscovery` | RAG index search |
| `AnalysisQueryTools` | `GetAnalysisResult`, `GetAnalysisSummary` | Retrieve prior analysis |
| `KnowledgeRetrievalTools` | `GetKnowledgeSource`, `SearchKnowledgeBase` | Knowledge retrieval |
| `TextRefinementTools` | `RefineText`, `ExtractKeyPoints`, `GenerateSummary` | Text manipulation |

All current tools are **read-only**. This project adds write-capable tools.

**Analysis Service** (`IAnalysisOrchestrationService`):
- `ExecuteAnalysisAsync` — full streaming analysis (re-analysis trigger)
- `ContinueAnalysisAsync` — conversational refinement (existing, not yet wired to SprkChat)
- `SaveWorkingDocumentAsync` — persist to SPE + Dataverse
- `ExecutePlaybookAsync` — playbook-driven execution
- `GetAnalysisAsync` — fetch full record

**Middleware Pipeline** (ADR-013 / AIPL-057): ContentSafety → CostControl → Telemetry → IChatClient

**DI Budget**: 12 of 15 registrations used (ADR-010). New tools are factory-instantiated — **0 additional DI slots required**.

### 2.4 Existing Building Blocks (Reusable)

| Component | Status | Reuse In |
|-----------|--------|----------|
| `SprkChatHighlightRefine` | Built | Package G (wire to editor panel) |
| `SprkChatPredefinedPrompts` | Built | Package D (extend with action categories) |
| `SprkChatContextSelector` | Built | Package D (integrate into action menu) |
| `POST /sessions/{id}/refine` | Built | Package G (wire to editor selections) |
| `PATCH /sessions/{id}/context` | Built | Package D (expose via action menu) |
| `useChatPlaybooks` hook | Built | Package D (feed action menu playbook list) |
| `ContinueAnalysisAsync` | Built | Package E (wire to SprkChat for re-analysis) |
| `RichTextEditor` ref API | Built | Package B (enhance for streaming inserts) |
| PlaybookBuilder `CommandPalette` | Built | Package D (reference implementation) |
| PlaybookBuilder `SuggestionBar` | Built | Package H (reference implementation) |
| `hostContext` pattern | Built | Package A (entity-scoped side pane) |

---

## 3. Architectural Decisions

### 3.1 SprkChat as Platform-Wide Side Pane

**Decision**: Deploy SprkChat as a **standalone Code Page web resource** opened via `Xrm.App.sidePanes.createPane()`, not embedded within individual workspaces.

**Rationale**:
- Available on **any** Dataverse form — Matters, Projects, Invoices, Analysis records
- Persists across form navigation (side panes stay open as user browses records)
- Independent deployment lifecycle — update SprkChat without redeploying host controls
- Clean separation of concerns — the host form provides context, SprkChat provides AI

**Context Resolution**:
```
Matter Form       → sidePanes.createPane() → SprkChat → hostContext: { entityType: "matter", entityId: "guid" }
Project Form      → sidePanes.createPane() → SprkChat → hostContext: { entityType: "project", entityId: "guid" }
Analysis Record   → sidePanes.createPane() → SprkChat → hostContext: { entityType: "analysis", entityId: "guid" }
```

The `hostContext` pattern (already built in R1) is the abstraction that makes this work. The playbook determines what SprkChat can do in each context — a Matter gets "Legal Research" playbooks, an Analysis gets "Document Profile" playbooks.

**Cross-Pane Communication** (SprkChat ↔ Analysis Workspace):
- `BroadcastChannel` API for real-time messaging between the side pane and host page
- Events: `document_stream_start`, `document_stream_token`, `document_stream_end`, `document_replaced`, `selection_changed`
- Fallback: `window.postMessage` for environments where BroadcastChannel is unavailable

### 3.2 Analysis Workspace Migration: PCF → Code Page

**Decision**: Rebuild the Analysis Workspace as a **Code Page** (standalone HTML/CSS/JS web resource, React 19).

**What It Becomes**: A 2-panel document editing environment:

```
┌──────────────────────────────┬──────────────────────────────┐
│      Analysis Output          │      Source Document          │
│      (RichTextEditor)         │      (Viewer)                 │
│                               │                               │
│  ← AI writes here via        │  ← read-only, collapsible     │
│    streaming tokens           │                               │
│  ← user edits directly       │                               │
│  ← diff view toggle          │                               │
└──────────────────────────────┴──────────────────────────────┘
                               +
┌──────────────────────────────┐
│  SprkChat Side Pane           │  ← separate Code Page
│  (persistent, cross-form)     │     via Xrm.App.sidePanes
└──────────────────────────────┘
```

**Why Code Page over PCF**:

| Factor | PCF (Current) | Code Page (Proposed) |
|--------|---------------|---------------------|
| React version | 16/17 (platform, can't upgrade) | 19 (bundled, we control) |
| Streaming UX | React 16 batching causes jank | `startTransition`, concurrent rendering for smooth streaming |
| Deployment | Field-bound on form column | Full viewport web resource via `navigateTo` |
| SprkChat coupling | Embedded child component | Decoupled — separate side pane |
| Layout control | Constrained by form | Full CSS grid/flexbox control |
| Component library | React 16-compatible only | Any modern library (Lexical latest, etc.) |
| Dark mode | Platform tokens only | Full CSS variable + `prefers-color-scheme` control |

**Migration Path**: The existing React components (`RichTextEditor`, `SourceDocumentViewer`) are in the shared library and are React 18/19-compatible. Migration involves:
- New webpack entry point (Code Page pattern from ADR-006)
- `createRoot()` instead of `ReactDOM.render()`
- Replace `context.webAPI` calls with direct BFF API calls (already happening via `apiBaseUrl`)
- Layout restructured for full viewport (2-panel instead of 3-column)
- Open via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... })`

### 3.3 Playbook-Governed Capabilities

**Decision**: Playbooks define what capabilities SprkChat has in each context, not just the system prompt.

**Current state**: Playbooks provide system prompt + knowledge scope. Tools are hardcoded in `ResolveTools()`.

**Proposed**: Playbooks declare a capability set that controls which tools are registered:

```
Playbook "Document Profile":
  capabilities: [search, analyze, write_back, reanalyze, selection_revise]

Playbook "Quick Q&A":
  capabilities: [search, summarize]
  // No write_back — read-only assistant

Playbook "Contract Review":
  capabilities: [search, analyze, write_back, selection_revise, web_search]

Playbook "General Assistant" (Matter/Project context):
  capabilities: [search, summarize, web_search]
  // No write_back — no editor to write to
```

`SprkChatAgentFactory.ResolveTools()` filters tools based on playbook capabilities. The action menu (Package D) only shows actions the current playbook supports.

### 3.4 Streaming Write Sessions (Not File Replacement)

**Decision**: AI edits to the working document stream token-by-token into the editor in real time, like Claude/ChatGPT artifact editing. Not bulk `setHtml()` replacement.

**User experience**:
```
User: "Add a section about regulatory compliance after the risk assessment"

Editor (live, streaming):
  ... existing content ...
  ## Risk Assessment
  [existing text]

  ## Regulatory Compliance        ← cursor appears, text streams in
  The document reveals several    ← user sees AI "typing" in real time
  regulatory considerations...    ← smooth concurrent rendering (React 19)
```

**Why this matters**:
- Users see progress immediately — no frozen UI waiting for a complete response
- Users can cancel mid-stream if the direction is wrong
- Matches the experience users expect from Claude/ChatGPT
- React 19 concurrent rendering (`startTransition`) keeps the editor responsive during streaming

**Architecture**:
- New SSE event: `document_stream` with positional metadata
- New Lexical plugin: `StreamingInsertPlugin` that appends tokens at a target position
- Existing `setHtml()` retained for bulk operations (re-analysis replacement)

### 3.5 Dual Write Modes: Live Streaming + Diff Review

**Decision**: Support two modes for AI-initiated document changes.

**Mode 1 — Live Streaming** (default for additions/expansions):
- AI writes directly into the editor via streaming tokens
- User sees real-time progress
- Cancel button stops the stream
- Best for: "Add a section about...", "Expand on the risk factors..."

**Mode 2 — Diff Review** (for revisions/replacements):
- AI proposes changes shown as a side-by-side or inline diff
- User reviews, then accepts, rejects, or edits
- Best for: "Rewrite the conclusion to be more formal", selection-based revision

```
┌────────────────────────────┬────────────────────────────┐
│  Original                  │  AI Proposed               │
├────────────────────────────┼────────────────────────────┤
│  ## Conclusion             │  ## Conclusion             │
│  The document shows        │  The document demonstrates │
│  some risk factors.        │  significant risk factors  │
│                            │  warranting immediate      │
│                            │  remediation.              │
└────────────────────────────┴────────────────────────────┘
                    [ Accept ] [ Reject ] [ Edit ]
```

Mode selection is automatic based on operation type, but users can override via a preference or the action menu.

### 3.6 Re-Analysis = Full Document Reprocessing

**Decision**: When the user requests re-analysis, the **entire analysis pipeline re-executes** with the new instructions. This is not an inline edit — it's a full reprocessing of the source document through the playbook.

**Flow**:
1. User: "Rerun the analysis focusing on financial risks"
2. LLM calls `RerunAnalysisAsync("Focus on financial risk factors")`
3. Tool calls `_analysisService.ExecutePlaybookAsync()` with original playbook + appended instructions
4. Editor shows progress state (streaming analysis output replacing content)
5. Result: New analysis version (previous version preserved for undo)

**Distinct from interactive editing**: "Add a paragraph about compliance" is a streaming write session (Section 3.4). "Rerun the analysis focusing on risks" is a full reprocessing pipeline.

---

## 4. Legacy Cleanup (Prerequisite)

Before building new features, remove deprecated code to reduce confusion and maintenance burden.

### 4.1 Frontend Removal

| Item | Location | Action |
|------|----------|--------|
| `useLegacyChat` flag | `AnalysisWorkspaceApp.tsx`, `types/index.ts`, `index.ts` | Remove flag and all conditional branches |
| Legacy chat panel | `AnalysisWorkspaceApp.tsx` lines 1404-1489 | Remove inline chat rendering |
| Legacy SSE hook | `AnalysisWorkspace/hooks/useSseStream.ts` (294 lines) | Delete file |
| `ResumeSessionDialog` | `AnalysisWorkspace/components/ResumeSessionDialog.tsx` | Delete file |
| Chat message badge | `AnalysisWorkspaceApp.tsx` lines 1397-1402 | Remove legacy badge |
| `MsalAuthProvider` | Internal auth for legacy chat | Remove if unused after cleanup |

### 4.2 Backend Removal

| Item | Location | Action |
|------|----------|--------|
| `POST /api/ai/analysis/{id}/continue` | `AnalysisEndpoints.cs` | Remove deprecated endpoint |
| `POST /api/ai/analysis/{id}/resume` | `AnalysisEndpoints.cs` | Remove deprecated endpoint |
| `sprk_chathistory` field usage | Legacy chat persistence | Stop writing; field remains in Dataverse schema |

### 4.3 Impact Assessment

- Zero user impact: `useLegacyChat` already defaults to `false`; no active consumers of legacy endpoints
- Removes ~500 lines of frontend code and 2 API endpoints
- Simplifies the Analysis Workspace codebase before migration to Code Page

---

## 5. Work Packages — Detailed Design

### Package A: SprkChat Side Pane (P0, Large)

**Goal**: Deploy SprkChat as a standalone Code Page web resource accessible as a persistent side pane on any Dataverse form.

#### 5.A.1 New Artifacts

**Code Page: `sprk_SprkChatPane`**
- Entry point: `src/client/code-pages/SprkChatPane/index.tsx`
- React 19 with `createRoot()`
- Receives context via URL parameters: `entityType`, `entityId`, `playbookId`, `sessionId`
- Renders `<FluentProvider>` → `<SprkChat>` with full viewport

**Side Pane Launcher** (ribbon button or form script):
```typescript
Xrm.App.sidePanes.createPane({
    title: "AI Assistant",
    imageSrc: "sprk_ai_icon",
    paneId: "sprkchat",
    canClose: true,
    width: 400,
}).then((pane) => {
    pane.navigate({
        pageType: "webresource",
        webresourceName: "sprk_SprkChatPane",
        data: `entityType=${entityType}&entityId=${entityId}`
    });
});
```

**Cross-Pane Communication Service**:
```typescript
// Shared module in @spaarke/ui-components
export class SprkChatBridge {
    private channel: BroadcastChannel;

    // SprkChat → Host (document editing events)
    emitDocumentStreamStart(position: InsertPosition): void;
    emitDocumentStreamToken(token: string): void;
    emitDocumentStreamEnd(): void;
    emitDocumentReplaced(html: string): void;

    // Host → SprkChat (selection, context events)
    emitSelectionChanged(selectedText: string, rect: DOMRect): void;
    emitContextChanged(entityType: string, entityId: string): void;

    // Subscriptions
    onDocumentStream(handler: StreamHandler): Unsubscribe;
    onSelectionChanged(handler: SelectionHandler): Unsubscribe;
}
```

#### 5.A.2 SprkChat Enhancements

- **Authentication**: Receive access token via URL parameter or `Xrm.Utility.getGlobalContext()` call
- **Session persistence**: Side pane preserves session across form navigations (same `sessionId`)
- **Context auto-detection**: When navigating to a new record, detect `entityType` + `entityId` change and offer to switch context or start new session
- **Responsive layout**: Full-height pane, adapts to 300-600px width range

#### 5.A.3 Key Decisions

- **No internal router**: SprkChat pane is single-purpose — just the chat interface
- **Session survives navigation**: User can browse records while keeping chat open
- **Playbook auto-selection**: Default playbook is selected based on `entityType` from playbook configuration

---

### Package B: Streaming Write Engine (P0, Large)

**Goal**: Enable token-by-token streaming writes into the RichTextEditor from AI tool responses.

#### 5.B.1 Backend: Streaming Document Events

**New SSE Event Types**:
```typescript
// Streaming write — token-by-token into editor
{ type: "document_stream_start", position: "after:section-id" | "end" | "replace:selection" }
{ type: "document_stream_token", content: "The " }
{ type: "document_stream_token", content: "document " }
{ type: "document_stream_token", content: "reveals..." }
{ type: "document_stream_end", summary: "Added regulatory compliance section" }

// Bulk replacement — for re-analysis results
{ type: "document_replace", content: "<full html>" }

// Progress — for long-running operations
{ type: "progress", content: "Analyzing document...", percent: 45 }
```

**New Tool Class: `WorkingDocumentTools`**

```csharp
public sealed class WorkingDocumentTools
{
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly IChatClient _chatClient;
    private readonly string _analysisId;  // constructor-injected, not LLM-visible

    // Tool 1: Stream an edit into the working document
    public async Task<string> EditWorkingDocumentAsync(
        [Description("Edit the analysis working document per user instruction")] string instruction,
        CancellationToken cancellationToken = default)

    // Tool 2: Append a new section to the working document
    public async Task<string> AppendSectionAsync(
        [Description("Add a new section to the analysis output")] string sectionTitle,
        string contentInstruction,
        CancellationToken cancellationToken = default)
}
```

**Streaming Flow**:
1. LLM calls `EditWorkingDocumentAsync("Add regulatory compliance section")`
2. Tool fetches current working document
3. Tool creates a focused `IChatClient` call with current doc + instruction
4. As tokens arrive from inner LLM call, they are emitted as `document_stream_token` SSE events
5. Frontend inserts tokens into editor in real time
6. On completion, `document_stream_end` signals the write is finished
7. Auto-save triggers

#### 5.B.2 Frontend: Lexical Streaming Insert

**New Lexical Plugin: `StreamingInsertPlugin`**
- Listens for `document_stream_start` → creates an insertion cursor at the target position
- On each `document_stream_token` → appends text at the cursor (with Markdown → Lexical node conversion)
- On `document_stream_end` → finalizes the insertion, pushes to version history
- Uses React 19 `startTransition` to keep the editor responsive during streaming
- Shows a pulsing cursor indicator where AI is writing

**New RichTextEditor Ref Methods**:
```typescript
interface IRichTextEditorRef {
    // Existing
    focus(): void;
    getHtml(): string;
    setHtml(html: string): void;
    clear(): void;

    // New — Streaming Write API
    beginStreamingInsert(position: InsertPosition): StreamHandle;
    appendStreamToken(handle: StreamHandle, token: string): void;
    endStreamingInsert(handle: StreamHandle): void;

    // New — Selection API (for Package G)
    getSelectedHtml(): string | null;
    replaceSelection(html: string): void;
    getSelectionRect(): DOMRect | null;
}
```

#### 5.B.3 Version History (Undo Support)

Every AI write operation snapshots the document state before modification:
- `useDocumentHistory` hook maintains a version stack (max 20)
- `pushVersion()` called before every AI-initiated change
- `undo()` / `redo()` restore snapshots
- Each AI operation = one undo step

---

### Package C: Analysis Workspace Code Page Migration (P0, Large)

**Goal**: Migrate the Analysis Workspace from a PCF control to a Code Page (React 19, full viewport).

#### 5.C.1 What It Becomes

A **2-panel document editing environment** deployed as a web resource:

```
┌──────────────────────────────┬──────────────────────────────┐
│      Analysis Output          │      Source Document          │
│      (RichTextEditor)         │      (Viewer)                 │
│                               │                               │
│  ← streaming write target     │  ← collapsible                │
│  ← diff view toggle           │  ← PDF/Office viewer          │
│  ← undo/redo toolbar          │                               │
└──────────────────────────────┴──────────────────────────────┘
```

SprkChat is **not embedded** — it lives in the side pane (Package A). Communication flows via `SprkChatBridge` (BroadcastChannel).

#### 5.C.2 Migration Steps

1. Create new Code Page: `src/client/code-pages/AnalysisWorkspace/index.tsx`
2. React 19 entry with `createRoot()` and `<FluentProvider>`
3. Port `AnalysisWorkspaceApp` layout to 2-panel (remove chat column)
4. Wire `SprkChatBridge` for cross-pane communication with SprkChat side pane
5. Port auto-save, export, and toolbar functionality
6. Remove legacy chat code (Section 4)
7. Register as web resource `sprk_AnalysisWorkspace`
8. Update Dataverse form to open via `Xrm.Navigation.navigateTo()` instead of PCF binding

#### 5.C.3 Component Reuse

| Component | Source | Migration Notes |
|-----------|--------|-----------------|
| `RichTextEditor` | `@spaarke/ui-components` | Already React 18/19 compatible |
| `SourceDocumentViewer` | `@spaarke/ui-components` | Already React 18/19 compatible |
| Toolbar (Save, Export, Copy) | `AnalysisWorkspaceApp` | Port to standalone component |
| Auto-save logic | `AnalysisWorkspaceApp` | Port using `useAutoSave` hook |

#### 5.C.4 SprkChatBridge Integration

```typescript
// In Analysis Workspace Code Page:
const bridge = new SprkChatBridge("analysis-workspace");

// Receive streaming writes from SprkChat side pane
bridge.onDocumentStreamStart((position) => {
    const handle = editorRef.current.beginStreamingInsert(position);
    streamHandleRef.current = handle;
});

bridge.onDocumentStreamToken((token) => {
    editorRef.current.appendStreamToken(streamHandleRef.current, token);
});

bridge.onDocumentStreamEnd(() => {
    editorRef.current.endStreamingInsert(streamHandleRef.current);
    triggerAutoSave();
});

// Send selection events to SprkChat side pane
const handleEditorSelection = (selectedText: string, rect: DOMRect) => {
    bridge.emitSelectionChanged(selectedText, rect);
};
```

---

### Package D: Action Menu / Command Palette (P0, Medium)

**Goal**: Provide a Claude Code-style `/` command menu in SprkChat for structured actions.

#### 5.D.1 Component: `SprkChatActionMenu`

A floating popover triggered by typing `/` as the first character in chat input:

```
┌─────────────────────────────────┐
│  / Command Menu                 │
├─────────────────────────────────┤
│  Playbooks                      │
│    Document Profile             │
│    Contract Review              │
│    Financial Analysis           │
│  Actions                        │
│    Re-analyze document          │
│    Summarize                    │
│    Extract key points           │
│    Export to Word               │
│  Search                         │
│    Search knowledge base        │
│    Web search                   │
│  Settings                       │
│    Switch document              │
│    Change mode (stream/diff)    │
└─────────────────────────────────┘
```

- Filterable: typing `/sum` narrows to "Summarize"
- Keyboard navigable (arrow keys + Enter + Escape)
- **Playbook-governed**: Only shows actions the current playbook supports (Section 3.3)

#### 5.D.2 Backend: `GET /api/ai/chat/actions`

Returns available actions based on session context and playbook capabilities:
```json
{
  "categories": [
    {
      "name": "Playbooks",
      "actions": [
        { "id": "switch-playbook:guid", "label": "Document Profile", "capabilities": ["search", "analyze", "write_back"] }
      ]
    },
    {
      "name": "Actions",
      "actions": [
        { "id": "reanalyze", "label": "Re-analyze document", "requiresCapability": "reanalyze" }
      ]
    }
  ]
}
```

#### 5.D.3 Reference Implementation

The PlaybookBuilder's `CommandPalette.tsx` and `SuggestionBar.tsx` provide proven patterns for:
- Keyboard navigation UX
- Action categorization
- Quick-filter search
- Contextual action visibility

---

### Package E: Re-Analysis Pipeline (P0, Medium)

**Goal**: Allow users to re-execute analysis with additional instructions through SprkChat.

#### 5.E.1 New Tool Class: `AnalysisExecutionTools`

```csharp
public sealed class AnalysisExecutionTools
{
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly string _analysisId;  // constructor-injected

    // Full re-analysis — reprocesses the source document
    public async Task<string> RerunAnalysisAsync(
        [Description("Re-execute the document analysis with added instructions")] string additionalInstructions,
        CancellationToken cancellationToken = default)

    // Conversational refinement — continues the analysis conversation
    public async Task<string> RefineAnalysisAsync(
        [Description("Refine the analysis with follow-up instructions")] string refinementInstruction,
        CancellationToken cancellationToken = default)
}
```

#### 5.E.2 Flow

1. User: "Rerun the analysis focusing on financial risk factors"
2. LLM calls `RerunAnalysisAsync("Focus on financial risk factors")`
3. Tool calls `_analysisService.ExecutePlaybookAsync()` with original playbook + appended instructions
4. Streaming analysis output emitted as `document_replace` SSE events
5. Editor replaces content with new analysis output (streaming progress shown)
6. Previous version pushed to undo stack
7. Chat message confirms: "Analysis re-executed with focus on financial risk factors."

#### 5.E.3 Key Decisions

- Re-analysis creates a **new version** — previous output preserved in undo stack
- CostControl middleware enforces token budget; confirmation prompt for expensive operations
- Progress events (`type: "progress"`) keep the UI responsive during long re-analysis

---

### Package F: Diff Compare View (P1, Medium)

**Goal**: Show AI-proposed changes as a diff before applying, similar to Claude Code's diff view.

#### 5.F.1 Component: `DiffCompareView`

```
┌────────────────────────────┬────────────────────────────┐
│  Original                  │  AI Proposed               │
├────────────────────────────┼────────────────────────────┤
│  ## Conclusion             │  ## Conclusion             │
│  The document shows        │- The document shows        │
│  some risk factors.        │+ The document demonstrates │
│                            │+ significant risk factors  │
│                            │+ warranting immediate      │
│                            │+ remediation.              │
└────────────────────────────┴────────────────────────────┘
                    [ Accept ] [ Reject ] [ Edit ]
```

- Side-by-side or inline diff modes
- Accept applies the change and pushes to undo stack
- Reject discards the proposal
- Edit opens the proposed text for manual modification before accepting

#### 5.F.2 Automatic Mode Selection

| Operation Type | Default Mode | Rationale |
|----------------|-------------|-----------|
| Add section / expand | Live streaming | User wants to see progress |
| Rewrite / revise existing | Diff review | User needs to verify changes |
| Selection-based revision | Diff review | Replacing specific text — show before/after |
| Re-analysis | Live streaming + replace | Full reprocessing — show progress |

Users can override via action menu: `/mode stream` or `/mode diff`.

---

### Package G: Selection-Based Revision (P1, Medium)

**Goal**: Select text in the editor, ask AI to revise it in-place.

#### 5.G.1 Cross-Pane Selection Flow

```
User selects text in Editor (Analysis Workspace Code Page)
       ↓
SprkChatBridge.emitSelectionChanged(selectedText, rect)
       ↓
SprkChat Side Pane shows refinement UI (SprkChatHighlightRefine)
       ↓
User enters instruction → POST /sessions/{id}/refine
       ↓
SSE streams refined text back
       ↓
SprkChat emits via bridge → Editor applies replacement
       ↓
Diff view shown (if mode=diff) OR direct replacement (if mode=stream)
```

#### 5.G.2 Editor Selection API

New ref methods on `RichTextEditor` (from Package B):
- `getSelectedHtml()` — get HTML of current selection
- `replaceSelection(html: string)` — replace selection with new content
- `getSelectionRect()` — bounding rect for floating toolbar positioning

#### 5.G.3 Dual Selection Sources

- **Editor selections**: Primary use case — revise text in the working document
- **Chat message selections**: Existing behavior — refine text from AI responses
- `SprkChatHighlightRefine` detects which source the selection is in and adjusts behavior

---

### Package H: Suggested Follow-Ups + Citations (P1, Medium)

**Goal**: After each response, show contextual follow-up suggestions. When AI references sources, provide clickable citations.

#### 5.H.1 Suggestions

**New SSE Event**:
```typescript
{ type: "suggestions", content: ["Expand on risk factors", "Compare with industry standards", "Export as executive brief"] }
```

**Component: `SprkChatSuggestions`** — clickable chips below the latest assistant message.

**Backend**: After main response, one focused LLM call (~100 tokens) generates 2-3 contextual suggestions. Emitted as final SSE event before `done`.

#### 5.H.2 Citations

**New SSE Event**:
```typescript
{ type: "citations", content: [
    { id: 1, source: "Contract_Amendment_v3.pdf", page: 4, excerpt: "..." },
    { id: 2, source: "Knowledge Base: Regulatory Requirements", chunkId: "abc123", excerpt: "..." }
] }
```

**Component: `SprkChatCitationPopover`** — clickable superscripts `[1]` in chat messages that open a popover with source details and "Open Source" link.

**Backend**: Modify search tools to return chunk IDs and source metadata. System prompt instructs AI to include citation markers.

---

### Package I: Web Search + Multi-Document (P2, Medium)

**Goal**: Add web search capability and support for multiple documents in a single session.

#### 5.I.1 Web Search

**New Tool Class: `WebSearchTools`**
```csharp
public sealed class WebSearchTools
{
    private readonly HttpClient _httpClient;
    private readonly string _bingApiKey;  // from Key Vault

    public async Task<string> SearchWebAsync(
        [Description("Search the web for current information")] string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
}
```

Uses Azure Bing Search API. Results marked as external content. Available via `/websearch` in action menu.

#### 5.I.2 Multi-Document Context

Extend `ChatKnowledgeScope` with `AdditionalDocumentIds`:
- Primary document in editor; secondary documents AI-accessible for cross-referencing
- Maximum 5 documents per session (token budget control)
- `SprkChatContextSelector` supports multi-select

---

## 6. Architecture Summary

### 6.1 New SSE Event Types

| Event Type | Package | Payload | Purpose |
|------------|---------|---------|---------|
| `token` | Existing | `{ content }` | Chat streaming text |
| `done` | Existing | `{ content: null }` | Stream complete |
| `error` | Existing | `{ content }` | Error message |
| `document_stream_start` | B | `{ position }` | Begin streaming write to editor |
| `document_stream_token` | B | `{ content }` | Single token for editor |
| `document_stream_end` | B | `{ summary }` | End streaming write |
| `document_replace` | E | `{ content }` | Bulk editor replacement (re-analysis) |
| `progress` | E | `{ content, percent }` | Long-running operation progress |
| `suggestions` | H | `{ content: string[] }` | Follow-up suggestions |
| `citations` | H | `{ content: Citation[] }` | Source references |

### 6.2 New AI Tool Classes

| Tool Class | Package | Methods | DI Impact |
|------------|---------|---------|-----------|
| `WorkingDocumentTools` | B | `EditWorkingDocumentAsync`, `AppendSectionAsync` | 0 (factory) |
| `AnalysisExecutionTools` | E | `RerunAnalysisAsync`, `RefineAnalysisAsync` | 0 (factory) |
| `WebSearchTools` | I | `SearchWebAsync` | 0 (factory) |

### 6.3 New Code Page Web Resources

| Web Resource | Package | React | Purpose |
|-------------|---------|-------|---------|
| `sprk_SprkChatPane` | A | 19 | Side pane chat — deployable on any form |
| `sprk_AnalysisWorkspace` | C | 19 | 2-panel editor + document viewer |

### 6.4 New Frontend Components

| Component | Package | Location | Purpose |
|-----------|---------|----------|---------|
| `SprkChatBridge` | A | `@spaarke/ui-components` | Cross-pane BroadcastChannel communication |
| `StreamingInsertPlugin` | B | `RichTextEditor` (Lexical) | Token-by-token editor inserts |
| `DiffCompareView` | F | `@spaarke/ui-components` | Side-by-side diff with accept/reject |
| `SprkChatActionMenu` | D | `SprkChat` | Command palette popover |
| `SprkChatSuggestions` | H | `SprkChat` | Follow-up suggestion chips |
| `SprkChatCitationPopover` | H | `SprkChat` | Citation source popover |

### 6.5 ADR Compliance

| ADR | Impact | Notes |
|-----|--------|-------|
| ADR-001 | Compliant | All new endpoints in existing Minimal API |
| ADR-006 | **Updated** | Analysis Workspace migrates from PCF to Code Page per ADR-006 guidance ("standalone → Code Page") |
| ADR-010 | Compliant | 0 additional DI registrations |
| ADR-012 | Compliant | Shared components in `@spaarke/ui-components`; `SprkChatBridge` as shared module |
| ADR-013 | Compliant | New tools follow `AIFunctionFactory.Create` pattern |
| ADR-021 | Compliant | All UI uses Fluent UI v9 |
| ADR-022 | **Relaxed** | Code Pages bundle React 19 (not platform-provided React 16) — consistent with ADR-022 Code Page guidance |

---

## 7. Parallel Execution Strategy

### 7.1 Package Dependency Graph

```
Package A (Side Pane) ──────────────┐
                                     ├── Package C (AW Migration) depends on A
Package B (Streaming Engine) ────────┤
                                     ├── Package E (Re-Analysis) depends on B
                                     ├── Package F (Diff View) depends on B
                                     └── Package G (Selection Revision) depends on B + C
Package D (Action Menu) ── independent (no dependencies)

Package H (Suggestions + Citations) ── depends on A (side pane must exist)
Package I (Web Search + Multi-Doc) ── depends on A (side pane must exist)
```

### 7.2 Sprint Execution Plan (Agent Teams)

**Sprint 1 — Foundation (3 parallel tracks)**:

| Track | Package | Team Focus | File Ownership |
|-------|---------|------------|----------------|
| Track 1 | A: SprkChat Side Pane | Frontend | `src/client/code-pages/SprkChatPane/`, `SprkChatBridge` |
| Track 2 | B: Streaming Write Engine | Full-stack | `RichTextEditor` plugins, `WorkingDocumentTools.cs`, SSE events |
| Track 3 | D: Action Menu | Frontend + API | `SprkChatActionMenu`, `/actions` endpoint |

**Sprint 2 — Integration (3 parallel tracks)**:

| Track | Package | Team Focus | File Ownership |
|-------|---------|------------|----------------|
| Track 1 | C: AW Code Page Migration | Frontend | `src/client/code-pages/AnalysisWorkspace/` |
| Track 2 | E: Re-Analysis Pipeline | Backend | `AnalysisExecutionTools.cs`, re-analysis flow |
| Track 3 | I: Web Search + Multi-Doc | Backend | `WebSearchTools.cs`, `ChatKnowledgeScope` |

**Sprint 3 — Polish (3 parallel tracks)**:

| Track | Package | Team Focus | File Ownership |
|-------|---------|------------|----------------|
| Track 1 | F: Diff Compare View | Frontend | `DiffCompareView` component |
| Track 2 | G: Selection-Based Revision | Full-stack | Selection API, cross-pane flow |
| Track 3 | H: Suggestions + Citations | Full-stack | `SprkChatSuggestions`, citation tracking |

### 7.3 Task Creation Guidance for `/project-pipeline`

Each package should be decomposed into tasks that maintain **clean file ownership boundaries** so agent teammates don't conflict:

| Package | Approximate Tasks | Key File Boundaries |
|---------|-------------------|---------------------|
| A | 12-15 | `code-pages/SprkChatPane/`, `SprkChatBridge.ts` |
| B | 15-20 | `RichTextEditor/plugins/`, `WorkingDocumentTools.cs`, SSE event types |
| C | 15-18 | `code-pages/AnalysisWorkspace/`, remove legacy from PCF |
| D | 10-12 | `SprkChatActionMenu.tsx`, `SprkChatInput` changes, `/actions` endpoint |
| E | 8-10 | `AnalysisExecutionTools.cs`, re-analysis orchestration |
| F | 8-10 | `DiffCompareView.tsx`, diff algorithm, mode toggle |
| G | 10-12 | Selection API methods, cross-pane selection flow |
| H | 10-12 | `SprkChatSuggestions.tsx`, `SprkChatCitationPopover.tsx`, citation tracking |
| I | 8-10 | `WebSearchTools.cs`, `ChatKnowledgeScope` extension |

**Total: 96-119 POML tasks**

---

## 8. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `BroadcastChannel` not available in all browsers | Low | High | Fallback to `window.postMessage`; detect at runtime |
| Streaming write causes Lexical state corruption | Medium | High | Extensive testing; `StreamingInsertPlugin` isolated from user edits during stream |
| Code Page migration breaks existing Dataverse form integrations | Medium | Medium | Maintain PCF as fallback during transition; feature flag |
| React 19 bundle size increases load time | Low | Medium | Code splitting; lazy load non-critical components |
| Cross-pane latency impacts streaming UX | Low | Medium | BroadcastChannel is synchronous within same origin; benchmark |
| Re-analysis token costs exceed budget | Medium | High | CostControl middleware; confirmation prompt |
| Diff view for large documents is slow | Medium | Medium | Virtual rendering; only diff visible sections |

---

## 9. Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| SprkChat accessible as side pane on any Dataverse form | Verified on Matter, Project, Analysis forms |
| AI streams edits into editor token-by-token | Visible streaming with <100ms per-token latency |
| Diff view shows before/after for revisions | Accept/reject workflow functional |
| Re-analysis reprocesses full document | New analysis output replaces editor content |
| Action menu responds to `/` in <200ms | Keyboard navigation works end-to-end |
| Analysis Workspace runs as Code Page (React 19) | No PCF dependency; full viewport layout |
| Playbook capabilities govern available tools | Switching playbook changes action menu + tool set |
| All features support dark mode | Visual inspection in both themes |
| Packages A, B, D executable in parallel | No file conflicts between tracks |
| 0 additional DI registrations | ADR-010 compliant |
| Legacy chat code fully removed | No `useLegacyChat` flag, no deprecated endpoints |

---

## 10. Out of Scope

- **PlaybookBuilder AI Assistant convergence** — Remains separate; future R3+ consideration
- **Real-time collaborative editing** (multiple users simultaneously)
- **Voice input** for chat
- **Mobile/responsive layout** for the workspace
- **Dataverse analysis persistence** (separate project: Task 032 in R1)
- **Custom playbook creation** from within SprkChat
- **Office Add-in integration** for SprkChat

---

## 11. Open Questions

1. **Side pane width**: Fixed 400px or user-resizable? Resizable adds complexity but improves UX.
2. **Cross-pane auth**: Should the side pane inherit the host form's auth token, or authenticate independently?
3. **Code Page transition**: Big-bang migration or incremental (PCF calls Code Page as embedded iframe)?
4. **Diff algorithm**: Use existing library (diff-match-patch, jsdiff) or build custom for HTML-aware diffing?
5. **Streaming cancellation**: When user cancels a streaming write, how to cleanly roll back partial inserts?
6. **PlaybookBuilder future**: Should Package D's action menu be designed with PlaybookBuilder adoption in mind (R3 convergence)?

---

*End of Design Document — Revision 2*
