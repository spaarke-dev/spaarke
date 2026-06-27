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
 */
import type { IDataService } from '../types/serviceInterfaces';
import type { ILookupItem } from '../types/LookupTypes';

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
