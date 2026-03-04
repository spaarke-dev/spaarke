# SprkChat LLM Agent Activation

> **Workstream Type**: Integration, Platform Module Development, & Deployment
> **Branch**: `work/ai-playbook-node-builder-r5` (current branch in this worktree)
> **Created**: 2026-03-04
> **Updated**: 2026-03-05
> **Goal**: Get SprkChat working as an always-available conversational LLM agent via the Side Pane Platform
> **Architecture Doc**: [`docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md`](../../docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md)

---

## Context

R2 built SprkChat (89 tasks, all complete). The code is 95% built. What's needed:
1. **Side Pane Platform** — a reusable core module for always-available side panes (like Microsoft Copilot)
2. **SprkChat as first pane consumer** — conversational LLM agent with tools and context awareness
3. **Dataverse configuration** — playbook records, ribbon solution, web resources

**This includes both platform module development (SidePaneManager) and deployment/configuration.**

---

## What SprkChat Does as LLM Agent

SprkChat as LLM agent = **conversational assistant with tool calling**:
- Gets system prompt from playbook Action record
- Has tools: DocumentSearch (RAG), KnowledgeRetrieval, TextRefinement, AnalysisQuery
- Knows the current document/entity via HostContext
- Streams responses with citations
- Cross-pane communication with Analysis Workspace via SprkChatBridge

**SprkChat does NOT execute playbook nodes in this role.** The playbook is a configuration container (system prompt + scopes + tool capabilities). The LLM decides tool calls conversationally.

---

## Architecture Summary

```
UCI Model-Driven App
    ↓ Code Page loads (e.g., workspace landing page)
    ↓ Injection snippet in <head> injects SidePaneManager into parent Dataverse shell
SidePaneManager.initialize() — auto-called on script load
    ↓ Reads PaneRegistry, creates panes via Xrm.App.sidePanes.createPane()
    ↓
SprkChat icon appears in side pane launcher (right bar)
    ↓ User clicks icon → pane expands
    ↓
SprkChatPane Code Page (side pane, 400px)
    ↓ Context: polls Xrm.Page.data.entity every 2s OR receives BroadcastChannel events
    ↓ Auth: independent token via Xrm.Utility.getGlobalContext()
    ↓ GET /api/ai/chat/playbooks → playbook selector
    ↓ POST /api/ai/chat/sessions → create session with PlaybookId + HostContext
    ↓ POST /api/ai/chat/sessions/{id}/messages → SSE streaming
    ↓
BFF API: SprkChatAgentFactory.CreateAgentAsync()
    ↓ PlaybookChatContextProvider resolves: system prompt, knowledge, skills
    ↓ Tools: DocumentSearchTools, KnowledgeRetrievalTools, TextRefinementTools
    ↓ IChatClient.GetStreamingResponseAsync() → SSE tokens
    ↓
SprkChatBridge (BroadcastChannel: sprk-workspace-{context})
    ↓ Events: document_stream_*, selection_changed, context_changed
    ↓
Analysis Workspace Code Page (main content area)
    ↓ Hooks: useDocumentStreaming, useSelectionBroadcast, useDiffReview
```

Full architecture: [`docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md`](../../docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md)

---

## Completed Work

### Phase A: Deploy Components (DONE)

- [x] **A1**: BFF API deployed — chat endpoints return 401 (route registered, auth required)
- [x] **A2**: SprkChatPane rebuilt (891 KB) and uploaded to Dataverse as `sprk_SprkChatPane`
- [x] **A3**: Launcher script compiled (module=None) and uploaded as `sprk_openSprkChatPane`
- [x] **A3b**: Analysis Workspace rebuilt (1002 KB) and uploaded as `sprk_analysisworkspace`
- [x] **A4**: Chat icon SVG uploaded as `sprk_SprkChatIcon16.svg`

### Phase B: Configure Playbook (DONE)

- [x] **B1**: Created Action "SprkChat Document Assistant" (`3c03edf0-8a17-f111-8343-7ced8d1dc988`)
  - Conversational system prompt for document Q&A, search, collaboration
