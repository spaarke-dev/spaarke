# ADR-012: Shared Component Library (Concise)

> **Status**: Accepted
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2025-12-18

---

## Decision

Maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` for reuse across PCF controls, future SPA, and add-ins.

**Rationale**: Prevents code duplication, ensures consistent UX, and centralizes maintenance for common UI components.

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

### ❌ MUST NOT

- **MUST NOT** mix Fluent UI versions (v9 only)
- **MUST NOT** reference PCF-specific APIs in shared components
- **MUST NOT** hard-code Dataverse entity names or schemas
- **MUST NOT** use custom CSS (Fluent tokens only)
- **MUST NOT** export components without tests

---

## Implementation Patterns

### Library Structure

```
src/client/shared/Spaarke.UI.Components/
├── src/
│   ├── components/     # DataGrid, CommandBar, StatusBadge, etc.
│   ├── hooks/          # useDataverseFetch, usePagination, useSelection
│   ├── services/       # EntityMetadataService, FormatterService
│   ├── types/          # Shared TypeScript types
│   ├── theme/          # spaarkeLight, spaarkeDark
│   └── utils/          # formatters, transformers, validators
└── package.json        # @spaarke/ui-components
```

### Consumption in PCF

```typescript
import { DataGrid, StatusBadge, formatters } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

<FluentProvider theme={spaarkeLight}>
    <DataGrid items={items} columns={columns} />
    <StatusBadge status="completed" label="Approved" />
</FluentProvider>
```

**See**: [React Hooks Pattern](../patterns/pcf/react-hooks.md)

### Component Design Rules

```typescript
// ✅ DO: Accept configuration via props
interface IDataGridProps<T> {
    items: T[];
    columns: IColumn[];
    onItemClick?: (item: T) => void;
}

// ❌ DON'T: Hard-code entity names
const query = "sprk_documents"; // WRONG - pass as prop
```

---

## When to Add to Shared Library

| Add to Shared | Keep in Module |
|---------------|----------------|
| Used by 2+ modules | Module-specific logic |
| Core Spaarke UX pattern | Experimental/POC |
| Clear, reusable API | Tight coupling to context |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | PCF over webresources |
| [ADR-011](ADR-011-dataset-pcf.md) | Dataset PCF uses shared library |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md)

For detailed context including:
- Complete directory structure
- Package.json configuration
- Component examples (DataGrid, StatusBadge, hooks)
- Migration path from module to shared
- Governance and ownership model

---

**Lines**: ~110
