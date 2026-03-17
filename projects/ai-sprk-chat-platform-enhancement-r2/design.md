# SprkChat Platform Enhancement R2 — Design Document

> **Author**: Ralph Schroeder
> **Date**: March 17, 2026
> **Status**: Draft
> **Predecessor**: ai-sprk-chat-workspace-companion (29 tasks, completed March 16, 2026)

---

## Executive Summary

SprkChat R1 established the foundational AI workspace companion — contextual launch, inline AI toolbar, plan preview with write-back, slash commands, and quick action chips. This R2 project elevates SprkChat from a functional prototype to a copilot-quality experience by addressing gaps identified during user testing:

1. Chat messages display raw markdown instead of formatted text
2. SprkChat doesn't "see" the source document content
3. Tools and commands are static, not context-aware
4. No mechanism for playbooks to trigger from natural language or produce UI outputs
5. No streaming (SSE) — responses arrive in bulk
6. No web search integration
7. No multi-document analysis support
8. No export-to-Word capability

The centerpiece of R2 is the **Playbook Dispatcher + UI Handoff Pattern** — enabling SprkChat to recognize user intent, match it to a playbook, execute AI-assisted preparation, and hand off to a pre-populated dialog for human completion.

---

## 1. Markdown Rendering Standardization

### Problem

SprkChat messages currently display raw markdown (`**bold**`, `## heading`, `- list item`). The Analysis Output editor (Lexical) has a `markdownToHtml` utility, but chat messages don't use it. Two rendering paths exist but neither is applied consistently.

### Design

Standardize on a **single markdown rendering pipeline** used everywhere:

| Surface | Current | Target |
|---------|---------|--------|
| Chat messages (SprkChatMessage) | Raw text | Rendered HTML via markdown pipeline |
| Plan preview steps | Raw text | Rendered HTML via markdown pipeline |
| Analysis Output (Lexical editor) | markdownToHtml → Lexical nodes | Same pipeline, different output target |
| Insert-to-editor | Raw markdown inserted | Rendered Lexical nodes inserted |

**Implementation approach**:
- Use `marked` (already available) or `markdown-it` as the single parser
- Create a shared `renderMarkdown(content: string, options?: RenderOptions): string` utility in `@spaarke/ui-components`
- Options include: `sanitize` (default true), `citations` (inject citation links), `codeHighlight` (syntax highlighting)
- SprkChatMessage uses `dangerouslySetInnerHTML` with sanitized output (DOMPurify)
- Lexical editor uses the same parser but converts to Lexical node tree instead of HTML string

**Styling**:
- Markdown HTML rendered inside chat bubbles inherits Fluent v9 tokens
- Code blocks use monospace with subtle background (semantic token `colorNeutralBackground3`)
- Tables, lists, headings all styled to match Fluent v9 typography scale
- Dark mode support via semantic tokens (no hard-coded colors per ADR-021)

---

## 2. Source Document Context Injection

### Problem

SprkChat does not "see" the source document that the analysis is based on. When the user says "summarize the key findings," the AI has no document content to work with — only the analysis metadata.

### Design

Inject source document content into the chat context when a session is created.

**Flow**:
1. AnalysisWorkspace launches SprkChat with `analysisId` (existing)
2. SprkChat calls `GET /api/ai/chat/context-mappings/analysis/{analysisId}` (existing)
3. BFF resolves the analysis → finds the source document (via `sprk_analysis.sprk_sourcedocument` lookup)
4. **NEW**: BFF fetches document content via SpeFileStore facade (text extraction or cached semantic chunks)
5. Document content injected into the system prompt as context (with token budget cap)

**Token Budget Management**:
- Azure OpenAI GPT-4o context window: 128K tokens
- Reserve 30K tokens for source document content
- Reserve 40K tokens for conversation history
- Reserve 30K tokens for tool results and AI response
- Reserve 28K tokens for system prompt, playbook instructions, and metadata
- If document exceeds 30K tokens: use semantic chunking (AI Search) to inject only relevant sections based on conversation context
- Cache extracted text in Redis (TTL: 1 hour) to avoid repeated SpeFileStore calls

**Multi-Document Support** (see Section 9):
- When multiple documents are associated, the 30K budget is split across documents
- Semantic search selects the most relevant chunks from each document

---

## 3. Write-Back via BroadcastChannel

