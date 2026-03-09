# Project Plan: AI Playbook Node Builder R5

> **Last Updated**: 2026-02-28
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Rebuild the Playbook Builder from PCF (React 16, react-flow-renderer v10) to a React 19 Code Page using @xyflow/react v12+, and close the canvas-to-execution gap by building typed configuration forms for all 7 node types.

**Scope**:
- Code Page scaffold with auth, DataverseClient, build pipeline
- Canvas migration from react-flow-renderer v10 to @xyflow/react v12
- Replace all mock data with real Dataverse queries
- Build configuration forms for 5 missing node types (Deliver Output, Send Email, Create Task, AI Completion, Wait)
- Template variable panel and node validation badges
- AI Assistant and template library migration
- PCF cleanup and form integration

**Execution Model**: Autonomous Claude Code with parallel task agents — no human approval gates between tasks/phases.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006**: Code Page for standalone dialogs; place in `src/client/code-pages/PlaybookBuilder/`
- **ADR-021**: Fluent UI v9 exclusively; dark mode mandatory; React 19 for Code Pages; `makeStyles` for styling
- **ADR-022**: Code Pages exempt from PCF React 16 constraint; React 19 bundled
- **ADR-013**: AI calls through BFF only; no API keys in browser
- **ADR-012**: Import from `@spaarke/ui-components` before building custom
- **ADR-023**: Use ChoiceDialog for 2-4 option dialogs
- **ADR-001**: BFF endpoints follow Minimal API pattern
- **ADR-010**: ≤15 non-framework DI registrations

**From Spec**:
- Zero mock/hardcoded data in any store or component
- Direct Dataverse REST API for CRUD (not BFF); BFF only for AI streaming
- Preserve canvas JSON backward compatibility with R4

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| React 19 Code Page | Standalone workspace; @xyflow v12 requires React 18+ | New project in `src/client/code-pages/PlaybookBuilder/` |
| Direct Dataverse REST API | Separation: Code Page owns build-time CRUD; BFF reads at execution | DataverseClient service with fetch() + Bearer token |
| @xyflow/react v12 | Typed generics, hooks API, better sub-flow support | Migration from react-flow-renderer v10 |
| Zustand preserved | Framework-agnostic stores; only data sources change | 6 stores migrate with minimal changes |
| Auth from AnalysisWorkspace | Proven multi-strategy pattern (5 Xrm methods + MSAL fallback) | Copy authService.ts + msalConfig.ts |

### Discovered Resources

**Applicable ADRs** (loaded):
- `.claude/adr/ADR-006-pcf-over-webresources.md` — Code Page placement
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9 + dark mode
- `.claude/adr/ADR-022-pcf-platform-libraries.md` — Code Page React 19 exemption
- `.claude/adr/ADR-013-ai-architecture.md` — AI architecture
- `.claude/adr/ADR-012-shared-components.md` — Shared UI library
- `.claude/adr/ADR-023-choice-dialog-pattern.md` — Choice dialog
- `.claude/adr/ADR-001-minimal-api.md` — Minimal API
- `.claude/adr/ADR-010-di-minimalism.md` — DI minimalism

**Applicable Constraints**:
- `.claude/constraints/webresource.md` — Code Page deployment rules
- `.claude/constraints/ai.md` — AI endpoint authorization, streaming
- `.claude/constraints/api.md` — BFF endpoint patterns

**Key Patterns**:
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page scaffold template
- `.claude/patterns/ai/streaming-endpoints.md` — SSE streaming
- `.claude/patterns/ai/analysis-scopes.md` — Scope resolution
- `.claude/patterns/auth/msal-client.md` — Multi-strategy auth
- `.claude/patterns/dataverse/web-api-client.md` — Fetch-based Dataverse client

**Reference Implementations**:
- `src/client/code-pages/AnalysisWorkspace/` — Auth, build pipeline, theme detection
- `src/client/code-pages/DocumentRelationshipViewer/` — @xyflow/react v12 + React 19
- `src/client/pcf/PlaybookBuilderHost/` — R4 source (migration origin)

**Applicable Skills**:
- `code-page-deploy` — Build and deploy Code Page
- `dataverse-deploy` — Deploy solution to Dataverse

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Scaffold (Serial — foundation for all other phases)
  ├─ Project structure, Webpack 5, build pipeline
  ├─ AuthService + DataverseClient
  └─ Entry point + FluentProvider + theme detection

