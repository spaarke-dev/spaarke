# Project Plan: SprkChat Interactive Collaboration (R2)

> **Last Updated**: 2026-02-25
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Transform SprkChat from an embedded read-only assistant into a platform-wide AI collaborator with streaming write sessions, diff compare views, re-analysis capabilities, and a command palette — all deployed as standalone Code Pages with playbook-governed capabilities.

**Scope**:
- SprkChat standalone side pane Code Page (any Dataverse form)
- Streaming write engine (token-by-token editor inserts)
- Analysis Workspace Code Page migration (React 19)
- Action menu / command palette (playbook-governed)
- Re-analysis pipeline (full document reprocessing)
- Diff compare view (Accept/Reject/Edit)
- Selection-based revision (cross-pane)
- Suggested follow-ups + citations
- Web search + multi-document context
- Legacy chat code removal

**Estimated Effort**: 96-119 POML tasks across 9 work packages

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: All new API endpoints MUST use Minimal API; no Azure Functions
- **ADR-006**: SprkChat side pane and Analysis Workspace MUST be Code Pages; placed in `src/client/code-pages/`
- **ADR-007**: Document access MUST go through `SpeFileStore` facade
- **ADR-008**: All AI endpoints MUST use endpoint filters for authorization; no global middleware
- **ADR-010**: DI registrations MUST remain ≤15; new tools factory-instantiated (0 new registrations)
- **ADR-012**: Shared components (`SprkChatBridge`, `DiffCompareView`, `StreamingInsertPlugin`) MUST live in `@spaarke/ui-components`
- **ADR-013**: AI tools MUST follow `AIFunctionFactory.Create` pattern; `ChatHostContext` MUST flow through pipeline
- **ADR-014**: Caching MUST use `IDistributedCache` (Redis); MUST NOT cache streaming tokens
- **ADR-015**: MUST NOT log document contents or full prompts; tenant-scoped artifacts
- **ADR-016**: Rate limiting MUST be applied to all AI endpoints; bounded concurrency
- **ADR-019**: MUST return ProblemDetails for all HTTP failures; terminal SSE error events
- **ADR-021**: All UI MUST use Fluent UI v9 exclusively; `makeStyles`/design tokens; dark mode required
- **ADR-022**: Code Pages bundle React 19 (`createRoot()`); PCF constraints do NOT apply to Code Pages

**From Spec**:
- MUST use `Xrm.App.sidePanes.createPane()` for side pane deployment
- MUST use `BroadcastChannel` API with `window.postMessage` fallback
- MUST use Lexical editor API for streaming inserts (not raw DOM)
- Cancel streaming writes MUST keep partial content + undo available

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Code Page (not PCF) for SprkChat pane | Platform-wide deployment, React 19, independent lifecycle | New `src/client/code-pages/SprkChatPane/` |
| Big-bang AW migration (not incremental) | Simpler than iframe transition, eliminates React 16 constraint | Full PCF replacement |
| BroadcastChannel for cross-pane comm | Synchronous same-origin, <10ms latency | New `SprkChatBridge` shared module |
| Factory-instantiated tool classes | 0 DI registrations; tools scoped to agent session | No changes to `Program.cs` |
| Playbook capability field on Dataverse entity | Admin-configurable; governs tools + actions | Schema update required |
| Streaming inserts via Lexical plugin | Editor-native; preserves undo/redo, selection state | New `StreamingInsertPlugin` |

### Discovered Resources

**Applicable ADRs** (full context):
- `.claude/adr/ADR-001-minimal-api.md` — Minimal API + BackgroundService
- `.claude/adr/ADR-006-pcf-over-webresources.md` — PCF vs Code Page decision matrix
- `.claude/adr/ADR-007-spefilestore.md` — SpeFileStore facade
- `.claude/adr/ADR-008-endpoint-filters.md` — Endpoint filters for auth
- `.claude/adr/ADR-010-di-minimalism.md` — DI ≤15 registrations
- `.claude/adr/ADR-012-shared-components.md` — Shared component library
- `.claude/adr/ADR-013-ai-architecture.md` — AI Tool Framework
- `.claude/adr/ADR-014-ai-caching.md` — Caching and reuse policy
- `.claude/adr/ADR-015-ai-data-governance.md` — Data governance
- `.claude/adr/ADR-016-ai-rate-limits.md` — Rate limits and backpressure
- `.claude/adr/ADR-019-problemdetails.md` — ProblemDetails error handling
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent UI v9 design system
- `.claude/adr/ADR-022-pcf-platform-libraries.md` — PCF platform libraries

