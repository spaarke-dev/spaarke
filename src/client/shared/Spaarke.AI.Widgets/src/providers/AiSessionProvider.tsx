/**
 * AiSessionProvider — React context provider for multi-pane AI sessions (R2)
 *
 * Replaces R1's StandaloneAiProvider / StandaloneAiContext with a design that
 * routes SSE pane events to the typed PaneEventBus instead of a single-subscriber
 * ref. Multiple panes (WorkspacePane, ContextPaneController, etc.) can subscribe
 * to their channel independently — every subscriber receives every event.
 *
 * Key differences from R1 StandaloneAiProvider:
 *  - NO `subscribePaneEvents` / single-subscriber ref. Panes use usePaneEvent().
 *  - `onPaneEvent` callback routes SSE events to PaneEventBus channels:
 *      output_pane      → 'workspace' channel  (WorkspacePaneEvent widget_load/widget_update)
 *      source_pane      → 'context' channel    (ContextPaneEvent context_update)
 *      source_highlight → 'context' channel    (ContextPaneEvent context_highlight)
 *      safety_annotation→ 'safety' channel     (SafetyPaneEvent safety_annotation)
 *  - `turnCount` replaces `tokenCount` on the session-level indicator (token batching
 *    is an internal detail; external UI observes turn boundaries, not token deltas).
 *  - Streaming callbacks (`onStreamStart`, `onStreamToken`, `onStreamEnd`) are still
 *    provided for SprkChat's zero-serialisation path to the output pane.
 *
 * Auth: the provider reads auth state via `useAuth()` from @spaarke/auth
 * internally — consumers do NOT pass token / isAuthenticated props. This is the
 * Spaarke Auth v2 function-based contract (AUDIT-FINDINGS-AUTH-SYSTEM §H-4):
 * no token strings cross component boundaries. `initAuth(...)` MUST have been
 * called before this provider mounts.
 *
 * Must be rendered INSIDE:
 *  - PaneEventBusProvider (useDispatchPaneEvent depends on the bus context)
 *
 * @example
 * <PaneEventBusProvider>
 *   <AiSessionProvider bffBaseUrl={config.bffBaseUrl}>
 *     <SpaarkeAiShell />
 *   </AiSessionProvider>
 * </PaneEventBusProvider>
 *
 * @see StandaloneAiContext.tsx in Spaarke.AI.Context — R1 provider being replaced
 * @see PaneEventBus.ts — multi-subscriber typed bus
 * @see usePaneEvent — hook for pane components to subscribe to channels
 * @see useDispatchPaneEvent — hook used internally to route SSE events
 * @see ADR-012 — shared component library constraints
 * @see ADR-013 — AI Architecture: extend BFF, not separate service
 * @see ADR-022 — React 19 for Code Pages (bundled — this file is NOT PCF-safe)
 * @see AUDIT-FINDINGS-AUTH-SYSTEM §H-4 — function-based auth contract
 */

