/**
 * useChatContextMapping - Analysis context mapping hook
 *
 * Fetches the analysis-scoped chat context from
 * GET /api/ai/chat/context-mappings/analysis/{analysisId}.
 *
 * Used to populate QuickActionChips and SlashCommandMenu when SprkChat is
 * opened alongside an analysis output in the AnalysisWorkspace Code Page.
 *
 * Follows the same auth and fetching pattern as useChatPlaybooks.ts:
 * - Accepts apiBaseUrl + accessToken as parameters
 * - Uses fetch() directly with Authorization: Bearer {token} header
 * - Normalises the base URL to remove trailing slashes or /api suffix
 * - Only fetches when analysisId and playbookId (or analysisId alone) change
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useCallback)
 * @see AnalysisChatContextEndpoints.cs - GET /api/ai/chat/context-mappings/analysis/{analysisId}
 */

import { useState, useEffect, useCallback } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Response types (mirror AnalysisChatContextResponse.cs)
// ─────────────────────────────────────────────────────────────────────────────

/** Inline action descriptor from the context mapping response. */
export interface IInlineActionInfo {
  /** Capability string key (e.g. "search", "selection_revise"). */
  id: string;
  /** Human-readable label for the chip / menu item. */
  label: string;
  /** How SprkChat handles the result: "chat" or "diff". */
  actionType: string;
  /** Optional tooltip / description text. */
  description?: string;
}

/** Lightweight playbook descriptor in the analysis context response. */
export interface IAnalysisPlaybookInfo {
  /** Dataverse GUID string of the sprk_analysisplaybook record. */
  id: string;
  /** Display name shown in the playbook selector. */
  name: string;
  /** Optional description for tooltip / help text. */
  description?: string;
}

/** Knowledge source scoped to the analysis context. */
export interface IAnalysisKnowledgeSourceInfo {
  /** Source category string (e.g. "rag_index", "inline", "reference"). */
  type: string;
  /** Dataverse GUID string of the knowledge source record. */
  id: string;
  /** Optional display label for the knowledge source. */
  label?: string;
}

/** Contextual metadata about the analysis record from Dataverse. */
export interface IAnalysisContextInfo {
  analysisId: string;
  analysisType?: string;
  matterType?: string;
  practiceArea?: string;
  sourceFileId?: string;
  sourceContainerId?: string;
}

/** Command entry from the DynamicCommandResolver catalog (R2-019). */
export interface ICommandEntry {
  /** Unique command identifier (slug). */
  id: string;
  /** Human-readable display label. */
  label: string;
  /** Short description of what the command does. */
  description: string;
  /** Slash command string including leading slash (e.g., "/search"). */
  trigger: string;
  /** Source category: "system", "playbook", or scope-qualified label. */
  category: string;
  /** Identifier of the contributing playbook or scope, null for system commands. */
  source: string | null;
}

/** Metadata about the active analysis scope (R2-021). */
export interface IAnalysisScopeMetadata {
  /** Dataverse GUID string of the active scope record. */
  scopeId: string;
  /** Display name of the scope. */
  scopeName: string;
  /** Optional description of the scope. */
  description?: string;
  /** Optional focus area for the scope. */
  focusArea?: string;
}

