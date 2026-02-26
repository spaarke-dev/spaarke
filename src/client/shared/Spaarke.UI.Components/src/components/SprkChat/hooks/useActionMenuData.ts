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

import { useState, useRef, useCallback } from "react";
import type { IChatAction, ChatActionCategory } from "../types";

// ─────────────────────────────────────────────────────────────────────────────
// API Response Types (matches ChatEndpoints.cs models)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Shape of a single action from the API response.
 * The `category` field is a numeric enum value from ActionCategory (C#):
 *   0 = Playbooks, 1 = Actions, 2 = Search, 3 = Settings
 */
interface ApiChatAction {
    id: string;
    label: string;
    description: string;
    icon: string;
    category: number;
    shortcut: string | null;
    requiredCapability: string | null;
}

/**
 * Shape of the GET /api/ai/chat/actions response body.
 * Matches ChatActionsResponse record in C#.
 */
interface ApiChatActionsResponse {
    actions: ApiChatAction[];
    categories: number[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Category Mapping
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Maps C# ActionCategory enum integer values to frontend ChatActionCategory strings.
 * Must stay in sync with ActionCategory enum in ChatAction.cs.
 */
const CATEGORY_MAP: Record<number, ChatActionCategory> = {
    0: "playbooks",
    1: "actions",
    2: "search",
    3: "settings",
};

// ─────────────────────────────────────────────────────────────────────────────
// Cache Key
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Builds a cache key from the session and entity context.
 * Cache is scoped per session+entity combination so that switching entity type
 * within the same session correctly refetches.
 */
function buildCacheKey(sessionId: string | undefined, entityType: string | undefined): string {
    return `${sessionId ?? "none"}|${entityType ?? "none"}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Transformer
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Transforms the API response into the IChatAction[] format expected by
 * SprkChatActionMenu. Maps numeric category values to string literals.
 */
function transformApiActions(apiActions: ApiChatAction[]): IChatAction[] {
    return apiActions.map((a) => ({
        id: a.id,
        label: a.label,
        description: a.description || undefined,
        icon: a.icon || undefined,
        category: CATEGORY_MAP[a.category] ?? "actions",
        shortcut: a.shortcut || undefined,
        disabled: false,
    }));
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Options
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Hook Result
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

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
export function useActionMenuData(options: UseActionMenuDataOptions): IUseActionMenuDataResult {
    const { sessionId, entityType, apiBaseUrl, accessToken } = options;

    const [actions, setActions] = useState<IChatAction[]>([]);
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);

    // Session-scoped cache: maps cache key → IChatAction[]
    const cacheRef = useRef<Map<string, IChatAction[]>>(new Map());

    // Track the last fetched cache key to detect context changes
    const lastCacheKeyRef = useRef<string>("");

    // Normalize URL: strip trailing slashes and trailing /api to prevent double /api/api/ prefix.
    const baseUrl = apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "");

    /**
     * Extract tenant ID from JWT access token for X-Tenant-Id header.
     * Azure AD tokens include 'tid' claim with the tenant GUID.
     */
    const extractTenantId = (token: string): string | null => {
        try {
            const parts = token.split(".");
            if (parts.length !== 3) return null;
            const payload = JSON.parse(atob(parts[1]));
            return payload.tid || null;
        } catch {
            return null;
        }
    };

    /**
     * Fetches actions from the API.
     * If a valid cache entry exists for the current context, returns it immediately.
     */
    const fetchActions = useCallback(
        async (forceRefresh: boolean = false): Promise<void> => {
            const cacheKey = buildCacheKey(sessionId, entityType);

            // Check cache first (unless force refresh)
            if (!forceRefresh && cacheRef.current.has(cacheKey)) {
                const cached = cacheRef.current.get(cacheKey)!;
                setActions(cached);
                setError(null);
                lastCacheKeyRef.current = cacheKey;
                return;
            }

            setIsLoading(true);
            setError(null);

            try {
                // Build query parameters
                const params = new URLSearchParams();
                if (sessionId) {
                    params.set("sessionId", sessionId);
                }
                if (entityType) {
                    params.set("entityType", entityType);
                }
                const queryString = params.toString();
                const url = `${baseUrl}/api/ai/chat/actions${queryString ? `?${queryString}` : ""}`;

                const tenantId = extractTenantId(accessToken);
                const response = await fetch(url, {
                    method: "GET",
                    headers: {
                        "Content-Type": "application/json",
                        Authorization: `Bearer ${accessToken}`,
                        ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
                    },
                });

                if (!response.ok) {
                    // Map specific status codes to user-friendly messages
                    switch (response.status) {
                        case 401:
                            throw new Error("Authentication required. Please sign in again.");
                        case 429:
                            throw new Error("Please try again in a moment.");
                        case 500:
                        case 502:
                        case 503:
                            throw new Error("The service is temporarily unavailable. Please try again.");
                        default: {
                            const errorText = await response.text();
                            throw new Error(
                                `Failed to load actions (${response.status}): ${errorText}`
                            );
                        }
                    }
                }

                const data: ApiChatActionsResponse = await response.json();
                const transformed = transformApiActions(data.actions || []);

                // Store in cache
                cacheRef.current.set(cacheKey, transformed);
                lastCacheKeyRef.current = cacheKey;

                setActions(transformed);
            } catch (err: unknown) {
                const message =
                    err instanceof Error
                        ? err.message
                        : "Failed to load actions. Please try again.";
                setError(message);
                // Keep stale cached data visible if available
                if (cacheRef.current.has(cacheKey)) {
                    setActions(cacheRef.current.get(cacheKey)!);
                }
            } finally {
                setIsLoading(false);
            }
        },
        [sessionId, entityType, baseUrl, accessToken]
    );

    /**
     * Invalidate the cache and re-fetch.
     * Call when the playbook changes or context is updated.
     */
    const refetch = useCallback(() => {
        // Clear entire cache to ensure fresh data on next fetch
        cacheRef.current.clear();
        fetchActions(true);
    }, [fetchActions]);

    // Auto-fetch when session or entity context changes
    // This ensures the first menu open has data ready
    const currentCacheKey = buildCacheKey(sessionId, entityType);
    if (currentCacheKey !== lastCacheKeyRef.current && sessionId) {
        // Trigger fetch if context changed and we have a session
        // Use setTimeout to avoid setState during render
        // The check ensures we only trigger once per context change
        lastCacheKeyRef.current = currentCacheKey;
        // Check if cached
        if (cacheRef.current.has(currentCacheKey)) {
            const cached = cacheRef.current.get(currentCacheKey)!;
            if (actions !== cached) {
                // Will be set on next render cycle
                Promise.resolve().then(() => {
                    setActions(cached);
                    setError(null);
                });
            }
        } else {
            // Fetch asynchronously
            Promise.resolve().then(() => fetchActions(false));
        }
    }

    return {
        actions,
        isLoading,
        error,
        refetch,
    };
}
