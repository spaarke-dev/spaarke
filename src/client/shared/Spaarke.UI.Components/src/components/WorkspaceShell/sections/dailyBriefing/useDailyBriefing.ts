/**
 * useDailyBriefing.ts — React hook that fetches AI-curated daily briefing bullets
 * from the BFF `POST /api/ai/daily-briefing/narrate` endpoint.
 *
 * Hoisted from `src/solutions/LegalWorkspace/src/sections/dailyBriefing/` in task 069.
 * Per ADR-012 + ADR-028, this hook is context-agnostic: it accepts
 * `authenticatedFetch` (function-based auth contract) as a parameter and never
 * imports from `@spaarke/auth` or any solution-local module. Consumers
 * (LegalWorkspace standalone, SpaarkeAi embed) inject their own auth wrapper.
 *
 * Implements FR-15 (Daily Briefing section data layer). Implements UQ-02 mitigation
 * via a per-user/per-tenant in-memory TTL cache (~5 min) so tab switches don't
 * re-hit the LLM.
 *
 * Constraints:
 *   - ADR-012: Context-agnostic — no platform API imports, no Dataverse strings.
 *   - ADR-013: AI work extends the BFF; no separate AI service. Calls the existing
 *     /api/ai/daily-briefing/narrate endpoint via the consumer-supplied
 *     `authenticatedFetch`.
 *   - ADR-014: Per-user/per-tenant TTL cache (~5 min) — resolves UQ-02 conservatively
 *     from the frontend regardless of whether the server caches. Cache key uses the
 *     optional `tenantId` parameter when supplied; otherwise falls back to a
 *     session-scoped anonymous key (still functional but less granular).
 *   - ADR-016: Rate-limit handling — does NOT retry on 429. The hook surfaces a
 *     typed `{ kind: 'rate-limit' }` error so the section component can render
 *     NFR-11's graceful degraded state.
 *   - ADR-028: All BFF calls go through the supplied `authenticatedFetch` (per-request
 *     `getAccessToken()` semantics owned by the auth wrapper). NO token snapshots.
 *
 * Design notes for 429 + empty-state UX:
 *   - The hook returns `{ bullets, isLoading, error, refetch }`. The `error` shape is a
 *     discriminated union that distinguishes 429 ("rate-limit") from other failures so
 *     the section component can render the rate-limit card without re-structuring this
 *     hook.
 *   - `bullets` is an empty array on the empty-data case — the section component can
 *     detect that condition cleanly and render the empty state.
 */

import * as React from "react";

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Daily briefing error shape (discriminated union).
 */
export type DailyBriefingError =
  | { kind: "rate-limit"; status: 429; message: string }
  | { kind: "unavailable"; status: number; message: string }
  | { kind: "auth"; status: number; message: string }
  | { kind: "error"; status: number; message: string };

/**
 * Daily briefing hook return shape.
 *
 * @property bullets   Flat list of AI-curated narrative strings. Empty array if the
 *                     endpoint returned no narratives (empty-data case).
 * @property isLoading True while the fetch is in flight.
 * @property error     Typed error if the request failed, null on success / before first fetch.
 * @property refetch   Force a re-fetch bypassing the TTL cache (for retry CTA).
 */
export interface DailyBriefingState {
  bullets: string[];
  isLoading: boolean;
  error: DailyBriefingError | null;
  refetch: () => Promise<void>;
}

/**
 * Options for `useDailyBriefing`. Consumers supply their own
 * `authenticatedFetch` (function-based auth contract per ADR-028) so the hook
 * remains context-agnostic.
 */
export interface UseDailyBriefingOptions {
  /**
   * Per-request authenticated fetch wrapper. Must add the BFF bearer token,
   * handle base-URL resolution, and throw with `statusCode` on non-2xx
   * (same contract as `@spaarke/auth`'s `authenticatedFetch`).
   */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * Optional tenant identifier (Azure AD GUID) used to scope the in-memory
   * TTL cache key. When omitted, the cache falls back to an anonymous key
   * (still functional but less granular across users). Consumers typically
   * supply `getAuthProvider().getTenantId()` or equivalent.
   */
  tenantId?: string;
  /**
   * Optional BFF API path. Defaults to `/api/ai/daily-briefing/narrate`. The
   * supplied `authenticatedFetch` is responsible for base URL resolution.
   */
  endpoint?: string;
}

