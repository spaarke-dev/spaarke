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
import { IPlaybookOption } from '../types';
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
export declare function useChatPlaybooks(options: UseChatPlaybooksOptions): IUseChatPlaybooksResult;
export {};
//# sourceMappingURL=useChatPlaybooks.d.ts.map