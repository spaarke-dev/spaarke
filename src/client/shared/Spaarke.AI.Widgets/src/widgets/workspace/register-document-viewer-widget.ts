/**
 * register-document-viewer-widget.ts
 *
 * Registers `DocumentViewerWidget` under the `'document-viewer'` workspace
 * widget type key. Imported as a side effect from the package barrel so the
 * widget is available before any shell mounts.
 *
 * Pattern reference: matches `register-workspace-widgets.ts` (R1-origin
 * widgets) and Calendar widget Pattern D (R3 task 115 — `calendar.registration.ts`
 * for LegalWorkspace). We keep this in its own file so future demo-widget
 * additions don't bloat the long-form `register-workspace-widgets.ts` file
 * and so the new widget's registration is reversible by deleting one file.
 *
 * Created in R4 task 042 (W-4) — first end-to-end Assistant → Workspace
 * widget mount demo per FR-02 / OC-R4-07.
 *
 * ADR-012 (shared lib), ADR-022 (React 19).
 */

import { registerWorkspaceWidget } from '../../registry/WorkspaceWidgetRegistry';

/**
 * The widget type ID under which DocumentViewerWidget is registered.
 * Exported so dispatchers (e.g. ConversationPane in SpaarkeAi) can reference
 * the string symbolically instead of repeating the literal.
 */
export const DOCUMENT_VIEWER_WIDGET_TYPE = 'document-viewer' as const;

registerWorkspaceWidget(
  DOCUMENT_VIEWER_WIDGET_TYPE,
  {
    displayName: 'Document Viewer',
    category: 'document',
    icon: 'DocumentRegular',
    /**
     * allowMultiple=true — a user may attach several files in one chat session;
     * each opens as its own workspace tab. The tab manager's FIFO cap
     * (MAX_WORKSPACE_TABS) still applies — the oldest tab evicts when the cap
     * is hit.
     */
    allowMultiple: true,
    /**
     * defaultOrder=150 — positioned after the existing intent dispatchers
     * (email-compose 100, meeting-schedule 110, create-project 120,
     * find-similar 130, workspace 140) so it sorts last among current widgets.
     */
    defaultOrder: 150,
  },
  () =>
    import('./DocumentViewerWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

/**
 * Sentinel export so callers can import this file as a NAMED side effect:
 *
 *   import { registerDocumentViewerWidget } from './widgets/workspace/register-document-viewer-widget';
 *   registerDocumentViewerWidget(); // "ensure widget is registered"
 *
 * The actual registration call above runs at module-evaluation time; this
 * function is a no-op that exists for explicitness at the call site.
 */
export function registerDocumentViewerWidget(): void {
  // Top-level side effect already executed when this module was imported.
}