**Applicable Skills**:
- `.claude/skills/code-page-deploy/` — Build and deploy Code Page web resources
- `.claude/skills/dataverse-deploy/` — Deploy solutions and web resources
- `.claude/skills/pcf-deploy/` — PCF build and deployment (for deprecation)
- `.claude/skills/bff-deploy/` — BFF API deployment
- `.claude/skills/code-review/` — Code quality review
- `.claude/skills/adr-check/` — ADR compliance validation
- `.claude/skills/context-handoff/` — State preservation

**Knowledge Articles**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — Full AI architecture guide
- `.claude/patterns/ai/streaming-endpoints.md` — SSE streaming patterns
- `.claude/patterns/ai/analysis-scopes.md` — Prompt construction scopes
- `.claude/patterns/pcf/theme-management.md` — Fluent v9 theme resolution
- `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` — Dialog patterns
- `.claude/constraints/api.md` — API constraints
- `.claude/constraints/ai.md` — AI constraints
- `.claude/constraints/pcf.md` — PCF/Code Page constraints
- `.claude/constraints/testing.md` — Testing constraints

**Reusable Code** (canonical implementations):
- `src/client/shared/.../SprkChat/` — SprkChat component family (6 components + 3 hooks)
- `src/client/shared/.../RichTextEditor/` — Lexical-based WYSIWYG editor
- `src/server/api/.../Services/Ai/Chat/Tools/TextRefinementTools.cs` — AI tool class pattern
- `src/server/api/.../Services/Ai/Chat/SprkChatAgentFactory.cs` — Tool registration pattern
- `src/server/api/.../Api/Ai/ChatEndpoints.cs` — SSE streaming endpoint pattern
- `src/client/code-pages/SemanticSearch/` — Code Page entry point reference
- `src/client/pcf/PlaybookBuilderHost/.../CommandPalette.tsx` — Action menu reference
- `src/client/pcf/PlaybookBuilderHost/.../SuggestionBar.tsx` — Suggestions reference

**Deployment Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` — PCF web resource deployment
- `scripts/Test-SdapBffApi.ps1` — API health validation

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation (Sprint 1 — 3 parallel tracks)
├─ Package A: SprkChat Side Pane Code Page
├─ Package B: Streaming Write Engine
├─ Package D: Action Menu / Command Palette
└─ Legacy Cleanup (prerequisite)

Phase 2: Integration (Sprint 2 — 3 parallel tracks)
├─ Package C: Analysis Workspace Code Page Migration
├─ Package E: Re-Analysis Pipeline
└─ Package I: Web Search + Multi-Document

Phase 3: Polish (Sprint 3 — 3 parallel tracks)
├─ Package F: Diff Compare View
├─ Package G: Selection-Based Revision
└─ Package H: Suggested Follow-Ups + Citations

Phase 4: Deployment & Validation
├─ Integration testing
├─ Dark mode / accessibility validation
├─ Performance benchmarking
└─ Dataverse deployment
```

### Parallel Execution Model

**Agent Team Structure**: Each sprint runs 3 independent tracks. Tasks within each track have clean file ownership boundaries so agents don't conflict.

**Placeholder Documentation Protocol**: Any task that produces placeholder/stub code MUST include a `<placeholders>` manifest listing:
- What is stubbed (function name, component, endpoint)
- What real implementation it needs (data source, API call, algorithm)
- Which downstream task completes the placeholder
- Build impact (will it compile? will tests pass with stubs?)

This ensures no placeholder survives to production unresolved.

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (Packages C, E depend on A, B)
- Phase 3 BLOCKED BY Phase 2 (Package G depends on B + C)
- Package H, I depend only on Package A (can start mid-Phase 2)

**High-Risk Items:**
- Lexical streaming insert stability — Mitigation: isolated plugin, extensive testing
- Code Page migration breaks forms — Mitigation: feature flag, PCF fallback
- Re-analysis token budget — Mitigation: CostControl middleware, user confirmation

---

## 4. Phase Breakdown

### Phase 1: Foundation (Sprint 1)

**Objectives:**
1. Deploy SprkChat as standalone side pane accessible from any Dataverse form
2. Build streaming write engine for token-by-token editor inserts
3. Implement `/` command palette with playbook-governed actions
4. Remove all legacy chat code

**Deliverables:**
- [ ] `sprk_SprkChatPane` Code Page web resource
- [ ] `SprkChatBridge` cross-pane communication module
- [ ] Side pane launcher (ribbon/form script)
- [ ] `StreamingInsertPlugin` Lexical plugin
- [ ] `WorkingDocumentTools` AI tool class
- [ ] New SSE events: `document_stream_start/token/end`
- [ ] `useDocumentHistory` hook (undo support, max 20 snapshots)
- [ ] `SprkChatActionMenu` component
- [ ] `GET /api/ai/chat/actions` endpoint
- [ ] Playbook capability field on Dataverse entity
- [ ] Legacy chat code removed (`useLegacyChat`, deprecated endpoints)

**Parallel Track Assignments (Agent Teams):**