### Problem

Current write-back goes directly from BFF → Dataverse (`IWorkingDocumentService`). The AnalysisWorkspace Lexical editor doesn't know the content was updated — user must refresh to see changes.

### Design

Write-back should route through BroadcastChannel so the Lexical editor receives the update in real-time:

**Flow**:
1. User approves plan in SprkChat → `POST /api/ai/chat/plan/approve`
2. BFF executes plan steps, accumulates content
3. BFF writes to Dataverse (existing) AND returns accumulated content in response
4. SprkChat receives response → posts `document_writeback` event via BroadcastChannel
5. AnalysisWorkspace's Lexical editor receives event → updates editor content
6. User sees the update immediately without refreshing

**BroadcastChannel Event**:
```typescript
interface DocumentWriteBackEvent {
  type: 'document_writeback';
  analysisId: string;
  content: string;       // The written content (markdown or HTML)
  target: string;        // 'sprk_analysisoutput.sprk_workingdocument'
  mode: 'replace' | 'append';
  timestamp: number;
}
```

**Lexical Editor Handler**:
- On `document_writeback` event: parse content → convert to Lexical nodes → replace or append at cursor/end
- Show a toast notification: "Analysis output updated by AI"
- If editor has unsaved user edits, show conflict dialog: "AI wants to update the output. Replace or append?"

---

## 4. Dynamic Slash Commands from Context

### Problem

Current slash commands are static — hardcoded in the shared library. They don't change based on the analysis record type, playbook, or document type.

### Design

Slash commands are dynamically resolved from three sources:

| Source | Example Commands | Resolution |
|--------|-----------------|------------|
| **Current playbook** | `/analyze-clauses`, `/extract-dates` | From playbook's JPS action definitions |
| **Related playbooks** | `/send-email`, `/generate-report` | From playbooks related by record type or metadata tags |
| **Scope capabilities** | `/search-knowledge-base`, `/web-search` | From available scopes independent of playbooks |

**Architecture**:

1. **Playbook Relationship Model** (Dataverse):
   - New N:N relationship: `sprk_playbook_relatedplaybooks` (self-referential on `sprk_playbook`)
   - OR tag-based: `sprk_playbook.sprk_tags` (comma-separated tags like `email,communication,matter`)
   - Matching: record type + document type + metadata tags → candidate playbooks

2. **Enhanced Context-Mappings Endpoint**:
   ```
   GET /api/ai/chat/context-mappings/analysis/{analysisId}

   Response (enhanced):
   {
     "currentPlaybook": { "id": "...", "name": "Patent Analysis", "capabilities": [...] },
     "commandCatalog": [
       { "command": "/analyze-clauses", "description": "...", "source": "current-playbook", "playbookId": "..." },
       { "command": "/send-email", "description": "...", "source": "related-playbook", "playbookId": "..." },
       { "command": "/search-knowledge", "description": "...", "source": "scope-capability", "scopeId": "..." }
     ],
     "capabilities": [100000000, 100000001, ...]
   }
   ```

3. **Client-Side Rendering**:
   - SlashCommandMenu groups commands by source: "This Playbook" / "Related" / "Knowledge Sources"
   - Each command shows its source playbook name as a subtitle
   - Selecting a related-playbook command may trigger PlaybookDispatcher (Section 5)

---

## 5. Playbook Dispatcher — Intent Recognition and UI Handoff

### Problem

Today, playbooks are pre-selected when an analysis is created. There is no mechanism for SprkChat to dynamically recognize that a user's natural language request should trigger a different playbook, nor for a playbook to produce a UI output (open a dialog) rather than just text.

### Design

The **PlaybookDispatcher** is a new BFF service that sits between chat input and the tool executor.

#### 5.1 Intent → Playbook Matching

When a user sends a message, before normal chat processing:

1. **Fast check**: Does the message match any command from the command catalog? (exact `/command` match — skip AI)
2. **Semantic check**: Send user message + available playbook catalog (names, descriptions, trigger phrases) to LLM
3. LLM returns: `{ matched: true/false, playbookId: "...", confidence: 0.0-1.0, extractedParams: { recipient: "John Smith", ... } }`
4. If confidence > 0.8: auto-proceed with confirmation message
5. If confidence 0.5-0.8: ask user to confirm playbook selection
6. If confidence < 0.5: pass through to normal chat

