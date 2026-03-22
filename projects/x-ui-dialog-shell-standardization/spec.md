# UI Dialog & Shell Standardization - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-19
> **Source**: design.md (owner discussion + design document)

## Executive Summary

Extract all create/wizard dialogs and the playbook browsing UI from the Corporate Workspace into standalone, independently deployable Code Page web resources backed by shared library components. Introduce `PlaybookLibraryShell` as a reusable shared component that replaces/absorbs AnalysisBuilder's core logic. Restructure the Corporate Workspace to use `navigateTo` calls instead of inline dialogs. Enable reuse of all wizard and playbook components from entity main form command bars, the Power Pages external SPA, and any future UI surface.

## Scope

### In Scope

**Part A: Service Abstraction Layer**
- Define `IDataService`, `IUploadService`, `INavigationService` interfaces in `@spaarke/ui-components/types` (per ADR-012 service portability model)
- Create Xrm.WebApi adapter implementation (for Code Page consumers)
- Create mock adapter (for unit tests)

**Part B: Extract Create Wizards to Shared Library**
- Move 7 wizard component sets from `src/solutions/LegalWorkspace/src/components/` to `@spaarke/ui-components/components/`:
  - CreateMatter (15 files)
  - CreateProject (7 files + provisioning)
  - CreateEvent (4 files)
  - CreateTodo (4 files)
  - CreateWorkAssignment (9 files)
  - SummarizeFiles (8 files)
  - FindSimilar (4 files)
- Refactor wizard services to accept `IDataService` abstraction instead of direct `webApi` calls
- Refactor DocumentUploadWizard (`src/solutions/DocumentUploadWizard/`) to consume shared library components

**Part C: Create Code Page Wrappers**
- Create new Vite-based solution folders under `src/solutions/` for each wizard:
  - `CreateMatterWizard/` → `sprk_creatematterwizard`
  - `CreateProjectWizard/` → `sprk_createprojectwizard`
  - `CreateEventWizard/` → `sprk_createeventwizard`
  - `CreateTodoWizard/` → `sprk_createtodowizard`
  - `CreateWorkAssignmentWizard/` → `sprk_createworkassignmentwizard`
  - `SummarizeFilesWizard/` → `sprk_summarizefileswizard`
  - `FindSimilarDialog/` → `sprk_findsimilar`
- Each wrapper is ~30-50 LOC: parse params, init auth, detect theme, render shared component with `embedded={true}`
- Migrate DocumentUploadWizard from webpack to Vite

**Part D: PlaybookLibraryShell**
- Extract the core logic from AnalysisBuilder `App.tsx` (~508 LOC) into a new shared component `PlaybookLibraryShell` in `@spaarke/ui-components`
- PlaybookLibraryShell provides: playbook browsing (card grid), scope configuration, custom scope tab, execution launch
- Absorb QuickStart intent handling — pre-selected playbook + locked scope via props (no separate QuickStartShell; uses WizardShell for step flow)
- Refactor existing `sprk_analysisbuilder` Code Page to be a thin wrapper around PlaybookLibraryShell
- Create `sprk_playbooklibrary` Code Page wrapper (or unify with `sprk_analysisbuilder` via URL params if functionally equivalent)
- Extract shared Playbook components already in `@spaarke/ui-components/components/Playbook/` — these stay; PlaybookLibraryShell composes them into a complete shell

**Part E: Restructure Corporate Workspace**
- Remove lazy-loaded wizard dialog imports and open/close state management from WorkspaceGrid.tsx
- Replace with `navigateTo` calls to open each wizard Code Page in Dataverse modal
- Handle post-dialog data refresh via `navigateTo` Promise resolution
- Get Started action cards open their targets directly via `navigateTo`
- PlaybookLibraryShell: the 4 playbook cards in Get Started remain inline in the workspace page and open their respective playbook intents directly
- Target: reduce WorkspaceGrid from ~835 LOC to ~400-500 LOC

**Part F: Shared Utilities**
- Extract `detectTheme()` into `@spaarke/ui-components/utils/themeDetection.ts` (consolidate existing detection logic)
- Extract `parseDataParams()` into `@spaarke/ui-components/utils/parseDataParams.ts` (standardize URL param parsing for all Code Pages)

**Part G: Power Pages SPA Integration**
- Update `src/client/external-spa/` to import wizard components from `@spaarke/ui-components`
- Document Upload wizard — render in SPA's own Dialog (no Dataverse chrome)
- PlaybookLibraryShell — embed with external-user-appropriate playbook filter
- Create BFF API adapter for `IDataService` (SPA context)

