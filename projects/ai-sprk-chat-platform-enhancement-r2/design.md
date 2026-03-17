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

The centerpiece of R2 is the **Playbook Dispatcher + UI Handoff Pattern** — enabling SprkChat to recognize user intent, match it to a playbook via metadata-driven matching and semantic search (no static relationship tables), execute AI-assisted preparation, and hand off to a pre-populated dialog for human completion. Playbooks define whether each step requires human confirmation (HITL) or executes autonomously.

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

## 3. Write-Back via SSE + BroadcastChannel

### Problem

Current write-back goes directly from BFF → Dataverse (`IWorkingDocumentService`). The AnalysisWorkspace Lexical editor doesn't know the content was updated — user must refresh to see changes.

### Design

Write-back content is delivered from the BFF via **SSE streaming** (Section 6) and then routed to the Lexical editor via **BroadcastChannel**. This means the write-back content streams in real-time — the user sees it arriving token-by-token in the chat, and the final result is pushed to the editor.

**Flow**:
1. User approves plan in SprkChat → `POST /api/ai/chat/plan/approve`
2. BFF executes plan steps via SSE stream — content arrives incrementally (token events)
3. SprkChat renders the streaming response in the chat message (user sees progress)
4. On `done` event: BFF has written to Dataverse AND the SSE response includes the accumulated content
5. SprkChat posts `document_writeback` event via BroadcastChannel with the final content
6. AnalysisWorkspace's Lexical editor receives event → updates editor content
7. User sees the update immediately in both the chat and the editor — no refresh needed

**SSE Events During Write-Back**:
```
event: token
data: {"content": "## Patent Analysis Summary\n\n"}

event: token
data: {"content": "Based on the review of claims 1-14..."}

event: writeback_committed
data: {"analysisId": "...", "target": "sprk_analysisoutput.sprk_workingdocument", "contentLength": 4523}

event: done
data: {"sessionId": "...", "messageId": "...", "writebackContent": "<full content>"}
```

**BroadcastChannel Event** (client-to-client, after SSE completes):
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

Slash commands are dynamically resolved via **metadata-driven matching** — no static relationship table needed. The PlaybookDispatcher (Section 5) is the intelligence layer that resolves which playbooks and scopes are relevant to the current chat context.

**Why No Relationship Table**: A canonical N:N `sprk_playbook_relatedplaybooks` table would require manual curation every time a playbook is added or changed. Instead, the PlaybookDispatcher uses the existing metadata on playbooks and scopes — record type associations, capability declarations, tags, and scope bindings — to dynamically compute which commands are available. This is superior because:
- New playbooks are automatically discoverable if their metadata matches the context
- No maintenance burden of keeping relationship tables in sync
- The AI-driven matching (Section 5) can find relevant playbooks even when metadata is imperfect

**Command Sources**:

| Source | Example Commands | How Resolved |
|--------|-----------------|------------|
| **Current playbook** | `/analyze-clauses`, `/extract-dates` | Direct: from playbook's JPS action definitions |
| **Context-matched playbooks** | `/send-email`, `/generate-report` | PlaybookDispatcher queries playbooks matching current record type, entity type, and metadata tags |
| **Scope capabilities** | `/search-knowledge-base`, `/web-search` | From scopes with declared capabilities (Section 8) available to the current context |

**Metadata-Driven Matching Logic**:
```
1. Get current context: { recordType, entityType, documentType, tags }
2. Query all playbooks where:
   - sprk_playbook.sprk_recordtype matches current recordType
   OR sprk_playbook.sprk_tags overlap with context tags
   OR sprk_playbook.sprk_entitytype matches current entityType
3. Query all scopes where:
   - sprk_scope.sprk_capabilities is not empty
   AND scope is active/available
4. Build command catalog from union of:
   - Current playbook actions (always included)
   - Context-matched playbook actions (filtered by metadata)
   - Scope capability commands
```

**Command Catalog**: The command catalog is an **auto-generated** runtime artifact — not a curated table. It is built fresh by the `AnalysisChatContextResolver` (Section 11) each time a chat session initializes, based on the metadata queries above. The catalog is cached in Redis (TTL 5 min) per analysis context to avoid repeated Dataverse queries.