**LLM Prompt for Matching**:
```
You are a playbook matcher. Given the user's message and the available playbooks,
determine if the user is requesting an action that matches a playbook.

Available playbooks:
{{commandCatalog}}

User message: "{{message}}"

Extract any parameters the user provided (names, dates, record references).
Return JSON: { matched, playbookId, confidence, extractedParams }
```

#### 5.2 Playbook Execution with UI Handoff

Once a playbook is matched, it executes within the chat session. The key innovation: **playbooks can have typed outputs including dialog opens**.

**Playbook Output Types**:

| Type | Behavior | Example |
|------|----------|---------|
| `text` | Content written to analysis output | "Here is the summary..." |
| `dialog` | Opens a Code Page with pre-populated parameters | Email dialog with AI-drafted body |
| `navigation` | Navigates to a Dataverse record or view | Open the matter record |
| `download` | Generates a file for download | Word document export |
| `insert` | Inserts content into Lexical editor | Analysis findings inserted at cursor |

**Dialog Output — The Core Pattern**:

```
User: "send analysis by email to the client"
    ↓
[PlaybookDispatcher]
  - Extracts: action=send_email, recipient_hint="the client"
  - Matches: "Send Email from Analysis" playbook (confidence: 0.92)
    ↓
SprkChat displays:
  "I'll help you send this analysis by email. Running the
   Send Email from Analysis playbook..."
    ↓
[Playbook Executes — AI steps]
  Step 1: Fetch sprk_analysisoutput content
  Step 2: Summarize for email (TextRefinementTools)
  Step 3: Resolve "the client" → lookup contacts on matter
          → John Smith (john.smith@client.com)
    ↓
[Playbook Output: dialog]
  Type: dialog
  CodePage: sprk_emailcomposer
  Parameters:
    to: "john.smith@client.com"
    subject: "Patent Analysis Summary — Matter 2024-0847"
    body: "<AI-generated email text>"
    regardingId: "<matter GUID>"
    regardingType: "sprk_matter"
    ↓
SprkChat: "I've prepared an email draft. Opening the email composer..."
    ↓
[BroadcastChannel event: open_dialog]
  → Host page (AnalysisWorkspace) receives event
  → Calls Xrm.Navigation.navigateTo({
       pageType: "webresource",
       webresourceName: "sprk_emailcomposer",
       data: "to=john.smith@client.com&subject=...&body=...&regardingId=..."
     })
    ↓
[Email Composer Dialog Opens — Code Page]
  To: john.smith@client.com (pre-filled)
  Subject: "Patent Analysis Summary — Matter 2024-0847" (pre-filled)
  Body: [AI-generated email, editable] (pre-filled)
  Attachments: [empty — user can add files]
  Associate to: Matter 2024-0847 (pre-filled)
  [Send] [Save Draft] [Cancel]
    ↓
User: reviews, tweaks wording, adds attachment, clicks Send
```

**Key Principles**:
- AI never performs side-effects (sending email, creating records) without human seeing the final form
- Dialogs are reusable Code Pages — they work both when opened manually AND when opened by playbook output
- The playbook does the AI-heavy lifting (content generation, entity lookup); the dialog handles the human interaction
- Parameters are passed via URL query string (existing Code Page pattern)

#### 5.3 Playbook Dispatcher Service (BFF)

```csharp
public interface IPlaybookDispatcher
{
    Task<PlaybookMatchResult> MatchAsync(
        string userMessage,
        IReadOnlyList<CommandCatalogEntry> availableCommands,
        ChatContext context,
        CancellationToken ct);

    Task<PlaybookExecutionResult> ExecuteAsync(
        string playbookId,
        Dictionary<string, string> extractedParams,
        ChatContext context,
        CancellationToken ct);
}

public record PlaybookMatchResult(
    bool Matched,
    string? PlaybookId,
    double Confidence,
    Dictionary<string, string>? ExtractedParams,
    string? ConfirmationMessage);

public record PlaybookExecutionResult(
    PlaybookOutputType OutputType,
    string? TextContent,
    DialogOutput? Dialog,
    NavigationOutput? Navigation,
    DownloadOutput? Download);

public record DialogOutput(
    string CodePageName,             // e.g., "sprk_emailcomposer"
    Dictionary<string, string> Parameters);

public enum PlaybookOutputType { Text, Dialog, Navigation, Download, Insert }
```

