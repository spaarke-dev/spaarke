/**
 * AnalysisAiContext — Shared React Context for unified Analysis Workspace
 *
 * Replaces BroadcastChannel cross-pane communication with direct React context.
 * Provides analysis state, editor refs, selection state, auth, and panel callbacks
 * to both the editor and chat panels in the unified workspace.
 *
 * Chat session lifecycle and context mapping are delegated to shared hooks from
 * @spaarke/ai-context (useChatSession, useChatContextMapping) per ADR-012.
 *
 * @see ADR-012 — SprkChat uses callback-based props from this context; shared hooks
 * @see ADR-021 — Fluent UI v9 design system
 */

import {
  createContext,
  useContext,
  useState,
  useCallback,
  useMemo,
  useRef,
  type ReactNode,
  type RefObject,
} from 'react';
// ---------------------------------------------------------------------------
// @spaarke/ai-context — shared hooks (task AIPU-050)
// ---------------------------------------------------------------------------
import {
  useChatSession,
  useChatContextMapping,
} from '@spaarke/ai-context';
import type {
  IAnalysisChatContextResponse,
} from '@spaarke/ai-context';
import { useAuthContext, type AuthContextValue } from './AuthContext';
import { useAnalysisLoader, type UseAnalysisLoaderResult } from '../hooks/useAnalysisLoader';
import { getHostContext } from '../services/hostContext';
import type { AnalysisRecord, DocumentMetadata, IChatMessage } from '../types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Editor operations exposed via ref for direct insert and streaming. */
export interface EditorRef {
  /** Insert content at the current cursor position (or end if no selection). */
  insert: (content: string) => void;
  /** Get current HTML content of the editor. */
  getContent: () => string;
}

/** Streaming callback handlers for SSE token flow to editor. */
export interface StreamingCallbacks {
  onStreamStart: (operationId: string) => void;
  onStreamToken: (token: string) => void;
  onStreamEnd: (operationId: string) => void;
}

/** Streaming state tracked by the context for UI indicators. */
export interface StreamingState {
  /** Whether a document stream is currently in progress */
  isStreaming: boolean;
  /** The operationId of the current (or last) streaming operation */
  operationId: string | null;
  /** Number of tokens received in the current streaming operation */
  tokenCount: number;
}

/** The full context value provided to all workspace panels. */
export interface AnalysisAiContextValue {
  // ── Analysis State ──────────────────────────────────────────────────────
  /** Loaded analysis record (null while loading) */
  analysis: AnalysisRecord | null;
  /** Loaded document metadata (null while loading) */
  document: DocumentMetadata | null;
  /** Whether any resource is currently loading */
  isLoading: boolean;
  /** Full loader result for advanced usage */
  loader: UseAnalysisLoaderResult;

  // ── Auth ────────────────────────────────────────────────────────────────
  /** Bearer access token (null when not authenticated) */
  token: string | null;
  /** Whether the user is fully authenticated */
  isAuthenticated: boolean;
  /** Full auth context for advanced usage */
  auth: AuthContextValue;

  // ── Host Context ────────────────────────────────────────────────────────
  /** Analysis ID from URL params */
  analysisId: string;
  /** Document ID from URL params */
  documentId: string;
  /** BFF API base URL */
  bffBaseUrl: string;

  // ── Editor Integration ──────────────────────────────────────────────────
  /** Ref to editor operations (set by EditorPanel) */
  editorRef: RefObject<EditorRef | null>;
  /** Current editor text selection (updated by EditorPanel) */
  editorSelection: string;
  /** Update the editor selection text (called by EditorPanel on selection change) */
  setEditorSelection: (text: string) => void;

  // ── Panel Callbacks (for SprkChat props) ────────────────────────────────
  /** Insert content into the editor at cursor position */
  onInsertToEditor: (content: string) => void;
  /** Streaming callbacks for SSE token flow to editor */
  streaming: StreamingCallbacks;
  /** Current streaming state (isStreaming, operationId, tokenCount) for UI indicators */
  streamingState: StreamingState;

