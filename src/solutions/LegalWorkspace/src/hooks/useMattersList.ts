/**
 * useMattersList — data-fetching hook for the My Portfolio Matters tab.
 *
 * Wraps DataverseService.getMattersByUser() with standard loading / error /
 * refetch state management. Follows the same pattern as usePortfolioHealth
 * (abort controller, simple in-memory cache keyed on userId, fetchKey for
 * manual refetch).
 *
 * Usage:
 *   const { matters, isLoading, error, refetch } = useMattersList(service, userId);
 *
 * When userId is not yet known (empty string) the hook remains in the loading
 * state without issuing a request.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { IMatter } from '../types/entities';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Options and return type
// ---------------------------------------------------------------------------

export interface IUseMattersListOptions {
  /**
   * Maximum number of matters to retrieve (default 5 — matches the
   * "Max 5 items with View All Matters footer" requirement).
   */
  top?: number;
  /**
   * Optional contact ID for the broad owner filter (Matters tab).
   * When provided, uses getMattersByBroadFilter instead of getMattersByUser,
   * returning records where the user is owner, modifier, assigned attorney,
   * or assigned paralegal.
   *
   * Pass null to use the broad filter without contact-based clauses.
   * Omit entirely to use the simple owner filter (My Portfolio).
   */
  contactId?: string | null;
  /**
   * Optional mock data for local development / Storybook.
   * When provided, bypasses the service call and resolves immediately.
   */
  mockData?: IMatter[];
}

export interface IUseMattersListResult {
  /** Fetched matter records (empty array while loading or on error) */
  matters: IMatter[];
  /** True while the initial fetch or a refetch is in progress */
  isLoading: boolean;
  /** Human-readable error message, or null when fetch succeeded */
  error: string | null;
  /** Total count returned — may differ from matters.length only if top changes */
  totalCount: number;
  /** Manually trigger a fresh fetch (e.g. on pull-to-refresh) */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Simple in-memory cache keyed on "userId:top" — prevents duplicate requests
// within the same PCF session when the component re-mounts.
// ---------------------------------------------------------------------------

interface ICacheEntry {
  matters: IMatter[];
  expiresAt: number;
}

/** Cache TTL: 2 minutes (aligns with ADR-009 Redis TTL guidance) */
const CACHE_TTL_MS = 2 * 60 * 1000;
const _cache = new Map<string, ICacheEntry>();

function getCached(key: string): IMatter[] | null {
  const entry = _cache.get(key);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(key);
    return null;
  }
  return entry.matters;
}

function setCached(key: string, matters: IMatter[]): void {
  _cache.set(key, { matters, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetch up to `top` matters owned by `userId` using the provided DataverseService.
 *
 * @param service  - DataverseService instance (from useDataverseService hook)
 * @param userId   - PCF user GUID (context.userSettings.userId). Hook waits when
 *                   empty string is provided (userId not yet resolved).
 * @param options  - Optional configuration overrides
 */
export function useMattersList(
  service: DataverseService,
  userId: string,
  options: IUseMattersListOptions = {}
): IUseMattersListResult {
  const top = options.top ?? 5;
  const { mockData } = options;
  // When contactId is explicitly provided (even as null), use the broad filter.
  // When undefined, use the simple owner filter (My Portfolio).
  const useBroadFilter = 'contactId' in options;
  const contactId = options.contactId ?? null;

  const [matters, setMatters] = useState<IMatter[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  // fetchKey increments on each refetch() call to re-trigger the effect
  const [fetchKey, setFetchKey] = useState<number>(0);
  // AbortController ref to cancel in-flight requests on unmount or re-fetch
  const abortRef = useRef<AbortController | null>(null);

  const refetch = useCallback(() => {
    // Invalidate cache for this user so refetch always hits Xrm.WebApi
    const cacheKey = useBroadFilter ? `matters-broad:${userId}:${top}` : `${userId}:${top}`;
    _cache.delete(cacheKey);
    setFetchKey((k) => k + 1);
  }, [userId, top, useBroadFilter]);

  useEffect(() => {
    // --- Mock data path (dev / Storybook) ---
    if (mockData) {
      setMatters(mockData);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- Wait for userId to be resolved ---
    if (!userId) {
      setIsLoading(true);
      setMatters([]);
      setError(null);
      return;
    }

    const cacheKey = useBroadFilter ? `matters-broad:${userId}:${top}` : `${userId}:${top}`;

    // --- Check in-memory cache ---
    const cached = getCached(cacheKey);
    if (cached) {
      setMatters(cached);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- Abort any previous in-flight request ---
    if (abortRef.current) {
      abortRef.current.abort();
    }
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);

    // DataverseService methods do not accept AbortSignal directly (Xrm.WebApi
    // does not expose abort). We use the controller only to handle the case
    // where the component unmounts before the promise resolves.
    let cancelled = false;
    controller.signal.addEventListener('abort', () => {
      cancelled = true;
    });

    const fetchPromise = useBroadFilter
      ? service.getMattersByBroadFilter(userId, contactId, { top })
      : service.getMattersByUser(userId, { top });

    fetchPromise
      .then((result) => {
        if (cancelled) return;

        if (result.success) {
          setCached(cacheKey, result.data);
          setMatters(result.data);
          setIsLoading(false);
          setError(null);
        } else {
          setError(result.error.message ?? 'Unable to load matters. Please try again.');
          setMatters([]);
          setIsLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message =
          err instanceof Error
            ? err.message
            : 'An unexpected error occurred loading matters.';
        setError(message);
        setMatters([]);
        setIsLoading(false);
      });

    return () => {
      controller.abort();
    };
    // fetchKey is intentionally included so refetch() re-triggers this effect
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [service, userId, top, mockData, fetchKey, useBroadFilter, contactId]);

  return {
    matters,
    isLoading,
    error,
    totalCount: matters.length,
    refetch,
  };
}
