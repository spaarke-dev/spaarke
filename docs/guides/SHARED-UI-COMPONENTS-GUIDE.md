# Shared UI Components Guide (`@spaarke/ui-components`)

> **Version**: 2.0.0
> **Location**: `src/client/shared/Spaarke.UI.Components/`
> **ADR**: [ADR-012](../adr/ADR-012-shared-component-library.md)
> **Last Updated**: 2026-03-10

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

**Pre-existing tsc errors**: The library has known tsc errors in ViewSelector, PageChrome, RichTextEditor, and SprkChat. These do not block emit (`noEmitOnError` is not set), so `dist/` is always produced.

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

### React Code Pages (React 18)

Code Pages bundle React 18 via Vite/webpack. Barrel imports are safe:

```typescript
// ✅ Both work for Code Pages
import { AiSummaryPopover, FindSimilarDialog, WizardShell } from "@spaarke/ui-components";
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
```

### Import Path Summary

| Consumer | Import Pattern | Why |
|----------|---------------|-----|
| PCF (React 16/17) | `@spaarke/ui-components/dist/components/{Name}` | Avoids pulling Lexical/jsx-runtime deps |
| Code Page (React 18) | `@spaarke/ui-components` | Barrel is safe; React 18 has jsx-runtime |
| LegalWorkspace (Vite) | `@spaarke/ui-components` | Barrel is safe; Vite tree-shakes |

---

## Component Inventory

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
| **SidePaneShell** | Reusable slide-in side panel layout | Code Pages |
| **SprkChat** | Streaming chat component with SSE | Code Pages |
| **DiffCompareView** | AI revision diff (side-by-side + inline) | Code Pages |
| **LookupField** | Search-as-you-type entity lookup | Code Pages |
| **SendEmailDialog** | Email composition dialog with To lookup | Code Pages |
| **AiSummaryPopover** | AI summary popover with lazy fetch and copy | PCF, Code Pages |
| **FindSimilarDialog** | Iframe dialog shell for DocumentRelationshipViewer | PCF, Code Pages |
| **WizardShell** | Multi-step wizard container with stepper | Code Pages |
| **WizardStepper** | Step indicator UI for wizards | Code Pages |
| **WizardSuccessScreen** | Success state after wizard completion | Code Pages |

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
| `WebApiLike` | Dataverse WebAPI abstraction |
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

## Versioning

| Version | Date | Key Changes |
|---------|------|-------------|
| 1.0.0 | Oct 2025 | Initial: DataGrid, SprkButton, themes, formatters |
| 2.0.0 | Feb 2026 | Fluent v9 selective imports, WizardShell, SidePaneShell, RichTextEditor, SprkChat, DiffCompareView, LookupField, SendEmailDialog, AiSummaryPopover, FindSimilarDialog |

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
| PCF Deployment Guide | `docs/guides/PCF-DEPLOYMENT-GUIDE.md` |

---

*Last updated: 2026-03-10*