**Enhanced Context-Mappings Endpoint**:
```
GET /api/ai/chat/context-mappings/analysis/{analysisId}

Response (enhanced):
{
  "currentPlaybook": { "id": "...", "name": "Patent Analysis", "capabilities": [...] },
  "commandCatalog": [
    { "command": "/analyze-clauses", "description": "...", "source": "current-playbook", "playbookId": "..." },
    { "command": "/send-email", "description": "...", "source": "context-matched", "playbookId": "...", "matchReason": "recordType=sprk_matter" },
    { "command": "/search-knowledge", "description": "...", "source": "scope-capability", "scopeId": "..." }
  ],
  "capabilities": [100000000, 100000001, ...]
}
```

**Client-Side Rendering**:
- SlashCommandMenu groups commands by source: "This Analysis" / "Available Actions" / "Knowledge Sources"
- Each command shows a brief description and its origin
- Selecting a context-matched playbook command triggers PlaybookDispatcher (Section 5) to instantiate and execute that playbook

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
- **Side-effects are defined by the playbook, not blanket-restricted.** Some playbooks use HITL (human-in-the-loop) patterns where the AI prepares content and opens a dialog for human completion (e.g., email composer). Other playbooks may execute autonomous side-effects (e.g., sending an automatic notification email, creating a follow-up task). The playbook definition and its tool/handler configuration determine whether a step requires human confirmation or executes autonomously.
- Dialogs are reusable Code Pages — they work both when opened manually AND when opened by playbook output
- The playbook does the AI-heavy lifting (content generation, entity lookup); HITL dialogs handle human interaction where the playbook defines it
- Parameters are passed via URL query string (existing Code Page pattern)

#### 5.3 Command Catalog — How It's Created

The command catalog is **auto-generated at session initialization**, not manually curated:

1. **Current playbook actions**: Extracted directly from the playbook's JPS definition — each action node becomes a command entry with its name, description, and parameter schema
2. **Context-matched playbook actions**: The `AnalysisChatContextResolver` (Section 11) queries Dataverse for playbooks matching the current record type, entity type, and metadata tags. Each matched playbook's actions are added to the catalog with their source attribution.
3. **Scope capability commands**: Scopes with declared capabilities (Section 8) contribute their available actions (e.g., a "Legal Research" scope contributes `/search-legal-database`)

The catalog is a runtime-computed array cached in Redis. It refreshes when the analysis context changes or the cache TTL (5 min) expires.

#### 5.4 Semantic Search for Playbook Matching

For natural language intent matching (not explicit `/command` invocation), the PlaybookDispatcher uses:

1. **Embedding index**: Each playbook's description, action names, and trigger phrases are pre-embedded using the Azure OpenAI embeddings model (`text-embedding-3-large`). These embeddings are stored in **Azure AI Search** alongside the existing semantic search index infrastructure.
2. **Runtime matching**: When a user message doesn't match an explicit command, the dispatcher embeds the message and performs a cosine similarity search against the playbook embedding index.
3. **Top-k candidates**: The top 3-5 matches (above a similarity threshold) are sent to the LLM for final selection and parameter extraction.
4. **Fallback**: If no candidates exceed the threshold, the message passes through to normal chat processing.

This two-stage approach (fast vector search → LLM refinement) avoids sending the entire playbook catalog to the LLM on every message.

#### 5.5 Playbook Architecture Extensions Required

Achieving the dispatcher + UI handoff pattern may require enhancements to the existing playbook/JPS architecture:

| Extension | Purpose | Impact |
|-----------|---------|--------|
| **Output node type** | New JPS node type `output` that declares the playbook's output type (text, dialog, navigation, download, insert) | JPS schema extension |
| **Dialog output parameters** | JPS `output` node specifies Code Page name + parameter mapping from step results to dialog fields | JPS schema extension |
| **Autonomous action flag** | Per-action flag `requiresConfirmation: true/false` — determines HITL vs autonomous execution | Action schema extension |
| **Trigger metadata** | Playbook-level fields: `sprk_triggerPhrases` (text), `sprk_recordType` (lookup), `sprk_entityType` (option set), `sprk_tags` (text) | Dataverse schema additions |
| **Scope capability actions** | Scopes need action definitions (not just capability flags) so they can contribute commands to the catalog | Scope schema extension |
| **Embedding storage** | Playbook description embeddings stored in AI Search for semantic matching | AI Search index extension |

