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

// FR-11 (task 022, FINALIZED by task 026): per-item cards Summarize/Draft
// response/Draft memo, backed by RAG (lane 2) + on-demand body. No overview
// tool of its own — overview parity for documents lives on the sibling
// 'documents-list' grid (FR-06/FR-07's "all grids"); this widget is the
// single-doc, tab-focus active-item surface (FR-04b/FR-11).
//
// Tool-name / landing NOTE (task 026 finalization, §11 reuse-first): these
// three `tool` strings are NOT closed-catalog BFF tool names invoked
// deterministically (unlike FR-09's email tools, which ARE — `draft_reply`/
// `draft_forward`/`summarize_thread` back real `EmailDraftToolHandler`
// catalog rows). Documents have no equivalent deterministic REST entry
// point. Instead `DocumentPerItemCards.tsx` maps each `tool` string to a
// deterministic chat instruction sent through the EXISTING SprkChat
// `pendingOutboundMessage` host-send seam, with the active document's id
// armed for that ONE turn via `onDecorateOutboundBody`'s `body.documentId`
// (the SAME one-shot, ADR-015 on-demand mechanism the shipped "Summarize
// this email" chip uses — `ConversationPane.tsx`'s `pendingEmailDocRef`/
// `handleDecorateOutboundBodyWithRevise`). The server resolves
// `body.documentId` through the SAME `DocumentContextService`/RAG lane-2
// pipeline that already backs "summarize this document" today
// (`ChatEndpoints.SendMessage` → `effectiveDocumentId` →
// `agentFactory.CreateAgentAsync`) — no new BFF tool, no new retrieval
// service.
//
// `landing` NOTE: documents have no composer-equivalent surface (that is an
// email-only UI concept, `SendEmailDialog`). All three cards therefore land
// `'chat'` for task 026 — a documented, narrower scope than FR-11's
// "composer/Compose for drafts" wording (CLAUDE.md §6.5 Path A project-
// scoped exception; see task 026 notes): the only real drafting surface for
// documents is Compose (ADR-049), and routing "Draft memo" there via the
// EXISTING R7-4 `compose-draft-document` keyword auto-router
// (`composeDraftRouting.ts`) would silently DROP the one-shot `documentId`
// grounding (that alternate Binding-dispatch path does not forward
// `body.documentId`) — a worse, ungrounded outcome than answering in chat.
// task 040 evaluated building a properly-grounded Compose landing for these
// two cards and decided AGAINST it (out of 040's registration-field scope —
// see the `interactionPattern` note below); D-8 (defer-issues.md) stays open
// for a future task to pick up threading `body.documentId` into the
// compose-draft-document Binding dispatch, if that becomes a priority.
// Object.freeze: only one widget (document-viewer) currently uses this
// object, but it's frozen for consistency with OVERVIEW_ONLY_CONTRACT/
// EMAIL_CONTRACT in register-workspace-widgets.ts and to guard against
// future in-place mutation once a second consumer (task 030/050) reads it.
// Explicit type annotation so each card's `landing` narrows to the
// AssistantCardLanding literal union instead of widening to `string`.
const DOCUMENT_VIEWER_PER_ITEM_CARDS: readonly AssistantContractCard[] = [
  // Answers IN CHAT from RAG/body with record-id citation (FR-11).
  { label: 'Summarize', tool: 'summarize_document', landing: 'chat' },
  // task 026: answers in chat (grounded via the one-shot documentId) — see the landing NOTE above.
  { label: 'Draft response', tool: 'draft_document_response', landing: 'chat' },
  // task 026: answers in chat (grounded via the one-shot documentId) — see the landing NOTE above.
  { label: 'Draft memo', tool: 'draft_document_memo', landing: 'chat' },
];

const DOCUMENT_VIEWER_CONTRACT: WidgetAssistantContract = Object.freeze({
  overviewTools: Object.freeze([]),
  perItemCards: Object.freeze(DOCUMENT_VIEWER_PER_ITEM_CARDS),
  // task 040 finalization (D-8, CLAUDE.md §6.5 Path A — see defer-issues.md D-8 and this
  // file's landing NOTE above): task 022 best-effort placeheld 'hybrid' anticipating
  // Draft response/Draft memo would land on composer/Compose per FR-11's literal wording.
  // task 026 shipped all three cards landing 'chat' instead (the only grounded option —
  // routing through the existing compose-draft-document auto-router would silently drop
  // the one-shot documentId grounding). task 040 evaluated building a properly-grounded
  // Compose landing (D-8's "Fix required") and decided AGAINST it for this task: doing so
  // would require threading body.documentId into the Binding-dispatch compose-draft-document
  // path — new BFF-adjacent wiring outside 040's scope (register-contract field work only;
  // no BFF C# changes). The minimal HONEST fix is to correct the DATA FIELD to match the
  // shipped behavior instead: since every perItemCards entry lands 'chat', this widget is
  // 'respond' per the AssistantInteractionPattern contract (types/shared.ts) — not 'hybrid'.
  // D-8 stays open for a future task to build the grounded Compose landing; if/when that
  // ships, flip this back to 'hybrid' (mixed chat + surface) alongside the landing changes.
  interactionPattern: 'respond',
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