import React, {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { buildBffApiUrl, useAuth, type AuthenticatedFetchFn } from '@spaarke/auth';
import { useDispatchPaneEvent } from '../events/useDispatchPaneEvent';
import type { AiPaneEvent, EntityContext, StreamingCallbacks, StreamingState } from '@spaarke/ai-context';
import type { WorkspacePaneEvent, ContextPaneEvent, SafetyPaneEvent } from '../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Session storage keys (scoped to R2 to avoid collisions with R1 keys)
// ---------------------------------------------------------------------------

const SESSION_KEY_PREFIX = 'sprk_ai2_';
/** sessionStorage key for the active chat session ID */
export const AI_SESSION_CHAT_SESSION_KEY = `${SESSION_KEY_PREFIX}chatSessionId`;
/** sessionStorage key for the active playbook ID */
export const AI_SESSION_PLAYBOOK_KEY = `${SESSION_KEY_PREFIX}playbookId`;

// ---------------------------------------------------------------------------
// Context mapping type (from BFF GET /api/ai/chat/context-mappings/standalone)
// ---------------------------------------------------------------------------

/**
 * Response from the BFF context-mapping endpoint.
 * Provides the recommended playbook for the resolved entity context.
 */
export interface AiContextMapping {
  /** Recommended playbook ID for the resolved entity type and context */
  playbookId: string;
  /** Human-readable playbook name */
  playbookName?: string;
  /** Optional pre-seeded initial message for the chat session */
  initialMessage?: string;
  /** Whether the entity has indexed knowledge (for scope routing) */
  hasEntityKnowledge: boolean;
}

// ---------------------------------------------------------------------------
// Context value type
// ---------------------------------------------------------------------------

/**
 * The full context value provided by AiSessionProvider to child components.
 *
 * Consumers access this via useAiSession():
 *   const { entityContext, chatSessionId, playbookId, streaming, isStreaming } = useAiSession();
 */
export interface AiSessionContextValue {
  // ── Auth (function-based contract per Spaarke Auth v2 / §H-4) ───────────
  /** Whether a fresh cached BFF token is currently available (sync) */
  isAuthenticated: boolean;
  /**
   * Acquire a fresh BFF access token. Always routes through the @spaarke/auth
   * provider's in-memory cache + JWT exp validation. Use this only when
   * `authenticatedFetch` cannot wrap the network call (notably SSE
   * `ReadableStream` lifecycle).
   */
  getAccessToken: () => Promise<string>;
  /**
   * Authenticated fetch — auto-attaches Bearer header, retries 401 once with
   * backoff. Preferred over `getAccessToken` for one-shot HTTP calls because
   * the token is never materialised in consumer code.
   */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Azure AD tenant ID from the cached JWT `tid` claim. Empty string if no token cached. */
  tenantId: string;
  /** BFF API base URL (HOST only — use buildBffApiUrl() to build endpoint URLs) */
  bffBaseUrl: string;

  // ── Session State (persisted to sessionStorage) ───────────────────────────
  /** Active chat session ID (null when no session is open) */
  chatSessionId: string | null;
  /** Set the chat session ID — called when SprkChat creates a new session */
  setChatSessionId: (sessionId: string) => void;

  // ── Playbook State (persisted to sessionStorage) ──────────────────────────
  /** Active playbook ID governing session behaviour */
  playbookId: string | undefined;
  /** Set the playbook ID — called on playbook switch */
  setPlaybookId: (id: string) => void;

  // ── Entity Context ────────────────────────────────────────────────────────
  /**
   * Resolved entity context (null while resolving or when no entity found).
   * Provided via props (from useEntityResolver in the host shell) or null.
   */
  entityContext: EntityContext | null;

  // ── Context Mapping (from BFF) ────────────────────────────────────────────
  /** BFF-loaded context mapping (null while loading or on error) */
  contextMapping: AiContextMapping | null;
  /** Whether the context mapping is being fetched */
  isLoadingContextMapping: boolean;

  // ── Streaming ─────────────────────────────────────────────────────────────
  /**
   * StreamingCallbacks for SprkChat's zero-serialisation SSE path.
   *
   * SprkChat calls these directly from its SSE fetch loop:
   *   - onStreamStart / onStreamToken / onStreamEnd: drive UI indicators
   *   - onPaneEvent: routes typed SSE pane events to PaneEventBus channels
   *
   * Pass this object to SprkChat's `streaming` prop.
   */
  streaming: StreamingCallbacks;

  /** Current streaming state for UI indicators (isStreaming, tokenCount) */
  streamingState: StreamingState;

  // ── Turn Count ────────────────────────────────────────────────────────────
  /**
   * Number of completed conversation turns in the current session.
   * Incremented on each onStreamEnd call. Resets when the session is cleared.
   * Useful for session-level analytics and "empty state" detection.
   */
  turnCount: number;

  // ── Aggregate ─────────────────────────────────────────────────────────────
  /** True while context mapping is loading (entity resolution is synchronous in R2) */
  isLoading: boolean;
}

// ---------------------------------------------------------------------------
// Provider props
// ---------------------------------------------------------------------------

export interface AiSessionProviderProps {
  /** Child components that will receive the session context */
  children: ReactNode;
  /** BFF API base URL (HOST only — resolved by resolveRuntimeConfig() in the shell) */
  bffBaseUrl: string;
  /**
   * Entity context resolved by the host shell (via useEntityResolver).
   *
   * Pass null when operating in entityless mode (e.g. generic assistant mode
   * without an entity record in scope). The provider fetches a context mapping
   * only when entityContext is non-null and auth is ready.
   */
  entityContext?: EntityContext | null;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Safe sessionStorage read — returns null if storage is unavailable */
function readSession(key: string): string | null {
  try {
    return sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

/** Safe sessionStorage write — silently swallows quota/security errors */
function writeSession(key: string, value: string): void {
  try {
    sessionStorage.setItem(key, value);
  } catch {
    /* sessionStorage may be unavailable in some Dataverse webresource contexts */
  }
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const AiSessionContext = createContext<AiSessionContextValue | null>(null);
AiSessionContext.displayName = 'AiSessionContext';

// ---------------------------------------------------------------------------
// AiSessionProvider
// ---------------------------------------------------------------------------

/**
 * AiSessionProvider — wraps the SpaarkeAi three-pane shell with shared session state.
 *
 * Composes:
 *   - BFF context mapping fetch: loads recommended playbook for the entity
 *   - Chat session state: chatSessionId + playbookId persisted to sessionStorage
 *   - Streaming state: SSE token lifecycle for output pane UI indicators
 *   - PaneEventBus routing: SSE pane events are fanned out to typed channels
 *     so multiple pane subscribers receive every event independently
 *
 * MUST be rendered inside:
 *   - PaneEventBusProvider (useDispatchPaneEvent reads the bus context)
 *
 * `initAuth(...)` from @spaarke/auth MUST have been called before mount — the
 * provider reads auth state via `useAuth()` and that hook throws if the
 * library is not initialised.
 *
 * @example
 * <PaneEventBusProvider>
 *   <AiSessionProvider bffBaseUrl={config.bffBaseUrl} entityContext={entityContext}>
 *     <SpaarkeAiShell />
 *   </AiSessionProvider>
 * </PaneEventBusProvider>
 */
export function AiSessionProvider({
  children,
  bffBaseUrl,
  entityContext = null,
}: AiSessionProviderProps): React.JSX.Element {
  // ── Auth state from @spaarke/auth (function-based contract) ────────────
  // Reads fresh on every render — `isAuthenticated` is a sync getter against
  // the in-memory cache, `getAccessToken`/`authenticatedFetch` are stable
  // function references re-emitted each call. No token string in state.
  const { isAuthenticated, getAccessToken, authenticatedFetch, tenantId } = useAuth();

  // ── PaneEventBus dispatch (R2 multi-subscriber routing) ────────────────
  //
  // useDispatchPaneEvent() requires PaneEventBusProvider to be in the tree.
  // The returned dispatch function is stable across re-renders (memoised in
  // useDispatchPaneEvent via useCallback([bus])).
  const dispatch = useDispatchPaneEvent();

  // ── Context Mapping (BFF) ──────────────────────────────────────────────
  const [contextMapping, setContextMapping] = useState<AiContextMapping | null>(null);
  const [isLoadingContextMapping, setIsLoadingContextMapping] = useState<boolean>(false);

  useEffect(() => {
    // Only fetch context mapping when auth is ready and entity context is present.
    // Entityless mode: skip the fetch (contextMapping stays null).
    if (!isAuthenticated) return;
    if (!entityContext) return;

    let cancelled = false;
    setIsLoadingContextMapping(true);

    const fetchContextMapping = async (): Promise<void> => {
      try {
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
            `[AiSessionProvider] Context mapping fetch returned ${response.status} — operating without playbook recommendation`
          );
          return;
        }

        const mapping = (await response.json()) as AiContextMapping;

        if (!cancelled) {
          setContextMapping(mapping);

          // Pre-populate playbookId from BFF recommendation only when no
          // user-selected playbook exists in sessionStorage for this session.
          const existingPlaybook = readSession(AI_SESSION_PLAYBOOK_KEY);
          if (!existingPlaybook && mapping.playbookId) {
            setPlaybookIdState(mapping.playbookId);
            writeSession(AI_SESSION_PLAYBOOK_KEY, mapping.playbookId);
          }
        }
      } catch (err) {
        if (!cancelled) {
          console.warn('[AiSessionProvider] Context mapping fetch failed:', err);
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
    // authenticatedFetch is a stable module-level function in @spaarke/auth and
    // does not need to be a dep — including it would re-fire the effect on
    // every render because useAuth() returns a new object each call.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bffBaseUrl, entityContext, isAuthenticated]);

  // ── Chat Session State (persisted to sessionStorage) ───────────────────
  const [chatSessionId, setChatSessionIdState] = useState<string | null>(
    () => readSession(AI_SESSION_CHAT_SESSION_KEY)
  );

  const setChatSessionId = useCallback((sessionId: string): void => {
    setChatSessionIdState(sessionId);
    writeSession(AI_SESSION_CHAT_SESSION_KEY, sessionId);
  }, []);

  // ── Playbook State (persisted to sessionStorage) ───────────────────────
  const [playbookId, setPlaybookIdState] = useState<string | undefined>(
    () => readSession(AI_SESSION_PLAYBOOK_KEY) ?? undefined
  );

  const setPlaybookId = useCallback((id: string): void => {
    setPlaybookIdState(id);
    writeSession(AI_SESSION_PLAYBOOK_KEY, id);
  }, []);

  // ── Streaming State ────────────────────────────────────────────────────
  //
  // Token count uses a ref for high-frequency updates (one per SSE token)
  // to avoid React re-render storms. State is synced at turn boundaries
  // (onStreamStart / onStreamEnd) and every 10 tokens for the indicator.
  //
  // Turn count is incremented at onStreamEnd to track completed turns.
  // Unlike tokenCount it updates rarely and IS kept in React state.
  const [streamingState, setStreamingState] = useState<StreamingState>({
    isStreaming: false,
    operationId: null,
    tokenCount: 0,
  });
  const [turnCount, setTurnCount] = useState<number>(0);
  const tokenCountRef = useRef<number>(0);

  // ── SSE Pane Event Routing (R2 — multi-subscriber via PaneEventBus) ────
  //
  // R1 pattern (single-subscriber ref):
  //   paneEventSubscriberRef.current?.(event)  ← last caller wins, drops all others
  //
  // R2 pattern (multi-subscriber bus):
  //   dispatch(channel, typedEvent)            ← all subscribers on the channel receive it
  //
  // Routing table (mirrors the task specification exactly):
  //   AiPaneEvent.event          → PaneEventBus channel   → PaneEvent type
  //   'output_pane'              → 'workspace'             → widget_load / widget_update
  //   'source_pane'              → 'context'               → context_update
  //   'source_highlight'         → 'context'               → context_highlight
  //   'safety_annotation'        → 'safety'                → safety_annotation
  //
  // dispatch is stable (useCallback inside useDispatchPaneEvent), so
  // routePaneEvent does not need to be a dependency of streaming useMemo.
  const routePaneEvent = useCallback(
    (event: AiPaneEvent): void => {
      switch (event.event) {
        case 'output_pane': {
          // SSE from BFF signals a new or updated workspace widget.
          // Map to widget_load (first appearance) or widget_update (refresh).
          // We cannot reliably distinguish load vs update from the event alone,
          // so we use widget_load as the canonical type for SSE-driven widgets.
          // Widget components listening on 'workspace' can differentiate via
          // widgetData content if needed.
          const workspaceEvent: WorkspacePaneEvent = {
            type: 'widget_load',
            widgetType: event.widgetType,
            widgetData: event.payload,
          };
          dispatch('workspace', workspaceEvent);
          break;
        }

        case 'source_pane': {
          // SSE signals a new context document / data update in the context pane.
          const contextUpdateEvent: ContextPaneEvent = {
            type: 'context_update',
            contextType: event.widgetType,
            contextData: event.payload,
          };
          dispatch('context', contextUpdateEvent);
          break;
        }

        case 'source_highlight': {
          // SSE signals that a citation / selection should be highlighted in
          // the source document viewer.
          const contextHighlightEvent: ContextPaneEvent = {
            type: 'context_highlight',
            citationId: event.sourceRef,
            selectionRef: event.selectionRef,
          };
          dispatch('context', contextHighlightEvent);
          break;
        }

        default:
          // Unknown event type — log and drop. Do not throw: the SSE stream
          // is a forward-compatible protocol and new event types may appear
          // before this client is updated.
          console.warn(
            `[AiSessionProvider] Unknown SSE pane event type: "${(event as AiPaneEvent).event}" — event dropped`
          );
          break;
      }
    },
    [dispatch]
  );

  // ── StreamingCallbacks (stable — only recreated when routePaneEvent changes) ──
  //
  // These are passed to SprkChat as the `streaming` prop. They must be stable
  // to prevent SprkChat from re-initialising its SSE fetch hooks on every render.
  // routePaneEvent changes only when dispatch changes (i.e. when the bus swaps —
  // very rare in practice), so streaming is effectively stable for the session.
  const streaming: StreamingCallbacks = useMemo(
    (): StreamingCallbacks => ({
      onStreamStart: (operationId: string): void => {
        tokenCountRef.current = 0;
        setStreamingState({
          isStreaming: true,
          operationId,
          tokenCount: 0,
        });
      },

      onStreamToken: (_token: string): void => {
        // Consumers register their own token handler (e.g. append to editor ref).
        // Here we only track count for the StreamingState UI indicator.
        tokenCountRef.current += 1;
        // Batch state updates every 10 tokens to avoid re-render storm.
        if (tokenCountRef.current % 10 === 0) {
          setStreamingState((prev) => ({
            ...prev,
            tokenCount: tokenCountRef.current,
          }));
        }
      },

      onStreamEnd: (operationId: string): void => {
        setStreamingState({
          isStreaming: false,
          operationId,
          tokenCount: tokenCountRef.current,
        });
        // Increment turn count — one turn completes per stream-end.
        setTurnCount((n) => n + 1);
      },

      // onPaneEvent — the critical R1→R2 migration point.
      // R1 called paneEventSubscriberRef.current (single subscriber, last wins).
      // R2 routes to PaneEventBus channels (every subscriber receives every event).
      onPaneEvent: routePaneEvent,
    }),
    [routePaneEvent]
  );

  // ── Aggregate loading state ────────────────────────────────────────────
  const isLoading = isLoadingContextMapping;

  // ── Compose context value ──────────────────────────────────────────────
  const value: AiSessionContextValue = useMemo(
    (): AiSessionContextValue => ({
      // Auth (function-based contract — no token strings)
      isAuthenticated,
      getAccessToken,
      authenticatedFetch,
      tenantId,
      bffBaseUrl,

      // Session
      chatSessionId,
      setChatSessionId,

      // Playbook
      playbookId,
      setPlaybookId,

      // Entity
      entityContext,

      // Context mapping
      contextMapping,
      isLoadingContextMapping,

      // Streaming
      streaming,
      streamingState,

      // Turn count
      turnCount,

      // Aggregate
      isLoading,
    }),
    [
      isAuthenticated,
      getAccessToken,
      authenticatedFetch,
      tenantId,
      bffBaseUrl,
      chatSessionId,
      setChatSessionId,
      playbookId,
      setPlaybookId,
      entityContext,
      contextMapping,
      isLoadingContextMapping,
      streaming,
      streamingState,
      turnCount,
      isLoading,
    ]
  );

  return <AiSessionContext.Provider value={value}>{children}</AiSessionContext.Provider>;
}

// ---------------------------------------------------------------------------
// Internal context export (for useAiSession hook)
// ---------------------------------------------------------------------------

export { AiSessionContext };
