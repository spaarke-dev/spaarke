/**
 * ComposeWorkspace.types.ts — shared types for the Compose workspace orchestrator
 * and its extracted hooks (`useComposeBroadcastChannel`, `useComposeCheckoutLifecycle`,
 * `useComposeHeartbeatGate`).
 *
 * Project: spaarkeai-compose-r1
 * Phase:   Phase 4 / Phase 5
 * Extracted: R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC) — types lifted
 *            here so the hooks can import them without forming a circular
 *            dependency on ComposeWorkspace.tsx.
 *
 * Re-exported from ComposeWorkspace.tsx for backwards-compatible imports.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx
 */

import type { ComposeAssistantToWorkspaceFlow, ComposeDocumentRef } from '../../types/compose-contracts';

// ---------------------------------------------------------------------------
// Document-context state machine
// ---------------------------------------------------------------------------

/**
 * Reducer state. The discriminated `status` field is the source of truth for
 * which child renders.
 *
 *   - `'empty'`   → no document selected; render ComposeEmptyState
 *   - `'loading'` → fetching DOCX bytes from BFF; render spinner
 *   - `'loaded'`  → editor + toolbar; document bytes available
 *   - `'saving'`  → preserving editor + toolbar but disable actions
 *   - `'error'`   → render error MessageBar with retry affordance
 */
export type ComposeWorkspaceStatus = 'empty' | 'loading' | 'loaded' | 'saving' | 'error';

/**
 * SPE check-out lifecycle status (Task 050 / Spike #3 §9; Task 051 multi-tab UX).
 *
 *   - `'idle'`               → no checkout attempted yet (initial state, or pre-promotion)
 *   - `'skipped'`            → no `sprkDocumentId` present (Path B ephemeral)
 *   - `'probing'`            → GET /api/documents/{id}/checkout-status in flight
 *   - `'acquiring'`          → POST /api/documents/{id}/checkout in flight
 *   - `'acquired'`           → 200 OK; lock held by current user (own or idempotent re-checkout)
 *   - `'conflict'`           → 409; doc locked by ANOTHER user (cross-user only)
 *   - `'same-user-conflict'` → probe revealed THIS user holds the lock from
 *                              another session (CheckoutStatusInfo.IsCurrentUser === true).
 *                              User must resolve via the ComposeConflictDialog.
 *   - `'discarding'`         → POST /api/documents/{id}/discard in flight as the
 *                              "force-close" step before re-checkout.
 *   - `'failed'`             → non-2xx / network error (non-fatal — editor remains usable)
 *   - `'cancelled'`          → user clicked "Cancel — close this tab" in the conflict
 *                              dialog, OR another tab posted a `force-closed` message.
 */
export type ComposeCheckoutStatus =
  | 'idle'
  | 'skipped'
  | 'probing'
  | 'acquiring'
  | 'acquired'
  | 'conflict'
  | 'same-user-conflict'
  | 'discarding'
  | 'failed'
  | 'cancelled';

/**
 * Subset of the BFF `CheckoutUserInfo` response shape we surface in the UI.
 * Mirrors `src/server/api/Sprk.Bff.Api/Models/CheckoutModels.cs`.
 */
export interface ComposeCheckoutLockedByInfo {
  id: string;
  name: string;
  /** ISO timestamp of when the conflicting lock was acquired. */
  checkedOutAt: string | null;
}

export interface ComposeWorkspaceState {
  status: ComposeWorkspaceStatus;
  documentRef: ComposeDocumentRef | null;
  sessionId: string;
  docxBytes: ArrayBuffer | null;
  /** ETag from last load — used as if-match on save (per ComposeEndpoints contract). */
  etag: string | null;
  /** Mammoth import warnings (Tier 1 safe). */
  importWarnings: Array<{ type: string; message: string }>;
  /** User-facing error message (NOT a Tier 3 sink). */
  errorMessage: string | null;
  /** Last assistant-inserted draft staged for confirm (Flow 5 R1 manual-confirm gate). */
  pendingAssistantInsert: ComposeAssistantToWorkspaceFlow | null;
  /** SPE check-out lifecycle (Task 050 / Spike #3 §9; Task 051 multi-tab UX). */
  checkoutStatus: ComposeCheckoutStatus;
  /** Populated only when `checkoutStatus === 'conflict'` (lock holder is a DIFFERENT user). */
  checkoutLockedBy: ComposeCheckoutLockedByInfo | null;
  /** Populated only when `checkoutStatus === 'same-user-conflict'`. Task 051. */
  sameUserConflictInfo: { checkedOutAt: string | null } | null;
  /** User-facing checkout failure message (set only when status is `'failed'`). */
  checkoutFailureMessage: string | null;
}