These extensions must be designed to be backwards-compatible — existing playbooks that lack the new fields should continue to work with text-only output and current-playbook-only resolution.

#### 5.6 Playbook Dispatcher Service (BFF)

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

SprkChat's `WebSearchTools` tool class exists but has no client-side rendering for search results. More importantly, users expect SprkChat to **synthesize** web search results into actionable insights — not just return raw links. When a user asks "as of March 2026, what are the best practices for non-compete clauses?", SprkChat should search the web, read the results, and compose a synthesized response with citations — similar to how Claude Code's web search provides insights, not just search result listings.

### Design

**Synthesized Search Experience**:

The core UX is: user asks a question → SprkChat searches the web → reads and synthesizes results → delivers a composed response with inline citations. The raw search results are available as expandable references, but the primary response is AI-synthesized.

**Example Flow**:
```
User: "What are the current best practices for non-compete clauses in employment agreements?"
    ↓
[WebSearchTools invoked by AI]
  - Searches Bing: "non-compete clause best practices 2026 employment law"
  - Returns top 5-8 results with snippets
    ↓
[AI synthesizes results into response]
  SprkChat: "Based on current guidance [1][2], non-compete clauses in 2026 should:

  1. **Be narrowly tailored** — limit geographic scope and duration (typically 12-18 months) [1]
  2. **Include consideration** — many states now require independent consideration beyond continued employment [3]
  3. **Comply with FTC guidance** — the FTC's 2024 rule banning most non-competes is under ongoing litigation [2][4]
  ...

  [1] Thomson Reuters: Non-Compete Agreements in 2026 — https://...
  [2] National Law Review: FTC Non-Compete Ban Status — https://...
  [3] ABA Journal: State-by-State Non-Compete Requirements — https://..."
```

**Two Modes**:

1. **AI-Invoked** (transparent): During reasoning, AI decides to search. Results are synthesized into the response with numbered citation references. The user sees a composed answer, not a list of links.
2. **User-Invoked** (`/search` or "search for..."): Explicit search with synthesized results. Optionally can show a search result panel for browsing raw results.

**Knowledge Scopes for Domain-Specific Search**:

Not all web searches are equal — legal research should target authoritative legal sources, financial research should target regulatory databases, etc. Knowledge scopes (Section 8) provide **search guidance** that directs the AI to the right sources:

| Knowledge Scope | Search Guidance | Target Sources |
|----------------|-----------------|----------------|
| "Legal Research" | Prioritize legal databases, law reviews, regulatory sites | Thomson Reuters, LexisNexis, court records, ABA, state bar associations |
| "Financial Compliance" | Prioritize regulatory and compliance sources | SEC.gov, FINRA, federal register, compliance journals |
| "Patent & IP" | Prioritize patent databases and IP law sources | USPTO, WIPO, Google Patents, IP law firms |
| "General Business" | Broad search with business credibility weighting | Industry publications, analyst reports, reputable news |

Each knowledge scope can have a `sprk_searchGuidance` field (text/JSON) that provides:
- Preferred domains to search
- Search query augmentation terms (e.g., always add "legal" to queries for the Legal Research scope)
- Source credibility ranking hints for the AI
- Excluded domains (e.g., exclude social media for legal research)

When SprkChat performs a web search, it checks which knowledge scopes are active in the current context and uses their search guidance to improve result quality. This connects directly to Section 8 (scope capabilities).

**Citation Rendering**:
- Search results referenced in AI response get numbered citations: `[1]`, `[2]`
- Citations rendered as clickable footnotes at bottom of message
- Each citation shows: title, URL, snippet, source credibility indicator
- Clicking opens the source in a new tab
- Expandable "View all sources" section shows full search results

