/**
 * RootColumnLock - the four core-ancestor root columns are NOT editable in the grid.
 *
 * unified-access-control-r2 task 168 f1, owner round 25 item 8 (binding): the editable Power Apps
 * home grids of sprk_event and sprk_analysis must not let a person type a root directly onto a
 * row. Task 156's rule keeps the regarding PAIR as the source of a filed row's root, so a root typed
 * onto a filed row is a copy the reconciliation job reverts (owner round 8 item 3); the forms lock
 * these columns (Lock-CoreAncestorStampColumnsOnForms.ps1) and this is the same lock for the grid.
 *
 *  - editor override: any attempt to edit one of the four columns is cancelled with
 *    `stopEditing(true)` (the documented pattern; it runs for every way the grid opens an editor,
 *    so keyboard navigation does not bypass it);
 *  - renderer override: the cell is shown as not editable (`columnEditable = false`), and the
 *    column definition is marked `editable = false`.
 * Every other column keeps inline editing.
 *
 * The column set is CoreAncestorResolver.CoreAncestorLookups (task 156). Matching is by the WHOLE
 * logical name, case-insensitively: sprk_RegardingMatter matches, sprk_regardingmatterstatus does not.
 */

import type {
  CellEditorOverride,
  CellEditorProps,
  CellRendererProps,
  ColumnDefinition,
  GetEditorParams,
  GetRendererParams,
} from '../types/PAGridCustomizer';

/** CoreAncestorResolver.CoreAncestorLookups: the four core-ancestor root lookups. */
export const LOCKED_ROOT_COLUMNS: readonly string[] = [
  'sprk_regardingproject',
  'sprk_regardingmatter',
  'sprk_regardingworkassignment',
  'sprk_regardingservicerequest',
];

/** True when `name` is one of the four root columns (whole value, case-insensitive). */
export function isLockedRootColumn(name: string | null | undefined): boolean {
  if (typeof name !== 'string') {
    return false;
  }
  const n = name.trim().toLowerCase();
  return LOCKED_ROOT_COLUMNS.indexOf(n) >= 0;
}

/** The column a renderer/editor call is for, or undefined. */
export function columnOf(params: GetRendererParams | undefined): ColumnDefinition | undefined {
  if (!params || !Array.isArray(params.colDefs)) {
    return undefined;
  }
  return params.colDefs[params.columnIndex];
}

/**
 * Renderer side. For a root column: mark the cell and its column not editable and return true (the
 * caller then keeps the default renderer, which draws the value read-only). Otherwise false.
 */
export function markRootCellReadOnly(props: CellRendererProps, params: GetRendererParams): boolean {
  const column = columnOf(params);
  if (!column || !isLockedRootColumn(column.name)) {
    return false;
  }
  props.columnEditable = false;
  column.editable = false;
  return true;
}

/**
 * Editor side. For a root column: cancel the edit (`stopEditing(true)`), mark the column not
 * editable, and return true. Otherwise false, and the default editor runs.
 */
export function cancelRootCellEdit(_props: CellEditorProps, params: GetEditorParams): boolean {
  const column = columnOf(params);
  if (!column || !isLockedRootColumn(column.name)) {
    return false;
  }
  column.editable = false;
  params.stopEditing(true);
  return true;
}

/** The editor override registered for every data type. Returns null: the default editor (cancelled for a root). */
export const rootColumnEditorOverride: CellEditorOverride = (props, params) => {
  cancelRootCellEdit(props, params);
  return null;
};