| Track | Package | File Ownership | Tags |
|-------|---------|----------------|------|
| Track 1 | A: Side Pane | `src/client/code-pages/SprkChatPane/`, `SprkChatBridge.ts` | `code-page`, `frontend` |
| Track 2 | B: Streaming Engine | `RichTextEditor/plugins/`, `WorkingDocumentTools.cs`, SSE types | `bff-api`, `frontend`, `lexical` |
| Track 3 | D: Action Menu | `SprkChatActionMenu.tsx`, `/actions` endpoint, playbook capabilities | `frontend`, `bff-api` |

**Inputs**: spec.md, design.md, existing SprkChat components, ChatEndpoints.cs, RichTextEditor

**Outputs**: Foundation layer for all subsequent packages

---

### Phase 2: Integration (Sprint 2)

**Objectives:**
1. Migrate Analysis Workspace from PCF to Code Page (React 19)
2. Enable full document re-analysis through SprkChat
3. Add web search and multi-document context

**Deliverables:**
- [ ] `sprk_AnalysisWorkspace` Code Page web resource (2-panel layout)
- [ ] Legacy PCF AnalysisWorkspace deprecated/removed
- [ ] `AnalysisExecutionTools` AI tool class
- [ ] `document_replace` and `progress` SSE events
- [ ] `WebSearchTools` AI tool class
- [ ] `ChatKnowledgeScope.AdditionalDocumentIds` extension
- [ ] Dataverse form updated to open Code Page via `navigateTo`
- [ ] SprkChatBridge integration (editor ↔ side pane)

**Parallel Track Assignments (Agent Teams):**

| Track | Package | File Ownership | Tags |
|-------|---------|----------------|------|
| Track 1 | C: AW Migration | `src/client/code-pages/AnalysisWorkspace/`, PCF cleanup | `code-page`, `frontend`, `migration` |
| Track 2 | E: Re-Analysis | `AnalysisExecutionTools.cs`, re-analysis flow | `bff-api`, `ai` |
| Track 3 | I: Web Search | `WebSearchTools.cs`, `ChatKnowledgeScope` extension | `bff-api`, `ai` |

**Inputs**: Phase 1 complete (SprkChatBridge, streaming engine, action menu)

**Outputs**: Full Analysis Workspace experience via Code Pages + side pane

---

### Phase 3: Polish (Sprint 3)

**Objectives:**
1. Implement diff compare view for AI-proposed changes
2. Enable selection-based revision across panes
3. Add suggested follow-ups and clickable citations

**Deliverables:**
- [ ] `DiffCompareView` component (side-by-side and inline modes)
- [ ] Automatic mode selection (stream vs diff) based on operation type
- [ ] Editor selection API (`getSelectedHtml`, `replaceSelection`, `getSelectionRect`)
- [ ] Cross-pane selection flow (editor → SprkChat → revision → editor)
- [ ] `SprkChatSuggestions` component (contextual follow-up chips)
- [ ] `SprkChatCitationPopover` component (source reference popovers)
- [ ] `suggestions` and `citations` SSE events
- [ ] Search tools return source metadata for citations

**Parallel Track Assignments (Agent Teams):**

| Track | Package | File Ownership | Tags |
|-------|---------|----------------|------|
| Track 1 | F: Diff View | `DiffCompareView.tsx`, diff algorithm | `frontend`, `ui-components` |
| Track 2 | G: Selection Revision | Selection API, cross-pane flow | `frontend`, `bff-api` |
| Track 3 | H: Suggestions + Citations | `SprkChatSuggestions.tsx`, `SprkChatCitationPopover.tsx` | `frontend`, `bff-api` |

**Inputs**: Phase 2 complete (AW Code Page, re-analysis, streaming engine)

**Outputs**: Complete interactive collaboration feature set

---

### Phase 4: Deployment & Validation

**Objectives:**
1. Integration test all cross-pane interactions
2. Validate dark mode, high-contrast, accessibility
3. Performance benchmark against NFRs
4. Deploy to Dataverse and Azure

**Deliverables:**
- [ ] Integration test suite for cross-pane communication
- [ ] Dark mode + high-contrast visual validation
- [ ] Performance benchmarks (<100ms streaming, <200ms action menu, <2s pane load)
- [ ] Code Pages deployed to Dataverse (`sprk_SprkChatPane`, `sprk_AnalysisWorkspace`)
- [ ] BFF API deployed with new endpoints and tool classes
- [ ] Dataverse form configurations updated
- [ ] Legacy PCF AnalysisWorkspace removed from solution
- [ ] Project wrap-up and lessons learned

**Inputs**: All Phase 1-3 deliverables

