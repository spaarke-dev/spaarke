# AI Playbook Node Builder R5 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-28
> **Source**: `projects/ai-playbook-node-builder-r5/design.md`
> **Branch**: `work/ai-playbook-node-builder-r5`
> **Predecessor**: `ai-playbook-node-builder-r4` (PCF control, React 16, react-flow-renderer v10)

---

## Executive Summary

Rebuild the Playbook Builder from a PCF control (React 16, `react-flow-renderer` v10) into a **standalone React 19 Code Page** using **@xyflow/react v12+**, and simultaneously close the critical canvas-to-execution gap by building typed configuration forms for all 7 node types. The current canvas was built as a visual POC — only 2 of 7 node types (AI Analysis, Condition) can actually execute end-to-end. This project makes the Playbook Builder a fully functional tool for composing executable AI playbooks.

### Why Now

- **Pre-release window** — no production users to migrate
- **All scope selectors use hardcoded mock data** — `'skill-1'` through `'skill-6'`, fake GUIDs `'50000000-...'`; replacing them requires touching every store anyway
- **Proven code page patterns** — AnalysisWorkspace and DocumentRelationshipViewer already establish auth, build pipeline, and deployment
- **@xyflow/react v12 already in codebase** — DocumentRelationshipViewer (`spaarke-wt-ai-semantic-search-ui-r3`) uses `@xyflow/react ^12.8.3` with React 19
- **5 of 7 node types are visual placeholders** — they create canvas boxes but produce execution errors because no configuration UI writes `sprk_configjson`

---

## Execution Model

### Autonomous Claude Code with Parallel Task Agents

**This project is executed entirely by Claude Code.** Task agents run simultaneously on independent work streams without requiring human approval between tasks or phases.

| Aspect | Approach |
|--------|----------|
| **Orchestration** | Claude Code `task-execute` skill drives each task autonomously |
| **Parallelism** | Independent tasks run as parallel Claude Code task agents (subagent_type: general-purpose) via the Task tool |
| **Approval gates** | None between tasks/phases — agents proceed automatically upon dependency satisfaction |
| **Human involvement** | Design review (this spec) only; implementation is fully autonomous |
| **Quality gates** | Automated: code-review + adr-check run at task completion (Step 9.5 of task-execute) |
| **Checkpointing** | Automatic via context-handoff every 3 steps or at 60% context usage |

### Parallel Execution Strategy

Tasks are decomposed into dependency groups. Within each group, all tasks execute simultaneously:

```
Phase 1: Scaffold (serial — foundation for everything)
  Task 001: Project structure + Webpack + build pipeline
  Task 002: AuthService + DataverseClient
  Task 003: Entry point + FluentProvider + theme detection

Phase 2: Canvas Migration (parallelizable after Phase 1)
  ┌─ Task 010: Install @xyflow/react, rewrite PlaybookCanvas.tsx
  ├─ Task 011: Migrate 7 node components to v12 NodeProps generics  ← parallel
  ├─ Task 012: Migrate ConditionEdge to v12 EdgeProps               ← parallel
  └─ Task 013: Migrate canvasStore to v12 types                     ← parallel

Phase 3: Scope Resolution (parallelizable after Phase 1)
  ┌─ Task 020: Rewrite scopeStore — real Dataverse queries
  ├─ Task 021: Rewrite modelStore — real Dataverse queries           ← parallel
  ├─ Task 022: Build ActionSelector component                        ← parallel
  └─ Task 023: Update playbookNodeSync to use DataverseClient        ← parallel

Phase 4: Node Config Forms (parallelizable — each form is independent)
  ┌─ Task 030: DeliverOutputForm.tsx
  ├─ Task 031: SendEmailForm.tsx                                     ← parallel
  ├─ Task 032: CreateTaskForm.tsx                                    ← parallel
  ├─ Task 033: AiCompletionForm.tsx                                  ← parallel
  ├─ Task 034: WaitForm.tsx                                          ← parallel
  ├─ Task 035: VariableReferencePanel.tsx                            ← parallel
  ├─ Task 036: NodeValidationBadge.tsx                               ← parallel
  └─ Task 037: Wire NodePropertiesForm to render type-specific forms ← after 030-036

Phase 5: AI Assistant & Templates (parallelizable after Phase 1)
  ┌─ Task 040: Migrate AiAssistantModal + 12 sub-components
  ├─ Task 041: Migrate aiAssistantStore (update token acquisition)   ← parallel
  └─ Task 042: Migrate templateStore + ExecutionOverlay              ← parallel

Phase 6: Integration & Polish (serial — requires all above)
  Task 050: Wire BuilderLayout with all panels
  Task 051: Keyboard shortcuts + auto-save end-to-end
  Task 052: Dark mode verification (ADR-021)
  Task 053: Build + deploy as web resource

Phase 7: Execution Verification & Cleanup (serial — final)
  Task 060: End-to-end execution test (all 7 node types)
  Task 061: Remove PCF PlaybookBuilderHost from solution
  Task 062: Update form scripts to open code page
```

