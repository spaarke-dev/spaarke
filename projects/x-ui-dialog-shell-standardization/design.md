# UI Dialog & Shell Standardization

## Executive Summary

Standardize all create/wizard dialogs and the Playbook Library into standalone, independently deployable Code Page web resources. Currently, the Corporate Workspace (`sprk_corporateworkspace`) embeds 11 lazy-loaded React dialogs directly in its bundle — creating a monolithic component that can't be reused from entity main forms, ribbon command bars, or the Power Pages external SPA. This project extracts each wizard into its own Code Page and introduces a new `PlaybookLibraryShell` for reusable playbook browsing/execution.

---

## Problem Statement

### Current Architecture

The Corporate Workspace Code Page (`corporateworkspace.html`) is a React 19 SPA that internally manages **11 lazy-loaded dialog components** via `WorkspaceGrid.tsx` (~835 LOC). Each dialog is rendered as an inline Fluent UI v9 `<Dialog>` overlay within the workspace's React tree.

**Embedded dialogs currently in WorkspaceGrid.tsx:**

| Dialog | Component | Pattern |
|--------|-----------|---------|
| Create Matter | `LazyWizardDialog` | CreateRecordWizard wrapper |
| Create Project | `LazyProjectWizardDialog` | CreateRecordWizard wrapper |
| Create Event | `LazyEventWizardDialog` | CreateRecordWizard wrapper |
| Create To Do | `LazyTodoWizardDialog` | CreateRecordWizard wrapper |
| Create Work Assignment | `LazyWorkAssignmentWizardDialog` | WizardShell direct |
| Summarize Files | `LazySummarizeFilesDialog` | WizardShell direct |
| Find Similar | `LazyFindSimilarDialog` | Custom dialog |
| Quick Start | `LazyQuickStartWizardDialog` | Config-driven WizardShell |
| Get Started Expand | `LazyGetStartedExpandDialog` | Grid dialog |
| Quick Summary Dashboard | `LazyQuickSummaryDashboardDialog` | Dashboard dialog |
| Close Project | `LazyCloseProjectDialog` | Confirmation dialog |

**One wizard already exists as a standalone Code Page:**
- `DocumentUploadWizard` (`sprk_DocumentUploadWizard`) — opened via `Xrm.Navigation.navigateTo`, gets Dataverse modal chrome (title bar, expand button, close)

### Problems

1. **No reusability from entity forms**: Create Matter wizard cannot be launched from the Matter main form `+ New` command bar — it only exists inside the Corporate Workspace bundle.

2. **Inconsistent dialog chrome**: Create Matter renders as a borderless Fluent `<Dialog>` (no expand button, no platform title bar). Document Upload renders in Dataverse modal chrome (expand button, title bar). Users see two different dialog experiences.

3. **Monolithic bundle**: All wizard code ships in the Corporate Workspace bundle even when only the workspace grid is needed. Each wizard adds to bundle size and initial parse time.

4. **No Power Pages reuse**: The Power Pages external SPA (`src/client/external-spa/`) cannot use these wizards — they're embedded in a Dataverse-specific Code Page, not importable as library components.

5. **WorkspaceGrid complexity**: Managing 11 dialog open/close states, 11 lazy imports, and cross-dialog refetch callbacks in a single 835-line component.

6. **Playbook Library not portable**: The playbook browsing/selection UI (`PlaybookCardGrid`, `ScopeConfigurator`) lives inside the Corporate Workspace and cannot be embedded in other contexts (entity forms, SPA, standalone page).

---

## Proposed Solution

### Three-Layer Architecture

