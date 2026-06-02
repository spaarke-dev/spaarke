/**
 * useDataGridContext — React context for the DataGrid framework's host extensions.
 *
 * Every host extension authored under the new framework — custom filter chips,
 * custom command-bar buttons, custom cell renderers, dialog wizards — needs the
 * same handful of state knobs from the surrounding `<DataGrid />`:
 *   - `selectedIds`     — the row selection set (preserved across lazy-load pages)
 *   - `refresh`         — re-fetch from page 1 (used by `delete-selected`, edits)
 *   - `currentView`     — display name of the active savedquery / inline config
 *   - `parentContext`   — drill-through context (entity / id / name)
 *   - `dataverseClient` — the resolved `IDataverseClient` (same instance the grid uses)
 *   - `entityMetadata`  — the projected metadata loaded during config resolution
 *
 * Without this context, every extension would prop-drill the same six values
 * through the command bar, filter strip, row menu, and dialog hierarchies. FR-DG-15
 * mandates this hook as the single source of truth for host extensions.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §7
 * **FR**: FR-DG-15 (useDataGridContext)
 * **ADR**: ADR-022 (React-16-safe — uses `createContext`/`useContext` only,
 *          NO `useId`, NO `useSyncExternalStore`, NO `useTransition`).
 */

import * as React from 'react';
import type {
  IDataverseClient,
  EntityMetadata,
} from '../services/IDataverseClient';

/**
 * Drill-through context — propagated from the host (e.g., a Custom Page embedded
 * on a Matter form) to every DataGrid extension.
 *
 * The shape mirrors design.md §6.1 `IDataGridProps.parentContext`.
 */
export interface DataGridParentContext {
  entityType: string;
  id: string;
  name: string;
  /** Additional host-specific fields propagated via `rowOpen.passContext`. */
  [extra: string]: string;
}

/**
 * The value returned by {@link useDataGridContext}.
 *
 * @see DataGridContextProvider — the only legitimate way to populate this.
 */
export interface DataGridContextValue {
  /** Current row selection — a Set of primaryId values, preserved across pages. */
  selectedIds: Set<string>;
  /** Trigger a hard refresh (re-fetch page 1, preserve filter/sort state). */
  refresh: () => void;
  /** Display name of the active savedquery / configjson. */
  currentView: string;
  /** Optional drill-through parent context. */
  parentContext?: DataGridParentContext;
  /** The resolved `IDataverseClient` instance the grid is using. */
  dataverseClient: IDataverseClient;
  /** Projected entity metadata loaded during config resolution. */
  entityMetadata: EntityMetadata;
}

/**
 * Internal context — never exported. Consumers use the `useDataGridContext` hook,
 * which throws a clearer error than React's default null-context message.
 */
const DataGridContext = React.createContext<DataGridContextValue | null>(null);

/**
 * Props for {@link DataGridContextProvider}.
 *
 * `children` is `React.ReactNode` (not `JSXElement`) to remain React-16-safe.
 */
export interface DataGridContextProviderProps {
  value: DataGridContextValue;
  children: React.ReactNode;
}

/**
 * Provider for the DataGrid extension context.
 *
 * Mounted internally by the `<DataGrid />` component so every extension rendered
 * inside it (filter chips, command bar, row menu, custom cell renderer) can call
 * `useDataGridContext()` without prop drilling.
 *
 * Hosts typically do NOT instantiate this directly.
 */
export const DataGridContextProvider: React.FC<DataGridContextProviderProps> = ({
  value,
  children,
}) => {
  // Authored with `React.createElement` (not JSX) so this module stays `.ts` per
  // the task 003 brief grep check. The actual `<DataGrid />` component file
  // (DataGrid.tsx) imports and renders this provider via JSX as normal.
  return React.createElement(
    DataGridContext.Provider,
    { value },
    children,
  );
};

/**
 * Consumer hook for the DataGrid extension context.
 *
 * **MUST be called from inside a `<DataGridContextProvider>`** — typically that
 * provider is wired by the `<DataGrid />` component itself. Calling outside will
 * throw a descriptive error to make the misuse obvious during development.
 *
 * @example
 * ```tsx
 * function CustomCommandButton() {
 *   const { selectedIds, refresh, dataverseClient } = useDataGridContext();
 *   const onClick = async () => {
 *     await Promise.all([...selectedIds].map((id) => dataverseClient.retrieveRecord(...)));
 *     refresh();
 *   };
 *   return <Button onClick={onClick}>Bulk update ({selectedIds.size})</Button>;
 * }
 * ```
 */
export function useDataGridContext(): DataGridContextValue {
  const ctx = React.useContext(DataGridContext);
  if (ctx === null) {
    throw new Error(
      '[useDataGridContext] Called outside a <DataGridContextProvider>. ' +
        'This hook may only be used in components rendered inside <DataGrid />.',
    );
  }
  return ctx;
}

/**
 * Variant that returns `null` instead of throwing when called outside the provider.
 *
 * Useful for components that may render both inside AND outside a `<DataGrid />`
 * (e.g., a shared `CustomCommandButton` that also renders on a form). Most
 * consumers should use the strict {@link useDataGridContext} instead.
 */
export function useDataGridContextOptional(): DataGridContextValue | null {
  return React.useContext(DataGridContext);
}