**Phases 2, 3, 4, and 5 can run in parallel** — they have no inter-phase dependencies (only a shared dependency on Phase 1 completion). Within each phase, tasks marked "parallel" can run simultaneously.

### File Ownership for Parallel Safety

Each parallel task agent owns specific files to prevent conflicts:

| Task Group | Owned Files | No Other Agent Touches |
|------------|-------------|----------------------|
| Canvas Migration | `components/canvas/`, `components/nodes/`, `components/edges/`, `stores/canvasStore.ts` | Scope Resolution agents |
| Scope Resolution | `stores/scopeStore.ts`, `stores/modelStore.ts`, `components/properties/ActionSelector.tsx`, `services/playbookNodeSync.ts` | Canvas Migration agents |
| Node Config Forms | `components/properties/{FormName}.tsx` (each agent owns one form) | Other form agents |
| AI Assistant | `components/ai-assistant/`, `stores/aiAssistantStore.ts`, `stores/templateStore.ts` | All others |

---

## Scope

### In Scope

1. **Code Page scaffold** — React 19 project structure, Webpack 5, build-webresource.ps1, entry point
2. **Authentication** — Multi-strategy auth from AnalysisWorkspace (Xrm platform + MSAL ssoSilent)
3. **DataverseClient service** — `fetch()`-based CRUD replacing PCF `context.webAPI`
4. **Canvas migration** — react-flow-renderer v10 → @xyflow/react v12+ with typed generics
5. **All 7 custom node types** migrated to v12 `NodeProps<Node<PlaybookNodeData>>` API
6. **Scope resolution** — Replace ALL mock data (skills, knowledge, tools, models) with real Dataverse queries
7. **ActionSelector component** — New dropdown querying `sprk_analysisaction` (currently missing entirely)
8. **isActive toggle** — New toggle on all nodes (field exists in Dataverse, no UI today)
9. **Node configuration forms for ALL 7 node types**:
   - DeliverOutputForm (delivery type, Handlebars template, output format)
   - SendEmailForm (To/CC recipients, subject, body, HTML toggle)
   - CreateTaskForm (subject, description, regarding object, owner, due date)
   - AiCompletionForm (system prompt, user prompt template, temperature, max tokens)
   - WaitForm (wait type, duration, datetime)
   - AI Analysis (already has basic UI — needs action selector + real data)
   - Condition (already complete — no changes needed)
10. **Template variable system** — `{{nodeName.output.fieldName}}` variable reference panel with insert-into-field
11. **Node validation badges** — Per-node config completeness indicator (red/yellow/green)
12. **playbookNodeSync rewrite** — `buildConfigJson()` maps all typed fields into `sprk_configjson`
13. **AI Assistant migration** — All 12 sub-components (framework-agnostic, minimal changes)
14. **Template library migration** — Switch to DataverseClient
15. **Auto-save** — Debounced 500ms save to `sprk_canvaslayoutjson` + node sync
16. **Dark mode** — Light, dark, and high-contrast theme support (ADR-021)
17. **PCF cleanup** — Remove PlaybookBuilderHost from solution, update form to open code page

### Out of Scope