**Part H: Ribbon / Command Bar Wiring**
- Create JS webresource `sprk_wizard_commands.js` with launch functions
- Add RibbonDiffXml entries for entity forms with `+ New` / wizard launch buttons
- Configure `navigateTo` calls with entity context parameters

### Out of Scope

- QuickSummaryDashboard — workspace-specific, not reusable elsewhere, remains inline
- GetStartedExpandDialog — workspace-specific grid dialog, remains inline
- CloseProjectDialog — simple confirmation, remains inline
- Smart To Do / Kanban — already has its own rendering mode via URL params; separate concern
- Activity Feed, Notification Panel — workspace-specific, not dialogs
- New wizard creation — this project extracts/standardizes existing wizards only
- New playbook creation — PlaybookLibraryShell browses/executes playbooks, doesn't create them (that's PlaybookBuilder)

### Affected Areas

- `src/client/shared/Spaarke.UI.Components/src/components/` — new wizard components, PlaybookLibraryShell, service interfaces
- `src/client/shared/Spaarke.UI.Components/src/types/` — IDataService, IUploadService, INavigationService
- `src/client/shared/Spaarke.UI.Components/src/utils/` — detectTheme, parseDataParams
- `src/solutions/LegalWorkspace/src/components/` — remove wizard components, simplify WorkspaceGrid
- `src/solutions/LegalWorkspace/src/components/GetStarted/` — update action card handlers to use navigateTo
- `src/solutions/LegalWorkspace/src/components/QuickStart/` — refactor to use PlaybookLibraryShell with intent pre-selection
- `src/solutions/DocumentUploadWizard/` — refactor to shared library; migrate webpack → Vite
- `src/solutions/AnalysisBuilder/` — refactor App.tsx to thin wrapper around PlaybookLibraryShell
- `src/solutions/CreateMatterWizard/` — new (Code Page wrapper)
- `src/solutions/CreateProjectWizard/` — new (Code Page wrapper)
- `src/solutions/CreateEventWizard/` — new (Code Page wrapper)
- `src/solutions/CreateTodoWizard/` — new (Code Page wrapper)
- `src/solutions/CreateWorkAssignmentWizard/` — new (Code Page wrapper)
- `src/solutions/SummarizeFilesWizard/` — new (Code Page wrapper)
- `src/solutions/FindSimilarDialog/` — new (Code Page wrapper)
- `src/solutions/PlaybookLibrary/` — new (Code Page wrapper, or unified with AnalysisBuilder)
- `src/client/external-spa/` — SPA integration with shared wizard components
- `infrastructure/dataverse/ribbon/` — RibbonDiffXml for entity form command bars

## Requirements

### Functional Requirements

1. **FR-01**: All create wizards (Matter, Project, Event, Todo, Work Assignment) launchable from both Corporate Workspace AND entity main form `+ New` command bars — Acceptance: navigateTo opens wizard from matter/project/event main forms
2. **FR-02**: SummarizeFiles and FindSimilar dialogs launchable as standalone Code Pages from ribbon buttons — Acceptance: can open from document subgrid command bar
3. **FR-03**: Document Upload wizard renders identically whether opened from Corporate Workspace or entity form — Acceptance: same step flow, same UI, same file handling
4. **FR-04**: PlaybookLibraryShell provides playbook browsing, scope configuration, and execution launch — Acceptance: replaces AnalysisBuilder App.tsx functionality with equivalent UX
5. **FR-05**: PlaybookLibraryShell supports intent-based pre-selection (replaces QuickStart patterns) — Acceptance: `intent="email-compose"` pre-selects correct playbook and locks scope
6. **FR-06**: Corporate Workspace WorkspaceGrid.tsx uses `navigateTo` for all wizard dialogs — Acceptance: no inline `<Dialog>` overlays for extractable wizards; WorkspaceGrid < 500 LOC
7. **FR-07**: Post-dialog data refresh works correctly — Acceptance: after creating a matter via navigateTo, the matters grid refreshes automatically
8. **FR-08**: All wizard Code Pages display clean titles in Dataverse modal chrome — Acceptance: "Create New Matter", "Upload Documents", etc. (not technical webresource names)
9. **FR-09**: Power Pages SPA can render Document Upload and PlaybookLibrary using shared components — Acceptance: same wizard content renders in SPA dialog without Dataverse chrome
10. **FR-10**: Get Started action cards in Corporate Workspace open their targets directly — Acceptance: clicking "Create Matter" card opens `sprk_creatematterwizard` via navigateTo

### Non-Functional Requirements

