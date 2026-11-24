/**
 * gridSettingsStore — the column widths and sort a user chose on a grid, remembered per browser
 * (spaarke-SPA-external-access-platform-r4 task 032, FR-19, #2489).
 *
 * WHERE. `localStorage` of this browser, ONE entry per grid, keyed by the grid's configuration id. There is no server
 * store and no cross-device sync (owner 2026-10-31: per browser first). Storage policy is the host's, so it lives here and
 * not in the shared grid (ADR-012): the shared `DataGrid` only reports and accepts plain settings.
 *
 * WHAT. The widths of the columns the user dragged (column name → px) and the sorted column and direction. No identity, no
 * record id, no row data. The key carries no user: two people on one browser share a grid's widths (the section-collapse
 * state in `recordSectionState.ts` is per user because it is a personal choice; a column width is not sensitive).
 *
 * FAIL-OPEN. Every read and write is wrapped in try/catch. A store that is missing, blocked (private mode, site data off),
 * full, throwing, or holding anything that is not exactly the stored shape means "no settings": the grid shows its
 * defaults, nothing is shown to the user and nothing is thrown. Settings are then simply not remembered.
 */
import type { ExternalDataGridProps } from '../widgets/ExternalDataGrid';

/** The grid's own settings type, read off the wrapper's props (this SPA never imports the shared grid's module). */
export type GridSettings = NonNullable<ExternalDataGridProps['initialSettings']>;

/** Bump the version when the stored shape changes, so an entry of an older shape is simply ignored. */
const SCHEMA_VERSION = 1;
const KEY_PREFIX = 'sprk.ext.gridSettings.v1';

const storageKeyOf = (configId: string): string => `${KEY_PREFIX}.${configId.toLowerCase()}`;

/** The widest column a stored entry may ask for (px). A wider one is a tampered entry: dropped, like any invalid value. */
const MAX_STORED_WIDTH_PX = 2000;

const isUsableWidth = (w: unknown): w is number =>
  typeof w === 'number' && Number.isFinite(w) && w > 0 && w <= MAX_STORED_WIDTH_PX;

/** Narrows anything read back from storage to settings, or `undefined` when it is not exactly the stored shape. */
function parseStored(raw: string | null): GridSettings | undefined {
  if (!raw) return undefined;
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return undefined;
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return undefined;
  const entry = parsed as { v?: unknown; widths?: unknown; sort?: unknown };
  if (entry.v !== SCHEMA_VERSION) return undefined;

  const widths: Record<string, number> = {};
  if (entry.widths !== undefined) {
    if (!entry.widths || typeof entry.widths !== 'object' || Array.isArray(entry.widths)) return undefined;
    for (const [id, w] of Object.entries(entry.widths as Record<string, unknown>)) {
      if (isUsableWidth(w)) widths[id] = w;
    }
  }

  let sort: GridSettings['sort'];
  if (entry.sort !== undefined) {
    const s = entry.sort as { columnId?: unknown; direction?: unknown } | null;
    if (
      !s ||
      typeof s !== 'object' ||
      typeof s.columnId !== 'string' ||
      s.columnId.length === 0 ||
      (s.direction !== 'ascending' && s.direction !== 'descending')
    ) {
      return undefined;
    }
    sort = { columnId: s.columnId, direction: s.direction };
  }

  return isEmpty({ widths, sort }) ? undefined : sort ? { widths, sort } : { widths };
}

/** True when the settings hold nothing the user chose (no width, no sort). */
export function isEmpty(settings: GridSettings | undefined): boolean {
  return !settings || (Object.keys(settings.widths ?? {}).length === 0 && !settings.sort);
}

/** The saved settings of one grid, or `undefined` when there are none or they cannot be read. Never throws. */
export function loadGridSettings(configId: string): GridSettings | undefined {
  if (!configId) return undefined;
  try {
    return parseStored(window.localStorage.getItem(storageKeyOf(configId)));
  } catch {
    return undefined;
  }
}

/** Forgets one grid's settings. A failing store is ignored. Never throws. */
export function clearGridSettings(configId: string): void {
  if (!configId) return;
  try {
    window.localStorage.removeItem(storageKeyOf(configId));
  } catch {
    // Nothing to clear, or the store is not available: the next load answers "no settings" either way.
  }
}

/**
 * Remembers one grid's settings (replacing its entry). Settings with nothing in them clear the entry instead, so a grid the
 * user reset leaves no trace. A failing store (blocked, full, throwing) is ignored. Never throws.
 */
export function saveGridSettings(configId: string, settings: GridSettings): void {
  if (!configId) return;
  if (isEmpty(settings)) {
    clearGridSettings(configId);
    return;
  }
  try {
    const widths: Record<string, number> = {};
    for (const [id, w] of Object.entries(settings.widths ?? {})) if (isUsableWidth(w)) widths[id] = w;
    const entry: { v: number; widths: Record<string, number>; sort?: GridSettings['sort'] } = {
      v: SCHEMA_VERSION,
      widths,
    };
    if (settings.sort) entry.sort = { columnId: settings.sort.columnId, direction: settings.sort.direction };
    window.localStorage.setItem(storageKeyOf(configId), JSON.stringify(entry));
  } catch {
    // Not remembered; the grid still shows what the user chose for this visit.
  }
}