---

## 6. SSE Streaming

### Problem

Chat responses arrive in bulk after the AI finishes generating. Users see a loading spinner, then the full response appears at once. This is not the expected copilot UX — users expect token-by-token streaming.

### Design

Replace the current request/response pattern with Server-Sent Events (SSE):

**BFF Changes**:
- `POST /api/ai/chat/send` → returns `text/event-stream` content type
- Streams events: `token` (incremental text), `tool_call` (AI invoking a tool), `tool_result` (tool output), `plan_preview` (plan detected), `done` (stream complete)
- Uses Azure OpenAI streaming API (already supported by the SDK)

**Client Changes**:
- Replace `fetch` + `await response.json()` with `EventSource` or `fetch` + `ReadableStream`
- SprkChatMessage renders tokens as they arrive (append to content string)
- Cursor/typing indicator during streaming
- Plan preview card appears mid-stream when `plan_preview` event arrives

**SSE Event Format**:
```
event: token
data: {"content": "Based on my analysis"}

event: token
data: {"content": " of the patent claims,"}

event: tool_call
data: {"tool": "DocumentSearch", "query": "patent claims section 4"}

event: tool_result
data: {"tool": "DocumentSearch", "summary": "Found 3 relevant claims..."}

event: plan_preview
data: {"planId": "...", "steps": [...], "writeBackTarget": "..."}

event: done
data: {"sessionId": "...", "messageId": "..."}
```

---

## 7. Web Search Integration

### Problem

SprkChat's `WebSearchTools` tool class exists but has no client-side rendering for search results. Users need both AI-invoked search (during reasoning) and explicit `/search` commands.

### Design

**Two Modes**:

1. **AI-Invoked** (transparent): During chat, AI decides to search. Results appear as collapsible citation cards within the response.
2. **User-Invoked** (`/search` or "search for..."): Explicit search with results displayed as a search result panel.

**Citation Rendering**:
- Search results referenced in AI response get numbered citations: `[1]`, `[2]`
- Citations rendered as clickable footnotes at bottom of message
- Each citation shows: title, URL, snippet
- Clicking opens the source in a new tab

**BFF Integration**:
- `WebSearchTools` already calls Bing/AI Search
- Add result metadata to the SSE stream so client can render citations
- Cache search results per session to avoid duplicate queries

---

## 8. Knowledge Source Capabilities Independent of Playbooks

### Problem

Currently, tool capabilities are gated by `sprk_playbookcapabilities` on the playbook record. If a knowledge source (scope) has search capabilities, SprkChat can only use them if the current playbook declares that capability. This is too restrictive.

### Design

Add scope-level capabilities that are available regardless of which playbook is active:

**Dataverse Changes**:
- New field: `sprk_scope.sprk_capabilities` (multi-select option set, same values as playbook capabilities)
- Example: "Legal Knowledge Base" scope has capabilities: `DocumentSearch`, `KnowledgeRetrieval`

**Resolution Logic**:
```
Available capabilities =
  CurrentPlaybook.Capabilities
  ∪ RelatedPlaybooks.Capabilities
  ∪ AvailableScopes.Capabilities
```

**Context-Mappings Enhancement**:
- Endpoint returns scope capabilities alongside playbook capabilities
- Tool registration uses the union of all capability sources
- Each tool knows its source (playbook vs scope) for audit/logging

---

## 9. Multi-Document Context

### Problem

Users need to analyze multiple documents together: "Compare these 5 contracts" or "Summarize findings across all uploaded documents."

### Design

**Document Set Concept**:
- Transient (session-level): Array of document IDs passed as chat context parameter
- User selects documents from the AnalysisWorkspace file list → "Analyze selected" button
- SprkChat receives document set in launch context

**Context Injection for Multi-Doc**:
- 30K token budget split across documents (e.g., 5 docs × 6K tokens each)
- Semantic chunking selects most relevant passages from each document
- System prompt identifies each document by name/number for cross-referencing
- AI can reference specific documents: "In Document 3 (Patent Application.pdf), clause 4.2 states..."

**BFF Changes**:
- `ChatContext.DocumentIds` accepts array (currently single `SourceDocumentId`)
- Context resolver fetches and chunks all documents
- Token budget allocation strategy: equal split, or weighted by relevance

