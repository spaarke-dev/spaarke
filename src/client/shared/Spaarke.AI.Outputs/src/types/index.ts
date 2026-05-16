/**
 * @spaarke/ai-outputs — Type Definitions
 *
 * Core widget type definitions for the AI output and source pane registries.
 * NOT PCF-safe — requires React 19.
 */

// Widget identity enums, generic prop types, and registry entry shapes (task 020).
export * from "./widget-types";

// Import enums locally so this file's interface bodies can reference them
import type { OutputWidgetType, SourceWidgetType } from "./widget-types";

import type React from "react";

// ---------------------------------------------------------------------------
// Widget component contracts (pane-level orchestration props)
// ---------------------------------------------------------------------------

/**
 * Pane-level base props for output widget orchestration (expand/collapse).
 * These are separate from OutputWidgetProps<T> (data delivery props) in
 * widget-types.ts — the orchestration layer uses these to manage layout.
 */
export interface OutputWidgetPaneProps {
  /** Unique identifier for this widget instance within the pane. */
  widgetId: string;
  /** Whether the widget is currently in an expanded/focused state. */
  isExpanded?: boolean;
  /** Callback fired when the user requests widget expansion. */
  onExpand?: (widgetId: string) => void;
  /** Callback fired when the user requests widget collapse. */
  onCollapse?: (widgetId: string) => void;
}

/**
 * Pane-level base props for source widget orchestration.
 */
export interface SourceWidgetPaneProps {
  /** Unique identifier for this widget instance within the pane. */
  widgetId: string;
  /** The source document/item identifier this widget represents. */
  sourceRef: string;
  /** Callback fired when the user selects a range or item in this source. */
  onSourceSelect?: (widgetId: string, selectionRef: string) => void;
}

// ---------------------------------------------------------------------------
// Registry entry types (pane orchestration layer)
// ---------------------------------------------------------------------------

/**
 * Registry entry for an output pane widget (pane orchestration layer).
 * The registry stores metadata + a lazy factory for the React component.
 */
export interface OutputWidgetRegistryEntry {
  /** The widget type identifier (used as the registry key). */
  type: OutputWidgetType;
  /** Human-readable display name for the widget. */
  displayName: string;
  /**
   * Lazy factory returning the widget component.
   * Use dynamic import() to enable code-splitting.
   *
   * @example
   * componentFactory: () => import('./output-widgets/SummaryWidget').then(m => m.SummaryWidget)
   */
  componentFactory: () => Promise<React.ComponentType<OutputWidgetPaneProps>>;
  /** Whether this widget can appear multiple times in the output pane. */
  allowMultiple?: boolean;
  /** Default render order (lower = higher priority in pane). */
  defaultOrder?: number;
}

/**
 * Registry entry for a source pane widget (pane orchestration layer).
 */
export interface SourceWidgetRegistryEntry {
  /** The widget type identifier (used as the registry key). */
  type: SourceWidgetType;
  /** Human-readable display name for the widget. */
  displayName: string;
  /**
   * Lazy factory returning the widget component.
   *
   * @example
   * componentFactory: () => import('./source-widgets/DocumentSourceWidget').then(m => m.DocumentSourceWidget)
   */
  componentFactory: () => Promise<React.ComponentType<SourceWidgetPaneProps>>;
  /** MIME types or source kinds this widget can handle. */
  supportedSourceKinds?: string[];
}

// ---------------------------------------------------------------------------
// Registry map types
// ---------------------------------------------------------------------------

/** Typed map of all registered output widgets. */
export type OutputWidgetRegistryMap = Map<OutputWidgetType, OutputWidgetRegistryEntry>;

/** Typed map of all registered source widgets. */
export type SourceWidgetRegistryMap = Map<SourceWidgetType, SourceWidgetRegistryEntry>;

// ---------------------------------------------------------------------------
// Cross-pane linking
// ---------------------------------------------------------------------------

/**
 * Represents a link between an output widget item and a source widget range.
 * Used by the cross-pane linking system (Wave 3, task 031).
 */
export interface CrossPaneLink {
  /** ID of the output widget that initiated the link. */
  outputWidgetId: string;
  /** Reference to the specific item within the output widget. */
  outputItemRef: string;
  /** ID of the source widget that is linked. */
  sourceWidgetId: string;
  /** Reference to the specific range/item in the source widget. */
  sourceSelectionRef: string;
}

// ---------------------------------------------------------------------------
// Chat history
// ---------------------------------------------------------------------------

/**
 * A single message in the AI chat history.
 * Populated by Wave 3 task 032.
 */
export interface ChatMessage {
  /** Unique message identifier. */
  id: string;
  /** Message role: user input or AI assistant response. */
  role: "user" | "assistant";
  /** Message content (may include markdown). */
  content: string;
  /** ISO 8601 timestamp when the message was created. */
  createdAt: string;
}

/**
 * Chat session data shape (BFF thread + messages).
 * Renamed from ChatSession to ChatSessionData (Wave 3, task 032) to avoid
 * conflict with the richer ChatSession UI type in chat-history/ChatHistoryPanel.types.ts.
 */
export interface ChatSessionData {
  /** Unique session identifier (maps to AI thread ID). */
  sessionId: string;
  /** Ordered list of messages in this session. */
  messages: ChatMessage[];
  /** ISO 8601 timestamp when the session was created. */
  createdAt: string;
  /** ISO 8601 timestamp of the last message. */
  lastMessageAt: string;
}

// ---------------------------------------------------------------------------
// SSE event payload stubs
// ---------------------------------------------------------------------------

/**
 * Base shape for SSE events directed at the output pane.
 * Refined in Wave 3 task 030.
 */
export interface OutputPaneEvent {
  event: "output_pane";
  widgetType: OutputWidgetType;
  payload: unknown;
}

/**
 * Base shape for SSE events directed at the source pane.
 * Refined in Wave 3 task 030.
 */
export interface SourcePaneEvent {
  event: "source_pane";
  widgetType: SourceWidgetType;
  payload: unknown;
}

/**
 * Base shape for SSE source highlight events.
 * Refined in Wave 3 task 030.
 */
export interface SourceHighlightEvent {
  event: "source_highlight";
  sourceRef: string;
  selectionRef: string;
}

/** Union of all output-pane-related SSE event types. */
export type AiOutputSseEvent = OutputPaneEvent | SourcePaneEvent | SourceHighlightEvent;
