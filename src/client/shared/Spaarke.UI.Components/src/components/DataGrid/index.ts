/**
 * `@spaarke/ui-components/components/DataGrid` — configuration-driven DataGrid framework.
 *
 * Phase A complete (tasks 001-008):
 * - Foundation contracts: tokens, DataGrid component, lazy-load, config resolution
 * - Column header primitives: ColumnHeaderMenu + ColumnFilterHeader (portal-fixed)
 * - 5 filter chip primitives: Lookup, OptionSet, DateRange, Text, Bool
 * - CommandBar primitive with 6 default actions + custom registry + CSV export
 *
 * Phase A acceptance gate (task 009) verifies Storybook coverage + MDA visual parity.
 *
 * **Note on internal type names**: `ColumnFilterType` and `ColumnFilterOption` exist
 * in both `columnHeader/ColumnFilterHeader.tsx` and `columnHeader/ColumnHeaderMenu.tsx`.
 * Top-level barrel does NOT re-export them to avoid duplicate-identifier errors.
 * Consumers needing those types should import directly from the source files.
 *
 * @see projects/spaarke-datagrid-framework-r1
 */

// ─── Foundation (task 001) ───
export { dataGridTokens } from './tokens';
export type { DataGridTokens } from './tokens';

// ─── Core DataGrid (task 003) ───
export { DataGrid, default as DataGridDefault } from './DataGrid';
export type { DataGridProps, DataGridHostContext } from './DataGrid';

export { useLazyLoad } from './useLazyLoad';
export type { UseLazyLoadOptions, UseLazyLoadResult } from './useLazyLoad';

export { resolveConfig, parseLayoutColumns } from './configResolution';
export type { DataGridOverrides, ResolvedConfig, ResolvedColumn } from './configResolution';

// ─── Parent-context FetchXML overlay (task 020 D-020-02 follow-up) ───
// + Host-filters overlay (task 033a — third permanent composition layer)
export { overlayParentContextFilter, overlayHostFilters } from './fetchXmlOverlay';
export type {
  DataGridParentContextLike,
  HostFilterCondition,
  HostFilterOperator,
} from './fetchXmlOverlay';

// ─── Column header primitives (task 004) ───
export { ColumnHeaderMenu } from './columnHeader/ColumnHeaderMenu';
export type { ColumnHeaderMenuProps, SortDirection } from './columnHeader/ColumnHeaderMenu';
export { ColumnFilterHeader } from './columnHeader/ColumnFilterHeader';
export type { ColumnFilterHeaderProps } from './columnHeader/ColumnFilterHeader';
// (ColumnFilterType + ColumnFilterOption are internal; import from source if needed.)

// ─── Per-column header content (Power-Apps-OOB-style chevron menu) ───
export { HeaderCellContent, default as HeaderCellContentDefault } from './HeaderCellContent';
export type { HeaderCellContentProps, HeaderSortDirection } from './HeaderCellContent';

// ─── View picker (shared — reusable by Phase D EventsPage + Phase E SearchResultsGrid) ───
// Renamed at barrel to `DataGridViewSelector` to avoid collision with the legacy
// `ViewSelector` exported by `components/DatasetGrid/ViewSelector` (retired in Phase F).
// The internal name in `./ViewSelector.tsx` stays `ViewSelector` — deep imports
// still work via `./ViewSelector` direct path.
export { ViewSelector as DataGridViewSelector, default as DataGridViewSelectorDefault } from './ViewSelector';
export type { ViewSelectorProps as DataGridViewSelectorProps, SavedView } from './ViewSelector';

// ─── Filter chip primitives (tasks 005-007) ───
export { LookupMultiFilterChip, useDebouncedValue } from './chips/LookupMultiFilterChip';
export type { LookupMultiFilterChipProps, LookupRecord } from './chips/LookupMultiFilterChip';

export { OptionSetMultiFilterChip } from './chips/OptionSetMultiFilterChip';
export type { OptionSetMultiFilterChipProps } from './chips/OptionSetMultiFilterChip';

export { DateRangeFilterChip, localDateToUtcBounds } from './chips/DateRangeFilterChip';
export type { DateRangeFilterChipProps, UtcDateBounds } from './chips/DateRangeFilterChip';

export { TextFilterChip } from './chips/TextFilterChip';
export type { TextFilterChipProps } from './chips/TextFilterChip';

export { BoolFilterChip } from './chips/BoolFilterChip';
export type { BoolFilterChipProps, BoolFilterValue } from './chips/BoolFilterChip';

// ─── Command bar primitive (task 008) ───
// Renamed at barrel to `DataGridCommandBar` to avoid collision with the unrelated
// `CommandBar` exported by components/PageChrome/. The internal name in
// commandBar/CommandBar.tsx stays `CommandBar` — deep imports still work.
export { CommandBar as DataGridCommandBar } from './commandBar/CommandBar';
export type { CommandBarProps as DataGridCommandBarProps } from './commandBar/CommandBar';

export {
  defaultCreateFormHandler,
  defaultDeleteSelectedHandler,
  defaultRefreshHandler,
  defaultExportExcelHandler,
  defaultEditColumnsHandler,
  defaultEditFiltersHandler,
  DEFAULT_ACTION_META,
  DEFAULT_ACTION_HANDLERS,
} from './commandBar/defaults';
export type { DefaultHandler, DefaultHandlerContext, DefaultActionMeta } from './commandBar/defaults';

export { exportCsv, escapeCsvField, csvFilename, formatYyyymmdd, UTF8_BOM } from './commandBar/csvExport';

export {
  registerCommandHandler,
  getCommandHandler,
  unregisterCommandHandler,
  clearCommandHandlers,
  listCommandHandlers,
} from './commandBar/registry';

// ─── Filter chip composition layer (filterChips/) ───
// Composes the Phase A primitive chips with the configjson FilterChipsConfig
// + entity metadata + the savedquery's FetchXML. See filterChips/index.ts.
export { FilterChipBar, discoverChips, augmentFetchXmlWithChips, deriveChipKindFromMetadata } from './filterChips';
export type { FilterChipBarProps, ChipDescriptor, ChipKind, ChipState, ChipValue } from './filterChips';
