export { ColumnHeaderMenu } from './ColumnHeaderMenu';
export type { ColumnHeaderMenuProps, SortDirection } from './ColumnHeaderMenu';

// Re-export ColumnHeaderMenu's types under menu-specific alias names to
// avoid clashes with ColumnFilterHeader's same-named exports when the
// package barrel re-exports both component groups. GridSection imports the
// non-aliased names directly via relative path (`../ColumnHeaderMenu/ColumnHeaderMenu`)
// so the original symbol names remain available at the source-file level.
export type {
  ColumnFilterType as ColumnMenuFilterType,
  ColumnFilterOption as ColumnMenuFilterOption,
} from './ColumnHeaderMenu';