**Outputs**: Production-ready deployment

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Lexical editor (latest) | GA | Low | Pin to tested version; `StreamingInsertPlugin` isolates integration |
| `Xrm.App.sidePanes` API | GA | Low | Verified in current environment |
| BroadcastChannel API | GA | Low | `window.postMessage` fallback |
| Azure Bing Search API | Pending | Medium | Required for Package I only; can defer |
| React 19 | GA | Low | Code Pages bundle independently |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R1 bug fixes | Deployed | Complete |
| SprkChat components | `src/client/shared/.../SprkChat/` | Production |
| RichTextEditor | `src/client/shared/.../RichTextEditor/` | Production |
| ChatEndpoints | `src/server/api/.../Api/Ai/ChatEndpoints.cs` | Production |
| SprkChatAgentFactory | `src/server/api/.../Services/Ai/Chat/SprkChatAgentFactory.cs` | Production |
| IAnalysisOrchestrationService | `src/server/api/.../Services/Ai/` | Production |

---

## 6. Testing Strategy

**Unit Tests** (80%+ coverage target):
- All new AI tool classes (`WorkingDocumentTools`, `AnalysisExecutionTools`, `WebSearchTools`)
- `SprkChatBridge` event emission and subscription
- `StreamingInsertPlugin` token insertion and position management
- `DiffCompareView` diff computation and mode selection
- `SprkChatActionMenu` filtering, keyboard navigation, playbook governance
- All new SSE event serialization/deserialization

**Integration Tests**:
- Chat session lifecycle with new tool classes
- Streaming write flow: SSE → BroadcastChannel → editor
- Re-analysis pipeline end-to-end
- Action menu API with different playbook capabilities
- Cross-pane communication reliability

**E2E/UI Tests** (via ui-test skill):
- SprkChat side pane on Matter, Project, Analysis forms
- Streaming write token insertion UX
- `/` action menu keyboard navigation
- Diff compare view Accept/Reject/Edit
- Dark mode rendering in all components
- Selection-based revision cross-pane flow

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] SprkChat Code Page loads in side pane within 2 seconds
- [ ] BroadcastChannel delivers events in <10ms
- [ ] Streaming tokens insert into editor with <100ms latency
- [ ] Action menu responds to `/` in <200ms
- [ ] Legacy chat artifacts removed (0 references to `useLegacyChat`)

**Phase 2:**
- [ ] Analysis Workspace Code Page renders full viewport (no PCF dependency)
- [ ] Re-analysis produces complete new output with progress indicator
- [ ] Web search returns results with proper citation metadata

**Phase 3:**
- [ ] Diff view correctly renders side-by-side and inline modes
- [ ] Selection-based revision flows across panes without data loss
- [ ] Suggestions display 2-3 relevant follow-ups after responses

**Phase 4:**
- [ ] All NFR benchmarks met (NFR-01 through NFR-08)
- [ ] 0 additional DI registrations verified
- [ ] All 11 success criteria from spec.md satisfied

### Business Acceptance

- [ ] SprkChat accessible from 3+ Dataverse form types
- [ ] Users can interactively edit documents via AI streaming
- [ ] Playbook model governs available AI capabilities per context

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Lexical streaming insert causes state corruption | Medium | High | Isolated plugin; document snapshots before every write; extensive testing |
| R2 | Code Page migration breaks Dataverse forms | Medium | Medium | Feature flag; maintain PCF fallback during transition |
| R3 | BroadcastChannel unavailable in some environments | Low | High | `window.postMessage` fallback with detection |
| R4 | Re-analysis token costs exceed budget | Medium | High | CostControl middleware; user confirmation prompt |
| R5 | React 19 bundle size impacts load time | Low | Medium | Code splitting; lazy load; <500KB gzipped target |
| R6 | Diff view slow for large HTML documents | Medium | Medium | Virtual rendering; diff visible sections only |
| R7 | Cross-pane timing issues during streaming | Low | Medium | Buffering; BroadcastChannel is synchronous same-origin |
| R8 | Placeholder code survives to production | Medium | High | `<placeholders>` manifest in every task; validation at wrap-up |

---

## 9. Placeholder Tracking Protocol

**Every task that produces placeholder/stub code MUST declare it:**

```xml
<placeholders>
  <placeholder location="src/path/to/file.ts" function="functionName">
    <stub-type>hardcoded-return | todo-comment | mock-data | no-op</stub-type>
    <real-implementation>Description of what the real code needs</real-implementation>
    <completed-by task="NNN">Task that will replace this placeholder</completed-by>
    <build-impact>compiles: yes | tests-pass: yes/no</build-impact>
  </placeholder>
</placeholders>
```

At project wrap-up (task 090), a placeholder audit verifies zero unresolved stubs remain.

---

## 10. Next Steps

1. **Generate task files** via `/task-create` (Step 3 of pipeline)
2. **Begin Phase 1** — 3 parallel tracks (Packages A, B, D + Legacy Cleanup)
3. **Checkpoint** after each sprint for integration validation

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
