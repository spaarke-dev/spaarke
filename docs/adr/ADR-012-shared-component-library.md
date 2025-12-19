# ADR-012: Shared Component Library for React/TypeScript Across Modules

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-10-03 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | Sprint 5 - Universal Dataset PCF |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-012 Concise](../../.claude/adr/ADR-012-shared-components.md) - ~110 lines, decision + constraints
- [PCF Constraints](../../.claude/constraints/pcf.md) - MUST/MUST NOT rules for PCF development
- [React Hooks Pattern](../../.claude/patterns/pcf/react-hooks.md) - Shared hooks examples

**When to load this full ADR**: Component examples, directory structure, governance model

---

## Context

Spaarke is building multiple front-end surfaces:
1. **PCF Controls** (model-driven apps, custom pages) - TypeScript/React
2. **Future React SPA** (planned) - React/TypeScript
3. **Office Add-ins** (potential) - TypeScript/React
4. **Power Pages customizations** (potential) - TypeScript/React

Without a shared component library, we risk:
- **Code duplication** - Implementing the same UI components multiple times
- **Inconsistent UX** - Different look-and-feel across surfaces
- **Maintenance burden** - Fixing bugs or updating styles in multiple places
- **Slow development** - Rebuilding common patterns repeatedly
- **Violated DRY principle** - Not leveraging existing work

### Current State
- **BFF API** (`src/server/api/Sprk.Bff.Api/`) - .NET backend
- **PCF Controls** (`src/client/pcf/`) - React/TypeScript
- **Shared library location** (`src/client/shared/Spaarke.UI.Components/`) - shared React/TS library

---

## Decision

**We will maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` that provides:**

1. **Reusable React components** (Fluent UI v9 based)
2. **Shared TypeScript utilities** (formatters, transformers, validators)
3. **Common types and interfaces** (DTOs, domain models)
4. **Theme definitions** (Spaarke light/dark themes)
5. **Shared hooks** (data fetching, caching, state management)

**This library will:**
- Be consumed by PCF controls as an NPM package (local workspace or private registry)
- Be consumed by future React SPA
- Be version-controlled separately but deployed together
- Follow Fluent UI v9 design system exclusively
- Maintain strict TypeScript typing

### UI/UX Standards (Required)

| Rule | Requirement |
|------|-------------|
| **Fluent UI everywhere** | All custom UI must use Fluent UI v9 components and design tokens as the primary UI/UX system (no bespoke component systems). |
| **Power Apps MDA fit** | Any custom UI embedded in model-driven apps must match the model-driven look/feel and interaction patterns (density, typography, spacing, dialogs, command patterns, focus/keyboard behavior). |
| **No hard-coded styling** | No hard-coded colors or “pixel-perfect” one-off styling that diverges from Fluent tokens; prefer semantic tokens and theming. |
| **Dark-mode compatible** | Everything we build (components, icons, illustrations) must render correctly in dark mode and high-contrast scenarios even if MDA dark mode is not officially shipped everywhere. |
| **Accessibility** | WCAG-aligned behavior (keyboard nav, focus states, contrast). Treat this as a release requirement for shared components. |

**Note:** If these requirements need to govern *all* UI surfaces (PCF, custom pages, webresources, icons/theme assets) beyond this shared library, create a dedicated ADR (e.g., “UI Theming + Dark Mode Compatibility”) to avoid scattering rules across multiple ADRs.

---

## Architecture

### Directory Structure

```
src/client/shared/Spaarke.UI.Components/
├── package.json                    # NPM package definition
├── tsconfig.json                   # Shared TypeScript config
├── .eslintrc.json                  # Shared linting rules
├── src/
│   ├── components/                 # Reusable React components
│   │   ├── DataGrid/               # Generic data grid
│   │   │   ├── DataGrid.tsx
│   │   │   ├── DataGrid.types.ts
│   │   │   └── index.ts
│   │   ├── CommandBar/             # Command bar with actions
│   │   ├── EntityPicker/           # Dataverse entity picker
│   │   ├── FileUploader/           # File upload component
│   │   ├── StatusBadge/            # Status indicator badges
│   │   └── index.ts                # Barrel export
│   ├── hooks/                      # Shared React hooks
│   │   ├── useDataverseFetch.ts    # Dataverse Web API fetching
│   │   ├── usePagination.ts        # Pagination logic
│   │   ├── useSelection.ts         # Selection management
│   │   └── index.ts
│   ├── services/                   # Business logic services
│   │   ├── EntityMetadataService.ts
│   │   ├── FormatterService.ts
│   │   ├── ValidationService.ts
│   │   └── index.ts
│   ├── types/                      # Shared TypeScript types
│   │   ├── dataverse.ts            # Dataverse types
│   │   ├── common.ts               # Common DTOs
│   │   └── index.ts
│   ├── theme/                      # Fluent UI themes
│   │   ├── spaarkeLight.ts
│   │   ├── spaarkeDark.ts
│   │   └── index.ts
│   ├── utils/                      # Utility functions
│   │   ├── formatters.ts           # Date, number, currency formatters
│   │   ├── transformers.ts         # Data transformers
│   │   ├── validators.ts           # Validation helpers
│   │   └── index.ts
│   └── index.ts                    # Main entry point
├── __tests__/                      # Component tests
│   ├── components/
│   ├── hooks/
│   └── utils/
└── README.md                       # Library documentation
```

### Consumption Pattern

**PCF Control (`src/client/pcf/UniversalDatasetGrid/`):**
```json
// package.json
{
  "dependencies": {
    "@spaarke/ui-components": "workspace:*",  // Workspace link
    "@fluentui/react-components": "^9.46.2",
    "react": "^18.2.0"
  }
}
```

```typescript
// index.ts - PCF entry point
import { DataGrid, useDataverseFetch, formatters } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  public updateView(context: ComponentFramework.Context<IInputs>): void {
    ReactDOM.render(
      React.createElement(FluentProvider, { theme: spaarkeLight },
        React.createElement(DataGrid, {
          data: transformedData,
          formatters: formatters,
          onAction: handleAction
        })
      ),
      this.container
    );
  }
}
```

**Future React SPA:**
```json
// package.json
{
  "dependencies": {
    "@spaarke/ui-components": "^1.0.0",  // Published package
    "@fluentui/react-components": "^9.46.2"
  }
}
```

```typescript
// App.tsx
import { DataGrid, EntityPicker, CommandBar } from "@spaarke/ui-components";
import { spaarkeLight } from "@spaarke/ui-components/theme";

