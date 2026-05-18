/**
 * @spaarke/ai-widgets — ContextWidgetAdapter
 *
 * Bridges the R1 SourceWidget prop API (SourceWidgetProps<T>) to the R2
 * ContextWidget prop API (ContextWidgetProps<T>).
 *
 * R1 source widgets are plain React functional components that receive:
 *   { data, isLoading, error, className }
 *
 * R2 ContextWidgetComponent components receive:
 *   { data, widgetType, isLoading, error, className }
 *
 * The extra `widgetType` field in R2 is the only structural difference in
 * props — this adapter handles the projection. Additionally, for widgets that
 * support citation highlighting (DocumentViewer, LegalLibrary, Citation), the
 * adapter exposes an `onHighlight` callback via an imperative ref so the
 * ContextPaneController can call it when a `context_highlight` event fires.
 *
 * Usage:
 *   const DocumentViewerContextWidget = createContextWidgetAdapter(
 *     DocumentViewerWidget,
 *     createDocumentViewerHighlighter,
 *   );
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import React, { forwardRef, useImperativeHandle, useRef } from 'react';
import type { ContextWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Highlight handle
// ---------------------------------------------------------------------------

/**
 * Imperative handle exposed by context widget adapters that support
 * citation highlighting. The ContextPaneController resolves this via a ref
 * and calls `onHighlight` when a `context_highlight` pane event is received.
 */
export interface ContextWidgetHighlightHandle {
  /**
   * Highlight the citation or source passage identified by `citationId`.
   *
   * @param citationId   - Matches the citationId field from the pane event.
   * @param selectionRef - Optional sub-range within the source (e.g. char offset).
   */
  onHighlight(citationId: string, selectionRef?: string): void;
}

// ---------------------------------------------------------------------------
// Highlighter factory type
// ---------------------------------------------------------------------------

/**
 * A factory function that receives the DOM container ref and returns an
 * `onHighlight` implementation for a specific source widget type.
 *
 * The factory is called once per mounted widget instance. It receives a
 * React ref pointing to the widget's root DOM element so the highlight
 * logic can query and scroll child elements.
 *
 * Returning `undefined` produces a no-op highlight handle (for widgets
 * that do not support citation highlighting).
 */
export type HighlighterFactory<T = unknown> = (
  containerRef: React.RefObject<HTMLDivElement | null>,
  getLatestData: () => T | undefined,
) => ((citationId: string, selectionRef?: string) => void) | undefined;

// ---------------------------------------------------------------------------
// Props for the adapted component
// ---------------------------------------------------------------------------

/**
 * Props accepted by the inner R1-style source widget component.
 * Matches SourceWidgetProps<T> from @spaarke/ai-outputs.
 */
interface SourceWidgetCompatProps<T> {
  data: T;
  isLoading?: boolean;
  error?: string;
  className?: string;
}

// ---------------------------------------------------------------------------
// createContextWidgetAdapter
// ---------------------------------------------------------------------------

/**
 * Wrap an R1 source widget component in the R2 ContextWidget prop contract.
 *
 * - Projects `ContextWidgetProps<T>` → `SourceWidgetProps<T>` (drops `widgetType`).
 * - Exposes a `ContextWidgetHighlightHandle` ref for citation highlighting.
 * - Wraps the inner component in a `<div>` so the adapter owns a DOM root
 *   that the highlighter factory can query.
 *
 * @param InnerWidget       - The R1 source widget functional component.
 * @param highlighterFactory - Optional factory that produces an `onHighlight`
 *                             implementation. Pass `undefined` for no-op widgets.
 * @returns A `React.ForwardRefExoticComponent` satisfying `ContextWidgetComponent<T>`
 *          that also exposes `ContextWidgetHighlightHandle` via its ref.
 */
export function createContextWidgetAdapter<T = unknown>(
  InnerWidget: React.ComponentType<SourceWidgetCompatProps<T>>,
  highlighterFactory?: HighlighterFactory<T>,
): React.ForwardRefExoticComponent<
  ContextWidgetProps<T> & React.RefAttributes<ContextWidgetHighlightHandle>
> {
  const AdaptedWidget = forwardRef<ContextWidgetHighlightHandle, ContextWidgetProps<T>>(
    function AdaptedWidget(props, ref) {
      const { data, widgetType: _widgetType, isLoading, error, className } = props;

      // Root DOM element ref for the highlighter factory.
      const containerRef = useRef<HTMLDivElement>(null);

      // Keep a stable getter for the latest data without re-creating the
      // highlight closure on every render.
      const dataRef = useRef<T | undefined>(data as T | undefined);
      dataRef.current = data as T | undefined;

      // Build the onHighlight implementation once (via factory) and expose it.
      // Use a null sentinel to distinguish "not yet initialized" from "no-op factory".
      const highlighterRef = useRef<
        ((citationId: string, selectionRef?: string) => void) | null | undefined
      >(null); // null = uninitialized sentinel

      // Initialise highlighter on first render (factory captures containerRef).
      // null  → not yet initialized; undefined → factory returned no-op.
      if (highlighterRef.current === null) {
        highlighterRef.current = highlighterFactory
          ? (highlighterFactory(containerRef, () => dataRef.current) ?? undefined)
          : undefined;
      }

      useImperativeHandle(
        ref,
        () => ({
          onHighlight(citationId: string, selectionRef?: string): void {
            highlighterRef.current?.(citationId, selectionRef);
          },
        }),
        [],
      );

      return (
        <div ref={containerRef} style={{ height: '100%', overflow: 'hidden' }}>
          <InnerWidget
            data={data as T}
            isLoading={isLoading}
            error={error}
            className={className}
          />
        </div>
      );
    },
  );

  AdaptedWidget.displayName = `ContextWidgetAdapter(${InnerWidget.displayName ?? InnerWidget.name ?? 'Unknown'})`;

  return AdaptedWidget;
}

