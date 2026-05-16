/**
 * ChatHistoryPanel.types.ts
 *
 * Local type definitions for the chat history panel component.
 *
 * NOTE: ChatSession defined here intentionally mirrors the shape returned by
 * useChatSession in @spaarke/ai-context but is NOT imported from there.
 * @spaarke/ai-outputs must NOT depend on @spaarke/ai-context to avoid
 * circular dependencies. See ADR-012.
 *
 * @see ADR-012 — Shared Component Library
 * @see ADR-021 — Fluent UI v9 design system
 */

// ---------------------------------------------------------------------------
// ChatSession — UI-focused session shape for the history panel
// ---------------------------------------------------------------------------

/**
 * A single AI chat session as displayed in the history panel.
 *
 * This shape mirrors IChatSession from @spaarke/ai-context but is defined
 * locally to avoid a circular package dependency. If the BFF API changes the
 * session contract, both shapes must be updated independently.
 */
export interface ChatSession {
  /** Unique session identifier (maps to the AI thread / BFF session ID). */
  id: string;
  /** Human-readable session title (AI-generated or user-set). */
  title: string;
  /** Short preview of the last message in the session (may be truncated). */
  lastMessagePreview?: string;
  /** ISO 8601 timestamp of the most recent message or context switch. */
  updatedAt: string;
  /** Entity type label, e.g. "Matter", "Project", "Document". */
  entityType?: string;
  /** Entity display name, e.g. "Acme Corp v Smith". */
  entityName?: string;
  /** Entity record identifier in Dataverse (GUID). */
  entityId?: string;
}

// ---------------------------------------------------------------------------
// ChatHistoryPanelProps
// ---------------------------------------------------------------------------

/**
 * Props for the ChatHistoryPanel root component.
 *
 * Data-fetching is the caller's responsibility (via useChatSession from
 * @spaarke/ai-context). This component is purely presentational with respect
 * to data — it receives sessions as a prop.
 */
export interface ChatHistoryPanelProps {
  /** Ordered list of prior chat sessions to display. */
  sessions: ChatSession[];
  /** When true, renders a centered Spinner in place of the session list. */
  isLoading?: boolean;
  /**
   * Called when the user clicks "Resume" on a session card.
   * If not provided, the Resume button is hidden on all cards.
   */
  onResume?: (sessionId: string) => void;
  /**
   * Called when the user clicks "Delete" on a session card.
   * If not provided, the Delete button is hidden on all cards.
   */
  onDelete?: (sessionId: string) => void;
  /** Optional additional CSS class name applied to the panel root element. */
  className?: string;
}

// ---------------------------------------------------------------------------
// ChatSessionCardProps
// ---------------------------------------------------------------------------

/**
 * Props for a single session card rendered inside ChatHistoryPanel.
 */
export interface ChatSessionCardProps {
  /** The session to display in this card. */
  session: ChatSession;
  /**
   * Called when the user clicks "Resume".
   * If not provided, the Resume button is not rendered.
   */
  onResume?: (sessionId: string) => void;
  /**
   * Called when the user clicks "Delete".
   * If not provided, the Delete button is not rendered.
   */
  onDelete?: (sessionId: string) => void;
  /** Whether this session is the currently active session. */
  isActive?: boolean;
}