- New node types beyond the existing 7 (configuration forms for existing types IS in scope)
- Playbook execution engine changes (BFF API / orchestration service / node executors are unchanged — they already read ConfigJson; the gap is that the canvas never writes it)
- AnalysisWorkspace changes (it already supports node-based execution)
- New Dataverse entities or schema changes (all tables already exist)
- Office add-in integration
- Staging/production deployment
- New BFF API endpoints (the Code Page uses direct Dataverse REST API for CRUD; existing BFF streaming endpoint for AI Assistant is unchanged)

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| New Code Page | `src/client/code-pages/PlaybookBuilder/` | Entire new project (scaffold, components, stores, services) |
| PCF to remove | `src/client/pcf/PlaybookBuilderHost/` | Delete after code page verified |
| Shared components | `src/client/shared/Spaarke.UI.Components/` | Import existing; potentially contribute new components |
| Solution XML | `src/solutions/` | Remove PCF, add web resource |
| Reference impl | `spaarke-wt-ai-semantic-search-ui-r3/.../DocumentRelationshipViewer/` | Read-only reference for @xyflow/react v12 patterns |
| Reference impl | `src/client/code-pages/AnalysisWorkspace/` | Read-only reference for auth, build pipeline, config patterns |

---

## Requirements

### Functional Requirements

1. **FR-01**: Code page opens from playbook form via `Xrm.Navigation.navigateTo` with `playbookId` parameter — Acceptance: Canvas loads with existing playbook data within 3 seconds
2. **FR-02**: Canvas renders all 7 node types on @xyflow/react v12 with correct visual representation — Acceptance: Drag-and-drop, connect, select, delete all functional
3. **FR-03**: Existing canvas JSON (`sprk_canvaslayoutjson`) loads without errors or data loss — Acceptance: Load playbook saved by R4 PCF, all nodes/edges preserved
4. **FR-04**: Skills load from `sprk_analysisskills` Dataverse table (not mock data) — Acceptance: Checkbox list shows real skill records with names/descriptions
5. **FR-05**: Knowledge loads from `sprk_aiknowledges` Dataverse table (not mock data) — Acceptance: Checkbox list shows real knowledge records
6. **FR-06**: Tools load from `sprk_analysistools` Dataverse table (not mock data) — Acceptance: Dropdown shows real tool records
7. **FR-07**: Actions load from `sprk_analysisactions` Dataverse table — Acceptance: New ActionSelector dropdown shows real action records
8. **FR-08**: Model deployments load from `sprk_aimodeldeployments` table (not mock data) — Acceptance: Dropdown shows real model deployment records with provider/capability
9. **FR-09**: Deliver Output node has configuration form with delivery type, Handlebars template editor, output format options — Acceptance: `sprk_configjson` contains `deliveryType`, `template`, `outputFormat` after save
10. **FR-10**: Send Email node has configuration form with To/CC, subject, body, HTML toggle — Acceptance: `sprk_configjson` contains `to`, `cc`, `subject`, `body`, `isHtml` after save
11. **FR-11**: Create Task node has configuration form with subject, description, regarding object, owner, due date — Acceptance: `sprk_configjson` contains all task fields after save
12. **FR-12**: AI Completion node has configuration form with system prompt, user prompt template, temperature, max tokens — Acceptance: `sprk_configjson` contains all AI completion fields after save
13. **FR-13**: Wait node has configuration form with wait type, duration, datetime — Acceptance: `sprk_configjson` contains `waitType` and associated fields after save
14. **FR-14**: Template variable panel shows available `{{nodeName.output.fieldName}}` references from upstream nodes — Acceptance: Panel lists upstream node output variables; click inserts `{{variable}}` into active field
15. **FR-15**: Node validation badge shows config completeness per node (red=missing required, yellow=partial, green=complete) — Acceptance: Badge updates in real-time as user fills in config
16. **FR-16**: Auto-save writes canvas JSON to `sprk_canvaslayoutjson` with 500ms debounce — Acceptance: Close and reopen preserves all changes
17. **FR-17**: Canvas-to-node sync creates/updates/deletes `sprk_playbooknode` records including `sprk_configjson` — Acceptance: Node records in Dataverse match canvas state including typed config
18. **FR-18**: AI Assistant modal works via BFF API SSE streaming — Acceptance: Chat, command palette, operation feedback all functional
19. **FR-19**: All 7 node types execute end-to-end without ConfigJson errors when run from AnalysisWorkspace — Acceptance: Build playbook → save → execute → all nodes produce output
20. **FR-20**: PCF PlaybookBuilderHost removed from solution; form opens code page instead — Acceptance: No PCF references remain; form navigateTo opens code page