**BFF Integration**:
- `WebSearchTools` already calls Bing/AI Search
- Enhance to accept search guidance from active knowledge scopes
- Add result metadata to the SSE stream so client can render citations
- AI receives full search result content (not just snippets) for deep synthesis
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

## 9. Multi-Document Context and Document Upload

### Problem

Users need to analyze multiple documents together: "Compare these 5 contracts" or "Summarize findings across all uploaded documents." Additionally, users should be able to **upload a document directly into the chat** to add it to the analysis context — e.g., drag-and-drop a PDF into SprkChat and say "compare this with the current analysis."

### Design

#### 9.1 Document Set from Analysis

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

#### 9.2 Document Upload into Chat

Users can upload a document directly into SprkChat to add it to the conversation context mid-session.

**UX**:
- Drag-and-drop zone in SprkChat input area (or attachment button)
- Upload triggers a processing pipeline before the document is usable
- SprkChat shows a processing indicator: "Processing document... (extracting text, analyzing structure)"
- Once processed, SprkChat confirms: "Added 'Contract_Amendment.pdf' to this conversation. I can now reference its contents."

**Processing Pipeline** (BFF):
1. **Upload**: File uploaded to BFF via `POST /api/ai/chat/{sessionId}/documents`
2. **Text extraction**: Document Intelligence extracts text and structure (tables, headings, sections)
3. **Chunking**: Content split into semantic chunks with embeddings generated
4. **Context injection**: Chunks added to the session's document context (within token budget)
5. **Temporary storage**: Uploaded document stored in session-scoped temp storage (Redis or blob), NOT persisted to SPE unless user explicitly saves

**Token Budget Impact**:
- Uploaded documents share the same 30K token budget as analysis documents
- If budget is exhausted, oldest/least-relevant chunks are evicted
- User is warned: "Document added but some earlier context was trimmed to fit. Ask me to focus on specific sections if needed."

**Supported Formats**: PDF, DOCX, PPTX, TXT, images (via Document Intelligence OCR)

**Security**: Uploaded documents are session-scoped and auto-deleted when the chat session ends. They are NOT indexed into AI Search or persisted to SPE unless the user explicitly chooses to save/associate them.

---

## 10. Open in Word

### Problem

Analysis output (stored as markdown/RTF in `sprk_analysisoutput.sprk_workingdocument` or the working file) needs to open directly in Microsoft Word for editing, sharing with clients, and stakeholder review. Users expect a "Open in Word" experience, not just a file download.

### Design

The analysis output content (markdown or RTF-equivalent) is converted to a Word-compatible format and opened in Word — either Word Online (via SharePoint/SPE) or Word Desktop.

**BFF Endpoint**:
```
GET /api/analysis/{analysisId}/export?format=docx&action=open
```

Query parameters:
- `format`: `docx` (default), `pdf`
- `action`: `open` (open in Word via SPE URL), `download` (return as file download)

**Implementation**:
- Use Open XML SDK (DocumentFormat.OpenXml NuGet package) — no external service needed
- Fetch `sprk_analysisoutput.sprk_workingdocument` content (markdown/RTF)
- Parse markdown/HTML → convert to Open XML paragraphs, tables, lists, headings
- Apply Spaarke document template (header, footer, logo)
- **For `action=open`**: Upload the .docx to SPE (associated container for the matter/project) → return the Word Online URL → client opens in new tab or iframe
- **For `action=download`**: Return `.docx` as file download

**Open in Word Flow**:
1. User clicks "Open in Word" in AnalysisWorkspace toolbar (or SprkChat `/open-in-word` command)
2. BFF generates .docx from current analysis output content
3. BFF uploads .docx to SPE container (associated with the matter/project)
4. BFF returns the SPE file URL (Word Online compatible)
5. Client opens URL — Word Online opens the document for editing
6. User edits in Word, saves back to SPE
7. If user returns to AnalysisWorkspace, they can re-import from the Word file

**Connection to Other Sections**:
- **Section 1 (Markdown Rendering)**: The same markdown parser used for chat rendering is used here for markdown → Open XML conversion
- **Section 3 (Write-Back)**: After write-back updates the analysis output, "Open in Word" generates from the latest content
- **Section 5 (Playbook Dispatcher)**: A playbook could have an output type of `download` that triggers Word export as its final step
- **Section 9 (Multi-Document)**: Export could include content from multiple document analyses in a single Word document

