import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';

/**
 * matterTypeLookupService.ts
 *
 * spaarkeai-word-add-in-r1 task 038: the pane's Matter quick-create must always collect a Matter Type
 * (owner decision 2026-09-11) and send it as `matterTypeId`. The type list comes from
 * `GET /api/office/search/matter-types` — a small, load-once reference list (5 rows in dev), NOT the
 * 2-character-minimum typeahead `/api/office/search/entities` uses. See
 * `projects/spaarkeai-word-add-in-r1/notes/038-quick-create-matter-type.md` §11 for the full
 * extend-vs-create analysis, and §8 for the owner's 2026-09-13 decision to keep this route and cache
 * its result rather than hard-code the five dev-environment types client-side.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs (GetMatterTypesAsync)
 * @see src/server/api/Sprk.Bff.Api/Models/Office/MatterTypeListResponse.cs
 */

/** One `sprk_mattertype_ref` row for the Matter Type dropdown. */
export interface MatterTypeChoice {
  id: string;
  name: string;
  code?: string;
}

/** Wire shape of one row in `GET /api/office/search/matter-types` (camelCase). */
interface MatterTypeOptionWire {
  id: string;
  name: string;
  code?: string;
}

/** Wire shape of the endpoint's response body. */
interface MatterTypeListResponseWire {
  results: MatterTypeOptionWire[];
}

// ── Caching (owner decision 2026-09-13) ───────────────────────────────────────────────────────────
//
// Matter-type ids ARE preserved across environments (scripts/Migrate-DataverseData.ps1), so a
// hard-coded client-side list was a real option — the owner chose the live call PLUS a cache instead,
// so a new or customer-added matter type appears without an add-in redeploy. Only a successful,
// NON-EMPTY result is ever cached: a failure or an empty list must never poison the cache, or the
// coordinator's Retry fix (above) would retry into a cache that just re-serves the same absence.

const CACHE_KEY = 'spaarke.officeAddin.matterTypes.v1';
const CACHE_TTL_MS = 24 * 60 * 60 * 1000; // ~24 hours

interface MatterTypesCacheEntry {
  fetchedAt: number;
  items: MatterTypeChoice[];
}

/** Session-lifetime cache — avoids even a `localStorage` round trip for repeat calls in one page load. */
let memoryCache: MatterTypesCacheEntry | null = null;

function isFresh(entry: MatterTypesCacheEntry): boolean {
  return Date.now() - entry.fetchedAt < CACHE_TTL_MS;
}

function isValidChoice(value: unknown): value is MatterTypeChoice {
  const v = value as { id?: unknown; name?: unknown; code?: unknown };
  return (
    typeof v?.id === 'string' && typeof v?.name === 'string' && (v.code === undefined || typeof v.code === 'string')
  );
}

/**
 * Reads the `localStorage` cache entry. Storage can throw inside the Office webview (quota, disabled
 * storage, a privacy mode) — wrapped so a storage fault degrades to "no cache", never a broken field.
 * A malformed stored value (bad JSON, wrong shape, non-array items) is treated the same way.
 */
function readLocalStorageCache(): MatterTypesCacheEntry | null {
  try {
    const raw = window.localStorage.getItem(CACHE_KEY);
    if (!raw) return null;

    const parsed = JSON.parse(raw) as Partial<MatterTypesCacheEntry> | null;
    if (typeof parsed?.fetchedAt !== 'number' || !Array.isArray(parsed.items) || !parsed.items.every(isValidChoice)) {
      return null;
    }
    return { fetchedAt: parsed.fetchedAt, items: parsed.items };
  } catch {
    return null;
  }
}

/** Writes the `localStorage` cache entry. Same try/catch rationale as {@link readLocalStorageCache}. */
function writeLocalStorageCache(entry: MatterTypesCacheEntry): void {
  try {
    window.localStorage.setItem(CACHE_KEY, JSON.stringify(entry));
  } catch {
    // Best-effort only — the in-memory cache (and the next live fetch) still work.
  }
}

