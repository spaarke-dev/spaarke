/**
 * useChatPlaybooks - Playbook discovery hook
 *
 * Fetches available playbooks from GET /api/ai/chat/playbooks.
 * Used to populate the playbook selector UI (quick-action chips)
 * before a chat session is created.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useCallback)
 * @see ChatEndpoints.cs - GET /api/ai/chat/playbooks
 */
import { useState, useEffect, useCallback } from 'react';
/**
 * Hook for discovering available playbooks.
 *
 * @param options - API configuration
 * @returns Playbook list state and refresh function
 *
 * @example
 * ```tsx
 * const { playbooks, isLoading, error, refresh } = useChatPlaybooks({
 *   apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
 *   accessToken: token,
 * });
 * ```
 */
export function useChatPlaybooks(options) {
    const { apiBaseUrl, accessToken, nameFilter } = options;
    const [playbooks, setPlaybooks] = useState([]);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState(null);
    // Normalize URL
    const baseUrl = apiBaseUrl.replace(/\/+$/, '');
    /**
     * Extract tenant ID from JWT for X-Tenant-Id header.
     */
    const extractTenantId = (token) => {
        try {
            const parts = token.split('.');
            if (parts.length !== 3)
                return null;
            const payload = JSON.parse(atob(parts[1]));
            return payload.tid || null;
        }
        catch {
            return null;
        }
    };
    const fetchPlaybooks = useCallback(async () => {
        setIsLoading(true);
        setError(null);
        try {
            const tenantId = extractTenantId(accessToken);
            const params = nameFilter ? `?nameFilter=${encodeURIComponent(nameFilter)}` : '';
            const response = await fetch(`${baseUrl}/api/ai/chat/playbooks${params}`, {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    Authorization: `Bearer ${accessToken}`,
                    ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
                },
            });
            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`Failed to load playbooks (${response.status}): ${errorText}`);
            }
            const data = await response.json();
            const playbookOptions = (data.playbooks || []).map((pb) => ({
                id: pb.id,
                name: pb.name,
                description: pb.description,
                isPublic: pb.isPublic,
            }));
            setPlaybooks(playbookOptions);
        }
        catch (err) {
            const errorObj = err instanceof Error ? err : new Error('Failed to load playbooks');
            setError(errorObj);
        }
        finally {
            setIsLoading(false);
        }
    }, [baseUrl, accessToken, nameFilter]);
    // Fetch on mount and when dependencies change
    useEffect(() => {
        fetchPlaybooks();
    }, [fetchPlaybooks]);
    return {
        playbooks,
        isLoading,
        error,
        refresh: fetchPlaybooks,
    };
}
//# sourceMappingURL=useChatPlaybooks.js.map