```
Layer 1: @spaarke/ui-components (shared library — SINGLE SOURCE OF TRUTH)
├── WizardShell                    — generic wizard frame (steps, nav, success screen)
├── CreateRecordWizard             — record-creation boilerplate (file upload, follow-on steps)
├── PlaybookLibraryShell (NEW)     — playbook browsing, selection, execution shell
├── CreateMatterWizard (MOVE)      — matter-specific wizard content
├── CreateProjectWizard (MOVE)     — project-specific wizard content
├── CreateEventWizard (MOVE)       — event-specific wizard content
├── CreateTodoWizard (MOVE)        — todo-specific wizard content
├── CreateWorkAssignmentWizard (MOVE) — work assignment wizard content
├── DocumentUploadWizard (MOVE)    — document upload wizard content
├── SummarizeFilesWizard (MOVE)    — summarize files wizard content
└── FindSimilarDialog (MOVE)       — semantic search dialog content

Layer 2a: Code Page wrappers (for Dataverse model-driven apps)
├── sprk_creatematterwizard/main.tsx       (~30-50 LOC each)
├── sprk_createprojectwizard/main.tsx
├── sprk_createeventwizard/main.tsx
├── sprk_createtodowizard/main.tsx
├── sprk_createworkassignmentwizard/main.tsx
├── sprk_documentuploadwizard/main.tsx     (already exists — refactor to use shared)
├── sprk_summarizefileswizard/main.tsx
├── sprk_findsimilar/main.tsx
├── sprk_playbooklibrary/main.tsx          (NEW)
└── sprk_corporateworkspace/               (SIMPLIFIED — calls navigateTo instead of inline)

Layer 2b: Power Pages SPA integration
└── Imports same components from @spaarke/ui-components
    └── Renders in SPA's own Dialog/modal (no Dataverse chrome)
    └── Same wizard UX, different hosting context
```

### Key Design Decisions

#### D1: Wizards as shared library components, not solution-specific code

All wizard logic (steps, services, form state, validation) moves to `@spaarke/ui-components`. Code Page wrappers become thin entry points (~30-50 LOC) that:
- Parse URL/data parameters
- Initialize auth (`@spaarke/auth`)
- Detect theme
- Render the shared wizard component with `embedded={true}` (WizardShell skips its own Dialog wrapper since Dataverse provides the chrome)

**Benefit**: Update wizard logic once in the shared library → every consumer (Code Page, Corporate Workspace, Power Pages SPA) gets the update on next build.

#### D2: Corporate Workspace calls `navigateTo` instead of rendering inline

Instead of 11 lazy-loaded `<Dialog>` overlays, WorkspaceGrid calls `Xrm.Navigation.navigateTo` to open each wizard as a separate Code Page in a Dataverse modal. This:
- Eliminates 11 `React.lazy` imports and 11 open/close state variables
- Reduces WorkspaceGrid from ~835 LOC to ~400-500 LOC
- Ensures consistent Dataverse modal chrome across all wizards

**Trade-off**: Wizards take slightly longer to open (separate bundle download vs. instant lazy-load). Mitigated by small bundle sizes (~30-80KB per wizard) and browser caching.

**Callback pattern**: When a wizard completes (e.g., matter created), the Corporate Workspace needs to refresh its data grids. Options:
- `navigateTo` returns a Promise that resolves when the dialog closes — use the resolution to trigger refetch
- `BroadcastChannel` API for cross-tab communication if needed

#### D3: PlaybookLibraryShell — a new reusable shell

The current playbook UI is split across `Playbook/PlaybookCardGrid.tsx`, `QuickStart/QuickStartWizardDialog.tsx`, and `GetStarted/ActionCardHandlers.ts`. These are tightly coupled to the Corporate Workspace.

**PlaybookLibraryShell** will be a new shared component (analogous to WizardShell but for playbook browsing/execution) that:
- Renders a browsable grid of available playbooks (filtered by context)
- Supports playbook selection → scope configuration → execution
- Can be embedded in different contexts with different configurations:
  - **Corporate Workspace**: Full library with all playbooks, launched from Get Started cards
  - **Entity main forms**: Filtered to entity-relevant playbooks, launched from command bar
  - **Power Pages SPA**: Subset of playbooks available to external users
  - **Standalone Code Page**: Full playbook library as its own page (`sprk_playbooklibrary`)

**Configuration interface** (conceptual):
```typescript
interface IPlaybookLibraryShellProps {
  // Context filtering
  entityType?: string;           // Filter playbooks by entity context
  entityId?: string;             // Pre-select entity for scope
  allowedPlaybookIds?: string[]; // Restrict to specific playbooks

  // Display mode
  mode: "grid" | "list" | "compact";
  embedded?: boolean;            // Skip Dialog wrapper when in Dataverse chrome

  // Callbacks
  onPlaybookComplete?: (result: IPlaybookResult) => void;
  onClose?: () => void;

  // Services (dependency injection for portability)
  playbookService: IPlaybookService;
  authContext: IAuthContext;
}
```