**Client Integration**:
- "Open in Word" button in AnalysisWorkspace toolbar (primary action)
- "Download as Word" option in dropdown (secondary)
- SprkChat commands: `/open-in-word`, `/export-word`
- PlaybookDispatcher can trigger export via `download` output type

---

## 11. AnalysisChatContextResolver — Real Implementation (Connective Tissue)

### Problem

The `AnalysisChatContextResolver` is currently **stubbed** — it returns all 7 capabilities regardless of the analysis playbook. This means every SprkChat session has access to every tool, which defeats the purpose of capability gating.

More importantly, this resolver is the **connective tissue** that ties together most of the other sections in this design. It is the single service that initializes a SprkChat session with all the context it needs — capabilities, commands, document content, scope guidance, and playbook metadata.

### Design

Replace the stub with a full implementation that orchestrates context assembly from multiple sources:

**Resolution Steps**:

1. **Query analysis record** → get `sprk_playbook` lookup, `sprk_sourcedocument`, record type, entity type, metadata tags
2. **Query current playbook** → get `sprk_playbookcapabilities`, JPS action definitions, output type declarations
3. **Query context-matched playbooks** (Section 4) → metadata-driven: find playbooks matching the analysis record type, entity type, and tags. No relationship table — pure metadata matching via the PlaybookDispatcher logic.
4. **Query available scopes** (Section 8) → get scope capabilities, search guidance, action definitions
5. **Fetch source document content** (Section 2) → extract text via SpeFileStore, chunk within token budget
6. **Build command catalog** (Section 4) → auto-generate from current playbook actions + context-matched playbook actions + scope capability actions
7. **Generate playbook embeddings check** (Section 5.4) → ensure playbook descriptions are indexed in AI Search for semantic matching
8. **Assemble web search guidance** (Section 7) → collect `sprk_searchGuidance` from active knowledge scopes
9. **Return full context** → capability set, command catalog, document content, search guidance, scope metadata

**How This Connects Everything**:

| Section | What the Resolver Provides |
|---------|---------------------------|
| Section 2 (Source Document) | Fetches and injects document content into session context |
| Section 4 (Dynamic Commands) | Builds the command catalog via metadata-driven playbook matching |
| Section 5 (Playbook Dispatcher) | Provides the playbook catalog that the dispatcher matches against |
| Section 7 (Web Search) | Collects search guidance from active knowledge scopes |
| Section 8 (Scope Capabilities) | Resolves scope-level capabilities independent of playbooks |
| Section 9 (Multi-Document) | Handles multi-document context assembly when multiple docs are associated |
| Section 10 (Open in Word) | Knows the SPE container association for Word export upload |

**Response Shape**:
```json
{
  "currentPlaybook": {
    "id": "...", "name": "Patent Analysis",
    "capabilities": [100000000, 100000001],
    "outputTypes": ["text", "insert"],
    "actions": [{ "name": "analyze-clauses", "description": "...", "params": [...] }]
  },
  "commandCatalog": [
    { "command": "/analyze-clauses", "source": "current-playbook", ... },
    { "command": "/send-email", "source": "context-matched", "matchReason": "recordType=sprk_matter", ... },
    { "command": "/search-legal", "source": "scope-capability", "scopeId": "...", ... }
  ],
  "capabilities": [100000000, 100000001, 100000003, 100000005],
  "documentContext": {
    "sourceDocumentId": "...",
    "contentAvailable": true,
    "tokenBudgetUsed": 24500,
    "documentCount": 1
  },
  "searchGuidance": {
    "preferredDomains": ["thomsonreuters.com", "lexisnexis.com"],
    "queryAugmentation": ["legal", "case law"],
    "excludedDomains": ["reddit.com"]
  },
  "scopeCapabilities": [
    { "scopeId": "...", "scopeName": "Legal Research", "capabilities": ["WebSearch", "KnowledgeRetrieval"] }
  ]
}
```

