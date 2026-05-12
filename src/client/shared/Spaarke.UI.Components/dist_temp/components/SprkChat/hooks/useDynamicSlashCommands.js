/**
 * useDynamicSlashCommands - Fetches and merges slash commands at session init
 *
 * Calls GET /api/ai/chat/sessions/{sessionId}/commands to retrieve dynamic
 * slash commands for the current session context (playbook + scope). Merges
 * them with DEFAULT_SLASH_COMMANDS (system commands that are always available).
 *
 * Re-fetches when playbookId or hostContext changes so commands reflect the
 * current analysis context.
 *
 * Each command carries a `source` discriminator ("system" | "playbook" | "scope")
 * so that R2-036 can group them visually in the slash menu.
 *
 * Scope commands (source="scope") are treated independently from playbook
 * commands (spec FR-11): they persist across playbook switches and only change
 * when the host entity context changes. Deduplication merges overlapping
 * scope + playbook commands into a single entry with combined attribution.
 *
 * Client-side caching (R2-038):
 * - Uses an in-memory Map<string, CachedCommands> stored in a React ref
 * - Cache key = `${sessionId}:${playbookId}:${scopeId}` (ADR-014 scoping)
 * - On cache hit, returns immediately without a network call
 * - On context change (playbook/scope), old cache entry is evicted before re-fetch
 * - On component unmount, entire cache is cleared (session-scoped lifetime)
 * - refresh() bypasses cache and re-fetches from BFF
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-013 - Flow ChatHostContext through scope command resolution
 * @see ADR-014 - Cache keys MUST include session-scoping; MUST NOT cache streaming tokens
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useCallback, useRef)
 * @see spec-FR-05 - Dynamic slash command resolution
 * @see spec-FR-11 - Scope capabilities independent of playbook
 * @see spec-FR-17 - Command catalog auto-generated from metadata
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { DEFAULT_SLASH_COMMANDS, } from '../../SlashCommandMenu/slashCommandMenu.types';
/**
 * Build the cache key for the command catalog.
 * Key structure: `${sessionId}:${playbookId}:${scopeId}` per ADR-014 scoping rules.
 * Missing values are represented as empty strings to maintain key structure.
 */
function buildCacheKey(sessionId, playbookId, scopeId) {
    return `${sessionId ?? ''}:${playbookId ?? ''}:${scopeId ?? ''}`;
}
// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Fetch dynamic slash commands from the BFF and merge with system defaults.
 *
 * Only fetches when `sessionId` is a non-empty string. When sessionId is
 * undefined (no session yet), returns DEFAULT_SLASH_COMMANDS immediately.
 *
 * On fetch failure, falls back to DEFAULT_SLASH_COMMANDS only — errors are
 * swallowed to avoid degrading the chat input experience.
 *
 * Client-side caching (R2-038):
 * - Cache is an in-memory Map stored in a React ref (not sessionStorage/localStorage)
 * - Cache hit returns immediately without a network call
 * - Context changes (playbook/scope) evict stale entries before re-fetching
 * - Component unmount clears the entire cache (session-scoped lifetime)
 * - refresh() bypasses the cache for manual invalidation
 *
 * @example
 * ```tsx
 * const { commands, isLoading, refresh } = useDynamicSlashCommands({
 *   sessionId: session?.sessionId,
 *   apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
 *   accessToken: token,
 *   playbookId,
 *   hostContext,
 * });
 * ```
 */