#### D4: Webresource display names

Each Code Page webresource gets a clean display name (shown in Dataverse modal title bar):

| Webresource Name | Display Name |
|------------------|-------------|
| `sprk_creatematterwizard` | Create New Matter |
| `sprk_createprojectwizard` | Create New Project |
| `sprk_createeventwizard` | Create New Event |
| `sprk_createtodowizard` | Create New To Do |
| `sprk_createworkassignmentwizard` | Create Work Assignment |
| `sprk_documentuploadwizard` | Upload Documents |
| `sprk_summarizefileswizard` | Summarize Files |
| `sprk_findsimilar` | Find Similar Documents |
| `sprk_playbooklibrary` | Playbook Library |

#### D5: Build tooling standardization

All new Code Pages use **Vite** with `vite-plugin-singlefile` (consistent with LegalWorkspace, EventsPage, SpeAdminApp). The existing DocumentUploadWizard (currently Webpack) should be migrated to Vite for consistency.

#### D6: Command bar / ribbon integration

Each extracted wizard can be launched from entity main form command bars:

| Entity Form | Command Button | Opens |
|-------------|---------------|-------|
| `sprk_matter` main form | `+ New` or `Create Matter` | `sprk_creatematterwizard` |
| `sprk_matter` main form | `Upload Document` | `sprk_documentuploadwizard` |
| `sprk_matter` main form | `Run Playbook` | `sprk_playbooklibrary` (filtered to matter) |
| `sprk_project` main form | `+ New` or `Create Project` | `sprk_createprojectwizard` |
| `sprk_event` main form | `Create Event` | `sprk_createeventwizard` |
| `sprk_event` main form | `Create To Do` | `sprk_createtodowizard` |

Each ribbon button calls a JS webresource function that invokes `navigateTo`.

---

## Scope

### In Scope

#### Part A: Extract Create Wizards to Shared Library

Move wizard components from `src/solutions/LegalWorkspace/src/components/` to `@spaarke/ui-components`:

| Wizard | Current Location | Files to Move |
|--------|------------------|---------------|
| CreateMatter | `LegalWorkspace/components/CreateMatter/` | 15 files (WizardDialog, CreateRecordStep, services, types, sub-steps) |
| CreateProject | `LegalWorkspace/components/CreateProject/` | 7 files (ProjectWizardDialog, CreateProjectStep, services, types, provisioning) |
| CreateEvent | `LegalWorkspace/components/CreateEvent/` | 4 files (EventWizardDialog, CreateEventStep, service, types) |
| CreateTodo | `LegalWorkspace/components/CreateTodo/` | 4 files (TodoWizardDialog, CreateTodoStep, service, types) |
| CreateWorkAssignment | `LegalWorkspace/components/CreateWorkAssignment/` | 9 files (orchestrator, steps, service, types) |
| SummarizeFiles | `LegalWorkspace/components/SummarizeFiles/` | 8 files (dialog, steps, service) |
| FindSimilar | `LegalWorkspace/components/FindSimilar/` | 4 files (dialog, results, service, types) |

Also refactor `DocumentUploadWizard` (`src/solutions/DocumentUploadWizard/`) to consume shared library components.

#### Part B: Create Code Page Wrappers

Create new solution folders under `src/solutions/` for each wizard Code Page:

- `src/solutions/CreateMatterWizard/` — Vite build → `sprk_creatematterwizard`
- `src/solutions/CreateProjectWizard/` — Vite build → `sprk_createprojectwizard`
- `src/solutions/CreateEventWizard/` — Vite build → `sprk_createeventwizard`
- `src/solutions/CreateTodoWizard/` — Vite build → `sprk_createtodowizard`
- `src/solutions/CreateWorkAssignmentWizard/` — Vite build → `sprk_createworkassignmentwizard`
- `src/solutions/SummarizeFilesWizard/` — Vite build → `sprk_summarizefileswizard`
- `src/solutions/FindSimilarDialog/` — Vite build → `sprk_findsimilar`

