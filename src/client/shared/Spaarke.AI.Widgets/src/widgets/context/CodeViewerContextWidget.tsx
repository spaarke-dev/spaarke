/**
 * @spaarke/ai-widgets — CodeViewerContextWidget
 *
 * R2 Context pane wrapper for the R1 CodeViewerWidget.
 *
 * Adapts CodeViewerWidget (SourceWidgetProps<CodeViewerData>) to the
 * R2 ContextWidgetComponent contract (ContextWidgetProps<CodeViewerData>).
 *
 * Citation highlight: no-op. Code blocks are rendered as a single <pre>/<code>
 * element with line numbers; there is no citation-addressable passage to scroll
 * to. A future enhancement may map citationId to a line number range via
 * selectionRef (e.g. `"line:24-31"`) and highlight those lines.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-081
 */

import { createContextWidgetAdapter, createNoOpHighlighter } from './ContextWidgetAdapter';
import { CodeViewerWidget } from '@spaarke/ai-outputs';
import type { CodeViewerData } from '@spaarke/ai-outputs';

// Re-export the payload type for consumers.
export type { CodeViewerData };

/**
 * CodeViewerContextWidget — R2 ContextWidgetComponent wrapping CodeViewerWidget.
 *
 * onHighlight is a safe no-op: code blocks do not have citation-addressable
 * sub-regions in the current R1 implementation.
 */
const CodeViewerContextWidget = createContextWidgetAdapter<CodeViewerData>(
  CodeViewerWidget,
  createNoOpHighlighter<CodeViewerData>(),
);

CodeViewerContextWidget.displayName = 'CodeViewerContextWidget';

export default CodeViewerContextWidget;
