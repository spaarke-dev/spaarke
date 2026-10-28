/**
 * recordFamilyService — the client side of `GET /api/v1/records/{table}/{id}/family`
 * (field-mapping-server-write-path-r1 spec FR-31, design §12.3 C).
 *
 * The endpoint is task 072. These types are the contract FR-31 / task 072 state:
 *   - query: `continuation` (opaque). `kind` defaults to Filing and `direction` to the full family,
 *     so this client sends neither (the Family view is the full Filing family, owner 2026-10-27);
 *   - response: `anchor`, optional `root`, `truncated`, `continuation`, `members[]`;
 *   - member: id, table, name, recordNumber, recordType, status, modifiedOn, kind, edgeKind,
 *     parentId, depth. No access metadata and no count of records the caller cannot read — the
 *     modal therefore has nothing to show about them.
 *
 * The fetch is injected (ADR-028: the host passes `authenticatedFetch`, never a token). It may THROW
 * on a non-2xx (`@spaarke/auth`'s fetch: `ApiError`/`AuthError`) or RETURN a failed response (the
 * external SPA, the Office add-ins): both reach the caller as a rejection.
 */

import { cleanGuid } from '../../utils/guid';
import type { ResponseFetchFn } from '../../utils/fetchTypes';

/** One record of the family, exactly as FR-31 returns it. Every display value may be null (field security, or none). */
export interface IFamilyMember {
  id: string;
  /** Dataverse logical name, e.g. `sprk_matter`. */
  table: string;
  name: string | null;
  /** Null for a table without a record-number column, or when field security hides it. */
  recordNumber: string | null;
  recordType: string | null;
  status: string | null;
  /** ISO 8601. */
  modifiedOn: string | null;
  /** The family kind of the walk that produced this member (`Filing` in W7). */
  kind: string;
  /** How the member is joined to its parent (`FilingCore`, `Stamp`, `LineageHop`, ...). */
  edgeKind: string;
  /** The member's parent in the family; null for the topmost member. */
  parentId: string | null;
  /** The level in the family, as the server numbers it. */
  depth: number;
}

/** The record the walk is described from (`anchor`) or the topmost readable record (`root`). */
export interface IFamilyRef {
  id: string;
  table: string;
  name?: string | null;
}

/** The FR-31 response body. */
export interface IFamilyResponse {
  anchor: IFamilyRef;
  /** Absent when there is no readable root (and for a rootless family kind). */
  root?: IFamilyRef | null;
  /** True when a cap cut the view; `continuation` then loads more. */
  truncated: boolean;
  /** Opaque, integrity-protected by the server. Send it back unchanged. */
  continuation?: string | null;
  members: IFamilyMember[];
}

/** A (table, id) pair — what an action receives for each confirmed row. */
export interface IFamilySelection {
  table: string;
  id: string;
}

/** The FR-31 route. The id is canonicalized (ADR-044) before it enters the URL. */
export function buildFamilyPath(table: string, recordId: string, continuation?: string | null): string {
  const base = `/api/v1/records/${encodeURIComponent(table)}/${encodeURIComponent(cleanGuid(recordId))}/family`;
  return continuation ? `${base}?continuation=${encodeURIComponent(continuation)}` : base;
}

/** The identity of a member for de-duplication and selection: (table, id), case/brace-insensitive id. */
export function familyKey(ref: { table: string; id: string }): string {
  return `${ref.table}|${cleanGuid(ref.id)}`;
}

/** Merges a further page into the loaded members: unique by (table, id), the first copy kept. */
export function mergeFamilyMembers(
  loaded: readonly IFamilyMember[],
  incoming: readonly IFamilyMember[]
): IFamilyMember[] {
  const seen = new Set(loaded.map(familyKey));
  const merged = [...loaded];
  for (const m of incoming) {
    const key = familyKey(m);
    if (seen.has(key)) continue;
    seen.add(key);
    merged.push(m);
  }
  return merged;
}

const str = (v: unknown): string | null => (typeof v === 'string' && v !== '' ? v : null);

function asMember(raw: unknown): IFamilyMember | null {
  if (typeof raw !== 'object' || raw === null) return null;
  const r = raw as Record<string, unknown>;
  const id = str(r.id);
  const table = str(r.table);
  if (!id || !table) return null;
  return {
    id,
    table,
    name: str(r.name),
    recordNumber: str(r.recordNumber),
    recordType: str(r.recordType),
    status: str(r.status),
    modifiedOn: str(r.modifiedOn),
    kind: str(r.kind) ?? 'Filing',
    edgeKind: str(r.edgeKind) ?? '',
    parentId: str(r.parentId),
    depth: typeof r.depth === 'number' && Number.isFinite(r.depth) ? r.depth : 0,
  };
}

function asRef(raw: unknown): IFamilyRef | null {
  if (typeof raw !== 'object' || raw === null) return null;
  const r = raw as Record<string, unknown>;
  const id = str(r.id);
  const table = str(r.table);
  return id && table ? { id, table, name: str(r.name) } : null;
}

/**
 * Reads the FR-31 body. The body is untrusted server JSON: a body that is not the documented shape is an
 * error (never an empty family), and a member without an id or table is dropped.
 */
export function parseFamilyResponse(body: unknown): IFamilyResponse {
  if (typeof body !== 'object' || body === null || !Array.isArray((body as { members?: unknown }).members)) {
    throw new Error('The family response was not in the expected shape.');
  }
  const b = body as Record<string, unknown>;
  const anchor = asRef(b.anchor);
  if (!anchor) throw new Error('The family response has no anchor.');
  const members = (b.members as unknown[]).map(asMember).filter((m): m is IFamilyMember => m !== null);
  return {
    anchor,
    root: asRef(b.root),
    truncated: b.truncated === true,
    continuation: str(b.continuation),
    members: mergeFamilyMembers([], members),
  };
}

/**
 * Fetches one page of the family. Rejects on any failure (404, 400, 401, network, malformed body); the
 * caller shows ONE fixed error state and never the cause (ADR-019, and FR-31's uniform 404).
 */
export async function fetchRecordFamily(
  fetchFn: ResponseFetchFn,
  table: string,
  recordId: string,
  continuation?: string | null
): Promise<IFamilyResponse> {
  const res = await fetchFn(buildFamilyPath(table, recordId, continuation), { method: 'GET' });
  if (!res.ok) throw new Error(`The family request failed (${res.status}).`);
  return parseFamilyResponse(await res.json());
}