Each wrapper follows the same thin pattern:
```typescript
// main.tsx (~30-50 LOC)
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { CreateMatterWizard } from "@spaarke/ui-components";
import { detectTheme, parseDataParams } from "@spaarke/ui-components/utils";

const params = parseDataParams();    // parse Xrm data envelope
const theme = detectTheme();         // detect Dataverse theme
const root = createRoot(document.getElementById("root")!);

root.render(
  <FluentProvider theme={theme}>
    <CreateMatterWizard
      embedded={true}                // skip Fluent Dialog wrapper
      {...params}                    // entity context, pre-fill data
      onClose={() => window.close()} // close Dataverse modal
    />
  </FluentProvider>
);
```

#### Part C: PlaybookLibraryShell

Create a new shared shell component in `@spaarke/ui-components`:

1. **PlaybookLibraryShell** — browsable playbook grid with filtering, selection, scope configuration, and execution launch
2. Extract relevant components from `LegalWorkspace/components/Playbook/` and `QuickStart/`
3. Create Code Page wrapper: `src/solutions/PlaybookLibrary/` → `sprk_playbooklibrary`
4. Design configuration interface for multi-context use (workspace, entity form, SPA)

#### Part D: Restructure Corporate Workspace

Simplify `WorkspaceGrid.tsx`:
1. Remove all 11 lazy-loaded dialog imports
2. Remove all dialog open/close state management
3. Replace with `navigateTo` calls to open each wizard Code Page
4. Handle post-dialog data refresh via `navigateTo` Promise resolution
5. Replace inline PlaybookCardGrid with `navigateTo` to `sprk_playbooklibrary` (or embed PlaybookLibraryShell directly depending on UX needs)
6. Target: reduce WorkspaceGrid from ~835 LOC to ~400-500 LOC

#### Part E: Power Pages SPA Integration

Update `src/client/external-spa/` to import wizard components from `@spaarke/ui-components`:
1. Document Upload wizard — render in SPA's own Dialog (no Dataverse chrome)
2. PlaybookLibraryShell — embed with external-user-appropriate playbook filter
3. Any other wizards relevant to external users

#### Part F: Ribbon / Command Bar Wiring

1. Create JS webresource for wizard launch functions (`sprk_wizard_commands.js`)
2. Add RibbonDiffXml entries for entity forms that need `+ New` or wizard launch buttons
3. Configure `navigateTo` calls with appropriate entity context parameters

---

## Out of Scope

- QuickSummaryDashboard and GetStartedExpandDialog — these are workspace-specific UI (not reusable elsewhere), remain inline in Corporate Workspace
- CloseProjectDialog — simple confirmation, remains inline
- Smart To Do / Kanban — already has its own rendering mode via URL params; separate concern
- Activity Feed — workspace-specific, not a dialog
- Notification Panel — workspace-specific
- New wizard creation — this project extracts/standardizes existing wizards only

---

## Technical Approach

### Shared Library Structure (Post-Migration)

