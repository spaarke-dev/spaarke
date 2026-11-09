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
import { isApiError, problemOf } from '../../utils/thrownFetchError';
import type { IFamilySelection } from './recordFamilyService';

/** The BFF route (`FieldMappingEndpoints`). */
export const PUSH_PATH = '/api/v1/field-mappings/push';

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

/**
 * A child the push refused or could not update; `error` is the server's fixed text. `reasonCode` is set only for the
 * mapping-source rule's codes (task 022 fix round 4), on the apply and on the preview.
 */
export interface IPushError {
  recordId: string;
  error: string;
  reasonCode?: string;
}

/**
 * Task 022 fix round 4 (the mapping-source rule): values copied from a secure source are written only into a record owned by
 * that source's isolation owner. A child that is not is not written (`FieldMappingPushSourceGate.NotIsolatedReasonCode`).
 */
export const SECURE_SOURCE_NOT_ISOLATED_REASON_CODE = 'field_mapping.push.secure_source_not_isolated';

/** Whether the source is secure, or the child's owner, could not be checked: the child is not written. */
export const SECURE_SOURCE_UNDETERMINED_REASON_CODE = 'field_mapping.push.secure_source_undetermined';

/**
 * A push from a secure source never changes what a child is filed under: a child whose pushed values would change its filing
 * is not written at all (`FieldMappingPushSourceGate.RefileNotAllowedReasonCode`).
 */
export const SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE = 'field_mapping.push.secure_source_refile_not_allowed';

/** The per-record wording of each mapping-source code (never a generic error). */
const SOURCE_RULE_TEXT: Readonly<Record<string, string>> = {
  [SECURE_SOURCE_NOT_ISOLATED_REASON_CODE]:
    "Some fields weren't copied to this record because the source record is secure.",
  [SECURE_SOURCE_UNDETERMINED_REASON_CODE]:
    "Fields weren't copied to this record because it could not be checked whether the source record is secure. Try again later.",
  [SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE]:
    "This record's filing can't be changed by a push from a secure record, so nothing was copied to it.",
};

/** True when the entry is a child the mapping-source rule kept the push from writing. */
export function isSourceRuleSkip(error: IPushError): boolean {
  return error.reasonCode !== undefined && Object.prototype.hasOwnProperty.call(SOURCE_RULE_TEXT, error.reasonCode);
}

/** The text shown for one child the push did not update: the mapping-source code's wording, else the server's fixed text. */
export function pushErrorText(error: IPushError): string {
  return isSourceRuleSkip(error) ? SOURCE_RULE_TEXT[error.reasonCode as string] : error.error;
}

/**
 * The preview's lines for the children the mapping-source rule keeps the push from writing, one per code; empty when there
 * are none. Those children are not listed with values and will not be updated.
 */
export function sourceRuleSkipText(errors: readonly IPushError[]): string[] {
  const lines: string[] = [];
  const notIsolated = errors.filter(e => e.reasonCode === SECURE_SOURCE_NOT_ISOLATED_REASON_CODE).length;
  const undetermined = errors.filter(e => e.reasonCode === SECURE_SOURCE_UNDETERMINED_REASON_CODE).length;
  const refile = errors.filter(e => e.reasonCode === SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE).length;
  if (refile > 0) {
    lines.push(
      refile === 1
        ? "1 record is not listed because its filing can't be changed by a push from a secure record."
        : `${refile} records are not listed because their filing can't be changed by a push from a secure record.`
    );
  }
  if (notIsolated > 0) {
    lines.push(
      notIsolated === 1
        ? "Some fields won't be copied to 1 record because the source record is secure, so it is not listed."
        : `Some fields won't be copied to ${notIsolated} records because the source record is secure, so they are not listed.`
    );
  }
  if (undetermined > 0) {
    lines.push(
      undetermined === 1
        ? '1 record is not listed because it could not be checked whether the source record is secure. Try again later.'
        : `${undetermined} records are not listed because it could not be checked whether the source record is secure. Try again later.`
    );
  }
  return lines;
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
  /**
   * Rules the profile has that a push does not apply (not Copy, not Manual Refresh, or, task 032b-1, a display copy such as an
   * invoice's attorney columns, which only the create sets). Carried on the preview and the apply.
   */
  skippedRuleCount: number;
  changes: IPushPreviewChange[];
  errors: IPushError[];
  /** Preview: ticked ids the server will not update (not children of the source for this person). */
  refusedChildIds: string[];
}

/** The stable code on the push route's 404 when no profile maps the (source, target) pair (`FieldMappingEndpoints.ProfileMissingReasonCode`). */
export const PROFILE_MISSING_REASON_CODE = 'field_mapping_profile_missing';

/**
 * A push the server answered with a non-2xx status (a fetch that RETURNS failures). The server's text is never read or shown;
 * only the stable `reasonCode` of a 404 is carried, so a missing profile can be told from a source that was not found.
 */
export class PushRequestError extends Error {
  constructor(
    public readonly status: number,
    /** The problem body's `reasonCode`, read for a 404 only; `undefined` when the body had none or was not JSON. */
    public readonly reasonCode?: string
  ) {
    super(`The push request failed (${status}).`);
    this.name = 'PushRequestError';
  }
}

/**
 * True when the server refused a push because no Filing edge joins the source to that table (409): the records of that
 * table cannot be pushed to. Reached from either fetch shape: a returned 409 ({@link PushRequestError}) or a thrown `ApiError`.
 */
export function isNoFilingEdgeRefusal(err: unknown): boolean {
  return (err instanceof PushRequestError && err.status === 409) || isApiError(err, 409);
}

/**
 * True when the server answered 404 with the `field_mapping_profile_missing` code: no profile maps the source to that table,
 * so it is reported for that table only. A 404 WITHOUT the code (the source was not found) is not this: it fails the whole
 * preview. Reached from either fetch shape: a returned 404 ({@link PushRequestError}) or a thrown `ApiError`.
 */
export function isNoProfileRefusal(err: unknown): boolean {
  if (err instanceof PushRequestError) return err.status === 404 && err.reasonCode === PROFILE_MISSING_REASON_CODE;
  return isApiError(err, 404) && problemOf(err)?.reasonCode === PROFILE_MISSING_REASON_CODE;
}

/** The `reasonCode` of a failed response's problem body, or `undefined` (no body, not JSON, not a string). Never throws. */
async function readReasonCode(res: { json?: () => Promise<unknown> }): Promise<string | undefined> {
  try {
    const body = await res.json?.();
    const code = typeof body === 'object' && body !== null ? (body as { reasonCode?: unknown }).reasonCode : undefined;
    return typeof code === 'string' ? code : undefined;
  } catch {
    return undefined;
  }
}

/** The request body for one target table. Ids are canonicalized (ADR-044). The server always uses the Filing family. */
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
  const reasonCode = text(r.reasonCode);
  return reasonCode ? { recordId, error: text(r.error) ?? '', reasonCode } : { recordId, error: text(r.error) ?? '' };
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
    refusedChildIds: Array.isArray(b.refusedChildIds)
      ? b.refusedChildIds.filter((id): id is string => typeof id === 'string')
      : [],
  };
}

/** Sends one push request. Rejects on any failure (4xx, 5xx, network, malformed body); the caller shows {@link PUSH_FAILED_TEXT}. */
export async function pushFieldMappings(fetchFn: ResponseFetchFn, request: IPushRequest): Promise<IPushResponse> {
  const res = await fetchFn(PUSH_PATH, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  if (!res.ok) throw new PushRequestError(res.status, res.status === 404 ? await readReasonCode(res) : undefined);
  return parsePushResponse(await res.json());
}
