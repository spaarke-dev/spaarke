/**
 * BFF child-record writes — the ONE client seam through which every product writer creates or re-files a CHILD record
 * (to-do, event, memo, invoice, report card, analysis, document; re-files of communications too).
 *
 * unified-access-control-r2 task 147 r1 (owner round 28 item 1, "E1 = A1, the existing G5 pattern"). A browser create
 * through `Xrm.WebApi` runs as the user, so Dataverse makes the USER the owner, in the user's own business unit: a child
 * of a secure project would be readable by everyone in that unit. The owner is a SERVER invariant (ADR-002 WP-1/WP-3),
 * so the writers send the SAME Web API payload they built to the BFF instead:
 *
 * - `POST /api/v1/child-records/{table}` — the BFF checks AS THE CALLER that they could create the row (privilege,
 *   AppendTo on every record it binds, no owner or field-secured column), then the application creates it owned by the
 *   team the ownership rule names (the Secure Record Owners team under a secure record) and records the caller as its
 *   creator person.
 * - Re-files — the caller's own update, with the owner re-derived and assigned: `PATCH /api/v1/child-records/{table}/{id}`
 *   for a to-do, memo, invoice, report card or analysis; `PATCH /api/v1/events/{id}/filing` for an event;
 *   `PATCH /api/communications/{id}/filing` for a communication (round 28: re-files go through their families).
 *
 * The client never sets the owner and never shares a child (WP-2). A refusal is surfaced with the server's
 * ProblemDetails message ({@link ChildRecordWriteError}); nothing is left user-owned.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs
 */

import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetch } from './bffDataServiceAdapter';

/** The tables a browser writer creates through the BFF (task 147's census). Lower-case logical names. */
export const BFF_CHILD_CREATE_TABLES: ReadonlySet<string> = new Set([
  'sprk_todo',
  'sprk_event',
  'sprk_memo',
  'sprk_invoice',
  'sprk_reportcard',
  'sprk_analysis',
  'sprk_document',
]);

/** The BFF re-file route for each table a browser writer re-files (one route per table). */
const REFILE_ROUTES: Readonly<Record<string, (id: string) => string>> = {
  sprk_todo: id => `/api/v1/child-records/sprk_todo/${id}`,
  sprk_memo: id => `/api/v1/child-records/sprk_memo/${id}`,
  sprk_invoice: id => `/api/v1/child-records/sprk_invoice/${id}`,
  sprk_reportcard: id => `/api/v1/child-records/sprk_reportcard/${id}`,
  sprk_analysis: id => `/api/v1/child-records/sprk_analysis/${id}`,
  sprk_event: id => `/api/v1/events/${id}/filing`,
  sprk_communication: id => `/api/communications/${id}/filing`,
};

/** True when `table` is created through the BFF. */
export function isBffChildCreateTable(table: string): boolean {
  return BFF_CHILD_CREATE_TABLES.has((table ?? '').toLowerCase());
}

/** True when `table` is re-filed (updated) through the BFF. */
export function isBffChildRefileTable(table: string): boolean {
  return Object.prototype.hasOwnProperty.call(REFILE_ROUTES, (table ?? '').toLowerCase());
}

/**
 * A refusal or failure of a BFF child-record write, carrying the server's message (ProblemDetails `detail`) so the
 * writer's existing error surface shows it verbatim.
 */
export class ChildRecordWriteError extends Error {
  /** The HTTP status the BFF answered with. */
  readonly status: number;
  /** The stable `reasonCode` from the ProblemDetails, when there is one. */
  readonly reasonCode?: string;

  constructor(message: string, status: number, reasonCode?: string) {
    super(message);
    this.name = 'ChildRecordWriteError';
    this.status = status;
    this.reasonCode = reasonCode;
  }
}

function cleanId(id: string): string {
  return (id ?? '').replace(/[{}]/g, '').toLowerCase();
}

function joinUrl(bffBaseUrl: string, path: string): string {
  return `${(bffBaseUrl ?? '').replace(/\/+$/, '')}${path}`;
}

async function failureOf(response: Response, fallback: string): Promise<ChildRecordWriteError> {
  let detail: string | undefined;
  let reasonCode: string | undefined;
  try {
    const text = await response.text();
    if (text) {
      try {
        const problem = JSON.parse(text) as Record<string, unknown>;
        detail =
          (typeof problem['detail'] === 'string' && (problem['detail'] as string)) ||
          (typeof problem['title'] === 'string' && (problem['title'] as string)) ||
          undefined;
        const code = problem['reasonCode'] ?? problem['errorCode'];
        reasonCode = typeof code === 'string' ? code : undefined;
      } catch {
        detail = text;
      }
    }
  } catch {
    // An unreadable body: the status alone.
  }
  return new ChildRecordWriteError(detail ?? `${fallback} (HTTP ${response.status}).`, response.status, reasonCode);
}

