# ADR-011: Dataset PCF Over Subgrids (Concise)

> **Status**: Accepted
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2025-12-18

---

## Decision

Use **Dataset PCF controls** instead of native Power Platform subgrids for list-based document management scenarios.

**Rationale**: Native subgrids have limited customization, poor bulk UX, and no support for custom actions. Dataset PCF provides virtual scrolling, bulk operations, custom actions, and consistent UX across contexts.

---

## Constraints

### ✅ MUST

- **MUST** use Dataset PCF for list-based document UX
- **MUST** implement/extend `src/client/pcf/UniversalDatasetGrid/`
- **MUST** reuse shared components from `@spaarke/ui-components`
- **MUST** use Fluent UI v9 exclusively
- **MUST** achieve 80%+ test coverage on PCF controls
- **MUST** include Storybook stories for components

### ❌ MUST NOT

- **MUST NOT** add new native subgrids without tech lead approval
- **MUST NOT** create bespoke JS webresources for list UX
- **MUST NOT** duplicate UI primitives (use shared library)

---

## Implementation Patterns

### When to Use Dataset PCF vs Native Subgrid

| Scenario | Technology |
|----------|------------|
| Related documents on forms | ✅ Dataset PCF |
| Document search/browse | ✅ Dataset PCF |
| Bulk operations with selection | ✅ Dataset PCF |
| Custom actions needed | ✅ Dataset PCF |
| Custom visualizations (cards, tiles) | ✅ Dataset PCF |
| Simple read-only reference (<20 records) | Native subgrid OK |
| Admin/configuration lists | Native subgrid OK |

### PCF Control Structure

```typescript
// index.ts - Dataset PCF entry point
import { DataGrid } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
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

**See**: [PCF Control Pattern](../patterns/pcf/control-initialization.md)

### Configuration Interface

```typescript
interface DocumentListConfig {
    entityType: "sprk_document" | "sprk_container";
    viewMode: "grid" | "cards" | "tiles";
    enableUpload: boolean;
    enableBulkActions: boolean;
    enableSearch: boolean;
}
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | PCF over webresources |
| [ADR-012](ADR-012-shared-components.md) | Shared component library |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-011-dataset-pcf-over-subgrids.md](../../docs/adr/ADR-011-dataset-pcf-over-subgrids.md)

For detailed context including:
- Native subgrid limitations analysis
- Implementation phases
- Performance targets
- Technical stack details

---

**Lines**: ~100