export const App = () => (
  <FluentProvider theme={spaarkeLight}>
    <CommandBar actions={actions} />
    <DataGrid data={data} />
  </FluentProvider>
);
```

---

## Component Library Setup

### Package Configuration

**src/shared/Spaarke.UI.Components/package.json:**
```json
{
  "name": "@spaarke/ui-components",
  "version": "1.0.0",
  "description": "Shared React/TypeScript component library for Spaarke",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "test": "jest",
    "lint": "eslint src --ext .ts,.tsx",
    "prepublishOnly": "npm run build"
  },
  "peerDependencies": {
    "@fluentui/react-components": "^9.46.2",
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  },
  "devDependencies": {
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0",
    "typescript": "^5.3.3",
    "jest": "^29.7.0",
    "@testing-library/react": "^14.0.0"
  }
}
```

**Workspace Configuration (root package.json):**
```json
{
  "workspaces": [
    "src/client/shared/Spaarke.UI.Components",
    "src/client/pcf/*"
  ]
}
```

---

## Component Development Guidelines

### 1. **Component Design Principles**

**✅ DO:**
- Build components that work in ANY context (PCF, SPA, add-ins)
- Accept configuration via props (no hard-coded behavior)
- Use Fluent UI v9 components exclusively
- Export TypeScript types alongside components
- Write unit tests for all components
- Document props with JSDoc comments

**❌ DON'T:**
- Reference PCF-specific APIs directly (pass context as props)
- Hard-code Dataverse entity names or schemas
- Mix styling systems (Fluent v9 only, no custom CSS)
- Export components without tests

### 2. **Example: Reusable DataGrid Component**

**src/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx:**
```typescript
import * as React from "react";
import {
  DataGrid as FluentDataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  TableCellLayout
} from "@fluentui/react-components";

