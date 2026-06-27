import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { apiClient, ApiClientError } from '@shared/services';

/**
 * React hook for querying the count of Spaarke `sprk_todo` records linked to a
 * `sprk_communication` (i.e. the current email) via `sprk_regardingcommunication`.
 *
 * Backs the Outlook add-in taskpane banner indicator per FR-28 / A-1
 * (smart-todo-decoupling-r3).
 *
 * Behavior (per FR-28 + NFR-09):
 * - Issues a single BFF call per `communicationId` per add-in session.
 *   Subsequent calls for the same id return cached results synchronously
 *   from a module-level in-memory cache (lost when the taskpane closes).
 * - When `communicationId` is undefined or empty, the hook is inert:
 *   no fetch, `count === 0`, `isLoading === false`.
 * - Surfaces loading + error state for the banner to render.
 *
 * Underlying query (server-side, per spec.md FR-28):
 *   GET sprk_todos?$filter=_sprk_regardingcommunication_value eq {commId}
 *     &$select=sprk_todoid,sprk_name,statecode
 *     &$top=10
 *
 * The hook itself does not talk to Dataverse directly. It calls the BFF endpoint
 * `GET /api/office/communications/{communicationId}/linked-todos` which performs
 * the OData query above and returns `{ count, todos }`. The BFF endpoint is the
 * standard configurable surface (no hardcoded org URLs) — same `apiClient`
 * configured at startup in `outlook/taskpane/index.tsx`.
 *
 * @see projects/smart-todo-decoupling-r3/design.md §7.2
 * @see projects/smart-todo-decoupling-r3/spec.md FR-28 / NFR-09 / A-1
 */

/**
 * Linked `sprk_todo` projection used by the banner (matches the `$select` shape).
 */
export interface LinkedTodo {
  /** sprk_todoid (GUID) */
  sprk_todoid: string;
  /** sprk_name (display name) */
  sprk_name: string;
  /** statecode: 0 = Active, 1 = Inactive */
  statecode: number;
}

/**
 * BFF response shape for the linked-todos lookup.
 */
export interface LinkedTodosResponse {
  /** Total count of matching todos (may be > todos.length if capped by $top=10) */
  count: number;
  /** Todo projections (capped to the first 10 per FR-28) */
  todos: LinkedTodo[];
}

/**
 * Hook return value.
 */
export interface UseLinkedTodosResult {
  /** Number of linked todos (0 if none, undefined communicationId, or before load) */
  count: number;
  /** Linked todo projections (empty array when count is 0 or before load) */
  todos: LinkedTodo[];
  /** True while the first lookup for this communicationId is in flight */
  isLoading: boolean;
  /** Last error message, or null when no error */
  error: string | null;
  /** True when the result for this communicationId came from the in-memory cache */
  fromCache: boolean;
  /** Force-refresh the cache entry for the current communicationId */
  refresh: () => Promise<void>;
}

/**
 * Module-level in-memory cache keyed by `communicationId`. Lives for the
 * lifetime of the add-in taskpane (per FR-28 + spec.md A-1: per-session cache).
 *
 * Exported only for testing — production code must not mutate it.
 */
export const __linkedTodosCache = new Map<string, LinkedTodosResponse>();

/**
 * Test-only helper to reset the module-level cache between tests.
 *
 * @internal
 */
export function __clearLinkedTodosCache(): void {
  __linkedTodosCache.clear();
}

/**
 * Build the BFF endpoint URL for the linked-todos query.
 *
 * The communicationId is encoded to defend against malformed Dataverse ids,
 * even though Dataverse ids are GUIDs.
 */
function buildEndpoint(communicationId: string): string {
  return `/api/office/communications/${encodeURIComponent(communicationId)}/linked-todos`;
}