- **NFR-01**: Wizard Code Page bundle size < 100KB each (excluding shared library) — ensures fast first-open
- **NFR-02**: All Code Pages built with Vite + `vite-plugin-singlefile` — no webpack (ADR-026)
- **NFR-03**: All Code Pages use React 19 — `"react": "^19.0.0"` in package.json (ADR-021)
- **NFR-04**: Shared library maintains `peerDependencies: "react": ">=16.14.0"` — PCF compatibility (ADR-022)
- **NFR-05**: No direct `Xrm.WebApi` calls in shared library components — all through `IDataService` abstraction (ADR-012)
- **NFR-06**: All UI uses Fluent v9 tokens exclusively — no hard-coded colors (ADR-021)
- **NFR-07**: Dark mode support across all wizard Code Pages — theme detection via shared utility

## Technical Constraints

### Applicable ADRs

- **ADR-006** (UI Surface Architecture): Code Pages are default for all new UI. Wizards are standalone Code Pages, not inline PCF dialogs.
- **ADR-012** (Shared Component Library): All wizard content in `@spaarke/ui-components`. Services use `IDataService` abstraction. Service portability tiers: pure logic and abstracted I/O belong in shared library; platform-bound code stays in consumers.
- **ADR-021** (Fluent UI v9 Design System): All UI uses Fluent v9 tokens. React 19 for Code Pages.
- **ADR-022** (PCF Platform Libraries): PCF controls remain on React 16/17. Shared library peerDependencies >= 16.14.0.
- **ADR-026** (Code Page Build Standard): Vite + `vite-plugin-singlefile` for all Code Pages. Single HTML file output.

### MUST Rules

- MUST use `IDataService` abstraction for all data access in shared wizard components
- MUST use Vite + `vite-plugin-singlefile` for all new Code Page wrappers
- MUST use React 19 (`createRoot`) in all Code Page entry points
- MUST use `embedded={true}` on WizardShell when rendering inside Dataverse modal chrome
- MUST use Fluent v9 semantic tokens for all styling (no hard-coded colors)
- MUST set clean webresource display names in Dataverse (e.g., "Create New Matter")
- MUST handle post-dialog refresh via `navigateTo` Promise resolution

### MUST NOT Rules

- MUST NOT call `Xrm.WebApi` directly from shared library components
- MUST NOT use webpack for new Code Pages (migrate existing webpack-based ones to Vite)
- MUST NOT create a separate QuickStartShell — use PlaybookLibraryShell with intent pre-selection + WizardShell for step flow
- MUST NOT bundle React in PCF controls (platform-provided)
- MUST NOT hard-code entity names as string literals in shared library (use configurable entity maps)

### Existing Patterns to Follow

- `src/solutions/EventsPage/` — canonical Vite Code Page reference
- `src/solutions/DocumentUploadWizard/` — existing standalone wizard Code Page (reference for navigateTo pattern, but migrate from webpack to Vite)
- `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx` — shared wizard shell (no changes needed)
- `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/` — shared record creation boilerplate
- `src/solutions/AnalysisBuilder/src/App.tsx` — current PlaybookLibraryShell logic to extract (~508 LOC)
- `src/client/shared/Spaarke.UI.Components/src/components/Playbook/` — existing shared playbook card grid and scope configurator

## Success Criteria

1. [ ] All 7 create/tool wizards launchable from both Corporate Workspace and entity main form command bars — Verify: test navigateTo from matter, project, event forms
2. [ ] Consistent Dataverse modal chrome (title bar, expand button) across all wizard dialogs — Verify: visual comparison of all 9 wizard dialogs
3. [ ] Single source of truth for wizard logic in `@spaarke/ui-components` — Verify: no wizard step components remain in `src/solutions/LegalWorkspace/src/components/Create*/`
4. [ ] PlaybookLibraryShell replaces AnalysisBuilder App.tsx core logic — Verify: AnalysisBuilder is thin wrapper; PlaybookLibraryShell works in workspace, entity forms, and standalone
5. [ ] Corporate Workspace WorkspaceGrid.tsx < 500 LOC — Verify: line count after restructuring
6. [ ] DocumentUploadWizard migrated from webpack to Vite — Verify: `vite.config.ts` exists, webpack config removed
7. [ ] Power Pages SPA can render Upload Documents and PlaybookLibrary using shared components — Verify: SPA renders wizard in own dialog, no Dataverse chrome
8. [ ] All services use `IDataService` abstraction — Verify: grep for direct `webApi.` calls in shared library returns zero results
9. [ ] Clean webresource display names in Dataverse — Verify: modal title bars show "Create New Matter" etc.

