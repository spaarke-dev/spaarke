/**
 * safeRegardingRecordUrl.ts — the one rule for a surface that opens the stored regarding record URL
 * (`sprk_regardingrecordurl`), field-mapping-server-write-path-r1 task 045f part B.
 *
 * `sprk_regardingrecordurl` is a display column derived from the row's regarding pair (the regarding record's table
 * and its id). A reader opens the stored value only when it is a same-origin record URL:
 *
 *   - RELATIVE: a path that starts with `/main.aspx?`; or
 *   - ABSOLUTE on the current origin: `http(s)://{host}[:port]/main.aspx?...` whose scheme, host and port equal
 *     `window.location.origin` (or the injected origin), with no credentials (what `buildRecordUrl` writes);
 *   - never protocol-relative (`//host`), any other scheme, another origin, a backslash, whitespace, a control
 *     character or a fragment;
 *   - naming exactly one `etn` (a table logical name) and exactly one `id` (a GUID).
 *
 * Any other value is never opened. The reader builds the URL from the pair instead
 * (`/main.aspx?pagetype=entityrecord&etn={table}&id={id}`), or, when it cannot do that either, does not navigate.
 *
 * The URL returned is always REBUILT in the canonical RELATIVE form from the parsed `etn` and `id` (an accepted
 * absolute value is returned as its relative form), so nothing else in the stored text (`appid`, other parameters,
 * the host) reaches the navigation call; a relative URL opens on the current origin.
 *
 * Pure and dependency-free so a plain web resource can carry the same few lines (copy `readSameOriginRecordTarget`).
 */

/** The record a regarding URL or pair names. `entityId` is the lowercase, brace-less "D" form. */
export interface IRegardingRecordTarget {
  entityName: string;
  entityId: string;
}

const TABLE_NAME = /^[a-z][a-z0-9_]*$/;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** True for a control character, whitespace, DEL, a backslash or a fragment marker anywhere in the value. */
function hasUnsafeChar(value: string): boolean {
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (code <= 0x20 || code === 0x7f || code === 0x5c || code === 0x23) return true;
  }
  return false;
}
const RELATIVE_RECORD_PREFIX = '/main.aspx?';

function normalizeTable(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const table = value.trim().toLowerCase();
  return TABLE_NAME.test(table) ? table : null;
}

function normalizeId(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const id = value
    .trim()
    .replace(/^\{(.*)\}$/, '$1')
    .toLowerCase();
  return GUID.test(id) ? id : null;
}

/** The canonical relative record URL for a table and id, or `null` when either is not a valid name or GUID. */
export function buildRegardingRecordUrl(
  table: string | null | undefined,
  id: string | null | undefined
): string | null {
  const t = normalizeTable(table);
  const i = normalizeId(id);
  return t && i ? `${RELATIVE_RECORD_PREFIX}pagetype=entityrecord&etn=${t}&id=${i}` : null;
}

/** The origin (`scheme://host[:port]`) a value names, or `null` when it is not a usable http(s) origin. */
function normalizeOrigin(value: string | null | undefined): string | null {
  if (typeof value !== 'string' || value.trim().length === 0) return null;
  try {
    const url = new URL(value.trim());
    return (url.protocol === 'https:' || url.protocol === 'http:') && url.origin !== 'null' ? url.origin : null;
  } catch {
    return null;
  }
}

/** `window.location.origin` of the running page, or `undefined` outside a browser. */
function currentOrigin(): string | undefined {
  return typeof window !== 'undefined' ? window.location?.origin : undefined;
}

/**
 * The query string of a same-origin record URL, or `null` when `value` is not one: a relative `/main.aspx?...`, or
 * an absolute `http(s)://` URL whose origin (scheme, host and port) equals `origin`, has no credentials and whose
 * path is `/main.aspx`.
 */
