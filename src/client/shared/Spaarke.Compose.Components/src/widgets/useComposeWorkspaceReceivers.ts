/**
 * useComposeWorkspaceReceivers — the `workspace`-channel leg of the Compose
 * three-pane coordination layer (spaarkeai-compose-r2 task 104, E2E-R5).
 *
 * This hook is the SINGLE terminal receiver for the Compose flows whose
 * reaction is owned by the Workspace pane (the editor is the insertion surface):
 *
 *   - Flow 3 `compose_context_insert`  (Context  → Workspace): a precedent /
 *     library clause is inserted into the editor at the cursor.
 *   - Flow 5 `compose_assistant_insert` (Assistant → Workspace): an AI draft is
 *     materialized into the editor — FROM the stored session-ledger entry when a
 *     `ledgerRef` is present (ADR-040 render-follows-store), else via the legacy
 *     R1 manual-confirm staging path.
 *   - `compose_qa_highlight` (Assistant → Workspace; task 072, FR-35 Doc Q&A
 *     stretch): a grounded, cited Text-path answer over the session-mounted
 *     document asks the editor to ephemerally highlight the cited source span.
 *     NOT a Flow-5 edit — no document mutation, no ledger entry; the editor
 *     resolves + renders a transient decoration only.
 *
 * WHY a hook (Component Justification §11 / folding task 070): the receiver logic
 * used to be an inline `usePaneEvent('workspace', …)` subscription in
 * ComposeWorkspace with `as unknown as` casts. Extracting it (a) removes the
 * casts now that the discriminants are typed on the bus union (ADR-030), (b)
 * makes the workspace-channel reception independently MOUNTABLE + REACHABLE for
 * the real-PaneEventBus forcing-function test (project E2E DoD row 3) WITHOUT
 * standing up a full TipTap editor, and (c) consolidates Flows 3 + 5 into ONE
 * coordination unit (the "one coordination layer" that supersedes task 070).
 * ComposeWorkspace consumes this exact hook, so the tested unit IS the shipped
 * receiver — no parallel implementation.
 *
 * The hook does NOT own the editor: it narrows the typed `workspace` event and
 * delegates to caller-supplied handlers (ComposeWorkspace passes its editor
 * materialize + reducer-dispatch handlers). The bus is REAL; the editor is the
 * caller's concern.
 *
 * @see @spaarke/ai-widgets PaneEventTypes.ts — WorkspacePaneEvent compose fields
 * @see ComposeWorkspace.tsx — the production consumer
 * @see design.md §7 (three-pane flow catalog); notes/e2e-gap-register.md Cluster 5
 */

// Deep-import (not the barrel) to skip the barrel's side-effect widget
// registration — matches ComposeWorkspace.tsx's import rationale.
import { usePaneEvent, type WorkspacePaneEvent } from '@spaarke/ai-widgets/events';

/**
 * Caller-supplied reactions for the two workspace-owned Compose flows. Each
 * receives the narrowed `WorkspacePaneEvent` (already guaranteed to carry the
 * matching discriminant) so the caller can read the typed compose fields
 * (`documentRef`, `contentHtml`, `ledgerRef`, `sourceClauseId`, …).
 */
export interface ComposeWorkspaceReceiverHandlers {
  /** Flow 3 — insert the precedent/library clause into the editor at cursor. */
  onContextInsert: (event: WorkspacePaneEvent) => void;
  /** Flow 5 — materialize the AI draft (from ledgerRef, or legacy staging). */
  onAssistantInsert: (event: WorkspacePaneEvent) => void;
  /**
   * Doc Q&A ephemeral highlight (task 072, FR-35) — a grounded, cited
   * Text-path answer over the session-mounted document. Optional: hosts that
   * do not wire the highlight (e.g. an older ComposeWorkspace snapshot) may
   * omit it; the event is silently dropped rather than throwing.
   */
  onQaHighlight?: (event: WorkspacePaneEvent) => void;
}

/**
 * Subscribe (on the REAL PaneEventBus) to the workspace-owned Compose flows
 * and route each to its handler. Non-compose `workspace` events (widget_load,
 * tab_change, …) are ignored — additive-discriminant contract (ADR-030).
 */
export function useComposeWorkspaceReceivers(handlers: ComposeWorkspaceReceiverHandlers): void {
  const { onContextInsert, onAssistantInsert, onQaHighlight } = handlers;

  usePaneEvent('workspace', (event: WorkspacePaneEvent): void => {
    if (event.type === 'compose_context_insert') {
      onContextInsert(event);
      return;
    }
    if (event.type === 'compose_assistant_insert') {
      onAssistantInsert(event);
      return;
    }
    if (event.type === 'compose_qa_highlight') {
      onQaHighlight?.(event);
    }
  });
}