/**
 * Creates a child record through the BFF (G5). `payload` is the Dataverse Web API payload the writer would have passed to
 * `Xrm.WebApi.createRecord` (column values and `@odata.bind` lookups). Resolves to the new record's id; rejects with a
 * {@link ChildRecordWriteError} carrying the server's message — nothing was created.
 */
export async function createChildRecordViaBff(
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string,
  table: string,
  payload: Record<string, unknown>
): Promise<string> {
  const entity = (table ?? '').toLowerCase();
  if (!isBffChildCreateTable(entity)) {
    throw new ChildRecordWriteError(`'${table}' records are not created through the BFF child-record route.`, 0);
  }

  const response = await authenticatedFetch(joinUrl(bffBaseUrl, `/api/v1/child-records/${entity}`), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload ?? {}),
  });
  if (!response.ok) {
    throw await failureOf(response, 'The record could not be created');
  }

  const body = (await response.json().catch(() => ({}))) as Record<string, unknown>;
  const id = body['id'];
  if (typeof id !== 'string' || !id) {
    throw new ChildRecordWriteError('The BFF did not return the new record id.', response.status);
  }
  return cleanId(id);
}

/**
 * Updates (re-files) a child record through the BFF: the caller's own update, with the owner re-derived when it changes
 * what the row is filed under. Rejects with a {@link ChildRecordWriteError} carrying the server's message — nothing was
 * written.
 */
export async function updateChildRecordViaBff(
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string,
  table: string,
  id: string,
  payload: Record<string, unknown>
): Promise<void> {
  const route = REFILE_ROUTES[(table ?? '').toLowerCase()];
  if (!route) {
    throw new ChildRecordWriteError(`'${table}' records are not re-filed through the BFF.`, 0);
  }

  const response = await authenticatedFetch(joinUrl(bffBaseUrl, route(cleanId(id))), {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload ?? {}),
  });
  if (!response.ok) {
    throw await failureOf(response, 'The record could not be updated');
  }
}

const BFF_CHILD_WRITES = Symbol.for('spaarke.bffChildWrites');

/** True when `service` already routes its child writes through the BFF (it was returned by {@link withBffChildWrites}). */
export function routesChildWritesThroughBff(service: IDataService | undefined): boolean {
  return !!service && (service as unknown as Record<symbol, unknown>)[BFF_CHILD_WRITES] === true;
}

/**
 * Decorates an {@link IDataService} so its CHILD-record writes go through the BFF: `createRecord` of a
 * {@link BFF_CHILD_CREATE_TABLES} table and `updateRecord` of a re-filed table. Reads, deletes and every other table pass
 * through to `inner` unchanged. The one seam the wizards and side panes route through.
 *
 * - Idempotent: a service this function already returned is returned as it is (so a follow-on writer that wraps the
 *   data service it was handed never loses the connection the host wired).
 * - `bffBaseUrl` may be `''`: hosts whose `authenticatedFetch` resolves relative `/api/...` paths (the Daily Briefing
 *   and SmartTodo convention) pass an empty base.
 * - Fail closed (ADR-003): with no `authenticatedFetch` (or no `bffBaseUrl` at all), a child write is REFUSED with a
 *   {@link ChildRecordWriteError} rather than sent to `inner` (`Xrm.WebApi`) — that would create the child owned by the
 *   user, the exposure this seam exists to close.
 */
export function withBffChildWrites(
  inner: IDataService,
  authenticatedFetch: AuthenticatedFetch | undefined,
  bffBaseUrl: string | undefined
): IDataService {
  if (routesChildWritesThroughBff(inner)) {
    return inner;
  }

  const notConfigured = (table: string): Promise<never> =>
    Promise.reject(
      new ChildRecordWriteError(
        `The ${table} record was not saved: this screen is not connected to the Spaarke service, which must save it.`,
        0,
        'child_record.bff_not_configured'
      )
    );

  const configured = !!authenticatedFetch && typeof bffBaseUrl === 'string';
  const service: IDataService = {
    createRecord: (entityName, data) =>
      !isBffChildCreateTable(entityName)
        ? inner.createRecord(entityName, data)
        : configured
          ? createChildRecordViaBff(authenticatedFetch!, bffBaseUrl!, entityName, data)
          : notConfigured(entityName),
    retrieveRecord: (entityName, id, options) => inner.retrieveRecord(entityName, id, options),
    retrieveMultipleRecords: (entityName, options) => inner.retrieveMultipleRecords(entityName, options),
    updateRecord: (entityName, id, data) =>
      !isBffChildRefileTable(entityName)
        ? inner.updateRecord(entityName, id, data)
        : configured
          ? updateChildRecordViaBff(authenticatedFetch!, bffBaseUrl!, entityName, id, data)
          : notConfigured(entityName),
    deleteRecord: (entityName, id) => inner.deleteRecord(entityName, id),
  };
  Object.defineProperty(service, BFF_CHILD_WRITES, { value: true, enumerable: false });
  return service;
}
