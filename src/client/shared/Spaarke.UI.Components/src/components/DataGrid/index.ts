/**
 * `@spaarke/ui-components/components/DataGrid` — configuration-driven DataGrid framework.
 *
 * Phase A surface exports:
 * - `dataGridTokens` + `DataGridTokens` type — MDA Power Apps grid UI parity tokens (task 001)
 * - `DataGrid` component — the core `<DataGrid configId={...} />` (task 003)
 * - `useLazyLoad` hook — FetchXML paging cookie chain (task 003)
 * - `resolveConfig` + types — pure three-tier config resolution (task 003)
 *
 * Filter chips, command bar, and column header menu primitives land in tasks 004–008.
 *
 * @see projects/spaarke-datagrid-framework-r1
 */

export { dataGridTokens } from './tokens';
export type { DataGridTokens } from './tokens';

export { DataGrid, default as DataGridDefault } from './DataGrid';
export type { DataGridProps, DataGridHostContext } from './DataGrid';

export { useLazyLoad } from './useLazyLoad';
export type { UseLazyLoadOptions, UseLazyLoadResult } from './useLazyLoad';

export { resolveConfig, parseLayoutColumns } from './configResolution';
export type {
  DataGridOverrides,
  ResolvedConfig,
  ResolvedColumn,
} from './configResolution';