export interface IColumn {
  key: string;
  name: string;
  fieldName: string;
  minWidth?: number;
  maxWidth?: number;
  isResizable?: boolean;
}

export interface IDataGridProps<T = any> {
  items: T[];
  columns: IColumn[];
  onItemClick?: (item: T) => void;
  onSelectionChange?: (selectedKeys: string[]) => void;
  loading?: boolean;
  emptyMessage?: string;
}

export function DataGrid<T extends { id: string }>(props: IDataGridProps<T>) {
  const { items, columns, onItemClick, loading, emptyMessage = "No records found" } = props;

  if (loading) {
    return <div>Loading...</div>;
  }

  if (items.length === 0) {
    return <div>{emptyMessage}</div>;
  }

  return (
    <FluentDataGrid items={items} columns={columns}>
      <DataGridHeader>
        <DataGridRow>
          {columns.map(col => (
            <DataGridHeaderCell key={col.key}>{col.name}</DataGridHeaderCell>
          ))}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody>
        {items.map(item => (
          <DataGridRow
            key={item.id}
            onClick={() => onItemClick?.(item)}
            style={{ cursor: onItemClick ? "pointer" : "default" }}
          >
            {columns.map(col => (
              <DataGridCell key={col.key}>
                <TableCellLayout truncate>
                  {(item as any)[col.fieldName]?.toString() ?? "-"}
                </TableCellLayout>
              </DataGridCell>
            ))}
          </DataGridRow>
        ))}
      </DataGridBody>
    </FluentDataGrid>
  );
}
```

**Usage in PCF Control:**
```typescript
import { DataGrid, IColumn } from "@spaarke/ui-components";

const columns: IColumn[] = [
  { key: "name", name: "Name", fieldName: "name" },
  { key: "status", name: "Status", fieldName: "statuscode" }
];

<DataGrid
  items={datasetItems}
  columns={columns}
  onItemClick={item => context.navigation.openForm({ entityId: item.id })}
/>
```

### 3. **Reusable Hooks Example**

**src/shared/Spaarke.UI.Components/src/hooks/usePagination.ts:**
```typescript
import { useState, useCallback } from "react";

