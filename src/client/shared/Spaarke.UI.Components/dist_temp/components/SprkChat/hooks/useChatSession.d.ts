/**
 * useChatSession - Session lifecycle management hook
 *
 * Manages chat session creation, history loading, context switching, and deletion.
 * All API calls target the ChatEndpoints.cs API contract.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see ChatEndpoints.cs - POST /sessions, GET /history, PATCH /context, DELETE /sessions
 */
import { IChatMessage, IUseChatSessionResult } from '../types';
interface UseChatSessionOptions {
    /** Base URL for the BFF API */
    apiBaseUrl: string;
    /** Bearer token for API authentication */
    accessToken: string;
    /** Pre-loaded messages to show before any session is created (e.g. from sprk_chathistory) */
    initialMessages?: IChatMessage[];
}
/**
 * Hook for managing chat session lifecycle.
 *
 * @param options - API configuration
 * @returns Session state and management functions
 *
 * @example
 * ```tsx
 * const {
 *   session, messages, isLoading, error,
 *   createSession, loadHistory, switchContext, deleteSession,
 *   addMessage, updateLastMessage
 * } = useChatSession({ apiBaseUrl: "https://api.example.com", accessToken: "token" });
 * ```
 */
export declare function useChatSession(options: UseChatSessionOptions): IUseChatSessionResult;
export {};
//# sourceMappingURL=useChatSession.d.ts.map