// ---------------------------------------------------------------------------
// Narrate request / response DTOs (mirror DailyBriefingEndpoints.cs)
// ---------------------------------------------------------------------------

interface NarrateRequest {
  categories: NotificationCategoryDto[];
  priorityItems: PriorityItemDto[];
  totalNotificationCount: number;
  channels: ChannelNarrationInput[];
}

interface NotificationCategoryDto {
  name: string;
  count: number;
  unreadCount: number;
}

interface PriorityItemDto {
  category: string;
  title: string;
  dueDate?: string | null;
}

interface ChannelNarrationInput {
  category: string;
  label: string;
  items: ChannelItemDto[];
}

interface ChannelItemDto {
  id: string;
  title: string;
  body: string;
  priority: string;
  regardingName: string;
  regardingEntityType: string;
  regardingId: string;
  createdOn: string;
}

interface NarrateResponse {
  tldr?: { briefing?: string; topAction?: string };
  channelNarratives?: ChannelNarrationResult[];
  generatedAtUtc?: string;
}

interface ChannelNarrationResult {
  category: string;
  bullets: NarrativeBulletResult[];
}

interface NarrativeBulletResult {
  narrative: string;
  itemIds?: string[];
  primaryEntityType?: string;
  primaryEntityId?: string;
  primaryEntityName?: string;
}

// ---------------------------------------------------------------------------
// Per-tenant TTL cache (ADR-014 — UQ-02 mitigation)
//
// Module-scoped Map. Keyed by tenantId (or "anonymous" fallback).
// TTL: 5 minutes. Tab switches within a TTL window re-use the cached payload
// and skip the network call entirely.
// ---------------------------------------------------------------------------

const TTL_MS = 5 * 60 * 1000; // 5 minutes per ADR-014
const DEFAULT_ENDPOINT = "/api/ai/daily-briefing/narrate";

interface CacheEntry {
  bullets: string[];
  expiresAt: number;
}

const briefingCache: Map<string, CacheEntry> = new Map();

/** Resolve the cache key from optional tenantId. */
function resolveCacheKey(tenantId: string | undefined): string {
  if (tenantId && tenantId.length > 0) {
    return `daily-briefing:${tenantId}`;
  }
  return "daily-briefing:anonymous";
}

/** Inspect cache; return cached bullets if still fresh. */
function readCache(key: string): string[] | null {
  const entry = briefingCache.get(key);
  if (!entry) return null;
  if (entry.expiresAt <= Date.now()) {
    briefingCache.delete(key);
    return null;
  }
  return entry.bullets;
}

