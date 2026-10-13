/**
 * record-targets - which record a deep link or a notification points at, and the ONE validation both go through
 * (spaarke-SPA-external-access-platform-r3 task 055; FR-07 link target, FR-08 feed item).
 *
 * Two inputs name a record, and neither is trusted:
 *
 *   - the page URL's `?record={type}:{id}` (a grant email, a Teams deep link). `type` must be one of
 *     {@link RECORD_LINK_TOKENS} and `id` a GUID, or the whole parameter is ignored (NFR-02). Task 036's email only writes
 *     `project`, `matter` and `workassignment`; the NFR-02 pattern also admits the other three entities the detail view
 *     can show (`document`, `invoice`, `servicerequest`).
 *   - a notification's `recordType`, which is an API ROUTE SEGMENT (`projects` | `matters` | `workassignments` |
 *     `invoices` | `servicerequests`; task 037), not an SPA route. {@link FEED_RECORD_TYPES} maps it to the detail view's entity.
 *
 * What comes out is a {@link RecordTarget}: an entity name `RecordDetailModal` knows and a lowercase GUID. It carries no
 * URL, host or scope, and it grants nothing: the detail view reads the record through the caller's own external-plane
 * token, and the server answers "denied" for a record outside the caller's set. A link can therefore only ever ASK to
 * open a record; it cannot widen what the caller may see.
 */

/** The record the shared opener should show. */
export interface RecordTarget {
  /** Logical name of a `RECORD_DETAIL_ENTITIES` entity. */
  entityName: string;
  /** Lowercase GUID. */
  recordId: string;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value);
}

/** `?record={type}` tokens -> detail entity. Exactly these; any other token means "no deep link". */
export const RECORD_LINK_TOKENS: Readonly<Record<string, string>> = {
  project: 'sprk_project',
  matter: 'sprk_matter',
  workassignment: 'sprk_workassignment',
  document: 'sprk_document',
  invoice: 'sprk_invoice',
  servicerequest: 'sprk_servicerequest',
};

/**
 * A notification's `recordType` (route segment, task 037) -> detail entity. Exactly the five the feed can produce: the
 * server's `SegmentByEntity` is built from `ExternalMessageEndpoints.Roots` (projects, matters, workassignments, invoices,
 * servicerequests), and each has a detail view in `RECORD_DETAIL_ENTITIES`.
 */
export const FEED_RECORD_TYPES: Readonly<Record<string, string>> = {
  projects: 'sprk_project',
  matters: 'sprk_matter',
  workassignments: 'sprk_workassignment',
  invoices: 'sprk_invoice',
  servicerequests: 'sprk_servicerequest',
};

function own<T>(table: Readonly<Record<string, T>>, key: string): T | undefined {
  return Object.prototype.hasOwnProperty.call(table, key) ? table[key] : undefined;
}

/** Name of the query parameter a deep link uses. */
export const RECORD_PARAM = 'record';

/** True when the query string carries a `record` parameter at all (valid or not). */
export function hasRecordParam(search: string): boolean {
  try {
    return new URLSearchParams(search).has(RECORD_PARAM);
  } catch {
    return false;
  }
}

/**
 * The record a query string asks to open, or null. Null for: no parameter, more than one `record` parameter (ambiguous),
 * a value that is not exactly `{token}:{guid}`, an unknown token (case-sensitive) or a malformed GUID.
 */
export function parseRecordLink(search: string): RecordTarget | null {
  let values: string[];
  try {
    values = new URLSearchParams(search).getAll(RECORD_PARAM);
  } catch {
    return null;
  }
  if (values.length !== 1) return null;
  const value = values[0];
  const colon = value.indexOf(':');
  if (colon <= 0) return null;
  const entityName = own(RECORD_LINK_TOKENS, value.slice(0, colon));
  const recordId = value.slice(colon + 1);
  if (!entityName || !isGuid(recordId)) return null;
  return { entityName, recordId: recordId.toLowerCase() };
}

/** The query string without its `record` parameter (every other parameter, e.g. `customer`, is kept as written). */
export function withoutRecordParam(search: string): string {
  const params = new URLSearchParams(search);
  params.delete(RECORD_PARAM);
  const rest = params.toString();
  return rest ? `?${rest}` : '';
}

/** The query string of a root-relative path (`/?customer=a&record=b` -> `?customer=a&record=b`), or ''. */
export function searchOfPath(path: string): string {
  const hash = path.indexOf('#');
  const noHash = hash >= 0 ? path.slice(0, hash) : path;
  const q = noHash.indexOf('?');
  return q >= 0 ? noHash.slice(q) : '';
}

/** The record a feed item opens, or null when its `recordType` is not one of the five or its id is not a GUID. */
export function recordTargetOfFeedItem(item: { recordType: string; recordId: string }): RecordTarget | null {
  const entityName = own(FEED_RECORD_TYPES, item.recordType);
  if (!entityName || !isGuid(item.recordId)) return null;
  return { entityName, recordId: item.recordId.toLowerCase() };
}