Phase 2: Canvas Migration (Parallel — after Phase 1)
  ├─ Install @xyflow/react, rewrite PlaybookCanvas
  ├─ Migrate 7 node components to v12 generics
  ├─ Migrate ConditionEdge to v12 EdgeProps
  └─ Migrate canvasStore to v12 types

Phase 3: Scope Resolution (Parallel — after Phase 1)
  ├─ Rewrite scopeStore with real Dataverse queries
  ├─ Rewrite modelStore with real Dataverse queries
  ├─ Build ActionSelector component
  └─ Update playbookNodeSync to use DataverseClient

Phase 4: Node Config Forms (Parallel — after Phase 1)
  ├─ DeliverOutputForm.tsx
  ├─ SendEmailForm.tsx
  ├─ CreateTaskForm.tsx
  ├─ AiCompletionForm.tsx
  ├─ WaitForm.tsx
  ├─ VariableReferencePanel.tsx
  ├─ NodeValidationBadge.tsx
  └─ Wire NodePropertiesForm routing (after all forms)

Phase 5: AI Assistant & Templates (Parallel — after Phase 1)
  ├─ Migrate AiAssistantModal + 12 sub-components
  ├─ Migrate aiAssistantStore
  └─ Migrate templateStore + ExecutionOverlay

Phase 6: Integration & Polish (Serial — requires all above)
  ├─ Wire BuilderLayout with all panels
  ├─ Keyboard shortcuts + auto-save end-to-end
  ├─ Dark mode verification (ADR-021)
  └─ Build + deploy as web resource

Phase 7: Verification & Cleanup (Serial — final)
  ├─ End-to-end execution test (all 7 node types)
  ├─ Remove PCF PlaybookBuilderHost from solution
  └─ Update form scripts to open code page
