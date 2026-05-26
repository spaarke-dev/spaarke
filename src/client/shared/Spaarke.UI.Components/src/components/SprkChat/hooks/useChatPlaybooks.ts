/**
 * useChatPlaybooks - Playbook discovery hook
 *
 * Fetches available playbooks from GET /api/ai/chat/playbooks.
 * Used to populate the playbook selector UI (quick-action chips)
 * before a chat session is created.
 *
 * Auth v2 (D-AUTH-1, D-AUTH-7):
 * - Accepts `authenticatedFetch` from the caller instead of a snapshotted
 *   `accessToken: string`. The fetch function handles Bearer attachment,
 *   X-Tenant-Id, and 401 retry internally.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useCallback)
 * @see ChatEndpoints.cs - GET /api/ai/chat/playbooks
 */

import { useState, useEffect, useCallback } from 'react';
import { IPlaybookOption, AuthenticatedFetchFn } from '../types';

interface UseChatPlaybooksOptions {
  /** Base URL for the BFF API */
  apiBaseUrl: string;
  /**
   * Authenticated fetch function (typically from `@spaarke/auth` or `useAuth()`).
   * MUST attach a fresh Bearer token on every call. Replaces the previous
   * `accessToken: string` snapshot prop.
   */
  authenticatedFetch: AuthenticatedFetchFn;
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
 *   authenticatedFetch, // from @spaarke/auth / useAuth()
 * });
 * ```
 */
export function useChatPlaybooks(options: UseChatPlaybooksOptions): IUseChatPlaybooksResult {
  const { apiBaseUrl, authenticatedFetch, nameFilter } = options;

  const [playbooks, setPlaybooks] = useState<IPlaybookOption[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<Error | null>(null);

  // Normalize URL
  const baseUrl = apiBaseUrl.replace(/\/+$/, '');

  const fetchPlaybooks = useCallback(async (): Promise<void> => {
    setIsLoading(true);
    setError(null);

    try {
      const params = nameFilter ? `?nameFilter=${encodeURIComponent(nameFilter)}` : '';
      const response = await authenticatedFetch(`${baseUrl}/api/ai/chat/playbooks${params}`, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
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
      const errorObj = err instanceof Error ? err : new Error('Failed to load playbooks');
      setError(errorObj);
    } finally {
      setIsLoading(false);
    }
  }, [baseUrl, authenticatedFetch, nameFilter]);

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