  // ── Chat State ──────────────────────────────────────────────────────────
  /** Current chat session ID (persisted to sessionStorage) */
  chatSessionId: string | null;
  /** Set chat session ID (called when session is created by SprkChat) */
  setChatSessionId: (sessionId: string) => void;
  /** Current playbook ID for the chat */
  playbookId: string | undefined;
  /** Set playbook ID (called on playbook switch) */
  setPlaybookId: (playbookId: string) => void;
  /** Pre-loaded chat history from sprk_chathistory (for SprkChat initialMessages) */
  chatHistory: IChatMessage[] | undefined;

  // ── Analysis Chat Context (from @spaarke/ai-context useChatContextMapping) ──
  /**
   * Analysis-scoped chat context mapping from the BFF API.
   * Provides available playbooks, inline actions, knowledge sources, and commands
   * for the current analysis. Null while loading or when analysisId is absent.
   * @see AnalysisChatContextEndpoints.cs — GET /api/ai/chat/context-mappings/analysis/{id}
   */
  contextMapping: IAnalysisChatContextResponse | null;
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const AnalysisAiContext = createContext<AnalysisAiContextValue | null>(null);

// ---------------------------------------------------------------------------
// Session Storage Keys
// ---------------------------------------------------------------------------

const SESSION_KEY_PREFIX = 'sprk_aw_';
const CHAT_SESSION_KEY = `${SESSION_KEY_PREFIX}chatSessionId`;
const PLAYBOOK_KEY = `${SESSION_KEY_PREFIX}playbookId`;

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export interface AnalysisAiProviderProps {
  children: ReactNode;
  /** BFF API base URL (resolved by host context or environment variable) */
  bffBaseUrl: string;
}

/**
 * AnalysisAiProvider — wraps the workspace component tree with shared context.
 *
 * Composes:
 * - AuthContext (token, refresh)
 * - useAnalysisLoader (analysis record, document metadata)
 * - useChatSession (@spaarke/ai-context) — session lifecycle management
 * - useChatContextMapping (@spaarke/ai-context) — analysis chat context
 * - Editor ref and selection state
 * - Panel callbacks (insert-to-editor, streaming)
 *
 * Must be rendered INSIDE AuthProvider (needs auth token).
 */
export function AnalysisAiProvider({ children, bffBaseUrl }: AnalysisAiProviderProps): JSX.Element {
  const auth = useAuthContext();
  const hostContext = getHostContext();

  // Analysis data loading
  const loader = useAnalysisLoader({
    analysisId: hostContext.analysisId,
    documentId: hostContext.documentId,
    token: auth.token,
  });

  // ── Chat session lifecycle (AIPU-050: delegated to @spaarke/ai-context) ──
  //
  // useChatSession manages the full chat session lifecycle via the BFF API.
  // The session object provides the canonical sessionId. The existing
  // sessionStorage persistence (chatSessionId) stays in the public API for
  // backward compatibility with ChatPanel and SprkChat.onSessionCreated.
  const chatSessionHook = useChatSession({
    bffBaseUrl,
    initialMessages: loader.analysis?.chatHistory,
  });

  // ── Analysis chat context mapping (AIPU-050: from @spaarke/ai-context) ──
  //
  // Fetches available playbooks, inline actions, commands, and knowledge sources
  // for the active analysis from GET /api/ai/chat/context-mappings/analysis/{id}.
  // Re-fetches automatically when analysisId or playbookId changes (spec FR-08).

  // Playbook state — persisted to sessionStorage (read initial value for context mapping)
  const [playbookId, setPlaybookIdState] = useState<string | undefined>(() => {
    try {
      return sessionStorage.getItem(PLAYBOOK_KEY) ?? undefined;
    } catch {
      return undefined;
    }
  });

  const { contextMapping } = useChatContextMapping({
    analysisId: hostContext.analysisId || undefined,
    playbookId,
    bffBaseUrl,
  });

  // Editor ref — set by EditorPanel when it mounts
  const editorRef = useRef<EditorRef | null>(null);

  // Editor selection state — updated by EditorPanel on text selection
  const [editorSelection, setEditorSelection] = useState<string>('');

  // ── Chat session ID — bridged from useChatSession + sessionStorage persistence ──
  //
  // SprkChat manages session creation internally and calls onSessionCreated with
  // the new session object. We extract the sessionId string and persist it.
  // The useChatSession hook's session object provides the authoritative session
  // state when the hook creates sessions directly (e.g., context switching).
  const [chatSessionId, setChatSessionIdState] = useState<string | null>(() => {
    // Initialise from sessionStorage so returning users resume their session.
    // This will be overridden once SprkChat calls onSessionCreated.
    try {
      return sessionStorage.getItem(CHAT_SESSION_KEY);
    } catch {
      return null;
    }
  });

  // Derive the effective session ID: prefer the useChatSession hook's authoritative
  // session (when it has created one directly), then fall back to the sessionStorage
  // value (set by SprkChat via setChatSessionId callback).
  const effectiveChatSessionId = chatSessionHook.session?.sessionId ?? chatSessionId;

  const setChatSessionId = useCallback((sessionId: string) => {
    setChatSessionIdState(sessionId);
    try {
      sessionStorage.setItem(CHAT_SESSION_KEY, sessionId);
    } catch {
      // sessionStorage may be unavailable in some contexts
    }
  }, []);

  const setPlaybookId = useCallback((id: string) => {
    setPlaybookIdState(id);
    try {
      sessionStorage.setItem(PLAYBOOK_KEY, id);
    } catch {
      // sessionStorage may be unavailable
    }
  }, []);

  // Insert-to-editor callback (direct ref call, no BroadcastChannel)
  const onInsertToEditor = useCallback((content: string) => {
    editorRef.current?.insert(content);
  }, []);

  // ── Streaming state (Task 007) ──────────────────────────────────────────
  //
  // Tracks streaming lifecycle for UI indicators (StreamingIndicator).
  // Token count uses a ref for high-frequency updates (one per SSE token)
  // to avoid React re-render storms. State is synced at start/end boundaries.
  const [streamingState, setStreamingState] = useState<StreamingState>({
    isStreaming: false,
    operationId: null,
    tokenCount: 0,
  });
  const tokenCountRef = useRef<number>(0);

  // Streaming callbacks (bypass React re-render — write directly to editor via ref)
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
      onStreamToken: (token: string) => {
        editorRef.current?.insert(token);
        tokenCountRef.current += 1;
        // Batch token count updates: sync to React state every 10 tokens
        // to give StreamingIndicator a reasonable update cadence without
        // re-rendering on every single token.
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
    }),
    []
  );

  // Compose the full context value
  const value: AnalysisAiContextValue = useMemo(
    () => ({
      // Analysis state
      analysis: loader.analysis,
      document: loader.document,
      isLoading: loader.isLoading,
      loader,

      // Auth
      token: auth.token,
      isAuthenticated: auth.isAuthenticated,
      auth,

      // Host context
      analysisId: hostContext.analysisId,
      documentId: hostContext.documentId,
      bffBaseUrl,

      // Editor integration
      editorRef,
      editorSelection,
      setEditorSelection,

      // Panel callbacks
      onInsertToEditor,
      streaming,
      streamingState,

      // Chat state (chatSessionId bridged from useChatSession + sessionStorage)
      chatSessionId: effectiveChatSessionId,
      setChatSessionId,
      playbookId,
      setPlaybookId,
      chatHistory: loader.analysis?.chatHistory,

      // Analysis chat context mapping (from @spaarke/ai-context useChatContextMapping)
      contextMapping,
    }),
    [
      loader,
      auth,
      hostContext.analysisId,
      hostContext.documentId,
      bffBaseUrl,
      editorSelection,
      setEditorSelection,
      onInsertToEditor,
      streaming,
      streamingState,
      effectiveChatSessionId,
      setChatSessionId,
      playbookId,
      setPlaybookId,
      contextMapping,
    ]
  );

  return <AnalysisAiContext.Provider value={value}>{children}</AnalysisAiContext.Provider>;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useAnalysisAi — access the shared workspace context from any component.
 *
 * @throws Error if used outside of an AnalysisAiProvider
 */
export function useAnalysisAi(): AnalysisAiContextValue {
  const context = useContext(AnalysisAiContext);
  if (!context) {
    throw new Error(
      'useAnalysisAi must be used within an AnalysisAiProvider. ' +
        'Wrap your component tree with <AnalysisAiProvider>.'
    );
  }
  return context;
}