```
src/client/shared/Spaarke.UI.Components/src/components/
├── Wizard/
│   ├── WizardShell.tsx              (existing — no changes)
│   ├── WizardStepper.tsx            (existing)
│   ├── WizardSuccessScreen.tsx      (existing)
│   ├── wizardShellTypes.ts          (existing)
│   └── wizardShellReducer.ts        (existing)
│
├── CreateRecordWizard/
│   ├── CreateRecordWizard.tsx       (existing — no changes)
│   └── types.ts                     (existing)
│
├── PlaybookLibraryShell/            (NEW)
│   ├── PlaybookLibraryShell.tsx     — main shell component
│   ├── PlaybookGrid.tsx             — extracted from PlaybookCardGrid
│   ├── PlaybookCard.tsx             — individual card
│   ├── ScopeConfigurator.tsx        — extracted from current Playbook/
│   ├── PlaybookExecutionView.tsx    — execution progress + results
│   ├── playbookLibraryTypes.ts      — config interface, playbook metadata
│   └── index.ts
│
├── CreateMatterWizard/              (MOVED from LegalWorkspace)
│   ├── CreateMatterWizard.tsx       — main component (wraps CreateRecordWizard)
│   ├── MatterInfoStep.tsx           — entity form step
│   ├── matterService.ts
│   └── types.ts
│
├── CreateProjectWizard/             (MOVED)
│   ├── CreateProjectWizard.tsx
│   ├── ProjectInfoStep.tsx
│   ├── ProvisioningProgressStep.tsx
│   ├── SecureProjectSection.tsx
│   ├── projectService.ts
│   └── types.ts
│
├── CreateEventWizard/               (MOVED)
│   ├── CreateEventWizard.tsx
│   ├── EventInfoStep.tsx
│   ├── eventService.ts
│   └── types.ts
│
├── CreateTodoWizard/                (MOVED)
│   ├── CreateTodoWizard.tsx
│   ├── TodoInfoStep.tsx
│   ├── todoService.ts
│   └── types.ts
│
├── CreateWorkAssignmentWizard/      (MOVED)
│   ├── CreateWorkAssignmentWizard.tsx
│   ├── SelectWorkStep.tsx
│   ├── AddFilesStep.tsx
│   ├── EnterInfoStep.tsx
│   ├── NextStepsSelectionStep.tsx
│   ├── AssignWorkStep.tsx
│   ├── CreateFollowOnEventStep.tsx
│   ├── workAssignmentService.ts
│   └── types.ts
│
├── DocumentUploadWizard/            (REFACTORED — shared content)
│   ├── DocumentUploadWizard.tsx
│   ├── AssociateToStep.tsx
│   ├── UploadProcessingStep.tsx
│   ├── uploadService.ts
│   └── types.ts
│
├── SummarizeFilesWizard/            (MOVED)
│   ├── SummarizeFilesWizard.tsx
│   ├── steps...
│   └── summarizeService.ts
│
└── FindSimilarDialog/               (MOVED)
    ├── FindSimilarDialog.tsx
    ├── FindSimilarResultsStep.tsx
    ├── findSimilarService.ts
    └── types.ts
```

### Service Abstraction for Portability (ADR-012 IDataService Pattern)

Wizard services currently call Dataverse WebApi directly (e.g., `webApi.createRecord`). Per the revised ADR-012 service portability model, services in the shared library must use abstracted dependencies — never direct platform API calls.

The `IDataService`, `IUploadService`, and `INavigationService` interfaces (defined in ADR-012) provide the abstraction:

```typescript
// Defined in @spaarke/ui-components/types (per ADR-012)
interface IDataService {
  createRecord(entityName: string, data: Record<string, unknown>): Promise<string>;
  retrieveRecord(entityName: string, id: string, options?: string): Promise<Record<string, unknown>>;
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
  updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<void>;
}
```

Each wizard accepts `dataService: IDataService` as a prop. Code Page wrappers pass the Xrm.WebApi adapter. SPA passes the BFF API adapter. Tests pass mock adapters.

### Dialog Close / Refresh Pattern

When Corporate Workspace opens a wizard via `navigateTo`, it needs to know when to refresh:

```typescript
// navigateTo returns a Promise that resolves when dialog closes
const result = await Xrm.Navigation.navigateTo(
  { pageType: "webresource", webresourceName: "sprk_creatematterwizard", data: encodedParams },
  { target: 2, width: { value: 70, unit: "%" }, height: { value: 70, unit: "%" } }
);
// Dialog closed — refresh the matters grid
await refetchMatters();
```

For richer communication (e.g., passing back the created record ID), use `window.opener.postMessage` or `BroadcastChannel`.

### Theme Detection (Shared Utility)

Extract the existing theme detection logic into a shared utility in `@spaarke/ui-components`:

```typescript
// @spaarke/ui-components/utils/detectTheme.ts
export function detectTheme(): Theme {
  // 1. Check URL param (?theme=dark)
  // 2. Check Xrm.Utility.getGlobalContext (Dataverse)
  // 3. Check parent frame luminance
  // 4. Fallback to webLightTheme
}
```

All Code Page `main.tsx` files use this shared utility instead of duplicating detection logic.

### URL Parameter Parsing (Shared Utility)

