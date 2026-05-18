/**
 * @spaarke/ai-widgets — Widget Interface Contracts
 *
 * WorkspaceWidget and ContextWidget prop interfaces, metadata types,
 * and shared widget contracts for the three-pane shell.
 *
 * React 19, NOT PCF-safe.
 *
 * Populated by task AIPU2-071 (interface definitions) and extended by
 * AIPU2-072/073 (registry types).
 */

import type React from 'react';

// ---------------------------------------------------------------------------
// WorkspaceWidget contracts
// ---------------------------------------------------------------------------

/**
 * Base props for all workspace pane widget components.
 *
 * Workspace widgets are AI-directed: the server sends a widget type string
 * and a typed data payload. The WorkspaceWidgetRegistry resolves the correct
 * component and the WorkspacePane renders it.
 *
 * @template T - The shape of the widget's data payload.
 */
export interface WorkspaceWidgetProps<T = unknown> {
  /** Parsed widget payload delivered via the AI streaming response. */
  data: T;
  /** Widget type string as sent by the server (for debugging and sub-routing). */
  widgetType: string;
  /** When true the widget should render a loading skeleton instead of data. */
  isLoading?: boolean;
  /** Human-readable error message — widget renders an error state when set. */
  error?: string;
  /** Optional class name for root element overrides (mergeClasses compatible). */
  className?: string;
}

/**
 * A workspace widget component type.
 * All workspace widget default exports must satisfy this signature.
 */
export type WorkspaceWidgetComponent<T = unknown> = React.ComponentType<WorkspaceWidgetProps<T>>;

/**
 * Metadata attached to each workspace widget registration.
 * Consumed by widget pickers, debug UI, and the WorkspacePane tab bar.
 */
export interface WidgetMetadata {
  /** Human-readable display name shown in the workspace tab bar and debug UI. */
  displayName: string;
  /**
   * Widget category used for grouping in widget pickers.
   * Examples: "document", "analysis", "data", "comparison"
   */
  category: string;
  /**
   * Optional default display order within the workspace pane tab bar.
   * Lower numbers appear first. Widgets without an order are appended last.
   */
  defaultOrder?: number;
  /**
   * When true, multiple instances of this widget may coexist in the workspace.
   * Defaults to false.
   */
  allowMultiple?: boolean;
}

// ---------------------------------------------------------------------------
// ContextWidget contracts
// ---------------------------------------------------------------------------

/**
 * Base props for all context pane widget components.
 *
 * Context widgets display supplementary information alongside the conversation.
 * They are always server-driven: unknown types return null rather than a
 * fallback widget, because unknown types indicate a version mismatch.
 *
 * @template T - The shape of the context widget's data payload.
 */
export interface ContextWidgetProps<T = unknown> {
  /** Parsed widget payload delivered via the AI streaming response. */
  data: T;
  /** Context widget type string as sent by the server. */
  widgetType: string;
  /** When true the widget should render a loading skeleton instead of data. */
  isLoading?: boolean;
  /** Human-readable error message — widget renders an error state when set. */
  error?: string;
  /** Optional class name for root element overrides (mergeClasses compatible). */
  className?: string;
}

/**
 * A context widget component type.
 * All context widget default exports must satisfy this signature.
 */
export type ContextWidgetComponent<T = unknown> = React.ComponentType<ContextWidgetProps<T>>;

// ---------------------------------------------------------------------------
// Interface-based widget contracts (task AIPU2-071)
//
// These are the typed interface contracts that widget implementations satisfy.
// Separate from the component-props types above (which are used by the
// React component layer). Both sets of types are exported from @spaarke/ai-widgets.
// ---------------------------------------------------------------------------

export type {
  // Shared supporting types
  WidgetRenderContext,
  Selection,
  ActionResult,
  WidgetState,
  WidgetRegistryEntry,
} from './shared';

// Re-export WidgetMetadata from shared.ts (the richer interface version).
// NOTE: The simpler WidgetMetadata interface declared above in this file
// is the component-layer version. The shared.ts version adds `icon`,
// makes `allowMultiple` and `defaultOrder` required, and is used by the
// registry layer. Consumers should import from `@spaarke/ai-widgets` and
// use whichever fields they need — both are structurally compatible for
// the subset of fields they share.
export type { WidgetMetadata as WidgetRegistryMetadata } from './shared';

// WorkspaceWidget<TData, TActions> interface + WidgetActionDescriptor
export type { WorkspaceWidget, WidgetActionDescriptor } from './WorkspaceWidget';

// ContextWidget<TData> interface
export type { ContextWidget } from './ContextWidget';
