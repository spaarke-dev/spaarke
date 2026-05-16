/**
 * useChatSession - Session lifecycle management hook
 *
 * Standalone hook for managing chat session creation, history loading,
 * context switching, and deletion. Extracted from SprkChat for use
 * across any Code Page AI surface.
 *
 * All API calls use ChatApiClient (which uses buildBffApiUrl + authenticatedFetch).
 *
 * @see ADR-012 — Shared Component Library
 * @see ADR-013 — AI Architecture (extend BFF, not separate service)
 * @see ChatEndpoints.cs — POST /sessions, GET /history, PATCH /context, DELETE /sessions
 */

import { useState, useCallback } from 'react';
import { ChatApiClient } from '../services/ChatApiClient';
import type {
  IChatSession,
  IChatMessage,
  IChatMessageMetadata,
  IHostContext,
  IUseChatSessionResult,
} from '../types/chat';

// ─────────────────────────────────────────────────────────────────────────────
// Hook options
// ─────────────────────────────────────────────────────────────────────────────

export interface UseChatSessionOptions {
  /**
   * BFF API base URL. Must be obtained via buildBffApiUrl() or resolveRuntimeConfig().
   * ChatApiClient will call buildBffApiUrl() internally for every request.
   */
  bffBaseUrl: string;
  /**
   * Pre-loaded messages to show before any session is created.
   * (e.g., from sprk_chathistory on the analysis record)
   */
  initialMessages?: IChatMessage[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook for managing chat session lifecycle.
 *
 * Standalone — does not depend on SprkChat internals.
 * All BFF calls go through ChatApiClient → buildBffApiUrl() → authenticatedFetch().
 *
 * @example
 * ```tsx
 * const {
 *   session, messages, isLoading, error,
 *   createSession, loadHistory, switchContext, deleteSession,
 *   addMessage, updateLastMessage
 * } = useChatSession({ bffBaseUrl: config.bffBaseUrl });
 * ```
 */
export function useChatSession(options: UseChatSessionOptions): IUseChatSessionResult {
  const { bffBaseUrl, initialMessages } = options;

  const [session, setSession] = useState<IChatSession | null>(null);
  const [messages, setMessages] = useState<IChatMessage[]>(() => initialMessages ?? []);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<Error | null>(null);

  // Lazily instantiate the client — recreate only when bffBaseUrl changes.
  // We use a ref-like pattern via useCallback closure rather than useRef
  // to stay within the React 16/17 API surface (per ADR-022).
  const getClient = useCallback(() => new ChatApiClient(bffBaseUrl), [bffBaseUrl]);

  // ── Create session ─────────────────────────────────────────────────────────

  const createSession = useCallback(
    async (documentId?: string, playbookId?: string, hostContext?: IHostContext): Promise<IChatSession | null> => {
      setIsLoading(true);
      setError(null);

      try {
        const client = getClient();
        const newSession = await client.createSession({
          documentId: documentId ?? null,
          playbookId: playbookId ?? null,
          hostContext: hostContext ?? null,
        });

        setSession(newSession);
        setMessages(initialMessages ?? []);
        return newSession;
      } catch (err: unknown) {
        const errorObj = err instanceof Error ? err : new Error('Failed to create session');
        setError(errorObj);
        return null;
      } finally {
        setIsLoading(false);
      }
    },
    [getClient, initialMessages]
  );

  // ── Load history ───────────────────────────────────────────────────────────

  const loadHistory = useCallback(async (): Promise<void> => {
    if (!session) {
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const client = getClient();
      const historyMessages = await client.getSessionHistory(session.sessionId);
      setMessages(historyMessages);
    } catch (err: unknown) {
      const errorObj = err instanceof Error ? err : new Error('Failed to load history');
      setError(errorObj);
    } finally {
      setIsLoading(false);
    }
  }, [session, getClient]);

  // ── Switch context ─────────────────────────────────────────────────────────

  const switchContext = useCallback(
    async (
      documentId?: string,
      playbookId?: string,
      hostContext?: IHostContext,
      additionalDocumentIds?: string[]
    ): Promise<void> => {
      if (!session) {
        return;
      }

      setIsLoading(true);
      setError(null);

      try {
        const client = getClient();
        await client.switchContext(session.sessionId, {
          documentId: documentId ?? null,
          playbookId: playbookId ?? null,
          hostContext: hostContext ?? null,
          additionalDocumentIds,
        });
      } catch (err: unknown) {
        const errorObj = err instanceof Error ? err : new Error('Failed to switch context');
        setError(errorObj);
      } finally {
        setIsLoading(false);
      }
    },
    [session, getClient]
  );

  // ── Delete session ─────────────────────────────────────────────────────────

  const deleteSession = useCallback(async (): Promise<void> => {
    if (!session) {
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const client = getClient();
      await client.deleteSession(session.sessionId);
      setSession(null);
      setMessages([]);
    } catch (err: unknown) {
      const errorObj = err instanceof Error ? err : new Error('Failed to delete session');
      setError(errorObj);
    } finally {
      setIsLoading(false);
    }
  }, [session, getClient]);

  // ── Local message management ───────────────────────────────────────────────

  /** Add a message to the local history (used when sending/receiving messages). */
  const addMessage = useCallback((message: IChatMessage) => {
    setMessages(prev => [...prev, message]);
  }, []);

  /** Update the content of the last message (used during streaming). */
  const updateLastMessage = useCallback((content: string) => {
    setMessages(prev => {
      if (prev.length === 0) return prev;
      const updated = [...prev];
      updated[updated.length - 1] = { ...updated[updated.length - 1], content };
      return updated;
    });
  }, []);

  /**
   * Update the metadata of the last message (Phase 2F).
   * Used to set plan_preview metadata after a plan_preview SSE event and to
   * update plan step statuses during plan execution streaming.
   */
  const updateLastMessageMetadata = useCallback((metadata: IChatMessageMetadata) => {
    setMessages(prev => {
      if (prev.length === 0) return prev;
      const updated = [...prev];
      updated[updated.length - 1] = {
        ...updated[updated.length - 1],
        metadata: {
          ...updated[updated.length - 1].metadata,
          ...metadata,
        },
      };
      return updated;
    });
  }, []);

  /**
   * Update a specific message's metadata by index (Phase 2F).
   * Accepts either a plain metadata object or a function updater.
   */
  const updateMessageMetadataAt = useCallback(
    (
      index: number,
      metadataOrUpdater:
        | IChatMessageMetadata
        | ((current: IChatMessageMetadata | undefined) => IChatMessageMetadata)
    ) => {
      setMessages(prev => {
        if (index < 0 || index >= prev.length) return prev;
        const updated = [...prev];
        const currentMetadata = updated[index].metadata;
        const newMetadata =
          typeof metadataOrUpdater === 'function'
            ? metadataOrUpdater(currentMetadata)
            : { ...currentMetadata, ...metadataOrUpdater };
        updated[index] = { ...updated[index], metadata: newMetadata };
        return updated;
      });
    },
    []
  );

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
    updateLastMessageMetadata,
    updateMessageMetadataAt,
  };
}
