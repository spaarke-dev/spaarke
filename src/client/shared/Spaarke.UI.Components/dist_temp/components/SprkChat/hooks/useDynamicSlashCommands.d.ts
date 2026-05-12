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
import { type SlashCommand } from '../../SlashCommandMenu/slashCommandMenu.types';
import type { IHostContext } from '../types';
export interface UseDynamicSlashCommandsOptions {
    /** Session ID — commands are fetched only after a session is created. */
    sessionId: string | undefined;
    /** Base URL for the BFF API. */
    apiBaseUrl: string;
    /** Bearer token for API authentication. */
    accessToken: string;
    /** Active playbook ID — re-fetch when playbook changes. */
    playbookId?: string;
    /** Host context — re-fetch when entity context changes. */
    hostContext?: IHostContext;
}
export interface IUseDynamicSlashCommandsResult {
    /** Merged command list: DEFAULT_SLASH_COMMANDS + dynamic commands from BFF. */
    commands: SlashCommand[];
    /**
     * Only the dynamic commands from the BFF (excludes system defaults).
     * Pass this to SprkChatInput.dynamicSlashCommands since SprkChatInput
     * already includes DEFAULT_SLASH_COMMANDS internally via useSlashCommands.
     */
    dynamicCommands: SlashCommand[];
    /** Whether the commands fetch is in progress. */
    isLoading: boolean;
    /**
     * Manually re-fetch commands, bypassing the in-memory cache.
     * Clears the current cache entry and fetches fresh data from the BFF.
     */
    refresh: () => void;
}
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
export declare function useDynamicSlashCommands(options: UseDynamicSlashCommandsOptions): IUseDynamicSlashCommandsResult;
//# sourceMappingURL=useDynamicSlashCommands.d.ts.map