function sameOriginRecordQuery(value: string, origin: string | null | undefined): string | null {
  if (value.startsWith(RELATIVE_RECORD_PREFIX)) return value.slice(RELATIVE_RECORD_PREFIX.length);
  if (!/^https?:\/\//i.test(value)) return null;
  const expected = normalizeOrigin(origin);
  if (!expected) return null;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  if (url.origin !== expected || url.username !== '' || url.password !== '') return null;
  if (url.pathname.toLowerCase() !== '/main.aspx') return null;
  return url.search.startsWith('?') ? url.search.slice(1) : null;
}

/**
 * Reads `{ entityName, entityId }` from a same-origin record URL (relative, or absolute on `origin`, which defaults
 * to `window.location.origin`), or `null` for any other value (another origin or scheme, protocol-relative, other
 * path, missing or repeated `etn`/`id`, a non-GUID id).
 */
export function readSameOriginRecordTarget(
  stored: string | null | undefined,
  origin?: string | null
): IRegardingRecordTarget | null {
  if (typeof stored !== 'string') return null;
  const value = stored.trim();
  if (hasUnsafeChar(value)) return null;
  const query = sameOriginRecordQuery(value, origin === undefined ? currentOrigin() : origin);
  if (query === null || query.length === 0) return null;
  let params: URLSearchParams;
  try {
    params = new URLSearchParams(query);
  } catch {
    return null;
  }
  const etns = params.getAll('etn');
  const ids = params.getAll('id');
  if (etns.length !== 1 || ids.length !== 1) return null;
  const entityName = normalizeTable(etns[0]);
  const entityId = normalizeId(ids[0]);
  return entityName && entityId ? { entityName, entityId } : null;
}

/**
 * The record a reader may open: the stored value when it is a same-origin relative record URL (and, when the row's
 * pair is known, it names that pair); otherwise the pair; otherwise `null` (do not navigate). `origin` is the
 * current origin an absolute stored URL must carry (default `window.location.origin`).
 */
export function resolveRegardingRecordTarget(
  stored: string | null | undefined,
  pairTable?: string | null,
  pairId?: string | null,
  origin?: string | null
): IRegardingRecordTarget | null {
  const pairEntity = normalizeTable(pairTable);
  const pairRecord = normalizeId(pairId);
  const pair = pairEntity && pairRecord ? { entityName: pairEntity, entityId: pairRecord } : null;
  const fromStored = readSameOriginRecordTarget(stored, origin);
  if (fromStored && (!pair || (fromStored.entityName === pair.entityName && fromStored.entityId === pair.entityId))) {
    return fromStored;
  }
  return pair;
}

/**
 * The URL a reader may open or render as an `href`, in the canonical relative form, or `null` when the reader
 * must not navigate. See {@link resolveRegardingRecordTarget}.
 */
export function safeRegardingRecordUrl(
  storedUrl: string | null | undefined,
  pairTable?: string | null,
  pairId?: string | null,
  origin?: string | null
): string | null {
  const target = resolveRegardingRecordTarget(storedUrl, pairTable, pairId, origin);
  return target ? buildRegardingRecordUrl(target.entityName, target.entityId) : null;
}

/** The minimal Xrm.WebApi shape {@link resolveRegardingPairTable} needs. */
export interface IRetrieveRecordWebApi {
  retrieveRecord(entityLogicalName: string, id: string, options?: string): Promise<Record<string, unknown>>;
}

/**
 * The table logical name a regarding record type (`sprk_recordtype_ref` row) stands for, or `null` when it cannot be
 * read. Never throws: a reader that gets `null` does not navigate.
 */
export async function resolveRegardingPairTable(
  webApi: IRetrieveRecordWebApi | null | undefined,
  recordTypeId: string | null | undefined
): Promise<string | null> {
  const typeId = normalizeId(recordTypeId);
  if (!typeId || !webApi || typeof webApi.retrieveRecord !== 'function') return null;
  try {
    const row = await webApi.retrieveRecord('sprk_recordtype_ref', typeId, '?$select=sprk_recordlogicalname');
    return normalizeTable(row?.sprk_recordlogicalname);
  } catch {
    return null;
  }
}
