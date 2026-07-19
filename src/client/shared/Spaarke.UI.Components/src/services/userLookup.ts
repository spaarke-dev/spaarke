/**
 * userLookup.ts
 *
 * Shared people-lookup helpers used by SendEmailStep and other recipient
 * pickers. Searches Dataverse systemuser and contact tables via the
 * abstracted {@link IDataService} (which wraps `Xrm.WebApi.retrieveMultipleRecords`
 * in PCF / Code Pages and a test mock in unit tests).
 *
 * History: `searchUsersAsLookup` was originally inlined inside
 * `components/CreateMatterWizard/matterService.ts`. It is exported here as
 * the canonical shared implementation; the matterService re-export remains
 * for backward compatibility but new code should import from
 * `@spaarke/ui-components/services/userLookup`.
 *
 * All three helpers:
 *   - Return up to 10 active records.
 *   - Format `name` as "Full Name (email)" for disambiguation.
 *   - Short-circuit when the query is empty / < 2 chars (no API call).
 *   - Throw on Web API failure (caller decides how to surface).
 *   - Tag each result's `ILookupItem.entityType` ('systemuser'/'contact') at
 *     the source table (task 060, FR-10) — `RecipientField` surfaces this
 *     onto `IRecipient.entityType` so a directory-resolved recipient carries
 *     its typed identity through to the caller.
 */
import type { IDataService } from '../types/serviceInterfaces';
import type { ILookupItem } from '../types/LookupTypes';
import { getCurrentUserId, getCurrentUserName } from '../utils/xrmContext';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Minimum chars before we issue a Web API query. */
const MIN_QUERY_LENGTH = 2;

/** Maximum records returned by each individual table search. */
const MAX_RESULTS_PER_TABLE = 10;

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/**
 * Escapes single quotes for safe inclusion in an OData filter literal.
 * Dataverse expects `'` inside a string literal to be doubled (`''`).
 */
function escapeODataLiteral(value: string): string {
  return value.replace(/'/g, "''");
}

/** Format a display name for a result row. */
function formatName(fullName: string, email: string | null | undefined): string {
  return email ? `${fullName} (${email})` : fullName;
}

// ---------------------------------------------------------------------------
// searchUsersAsLookup
// ---------------------------------------------------------------------------

/**
 * Search active systemuser records by name fragment.
 *
 * @param dataService - Abstracted Dataverse Web API (PCF/Xrm-backed in production).
 * @param query - Search fragment (case-insensitive `contains` on `fullname`).
 * @returns Up to 10 results; empty array when `query` is too short.
 */
export async function searchUsersAsLookup(dataService: IDataService, query: string): Promise<ILookupItem[]> {
  if (!query || query.trim().length < MIN_QUERY_LENGTH) {
    return [];
  }
  const safe = escapeODataLiteral(query.trim());
  const options =
    `?$select=systemuserid,fullname,internalemailaddress` +
    `&$filter=contains(fullname,'${safe}') and isdisabled eq false` +
    `&$orderby=fullname asc` +
    `&$top=${MAX_RESULTS_PER_TABLE}`;

  const result = await dataService.retrieveMultipleRecords('systemuser', options);
  return result.entities.map(e => ({
    id: e['systemuserid'] as string,
    name: formatName(e['fullname'] as string, e['internalemailaddress'] as string | undefined),
    entityType: 'systemuser' as const,
  }));
}

// ---------------------------------------------------------------------------
// searchContactsAsLookup
// ---------------------------------------------------------------------------

/**
 * Search active contact records by name fragment.
 *
 * @param dataService - Abstracted Dataverse Web API.
 * @param query - Search fragment (case-insensitive `contains` on `fullname`).
 * @returns Up to 10 results; empty array when `query` is too short.
 */
export async function searchContactsAsLookup(dataService: IDataService, query: string): Promise<ILookupItem[]> {
  if (!query || query.trim().length < MIN_QUERY_LENGTH) {
    return [];
  }
  const safe = escapeODataLiteral(query.trim());
  const options =
    `?$select=contactid,fullname,emailaddress1` +
    `&$filter=contains(fullname,'${safe}') and statecode eq 0` +
    `&$orderby=fullname asc` +
    `&$top=${MAX_RESULTS_PER_TABLE}`;

  const result = await dataService.retrieveMultipleRecords('contact', options);
  return result.entities.map(e => ({
    id: e['contactid'] as string,
    name: formatName(e['fullname'] as string, e['emailaddress1'] as string | undefined),
    entityType: 'contact' as const,
  }));
}

// ---------------------------------------------------------------------------
// searchUsersAndContacts
// ---------------------------------------------------------------------------

/**
 * Search systemuser and contact tables in parallel and return a merged list.
 *
 * De-duplication strategy: results are keyed by the email address extracted
 * from the formatted name (the substring inside the trailing parentheses). If
 * an entry has no email, the full name is used as the key. Earlier entries
 * win when duplicates collide; we process systemuser results first so that
 * an internal user shadowing a contact with the same email is preferred.
 *
 * @param dataService - Abstracted Dataverse Web API.
 * @param query - Search fragment.
 * @returns Up to ~20 merged results (capped per-table).
 */
export async function searchUsersAndContacts(dataService: IDataService, query: string): Promise<ILookupItem[]> {
  if (!query || query.trim().length < MIN_QUERY_LENGTH) {
    return [];
  }

  // Run both lookups in parallel; tolerate individual table failures so a
  // single bad-permissions error doesn't blank out the whole picker.
  const [usersResult, contactsResult] = await Promise.allSettled([
    searchUsersAsLookup(dataService, query),
    searchContactsAsLookup(dataService, query),
  ]);

  const users = usersResult.status === 'fulfilled' ? usersResult.value : [];
  const contacts = contactsResult.status === 'fulfilled' ? contactsResult.value : [];

  if (usersResult.status === 'rejected') {
    console.warn('[userLookup] searchUsersAsLookup failed:', usersResult.reason);
  }
  if (contactsResult.status === 'rejected') {
    console.warn('[userLookup] searchContactsAsLookup failed:', contactsResult.reason);
  }

  const seen = new Set<string>();
  const merged: ILookupItem[] = [];
  for (const item of [...users, ...contacts]) {
    const key = extractEmailKey(item.name) ?? item.name.trim().toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    merged.push(item);
  }
  return merged;
}

/**
 * Extract the email address from a name formatted as "Full Name (email)".
 * Returns `null` when no parenthesized segment is present.
 *
 * Exported for unit testing.
 */
export function extractEmailKey(name: string): string | null {
  const match = name.match(/\(([^)]+)\)\s*$/);
  if (!match) return null;
  const candidate = match[1].trim().toLowerCase();
  // Loose validation -- it just needs an "@" to count as an email key.
  return candidate.includes('@') ? candidate : null;
}