- [x] **B2**: Created Playbook "SprkChat Document Assistant" (`5ece14f7-8a17-f111-8343-7ced8d1dc988`)
  - Set as public (visible to all users)
- [x] **B3**: Associated Action with Playbook via N:N relationship

### Phase C1: Ribbon Attempt (DONE — approach changed)

- [x] **C1**: Application Ribbon solution v1.2 imported with Chat button
  - **Issue discovered**: `Mscrm.GlobalTab` buttons don't render visually in UCI
  - **Resolution**: Use the hidden button + enable rule pattern instead (the enable rule JS fires on every page navigation even though the button is never rendered)
  - See architecture doc for full explanation

### Documentation (DONE)

- [x] Architecture document: `docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md`
- [x] Updated `docs/architecture/INDEX.md`

---

## Completed: Platform Module + Deployment

### Task 1: Create SidePaneManager Core Module (DONE)

- [x] Created `src/client/side-pane-manager/SidePaneManager.ts` with pane registry, frame-walking API access, context detection, and auto-initialization
- [x] Created `src/client/side-pane-manager/types.ts` with PaneConfig interface and Xrm API type declarations
- [x] Created `src/client/side-pane-manager/tsconfig.json` (ES2020, module None, outFile concatenation)
- [x] Compiled to `out/SidePaneManager.js` (9,629 bytes)
- [x] Deployed as `sprk_SidePaneManager` web resource

### Task 2: Application Ribbon + Code Page Injection (DONE)

- [x] Updated ApplicationRibbon customizations.xml with SidePaneManager hidden trigger (v1.3)
- [x] **KEY FINDING**: `Mscrm.GlobalTab` enable rules do NOT reliably fire in current UCI (2026)
- [x] **ACTUAL MECHANISM**: Code Page injection — workspace HTML injects SidePaneManager into parent Dataverse shell via `window.parent.document.createElement('script')`
- [x] Updated workspace Code Page source (`LegalWorkspace/index.html`) with injection snippet
- [x] Rebuilt workspace HTML via Vite (1,287 KB)
- [x] SidePaneManager auto-calls `initialize()` on script load

### Task 3: Deploy and Test Side Pane Registration (DONE)

- [x] `sprk_SidePaneManager` deployed as JS web resource
- [x] ApplicationRibbon v1.3 imported (ribbon is present but Code Page injection is the actual trigger)
- [x] Workspace HTML rebuilt and deployed with injection snippet
- [x] SprkChat icon appears in right side pane launcher bar
- [x] Icon visible after opening workspace landing page

---

## Remaining Tasks

### Task 4: End-to-End SprkChat Testing

**Type**: Integration testing
**Steps**:
- [ ] **4a**: Open any form → SprkChat icon in launcher → click → pane opens
- [ ] **4b**: Playbook selector shows "SprkChat Document Assistant"
- [ ] **4c**: Select playbook → session creates (POST /api/ai/chat/sessions returns 200)
- [ ] **4d**: Send message → streaming response appears
- [ ] **4e**: Navigate to different record → context change detected (console log)
- [ ] **4f**: Open Analysis Workspace → SprkChatBridge communication works
- [ ] **4g**: Dark mode detection works
- [ ] **4h**: Session persistence (collapse/expand pane → session preserved)

---

### Task 5: Move contextService.ts to Shared Library (Optional)

**Type**: Refactoring
**Purpose**: Make context detection reusable for any future side pane, not just SprkChat

**Changes**:
- Move `src/client/code-pages/SprkChatPane/src/services/contextService.ts`
  → `src/client/shared/Spaarke.UI.Components/src/services/contextService.ts`
- Update SprkChatPane imports
- Export from shared library barrel

**Can be deferred** until a second pane consumer needs context detection.

---

## Chat API Endpoints (Reference)

| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/ai/chat/sessions` | Create session (accepts HostContext) |
| POST | `/api/ai/chat/sessions/{id}/messages` | Send message → SSE stream |
| POST | `/api/ai/chat/sessions/{id}/refine` | Refine selected text |
| GET | `/api/ai/chat/sessions/{id}/history` | Get chat history |
| PATCH | `/api/ai/chat/sessions/{id}/context` | Switch playbook/document context |
| DELETE | `/api/ai/chat/sessions/{id}` | Delete session |
| GET | `/api/ai/chat/playbooks` | List available playbooks |

## SprkChat Tools (Reference)

| Tool | Gate | Purpose |
|------|------|---------|
| DocumentSearchTools | Always (if IRagService) | Vector search across indexed documents |
| KnowledgeRetrievalTools | Always (if IRagService) | Retrieve knowledge base content |
| TextRefinementTools | Always | Reformat, summarize, extract key points |
| AnalysisQueryTools | Always (if IAnalysisOrchestrationService) | Get analysis results by ID |
| AnalysisExecutionTools | `reanalyze` capability | Re-run analysis pipeline |
| WebSearchTools | `web_search` capability | Public web search (mock until Bing API) |

## Key Files Reference

### Platform Module (NEW)
- `src/client/side-pane-manager/SidePaneManager.ts` — Core loader with pane registry
- `src/client/side-pane-manager/types.ts` — PaneConfig interfaces

### Frontend (Existing)
- `src/client/code-pages/SprkChatPane/src/App.tsx` — SprkChat React app
- `src/client/code-pages/SprkChatPane/src/services/contextService.ts` — Context detection + polling
- `src/client/code-pages/AnalysisWorkspace/src/hooks/useDocumentStreaming.ts` — Bridge listener
- `src/client/shared/Spaarke.UI.Components/src/services/SprkChatBridge.ts` — Cross-pane typed events

### Backend (Existing)
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — All chat endpoints
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — Agent construction
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` — Prompt resolution
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/` — All tool classes

### Dataverse Configuration (Existing)
- Action: "SprkChat Document Assistant" (`3c03edf0-8a17-f111-8343-7ced8d1dc988`)
- Playbook: "SprkChat Document Assistant" (`5ece14f7-8a17-f111-8343-7ced8d1dc988`)
- Web resources: `sprk_SprkChatPane`, `sprk_openSprkChatPane`, `sprk_SprkChatIcon16.svg`, `sprk_analysisworkspace`
- ApplicationRibbon solution v1.3 (SidePaneManager trigger — ribbon present but Code Page injection is actual mechanism)

## Dataverse Web Resources (Deployed)

| Name | Type | Size | Status |
|------|------|------|--------|
| `sprk_SprkChatPane` | Webpage (HTML) | 891 KB | Deployed |
| `sprk_openSprkChatPane` | Script (JS) | 15 KB | Deployed (to be replaced by SidePaneManager) |
| `sprk_SprkChatIcon16.svg` | SVG | <1 KB | Deployed |
| `sprk_analysisworkspace` | Webpage (HTML) | 1002 KB | Deployed |
| `sprk_SidePaneManager` | Script (JS) | 9.6 KB | Deployed (auto-initializes) |

---

## Out of Scope (Separate Projects)

- Standalone actions ("Analyze Contract", "Write Email") — separate use case project
- SprkChat as agentic trigger (executing multi-node playbooks from chat)
- JPS (JSON Prompt Schema) — separate project in `spaarke-wt-ai-json-prompt-schema`
- RAG integration in playbook nodes — fold into JPS Phase 3
- Additional side panes (Actions, Notifications) — future extensions after SprkChat works

---

## Quick Recovery

**If this is your first time reading this file after compaction**, here's what to do:

1. Read this WORKSTREAM.md for full context
2. Read the architecture doc: `docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md`
3. Check the "Remaining Tasks" section above — start with Task 4 (end-to-end testing)
4. Key decisions already made:
   - **Code Page injection** loads SidePaneManager (NOT ribbon enable rules — those don't work in UCI 2026)
   - SidePaneManager as reusable core module (not SprkChat-specific)
   - Context detection via Xrm polling + BroadcastChannel (dual mode)
   - Each pane authenticates independently (no tokens cross the bridge)
   - `canClose: false` + `alwaysRender: true` for SprkChat (Copilot-like persistence)
5. The `openSprkChatPane.ts` launcher has been **replaced** by `SidePaneManager.ts`
6. **Known issue**: `SprkChatBridge is not a constructor` error in SprkChatPane — needs investigation (Task 4)
