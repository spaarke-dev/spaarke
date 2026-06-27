# Spaarke AI Platform Unification R6 — Design

> **Project**: spaarke-ai-platform-unification-r6
> **Created**: 2026-06-06
> **Last refined**: 2026-06-07 (refinement against existing architecture docs + verified code state)
> **Status**: Design (pre-spec)
> **Predecessor**: R5 closed with known limitations after SC-18 walkthrough surfaced systemic architecture gaps (9 diagnostic cycles 2026-06-04 → 2026-06-06)
> **Forcing function**: R5 proved that "extending existing seams" is not viable when the underlying seams aren't actually implemented end-to-end on the chat-agent side. R6 is the convergence phase that aligns the chat-agent with the production-mature playbook side of the platform, builds the unimplemented typed tool handlers, adds the missing scopes, and closes the bidirectional workspace gap.
> **Framing**: The platform's playbook side (`PlaybookExecutionEngine`, JPS pipeline, scope library, `IAnalysisToolHandler` registry, safety pipeline, public contracts facades, Cosmos persistence, capability routing, 4-stage shell lifecycle) is production-mature. The chat-agent side carries hardcoded scaffolding (12 tool classes in C#, hardcoded persona, FK bypass in `SessionSummarizeOrchestrator`) that R5 surfaced via 9 diagnostic cycles. R6 converges the two sides on the same scope library + facade-routed invocation pattern (the pre-fill flow is the proof-of-concept). R6 is **convergence + filling specific gaps**, not invention.

---

## 1. Vision & Framing

### 1.1 What R6 is

R6 is the **convergence phase** that aligns the conversational chat-agent with the production-mature playbook side of the platform. The Scope library (Actions, Skills, Knowledge, Tools), JPS pipeline, `PlaybookExecutionEngine`, `IAnalysisToolHandler` registry, 11 node executors, safety pipeline, AI Public Contracts facades, Cosmos persistence, CapabilityRouter, 4-stage shell lifecycle, and pre-fill pattern are already production. R6 converges the chat-agent side onto these patterns: persona becomes a scope, the 12 hardcoded chat-tool classes become Dataverse-backed handlers, one generic `invoke_playbook` chat tool replaces the specialized bridges, the chat `/summarize` FK bypass is removed, the renderer becomes schema-aware, and the Assistant ↔ Context ↔ Workspace surface becomes tri-directional.

The R6 deliverable is **a platform where conversational + playbook modes share the same scope library, so authoring a capability once makes it available in both modes**. Plus building the 8 typed tool handlers (`EntityExtractorHandler`, `ClauseAnalyzerHandler`, etc.) that the platform's data model anticipates but only stubs in code today. After R6, R7+ feature work becomes "insert a scope + JPS prompt + reference it from a playbook" — not "write a tool class, write an orchestrator, write a renderer, wire a tab."

### 1.2 What R6 is NOT

- **Not building to Microsoft Agent Framework.** A Spaarke-side functional/requirements review concluded that Agent Framework is not appropriate for R6 (or near-term). R6 treats the in-process `PlaybookExecutionEngine` as the engine, full stop. No "future replacement" abstractions, no "backend flexibility" seams, no Agent Framework framing anywhere in R6 design or implementation. If Agent Framework is later evaluated, that is a separate project's call.
- **Not a replacement for what works.** The Scope library, JPS pipeline, 11 node executors, playbook visual canvas, pre-fill flow, safety pipeline, public contracts facades, Cosmos persistence, CapabilityRouter, M365 Copilot integration, 4-stage shell lifecycle — all stay exactly as-is. R6 builds on them; never around them.
- **Not a rewrite.** The core services (`SessionSummarizeOrchestrator`, `SprkChatAgent`, `RagService`, `PlaybookExecutionEngine`) survive. R6 wires the chat path through the playbook engine; it does not replace the engine.
- **Not a UI redesign.** The three-pane shell, SprkChat component, workspace tab strip — all stay. The pane communication layer becomes bidirectional + adds a Context-pane execution-trace widget (Pillar 6), but the visual model doesn't change radically.
- **Not invention.** Most of what design.md previously framed as "build" is actually "reuse" — see §3.12 What Already Exists. Genuine new work is concentrated in: 8 typed handlers, persona scope, tri-directional state model, schema-aware renderers, command router, cross-conversation memory utilization.
- **Not optional.** The patterns in R6 are the ones every "AI assistant + workspace" product (Cursor, Linear AI, Claude Artifacts, GitHub Copilot Workspace) has converged on. R6 brings Spaarke to category baseline.

### 1.3 Why this matters

R5's SC-18 walkthrough produced 9 diagnostic cycles surfacing bugs that all trace to the SAME ROOT CAUSE: the platform's abstractions are incomplete or bypassed. Examples R5 hit:

- **Cycle 3**: `BuildKnowledgeDocuments` emitted `Tags = null` because the model wasn't designed for the asymmetric-schema case (R5 added a fourth Azure Search index without reconciling the producer model).
- **Cycle 4**: 8 customer-corpus-only fields serialize for session-files writes because the model is shared across indexes with no per-index discipline. R5 fixed at the wire layer with `JsonIgnore.WhenWritingNull` but the underlying design problem (one model, many schemas, no factory pattern) persists.
- **Cycle 5**: Two upload paths (paperclip and `[action:upload]`) produced two different session states because the platform never decided whether file holding is client-side or server-side. R5 chose intent-driven but didn't define when each path applies.
- **Cycle 6**: `useChatFileAttachment.handleAttachInputChange` cleared the FileList before passing it to the hook because `e.target.value = ''` mutates the live FileList — a pre-existing bug that worked around because the paperclip had been broken anyway.
- **Cycle 7**: PDF extraction failed because pdfjs Web Worker URL was unconfigured — a known requirement of pdfjs v4+ that the platform hadn't accommodated.
- **Cycle 8**: `DocumentViewerWidget` shipped as a stub (R5 task 022 deferred), still mounted on attachment-ready, showing "Preview not available" to confused users.
- **Cycle 9**: SSE bridge emitted `$.tldr` (JSONPath) but widget schema expected `tldr` (bare key) — the IncrementalJsonParser convention and the rendering schema convention were never reconciled.

**Plus deferred items** that ARE bugs and SHOULD be visible:

- TL;DR (array field) renders raw JSON tokens (`"data loss.","Organizations miss..."`) because the renderer doesn't parse array-typed structured outputs.
- Entities (object field) renders raw JSON tokens (`organizations":["ACME Corporation"]`) for the same reason.
- Duplicate fire: typing `/summarize` produces a summary in the Assistant pane AND a (partly broken) summary in the Workspace pane. Two LLM calls; user sees two outputs.
- The playbook `summarize-document-for-chat@v1` exists in Dataverse but isn't FK-wired to its action `SUM-CHAT@v1`; runtime works because the orchestrator bypasses the playbook entity and loads the action directly by code.
- The chat persona is hardcoded in `BuildDefaultSystemPrompt`. No prompt engineering process, no eval harness, no tenant override, no Dataverse-driven configuration. Yet this is the production AI assistant's voice.

Every one of these is fixable, but they share a structural cause: **the platform's abstractions aren't load-bearing yet**. The entities and concepts exist (playbook, action, tool, persona, widget) but the code paths often shortcut around them. R6 makes them load-bearing.

### 1.4 What R6 ships visibly

End-user-visible changes are deliberately small in R6 — this is foundation work:

- Workspace summary actually renders properly (TL;DR as bullets, Entities as orgs/persons lists)
- Only ONE summary appears per `/summarize` (no duplicate)
- Adding a new playbook to Dataverse appears in the assistant's available capabilities WITHOUT a code deploy
- A "Send to Workspace" affordance lets users promote chat content to workspace artifacts
- "Add to Assistant" lets users expose a user-created workspace tab to the agent
- The assistant knows what's on the workspace (open tabs, active tab, user selection) and can act on it
- The Context pane shows what the AI is doing in real time (tool calls, knowledge sources, decisions — Claude-Code-like process stream)
- Longer conversations retain key context across sessions (cross-conversation memory utilization)
- Persona is tenant-customizable without code deployment
- Conversational refinement, follow-up, comparison across past sessions — all preserved and enhanced (Pillar 7 memory utilization)

That's a deliberate restraint — most R6 work is invisible plumbing. The visible reward comes in R7 when adding a new AI capability takes a week not a sprint.

### 1.5 Foundational Principle — LLM as augmented partner

**The LLM is the primary cognitive agent. The scope library augments it. Playbooks are one form of capability the LLM can choose to invoke.** This principle is binding for every R6 pillar.

Three implications:

1. **Conversational + playbook are not separate worlds.** The chat-agent has access to all registered scopes (Actions, Skills, Tools, Knowledge, Persona, Outputs). Playbooks are a way to compose these into deterministic multi-step workflows. The conversational mode uses the same primitives via natural-language interaction. After R6, authoring a capability once (as a scope or playbook) makes it available in both modes — the chat-agent reads `sprk_analysistool` rows the same way the playbook side does, with an `AvailableInContexts` discriminator.

2. **LLM strengths are preserved.** R6 does not constrain the LLM's free-form conversational ability. Structure is augmentation, not subordination. The `CapabilityRouter` (production R2) restricts the tool list per turn but never prevents conversational response.

3. **Convergence, not invention.** The playbook-side infrastructure (Scope library, JPS pipeline, tool handler registry, execution engine, node executors, safety pipeline, public contracts facades, Cosmos persistence, capability routing, stage lifecycle) is production-mature. R6 aligns the chat-agent side with it; R6 does not rebuild any of these. See §3.12 for the inventory of what already exists.

### 1.6 Conversational primacy is non-negotiable

The conversational chat experience is the **primary user experience** of the Spaarke AI Assistant. Playbooks are a structured capability the LLM can choose; they are not the dominant or exclusive form of work. Most user turns never invoke a playbook at all — the LLM responds using its native capabilities augmented by bound persona + skills + knowledge.

R6 commits to:

1. **The LLM remains the primary cognitive agent.** Free-form dialogue, refinement, follow-up questions, mid-conversation context injection ("oh and also consider..."), comparison across past sessions ("how does this compare to yesterday's contract?") — all of these work because the LLM has full conversation context (Cosmos `sessions` container, production) + cross-conversation memory (Cosmos `memory` container, production infrastructure with R6 utilization) + bound knowledge (RAG-indexed corpus via L1/L2/L3 retrieval, production).

2. **The scope library augments, never replaces.** Skills configure tone/technique via prompt fragments. Knowledge grounds responses via injected text or RAG retrieval. Tools provide deterministic sub-capabilities the LLM can choose. Persona defines voice. Output schemas declare rendering destination. None short-circuit the LLM's conversational ability.

3. **Playbooks are one tool among many.** Available via the generic `invoke_playbook(playbookId, params)` chat tool when the LLM determines structured multi-step work is needed (Pillar 3). After playbook invocation, conversation resumes normally — the playbook output is just another message in conversation history. The user can immediately ask follow-up questions, request refinements, or pivot to an unrelated topic.

4. **CapabilityRouter restricts tool list, not LLM ability.** Per-turn intent classification (keyword → GPT-4o-mini → broad superset fallback) narrows the function-calling tool set the LLM sees but never prevents the LLM from responding conversationally. Layer 3 fallback gives the LLM the broad superset when intent is unclear.

5. **Conversation history + memory are the substrate for refinement.** Cosmos `sessions` (per-session message history, 90d) + Cosmos `memory` (cross-conversation matter-scoped recall, 90d) + bound Knowledge (RAG-indexed corpus) together provide the context the LLM uses for natural refinement, comparison, follow-up, and context injection. R6 Pillar 7 activates utilization of the existing infrastructure.

6. **The user can always pivot.** Mid-summarization, the user can ask an unrelated question and the LLM handles it conversationally. Mid-conversation, the user can invoke a playbook explicitly via slash command. No mode-lock. No conversation gets "stuck" in a playbook flow.

7. **Workspace tabs are conversational artifacts.** With the bidirectional state model (Pillar 6), what's on the workspace is part of the conversation context. "Look at the third tab" / "Update the summary to be shorter" / "Compare these findings to the contract on the second tab" are answerable because the LLM can read workspace state via per-widget `getAgentVisibleState()` (Pillar 9).

8. **Every R6 pillar serves conversational UX**:
   - Pillar 1 (Persona scope): makes voice tunable; LLM's conversational ability unchanged
   - Pillar 2 (Tool registry + 8 typed handlers): more deterministic capabilities the LLM CAN invoke when it chooses
   - Pillar 3 (Generic `invoke_playbook`): replaces specialized bridges; LLM decides when to invoke a playbook vs respond conversationally
   - Pillar 4 (FK redirect): architectural cleanup; conversational UX unchanged
   - Pillar 5 (Output-type + schema-aware rendering): fixes how playbook output displays; doesn't constrain conversation
   - Pillar 6 (Tri-directional state + Context trace): ENHANCES conversational ability — LLM can reference workspace state in conversation; user sees what the LLM is doing
   - Pillar 7 (Memory utilization): MOST DIRECT enabler of conversational refinement — cross-conversation recall makes "yesterday's contract" meaningful
   - Pillar 8 (Command router): slash commands COMPLEMENT conversation, not replace it; natural language requests still work
   - Pillar 9 (Widget visibility contract): lets the LLM see workspace state without bloating prompt context; enables workspace-aware conversation

The conversational user experience is the product. The scope library and playbooks are how Spaarke specializes the LLM for legal-operations work. R6 makes that specialization data-driven and the conversation surface bidirectional with the workspace + context panes.

---

## 2. R5 Closeout — What Lands and What Defers

### 2.1 R5 ships (cycle-6 through cycle-9 fixes as one PR)

