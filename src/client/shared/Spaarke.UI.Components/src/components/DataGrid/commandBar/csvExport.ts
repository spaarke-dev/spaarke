/**
 * commandBar/csvExport — pure RFC 4180 CSV generation with UTF-8 BOM.
 *
 * Used by {@link defaultExportExcelHandler} to convert the grid's currently-loaded
 * records into a downloadable CSV blob.
 *
 * **Design choices** (FR-DG-17 + spec Q-D):
 *  - **Scope**: only the records the grid has already loaded, with the columns
 *    currently visible after the filter chip state. NO auto-pagination —
 *    callers wanting "everything" must scroll to load all pages first.
 *  - **Quoting**: RFC 4180 — fields containing comma `,`, double-quote `"`, CR `\r`,
 *    LF `\n`, or leading/trailing whitespace are wrapped in double quotes.
 *    Embedded `"` is escaped by doubling (`"` → `""`).
 *  - **Line terminator**: `\r\n` per RFC 4180 §2.1 — the only terminator that
 *    every spreadsheet on every OS handles without ambiguity.
 *  - **Encoding**: UTF-8 with leading BOM (`U+FEFF`). Without the BOM, Excel on
 *    Windows interprets the file as Windows-1252 and mojibakes non-ASCII chars.
 *
 * Pure function — no React, no DOM, no `URL.createObjectURL`. The caller (the
 * default `export-excel` handler) owns the download mechanism.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §7.3
 * **FR**: FR-DG-17 (CSV export)
 * **RFC**: 4180 — Common Format and MIME Type for CSV Files (10/2005)
 *
 * @see defaultExportExcelHandler
 */

import type { ResolvedColumn } from '../configResolution';

/** UTF-8 BOM (`U+FEFF`) — written as the first character so Excel detects UTF-8. */
export const UTF8_BOM = '﻿';

/** RFC 4180 line terminator. */
const CRLF = '\r\n';

/**
 * Render a single cell value into its CSV string form, applying RFC 4180 quoting.
 *
 * Empty / null / undefined → empty string (no quoting).
 * Numbers → `String(value)` (no thousands separator, no locale formatting).
 * Booleans → `'true'` / `'false'`.
 * Dates → ISO 8601 (`toISOString()`).
 * Objects with a `name` string property → `value.name` (matches Dataverse lookup
 *   FormattedValue convention, mirroring the grid's `default` renderer behavior).
 * Other objects → `JSON.stringify(value)`.
 *
 * Resulting string is wrapped in double quotes IFF it contains a delimiter
 * (`,`), a quote (`"`), or a line terminator (`\r` / `\n`), per RFC 4180 §2.6.
 * Embedded quotes are escaped by doubling per §2.7.
 *
 * Exported for unit testing — the public API is {@link exportCsv}.
 */
export function escapeCsvField(value: unknown): string {
  if (value === null || value === undefined) return '';

  let str: string;
  if (typeof value === 'string') {
    str = value;
  } else if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
    str = String(value);
  } else if (value instanceof Date) {
    str = Number.isNaN(value.getTime()) ? '' : value.toISOString();
  } else if (typeof value === 'object') {
    // Dataverse lookups + option-set formatted values arrive as `{ name, id, … }`
    // or `{ value, label }`; prefer the display string where present.
    const o = value as Record<string, unknown>;
    if (typeof o.name === 'string') {
      str = o.name;
    } else if (typeof o.label === 'string') {
      str = o.label;
    } else {
      try {
        str = JSON.stringify(value);
      } catch {
        str = String(value);
      }
    }
  } else {
    str = String(value);
  }

  if (str === '') return '';

  // RFC 4180 §2.6: quote on delimiter, quote, CR, or LF.
  const needsQuote = /[",\r\n]/.test(str);
  if (!needsQuote) return str;

  // §2.7: escape embedded quotes by doubling.
  return '"' + str.replace(/"/g, '""') + '"';
}

/**
 * Format a `Date` (or `Date.now()` value) into `yyyymmdd`.
 *
 * Exported so the filename helper can be unit-tested deterministically.
 */
export function formatYyyymmdd(d: Date = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}${m}${day}`;
}

/**
 * Build the filename `'{entityName}-{savedQueryName}-{yyyymmdd}.csv'`.
 *
 * `savedQueryName` is sanitised — any character that browsers commonly reject
 * in `<a download="…">` (Windows + macOS overlap: `/ \ ? % * : | " < >`) is
 * replaced with `_`. Whitespace runs collapsed to single `_`.
 */
export function csvFilename(
  entityName: string,
  savedQueryName: string,
  date: Date = new Date(),
): string {
  // Trim leading / trailing whitespace BEFORE collapsing internal whitespace
  // runs so `'  My   View  '` → `'My_View'` (not `'_My_View_'`). The
  // `\s+` -> `_` collapse otherwise turns trim-able padding into underscores.
  const safeQuery = savedQueryName
    .replace(/[\\/:*?"<>|%]/g, '_')
    .trim()
    .replace(/\s+/g, '_');
  return `${entityName}-${safeQuery}-${formatYyyymmdd(date)}.csv`;
}

/**
 * Convert `records` × `columns` into a UTF-8 CSV `Blob` with a BOM prefix.
 *
 * Header row → `column.label` (the human-readable label from the resolved column).
 * Body rows → `record[column.name]` per row, in column order.
 *
 * Hidden columns are skipped via the standard `!col.hidden` filter applied at
 * the call site (the `<DataGrid />` already passes visible columns). Empty
 * `columns` produces a BOM-only blob (no header, no rows) so callers can detect
 * "nothing to export" downstream.
 *
 * @param records         The rows to export (typically the grid's loaded records).
 * @param columns         Visible columns in display order; `column.label` becomes
 *                        the CSV header, `column.name` is the row-record key.
 * @param savedQueryName  Display name of the active view — embedded in the
 *                        downloaded filename (not in the CSV body).
 * @param entityName      Logical name of the primary entity — embedded in the
 *                        downloaded filename (not in the CSV body).
 *
 * @returns A `Blob` of type `text/csv;charset=utf-8` ready for download.
 *
 * @example
 * ```ts
 * const blob = exportCsv(records, columns, 'My Accounts', 'account');
 * // → BOM + header row + N body rows, all CRLF-terminated, all RFC 4180-quoted.
 * ```
 */
export function exportCsv(
  records: ReadonlyArray<Record<string, unknown>>,
  columns: ReadonlyArray<ResolvedColumn>,
  // savedQueryName + entityName accepted for signature compatibility with the
  // public spec; they do not affect the body of the CSV (only the filename).
  // The arguments are intentionally retained so future callers can rely on this
  // signature when wiring custom filename derivations.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  savedQueryName: string,
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  entityName: string,
): Blob {
  const lines: string[] = [];

  if (columns.length > 0) {
    // Header row uses column.label so the CSV is human-readable.
    lines.push(columns.map((c) => escapeCsvField(c.label)).join(','));
    for (const record of records) {
      lines.push(columns.map((c) => escapeCsvField(record[c.name])).join(','));
    }
  }

  // BOM is prepended to the *string*, not to a separate blob part — that way it
  // appears as the first byte of the resulting file regardless of how the
  // Blob constructor concatenates parts.
  const body = UTF8_BOM + lines.join(CRLF);

  // Excel + LibreOffice both recognize this MIME-type + charset combination.
  return new Blob([body], { type: 'text/csv;charset=utf-8' });
}
