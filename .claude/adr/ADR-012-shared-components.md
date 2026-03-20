# ADR-012: Shared Component Library (Concise)

> **Status**: Accepted (Revised 2026-03-19)
> **Domain**: Frontend Architecture
> **Last Updated**: 2026-03-19

---

## Decision

Maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` as the **single source of truth** for all reusable UI — components, hooks, services, types, and themes. The library is consumed by PCF controls (React 16/17), Code Pages (React 19), and the Power Pages SPA.

**Rationale**: Prevents code duplication, ensures consistent UX, and centralizes maintenance. All wizard, dialog, shell, and grid components belong in the shared library — not duplicated per-solution.

---

## Constraints

### MUST

- **MUST** use Fluent UI v9 components exclusively
- **MUST** import shared components via `@spaarke/ui-components`
- **MUST** use semantic tokens for theming (no hard-coded colors)
- **MUST** support dark mode and high-contrast
- **MUST** match model-driven app interaction patterns
- **MUST** export TypeScript types alongside components
- **MUST** achieve 90%+ test coverage on shared components
- **MUST** author components to be React 19-compatible (used in Code Pages)
- **MUST** verify React 16/17 compatibility for components consumed by PCF
- **MUST** use **runtime-context abstractions** for platform-specific operations (see Service Architecture below)

### MUST NOT

- **MUST NOT** mix Fluent UI versions (v9 only)
- **MUST NOT** reference PCF-specific APIs (`ComponentFramework.*`) in shared components
- **MUST NOT** hard-code Dataverse entity names or schemas as string literals (use configurable entity maps)
- **MUST NOT** use custom CSS (Fluent tokens only)
- **MUST NOT** use React 18/19-only APIs (`useTransition`, `useDeferredValue`) in components intended for PCF
- **MUST NOT** export components without tests
- **MUST NOT** call `Xrm.WebApi` or other platform APIs directly — accept via abstraction

---

## Service Architecture: What Belongs in Shared Library

The original constraint ("zero service dependencies, callback-based only") was too rigid. The shared library already contains services (`CommandRegistry`, `FetchXmlService`, `EntityCreationService`, etc.) and this is correct — services with **abstracted dependencies** are portable and testable.

### Service Portability Tiers

| Tier | What It Means | OK in Shared Library? | Examples |
|------|--------------|----------------------|----------|
| **Pure logic** | No I/O, no platform APIs, no side effects | Yes | Validators, formatters, transformers, reducers |
| **Abstracted I/O** | Accepts data service interface via props/constructor; never calls platform APIs directly | Yes | Wizard orchestrators, entity creation services, upload services |
| **Platform-bound** | Calls `Xrm.WebApi`, `ComponentFramework`, `window.parent.Xrm` directly | **No** — keep in consumer | PCF `index.ts`, Code Page `main.tsx`, ribbon scripts |

### The IDataService Pattern

Services that need to read/write data accept an abstraction, not a concrete API. Three core interfaces live in `types/`:

| Interface | Methods |
|-----------|---------|
| `IDataService` | `createRecord`, `retrieveRecord`, `retrieveMultipleRecords`, `updateRecord`, `deleteRecord` |
| `IUploadService` | `uploadFile`, `getContainerIdForEntity` |
| `INavigationService` | `openRecord`, `openDialog`, `closeDialog` |

Supporting types: `UploadOptions`, `UploadResult`, `DialogOptions`, `DialogResult`.

**Pre-built adapters** in `utils/adapters/` wire interfaces to concrete platforms:

| Adapter Factory | Target Platform |
|----------------|-----------------|
| `createXrmDataService()` | Xrm.WebApi (Code Pages in Dataverse) |
| `createXrmUploadService(bffBaseUrl)` | BFF API upload endpoints |
| `createXrmNavigationService()` | Xrm.Navigation |
| `createBffDataService(authFetch, bffBaseUrl)` | BFF API (Power Pages SPA) |
| `createBffUploadService(authFetch, bffBaseUrl)` | BFF API (SPA) |
| `createBffNavigationService(navigate?)` | SPA router |
| `createMockDataService()` | jest.fn() stubs for unit tests |
| `createMockUploadService()` | jest.fn() stubs |
| `createMockNavigationService()` | jest.fn() stubs |

Consumers call the factory in their `main.tsx` and pass the result as props — shared components never touch platform APIs directly.

### What Goes Where

| In Shared Library | In Consumer (Code Page / PCF / SPA) |
|-------------------|-------------------------------------|
| Wizard step components and orchestration | `main.tsx` entry point with platform init |
| Entity-specific form components (CreateMatterStep, etc.) | `Xrm.WebApi` / BFF adapter implementation |
| Upload service logic (dedup, validation, progress) | Platform-specific auth (`@spaarke/auth` init) |
| Business rules (validation, field defaults) | `navigateTo` / dialog opening code |
| Shell components (WizardShell, PlaybookLibraryShell) | Theme detection bootstrap |
| Shared service interfaces (`IDataService`, `IUploadService`) | Concrete service implementations |

---

## Component Inventory (v2.0.0+)

### Shell Components

| Component | Description | PCF Safe? |
|-----------|-------------|-----------|
| WizardShell / Stepper / SuccessScreen | Multi-step wizard frame | Yes |
| CreateRecordWizard | Record-creation boilerplate (file upload, follow-on steps) | Yes |
| PlaybookLibraryShell (planned) | Playbook browsing, selection, execution | Code Pages only |

### Domain Wizard Components (extracted from LegalWorkspace)

| Component | Files | PCF Safe? |
|-----------|-------|-----------|
| CreateMatterWizard | 17 files | Yes (uses CreateRecordWizard) |
| CreateProjectWizard | 10 files | Yes (uses CreateRecordWizard) |
| CreateEventWizard | 5 files | Yes (uses CreateRecordWizard) |
| CreateTodoWizard | 5 files | Yes (uses CreateRecordWizard, todoflag=true) |
| CreateWorkAssignmentWizard | 10 files | Yes (uses WizardShell directly) |
| SummarizeFilesWizard | 9 files | Code Pages only |
| PlaybookLibraryShell | 2 files | Code Pages only (intent pre-selection, browse/execute) |
| DocumentUploadWizard | — | Yes |
| FindSimilarDialog | — | Yes |

### Utilities (utils/)

| Utility | Description |
|---------|-------------|
| `resolveCodePageTheme()` | 4-level cascade: localStorage → URL param → navbar DOM → system preference |
| `setupCodePageThemeListener(callback)` | Reactive theme change listener (cross-tab + system) |
| `parseDataParams()` | Parse Xrm.Navigation.navigateTo data envelope + raw URL params |

### UI Components (16 groups)

| Component | Description | PCF Safe? |
|-----------|-------------|-----------|
| SprkButton | Button with tooltip | Yes |
| DatasetGrid (Grid/Card/List/Virtualized) | Multi-view dataset | Yes |
| ViewSelector | View mode switcher | Yes |
| CommandToolbar | Action bar | Yes |
| PageChrome | Page header (OOB parity) | Yes |
| ChoiceDialog | Simple choice dialog | Yes |
| SidePaneShell | Slide-in side panel | Yes |
| DiffCompareView | AI diff viewer | Yes |
| LookupField | Search-as-you-type lookup | Yes |
| SendEmailDialog | Email composition | Yes |
| AiSummaryPopover | AI summary with lazy fetch | Yes (deep import) |
| FindSimilarDialog | Iframe dialog shell | Yes (deep import) |
| RichTextEditor | Lexical WYSIWYG | **No** (needs jsx-runtime) |
| SprkChat | SSE streaming chat | Code Pages only |
| FileUpload | Drag-and-drop upload zone | Yes |
| AiFieldTag | AI badge for pre-filled fields | Yes |

### Code Page Wrappers (in src/solutions/, NOT in shared library)

| Wrapper | Web Resource | Description |
|---------|-------------|-------------|
| CreateMatterWizard | `sprk_creatematterwizard` | ~40 LOC thin wrapper |
| CreateProjectWizard | `sprk_createprojectwizard` | ~40 LOC thin wrapper |
| CreateEventWizard | `sprk_createeventwizard` | ~40 LOC thin wrapper |
| CreateTodoWizard | `sprk_createtodowizard` | ~40 LOC thin wrapper |
| CreateWorkAssignmentWizard | `sprk_createworkassignmentwizard` | ~40 LOC thin wrapper |
| SummarizeFilesWizard | `sprk_summarizefileswizard` | ~40 LOC thin wrapper |
| FindSimilarCodePage | `sprk_findsimilar` | Direct iframe rendering |
| PlaybookLibrary | `sprk_playbooklibrary` | Supports intent param |

### Hooks (18), Services (19+), Types (14+)

See full inventory in [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md).

---

## PCF Import Pattern (Critical)

PCF controls **must use deep imports** to avoid pulling in Lexical/RichTextEditor which requires `react/jsx-runtime` (unavailable in React 16):

```typescript
// ✅ PCF — deep import
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
import { AiSummaryPopover } from "@spaarke/ui-components/dist/components/AiSummaryPopover";

