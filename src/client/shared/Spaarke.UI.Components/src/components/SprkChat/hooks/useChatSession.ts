/**
 * useChatSession - Session lifecycle management hook
 *
 * Manages chat session creation, history loading, context switching, and deletion.
 * All API calls target the ChatEndpoints.cs API contract.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see ChatEndpoints.cs - POST /sessions, GET /history, PATCH /context, DELETE /sessions
 */

import { useState, useCallback } from "react";
import {
    IChatSession,
    IChatMessage,
    IUseChatSessionResult,
    IHostContext,
} from "../types";

interface UseChatSessionOptions {
    /** Base URL for the BFF API */
    apiBaseUrl: string;
    /** Bearer token for API authentication */
    accessToken: string;
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
export function useChatSession(options: UseChatSessionOptions): IUseChatSessionResult {
    const { apiBaseUrl, accessToken } = options;

    const [session, setSession] = useState<IChatSession | null>(null);
    const [messages, setMessages] = useState<IChatMessage[]>([]);
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [error, setError] = useState<Error | null>(null);

    // Normalize: strip trailing slashes AND trailing /api to prevent double /api/api/ prefix.
    // The AnalysisWorkspace PCF stores apiBaseUrl as "https://host/api" but all route
    // constants below already include the /api prefix.
    const baseUrl = apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "");

    /**
     * Make an authenticated API request.
     */
    /**
     * Extract tenant ID from JWT access token for X-Tenant-Id header.
     * Azure AD tokens include 'tid' claim with the tenant GUID.
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

    const apiRequest = useCallback(
        async (url: string, init?: RequestInit): Promise<Response> => {
            const tenantId = extractTenantId(accessToken);
            const response = await fetch(url, {
                ...init,
                headers: {
                    "Content-Type": "application/json",
                    Authorization: `Bearer ${accessToken}`,
                    ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
                    ...(init?.headers || {}),
                },
            });
            return response;
        },
        [accessToken]
    );

    /**
     * Create a new chat session.
     * POST /api/ai/chat/sessions
     */
    const createSession = useCallback(
        async (documentId?: string, playbookId?: string, hostContext?: IHostContext): Promise<IChatSession | null> => {
            setIsLoading(true);
            setError(null);

            try {
                const response = await apiRequest(
                    `${baseUrl}/api/ai/chat/sessions`,
                    {
                        method: "POST",
                        body: JSON.stringify({
                            documentId: documentId || null,
                            playbookId: playbookId || null,
                            hostContext: hostContext || null,
                        }),
                    }
                );

                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(
                        `Failed to create session (${response.status}): ${errorText}`
                    );
                }

                const data = await response.json();
                const newSession: IChatSession = {
                    sessionId: data.sessionId,
                    createdAt: data.createdAt,
                };

                setSession(newSession);
                setMessages([]);
                return newSession;
            } catch (err: unknown) {
                const errorObj =
                    err instanceof Error ? err : new Error("Failed to create session");
                setError(errorObj);
                return null;
            } finally {
                setIsLoading(false);
            }
        },
        [apiRequest, baseUrl]
    );

    /**
     * Load message history for the current session.
     * GET /api/ai/chat/sessions/{sessionId}/history
     */
    const loadHistory = useCallback(async (): Promise<void> => {
        if (!session) {
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            const response = await apiRequest(
                `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/history`
            );

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(
                    `Failed to load history (${response.status}): ${errorText}`
                );
            }

            const data = await response.json();
            const historyMessages: IChatMessage[] = (data.messages || []).map(
                (m: { role: string; content: string; timestamp: string }) => ({
                    role: m.role,
                    content: m.content,
                    timestamp: m.timestamp,
                })
            );

            setMessages(historyMessages);
        } catch (err: unknown) {
            const errorObj =
                err instanceof Error ? err : new Error("Failed to load history");
            setError(errorObj);
        } finally {
            setIsLoading(false);
        }
    }, [session, apiRequest, baseUrl]);

    /**
     * Switch the document/playbook context for the current session.
     * PATCH /api/ai/chat/sessions/{sessionId}/context
     */
    const switchContext = useCallback(
        async (documentId?: string, playbookId?: string, hostContext?: IHostContext): Promise<void> => {
            if (!session) {
                return;
            }

            setIsLoading(true);
            setError(null);

            try {
                const response = await apiRequest(
                    `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/context`,
                    {
                        method: "PATCH",
                        body: JSON.stringify({
                            documentId: documentId || null,
                            playbookId: playbookId || null,
                            hostContext: hostContext || null,
                        }),
                    }
                );

                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(
                        `Failed to switch context (${response.status}): ${errorText}`
                    );
                }
            } catch (err: unknown) {
                const errorObj =
                    err instanceof Error ? err : new Error("Failed to switch context");
                setError(errorObj);
            } finally {
                setIsLoading(false);
            }
        },
        [session, apiRequest, baseUrl]
    );

    /**
     * Delete the current session.
     * DELETE /api/ai/chat/sessions/{sessionId}
     */
    const deleteSession = useCallback(async (): Promise<void> => {
        if (!session) {
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            const response = await apiRequest(
                `${baseUrl}/api/ai/chat/sessions/${session.sessionId}`,
                { method: "DELETE" }
            );

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(
                    `Failed to delete session (${response.status}): ${errorText}`
                );
            }

            setSession(null);
            setMessages([]);
        } catch (err: unknown) {
            const errorObj =
                err instanceof Error ? err : new Error("Failed to delete session");
            setError(errorObj);
        } finally {
            setIsLoading(false);
        }
    }, [session, apiRequest, baseUrl]);

    /**
     * Add a message to the local history (used when sending/receiving messages).
     */
    const addMessage = useCallback((message: IChatMessage) => {
        setMessages((prev) => [...prev, message]);
    }, []);

    /**
     * Update the content of the last message in history (used during streaming).
     */
    const updateLastMessage = useCallback((content: string) => {
        setMessages((prev) => {
            if (prev.length === 0) {
                return prev;
            }
            const updated = [...prev];
            updated[updated.length - 1] = {
                ...updated[updated.length - 1],
                content,
            };
            return updated;
        });
    }, []);

    return {
        session,
        messages,
        isLoading,
        error,
        createSession,
        loadHistory,
        switchContext,
        deleteSession,
        addMessage,
        updateLastMessage,
    };
}