---

## 10. Export to Word

### Problem

Analysis output needs to be exportable as a Word document (.docx) for sharing with clients and stakeholders who don't have Dataverse access.

### Design

**BFF Endpoint**:
```
GET /api/analysis/{analysisId}/export?format=docx
```

**Implementation**:
- Use Open XML SDK (DocumentFormat.OpenXml NuGet package) — no external service needed
- Fetch `sprk_analysisoutput.sprk_workingdocument` content
- Parse markdown/HTML → convert to Open XML paragraphs, tables, lists, headings
- Apply Spaarke document template (header, footer, logo)
- Return `.docx` as file download

**Client Integration**:
- "Export" button in AnalysisWorkspace toolbar
- SprkChat can also trigger export via command: `/export-word`
- Dialog: choose format (Word, PDF), include/exclude sections

---

## 11. AnalysisChatContextResolver — Real Implementation

### Problem

The `AnalysisChatContextResolver` is currently **stubbed** — it returns all 7 capabilities regardless of the analysis playbook. This means every SprkChat session has access to every tool, which defeats the purpose of capability gating.

### Design

Replace the stub with a real Dataverse query:

1. Query `sprk_analysis` by ID → get `sprk_playbook` lookup
2. Query `sprk_playbook` → get `sprk_playbookcapabilities` (multi-select option set)
3. Query related playbooks (Section 4) → get their capabilities
4. Query available scopes (Section 8) → get their capabilities
5. Build and return the full capability set + command catalog

**Caching**: Redis cache with key `context:{analysisId}`, TTL 5 minutes.

---

## Technical Dependencies

| Dependency | Required For | Notes |
|-----------|-------------|-------|
| `marked` or `markdown-it` | Markdown rendering | Already in some node_modules; standardize on one |
| `dompurify` | HTML sanitization | Required for safe `dangerouslySetInnerHTML` |
| `DocumentFormat.OpenXml` | Word export | NuGet package, no external service |
| `EventSource` / `ReadableStream` | SSE streaming | Native browser APIs |
| SpeFileStore text extraction | Source document injection | May need new extraction method |
| Redis | Context caching, session state | Already in use (ADR-009) |

---

## Affected Components

| Component | Changes |
|-----------|---------|
| `@spaarke/ui-components` (SprkChat) | Markdown rendering, SSE streaming, playbook dispatch UX, citation cards |
| `SprkChatPane` (Code Page) | SSE connection, updated message rendering |
| `AnalysisWorkspace` (Code Page) | BroadcastChannel handlers for write-back and dialog open events |
| `Sprk.Bff.Api` | PlaybookDispatcher, enhanced context-mappings, SSE endpoints, Word export, source doc injection |
| Dataverse schema | Scope capabilities field, playbook relationships |
| New Code Pages | Email Composer (sprk_emailcomposer), potentially others as dialog outputs |

---

## Success Criteria

1. Chat messages render formatted text (headings, bold, code blocks, lists) — not raw markdown
2. SprkChat can reference and reason about the source document content
3. Slash commands change dynamically based on analysis record type and playbook
4. User can say "send this by email" and get a pre-populated email dialog
5. Chat responses stream token-by-token with typing indicator
6. Web search results appear as clickable citations
7. Multiple documents can be analyzed in a single chat session
8. Analysis output can be exported as a Word document
9. AnalysisChatContextResolver returns real capabilities from Dataverse (not stubbed)
10. Write-back updates appear in the Lexical editor without page refresh

---

## Out of Scope (R3+)

- Voice input/output
- Mobile-responsive SprkChat layout
- Playbook builder UI (visual editor for creating playbooks)
- Multi-user collaborative chat sessions
- Offline/disconnected mode
- Custom AI model fine-tuning per tenant

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| Token budget exhaustion with large documents | AI responses truncated or errors | Hard cap at 30K tokens for source content; semantic chunking for overflow |
| PlaybookDispatcher false positives | Wrong playbook triggered | Confidence threshold + user confirmation for < 0.8 confidence |
| SSE connection drops | Lost streaming response | Reconnection logic with message ID resumption |
| Open XML complexity for rich formatting | Word export loses formatting | Start with basic formatting; iterate on fidelity |
| Scope capability explosion | Too many tools registered, confusing AI | Cap at 20 active tools per session; prioritize by relevance |