Standardize how Code Pages receive context from `navigateTo`:

```typescript
// @spaarke/ui-components/utils/parseDataParams.ts
export function parseDataParams(): Record<string, string> {
  // Xrm.Navigation.navigateTo passes params as: ?data=key1%3Dvalue1%26key2%3Dvalue2
  const urlParams = new URLSearchParams(window.location.search);
  const dataEnvelope = urlParams.get("data") || "";
  return Object.fromEntries(new URLSearchParams(dataEnvelope));
}
```

---

## Success Criteria

1. All create wizards launchable from both Corporate Workspace AND entity main form command bars
2. Consistent Dataverse modal chrome (title bar, expand button) across all wizard dialogs
3. Single source of truth for wizard logic in `@spaarke/ui-components`
4. PlaybookLibraryShell usable in Corporate Workspace, entity forms, and Power Pages SPA
5. Corporate Workspace WorkspaceGrid.tsx reduced to <500 LOC
6. Document Upload wizard migrated from Webpack to Vite
7. Power Pages SPA can render Upload Documents and PlaybookLibrary using shared components
8. Clean webresource display names in Dataverse modal title bars

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Wizard open latency (separate bundle download) | Users notice delay on first open | Small bundles (<80KB), browser caching, preload hints |
| Cross-dialog state loss (no shared React tree) | Refetch data after wizard close may flash | `navigateTo` Promise + targeted refetch (not full page reload) |
| Service abstraction complexity | Over-engineering for SPA portability | Keep `IDataService` minimal (4 methods); implement adapters only when SPA integration begins |
| Bundle duplication (shared library in each Code Page) | Increased total download if user opens multiple wizards | `vite-plugin-singlefile` inlines everything; each page is self-contained and cached independently |
| Playbook Library Shell scope creep | Trying to make it too configurable | Start with Corporate Workspace + entity form use cases only; add SPA config later |

---

## Dependencies

- `@spaarke/ui-components` — shared component library (already exists, will be extended)
- `@spaarke/auth` — authentication for BFF API calls from Code Pages (already exists)
- Vite + `vite-plugin-singlefile` — build tooling (already used by most solutions)
- Dataverse webresource deployment — via `pac solution` or deploy scripts
- Ribbon/command bar customization — via RibbonDiffXml

## Governing ADRs

| ADR | Relevance to This Project |
|-----|---------------------------|
| **ADR-006** (UI Surface Architecture) | Code Pages are the default for all new UI. Wizards extracted as standalone Code Pages, not inline PCF dialogs. |
| **ADR-012** (Shared Component Library) | All wizard content moves to `@spaarke/ui-components`. Services use `IDataService` abstraction for portability across Dataverse and Power Pages SPA. |
| **ADR-021** (Fluent UI v9 Design System) | All UI uses Fluent v9 tokens. React 19 for Code Pages. |
| **ADR-022** (PCF Platform Libraries) | PCF controls remain on React 16/17. Shared library `peerDependencies: ">=16.14.0"`. |
| **ADR-026** (Code Page Build Standard) | All new Code Page wrappers use Vite + `vite-plugin-singlefile`. DocumentUploadWizard migrates from webpack to Vite. |

---

## Phasing Recommendation

| Phase | Scope | Rationale |
|-------|-------|-----------|
| **Phase 1** | Extract CreateMatter + CreateProject to shared library + Code Pages. Restructure Corporate Workspace to use `navigateTo` for these two. | Highest reuse value (most requested from entity forms). Validates the pattern. |
| **Phase 2** | Extract remaining wizards (Event, Todo, WorkAssignment, SummarizeFiles, FindSimilar). Migrate DocumentUploadWizard to Vite. | Complete wizard extraction. |
| **Phase 3** | Build PlaybookLibraryShell. Create `sprk_playbooklibrary` Code Page. Integrate into Corporate Workspace and entity forms. | New shared component, benefits from validated pattern. |
| **Phase 4** | Power Pages SPA integration. Service abstraction layer (`IDataService`). | Extends to external users. |
| **Phase 5** | Ribbon/command bar wiring for all entity forms. | Broadest reach — all forms can launch wizards. |