/** Full analysis chat context response from the BFF API. */
export interface IAnalysisChatContextResponse {
  /** Dataverse GUID string of the default playbook, or empty when unresolved. */
  defaultPlaybookId: string;
  /** Display name of the default playbook. */
  defaultPlaybookName: string;
  /** All playbooks available for this analysis context. */
  availablePlaybooks: IAnalysisPlaybookInfo[];
  /** Inline AI actions derived from the playbook's capabilities. */
  inlineActions: IInlineActionInfo[];
  /** Knowledge sources scoped to this analysis context. */
  knowledgeSources: IAnalysisKnowledgeSourceInfo[];
  /** Contextual metadata about the analysis record. */
  analysisContext: IAnalysisContextInfo;
  /**
   * Dynamic command catalog from DynamicCommandResolver (R2-019/R2-020).
   * Contains system, playbook, and scope-contributed slash commands.
   * Null/undefined when command resolution is unavailable.
   * Note: Commands are also available via the dedicated /sessions/{id}/commands endpoint.
   */
  commands?: ICommandEntry[];
  /**
   * Scope-level search guidance from sprk_searchGuidance on the active scope(s).
   * Used by WebSearchTools for scope-guided web search (FR-10).
   */
  searchGuidance?: string;
  /**
   * Lightweight metadata about the active scope(s): name, description, and focus area.
   * Null when no scopes are active for this analysis context.
   */
  scopeMetadata?: IAnalysisScopeMetadata;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook options and return type
// ─────────────────────────────────────────────────────────────────────────────

interface UseChatContextMappingOptions {
  /** Analysis record ID (sprk_analysisoutput GUID without braces). */
  analysisId: string | undefined;
  /** Active playbook ID — re-fetch when playbook changes. */
  playbookId: string | undefined;
  /** Base URL for the BFF API. */
  apiBaseUrl: string;
  /** Bearer token for API authentication. */
  accessToken: string;
}

export interface IUseChatContextMappingResult {
  /** The resolved analysis chat context, or null when not yet fetched / analysisId absent. */
  contextMapping: IAnalysisChatContextResponse | null;
  /** Whether the fetch is in progress. */
  isLoading: boolean;
  /** Error from the last fetch operation, or null. */
  error: Error | null;
  /** Manually re-fetch the context mapping (e.g. after playbook change). */
  refresh: () => void;
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
 * Re-fetches automatically when `analysisId` or `playbookId` changes, ensuring
 * QuickActionChips update when the user switches playbooks (spec FR-08).
 *
 * Auth follows useChatPlaybooks.ts pattern:
 * - Sends `Authorization: Bearer {accessToken}` header
 * - Extracts `X-Tenant-Id` from the JWT tid claim when present
 *
 * @example
 * ```tsx
 * const { contextMapping, isLoading } = useChatContextMapping({
 *   analysisId,
 *   playbookId,
 *   apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
 *   accessToken: token,
 * });
 * ```
 */
export function useChatContextMapping(
  options: UseChatContextMappingOptions
): IUseChatContextMappingResult {
  const { analysisId, playbookId, apiBaseUrl, accessToken } = options;

  const [contextMapping, setContextMapping] = useState<IAnalysisChatContextResponse | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<Error | null>(null);

  // Normalise URL — remove trailing slash or stray /api suffix (same as useChatPlaybooks.ts)
  const baseUrl = apiBaseUrl.replace(/\/+$/, '').replace(/\/api\/?$/, '');

  /**
   * Extract tenant ID from JWT for X-Tenant-Id header.
   * Matches the pattern in useChatPlaybooks.ts.
   */
  const extractTenantId = (token: string): string | null => {
    try {
      const parts = token.split('.');
      if (parts.length !== 3) return null;
      const payload = JSON.parse(atob(parts[1]));
      return payload.tid || null;
    } catch {
      return null;
    }
  };

  const fetchContextMapping = useCallback(async (): Promise<void> => {
    // Skip when analysisId is absent — SprkChat operates in generic mode
    if (!analysisId) {
      setContextMapping(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const tenantId = extractTenantId(accessToken);
      const url = `${baseUrl}/api/ai/chat/context-mappings/analysis/${encodeURIComponent(analysisId)}`;

      const response = await fetch(url, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${accessToken}`,
          ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
        },
      });

      if (response.status === 404) {
        // Analysis record not found — clear mapping silently (not an error for the UI)
        setContextMapping(null);
        return;
      }

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(
          `Failed to load analysis chat context (${response.status}): ${errorText}`
        );
      }

      const data: IAnalysisChatContextResponse = await response.json();
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
  }, [baseUrl, accessToken, analysisId, playbookId]);

  // Fetch on mount and whenever analysisId or playbookId changes (spec FR-08)
  useEffect(() => {
    fetchContextMapping();
  }, [fetchContextMapping]);

  return {
    contextMapping,
    isLoading,
    error,
    refresh: () => { fetchContextMapping(); },
  };
}
