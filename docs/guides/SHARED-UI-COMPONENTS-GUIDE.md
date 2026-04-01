# Shared UI Components Guide (`@spaarke/ui-components`)

> **Version**: 2.0.0
> **Location**: `src/client/shared/Spaarke.UI.Components/`
> **ADR**: [ADR-012](../adr/ADR-012-shared-component-library.md)
> **Last Updated**: 2026-03-30

---

## Overview

`@spaarke/ui-components` is Spaarke's shared React/TypeScript component library. It provides reusable UI components, hooks, services, types, and theming consumed by both **PCF controls** (React 16/17) and **React Code Pages** (React 18).

All components use **Fluent UI v9** exclusively and support dark mode via semantic tokens.

---

## Quick Start

### Build the library

```bash
cd src/client/shared/Spaarke.UI.Components
npm install
npm run build    # TypeScript compilation → dist/
```

### Consume in a project

**package.json** (file: reference for workspace linking):
```json
{
  "devDependencies": {
    "@spaarke/ui-components": "file:../../client/shared/Spaarke.UI.Components"
  }
}
```

**Import** (barrel export):
```typescript
import { AiSummaryPopover, FindSimilarDialog, WizardShell } from "@spaarke/ui-components";
```

---

## Build Workflow

| Command | Purpose |
|---------|---------|
| `npm run build` | TypeScript compilation (tsc) → `dist/` |
| `npm run build:watch` | Watch mode for development |
| `npm run clean` | Remove `dist/` |
| `npm run test` | Jest test suite |
| `npm run lint` | ESLint check |

**Build order matters**: Always build the shared library *before* building consumers (PCF, Code Pages). Consumer builds resolve imports from `dist/`.

<!-- TODO(ai-procedure-refactoring): Known tsc error list (ViewSelector, PageChrome, RichTextEditor, SprkChat) may be stale — components may have been fixed or new ones added; verify against current build output -->

**Pre-existing tsc errors**: The library has known tsc errors in ViewSelector, PageChrome, RichTextEditor, and SprkChat. These do not block emit (`noEmitOnError` is not set), so `dist/` is always produced.

---

## React Version Compatibility

The shared library is consumed across multiple surfaces with different React runtimes:

| Consumer | React Version | Entry Point | Import Pattern |
|----------|--------------|-------------|----------------|
| PCF Controls | React 16/17 (platform-provided, not bundled) | `ReactControl` or `StandardControl` | Deep imports only |
| Code Pages | React 18/19 (bundled via webpack/Vite) | `createRoot()` | Barrel or deep imports |
| Power Pages SPA | React 18 (bundled via Vite) | `createRoot()` | Barrel or deep imports |

**Why this matters**: PCF controls run in a Dataverse form where React is injected by the platform (React 16/17). Code Pages and SPAs bundle their own React, so they can use any version. Components in the shared library must be authored to be React 19-compatible (latest) while maintaining React 16/17 compatibility for PCF consumers.

---

## Consumer Patterns

### PCF Controls (React 16/17)

PCF controls use **platform-provided** React 16/17. The shared library is a `devDependency` (not bundled — resolved at build time by webpack).

**Critical: Use deep imports** to avoid pulling in all exports (some components like RichTextEditor use Lexical which requires `react/jsx-runtime` unavailable in React 16):

```typescript
// ✅ CORRECT — deep import avoids barrel export tree
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
import { AiSummaryPopover } from "@spaarke/ui-components/dist/components/AiSummaryPopover";

// ❌ WRONG — barrel import pulls in ALL components including Lexical
import { FindSimilarDialog } from "@spaarke/ui-components";
```

**When is barrel import safe for PCF?** Only when the component and all its transitive dependencies are React 16-compatible. Currently, the RichTextEditor (Lexical) breaks barrel imports for PCF.

### React Code Pages (React 18/19)

Code Pages bundle React 18/19 via Vite/webpack. Barrel imports are safe:

```typescript
// ✅ Both work for Code Pages
import { AiSummaryPopover, FindSimilarDialog, WizardShell } from "@spaarke/ui-components";
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
```

### Power Pages SPA (React 18)