export function useDynamicSlashCommands(options) {
    const { sessionId, apiBaseUrl, accessToken, playbookId, hostContext } = options;
    // Playbook commands change when playbookId changes; scope commands only change
    // when the host entity context changes (FR-11: scope capabilities independent
    // of playbook). Storing them separately prevents playbook switches from
    // clobbering persistent scope commands.
    const [playbookCommands, setPlaybookCommands] = useState([]);
    const [scopeCommands, setScopeCommands] = useState([]);
    const [isLoading, setIsLoading] = useState(false);
    // ─── In-memory command catalog cache (R2-038) ───────────────────────────
    // Map<cacheKey, ICachedCommandResult> stored in a ref so it persists across
    // renders without causing re-renders. Cleared on unmount.
    const cacheRef = useRef(new Map());
    // Track the previous cache key to detect context changes and evict stale entries
    const prevCacheKeyRef = useRef('');
    // Derive scopeId from hostContext for cache key construction (ADR-014)
    const scopeId = hostContext?.entityId;
    // Normalise URL — remove trailing slash
    const baseUrl = apiBaseUrl.replace(/\/+$/, '');
    /**
     * Extract tenant ID from JWT for X-Tenant-Id header.
     * Matches the pattern in useChatContextMapping.ts / useChatPlaybooks.ts.
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
    /**
     * Convert a BFF command response item to a SlashCommand for the UI.
     * Icons are not provided by the BFF — dynamic commands render without an icon
     * (the SlashCommandMenu handles icon-less items gracefully).
     *
     * Category mapping preserves the BFF source discriminator so that
     * SlashCommandMenu grouping (by source) and category semantics stay aligned:
     *   - "system"   → category: 'system'
     *   - "playbook" → category: 'playbook'
     *   - "scope"    → category: 'scope'
     */
    const toSlashCommand = (item) => ({
        id: item.id,
        label: item.label,
        description: item.description,
        trigger: item.trigger,
        category: item.category === 'system' ? 'system' : item.category === 'scope' ? 'scope' : 'playbook',
        source: item.source,
        sourceName: item.sourceName,
    });
    /**
     * Fetch commands from the BFF, with optional cache bypass.
     * When bypassCache is false (default), checks the in-memory cache first
     * and returns cached results without a network call on cache hit.
     */
    const fetchCommands = useCallback(async (bypassCache = false) => {
        // Skip when sessionId is absent — no session means no commands endpoint to call
        if (!sessionId) {
            setPlaybookCommands([]);
            setScopeCommands([]);
            setIsLoading(false);
            return;
        }
        const currentKey = buildCacheKey(sessionId, playbookId, scopeId);
        // ─── Context change detection: evict stale cache entry ──────────────
        if (prevCacheKeyRef.current && prevCacheKeyRef.current !== currentKey) {
            cacheRef.current.delete(prevCacheKeyRef.current);
        }
        prevCacheKeyRef.current = currentKey;
        // ─── Cache hit: return cached commands without network call ──────────
        if (!bypassCache) {
            const cached = cacheRef.current.get(currentKey);
            if (cached) {
                setPlaybookCommands(cached.playbookCommands);
                setScopeCommands(cached.scopeCommands);
                setIsLoading(false);
                return;
            }
        }
        else {
            // bypassCache: clear current entry so we re-fetch
            cacheRef.current.delete(currentKey);
        }
        // ─── Cache miss: fetch from BFF ─────────────────────────────────────
        setIsLoading(true);
        try {
            const tenantId = extractTenantId(accessToken);
            const url = `${baseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/commands`;
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    Authorization: `Bearer ${accessToken}`,
                    ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
                },
            });
            if (!response.ok) {
                // On error, fall back to system commands only — don't break the input
                console.warn(`[useDynamicSlashCommands] Failed to fetch commands (${response.status}). Falling back to system commands.`);
                setPlaybookCommands([]);
                // Preserve existing scope commands on playbook-only failures
                return;
            }
            const data = await response.json();
            // Convert dynamic commands to SlashCommand[], deduplicating against system defaults
            const systemIds = new Set(DEFAULT_SLASH_COMMANDS.map(c => c.id));
            const resolved = data.dynamicCommands
                .map(toSlashCommand)
                .filter(cmd => !systemIds.has(cmd.id));
            // Partition into scope commands and playbook commands.
            // Scope commands (source="scope") are stored separately so they persist
            // across playbook switches (FR-11).
            const nextScopeCommands = [];
            const nextPlaybookCommands = [];
            for (const cmd of resolved) {
                if (cmd.source === 'scope') {
                    nextScopeCommands.push(cmd);
                }
                else {
                    nextPlaybookCommands.push(cmd);
                }
            }
            // ─── Store in cache (R2-038) ────────────────────────────────────
            cacheRef.current.set(currentKey, {
                playbookCommands: nextPlaybookCommands,
                scopeCommands: nextScopeCommands,
            });
            setScopeCommands(nextScopeCommands);
            setPlaybookCommands(nextPlaybookCommands);
        }
        catch (err) {
            // Swallow errors — slash commands are non-critical; fall back to defaults
            console.warn('[useDynamicSlashCommands] Error fetching commands:', err);
            setPlaybookCommands([]);
            // Preserve existing scope commands on transient failures
        }
        finally {
            setIsLoading(false);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [baseUrl, accessToken, sessionId, playbookId, scopeId, hostContext?.entityType]);
    // Clear scope commands when the host entity context changes — scope commands
    // are tied to the entity, not the playbook (ADR-013: flow ChatHostContext).
    // A new entity means the old scope's commands are no longer valid.
    useEffect(() => {
        setScopeCommands([]);
    }, [hostContext?.entityId, hostContext?.entityType]);
    // Fetch on mount and whenever session, playbook, or host context changes
    useEffect(() => {
        fetchCommands();
    }, [fetchCommands]);
    // ─── Cleanup: clear entire cache on unmount (session-scoped lifetime) ───
    useEffect(() => {
        return () => {
            cacheRef.current.clear();
        };
    }, []);
    // ─────────────────────────────────────────────────────────────────────────
    // Deduplication: scope + playbook → single merged list
    //
    // When both a scope and a playbook contribute a command with the same id,
    // keep the playbook version (it may carry playbook-specific prompt config)
    // but annotate with combined attribution so the UI shows both origins.
    // ─────────────────────────────────────────────────────────────────────────
    const dynamicCommands = deduplicateScopeAndPlaybookCommands(scopeCommands, playbookCommands);
    // Merge: system commands (always present, source='system') + deduplicated dynamic commands
    // System commands are sourced from the constant, not the API, to guarantee presence
    const commands = [...DEFAULT_SLASH_COMMANDS, ...dynamicCommands];
    return {
        commands,
        dynamicCommands,
        isLoading,
        refresh: () => {
            fetchCommands(true);
        },
    };
}
// ─────────────────────────────────────────────────────────────────────────────
// Deduplication helper
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Merge scope commands and playbook commands into a single list, deduplicating
 * entries that share the same `id`.
 *
 * Deduplication rules (spec constraint):
 * - When both scope and playbook contribute a command with the same id, keep
 *   the playbook version (it may include playbook-specific configuration).
 * - Annotate the merged entry with combined attribution in `sourceName`
 *   (e.g., "Email Playbook + Legal Research").
 * - Scope-only commands appear with source="scope" and their original sourceName.
 * - Playbook-only commands appear with source="playbook" and their original sourceName.
 *
 * @param scopeCmds  - Commands from the active analysis scope (source="scope")
 * @param playbookCmds - Commands from the active playbook (source="playbook")
 * @returns Merged, deduplicated command list
 */