/**
 * useLinkedTodosForCommunication — hook backing the Outlook add-in indicator banner.
 *
 * @param communicationId The `sprk_communicationid` of the saved email, or
 *                        `undefined` when the email has not (yet) been saved
 *                        to Spaarke. When undefined, the hook is inert.
 * @returns Banner-ready state: count, todos, isLoading, error, fromCache, refresh.
 */
export function useLinkedTodosForCommunication(communicationId: string | undefined): UseLinkedTodosResult {
  // Normalize "empty/whitespace" → undefined so callers can pass through raw
  // strings without per-call guards.
  const normalizedId = useMemo(() => {
    if (!communicationId) return undefined;
    const trimmed = communicationId.trim();
    return trimmed.length > 0 ? trimmed : undefined;
  }, [communicationId]);

  // Seed state from cache synchronously when present — this is the NFR-09
  // "cached after first hit" path. No render flash on re-open.
  const cached = normalizedId ? __linkedTodosCache.get(normalizedId) : undefined;

  const [count, setCount] = useState<number>(cached?.count ?? 0);
  const [todos, setTodos] = useState<LinkedTodo[]>(cached?.todos ?? []);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [fromCache, setFromCache] = useState<boolean>(cached !== undefined);

  // Track the in-flight request so unmount / id-change can ignore stale results.
  const inFlightIdRef = useRef<string | null>(null);

  const fetchLinkedTodos = useCallback(async (idToFetch: string, options: { force?: boolean } = {}): Promise<void> => {
    const { force = false } = options;

    // Cache hit — return synchronously without a network round-trip.
    if (!force) {
      const cachedEntry = __linkedTodosCache.get(idToFetch);
      if (cachedEntry) {
        setCount(cachedEntry.count);
        setTodos(cachedEntry.todos);
        setFromCache(true);
        setIsLoading(false);
        setError(null);
        return;
      }
    }

    inFlightIdRef.current = idToFetch;
    setIsLoading(true);
    setError(null);
    setFromCache(false);

    try {
      const response = await apiClient.get<LinkedTodosResponse>(buildEndpoint(idToFetch));

      // Guard against stale responses if the user switched emails mid-flight.
      if (inFlightIdRef.current !== idToFetch) {
        return;
      }

      // Normalize defensively — server SHOULD always return both fields, but
      // be tolerant of partial payloads.
      const safeResponse: LinkedTodosResponse = {
        count: typeof response?.count === 'number' ? response.count : (response?.todos?.length ?? 0),
        todos: Array.isArray(response?.todos) ? response.todos : [],
      };

      __linkedTodosCache.set(idToFetch, safeResponse);
      setCount(safeResponse.count);
      setTodos(safeResponse.todos);
      setError(null);
    } catch (err) {
      if (inFlightIdRef.current !== idToFetch) {
        return;
      }
      const message =
        err instanceof ApiClientError
          ? err.error.detail || err.error.title || 'Failed to load linked to-dos'
          : err instanceof Error
            ? err.message
            : 'Failed to load linked to-dos';
      setError(message);
      // Do NOT cache failures — next render attempt will retry.
    } finally {
      if (inFlightIdRef.current === idToFetch) {
        setIsLoading(false);
      }
    }
  }, []);

  // Reset banner state immediately when communicationId becomes undefined
  // (user switched to an unsaved email), without leaking the previous count.
  useEffect(() => {
    if (!normalizedId) {
      inFlightIdRef.current = null;
      setCount(0);
      setTodos([]);
      setIsLoading(false);
      setError(null);
      setFromCache(false);
      return;
    }

    // Drive the fetch. Cache hits resolve synchronously inside fetchLinkedTodos.
    void fetchLinkedTodos(normalizedId);
  }, [normalizedId, fetchLinkedTodos]);

  const refresh = useCallback(async (): Promise<void> => {
    if (!normalizedId) return;
    await fetchLinkedTodos(normalizedId, { force: true });
  }, [normalizedId, fetchLinkedTodos]);

  return {
    count,
    todos,
    isLoading,
    error,
    fromCache,
    refresh,
  };
}
