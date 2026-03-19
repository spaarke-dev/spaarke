# ADR-012: Shared Component Library for React/TypeScript Across Modules

| Field | Value |
|-------|-------|
| Status | **Accepted** (Revised) |
| Date | 2025-10-03 |
| Updated | 2026-03-19 |
| Authors | Spaarke Engineering |
| Sprint | Sprint 5 - Universal Dataset PCF |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-012 Concise](../../.claude/adr/ADR-012-shared-components.md) - ~150 lines, decision + constraints + service architecture
- [PCF Constraints](../../.claude/constraints/pcf.md) - MUST/MUST NOT rules for PCF development
- [React Hooks Pattern](../../.claude/patterns/pcf/react-hooks.md) - Shared hooks examples

**When to load this full ADR**: Component examples, directory structure, governance model, service portability details

---

## Context

Spaarke builds multiple front-end surfaces:
1. **PCF Controls** (model-driven app forms — field-bound) - TypeScript/React 16/17 (platform-provided)
2. **React Code Pages** (standalone dialogs/pages opened via navigateTo) - TypeScript/React 19 (bundled)
3. **Office Add-ins** - TypeScript/React 19 (bundled)
4. **Power Pages SPA** (external portal) - React 19 (bundled)

Without a shared component library, we risk:
- **Code duplication** - Implementing the same UI components multiple times
- **Inconsistent UX** - Different look-and-feel across surfaces
- **Maintenance burden** - Fixing bugs or updating styles in multiple places
- **No reusability** - Wizard components trapped inside specific solutions, unreachable from other contexts

### Current State
- **BFF API** (`src/server/api/Sprk.Bff.Api/`) - .NET backend
- **PCF Controls** (`src/client/pcf/`) - React/TypeScript
- **Code Pages** (`src/solutions/`) - Standalone React 19 apps
- **Shared library** (`src/client/shared/Spaarke.UI.Components/`) - shared React/TS library
- **Power Pages SPA** (`src/client/external-spa/`) - External portal

---

## Decision

**Maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` as the single source of truth for all reusable UI.**

The library provides:
1. **Reusable React components** (Fluent UI v9 based) — including shells, wizards, grids, dialogs
2. **Domain-specific wizard content** — entity creation wizards, upload wizards (with abstracted data access)
3. **Shared TypeScript utilities** (formatters, transformers, validators)
4. **Common types and interfaces** (DTOs, domain models, service abstractions)
5. **Theme definitions** (Spaarke light/dark themes)
6. **Shared hooks** (data fetching, caching, state management)
7. **Shared services** (with abstracted dependencies — see Service Architecture)

### UI/UX Standards (Required)

| Rule | Requirement |
|------|-------------|
| **Fluent UI everywhere** | All custom UI must use Fluent UI v9 components and design tokens |
| **Power Apps MDA fit** | Custom UI embedded in model-driven apps must match MDA patterns |
| **No hard-coded styling** | No hard-coded colors — use semantic tokens and theming |
| **Dark-mode compatible** | Everything must render correctly in dark mode and high-contrast |
| **Accessibility** | WCAG-aligned behavior (keyboard nav, focus states, contrast) |

---

## Service Architecture: Portability Through Abstraction

### The Evolution

The original ADR-012 (v1.3) required "callback-based props with zero service dependencies." This constraint was too rigid — the shared library already contained services (`CommandRegistry`, `FetchXmlService`, `EntityCreationService`) that worked correctly because they used abstracted dependencies, not direct platform API calls.

**The real principle**: shared components and services must be **portable across runtime contexts** (Dataverse model-driven app, Power Pages SPA, unit tests). This is achieved through dependency abstraction, not by banning services entirely.

### Service Portability Tiers

| Tier | Description | In Shared Library? | Examples |
|------|-------------|-------------------|----------|
| **Pure logic** | No I/O, no platform APIs, no side effects | Yes | Validators, formatters, reducers, transformers, field mapping rules |
| **Abstracted I/O** | Accepts data service interface via props or constructor; never calls platform APIs directly | Yes | Wizard orchestrators, entity creation services, upload coordinators, playbook services |
| **Platform-bound** | Directly calls `Xrm.WebApi`, `ComponentFramework`, `window.parent.Xrm`, or BFF endpoints | **No** — keep in consumer | Code Page `main.tsx`, PCF `index.ts`, ribbon scripts, SPA API clients |

### The IDataService Abstraction

Services that need to read/write Dataverse data accept an interface, not a concrete API:

```typescript
// Defined in @spaarke/ui-components/types
export interface IDataService {
  createRecord(entityName: string, data: Record<string, unknown>): Promise<string>;
  retrieveRecord(entityName: string, id: string, options?: string): Promise<Record<string, unknown>>;
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
  updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<void>;
  deleteRecord(entityName: string, id: string): Promise<void>;
}

