/**
 * StandaloneAiContext — React context provider for standalone AI sessions
 *
 * Provides entity-scoped AI state to the SpaarkeAi Code Page three-pane layout:
 *   - Entity context resolved from URL parameters (useEntityResolver)
 *   - Context mapping from BFF (GET /api/ai/chat/context-mappings/standalone)
 *   - Chat session ID persisted to sessionStorage
 *   - Playbook ID persisted to sessionStorage
 *   - Streaming callbacks for SSE token flow to output pane
 *
 * This provider is the generalised equivalent of AnalysisAiContext for the
 * standalone (no analysis record) use case. Compose it INSIDE AuthProvider.
 *
 * @see AnalysisAiContext.tsx in AnalysisWorkspace — the analysis-scoped analogue
 * @see ADR-012 — shared component library constraints
 * @see auth.md constraint — all BFF URLs via buildBffApiUrl(), authenticatedFetch()
 *
 * Standards: ADR-012 (shared library), ADR-021 (Fluent UI v9 — theme tokens via props, not hardcoded)
 * NOT PCF-safe — this provider uses React 19 APIs (useContext, ReactNode from 'react').
 */

import * as React from 'react';
import {
  createContext,
  useContext,
  useState,
  useCallback,
  useMemo,
  useRef,
  useEffect,
  type ReactNode,
} from 'react';
import type { AiPaneEvent } from '../types';
import { buildBffApiUrl, authenticatedFetch } from '@spaarke/auth';
import { useEntityResolver } from './useEntityResolver';
import type {
  StandaloneAiContextValue,
  StandaloneAiProviderProps,
  StandaloneContextMapping,
} from '../types/standalone-context';
import {
  STANDALONE_CHAT_SESSION_KEY,
  STANDALONE_PLAYBOOK_KEY,
} from '../types/standalone-context';
import type { StreamingCallbacks, StreamingState } from '../types';

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const StandaloneAiContext = createContext<StandaloneAiContextValue | null>(null);

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

/**
 * StandaloneAiProvider — wraps the SpaarkeAi Code Page with shared AI state.
 *
 * Composes:
 *   - useEntityResolver: resolves entity context from URL params + Xrm frame-walk
 *   - BFF context mapping fetch: loads recommended playbook for the entity
 *   - Chat session state: chatSessionId + playbookId persisted to sessionStorage
 *   - Streaming state: SSE token lifecycle for output pane UI indicators
 *
 * Must be rendered INSIDE AuthProvider (needs auth token for BFF calls).
 *
 * @example
 * <AuthProvider>
 *   <StandaloneAiProvider bffBaseUrl={config.bffBaseUrl} token={token} isAuthenticated={isAuthenticated}>
 *     <SpaarkeAiApp />
 *   </StandaloneAiProvider>
 * </AuthProvider>
 */
