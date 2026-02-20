/**
 * useEvents — React hook for fetching sprk_event records from Dataverse.
 *
 * Wraps DataverseService.getEventsFeed with loading/error state management,
 * in-memory caching, and a stable refetch trigger.
 *
 * The hook is filter-aware: changing `filter` triggers a fresh fetch and clears
 * cached results only for that cache key, so switching pills is fast when results
 * have been seen before within the same session.
 *
 * Usage:
 *   const { events, isLoading, error, totalCount, refetch } = useEvents({
 *     webApi: context.webAPI,
 *     userId: context.userSettings.userId,
 *     filter: EventFilterCategory.HighPriority,
 *     top: 500,
 *   });
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { DataverseService } from '../services/DataverseService';
import { IEvent } from '../types/entities';
import { EventFilterCategory } from '../types/enums';
import type { IWebApi } from '../types/xrm';

// ---------------------------------------------------------------------------
// In-memory cache — keyed on userId + filter + top
// ---------------------------------------------------------------------------

const CACHE_TTL_MS = 60 * 1000; // 1 minute — short TTL to keep feed fresh

interface ICacheEntry {
  events: IEvent[];
  expiresAt: number;
}

const _cache = new Map<string, ICacheEntry>();

function makeCacheKey(userId: string, filter: EventFilterCategory, top: number): string {
  return `${userId}:${filter}:${top}`;
}

function getCachedEvents(key: string): IEvent[] | null {
  const entry = _cache.get(key);
  if (!entry) return null;
  if (Date.now() > entry.expiresAt) {
    _cache.delete(key);
    return null;
  }
  return entry.events;
}

function setCachedEvents(key: string, events: IEvent[]): void {
  _cache.set(key, { events, expiresAt: Date.now() + CACHE_TTL_MS });
}

// ---------------------------------------------------------------------------
// Hook options and result types
// ---------------------------------------------------------------------------

export interface IUseEventsOptions {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /** Active filter category — changing this triggers a re-fetch */
  filter?: EventFilterCategory;
  /** Maximum records to fetch (default 500 for virtual list support) */
  top?: number;
  /**
   * Optional mock events for local development.
   * When provided, bypasses Xrm.WebApi and resolves immediately.
   */
  mockEvents?: IEvent[];
}

export interface IUseEventsResult {
  /** Fetched and sorted event items for the current filter */
  events: IEvent[];
  /** True while the initial fetch or a filter-change fetch is in progress */
  isLoading: boolean;
  /** User-friendly error message, null when healthy */
  error: string | null;
  /** Total count of events returned (useful for All-filter badge) */
  totalCount: number;
  /** Trigger a fresh fetch, bypassing cache */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Priority level numeric mapping for client-side sort
// ---------------------------------------------------------------------------

/**
 * Derive a numeric priority rank from the IEvent.sprk_priority option-set value.
 *
 * Dataverse choice: 0=Low, 1=Normal, 2=High, 3=Urgent.
 * Higher value = more critical. Used directly as sort rank.
 * Falls back to sprk_priorityscore when sprk_priority is absent.
 */
function getPriorityRank(event: IEvent): number {
  // sprk_priority stores the Dataverse option set integer (0-3).
  // Higher == more critical — usable directly as sort rank.
  if (event.sprk_priority !== undefined && event.sprk_priority !== null) {
    return event.sprk_priority;
  }
  // Fall back: convert priorityscore (0-100) to a 0-3 bucket
  const score = event.sprk_priorityscore ?? 0;
  if (score >= 75) return 3; // Urgent
  if (score >= 50) return 2; // High
  if (score >= 25) return 1; // Normal
  return 0;                  // Low
}

/**
 * Sort events: primary by priority rank descending, secondary by timestamp descending.
 * Returns a NEW array — does not mutate the original.
 */
export function sortEvents(events: IEvent[]): IEvent[] {
  return [...events].sort((a, b) => {
    const rankDiff = getPriorityRank(b) - getPriorityRank(a);
    if (rankDiff !== 0) return rankDiff;
    // Secondary: modifiedon descending (most recent first)
    const aTime = new Date(a.modifiedon).getTime();
    const bTime = new Date(b.modifiedon).getTime();
    return bTime - aTime;
  });
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useEvents(options: IUseEventsOptions): IUseEventsResult {
  const {
    webApi,
    userId,
    filter = EventFilterCategory.All,
    top = 500,
    mockEvents,
  } = options;

  const [events, setEvents] = useState<IEvent[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState<number>(0);

  // Stable service reference — only recreates when webApi changes
  const serviceRef = useRef<DataverseService>(new DataverseService(webApi));
  useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  const refetch = useCallback(() => {
    // Invalidate cache for this key and trigger re-fetch
    const key = makeCacheKey(userId, filter, top);
    _cache.delete(key);
    setFetchKey((k) => k + 1);
  }, [userId, filter, top]);

  useEffect(() => {
    // --- Mock data path ---
    if (mockEvents) {
      setEvents(sortEvents(mockEvents));
      setIsLoading(false);
      setError(null);
      return;
    }

    // --- Guard: userId must be present ---
    if (!userId) {
      setIsLoading(false);
      setError(null);
      setEvents([]);
      return;
    }

    const key = makeCacheKey(userId, filter, top);

    // --- Check in-memory cache ---
    const cached = getCachedEvents(key);
    if (cached) {
      setEvents(cached);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    serviceRef.current
      .getEventsFeed(userId, filter, { top })
      .then((result) => {
        if (cancelled) return;

        if (!result.success) {
          setError(result.error?.message ?? 'Unable to load updates feed. Please try again.');
          setIsLoading(false);
          return;
        }

        const sorted = sortEvents(result.data);
        setCachedEvents(key, sorted);
        setEvents(sorted);
        setIsLoading(false);
        setError(null);
      })
      .catch((err: Error) => {
        if (cancelled) return;
        setError(err.message ?? 'An unexpected error occurred loading the feed.');
        setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId, filter, top, mockEvents, fetchKey]);

  return {
    events,
    isLoading,
    error,
    totalCount: events.length,
    refetch,
  };
}
