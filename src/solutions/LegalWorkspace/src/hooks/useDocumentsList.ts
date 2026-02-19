/**
 * useDocumentsList — data-fetching hook for the My Portfolio Documents tab.
 *
 * Wraps DataverseService.getDocumentsByUser() with standard loading / error /
 * refetch state management. Follows the same pattern as useMattersList and
 * useProjectsList: abort controller, simple in-memory cache keyed on userId:top,
 * fetchKey for manual refetch.
 *
 * Usage:
 *   const { documents, isLoading, error, totalCount, refetch } =
 *     useDocumentsList(service, userId, { top: 5 });
 *
 * When userId is not yet known (empty string) the hook remains in the loading
 * state without issuing a request.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { IDocument } from '../types/entities';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Options and return type
// ---------------------------------------------------------------------------

export interface IUseDocumentsListOptions {
  /**
   * Maximum number of documents to retrieve (default 5 — matches the
   * "Max 5 items with View All Documents footer" requirement).
   */
  top?: number;
  /**
   * Optional mock data for local development / Storybook.
   * When provided, bypasses the service call and resolves immediately.
   */
  mockData?: IDocument[];
}

export interface IUseDocumentsListResult {
  /** Fetched document records (empty array while loading or on error) */
  documents: IDocument[];
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
  documents: IDocument[];
  expiresAt: number;
}

/** Cache TTL: 2 minutes (aligns with ADR-009 Redis TTL guidance) */
const CACHE_TTL_MS = 2 * 60 * 1000;
const _cache = new Map<string, ICacheEntry>();

function getCached(key: string): IDocument[] | null {
  const entry = _cache.get(key);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(key);
    return null;
  }
  return entry.documents;
}

function setCached(key: string, documents: IDocument[]): void {
  _cache.set(key, { documents, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetch up to `top` documents owned by `userId` using the provided DataverseService.
 *
 * @param service  - DataverseService instance (from useDataverseService hook)
 * @param userId   - PCF user GUID (context.userSettings.userId). Hook waits when
 *                   empty string is provided (userId not yet resolved).
 * @param options  - Optional configuration overrides
 */
export function useDocumentsList(
  service: DataverseService,
  userId: string,
  options: IUseDocumentsListOptions = {}
): IUseDocumentsListResult {
  const top = options.top ?? 5;
  const { mockData } = options;

  const [documents, setDocuments] = useState<IDocument[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  // fetchKey increments on each refetch() call to re-trigger the effect
  const [fetchKey, setFetchKey] = useState<number>(0);
  // AbortController ref to cancel in-flight requests on unmount or re-fetch
  const abortRef = useRef<AbortController | null>(null);

  const refetch = useCallback(() => {
    // Invalidate cache for this user so refetch always hits Xrm.WebApi
    const cacheKey = `documents:${userId}:${top}`;
    _cache.delete(cacheKey);
    setFetchKey((k) => k + 1);
  }, [userId, top]);

  useEffect(() => {
    // --- Mock data path (dev / Storybook) ---
    if (mockData) {
      setDocuments(mockData);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- Wait for userId to be resolved ---
    if (!userId) {
      setIsLoading(true);
      setDocuments([]);
      setError(null);
      return;
    }

    const cacheKey = `documents:${userId}:${top}`;

    // --- Check in-memory cache ---
    const cached = getCached(cacheKey);
    if (cached) {
      setDocuments(cached);
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

    service
      .getDocumentsByUser(userId, { top })
      .then((result) => {
        if (cancelled) return;

        if (result.success) {
          setCached(cacheKey, result.data);
          setDocuments(result.data);
          setIsLoading(false);
          setError(null);
        } else {
          setError(result.error.message ?? 'Unable to load documents. Please try again.');
          setDocuments([]);
          setIsLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message =
          err instanceof Error
            ? err.message
            : 'An unexpected error occurred loading documents.';
        setError(message);
        setDocuments([]);
        setIsLoading(false);
      });

    return () => {
      controller.abort();
    };
    // fetchKey is intentionally included so refetch() re-triggers this effect
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [service, userId, top, mockData, fetchKey]);

  return {
    documents,
    isLoading,
    error,
    totalCount: documents.length,
    refetch,
  };
}
