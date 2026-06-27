/**
 * @spaarke/ai-widgets — WebSourceContextWidget
 *
 * R2 Context pane wrapper for the R1 WebSourceWidget.
 *
 * Adapts WebSourceWidget (SourceWidgetProps<WebSourceData>) to the
 * R2 ContextWidgetComponent contract (ContextWidgetProps<WebSourceData>).
 *
 * Citation highlight: no-op. Web source iframes are cross-origin embeds;
 * programmatic scroll to a citation passage is not supported.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import { createContextWidgetAdapter, createNoOpHighlighter } from './ContextWidgetAdapter';
import { WebSourceWidget } from '@spaarke/ai-outputs';
import type { WebSourceData } from '@spaarke/ai-outputs';

// Re-export the payload type for consumers.
export type { WebSourceData };

/**
 * WebSourceContextWidget — R2 ContextWidgetComponent wrapping WebSourceWidget.
 *
 * onHighlight is a safe no-op: web source iframes are cross-origin and cannot
 * be scrolled to a specific passage from outside the frame.
 */
const WebSourceContextWidget = createContextWidgetAdapter<WebSourceData>(
  WebSourceWidget,
  createNoOpHighlighter<WebSourceData>()
);

WebSourceContextWidget.displayName = 'WebSourceContextWidget';

export default WebSourceContextWidget;
