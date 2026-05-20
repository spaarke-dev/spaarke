/**
 * AnalysisAiContext — Shared React Context for unified Analysis Workspace
 *
 * Replaces BroadcastChannel cross-pane communication with direct React context.
 * Provides analysis state, editor refs, selection state, function-based auth,
 * and panel callbacks to both the editor and chat panels in the unified workspace.
 *
 * Chat session lifecycle and context mapping are delegated to shared hooks from
 * @spaarke/ai-context (useChatSession, useChatContextMapping) per ADR-012.
 *
 * Spaarke Auth v2 (task 026):
 *   - NO `token: string` in context value. SprkChat receives `authenticatedFetch`
 *     and `getAccessToken` instead. Token strings never cross a component
 *     boundary (CLAUDE.md §D-AUTH-1).
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
import { useChatSession, useChatContextMapping } from '@spaarke/ai-context';
import type { IAnalysisChatContextResponse } from '@spaarke/ai-context';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { useAuth } from '../hooks/useAuth';
import { useAnalysisLoader, type UseAnalysisLoaderResult } from '../hooks/useAnalysisLoader';
import { getHostContext } from '../services/hostContext';
import type { AnalysisRecord, DocumentMetadata, IChatMessage } from '../types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Editor operations exposed via ref for direct insert and streaming. */
export interface EditorRef {
  insert: (content: string) => void;
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
  isStreaming: boolean;
  operationId: string | null;
  tokenCount: number;
}

/** The full context value provided to all workspace panels. */
export interface AnalysisAiContextValue {
  // ── Analysis State ──────────────────────────────────────────────────────
  analysis: AnalysisRecord | null;
  document: DocumentMetadata | null;
  isLoading: boolean;
  loader: UseAnalysisLoaderResult;

  // ── Auth (function-based — Spaarke Auth v2) ─────────────────────────────
  /** Whether the user is fully authenticated (bootstrap complete + token cached). */
  isAuthenticated: boolean;
  /** Authenticated fetch — pass to SprkChat or call directly for BFF requests. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Token getter — pass to SprkChat (for SSE) or call before opening a stream. */
  getAccessToken: () => Promise<string>;

  // ── Host Context ────────────────────────────────────────────────────────
  analysisId: string;
  documentId: string;
  bffBaseUrl: string;

  // ── Editor Integration ──────────────────────────────────────────────────
  editorRef: RefObject<EditorRef | null>;
  editorSelection: string;
  setEditorSelection: (text: string) => void;

  // ── Panel Callbacks (for SprkChat props) ────────────────────────────────
  onInsertToEditor: (content: string) => void;
  streaming: StreamingCallbacks;
  streamingState: StreamingState;

  // ── Chat State ──────────────────────────────────────────────────────────
  chatSessionId: string | null;
  setChatSessionId: (sessionId: string) => void;
  playbookId: string | undefined;
  setPlaybookId: (playbookId: string) => void;
  chatHistory: IChatMessage[] | undefined;

  // ── Analysis Chat Context (from @spaarke/ai-context useChatContextMapping) ──
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
 * Must be rendered INSIDE AuthProvider AND only after `isAuthenticated === true`
 * (else useAuth() will return reject-on-call stand-ins for authenticatedFetch).
 */
export function AnalysisAiProvider({ children, bffBaseUrl }: AnalysisAiProviderProps): JSX.Element {
  const { isAuthenticated, authenticatedFetch, getAccessToken } = useAuth();
  const hostContext = getHostContext();

  // Analysis data loading
  const loader = useAnalysisLoader({
    analysisId: hostContext.analysisId,
    documentId: hostContext.documentId,
    isAuthenticated,
    authenticatedFetch,
  });

  // ── Chat session lifecycle (AIPU-050: delegated to @spaarke/ai-context) ──
  const chatSessionHook = useChatSession({
    bffBaseUrl,
    initialMessages: loader.analysis?.chatHistory,
  });

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
  const [chatSessionId, setChatSessionIdState] = useState<string | null>(() => {
    try {
      return sessionStorage.getItem(CHAT_SESSION_KEY);
    } catch {
      return null;
    }
  });

  const effectiveChatSessionId = chatSessionHook.session?.sessionId ?? chatSessionId;

  const setChatSessionId = useCallback((sessionId: string) => {
    setChatSessionIdState(sessionId);
    try {
      sessionStorage.setItem(CHAT_SESSION_KEY, sessionId);
    } catch {
      /* sessionStorage may be unavailable */
    }
  }, []);

  const setPlaybookId = useCallback((id: string) => {
    setPlaybookIdState(id);
    try {
      sessionStorage.setItem(PLAYBOOK_KEY, id);
    } catch {
      /* sessionStorage may be unavailable */
    }
  }, []);

  // Insert-to-editor callback (direct ref call, no BroadcastChannel)
  const onInsertToEditor = useCallback((content: string) => {
    editorRef.current?.insert(content);
  }, []);

  // ── Streaming state ──────────────────────────────────────────────────────
  const [streamingState, setStreamingState] = useState<StreamingState>({
    isStreaming: false,
    operationId: null,
    tokenCount: 0,
  });
  const tokenCountRef = useRef<number>(0);

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

      // Auth (function-based)
      isAuthenticated,
      authenticatedFetch,
      getAccessToken,

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

      // Chat state
      chatSessionId: effectiveChatSessionId,
      setChatSessionId,
      playbookId,
      setPlaybookId,
      chatHistory: loader.analysis?.chatHistory,

      // Analysis chat context mapping
      contextMapping,
    }),
    [
      loader,
      isAuthenticated,
      authenticatedFetch,
      getAccessToken,
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
