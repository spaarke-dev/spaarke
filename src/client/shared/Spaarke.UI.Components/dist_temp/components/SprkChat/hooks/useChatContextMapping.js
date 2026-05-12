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
export function useChatContextMapping(options) {
    const { analysisId, playbookId, apiBaseUrl, accessToken } = options;
    const [contextMapping, setContextMapping] = useState(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState(null);
    // Normalise URL — remove trailing slash
    const baseUrl = apiBaseUrl.replace(/\/+$/, '');
    /**
     * Extract tenant ID from JWT for X-Tenant-Id header.
     * Matches the pattern in useChatPlaybooks.ts.
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
    const fetchContextMapping = useCallback(async () => {
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
                throw new Error(`Failed to load analysis chat context (${response.status}): ${errorText}`);
            }
            const data = await response.json();
            setContextMapping(data);
        }
        catch (err) {
            const errorObj = err instanceof Error ? err : new Error('Failed to load analysis chat context');
            setError(errorObj);
            // Clear stale mapping on error to avoid showing outdated chips
            setContextMapping(null);
        }
        finally {
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
//# sourceMappingURL=useChatContextMapping.js.map