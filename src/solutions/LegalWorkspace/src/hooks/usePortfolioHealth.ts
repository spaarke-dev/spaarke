import { useState, useEffect, useCallback, useRef } from "react";
import { IPortfolioHealth } from "../types/portfolio";

/** BFF endpoint path — absolute path assumes the BFF base URL is resolved at runtime */
const PORTFOLIO_ENDPOINT = "/api/workspace/portfolio";

/** Cache duration in milliseconds (2 minutes — aligns with ADR-009 Redis TTL guidance) */
const CACHE_TTL_MS = 2 * 60 * 1000;

export interface IUsePortfolioHealthOptions {
  /**
   * Base URL of the BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net").
   * When omitted the hook returns mock/loading data — BFF endpoint (task 008) may
   * not yet be deployed.
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
  mockData?: IPortfolioHealth;
}

export interface IUsePortfolioHealthResult {
  data: IPortfolioHealth | null;
  isLoading: boolean;
  error: string | null;
  /** Manually trigger a refresh */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Simple in-memory cache keyed on URL — prevents redundant fetches within
// the same session (e.g. component unmount/remount).
// ---------------------------------------------------------------------------
interface ICacheEntry {
  data: IPortfolioHealth;
  expiresAt: number;
}

const _cache = new Map<string, ICacheEntry>();

function getCached(url: string): IPortfolioHealth | null {
  const entry = _cache.get(url);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(url);
    return null;
  }
  return entry.data;
}

function setCached(url: string, data: IPortfolioHealth): void {
  _cache.set(url, { data, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * usePortfolioHealth — fetches portfolio health summary from the BFF endpoint.
 *
 * Behaviour when `bffBaseUrl` is not provided:
 *   - Returns `{ data: null, isLoading: true, error: null }` indefinitely
 *     so that the UI shows skeleton placeholders while the BFF is unavailable.
 *
 * Behaviour when `mockData` is provided:
 *   - Resolves immediately with mock data (useful for dev / Storybook).
 *
 * Behaviour when `bffBaseUrl` + `accessToken` are provided:
 *   - Fetches from `/api/workspace/portfolio` with Authorization header.
 *   - Caches successful responses for CACHE_TTL_MS.
 *   - Returns `error` string on non-2xx or network failure without crashing the UI.
 */
export function usePortfolioHealth(
  options: IUsePortfolioHealthOptions = {}
): IUsePortfolioHealthResult {
  const { bffBaseUrl, accessToken, mockData } = options;

  const [data, setData] = useState<IPortfolioHealth | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  // Increment to trigger refetch
  const [fetchKey, setFetchKey] = useState<number>(0);

  const refetch = useCallback(() => {
    setFetchKey((k) => k + 1);
  }, []);

  useEffect(() => {
    // --- Mock data path ---
    if (mockData) {
      setData(mockData);
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- No BFF configured — remain in loading state ---
    if (!bffBaseUrl) {
      setIsLoading(true);
      setData(null);
      setError(null);
      return;
    }

    const url = `${bffBaseUrl.replace(/\/$/, "")}${PORTFOLIO_ENDPOINT}`;

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
      "Content-Type": "application/json",
    };
    if (accessToken) {
      headers["Authorization"] = `Bearer ${accessToken}`;
    }

    fetch(url, { headers, signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          // Attempt to read ProblemDetails body for a user-friendly message
          let message = `Portfolio data unavailable (HTTP ${response.status})`;
          try {
            const problem = await response.json();
            if (problem?.title) message = problem.title;
          } catch {
            // ignore parse errors
          }
          throw new Error(message);
        }
        return response.json() as Promise<IPortfolioHealth>;
      })
      .then((fetched) => {
        setCached(url, fetched);
        setData(fetched);
        setIsLoading(false);
        setError(null);
      })
      .catch((err: Error) => {
        if (err.name === "AbortError") return; // component unmounted — ignore
        setError(err.message ?? "Unable to load portfolio data. Please try again.");
        setIsLoading(false);
      });

    return () => {
      controller.abort();
    };
    // fetchKey intentionally included so refetch() re-triggers this effect
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bffBaseUrl, accessToken, mockData, fetchKey]);

  return { data, isLoading, error, refetch };
}