### Non-Functional Requirements

- **NFR-01**: Canvas loads within 3 seconds for playbooks with up to 50 nodes
- **NFR-02**: Scope data (skills, knowledge, tools, actions, models) loads in parallel within 2 seconds
- **NFR-03**: Auto-save completes within 1 second for typical playbooks (< 30 nodes)
- **NFR-04**: Bundle size < 1.5 MB (gzipped) — consistent with AnalysisWorkspace (~1 MB)
- **NFR-05**: Token refresh occurs proactively (4-minute interval) to prevent expiration during long design sessions
- **NFR-06**: Dark mode, light mode, and high-contrast mode all render correctly (ADR-021)
- **NFR-07**: WCAG 2.1 AA accessibility compliance (ADR-021)
- **NFR-08**: Zero mock/hardcoded data in any store or component

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| **ADR-006** | Code Page placement and architecture | MUST use Code Page for standalone dialogs; place in `src/client/code-pages/` |
| **ADR-021** | Fluent UI v9, dark mode, React version | MUST use Fluent v9 exclusively; MUST support dark mode; React 19 for Code Pages |
| **ADR-022** | PCF platform libraries (confirms Code Page exemption) | React 19 is explicitly permitted for Code Pages; no PCF manifest needed |
| **ADR-013** | AI Architecture | MUST NOT call Azure AI directly from browser; AI streaming via BFF only |
| **ADR-012** | Shared component library | MUST import from `@spaarke/ui-components` before building custom |
| **ADR-023** | Choice dialog pattern | Use `ChoiceDialog` from shared library for option dialogs |
| **ADR-001** | Minimal API (tangential — BFF side) | Any new BFF endpoints MUST follow Minimal API pattern |
| **ADR-010** | DI minimalism (tangential — BFF side) | ≤15 non-framework DI registrations |

### MUST Rules

- MUST place project in `src/client/code-pages/PlaybookBuilder/`
- MUST use React 19 `createRoot()` entry point
- MUST bundle React 19 + Fluent v9 in output
- MUST use `@fluentui/react-components` exclusively (no Fluent v8, no MUI, no Ant Design)
- MUST use `@fluentui/react-icons` for all icons
- MUST use `makeStyles` (Griffel) for custom styling — including @xyflow node/edge renderers
- MUST use Fluent design tokens for all colors, spacing, typography — no hard-coded hex/rgb
- MUST wrap root in `FluentProvider` with theme
- MUST support light, dark, and high-contrast modes
- MUST read parameters via `URLSearchParams` (not PCF context)
- MUST build with Webpack 5 + `build-webresource.ps1` (two-step pipeline)
- MUST deploy as single self-contained HTML file `out/sprk_playbookbuilder.html`
- MUST import shared components from `@spaarke/ui-components` before building custom equivalents
- MUST use `fetch()` + Bearer token for Dataverse Web API calls (not PCF `context.webAPI`)
- MUST NOT call Azure AI services directly from browser (use BFF API for AI streaming)
- MUST NOT expose API keys in client-side code
- MUST NOT use Fluent v8 or alternative UI libraries
- MUST NOT hard-code colors in node/edge renderers
- MUST NOT deploy `index.html` + `bundle.js` as separate web resources

### Existing Patterns to Follow

| Pattern | Source | What to Copy |
|---------|--------|-------------|
| Code Page entry point | `src/client/code-pages/AnalysisWorkspace/index.tsx` | `createRoot` + `FluentProvider` + theme detection |
| Auth service | `src/client/code-pages/AnalysisWorkspace/services/authService.ts` | Multi-strategy token acquisition |
| MSAL config | `src/client/code-pages/AnalysisWorkspace/config/msalConfig.ts` | Azure AD configuration |
| Build pipeline | `src/client/code-pages/AnalysisWorkspace/webpack.config.js` | Webpack 5 + esbuild-loader |
| Inline HTML | `src/client/code-pages/AnalysisWorkspace/build-webresource.ps1` | JS → single HTML file |
| @xyflow/react v12 | `spaarke-wt-ai-semantic-search-ui-r3/.../DocumentRelationshipViewer/` | Named imports, typed NodeProps, hooks API |
| Force layout | `spaarke-wt-ai-semantic-search-ui-r3/.../hooks/useForceLayout.ts` | d3-force integration with @xyflow |

