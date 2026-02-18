/**
 * useParallelDataLoad — orchestrates parallel initial data fetching for the
 * Legal Operations Workspace.
 *
 * Performance rationale (NFR-01, NFR-07):
 *   - Each workspace block fetches independently in sequence when mounted
 *     naively. This hook fires all independent queries simultaneously via
 *     Promise.all so the critical-path latency is max(query_a, query_b, ...)
 *     rather than sum(query_a + query_b + ...).
 *   - For 500 matters at NFR-07 (< 2 s) and page load at NFR-01 (< 3 s),
 *     parallelisation reduces blocking time to the slowest single query.
 *
 * Independent query groups fired in parallel on mount:
 *   1. Matters list        — useMattersList (Xrm.WebApi: sprk_matter)
 *   2. Events feed         — useEvents      (Xrm.WebApi: sprk_event, top 500)
 *   3. To-do items         — useTodoItems   (Xrm.WebApi: sprk_event where todoflag=true)
 *   4. Notification count  — useNotifications (mock in R1; future: Xrm.WebApi poll)
 *
 * Portfolio Health (usePortfolioHealth) is NOT included here because it hits
 * the BFF HTTP endpoint and has different error/loading semantics — it is
 * left on its own refresh cycle inside PortfolioHealthBlock.
 *
 * Usage (inside WorkspaceGrid or a parent component):
 *
 *   const {
 *     mattersState,
 *     eventsState,
 *     todosState,
 *     notificationsState,
 *     isAnyLoading,
 *   } = useParallelDataLoad({ webApi, userId, service });
 *
 * Each *State object exposes { data, isLoading, error, refetch } so individual
 * blocks can subscribe to their own slice without a shared loading waterfall.
 *
 * Implementation notes:
 *   - useParallelDataLoad itself does NOT fetch — it is a composition hook
 *     that wires up the four individual hooks and exposes a combined
 *     `isAnyLoading` boolean for the Shell to decide when to show the
 *     top-level skeleton overlay.
 *   - Each child hook manages its own cache, abort, and refetch lifecycle,
 *     ensuring independent error recovery.
 *   - The hooks fire on initial mount simultaneously because React processes
 *     each hook's useEffect in the same browser task queue before any I/O
 *     callback fires, yielding effectively parallel dispatch.
 */

import { useMemo } from 'react';
import { DataverseService } from '../services/DataverseService';
import { useMattersList, IUseMattersListResult } from './useMattersList';
import { useEvents, IUseEventsResult } from './useEvents';
import { useTodoItems, IUseTodoItemsResult } from './useTodoItems';
import { useNotifications, IUseNotificationsResult } from './useNotifications';
import { EventFilterCategory } from '../types/enums';
import { IMatter } from '../types/entities';
import { IEvent } from '../types/entities';

// ---------------------------------------------------------------------------
// Hook options
// ---------------------------------------------------------------------------

export interface IUseParallelDataLoadOptions {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: ComponentFramework.WebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /**
   * Stable DataverseService instance. Callers must memoize this to prevent
   * unnecessary re-renders:
   *   const service = useMemo(() => new DataverseService(webApi), [webApi]);
   */
  service: DataverseService;
  /**
   * Maximum number of matters to fetch (default: 5 — matches portfolio widget
   * "Max 5 items with View All Matters footer" requirement).
   */
  mattersTop?: number;
  /**
   * Maximum number of events to fetch for the feed (default: 500 — NFR-07).
   */
  eventsTop?: number;
  /**
   * Optional mock data for local development / testing.
   */
  mockMatters?: IMatter[];
  mockEvents?: IEvent[];
  mockTodos?: IEvent[];
}

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface IUseParallelDataLoadResult {
  /** Matters list state — consumed by MyPortfolioWidget Matters tab */
  mattersState: IUseMattersListResult;
  /** Events feed state — consumed by ActivityFeed block */
  eventsState: IUseEventsResult;
  /** To-do items state — consumed by SmartToDo block */
  todosState: IUseTodoItemsResult;
  /** Notifications state — consumed by PageHeader notification bell */
  notificationsState: IUseNotificationsResult;
  /**
   * True while ANY of the four parallel fetches is still in flight.
   * Use this to show/hide a top-level skeleton overlay.
   * Individual blocks should rely on their own isLoading for granular skeletons.
   */
  isAnyLoading: boolean;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useParallelDataLoad — fires all four independent Xrm.WebApi queries
 * simultaneously on mount so that the combined latency is bounded by the
 * slowest single query rather than their sum.
 *
 * Each query:
 *   - Has its own independent loading, error, and refetch state
 *   - Falls back gracefully on error without blocking sibling blocks
 *   - Uses the existing per-hook in-memory cache (same TTL as before)
 */
export function useParallelDataLoad(
  options: IUseParallelDataLoadOptions
): IUseParallelDataLoadResult {
  const {
    webApi,
    userId,
    service,
    mattersTop = 5,
    eventsTop = 500,
    mockMatters,
    mockEvents,
    mockTodos,
  } = options;

  // ── 1. Matters list ──────────────────────────────────────────────────────
  // Fetches sprk_matter records for the current user (top N).
  const mattersState = useMattersList(service, userId, {
    top: mattersTop,
    mockData: mockMatters,
  });

  // ── 2. Events feed ───────────────────────────────────────────────────────
  // Fetches all 500 sprk_event records (All filter); ActivityFeed does
  // client-side filtering so only one round-trip is needed.
  const eventsState = useEvents({
    webApi,
    userId,
    filter: EventFilterCategory.All,
    top: eventsTop,
    mockEvents,
  });

  // ── 3. To-do items ───────────────────────────────────────────────────────
  // Fetches sprk_event where sprk_todoflag=true AND sprk_todostatus!=Dismissed.
  const todosState = useTodoItems({
    webApi,
    userId,
    mockItems: mockTodos,
  });

  // ── 4. Notifications ─────────────────────────────────────────────────────
  // R1: Mock implementation (static data, no Xrm.WebApi call).
  // Future: replace internals with Xrm.WebApi polling or BFF endpoint.
  const notificationsState = useNotifications();

  // ── Combined loading flag ─────────────────────────────────────────────────
  // Memoized so the boolean reference only changes when the individual
  // isLoading values actually change — prevents unnecessary re-renders in
  // consumers that only care about the combined state.
  const isAnyLoading = useMemo(
    () =>
      mattersState.isLoading ||
      eventsState.isLoading ||
      todosState.isLoading ||
      notificationsState.isLoading,
    [
      mattersState.isLoading,
      eventsState.isLoading,
      todosState.isLoading,
      notificationsState.isLoading,
    ]
  );

  return {
    mattersState,
    eventsState,
    todosState,
    notificationsState,
    isAnyLoading,
  };
}