function removeLocalStorageCache(): void {
  try {
    window.localStorage.removeItem(CACHE_KEY);
  } catch {
    // Best-effort only.
  }
}

/** A fresh, non-empty cache entry from memory (checked first) or `localStorage`, or `null`. */
function getFreshCachedEntry(): MatterTypesCacheEntry | null {
  if (memoryCache && isFresh(memoryCache) && memoryCache.items.length > 0) {
    return memoryCache;
  }
  const stored = readLocalStorageCache();
  if (stored && isFresh(stored) && stored.items.length > 0) {
    memoryCache = stored; // promote — the next call in this page load skips localStorage entirely.
    return stored;
  }
  return null;
}

function setCachedEntry(items: MatterTypeChoice[]): void {
  // Cache only successful, NON-EMPTY results (owner decision) — an empty list is indistinguishable
  // from "haven't checked yet" and must not block a later, correct fetch.
  if (items.length === 0) return;
  const entry: MatterTypesCacheEntry = { fetchedAt: Date.now(), items };
  memoryCache = entry;
  writeLocalStorageCache(entry);
}

/**
 * Clears the matter-types cache (memory + `localStorage`). Call this when a quick-create response
 * warns that the chosen `matterTypeId` was not found — a renamed or removed type must not linger in
 * the cache; the NEXT open of the pane (or the next `fetchMatterTypes` call after this one) re-fetches.
 */
export function clearMatterTypesCache(): void {
  memoryCache = null;
  removeLocalStorageCache();
}

/**
 * A quick-create response's warnings, as sentences (task 030 shape — no machine codes). Returns
 * whether any of them indicates the chosen matter type was not found, as opposed to a merely
 * transient "could not be checked" warning (which must NOT invalidate the cache — the type is likely
 * still valid).
 */
export function warningsIndicateMatterTypeNotFound(warnings: readonly string[] | undefined): boolean {
  if (!warnings) return false;
  return warnings.some(w => {
    const lower = w.toLowerCase();
    return lower.includes('matter type') && lower.includes('not found');
  });
}

/**
 * Fetches the active Matter Type reference rows for the required dropdown on Matter quick-create,
 * serving a cached result when one is fresh and non-empty (owner decision 2026-09-13 — see the module
 * header). THROWS on a non-2xx response, a network failure, or a malformed body — a failed load and
 * "the table legitimately has zero active rows" are different states the caller (`SaveFlow`) must tell
 * apart: the field is required either way, but only a genuine failure gets a "couldn't load, Retry"
 * affordance (coordinator fix, 2026-09-13). Neither outcome is cached.
 *
 * @param apiBaseUrl BFF base URL.
 * @param token Already-acquired access token for the first attempt.
 * @param getRetryToken Re-acquires a token for the single 401 retry (`authenticatedJsonFetch` contract).
 * @returns The active matter types, ordered by name (server-ordered; not re-sorted here), from the
 *   cache or a live call. May be `[]` when a LIVE call succeeded and the reference table has no active
 *   rows (never from the cache, which never stores an empty result).
 * @throws {Error} on any non-2xx response, network failure, or malformed body from a live call.
 */
export async function fetchMatterTypes(
  apiBaseUrl: string,
  token: string,
  getRetryToken: () => Promise<string>
): Promise<MatterTypeChoice[]> {
  const cached = getFreshCachedEntry();
  if (cached) return cached.items;

  const res = await authenticatedJsonFetch(
    `${apiBaseUrl}/api/office/search/matter-types`,
    { headers: { 'Content-Type': 'application/json' } },
    token,
    { getRetryToken }
  );
  if (!res.ok) {
    throw new Error(`Matter types request failed (${res.status})`);
  }

  const data = (await res.json()) as MatterTypeListResponseWire;
  if (!Array.isArray(data.results)) {
    throw new Error('Matter types response was malformed');
  }

  const items = data.results
    .filter((r): r is MatterTypeOptionWire => typeof r?.id === 'string' && typeof r?.name === 'string')
    .map(r => ({ id: r.id, name: r.name, ...(r.code ? { code: r.code } : {}) }));

  setCachedEntry(items);
  return items;
}