The external SPA (`src/client/external-spa/`) bundles React 18 via Vite. Barrel imports are safe. Uses BFF adapters instead of Xrm adapters.

### Import Path Summary

| Consumer | Import Pattern | Why |
|----------|---------------|-----|
| PCF (React 16/17) | `@spaarke/ui-components/dist/components/{Name}` | Avoids pulling Lexical/jsx-runtime deps |
| Code Page (React 18/19) | `@spaarke/ui-components` | Barrel is safe; React 18+ has jsx-runtime |
| Power Pages SPA (React 18) | `@spaarke/ui-components` | Barrel is safe; Vite tree-shakes |

---

## Component Inventory

<!-- TODO(ai-procedure-refactoring): Component inventory tables below are a snapshot from 2026-03-30 — new components added to the shared library will not appear here; check src/client/shared/Spaarke.UI.Components/src/components/ for the authoritative list -->

### Shell & Wizard Components (`src/components/`)

| Component | Description | Consumers |
|-----------|-------------|-----------|
| **WizardShell** | Multi-step wizard container with stepper and navigation | Code Pages |
| **WizardStepper** | Step indicator UI for wizards | Code Pages |
| **WizardSuccessScreen** | Success state after wizard completion | Code Pages |
| **CreateRecordWizard** | Record-creation boilerplate (file upload + create + next steps) wrapping WizardShell | Code Pages |
| **PlaybookLibraryShell** | Playbook browsing, selection, and execution shell | Code Pages, SPA |
| **SidePaneShell** | Reusable slide-in side panel layout | Code Pages |
| **WorkspaceShell** | Declarative responsive workspace layout container; renders rows of sections from a `WorkspaceConfig` object | Code Pages, SPA |
| **SectionPanel** | Titled bordered section card with optional toolbar, badge count, and collapsible body | Code Pages, SPA |
| **ActionCard** / **ActionCardRow** | Square action cards (icon + label) arranged in a wrapping row — "Get Started" pattern | Code Pages, SPA |
| **MetricCard** / **MetricCardRow** | Square metric cards (value + trend + badge) arranged in a wrapping row — "Quick Summary" pattern | Code Pages, SPA |

### Workspace Header Component (`src/solutions/LegalWorkspace/`)

| Component | Description | Consumers |
|-----------|-------------|-----------|
| **WorkspaceHeader** | Workspace layout dropdown switcher with settings gear and "New Layout" button; lists system and user layouts | Code Pages, SPA |

### Domain Wizard Components (`src/components/`)

| Component | Description | Consumers |
|-----------|-------------|-----------|
| **CreateMatterWizard** | Matter creation wizard (uses CreateRecordWizard) | Code Pages |
| **CreateProjectWizard** | Project creation wizard (uses CreateRecordWizard) | Code Pages |
| **CreateEventWizard** | Event creation wizard (uses CreateRecordWizard) | Code Pages |
| **CreateTodoWizard** | Todo creation wizard (uses CreateRecordWizard) | Code Pages |
| **CreateWorkAssignmentWizard** | Work assignment wizard (uses WizardShell directly — different step sequence) | Code Pages |
| **SummarizeFilesWizard** | File summarization wizard with AI analysis step | Code Pages |
| **FindSimilarDialog** | Semantic search dialog shell for DocumentRelationshipViewer | PCF, Code Pages |

### UI Components (`src/components/`)

| Component | Description | Consumers |
|-----------|-------------|-----------|
| **SprkButton** | Fluent v9 Button wrapper with optional tooltip | All |
| **DatasetGrid** | Multi-view dataset container (Grid, Card, List, Virtualized) | PCF, Code Pages |
| **ViewSelector** | View mode switcher for DatasetGrid | PCF, Code Pages |
| **CommandToolbar** | Action bar for grids and pages | PCF, Code Pages |
| **PageChrome** | Page header/chrome (OOB parity) | Code Pages |
| **RichTextEditor** | Lexical-based WYSIWYG editor | Code Pages only* |
| **ChoiceDialog** | Simple choice dialog | All |
| **EventDueDateCard** | Event date display card | Code Pages |
| **SprkChat** | Streaming chat component with SSE | Code Pages |
| **DiffCompareView** | AI revision diff (side-by-side + inline) | Code Pages |
| **LookupField** | Search-as-you-type entity lookup | Code Pages |
| **SendEmailDialog** | Email composition dialog with To lookup | Code Pages |
| **AiSummaryPopover** | AI summary popover with lazy fetch and copy | PCF, Code Pages |

