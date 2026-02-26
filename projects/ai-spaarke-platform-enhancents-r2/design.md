# SprkChat Interactive Collaboration — Design Document

> **Project**: ai-spaarke-platform-enhancements-r2
> **Author**: Ralph Schroeder / Claude Code
> **Date**: 2026-02-25
> **Status**: Draft — Pending Review

---

## 1. Executive Summary

SprkChat today is a **read-only AI assistant** — it can answer questions about documents, retrieve analysis results, search knowledge bases, and refine text passages. However, it cannot write back to the working document, re-execute analysis with user context, or provide an action-oriented command interface.

This project transforms SprkChat from a passive Q&A assistant into an **active AI collaborator** that can modify the working document, re-run analysis with user-supplied instructions, revise selected text inline, and expose a rich action menu for playbook switching, web search, and other operations.

### Driving User Expectations

Users working in the Analysis Workspace expect:

1. **Chat-directed document editing** — "Add a section about regulatory compliance" → AI updates the working document
2. **Re-analysis with context** — "Rerun the analysis but focus on financial risks" → AI re-executes with added instructions
3. **Selection-based revision** — Select text in the editor, ask AI to rewrite/expand/simplify it in-place
4. **Action menu / command palette** — Quick access to playbook switching, web search, export, and other operations (inspired by Claude Code's `/` command pattern)

### Scope

Nine feature phases organized by priority:

| Priority | Phase | Feature | Size |
|----------|-------|---------|------|
| P0 | 1 | Chat-to-Editor Write-Back | L |
| P0 | 2 | Re-Analysis with User Context | M |
| P0 | 3 | Action Menu / Command Palette | M |
| P1 | 4 | Selection-Based Revision (Editor) | M |
| P1 | 5 | Suggested Follow-Up Actions | S |
| P1 | 6 | Citation Grounding & Source Links | L |
| P2 | 7 | Web Search Integration | M |
| P2 | 8 | Undo AI Changes (Version Stack) | M |
| P2 | 9 | Multi-Document Context | S |

**Estimated task count**: 85–106 POML tasks across all phases.

---

## 2. Current Architecture (Baseline)

### 2.1 Frontend — Analysis Workspace (PCF)

The Analysis Workspace is a 3-column PCF control layout:

```
┌─────────────────────┬──────────────────┬──────────────────┐
│   Analysis Output   │  Source Document  │   Conversation   │
│   (RichTextEditor)  │  (Viewer)         │   (SprkChat)     │
│                     │                   │                  │
│  ← editable panel   │  ← read-only      │  ← AI chat       │
│  setHtml() / getHtml│  collapsible      │  350px min width  │
│                     │                   │                  │
└─────────────────────┴──────────────────┴──────────────────┘
```

**RichTextEditor** (Lexical-based):
- Ref API: `getHtml()`, `setHtml(html)`, `focus()`, `clear()`
- Props: `value`, `onChange`, `readOnly`, `isDarkMode`
- No streaming insert API — only full `setHtml()` replacement
- No external selection manipulation API

**SprkChat** component:
- Props: `sessionId`, `playbookId`, `documentId`, `hostContext`, `contentRef`, `predefinedPrompts`, `playbooks`, `documents`
- Sub-components: `SprkChatInput`, `SprkChatMessage`, `SprkChatContextSelector`, `SprkChatPredefinedPrompts`, `SprkChatHighlightRefine`
- Hooks: `useChatSession`, `useSseStream`, `useChatPlaybooks`
- `contentRef` monitors text selection for highlight-refine (currently pointed at chat message list)

**Critical Gap**: No message passing between SprkChat and RichTextEditor. They are sibling components in `AnalysisWorkspaceApp` with no shared state or callbacks for document mutation.

### 2.2 Backend — BFF API

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

All tools are **read-only** — none can modify the working document or trigger re-analysis.

**Analysis Service** (`IAnalysisOrchestrationService`):
- `ExecuteAnalysisAsync` — full streaming analysis
- `ContinueAnalysisAsync` — conversational refinement (existing but not wired to SprkChat)
- `SaveWorkingDocumentAsync` — persist to SPE + Dataverse
- `ExecutePlaybookAsync` — playbook-driven execution
- `GetAnalysisAsync` — fetch full record

**Middleware Pipeline** (ADR-013 / AIPL-057):
- ContentSafety → CostControl → Telemetry → IChatClient

**DI Budget**: 12 of 15 registrations used (ADR-010). New tools are factory-instantiated, not DI-registered — **0 additional DI slots required**.

### 2.3 Existing Building Blocks (Reusable)

| Component | Status | Reuse Opportunity |
|-----------|--------|-------------------|
| `SprkChatHighlightRefine` | Built | Extend to editor panel (Phase 4) |
| `SprkChatPredefinedPrompts` | Built | Extend with action categories (Phase 3) |
| `SprkChatContextSelector` | Built | Integrate into action menu (Phase 3) |
| `POST /sessions/{id}/refine` | Built | Wire to editor selections (Phase 4) |
| `PATCH /sessions/{id}/context` | Built | Expose via action menu (Phase 3) |
| `useChatPlaybooks` hook | Built | Feed action menu playbook list (Phase 3) |
| `ContinueAnalysisAsync` | Built | Wire to SprkChat for re-analysis (Phase 2) |
| `RichTextEditor` ref API | Built | Use `setHtml()` for write-back (Phase 1) |

---

## 3. Feature Phases — Detailed Design

### Phase 1: Chat-to-Editor Write-Back (P0, Large)

**Goal**: Allow SprkChat to programmatically update the working document in the RichTextEditor panel.

#### 3.1.1 Problem

When a user says "Add a section about regulatory compliance to the analysis," SprkChat can generate the content but has no mechanism to push it into the editor. The generated text appears only as a chat message — the user must manually copy-paste.

#### 3.1.2 Solution Architecture

```
User → SprkChat → BFF API (new tool) → LLM generates content
                                         ↓
                                   SSE event: { type: "document_update", content: "..." }
                                         ↓
SprkChat → onDocumentUpdate callback → AnalysisWorkspaceApp → editorRef.setHtml(merged)
```

**New SSE Event Type**:
```typescript
{ type: "document_update", content: "<html>...</html>", operation: "append" | "replace" | "insert_section" }
```

#### 3.1.3 Backend Changes

**New Tool Class: `WorkingDocumentTools`**

```csharp
public sealed class WorkingDocumentTools
{
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly IChatClient _chatClient;
    private readonly string _analysisId;  // constructor-injected, not LLM-visible

    // Tool 1: Update working document based on chat instruction
    public async Task<string> UpdateWorkingDocumentAsync(
        [Description("Update the analysis working document")] string instruction,
        CancellationToken cancellationToken = default)

    // Tool 2: Append a new section to the working document
    public async Task<string> AppendSectionAsync(
        [Description("Add a new section to the analysis output")] string sectionTitle,
        string contentInstruction,
        CancellationToken cancellationToken = default)
}
```

**Flow**:
1. LLM calls `UpdateWorkingDocumentAsync("Add regulatory compliance section")`
2. Tool fetches current working document via `_analysisService.GetAnalysisAsync()`
3. Tool sends current document + instruction to `IChatClient` with focused prompt
4. Tool returns the updated document content
5. Agent streams the response, endpoint emits `document_update` SSE event
6. Frontend applies the update to RichTextEditor

**New Endpoint Enhancement**: Modify `POST /sessions/{id}/messages` to detect `document_update` tool results and emit a new SSE event type alongside the normal `token` events.

#### 3.1.4 Frontend Changes

**AnalysisWorkspaceApp** — New callback bridge:
```typescript
// New prop on SprkChat
onDocumentUpdate?: (html: string, operation: "append" | "replace" | "insert_section") => void;

// In AnalysisWorkspaceApp:
const handleDocumentUpdate = useCallback((html: string, operation: string) => {
    if (operation === "replace") {
        editorRef.current?.setHtml(html);
    } else if (operation === "append") {
        const current = editorRef.current?.getHtml() ?? "";
        editorRef.current?.setHtml(current + html);
    }
    // Trigger auto-save
    onWorkingDocumentChange(editorRef.current?.getHtml() ?? "");
}, []);
```

**SprkChat** — Parse new SSE event:
```typescript
// In useSseStream hook, handle new event type:
case "document_update":
    props.onDocumentUpdate?.(event.content, event.operation);
    break;
```

#### 3.1.5 Key Decisions

- **Full document replacement vs. diff-based patching**: Start with full replacement via `setHtml()`. Diff-based patching (Phase 8 dependency) can be added later.
- **Auto-save after AI edit**: Trigger `onWorkingDocumentChange` callback after every document update so the existing auto-save mechanism persists changes.
- **Confirmation UX**: Show a toast/notification in the editor panel when AI updates the document: "Document updated by AI — review changes."

---

### Phase 2: Re-Analysis with User Context (P0, Medium)

**Goal**: Allow the user to re-execute the analysis with additional instructions or context provided through chat.

#### 3.2.1 Problem

After reviewing an initial analysis, users often want to refine the focus: "Rerun this but focus on financial risks" or "Redo the analysis including the amendment documents." Currently, re-analysis requires navigating back to the analysis setup and manually reconfiguring.

#### 3.2.2 Solution Architecture

**New Tool Class: `AnalysisExecutionTools`**

```csharp
public sealed class AnalysisExecutionTools
{
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly string _analysisId;  // constructor-injected

    // Tool 1: Re-execute analysis with additional user instructions
    public async Task<string> RerunAnalysisAsync(
        [Description("Re-execute the document analysis with added instructions")] string additionalInstructions,
        CancellationToken cancellationToken = default)

    // Tool 2: Continue analysis conversation (existing ContinueAnalysisAsync)
    public async Task<string> RefineAnalysisAsync(
        [Description("Refine the analysis with follow-up instructions")] string refinementInstruction,
        CancellationToken cancellationToken = default)
}
```

**Flow**:
1. User: "Rerun the analysis but focus on financial risk factors"
2. LLM calls `RerunAnalysisAsync("Focus on financial risk factors")`
3. Tool calls `_analysisService.ExecutePlaybookAsync()` with the original playbook + appended user instructions
4. Streaming analysis results flow back through SSE
5. New `document_update` event pushes the updated output to the editor

#### 3.2.3 Frontend Changes

- Show a progress indicator in the editor panel during re-analysis (replace content with spinner + "Re-analyzing...")
- Emit `document_update` SSE events as analysis completes, replacing the editor content
- Preserve chat history across re-analysis (the re-analysis is a tool call within the existing chat session)

#### 3.2.4 Key Decisions

- **Preserve vs. replace**: Re-analysis replaces the working document entirely. The original is recoverable via Phase 8 (Undo) or by re-fetching the prior analysis record.
- **Streaming**: Re-analysis streams progress events. The frontend shows a progress state rather than appearing frozen.
- **Token budget**: Re-analysis is expensive. The CostControl middleware (AIPL-057) enforces the existing budget. Consider a confirmation prompt: "This will re-analyze the document. Proceed?"

---

### Phase 3: Action Menu / Command Palette (P0, Medium)

**Goal**: Provide a Claude Code-style `/` command menu for quick access to playbook switching, predefined actions, web search, and other operations.

#### 3.3.1 Problem

SprkChat currently requires users to type natural language for every interaction. Power users want quick access to structured actions: switch playbooks, trigger specific analysis types, search the web, export results.

#### 3.3.2 Solution Architecture

**New Component: `SprkChatActionMenu`**

A floating popover triggered by typing `/` in the chat input field:

```
┌─────────────────────────────────┐
│  / Command Menu                 │
├─────────────────────────────────┤
│  📋 Playbooks                   │
│    ├─ Document Profile          │
│    ├─ Contract Review           │
│    └─ Financial Analysis        │
│  🔄 Actions                     │
│    ├─ Re-analyze                │
│    ├─ Summarize document        │
│    ├─ Extract key points        │
│    └─ Export to Word            │
│  🔍 Search                      │
│    ├─ Search knowledge base     │
│    └─ Web search (Phase 7)      │
│  ⚙️ Settings                    │
│    └─ Switch document           │
└─────────────────────────────────┘
```

#### 3.3.3 Frontend Changes

**`SprkChatActionMenu` component**:
- Triggered when user types `/` as the first character in the input
- Filterable — typing `/sum` narrows to "Summarize document"
- Keyboard navigable (arrow keys + Enter)
- Categories: Playbooks, Actions, Search, Settings
- Each action maps to either:
  - A pre-filled chat message (e.g., `/summarize` → "Please summarize the current document")
  - A direct API call (e.g., `/switch-playbook Contract Review` → `PATCH /context`)
  - A UI state change (e.g., `/export` → trigger export dialog)

**`SprkChatInput` enhancement**:
- Detect `/` prefix and show `SprkChatActionMenu` popover
- Pass selected action back as either a message or a callback invocation

#### 3.3.4 Backend Changes

**New Endpoint: `GET /api/ai/chat/actions`**

Returns available actions based on current session context:
```json
{
  "categories": [
    {
      "name": "Playbooks",
      "icon": "clipboard",
      "actions": [
        { "id": "switch-playbook:guid", "label": "Document Profile", "description": "..." }
      ]
    },
    {
      "name": "Actions",
      "icon": "wand",
      "actions": [
        { "id": "reanalyze", "label": "Re-analyze", "description": "Re-run analysis with new instructions" },
        { "id": "summarize", "label": "Summarize document", "description": "Generate executive summary" }
      ]
    }
  ]
}
```

Actions are context-sensitive — available actions depend on:
- Whether a document is loaded (show/hide document-specific actions)
- Whether an analysis exists (show/hide re-analysis)
- Available playbooks for the current entity type

#### 3.3.5 Key Decisions

- **`/` trigger only in empty or start-of-input**: Typing `/` mid-sentence does not trigger the menu
- **Extensible action registry**: Actions are defined server-side and can be extended without frontend changes
- **Keyboard-first**: Full keyboard navigation (arrow keys, Enter, Escape) for power users

---

### Phase 4: Selection-Based Revision in Editor (P1, Medium)

**Goal**: Allow users to select text in the RichTextEditor and have AI revise it in-place via SprkChat's refinement flow.

#### 3.4.1 Problem

`SprkChatHighlightRefine` already exists but is wired to the **chat message list** (`contentRef`). Users want to select text in the **editor panel** and have AI revise it directly in the working document.

#### 3.4.2 Solution Architecture

Two integration points:

1. **Editor-side selection detection**: Reuse `SprkChatHighlightRefine` but point its `contentRef` at the editor container
2. **Write-back after refinement**: Use `POST /sessions/{id}/refine` → SSE stream → replace selected text in editor

#### 3.4.3 RichTextEditor Enhancement

**New ref methods**:
```typescript
interface IRichTextEditorRef {
    // Existing
    focus(): void;
    getHtml(): string;
    setHtml(html: string): void;
    clear(): void;

    // New for Phase 4
    getSelectedHtml(): string | null;       // Get HTML of current selection
    replaceSelection(html: string): void;   // Replace selection with new content
    getSelectionRect(): DOMRect | null;      // Get selection bounding rect for toolbar positioning
}
```

These methods wrap Lexical's selection API to provide a clean external interface.

#### 3.4.4 Frontend Integration

```
User selects text in Editor
       ↓
SprkChatHighlightRefine detects selection (via editorContainerRef)
       ↓
User enters refinement instruction
       ↓
SprkChat calls POST /sessions/{id}/refine with { selectedText, instruction }
       ↓
SSE streams refined text back
       ↓
editorRef.replaceSelection(refinedText) — in-place replacement
```

**AnalysisWorkspaceApp** changes:
- Pass `editorContainerRef` to SprkChat as `contentRef` (instead of chat message list)
- Or: support **dual contentRef** — both editor and chat message list can trigger highlight-refine

#### 3.4.5 Key Decisions

- **Dual selection sources**: Support selection in both the editor (primary use case) and chat messages (existing behavior). The toolbar shows context-appropriate actions based on which panel the selection is in.
- **Preserve formatting**: When replacing selected text, preserve the surrounding HTML structure. The `replaceSelection()` method must handle Lexical's node tree correctly.
- **Undo integration**: Each selection replacement should push to the undo stack (Phase 8).

---

### Phase 5: Suggested Follow-Up Actions (P1, Small)

**Goal**: After each assistant response, show 2–3 contextual follow-up suggestions the user can click to continue the conversation.

#### 3.5.1 Solution

**New SSE Event Type**:
```typescript
{ type: "suggestions", content: JSON.stringify(["Expand on the risk factors", "Compare with industry standards", "Export as executive brief"]) }
```

**Backend**: After the main response completes, the agent makes one additional LLM call with a focused prompt:
> "Based on the conversation and your last response, suggest 2-3 concise follow-up questions or actions the user might want to take. Return as a JSON array of strings."

The suggestions are emitted as a final SSE event before `done`.

**Frontend**: New `SprkChatSuggestions` component renders clickable chips below the latest assistant message. Clicking a suggestion sends it as the next user message.

#### 3.5.2 Key Decisions

- **Cost**: One additional LLM call per response (~100 tokens). Use a small/fast model or cache common suggestion patterns.
- **Opt-out**: Include a setting to disable suggestions for users who find them distracting.
- **Contextual**: Suggestions should reference the current document and analysis state, not be generic.

---

### Phase 6: Citation Grounding & Source Links (P1, Large)

**Goal**: When the AI references specific content from the knowledge base or source document, provide inline citations that link back to the source.

#### 3.6.1 Problem

Users need to verify AI-generated claims. Currently, the AI may say "According to Section 4.2 of the agreement..." but provides no clickable link or exact source reference.

#### 3.6.2 Solution Architecture

**Backend — Citation Tracking**:
- Modify `DocumentSearchTools` and `KnowledgeRetrievalTools` to return chunk IDs and source metadata alongside content
- The agent's system prompt instructs it to include citation markers: `[1]`, `[2]`, etc.
- A post-processing step maps citation markers to source chunks

**New SSE Event Type**:
```typescript
{ type: "citations", content: JSON.stringify([
    { id: 1, source: "Contract_Amendment_v3.pdf", page: 4, excerpt: "..." },
    { id: 2, source: "Knowledge Base: Regulatory Requirements", chunkId: "abc123", excerpt: "..." }
]) }
```

**Frontend — `SprkChatCitationPopover`**:
- Render citation markers as clickable superscripts in chat messages
- Clicking opens a popover with source excerpt, document name, and "Open Source" link
- "Open Source" navigates to the document viewer at the relevant page/section

#### 3.6.3 Key Decisions

- **Chunk-level granularity**: Citations reference specific chunks from the RAG index, not entire documents
- **Best-effort**: Not all AI statements will have citations. The system encourages but does not force citation on every claim.
- **Performance**: Citation metadata is collected during tool calls and emitted once after the response completes (not inline with streaming tokens)

---

### Phase 7: Web Search Integration (P2, Medium)

**Goal**: Allow the AI to search the web for current information when the knowledge base doesn't have the answer.

#### 3.7.1 Solution Architecture

**New Tool Class: `WebSearchTools`**

```csharp
public sealed class WebSearchTools
{
    private readonly HttpClient _httpClient;
    private readonly string _bingApiKey;  // constructor-injected from Key Vault

    public async Task<string> SearchWebAsync(
        [Description("Search the web for current information")] string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
}
```

**Integration**: Uses Azure Bing Search API (already available in the Azure resource group). Results are formatted as a markdown list with titles, snippets, and URLs.

**Action Menu**: Web search appears in the `/` action menu under "Search" category.

#### 3.7.2 Key Decisions

- **Guardrails**: Web search results are clearly marked as external content. The system prompt instructs the AI to distinguish between knowledge base content and web results.
- **Cost**: Bing Search API has a per-query cost. Consider rate limiting or requiring explicit user opt-in via the action menu.
- **Content safety**: Web search results pass through the existing ContentSafety middleware.

---

### Phase 8: Undo AI Changes (P2, Medium)

**Goal**: Provide an undo/redo mechanism for AI-initiated document changes, allowing users to revert unwanted modifications.

#### 3.8.1 Solution Architecture

**New Hook: `useDocumentHistory`**

```typescript
const { canUndo, canRedo, undo, redo, pushVersion } = useDocumentHistory(editorRef, maxVersions);
```

- Maintains a version stack of HTML snapshots
- `pushVersion()` is called before every AI-initiated document update (Phase 1, 2, 4)
- `undo()` / `redo()` restore previous/next versions via `editorRef.setHtml()`
- Stack limited to `maxVersions` (default: 20) to prevent memory issues

**UI**: Undo/Redo buttons in the editor toolbar (or in the chat panel as a notification: "AI updated the document. [Undo]")

#### 3.8.2 Key Decisions

- **Scope**: Only AI-initiated changes are tracked in this stack. User manual edits use Lexical's built-in undo.
- **Granularity**: Each AI operation (write-back, re-analysis, selection revision) is one undo step.
- **Persistence**: Version stack is in-memory only (React state). Refreshing the page loses undo history. Full persistence is out of scope.

---

### Phase 9: Multi-Document Context (P2, Small)

**Goal**: Allow the AI to work with multiple documents simultaneously, comparing and cross-referencing content.

#### 3.9.1 Solution Architecture

**Backend**: Extend `ChatKnowledgeScope` to support multiple active document IDs:
```csharp
public record ChatKnowledgeScope(
    IReadOnlyList<string> RagKnowledgeSourceIds,
    string? InlineContent,
    string? SkillInstructions,
    string? ActiveDocumentId,           // Primary document
    IReadOnlyList<string>? AdditionalDocumentIds,  // New: secondary documents
    string? ParentEntityType,
    string? ParentEntityId);
```

**Frontend**: Extend `SprkChatContextSelector` to support multi-select for documents. The primary document remains in the editor; secondary documents are available to the AI for cross-referencing.

**Tools**: `AnalysisQueryTools` and `DocumentSearchTools` can query across all documents in the knowledge scope.

#### 3.9.2 Key Decisions

- **Primary vs. secondary**: Only the primary document appears in the editor. Secondary documents are AI-accessible only.
- **Token budget**: Multiple documents increase context size. The system prompt summarizes secondary documents rather than including full content.
- **Limit**: Maximum 5 documents in a single session to control token costs.

---

## 4. Architecture & Integration

### 4.1 New SSE Event Types (Summary)

| Event Type | Phase | Payload | Purpose |
|------------|-------|---------|---------|
| `token` | Existing | `{ content: string }` | Streaming text chunk |
| `done` | Existing | `{ content: null }` | Stream complete |
| `error` | Existing | `{ content: string }` | Error message |
| `document_update` | Phase 1 | `{ content: string, operation: string }` | Write to editor |
| `suggestions` | Phase 5 | `{ content: string[] }` | Follow-up suggestions |
| `citations` | Phase 6 | `{ content: Citation[] }` | Source references |
| `progress` | Phase 2 | `{ content: string, percent: number }` | Re-analysis progress |

### 4.2 New AI Tool Classes (Summary)

| Tool Class | Phase | Methods | DI Impact |
|------------|-------|---------|-----------|
| `WorkingDocumentTools` | 1 | `UpdateWorkingDocumentAsync`, `AppendSectionAsync` | 0 (factory) |
| `AnalysisExecutionTools` | 2 | `RerunAnalysisAsync`, `RefineAnalysisAsync` | 0 (factory) |
| `WebSearchTools` | 7 | `SearchWebAsync` | 0 (factory) |

All new tool classes follow the established pattern: constructor-injected infrastructure values, factory-instantiated in `SprkChatAgentFactory.ResolveTools()`, registered as `AIFunction` objects. **0 additional DI registrations required** (ADR-010 compliant).

### 4.3 New Frontend Components (Summary)

| Component | Phase | Parent | Purpose |
|-----------|-------|--------|---------|
| `SprkChatActionMenu` | 3 | `SprkChatInput` | Command palette popover |
| `SprkChatSuggestions` | 5 | `SprkChat` | Follow-up suggestion chips |
| `SprkChatCitationPopover` | 6 | `SprkChatMessage` | Citation source popover |

### 4.4 Modified Existing Components

| Component | Phases | Changes |
|-----------|--------|---------|
| `AnalysisWorkspaceApp` | 1, 4, 8 | Add editor↔chat bridge callbacks, undo buttons |
| `SprkChat` | 1, 3, 5 | New props (`onDocumentUpdate`), parse new SSE events |
| `SprkChatInput` | 3 | Detect `/` prefix, show action menu |
| `SprkChatHighlightRefine` | 4 | Support dual `contentRef` (editor + chat) |
| `RichTextEditor` | 4 | New ref methods: `getSelectedHtml()`, `replaceSelection()`, `getSelectionRect()` |
| `useSseStream` | 1, 5, 6 | Handle new SSE event types |
| `useChatSession` | 3 | Handle action menu commands |

### 4.5 Modified Backend Services

| Service | Phases | Changes |
|---------|--------|---------|
| `SprkChatAgentFactory` | 1, 2, 7 | Register new tool classes in `ResolveTools()` |
| `ChatEndpoints` | 1, 2, 3, 5, 6 | New SSE event emission, new `/actions` endpoint |
| `SprkChatAgent` | 5 | Post-response suggestion generation |

### 4.6 ADR Compliance

| ADR | Impact | Notes |
|-----|--------|-------|
| ADR-001 | Compliant | All new endpoints in existing Minimal API; no Azure Functions |
| ADR-006 | Compliant | All UI in PCF (AnalysisWorkspace) and shared components; no legacy JS |
| ADR-010 | Compliant | 0 additional DI registrations; tools are factory-instantiated |
| ADR-012 | Compliant | New shared components in `@spaarke/ui-components` |
| ADR-013 | Compliant | Extends AI Tool Framework pattern; new tools follow `AIFunctionFactory.Create` |
| ADR-021 | Compliant | All UI uses Fluent UI v9; dark mode supported |
| ADR-022 | Compliant | PCF components use React 16 APIs; shared components are compatible |

---

## 5. Implementation Sequencing

### 5.1 Phase Dependencies

```
Phase 1 (Write-Back) ──────────────────┐
    ↓                                   │
Phase 2 (Re-Analysis) ─── depends on ──┤
    ↓                                   │
Phase 4 (Selection Revision) ──────────┤
    ↓                                   │
Phase 8 (Undo) ──── depends on ────────┘
                     (all write phases)

Phase 3 (Action Menu) ── independent ── can parallel with Phase 1

Phase 5 (Suggestions) ── independent ── can follow any phase

Phase 6 (Citations) ── independent ── can follow Phase 1

Phase 7 (Web Search) ── independent ── can follow Phase 3

Phase 9 (Multi-Doc) ── independent ── can follow any phase
```

### 5.2 Recommended Execution Order

| Order | Phase | Rationale |
|-------|-------|-----------|
| 1st | Phase 1 (Write-Back) | Foundation for all write operations |
| 2nd | Phase 3 (Action Menu) | High user-facing value, independent |
| 3rd | Phase 2 (Re-Analysis) | Depends on Phase 1 write-back infrastructure |
| 4th | Phase 4 (Selection Revision) | Builds on Phase 1 + existing highlight-refine |
| 5th | Phase 5 (Suggestions) | Quick win, small scope |
| 6th | Phase 8 (Undo) | Requires all write phases to be stable |
| 7th | Phase 6 (Citations) | Large scope, independent timeline |
| 8th | Phase 7 (Web Search) | External dependency (Bing API) |
| 9th | Phase 9 (Multi-Document) | Lowest priority, smallest scope |

### 5.3 Parallelization Opportunities

- **Phase 1 + Phase 3** can develop in parallel (different file ownership: API tools vs. UI components)
- **Phase 5 + Phase 6** can develop in parallel after Phase 1 completes
- **Phase 7 + Phase 8** can develop in parallel

---

## 6. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `setHtml()` causes editor flicker during AI updates | Medium | Medium | Implement optimistic rendering; batch updates |
| Re-analysis token costs exceed budget | Medium | High | CostControl middleware enforces limits; add confirmation prompt |
| `/` command conflicts with user input | Low | Low | Only trigger when `/` is first character; Escape dismisses |
| Selection-based revision loses formatting | Medium | Medium | Implement Lexical-aware replacement; test with complex HTML |
| Undo stack memory usage | Low | Low | Cap at 20 versions; use compressed snapshots |
| Web search returns irrelevant/harmful content | Medium | Medium | ContentSafety middleware filters; clear external content markers |
| Multi-document context exceeds token limits | Medium | High | Summarize secondary documents; limit to 5 documents |

---

## 7. Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| User can update working document via chat | Chat instruction → document updated within 5 seconds |
| User can re-analyze with new context | Re-analysis completes and replaces editor content |
| Action menu is discoverable and fast | `/` shows menu in <200ms; keyboard navigation works |
| Selection revision works in editor | Select text → refine → text replaced in-place |
| Suggestions are contextually relevant | >70% of suggestions are actionable (user testing) |
| Citations link to correct sources | Citation click opens correct document/page |
| Undo reliably reverts AI changes | Undo restores exact previous state |
| All features support dark mode | Visual inspection in both themes |
| 0 additional DI registrations | `AiModule.cs` registration count unchanged |

---

## 8. Out of Scope

- **Real-time collaborative editing** (multiple users editing simultaneously)
- **Document version history persistence** (beyond in-memory undo stack)
- **Custom playbook creation** from within SprkChat
- **Voice input** for chat
- **Mobile/responsive layout** for the 3-column workspace
- **Dataverse write-back of analysis** (separate project: Task 032 in R1)

---

## 9. Open Questions

1. **Confirmation UX for destructive operations**: Should re-analysis require explicit user confirmation before replacing the working document? Or is undo sufficient?
2. **Citation format**: Should citations be numbered `[1]` or use author-date style? Numbered is simpler for AI to generate.
3. **Web search API choice**: Azure Bing Search vs. alternative? Bing is already in the Azure resource group.
4. **Action menu extensibility**: Should actions be purely server-driven (API returns available actions) or have some client-side registration for faster UX?
5. **Selection revision scope**: Should the highlight-refine toolbar also appear over the source document viewer (read-only panel), allowing users to ask questions about specific passages?

---

*End of Design Document*