// ❌ PCF — barrel import pulls in ALL components
import { FindSimilarDialog } from "@spaarke/ui-components";

// ✅ Code Pages — barrel is safe (React 19 has jsx-runtime)
import { FindSimilarDialog, WizardShell } from "@spaarke/ui-components";
```

---

## When to Add to Shared Library

| Add to Shared Library | Keep in Consumer |
|----------------------|------------------|
| Used by 2+ modules/surfaces | Truly module-specific rendering logic |
| Core Spaarke UX pattern (wizard, shell, grid) | Platform bootstrap code (`main.tsx`, PCF `index.ts`) |
| Service with abstracted dependencies (`IDataService`) | Concrete platform API calls (`Xrm.WebApi`) |
| Entity-specific wizard content (steps, forms) | One-off experimental UI |
| Business rules (validation, defaults, transformations) | — |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Code Pages are default surface; shared library consumed by both |
| [ADR-021](ADR-021-fluent-design-system.md) | All components use Fluent v9 tokens |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF uses React 16 (platform); Code Pages use React 19 (bundled) |
| [ADR-026](ADR-026-full-page-custom-page-standard.md) | Build tooling for Code Pages (Vite + singlefile) |

---

## Source Documentation

- **Full ADR**: [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md)
- **Developer Guide**: [docs/guides/SHARED-UI-COMPONENTS-GUIDE.md](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md)

---

**Lines**: ~217
