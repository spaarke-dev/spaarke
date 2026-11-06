/**
 * recordFamilyPushService — the client side of `POST /api/v1/field-mappings/push`
 * (field-mapping-server-write-path-r1 task 040, spec FR-34 / FR-35).
 *
 * One request shape serves the three steps of an action: a PREVIEW (`preview: true`, nothing is written), and the APPLY
 * (the same request without `preview`, with `childIds` = exactly the children the person saw in the preview). The server
 * re-derives the children as the caller and re-checks everything; the ids sent here are a confirmation, never an authority.
 *
 * The fetch is injected (ADR-028: the host passes `authenticatedFetch`, never a token). It may THROW on a non-2xx
 * (`@spaarke/auth`'s fetch) or RETURN a failed response (the external SPA, the Office add-ins): both reach the caller as a
 * rejection. The cause is never shown (ADR-019); the panel renders {@link PUSH_FAILED_TEXT}.
 */

import { cleanGuid } from '../../utils/guid';
import type { ResponseFetchFn } from '../../utils/fetchTypes';
import type { IFamilySelection } from './recordFamilyService';

/** The BFF route (`FieldMappingEndpoints`). */
export const PUSH_PATH = '/api/v1/field-mappings/push';

/** The only family kind a push supports (ontology 3b: Push and Update Assigned To are Filing-only). */
export const PUSH_KIND = 'Filing';

/** ADR-019: fixed, actionable text; the cause is never rendered. */
export const PUSH_FAILED_TEXT = 'The update could not be completed. Close this window and try again.';

/** The record a push copies FROM. */
export interface IPushSource {
  /** Logical name, e.g. `sprk_matter`. */
  table: string;
  id: string;
}

/** What is sent. `childIds` is the confirmation; `includeAssignedTo` is the opt-in for the Assigned-To fields. */
export interface IPushRequest {
  sourceEntity: string;
  sourceRecordId: string;
  targetEntity: string;
  preview?: boolean;
  includeAssignedTo?: boolean;
  childIds?: string[];
  kind?: string;
}

/** One value a preview says would change. `flags` are the Assigned-To policies the change meets. */
export interface IPushPreviewChange {
  recordId: string;
  name: string | null;
  column: string;
  from: string | null;
  to: string | null;
  /** `secure` (a suggestion, no access written), `restricted` (no contact grant), `limited` (no organization grant). */
  flags: string[];
}

/** A child the push refused or could not update; `error` is the server's fixed text. */
export interface IPushError {
  recordId: string;
  error: string;
}

/** The response, reduced to what the panel uses. Absent counts read as zero. */
export interface IPushResponse {
  success: boolean;
  /** True for a preview: nothing was written. */
  preview: boolean;
  updatedCount: number;
  failedCount: number;
  /** Candidate children left out because the caller lacks Write on them (preview). */
  skippedNoWriteCount: number;
  /** More children exist than one push handles, so a preview list is partial. */
  truncated: boolean;
  /** Rules the profile has that a push does not apply (not Copy, or not Manual Refresh). */
  skippedRuleCount: number;
  changes: IPushPreviewChange[];
  errors: IPushError[];
}

/** The request body for one target table. Ids are canonicalized (ADR-044); `kind` is always Filing. */
export function buildPushRequest(
  source: IPushSource,
  targetEntity: string,
  options: { preview: boolean; includeAssignedTo: boolean; childIds?: readonly string[] }
): IPushRequest {
  const request: IPushRequest = {
    sourceEntity: source.table,
    sourceRecordId: cleanGuid(source.id),
    targetEntity,
    preview: options.preview,
    includeAssignedTo: options.includeAssignedTo,
    kind: PUSH_KIND,
  };
  if (options.childIds) request.childIds = options.childIds.map(cleanGuid);
  return request;
}

/** The ticked rows grouped by table, ids de-duplicated, in first-seen order. One push is made per table. */
export function groupRowsByTable(rows: readonly IFamilySelection[]): Array<{ table: string; ids: string[] }> {
  const groups = new Map<string, string[]>();
  for (const row of rows) {
    const ids = groups.get(row.table) ?? [];
    const id = cleanGuid(row.id);
    if (!ids.includes(id)) ids.push(id);
    groups.set(row.table, ids);
  }
  return [...groups].map(([table, ids]) => ({ table, ids }));
}

const text = (v: unknown): string | null => (typeof v === 'string' ? v : null);
const count = (v: unknown): number => (typeof v === 'number' && Number.isFinite(v) && v > 0 ? Math.floor(v) : 0);

function asChange(raw: unknown): IPushPreviewChange {
  if (typeof raw !== 'object' || raw === null) throw new Error('The push response has a change that is not an object.');
  const r = raw as Record<string, unknown>;
  const recordId = text(r.recordId);
  const column = text(r.column);
  if (!recordId || !column) throw new Error('The push response has a change without a record or column.');
  return {
    recordId,
    name: text(r.name),
    column,
    from: text(r.from),
    to: text(r.to),
    flags: Array.isArray(r.flags) ? r.flags.filter((f): f is string => typeof f === 'string') : [],
  };
}

function asError(raw: unknown): IPushError {
  if (typeof raw !== 'object' || raw === null) throw new Error('The push response has an error that is not an object.');
  const r = raw as Record<string, unknown>;
  const recordId = text(r.recordId);
  if (!recordId) throw new Error('The push response has an error without a record.');
  return { recordId, error: text(r.error) ?? '' };
}

/** Reads the body. It is untrusted server JSON: a body that is not the documented shape is an error, never an empty result. */
export function parsePushResponse(body: unknown): IPushResponse {
  if (typeof body !== 'object' || body === null) throw new Error('The push response was not in the expected shape.');
  const b = body as Record<string, unknown>;
  return {
    success: b.success === true,
    preview: b.preview === true,
    updatedCount: count(b.updatedCount),
    failedCount: count(b.failedCount),
    skippedNoWriteCount: count(b.skippedNoWriteCount),
    truncated: b.truncated === true,
    skippedRuleCount: count(b.skippedRuleCount),
    changes: Array.isArray(b.changes) ? b.changes.map(asChange) : [],
    errors: Array.isArray(b.errors) ? b.errors.map(asError) : [],
  };
}

/** Sends one push request. Rejects on any failure (4xx, 5xx, network, malformed body); the caller shows {@link PUSH_FAILED_TEXT}. */
export async function pushFieldMappings(fetchFn: ResponseFetchFn, request: IPushRequest): Promise<IPushResponse> {
  const res = await fetchFn(PUSH_PATH, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  if (!res.ok) throw new Error(`The push request failed (${res.status}).`);
  return parsePushResponse(await res.json());
}