export interface IPaginationState {
  currentPage: number;
  pageSize: number;
  totalRecords: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export function usePagination(initialPageSize: number = 25) {
  const [state, setState] = useState<IPaginationState>({
    currentPage: 1,
    pageSize: initialPageSize,
    totalRecords: 0,
    hasNextPage: false,
    hasPreviousPage: false
  });

  const nextPage = useCallback(() => {
    if (state.hasNextPage) {
      setState(prev => ({ ...prev, currentPage: prev.currentPage + 1 }));
    }
  }, [state.hasNextPage]);

  const previousPage = useCallback(() => {
    if (state.hasPreviousPage) {
      setState(prev => ({ ...prev, currentPage: prev.currentPage - 1 }));
    }
  }, [state.hasPreviousPage]);

  const setTotalRecords = useCallback((total: number) => {
    setState(prev => ({
      ...prev,
      totalRecords: total,
      hasNextPage: prev.currentPage * prev.pageSize < total,
      hasPreviousPage: prev.currentPage > 1
    }));
  }, []);

  return { state, nextPage, previousPage, setTotalRecords };
}
```

---

## Consequences

### Positive

✅ **Code Reuse**
- Write once, use everywhere (PCF, SPA, add-ins)
- Reduced development time for new features
- Consistent behavior across surfaces

✅ **Maintainability**
- Single source of truth for common components
- Bug fixes propagate to all consumers
- Centralized testing

✅ **Consistency**
- Unified UX across all Spaarke surfaces
- Single theme definition
- Shared design patterns

✅ **Quality**
- Higher test coverage (shared tests benefit all consumers)
- Better TypeScript typing
- Reusable best practices

✅ **Developer Experience**
- Clear separation of concerns
- Well-documented, versioned components
- Easy to onboard new developers

### Negative

❌ **Initial Setup Effort**
- Requires workspace configuration
- Need to extract and generalize existing code
- Learning curve for component library patterns

❌ **Version Management**
- Must coordinate versions across consumers
- Breaking changes require careful migration
- Need semantic versioning discipline

❌ **Build Complexity**
- Shared library must build before consumers
- CI/CD pipeline needs additional steps
- Workspace linking for local development

### Mitigation Strategies

**For Setup Effort:**
- Start small: Extract 3-5 core components initially
- Document component library patterns
- Provide templates for new components

**For Version Management:**
- Use semantic versioning strictly (semver)
- Maintain CHANGELOG.md
- Test breaking changes in consumers before release

**For Build Complexity:**
- Use NPM workspaces for local linking
- Automate builds in CI/CD
- Provide clear build documentation

---

## Operationalization

### Phase 1: Library Setup (Sprint 5)

**Create library structure:**
```bash
mkdir -p src/shared/Spaarke.UI.Components/src/{components,hooks,services,types,theme,utils}
cd src/shared/Spaarke.UI.Components
npm init -y
npm install --save-peer @fluentui/react-components react react-dom
npm install --save-dev typescript @types/react @types/react-dom
```

**Initialize workspace:**
```json
// Root package.json
{
  "private": true,
  "workspaces": [
    "src/shared/Spaarke.UI.Components",
    "power-platform/pcf/UniversalDataset"
  ]
}
```

**Link in PCF project:**
```bash
cd power-platform/pcf/UniversalDataset
npm install @spaarke/ui-components@workspace:*
```

### Phase 2: Extract Core Components (Sprint 5)

**Priority 1 - Extract to shared library:**
1. **DataGrid** - Generic data grid (from UniversalDataset GridView)
2. **Formatters** - Date, number, currency formatters
3. **Transformers** - Data transformation utilities
4. **Theme** - Spaarke light/dark themes
5. **Types** - Common TypeScript interfaces

**Priority 2 - Build in shared library:**
1. **CommandBar** - Action toolbar
2. **StatusBadge** - Status indicators
3. **EntityPicker** - Dataverse entity picker

### Phase 3: PCF Control Integration (Sprint 5)

**Update UniversalDataset to consume shared library:**
```typescript
// Before: Local implementation
import { GridView } from "./src/components/views/GridView";

// After: Shared library
import { DataGrid } from "@spaarke/ui-components";
```

### Development Workflow

**Making changes to shared library:**
```bash
# 1. Edit component in src/shared/Spaarke.UI.Components/
# 2. Build library
cd src/shared/Spaarke.UI.Components
npm run build

# 3. Test in PCF control (automatically linked via workspace)
cd ../../../power-platform/pcf/UniversalDataset
npm run start  # Changes reflected immediately
```

### Publishing Strategy

**Local development:**
- Use NPM workspaces (no publishing needed)
- Automatic linking via `workspace:*` protocol

**Production deployment:**
- Option A: Publish to private NPM registry (Azure Artifacts)
- Option B: Publish to GitHub Packages
- Option C: Keep workspace-linked (deploy together)

**Recommended:** Start with workspace linking, move to private registry when SPA is added

---

## Component Ownership & Governance

### Who Owns Components?

**Shared Library Team** (rotating ownership):
- Reviews PRs to shared components
- Ensures quality, tests, documentation
- Coordinates breaking changes

### When to Add a Component?

**✅ Add to shared library when:**
- Component used by 2+ modules
- Component represents core Spaarke UX pattern
- Component has clear, reusable API

**❌ Keep in module when:**
- Component is module-specific (PCF-only logic)
- Component is experimental/proof-of-concept
- Component has tight coupling to module context

### Migration Path

**Existing code → Shared library:**
1. Generalize: Remove module-specific dependencies
2. Extract: Move to `src/shared/Spaarke.UI.Components/src/components/`
3. Test: Add unit tests
4. Document: Add JSDoc and README entry
5. Replace: Update consumers to import from `@spaarke/ui-components`

---

## Examples of Shared Components

### 1. StatusBadge (Reusable across modules)

**src/shared/Spaarke.UI.Components/src/components/StatusBadge/StatusBadge.tsx:**
```typescript
import * as React from "react";
import { Badge } from "@fluentui/react-components";

export interface IStatusBadgeProps {
  status: "pending" | "inprogress" | "completed" | "failed";
  label: string;
}

const colorMap = {
  pending: "warning",
  inprogress: "informative",
  completed: "success",
  failed: "danger"
} as const;

export const StatusBadge: React.FC<IStatusBadgeProps> = ({ status, label }) => (
  <Badge appearance="filled" color={colorMap[status]}>
    {label}
  </Badge>
);
```

**Usage in PCF:**
```typescript
import { StatusBadge } from "@spaarke/ui-components";

<StatusBadge status="completed" label="Approved" />
```

**Usage in future SPA:**
```typescript
import { StatusBadge } from "@spaarke/ui-components";

<StatusBadge status="inprogress" label="Processing" />
```

### 2. Formatters (Pure utility functions)

**src/shared/Spaarke.UI.Components/src/utils/formatters.ts:**
```typescript
export const formatters = {
  date: (value: Date | string): string => {
    const date = typeof value === "string" ? new Date(value) : value;
    return new Intl.DateTimeFormat("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric"
    }).format(date);
  },

