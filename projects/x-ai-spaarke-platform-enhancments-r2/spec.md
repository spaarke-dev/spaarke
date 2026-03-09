# SprkChat Interactive Collaboration — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-25
> **Source**: projects/ai-spaarke-platform-enhancents-r2/design.md (Revision 2)

---

## Executive Summary

Transform SprkChat from an embedded, read-only AI assistant into a **platform-wide AI collaborator** deployed as a standalone Dataverse side pane. SprkChat becomes accessible on any form (Matters, Projects, Invoices, Analysis records) with playbook-governed capabilities. The Analysis Workspace migrates from PCF (React 16) to a Code Page (React 19) enabling streaming write sessions, diff compare views, and modern concurrent rendering. Nine work packages are structured for **3-track parallel execution** by agent teams.

---

## Scope

### In Scope

**Package A — SprkChat Side Pane (P0, L)**
- New Code Page web resource `sprk_SprkChatPane` (React 19)
- Side pane deployment via `Xrm.App.sidePanes.createPane()`
- Cross-pane communication via `SprkChatBridge` (BroadcastChannel API)
- Independent auth via `Xrm.Utility.getGlobalContext()` per pane
- Context auto-detection from host form (`entityType`, `entityId`)
- Session persistence across form navigation

**Package B — Streaming Write Engine (P0, L)**
- Token-by-token streaming writes into RichTextEditor via Lexical `StreamingInsertPlugin`
- New SSE events: `document_stream_start`, `document_stream_token`, `document_stream_end`
- New AI tool class: `WorkingDocumentTools` (edit, append section)
- New RichTextEditor ref methods: `beginStreamingInsert()`, `appendStreamToken()`, `endStreamingInsert()`
- Cancel behavior: keep partial content + undo available
- Document version history hook (`useDocumentHistory`, max 20 snapshots)

**Package C — Analysis Workspace Code Page Migration (P0, L)**
- Big-bang migration from PCF to Code Page (no incremental/iframe transition)
- New Code Page web resource `sprk_AnalysisWorkspace` (React 19)
- 2-panel layout: Analysis Output (RichTextEditor) + Source Document (Viewer)
- SprkChat removed from embedded layout — lives in side pane (Package A)
- Wire `SprkChatBridge` for cross-pane document streaming and selection events
- Port auto-save, export, toolbar functionality
- Remove all legacy chat code (`useLegacyChat` flag, legacy SSE hook, `ResumeSessionDialog`)

**Package D — Action Menu / Command Palette (P0, M)**
- New `SprkChatActionMenu` component — `/` trigger, filterable, keyboard navigable
- New endpoint `GET /api/ai/chat/actions` — context-sensitive, playbook-governed
- Playbook capability declarations stored on Dataverse playbook record (new multi-select field)
- `SprkChatAgentFactory.ResolveTools()` filters tools by playbook capabilities

**Package E — Re-Analysis Pipeline (P0, M)**
- New AI tool class: `AnalysisExecutionTools` (rerun, refine)
- Full document reprocessing via `IAnalysisOrchestrationService.ExecutePlaybookAsync()`
- Bulk replacement SSE event: `document_replace`
- Progress SSE event: `progress` with percent indicator
- Previous version pushed to undo stack before replacement

**Package F — Diff Compare View (P1, M)**
- New `DiffCompareView` component — side-by-side or inline diff
- Accept / Reject / Edit workflow
- Automatic mode selection: additions → live stream; revisions → diff review
- User override via `/mode stream` or `/mode diff`

**Package G — Selection-Based Revision (P1, M)**
- Cross-pane selection flow: Editor selection → BroadcastChannel → SprkChat refinement UI
- New RichTextEditor ref methods: `getSelectedHtml()`, `replaceSelection()`, `getSelectionRect()`
- Dual selection sources: editor panel (primary) and chat messages (existing)

**Package H — Suggested Follow-Ups + Citations (P1, M)**
- `SprkChatSuggestions` component — clickable chips after assistant responses
- New SSE event: `suggestions` (2-3 contextual follow-ups)
- `SprkChatCitationPopover` component — clickable `[1]` superscripts with source details
- New SSE event: `citations` with chunk ID, source, page, excerpt
- Modify search tools to return source metadata

