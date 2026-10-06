/**
 * RootColumnLock - the regarding FILING columns are NOT editable in the grid.
 *
 * unified-access-control-r2 task 168. Owner round 25 item 8 (binding): the editable Power Apps home
 * grids of sprk_event and sprk_analysis must not let a person type a root directly onto a row. Task
 * 156's rule keeps the regarding PAIR as the source of a filed row's root, so a root typed onto a
 * filed row is a copy the reconciliation job reverts (owner round 8 item 3); the forms lock these
 * columns (Lock-CoreAncestorStampColumnsOnForms.ps1) and this is the same lock for the grid.
 *
 * Owner round 38 (binding): the grid lock covers the same columns the forms HIDE as well
 * (Add-RegardingFilingPickerToForms.ps1): the ADR-024 pair and every non-root sprk_regarding*
 * lookup. A person can add them to the grid with "Edit columns"; a typed pair re-files the row
 * without the picker. The set comes BY NAME from ONE shared list, config/regarding-filing-columns.json
 * at the repository root, the file both form scripts read. Never a second copy here.
 *
 * A FILING column (the shared file's rule), compared as a WHOLE logical name, case-insensitively:
 *  - a root column (CoreAncestorResolver.CoreAncestorLookups), whatever its data type;
 *  - the record-type column or a pair text column, whatever its data type;
 *  - a column whose name starts with the lookup prefix (sprk_regarding) and whose data type is one of
 *    the lookup types: the column definition's dataType, the cell props' columnDataType, or the data
 *    type the grid dispatched the override for (any of them; they agree in the grid).
 * sprk_RegardingMatter matches; sprk_regardingmatterstatus is not a root (and, unless it is a lookup,
 * not a filing column at all).
 *
 *  - editor override: any attempt to edit a filing column is cancelled with `stopEditing(true)` (the
 *    documented pattern; it runs for every way the grid opens an editor, so keyboard navigation does
 *    not bypass it);
 *  - renderer override: the cell is shown as not editable (`columnEditable = false`), and the column
 *    definition is marked `editable = false`. The cell keeps its renderer (the default one, or the
 *    regarding link for a pair name / id cell).
 * Every other column keeps inline editing.
 */

import filingColumns from '../../../../../config/regarding-filing-columns.json';
import type {
  CellEditorOverride,
  CellEditorProps,
  CellRendererProps,
  ColumnDefinition,
  GetEditorParams,
  GetRendererParams,
} from '../types/PAGridCustomizer';

function lowerAll(values: readonly string[]): readonly string[] {
  return values.map(v => v.trim().toLowerCase()).filter(v => v.length > 0);
}

/** CoreAncestorResolver.CoreAncestorLookups: the four core-ancestor root lookups (from the shared list). */
export const LOCKED_ROOT_COLUMNS: readonly string[] = lowerAll(filingColumns.rootColumns);

/** The ADR-024 pair: the record-type lookup and the pair text columns (from the shared list). */
export const PAIR_COLUMNS: readonly string[] = lowerAll([
  filingColumns.recordTypeColumn,
  ...filingColumns.pairTextColumns,
]);

/** A column whose name starts with this and whose type is a lookup type is a filing column. */
export const FILING_LOOKUP_PREFIX: string = filingColumns.lookupPrefix.trim().toLowerCase();

/** The data type names that count as a lookup (the same names the form scripts read from metadata). */
export const FILING_LOOKUP_TYPES: readonly string[] = filingColumns.lookupTypes.map(t => t.trim());

function normalizedName(name: string | null | undefined): string | null {
  return typeof name === 'string' ? name.trim().toLowerCase() : null;
}

/** True when `name` is one of the four root columns (whole value, case-insensitive). */
export function isLockedRootColumn(name: string | null | undefined): boolean {
  const n = normalizedName(name);
  return n !== null && LOCKED_ROOT_COLUMNS.indexOf(n) >= 0;
}

/**
 * True when the column is a filing column: a root, a pair column, or a sprk_regarding* column of a
 * lookup type. `dataTypes` are the types the grid reports for the cell (any one being a lookup type
 * makes it a lookup).
 */
export function isFilingColumn(name: string | null | undefined, dataTypes: readonly (string | undefined)[]): boolean {
  const n = normalizedName(name);
  if (n === null || n.length === 0) {
    return false;
  }
  if (LOCKED_ROOT_COLUMNS.indexOf(n) >= 0 || PAIR_COLUMNS.indexOf(n) >= 0) {
    return true;
  }
  if (!n.startsWith(FILING_LOOKUP_PREFIX)) {
    return false;
  }
  return dataTypes.some(t => typeof t === 'string' && FILING_LOOKUP_TYPES.indexOf(t) >= 0);
}

/** The column a renderer/editor call is for, or undefined. */
export function columnOf(params: GetRendererParams | undefined): ColumnDefinition | undefined {
  if (!params || !Array.isArray(params.colDefs)) {
    return undefined;
  }
  return params.colDefs[params.columnIndex];
}

/**
 * Renderer side. For a filing column: mark the cell and its column not editable and return true (the
 * caller keeps the cell's renderer, which then draws the value read-only). Otherwise false.
 */
export function markFilingCellReadOnly(
  props: CellRendererProps,
  params: GetRendererParams,
  dispatchedType?: string
): boolean {
  const column = columnOf(params);
  if (!column || !isFilingColumn(column.name, [column.dataType, props.columnDataType, dispatchedType])) {
    return false;
  }
  props.columnEditable = false;
  column.editable = false;
  return true;
}

/**
 * Editor side. For a filing column: cancel the edit (`stopEditing(true)`), mark the column not
 * editable, and return true. Otherwise false, and the default editor runs.
 */
export function cancelFilingCellEdit(
  props: CellEditorProps,
  params: GetEditorParams,
  dispatchedType?: string
): boolean {
  const column = columnOf(params);
  if (!column || !isFilingColumn(column.name, [column.dataType, props.columnDataType, dispatchedType])) {
    return false;
  }
  column.editable = false;
  params.stopEditing(true);
  return true;
}

/**
 * The editor override for one data type (registered for every data type). Returns null: the default
 * editor, cancelled for a filing column.
 */
export function filingColumnEditorOverride(dispatchedType: string): CellEditorOverride {
  return (props, params) => {
    cancelFilingCellEdit(props, params, dispatchedType);
    return null;
  };
}
