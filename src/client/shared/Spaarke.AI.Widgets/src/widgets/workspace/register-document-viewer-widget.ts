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
import { documentViewerWidgetVisibility } from './pillar9-visibility';
// Assistant-contract metadata SHAPE (FR-08 + FR-15 SHAPE, R3 task 022).
import type { WidgetAssistantContract, AssistantContractCard } from '../../types/shared';

/**
 * The widget type ID under which DocumentViewerWidget is registered.
 * Exported so dispatchers (e.g. ConversationPane in SpaarkeAi) can reference
 * the string symbolically instead of repeating the literal.
 */
export const DOCUMENT_VIEWER_WIDGET_TYPE = 'document-viewer' as const;

// FR-11 (task 022): per-item cards Summarize/Draft response/Draft memo,
// backed by RAG (lane 2) + on-demand body. No overview tool of its own —
// overview parity for documents lives on the sibling 'documents-list' grid
// (FR-06/FR-07's "all grids"); this widget is the single-doc, tab-focus
// active-item surface (FR-04b/FR-11).
//
// Tool-name NOTE: spec FR-11 names the three cards but not the backing tool
// identifiers (unlike FR-09's email tools, which are named explicitly:
// draft_reply/draft_forward/summarize_thread). `summarize_document` /
// `draft_document_response` / `draft_document_memo` below are task-022
// placeholder names chosen to mirror the email convention; the document
// per-item build task (026) is the authoritative source — confirm/update
// these three strings there if it lands under different catalog names.
// Object.freeze: only one widget (document-viewer) currently uses this
// object, but it's frozen for consistency with OVERVIEW_ONLY_CONTRACT/
// EMAIL_CONTRACT in register-workspace-widgets.ts and to guard against
// future in-place mutation once a second consumer (task 030/050) reads it.
// Explicit type annotation so each card's `landing` narrows to the
// AssistantCardLanding literal union instead of widening to `string`.
const DOCUMENT_VIEWER_PER_ITEM_CARDS: readonly AssistantContractCard[] = [
  // Answers IN CHAT from RAG/body with record-id citation (FR-11).
  { label: 'Summarize', tool: 'summarize_document', landing: 'chat' },
  // Quick reply-style output — lands on the composer.
  { label: 'Draft response', tool: 'draft_document_response', landing: 'composer' },
  // Longer-form document output — lands on the Compose (ADR-049) surface.
  { label: 'Draft memo', tool: 'draft_document_memo', landing: 'compose' },
];

const DOCUMENT_VIEWER_CONTRACT: WidgetAssistantContract = Object.freeze({
  overviewTools: Object.freeze([]),
  perItemCards: Object.freeze(DOCUMENT_VIEWER_PER_ITEM_CARDS),
  // hybrid: Summarize answers in chat (respond); Draft response/memo open a
  // surface (direct) — the widget mixes both.
  interactionPattern: 'hybrid',
});

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
    // FR-B1/FR-C3 (task 020): the canonical document-preview widget.
    contextType: 'document',
    // FR-11 (task 022): see DOCUMENT_VIEWER_CONTRACT above.
    assistantContract: DOCUMENT_VIEWER_CONTRACT,
  },
  () =>
    import('./DocumentViewerWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>,
  // Pillar 9 visibility opt-in (task 073, D-C-28). DocumentViewer category:
  // exposes file metadata + selection state. selectionText capped at 200
  // chars per FR-57 acceptance. See `pillar9-visibility.ts` for the
  // derivation + ADR-015 privacy rationale.
  documentViewerWidgetVisibility
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