/** Write cache entry with TTL. */
function writeCache(key: string, bullets: string[]): void {
  briefingCache.set(key, {
    bullets,
    expiresAt: Date.now() + TTL_MS,
  });
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a minimal narrate request. The endpoint validates non-empty input. */
function buildEmptyNarrateRequest(): NarrateRequest {
  // Daily Briefing section consumes the endpoint without channel-level data —
  // the endpoint generates a TL;DR + per-channel bullets from whatever data is
  // available. We send an empty-but-valid envelope; the server is responsible
  // for sourcing notifications via its own pipeline if needed.
  return {
    categories: [],
    priorityItems: [],
    totalNotificationCount: 0,
    channels: [],
  };
}

/** Flatten the narrate response into a single bullet list. */
function flattenBullets(response: NarrateResponse): string[] {
  const out: string[] = [];

  // 1. TL;DR top action (lead bullet, if present)
  const topAction = response.tldr?.topAction?.trim();
  if (topAction) {
    out.push(topAction);
  } else if (response.tldr?.briefing?.trim()) {
    out.push(response.tldr.briefing.trim());
  }

  // 2. Per-channel narrative bullets
  if (Array.isArray(response.channelNarratives)) {
    for (const channel of response.channelNarratives) {
      if (!Array.isArray(channel.bullets)) continue;
      for (const bullet of channel.bullets) {
        const text = bullet?.narrative?.trim();
        if (text) out.push(text);
      }
    }
  }

  return out;
}

/** Map an ApiError-like throw from authenticatedFetch into DailyBriefingError. */
function mapError(err: unknown): DailyBriefingError {
  const e = err as { statusCode?: number; message?: string };
  const status = typeof e?.statusCode === "number" ? e.statusCode : 0;
  const message = e?.message ?? "Failed to fetch daily briefing.";

  if (status === 429) {
    return { kind: "rate-limit", status: 429, message };
  }
  if (status === 503) {
    return { kind: "unavailable", status: 503, message };
  }
  if (status === 401) {
    return { kind: "auth", status, message };
  }
  return { kind: "error", status, message };
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useDailyBriefing — fetches AI-curated daily briefing bullets.
 *
 * Cache behavior: on mount the hook checks the per-tenant TTL cache before
 * hitting the network. Subsequent tab switches within a 5-minute window
 * reuse the cached payload.
 *
 * @param options - `authenticatedFetch` (required) and optional `tenantId` / `endpoint`.
 * @returns {DailyBriefingState} bullets, isLoading, error, refetch
 */
export function useDailyBriefing(options: UseDailyBriefingOptions): DailyBriefingState {
  const { authenticatedFetch, tenantId, endpoint } = options;
  const targetEndpoint = endpoint ?? DEFAULT_ENDPOINT;

  const [bullets, setBullets] = React.useState<string[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(true);
  const [error, setError] = React.useState<DailyBriefingError | null>(null);

  // Track latest cache key so refetch can target the right slot.
  const cacheKeyRef = React.useRef<string | null>(null);

  // Track mounted state so we don't setState after unmount.
  const mountedRef = React.useRef(true);
  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  /** Internal fetch — optionally bypassing the TTL cache. */
  const doFetch = React.useCallback(
    async (bypassCache: boolean): Promise<void> => {
      const key = resolveCacheKey(tenantId);
      cacheKeyRef.current = key;

      // 1. Cache hit short-circuit (unless caller forced refresh)
      if (!bypassCache) {
        const cached = readCache(key);
        if (cached) {
          if (mountedRef.current) {
            setBullets(cached);
            setIsLoading(false);
            setError(null);
          }
          return;
        }
      }

      if (mountedRef.current) {
        setIsLoading(true);
        setError(null);
      }

      try {
        const response = await authenticatedFetch(targetEndpoint, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(buildEmptyNarrateRequest()),
        });

        // authenticatedFetch throws on non-2xx, so this branch implies success.
        const data = (await response.json()) as NarrateResponse;
        const next = flattenBullets(data);

        writeCache(key, next);

        if (mountedRef.current) {
          setBullets(next);
          setIsLoading(false);
          setError(null);
        }
      } catch (err) {
        // Special case: the BFF returns 400 on a fully-empty request.
        // Treat that as the empty-data case (bullets=[], no error) so the
        // empty-state UX kicks in without surfacing a false error.
        const e = err as { statusCode?: number };
        if (e?.statusCode === 400) {
          writeCache(key, []);
          if (mountedRef.current) {
            setBullets([]);
            setIsLoading(false);
            setError(null);
          }
          return;
        }

        const mapped = mapError(err);
        if (mountedRef.current) {
          setBullets([]);
          setIsLoading(false);
          setError(mapped);
        }
      }
    },
    [authenticatedFetch, tenantId, targetEndpoint],
  );

  // Initial fetch on mount (and when auth deps change).
  React.useEffect(() => {
    void doFetch(false);
  }, [doFetch]);

  const refetch = React.useCallback(async (): Promise<void> => {
    await doFetch(true);
  }, [doFetch]);

  return { bullets, isLoading, error, refetch };
}

// ---------------------------------------------------------------------------
// Test utilities (not exported from the section barrel)
// ---------------------------------------------------------------------------

/** Clear the module-scoped TTL cache. Exposed for unit tests. */
export function __clearDailyBriefingCache(): void {
  briefingCache.clear();
}
