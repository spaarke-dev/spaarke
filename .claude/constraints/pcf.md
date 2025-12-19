# PCF Control Constraints

> **Domain**: PCF/Frontend Development
> **Source ADRs**: ADR-006, ADR-011, ADR-012
> **Last Updated**: 2025-12-19

---

## When to Load This File

Load when:
- Creating new PCF controls
- Building model-driven app UI
- Working with shared component library
- Reviewing PCF/frontend code

---

## MUST Rules

### Architecture (ADR-006)

- ✅ **MUST** build new interactive UI as PCF controls
- ✅ **MUST** place PCF controls in `src/client/pcf/`
- ✅ **MUST** use React for SPA surfaces (Power Pages, add-ins)
- ✅ **MUST** keep ribbon/command bar scripts minimal (invocation only)

### Dataset PCF (ADR-011)

- ✅ **MUST** use Dataset PCF for list-based document UX
- ✅ **MUST** implement/extend `src/client/pcf/UniversalDatasetGrid/`
- ✅ **MUST** achieve 80%+ test coverage on PCF controls
- ✅ **MUST** include Storybook stories for components
- ✅ **MUST** use virtual scrolling for large lists

### Shared Components (ADR-012)

- ✅ **MUST** use Fluent UI v9 components exclusively
- ✅ **MUST** import shared components via `@spaarke/ui-components`
- ✅ **MUST** use semantic tokens for theming (no hard-coded colors)
- ✅ **MUST** support dark mode and high-contrast
- ✅ **MUST** match model-driven app interaction patterns
- ✅ **MUST** export TypeScript types alongside components
- ✅ **MUST** achieve 90%+ test coverage on shared components

---

## MUST NOT Rules

### Architecture (ADR-006)

- ❌ **MUST NOT** create new legacy JavaScript webresources
- ❌ **MUST NOT** add business logic to ribbon scripts
- ❌ **MUST NOT** make remote calls from ribbon scripts

### Dataset PCF (ADR-011)

- ❌ **MUST NOT** add new native subgrids without tech lead approval
- ❌ **MUST NOT** create bespoke JS webresources for list UX
- ❌ **MUST NOT** duplicate UI primitives (use shared library)

### Shared Components (ADR-012)

- ❌ **MUST NOT** mix Fluent UI versions (v9 only)
- ❌ **MUST NOT** reference PCF-specific APIs in shared components
- ❌ **MUST NOT** hard-code Dataverse entity names or schemas
- ❌ **MUST NOT** use custom CSS (Fluent tokens only)
- ❌ **MUST NOT** export components without tests

---

## Quick Reference Patterns

### PCF Control Entry Point

```typescript
import { DataGrid } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        ReactDOM.render(
            <FluentProvider theme={spaarkeLight}>
                <DataGrid items={items} columns={columns} />
            </FluentProvider>,
            this.container
        );
    }
}
```

### Shared Component Import

```typescript
import {
    DataGrid,
    StatusBadge,
    CommandBar,
    useDataverseFetch,
    formatters
} from "@spaarke/ui-components";
```

### When to Use Dataset PCF vs Native Subgrid

| Scenario | Technology |
|----------|------------|
| Related documents on forms | ✅ Dataset PCF |
| Bulk operations with selection | ✅ Dataset PCF |
| Custom actions needed | ✅ Dataset PCF |
| Simple read-only reference (<20 records) | Native subgrid OK |
| Admin/configuration lists | Native subgrid OK |

---

## Directory Structure

```
src/client/
├── pcf/                                    # PCF Controls
│   ├── UniversalDatasetGrid/               # Main dataset PCF
│   └── UniversalQuickCreate/               # Quick create dialog
├── shared/
│   └── Spaarke.UI.Components/              # Shared React library
│       ├── src/components/                 # DataGrid, CommandBar, etc.
│       ├── src/hooks/                      # useDataverseFetch, etc.
│       ├── src/theme/                      # spaarkeLight, spaarkeDark
│       └── src/utils/                      # formatters, validators
└── office-addins/                          # Office Add-ins
```

---

## Pattern Files (Complete Examples)

- [PCF Control Initialization](../patterns/pcf/control-initialization.md) - Lifecycle and React root
- [Theme Management](../patterns/pcf/theme-management.md) - Dark mode and theme resolution
- [Dataverse Queries](../patterns/pcf/dataverse-queries.md) - WebAPI and environment variables
- [Error Handling](../patterns/pcf/error-handling.md) - Error boundaries and user experience
- [Dialog Patterns](../patterns/pcf/dialog-patterns.md) - Dialog close and navigation

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-006](../adr/ADR-006-pcf-over-webresources.md) | PCF vs webresources | Exception approval |
| [ADR-011](../adr/ADR-011-dataset-pcf.md) | Dataset PCF vs subgrids | Implementation phases |
| [ADR-012](../adr/ADR-012-shared-components.md) | Shared library | Component governance |

---

**Lines**: ~145
**Purpose**: Single-file reference for all PCF development constraints