export function StandaloneAiProvider({
  children,
  bffBaseUrl,
  token,
  isAuthenticated,
}: StandaloneAiProviderProps): React.JSX.Element {
  // ── Entity Resolution ──────────────────────────────────────────────────
  const { entityContext, isResolving: isResolvingEntity } = useEntityResolver();

  // ── Context Mapping (BFF) ──────────────────────────────────────────────
  const [contextMapping, setContextMapping] = useState<StandaloneContextMapping | null>(null);
  const [isLoadingContextMapping, setIsLoadingContextMapping] = useState<boolean>(false);

  useEffect(() => {
    // Only fetch context mapping when auth is ready and entity is resolved
    if (!isAuthenticated || !token || isResolvingEntity) return;
    if (!entityContext) return; // No entity — operate in entityless mode

    let cancelled = false;
    setIsLoadingContextMapping(true);

    const fetchContextMapping = async (): Promise<void> => {
      try {
        // Build query params from resolved entity context
        const params = new URLSearchParams();
        params.set('entityType', entityContext.entityType);
        params.set('entityId', entityContext.entityId);
        if (entityContext.matterId) params.set('matterId', entityContext.matterId);
        if (entityContext.projectId) params.set('projectId', entityContext.projectId);
        if (entityContext.documentId) params.set('documentId', entityContext.documentId);

        // All BFF URL construction MUST use buildBffApiUrl() per auth.md constraint
        const url = buildBffApiUrl(bffBaseUrl, `/ai/chat/context-mappings/standalone?${params}`);

        const response = await authenticatedFetch(url);
        if (!response.ok) {
          console.warn(
            `[StandaloneAiContext] Context mapping fetch returned ${response.status} — operating without playbook recommendation`
          );
          return;
        }

        const mapping = (await response.json()) as StandaloneContextMapping;

        if (!cancelled) {
          setContextMapping(mapping);

          // Pre-populate playbookId from BFF recommendation if not already set in session
          const existingPlaybook = (() => {
            try {
              return sessionStorage.getItem(STANDALONE_PLAYBOOK_KEY);
            } catch {
              return null;
            }
          })();

          if (!existingPlaybook && mapping.playbookId) {
            setPlaybookIdState(mapping.playbookId);
            try {
              sessionStorage.setItem(STANDALONE_PLAYBOOK_KEY, mapping.playbookId);
            } catch {
              /* sessionStorage may be unavailable */
            }
          }
        }
      } catch (err) {
        if (!cancelled) {
          console.warn('[StandaloneAiContext] Context mapping fetch failed:', err);
        }
      } finally {
        if (!cancelled) {
          setIsLoadingContextMapping(false);
        }
      }
    };

    void fetchContextMapping();
    return () => {
      cancelled = true;
    };
  }, [bffBaseUrl, entityContext, isAuthenticated, isResolvingEntity, token]);

  // ── Chat Session State (persisted to sessionStorage) ───────────────────
  const [chatSessionId, setChatSessionIdState] = useState<string | null>(() => {
    try {
      return sessionStorage.getItem(STANDALONE_CHAT_SESSION_KEY);
    } catch {
      return null;
    }
  });

  const setChatSessionId = useCallback((sessionId: string) => {
    setChatSessionIdState(sessionId);
    try {
      sessionStorage.setItem(STANDALONE_CHAT_SESSION_KEY, sessionId);
    } catch {
      /* sessionStorage may be unavailable in some contexts */
    }
  }, []);

  // ── Playbook State (persisted to sessionStorage) ───────────────────────
  const [playbookId, setPlaybookIdState] = useState<string | undefined>(() => {
    try {
      return sessionStorage.getItem(STANDALONE_PLAYBOOK_KEY) ?? undefined;
    } catch {
      return undefined;
    }
  });

  const setPlaybookId = useCallback((id: string) => {
    setPlaybookIdState(id);
    try {
      sessionStorage.setItem(STANDALONE_PLAYBOOK_KEY, id);
    } catch {
      /* sessionStorage may be unavailable */
    }
  }, []);

  // ── Streaming State ────────────────────────────────────────────────────
  //
  // Streaming state tracks SSE lifecycle for UI indicators (e.g., a spinner
  // while the AI streams content to the output pane).
  //
  // Token count uses a ref for high-frequency updates (one per SSE token)
  // to avoid React re-render storms. State is synced at start/end boundaries
  // and every 10 tokens for StreamingIndicator updates.
  //
  // See AnalysisAiContext.tsx streaming section for the pattern origin.
  const [streamingState, setStreamingState] = useState<StreamingState>({
    isStreaming: false,
    operationId: null,
    tokenCount: 0,
  });
  const tokenCountRef = useRef<number>(0);

  // Pane event subscriber ref — holds the handler registered by OutputPanel or SourcePanel.
  // Using a ref (not state) because pane events arrive synchronously from the SprkChat SSE
  // fetch loop and must not trigger a re-render of StandaloneAiProvider.
  // At most one subscriber is active at a time (last call wins).
  const paneEventSubscriberRef = useRef<((event: AiPaneEvent) => void) | null>(null);

  // subscribePaneEvents — stable identity via useCallback (no deps — accesses ref only)
  const subscribePaneEvents = useCallback(
    (handler: ((event: AiPaneEvent) => void) | null) => {
      paneEventSubscriberRef.current = handler;
    },
    []
  );

  // Streaming callbacks — zero-serialization path to output pane widget ref
  const streaming: StreamingCallbacks = useMemo(
    () => ({
      onStreamStart: (operationId: string) => {
        tokenCountRef.current = 0;
        setStreamingState({
          isStreaming: true,
          operationId,
          tokenCount: 0,
        });
      },
      onStreamToken: (_token: string) => {
        // Consumers register their own token handler (e.g., append to editor ref).
        // Here we only track count for StreamingState UI indicator.
        tokenCountRef.current += 1;
        // Batch state updates every 10 tokens to avoid re-render storm
        if (tokenCountRef.current % 10 === 0) {
          setStreamingState(prev => ({
            ...prev,
            tokenCount: tokenCountRef.current,
          }));
        }
      },
      onStreamEnd: (operationId: string) => {
        setStreamingState({
          isStreaming: false,
          operationId,
          tokenCount: tokenCountRef.current,
        });
      },
      // Task 041: Forward pane-routing SSE events to the active subscriber.
      // Called synchronously from SprkChat's SSE fetch loop via ChatPanel's onPaneEvent prop.
      onPaneEvent: (event: AiPaneEvent) => {
        const handler = paneEventSubscriberRef.current;
        if (handler) {
          handler(event);
        }
      },
    }),
    []
  );

  // ── Aggregate loading state ────────────────────────────────────────────
  const isLoading = isResolvingEntity || isLoadingContextMapping;

  // ── Compose context value ──────────────────────────────────────────────
  const value: StandaloneAiContextValue = useMemo(
    () => ({
      // Entity
      entityContext,
      isResolvingEntity,

      // Auth
      token,
      isAuthenticated,

      // BFF
      bffBaseUrl,

      // Context mapping
      contextMapping,
      isLoadingContextMapping,

      // Chat session
      chatSessionId,
      setChatSessionId,

      // Playbook
      playbookId,
      setPlaybookId,

      // Streaming
      streaming,
      streamingState,

      // Pane SSE events
      subscribePaneEvents,

      // Aggregate
      isLoading,
    }),
    [
      entityContext,
      isResolvingEntity,
      token,
      isAuthenticated,
      bffBaseUrl,
      contextMapping,
      isLoadingContextMapping,
      chatSessionId,
      setChatSessionId,
      playbookId,
      setPlaybookId,
      streaming,
      streamingState,
      subscribePaneEvents,
      isLoading,
    ]
  );

  return <StandaloneAiContext.Provider value={value}>{children}</StandaloneAiContext.Provider>;
}

// ---------------------------------------------------------------------------
// Internal context export (for useStandaloneAi hook)
// ---------------------------------------------------------------------------

export { StandaloneAiContext };