export interface IUploadService {
  uploadFile(containerId: string, file: File, onProgress?: (pct: number) => void): Promise<string>;
  getContainerIdForEntity(entityName: string, entityId: string): Promise<string>;
}

export interface INavigationService {
  openRecord(entityName: string, entityId: string): void;
  openDialog(webresourceName: string, data: string, options?: { width: number; height: number }): Promise<void>;
  closeDialog(): void;
}
```

### Adapter Pattern (Consumers Provide Implementations)

```typescript
// ✅ Code Page main.tsx — Xrm.WebApi adapter
const xrmDataService: IDataService = {
  createRecord: (entity, data) => Xrm.WebApi.createRecord(entity, data).then(r => r.id),
  retrieveRecord: (entity, id, opts) => Xrm.WebApi.retrieveRecord(entity, id, opts),
  retrieveMultipleRecords: (entity, opts) => Xrm.WebApi.retrieveMultipleRecords(entity, opts),
  updateRecord: (entity, id, data) => Xrm.WebApi.updateRecord(entity, id, data),
  deleteRecord: (entity, id) => Xrm.WebApi.deleteRecord(entity, id),
};

// ✅ Power Pages SPA — BFF API adapter
const bffDataService: IDataService = {
  createRecord: (entity, data) =>
    authenticatedFetch(`/api/${entity}`, { method: "POST", body: JSON.stringify(data) })
      .then(r => r.json()).then(j => j.id),
  retrieveRecord: (entity, id, opts) =>
    authenticatedFetch(`/api/${entity}/${id}?${opts || ""}`).then(r => r.json()),
  // ...
};

// ✅ Unit tests — mock adapter
const mockDataService: IDataService = {
  createRecord: jest.fn().mockResolvedValue("mock-id"),
  retrieveRecord: jest.fn().mockResolvedValue({ name: "Test" }),
  retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
  updateRecord: jest.fn().mockResolvedValue(undefined),
  deleteRecord: jest.fn().mockResolvedValue(undefined),
};
```

### Wizard Component Example

```typescript
// ✅ Shared library — portable wizard component
export interface ICreateMatterWizardProps {
  dataService: IDataService;
  uploadService: IUploadService;
  navigationService: INavigationService;
  currentUserId: string;
  embedded?: boolean;  // true = skip Fluent Dialog (Dataverse chrome provides it)
  onClose: () => void;
}

