# SprkChat Platform Enhancement R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-17
> **Source**: design.md (782 lines, iteratively developed from user testing feedback)
> **Predecessor**: ai-sprk-chat-workspace-companion (29 tasks, completed 2026-03-16)

## Executive Summary

SprkChat R1 established the AI workspace companion with contextual launch, inline AI toolbar, plan preview with write-back, slash commands, and quick action chips. R2 elevates SprkChat to a copilot-quality experience by addressing user testing gaps: raw markdown display, missing source document context, static commands, no streaming, no playbook dispatch, and no web search synthesis.

The centerpiece is the **Playbook Dispatcher + UI Handoff Pattern** — SprkChat recognizes user intent via metadata-driven matching and semantic search, executes playbook-defined AI preparation, and hands off to pre-populated dialogs for human completion. Playbooks define per-action whether steps require human confirmation (HITL) or execute autonomously.

---

## Scope

### In Scope

1. **Markdown rendering standardization** — single pipeline for chat messages, plan previews, editor insertion, and Word export
2. **Source document context injection** — conversation-aware semantic chunking within 30K token budget
3. **Write-back via SSE + BroadcastChannel** — streaming delivery from BFF, real-time editor update
4. **Dynamic slash commands** — metadata-driven resolution from playbooks and scopes (no relationship table)
5. **Playbook Dispatcher** — intent recognition, semantic matching, typed outputs (text/dialog/navigation/download/insert), HITL vs autonomous execution
6. **SSE streaming** — token-by-token chat responses via Server-Sent Events
7. **Web search synthesis** — AI-composed responses with citations, guided by knowledge scope search preferences
8. **Scope capabilities independent of playbooks** — scopes declare their own tools/actions
9. **Multi-document context + document upload** — analyze document sets, drag-and-drop upload with optional SPE persistence
10. **Open in Word** — analysis output converts to .docx, uploads to SPE, opens in Word Online
11. **AnalysisChatContextResolver real implementation** — connective tissue assembling capabilities, commands, documents, search guidance, and scope metadata
12. **JPS architecture extensions** — output node type, autonomous action flag, trigger metadata (Dataverse schema + JPS JSON)
13. **Playbook embedding index** — dedicated AI Search index for semantic playbook matching

### Out of Scope

- Voice input/output
- Mobile-responsive SprkChat layout
- Playbook builder UI (visual editor for creating playbooks)
- Multi-user collaborative chat sessions
- Offline/disconnected mode
- Custom AI model fine-tuning per tenant
- Static playbook relationship tables (replaced by metadata-driven matching)

### Affected Areas

- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` — markdown rendering, SSE streaming, playbook dispatch UX, citation cards, document upload zone
- `src/client/shared/Spaarke.UI.Components/src/services/` — new renderMarkdown utility, BroadcastChannel event types
- `src/client/code-pages/SprkChatPane/` — SSE connection, updated message rendering, document drag-and-drop
- `src/client/code-pages/AnalysisWorkspace/` — BroadcastChannel handlers for write-back, dialog open, Open in Word
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` — PlaybookDispatcher, AnalysisChatContextResolver, SSE streaming
- `src/server/api/Sprk.Bff.Api/Api/Ai/` — enhanced chat endpoints, Word export endpoint, document upload endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/` — enhanced WebSearchTools with scope guidance
- Dataverse schema — scope capabilities field, playbook trigger metadata fields, scope searchGuidance field
- JPS schema — output node type, autonomous action flag, trigger phrase metadata
- AI Search — new dedicated playbook-embeddings index

---

## Requirements

### Functional Requirements

1. **FR-01**: Chat messages render markdown as formatted HTML (headings, bold, code blocks, lists, tables) using a shared `renderMarkdown()` utility in `@spaarke/ui-components` — Acceptance: No raw markdown symbols visible in chat bubbles; dark mode renders correctly via semantic tokens
2. **FR-02**: SprkChat injects source document content into the chat system prompt when a session initializes, within a 30K token budget — Acceptance: AI can answer questions about document content without user pasting text
3. **FR-03**: Document context uses conversation-aware semantic chunking — re-selects relevant chunks after each user message when the document exceeds the token budget — Acceptance: Asking about section 8 of a long document surfaces section 8 content even if initial chunks were from sections 1-3
4. **FR-04**: Write-back content streams from BFF via SSE, is rendered token-by-token in the chat, then pushes final content to the Lexical editor via BroadcastChannel — Acceptance: Editor updates without page refresh; user sees streaming progress in chat
5. **FR-05**: Slash commands are dynamically resolved at session init from three sources: current playbook actions, context-matched playbook actions (metadata-driven), and scope capability commands — Acceptance: Different analysis record types show different available commands
6. **FR-06**: PlaybookDispatcher matches user natural language to playbooks via two-stage process: vector similarity search (dedicated AI Search index) → LLM refinement with parameter extraction — Acceptance: "send analysis by email to John" matches email playbook with extracted recipient
7. **FR-07**: Playbooks declare typed outputs (text, dialog, navigation, download, insert) via JPS `output` node — Acceptance: Email playbook opens email composer Code Page with pre-populated fields
8. **FR-08**: Playbook actions declare `requiresConfirmation: true/false` — HITL actions open dialogs for human completion; autonomous actions execute directly — Acceptance: Notification email sends without dialog; client email opens dialog for review
9. **FR-09**: Chat responses stream token-by-token via SSE with typing indicator — Acceptance: Visible progressive rendering matching copilot UX patterns
10. **FR-10**: Web search synthesizes results into AI-composed responses with numbered inline citations, guided by knowledge scope `sprk_searchGuidance` — Acceptance: Legal research queries prioritize authoritative legal sources per scope guidance
11. **FR-11**: Scopes declare capabilities independent of playbooks via `sprk_scope.sprk_capabilities` — Acceptance: Legal Research scope contributes `/search-legal-database` command regardless of which playbook is active
12. **FR-12**: Users can select multiple documents from AnalysisWorkspace file list for multi-document analysis (shared 30K token budget) — Acceptance: AI can cross-reference and compare across 5 documents in a single session
13. **FR-13**: Users can upload documents into SprkChat via drag-and-drop; uploaded docs are processed via Document Intelligence and injected into session context — Acceptance: Dragging a PDF into chat makes its content available for AI reasoning within ~10 seconds
14. **FR-14**: Uploaded documents can optionally be persisted to the matter's SPE container via user action ("save this to the matter files") — Acceptance: Saved documents appear in the matter's file browser
15. **FR-15**: "Open in Word" generates .docx from analysis output, uploads to SPE container, returns Word Online URL — Acceptance: User clicks button → Word Online opens with formatted analysis content
16. **FR-16**: AnalysisChatContextResolver replaces stub with real Dataverse queries assembling full session context (capabilities, commands, documents, search guidance, scope metadata) — Acceptance: SprkChat sessions show only capabilities from the analysis's playbook + matched playbooks + scopes
17. **FR-17**: Command catalog is auto-generated at session init from metadata-driven queries (no static relationship table) and cached in Redis (TTL 5 min) — Acceptance: Adding a new playbook with matching record type makes its commands appear in SprkChat without configuration
18. **FR-18**: Playbook trigger metadata stored as Dataverse fields (`sprk_triggerPhrases`, `sprk_recordType`, `sprk_entityType`, `sprk_tags`); output types and action flags stored in JPS JSON definition — Acceptance: Both queryable matching (Dataverse) and execution-time behavior (JPS) work correctly

### Non-Functional Requirements

- **NFR-01**: SSE streaming latency — first token visible within 500ms of send
- **NFR-02**: Document upload processing — text extraction completes within 15 seconds for documents under 50 pages
- **NFR-03**: Context resolver initialization — full context assembly completes within 3 seconds (with Redis cache)
- **NFR-04**: Playbook semantic matching — vector search + LLM refinement completes within 2 seconds
- **NFR-05**: Token budget enforcement — never exceed 128K total context window; graceful degradation with user notification
- **NFR-06**: Session-scoped uploads auto-deleted when chat session ends (security/privacy)
- **NFR-07**: All AI endpoint calls rate-limited per ADR-016
- **NFR-08**: Cache streaming tokens prohibited per ADR-014 — cache only final outcomes

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| **ADR-001** | BFF endpoints (SSE, export, upload) | MUST use Minimal API; MUST NOT introduce Azure Functions |
| **ADR-006** | Code Pages for dialogs (email composer) | Field-bound → PCF; standalone dialog → Code Page |
| **ADR-008** | All new endpoints | MUST use endpoint filters for auth; MUST NOT use global middleware |
| **ADR-009** | Context resolver caching, session state | MUST use Redis (`IDistributedCache`); MUST NOT cache auth decisions |
| **ADR-010** | PlaybookDispatcher, ContextResolver DI | ≤15 non-framework DI registrations; register concretes |
| **ADR-012** | SprkChat components, renderMarkdown | MUST use `@spaarke/ui-components`; callback-based props; React 18 compatible |
| **ADR-013** | AI tools, PlaybookDispatcher, streaming | MUST extend BFF (no separate AI service); MUST use SpeFileStore for files; MUST flow ChatHostContext |
| **ADR-014** | Context caching, embedding caching | MUST NOT cache streaming tokens; MUST version cache keys; MUST scope by tenant |
| **ADR-015** | Document injection, search synthesis | MUST send minimum text; MUST NOT log document contents |
| **ADR-016** | All AI endpoints (SSE, dispatcher, search) | MUST apply rate limiting; MUST bound concurrency; MUST return 429/503 ProblemDetails |
| **ADR-021** | All UI (markdown styles, citation cards, upload zone) | MUST use Fluent v9 tokens; MUST support dark mode; MUST NOT hard-code colors |
| **ADR-022** | Code Pages (SprkChatPane, AnalysisWorkspace, EmailComposer) | Code Pages use React 19 `createRoot()` bundled |

### MUST Rules

- MUST use single markdown parser across all surfaces (chat, editor, Word export)
- MUST sanitize rendered markdown HTML via DOMPurify before `dangerouslySetInnerHTML`
- MUST stream SSE via `text/event-stream` content type from Minimal API endpoints
- MUST NOT cache streaming tokens (ADR-014) — cache final write-back content only
- MUST NOT create static playbook relationship tables — use metadata-driven matching
- MUST NOT send full document content in Service Bus payloads (ADR-015)
- MUST NOT log extracted document text or email body content (ADR-015)
- MUST NOT exceed 128K total token context window — enforce budget partitioning
- MUST NOT allow unbounded `Task.WhenAll` on AI service calls (ADR-016)
- MUST scope all cache keys by tenant (ADR-014)
- MUST use endpoint filters for authorization on all new endpoints (ADR-008)
- MUST use `IDistributedCache` (Redis) for all cross-request caching (ADR-009)
- MUST flow `ChatHostContext` through full chat pipeline for entity-scoped search (ADR-013)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` for existing chat endpoint patterns
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs` for plan detection pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs` for resolver stub to replace
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` for existing chat component patterns
- See `src/client/code-pages/AnalysisWorkspace/src/services/markdownToHtml.ts` for existing markdown utility
- See `.claude/patterns/api/` for BFF endpoint patterns
- See `.claude/patterns/pcf/` for Code Page patterns

---

## Success Criteria

1. [ ] Chat messages render formatted text (headings, bold, code blocks, lists) — not raw markdown — Verify: visual inspection in browser
2. [ ] SprkChat references and reasons about source document content — Verify: ask about specific document sections
3. [ ] Conversation-aware chunking re-selects relevant chunks as conversation evolves — Verify: ask about different document sections in sequence
4. [ ] Slash commands change dynamically based on analysis context — Verify: different record types show different commands
5. [ ] PlaybookDispatcher matches "send this by email" to email playbook with extracted parameters — Verify: end-to-end test with email composer dialog
6. [ ] Playbooks with `dialog` output type open pre-populated Code Page dialogs — Verify: email composer opens with AI-generated content
7. [ ] Autonomous playbook actions execute without dialog; HITL actions require confirmation — Verify: test both paths
8. [ ] Chat responses stream token-by-token via SSE — Verify: visible progressive rendering
9. [ ] Web search synthesizes results with citations guided by scope search preferences — Verify: legal query uses legal scope guidance
10. [ ] Scope capabilities contribute commands independent of active playbook — Verify: Legal Research scope adds commands without playbook change
11. [ ] Multi-document analysis works with 5+ documents — Verify: cross-reference questions across documents
12. [ ] Document upload processes and injects into context — Verify: drag-and-drop PDF, then ask about its content
13. [ ] Uploaded documents can persist to SPE — Verify: save to matter files, confirm in file browser
14. [ ] "Open in Word" generates .docx and opens in Word Online — Verify: click button, Word Online opens with content
15. [ ] AnalysisChatContextResolver returns real capabilities (not stubbed) — Verify: different playbooks yield different tool sets
16. [ ] Write-back streams via SSE and updates Lexical editor via BroadcastChannel — Verify: no page refresh needed

---

## Dependencies

### Prerequisites

- R1 implementation complete (29 tasks) — SprkChat contextual launch, inline toolbar, plan preview, write-back plumbing
- Azure OpenAI `text-embedding-3-large` deployment active (for playbook embeddings)
- Azure AI Search service available (for dedicated playbook-embeddings index)
- Document Intelligence service available (for uploaded document processing)
- Redis configured and operational (ADR-009)

### External Dependencies

- Azure OpenAI streaming API (for SSE token streaming)
- Bing Web Search API or equivalent (for web search synthesis)
- SpeFileStore facade (for document content extraction and Word upload)
- Open XML SDK NuGet package (`DocumentFormat.OpenXml`) for Word generation
- `marked` or `markdown-it` npm package (standardize on one)
- `dompurify` npm package (HTML sanitization)

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Document chunking strategy | Should chunking be conversation-aware or static at session init? | **Conversation-aware** — re-select relevant chunks after each user message | BFF must re-evaluate chunk selection per turn; adds latency but improves accuracy for long documents |
| Uploaded document persistence | Should uploads be session-scoped throwaway or optionally persistent? | **Optional persist to SPE** — user can choose to save uploaded doc to matter's SPE container | Need SpeFileStore upload integration for chat-uploaded documents; UX needs "save to files" action |
| JPS architecture extensions | Dataverse schema changes, JPS JSON extensions, or both? | **Dataverse schema + JPS JSON** — trigger metadata on Dataverse (queryable), output types and action flags in JPS (execution-time) | Two-layer implementation: Dataverse fields for playbook discovery/matching, JPS JSON for runtime behavior |
| Playbook embedding storage | Dedicated AI Search index or shared RAG index? | **Dedicated index** — separate `playbook-embeddings` index for clean separation and independent scaling | New AI Search index to create; separate from document RAG index; purpose-built schema for playbook matching |

---

## Assumptions

*Proceeding with these assumptions (no explicit contrary guidance):*

- **Markdown parser**: Assuming `marked` (already present in some node_modules) unless evaluation shows `markdown-it` is superior — decision point during task implementation
- **SSE implementation**: Assuming `fetch` + `ReadableStream` (not `EventSource`) for POST-based SSE — standard pattern for non-GET streaming
- **Token counting**: Assuming tiktoken-compatible token counting for budget management — may need `@dqbd/tiktoken` or server-side counting
- **Playbook count**: Assuming <100 total playbooks in production — affects whether in-memory matching could supplement AI Search
- **Email sending**: Assuming Microsoft Graph `sendMail` API via BFF — no direct SMTP or third-party email service
- **Word template**: Assuming a basic Spaarke document template (header/footer/logo) exists or will be created during implementation
- **BroadcastChannel browser support**: Assuming modern Edge/Chrome only (Dataverse platform target) — no polyfill needed

---

## Unresolved Questions

*These may surface during implementation but are not blocking:*

- [ ] **Token counting precision**: Should we count tokens server-side (accurate) or client-side (faster feedback)? — Affects: document upload budget warnings
- [ ] **SSE reconnection**: What happens to in-flight streaming if the user navigates away and returns? — Affects: session recovery UX
- [ ] **Playbook embedding refresh**: On playbook create/update, should re-indexing be synchronous (blocking) or async (eventual consistency)? — Affects: admin UX when creating playbooks
- [ ] **Word re-import**: When user edits .docx in Word Online and returns to AnalysisWorkspace, should changes auto-sync back? — Affects: bidirectional sync complexity
- [ ] **Search API quota**: Bing Web Search API has rate limits — what's the expected query volume per hour? — Affects: caching strategy and cost projection

---

*AI-optimized specification. Original design: design.md*