export type ComposeWorkspaceAction =
  | { kind: 'requestLoad'; documentRef: ComposeDocumentRef; sessionId: string }
  | {
      kind: 'loadSucceeded';
      docxBytes: ArrayBuffer;
      etag: string | null;
      sessionId: string;
      sprkDocumentId?: string;
      fileName?: string;
    }
  | { kind: 'loadFailed'; errorMessage: string }
  | { kind: 'requestSave' }
  | { kind: 'saveSucceeded'; sprkDocumentId?: string; etag: string | null }
  | { kind: 'saveFailed'; errorMessage: string }
  | { kind: 'reset' }
  | { kind: 'importWarnings'; warnings: Array<{ type: string; message: string }> }
  | { kind: 'pendingAssistantInsert'; payload: ComposeAssistantToWorkspaceFlow }
  | { kind: 'clearPendingAssistantInsert' }
  // ── Task 050 (Spike #3 §9): SPE check-out lifecycle actions ───────────────
  | { kind: 'checkoutSkipped' }
  | { kind: 'checkoutRequested' }
  | { kind: 'checkoutAcquired' }
  | { kind: 'checkoutConflict'; lockedBy: ComposeCheckoutLockedByInfo }
  | { kind: 'checkoutFailed'; failureMessage: string }
  // ── Task 051 (Spike #3 §1 multi-tab UX): probe + same-user conflict ─────
  | { kind: 'checkoutProbeRequested' }
  | { kind: 'checkoutSameUserConflict'; checkedOutAt: string | null }
  | { kind: 'checkoutDiscarding' }
  | { kind: 'checkoutCancelled' };

export const INITIAL_STATE: ComposeWorkspaceState = {
  status: 'empty',
  documentRef: null,
  sessionId: '',
  docxBytes: null,
  etag: null,
  importWarnings: [],
  errorMessage: null,
  pendingAssistantInsert: null,
  checkoutStatus: 'idle',
  checkoutLockedBy: null,
  sameUserConflictInfo: null,
  checkoutFailureMessage: null,
};

export function composeWorkspaceReducer(
  state: ComposeWorkspaceState,
  action: ComposeWorkspaceAction
): ComposeWorkspaceState {
  switch (action.kind) {
    case 'requestLoad':
      return {
        ...INITIAL_STATE,
        status: 'loading',
        documentRef: action.documentRef,
        sessionId: action.sessionId,
      };
    case 'loadSucceeded':
      return {
        ...state,
        status: 'loaded',
        docxBytes: action.docxBytes,
        etag: action.etag,
        sessionId: action.sessionId,
        documentRef: state.documentRef
          ? {
              ...state.documentRef,
              sprkDocumentId: action.sprkDocumentId ?? state.documentRef.sprkDocumentId,
              fileName: action.fileName ?? state.documentRef.fileName,
            }
          : state.documentRef,
        errorMessage: null,
      };
    case 'loadFailed':
      return {
        ...state,
        status: 'error',
        errorMessage: action.errorMessage,
      };
    case 'requestSave':
      if (state.status !== 'loaded') return state;
      return { ...state, status: 'saving', errorMessage: null };
    case 'saveSucceeded':
      return {
        ...state,
        status: 'loaded',
        etag: action.etag,
        documentRef: state.documentRef
          ? {
              ...state.documentRef,
              sprkDocumentId: action.sprkDocumentId ?? state.documentRef.sprkDocumentId,
            }
          : state.documentRef,
      };
    case 'saveFailed':
      return { ...state, status: 'loaded', errorMessage: action.errorMessage };
    case 'reset':
      return INITIAL_STATE;
    case 'importWarnings':
      return { ...state, importWarnings: action.warnings };
    case 'pendingAssistantInsert':
      return { ...state, pendingAssistantInsert: action.payload };
    case 'clearPendingAssistantInsert':
      return { ...state, pendingAssistantInsert: null };
    // ── Task 050: SPE check-out lifecycle ───────────────────────────────────
    case 'checkoutSkipped':
      return {
        ...state,
        checkoutStatus: 'skipped',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutRequested':
      return {
        ...state,
        checkoutStatus: 'acquiring',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutAcquired':
      return {
        ...state,
        checkoutStatus: 'acquired',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutConflict':
      return {
        ...state,
        checkoutStatus: 'conflict',
        checkoutLockedBy: action.lockedBy,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutFailed':
      return {
        ...state,
        checkoutStatus: 'failed',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: action.failureMessage,
      };
    // ── Task 051: probe + same-user conflict ────────────────────────────────
    case 'checkoutProbeRequested':
      return {
        ...state,
        checkoutStatus: 'probing',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutSameUserConflict':
      return {
        ...state,
        checkoutStatus: 'same-user-conflict',
        checkoutLockedBy: null,
        sameUserConflictInfo: { checkedOutAt: action.checkedOutAt },
        checkoutFailureMessage: null,
      };
    case 'checkoutDiscarding':
      return {
        ...state,
        checkoutStatus: 'discarding',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutCancelled':
      return {
        ...state,
        checkoutStatus: 'cancelled',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    default:
      return state;
  }
}
