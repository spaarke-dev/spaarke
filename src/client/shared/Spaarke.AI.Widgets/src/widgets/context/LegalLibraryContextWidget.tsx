/**
 * @spaarke/ai-widgets — LegalLibraryContextWidget
 *
 * R2 Context pane wrapper for the R1 LegalLibraryWidget.
 *
 * Adapts LegalLibraryWidget (SourceWidgetProps<LegalLibraryData>) to the
 * R2 ContextWidgetComponent contract (ContextWidgetProps<LegalLibraryData>)
 * and exposes citation highlighting via ContextWidgetAdapter.
 *
 * Citation highlight behaviour:
 * - Scrolls the excerpt blockquote into view and applies a transient highlight ring.
 * - The widget renders a single case/statute card, so citation scroll targets
 *   the excerpt as the most relevant passage in the card.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import { createContextWidgetAdapter, createLegalLibraryHighlighter } from './ContextWidgetAdapter';
import { LegalLibraryWidget } from '@spaarke/ai-outputs';
import type { LegalLibraryData } from '@spaarke/ai-outputs';

// Re-export the payload type for consumers.
export type { LegalLibraryData };

/**
 * LegalLibraryContextWidget — R2 ContextWidgetComponent wrapping LegalLibraryWidget.
 *
 * Satisfies `ContextWidgetComponent<LegalLibraryData>` and exposes
 * `ContextWidgetHighlightHandle` via its ref, scrolling the excerpt into view
 * on citation highlight.
 */
const LegalLibraryContextWidget = createContextWidgetAdapter<LegalLibraryData>(
  LegalLibraryWidget,
  createLegalLibraryHighlighter<LegalLibraryData>()
);

LegalLibraryContextWidget.displayName = 'LegalLibraryContextWidget';

export default LegalLibraryContextWidget;