function deduplicateScopeAndPlaybookCommands(scopeCmds, playbookCmds) {
    // Index scope commands by id for O(1) lookup
    const scopeById = new Map();
    for (const cmd of scopeCmds) {
        scopeById.set(cmd.id, cmd);
    }
    // Track which scope command ids have been consumed by playbook dedup
    const consumedScopeIds = new Set();
    // Process playbook commands: merge with overlapping scope commands
    const merged = playbookCmds.map(pbCmd => {
        const scopeCmd = scopeById.get(pbCmd.id);
        if (!scopeCmd) {
            // Playbook-only command — no scope overlap
            return pbCmd;
        }
        // Duplicate found — prefer playbook version but combine attribution
        consumedScopeIds.add(pbCmd.id);
        const playbookName = pbCmd.sourceName || 'Playbook';
        const scopeName = scopeCmd.sourceName || 'Scope';
        const combinedName = `${playbookName} + ${scopeName}`;
        return {
            ...pbCmd,
            sourceName: combinedName,
        };
    });
    // Append scope-only commands (not consumed by dedup)
    for (const scopeCmd of scopeCmds) {
        if (!consumedScopeIds.has(scopeCmd.id)) {
            merged.push(scopeCmd);
        }
    }
    return merged;
}
//# sourceMappingURL=useDynamicSlashCommands.js.map