export const CreateMatterWizard: React.FC<ICreateMatterWizardProps> = (props) => {
  // All data access goes through props.dataService
  // All file uploads go through props.uploadService
  // All navigation goes through props.navigationService
  // Zero direct platform API calls
  return <WizardShell embedded={props.embedded} ... />;
};
```

### What Goes Where (Decision Guide)

| In Shared Library (`@spaarke/ui-components`) | In Consumer (Code Page / PCF / SPA) |
|----------------------------------------------|--------------------------------------|
| `WizardShell`, `CreateRecordWizard`, `PlaybookLibraryShell` | `main.tsx` — platform init, `createRoot`, theme detection |
| Entity-specific wizard components (steps, forms) | `IDataService` adapter (Xrm.WebApi or BFF) |
| Service interfaces (`IDataService`, `IUploadService`) | `IUploadService` adapter (SDAP client or BFF) |
| Business logic (validation, field defaults, transformations) | `INavigationService` adapter (Xrm.Navigation or SPA router) |
| Upload coordination (dedup, validation, progress tracking) | Auth initialization (`@spaarke/auth`) |
| Entity schema maps (configurable, not hard-coded) | `navigateTo` / dialog opening code |

---

## Architecture

### Directory Structure (Current + Planned)

```
src/client/shared/Spaarke.UI.Components/
├── package.json                    # @spaarke/ui-components v2.0.0
├── tsconfig.json                   # ES2020 target, React JSX, strict mode
├── src/
│   ├── index.ts                    # Main barrel export
│   │
│   ├── components/
│   │   ├── Wizard/                 # Shell: multi-step wizard frame
│   │   ├── CreateRecordWizard/     # Generic record-creation boilerplate
│   │   ├── PlaybookLibraryShell/   # (PLANNED) Playbook browsing/execution shell
│   │   │
│   │   ├── CreateMatterWizard/     # (EXTRACTING) Matter wizard content
│   │   ├── CreateProjectWizard/    # (EXTRACTING) Project wizard content
│   │   ├── CreateEventWizard/      # (EXTRACTING) Event wizard content
│   │   ├── CreateTodoWizard/       # (EXTRACTING) Todo wizard content
│   │   ├── CreateWorkAssignmentWizard/ # (EXTRACTING) Work assignment wizard
│   │   ├── DocumentUploadWizard/   # (EXTRACTING) Upload wizard content
│   │   ├── SummarizeFilesWizard/   # (EXTRACTING) Summarize wizard content
│   │   ├── FindSimilarDialog/      # (EXTRACTING) Semantic search dialog
│   │   │
│   │   ├── DatasetGrid/            # Multi-view dataset renderer
│   │   ├── Toolbar/                # Command/action bar
│   │   ├── PageChrome/             # Page header (OOB parity)
│   │   ├── SidePane/               # Slide-in panel
│   │   ├── ChoiceDialog/           # Simple choice dialog
│   │   ├── LookupField/            # Search-as-you-type lookup
│   │   ├── FileUpload/             # Drag-and-drop upload zone
│   │   ├── SendEmailDialog/        # Email composition
│   │   ├── RichTextEditor/         # Lexical WYSIWYG (Code Pages only)
│   │   ├── SprkChat/               # SSE streaming chat
│   │   ├── DiffCompareView/        # AI diff viewer
│   │   ├── AiSummaryPopover/       # AI summary popover
│   │   ├── AiFieldTag/             # AI badge for pre-filled fields
│   │   ├── AiProgressStepper/      # Multi-step AI progress
│   │   ├── InlineAiToolbar/        # Floating toolbar on selection
│   │   ├── SlashCommandMenu/       # Command palette via /
│   │   ├── MiniGraph/              # Lightweight relationship graph
│   │   └── RelationshipCountCard/  # Relationship count with drill
│   │
│   ├── hooks/                      # 18 shared React hooks
│   ├── services/                   # 19+ shared services (abstracted I/O)
│   ├── types/                      # 14 type files + service interfaces
│   ├── theme/                      # Spaarke brand, light/dark themes
│   ├── utils/                      # Formatters, helpers, theme detection
│   └── icons/                      # Fluent v9 icon registry
│
├── dist/                           # Compiled output (tsc)
├── __tests__/                      # Component tests
└── __mocks__/                      # Jest mock fixtures
```

### Consumption Patterns

**PCF Control (React 16/17, platform-provided):**
```json
{
  "dependencies": {
    "@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components"
  },
  "devDependencies": {
    "@types/react": "^16.14.0",
    "react": "^16.14.0",
    "@fluentui/react-components": "^9.46.0"
  }
}
```

**Code Page (React 19, bundled):**
```json
{
  "dependencies": {
    "@spaarke/ui-components": "file:../../client/shared/Spaarke.UI.Components",
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "@fluentui/react-components": "^9.54.0"
  }
}
```

**Power Pages SPA (React 19, bundled):**
```json
{
  "dependencies": {
    "@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components",
    "react": "^19.0.0",
    "react-dom": "^19.0.0"
  }
}
```

---

## PCF Import Pattern (Critical)

PCF controls **must use deep imports** to avoid pulling in Lexical/RichTextEditor which requires `react/jsx-runtime` (unavailable in React 16):

```typescript
// ✅ PCF — deep import
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
import { AiSummaryPopover } from "@spaarke/ui-components/dist/components/AiSummaryPopover";

// ❌ PCF — barrel import pulls in ALL components (breaks on jsx-runtime)
import { FindSimilarDialog } from "@spaarke/ui-components";

