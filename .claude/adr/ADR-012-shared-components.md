# ADR-012: Shared Component Library (Concise)

> **Status**: Accepted (Revised 2026-02-23)
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2026-02-23

---

## Decision

Maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` for reuse across **both PCF controls and React Code Pages**. The library must be React 18-compatible (consumed by Code Pages), and tested against React 16/17 for PCF compatibility.

**Rationale**: Prevents code duplication, ensures consistent UX, and centralizes maintenance. The wizard, side panel, filter panel, and grid primitives are used by multiple surfaces — they belong in the shared library, not recreated per-dialog.

---

## Constraints

### ✅ MUST

- **MUST** use Fluent UI v9 components exclusively
- **MUST** import shared components via `@spaarke/ui-components`
- **MUST** use semantic tokens for theming (no hard-coded colors)
- **MUST** support dark mode and high-contrast
- **MUST** match model-driven app interaction patterns
- **MUST** export TypeScript types alongside components
- **MUST** achieve 90%+ test coverage on shared components
- **MUST** author components to be React 18-compatible (used in Code Pages)
- **MUST** verify React 16/17 compatibility for components consumed by PCF

### ❌ MUST NOT

- **MUST NOT** mix Fluent UI versions (v9 only)
- **MUST NOT** reference PCF-specific APIs (`ComponentFramework.*`) in shared components
- **MUST NOT** hard-code Dataverse entity names or schemas
- **MUST NOT** use custom CSS (Fluent tokens only)
- **MUST NOT** use React 18-only APIs (`useTransition`, `useDeferredValue`) in components intended for PCF
- **MUST NOT** export components without tests

---

## Library Structure

```
src/client/shared/Spaarke.UI.Components/
├── src/
│   ├── components/
│   │   ├── layout/
│   │   │   ├── WizardDialog/       # Multi-step wizard (Create Matter, etc.)
│   │   │   ├── SidePanel/          # Slide-in panel (filter pane, detail pane)
│   │   │   └── PageLayout/         # Standard page with header + main + sidebar
│   │   ├── data/
│   │   │   ├── DataGrid/           # Virtualized grid with selection/bulk ops
│   │   │   ├── CommandBar/         # Action bar for grids and pages
│   │   │   └── FilterPanel/        # Faceted filter panel
│   │   ├── feedback/
│   │   │   ├── StatusBadge/        # Status indicator
│   │   │   ├── LoadingState/       # Skeleton loaders
│   │   │   ├── EmptyState/         # Zero-results state
│   │   │   └── ErrorState/         # Error with retry
│   │   └── navigation/
│   │       └── StepIndicator/      # Wizard step progress
│   ├── hooks/
│   │   ├── useDataverseFetch/      # Dataverse WebAPI wrapper
│   │   ├── usePagination/
│   │   └── useSelection/
│   ├── services/
│   │   ├── EntityMetadataService/
│   │   └── FormatterService/
│   ├── types/                      # Shared TypeScript types
│   ├── theme/
│   │   ├── spaarkeLight.ts
│   │   └── spaarkeDark.ts
│   └── utils/                      # formatters, transformers, validators
└── package.json                    # @spaarke/ui-components
```

---

## Standard Layout Templates

### WizardDialog (multi-step form)

Used by: Create Matter wizard, Create Project wizard, Document Upload wizard.

```tsx
import { WizardDialog, WizardStep } from "@spaarke/ui-components";

<WizardDialog
    title="Create New Matter"
    steps={[
        { id: "files", label: "Add file(s)", icon: <DocumentAdd20Regular /> },
        { id: "record", label: "Create record", icon: <FormNew20Regular /> },
        { id: "next", label: "Next Steps", icon: <CheckmarkCircle20Regular /> },
    ]}
    activeStep="files"
    onCancel={handleCancel}
    onNext={handleNext}
    onComplete={handleComplete}
>
    <AddFilesStep onFilesSelected={setFiles} />
</WizardDialog>
```

### SidePanel (filter / detail pane)

Used by: Events calendar filter, Document details pane, date range picker pane.

```tsx
import { SidePanel } from "@spaarke/ui-components";

<SidePanel
    title="Date Filter: Event"
    position="end"         // "start" | "end"
    width={320}
    open={isPanelOpen}
    onDismiss={() => setPanelOpen(false)}
>
    <DateRangeFilter onApply={handleFilter} onClear={handleClear} />
</SidePanel>
```

---

## Consumption in PCF (React 16/17)

```typescript
import { DataGrid, StatusBadge, FilterPanel } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

ReactDOM.render(   // React 16 API
    <FluentProvider theme={spaarkeLight}>
        <DataGrid items={items} columns={columns} />
    </FluentProvider>,
    this.container
);
```

## Consumption in Code Page (React 18)

```typescript
import { WizardDialog, SidePanel } from "@spaarke/ui-components";
import { createRoot } from "react-dom/client";  // React 18

createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={webLightTheme}>
        <WizardDialog ... />
    </FluentProvider>
);
```

---

## When to Add to Shared Library

| Add to Shared Library | Keep in Module |
|----------------------|----------------|
| Used by 2+ modules/surfaces | Module-specific logic |
| Core Spaarke UX pattern (wizard, side panel) | Experimental/POC |
| Clear, reusable API with props | Tight coupling to business context |
| Layout primitive | One-off dialog with unique flow |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Shared library consumed by both PCF and Code Pages |
| [ADR-011](ADR-011-dataset-pcf.md) | DataGrid, CommandBar used by Dataset PCF |
| [ADR-021](ADR-021-fluent-design-system.md) | All components use Fluent v9 tokens |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md)

---

**Lines**: ~130
