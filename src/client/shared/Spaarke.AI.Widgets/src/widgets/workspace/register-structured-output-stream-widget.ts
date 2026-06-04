/**
 * register-structured-output-stream-widget.ts
 *
 * Registers `StructuredOutputStreamWidget` under the `'structured-output-stream'`
 * workspace widget type key. Imported as a side effect from the package barrel
 * so the widget is available before any shell mounts.
 *
 * Pattern reference: mirrors `register-document-viewer-widget.ts` (R4 task 042
 * canonical) and `register-search-criteria-result-widget.ts` (R4 task 043).
 * Keeping each newly-added widget in its own register-*.ts file keeps the
 * long-form `register-workspace-widgets.ts` barrel stable + makes the widget's
 * registration reversible by deleting one file.
 *
 * Created in R5 task 017 (D2-07) — Workspace widget that renders structured AI
 * output progressively (Summarize streaming, FR-02) AND statically (Insights
 * playbook envelope, FR-13). The two use-cases differ only in the schema
 * passed via `widgetData` — risk UR-02 mitigation.
 *
 * ADR-012 (shared lib), ADR-022 (React 19), ADR-030 (additive event types).
 */

import { registerWorkspaceWidget } from '../../registry/WorkspaceWidgetRegistry';

/**
 * The widget type ID under which StructuredOutputStreamWidget is registered.
 * Exported so dispatchers (e.g. ConversationPane in SpaarkeAi, task 020 /
 * D2-11; InsightsResponseRenderer in task 026 / D2-16) can reference the
 * string symbolically instead of repeating the literal.
 */
export const STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE = 'structured-output-stream' as const;

registerWorkspaceWidget(
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
  {
    displayName: 'Structured AI Output',
    category: 'analysis',
    /**
     * Fluent v9 icon name. `TextBulletListSquareSparkleRegular` evokes a
     * structured (list-shaped) AI output with the brand sparkle.
     */
    icon: 'TextBulletListSquareSparkleRegular',
    /**
     * allowMultiple=true — FR-06 requires that each Summarize invocation
     * opens its own workspace tab so the user can compare outputs side-by-
     * side. The workspace tab manager's FIFO cap (MAX_WORKSPACE_TABS) still
     * applies — the oldest tab evicts when the cap is hit.
     *
     * Also used by Insights playbook static rendering (task 026): each
     * playbook turn can mount its own static tab without clobbering prior
     * structured outputs.
     */
    allowMultiple: true,
    /**
     * defaultOrder=160 — positioned just past DocumentViewerWidget (150) so
     * structured AI outputs sort after document previews in any auto-open
     * widget order. Tab insertion order is the dominant UX signal — this
     * default only matters for batch-mount scenarios.
     */
    defaultOrder: 160,
  },
  () =>
    import('./StructuredOutputStreamWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

/**
 * Sentinel export so callers can import this file as a NAMED side effect:
 *
 *   import { registerStructuredOutputStreamWidget } from './widgets/workspace/register-structured-output-stream-widget';
 *   registerStructuredOutputStreamWidget(); // "ensure widget is registered"
 *
 * The actual registration call above runs at module-evaluation time; this
 * function is a no-op that exists for explicitness at the call site.
 */
export function registerStructuredOutputStreamWidget(): void {
  // Top-level side effect already executed when this module was imported.
}
