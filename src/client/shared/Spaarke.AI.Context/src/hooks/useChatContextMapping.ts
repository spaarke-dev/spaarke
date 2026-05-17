/**
 * useChatContextMapping — Analysis context mapping hook
 *
 * Fetches the analysis-scoped chat context from
 * GET /api/ai/chat/context-mappings/analysis/{analysisId}.
 *
 * Standalone — does not depend on SprkChat internals.
 * All API calls go through ChatApiClient → buildBffApiUrl() → authenticatedFetch().
 *
 * Used to populate QuickActionChips and SlashCommandMenu when an AI surface is
 * opened alongside an analysis output.
 *
 * @see ADR-012 — Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-013 — AI Architecture
 * @see AnalysisChatContextEndpoints.cs — GET /api/ai/chat/context-mappings/analysis/{analysisId}
 */

import { useState, useEffect, useCallback } from 'react';
import { ChatApiClient } from '../services/ChatApiClient';
import type { IAnalysisChatContextResponse, IUseChatContextMappingResult } from '../types/chat';

// ─────────────────────────────────────────────────────────────────────────────
// Hook options
// ─────────────────────────────────────────────────────────────────────────────

export interface UseChatContextMappingOptions {
  /** Analysis record ID (sprk_analysisoutput GUID without braces). */
  analysisId: string | undefined;
  /**
   * Active playbook ID — re-fetch when playbook changes (spec FR-08).
   * The playbook ID is included as a dependency so that switching playbooks
   * re-fetches the context mapping with updated inline actions.
   */
  playbookId: string | undefined;
  /**
   * BFF API base URL. Must be obtained via resolveRuntimeConfig() or
   * buildBffApiUrl(). ChatApiClient constructs the full URL internally.
   */
  bffBaseUrl: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Fetch the analysis-scoped chat context mapping from the BFF API.
 *
 * Only fetches when `analysisId` is a non-empty string. When `analysisId`
 * is undefined or empty, returns `{ contextMapping: null, isLoading: false, error: null }`.
 *
 * Re-fetches automatically when `analysisId` or `playbookId` changes.
 *
 * @example
 * ```tsx
 * const { contextMapping, isLoading } = useChatContextMapping({
 *   analysisId,
 *   playbookId,
 *   bffBaseUrl: config.bffBaseUrl,
 * });
 * ```
 */
export function useChatContextMapping(options: UseChatContextMappingOptions): IUseChatContextMappingResult {
  const { analysisId, playbookId, bffBaseUrl } = options;

  const [contextMapping, setContextMapping] = useState<IAnalysisChatContextResponse | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<Error | null>(null);

  const getClient = useCallback(() => new ChatApiClient(bffBaseUrl), [bffBaseUrl]);

  const fetchContextMapping = useCallback(async (): Promise<void> => {
    // Skip when analysisId is absent — operates in generic mode
    if (!analysisId) {
      setContextMapping(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const client = getClient();
      const data = await client.getAnalysisContextMapping(analysisId);
      setContextMapping(data);
    } catch (err: unknown) {
      const errorObj = err instanceof Error ? err : new Error('Failed to load analysis chat context');
      setError(errorObj);
      // Clear stale mapping on error to avoid showing outdated chips
      setContextMapping(null);
    } finally {
      setIsLoading(false);
    }

    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [getClient, analysisId, playbookId]);

  // Fetch on mount and whenever analysisId or playbookId changes (spec FR-08)
  useEffect(() => {
    fetchContextMapping();
  }, [fetchContextMapping]);

  return {
    contextMapping,
    isLoading,
    error,
    refresh: () => {
      fetchContextMapping();
    },
  };
}