| Fix | What it does | Status |
|---|---|---|
| `useChatFileAttachment.ts` FileList snapshot fix | Paperclip → addFiles properly receives the FileList (Array.from before input.value = '') | ✅ deployed cycle 6 |
| `useChatFileAttachment.ts` cross-package File forwarding | AttachmentChip.file?: File; ChatAttachment.file?: File; populated in addFiles; forwarded through derivation; onAttachmentReady carries it. ConversationPane prefers `attachment.file ?? syntheticFallback`. PDF/DOCX upload binary correctly. | ✅ deployed cycle 6 |
| `useChatFileAttachment.ts` pdfjs workerSrc | Default to versioned jsdelivr CDN URL when consumer doesn't pre-configure GlobalWorkerOptions. PDF extraction works in single-file Vite bundles. | ✅ deployed cycle 7 |
| `ConversationPane.tsx` suppress misleading DocumentViewerWidget dispatch | The R5 task 022 stub widget was rendering "Preview not available" on every upload. Dispatch suppressed; chip strip alone confirms the file. Reinstate when task 022 properly upgrades the widget OR when R6 Pillar 9 lands. | ✅ deployed cycle 8 |
| Task 038 — Workspace-pane Summary tab + auto-focus | `StructuredOutputStreamWidget` registered as the leftmost tab in WorkspacePane; correlationId = chat sessionId; auto-focuses on `workspace.streaming_started`; manual-override respected; resets on `streaming_complete`. + `WorkspaceTabManager.prependTab`. + `streamId: chatSessionId` wired through `executeSummarizeIntent`. 14 new tests pass. | ✅ deployed cycle 8 |
| `sseToPaneEventBridge.ts` JSONPath prefix strip | IncrementalJsonParser emits `$.tldr` (JSONPath); widget schema expects `tldr` (bare key). Bridge normalizes `$.X` → `X` before publishing field_delta events. | ✅ deployed cycle 9 |
| Husky pre-commit fix | Added `#!/bin/sh` shebang; marked executable in git index. Was blocking commits on Windows. | ✅ deployed cycle 6 |
| `KnowledgeDocument` schema-conformance + 8 JsonIgnore attributes (R5 PR #361) | Customer-corpus-only fields suppress in JSON when null/default — session-files writes no longer 400 on deploymentId / deploymentModel / etc. Includes wire-format regression test. | ✅ landed in master via PR #361 |

These accumulated over 9 diagnostic cycles. They need to be consolidated into ONE PR and merged before R6 starts. Roughly: commit + push + auto-merge + verify against current Spaarke Dev.

### 2.2 R5 defers (documented limitations, fixed in R6)

| Limitation | What's broken | Where R6 fixes it |
|---|---|---|
| TL;DR renders raw JSON array fragments | `tldr: string[]` streams as token-by-token JSON (`[`, `"`, `,`, `"`, `]`). Renderer concatenates literally. | R6 Pillar 5 (output-type aware renderers) + Pillar 9 (widget visibility contracts) |
| Entities renders raw JSON object fragments | `entities: {organizations: string[], persons: string[]}` streams as nested JSON tokens. Renderer concatenates literally. | R6 Pillar 5 + Pillar 9 |
| Duplicate fire (chat + workspace both summarize) | SprkChat's `onBeforeSendMessage` is INFORMATIONAL — can't suppress the chat-agent's parallel send. Both paths fire for `/summarize` with held files. | R6 Pillar 8 (command router) + Pillar 6 (workspace state — agent decides surface) |
| Playbook → Action FK linkage missing | `summarize-document-for-chat@v1` playbook row exists; its node has no `sprk_actionid` linkage. SUM-CHAT@v1 action exists separately. Orchestrator works because it loads action directly by `actionCode`, bypassing the playbook FK chain. | R6 Pillar 4 (playbook FK resolution) |
| Persona is hardcoded in C# | `PlaybookChatContextProvider.BuildDefaultSystemPrompt` is a private method. No prompt engineering, no eval, no tenant override, no versioning. | R6 Pillar 1 (persona as data) |
| Tools are code-defined despite `sprk_analysistools` entity | Factory pulls tools from DI (`GetServices<AIFunction>()`); descriptions are C# `[Description]` attributes. `sprk_analysistools` rows exist in Dataverse but are not consulted at runtime. | R6 Pillar 2 (tool registry as data) |
| One C# tool class per playbook (e.g., `InvokeSummarizePlaybookTool`) | Adding a new playbook requires authoring a new tool class. Doesn't scale. LLM sees a long list of similar tools. | R6 Pillar 3 (generic `invoke_playbook` tool) |
| Workspace ↔ Assistant is one-way | Agent can push to workspace via `widget_load` events. Agent CANNOT read current workspace state (open tabs, active tab, user selection). User cannot push from workspace to assistant. | R6 Pillar 6 (workspace state + bidirectional events) + Pillar 9 (visibility contracts) |
| Memory is sliding window only | No summarization of old turns, no pinned context, no token-budget compression for long conversations. | R6 Pillar 7 (context window management) |
| No formal slash/hash command vocabulary | Today: ad-hoc regex matchers. No distinction between hard commands (deterministic) vs intent shortcuts vs reference syntax. | R6 Pillar 8 (command router) |
| `DocumentViewerWidget` is a stub (R5 task 022 was 🔲 not-started) | The widget shows "Preview not available" for client-uploaded chat files. R5 suppressed the dispatch as a workaround. | R6 Pillar 9 (widget visibility contracts + proper preview wiring) |
| Workspace artifacts are not durable | A workspace tab created from a chat exists only in the current session; closing/refreshing loses it. Tabs are not pinnable, not exportable, not linkable to matters. | R6 Pillar 6 (workspace state model includes persistence) |

These are all real limitations. None is shipped as "working." R5's closeout note will say: "Workspace summary path is plumbed end-to-end but renders raw JSON for array/object fields; the chat agent's summary is the canonical user-visible output until R6 ships."

### 2.3 R5 tasks officially closed-out as deferred-to-R6

| R5 task | Was | Becomes |
|---|---|---|
| 037 — Context-pane execution-trace widget | 🔲 not-started | ⏭️ deferred-to-R6 (depends on output-type rendering pattern from Pillar 5) |
| 035 — SC-18 walkthrough re-run + signoff | 🔲 not-started | ⏭️ deferred-to-R6 (R6 produces a redesigned vertical slice as the validation target) |
| 022 — DocumentViewerWidget upgrade (R4 stub → renderer-driven) | 🔲 not-started | ⏭️ deferred-to-R6 Pillar 9 (widget visibility contract resolves the design properly) |
| 030, 031 — Phase 2 acceptance gates | 🔲 not-started | ⏭️ deferred-to-R6 (replaced by R6 acceptance gates) |

R5 thus ends as: Phase 1 ✅, Phase 2 ✅ on backend (PR #354, #359, #361) + frontend cycle-6 through cycle-9 fixes; UX known-limited; tasks 022, 030, 031, 035, 037 deferred-to-R6. Phase 3 (D3-01 through D3-05) deferred entirely — these are polish + telemetry + lessons-learned items that depend on a working baseline first.

---

## 3. How the Platform Actually Works Today

This section is the unvarnished truth of what's running in production. It is intentionally detailed because R6's design depends on understanding the gaps precisely.

### 3.1 The dual-path problem — both fire on `/summarize`

When the user uploads a file via paperclip + types "summarize" + presses Enter, **two LLM calls run in parallel**:

**Path A** (Assistant pane — chat agent):
1. `useChatFileAttachment` extracts text client-side (mammoth for DOCX, pdfjs for PDF), constructs FR-07 inline attachment `{ filename, contentType, textContent }`
2. SprkChat's `onBeforeSendMessage` is INFORMATIONAL (host cannot suppress the send)
3. POST `/api/ai/chat/sessions/{id}/messages` with the user text + inline attachments
4. BFF `ChatEndpoints.SendMessageAsync` → `SprkChatAgentFactory.CreateAgentAsync` (full prompt build, 7+ enrichment layers)
5. Azure OpenAI chat completion call: standard mode, no JSON schema, tools registered (including `InvokeSummarizePlaybookTool`)
6. LLM has the FULL document text inline; usually responds DIRECTLY in markdown without calling the tool (calling the tool would be redundant — it has the text already)
7. Response streams as `data: {type:"text",content:"..."}` SSE chunks; SprkChat renders markdown progressively in the chat thread

**Path B** (Workspace pane — direct `/summarize`):
1. ConversationPane's `handleBeforeSendMessage` runs `matchIntent` (matches `summarize-session` via slash/keyword/button-id)
2. `executeSummarizeIntent` fires in parallel with SprkChat's send (cannot suppress per step A.2 above)
3. POST file binary to `/api/ai/chat/sessions/{id}/documents`
4. BFF `ChatDocumentEndpoints.UploadDocumentAsync` runs `ITextExtractor` (Azure DocumentIntelligence) on the binary, generates embeddings via `text-embedding-3-large`, indexes chunks to `spaarke-session-files` Azure AI Search index, updates `ChatSession.UploadedFiles` in Redis
5. POST `/api/ai/chat/sessions/{id}/summarize` with `{ fileIds, style }`
6. BFF `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync`: queries `spaarke-session-files` (filter by `tenantId + sessionId + documentId`), retrieves chunks ordered by `chunkIndex`, loads the `SUM-CHAT@v1` action directly by `actionCode` (bypassing playbook FK chain), pulls its `sprk_systemprompt` + output schema
7. Azure OpenAI chat completion call: **Structured Outputs mode** with `response_format: json_schema`, LLM is strictly constrained to emit JSON matching the schema
8. As tokens stream, `IncrementalJsonParser` emits `FieldDeltaEvent { path: "$.tldr", content, sequence }` events
9. Orchestrator wraps each as `AnalysisChunk.FromDelta` and yields
10. `SummarizeSessionEndpoint` writes each chunk as SSE `data: {...}\n\n`
11. Browser `executeSummarizeIntent` reads SSE, `sseToPaneEventBridge` maps `chunk.type='delta'` → PaneEventBus `workspace.field_delta` event (with JSONPath prefix stripped by cycle-9 fix)
12. `StructuredOutputStreamWidget` (mounted in workspace's Summary tab, correlationId = chat sessionId) subscribes, accumulates content per fieldPath, renders by `displayHint` (heading / paragraph / badge / list)

Both calls hit Azure OpenAI. Both consume tokens. Both produce summaries. The user sees both. Path A reads better (markdown chat); Path B has rendering bugs (array/object fields show raw JSON).

This duplicate-fire is **unintentional** — a workaround that R5 documented in implementation notes §2 as a deferred problem. R6 closes it.

### 3.2 `SprkChatAgentFactory.CreateAgentAsync` — 7-layer prompt build

Every `/messages` POST triggers a fresh agent. The factory does the following (in order — each layer can add to the system prompt):

**Layer 1 — Base system prompt (PERSONA)**
- Resolved via `IChatContextProvider.GetContextAsync()` → `PlaybookChatContextProvider`
- If no playbook is active: `BuildDefaultSystemPrompt(null)` — a hardcoded C# string in `PlaybookChatContextProvider`
- If a playbook is active: pulls `sprk_systemprompt` from the playbook's action (via FK chain — but see §3.4 below for the actual flow)
- **GAP**: The default persona is hardcoded; not data-driven, not versioned, not tenant-customizable, not optimized via prompt engineering

**Layer 2 — Document context injection (R2-011, R2-012)**
- `EnrichWithDocumentContextAsync` factory-instantiates `DocumentContextService`
- Loads the primary document + any additional documents
- Enforces a 30K-token budget
- When document exceeds budget: conversation-aware re-selection via embedding similarity to the latest user message
- Used when the agent's context includes a focal document (e.g., from `documentId` route param)

**Layer 3 — Entity context (when `hostContext.PageType` is set)**
- `EnrichSystemPrompt` block in `PlaybookChatContextProvider` (line 380+)
- Reads `hostContext.EntityType`, `EntityId`, `EntityName`, `PageType`
- Appends: *"Context: You are assisting with {EntityType} record '{EntityName}'. The user is viewing the {humanReadablePageType}."*
- Skipped when `PageType` is empty or "unknown" (client didn't supply it)
- **For SpaarkeAi standalone tests this branch usually SKIPS** because `PageType` isn't supplied
- **GAP**: Which fields per entity are included is hardcoded in `ParentEntityContext.EntityTypes`; not data-driven

**Layer 4 — Active Capabilities enrichment (R2-021, FR-11)**
- `CreateCommandResolver()` instantiates `DynamicCommandResolver`
- Resolves available commands for this tenant + host context
- Appends a "### Active Capabilities" section to the system prompt listing scope-contributed slash commands
- **GAP**: These commands could be expressed as playbook skills; today they're a parallel concept

**Layer 5 — Session Files manifest (R5 task 033)**
- If `ChatContext.UploadedFiles` has entries: appends a manifest listing `fileId + fileName + count`
- Named tool identifier `invoke_summarize_playbook` explicit in the suffix so the LLM uses the right tool name
- NEVER includes file content / chunk text / binary (ADR-015)

**Layer 6 — Capability routing (per-turn tool filtering, AIPU2-061)**
- `ICapabilityRouter.RouteAsync(userMessage, playbookName, ct)` runs a 3-tier router (keyword → embedding → LLM classifier)
- Returns `CapabilityRoutingResult { SelectedCapabilities, SelectedToolNames }`
- `ICapabilityValidator.FilterAsync(candidates)` then removes tools blocked by kill-switch / tenant / role
- The validated tool set is what the LLM actually sees this turn
- When router returns empty or unavailable: fallback to the full playbook-capabilities-gated set

**Layer 7 — Tool registration**
- Tools resolved from DI: `_serviceProvider.GetServices<AIFunction>()`
- Filtered by the routed/validated capabilities
- Registered with the Azure OpenAI call as `tools: [...]`
- Each tool's `[Description]` attribute is hardcoded in its C# class
- **GAP**: `sprk_analysistools` Dataverse entity is NOT consulted at runtime

The actual Azure OpenAI call is then made with:
- `messages`: [system (assembled from layers 1-5), historical turns from Redis, current user turn including FR-07 attachments]
- `tools`: [filtered, registered AIFunction list]
- `model`: configured deployment name
- `tool_choice`: "auto" (LLM decides)

After the call, response streams back as SSE; tool calls are executed mid-stream when the LLM emits them.

### 3.3 The Persona Layer — what it IS today

A close look at the chat assistant persona today:

- **File**: `PlaybookChatContextProvider.cs`, method `BuildDefaultSystemPrompt(string? playbookName)` — a private function
- **Content**: a hardcoded string built by concatenation of: "You are the Spaarke Assistant…" + playbook-name interpolation + some general behavior guidelines
- **Tenant customization**: none at the persona layer (some at the capability layer via `DynamicCommandResolver`)
- **Versioning**: tracked only via git history of the C# file
- **Optimization**: any developer can edit; no eval harness; no A/B testing
- **R6 Pillar 1 target**: persona-as-data — `sprk_aipersona` entity (or extend `sprk_analysisaction` with `actionType=Persona`) with the prompt as data, tenant overrides, versioning, eval suite

### 3.4 The Playbook entity model — intended vs implemented

**Intended design**:
```
sprk_analysisplaybook (the playbook)
   ├─ sprk_playbooknode[] (the nodes — ordered, with branching)
   │    ├─ sprk_actionid (FK to sprk_analysisaction)
   │    │     └─ sprk_systemprompt + outputSchema + actionType
   │    ├─ skills[] (attached behaviors)
   │    ├─ knowledge[] (attached knowledge sources)
   │    └─ tools[] (allowed tool subset)
   └─ output type per node (chat / workspace / both / side-effect)
```

The orchestrator should:
1. Receive a playbook invocation
2. Load the playbook by name or ID
3. Walk the playbook's nodes in order
4. For each node, load its action + skills + knowledge + tools
5. Compose the LLM call from the node's configuration
6. Route the output per the node's output-type

**What actually happens for `/summarize`** (the R5 vertical slice):

1. `SessionSummarizeOrchestrator` receives `{ tenantId, sessionId, fileIds, style }`
2. Loads the `SUM-CHAT@v1` action **directly by actionCode** (`var actionConfig = ... GetByCodeAsync("SUM-CHAT@v1")`)
3. Extracts `actionConfig.SystemPrompt` + `actionConfig.OutputSchemaJson`
4. Builds the Azure OpenAI call from those
5. Returns the stream

The orchestrator NEVER:
- Loads the `summarize-document-for-chat@v1` playbook row
- Walks its nodes
- Reads any node's attached skills / tools / knowledge

The `summarize-document-for-chat@v1` playbook row IS in Dataverse (R5 task 011 deployed it), but its single node is not actually FK-wired to the `SUM-CHAT@v1` action. Even if the FK were set, the orchestrator wouldn't traverse it.

**Implication**: the playbook entity is currently a label, not an orchestration unit. There is no multi-node playbook executing today (some single-node executions go through `AiAnalysisNodeExecutor`, but the orchestrator-by-actionCode shortcut is what Summarize uses).

**R6 Pillar 4 target**: orchestrator MUST resolve via playbook → node → action FK chain; shortcuts removed; playbook abstraction is load-bearing.

### 3.5 The Tool architecture — intended vs implemented

**Intended design** (matches the existence of `sprk_analysistools` entity in Dataverse):

- `sprk_analysistools` rows define each tool: name, description (LLM-facing), parameter schema, handler class name, configuration JSON, per-tenant enablement, capability gates
- At startup, a discovery service reads `sprk_analysistools` and binds each row to its handler class (via reflection or DI keyed registration)
- At chat time, the factory resolves the available tools for this tenant + playbook + capability set FROM `sprk_analysistools`
- The LLM-facing descriptions, parameter schemas, etc. all come from Dataverse

**What actually happens**:

- Tools are registered in DI as `services.AddSingleton<AIFunction, ConcreteToolClass>()` in `AnalysisServicesModule` or equivalent module
- Each tool's `[Description("...")]` attribute (or method-returned description) is the LLM-facing description, hardcoded
- The factory calls `_serviceProvider.GetServices<AIFunction>()` to get the full list
- Capability filtering (via `ICapabilityRouter` + `ICapabilityValidator`) is a separate layer that's well-implemented and code-based
- `sprk_analysistools` rows exist in Dataverse but are NOT consulted at runtime — the entity is essentially decorative

**Implication**:
- Adding a new tool = code change + DI registration + deploy + restart
- Tool descriptions can't be A/B tested without a deploy
- Per-tenant tool enablement is binary (kill switch) not per-tool data-driven
- The descriptions ARE optimizable, but only by C# developers writing prose into attribute strings

**R6 Pillar 2 target**: tool registry IS the `sprk_analysistools` table; descriptions are data; handler class binding happens at startup; the entity is load-bearing.

### 3.6 Specialized vs Generic tool invocation

**Today's pattern**: one tool class per AI capability that the LLM should invoke.
- `InvokeSummarizePlaybookTool` (R5 task 015)
- Future: `InvokeDraftResponseTool`, `InvokeCreateMatterTool`, `InvokeExtractEntitiesTool`, …

Each is a separate C# class with hardcoded description. Each requires deploy to add. LLM has to disambiguate among many similar-looking tools.

**Better pattern (R6 Pillar 3)**: ONE generic tool that the LLM uses to invoke any playbook.

```csharp
[Function("invoke_playbook")]
[Description(
  "Invoke a structured playbook to perform a specialized analysis or action. " +
  "Use when the user requests something matching one of the available playbooks. " +
  "Available playbooks (read from Dataverse at chat-build time and interpolated here): " +
  "{PLAYBOOK_LIST}"
)]
async Task<PlaybookResult> InvokePlaybook(
  string playbookName,        // From sprk_analysisplaybook.sprk_name
  Dictionary<string, object> parameters)
```

At prompt-build time, `{PLAYBOOK_LIST}` is interpolated with active playbooks' descriptions: *"summarize-document-for-chat@v1 — Streaming structured summary of session-uploaded files. Outputs tldr/summary/keywords/entities."* etc.

**Total agent tool surface becomes ~5-10 tools**, regardless of how many AI capabilities the platform supports:
- `invoke_playbook(name, params)` — structured analysis (replaces all per-playbook tools)
- `search_knowledge(query, scope)` — RAG retrieval (replaces specialized search tools)
- `read_entity(entityType, entityId)` — Dataverse read
- `update_entity(entityType, entityId, changes)` — Dataverse write
- `send_workspace_artifact(content, widgetType)` — push to workspace
- `send_email(to, subject, body, attachments)` — send mail via Graph
- Plus a few platform tools (`navigate_to`, `open_url`, `list_recent_files`)

This is the established generic-tool-with-options pattern used by OpenAI Assistants API and Anthropic's Computer Use. **R6 Pillar 3 adopts it** — implemented over Spaarke's existing in-process `PlaybookExecutionEngine` via the production facade pattern (§3.15), not any external runtime.

### 3.6.1 Why generic-selector is better than tool-explosion

| Concern | Tool-per-playbook (today) | Generic invoke_playbook (R6) |
|---|---|---|
| Adding a new AI capability | New C# class + DI registration + deploy + restart | Insert Dataverse row |
| LLM tool selection accuracy | Degrades as tools multiply | Stable (one tool to recognize; routing happens via playbook description) |
| Tool description quality | Spread across N C# files | One playbook description per capability — single source |
| A/B testing capabilities | Hard (requires deploys) | Trivial (swap Dataverse rows; LLM sees same tool surface) |
| Per-tenant enablement | Binary kill switches per tool | Per-tenant filter on the playbook list shown to the LLM |

### 3.7 Memory / Context window — what's actually there

The R5 walkthrough surfaced that "stateless agent" is misleading. Let me clarify what IS implemented:

**Per-call assembly (every `/messages` POST)**:
1. Factory creates a fresh `SprkChatAgent` (no instance state)
2. History is pulled from Redis via `ChatSessionManager.GetSessionAsync` — recent messages, up to a token budget
3. System prompt is assembled from the 7 enrichment layers (§3.2)
4. The full prompt (system + history + current turn + inline attachments) is sent to Azure OpenAI

**Memory management**:
- `MaxSystemPromptTokenBudget = 8000` (hardcoded in `PlaybookChatContextProvider`)
- `EstimateTokenCount` helper for budget tracking
- History capped at N most recent OR by token budget (specific implementation in `ChatSessionManager`)
- When history exceeds budget: **sliding window** (oldest dropped)
- No summarization of old turns
- No pinned context
- No selective recall via embeddings

**Implication**:
- For short conversations (10-20 turns), the agent has full memory
- For long conversations (50+ turns), oldest context is lost — agent forgets early facts
- For complex multi-document workflows, the agent's coherence degrades over time

**R6 Pillar 7 target**: smarter memory — sliding + summarization compression for old turns + pinned context (user preferences, critical facts) + token-budget-aware composition. Likely also embed every turn so any prior context is retrievable via similarity search.

### 3.8 Workspace ↔ Assistant — the one-way wall

**What works (Assistant → Workspace, weakly)**:
- Agent tool calls CAN dispatch `workspace.widget_load` PaneEventBus events
- WorkspacePane subscribes and mounts a tab when the event arrives
- R5 added `workspace.streaming_started / field_delta / streaming_complete` for structured-output streaming

**What's missing**:
- Agent CANNOT read current workspace state. It doesn't know which tabs are open, which is active, what the user is looking at, what they've selected, what they've edited
- Agent CANNOT MODIFY existing workspace tabs (replace content, update specific field, close a tab)
- User CANNOT explicitly push from chat → workspace (no "Send to Workspace" button)
- User CANNOT explicitly expose a workspace tab → assistant (no "Add to Assistant" button)
- User-created workspace tabs (Calendar, Dashboard, etc.) are invisible to the assistant
- Workspace tabs don't survive page reload — they exist in browser state only
- No provenance ("this tab was generated from chat session X at time Y")
- No conflict resolution (if user edits while agent is also generating)

**R6 Pillar 6 target**: bidirectional state model — every workspace tab is a typed, persisted, agent-visible (configurably) artifact with provenance. PaneEventBus carries reads + writes. Per-turn agent prompt includes a workspace snapshot.

### 3.9 Text Extraction modes

| Path | Engine | Where | Quality |
|---|---|---|---|
| Path A inline attachment | mammoth (DOCX) / pdfjs (PDF) | Browser | Linear text; flattens tables, strips layout; no OCR |
| Path B `/documents` POST | Azure DocumentIntelligence | BFF server | Layout-aware; tables preserved; OCR for scanned PDFs; markdown output |
| Path B fallback (no `attachment.file`) | None — sends extracted text as synthetic file | BFF server tries to parse text as DOCX/PDF | Empty result (binary parse fails) |

The R5 cycle-6 cross-package File-forwarding fix eliminates the fallback case for the paperclip path — original binary is now forwarded. The fallback can still trigger if a future consumer of SprkChat's `onAttachmentReady` doesn't populate `chat.file`.

**R6 implication**: extraction quality is divergent between paths. Path A summaries can be lower-fidelity for complex PDFs. R6 should either (a) standardize on Path B (server-side extraction always), (b) document the divergence explicitly, or (c) compute a quality score and let the agent decide. The Architecture-X path (§4.4) inherently uses server-side because the agent's tool calls the structured orchestrator.

### 3.10 LLM call modes — standard chat vs Structured Outputs

| Mode | Used by | Output shape | Streaming |
|---|---|---|---|
| Standard chat completion | Path A (chat agent) | Free-form text; LLM decides format | Token stream as plain text |
| Structured Outputs (`response_format: json_schema`) | Path B (`SessionSummarizeOrchestrator`) | Strict JSON matching schema | Token stream as JSON; parser extracts field-level deltas |

The schema enforcement is a guarantee of the Azure OpenAI API. For Path B, the LLM CANNOT produce malformed JSON or extra fields. The schema is part of the JSON schema's `additionalProperties: false` constraint.

Path A's free-form output is what enables markdown formatting, bullet variation, conversational tone. Path B's strict JSON is what enables reliable downstream processing (entity extraction, search indexing, dashboard population). The two modes serve different purposes; R6 should keep BOTH but route them more intentionally.

### 3.11 Renderer architecture today

`StructuredOutputStreamWidget` is the workspace-pane renderer for streaming structured outputs. It:

- Subscribes to `workspace.streaming_started / field_delta / streaming_complete` PaneEventBus events
- Filters by `correlationId === event.streamId`
- Reducer accumulates content per `fieldPath` (string concatenation)
- Renders each field per its `displayHint`: `heading` / `paragraph` / `badge` / `list`

**The bugs**:
- `displayHint: 'heading'` for `tldr` — the playbook emits `tldr: string[]` (JSON array). The reducer concatenates the raw JSON tokens. The renderer renders the accumulated string as a heading — including the `[`, `"`, `,`, `]` characters.
- `displayHint: 'list'` for `entities` — the playbook emits `entities: { organizations: string[], persons: string[] }` (nested JSON object). Same problem.
- The renderer assumes each fieldPath's content is a SCALAR streamable string. It doesn't know how to handle array or object types.

**Why this happened**: the streaming pattern was designed for scalar fields (TL;DR was originally a single string; got changed to an array late in spec development). The renderer wasn't updated. The bug is structural — the rendering pattern needs to be schema-aware.

**R6 Pillar 5 target**: renderer is schema-aware. For array-typed fields, accumulate token deltas until streaming_complete, then JSON.parse, then render as bullets. For object-typed fields, similar — parse, then render as labeled lists or key-value blocks. The renderer reads the schema's type per field and dispatches to the right sub-renderer.

### 3.12 What already exists (canonical references) — R6 reuses, does not rebuild

Before R6 work begins, this is what's production-mature today. R6 PRESERVES and BUILDS ON these. R6 does not rebuild any of them. Authoritative architecture docs cross-referenced inline.

**Scopes infrastructure** (per [`docs/architecture/scope-architecture.md`](../../docs/architecture/scope-architecture.md)):

| Capability | Code location |
|---|---|
| 4 scope entities (`sprk_analysisaction`, `sprk_analysisskill`, `sprk_analysisknowledge`, `sprk_analysistool`) | Dataverse |
| Standalone scope endpoints (`/api/ai/scopes/{actions,skills,knowledge,tools}`) | `Api/Ai/ScopeEndpoints.cs` |
| SYS-/CUST- ownership with HTTP 403 immutability | `Services/Scopes/OwnershipValidator.cs` |
| Single-level inheritance (Extend) with `GetEffectiveScopeAsync` merging | `Services/Scopes/ScopeInheritanceService.cs` |
| "Save As" deep copy with auto-suffix duplicate-name handling | `Services/Scopes/ScopeCopyService.cs` |
| Gap detection (keyword + AI classification across 8 categories) | `Services/Ai/ScopeGapDetector.cs` |
| Per-playbook N:N + per-node N:N override | `Services/Ai/ScopeResolverService.cs:267,270,273` |
| Decomposed entity services (`ActionService`, `SkillService`, `KnowledgeService`, `ToolService`) per ADR-010 | `Services/Ai/` |

**Playbook execution** (per [`docs/architecture/playbook-architecture.md`](../../docs/architecture/playbook-architecture.md)):

| Capability | Code location |
|---|---|
| 11 node executors (see §3.16 for full list) | `Services/Ai/Nodes/` |
| `PlaybookExecutionEngine` (dual mode: batch + conversational) | `Services/Ai/PlaybookExecutionEngine.cs` |
| `PlaybookOrchestrationService` (FK chain traversal, parallel batching, topological sort) | `Services/Ai/PlaybookOrchestrationService.cs` |
| Parallel batching with SemaphoreSlim throttle (default 3, Azure OpenAI TPM-tuned) | (same) |
| 3-tier knowledge retrieval (L1 references / L2 RAG / L3 records) | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` |
| Per-token streaming via `IStreamingAnalysisToolHandler` | `Services/Ai/IStreamingAnalysisToolHandler.cs` |
| `PlaybookSchedulerService` (notification-mode playbooks on cron) | `Services/PlaybookSchedulerService.cs` |
| `TemplateEngine` (Handlebars.NET with array flattening) | `Services/Ai/TemplateEngine.cs` |
| Visual playbook canvas (`PlaybookBuilder` Code Page) | `src/client/code-pages/PlaybookBuilder/` |
| AI Builder agentic loop (canvas + scope manipulation tools) | `Services/Ai/Builder/BuilderAgentService.cs` |

**JPS pipeline** (per [`docs/guides/JPS-AUTHORING-GUIDE.md`](../../docs/guides/JPS-AUTHORING-GUIDE.md)):

| Capability | Code location |
|---|---|
| 6-layer rendering (Override Merge → Scope Resolution → $ref → Template Params → $choices → Render) | `Services/Ai/PromptSchemaRenderer.cs` |
| `JpsRefResolver` ($ref extraction) | `Services/Ai/JpsRefResolver.cs:13-68` |
| `PromptSchemaOverrideMerger` ($clear / __replace directives) | `Services/Ai/PromptSchemaOverrideMerger.cs:23-79` |
| `LookupChoicesResolver` (5 prefixes: lookup / optionset / multiselect / boolean / downstream) | `Services/Ai/LookupChoicesResolver.cs:30-100` |
| Format detection + dual-path (JPS vs flat-text fallback) | `Services/Ai/PromptSchemaRenderer.cs:111-114` |
| JSON Schema generation for Structured Outputs | `Services/Ai/PromptSchemaRenderer.cs:334-366` |
| `Seed-JpsActions.ps1` tooling (-WhatIf, -BackupPath, -Environment) | `scripts/Seed-JpsActions.ps1` |

**Chat agent infrastructure** (per [`docs/architecture/chat-architecture.md`](../../docs/architecture/chat-architecture.md)):

| Capability | Code location |
|---|---|
| `SprkChatAgent` + `SprkChatAgentFactory` (transient agent per request) | `Services/Ai/Chat/` |
| `CapabilityRouter` 3-tier (keyword → GPT-4o-mini → broad-superset fallback) | `Services/Ai/Capabilities/CapabilityRouter.cs:53-149` |
| Two keyed `IChatClient` instances (raw + function-invocation) | `Infrastructure/DI/AiModule.cs:136,144` |
| Safety pipeline (PromptShield + Groundedness + Citations + Privilege + Cross-matter) | `SafetyPipelineMiddleware.cs`, `AiSafetyModule.cs` |
| AI Public Contracts facades per ADR-013 (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) | `Services/Ai/PublicContracts/` |
| `InsightsIntentClassifier` (playbook vs RAG routing with `forceMode` override) | `Services/Ai/Insights/` |

**Persistence**:

| Capability | Code location |
|---|---|
| Cosmos DB containers (sessions, prompts, audit, memory, feedback) — write-through, 90d TTL | `Infrastructure/DI/AiPersistenceModule.cs:77-107` |
| Redis hot tier (24h TTL) + Cosmos warm tier | (write-through pattern) |
| `IEmbeddingCache` (SHA256 keys, 7-day TTL) | (cache layer) |
| Per-matter memory snapshots in Cosmos `memory` container | `MatterMemoryService` |

**Frontend infrastructure** (per [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) + [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md)):

| Capability | Code location |
|---|---|
| 4-channel `PaneEventBus` (workspace / context / conversation / safety) per ADR-030 | `Spaarke.AI.Widgets/src/events/PaneEventBus.ts:68-74` |
| 4-stage shell lifecycle (welcome / loading / active-chat / review) per ADR-031 | `Spaarke.AI.Widgets/src/interactions/StageTransitionRules.ts:56` |
| Pure `determineStage()` function | (same) |
| 12 context-pane widgets (Document/Citation/Findings/EntityInfo/FilePreview/etc.) | `Spaarke.AI.Widgets/src/widgets/context/` |
| `useAiPrefill` hook + `findBestLookupMatch` + `AiFieldTag` | `@spaarke/ui-components` |
| `WorkspaceWidgetRegistry` (lazy widget factories) | `Spaarke.AI.Widgets/src/registry/` |
| `WorkspaceTabManager` (tab state class) | `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` |

**Pre-fill flow** (per [`docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md`](../../docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md)) — the canonical proof of "playbook from anywhere":

| Capability | Code location |
|---|---|
| `MatterPreFillService` / `ProjectPreFillService` (BFF scoped services) | `Services/Workspace/` |
| Endpoints `POST /api/workspace/{entity}/pre-fill` | (registered) |
| Facade-routed invocation via `IWorkspacePrefillAi.ExecutePlaybookAsync` (NOT direct injection) | `MatterPreFillService.cs:310-335` |
| `Workspace:PreFillPlaybookId` / `Workspace:ProjectPreFillPlaybookId` config | `appsettings.json` |
| `$choices`-constrained output for exact Dataverse value matching | (JPS) |
| 45-second hardcoded timeout | `MatterPreFillService.cs:310-311` |
| `useAiPrefill` frontend hook + lookup resolution + "AI" sparkle badges | `@spaarke/ui-components` |

**Documented binding ADRs** (R6 honors without modification):

- **ADR-013** (AI architecture): AI as BFF extension; PublicContracts facade boundary; no separate AI microservice
- **ADR-030** (Pane Event Bus): closed at 4 channels; additive event types only
- **ADR-031** (Stage lifecycle): 4-stage deterministic from `SessionState`
- **ADR-014, ADR-016**: AI caching + rate limiting
- **ADR-028** (Spaarke Auth v2): no token snapshots; OBO + MI patterns
- **ADR-010** (DI minimalism): module pattern for new registrations
- **ADR-008** (Endpoint filters): authorization filters, not global middleware
- **ADR-015** (AI data governance): no logging of user message content

### 3.13 The two tool worlds (chat-agent vs playbook-analysis)

The platform's word "tool" refers to TWO distinct concepts that use the same name. R6 converges them.

| Concept | Where used | Data source today | Pattern |
|---|---|---|---|
| **Chat-agent function-calling tool** | LLM's tool list in `SprkChatAgentFactory.ResolveTools()` lines 721-1327 | 12 hardcoded C# classes (10 pre-R5, 2 R5: `InvokeSummarizePlaybookTool`, `InvokeInsightsQueryTool`) | Plain class + public method, wrapped as `Microsoft.Extensions.AI.AIFunction`, registered in DI |
| **Playbook AI-analysis tool** | Per-node N:N relationship `sprk_playbooknode_tool`, resolved at runtime by `IScopeResolverService.ResolveNodeScopesAsync()` | `sprk_analysistool` Dataverse rows | `sprk_handlerclass` string → `IToolHandlerRegistry.GetHandler()` → `IAnalysisToolHandler.ExecuteAsync()` (auto-discovery via reflection at startup) |

**Today**: these are orthogonal worlds. Chat-agent tools are code; playbook tools are data. No cross-consumption. The 12 chat-agent tool classes were committed across multiple projects (Feb–Jun 2026); only 2 of them are R5 work (`InvokeSummarizePlaybookTool` + `InvokeInsightsQueryTool` — the two bridge tools).

**Playbook-side handler implementation status today** (verified): 4 of 12 declared handlers actually exist as C# classes:
- ✅ `GenericAnalysisHandler` (workhorse — JPS-driven, 821 LOC)
- ✅ `DocumentClassifierHandler` (with optional RAG few-shot, 970 LOC)
- ✅ `SummaryHandler` (chunk → summarize → synthesize, 893 LOC)
- ✅ `SemanticSearchToolHandler` (search capability)
- ❌ `EntityExtractorHandler` (referenced in seed; no code)
- ❌ `ClauseAnalyzerHandler` (referenced in seed; no code)
- ❌ `RiskDetectorHandler` (referenced in seed; no code)
- ❌ `ClauseComparisonHandler` (referenced in seed; no code)
- ❌ `DateExtractorHandler` (referenced in seed; no code)
- ❌ `FinancialCalculatorHandler` (referenced in seed; no code)
- ❌ `InvoiceExtractionToolHandler` (referenced in seed; no code)
- ❌ `FinancialCalculationToolHandler` (referenced in seed, marked "Stubbed for MVP"; no code)

When a playbook references a missing handler, runtime fails with "Tool handler not registered" — playbook authors can't reliably use the typed handlers. In practice they use `GenericAnalysisHandler` + JPS prompts for all custom work.

**After R6**: both worlds share the same underlying `IAnalysisToolHandler` registry (renamed to `IToolHandler` for clarity). The `AnalysisTool` DTO adds:
- `AvailableInContexts`: `Playbook` / `Chat` / `Both`
- `JsonSchema`: parameter declaration for chat function-calling

The chat-agent's tool resolution reads `sprk_analysistool` rows the same way the playbook side does, wraps them as `AIFunction` via an adapter (`ToolHandlerToAIFunctionAdapter`), and registers with `Microsoft.Extensions.AI`. The 8 typed handlers get real implementations.

**Authoring a new capability** becomes: write the C# handler class once (if needed; `GenericAnalysisHandler` suffices for prompt-only capabilities), insert a `sprk_analysistool` row declaring `AvailableInContexts`. The capability is then available in whichever context the row declares — no second registration site, no factory edits, no duplicated description text.

### 3.14 Scopes are first-class today

The Scope library is production-mature with full management infrastructure (per `docs/architecture/scope-architecture.md`). R6 does not rebuild scope CRUD, ownership, inheritance, gap detection, or endpoints — these all ship.

**R6 adds two scope entities to complete the model**:

1. **Persona as the 5th scope** (`sprk_aipersona`):
   - Currently hardcoded in `PlaybookChatContextProvider.BuildDefaultSystemPrompt()`
   - Playbook-bound personas come from the playbook's primary action's `sprk_systemprompt` via `PlaybookChatContextProvider` (lines 160-187)
   - R6 makes persona a standalone scope following existing patterns (SYS-/CUST-, inheritance, SaveAs, gap detection compatibility)
   - Resolution order: global SYS- persona → tenant CUST- persona → playbook-bound persona (most specific wins)
   - Existing 7-layer enrichment in `SprkChatAgentFactory.CreateAgentAsync` reads from `sprk_aipersona` instead of hardcoded text

2. **Output as the 6th scope** (rendering-destination + schema):
   - `AnalysisOutputEntity` represents output VALUES today; not output SCHEMAS
   - R6 adds output SCHEMAS as scopes declaring `widgetType`, `widgetSchema`, `renderingDestination` (chat / workspace / both / side-effect)
   - Enables Pillar 5 (output-type-aware rendering)

After R6, the Scope library is a complete six-entity model: **Action, Skill, Tool, Knowledge, Persona, Output** — all SYS-/CUST-, all inheritable, all consumable standalone via existing endpoints, all composable into playbooks via existing N:N relationships.

### 3.15 Pre-fill is the canonical "playbook from anywhere" pattern

The wizard pre-fill flow (Create New Matter / Create New Project / Create Work Assignment) is the platform's production proof that a playbook can be invoked from any UX context — not just from the playbook canvas. R6 PRESERVES this pattern entirely and uses it as the model for the generic `invoke_playbook` chat tool.

**Pattern shape** (per `docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md`):

1. Wizard uploads files → BFF endpoint (`POST /api/workspace/matters/pre-fill`)
2. BFF service (`MatterPreFillService`) extracts text + invokes facade
3. Facade (`IWorkspacePrefillAi.ExecutePlaybookAsync`) — NOT direct `IPlaybookOrchestrationService` injection (honors ADR-013 facade boundary)
4. Playbook configurable via app settings (`Workspace:PreFillPlaybookId`)
5. Stream returns `NodeCompleted` events with `StructuredData` JSON (preferred) or `TextContent` fallback
6. `$choices` resolution constrains AI output to exact Dataverse values
7. Frontend `useAiPrefill` hook handles invocation + lookup matching + "AI" sparkle badges
8. 45-second hardcoded timeout (non-blocking for UX)

**R6 reuses this pattern** for the generic chat tool. The `invoke_playbook(playbookId, params)` tool's handler:
- Calls a new facade method (extend `IWorkspacePrefillAi` or add `IInvokePlaybookAi`) following the same shape
- Hardcoded reasonable timeout
- Returns structured result the LLM presents conversationally in the chat thread
- No direct `IPlaybookOrchestrationService` injection (honors ADR-013 facade boundary)

The wizard-pre-fill flow stays exactly as-is — see §3.17 for the binding constraint.

### 3.16 Node executor surface preserved (not modified in R6)

The 11 node executors documented in `docs/architecture/playbook-architecture.md` are kept entirely as-is. R6 does NOT modify, deprecate, or extend any of them. They are the platform's processing-step library.

| Node executor | ActionType | What it does | R6 status |
|---|---|---|---|
| `AiAnalysisNodeExecutor` | 0 | LLM-powered analysis via tool handler registry + L1/L2/L3 knowledge + JPS rendering | Untouched. R6 builds the 8 missing typed tool handlers it dispatches to (Pillar 2). |
| `AiCompletionNodeExecutor` | 1 | Raw LLM completion with system prompt + user template | Untouched. |
| `ConditionNodeExecutor` | 30 | JSON expression evaluation, true/false branching | Untouched. |
| Start | 33 | Entry-point marker | Untouched. |
| `CreateTaskNodeExecutor` | 20 | Creates Dataverse task records via OData PATCH | Untouched. |
| `SendEmailNodeExecutor` | 21 | Sends email via Microsoft Graph (OBO) | Untouched. |
| `UpdateRecordNodeExecutor` | 22 | Writes typed field mappings (string/choice/boolean/number + lookups) to Dataverse | Untouched. |
| `DeliverOutputNodeExecutor` | 40 | Handlebars template rendering of upstream node outputs (Markdown/HTML/Text/JSON) | Untouched. |
| `DeliverToIndexNodeExecutor` | 41 | Enqueues RAG semantic indexing job via Service Bus | Untouched. |
| `CreateNotificationNodeExecutor` | 50 | Dataverse appnotification with idempotency check + iterate-items mode | Untouched. |
| `QueryDataverseNodeExecutor` | 51 | FetchXML execution with date/user variable resolution | Untouched. |

**Reserved ActionType values** for future expansion (not R6 scope): `RuleEngine` (10), `Calculation` (11), `DataTransform` (12), `CallWebhook` (23), `SendTeamsMessage` (24), `Parallel` (31), `Wait` (32). These remain enum placeholders without executors and are not R6 work.

### 3.17 Pre-fill flow preserved entirely (binding)

R6 MUST NOT modify the existing pre-fill flow used by Create New Matter, Create New Project, Create Work Assignment, and any other wizard that uses it. This is binding:

- `AppOnlyDocumentAnalysisJobHandler` running the "Document Profile" playbook on background upload analysis — **UNCHANGED**
- `MatterPreFillService` / `ProjectPreFillService` BFF services — **UNCHANGED**
- Endpoints `POST /api/workspace/{entity}/pre-fill` — **UNCHANGED**
- `IWorkspacePrefillAi` facade — **UNCHANGED** (R6 may extend with new methods but existing signatures stay)
- `useAiPrefill` hook + `findBestLookupMatch` utility + `AiFieldTag` component — **UNCHANGED**
- `Workspace:PreFillPlaybookId` / `Workspace:ProjectPreFillPlaybookId` configuration model — **UNCHANGED**
- `$choices`-constrained playbook output contract — **UNCHANGED**
- 45-second timeout — **UNCHANGED**

R6 treats this flow as canonical proven architecture and inherits from it for the generic playbook tool (Pillar 3). Future-contributor note: when refactoring tool dispatch or facade interfaces, the pre-fill code paths are a regression-test bar — they must continue to work without modification.

---

## 4. R6 Target Architecture

### 4.0 Strategic Constraints (binding for R6)

R6 honors these constraints derived from existing ADRs + architecture decisions. Pillar implementations must respect all of them.

1. **NO Microsoft Agent Framework.** A Spaarke-side functional/requirements review concluded that Agent Framework is not appropriate for R6 (or near-term). R6 treats the in-process `PlaybookExecutionEngine` as THE engine, full stop. No "backend flexibility" abstractions, no "future replacement" seams, no Agent Framework references in design or code. If Agent Framework is later evaluated, that is a separate project's call.

2. **ADR-013 facade boundary**: CRUD-side AI consumers MUST route through `Services/Ai/PublicContracts/` facades. R6's generic `invoke_playbook` chat tool dispatches via a facade method, not direct `IPlaybookOrchestrationService` injection.

3. **ADR-030 PaneEventBus closure**: Exactly 4 channels (workspace, context, conversation, safety). R6 adds additive event types only; no 5th channel.

4. **ADR-031 stage lifecycle determinism**: 4 stages (welcome / loading / active-chat / review). Pure `determineStage()` from `SessionState`. R6 does not add stages or modify transitions.

5. **Conversational primacy** (§1.6): LLM is the primary cognitive agent. Scopes augment; never replace. Playbooks are one capability among many.

6. **Playbooks remain the primary structured composition model**: Visual canvas, JPS, FK-traversed multi-node execution — all stay. R6 does not introduce a competing composition model.

7. **M365 Copilot integration thinness preserved**: Agent Gateway endpoints (`/api/agent/*`) remain thin adapters delegating to BFF services. R6 makes no changes to this contract.

8. **Insights "Zone B consumer" pattern preserved**: Chat-agent calls Insights via HTTP per ADR-013 §3.5; no direct injection of Insights internals from the chat agent. R6 reuses `InsightsIntentClassifier` for the chat-agent's playbook-vs-RAG routing decision.

9. **Pre-fill pattern preserved** (§3.17): Wizard pre-fill flow is untouched. R6's `invoke_playbook` tool follows its facade-routed shape.

10. **Document summary unified through playbook engine**: R6 Pillar 4 redirects `SessionSummarizeOrchestrator` through `PlaybookExecutionEngine`. No orphaned AI pipelines.

11. **Conversational + playbook share scope library**: Capabilities authored once (as scopes) are available in both modes via `AvailableInContexts` discriminator on `sprk_analysistool` rows.

12. **Conversation history + memory are the substrate for refinement**: Cosmos `sessions` + Cosmos `memory` are production infrastructure. R6 activates utilization for cross-conversation recall and smart context selection.

### 4.1 The 9 Refined Pillars

### Pillar 1 — Persona as 5th Scope Entity

**Current state**: Persona is hardcoded in `PlaybookChatContextProvider.BuildDefaultSystemPrompt()`. Playbook-bound personas come from the playbook's primary action's `sprk_systemprompt` via `PlaybookChatContextProvider:160-187`. Personas cannot be optimized without C# deploy, no tenant customization, no versioning.

**Gap**: The scope library has 4 production entities (Action / Skill / Knowledge / Tool) with full management infrastructure (SYS-/CUST-, inheritance, SaveAs, gap detection, standalone endpoints). Persona is the missing 5th scope.

**R6 work** — small addition following existing scope patterns:

1. New entity `sprk_aipersona`:
   - Inherits the standard scope pattern (`name`, `description`, owner-prefix discriminator, `parentScopeId` for inheritance)
   - `systemPrompt` field (the persona text)
   - `scopeType` (global / tenant / playbook-attached)
   - Versioned via existing scope versioning model
2. `PlaybookChatContextProvider` resolution order: global SYS- persona → tenant CUST- persona → playbook-bound persona (most specific wins). Same lookup pattern existing scopes use.
3. New endpoint `/api/ai/scopes/personas` (mirrors existing 4 scope endpoints)
4. Seed: a default global SYS- persona row containing the current hardcoded `BuildDefaultSystemPrompt()` text
5. Power Apps form for CUST- persona authoring (Dataverse-driven; no custom UI needed)

**What R6 reuses** (no changes):
- `OwnershipValidator` (SYS-/CUST- enforcement)
- `ScopeInheritanceService` (Extend pattern)
- `ScopeCopyService` (Save As)
- `ScopeManagementService` (CRUD)
- The 7-layer enrichment pipeline in `SprkChatAgentFactory.CreateAgentAsync` (system prompt assembly — just sources persona text from `sprk_aipersona` instead of hardcoded string)
- Playbook-bound prompt resolution via existing `PlaybookChatContextProvider` (unchanged)

**Conversational UX impact**: Voice tunable per tenant; LLM's conversational ability unchanged. Persona is augmentation.

**R6 effort**: ~3 days.

### Pillar 2 — Tool Registry Convergence + 8 Typed Handlers

**Current state**: TWO orthogonal tool worlds (§3.13):
- Chat-agent: 12 hardcoded C# classes in `SprkChatAgentFactory.ResolveTools()` (lines 721-1327)
- Playbook: `sprk_analysistool` rows + `IAnalysisToolHandler` registry + auto-discovery + dispatch — 4 of 12 declared handlers actually implemented; 8 referenced in seed data but missing in code

**Gap**: 
- Chat-agent tools should be data-driven the same way playbook tools are
- 8 typed handlers declared in seed data aren't implemented (playbook authors get "Tool handler not registered" errors when they reference them)

**R6 work**:

1. **Generalize the existing registry** for cross-context use:
   - Rename `IAnalysisToolHandler` → `IToolHandler` (signatures unchanged; backward-compatible alias)
   - Add `AvailableInContexts` field to `AnalysisTool` DTO (values: `Playbook` / `Chat` / `Both`)
   - Add `JsonSchema` field for chat-context tools (parameter declaration for function calling)
   - DI registration + dispatch via `IToolHandlerRegistry` — UNCHANGED (assembly auto-discovery still works)
   - Execution context: shared base + mode-specific extensions (`PlaybookExecutionContext` keeps current `ToolExecutionContext` shape; `ChatInvocationContext` adds chat-session + LLM parameter passing)

2. **Build the 8 unimplemented typed handlers**:
   - `EntityExtractorHandler` — Named Entity Recognition (LLM + code validation/normalization)
   - `ClauseAnalyzerHandler` — Contract clause structuring (LLM + structural diff)
   - `RiskDetectorHandler` — Risk identification + severity scoring (LLM + code-based scoring)
   - `ClauseComparisonHandler` — Pure deterministic text-diff with structural awareness
   - `DateExtractorHandler` — Pure deterministic date normalization + relative-date resolution
   - `FinancialCalculatorHandler` — Pure deterministic math on extracted figures (currency-aware)
   - `InvoiceExtractionToolHandler` — LLM extraction + line-item arithmetic
   - `FinancialCalculationToolHandler` — Pure deterministic formulas (rate × principal × time)

3. **Build chat-agent adapter** (`ToolHandlerToAIFunctionAdapter`):
   - Reads `sprk_analysistool` rows where `AvailableInContexts ∋ "Chat"`
   - Wraps each `IToolHandler` as `AIFunction` via `AIFunctionFactory.Create()` with JSON Schema from row's `parameterSchema`
   - Registers with `Microsoft.Extensions.AI` infrastructure (existing function-invocation `IChatClient` picks them up)

4. **Migrate the 12 hardcoded chat-agent tool classes** to follow the same shape:
   - 10 pre-R5 classes (DocumentSearch, AnalysisQuery, Knowledge, TextRefinement, WorkingDocument, AnalysisExecution, WebSearch, CodeInterpreter, LegalResearch, VerifyCitations) — refactor as `IToolHandler` implementations + corresponding `sprk_analysistool` rows with `AvailableInContexts = "Chat"` (or `"Both"` where applicable)
   - 2 R5 bridge classes (`InvokeSummarizePlaybookTool`, `InvokeInsightsQueryTool`) — replaced by ONE generic `invoke_playbook` tool (Pillar 3)

**What R6 reuses** (no changes):
- `Microsoft.Extensions.AI` / `AIFunction` / `IChatClient` infrastructure
- `CapabilityRouter` 3-tier per-turn filtering
- Capability gates from `GetPlaybookCapabilitiesAsync`
- `IToolHandlerRegistry` auto-discovery via reflection at startup
- `GenericAnalysisHandler` as workhorse fallback
- Playbook-side scope resolution via `IScopeResolverService.ResolveNodeScopesAsync()`

**Conversational UX impact**: More deterministic capabilities the LLM CAN invoke when it chooses. LLM's conversational ability unchanged. CapabilityRouter continues to filter per turn.

**R6 effort**: 
- Infrastructure (generalization + adapter + chat-tool migration): ~3-5 days
- 8 typed handlers: ~15-25 days (parallelizable as separate workstream; some are pure deterministic, some LLM-assisted with structured logic)

### Pillar 3 — Generic `invoke_playbook` Chat Tool (via facade pattern)

**Current state**: 2 specialized C# bridge tools (`InvokeSummarizePlaybookTool`, `InvokeInsightsQueryTool`) — one per chat-callable playbook. Adding a new chat-callable playbook requires a new C# tool class + deploy. Doesn't scale.

**Gap**: One generic chat tool that routes to any playbook based on its registered description.

**R6 work**: ONE generic `invoke_playbook(playbookId, params)` chat tool following the production **pre-fill facade-routed pattern** (§3.15, §3.17).

Implementation:
- Extend `IWorkspacePrefillAi` facade with `ExecuteAnyPlaybookAsync(...)` method, OR add new `IInvokePlaybookAi` facade — honors ADR-013 facade boundary (R6 must not inject `IPlaybookOrchestrationService` directly into the chat-tool handler)
- Tool description dynamically populated at chat-agent build time with active playbook list (filtered by tenant + capability gates)
- Tool handler:
  - Validates playbookId against `sprk_analysisplaybook` rows accessible to this tenant
  - Validates parameters against playbook's parameter schema (via `$choices` resolution where applicable)
  - Calls facade → which calls `IPlaybookOrchestrationService.ExecuteAsync()`
  - Streams `NodeCompleted` events with `StructuredData` (preferred) or `TextContent` (fallback)
  - Returns conversational result the LLM presents to the user
- Two R5 bridges (`InvokeSummarize…`, `InvokeInsights…`) removed; their callers route through the generic tool

**What R6 reuses** (no changes):
- `PlaybookExecutionEngine` (dual mode)
- `PlaybookOrchestrationService` (FK chain traversal, parallel batching, topological sort)
- All 11 node executors
- JPS pipeline (6-layer rendering)
- Pre-fill flow (§3.17) — UNCHANGED; this pattern is the template R6 follows
- `InsightsIntentClassifier` — chat agent uses this for playbook-vs-RAG routing decisions before invoking

**Conversational UX impact**: LLM sees ONE `invoke_playbook` tool with a list of available playbook descriptions. LLM decides when to invoke a playbook vs respond conversationally vs use a different tool. After playbook execution returns, the LLM presents the result in chat and conversation resumes normally.

**R6 effort**: ~2 days.

### Pillar 4 — Route Chat `/summarize` Through `PlaybookExecutionEngine`

**Current state**: `SessionSummarizeOrchestrator.cs:443-481` bypasses the playbook FK chain entirely. Loads `SUM-CHAT@v1` action by alternate key (`sprk_actioncode`) instead of via playbook → node → action.

**Gap**: Architectural inconsistency. The batch playbook orchestrator (`PlaybookOrchestrationService`) correctly traverses the FK chain. Only the chat `/summarize` path bypasses.

**R6 work**: Redirect `SessionSummarizeOrchestrator` to invoke `PlaybookExecutionEngine.ExecuteAsync(playbookId: "summarize-document-for-chat@v1", ...)`. The engine already traverses the FK chain correctly; the chat path joins that pattern.

Implementation:
- Data fix: wire `summarize-document-for-chat@v1` playbook's node FK to the `SUM-CHAT@v1` action row
- `SessionSummarizeOrchestrator` becomes a thin caller: looks up playbook by name → passes to `PlaybookExecutionEngine` → streams results back
- All existing functionality preserved (session-files Azure Search filter, Structured Outputs, streaming JSON delta, JSONPath strip in bridge)

**What R6 reuses** (no changes):
- `SUM-CHAT@v1` action definition + JPS prompt
- Structured Outputs mode + `IncrementalJsonParser`
- `sseToPaneEventBridge` JSONPath strip (R5 cycle-9 fix)
- Session-files index queries (`spaarke-session-files` filter by tenantId + sessionId + documentId)
- All R5 chat-side wiring (intent matcher, executeSummarizeIntent, attachment flow)

**Conversational UX impact**: Architectural cleanup; user-visible behavior unchanged.

**R6 effort**: ~2 days.

### Pillar 5 — Output Schema as 6th Scope + Schema-Aware Renderers

**Current state**:
- Output destination is hardcoded per call site (chat agent renders text in chat; `/summarize` endpoint streams to workspace)
- `StructuredOutputStreamWidget` renderer is string-only: for array-typed (`tldr`) or object-typed (`entities`) fields, it concatenates raw JSON tokens producing the R5 rendering bugs (TL;DR shows `data loss.","Organizations miss..."`; Entities shows `organizations":["ACME..."]`)

**Gap**: Output schemas should be declarable as data, and renderers should be schema-aware.

**R6 work**:

1. **Add Output as a 6th scope** (`sprk_analysisoutput_schema`):
   - Inherits standard scope pattern (SYS-/CUST-, inheritance, SaveAs)
   - `renderingDestination` (chat / workspace / both / side-effect)
   - `widgetType` (for workspace destinations) — e.g., `StructuredOutputStreamWidget`, `RichFilePreview`, `EntitySummaryWidget`
   - `widgetSchema` (the schema the renderer expects, declaring each field's type: string / array / object)
   - Referenced from `sprk_analysisaction` or `sprk_playbooknode` via lookup (an action declares which output schema it produces)

2. **Make renderers schema-aware**:
   - Renderer registers what schemas it can handle (registry pattern matches existing `WorkspaceWidgetRegistry`)
   - For string fields: accumulate token deltas, render progressively (current behavior, unchanged)
   - For array fields: accumulate tokens until `streaming_complete`, JSON.parse, render as bulleted list
   - For object fields: accumulate, parse, render as labeled key-value blocks
   - Renderer reads the scope's schema per field and dispatches to the right sub-renderer

3. **Orchestrator routes per output scope**:
   - For `chat`: returns text/markdown response inline (existing chat behavior)
   - For `workspace`: publishes PaneEventBus events with configured widget type (additive event types within existing `workspace` channel per ADR-030)
   - For `both`: emits chat ack + workspace artifact via ONE LLM call (eliminates the R5 duplicate-fire problem structurally)

**What R6 reuses** (no changes):
- `StructuredOutputStreamWidget` core (correlationId filtering, PaneEventBus subscription)
- `sseToPaneEventBridge` SSE → PaneEventBus transformer (R5 JSONPath strip preserved)
- JPS Structured Outputs JSON Schema generation
- PaneEventBus channels (additive event types only)
- 4-stage shell lifecycle

**Conversational UX impact**: Workspace summaries render properly. Chat behavior unchanged. The user can immediately ask follow-up questions or refinements after a summary completes — the structured output is in the workspace AND in conversation history.

**R6 effort**: ~5-7 days.

### Pillar 6 — Tri-directional Assistant ↔ Context ↔ Workspace State Model + Execution-Trace Widget

**Current state** (verified against code):
- Chat agent CAN dispatch `workspace.widget_load` PaneEventBus events (mount tabs)
- Chat agent CANNOT read current workspace state (open tabs, active tab, user selection, edits)
- User CANNOT promote chat → workspace ("Send to Workspace" button)
- User CANNOT promote workspace → chat ("Add to Assistant" toggle)
- Context pane is a sink: 12 widgets across 5 stages; NO execution-trace widget (R5 task 037 deferred)
- Pane communication via 4-channel PaneEventBus (workspace/context/conversation/safety per ADR-030)

**Gap**: One-directional today; needs to be tri-directional with Context pane as the third member.

**R6 work**:

**Workspace State Model** (typed, persisted, agent-readable):
- Canonical Tab Schema (`WorkspaceTab` interface): typed `widgetType`, `widgetData`, `sessionId`, `visibleToAssistant: boolean`, `sourceProvenance`, `matterContext`, `isPinned`, `canEdit`, audit fields
- Redis hot tier (24h TTL) for active session — reuse existing Redis infrastructure
- Cosmos durable tier for pinned tabs + matter-attached artifacts — reuse existing Cosmos `memory` container or add `workspace_tabs` container per established pattern
- Per-turn workspace snapshot included in agent system prompt: *"Tab 1 (active): Summary of Engagement Letter.docx, created 5 min ago. Tab 2: Documents pinned for Matter X. User is viewing Tab 1; has selected text: 'ACME Corporation...'"*

**Read API** (agent reads workspace state):
- `GET /api/workspace/state` BFF endpoint (tenant-scoped, session-scoped)
- Per-widget `getAgentVisibleState()` contract (Pillar 9 details)
- At `CreateAgentAsync` time, factory queries workspace state + includes in system prompt

**Write API** (chat tools mutate workspace):
- `send_workspace_artifact(content, widgetType)` — new chat tool
- `update_workspace_tab(tabId, changes)` — new chat tool
- `close_workspace_tab(tabId)` — new chat tool
- All dispatch via existing PaneEventBus on `workspace` channel (additive event types; no 5th channel per ADR-030)

**Context-pane execution-trace widget** (R5 task 037, deferred — Claude-Code-like process stream):
- New widget type registered with `ContextWidgetRegistry`
- Subscribes to additive events on `context` channel (no new channel per ADR-030):
  - `context.tool_call_started` / `context.tool_call_completed`
  - `context.knowledge_retrieved` (RAG queries with source provenance)
  - `context.playbook_node_executing` / `context.playbook_node_completed`
  - `context.decision_made` (capability-router decisions visible)
- Renders ordered timeline of tool invocations, knowledge sources consulted, decisions made — matching Claude Code's transparency surface
- Per-turn telemetry from chat agent + playbook execution emits these events (existing telemetry infrastructure from R5 task 008)

**Bidirectional flow** (PaneEventBus additive event types):
- Workspace → Assistant: `workspace.user_selection`, `workspace.tab_edited`, `workspace.tab_focused` (informs LLM's next-turn context)
- Workspace → Context: `workspace.tab_provenance_clicked` (Context shows tab's execution history via trace widget)
- Assistant → Context: `context.execution_started` / `context.execution_completed` (formalize existing telemetry flow)
- Context → Workspace: `context.source_referenced` (highlight related tab)

**User affordances**:
- "Send to Workspace" button on chat assistant messages
- "Add to Assistant" toggle on user-created tabs (flips `visibleToAssistant: true`)
- "Pin to Matter" persists tab attached to a matter record

**Conflict resolution**:
- User edit timestamp tracked per tab
- Agent updates check timestamp + merge OR refuse with "tab was edited by user; please re-read"
- User wins per Q7 (no OT/CRDT in R6)

**What R6 reuses** (no changes):
- 4-channel PaneEventBus per ADR-030
- 4-stage shell lifecycle per ADR-031
- 12 existing context-pane widgets (new execution-trace widget added; existing untouched)
- Pre-fill flow (§3.17) — UNCHANGED
- ChatSession persistence in Cosmos `sessions` container
- Redis hot tier infrastructure

**Conversational UX impact**: ENHANCES conversational ability. The LLM can reference workspace state in conversation ("update the summary in Tab 1 to be shorter"). The user sees what the LLM is doing in real time via the Context-pane trace. Workspace tabs become conversational artifacts. Refinement, comparison, follow-up — all work because the LLM has workspace + memory + conversation context.

**R6 effort**: ~13-20 days (largest pillar). State model + read/write APIs + bidirectional events + Context trace widget + UI affordances + conflict logic + tests.

### Pillar 7 — Cross-Conversation Memory + Smart Recall (Utilize Existing Infrastructure)

**Current state** (verified):
- Cosmos `memory` container (90d TTL, `/userId` partition) — **EXISTS in production**
- Cosmos `sessions` container (90d TTL, `/userId` partition) — **EXISTS**; full conversation history written through
- Redis hot tier (24h TTL) for active session — **EXISTS**
- 8K token system prompt budget — **EXISTS** in `PlaybookChatContextProvider`
- Sliding-window memory only — current implementation
- No summarization compression, no pinned context, no selective recall

**Gap**: The persistence infrastructure exists but utilization is limited. R6 builds smart memory composition on top.

**R6 work** (utilizes existing Cosmos containers + Redis + token budget):

1. **Smart memory composition**:
   - Sliding window (kept; recent verbatim)
   - **Summarization compression** — when sliding window exceeds budget, replace oldest M turns with LLM-generated summary. Append `[Conversation summary: ...]` at history top.
   - **Pinned context** — explicit user preferences, system rules, key facts that never drop from prompt. Stored in Cosmos `memory` container with `pinType: 'user-preference' | 'system-rule' | 'matter-fact'`.
   - **Selective recall** — embed every turn at write time; retrieve via embedding similarity for relevant old turns when current question relates to them.
   - **Hierarchical memory**: recent verbatim (last 10 turns) + compressed mid-distance (turns 10-50 as summary blocks) + retrieved old (turns 50+ via similarity).

2. **Cross-conversation recall** (utilization of existing `memory` container):
   - Per-matter memory snapshots — already supported by `MatterMemoryService`; R6 activates utilization in chat-agent system prompt assembly
   - At chat-agent build time, current session's matter context loads related memory snapshots
   - Conversation across sessions on the same matter is coherent

3. **Token budget API**:
   - Shared budget tracker so factory + document context + knowledge retrieval + memory don't over-commit prompt tokens
   - Respects existing 8K budget; doesn't change the ceiling

4. **User-facing memory affordances** (R7+ stretch; R6 builds primitives):
   - "What do you remember about me?" agent capability (reads pinned context)
   - "Forget X" removes a pinned fact
   - "Always X" pins a new preference

**What R6 reuses** (no changes):
- Cosmos `sessions` + `memory` containers (write-through, partition keys, TTLs)
- Redis hot tier (24h TTL)
- `MatterMemoryService`, `SessionPersistenceService` (existing services)
- 8K token budget hardcoded ceiling
- `IEmbeddingCache` for vectorizing turns at write time

**Conversational UX impact**: **MOST DIRECT enabler of conversational refinement.** Cross-conversation recall + smart context selection + pinned context means:
- "The contract we reviewed yesterday" actually works (cross-session matter memory)
- "Make it more terse" mid-conversation works with full context (sliding + compressed history)
- "Always use formal tone for opposing counsel" persists as a pinned preference
- 100-turn conversations remain coherent (oldest facts retrievable via summary or similarity)

**R6 effort**: ~4-6 days (compression service + pinned-context entity + selective-recall + budget tracker + integration). Less than original estimate because infrastructure exists.

### Pillar 8 — Command Router (slash/hash/at vocabulary)

**Current state**: Ad-hoc regex matchers in `intentMatcher.ts`. No formal vocabulary or parser layer. The R5 intent matcher (task 036) is a tiny precursor.

**Gap**: Formal command vocabulary + parser layer.

**R6 work** — formal vocabulary + router (no change from original framing):

**Hard slashes** (deterministic system commands; bypass LLM):
- `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter [matterId]`, `/pin`

**Soft slashes** (intent shortcuts; flow through agent with priority signal):
- `/summarize`, `/draft`, `/extract-entities`, `/analyze`

**References** (data identifiers; agent uses as context):
- `#contracts` — scope reference (search within tag)
- `@<entity>` — entity reference (resolves to matter / person / organization)
- `#engagement-letter.docx` — file reference (resolves to session file or workspace tab)

**Composition**:
- `/summarize #engagement-letter.docx`
- `/draft response to @opposing-counsel about #motion-to-dismiss`

**Implementation**:
- `CommandRouter` parser layer parses user input into structured `Intent { command, references[], rawText }`
- Hard slashes execute directly (no LLM call)
- Soft slashes set strong intent signal + route to agent (agent can still ask clarifying questions if intent isn't actionable)
- References resolve at parse time + included in agent's prompt as known entities

**What R6 reuses** (no changes):
- Existing `CapabilityRouter` 3-tier per-turn routing (CommandRouter complements; doesn't replace)
- Natural language requests still work — slashes are shortcuts, not replacements
- Existing `intentMatcher.ts` patterns evolve into the parser layer (R5 task 036's groundwork)

**Conversational UX impact**: Slashes COMPLEMENT conversation, never replace it. Saying "summarize this" in natural language still works (CapabilityRouter Layer 1 catches it); `/summarize` is the explicit shortcut for power users.

**R6 effort**: ~5-8 days.

### Pillar 9 — Workspace Widget Visibility Contract

**Current state**: Workspace widgets are black-box to the agent. No `getAgentVisibleState()` mechanism. CalendarSection's controlled-component pattern (per `SPAARKEAI-COMPONENT-MODEL.md` §6.5) exists but is parent-only, not agent-facing.

**Gap**: Agent needs a token-efficient way to "see" each widget's state. Reading the entire widget data per turn would blow the token budget.

**R6 work** — per-widget visibility contract enabling agent awareness without token bloat:

**Contract**:
- Each widget type implements `getAgentVisibleState(): SerializedWidgetState`
- Returns compact, schema-typed representation of widget state
- Examples:
  - Summary tab: `{ widgetType: 'Summary', summary: '...', tldr: [...], hasUserEdits: bool }`
  - DocumentViewer: `{ widgetType: 'DocumentViewer', filename, mimeType, sizeBytes, hasSelection, selectionText? }`
  - Dashboard: `{ widgetType: 'Dashboard', dashboardName, lastViewedSection }` — NOT the full chart data
  - Table: `{ widgetType: 'Table', rowCount, sortColumn, filteredColumns, selectedRows[] }`

**Integration**:
- Per-turn agent prompt builder gathers `getAgentVisibleState()` from each Assistant-visible tab
- Includes in system prompt's "Workspace State" block
- Widgets declare default agent-visibility in `WorkspaceWidgetRegistry` registration metadata (optional `getVisibleState?: () => unknown` field)
- User can override via "Add to Assistant" toggle (Pillar 6)
- Privacy: widgets that should NOT expose to LLM (e.g., user's private dashboard) don't appear in agent prompts (ADR-015 data governance)

**What R6 reuses** (no changes):
- `WorkspaceWidgetRegistry` core registration (extended with optional field)
- Existing widgets continue to work (visibility opt-in, not retrofitted automatically)
- ADR-015 data governance — widgets choose what to expose

**Conversational UX impact**: Enables workspace-aware conversation. "Update the summary in the workspace to be shorter" — agent knows which tab + can target it. "What does the selected text in the document mean?" — agent has the selection. "What did we conclude on the third tab?" — agent reads tab state.

**R6 effort**: ~3-5 days (contract definition + per-widget implementation + prompt-builder integration).

---

## 5. Implementation Sequencing

Approximate calendar weeks; refined estimates per §4 pillar work (substantially smaller than original framing because most of the platform's data-driven architecture is already production-mature — see §3.12). The 8 typed tool handlers (Pillar 2) are a separable workstream that can run in parallel with the architectural critical path.

### Phase A — Foundations (Week 1-2)

**Goal**: Chat-agent aligned with playbook-side patterns (persona as scope, tool registry convergence infrastructure, FK redirect, generic playbook tool).

| Pillar | Days |
|---|---|
| 1 — Persona as 5th Scope Entity | 3 |
| 2 — Tool Registry Convergence (infrastructure only; handlers parallel) | 3-5 |
| 3 — Generic `invoke_playbook` Chat Tool | 2 |
| 4 — Route Chat `/summarize` Through PlaybookExecutionEngine | 2 |

**Exit criteria**:
- Chat-agent tool list driven by `sprk_analysistool` rows with `AvailableInContexts ∋ "Chat"`
- Persona is a Dataverse-driven scope (`sprk_aipersona`)
- One generic `invoke_playbook` chat tool exists; specialized bridges removed
- `SessionSummarizeOrchestrator` routes through `PlaybookExecutionEngine` with FK-resolved playbook → node → action chain

### Phase B — Rendering + Output Schemas (Week 2-3)

**Goal**: Output schemas declarable as data; renderers are schema-aware; R5 TL;DR/Entities bugs fixed structurally.

| Pillar | Days |
|---|---|
| 5 — Output Schema as 6th Scope + Schema-Aware Renderers | 5-7 |

**Exit criteria**:
- Workspace renderer handles array-typed (`tldr`) and object-typed (`entities`) fields correctly
- Output destination declared in `sprk_analysisoutput_schema` rows (chat / workspace / both / side-effect)
- New playbook can target a specific surface without code changes
- Duplicate-fire problem eliminated structurally (one orchestrator call emits chat ack + workspace artifact)

### Phase C — Tri-directional Shell + Memory (Week 3-5)

**Goal**: Workspace state is typed, persistable, agent-readable. Context pane gets execution-trace widget. Cross-conversation memory utilization activates.

| Pillar | Days |
|---|---|
| 6 — Tri-directional State Model + Context Trace | 13-20 (largest pillar) |
| 9 — Workspace Widget Visibility Contract | 3-5 |
| 7 — Memory Utilization (parallel with 6) | 4-6 |

Pillar 7 runs in parallel with Pillar 6 (different code paths — Cosmos memory layer vs frontend state model). Pillar 9 lands at the end of Phase C (contract requires the state model + execution-trace widget).

**Exit criteria**:
- Agent has accurate workspace awareness in every prompt (via `getAgentVisibleState()`)
- "Update the summary in the workspace" works (LLM targets specific tab)
- User can "Send to Workspace" + "Add to Assistant" + "Pin to Matter"
- Context pane shows live execution trace (tool calls, knowledge retrieved, decisions made)
- Cross-conversation memory recalls prior-matter context ("the contract we reviewed yesterday")
- Pinned facts persist as user preferences

### Phase D — Command Router + Polish (Week 5-6)

**Goal**: Formal command vocabulary; integration tests; bug bash.

| Pillar | Days |
|---|---|
| 8 — Command Router | 5-8 |
| Integration tests | 3-4 |
| Polish + bug bash | 2-3 |

**Exit criteria**:
- `/help` works and is discoverable
- Hard slashes bypass LLM (instant response)
- Soft slashes route via agent with prioritized intent
- Reference syntax (`@`, `#`) parses + resolves
- All R6 changes have integration test coverage

### Parallel workstream — 8 typed tool handlers

Runs alongside Phases A-D; not on the architectural critical path. Handlers are isolated domain code that can be authored independently and integrated as they complete.

| Handler | Type | Days |
|---|---|---|
| EntityExtractorHandler | LLM-assisted + structured logic | 2-4 |
| ClauseAnalyzerHandler | LLM-assisted + structural diff | 3-5 |
| RiskDetectorHandler | LLM-assisted + scoring code | 2-4 |
| ClauseComparisonHandler | Pure deterministic text-diff | 1-2 |
| DateExtractorHandler | Pure deterministic date normalization | 1-2 |
| FinancialCalculatorHandler | Pure deterministic math | 1-2 |
| InvoiceExtractionToolHandler | LLM extraction + line-item math | 3-5 |
| FinancialCalculationToolHandler | Pure deterministic formulas | 2-3 |

**Subtotal**: 15-25 days for handlers (parallel; doesn't extend critical path)

### Effort summary

| Category | Days |
|---|---|
| Phase A foundations (Pillars 1-4) | 10-12 |
| Phase B rendering (Pillar 5) | 5-7 |
| Phase C tri-directional + memory (Pillars 6, 7, 9) | 20-31 |
| Phase D router + polish (Pillar 8 + tests) | 10-15 |
| **Architectural critical path** | **~45-65 days** |
| **8 typed handlers (parallel workstream)** | **~15-25 days** |
| **Total scope** | **~60-90 engineer-days** |

### Calendar timeline

- **Single developer, full focus, sequential**: ~7-9 weeks
- **Two developers parallel**:
  - Dev A: architectural critical path (Pillars 1, 2-infra, 3, 4, 5, 6, 7, 8, 9)
  - Dev B: 8 typed handlers (parallel workstream) + integration tests
  - Compresses to ~4-5 weeks calendar
- **Recommended pacing**: 5-7 calendar weeks with appropriate parallelism. Hard handlers (Invoice, ClauseAnalyzer) can extend if domain complexity exceeds estimates.

---

## 6. Vertical-Slice Validation Target

R6 should produce a redesigned Summarize vertical slice that validates every pillar end-to-end. Same user-facing intent ("summarize a document") but executed through the R6 patterns:

1. User uploads file via paperclip — frontend client-side text extraction (mammoth/pdfjs) UNCHANGED from R5
2. User types `/summarize` — soft slash, parsed by `CommandRouter` (Pillar 8), priority intent signal passed to agent
3. Agent's chat-completion call assembled with:
   - **Persona** pulled from `sprk_aipersona` (Pillar 1; data-driven, tenant-resolvable, versioned)
   - **Tools** pulled from `sprk_analysistool` rows with `AvailableInContexts ∋ "Chat"` (Pillar 2), wrapped as `AIFunction` via `ToolHandlerToAIFunctionAdapter` — including the generic `invoke_playbook` tool with description containing active playbook list (Pillar 3)
   - **Skills + Knowledge** inherited from any bound playbook's scope set
   - **Workspace state** included in system prompt: each tab's `getAgentVisibleState()` output (Pillar 9), filtered by `visibleToAssistant: true`
   - **Memory**: recent verbatim turns (Cosmos `sessions`) + compressed mid-distance summary + pinned context + selective recall via embedding similarity (Pillar 7)
4. Agent decides: user requested `/summarize` with priority intent → calls `invoke_playbook("summarize-document-for-chat@v1", { fileIds: [...] })`
5. Generic tool handler routes via facade (`IWorkspacePrefillAi.ExecuteAnyPlaybookAsync` or new `IInvokePlaybookAi`) — ADR-013 facade boundary honored
6. Facade calls `PlaybookExecutionEngine.ExecuteAsync(playbookId, params)` — engine traverses playbook → node → action FK chain (Pillar 4; no bypass)
7. Playbook walked: single node, action `SUM-CHAT@v1` (FK-linked), Output scope declares `renderingDestination: "both"` (Pillar 5) → chat ack + workspace artifact
8. LLM call: Structured Outputs constrained to JSON Schema from JPS `output.fields`
9. As tokens stream:
   - Context pane execution-trace widget shows the playbook node executing + tool call started/completed + knowledge retrieved (Pillar 6 additive `context.*` events)
   - Workspace tab streams via `sseToPaneEventBridge` → PaneEventBus `workspace.field_delta` events → `StructuredOutputStreamWidget` (schema-aware: TL;DR renders as bulleted list, Entities renders as orgs/persons labeled groups per Pillar 5)
10. Workspace tab created with `sessionId: current, visibleToAssistant: true, sourceProvenance: { kind: 'playbook-execution', refId: playbookRunId }, matterContext: ...` (Pillar 6 state model)
11. Agent emits chat acknowledgment ("I've added a summary to the Workspace.") — SHORT, not a duplicate full summary
12. User can now:
    - Naturally refine ("make the risk section more detailed") — LLM has prior playbook output in conversation history + can read workspace tab state → responds conversationally; can update tab via `update_workspace_tab(tabId, ...)` chat tool (Pillar 6 write API)
    - Pivot ("compare this to yesterday's contract") — LLM uses cross-conversation memory (Pillar 7) to recall yesterday's analysis from `memory` container
    - Click "Send to Matter" on the tab → tab persists in Cosmos durable tier (Pillar 6)
    - Click into the workspace and edit the summary inline → edit timestamp tracked; agent's next read sees user edits
    - Type "make it shorter" — agent reads workspace state via `getAgentVisibleState()`, knows which tab to target, dispatches `update_workspace_tab`
    - Type `/clear` (hard slash) → session resets without LLM call (Pillar 8)
    - Close the tab → confirmation
13. Throughout: conversational ability is preserved. Most turns DON'T invoke a playbook; the LLM responds using bound persona + skills + knowledge + memory + workspace state. The vertical slice exercises the LLM's full conversational range alongside the structured playbook flow.

This validates every pillar.

---

## 7. Open Questions / Decisions Needed

These need user input before R6 implementation kicks off:

### Q1 — Persona scope hierarchy

How should personas compose? Options:
- A) Single active persona per scope; most specific wins (global < tenant < playbook-attached)
- B) Composable personas (global persona text + tenant overlay text + playbook-specific section, concatenated)
- C) Just tenant-level overrides (playbook prompt stays separate from persona scope)

Recommendation: **A** (most-specific-wins), matching the existing scope inheritance pattern. Simpler and consistent with `ScopeInheritanceService` behavior.

### Q2 — Persona entity: standalone vs extension

`sprk_aipersona` as a new standalone entity, OR extend `sprk_analysisaction` with `actionType=Persona` discriminator?

- Standalone: cleaner conceptual model; persona is genuinely different from action
- Extension: fewer entities; reuses existing action infrastructure

Recommendation: **standalone `sprk_aipersona` entity**. Persona is functionally distinct (no JPS, no `$choices`, no output schema — just a system-prompt text + inheritance). The scope library's strength is each entity type having a clear conceptual role.

### Q3 — Scope admin UI scope

Power Apps Dataverse forms can author scope rows today. Do we need a custom admin UI for persona/output-schema scope authoring in R6, or defer to R7?

Recommendation: **defer to R7**. Power Apps forms work fine for SYS-/CUST- scope authoring; custom UI is a polish investment. Builder agent (`BuilderAgentService`) can author scope rows agentically when needed.

### Q4 — Workspace tab persistence policy

Should ALL workspace tabs persist by default, or only when explicitly pinned? Default-persist vs default-ephemeral.

- Default-persist: better for users who don't think to pin; storage cost
- Default-ephemeral: cleaner; user has to opt-in to keep
- Hybrid: agent-generated tabs default-ephemeral until pinned; user-pinned + matter-attached survive

Recommendation: **hybrid**. Aligns with how Cosmos `memory` container already partitions (per-user pinned content vs ephemeral session state).

### Q5 — Output Schema scope entity scope

Same question as Q2 but for the 6th scope (Output): standalone `sprk_analysisoutput_schema` entity, OR extend `sprk_analysisaction` with output-schema fields?

Recommendation: **extend `sprk_analysisaction`**. The output schema is a property of an action (what does this action produce + where does it go). Different from persona (which is unrelated to specific actions).

### Q6 — Slash command vocabulary final list

We listed candidate hard/soft slashes. Need to finalize the production set:
- Should `/save-to-matter` be a hard slash (deterministic) or soft (agent confirms before writing)?
- `/draft` — is this a slash command, or always natural language?
- How many slashes is too many?

Recommendation: **~5 hard slashes + ~5 soft slashes**. Drive a UI affordance ("show available commands" menu via `/help`).

### Q7 — Cross-conversation memory user affordances in R6 vs R7

Pillar 7 builds the cross-conversation memory storage + retrieval. User-facing affordances ("What do you remember about me?" / "Forget X" / "Always X") — ship in R6 or defer to R7?

Recommendation: **R6 builds primitives; basic user-facing affordances ship in R6**. The chat-agent can recognize "remember/forget/always" intents via existing CapabilityRouter + dispatch to pinned-context tool. Full UI panel ("Pinned Memory" management view) is R7 polish.

### Q8 — Conflict resolution policy

If the user edits a workspace tab while the agent is also generating an update, what wins?

- Last writer wins (simple, sometimes destructive)
- User wins (agent gets a "tab was edited by user; please re-read" response)
- Operational transform / CRDT (complex, real-time merge)

Recommendation: **user wins**. Agent reads state at write time; if user edit timestamp is newer than agent's read, agent refuses with a polite re-read prompt. CRDT is overkill for R6.

### Q9 — Tool registry migration timing

Migrate all 12 hardcoded chat-agent tool classes to `sprk_analysistool` rows in one shot, or migrate incrementally?

- One shot: faster but riskier
- Incremental: validate per-tool; both registrations co-exist temporarily

Recommendation: **incremental**. Start with the generic `invoke_playbook` tool (Pillar 3), validate, then migrate the remaining 10 over Phase A. The 2 R5 bridges (`InvokeSummarize…`, `InvokeInsights…`) are removed when generic tool ships.

### Q10 — Eval harness scope

Should R6 include an eval harness for personas + playbooks, or defer to R7?

R6 makes everything data-driven; an eval harness lets us measure quality changes. But it's a real engineering investment (~1-2 weeks).

Recommendation: **lightweight eval in R6, full harness in R7**. R6 ships: a set of test conversations + expected behaviors per persona/playbook (can be markdown-driven). Full harness with metrics + CI integration is R7.

### Q11 — Pre-fill facade extension vs new facade

Pillar 3's generic `invoke_playbook` chat tool needs a facade. Options:
- Extend existing `IWorkspacePrefillAi` with `ExecuteAnyPlaybookAsync`
- Add new `IInvokePlaybookAi` facade (cleaner separation; small surface)

Recommendation: **new `IInvokePlaybookAi` facade**. `IWorkspacePrefillAi` is conceptually about wizard pre-fill (specific use case); a generic playbook-invocation facade is conceptually distinct and avoids overloading the existing facade.

---

## 8. Appendix — Key Discussion Notes (verbatim insights captured for R6 reference)

These are condensed insights from the architecture chat. Preserved verbatim because they capture nuance that the structured sections above abstract away.

### A. Why two paths exist (history vs intent)

> The chat agent (Path A) was built FIRST, as part of the SprkChat system (R2/R3 era). It's the general-purpose conversational AI. R5 (current project) introduced Path B as a vertical slice with a different design philosophy: structured outputs streamed to a dedicated workspace surface, deterministic execution (LLM bypassed for routing). It was added ALONGSIDE Path A — not as a replacement. The duplicate response is unintentional. It's two layers that both happen to fire for the same input.

### B. Why "same playbook, different outputs"

> Both paths SHOULD use the same playbook ("summarize-document-for-chat@v1"). They DO call the same orchestrator (`SessionSummarizeOrchestrator`). But they're getting DIFFERENT INPUTS:
> - Chat agent: gets the inline attachment text (correct, from FR-07)
> - /summarize: queries the index, gets whatever's there
>
> Same playbook in the sense both can invoke `SessionSummarizeOrchestrator` server-side. But Path A's agent often skips that tool entirely and writes a chat reply from the inline text. Path B always calls the orchestrator + always gets structured JSON.

### C. The persona problem

> The default chat-assistant persona is built by `PlaybookChatContextProvider.BuildDefaultSystemPrompt(null)` in code, NOT loaded from configuration or Dataverse. It's a hardcoded string assembled by a private method. No one explicitly authored it as a "persona" — it's just whatever a developer wrote in code at some point.
>
> This is a real gap. A production AI legal assistant needs a deliberately crafted base persona (legal-domain tone, knowledge boundaries, refusal patterns, citation behavior), optimized via prompt engineering + evals, versioned, tenant-configurable, likely stored as data.

### D. The playbook FK bypass

> The `summarize-document-for-chat@v1` playbook has ONE node that references the `SUM-CHAT@v1` action. That action carries the prompt. (CORRECTED: that playbook does not have an Action associated. There is Action 'SUM-CHAT@v1' but its not associated to that playbook. The orchestrator works because it loads the action directly by actionCode, bypassing the playbook FK chain.)
>
> The playbook entity is currently a label, not an orchestration unit. There is no multi-node playbook executing today.

### E. The tool architecture intent

> The user's correction: we have a Dataverse entity called sprk_analysistools that identify a handler class and configuration. Playbooks have tools that are defined for use by that playbook. Are these used by the Factory — or does the factory just get them directly from the code?
>
> Answer: the factory pulls tools directly from DI via `IServiceProvider.GetServices<AIFunction>()`. The tools are registered as code (one DI registration per tool class in `AnalysisServicesModule` or similar). `sprk_analysistools` is NOT used by `SprkChatAgentFactory`. There's a disconnect.

### F. The generic playbook selector insight

> The user's question: "I'm not clear on these playbook selector tools — that seems like we would then have to build a huge set of specialized tools to actually invoke a playbook? Wouldn't it be better to have a generic tool that the LLM uses to select playbooks by matching the LLM's read of the context and each playbooks 'profile'?"
>
> This is the right architectural pivot. The agent's tool surface becomes ~5-10 tools regardless of how many AI capabilities the platform supports. Adding a new playbook = data change, no code deploy.

### G. The workspace ↔ assistant bidirectional requirement

> The user's framing: "how do we get the Assistant to interact with the Workspace? The expectation is that the user can use the Assistant to direct the AI to make changes in the Workspace — this is critical. ALSO the user may make changes directly in the workspace (e.g., edit a document) and the Assistant needs to be able to 'see' the current state of the Workspace — also critical."
>
> The user's scoping idea: "if each new workspace 'tab' has the Assistant session id that created it then that is the required context or association. For example, if the user has opened a Workspace dashboard widget that does not need to be visible to the Assistant. BUT we may need to have a way for the user to 'Add to Assistant' in which case the workspace is exposed to the Assistant."

### H. Why the assistant pane vs workspace pane have different roles

> Path A is naturally a dialog — every turn carries context forward. Path B is more like a command — fire and forget; output is an artifact.
>
> That's why the chat-pane + workspace-pane architecture in many products is split this way:
> - Chat is the conversational, action-oriented surface
> - Workspace is the artifact-collection surface
>
> The chat orchestrates actions (including the action of producing a structured summary); the workspace shows the artifacts (the resulting summary, the resulting matter, the resulting email draft, the resulting timeline). They're complementary, not redundant.

### I. The context window clarification

> The user's challenge: "you say that the agent is stateless — which makes sense BUT are there some instances where we need the state or maintain some window of state 'memory' in the session memory; back/forth to redis seems time consuming. Isn't this kind of a 'context window' — does our sprkchat Assistant have a context window?"
>
> Yes. Every chat completion call has a context window — that's how LLMs work. "Stateless agent" means the C# object doesn't hold session state in memory; state is in Redis. Conversation history IS loaded from Redis on every turn and injected into the LLM's context window. The question is HOW we manage that — sliding window only today; needs compression + pinning + selective recall for long conversations.

### J. The renderer schema-awareness gap

> Looking at the user's screenshot: TL;DR shows `data loss.","Organizations miss opportunities to streamline and leverage intellectual asset data."` — note the `,"` separators bleeding through.
>
> The playbook emits `tldr` as a JSON array `["bullet 1", "bullet 2", "bullet 3"]`. The streaming parser emits raw tokens including `[`, `"`, `,`, `"`, `]`. The widget concatenates them literally and renders as a heading.
>
> The renderer needs to be schema-aware: for array-typed fields, accumulate until streaming_complete, then JSON.parse, then render as bullets. For object-typed fields, similar.

### K. Reference products / competitive bar

> Each AI-assisted product has solved (or is actively solving) bidirectional state between conversation and workspace:
> - Cursor (chat ↔ code editor — bidirectional, real-time)
> - GitHub Copilot Workspace (chat → tasks → code → review, all coordinated)
> - Linear AI (chat → issue creation → comments → assignments)
> - Claude with Artifacts (chat → side-panel artifact, editable, regeneration-aware)
>
> Spaarke needs the same. This is category baseline, not future-looking R&D.

### L. The "Insert" button observation

> User: "we need to investigate the 'insert' button — BUT this is really just an example of the bigger interaction point: how do we get the Assistant to interact with the Workspace? The expectation is that the user can use the Assistant to direct the AI to make changes in the Workspace — this is critical."
>
> The model: chat is the conversation, the workspace is filled by explicit user action or by the agent's decision to surface an artifact. "Pull to workspace, with agent-push for clearly artifact-shaped intents."

### M. Why the user wants this in R6 not later

> User: "all the 'Thought' points are important and part of the plan — keep in mind 'long term' is not that long. These are all considered basics for an AI assisted legal application."
>
> Agreed. This is the architecture of an AI-assisted application as a category, not a future-looking R&D project. The competitive bar is set by Cursor / Copilot Workspace / Linear AI / Claude Artifacts. Each has solved bidirectional state between conversation and workspace. Spaarke needs the same.

---

## 9. R6 Project Initialization Checklist

To formally start R6 after R5 close-out:

- [ ] R5 consolidated PR merged (cycle-6 through cycle-9 fixes)
- [ ] R5 `current-task.md` updated to "R5 closed; R6 in design"
- [ ] R5 `TASK-INDEX.md` updated with deferred-to-R6 tags on tasks 022, 030, 031, 035, 037
- [ ] R5 lessons-learned note authored
- [ ] R6 spec.md (formal FRs derived from this design.md)
- [ ] R6 plan.md (WBS with task decomposition per pillar)
- [ ] R6 CLAUDE.md (project-scoped rules — extend root CLAUDE.md)
- [ ] R6 tasks/ folder (POML files per task)
- [ ] R6 design decisions captured in `notes/` (one per pillar where significant tradeoffs need recording)
- [ ] Open questions Q1-Q8 reviewed + decided
- [ ] Architecture review meeting (if applicable)
- [ ] Feature branch + worktree

---

## 10. Closing Note

R6 is **convergence + finishing what was started**. Most of the platform's data-driven architecture (Scope library, JPS pipeline, playbook execution engine, 11 node executors, safety pipeline, public contracts facades, Cosmos persistence, CapabilityRouter, 4-stage lifecycle, pre-fill flow) is production-mature. The chat-agent side has not yet aligned with these patterns. R5's SC-18 walkthrough surfaced the misalignment as 9 diagnostic cycles. R6 closes the gap.

**The work is concentrated in three categories**:

1. **Convergence** — chat-agent tool list driven from `sprk_analysistool` rows the same way playbook tools are; persona becomes a scope; `/summarize` routes through the playbook engine; one generic `invoke_playbook` chat tool replaces specialized bridges. Architectural cleanup. ~10-13 days.

2. **Honest completion** — build the 8 unimplemented typed handlers that the platform's data model anticipates (EntityExtractor, ClauseAnalyzer, RiskDetector, etc.). Real domain code that makes the platform's "data-driven extensibility" claim true. Parallel workstream. ~15-25 days.

3. **Tri-directional shell + memory utilization** — Assistant ↔ Context ↔ Workspace state model with execution-trace widget; cross-conversation memory utilization on existing Cosmos infrastructure; command router. ~30-40 days.

**Total**: ~60-90 engineer-days; ~5-7 calendar weeks with parallelism. The original 6-week estimate stands, but the work is distributed more honestly against what already exists.

**R6's value proposition**: not "ship a feature" but "align the chat-agent with the platform's existing data-driven architecture, build the typed tool handlers the data model anticipates, and close the workspace-assistant loop." After R6, R7+ feature work becomes "design a playbook in data, declare its output schema, reference scopes" — and the conversational user experience remains primary throughout.

---

*Authored 2026-06-06 from architecture chat with project owner; refined 2026-06-07 against verified code state + authoritative architecture docs (`playbook-architecture.md`, `scope-architecture.md`, `chat-architecture.md`, `AI-ARCHITECTURE.md`, `JPS-AUTHORING-GUIDE.md`, `SCOPE-CONFIGURATION-GUIDE.md`, `WORKSPACE-AI-PREFILL-GUIDE.md`, ADR-013/030/031). The 9 refined pillars + 11 open questions + appendix notes are load-bearing — do not consolidate or summarize without preserving them as recoverable detail.*
