/**
 * @spaarke/ai-context — StandaloneAiContextValue interface
 *
 * The full context value exposed by StandaloneAiContext to child components.
 * Covers entity resolution, chat session state, playbook state, streaming,
 * and auth — everything SprkChat and output/source pane widgets need.
 *
 * Standards: ADR-012 (shared library — abstracted interfaces, not platform APIs)
 *
 * @see StandaloneAiContext.tsx — the provider that populates this value
 * @see useStandaloneAi.ts — the consumer hook
 * @see AnalysisAiContextValue — the analysis-scoped analogue (with analysis record + doc metadata)
 */

import type { EntityContext } from './entity-context';
import type { StreamingCallbacks, StreamingState } from './index';

// ---------------------------------------------------------------------------
// Session Storage Keys (exported for use by consumers writing to storage)
// ---------------------------------------------------------------------------

export const STANDALONE_SESSION_KEY_PREFIX = 'sprk_sai_';
export const STANDALONE_CHAT_SESSION_KEY = `${STANDALONE_SESSION_KEY_PREFIX}chatSessionId`;
export const STANDALONE_PLAYBOOK_KEY = `${STANDALONE_SESSION_KEY_PREFIX}playbookId`;

// ---------------------------------------------------------------------------
// Context Mapping Response (from BFF)
// ---------------------------------------------------------------------------

/**
 * Response from GET /api/ai/chat/context-mappings/standalone.
 * Maps the resolved entity to the appropriate playbook and initial chat state.
 */
export interface StandaloneContextMapping {
  /** Recommended playbook ID for the entity type and context */
  playbookId: string;
  /** Human-readable playbook name */
  playbookName?: string;
  /** Optional pre-seeded initial message for the chat */
  initialMessage?: string;
  /** Whether the entity has indexed knowledge (for scope routing) */
  hasEntityKnowledge: boolean;
}

// ---------------------------------------------------------------------------
// StandaloneAiContextValue — full provider value
// ---------------------------------------------------------------------------

/**
 * The full context value provided by StandaloneAiProvider to child components.
 *
 * Consumers receive this via the useStandaloneAi() hook:
 *   const { entityContext, chatSessionId, playbookId, streaming } = useStandaloneAi();
 */
export interface StandaloneAiContextValue {
  // ── Entity Context ───────────────────────────────────────────────────────
  /**
   * Resolved entity context (null while resolving or when no entity found).
   * Populated by useEntityResolver from URL params + Xrm frame-walk fallback.
   */
  entityContext: EntityContext | null;

  /** Whether entity resolution is in progress */
  isResolvingEntity: boolean;

  // ── Auth ─────────────────────────────────────────────────────────────────
  /** Bearer access token for BFF API calls (null when not authenticated) */
  token: string | null;
  /** Whether the user is currently authenticated */
  isAuthenticated: boolean;

  // ── BFF API ──────────────────────────────────────────────────────────────
  /** BFF API base URL (HOST only — use buildBffApiUrl() to build endpoint URLs) */
  bffBaseUrl: string;

  // ── Context Mapping (from BFF) ───────────────────────────────────────────
  /**
   * Loaded context mapping from BFF (null while loading or on error).
   * Contains the recommended playbookId for the entity.
   */
  contextMapping: StandaloneContextMapping | null;

  /** Whether context mapping is being fetched */
  isLoadingContextMapping: boolean;

  // ── Chat Session State ───────────────────────────────────────────────────
  /** Active chat session ID (null when no session is open), persisted to sessionStorage */
  chatSessionId: string | null;
  /** Set chat session ID (called when SprkChat creates a new session) */
  setChatSessionId: (sessionId: string) => void;

  // ── Playbook State ───────────────────────────────────────────────────────
  /** Active playbook ID governing session behavior, persisted to sessionStorage */
  playbookId: string | undefined;
  /** Set playbook ID (called on playbook switch) */
  setPlaybookId: (playbookId: string) => void;

  // ── Streaming ────────────────────────────────────────────────────────────
  /** Streaming callbacks for SSE token flow (registered by output pane or editor) */
  streaming: StreamingCallbacks;
  /** Current streaming state for UI indicators */
  streamingState: StreamingState;

  // ── Loading Aggregate ────────────────────────────────────────────────────
  /** True when any async operation is in flight (entity resolution OR context mapping) */
  isLoading: boolean;
}

// ---------------------------------------------------------------------------
// Provider Props
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react';

export interface StandaloneAiProviderProps {
  children: ReactNode;
  /** BFF API base URL (HOST only — resolved by resolveRuntimeConfig() in index.tsx) */
  bffBaseUrl: string;
  /** Access token for BFF API calls (from @spaarke/auth AuthProvider) */
  token: string | null;
  /** Whether auth is ready */
  isAuthenticated: boolean;
}
