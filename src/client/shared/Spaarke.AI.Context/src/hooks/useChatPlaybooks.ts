/**
 * useChatPlaybooks — Playbook discovery hook
 *
 * Fetches available playbooks from GET /api/ai/chat/playbooks.
 * Used to populate the playbook selector UI before a chat session is created.
 *
 * Standalone — does not depend on SprkChat internals.
 * All API calls go through ChatApiClient → buildBffApiUrl() → authenticatedFetch().
 *
 * @see ADR-012 — Shared Component Library
 * @see ADR-013 — AI Architecture
 * @see ChatEndpoints.cs — GET /api/ai/chat/playbooks
 */

import { useState, useEffect, useCallback } from 'react';
import { ChatApiClient } from '../services/ChatApiClient';
import type { IPlaybookOption, IUseChatPlaybooksResult } from '../types/chat';

// ─────────────────────────────────────────────────────────────────────────────
// Hook options
// ─────────────────────────────────────────────────────────────────────────────

export interface UseChatPlaybooksOptions {
  /**
   * BFF API base URL. Must be obtained via resolveRuntimeConfig() or
   * buildBffApiUrl(). ChatApiClient constructs the full URL internally.
   */
  bffBaseUrl: string;
  /** Optional name filter for playbook search. */
  nameFilter?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook for discovering available playbooks.
 *
 * Standalone — does not depend on SprkChat internals.
 *
 * @example
 * ```tsx
 * const { playbooks, isLoading, error, refresh } = useChatPlaybooks({
 *   bffBaseUrl: config.bffBaseUrl,
 * });
 * ```
 */
export function useChatPlaybooks(options: UseChatPlaybooksOptions): IUseChatPlaybooksResult {
  const { bffBaseUrl, nameFilter } = options;

  const [playbooks, setPlaybooks] = useState<IPlaybookOption[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<Error | null>(null);

  const getClient = useCallback(() => new ChatApiClient(bffBaseUrl), [bffBaseUrl]);

  const fetchPlaybooks = useCallback(async (): Promise<void> => {
    setIsLoading(true);
    setError(null);

    try {
      const client = getClient();
      const playbookOptions = await client.getPlaybooks(nameFilter);
      setPlaybooks(playbookOptions);
    } catch (err: unknown) {
      const errorObj = err instanceof Error ? err : new Error('Failed to load playbooks');
      setError(errorObj);
    } finally {
      setIsLoading(false);
    }
  }, [getClient, nameFilter]);

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
