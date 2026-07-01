/**
 * Spaarke Compose — Spike #2 stub-receiver wiring (R1 prototype)
 *
 * Demonstrates how the LOCKED contracts in `./contracts.ts` plug into the
 * existing `@spaarke/ai-widgets` PaneEventBus. This file is REFERENCE
 * material — it is not built as production code. The actual production
 * subscriber wiring lands in Phase 4 (tasks 042 + 043 — ComposeWorkspace +
 * ComposeToolbar) following the patterns shown here.
 *
 * Why this isn't run as code:
 *  - The spike directory is throwaway per POML constraint
 *    (`prototype code lives in notes/spikes/ ONLY`).
 *  - The PaneEventBus types come from `@spaarke/ai-widgets`, which is an
 *    npm workspace package. Building a standalone Vite + React harness
 *    here would require duplicating tsconfig + workspace paths solely to
 *    re-prove what the unit-test contract in
 *    `Spaarke.AI.Widgets/src/events/__tests__/PaneEventBus.test.ts`
 *    already proves: subscribe/dispatch/unsubscribe work, multi-subscriber
 *    works, additive event types don't break existing subscribers.
 *  - The locked artifact IS the deliverable. Receivers can be stubs.
 *
 * READ THIS FILE LINE-BY-LINE — it is the dispatcher/subscriber pattern
 * Phase 4 tasks will copy.
 */

import type {
  PaneEventBus,
  // The above import is the existing class from @spaarke/ai-widgets/events/PaneEventBus.
  // Replace with the real path when promoting to production:
  //   import { PaneEventBus } from '@spaarke/ai-widgets';
  // For this throwaway file, the type-only import keeps tsc --noEmit honest.
} from '../../../../src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus';

import {
  logFlowEvent,
  type ComposeAssistantToContextFlow,
  type ComposeAssistantToWorkspaceFlow,
  type ComposeContextToAssistantFlow,
  type ComposeContextToWorkspaceFlow,
  type ComposeDocumentRef,
  type ComposeSelection,
  type ComposeWorkspaceToAssistantFlow,
  type ComposeWorkspaceToContextFlow,
} from './contracts';

// ===========================================================================
// DISPATCHER patterns — three R1-wired flows (1, 2, 5)
// ===========================================================================

/**
 * Flow 1 dispatcher — Workspace → Context.
 *
 * Called by ComposeWorkspace.tsx (Phase 4 task 042) on TipTap
 * selection-change debounce.
 */
export function dispatchFlow1(
  bus: PaneEventBus,
  documentRef: ComposeDocumentRef,
  selection: ComposeSelection,
  sessionId: string,
): void {
  const event: ComposeWorkspaceToContextFlow = {
    type: 'compose_selection_changed',
    documentRef,
    selection,
    sessionId,
    timestamp: new Date().toISOString(),
  };
  // Existing PaneEventBus types accept this additively (ADR-030 rule).
  // Subscribers that don't recognize 'compose_selection_changed' tolerate
  // the unknown event.type per the existing additive contract.
  bus.dispatch('context', event as never);
}

/**
 * Flow 2 dispatcher — Workspace → Assistant.
 *
 * Called by ComposeWorkspace.tsx (Phase 4 task 042) on the SAME
 * selection-change event as Flow 1 (when selection meets minimum-size
 * threshold for action eligibility, e.g. ≥10 chars).
 */
export function dispatchFlow2(
  bus: PaneEventBus,
  documentRef: ComposeDocumentRef,
  selection: ComposeSelection,
  sessionId: string,
): void {
  if (selection.selectionText.length < 10) return; // too short for actions
  const event: ComposeWorkspaceToAssistantFlow = {
    type: 'compose_selection_offer',
    documentRef,
    selection,
    jpsScope: 'compose-selection',
    sessionId,
    timestamp: new Date().toISOString(),
  };
  bus.dispatch('conversation', event as never);
}

/**
 * Flow 5 dispatcher — Assistant → Workspace.
 *
 * Called by ConversationPane.tsx (or the playbook-action-result wiring)
 * after a JPS playbook node produces a draft suitable for insertion.
 * R1 = require user confirm (default true).
 */
export function dispatchFlow5(
  bus: PaneEventBus,
  documentRef: ComposeDocumentRef,
  sourcePlaybookId: string,
  sourceNodeId: string,
  contentHtml: string,
  sessionId: string,
  options?: {
    format?: 'html' | 'prosemirror-json';
    insertMode?: 'replace-selection' | 'insert-at-cursor' | 'append';
    requireUserConfirm?: boolean;
  },
): void {
  const event: ComposeAssistantToWorkspaceFlow = {
    type: 'compose_assistant_insert',
    documentRef,
    sourcePlaybookId,
    sourceNodeId,
    contentHtml,
    format: options?.format ?? 'html',
    insertMode: options?.insertMode ?? 'insert-at-cursor',
    requireUserConfirm: options?.requireUserConfirm ?? true,
    sessionId,
    timestamp: new Date().toISOString(),
  };
  bus.dispatch('workspace', event as never);
}

// ===========================================================================
// STUB SUBSCRIBERS — six receivers, all logging-only
// ===========================================================================

