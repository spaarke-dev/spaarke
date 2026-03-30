# Spaarke UX Management

> **Last Updated**: March 19, 2026
>
> **Purpose**: Documents the Spaarke UX architecture including the three-layer UI model, service abstractions, reusable dialog/wizard components, shell selection, Code Page wrappers, wizard command handlers, entity command bar configuration, theme persistence, and ribbon customizations.

---

## Table of Contents

1. [UI Architecture: Three-Layer Model](#ui-architecture-three-layer-model)
2. [Service Abstraction (IDataService Pattern)](#service-abstraction-idataservice-pattern)
3. [Shell Selection Decision Tree](#shell-selection-decision-tree)
4. [Reusable Dialog and Wizard Components](#reusable-dialog-and-wizard-components)
5. [WizardShell](#wizardshell)
6. [Code Page Wrappers](#code-page-wrappers)
7. [How to Create a New Dialog or Wizard](#how-to-create-a-new-dialog-or-wizard)
8. [Wizard Command Handlers (sprk_wizard_commands.js)](#wizard-command-handlers-sprk_wizard_commandsjs)
9. [Entity Command Bar Buttons](#entity-command-bar-buttons)
10. [Dark Mode Theme Persistence](#dark-mode-theme-persistence)
11. [Entity Ribbon Customizations](#entity-ribbon-customizations)
12. [Maintenance Procedures](#maintenance-procedures)
13. [WorkspaceShell Architecture](#workspaceshell-architecture)

---

## UI Architecture: Three-Layer Model

All Spaarke UI dialogs and wizards follow a three-layer architecture that separates shared components from deployment wrappers and consumers.

```
Layer 1: @spaarke/ui-components (shared library)
├── types/        → IDataService, IUploadService, INavigationService
├── utils/        → resolveCodePageTheme(), parseDataParams()
│   └── adapters/ → createXrmDataService(), createBffDataService(), etc.
├── components/   → WizardShell, CreateRecordWizard, PlaybookLibraryShell,
│                   CreateMatterWizard, CreateProjectWizard, CreateEventWizard,
│                   CreateTodoWizard, CreateWorkAssignmentWizard,
│                   SummarizeFilesWizard, FindSimilarDialog

Layer 2: Code Page wrappers (~30-50 LOC each)
├── src/solutions/CreateMatterWizard/         → sprk_creatematterwizard
├── src/solutions/CreateProjectWizard/        → sprk_createprojectwizard
├── src/solutions/CreateEventWizard/          → sprk_createeventwizard
├── src/solutions/CreateTodoWizard/           → sprk_createtodowizard
├── src/solutions/CreateWorkAssignmentWizard/ → sprk_createworkassignmentwizard
├── src/solutions/DocumentUploadWizard/       → sprk_documentuploadwizard
├── src/solutions/SummarizeFilesWizard/       → sprk_summarizefileswizard
├── src/solutions/FindSimilarCodePage/        → sprk_findsimilar
├── src/solutions/PlaybookLibrary/            → sprk_playbooklibrary

Layer 3: Consumers
├── Corporate Workspace (navigateTo calls from LegalWorkspace)
├── Entity form command bars (ribbon → sprk_wizard_commands.js → navigateTo)
├── Power Pages SPA (BFF adapters, direct component import)
```

### Why Three Layers?

| Layer | Responsibility | Changes When... |
|-------|---------------|-----------------|
| **Layer 1** (shared library) | All domain logic, UI rendering, service contracts | Business rules change, new fields, new step logic |
| **Layer 2** (Code Page wrapper) | Mount React, resolve theme, create adapters, pass props | Never (boilerplate is stable) |
| **Layer 3** (consumer) | Trigger `navigateTo` with entity context | New entry points, new button placements |

This separation means a bug fix or feature change in a wizard component is made once in Layer 1 and automatically available to all Code Page wrappers and consumers without modification.

---

## Service Abstraction (IDataService Pattern)

Shared components accept service interfaces instead of platform-specific APIs. This makes components portable across Dataverse Code Pages, Power Pages SPAs, and test harnesses.

### Service Interfaces

All interfaces are defined in `src/client/shared/Spaarke.UI.Components/src/types/serviceInterfaces.ts`:

| Interface | Purpose | Key Methods |
|-----------|---------|-------------|
| `IDataService` | CRUD operations against Dataverse entities | `createRecord`, `retrieveRecord`, `retrieveMultipleRecords`, `updateRecord`, `deleteRecord` |
| `IUploadService` | File upload to SharePoint Embedded via BFF | `uploadFile`, `getContainerIdForEntity` |
| `INavigationService` | Navigate to records, open dialogs, close dialogs | `openRecord`, `openDialog`, `closeDialog` |

### Adapter Selection Guide

Choose the correct adapter factory based on the hosting context:

| Context | Data Adapter | Upload Adapter | Navigation Adapter |
|---------|-------------|----------------|--------------------|
| **Dataverse Code Page** | `createXrmDataService()` | `createXrmUploadService(bffBaseUrl)` | `createXrmNavigationService()` |
| **Power Pages SPA** | `createBffDataService(authFetch, bffBaseUrl)` | `createBffUploadService(authFetch, bffBaseUrl)` | `createBffNavigationService(navigate)` |
| **Unit Tests** | `createMockDataService()` (hand-rolled) | `createMockUploadService()` (hand-rolled) | `createMockNavigationService()` (hand-rolled) |

### Adapter Imports

```typescript
// Xrm adapters (Code Pages, PCF controls)
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";

// BFF adapters (Power Pages SPA)
import { createBffDataService } from "@spaarke/ui-components/utils/adapters/bffDataServiceAdapter";
import { createBffUploadService } from "@spaarke/ui-components/utils/adapters/bffUploadServiceAdapter";
import { createBffNavigationService } from "@spaarke/ui-components/utils/adapters/bffNavigationServiceAdapter";
```

### Test Mock Example

```typescript
const mockDataService: IDataService = {
  createRecord: jest.fn().mockResolvedValue("00000000-0000-0000-0000-000000000001"),
  retrieveRecord: jest.fn().mockResolvedValue({ sprk_name: "Test" }),
  retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
  updateRecord: jest.fn().mockResolvedValue(undefined),
  deleteRecord: jest.fn().mockResolvedValue(undefined),
};
```

---

## Shell Selection Decision Tree

| Need | Use | Why |
|------|-----|-----|
| Multi-step wizard with file upload + entity creation | **CreateRecordWizard** | Handles upload, form, next-steps boilerplate; standardized 4-step flow |
| Multi-step wizard with custom step sequence | **WizardShell** directly | Full control over steps, step count, and flow (e.g., CreateWorkAssignment) |
| Playbook browse + execute | **PlaybookLibraryShell** | Tab UI with browse/custom scope tabs, intent mode for pre-selected playbooks |
| Single-step dialog (confirmation, simple form) | **Fluent UI Dialog** | No wizard overhead needed; use `@fluentui/react-components` `<Dialog>` |
| Slide-in side panel | **SidePaneShell** | Slide-in panel with navigation; used by CalendarSidePane, EventDetailSidePane, TodoDetailSidePane |

### Decision Flowchart

```
Does the user need to complete multiple steps?
├── NO → Use Fluent UI Dialog or SidePaneShell
└── YES
    ├── Is it a standard "upload files → fill form → pick next steps → finish" flow?
    │   └── YES → Use CreateRecordWizard
    ├── Is it a playbook browse/execute flow?
    │   └── YES → Use PlaybookLibraryShell
    └── Otherwise → Use WizardShell directly (custom steps)
```

---

## Reusable Dialog and Wizard Components

### Component Inventory

#### Shell Components (Layout + Navigation)

| Component | Location | Purpose |
|-----------|----------|---------|
| **WizardShell** | `@spaarke/ui-components/components/Wizard/` | Generic multi-step wizard dialog with sidebar stepper, step navigation, dynamic step injection, success/error handling |
| **CreateRecordWizard** | `@spaarke/ui-components/components/CreateRecordWizard/` | Standardized 4-step wizard (Add Files, Details, Next Steps, follow-on steps) built on WizardShell |
| **PlaybookLibraryShell** | `@spaarke/ui-components/components/PlaybookLibraryShell/` | Tab UI for browsing playbook catalog, selecting scope, launching intent-based wizard flows |

#### Domain Wizard Components

| Component | Location | Shell Used | Entity Created |
|-----------|----------|------------|----------------|
| **CreateMatterWizard** | `@spaarke/ui-components/components/CreateMatterWizard/` | CreateRecordWizard | `sprk_matter` |
| **CreateProjectWizard** | `@spaarke/ui-components/components/CreateProjectWizard/` | CreateRecordWizard | `sprk_project` |
| **CreateEventWizard** | `@spaarke/ui-components/components/CreateEventWizard/` | CreateRecordWizard | `sprk_event` |
| **CreateTodoWizard** | `@spaarke/ui-components/components/CreateTodoWizard/` | CreateRecordWizard | `sprk_event` (todoflag=true) |
| **CreateWorkAssignmentWizard** | `@spaarke/ui-components/components/CreateWorkAssignmentWizard/` | WizardShell (directly) | `sprk_workassignment` |
| **SummarizeFilesWizard** | `@spaarke/ui-components/components/SummarizeFilesWizard/` | WizardShell | (no entity; AI file summarization) |
| **FindSimilarDialog** | `@spaarke/ui-components/components/FindSimilarDialog/` | Fluent UI Dialog | (no entity; AI similarity search) |

#### Common Props Pattern

All domain wizard components accept:

```typescript
interface CommonWizardProps {
  open: boolean;
  embedded?: boolean;         // true when hosted in Dataverse dialog
  dataService: IDataService;
  uploadService: IUploadService;
  navigationService: INavigationService;
  onClose: () => void;
  authenticatedFetch: typeof fetch;
  bffBaseUrl: string;
}
```

---

## WizardShell

**WizardShell** is a domain-free, reusable wizard dialog shell located at `src/client/shared/Spaarke.UI.Components/src/components/Wizard/`. It owns layout (sidebar stepper + content area + footer), navigation state, and the finish/success flow. All domain logic lives in the consumer.

### Key Design Principles

- **Zero domain imports** -- `wizardShellTypes.ts` imports only React types; all domain content is injected via callbacks
- **Consumer-owned domain state** -- uploaded files, form values, API results are managed by the consumer's own reducer; WizardShell tracks only navigation state
- **Dynamic steps** -- steps can be added/removed at runtime via the imperative `IWizardShellHandle` (e.g., a "Next Steps" selection screen injects follow-on steps)

### Props

```typescript
interface IWizardShellProps {
  open: boolean;
  embedded?: boolean;        // Renders as full-page layout without Dialog overlay
  title: string;
  hideTitle?: boolean;       // Hides custom title bar when Dataverse provides its own chrome
  ariaLabel?: string;
  steps: IWizardStepConfig[];
  onClose: () => void;
  onFinish: () => Promise<IWizardSuccessConfig | void>;
  finishLabel?: string;      // Defaults to "Finish"
  finishingLabel?: string;   // Defaults to "Processing..."
}
```

#### `embedded` Prop

When `embedded` is `true`, the shell renders as a full-page layout without the Fluent UI `<Dialog>` overlay wrapper. Use this when the wizard is already hosted inside a Dataverse dialog (a Code Page opened via `navigateTo` with `target: 2`). The Dataverse dialog provides its own chrome (title bar + close button), so WizardShell avoids doubling up.

#### `hideTitle` Prop

When `hideTitle` is `true`, the shell's custom title bar is hidden. This is typically used together with `embedded` since the Dataverse dialog already renders a title bar from the `title` property passed to `navigateTo`.

### Step Configuration

```typescript
interface IWizardStepConfig {
  id: string;
  label: string;
  renderContent: (handle: IWizardShellHandle) => React.ReactNode;
  canAdvance: () => boolean;
  isEarlyFinish?: () => boolean;
  isSkippable?: boolean;       // Shows "Skip" button for optional follow-on steps
  footerActions?: React.ReactNode;
}
```

### Imperative Handle

```typescript
interface IWizardShellHandle {
  addDynamicStep(config: IWizardStepConfig, canonicalOrder?: string[]): void;
  removeDynamicStep(stepId: string): void;
  requestUpdate(): void;
  readonly state: IWizardShellState;
}
```

### Success Screen

When `onFinish` resolves with an `IWizardSuccessConfig`, the shell replaces all step content with a success view and hides the footer:

```typescript
interface IWizardSuccessConfig {
  icon: React.ReactNode;
  title: string;
  body: React.ReactNode;
  actions: React.ReactNode;
  warnings?: string[];
}
```

### Relationship to Other Shells

- **CreateRecordWizard** wraps WizardShell with a standardized 4-step flow (Add Files, Details, Next Steps, follow-on steps). Most entity-creation wizards use this.
- **PlaybookLibraryShell** does NOT use WizardShell internally; it has its own tab-based layout. However, when a user launches a playbook, the resulting wizard flow uses WizardShell.
- **CreateWorkAssignmentWizard** uses WizardShell directly because its step sequence differs from the standard CreateRecordWizard flow.

---

## Code Page Wrappers

Code Page wrappers are thin (~30-50 LOC) React 18 entry points that mount a shared component inside a Dataverse modal dialog. Each wrapper follows the same pattern.

### Wrapper Inventory

| Code Page | Web Resource Name | Shared Component | Solution Directory |
|-----------|-------------------|------------------|--------------------|
| Create Matter Wizard | `sprk_creatematterwizard` | `CreateMatterWizard` | `src/solutions/CreateMatterWizard/` |
| Create Project Wizard | `sprk_createprojectwizard` | `CreateProjectWizard` | `src/solutions/CreateProjectWizard/` |
| Create Event Wizard | `sprk_createeventwizard` | `CreateEventWizard` | `src/solutions/CreateEventWizard/` |
| Create To Do Wizard | `sprk_createtodowizard` | `CreateTodoWizard` | `src/solutions/CreateTodoWizard/` |
| Create Work Assignment | `sprk_createworkassignmentwizard` | `CreateWorkAssignmentWizard` | `src/solutions/CreateWorkAssignmentWizard/` |
| Document Upload Wizard | `sprk_documentuploadwizard` | `DocumentUploadWizard` | `src/solutions/DocumentUploadWizard/` |
| Summarize Files Wizard | `sprk_summarizefileswizard` | `SummarizeFilesWizard` | `src/solutions/SummarizeFilesWizard/` |
| Find Similar | `sprk_findsimilar` | `FindSimilarDialog` | `src/solutions/FindSimilarCodePage/` |
| Playbook Library | `sprk_playbooklibrary` | `PlaybookLibraryShell` | `src/solutions/PlaybookLibrary/` |

### Standard Wrapper Pattern

Every Code Page wrapper's `main.tsx` follows this structure:

```typescript
import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { MyWizardComponent } from "@spaarke/ui-components/components/MyWizardComponent";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const uploadService = React.useMemo(() => createXrmUploadService(params.bffBaseUrl || ""), [params.bffBaseUrl]);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const handleClose = React.useCallback(() => {
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <MyWizardComponent
        open={true}
        embedded={true}
        dataService={dataService}
        uploadService={uploadService}
        navigationService={navigationService}
        onClose={handleClose}
        authenticatedFetch={fetch.bind(window)}
        bffBaseUrl={params.bffBaseUrl || ""}
      />
    </FluentProvider>
  );
}

const rootElement = document.getElementById("root");
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode><App /></React.StrictMode>
  );
}
```

Key characteristics:
- Uses **React 18** `createRoot` (bundled, not platform-provided)
- Resolves theme via `resolveCodePageTheme()` (4-level cascade: localStorage, URL flags, navbar DOM, system preference)
- Parses URL parameters via `parseDataParams()` (handles both Xrm `data` envelope and raw query params)
- Creates **Xrm service adapters** (IDataService, IUploadService, INavigationService)
- Passes `embedded={true}` so WizardShell renders without its own Dialog overlay
- Uses deep imports from `@spaarke/ui-components` to minimize bundle size

---

## How to Create a New Dialog or Wizard

### For a standalone dialog (no multi-step flow)

Use a standard Fluent UI v9 `Dialog` or the `SidePanel` component from `@spaarke/ui-components`. Do **not** use WizardShell for single-step dialogs.

### For a multi-step wizard

#### Step 1: Create the wizard component in the shared library

Create a new directory under `src/client/shared/Spaarke.UI.Components/src/components/MyNewWizard/` with:

- `MyNewWizard.tsx` -- the wizard component that accepts `IDataService`, `IUploadService`, `INavigationService` as props
- `index.ts` -- barrel export

If using CreateRecordWizard, your component wraps it and provides entity-specific configuration (field mappings, validation, next-step options). If using WizardShell directly, your component builds the `IWizardStepConfig[]` array and provides the `onFinish` handler.

**Important**: The component must accept `embedded?: boolean` and pass it through to the shell. It must NOT import any Xrm or PCF-specific types.

#### Step 2: Create the Code Page wrapper

1. Create a new directory: `src/solutions/MyNewWizard/`
2. Add `src/main.tsx` following the [Standard Wrapper Pattern](#standard-wrapper-pattern)
3. Add `package.json`, `tsconfig.json`, `webpack.config.js` following any existing Code Page wrapper as a template
4. Add `index.html` with a `<div id="root"></div>` element

The wrapper should be ~30-50 lines. It only handles mounting, theme resolution, adapter creation, and parameter parsing.

#### Step 3: Deploy the web resource

Build and deploy using the code-page-deploy skill:

```bash
cd src/solutions/MyNewWizard
npm install && npm run build
# Deploy sprk_mynewwizard web resource to Dataverse
```

#### Step 4: Wire navigateTo from the consumer

**From a workspace (Corporate Workspace, etc.):**
```typescript
Xrm.Navigation.navigateTo(
  { pageType: "webresource", webresourceName: "sprk_mynewwizard", data: `entityType=${entityType}&entityId=${entityId}` },
  { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" }, title: "My New Wizard" }
);
```

**From a ribbon command button:**
Add a handler in `sprk_wizard_commands.js` (see [Wizard Command Handlers](#wizard-command-handlers-sprk_wizard_commandsjs)).

#### Step 5: Dynamic steps pattern

If a step's selection should inject or remove subsequent steps, call `handle.addDynamicStep(config, canonicalOrder)` inside `renderContent`. Pass `canonicalOrder` (array of all possible step IDs in desired order) so steps sort correctly regardless of selection order.

---

## Wizard Command Handlers (sprk_wizard_commands.js)

**Location**: `src/client/webresources/js/sprk_wizard_commands.js`
**Deployed as**: `sprk_wizard_commands` (Dataverse web resource)
**Namespace**: `Spaarke.Commands.Wizards`

This file provides ribbon command handler functions that open wizard Code Pages from entity form command bars. Each handler extracts entity context from the form's `PrimaryControl`, builds a data string, and opens the Code Page via `Xrm.Navigation.navigateTo`.

### Available Functions

| Function | Web Resource Opened | Dialog Size |
|----------|-------------------|-------------|
| `openCreateMatterWizard(primaryControl)` | `sprk_creatematterwizard` | 85% x 85% |
| `openCreateProjectWizard(primaryControl)` | `sprk_createprojectwizard` | 85% x 85% |
| `openCreateEventWizard(primaryControl)` | `sprk_createeventwizard` | 85% x 85% |
| `openCreateTodoWizard(primaryControl)` | `sprk_createtodowizard` | 85% x 85% |
| `openDocumentUploadWizard(primaryControl)` | `sprk_documentuploadwizard` | 85% x 85% |
| `openSummarizeFilesWizard(primaryControl)` | `sprk_summarizefileswizard` | 85% x 85% |
| `openFindSimilarDialog(primaryControl)` | `sprk_findsimilar` | 70% x 80% |
| `openPlaybookLibrary(primaryControl)` | `sprk_playbooklibrary` | 85% x 85% |
| `openPlaybookWithIntent(primaryControl, intent)` | `sprk_playbooklibrary` | 85% x 85% |

### How It Works

1. Ribbon button `<CommandDefinition>` references `Library="$webresource:sprk_wizard_commands"` and `FunctionName="Spaarke.Commands.Wizards.openXxxWizard"`
2. Handler calls `getEntityContext(primaryControl)` to extract `entityType`, `entityId`, and `entityName` from the form context
3. Handler calls `openWizardDialog(primaryControl, webresourceName, data, title, options)` which:
   - Calls `Xrm.Navigation.navigateTo` with `pageType: "webresource"`
   - On dialog close (success or cancel), refreshes the form data via `primaryControl.data.refresh()`

### Adding a New Wizard Command

1. Add a new function to the `return` object in `sprk_wizard_commands.js`:

```javascript
openMyNewWizard: function (primaryControl) {
  var ctx = getEntityContext(primaryControl);
  var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
  openWizardDialog(primaryControl, "sprk_mynewwizard", data, "My New Wizard");
},
```

2. Reference it in the ribbon XML `<CommandDefinition>`:

```xml
<CommandDefinition Id="sprk.MyNewWizard.Command">
  <EnableRules />
  <DisplayRules />
  <Actions>
    <JavaScriptFunction Library="$webresource:sprk_wizard_commands"
                        FunctionName="Spaarke.Commands.Wizards.openMyNewWizard">
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

3. Redeploy the `sprk_wizard_commands` web resource and publish.

---

## Entity Command Bar Buttons

The following table shows which wizard buttons are configured on each entity form's command bar via ribbon solutions.

| Entity | Buttons |
|--------|---------|
| `sprk_matter` | Create Project, Create Event, Create To Do, Upload Documents, Summarize Files, Find Similar, Playbook Library |
| `sprk_project` | Upload Documents, Summarize Files, Find Similar, Playbook Library |
| `sprk_event` | Upload Documents, Summarize Files, Find Similar, Playbook Library |

### Ribbon Solution Mapping

| Entity | Ribbon Solution | Location |
|--------|----------------|----------|
| `sprk_matter` | MatterRibbons | `infrastructure/dataverse/ribbon/MatterRibbons/` |
| `sprk_project` | ThemeMenuRibbons | `infrastructure/dataverse/ribbon/ThemeMenuRibbons/` |
| `sprk_event` | EventCommands | `src/solutions/EventCommands/` |

All command bar buttons use `$webresource:sprk_wizard_commands` as their JavaScript library and pass `CrmParameter Value="PrimaryControl"` to the handler function.

---

## Dark Mode Theme Persistence

### Overview

Spaarke implements user-selectable dark mode that **persists across browser sessions**. Users can choose between:

| Theme | Behavior |
|-------|----------|
| **Auto** | Follows system preference (default) |
| **Light** | Always light mode |
| **Dark** | Always dark mode |

### Technical Implementation

Power Platform MDA dark mode is controlled via URL flag: `flags=themeOption%3Ddarkmode`

The persistence mechanism uses:
1. **localStorage** - Stores user preference (`spaarke-theme` key)
2. **Ribbon EnableRule** - Triggers on every entity grid load
3. **URL Redirect** - Adds/removes dark mode flag based on stored preference

```
┌─────────────────────────────────────────────────────────────────┐
│                    Theme Persistence Flow                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  User clicks "Dark" in Theme menu                               │
│         │                                                        │
│         ▼                                                        │
│  localStorage.setItem('spaarke-theme', 'dark')                  │
│         │                                                        │
│         ▼                                                        │
│  Redirect to URL with flags=themeOption%3Ddarkmode              │
│         │                                                        │
│         ▼                                                        │
│  ═══════════════════════════════════════════════════            │
│  Later: User navigates to different entity grid                 │
│         │                                                        │
│         ▼                                                        │
│  Ribbon loads → EnableRule calls isEnabled()                    │
│         │                                                        │
│         ▼                                                        │
│  init() checks: localStorage='dark' but URL missing flag?       │
│         │                                                        │
│         ▼                                                        │
│  Auto-redirect to add dark mode flag                            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Components

#### Web Resource: `sprk_ThemeMenu.js`

**Location**: `src/client/webresources/js/sprk_ThemeMenu.js`

Key functions:
- `Spaarke.Theme.setTheme(theme)` - Called by ribbon buttons, saves to localStorage and redirects
- `Spaarke.Theme.isEnabled()` - Called by ribbon EnableRule, triggers `init()` as side effect
- `Spaarke.Theme.init()` - Checks localStorage vs URL and redirects if mismatch

#### Code Page Theme Resolution

Code Pages do NOT use the ribbon-based mechanism. Instead, they resolve theme via `resolveCodePageTheme()` from `@spaarke/ui-components` (unified `themeStorage.ts` module), which uses a 3-level cascade:

1. **localStorage** (`spaarke-theme` key) -- user's explicit preference
2. **URL flags** (`flags` param with `themeOption=dark|light`)
3. **Navbar DOM** -- reads Dataverse navbar background-color luminance
4. **Default: light** -- OS `prefers-color-scheme` is intentionally NOT consulted (ADR-021)

Code Pages also listen for theme changes via `setupCodePageThemeListener()` to react to cross-tab localStorage changes and system preference changes.

#### Ribbon Solutions

| Solution | Entities Covered | Location |
|----------|------------------|----------|
| DocumentRibbons | sprk_spedocument | `infrastructure/dataverse/ribbon/DocumentRibbons/` |
| MatterRibbons | sprk_Matter | `infrastructure/dataverse/ribbon/MatterRibbons/` |
| ThemeMenuRibbons | sprk_Project, sprk_Invoice, sprk_Event | `infrastructure/dataverse/ribbon/ThemeMenuRibbons/` |

#### Ribbon Structure (per entity)

Each entity ribbon includes:
- **FlyoutAnchor** - Theme dropdown menu at `Mscrm.HomepageGrid.{entity}.MainTab.Actions.Controls._children`
- **Buttons** - Auto, Light, Dark options
- **Commands** - SetAuto, SetLight, SetDark calling `Spaarke.Theme.setTheme()`
- **EnableRule** - Calls `Spaarke.Theme.isEnabled()` which triggers theme enforcement

### Web Resource Icons

| Resource Name | Purpose |
|---------------|---------|
| sprk_ThemeMenu16.svg | Flyout button icon (16x16) |
| sprk_ThemeMenu32.svg | Flyout button icon (32x32) |
| sprk_ThemeAuto16.svg | Auto option icon |
| sprk_ThemeLight16.svg | Light option icon |
| sprk_ThemeDark16.svg | Dark option icon |

---

## Entity Ribbon Customizations

### Entities with Theme Menu

The following entities have the Theme menu ribbon button configured:

| Entity Schema Name | Display Name | Ribbon Solution |
|--------------------|--------------|-----------------|
| sprk_spedocument | Document | DocumentRibbons |
| sprk_Matter | Matter | MatterRibbons |
| sprk_Project | Project | ThemeMenuRibbons |
| sprk_Invoice | Invoice | ThemeMenuRibbons |
| sprk_Event | Event | ThemeMenuRibbons |

### Ribbon Location Pattern

All theme menu buttons use location:
```
Mscrm.HomepageGrid.{entity_lowercase}.MainTab.Actions.Controls._children
```

Example for sprk_Project:
```
Mscrm.HomepageGrid.sprk_project.MainTab.Actions.Controls._children
```

---

## Maintenance Procedures

### Adding Theme Menu to a New Entity

When adding a new main entity to the application, you **must** add the Theme menu ribbon to ensure theme persistence works when users navigate to that entity.

#### Option 1: Add to Existing ThemeMenuRibbons Solution (Recommended)

1. Edit `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml`

2. Add a new `<Entity>` block following this template:

```xml
<Entity>
  <Name LocalizedName="{DisplayName}" OriginalName="{DisplayName}">sprk_{EntityName}</Name>
  <EntityInfo>
    <entity Name="sprk_{EntityName}" unmodified="1">
      <attributes />
    </entity>
  </EntityInfo>
  <RibbonDiffXml>
    <CustomActions>
      <CustomAction Id="sprk.ThemeMenu.{EntityName}.CustomAction" Location="Mscrm.HomepageGrid.sprk_{entityname_lowercase}.MainTab.Actions.Controls._children" Sequence="900">
        <CommandUIDefinition>
          <FlyoutAnchor Id="sprk.ThemeMenu.{EntityName}.Flyout" Command="sprk.ThemeMenu.{EntityName}.Command" LabelText="Theme" ToolTipTitle="Select Theme" ToolTipDescription="Choose your preferred color theme" Image16by16="$webresource:sprk_ThemeMenu16.svg" Image32by32="$webresource:sprk_ThemeMenu32.svg" PopulateDynamically="false" TemplateAlias="o1">
            <Menu Id="sprk.ThemeMenu.{EntityName}.Menu">
              <MenuSection Id="sprk.ThemeMenu.{EntityName}.Section" Title="Color Theme" Sequence="10">
                <Controls Id="sprk.ThemeMenu.{EntityName}.Controls">
                  <Button Id="sprk.ThemeMenu.{EntityName}.Auto" Command="sprk.ThemeMenu.{EntityName}.SetAuto" LabelText="Auto" Image16by16="$webresource:sprk_ThemeAuto16.svg" Sequence="10" />
                  <Button Id="sprk.ThemeMenu.{EntityName}.Light" Command="sprk.ThemeMenu.{EntityName}.SetLight" LabelText="Light" Image16by16="$webresource:sprk_ThemeLight16.svg" Sequence="20" />
                  <Button Id="sprk.ThemeMenu.{EntityName}.Dark" Command="sprk.ThemeMenu.{EntityName}.SetDark" LabelText="Dark" Image16by16="$webresource:sprk_ThemeDark16.svg" Sequence="30" />
                </Controls>
              </MenuSection>
            </Menu>
          </FlyoutAnchor>
        </CommandUIDefinition>
      </CustomAction>
    </CustomActions>
    <Templates><RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates></Templates>
    <CommandDefinitions>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.Command"><EnableRules><EnableRule Id="sprk.ThemeMenu.{EntityName}.EnableRule" /></EnableRules><DisplayRules /><Actions /></CommandDefinition>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.SetAuto"><EnableRules /><DisplayRules /><Actions><JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js" FunctionName="Spaarke.Theme.setTheme"><StringParameter Value="auto" /></JavaScriptFunction></Actions></CommandDefinition>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.SetLight"><EnableRules /><DisplayRules /><Actions><JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js" FunctionName="Spaarke.Theme.setTheme"><StringParameter Value="light" /></JavaScriptFunction></Actions></CommandDefinition>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.SetDark"><EnableRules /><DisplayRules /><Actions><JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js" FunctionName="Spaarke.Theme.setTheme"><StringParameter Value="dark" /></JavaScriptFunction></Actions></CommandDefinition>
    </CommandDefinitions>
    <RuleDefinitions><TabDisplayRules /><DisplayRules /><EnableRules><EnableRule Id="sprk.ThemeMenu.{EntityName}.EnableRule"><CustomRule FunctionName="Spaarke.Theme.isEnabled" Library="$webresource:sprk_ThemeMenu.js" Default="true" /></EnableRule></EnableRules></RuleDefinitions>
    <LocLabels />
  </RibbonDiffXml>
</Entity>
```

3. Update Solution.xml to add the entity as a RootComponent:

```xml
<RootComponent type="1" schemaName="sprk_{EntityName}" behavior="2" />
```

4. Pack and import:

```bash
cd infrastructure/dataverse/ribbon/ThemeMenuRibbons
pac solution pack --zipfile ThemeMenuRibbons.zip --folder .
pac solution import --path ThemeMenuRibbons.zip --publish-changes
```

#### Option 2: Create Separate Ribbon Solution

For entities that require additional ribbon customizations beyond the theme menu, create a dedicated solution following the pattern in `DocumentRibbons/` or `MatterRibbons/`.

### Updating the Theme Menu JavaScript

If you need to modify theme behavior:

1. Edit `src/client/webresources/js/sprk_ThemeMenu.js`

2. Update the web resource in Dataverse:

```powershell
# Using Dataverse Web API (PowerShell)
$content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content -Path "src/client/webresources/js/sprk_ThemeMenu.js" -Raw)))

$body = @{
    content = $content
} | ConvertTo-Json

Invoke-RestMethod -Uri "$env:DATAVERSE_URL/api/data/v9.2/webresourceset(sprk_ThemeMenu.js_GUID)" `
    -Method PATCH -Body $body -Headers $headers -ContentType "application/json"
```

3. Publish the web resource in the Power Platform maker portal

### Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Theme doesn't persist to new entity | Entity missing ribbon customization | Add theme menu ribbon per procedure above |
| Theme menu not appearing | Ribbon solution not imported | Import the relevant ribbon solution |
| Dark mode flickers on load | EnableRule not triggering | Verify EnableRule references `Spaarke.Theme.isEnabled` |
| Console errors on theme change | Web resource not published | Publish `sprk_ThemeMenu.js` |
| Code Page opens in wrong theme | localStorage not set or theme cascade failing | Check `resolveCodePageTheme()` cascade; verify `spaarke-theme` in localStorage |
| Wizard buttons missing on entity form | Ribbon solution not deployed or `sprk_wizard_commands` not published | Import ribbon solution and publish web resource |

### Known Limitations

1. **Initial page load** - The very first page load after login may briefly show wrong theme until ribbon loads
2. **Non-entity pages** - Pages without entity grids (dashboards, settings) won't trigger ribbon, so theme may not enforce until navigating to an entity
3. **URL sharing** - If a user shares a URL with/without dark mode flag, recipient may see different theme until they navigate

---

## Future Considerations

### Potential Improvements

1. **Application-level enforcement** - Investigate using App Module ribbon or Client API `Xrm.Navigation` hooks for more universal enforcement
2. **Server-side preference** - Store theme preference in Dataverse user settings for cross-browser persistence
3. **ThemeEnforcer PCF** - A PCF control exists at `src/client/pcf/ThemeEnforcer/` that could be embedded in sitemap for alternative enforcement (currently not used)

---

## WorkspaceShell Architecture

WorkspaceShell is a **full-page declarative layout container** for workspace dashboards. Unlike WizardShell and SidePaneShell (which are imperative dialog containers), WorkspaceShell consumes a `WorkspaceConfig` object and renders a responsive multi-row grid of section panels. It lives in `@spaarke/ui-components` alongside the dialog shells but serves a fundamentally different purpose.

### Component Hierarchy

```
WorkspaceHeader (dropdown layout switcher + settings gear)
└── WorkspaceShell (layout container)
    ├── Row 1: SectionPanel[] (CSS grid columns, e.g., "1fr 1fr")
    ├── Row 2: SectionPanel[]
    └── Row N: SectionPanel[]
        └── SectionPanel
            ├── Header (title + badge count + collapse toggle)
            ├── Toolbar (optional: refresh, add, open buttons)
            └── Content (action-cards | metric-cards | content renderProp)
```

- **WorkspaceHeader** (`src/components/WorkspaceHeader/`) — dropdown switcher listing system and user layouts, settings button, and "+ New Workspace" action. Pure presentational; parent supplies data and callbacks.
- **WorkspaceShell** (`@spaarke/ui-components/components/WorkspaceShell/`) — reads `WorkspaceConfig` and renders rows as CSS grid containers. Supports `"rows"` (multi-column) and `"single-column"` layout modes. Multi-column rows collapse to single column at viewport width ≤767px.
- **SectionPanel** (`@spaarke/ui-components/components/WorkspaceShell/SectionPanel`) — bordered card with title bar, optional badge, optional toolbar, and collapsible content area. Three content types: `action-cards` (ActionCardRow), `metric-cards` (MetricCardRow), and `content` (consumer-supplied renderProp).

### Section Registry Pattern

Sections self-register via `SectionRegistration` objects. Each registration declares an `id`, `label`, `category`, `icon`, and a `factory(context)` function that produces a `SectionConfig` at render time.

```
sections/
├── getStarted.registration.ts      → "get-started" (action-cards)
├── quickSummary.registration.ts    → "quick-summary" (metric-cards)
├── latestUpdates.registration.ts   → "latest-updates" (content)
├── todo.registration.ts            → "todo" (content)
└── documents.registration.ts       → "documents" (content)

sectionRegistry.ts → SECTION_REGISTRY (aggregated array of all registrations)
```

Adding a new section requires two steps: create a `{name}.registration.ts` file, then import it into `sectionRegistry.ts`.

### Data Flow

```
BFF GET /api/workspace/layouts/default → LayoutJson (persisted in sprk_sectionsjson)
+ SECTION_REGISTRY (code-side, all available section factories)
→ buildDynamicWorkspaceConfig(layoutJson, registry, context) → WorkspaceConfig
→ <WorkspaceShell config={config} />
```

The `LayoutJson` describes row structure (IDs, CSS grid columns, section assignments). The registry describes how to construct each section. `buildDynamicWorkspaceConfig()` reconciles the two: it validates the schema version, resolves each section ID against the registry, calls the factory function, handles unknown sections gracefully (warns and skips), and manages row overflow when more sections exist than column slots.

A system default layout (`SYSTEM_DEFAULT_LAYOUT_JSON`) is used as fallback when no user configuration exists or the schema version is unsupported.

### Key Distinction from Dialog Shells

| Aspect | WorkspaceShell | WizardShell / SidePaneShell |
|--------|---------------|----------------------------|
| **Purpose** | Full-page workspace dashboard layout | Modal dialog / side panel container |
| **API style** | Declarative config object (`WorkspaceConfig`) | Imperative props (steps, onFinish, open/close) |
| **Content model** | N sections in M rows, each with typed content | Sequential steps or single panel body |
| **Lifecycle** | Always mounted as page content | Opened/closed by user action |
| **User configuration** | Layout JSON stored in Dataverse, switchable via WorkspaceHeader | Not user-configurable |

WorkspaceShell and dialog shells coexist in the same shared library (`@spaarke/ui-components`) but address different UX patterns. Workspace sections may themselves open dialog shells (e.g., the To Do section's "Add" button opens `sprk_createtodowizard` via `navigateTo`).

---

## Related Documentation

- [ADR-006: PCF over Web Resources](../adr/ADR-006-pcf-over-webresources.md) - Architecture decision for UI components (Code Pages for standalone dialogs)
- [ADR-012: Shared Component Library](../adr/ADR-012-shared-component-library.md) - Shared `@spaarke/ui-components` across modules
- [ADR-021: Fluent UI v9 Design System](../adr/ADR-021-fluent-ui-v9.md) - Dark mode required, Fluent UI v9 exclusive
- [ADR-026: UI Dialog Shell Standardization](../adr/ADR-026-ui-dialog-shell-standardization.md) - Three-layer model, service abstractions, Code Page wrappers
- [WizardShell Types](../../src/client/shared/Spaarke.UI.Components/src/types/serviceInterfaces.ts) - Service interface definitions (IDataService, IUploadService, INavigationService)
- [WizardShell Component Types](../../src/client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes.ts) - Full type definitions and JSDoc for the WizardShell API
- [MDA Dark Mode Theme Project](../../projects/mda-darkmode-theme/spec.md) - Original design specification
