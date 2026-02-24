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

import { useState, useEffect, useCallback } from "react";
import { IPlaybookOption } from "../types";

interface UseChatPlaybooksOptions {
    /** Base URL for the BFF API */
    apiBaseUrl: string;
    /** Bearer token for API authentication */
    accessToken: string;
    /** Optional name filter for playbook search */
    nameFilter?: string;
}

export interface IUseChatPlaybooksResult {
    /** Available playbooks (user-owned + public, deduplicated) */
    playbooks: IPlaybookOption[];
    /** Whether the fetch is in progress */
    isLoading: boolean;
    /** Error from the last fetch operation */
    error: Error | null;
    /** Manually refresh the playbook list */
    refresh: () => Promise<void>;
}

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
export function useChatPlaybooks(options: UseChatPlaybooksOptions): IUseChatPlaybooksResult {
    const { apiBaseUrl, accessToken, nameFilter } = options;

    const [playbooks, setPlaybooks] = useState<IPlaybookOption[]>([]);
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [error, setError] = useState<Error | null>(null);

    // Normalize URL
    const baseUrl = apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "");

    /**
     * Extract tenant ID from JWT for X-Tenant-Id header.
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

    const fetchPlaybooks = useCallback(async (): Promise<void> => {
        setIsLoading(true);
        setError(null);

        try {
            const tenantId = extractTenantId(accessToken);
            const params = nameFilter ? `?nameFilter=${encodeURIComponent(nameFilter)}` : "";
            const response = await fetch(`${baseUrl}/api/ai/chat/playbooks${params}`, {
                method: "GET",
                headers: {
                    "Content-Type": "application/json",
                    Authorization: `Bearer ${accessToken}`,
                    ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
                },
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`Failed to load playbooks (${response.status}): ${errorText}`);
            }

            const data = await response.json();
            const playbookOptions: IPlaybookOption[] = (data.playbooks || []).map(
                (pb: { id: string; name: string; description?: string; isPublic?: boolean }) => ({
                    id: pb.id,
                    name: pb.name,
                    description: pb.description,
                    isPublic: pb.isPublic,
                })
            );

            setPlaybooks(playbookOptions);
        } catch (err: unknown) {
            const errorObj = err instanceof Error ? err : new Error("Failed to load playbooks");
            setError(errorObj);
        } finally {
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
