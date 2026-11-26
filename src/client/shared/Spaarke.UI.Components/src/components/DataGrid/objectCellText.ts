/**
 * objectCellText - the one rule for the text of an object cell value that has no server label (D-34, #3045).
 *
 * Shared by the grid's display (`renderCellValue` in DataGrid.tsx) and the CSV export (`escapeCsvField` in
 * commandBar/csvExport.ts), so what a cell shows and what the file holds cannot drift. No imports: both can load it
 * without a cycle.
 *
 * - An SDK `AliasedValue` (a link-entity column: `{ attributeLogicalName, entityLogicalName, value }`) is its inner
 *   value, read by this same rule.
 * - A `name` string wins (a lookup), then a `label` string.
 * - A `Date` is its ISO text ('' when invalid).
 * - An array is its readable items, comma separated.
 * - Any other object has nothing a person can read (the option set `{ value: 1, extensionData: null }` carries a
 *   number that means nothing without its label), so it is '': never JSON, never "[object Object]".
 */
export function objectCellText(value: object): string {
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? '' : value.toISOString();
  if (Array.isArray(value)) {
    return value
      .map(item =>
        item !== null && typeof item === 'object' ? objectCellText(item) : item == null ? '' : String(item)
      )
      .filter(text => text !== '')
      .join(', ');
  }
  const v = value as Record<string, unknown>;
  if (typeof v.attributeLogicalName === 'string' && 'value' in v) {
    const inner = v.value;
    if (inner !== null && typeof inner === 'object') return objectCellText(inner);
    return inner == null ? '' : String(inner);
  }
  if (typeof v.name === 'string') return v.name;
  if (typeof v.label === 'string') return v.label;
  return '';
}
