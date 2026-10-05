import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';

/**
 * referenceListService.ts
 *
 * The "+ New" create form's dropdown lists, from `GET /api/office/search/{list}`:
 *   - `matter-types`   — required on a Matter (task 038, owner decision 2026-09-11);
 *   - `practice-areas` — required on a Matter (task 100, owner decision B);
 *   - `project-types`  — optional on a Project (task 100).
 * Small, load-once reference lists, NOT the 2-character-minimum typeahead `/api/office/search/entities` uses.
 * Task 100 generalized task 038's matter-type loader (one route, one loader, one cache per list) — see
 * `projects/spaarkeai-word-add-in-r1/notes/100-create-record-fields.md`.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs (GetReferenceListAsync)
 * @see src/server/api/Sprk.Bff.Api/Services/Office/OfficeSearchService.cs (ReferenceLists — the closed table)
 */

/** The lists the server offers. Any other name is a 404 there. */
export type ReferenceListName = 'matter-types' | 'practice-areas' | 'project-types';

/** One reference row for a create-form dropdown. */
export interface ReferenceChoice {
  id: string;
  name: string;
  code?: string;
}

/** Wire shape of one row (camelCase). */
interface ReferenceOptionWire {
  id: string;
  name: string;
  code?: string;
}

/** Wire shape of the endpoint's response body. */
interface ReferenceListResponseWire {
  results: ReferenceOptionWire[];
}

/**
 * What each list is called in a server warning (`RecordCreationService.CheckReferenceAsync`: "The selected {label}
 * was not found, …") — the phrase {@link warningsIndicateReferenceNotFound} looks for.
 */
const WARNING_LABEL: Record<ReferenceListName, string> = {
  'matter-types': 'matter type',
  'practice-areas': 'practice area',
  'project-types': 'project type',
};

// ── Caching (owner decision 2026-09-13, task 038; extended to every list by task 100) ─────────────────────────
//
// The owner chose the live call PLUS a cache over a hard-coded client list, so a new or customer-added reference row
// appears without an add-in redeploy. Only a successful, NON-EMPTY result is ever cached: a failure or an empty list
// must never poison the cache, or a Retry would retry into a cache that just re-serves the same absence. The
// matter-types key is the one task 038 shipped, so a pane already holding a cached list keeps it.

const CACHE_TTL_MS = 24 * 60 * 60 * 1000; // ~24 hours

function cacheKey(list: ReferenceListName): string {
  return list === 'matter-types'
    ? 'spaarke.officeAddin.matterTypes.v1'
    : `spaarke.officeAddin.referenceList.${list}.v1`;
}

interface ReferenceCacheEntry {
  fetchedAt: number;
  items: ReferenceChoice[];
}

/** Session-lifetime cache — avoids even a `localStorage` round trip for repeat calls in one page load. */
const memoryCache = new Map<ReferenceListName, ReferenceCacheEntry>();

function isFresh(entry: ReferenceCacheEntry): boolean {
  return Date.now() - entry.fetchedAt < CACHE_TTL_MS;
}

function isValidChoice(value: unknown): value is ReferenceChoice {
  const v = value as { id?: unknown; name?: unknown; code?: unknown };
  return (
    typeof v?.id === 'string' && typeof v?.name === 'string' && (v.code === undefined || typeof v.code === 'string')
  );
}

/**
 * Reads the `localStorage` cache entry. Storage can throw inside the Office webview (quota, disabled storage, a
 * privacy mode) — wrapped so a storage fault degrades to "no cache", never a broken field. A malformed stored value
 * (bad JSON, wrong shape, non-array items) is treated the same way.
 */
function readLocalStorageCache(list: ReferenceListName): ReferenceCacheEntry | null {
  try {
    const raw = window.localStorage.getItem(cacheKey(list));
    if (!raw) return null;

    const parsed = JSON.parse(raw) as Partial<ReferenceCacheEntry> | null;
    if (typeof parsed?.fetchedAt !== 'number' || !Array.isArray(parsed.items) || !parsed.items.every(isValidChoice)) {
      return null;
    }
    return { fetchedAt: parsed.fetchedAt, items: parsed.items };
  } catch {
    return null;
  }
}

