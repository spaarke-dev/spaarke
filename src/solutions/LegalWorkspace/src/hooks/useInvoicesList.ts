/**
 * useInvoicesList — data-fetching hook for the Invoices tab.
 *
 * Wraps DataverseService.getInvoicesByBroadFilter() with standard loading /
 * error / refetch state management. Follows the same pattern as useMattersList:
 * abort controller, simple in-memory cache keyed on userId:top, fetchKey for
 * manual refetch.
 *
 * Usage:
 *   const { invoices, isLoading, error, totalCount, refetch } =
 *     useInvoicesList(service, userId, contactId, { top: 50 });
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { IInvoice } from '../types/entities';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Options and return type
// ---------------------------------------------------------------------------

export interface IUseInvoicesListOptions {
  /** Maximum number of invoices to retrieve (default 50). */
  top?: number;
}

export interface IUseInvoicesListResult {
  /** Fetched invoice records (empty array while loading or on error) */
  invoices: IInvoice[];
  /** True while the initial fetch or a refetch is in progress */
  isLoading: boolean;
  /** Human-readable error message, or null when fetch succeeded */
  error: string | null;
  /** Total count returned from last successful fetch */
  totalCount: number;
  /** Manually trigger a fresh fetch */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Simple in-memory cache
// ---------------------------------------------------------------------------

interface ICacheEntry {
  invoices: IInvoice[];
  expiresAt: number;
}

const CACHE_TTL_MS = 2 * 60 * 1000;
const _cache = new Map<string, ICacheEntry>();

function getCached(key: string): IInvoice[] | null {
  const entry = _cache.get(key);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(key);
    return null;
  }
  return entry.invoices;
}

function setCached(key: string, invoices: IInvoice[]): void {
  _cache.set(key, { invoices, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetch invoices using the broad owner filter.
 *
 * @param service   - DataverseService instance
 * @param userId    - PCF user GUID
 * @param contactId - Linked contact GUID (or null). Pass null once resolved
 *                    to enable fetching even without a linked contact.
 * @param options   - Optional configuration overrides
 */
export function useInvoicesList(
  service: DataverseService,
  userId: string,
  contactId: string | null,
  options: IUseInvoicesListOptions = {}
): IUseInvoicesListResult {
  const top = options.top ?? 50;

  const [invoices, setInvoices] = useState<IInvoice[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState<number>(0);
  const abortRef = useRef<AbortController | null>(null);

  const refetch = useCallback(() => {
    const cacheKey = `invoices:${userId}:${top}`;
    _cache.delete(cacheKey);
    setFetchKey((k) => k + 1);
  }, [userId, top]);

  useEffect(() => {
    if (!userId) {
      setIsLoading(true);
      setInvoices([]);
      setError(null);
      return;
    }

    const cacheKey = `invoices:${userId}:${top}`;

    const cached = getCached(cacheKey);
    if (cached) {
      setInvoices(cached);
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
      .getInvoicesByBroadFilter(userId, contactId, { top })
      .then((result) => {
        if (cancelled) return;

        if (result.success) {
          setCached(cacheKey, result.data);
          setInvoices(result.data);
          setIsLoading(false);
          setError(null);
        } else {
          setError(result.error.message ?? 'Unable to load invoices. Please try again.');
          setInvoices([]);
          setIsLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message =
          err instanceof Error
            ? err.message
            : 'An unexpected error occurred loading invoices.';
        setError(message);
        setInvoices([]);
        setIsLoading(false);
      });

    return () => {
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [service, userId, top, contactId, fetchKey]);

  return {
    invoices,
    isLoading,
    error,
    totalCount: invoices.length,
    refetch,
  };
}
