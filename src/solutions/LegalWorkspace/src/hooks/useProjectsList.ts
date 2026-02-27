/**
 * useProjectsList — data-fetching hook for the My Portfolio Projects tab.
 *
 * Wraps DataverseService.getProjectsByUser() with standard loading / error /
 * refetch state management. Follows the same pattern as useMattersList:
 * abort controller, simple in-memory cache keyed on userId:top, fetchKey for
 * manual refetch.
 *
 * Usage:
 *   const { projects, isLoading, error, totalCount, refetch } =
 *     useProjectsList(service, userId, { top: 5 });
 *
 * When userId is not yet known (empty string) the hook remains in the loading
 * state without issuing a request.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { IProject } from '../types/entities';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Options and return type
// ---------------------------------------------------------------------------

export interface IUseProjectsListOptions {
  /**
   * Maximum number of projects to retrieve (default 5 — matches the
   * "Max 5 items with View All Projects footer" requirement).
   */
  top?: number;
  /**
   * Optional contact ID for the broad owner filter (Projects tab).
   * When provided, uses getProjectsByBroadFilter instead of getProjectsByUser.
   *
   * Pass null to use the broad filter without contact-based clauses.
   * Omit entirely to use the simple owner filter (My Portfolio).
   */
  contactId?: string | null;
  /**
   * Optional mock data for local development / Storybook.
   * When provided, bypasses the service call and resolves immediately.
   */
  mockData?: IProject[];
}

export interface IUseProjectsListResult {
  /** Fetched project records (empty array while loading or on error) */
  projects: IProject[];
  /** True while the initial fetch or a refetch is in progress */
  isLoading: boolean;
  /** Human-readable error message, or null when fetch succeeded */
  error: string | null;
  /** Total count returned from last successful fetch */
  totalCount: number;
  /** Manually trigger a fresh fetch (e.g. on tab switch or pull-to-refresh) */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Simple in-memory cache keyed on "userId:top"
// Prevents duplicate requests within the same PCF session on re-mount.
// ---------------------------------------------------------------------------

interface ICacheEntry {
  projects: IProject[];
  expiresAt: number;
}

/** Cache TTL: 2 minutes (aligns with ADR-009 Redis TTL guidance) */
const CACHE_TTL_MS = 2 * 60 * 1000;
const _cache = new Map<string, ICacheEntry>();

function getCached(key: string): IProject[] | null {
  const entry = _cache.get(key);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(key);
    return null;
  }
  return entry.projects;
}

function setCached(key: string, projects: IProject[]): void {
  _cache.set(key, { projects, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetch up to `top` projects owned by `userId` using the provided DataverseService.
 *
 * @param service  - DataverseService instance (from useDataverseService hook)
 * @param userId   - PCF user GUID (context.userSettings.userId). Hook waits when
 *                   empty string is provided (userId not yet resolved).
 * @param options  - Optional configuration overrides
 */
export function useProjectsList(
  service: DataverseService,
  userId: string,
  options: IUseProjectsListOptions = {}
): IUseProjectsListResult {
  const top = options.top ?? 5;
  const { mockData } = options;
  const useBroadFilter = 'contactId' in options;
  const contactId = options.contactId ?? null;

  const [projects, setProjects] = useState<IProject[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  // fetchKey increments on each refetch() call to re-trigger the effect
  const [fetchKey, setFetchKey] = useState<number>(0);
  // AbortController ref to cancel in-flight requests on unmount or re-fetch
  const abortRef = useRef<AbortController | null>(null);

  const refetch = useCallback(() => {
    // Invalidate cache for this user so refetch always hits Xrm.WebApi
    const cacheKey = useBroadFilter ? `projects-broad:${userId}:${top}` : `projects:${userId}:${top}`;
    _cache.delete(cacheKey);
    setFetchKey((k) => k + 1);
  }, [userId, top, useBroadFilter]);

  useEffect(() => {
    // --- Mock data path (dev / Storybook) ---
    if (mockData) {
      setProjects(mockData);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- Wait for userId to be resolved ---
    if (!userId) {
      setIsLoading(true);
      setProjects([]);
      setError(null);
      return;
    }

    const cacheKey = useBroadFilter ? `projects-broad:${userId}:${top}` : `projects:${userId}:${top}`;

    // --- Check in-memory cache ---
    const cached = getCached(cacheKey);
    if (cached) {
      setProjects(cached);
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
      ? service.getProjectsByBroadFilter(userId, contactId, { top })
      : service.getProjectsByUser(userId, { top });

    fetchPromise
      .then((result) => {
        if (cancelled) return;

        if (result.success) {
          setCached(cacheKey, result.data);
          setProjects(result.data);
          setIsLoading(false);
          setError(null);
        } else {
          setError(result.error.message ?? 'Unable to load projects. Please try again.');
          setProjects([]);
          setIsLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message =
          err instanceof Error
            ? err.message
            : 'An unexpected error occurred loading projects.';
        setError(message);
        setProjects([]);
        setIsLoading(false);
      });

    return () => {
      controller.abort();
    };
    // fetchKey is intentionally included so refetch() re-triggers this effect
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [service, userId, top, mockData, fetchKey, useBroadFilter, contactId]);

  return {
    projects,
    isLoading,
    error,
    totalCount: projects.length,
    refetch,
  };
}