## Dependencies

### Prerequisites
- `@spaarke/ui-components` shared library (exists, will be extended)
- `@spaarke/auth` authentication library (exists)
- Vite + `vite-plugin-singlefile` build tooling (already used by most solutions)
- WizardShell and CreateRecordWizard (exist, no changes needed)

### External Dependencies
- Dataverse webresource deployment (via `pac solution` or deploy scripts)
- Ribbon/command bar customization (via RibbonDiffXml XML)
- Webresource display name configuration in Dataverse solution

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| QuickStart handling | Should QuickStart get its own shell or fold into existing patterns? | Standalone wizard Code Pages with PlaybookLibraryShell providing context. No separate QuickStartShell — rebuild/fold into WizardShell. Don't over-engineer "everything is a component." | QuickStart intents become PlaybookLibraryShell with pre-selected playbook + WizardShell for step flow. No new shell type. |
| IDataService timing | Introduce in Phase 1 or defer to Phase 4? | Phase 1 — it's a prerequisite per ADR-012. | IDataService interfaces and Xrm adapter built in Phase 1 alongside first wizard extraction. |
| PlaybookLibraryShell rendering in workspace | Open as modal or embed inline? | PlaybookLibraryShell is a feature for multiple contexts, not just workspace. Should replace/combine with AnalysisBuilder. The 4 Get Started cards open directly — those are part of the corporate page itself. | PlaybookLibraryShell = extracted AnalysisBuilder core. `sprk_analysisbuilder` becomes thin wrapper. Cards in workspace open targets directly via navigateTo. |
| AnalysisBuilder combination | Combine with PlaybookLibraryShell? | Yes — extract AnalysisBuilder App.tsx core into PlaybookLibraryShell. More efficient than building separately. | AnalysisBuilder (~508 LOC App.tsx) becomes the starting point for PlaybookLibraryShell extraction. |
| SummarizeFiles / FindSimilar | Wizard pattern or different? | Same pattern — standalone dialogs reusable from ribbon buttons on entity forms. | Treated identically to create wizards: shared library component + Code Page wrapper. |

## Assumptions

- **Dialog close communication**: `navigateTo` Promise resolution is sufficient for post-dialog refresh (no BroadcastChannel needed initially)
- **Bundle size**: Individual wizard Code Pages will be < 100KB; acceptable first-open latency
- **PlaybookLibraryShell and AnalysisBuilder unification**: Both can share the same underlying component; differences handled via props/URL params (not separate components)
- **SPA adapter**: BFF API adapter for `IDataService` will be built when SPA integration begins (Part G), not before
- **Existing tests**: Wizard components being moved will retain existing test coverage; new Code Page wrappers get basic render tests

## Unresolved Questions

- [ ] `sprk_analysisbuilder` vs `sprk_playbooklibrary` — should these be unified into a single webresource with different URL params, or remain separate? — Blocks: PlaybookLibraryShell Code Page wrapper decision
- [ ] Which entity forms get ribbon buttons in Phase 5? — Blocks: RibbonDiffXml scope (start with matter + project, expand later?)

## Phasing

| Phase | Parts | Scope | Rationale |
|-------|-------|-------|-----------|
| **Phase 1** | A, B (partial), C (partial), E (partial), F | IDataService interfaces + Xrm adapter. Extract CreateMatter + CreateProject to shared library + Code Pages. Update Corporate Workspace to use navigateTo for these two. Shared utilities (detectTheme, parseDataParams). | Highest reuse value. Validates the full pattern end-to-end. |
| **Phase 2** | B (remaining), C (remaining) | Extract remaining wizards (Event, Todo, WorkAssignment, SummarizeFiles, FindSimilar). Migrate DocumentUploadWizard to Vite. Update Corporate Workspace for all remaining dialogs. | Completes wizard extraction. |
| **Phase 3** | D | Build PlaybookLibraryShell from AnalysisBuilder core. Absorb QuickStart intents. Create Code Page wrapper. Refactor AnalysisBuilder to thin wrapper. Update Corporate Workspace playbook cards. | New shared component, benefits from validated pattern. |
| **Phase 4** | G | Power Pages SPA integration. BFF API adapter for IDataService. Document Upload + PlaybookLibrary in SPA. | Extends to external users. |
| **Phase 5** | H | Ribbon/command bar wiring for entity forms. RibbonDiffXml for matter, project, event forms. | Broadest reach — all forms can launch wizards. |

---

*AI-optimized specification. Original: design.md*
