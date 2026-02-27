/**
 * useDocumentsTabList — data-fetching hook for the Documents tab.
 *
 * Uses the broad document filter (owner/creator/modifier/workspace/checked-out)
 * via DataverseService.getDocumentsForTab(). Follows the same pattern as
 * useDocumentsList: abort controller, module-level cache with 2-min TTL,
 * fetchKey for manual refetch.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { IDocument } from '../types/entities';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Options and return type
// ---------------------------------------------------------------------------

export interface IUseDocumentsTabListOptions {
  top?: number;
}

export interface IUseDocumentsTabListResult {
  documents: IDocument[];
  isLoading: boolean;
  error: string | null;
  totalCount: number;
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Module-level cache (2-min TTL)
// ---------------------------------------------------------------------------

interface ICacheEntry {
  documents: IDocument[];
  expiresAt: number;
}

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

export function useDocumentsTabList(
  service: DataverseService,
  userId: string,
  options: IUseDocumentsTabListOptions = {}
): IUseDocumentsTabListResult {
  const top = options.top ?? 50;

  const [documents, setDocuments] = useState<IDocument[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState<number>(0);
  const abortRef = useRef<AbortController | null>(null);

  const refetch = useCallback(() => {
    const cacheKey = `documentsTab:${userId}:${top}`;
    _cache.delete(cacheKey);
    setFetchKey((k) => k + 1);
  }, [userId, top]);

  useEffect(() => {
    if (!userId) {
      setIsLoading(true);
      setDocuments([]);
      setError(null);
      return;
    }

    const cacheKey = `documentsTab:${userId}:${top}`;

    const cached = getCached(cacheKey);
    if (cached) {
      setDocuments(cached);
      setIsLoading(false);
      setError(null);
      return;
    }

    if (abortRef.current) {
      abortRef.current.abort();
    }
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);

    let cancelled = false;
    controller.signal.addEventListener('abort', () => {
      cancelled = true;
    });

    service
      .getDocumentsForTab(userId, { top })
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [service, userId, top, fetchKey]);

  return {
    documents,
    isLoading,
    error,
    totalCount: documents.length,
    refetch,
  };
}