// ✅ Code Pages / SPA — barrel is safe (React 19 has jsx-runtime)
import { FindSimilarDialog, WizardShell, CreateMatterWizard } from "@spaarke/ui-components";
```

---

## Component Ownership & Governance

### When to Add to Shared Library

| Add to Shared Library | Keep in Consumer |
|----------------------|------------------|
| Used by 2+ modules/surfaces | Truly module-specific rendering |
| Core Spaarke UX pattern (wizard, shell, grid) | Platform bootstrap (`main.tsx`, PCF `index.ts`) |
| Service with abstracted dependencies | Concrete platform API calls |
| Entity-specific wizard content (steps, forms) | One-off experimental UI |
| Business rules and validations | — |

### Migration Path (Existing Code → Shared Library)

1. **Abstract**: Replace direct `Xrm.WebApi` calls with `IDataService` interface
2. **Extract**: Move to `src/client/shared/Spaarke.UI.Components/src/components/`
3. **Test**: Add unit tests using mock data service adapter
4. **Document**: Add JSDoc and update component inventory
5. **Replace**: Update consumers to import from `@spaarke/ui-components`

---

## Consequences

### Positive

- **Single source of truth**: Update wizard logic once, all consumers get the update
- **Cross-context reuse**: Same wizard works in Dataverse, Power Pages SPA, and tests
- **Testability**: Mock adapters make services fully unit-testable
- **Independent deployment**: Code Page wrappers are thin (~30-50 LOC); shared library carries the logic
- **Consistent UX**: Unified design across all surfaces

### Negative

- **Abstraction overhead**: `IDataService` pattern adds indirection vs direct `Xrm.WebApi` calls
- **Version coordination**: Breaking changes in shared library require updating all consumers
- **Build order**: Shared library must build before consumers
- **Initial extraction effort**: Moving wizard content from LegalWorkspace to shared library

---

## Compliance Checklist

Use as a **pass/fail** review gate for PRs that add or change shared components.

**Design system & UX:**
- Uses Fluent UI v9 components and tokens exclusively
- Matches model-driven app interaction patterns when embedded

**Theming:**
- No hard-coded colors; styling is token-driven
- Renders correctly with both `spaarkeLight` and `spaarkeDark` themes
- Icons/visuals work in dark mode and high-contrast

**Accessibility:**
- Keyboard navigation works; visible focus states preserved
- Text/icon contrast meets WCAG 2.1 AA

**Service architecture:**
- No direct platform API calls (`Xrm.WebApi`, `ComponentFramework`)
- Data access goes through `IDataService` or similar abstraction
- Service is testable with mock adapter

**Packaging:**
- Public exports are intentional (barrel exports)
- TypeScript types exported alongside components
- Library `peerDependencies` specifies `"react": ">=16.14.0"`
- Components targeting PCF do NOT use React 18/19-only APIs

---

## Related ADRs

- [ADR-006: UI Surface Architecture](./ADR-006-prefer-pcf-over-webresources.md) - Code Pages as default surface
- [ADR-021: Fluent UI v9 Design System](./ADR-021-fluent-ui-design-system.md) - Design system standard
- [ADR-022: PCF Platform Libraries](./ADR-022-pcf-platform-libraries.md) - React version by surface
- [ADR-026: Full-Page Custom Page Standard](./ADR-026-full-page-custom-page-standard.md) - Build tooling

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-10-03 | 1.0 | Initial ADR creation | Spaarke Engineering |
| 2025-12-12 | 1.1 | Added UI/UX standards and compliance checklist | Spaarke Engineering |
| 2026-02-23 | 1.2 | Updated for two-tier architecture; PCF React 16, Code Pages React 18; widened peerDeps | Spaarke Engineering |
| 2026-03-10 | 1.3 | Updated component inventory to v2.0.0. Added PCF deep import pattern. Added callback-based props constraint. | Spaarke Engineering |
| 2026-03-19 | 2.0 | **Major revision**: Replaced rigid "zero service dependencies / callback-based only" constraint with **service portability tiers** and `IDataService` abstraction pattern. Added domain wizard components (CreateMatter, CreateProject, etc.) to shared library inventory as extraction targets. Added IDataService, IUploadService, INavigationService interface definitions. Updated React version to 19 per ADR-021. Added Power Pages SPA as consumer. Added adapter pattern examples (Xrm, BFF, mock). Updated compliance checklist for service architecture. | Spaarke Engineering |