---

## Success Criteria

### Migration Verification

1. [ ] All 7 node types render correctly on @xyflow/react v12 canvas — Verify: visual inspection
2. [ ] Drag-and-drop from palette creates nodes at correct position — Verify: `screenToFlowPosition()` works
3. [ ] Edge connections work including condition branch routing — Verify: true/false handles route correctly
4. [ ] Existing playbook canvas JSON loads without errors — Verify: load R4-saved playbook
5. [ ] Snap-to-grid, zoom, pan, fit view all functional — Verify: interactive testing
6. [ ] MiniMap shows correct node colors by type — Verify: visual inspection
7. [ ] Keyboard shortcuts work (Ctrl+Z, Ctrl+S, Delete) — Verify: each shortcut tested
8. [ ] Dark mode renders correctly (ADR-021) — Verify: toggle theme, no hard-coded colors

### Dataverse Integration

9. [ ] Skills load from `sprk_analysisskills` (not mock data) — Verify: real GUIDs in selection
10. [ ] Knowledge loads from `sprk_aiknowledges` (not mock data) — Verify: real GUIDs in selection
11. [ ] Tools load from `sprk_analysistools` (not mock data) — Verify: real GUIDs in dropdown
12. [ ] Actions load from `sprk_analysisactions` — Verify: ActionSelector dropdown populated
13. [ ] Model deployments load from `sprk_aimodeldeployments` (not mock data) — Verify: real GUIDs
14. [ ] Auto-save writes canvas JSON to `sprk_canvaslayoutjson` — Verify: inspect record after edit
15. [ ] Node sync creates/updates/deletes `sprk_playbooknode` records — Verify: query node records
16. [ ] Selected scope IDs are real Dataverse GUIDs (not 'skill-1', etc.) — Verify: inspect node records

### Execution Integration (Per-Node ConfigJson)

17. [ ] AI Analysis node: ConfigJson contains scope IDs, model, tool — Verify: executor succeeds
18. [ ] AI Completion node: ConfigJson contains prompts, temperature, tokens — Verify: executor succeeds
19. [ ] Deliver Output node: ConfigJson contains deliveryType, template, format — Verify: formatted output (not raw JSON)
20. [ ] Send Email node: ConfigJson contains to, cc, subject, body, isHtml — Verify: email sends
21. [ ] Create Task node: ConfigJson contains subject, description, regarding, owner — Verify: task created
22. [ ] Wait node: ConfigJson contains waitType, duration/datetime — Verify: executor pauses
23. [ ] Condition node: conditionJson evaluates correctly, branches route — Verify: branching works
24. [ ] Template variables `{{nodeName.output.fieldName}}` resolve to upstream values — Verify: output contains resolved text
25. [ ] Node validation badges show config completeness — Verify: red/yellow/green states

### End-to-End

26. [ ] Open code page from playbook form → canvas loads — Verify: navigateTo works
27. [ ] Design playbook → auto-save → close → reopen → canvas intact — Verify: data persistence
28. [ ] All 7 node types execute end-to-end from AnalysisWorkspace — Verify: no ConfigJson errors
29. [ ] AI Assistant chat works (SSE streaming to BFF API) — Verify: chat conversation flows
30. [ ] Template library loads and applies templates — Verify: template applies to canvas

### Graduation from R4 PCF

31. [ ] Zero mock/hardcoded data in any store — Verify: grep for mock arrays, fake GUIDs
32. [ ] PCF control removed from solution XML — Verify: no PlaybookBuilderHost references
33. [ ] Form updated to open code page — Verify: form button opens code page
34. [ ] No `react-flow-renderer` references remain — Verify: grep confirms zero hits

---

## Technology Stack