*\*RichTextEditor uses Lexical which requires `react/jsx-runtime` — not available in PCF React 16.*

### Hooks (`src/hooks/`)

| Hook | Purpose |
|------|---------|
| `useDatasetMode` | Dataset display mode management |
| `useHeadlessMode` | Headless/standalone mode detection |
| `useVirtualization` | Row virtualization for large datasets |
| `useKeyboardShortcuts` | Keyboard shortcut management |
| `useEntityTypeConfig` | Entity-specific configuration |
| `useDirtyFields` | Track field changes for optimistic save |
| `useOptimisticSave` | Optimistic save with rollback |
| `useWriteMode` | Write/edit mode toggle |
| `useSseStream` | Server-Sent Events streaming |
| `useAiSummary` | AI summary fetch with caching |

### Services (`src/services/`)

| Service | Purpose |
|---------|---------|
| `CommandRegistry` | Register and discover toolbar commands |
| `CommandExecutor` | Execute registered commands |
| `FieldMappingService` | Map entity fields to display columns |
| `EventTypeService` | Event type configuration |
| `FetchXmlService` | Build FetchXML queries |
| `ViewService` | Saved view management |
| `ConfigurationService` | Grid/dataset configuration |
| `SprkChatBridge` | Chat SSE event bridge |

### Types (`src/types/`)

| Type Module | Contents |
|-------------|----------|
| `DatasetTypes` | Dataset, column, row interfaces |
| `CommandTypes` | Command definitions, handlers |
| `ColumnRendererTypes` | Column renderer configs |
| `EntityConfigurationTypes` | Entity-specific config |
| `LookupTypes` | `ILookupItem` for search lookups |
| `WebApiLike` | Dataverse WebAPI abstraction (low-level) |
| `serviceInterfaces` | `IDataService`, `IUploadService`, `INavigationService` (high-level service abstractions) |
| `FetchXmlTypes` | FetchXML query types |
| `ConfigurationTypes` | Configuration schemas |

### Theme (`src/theme/`)

