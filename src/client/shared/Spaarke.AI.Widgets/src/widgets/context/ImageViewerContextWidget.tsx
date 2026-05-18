/**
 * @spaarke/ai-widgets — ImageViewerContextWidget
 *
 * R2 Context pane wrapper for the R1 ImageViewerWidget.
 *
 * Adapts ImageViewerWidget (SourceWidgetProps<ImageViewerData>) to the
 * R2 ContextWidgetComponent contract (ContextWidgetProps<ImageViewerData>).
 *
 * Citation highlight: no-op. Images are rendered as a single <img> element;
 * there is no sub-image region to scroll to for a given citationId.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import { createContextWidgetAdapter, createNoOpHighlighter } from './ContextWidgetAdapter';
import { ImageViewerWidget } from '@spaarke/ai-outputs';
import type { ImageViewerData } from '@spaarke/ai-outputs';

// Re-export the payload type for consumers.
export type { ImageViewerData };

/**
 * ImageViewerContextWidget — R2 ContextWidgetComponent wrapping ImageViewerWidget.
 *
 * onHighlight is a safe no-op: images do not have addressable sub-regions
 * that can be scrolled to via a citation identifier.
 */
const ImageViewerContextWidget = createContextWidgetAdapter<ImageViewerData>(
  ImageViewerWidget,
  createNoOpHighlighter<ImageViewerData>(),
);

ImageViewerContextWidget.displayName = 'ImageViewerContextWidget';

export default ImageViewerContextWidget;