| Aspect | Value |
|--------|-------|
| React | 19.0.0 (bundled) |
| Graph Library | @xyflow/react ^12.8.3 |
| State Management | Zustand 5.x |
| UI Framework | Fluent UI v9 (`@fluentui/react-components`) |
| Icons | `@fluentui/react-icons` |
| Shared Components | `@spaarke/ui-components` (workspace:*) |
| Auth | Xrm platform strategies + @azure/msal-browser ^4.x |
| Dataverse Access | Direct REST API via fetch() |
| Build | Webpack 5 + esbuild-loader → build-webresource.ps1 |
| Deployment | Single inline HTML web resource (`sprk_playbookbuilder.html`) |
| Layout (optional) | d3-force ^3.0.0 for auto-layout |

---

## Component Inventory

### Components to Create (New)

| Component | Purpose | Priority |
|-----------|---------|----------|
| `DataverseClient` | fetch()-based Dataverse Web API CRUD | P0 — everything depends on this |
| `AuthService` | Multi-strategy token acquisition (from AnalysisWorkspace) | P0 |
| `usePlaybookLoader` | Load playbook + scope data on mount | P0 |
| `useAutoSave` | Debounced save + node sync | P0 |
| `ActionSelector` | Dropdown querying `sprk_analysisaction` | P1 |
| `DeliverOutputForm` | Delivery type, template editor, output format | P1 |
| `SendEmailForm` | Recipients, subject, body, HTML toggle | P1 |
| `CreateTaskForm` | Subject, description, regarding, owner, due date | P1 |
| `AiCompletionForm` | System prompt, user prompt, temperature, max tokens | P1 |
| `WaitForm` | Wait type, duration, datetime | P1 |
| `VariableReferencePanel` | Lists upstream `{{variables}}`, click-to-insert | P1 |
| `NodeValidationBadge` | Per-node config completeness indicator | P2 |
| `useAuth` | Auth hook for React tree (from AnalysisWorkspace) | P0 |
| `useThemeDetection` | Detect light/dark mode (from AnalysisWorkspace) | P0 |

### Components to Migrate (From R4 PCF — ~70% as-is)

| Component | Migration Effort | Changes Required |
|-----------|-----------------|------------------|
| BuilderLayout.tsx | Low | Remove PCF container refs |
| 7 Node components | Medium | Update to v12 `NodeProps<Node<T>>` generics |
| ConditionEdge.tsx | Medium | Update to v12 EdgeProps generics |
| Canvas.tsx → PlaybookCanvas.tsx | High | Rewrite for v12 API (named imports, hooks) |
| PropertiesPanel.tsx | Low | As-is |
| NodePropertiesForm.tsx | Medium | Add type-specific form routing + new accordion sections |
| ScopeSelector.tsx | High | Rewrite data source (mock → Dataverse) |
| ModelSelector.tsx | High | Rewrite data source (mock → Dataverse) |
| ConditionEditor.tsx | Low | As-is |
| AiAssistantModal + 12 sub-components | Low | Update token acquisition only |
| canvasStore.ts | Medium | Update to v12 types |
| scopeStore.ts | High | Complete rewrite (mock → Dataverse queries) |
| modelStore.ts | High | Complete rewrite (mock → Dataverse queries) |
| aiAssistantStore.ts | Low | Update token acquisition |
| executionStore.ts | Low | As-is |
| templateStore.ts | Medium | Switch to DataverseClient |
| playbookNodeSync.ts | High | Use DataverseClient + buildConfigJson() for all node types |
| AiPlaybookService.ts | Low | Update auth token source |
| useExecutionStream.ts | Low | As-is |
| useKeyboardShortcuts.ts | Low | As-is |
| useResponsive.ts | Low | As-is |

### PCF Coupling Points to Replace (11 Total)

| PCF API | Replacement |
|---------|-------------|
| `context.webAPI.updateRecord()` | `DataverseClient.updateRecord()` via `fetch()` |
| `context.webAPI.retrieveRecord()` | `DataverseClient.retrieveRecord()` via `fetch()` |
| `context.webAPI.createRecord()` | `DataverseClient.createRecord()` via `fetch()` |
| `context.webAPI.deleteRecord()` | `DataverseClient.deleteRecord()` via `fetch()` |
| `context.webAPI.retrieveMultipleRecords()` | `DataverseClient.retrieveMultipleRecords()` via `fetch()` |
| `context.parameters.*` | `URLSearchParams` from `window.location.search` |
| `context.mode.trackContainerResize()` | `ResizeObserver` |
| `context.mode.allocatedWidth/Height` | `window.innerWidth/Height` |
| `context.mode.contextInfo.entityId` | URL param `?playbookId={guid}` |
| `context.notifyOutputChanged()` | Remove (auto-save only) |
| `Xrm.Navigation.openForm()` | `window.open()` or `Xrm.Navigation.navigateTo` |