**Package I — Web Search + Multi-Document (P2, M)**
- New AI tool class: `WebSearchTools` (Azure Bing Search API)
- Extend `ChatKnowledgeScope` with `AdditionalDocumentIds` (max 5)
- Multi-select document support in `SprkChatContextSelector`

### Out of Scope

- PlaybookBuilder AI Assistant convergence (remains separate; R3+ consideration)
- Real-time collaborative editing (multiple simultaneous users)
- Voice input for chat
- Mobile/responsive layout for the workspace
- Dataverse analysis persistence (separate: R1 Task 032)
- Custom playbook creation from within SprkChat
- Office Add-in integration for SprkChat

### Affected Areas

- `src/client/code-pages/SprkChatPane/` — NEW: Side pane Code Page
- `src/client/code-pages/AnalysisWorkspace/` — NEW: Migrated workspace Code Page
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` — Enhanced with action menu, suggestions, citations
- `src/client/shared/Spaarke.UI.Components/src/components/RichTextEditor/` — Enhanced with streaming insert, selection API
- `src/client/pcf/AnalysisWorkspace/` — DEPRECATED: Legacy PCF removed after Code Page migration
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/` — New tool classes: `WorkingDocumentTools`, `AnalysisExecutionTools`, `WebSearchTools`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — Playbook capability filtering in `ResolveTools()`
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — New SSE events, new `/actions` endpoint
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — Remove deprecated `/continue` and `/resume` endpoints
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/` — Tests for all new tool classes
- Dataverse: New multi-select capability field on Playbook entity

---

## Requirements

### Functional Requirements

1. **FR-01**: SprkChat MUST be deployable as a persistent side pane on any Dataverse form via `Xrm.App.sidePanes.createPane()` — Acceptance: Verified on Matter, Project, and Analysis forms
2. **FR-02**: SprkChat side pane MUST auto-detect host form context (`entityType`, `entityId`) and select an appropriate default playbook — Acceptance: Opening side pane on a Matter form loads legal playbooks
3. **FR-03**: SprkChat sessions MUST persist across form navigation while the side pane remains open — Acceptance: Navigate between records, chat history preserved
4. **FR-04**: Each Code Page (SprkChat pane, Analysis Workspace) MUST authenticate independently via `Xrm.Utility.getGlobalContext()` — Acceptance: No shared auth tokens via BroadcastChannel
5. **FR-05**: AI tool responses MUST stream token-by-token into the RichTextEditor via `StreamingInsertPlugin` — Acceptance: Visible character-by-character insertion with <100ms per-token latency
6. **FR-06**: Cancelling a streaming write MUST keep partial content in the editor with undo available to revert — Acceptance: Cancel mid-stream → partial content visible → undo restores pre-stream state
7. **FR-07**: Every AI-initiated document modification MUST snapshot the document state before writing, enabling undo/redo (max 20 versions) — Acceptance: Undo after any AI edit restores exact previous state
8. **FR-08**: Analysis Workspace MUST be rebuilt as a Code Page (React 19) with a 2-panel layout (editor + document viewer) — Acceptance: Full viewport rendering, no PCF dependency
9. **FR-09**: Cross-pane communication between SprkChat and Analysis Workspace MUST use `BroadcastChannel` API with `window.postMessage` fallback — Acceptance: Document stream events flow between panes in <10ms
10. **FR-10**: The `/` command palette MUST trigger on first-character `/` in chat input, be filterable by typing, and fully keyboard navigable — Acceptance: `/sum` filters to "Summarize", arrow keys + Enter selects
11. **FR-11**: Available actions in the command palette MUST be governed by the current playbook's capability declarations — Acceptance: Switching to a "Quick Q&A" playbook hides write-back and re-analysis actions
12. **FR-12**: Playbook capability declarations MUST be stored as a multi-select field on the Dataverse Playbook entity — Acceptance: Admin can configure capabilities per playbook in the Dataverse form
13. **FR-13**: Re-analysis MUST trigger full document reprocessing via `IAnalysisOrchestrationService.ExecutePlaybookAsync()` with user-supplied additional instructions — Acceptance: "Rerun focusing on financial risks" produces a complete new analysis
14. **FR-14**: Re-analysis output MUST replace the editor content as a bulk operation with progress indicator — Acceptance: Progress bar shows during re-analysis; editor shows new output on completion
15. **FR-15**: Diff compare view MUST show side-by-side or inline diff of AI-proposed changes with Accept/Reject/Edit actions — Acceptance: User reviews proposed revision, clicks Accept → applied; Reject → discarded
16. **FR-16**: Write mode MUST be automatically selected: additions/expansions → live streaming; revisions/replacements → diff review — Acceptance: "Add a section" streams live; "Rewrite the conclusion" shows diff
17. **FR-17**: Users MUST be able to select text in the editor and trigger AI revision via the SprkChat side pane — Acceptance: Select text in editor → refinement UI appears in side pane → revised text replaces selection
18. **FR-18**: After each assistant response, SprkChat MUST display 2-3 contextual follow-up suggestion chips — Acceptance: Suggestions are relevant to current document/analysis, clicking sends as next message
19. **FR-19**: When AI references knowledge base content, chat messages MUST include clickable citation markers linking to source chunks — Acceptance: Click `[1]` → popover shows source excerpt, document name, page
20. **FR-20**: All legacy chat code MUST be removed: `useLegacyChat` flag, legacy SSE hook, `ResumeSessionDialog`, deprecated `/continue` and `/resume` endpoints — Acceptance: No legacy chat artifacts remain in codebase

### Non-Functional Requirements

- **NFR-01**: Streaming write latency MUST be <100ms per token from SSE event to editor insertion
- **NFR-02**: Side pane MUST load and be interactive within 2 seconds of `createPane()` call
- **NFR-03**: Cross-pane BroadcastChannel messages MUST deliver within 10ms (same-origin synchronous)
- **NFR-04**: All UI MUST support light, dark, and high-contrast modes (ADR-021)
- **NFR-05**: All UI MUST meet WCAG 2.1 AA accessibility standards (ADR-021)
- **NFR-06**: Code Page bundles MUST use code splitting to keep initial load <500KB gzipped
- **NFR-07**: 0 additional DI registrations — all new tool classes factory-instantiated (ADR-010)
- **NFR-08**: Re-analysis MUST respect CostControl middleware token budget (AIPL-057)

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | All new API endpoints MUST use Minimal API pattern; no Azure Functions |
| **ADR-006** | SprkChat side pane and Analysis Workspace MUST be Code Pages (not PCF); placed in `src/client/code-pages/` |
| **ADR-008** | All AI chat endpoints MUST use endpoint filters for authorization; no global middleware |
| **ADR-010** | DI registrations MUST remain ≤15; new tools are factory-instantiated in `SprkChatAgentFactory.ResolveTools()` |
| **ADR-012** | Shared components (`SprkChatBridge`, `DiffCompareView`, `StreamingInsertPlugin`) MUST live in `@spaarke/ui-components`; React 18-compatible |
| **ADR-013** | AI tools MUST follow `AIFunctionFactory.Create` pattern; `ChatHostContext` MUST flow through pipeline; rate limiting required |
| **ADR-021** | All UI MUST use Fluent UI v9 exclusively; `makeStyles`/design tokens only; Code Pages use React 19 `createRoot()` |
| **ADR-022** | Code Pages bundle React 19 (not platform-provided); PCF constraints do NOT apply to Code Pages |

### MUST Rules

- MUST use `Xrm.App.sidePanes.createPane()` for side pane deployment (Dataverse SDK)
- MUST use `BroadcastChannel` for cross-pane communication with `window.postMessage` fallback
- MUST use Lexical editor API for streaming inserts (not raw DOM manipulation)
- MUST use `@fluentui/react-components` v9 exclusively — no v8, no hard-coded colors
- MUST wrap all Code Page roots in `<FluentProvider theme={...}>`
- MUST return `ProblemDetails` for all API error responses (ADR-001)
- MUST apply rate limiting to all AI endpoints (`ai-stream`, `ai-batch` groups)
- MUST use endpoint filters (`AiAuthorizationFilter`) on all chat endpoints (ADR-008)
- MUST register concretes by default in DI, not interfaces (ADR-010)
- MUST access files through `SpeFileStore` facade only (ADR-007 via ADR-013)

### MUST NOT Rules

- MUST NOT create legacy JavaScript web resources (ADR-006)
- MUST NOT use React 18+ APIs in any remaining PCF controls (ADR-022)
- MUST NOT create global middleware for AI authorization (ADR-008)
- MUST NOT create a separate AI microservice (ADR-013)
- MUST NOT expose API keys to client-side code (ADR-013)
- MUST NOT hard-code Dataverse entity names/schemas in shared components (ADR-012)
- MUST NOT mix Fluent UI versions (ADR-021)
- MUST NOT use custom CSS — Fluent design tokens only (ADR-012, ADR-021)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` for SSE streaming endpoint pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/TextRefinementTools.cs` for AI tool class pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` for tool registration pattern
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` for SprkChat component architecture
- See `src/client/shared/Spaarke.UI.Components/src/components/RichTextEditor/` for Lexical editor ref API
- See `src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/CommandPalette.tsx` for action menu reference
- See `src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/SuggestionBar.tsx` for suggestions reference
- See `.claude/patterns/` for detailed implementation patterns

---

## Parallel Execution Strategy

### Package Dependency Graph

```
A (Side Pane) ─────────────────┐
                                ├── C (AW Migration) depends on A
