/**
 * useQuickSummary — fetches the Quick Summary card metrics from the BFF briefing endpoint.
 *
 * The BFF /api/workspace/briefing endpoint returns rich portfolio metrics that map
 * directly to the IQuickSummary shape needed by QuickSummaryCard and BriefingDialog.
 *
 * Behaviour when `bffBaseUrl` is not provided:
 *   - Returns `{ data: null, isLoading: false, error: null }` so the card shows
 *     its "No summary data available" state without a blank spinner.
 *
 * Behaviour when `mockData` is provided:
 *   - Resolves immediately with mock data (useful for dev / Storybook).
 *
 * Behaviour when `bffBaseUrl` + `accessToken` are provided:
 *   - Fetches from `/api/workspace/briefing` with Authorization header.
 *   - Caches successful responses for CACHE_TTL_MS.
 *   - Exposes a `refetch()` function so consumers can refresh after data-changing
 *     operations (e.g. creating a new matter, completing a to-do).
 *
 * Cross-block refresh integration:
 *   - Consumers can call `refetch()` after to-do create/complete/dismiss to keep
 *     the Quick Summary counts current without a full page reload.
 *
 * Usage:
 *   const { data, isLoading, error, refetch } = useQuickSummary({
 *     bffBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
 *     accessToken: token,
 *   });
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { IQuickSummary } from '../types';

/** BFF endpoint path */
const BRIEFING_ENDPOINT = '/api/workspace/briefing';

/** Cache TTL: 2 minutes (shorter than portfolio health since summary changes after to-do operations) */
const CACHE_TTL_MS = 2 * 60 * 1000;

// ---------------------------------------------------------------------------
// BFF response shape (mirrors BriefingResponse.cs)
// ---------------------------------------------------------------------------

interface IBriefingResponse {
  activeMatters: number;
  totalSpend: number;
  totalBudget: number;
  utilizationPercent: number;
  mattersAtRisk: number;
  overdueEvents: number;
  topPriorityMatter?: {
    matterId: string;
    name: string;
    deadline?: string | null;
    reason: string;
  } | null;
  narrative: string;
  isAiEnhanced: boolean;
  generatedAt: string;
}

// ---------------------------------------------------------------------------
// Currency formatting helper
// ---------------------------------------------------------------------------

function formatCompactCurrency(value: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    notation: 'compact',
    maximumFractionDigits: 0,
  }).format(value);
}

// ---------------------------------------------------------------------------
// Map BFF response → IQuickSummary
// ---------------------------------------------------------------------------

function mapBriefingToQuickSummary(resp: IBriefingResponse): IQuickSummary {
  return {
    activeCount: resp.activeMatters,
    spendFormatted: formatCompactCurrency(resp.totalSpend),
    budgetFormatted: formatCompactCurrency(resp.totalBudget),
    atRiskCount: resp.mattersAtRisk,
    overdueCount: resp.overdueEvents,
    topPriorityMatter: resp.topPriorityMatter?.name,
    briefingText: resp.narrative,
  };
}

// ---------------------------------------------------------------------------
// In-memory cache keyed on URL — prevents redundant fetches within the same
// PCF session (e.g. component unmount/remount). Shared across all hook instances.
// ---------------------------------------------------------------------------

interface ICacheEntry {
  data: IQuickSummary;
  expiresAt: number;
}

const _cache = new Map<string, ICacheEntry>();

function getCached(url: string): IQuickSummary | null {
  const entry = _cache.get(url);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(url);
    return null;
  }
  return entry.data;
}

function setCached(url: string, data: IQuickSummary): void {
  _cache.set(url, { data, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook options and result types
// ---------------------------------------------------------------------------

export interface IUseQuickSummaryOptions {
  /**
   * Base URL of the BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net").
   * When omitted the hook returns null data so the card shows its empty state.
   */
  bffBaseUrl?: string;
  /**
   * Bearer token for authenticating against the BFF.
   * Typically obtained from the MSAL auth provider.
   */
  accessToken?: string;
  /**
   * Optional mock data for local development / Storybook.
   * When provided, bypasses the fetch and resolves immediately.
   */
  mockData?: IQuickSummary;
}

export interface IUseQuickSummaryResult {
  /** Quick summary metrics, or null while loading / when BFF is not configured */
  data: IQuickSummary | null;
  /** True while the BFF fetch is in-flight */
  isLoading: boolean;
  /** User-friendly error string, or null when healthy */
  error: string | null;
  /**
   * Manually trigger a cache-bypassing refresh.
   *
   * Call this after data-changing operations (create/complete/dismiss to-do,
   * create new matter) to keep Quick Summary metrics current.
   */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useQuickSummary(
  options: IUseQuickSummaryOptions = {}
): IUseQuickSummaryResult {
  const { bffBaseUrl, accessToken, mockData } = options;

  const [data, setData] = useState<IQuickSummary | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  // Increment to trigger cache-bypassing refetch
  const [fetchKey, setFetchKey] = useState<number>(0);

  const refetch = useCallback(() => {
    // Invalidate cache so the next effect run hits the BFF
    if (bffBaseUrl) {
      const url = `${bffBaseUrl.replace(/\/$/, '')}${BRIEFING_ENDPOINT}`;
      _cache.delete(url);
    }
    setFetchKey((k) => k + 1);
  }, [bffBaseUrl]);

  useEffect(() => {
    // --- Mock data path ---
    if (mockData) {
      setData(mockData);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- No BFF configured — show empty state (not loading) ---
    if (!bffBaseUrl) {
      setData(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    const url = `${bffBaseUrl.replace(/\/$/, '')}${BRIEFING_ENDPOINT}`;

    // --- Check in-memory cache ---
    const cached = getCached(url);
    if (cached) {
      setData(cached);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- Abort any in-flight request ---
    if (abortRef.current) {
      abortRef.current.abort();
    }
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);

    const headers: HeadersInit = {
      'Content-Type': 'application/json',
    };
    if (accessToken) {
      headers['Authorization'] = `Bearer ${accessToken}`;
    }

    fetch(url, { headers, signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          let message = `Quick Summary unavailable (HTTP ${response.status})`;
          try {
            const problem = await response.json();
            if (problem?.title) message = problem.title;
          } catch {
            // ignore parse errors
          }
          throw new Error(message);
        }
        return response.json() as Promise<IBriefingResponse>;
      })
      .then((fetched) => {
        const mapped = mapBriefingToQuickSummary(fetched);
        setCached(url, mapped);
        setData(mapped);
        setIsLoading(false);
        setError(null);
      })
      .catch((err: Error) => {
        if (err.name === 'AbortError') return; // component unmounted — ignore
        setError(err.message ?? 'Unable to load portfolio summary. Please try again.');
        setIsLoading(false);
      });

    return () => {
      controller.abort();
    };
    // fetchKey is intentionally included so refetch() re-triggers this effect
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bffBaseUrl, accessToken, mockData, fetchKey]);

  return { data, isLoading, error, refetch };
}