---

## Data Architecture

### Dataverse Tables (All Existing — No Schema Changes)

| Table | Role | Code Page Access |
|-------|------|-----------------|
| `sprk_analysisplaybook` | Playbook record (name, description, canvasJson) | Read + Write (CRUD) |
| `sprk_playbooknode` | Executable node records (1:N from playbook) | Read + Write (sync) |
| `sprk_analysisskill` | Skill definitions | Read only |
| `sprk_aiknowledge` | Knowledge source definitions | Read only |
| `sprk_analysistool` | Tool definitions | Read only |
| `sprk_analysisaction` | Action definitions | Read only |
| `sprk_aimodeldeployment` | AI model deployment configurations | Read only |
| `sprk_playbooknode_analysisskill` | Node ↔ Skill (N:N) | Write (associate/disassociate) |
| `sprk_playbooknode_aiknowledge` | Node ↔ Knowledge (N:N) | Write (associate/disassociate) |

### Key Fields on sprk_playbooknode

| Field | Purpose | Written By |
|-------|---------|-----------|
| `sprk_name` | Node display name | playbookNodeSync |
| `sprk_executionorder` | Topological sort order | playbookNodeSync (Kahn's algorithm) |
| `sprk_configjson` | **Type-specific execution config** — THE critical field | playbookNodeSync via `buildConfigJson()` |
| `sprk_outputvariable` | Output variable name for template references | playbookNodeSync |
| `sprk_isactive` | Enable/disable toggle | playbookNodeSync |
| `sprk_timeoutseconds` | Execution timeout | playbookNodeSync |
| `sprk_retrycount` | Retry on failure | playbookNodeSync |
| `sprk_conditionjson` | Condition expression (condition nodes only) | playbookNodeSync |
| `sprk_dependsonjson` | Upstream node GUIDs (execution graph) | playbookNodeSync |
| `sprk_actionid` | Lookup → sprk_analysisaction | playbookNodeSync |
| `sprk_toolid` | Lookup → sprk_analysistool | playbookNodeSync |
| `sprk_modeldeploymentid` | Lookup → sprk_aimodeldeployment | playbookNodeSync |

### Separation of Concerns (Unchanged)

```
Playbook Builder (Code Page)     →  Owns Dataverse CRUD at build time
  - Creates/updates sprk_playbooknode records
  - Reads scope tables (skills, knowledge, tools, actions, models)
  - Writes sprk_configjson with typed node config
  - Saves canvas JSON to sprk_analysisplaybook

BFF API                           →  Only reads at execution time
  - Reads sprk_playbooknode records
  - Resolves scopes via N:N tables
  - Executes nodes via PlaybookOrchestrationService
  - NO changes to BFF API required
```

---

## Dependencies

### Prerequisites

- Phase 1 scaffold must complete before Phases 2-5 can start
- `DataverseClient` + `AuthService` must exist before any Dataverse-dependent work
- Real data must exist in Dataverse scope tables (`sprk_analysisskill`, etc.) for integration testing

### Internal Dependencies

| Dependency | Purpose | Status |
|------------|---------|--------|
| AnalysisWorkspace code page | Auth, build pipeline, config patterns to copy | Exists — read-only reference |
| DocumentRelationshipViewer | @xyflow/react v12 patterns to follow | Exists — read-only reference |
| BFF API streaming endpoint | AI Assistant chat (`/api/ai/playbook-builder/process`) | Exists — unchanged |
| Dataverse dev environment | Real scope table data | Must verify populated |

### External Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| @xyflow/react | ^12.8.3 | Canvas graph library |
| d3-force | ^3.0.0 | Auto-layout (optional) |
| react | ^19.0.0 | UI framework |
| react-dom | ^19.0.0 | DOM rendering |
| @fluentui/react-components | ^9.54.0 | Fluent UI v9 |
| @fluentui/react-icons | ^2.0.0 | Icons |
| @azure/msal-browser | ^4.x | Auth fallback |
| zustand | ^5.x | State management |
| webpack | ^5.x | Build tooling |

---

## Architecture Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Code Page over PCF | Playbook Builder is a standalone workspace, not a form field; React 19 unlocks @xyflow v12 | ADR-006 |
| Direct Dataverse REST API (not BFF) | Separation of concerns: Code Page owns build-time CRUD; BFF only reads at execution time | — |
| Preserve canvas JSON format | v12 uses same node/edge schema as v10; no data migration needed | — |
| Auth from AnalysisWorkspace | Proven multi-strategy pattern; handles all hosting scenarios | — |
| Zustand stays (not rewritten) | All 6 stores are framework-agnostic; only data sources change | — |
| Typed config in PlaybookNodeData | Each node type's config fields are explicit properties (not generic Record); flows through buildConfigJson() into sprk_configjson | — |

---

## Owner Clarifications

*Captured during design review:*

| Topic | Clarification | Impact |
|-------|--------------|--------|
| Separation of concerns | "The BFF API should only be involved at execution time, not during pure Dataverse record create/update" | Code Page uses direct Dataverse REST API for all CRUD; BFF only for AI streaming |
| Mock data removal | "We need to remove all stub or hardcoded code — this needs to be a fully functioning system, not a POC" | Zero tolerance for mock data in any store; all selectors must query real Dataverse tables |
| Canvas purpose | "The Playbook Builder is how we build playbooks" — canvas must support full execution configuration, not just visual layout | Added Phase 4 (Node Configuration Forms) — 7 new form components + variable reference panel + validation |
| Execution model | "Project will be executed by Claude Code, with parallel work done by Claude Code task agents running simultaneously and without requiring approval between tasks/phases" | Autonomous execution with parallel task agents; no human approval gates between tasks |
| Project naming | R4 = current PCF work (completed); R5 = this Code Page rebuild; `x-` prefix means archived | Branch: `work/ai-playbook-node-builder-r5` |

---

## Assumptions

*Proceeding with these assumptions:*

- **Dataverse scope tables are populated**: `sprk_analysisskill`, `sprk_aiknowledge`, `sprk_analysistool`, `sprk_analysisaction`, `sprk_aimodeldeployment` have real records in the dev environment. If empty, the UI will show "No items available" states — which is correct behavior but not useful for testing.
- **Canvas JSON backward compatibility**: @xyflow/react v12 uses the same node/edge schema as react-flow-renderer v10. If any schema differences surface, a migration function will be needed (low probability based on v12 documentation and DocumentRelationshipViewer usage).
- **N:N relationship writes from Code Page**: The Dataverse Web API supports `POST /{entitySet}({id})/{navigationProperty}/$ref` for associate and `DELETE` for disassociate. If this doesn't work from a Code Page context, fallback to `$batch` requests.
- **BFF AI streaming endpoint is stable**: The existing `/api/ai/playbook-builder/process` SSE endpoint works correctly and requires no changes for this migration.
- **Executor ConfigJson schemas match design**: The ConfigJson schemas documented in design.md Section 7.2 match what the actual node executors expect. If executors expect different field names, the `buildConfigJson()` mapping will need adjustment during Phase 7 verification.

---

## Unresolved Questions

- [ ] **Exact Dataverse table field names**: The design uses inferred field names (e.g., `sprk_analysisskill.sprk_category`). Verify actual field names against the dev environment before implementing scope store queries — Blocks: Phase 3 (scope resolution)
- [ ] **N:N relationship table names**: Verify `sprk_playbooknode_analysisskill` and `sprk_playbooknode_aiknowledge` are the correct relationship table names — Blocks: Phase 3 (node sync)
- [ ] **Wait node executor**: No executor exists yet (`WaitNodeExecutor`). The config form can be built, but execution will require a BFF-side executor to be implemented separately — Blocks: Full end-to-end for Wait nodes only
- [ ] **AI Completion executor**: No executor exists yet (`AiCompletionNodeExecutor`). Same situation as Wait — config form can be built but execution requires separate BFF work — Blocks: Full end-to-end for AI Completion nodes only

---

*AI-optimized specification. Original design: `projects/ai-playbook-node-builder-r5/design.md`*