function writeLocalStorageCache(list: ReferenceListName, entry: ReferenceCacheEntry): void {
  try {
    window.localStorage.setItem(cacheKey(list), JSON.stringify(entry));
  } catch {
    // Best-effort only — the in-memory cache (and the next live fetch) still work.
  }
}

function removeLocalStorageCache(list: ReferenceListName): void {
  try {
    window.localStorage.removeItem(cacheKey(list));
  } catch {
    // Best-effort only.
  }
}

/** A fresh, non-empty cache entry from memory (checked first) or `localStorage`, or `null`. */
function getFreshCachedEntry(list: ReferenceListName): ReferenceCacheEntry | null {
  const inMemory = memoryCache.get(list);
  if (inMemory && isFresh(inMemory) && inMemory.items.length > 0) {
    return inMemory;
  }
  const stored = readLocalStorageCache(list);
  if (stored && isFresh(stored) && stored.items.length > 0) {
    memoryCache.set(list, stored); // promote — the next call in this page load skips localStorage entirely.
    return stored;
  }
  return null;
}

function setCachedEntry(list: ReferenceListName, items: ReferenceChoice[]): void {
  // Cache only successful, NON-EMPTY results — an empty list is indistinguishable from "haven't checked yet" and must
  // not block a later, correct fetch.
  if (items.length === 0) return;
  const entry: ReferenceCacheEntry = { fetchedAt: Date.now(), items };
  memoryCache.set(list, entry);
  writeLocalStorageCache(list, entry);
}

/**
 * Clears one list's cache (memory + `localStorage`). Called when a quick-create response warns that the chosen row
 * was not found — a renamed or removed row must not linger; the next fetch re-reads the table.
 */
export function clearReferenceListCache(list: ReferenceListName): void {
  memoryCache.delete(list);
  removeLocalStorageCache(list);
}

/**
 * Whether a quick-create response's warnings (sentences — no machine codes) say the row chosen from `list` was not
 * found, as opposed to a merely transient "could not be checked" (which must NOT invalidate the cache — the row is
 * likely still valid).
 */
export function warningsIndicateReferenceNotFound(
  list: ReferenceListName,
  warnings: readonly string[] | undefined
): boolean {
  if (!warnings) return false;
  const label = WARNING_LABEL[list];
  return warnings.some(w => {
    const lower = w.toLowerCase();
    return lower.includes(label) && lower.includes('not found');
  });
}

/**
 * Fetches one list's active rows, serving a cached result when one is fresh and non-empty. THROWS on a non-2xx
 * response, a network failure, or a malformed body — a failed load and "the table legitimately has zero active rows"
 * are different states the caller must tell apart (a failure gets a "couldn't load, Retry" affordance). Neither is
 * cached.
 *
 * @param list Which list.
 * @param apiBaseUrl BFF base URL.
 * @param token Already-acquired access token for the first attempt.
 * @param getRetryToken Re-acquires a token for the single 401 retry (`authenticatedJsonFetch` contract).
 * @returns The active rows, ordered by name (server-ordered; not re-sorted here). May be `[]` only from a LIVE call.
 * @throws {Error} on any non-2xx response, network failure, or malformed body from a live call.
 */
export async function fetchReferenceList(
  list: ReferenceListName,
  apiBaseUrl: string,
  token: string,
  getRetryToken: () => Promise<string>
): Promise<ReferenceChoice[]> {
  const cached = getFreshCachedEntry(list);
  if (cached) return cached.items;

  const res = await authenticatedJsonFetch(
    `${apiBaseUrl}/api/office/search/${list}`,
    { headers: { 'Content-Type': 'application/json' } },
    token,
    { getRetryToken }
  );
  if (!res.ok) {
    throw new Error(`Reference list ${list} request failed (${res.status})`);
  }

  const data = (await res.json()) as ReferenceListResponseWire;
  if (!Array.isArray(data.results)) {
    throw new Error(`Reference list ${list} response was malformed`);
  }

  const items = data.results
    .filter((r): r is ReferenceOptionWire => typeof r?.id === 'string' && typeof r?.name === 'string')
    .map(r => ({ id: r.id, name: r.name, ...(r.code ? { code: r.code } : {}) }));

  setCachedEntry(list, items);
  return items;
}
