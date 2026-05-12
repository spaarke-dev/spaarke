/**
 * useActionMenuData - Fetches and caches action menu data from the BFF API
 *
 * Connects to GET /api/ai/chat/actions to retrieve capability-filtered actions
 * for the SprkChatActionMenu component. Implements session-scoped caching to
 * ensure subsequent menu opens return instantly (< 200ms per FR-10).
 *
 * Cache is invalidated when:
 * - The session ID changes (new session)
 * - The entity type changes (context switch)
 * - The caller explicitly calls refetch() (e.g., playbook switch)
 *
 * @see ADR-012 - Shared Component Library (hook in @spaarke/ui-components)
 * @see ADR-021 - Fluent UI v9 for loading/error states
 * @see ADR-022 - React 16 APIs only (useState, useRef, useCallback)
 * @see ChatEndpoints.cs - GET /api/ai/chat/actions
 * @see spec-FR-10 - Action menu must respond in < 200ms
 */
import type { IChatAction } from '../types';
export interface UseActionMenuDataOptions {
    /** Chat session ID — used for session-scoped caching */
    sessionId: string | undefined;
    /** Entity type of the host record (e.g., "matter", "project") */
    entityType: string | undefined;
    /** Base URL for the BFF API */
    apiBaseUrl: string;
    /** Bearer token for API authentication */
    accessToken: string;
}
export interface IUseActionMenuDataResult {
    /** Available actions for the action menu */
    actions: IChatAction[];
    /** Whether a fetch is currently in progress */
    isLoading: boolean;
    /** User-friendly error message if the last fetch failed, null otherwise */
    error: string | null;
    /**
     * Invalidate the cache and re-fetch actions.
     * Call on playbook switch or context change.
     */
    refetch: () => void;
}
/**
 * Fetches action menu data from GET /api/ai/chat/actions with session-scoped caching.
 *
 * On the first call (or after invalidation), fetches from the API.
 * Subsequent calls with the same session/entity context return cached data
 * instantly, ensuring < 200ms response time (FR-10).
 *
 * @param options - Session context and API configuration
 * @returns Actions, loading state, error state, and refetch function
 *
 * @example
 * ```tsx
 * const { actions, isLoading, error, refetch } = useActionMenuData({
 *   sessionId: session?.sessionId,
 *   entityType: hostContext?.entityType,
 *   apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
 *   accessToken: token,
 * });
 * ```
 */
export declare function useActionMenuData(options: UseActionMenuDataOptions): IUseActionMenuDataResult;
//# sourceMappingURL=useActionMenuData.d.ts.map