  currency: (value: number, currencyCode: string = "USD"): string => {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency: currencyCode
    }).format(value);
  },

  number: (value: number): string => {
    return new Intl.NumberFormat("en-US").format(value);
  }
};
```

---

## Success Metrics

**Code Reuse:**
- \>70% of UI components shared between PCF and SPA
- <10% code duplication across modules

**Development Velocity:**
- 50% faster feature development using shared components
- New module can use 80% shared components

**Quality:**
- 90%+ test coverage on shared components
- Zero runtime errors from shared library (first 3 months)

**Consistency:**
- 100% Fluent UI v9 compliance
- Single theme definition used everywhere

---

## Compliance Checklist

Use this as a **pass/fail** review gate for PRs that add or change shared components.

**Design system & UX**
- Uses Fluent UI v9 components and tokens; no alternate UI framework introduced.
- Matches model-driven app interaction patterns when embedded (dialogs, spacing/density, typography, command patterns).

**Theming (including dark mode)**
- No hard-coded colors (including inline styles); styling is token/semantic-token driven.
- Component renders correctly with both `spaarkeLight` and `spaarkeDark` themes.
- Icons/visuals work in dark mode and high-contrast (e.g., use `currentColor`/tokens; avoid fixed-color SVGs unless variants are provided).

**Accessibility**
- Keyboard navigation works end-to-end; visible focus states are preserved.
- Text/icon contrast meets accessibility expectations in light, dark, and high-contrast.

**Packaging & API hygiene**
- Public exports are intentional (barrel exports); no accidental deep import dependencies.
- Component APIs are typed; breaking changes are versioned appropriately.
- Shared library does not take dependencies on app-specific modules (keep it reusable across PCF/SPA).

---

## Related ADRs

- [ADR-006: Prefer PCF Controls Over Web Resources](./ADR-006-prefer-pcf-over-webresources.md) - PCF development strategy
- [ADR-011: Dataset PCF Over Subgrids](./ADR-011-dataset-pcf-over-subgrids.md) - Reusability principles for PCF
- [ADR-010: DI Minimalism](./ADR-010-di-minimalism.md) - Service abstraction principles

---

## References

- [NPM Workspaces Documentation](https://docs.npmjs.com/cli/v8/using-npm/workspaces)
- [TypeScript Handbook - Modules](https://www.typescriptlang.org/docs/handbook/modules.html)
- [Fluent UI React v9](https://react.fluentui.dev/)
- [Component Library Best Practices](https://blog.bitsrc.io/how-to-build-a-react-component-library-d92a2da8eab9)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-10-03 | 1.0 | Initial ADR creation | Spaarke Engineering |
| 2025-12-12 | 1.1 | Added explicit UI/UX standards and compliance checklist (Fluent + MDA fit + dark-mode compatibility) | Spaarke Engineering |

---

**Next Actions:**
1. ✅ Document ADR-012 (this document)
2. 🔄 Update Sprint 5 implementation plan to include shared library setup
3. 🔄 Create `src/shared/Spaarke.UI.Components/` directory structure
4. 🔄 Extract core components from UniversalDataset PCF
5. 🔄 Set up NPM workspace configuration