// ---------------------------------------------------------------------------
// Current-user "Assign to me" resolution
// ---------------------------------------------------------------------------
//
// spaarkeai-assistant-enhancements-r1 task 014 / FR-A4: "assign it to me" on
// a Create*Wizard sets the assignee to the current user. Per the binding
// project constraint, assignee resolution MUST reuse the current-user
// identity mechanism already available to the client (`getCurrentUserId` /
// `getCurrentUserName` in `utils/xrmContext.ts`) — it must NOT introduce a
// second identity mechanism.
//
// Some assignee-style fields target `systemuser` directly (e.g.
// `sprk_workassignment.sprk_assignedto`) — for those, the Xrm current-user
// identity applies as-is (see `getCurrentUserAsLookupItem`). Others target
// `contact` (e.g. `sprk_event.sprk_assignedto`, `sprk_todo.sprk_assignedto`
// per the 2026-06-21 UAT migration) — for those, the current systemuser must
// be translated to their own contact record. That translation chain
// (systemuser id -> internalemailaddress -> matching contact by email) is
// NOT a new identity mechanism: it is the exact pattern already established
// in `CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.resolveCurrentUserEmail`
// (systemuser lookup) chained onto the existing `contact` search already used
// throughout this file — just composed once, here, for reuse.

/**
 * The current Dataverse user's own identity, straight from the Xrm host
 * context — no data round-trip. Use for `systemuser`-targeted assignee
 * fields (e.g. `sprk_workassignment.sprk_assignedto`).
 *
 * @returns `{ id, name }` or `null` when no Xrm host context is reachable.
 */
export function getCurrentUserAsLookupItem(): ILookupItem | null {
  const id = getCurrentUserId();
  const name = getCurrentUserName();
  if (!id || !name) return null;
  return { id, name };
}

/**
 * Resolve the current Dataverse user onto their own OOB `contact` record.
 * Use for `contact`-targeted assignee fields (e.g. `sprk_event.sprk_assignedto`,
 * `sprk_matter`'s Assigned Attorney/Paralegal).
 *
 * Resolution chain (graceful at every step — returns `null` rather than
 * throwing, so callers can fall back to manual search):
 *   1. Current systemuser id (`getCurrentUserId` — Xrm host context)
 *   2. `systemuser.internalemailaddress`
 *   3. `contact` where `emailaddress1` equals that email (top 1)
 *
 * @param dataService - Abstracted Dataverse Web API.
 * @param options.getCurrentUserId - Test-injection seam overriding the Xrm
 *   probe (mirrors the established `eventService`/`workAssignmentService`
 *   `getCurrentUserId` override convention).
 * @returns The matching contact as an `ILookupItem`, or `null` when the
 *   current user is unresolvable or has no corresponding contact record.
 */
export async function resolveCurrentUserAsContactAssignee(
  dataService: IDataService,
  options?: { getCurrentUserId?: () => string | undefined }
): Promise<ILookupItem | null> {
  const resolveUserId = options?.getCurrentUserId ?? getCurrentUserId;
  const userId = resolveUserId();
  if (!userId) return null;

  try {
    const user = await dataService.retrieveRecord(
      'systemuser',
      userId.replace(/[{}]/g, ''),
      '?$select=internalemailaddress,fullname'
    );
    const email = user['internalemailaddress'] as string | undefined;
    if (!email) return null;

    const safeEmail = escapeODataLiteral(email);
    const result = await dataService.retrieveMultipleRecords(
      'contact',
      `?$select=contactid,fullname&$filter=emailaddress1 eq '${safeEmail}'&$top=1`
    );
    const match = result.entities[0];
    if (!match) return null;

    return { id: match['contactid'] as string, name: match['fullname'] as string };
  } catch (err) {
    console.warn(
      '[userLookup] resolveCurrentUserAsContactAssignee failed (non-fatal):',
      err instanceof Error ? err.message : err
    );
    return null;
  }
}