**Caching**: Redis cache with key `context:{analysisId}`, TTL 5 minutes. Cache is invalidated when the analysis record or its associations change.

---

## Technical Dependencies

| Dependency | Required For | Notes |
|-----------|-------------|-------|
| `marked` or `markdown-it` | Markdown rendering | Already in some node_modules; standardize on one |
| `dompurify` | HTML sanitization | Required for safe `dangerouslySetInnerHTML` |
| `DocumentFormat.OpenXml` | Word export / Open in Word | NuGet package, no external service |
| `EventSource` / `ReadableStream` | SSE streaming | Native browser APIs |
| SpeFileStore text extraction | Source document injection | May need new extraction method |
| Document Intelligence | Uploaded document text extraction | Existing Azure resource |
| Azure AI Search | Playbook embedding index, semantic chunking | Existing resource; new index for playbook embeddings |
| Azure OpenAI `text-embedding-3-large` | Playbook description embeddings | Existing deployment |
| Redis | Context caching, session state, uploaded doc temp storage | Already in use (ADR-009) |

---

## Affected Components

| Component | Changes |
|-----------|---------|
| `@spaarke/ui-components` (SprkChat) | Markdown rendering, SSE streaming, playbook dispatch UX, citation cards, document upload zone |
| `SprkChatPane` (Code Page) | SSE connection, updated message rendering, document drag-and-drop |
| `AnalysisWorkspace` (Code Page) | BroadcastChannel handlers for write-back, dialog open, and Open in Word events |
| `Sprk.Bff.Api` | PlaybookDispatcher, enhanced AnalysisChatContextResolver, SSE endpoints, Word export, source doc injection, document upload endpoint |
| Dataverse schema | Scope capabilities field, playbook trigger metadata fields (tags, recordType, entityType), scope searchGuidance field |
| JPS schema | Output node type, autonomous action flag, trigger phrase metadata |
| AI Search | New playbook embedding index for semantic matching |
| New Code Pages | Email Composer (sprk_emailcomposer), potentially others as dialog outputs |

---

## Success Criteria

1. Chat messages render formatted text (headings, bold, code blocks, lists) — not raw markdown
2. SprkChat can reference and reason about the source document content
3. Slash commands change dynamically based on analysis record type, playbook, and scope capabilities — no static relationship table
4. User can say "send this by email" and get a pre-populated email dialog (PlaybookDispatcher + UI handoff)
5. Playbooks can define autonomous side-effects (e.g., auto-notification) OR HITL dialogs — controlled by playbook definition
6. Chat responses stream token-by-token via SSE with typing indicator
7. Web search results are synthesized into AI responses with inline citations, guided by knowledge scope search preferences
8. Multiple documents can be analyzed in a single chat session
9. Users can upload documents directly into SprkChat to add to conversation context
10. Analysis output opens directly in Word (via SPE) for editing and sharing
11. AnalysisChatContextResolver is the connective tissue — assembles capabilities, commands, document content, search guidance, and scope metadata from Dataverse
12. Write-back updates stream via SSE and appear in the Lexical editor via BroadcastChannel without page refresh

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
| Token budget exhaustion with large documents | AI responses truncated or errors | Hard cap at 30K tokens for source content; semantic chunking for overflow; warn user when budget trimmed |
| PlaybookDispatcher false positives | Wrong playbook triggered | Two-stage matching (vector search → LLM); confidence threshold + user confirmation for < 0.8 |
| SSE connection drops | Lost streaming response | Reconnection logic with message ID resumption |
| Open XML complexity for rich formatting | Word export loses formatting | Start with basic formatting; iterate on fidelity |
| Scope capability explosion | Too many tools registered, confusing AI | Cap at 20 active tools per session; prioritize by relevance |
| Uploaded document processing latency | User waits for Document Intelligence extraction | Show processing indicator; allow chat to continue while processing runs async |
| Playbook embedding index staleness | New playbooks not found by semantic search | Re-index on playbook create/update; TTL-based refresh |
| Autonomous playbook side-effects | Unintended actions without user awareness | Playbook definition explicitly declares `requiresConfirmation` per action; audit logging for all autonomous actions |
