/**
 * @spaarke/ai-widgets — DocumentViewerContextWidget
 *
 * R2 Context pane wrapper for the R1 DocumentViewerWidget.
 *
 * Adapts DocumentViewerWidget (SourceWidgetProps<DocumentViewerData>) to the
 * R2 ContextWidgetComponent contract (ContextWidgetProps<DocumentViewerData>)
 * and exposes citation highlighting via ContextWidgetAdapter.
 *
 * Citation highlight behaviour:
 * - Queries the container for `[data-citation-id]` elements (future overlay).
 * - Falls back to a console.debug trace for embedded PDF/iframe viewers that
 *   cannot be scrolled programmatically from outside the embed origin.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import { createContextWidgetAdapter, createDocumentViewerHighlighter } from './ContextWidgetAdapter';
import { DocumentViewerWidget } from '@spaarke/ai-outputs';
import type { DocumentViewerData } from '@spaarke/ai-outputs';

// Re-export the payload type for consumers.
export type { DocumentViewerData };

/**
 * DocumentViewerContextWidget — R2 ContextWidgetComponent wrapping DocumentViewerWidget.
 *
 * Satisfies `ContextWidgetComponent<DocumentViewerData>` and exposes
 * `ContextWidgetHighlightHandle` via its ref for citation scroll support.
 */
const DocumentViewerContextWidget = createContextWidgetAdapter<DocumentViewerData>(
  DocumentViewerWidget,
  createDocumentViewerHighlighter<DocumentViewerData>(),
);

DocumentViewerContextWidget.displayName = 'DocumentViewerContextWidget';

export default DocumentViewerContextWidget;
