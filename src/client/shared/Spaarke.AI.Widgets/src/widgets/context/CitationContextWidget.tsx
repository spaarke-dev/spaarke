/**
 * @spaarke/ai-widgets — CitationContextWidget
 *
 * R2 Context pane wrapper for the R1 CitationWidget.
 *
 * Adapts CitationWidget (SourceWidgetProps<CitationData>) to the
 * R2 ContextWidgetComponent contract (ContextWidgetProps<CitationData>)
 * and exposes citation highlighting via ContextWidgetAdapter.
 *
 * Citation highlight behaviour:
 * - The R1 CitationWidget renders <li> elements without data-citation-id
 *   attributes. This wrapper renders a thin overlay layer: each citation item
 *   in the scroll container receives a `data-citation-id` attribute via a
 *   DOM mutation after mount, keyed on the citation's `id` field.
 * - On highlight: scrolls the matching item into view with a transient ring.
 * - Uses a custom HighlighterFactory that reads the data payload to map
 *   citation id → rendered DOM node via index position (since the list order
 *   matches data.citations order).
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import React, { forwardRef, useEffect, useImperativeHandle, useRef } from 'react';
import { CitationWidget } from '@spaarke/ai-outputs';
import type { CitationData } from '@spaarke/ai-outputs';
import type { ContextWidgetProps } from '../../types/widget-types';
import type { ContextWidgetHighlightHandle } from './ContextWidgetAdapter';

// Re-export the payload type for consumers.
export type { CitationData };

// ---------------------------------------------------------------------------
// CitationContextWidget
// ---------------------------------------------------------------------------

/**
 * CitationContextWidget — R2 ContextWidgetComponent wrapping CitationWidget.
 *
 * This widget does NOT use createContextWidgetAdapter() because it needs to
 * annotate DOM nodes with data-citation-id attributes after each render to
 * enable the citation highlight scroll logic.
 *
 * Satisfies `ContextWidgetComponent<CitationData>` and exposes
 * `ContextWidgetHighlightHandle` via its ref.
 */
const CitationContextWidget = forwardRef<
  ContextWidgetHighlightHandle,
  ContextWidgetProps<CitationData>
>(function CitationContextWidget(props, ref) {
  const { data, widgetType: _widgetType, isLoading, error, className } = props;

  const containerRef = useRef<HTMLDivElement>(null);

  // Keep a stable reference to the latest data without triggering extra effects.
  const dataRef = useRef<CitationData | undefined>(data as CitationData | undefined);
  dataRef.current = data as CitationData | undefined;

  // After each render, annotate <li> elements with data-citation-id attributes.
  // CitationWidget renders an <ol> whose children are <li> elements in the same
  // order as data.citations. We match by index.
  useEffect(() => {
    const container = containerRef.current;
    const citations = dataRef.current?.citations;
    if (!container || !citations || citations.length === 0) return;

    const listItems = container.querySelectorAll<HTMLLIElement>('li');
    listItems.forEach((li, index) => {
      const citation = citations[index];
      if (citation) {
        li.setAttribute('data-citation-id', citation.id);
      }
    });
  });

  // Expose highlight handle.
  useImperativeHandle(
    ref,
    () => ({
      onHighlight(citationId: string): void {
        const container = containerRef.current;
        if (!container) return;

        const target = container.querySelector<HTMLElement>(
          `[data-citation-id="${CSS.escape(citationId)}"]`,
        );

        if (!target) {
          console.debug(
            `[ai-widgets] CitationContextWidget.onHighlight: citation "${citationId}" not found in rendered list.`,
          );
          return;
        }

        target.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        target.classList.add('context-citation-highlight');
        setTimeout(() => target.classList.remove('context-citation-highlight'), 2000);
      },
    }),
    [],
  );

  return (
    <div ref={containerRef} style={{ height: '100%', overflow: 'hidden' }}>
      <CitationWidget
        data={data as CitationData}
        isLoading={isLoading}
        error={error}
        className={className}
      />
    </div>
  );
});

CitationContextWidget.displayName = 'CitationContextWidget';

export default CitationContextWidget;