```

### Parallel Execution Strategy

**Phases 2, 3, 4, and 5 can run in parallel** after Phase 1 completes. Within each phase, tasks marked parallel have no inter-task dependencies.

File ownership prevents conflicts:
- Canvas Migration owns: `components/canvas/`, `components/nodes/`, `components/edges/`, `stores/canvasStore.ts`
- Scope Resolution owns: `stores/scopeStore.ts`, `stores/modelStore.ts`, `components/properties/ActionSelector.tsx`, `services/playbookNodeSync.ts`
- Node Config Forms owns: `components/properties/{FormName}.tsx` (each agent owns one form)
- AI Assistant owns: `components/ai-assistant/`, `stores/aiAssistantStore.ts`, `stores/templateStore.ts`

### Critical Path

**Blocking Dependencies:**
- Phase 1 BLOCKS Phases 2, 3, 4, 5 (foundation)
- Phases 2-5 BLOCK Phase 6 (integration)
- Phase 6 BLOCKS Phase 7 (verification)
- Tasks 030-036 BLOCK Task 037 (NodePropertiesForm routing)

**High-Risk Items:**
- @xyflow v12 API migration — Mitigation: DocumentRelationshipViewer is proven reference
- N:N relationship writes — Mitigation: Fallback to $batch if direct associate fails
- Canvas JSON compatibility — Mitigation: v12 uses same schema; test with R4-saved playbooks

---

## 4. Phase Breakdown

### Phase 1: Scaffold (Serial)

**Objectives:**
1. Create Code Page project structure with Webpack 5 build pipeline
2. Implement multi-strategy auth (from AnalysisWorkspace)
3. Create DataverseClient service for all CRUD operations
4. Set up entry point with FluentProvider and theme detection

**Deliverables:**
- [ ] `src/client/code-pages/PlaybookBuilder/` project structure
- [ ] `webpack.config.js` + `build-webresource.ps1` build pipeline
- [ ] `package.json` with all dependencies (@xyflow/react, Fluent v9, React 19, Zustand)
- [ ] `AuthService.ts` with multi-strategy token acquisition
- [ ] `DataverseClient.ts` with CRUD + associate/disassociate
- [ ] `index.tsx` entry point with `createRoot` + `FluentProvider`
- [ ] `useThemeDetection` hook for dark/light/high-contrast
- [ ] Successful build producing `out/sprk_playbookbuilder.html`

**Inputs**: AnalysisWorkspace code page (auth, build pipeline), DocumentRelationshipViewer (package.json)
**Outputs**: Compilable project with build pipeline, auth service, DataverseClient

### Phase 2: Canvas Migration (Parallel after Phase 1)

**Objectives:**
1. Install @xyflow/react v12 and rewrite PlaybookCanvas
2. Migrate all 7 node components to v12 typed generics
3. Migrate ConditionEdge to v12 EdgeProps
4. Update canvasStore to v12 types

**Deliverables:**
- [ ] `PlaybookCanvas.tsx` rewritten with @xyflow/react v12 named imports
- [ ] 7 node components using `NodeProps<Node<PlaybookNodeData>>` generics
- [ ] `ConditionEdge.tsx` using v12 EdgeProps
- [ ] `canvasStore.ts` updated to v12 types (Node, Edge, ReactFlowInstance)
- [ ] Drag-and-drop, connect, select, delete all functional
- [ ] MiniMap, snap-to-grid, zoom, pan working

**Inputs**: R4 PCF PlaybookBuilderHost components, DocumentRelationshipViewer patterns
**Outputs**: Working canvas with all node types rendering on v12

### Phase 3: Scope Resolution (Parallel after Phase 1)

**Objectives:**
1. Replace all mock data with real Dataverse queries
2. Build ActionSelector component
3. Update playbookNodeSync to use DataverseClient

**Deliverables:**
- [ ] `scopeStore.ts` rewritten — queries `sprk_analysisskill`, `sprk_aiknowledge`, `sprk_analysistool`
- [ ] `modelStore.ts` rewritten — queries `sprk_aimodeldeployment`
- [ ] `ActionSelector.tsx` — dropdown querying `sprk_analysisaction`
- [ ] `playbookNodeSync.ts` — uses DataverseClient for CRUD, writes `sprk_configjson`
- [ ] Zero mock/hardcoded data (no 'skill-1', no fake GUIDs)
- [ ] N:N relationships (associate/disassociate) working

**Inputs**: DataverseClient service, Dataverse dev environment with populated scope tables
**Outputs**: All scope selectors showing real data; node sync creating real records

### Phase 4: Node Config Forms (Parallel after Phase 1)

**Objectives:**
1. Build typed configuration forms for 5 missing node types
2. Create template variable panel and node validation badges
3. Wire NodePropertiesForm to route to type-specific forms

**Deliverables:**
- [ ] `DeliverOutputForm.tsx` — delivery type, Handlebars template, output format
- [ ] `SendEmailForm.tsx` — To/CC, subject, body, HTML toggle
- [ ] `CreateTaskForm.tsx` — subject, description, regarding, owner, due date
- [ ] `AiCompletionForm.tsx` — system prompt, user prompt, temperature, max tokens
- [ ] `WaitForm.tsx` — wait type, duration, datetime
- [ ] `VariableReferencePanel.tsx` — upstream `{{variable}}` listing with click-to-insert
- [ ] `NodeValidationBadge.tsx` — red/yellow/green config completeness
- [ ] `NodePropertiesForm.tsx` updated to render type-specific forms
- [ ] All forms write valid fields that `buildConfigJson()` maps to `sprk_configjson`

**Inputs**: design.md Section 7.2 (ConfigJson schemas per node type), existing NodePropertiesForm
**Outputs**: Complete configuration UI for all 7 node types

### Phase 5: AI Assistant & Templates (Parallel after Phase 1)

**Objectives:**
1. Migrate AiAssistantModal and all 12 sub-components
2. Update token acquisition in stores
3. Migrate template library to DataverseClient

**Deliverables:**
- [ ] `AiAssistantModal` + 12 sub-components migrated (framework-agnostic, minimal changes)
- [ ] `aiAssistantStore.ts` updated — token acquisition via AuthService
- [ ] `templateStore.ts` updated — uses DataverseClient for template CRUD
- [ ] `ExecutionOverlay` migrated
- [ ] AI chat streaming works via BFF SSE endpoint
- [ ] Template library loads and applies templates

**Inputs**: R4 PCF AI Assistant components, AuthService
**Outputs**: Working AI Assistant modal with streaming; template library functional

### Phase 6: Integration & Polish (Serial — requires Phases 2-5)

**Objectives:**
1. Wire BuilderLayout with all panels (canvas, properties, AI assistant, templates)
2. Implement keyboard shortcuts and end-to-end auto-save
3. Verify dark mode compliance (ADR-021)
4. Build and deploy as web resource

**Deliverables:**
- [ ] `BuilderLayout.tsx` wired with all panels
- [ ] Keyboard shortcuts (Ctrl+Z, Ctrl+S, Delete) working
- [ ] Auto-save with 500ms debounce + node sync
- [ ] Dark mode, light mode, high-contrast all correct
- [ ] Build produces `out/sprk_playbookbuilder.html` (< 1.5 MB gzipped)
- [ ] Deployed to dev Dataverse environment

**Inputs**: All Phase 2-5 outputs
**Outputs**: Fully functional, deployed Code Page

### Phase 7: Verification & Cleanup (Serial — final)

**Objectives:**
1. End-to-end execution test for all 7 node types
2. Remove PCF PlaybookBuilderHost from solution
3. Update form to open code page

**Deliverables:**
- [ ] All 7 node types execute end-to-end from AnalysisWorkspace
- [ ] PCF control references removed from solution XML
- [ ] Form button opens code page via `navigateTo`
- [ ] No `react-flow-renderer` references remain
- [ ] Lessons learned documented

**Inputs**: Deployed Code Page, AnalysisWorkspace
**Outputs**: Complete, verified system; R4 PCF retired

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| @xyflow/react v12 | GA | Low | Already used by DocumentRelationshipViewer |
| React 19 | GA | Low | Already used by multiple Code Pages |
| Fluent UI v9 | GA | Low | Standard across all Spaarke UI |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| AnalysisWorkspace code page | `src/client/code-pages/AnalysisWorkspace/` | Production |
| DocumentRelationshipViewer | `src/client/code-pages/DocumentRelationshipViewer/` | Production |
| R4 PlaybookBuilderHost PCF | `src/client/pcf/PlaybookBuilderHost/` | Production (to be replaced) |
| BFF AI streaming endpoint | `src/server/api/Sprk.Bff.Api/` | Production |
| Dataverse dev environment | `spaarkedev1.crm.dynamics.com` | Ready |

---

## 6. Testing Strategy

**Integration Testing**:
- Load playbook saved by R4 PCF → verify canvas renders correctly
- Save playbook → verify `sprk_playbooknode` records created with valid `sprk_configjson`
- Execute all 7 node types → verify no ConfigJson errors

**Manual Verification**:
- Dark mode toggle → verify no hard-coded colors
- Drag-and-drop from palette → verify node creation
- Template variables → verify `{{variable}}` resolution
- AI Assistant chat → verify SSE streaming

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1 (Scaffold):**
- [ ] Project builds and produces single HTML web resource
- [ ] Auth token acquired successfully in dev environment
- [ ] DataverseClient can read/write Dataverse records

**Phase 2 (Canvas):**
- [ ] All 7 node types render on @xyflow/react v12
- [ ] Drag-and-drop, connect, delete functional

**Phase 3 (Scopes):**
- [ ] Zero mock data — all selectors show real Dataverse records
- [ ] Node sync writes sprk_playbooknode with sprk_configjson

**Phase 4 (Forms):**
- [ ] All 7 node types have configuration forms
- [ ] sprk_configjson contains valid typed config for each node type

**Phase 6 (Integration):**
- [ ] Auto-save + dark mode + keyboard shortcuts all working
- [ ] Bundle < 1.5 MB gzipped

**Phase 7 (Verification):**
- [ ] All 7 node types execute end-to-end
- [ ] PCF removed, form opens code page

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | @xyflow v12 API breaking changes during migration | Low | Medium | DocumentRelationshipViewer proves compatibility |
| R2 | N:N relationship writes fail from Code Page context | Low | Medium | Fallback to $batch requests |
| R3 | Canvas JSON incompatibility between v10 and v12 | Low | High | Test with R4-saved playbooks; add migration function if needed |
| R4 | Missing executors (Wait, AI Completion) prevent full E2E | High | Low | Build config forms; note execution needs separate BFF work |
| R5 | Scope tables empty in dev environment | Medium | Medium | Verify data exists before Phase 3; create test records if needed |

---

## 9. Next Steps

1. **Generate task files** — `/task-create` to decompose phases into POML tasks
2. **Begin Phase 1** — Start with project scaffold (Task 001)
3. **Parallel execution** — Launch Phases 2-5 simultaneously after Phase 1

---

**Status**: Ready for Tasks
**Next Action**: Generate task files from this plan

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
