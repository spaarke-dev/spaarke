# Frontend UI Constraints

> **Domain**: PCF Controls & React Code Pages
> **Source ADRs**: ADR-006, ADR-011, ADR-012, ADR-021, ADR-022
> **Last Updated**: 2026-02-23

---

## When to Load This File

Load when:
- Creating new PCF controls or React Code Pages
- Building model-driven app UI (forms, dialogs, standalone pages)
- Working with shared component library
- Reviewing PCF/frontend code

---

## First Decision: PCF or React Code Page?

| Situation | Use |
|-----------|-----|
| Field bound to Dataverse form (bound property, `updateView()` lifecycle) | **PCF** |
| Opened as a standalone dialog via `navigateTo` | **React Code Page** |
| Dataset embedded on a form (subgrid replacement) | **Dataset PCF** |
| Standalone list/browse page opened as dialog | **React Code Page** |
| Multi-step wizard dialog (Create Matter, etc.) | **React Code Page** |
| Side panel / filter panel inside a custom page | **React Code Page** |

---

## MUST Rules

### Architecture (ADR-006)

- ✅ **MUST** use PCF for field-bound form controls
- ✅ **MUST** use React Code Page for standalone dialogs and custom pages
- ✅ **MUST** place PCF controls in `src/client/pcf/`
- ✅ **MUST** place React Code Pages in `src/client/code-pages/`
- ✅ **MUST** keep ribbon/command bar scripts minimal (invocation only)

### PCF Controls (ADR-022)

- ✅ **MUST** use React 16 APIs (`ReactDOM.render` or ReactControl pattern)
- ✅ **MUST** declare platform libraries in `ControlManifest.Input.xml`
- ✅ **MUST** use `devDependencies` for React in PCF projects (not `dependencies`)
- ✅ **MUST** include `featureconfig.json` with `pcfReactPlatformLibraries: "on"`

### React Code Pages (ADR-006, ADR-021)

- ✅ **MUST** use React 18 `createRoot()` entry point
- ✅ **MUST** bundle React 18 + Fluent v9 in Code Page output
- ✅ **MUST** read parameters from `URLSearchParams` (passed via `navigateTo` `data` field)
- ✅ **MUST** open via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... })`

### Dataset PCF (ADR-011)

- ✅ **MUST** use Dataset PCF for list-based document UX on forms
- ✅ **MUST** implement/extend `src/client/pcf/UniversalDatasetGrid/`
- ✅ **MUST** achieve 80%+ test coverage on PCF controls

### Shared Components (ADR-012)

- ✅ **MUST** use Fluent UI v9 components exclusively
- ✅ **MUST** import shared components via `@spaarke/ui-components`
- ✅ **MUST** use semantic tokens for theming (no hard-coded colors)
- ✅ **MUST** support dark mode and high-contrast
- ✅ **MUST** export TypeScript types alongside components
- ✅ **MUST** achieve 90%+ test coverage on shared components

---

## MUST NOT Rules

### Architecture (ADR-006)

- ❌ **MUST NOT** create legacy JavaScript webresources (no-framework JS, jQuery, ad hoc scripts)
- ❌ **MUST NOT** add business logic to ribbon scripts
- ❌ **MUST NOT** make remote calls from ribbon scripts
- ❌ **MUST NOT** wrap a standalone dialog in custom page + PCF when a Code Page is simpler

### PCF Controls (ADR-022)

- ❌ **MUST NOT** use React 18 APIs in PCF controls (`createRoot`, concurrent features)
- ❌ **MUST NOT** import from `react-dom/client` in PCF code
- ❌ **MUST NOT** bundle React/Fluent in PCF output

### Dataset PCF (ADR-011)

- ❌ **MUST NOT** add new native subgrids without tech lead approval
- ❌ **MUST NOT** create bespoke JS webresources for list UX
- ❌ **MUST NOT** duplicate UI primitives (use shared library)

### Shared Components (ADR-012)

- ❌ **MUST NOT** mix Fluent UI versions (v9 only)
- ❌ **MUST NOT** reference PCF-specific APIs in shared components
- ❌ **MUST NOT** hard-code Dataverse entity names or schemas
- ❌ **MUST NOT** use custom CSS (Fluent tokens only)
- ❌ **MUST NOT** use React 18-only APIs in components intended for PCF consumption

---

## Quick Reference: Opening a Dialog

### PCF → React Code Page dialog

```typescript
// NavigationService.ts
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_documentrelationshipviewer",
        data: `documentId=${documentId}`,
    },
    { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
);
```

### Code Page: reading the parameter

```typescript
// index.tsx
const params = new URLSearchParams(window.location.search);
const documentId = params.get("documentId") ?? "";
```

---

## Directory Structure

```
src/client/
├── pcf/                                    # Field-bound PCF controls (React 16/17)
│   ├── SemanticSearchControl/              # Form-embedded document search
│   ├── UniversalDatasetGrid/              # Dataset PCF for form subgrids
│   └── DocumentRelationshipViewer/        # (legacy — migrate to code-pages/)
├── code-pages/                            # Standalone dialogs & pages (React 18)
│   ├── DocumentRelationshipViewer/        # Graph visualization dialog
│   ├── CreateMatterWizard/                # Multi-step matter creation
│   └── DocumentUploadDialog/              # File upload wizard
├── shared/
│   └── Spaarke.UI.Components/             # Shared React library (both surfaces)
│       ├── src/components/layout/         # WizardDialog, SidePanel, PageLayout
│       ├── src/components/data/           # DataGrid, FilterPanel, CommandBar
│       ├── src/components/feedback/       # LoadingState, EmptyState, ErrorState
│       ├── src/hooks/
│       ├── src/theme/
│       └── src/utils/
└── office-addins/                         # Office Add-ins (React 18)
```

---

## Pattern Files

- [Dialog Patterns](../patterns/pcf/dialog-patterns.md) — PCF dialog close, Code Page dialog opening
- [PCF Control Initialization](../patterns/pcf/control-initialization.md) — Lifecycle and React root
- [Theme Management](../patterns/pcf/theme-management.md) — Dark mode and theme resolution
- [Dataverse Queries](../patterns/pcf/dataverse-queries.md) — WebAPI and environment variables
- [Error Handling](../patterns/pcf/error-handling.md) — Error boundaries and user experience

---

## Source ADRs

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-006](../adr/ADR-006-pcf-over-webresources.md) | Two-tier: PCF vs Code Page | Surface selection |
| [ADR-011](../adr/ADR-011-dataset-pcf.md) | Dataset PCF vs subgrids | List/grid decisions |
| [ADR-012](../adr/ADR-012-shared-components.md) | Shared library | Component governance |
| [ADR-021](../adr/ADR-021-fluent-design-system.md) | Fluent v9 + React version split | Styling, theming |
| [ADR-022](../adr/ADR-022-pcf-platform-libraries.md) | PCF platform libraries | PCF-specific React rules |

---

**Lines**: ~165
**Purpose**: Single-file reference for all frontend UI development constraints