// ---------------------------------------------------------------------------
// Built-in highlighter factories
// ---------------------------------------------------------------------------

/**
 * Highlighter factory for the DocumentViewerWidget.
 *
 * The DocumentViewer renders either a PDF <object> or an <iframe>. Both are
 * cross-origin embeds — the adapter cannot reach into their DOM. Instead we
 * use a data-citation-id attribute on any supplementary in-page overlay
 * elements. If none exist (most cases), we scroll the container to the top
 * and emit a console.debug so developers can hook custom logic.
 *
 * In practice: DocumentViewer citation highlight signals the outer pane to
 * surface the relevant page/range. A future enhancement may pass the
 * selectionRef to the embedded viewer via the URL hash or postMessage.
 */
export function createDocumentViewerHighlighter<T>(): HighlighterFactory<T> {
  return (containerRef) => (citationId: string, selectionRef?: string) => {
    const container = containerRef.current;
    if (!container) return;

    // Attempt to scroll to an element annotated with the citationId.
    const target = container.querySelector<HTMLElement>(
      `[data-citation-id="${CSS.escape(citationId)}"]`,
    );
    if (target) {
      target.scrollIntoView({ behavior: 'smooth', block: 'center' });
      target.classList.add('context-citation-highlight');
      setTimeout(() => target.classList.remove('context-citation-highlight'), 2000);
      return;
    }

    // Embedded viewer (iframe/object): emit a debug trace.
    console.debug(
      `[ai-widgets] DocumentViewerWidget.onHighlight: citation "${citationId}"` +
        (selectionRef ? ` @ ${selectionRef}` : '') +
        ' — embedded viewer does not support programmatic scroll; ' +
        'pass selectionRef via URL hash or postMessage to the embed.',
    );
  };
}

/**
 * Highlighter factory for the CitationWidget.
 *
 * The CitationWidget renders a numbered <ol> where each <li> carries a
 * `data-citation-id` attribute matching the citation's `id` field.
 * Scrolls the matching item into view and applies a transient highlight ring.
 */
export function createCitationHighlighter<T>(): HighlighterFactory<T> {
  return (containerRef) => (citationId: string) => {
    const container = containerRef.current;
    if (!container) return;

    const target = container.querySelector<HTMLElement>(
      `[data-citation-id="${CSS.escape(citationId)}"]`,
    );
    if (!target) {
      console.debug(
        `[ai-widgets] CitationWidget.onHighlight: citation "${citationId}" not found in rendered list.`,
      );
      return;
    }

    target.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    target.classList.add('context-citation-highlight');
    setTimeout(() => target.classList.remove('context-citation-highlight'), 2000);
  };
}

/**
 * Highlighter factory for the LegalLibraryWidget.
 *
 * LegalLibraryWidget renders a single case/statute card. When a citation
 * highlight arrives we scroll the excerpt blockquote into view and apply a
 * transient highlight ring (the selectionRef is informational — there is no
 * sub-paragraph navigation in a single-card widget).
 */
export function createLegalLibraryHighlighter<T>(): HighlighterFactory<T> {
  return (containerRef) => (citationId: string, selectionRef?: string) => {
    const container = containerRef.current;
    if (!container) return;

    // Scroll the excerpt into view as the primary highlight target.
    const excerpt = container.querySelector<HTMLElement>('blockquote');
    if (excerpt) {
      excerpt.scrollIntoView({ behavior: 'smooth', block: 'center' });
      excerpt.classList.add('context-citation-highlight');
      setTimeout(() => excerpt.classList.remove('context-citation-highlight'), 2000);
    }

    console.debug(
      `[ai-widgets] LegalLibraryWidget.onHighlight: citation "${citationId}"` +
        (selectionRef ? ` @ ${selectionRef}` : ''),
    );
  };
}

/**
 * No-op highlighter for widgets that do not support citation highlighting
 * (WebSource, ImageViewer, CodeViewer). Returns undefined so the adapter
 * exposes a handle that silently ignores highlight calls.
 */
export function createNoOpHighlighter<T>(): HighlighterFactory<T> {
  return () => undefined;
}
