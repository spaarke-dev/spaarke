# CLAUDE.md - Shared UI Components Library

> **Last Updated**: December 3, 2025
>
> **Purpose**: Module-specific instructions for the shared React/TypeScript component library.

## Module Overview

**@spaarke/ui-components** is a shared component library providing:
- Reusable React components (Fluent UI v9 based)
- Shared TypeScript utilities (formatters, validators)
- Common types and interfaces
- Theme definitions

This library is consumed by PCF controls, future SPAs, and Office Add-ins.

## Key Structure

```
src/client/shared/Spaarke.UI.Components/
├── package.json              # NPM package definition
├── tsconfig.json             # TypeScript configuration
├── src/
│   ├── components/           # Reusable React components
│   │   ├── DataGrid/
│   │   ├── StatusBadge/
│   │   └── index.ts
│   ├── hooks/                # Shared React hooks
│   │   ├── usePagination.ts
│   │   └── index.ts
│   ├── services/             # Business logic services
│   ├── types/                # TypeScript interfaces
│   ├── theme/                # Fluent UI themes
│   ├── utils/                # Utility functions
│   │   ├── formatters.ts
│   │   └── index.ts
│   └── index.ts              # Main entry point
└── __tests__/                # Component tests
```

## Architecture Constraints

### From ADR-012: Shared Component Library

```typescript
// ✅ CORRECT: Context-agnostic components
interface IDataGridProps<T> {
    items: T[];
    columns: IColumn[];
    onItemClick?: (item: T) => void;
}

export function DataGrid<T>({ items, columns, onItemClick }: IDataGridProps<T>) {
    // Works in PCF, SPA, or Add-in
}

// ❌ WRONG: PCF-specific dependencies
interface IBadProps {
    context: ComponentFramework.Context<IInputs>;  // PCF-specific!
}
```

### Fluent UI v9 Only
```typescript
// ✅ CORRECT: Fluent UI v9
import { Button, Input, DataGrid } from "@fluentui/react-components";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

// ❌ WRONG: Fluent UI v8
import { DefaultButton } from "@fluentui/react";  // DON'T USE
```

## Component Design Guidelines

### Accept Configuration via Props
```typescript
// ✅ CORRECT: Configurable via props
interface IStatusBadgeProps {
    status: "pending" | "active" | "completed";
    label?: string;
    size?: "small" | "medium" | "large";
}

export const StatusBadge: React.FC<IStatusBadgeProps> = ({
    status,
    label,
    size = "medium"
}) => {
    // Implementation
};
```

### Export Types Alongside Components
```typescript
// components/DataGrid/index.ts
export { DataGrid } from './DataGrid';
export type { IDataGridProps, IColumn } from './DataGrid.types';
```

### Document with JSDoc
```typescript
/**
 * Displays a status badge with semantic coloring.
 *
 * @param status - The status to display (pending, active, completed)
 * @param label - Optional label override (defaults to status name)
 * @param size - Badge size variant
 *
 * @example
 * ```tsx
 * <StatusBadge status="active" label="In Progress" />
 * ```
 */
export const StatusBadge: React.FC<IStatusBadgeProps> = (props) => {
    // Implementation
};
```

## Utility Patterns

### Formatters
```typescript
// utils/formatters.ts
export const formatters = {
    date: (value: Date | string): string => {
        const date = typeof value === "string" ? new Date(value) : value;
        return new Intl.DateTimeFormat("en-US", {
            year: "numeric",
            month: "short",
            day: "numeric"
        }).format(date);
    },

    currency: (value: number, code = "USD"): string => {
        return new Intl.NumberFormat("en-US", {
            style: "currency",
            currency: code
        }).format(value);
    },

    fileSize: (bytes: number): string => {
        const units = ["B", "KB", "MB", "GB"];
        let i = 0;
        while (bytes >= 1024 && i < units.length - 1) {
            bytes /= 1024;
            i++;
        }
        return `${bytes.toFixed(1)} ${units[i]}`;
    }
};
```

### Custom Hooks
```typescript
// hooks/usePagination.ts
export function usePagination(initialPageSize = 25) {
    const [state, setState] = useState({
        currentPage: 1,
        pageSize: initialPageSize,
        totalRecords: 0
    });

    const nextPage = useCallback(() => {
        setState(prev => ({ ...prev, currentPage: prev.currentPage + 1 }));
    }, []);

    const previousPage = useCallback(() => {
        setState(prev => ({ ...prev, currentPage: Math.max(1, prev.currentPage - 1) }));
    }, []);

    return { ...state, nextPage, previousPage };
}
```

## Barrel Exports

```typescript
// src/index.ts - Main entry point
export * from './components';
export * from './hooks';
export * from './utils';
export * from './types';
export * from './theme';
```

```typescript
// src/components/index.ts
export { DataGrid } from './DataGrid';
export type { IDataGridProps, IColumn } from './DataGrid';

export { StatusBadge } from './StatusBadge';
export type { IStatusBadgeProps } from './StatusBadge';
```

## Testing Guidelines

```typescript
// __tests__/components/StatusBadge.test.tsx
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { StatusBadge } from '../src/components/StatusBadge';

const renderWithProvider = (ui: React.ReactElement) =>
    render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

describe('StatusBadge', () => {
    it('renders with correct label', () => {
        renderWithProvider(<StatusBadge status="active" label="In Progress" />);
        expect(screen.getByText('In Progress')).toBeInTheDocument();
    });

    it('uses status as default label', () => {
        renderWithProvider(<StatusBadge status="completed" />);
        expect(screen.getByText('completed')).toBeInTheDocument();
    });
});
```

## Build Commands

```bash
# Install dependencies
npm install

# Build library
npm run build

# Run tests
npm test

# Lint
npm run lint
```

## Consumption in PCF

```json
// PCF control package.json
{
    "dependencies": {
        "@spaarke/ui-components": "workspace:*"
    }
}
```

```typescript
// PCF control
import { DataGrid, StatusBadge, formatters } from "@spaarke/ui-components";

const App = ({ data }) => (
    <DataGrid
        items={data}
        columns={[
            { key: "name", name: "Name", fieldName: "name" },
            { key: "date", name: "Created", fieldName: "createdOn", 
              onRender: (item) => formatters.date(item.createdOn) }
        ]}
    />
);
```

## When to Add Components

**✅ Add to shared library when:**
- Component used by 2+ modules
- Component represents core Spaarke UX pattern
- Component has clear, reusable API

**❌ Keep in module when:**
- Component is module-specific
- Component is experimental
- Component has tight coupling to module context

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Make components context-agnostic | Reference PCF APIs directly |
| Use Fluent UI v9 exclusively | Mix styling systems |
| Export TypeScript types | Use `any` types |
| Write unit tests for all components | Ship untested components |
| Document props with JSDoc | Leave props undocumented |
| Use semantic versioning | Make breaking changes without version bump |

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
