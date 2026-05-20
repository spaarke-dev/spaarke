/**
 * @spaarke/ai-widgets — ContextWidget interface
 *
 * Defines the contract that every Context pane widget MUST implement.
 * The Context pane is simpler than the Workspace pane: it has no tabs,
 * no user-triggered actions, and no state serialization requirement.
 * Its primary responsibility is presenting sources, citations, and
 * supplementary metadata that contextualizes the active Workspace widget.
 *
 * Generic parameter:
 * - TData — the shape of data the widget renders (e.g. CitationList).
 *            Defaults to `unknown` for registry storage.
 *
 * Task: AIPU2-071
 * FR:   FR-201, FR-204
 */

import type React from 'react';
import type { WidgetRenderContext } from './shared';

/**
 * Contract for every widget rendered inside the Context pane.
 *
 * Context widgets are simpler than WorkspaceWidgets: they render supporting
 * information (citations, entity details, progress, related items) alongside
 * the active Workspace tab. They do not support user actions or state
 * serialization.
 *
 * The shell passes `isLoading: true` during the initial data fetch and
 * whenever the active Workspace tab changes, giving Context widgets time to
 * load the relevant supplementary data.
 *
 * @template TData - Shape of data delivered to `render()`. Defaults to `unknown`.
 *
 * @example
 * ```ts
 * class CitationListWidget implements ContextWidget<CitationList> {
 *   render(data, context) {
 *     if (isLoading) return <Spinner />;
 *     return <CitationPanel citations={data.citations} />;
 *   }
 *   onHighlight(citationId, selectionRef) {
 *     // scroll cited passage into view
 *   }
 * }
 * ```
 */
export interface ContextWidget<TData = unknown> {
  // -------------------------------------------------------------------------
  // Display
  // -------------------------------------------------------------------------

  /**
   * Produce the React element tree for this Context pane widget.
   *
   * When `isLoading` is `true`, implementations SHOULD render a Fluent UI
   * `Spinner` or skeleton placeholder. The shell passes `isLoading: true`
   * while fetching supplementary data after a Workspace tab switch.
   *
   * @param data      - The widget's data payload. May be the zero-value of
   *                    `TData` while loading.
   * @param context   - Ambient session and entity context from the shell.
   * @returns A React element — MUST NOT return `null`.
   */
  render(data: TData, context: WidgetRenderContext): React.ReactElement;

  // -------------------------------------------------------------------------
  // Cross-pane communication
  // -------------------------------------------------------------------------

  /**
   * Called by the shell to highlight a specific citation or source passage
   * within this Context pane widget.
   *
   * Implement to scroll the cited passage into view and apply a highlight
   * ring. Triggered when the user clicks a citation in the Workspace pane.
   *
   * This method is optional. Widgets that do not render citations (e.g. the
   * entity info panel) may omit it.
   *
   * @param citationId   - Identifier of the citation to highlight.
   *                       Matches the `citationId` field in the Workspace
   *                       widget's rendered citation references.
   * @param selectionRef - Optional sub-range within the cited source
   *                       (e.g. a paragraph ID or character offset pair).
   *                       Omitted when the citation targets the entire source.
   */
  onHighlight?: (citationId: string, selectionRef?: string) => void;
}