B (Streaming Engine) ───────────┤
                                ├── E (Re-Analysis) depends on B
                                ├── F (Diff View) depends on B
                                └── G (Selection Revision) depends on B + C

D (Action Menu) ── independent (no dependencies)
H (Suggestions + Citations) ── depends on A
I (Web Search + Multi-Doc) ── depends on A
```

### Sprint Plan (3 Parallel Tracks)

**Sprint 1 — Foundation**:

| Track | Package | Focus | File Ownership |
|-------|---------|-------|----------------|
| 1 | A: SprkChat Side Pane | Frontend | `src/client/code-pages/SprkChatPane/`, `SprkChatBridge.ts` |
| 2 | B: Streaming Write Engine | Full-stack | `RichTextEditor/plugins/`, `WorkingDocumentTools.cs`, SSE events |
| 3 | D: Action Menu | Frontend + API | `SprkChatActionMenu.tsx`, `/actions` endpoint, playbook capabilities |

**Sprint 2 — Integration**:

| Track | Package | Focus | File Ownership |
|-------|---------|-------|----------------|
| 1 | C: AW Code Page Migration | Frontend | `src/client/code-pages/AnalysisWorkspace/`, legacy cleanup |
| 2 | E: Re-Analysis Pipeline | Backend | `AnalysisExecutionTools.cs`, re-analysis orchestration |
| 3 | I: Web Search + Multi-Doc | Backend | `WebSearchTools.cs`, `ChatKnowledgeScope` extension |

**Sprint 3 — Polish**:

| Track | Package | Focus | File Ownership |
|-------|---------|-------|----------------|
| 1 | F: Diff Compare View | Frontend | `DiffCompareView.tsx`, diff algorithm |
| 2 | G: Selection-Based Revision | Full-stack | Selection API, cross-pane selection flow |
| 3 | H: Suggestions + Citations | Full-stack | `SprkChatSuggestions.tsx`, `SprkChatCitationPopover.tsx` |

### Task Decomposition Guidance

Each package decomposes into tasks with **clean file ownership boundaries** for agent team parallelism:

| Package | Est. Tasks | Key Boundary |
|---------|-----------|--------------|
| A | 12-15 | `code-pages/SprkChatPane/`, `SprkChatBridge.ts` |
| B | 15-20 | `RichTextEditor/plugins/`, `WorkingDocumentTools.cs` |
| C | 15-18 | `code-pages/AnalysisWorkspace/`, PCF legacy removal |
| D | 10-12 | `SprkChatActionMenu.tsx`, `/actions` endpoint |
| E | 8-10 | `AnalysisExecutionTools.cs`, re-analysis flow |
| F | 8-10 | `DiffCompareView.tsx`, mode toggle |
| G | 10-12 | Selection API, cross-pane flow |
| H | 10-12 | Suggestions + citations components |
| I | 8-10 | `WebSearchTools.cs`, knowledge scope extension |
| **Total** | **96-119** | |

---

## Success Criteria

1. [ ] SprkChat accessible as side pane on Matter, Project, and Analysis forms — Verify: manual test on each form type
2. [ ] AI streams edits into editor token-by-token with <100ms latency — Verify: performance timing in browser DevTools
3. [ ] Diff view shows before/after with Accept/Reject/Edit — Verify: revision workflow end-to-end test
4. [ ] Re-analysis reprocesses full document with progress indicator — Verify: "Rerun analysis" produces complete new output
5. [ ] Action menu responds to `/` in <200ms with keyboard navigation — Verify: UI interaction test
6. [ ] Analysis Workspace runs as Code Page (React 19, no PCF dependency) — Verify: no PCF artifacts in deployed solution
7. [ ] Playbook capabilities govern available tools and actions — Verify: switching playbook changes action menu and tool set
8. [ ] All UI supports light, dark, and high-contrast modes — Verify: visual inspection in all three themes
9. [ ] Packages A, B, D executable in parallel with no file conflicts — Verify: agent team dry run on task file ownership
10. [ ] 0 additional DI registrations (ADR-010) — Verify: count registrations in `AiModule.cs`
11. [ ] All legacy chat code removed — Verify: no `useLegacyChat`, no deprecated endpoints in codebase

---

## Dependencies

### Prerequisites

- R1 completion: All R1 bug fixes deployed (UseFunctionInvocation, tenantId, document context) — **DONE**
- Azure Bing Search API provisioned in resource group (for Package I)
- Dataverse Playbook entity schema update: add multi-select capability field (for Package D)

### External Dependencies

- Lexical editor library: streaming insert API compatibility (verify with latest Lexical version)
- `Xrm.App.sidePanes` API: available in current Dataverse environment (verify browser support)
- BroadcastChannel API: supported in all target browsers (IE11 excluded; Edge/Chrome/Firefox supported)

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Cross-pane auth | Share auth tokens via BroadcastChannel or independent auth per pane? | **Independent auth** — each Code Page calls `Xrm.Utility.getGlobalContext()` | BroadcastChannel carries only document/selection events, not auth tokens |
| AW migration | Big-bang replacement or incremental iframe transition? | **Big-bang replacement** — remove PCF entirely, deploy Code Page | No interim iframe wrapper needed; full testing required before switch |
| Cancel behavior | What happens to partial content when streaming write is cancelled? | **Keep partial + undo available** | Simpler implementation; `useDocumentHistory` provides revert |
| Capability storage | Where do playbook capability declarations live? | **Dataverse playbook record** — new multi-select field | Requires Dataverse schema update; admin-configurable per playbook |

---

## Assumptions

- **Side pane width**: Assuming user-resizable (300-600px range) — affects `SprkChatPane` responsive layout
- **Diff algorithm**: Assuming use of existing library (jsdiff or diff-match-patch) for text diffing — not building custom
- **Streaming position**: Assuming "append to end" as default insert position for streaming writes — positional inserts (after specific section) are stretch goal
- **PlaybookBuilder convergence**: Assuming NOT in scope — PlaybookBuilder AI Assistant remains separate with its own endpoints and UI
- **Dataverse form configuration**: Assuming the Analysis form can be updated to use `navigateTo` for the Code Page — no custom page required

---

## Unresolved Questions

- [ ] **Side pane resizing**: Fixed 400px or user-resizable? — Blocks: SprkChatPane responsive design decisions
- [ ] **Diff algorithm for HTML**: jsdiff works on text; HTML-aware diffing may need a specialized library — Blocks: Package F implementation choice
- [ ] **Streaming insert positioning**: Can Lexical reliably insert at arbitrary positions (e.g., "after heading X")? — Blocks: Package B advanced positioning features
- [ ] **PlaybookBuilder future adoption**: Should Package D's action menu be designed for PlaybookBuilder reuse in R3? — Blocks: action menu extensibility architecture

---

*AI-optimized specification. Original design: projects/ai-spaarke-platform-enhancents-r2/design.md (Revision 2)*