/**
 * Registers all six stub subscribers on the bus. Returns an `unsubscribe`
 * function that detaches them all.
 *
 * R1 production usage (Phase 4):
 *   - ContextPaneController.tsx subscribes Flows 1 + 6 receivers
 *   - ConversationPane.tsx subscribes Flows 2 + 4 receivers
 *   - ComposeWorkspace.tsx subscribes Flows 3 + 5 receivers
 *
 * R1 BEHAVIOUR (per `COMPOSE_FLOW_RECEIVER_MATRIX`):
 *   - Flows 1, 2 — log + parallel existing-mechanism UX
 *   - Flow 5    — log + manual-confirm UI gate (no auto-insertion)
 *   - Flows 3, 4, 6 — log only
 */
export function registerStubReceivers(bus: PaneEventBus): () => void {
  const unsubscribers: Array<() => void> = [];

  // Flow 1 receiver (on `context` channel)
  unsubscribers.push(
    bus.subscribe('context', (event) => {
      if (event.type === 'compose_selection_changed') {
        logFlowEvent('1-workspace-to-context', event as unknown as ComposeWorkspaceToContextFlow);
      }
    }),
  );

  // Flow 6 receiver (also on `context` channel — same subscriber slot)
  unsubscribers.push(
    bus.subscribe('context', (event) => {
      if (event.type === 'compose_assistant_insight') {
        logFlowEvent('6-assistant-to-context', event as unknown as ComposeAssistantToContextFlow);
      }
    }),
  );

  // Flow 2 receiver (on `conversation` channel)
  unsubscribers.push(
    bus.subscribe('conversation', (event) => {
      if (event.type === 'compose_selection_offer') {
        logFlowEvent('2-workspace-to-assistant', event as unknown as ComposeWorkspaceToAssistantFlow);
      }
    }),
  );

  // Flow 4 receiver (also on `conversation` channel)
  unsubscribers.push(
    bus.subscribe('conversation', (event) => {
      if (event.type === 'compose_context_offer') {
        logFlowEvent('4-context-to-assistant', event as unknown as ComposeContextToAssistantFlow);
      }
    }),
  );

  // Flow 3 receiver (on `workspace` channel)
  unsubscribers.push(
    bus.subscribe('workspace', (event) => {
      if (event.type === 'compose_context_insert') {
        logFlowEvent('3-context-to-workspace', event as unknown as ComposeContextToWorkspaceFlow);
      }
    }),
  );

  // Flow 5 receiver (also on `workspace` channel)
  unsubscribers.push(
    bus.subscribe('workspace', (event) => {
      if (event.type === 'compose_assistant_insert') {
        logFlowEvent('5-assistant-to-workspace', event as unknown as ComposeAssistantToWorkspaceFlow);
      }
    }),
  );

  return (): void => {
    for (const u of unsubscribers) u();
  };
}

// ===========================================================================
// VALIDATION — manual round-trip check (run mentally; not built)
// ===========================================================================

/**
 * Smoke-test pseudo-code. To execute this for real, the developer would:
 *
 *   1. cd src/client/shared/Spaarke.AI.Widgets
 *   2. npm test -- contracts-roundtrip.test.ts  (test would be created in Phase 4)
 *   3. Verify all 6 console.info lines appear in order
 *
 * For Spike #2 the validation is structural:
 *   - tsc --noEmit on contracts.ts must pass (no type errors)
 *   - All 6 interfaces have ADR-015 privacy annotations on user-content fields
 *   - All 6 interfaces include sessionId + timestamp (correlation requirements)
 *   - Channel mapping table is consistent with PaneEventBus.constructor channel set
 *   - No interface persists transient state via HostContext (design.md §14 row 2)
 *
 * Type-check verification of `contracts.ts` alone is the gate. Runtime
 * verification is Phase 4 task 042's responsibility when it lands the
 * production wiring.
 */
export function validationPseudoCode(): void {
  // Pseudo-code for the round-trip smoke test:
  //
  //   const bus = new PaneEventBus();
  //   const cleanup = registerStubReceivers(bus);
  //
  //   const doc: ComposeDocumentRef = { speDriveItemId: 'spe-test-001', fileName: 'test.docx' };
  //   const sel: ComposeSelection = { from: 100, to: 145, selectionText: 'force majeure clause shall apply', contextLabel: 'Clause 7.2' };
  //   const session = 'session-test-001';
  //
  //   dispatchFlow1(bus, doc, sel, session);
  //     // → console: "[Compose Flow 1-workspace-to-context] event.type=compose_selection_changed ..."
  //
  //   dispatchFlow2(bus, doc, sel, session);
  //     // → console: "[Compose Flow 2-workspace-to-assistant] event.type=compose_selection_offer ..."
  //
  //   dispatchFlow5(bus, doc, '47686eb1-9916-f111-8343-7c1e520aa4df', 'deliver-output', '<p>draft</p>', session);
  //     // → console: "[Compose Flow 5-assistant-to-workspace] event.type=compose_assistant_insert ..."
  //
  //   cleanup();
  //   // No further events delivered.
  //
  // Expected: 3 lines logged in sequence. Order is preserved because
  // PaneEventBus iterates subscribers synchronously (per its implementation
  // in src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts:139).
}