| Export | Description |
|--------|-------------|
| `spaarkeBrand` | Spaarke BrandVariants (Blue #2173d7) |
| `spaarkeLight` | Light theme (Fluent v9 `createLightTheme`) |
| `spaarkeDark` | Dark theme (Fluent v9 `createDarkTheme`) |

### Utilities (`src/utils/`)

| Utility | Purpose |
|---------|---------|
| `themeDetection` | Detect Dataverse theme (dark/light) |
| `themeStorage` | Persist theme preference |
| `xrmContext` | Resolve Xrm global in various contexts |
| `adapters/xrmDataServiceAdapter` | `createXrmDataService()`, `createXrmUploadService()`, `createXrmNavigationService()` |
| `adapters/bffDataServiceAdapter` | `createBffDataService()`, `createBffUploadService()`, `createBffNavigationService()` |
| `adapters/mockDataServiceAdapter` | `createMockDataService()`, `createMockUploadService()`, `createMockNavigationService()` |

### Icons (`src/icons/`)

| Export | Description |
|--------|-------------|
| `SprkIcons` | Icon component registry |

---

## Component Design Principles

### Callback-Based Props (Zero Service Dependencies)

Shared components accept behavior via callback props. They never import services directly — the consumer provides all side effects.

```typescript
// ✅ CORRECT — consumer provides behavior
export interface ISendEmailDialogProps {
  open: boolean;
  onClose: () => void;
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;
  onSend: (payload: ISendEmailPayload) => Promise<void>;
}

// ❌ WRONG — component imports services
import { authenticatedFetch } from "../../services/authInit";
```

### Context-Agnostic

Components never reference PCF APIs (`ComponentFramework.*`), Xrm globals, or entity-specific schemas. All context is passed via props.

### Fluent v9 Only

All styling uses Fluent UI v9 `makeStyles`, `tokens`, and `shorthands`. No custom CSS files, no hard-coded colors.

---

## Service Abstractions (`IDataService`, `IUploadService`, `INavigationService`)

Shared components need to perform data operations (CRUD, file upload, navigation) but must not call platform APIs directly. Three interfaces in `src/types/serviceInterfaces.ts` provide the abstraction layer:

### IDataService

High-level Dataverse entity operations. Components accept `IDataService` as a prop and call methods like `createRecord`, `retrieveRecord`, `retrieveMultipleRecords`, `updateRecord`, and `deleteRecord`.

```typescript
export interface IDataService {
  createRecord(entityName: string, data: Record<string, unknown>): Promise<string>;
  retrieveRecord(entityName: string, id: string, options?: string): Promise<Record<string, unknown>>;
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
  updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<void>;
  deleteRecord(entityName: string, id: string): Promise<void>;
}
```

### IUploadService

File upload operations abstracted from the underlying storage mechanism (SharePoint Embedded, Blob Storage, etc.):

```typescript
export interface IUploadService {
  uploadFile(entityName: string, entityId: string, file: File, options?: UploadOptions): Promise<UploadResult>;
  getContainerIdForEntity(entityName: string, entityId: string): Promise<string>;
}
```

### INavigationService

Navigation and dialog operations abstracted from `Xrm.Navigation`:

```typescript
export interface INavigationService {
  openRecord(entityName: string, entityId: string): Promise<void>;
  openDialog(webresourceName: string, data?: string, options?: DialogOptions): Promise<DialogResult>;
  closeDialog(result?: unknown): void;
}
```

### How Components Consume Services

Services are passed as props from the consumer (Code Page wrapper, PCF control, or test harness). The shared component never imports platform APIs.

```typescript
// ✅ CORRECT — shared component accepts service via props
interface ICreateMatterWizardProps {
  matterId: string;
  dataService: IDataService;
  uploadService: IUploadService;
  navigationService: INavigationService;
}

// ❌ WRONG — shared component imports Xrm directly
import { getXrm } from "../utils/xrmContext"; // This belongs in the adapter, not the component
```

---

## Adapter Pattern (Xrm, BFF, Mock)

Pre-built adapter factories in `src/utils/adapters/` wire the service interfaces to concrete platforms. Consumers call the factory in their entry point and pass the result as props.

### When to Use Each Adapter

| Adapter | Factory Function | Use Case |
|---------|-----------------|----------|
| **Xrm** | `createXrmDataService()` | Code Pages running inside Dataverse (Xrm global available) |
| | `createXrmUploadService(bffBaseUrl)` | File uploads via BFF API from a Code Page |
| | `createXrmNavigationService()` | Navigation/dialogs from a Code Page |
| **BFF** | `createBffDataService(authFetch, bffBaseUrl)` | Power Pages SPA (no Xrm, all data via BFF API) |
| | `createBffUploadService(authFetch, bffBaseUrl)` | File uploads from the SPA |
| | `createBffNavigationService(navigate?)` | SPA router navigation |
| **Mock** | `createMockDataService()` | Unit tests with `jest.fn()` stubs |
| | `createMockUploadService()` | Unit tests |
| | `createMockNavigationService()` | Unit tests |

### Example: Wiring Adapters in a Code Page

```typescript
// main.tsx — Code Page entry point
import { createXrmDataService, createXrmUploadService, createXrmNavigationService } from "@spaarke/ui-components";

const dataService = createXrmDataService();
const uploadService = createXrmUploadService("https://spe-api-dev-67e2xz.azurewebsites.net");
const navigationService = createXrmNavigationService();

createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
        <CreateMatterWizard
            dataService={dataService}
            uploadService={uploadService}
            navigationService={navigationService}
        />
    </FluentProvider>
);
```

### Example: Wiring Adapters in a Test

```typescript
import { createMockDataService, createMockUploadService, createMockNavigationService } from "@spaarke/ui-components";

const mockData = createMockDataService();
const mockUpload = createMockUploadService();
const mockNav = createMockNavigationService();

render(
    <FluentProvider theme={webLightTheme}>
        <CreateMatterWizard dataService={mockData} uploadService={mockUpload} navigationService={mockNav} />
    </FluentProvider>
);

expect(mockData.createRecord).toHaveBeenCalledWith("sprk_matter", expect.objectContaining({ sprk_name: "Test" }));
```

---

## Code Page Wizard Wrapper Pattern

Code Page wrappers are thin `main.tsx` entry points that bootstrap a shared-library wizard component. The wrapper handles platform-specific setup (React 18 `createRoot`, Fluent theme, adapter wiring) while all wizard logic lives in `@spaarke/ui-components`.

**Key rule**: The Code Page wrapper contains **zero business logic**. It reads URL params, creates adapters, and renders the shared wizard.

```
src/client/code-pages/CreateMatterWizard/
├── index.html          # HTML shell with #root div
├── package.json        # React 18, Fluent v9, shared library dep
├── main.tsx            # ~30 lines: createRoot + adapter wiring
└── vite.config.ts      # Vite + singlefile (standard template)
```

All wizard steps, validation, entity creation logic, and orchestration are in the shared library (`src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/`).

For the full project structure template and build tooling, see the [Full-Page Custom Page Template](../../.claude/patterns/webresource/full-page-custom-page.md) pattern. For dialog-specific patterns (opening, closing, parameter passing), see [Dialog Patterns](../../.claude/patterns/pcf/dialog-patterns.md).

---

## Adding a New Component

### 1. Create the component

```
src/components/{ComponentName}/
├── {ComponentName}.tsx       # Component implementation
└── index.ts                  # Barrel export
```

### 2. Export from barrel

Add to `src/components/index.ts`:
```typescript
export * from "./{ComponentName}";
```

### 3. Build and verify

```bash
npm run build   # Check for tsc errors in your new component
```

### 4. Consume

Import in your consumer project. Remember: PCF uses deep imports, Code Pages use barrel.

### Decision: Shared Library vs. Module-Local

| Add to Shared Library | Keep in Module |
|----------------------|----------------|
| Used by 2+ consumers | Single consumer only |
| Core Spaarke UX pattern | Module-specific business logic |
| Clear, callback-based API | Tight coupling to services/context |
| Reusable layout primitive | Experimental/prototype |

---

## Section Registry Pattern

The Section Registry is the extension point for adding workspace sections to any `WorkspaceShell`-based workspace. It decouples section metadata and rendering from the workspace layout logic.

### Key Interfaces (exported from `@spaarke/ui-components`)

**`SectionRegistration`** — the only interface a section author needs to implement:

| Field | Type | Description |
|-------|------|-------------|
| `id` | `string` | Unique section identifier (stored in Dataverse layout JSON) |
| `label` | `string` | Display name shown in the layout wizard checklist |
| `description` | `string` | One-line description shown in the wizard |
| `icon` | `FluentIcon` | Fluent icon for wizard and section header |
| `category` | `SectionCategory` | Grouping category: `"overview"` \| `"data"` \| `"ai"` \| `"productivity"` |
| `defaultHeight` | `string?` | Suggested default height (e.g., `"560px"`); `undefined` = auto |
| `factory` | `(ctx: SectionFactoryContext) => SectionConfig` | Produces the runtime section config for WorkspaceShell |

**`SectionFactoryContext`** — standard context passed to every section factory:

| Field | Type | Description |
|-------|------|-------------|
| `webApi` | `unknown` | Xrm.WebApi for Dataverse queries |
| `userId` | `string` | Current user's systemuserid GUID |
| `service` | `unknown` | DataverseService for document/entity operations |
| `bffBaseUrl` | `string` | BFF API base URL |
| `onNavigate` | `(target: NavigateTarget) => void` | Navigate to a Dataverse view, record, or URL |
| `onOpenWizard` | `(webResourceName, data?, options?) => void` | Open a Code Page wizard dialog |
| `onBadgeCountChange` | `(count: number) => void` | Push badge count updates to the workspace header |
| `onRefetchReady` | `(refetch: () => void) => void` | Register a refetch callback for cross-section refresh |

### `SECTION_REGISTRY` Array

Each workspace solution (e.g., `src/solutions/LegalWorkspace/src/sectionRegistry.ts`) maintains a `SECTION_REGISTRY` array — the single source of truth for available sections in that workspace. It is a `readonly SectionRegistration[]` that lists all registered sections in default display order.

Helper utilities are provided alongside the registry:

- `getSectionById(id)` — look up a registration by ID
- `getSectionsByCategory(category)` — filter registrations by category

### How to Add a New Section

1. **Create a registration file** — `src/solutions/{Workspace}/src/sections/{name}.registration.ts`

```typescript
import type { SectionRegistration } from "@spaarke/ui-components";
import { CalendarRegular } from "@fluentui/react-icons";

export const myNewSectionRegistration: SectionRegistration = {
  id: "my-new-section",
  label: "My New Section",
  description: "Displays upcoming calendar events.",
  icon: CalendarRegular,
  category: "productivity",
  defaultHeight: "400px",
  factory: (ctx) => ({
    id: "my-new-section",
    type: "content",
    title: "My New Section",
    renderContent: () => <MyComponent webApi={ctx.webApi} userId={ctx.userId} />,
  }),
};
```

2. **Register it** — import and add to the `SECTION_REGISTRY` array in `sectionRegistry.ts`:

```typescript
import { myNewSectionRegistration } from "./sections/myNewSection.registration";

export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  // ... existing registrations ...
  myNewSectionRegistration,
] as const;
```

The workspace layout wizard automatically picks up the new section from the registry. No other wiring is required.

---

## Versioning

| Version | Date | Key Changes |
|---------|------|-------------|
| 1.0.0 | Oct 2025 | Initial: DataGrid, SprkButton, themes, formatters |
| 2.0.0 | Feb 2026 | Fluent v9 selective imports, WizardShell, SidePaneShell, RichTextEditor, SprkChat, DiffCompareView, LookupField, SendEmailDialog, AiSummaryPopover, FindSimilarDialog |
| 2.1.0 | Mar 2026 | CreateRecordWizard, PlaybookLibraryShell, domain wizards (Matter, Project, Event, Todo, WorkAssignment, SummarizeFiles), IDataService/IUploadService/INavigationService interfaces, Xrm/BFF/Mock adapters |
| 2.2.0 | Mar 2026 | WorkspaceShell, SectionPanel, ActionCard/ActionCardRow, MetricCard/MetricCardRow, SectionRegistration/SectionFactoryContext types, Section Registry pattern for workspace personalization |

**Packaged tarballs**: `spaarke-ui-components-1.0.0.tgz`, `spaarke-ui-components-2.0.0.tgz`

---

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| PCF build fails with `react/jsx-runtime` | Barrel import pulls in Lexical (RichTextEditor) | Use deep imports: `@spaarke/ui-components/dist/components/{Name}` |
| Consumer uses stale component | Shared library not rebuilt after changes | Run `npm run build` in shared library first |
| tsc errors during shared library build | Pre-existing errors in ViewSelector, PageChrome, etc. | These don't block `dist/` output — safe to ignore |
| Types not found in consumer | `dist/` directory missing or outdated | Run `npm run build` to regenerate |
| Theme not applied | Missing `<FluentProvider>` wrapper | Wrap app root in `<FluentProvider theme={spaarkeLight}>` |

---

## Related Resources

| Resource | Path |
|----------|------|
| ADR-012 (full) | `docs/adr/ADR-012-shared-component-library.md` |
| ADR-012 (concise) | `.claude/adr/ADR-012-shared-components.md` |
| ADR-021 (Fluent v9) | `docs/adr/ADR-021-fluent-design-system.md` |
| ADR-022 (PCF platform libs) | `docs/adr/ADR-022-pcf-platform-libraries.md` |
| Dialog Patterns | `.claude/patterns/pcf/dialog-patterns.md` |
| Full-Page Custom Page Template | `.claude/patterns/webresource/full-page-custom-page.md` |
| Service Interfaces (source) | `src/client/shared/Spaarke.UI.Components/src/types/serviceInterfaces.ts` |
| PCF Deployment Guide | `docs/guides/PCF-DEPLOYMENT-GUIDE.md` |

---

*Last updated: 2026-03-30*
