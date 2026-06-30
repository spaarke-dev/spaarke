/**
 * ComposeWorkspace.tsx — workspace-level orchestrator for the Spaarke Compose surface.
 *
 * Project:   spaarkeai-compose-r1
 * Tasks:     042 — Frontend: create `ComposeWorkspace.tsx` (W5, initial)
 *            050 — Acquire SPE check-out on Compose open (W6 — Spike #3 §9
 *                  REVISED scope: frontend-only call to existing
 *                  `POST /api/documents/{documentId}/checkout`; NO BFF code.
 *                  Path A ADR Tension approved post-Wave-0 2026-06-29.)
 * Phase:     Phase 4 (W5) / Phase 5 (W6)
 * Wave:      W5 (composes W4 siblings 043 + 044 + 045) + W6 (task 050 SPE checkout)
 *
 * Purpose:
 *   Composes the three Compose Phase-4 surfaces into a single mountable widget:
 *   - W4-045 `ComposeEditor`     (shared lib `@spaarke/compose-components`)
 *   - W4-043 `ComposeToolbar`    (SpaarkeAi solution, sibling file)
 *   - W4-044 `ComposeEmptyState` (SpaarkeAi solution, sibling file)
 *
 *   The workspace owns the document-context state machine, the BFF load/save
 *   wiring, the PaneEventBus subscribers for the three R1-wired flows (1, 2, 5
 *   per `compose-contracts.ts` `COMPOSE_FLOW_RECEIVER_MATRIX`), and the keep-it-
 *   alive contract of the 21 host MUSTs in
 *   `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (clean unmount,
 *   no theme reassignment, no global window-level state mutation).
 *
 * Location decision (CLAUDE.md §11 component justification):
 *   - Existing: no orchestrator exists for the Compose surface. Calendar's
 *     `CalendarWorkspaceWidget` is structurally analogous (Pattern D) but
 *     read-only events; ConversationPane is chat. Neither composes a DOCX
 *     editor + workspace toolbar + empty-state branch.
 *   - Extension: extension is not viable. CalendarWorkspaceWidget consumes
 *     EventsPageContext + DataGrid; this surface consumes ComposeEditor + DOCX
 *     bytes + BFF Compose endpoints; the surface areas don't overlap.
 *   - Cost-of-doing-nothing: FR-02 fails — the registered `compose-editor`
 *     section (W1b-040) has nothing real to mount (W6-046 still wires the
 *     placeholder); the W4 siblings have no host that threads them together;
 *     the Phase 6 smoke test (FR-10 + FR-20) cannot fire because there's no
 *     UI surface to dispatch from.
 *   - The orchestrator lives in the SpaarkeAi solution (NOT shared lib) per
 *     POML §1 + outputs because it consumes solution-local
 *     `compose-contracts.ts` (Flow 1/2/5 narrowing requires the contract
 *     discriminants), ComposeToolbar (solution), and ComposeEmptyState
 *     (solution). Shared lib `@spaarke/compose-components` retains only the
 *     reusable ComposeEditor widget per W4 architectural lock.
 *
 * Pattern D shape (Calendar precedent):
 *   The LegalWorkspace section registration shim
 *   (`src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts`)
 *   currently renders an inline placeholder; W6-046 replaces that with a thin
 *   delegation to a shared shim that mounts `ComposeWorkspace` from this file.
 *   This file is the workspace-side orchestrator; the LegalWorkspace side is a
 *   single-line factory. Same shape as Calendar
 *   (calendar.registration.ts → CalendarWorkspaceWidget).
 *
 * State management (chose useReducer over useState):
 *   The document-context state machine has multiple atomic transitions:
 *     idle → loading → loaded
 *     idle → empty (browse/search request fires)
 *     loaded → saving → loaded
 *     loaded → error
 *   These transitions touch 4–5 fields atomically (docRef, sessionId, bytes,
 *   warnings, status); useReducer keeps them coherent. useContext would be
 *   overkill — there's exactly one consumer (this file) and no nested
 *   subscribers. Rejected useState because each transition would require
 *   ≥3 setX() calls in sequence, opening a race window where intermediate
 *   renders see partial state.
 *
 * PaneEventBus subscribers wired in R1 (per compose-contracts.ts
 * COMPOSE_FLOW_RECEIVER_MATRIX):
 *   Flow 1 — `compose_selection_changed` on `context`: emitted by ComposeEditor;
 *           subscriber here LOGS only (R2 wires precedent lookup).
 *   Flow 2 — `compose_selection_offer` on `conversation`: emitted by
 *           ComposeEditor; subscriber here LOGS only (R2 wires action menu).
 *   Flow 5 — `compose_assistant_insert` on `workspace`: emitted by the
 *           Assistant pane after a playbook action; subscriber here LOGS
 *           only AND honors `requireUserConfirm` per Spike #2 §10.3 BINDING.
 *
 *   Flows 3, 4, 6 are CONTRACT-ONLY in R1; no subscribers needed (parents
 *   that consume the editor or context pane stub them at their own boundary).
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 only; `makeStyles` + `tokens.*` (semantic).
 *   - ADR-022: React 19.
 *   - ADR-028: Auth NOT touched here — `useDocumentActions` (consumed by
 *     ComposeToolbar) handles `authenticatedFetch`; ComposeEditor heartbeat
 *     uses `authenticatedFetch` internally. The load/save calls below use
 *     `authenticatedFetch` from `@spaarke/auth`.
 *   - ADR-030: additive event-type discriminants; subscribers narrow on
 *     `event.type` before reading discriminant-specific fields.
 *   - CLAUDE.md §3 sub-agent write boundary — this task does NOT touch
 *     `.claude/` paths (verified).
 *   - CLAUDE.md §6.5 ADR Conflict Resolution — no new tensions surfaced.
 *   - CLAUDE.md §11 component justification — see file header.
 *   - LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md 21 host MUSTs:
 *       * Theme ownership preserved — uses `tokens.*` only; never assigns a
 *         FluentProvider; host owns the theme.
 *       * sessionStorage NOT touched — Compose's "current doc" is in-memory
 *         per spec; ChatSession reuse handles cross-session continuity.
 *       * webApi shim NOT consumed — Compose talks to BFF directly per
 *         `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` (large payload =
 *         BFF; multi-tenant scoping = BFF; OBO exchange = BFF).
 *       * Mount semantics — defensive null/empty handling; clean unmount via
 *         aborted-fetch + reducer reset; no orphaned timers (heartbeat lives
 *         inside ComposeEditor and tears down on its unmount).
 *       * Lifecycle hooks — fires `onComposeMount` / `onComposeUnmount` so
 *         the host (W6-046) can track mounts; no global side-effects.
 *
 * @see projects/spaarkeai-compose-r1/tasks/042-frontend-create-compose-workspace.poml
 * @see projects/spaarkeai-compose-r1/spec.md FR-02, FR-03, FR-12, FR-18, FR-20
 * @see projects/spaarkeai-compose-r1/design.md §5 (six flows), §11 (component reuse map)
 * @see src/solutions/SpaarkeAi/src/types/compose-contracts.ts (Flow 1, 2, 5 contracts)
 * @see src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeToolbar.tsx
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeEmptyState.tsx
 * @see src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs (Load + Save contracts)
 * @see docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md (21 host MUSTs)
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  mergeClasses,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Text,
  Spinner,
} from '@fluentui/react-components';
import {
  ComposeEditor,
  type ComposeEditorHandle,
  type ComposeEditorDocumentRef,
} from '@spaarke/compose-components';
import {
  useDispatchPaneEvent,
  usePaneEvent,
  type WorkspacePaneEvent,
  type ContextPaneEvent,
  type ConversationPaneEvent,
} from '@spaarke/ai-widgets';
import { authenticatedFetch, buildBffApiUrl } from '@spaarke/auth';

import { ComposeToolbar, type ComposeSummarizeRequestEvent } from './ComposeToolbar';
import { ComposeEmptyState } from './ComposeEmptyState';
import { ComposeConflictDialog } from './ComposeConflictDialog';
import type {
  ComposeDocumentRef,
  ComposeAssistantToWorkspaceFlow,
  ComposeWorkspaceToContextFlow,
  ComposeWorkspaceToAssistantFlow,
} from '../../types/compose-contracts';

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
 *
 * The `documentRef` is the canonical pointer (SPE drive-item id +/- Dataverse
 * id post-promotion); `sessionId` correlates flows; `docxBytes` is the buffer
 * passed to ComposeEditor (re-mounts on identity change).
 */
type ComposeWorkspaceStatus = 'empty' | 'loading' | 'loaded' | 'saving' | 'error';

/**
 * SPE check-out lifecycle status (Task 050 / Spike #3 §9; Task 051 multi-tab UX).
 *
 *   - `'idle'`               → no checkout attempted yet (initial state, or pre-promotion)
 *   - `'skipped'`            → no `sprkDocumentId` present (Path B ephemeral; checkout
 *                              cannot fire until first-Save promotion creates the row)
 *   - `'probing'`            → GET /api/documents/{id}/checkout-status in flight (Task 051)
 *   - `'acquiring'`          → POST /api/documents/{id}/checkout in flight
 *   - `'acquired'`           → 200 OK; lock held by current user (own or idempotent re-checkout)
 *   - `'conflict'`           → 409; doc locked by ANOTHER user (cross-user only)
 *   - `'same-user-conflict'` → probe revealed THIS user holds the lock from
 *                              another session (CheckoutStatusInfo.IsCurrentUser === true).
 *                              The user must resolve via the ComposeConflictDialog
 *                              (Force-close → discard then acquire; Go-to-other →
 *                              focus other tab via BroadcastChannel; Cancel → unmount).
 *                              Task 051 (FR-16). See Spike #3 §1 + §9.
 *   - `'discarding'`         → POST /api/documents/{id}/discard in flight as the
 *                              "force-close" step before re-checkout. Task 051.
 *   - `'failed'`             → non-2xx / network error (non-fatal — editor remains usable,
 *                              heartbeat still attempts; banner surfaces the warning)
 *   - `'cancelled'`          → user clicked "Cancel — close this tab" in the conflict
 *                              dialog. Editor unmounts; empty-state with informational
 *                              banner is shown by the host. Task 051.
 *
 * Per Spike #3 §9 + dispatcher direction: checkout is a Dataverse-side lock
 * acquired AFTER load succeeds; load + checkout are independent calls that
 * happen to be sequenced for clarity. Checkout failure does NOT block editing
 * (degraded mode acceptable for R1).
 *
 * Same-user multi-tab detection (Task 051):
 *   DocumentCheckoutService.CheckoutAsync returns 200 OK for same-user re-checkout
 *   (idempotent per DocumentCheckoutService.cs:121-152). This means same-user
 *   multi-tab would silently lock both tabs to the same lock with no warning.
 *   Task 051 adds a separate GET /checkout-status probe BEFORE the checkout call:
 *     - If response.IsCheckedOut === true AND response.IsCurrentUser === true
 *       → transition to 'same-user-conflict' and render ComposeConflictDialog
 *     - Otherwise proceed with the existing 'acquiring' transition.
 *   The server already maps Azure AD OID → Dataverse systemuserid via
 *   LookupDataverseUserIdAsync (DocumentCheckoutService.cs:442-455); the client
 *   does NOT need to resolve the user's systemuserid itself.
 */
type ComposeCheckoutStatus =
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
 * Mirrors `src/server/api/Sprk.Bff.Api/Models/CheckoutModels.cs` — kept narrow
 * (only `name` is displayed in R1 — `id` is reserved for task 051 same-user
 * detection; `email` is not surfaced per minimum-data UX).
 */
interface ComposeCheckoutLockedByInfo {
  id: string;
  name: string;
  /** ISO timestamp of when the conflicting lock was acquired. */
  checkedOutAt: string | null;
}

interface ComposeWorkspaceState {
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
  /** Populated only when `checkoutStatus === 'same-user-conflict'` (lock holder is THIS user, another session). Task 051. */
  sameUserConflictInfo: { checkedOutAt: string | null } | null;
  /** User-facing checkout failure message (set only when status is `'failed'`). Tier 1 safe (HTTP status + correlation only — never document content). */
  checkoutFailureMessage: string | null;
}

type ComposeWorkspaceAction =
  | { kind: 'requestLoad'; documentRef: ComposeDocumentRef; sessionId: string }
  | {
      kind: 'loadSucceeded';
      docxBytes: ArrayBuffer;
      etag: string | null;
      sessionId: string;
      // The server may have promoted the document on first-Save earlier; if
      // the response includes a `sprkDocumentId`, we update the ref.
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

const INITIAL_STATE: ComposeWorkspaceState = {
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

function reducer(state: ComposeWorkspaceState, action: ComposeWorkspaceAction): ComposeWorkspaceState {
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
      // Only valid from 'loaded' — guard defensively.
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
      // Stay on 'loaded' (the editor's content is intact) but surface the error.
      return { ...state, status: 'loaded', errorMessage: action.errorMessage };
    case 'reset':
      return INITIAL_STATE;
    case 'importWarnings':
      return { ...state, importWarnings: action.warnings };
    case 'pendingAssistantInsert':
      return { ...state, pendingAssistantInsert: action.payload };
    case 'clearPendingAssistantInsert':
      return { ...state, pendingAssistantInsert: null };
    // ── Task 050 (Spike #3 §9): SPE check-out lifecycle ─────────────────────
    case 'checkoutSkipped':
      // Path B ephemeral: no sprkDocumentId yet. Set terminal-but-recoverable
      // status so the UI can render an informational banner and the next
      // post-Save-promotion mount can retry.
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
      // 200 OK — current user holds the lock (own or idempotent re-checkout).
      // Heartbeat (W4-045) keeps it alive on its own schedule.
      return {
        ...state,
        checkoutStatus: 'acquired',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutConflict':
      // 409 — locked by a DIFFERENT user. Same-user re-checkout returns 200
      // per DocumentCheckoutService idempotency; task 051 detects the
      // same-user multi-tab case via a separate /checkout-status probe.
      return {
        ...state,
        checkoutStatus: 'conflict',
        checkoutLockedBy: action.lockedBy,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutFailed':
      // Network / 5xx — non-fatal. Editor remains usable in degraded mode;
      // the banner informs the user that the lock could not be acquired.
      return {
        ...state,
        checkoutStatus: 'failed',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: action.failureMessage,
      };
    // ── Task 051 (Spike #3 §1 multi-tab UX): probe + same-user conflict ───
    case 'checkoutProbeRequested':
      // GET /checkout-status in flight. Predecessor of 'acquiring' — replaces
      // the direct 'idle' → 'acquiring' transition from task 050.
      return {
        ...state,
        checkoutStatus: 'probing',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutSameUserConflict':
      // Probe revealed THIS user holds the lock from another session.
      // Render ComposeConflictDialog; user must choose Force-close / Go-to-
      // other / Cancel. No automatic resolution.
      return {
        ...state,
        checkoutStatus: 'same-user-conflict',
        checkoutLockedBy: null,
        sameUserConflictInfo: { checkedOutAt: action.checkedOutAt },
        checkoutFailureMessage: null,
      };
    case 'checkoutDiscarding':
      // POST /discard in flight as the "force-close" step before acquiring
      // a fresh lock in this tab.
      return {
        ...state,
        checkoutStatus: 'discarding',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutCancelled':
      // User cancelled out of the conflict dialog. Editor remains in
      // 'loaded' state but checkout is unresolved; the host shows an
      // informational banner. The user can refresh to retry.
      return {
        ...state,
        checkoutStatus: 'cancelled',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    default:
      // Exhaustiveness check
      return state;
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * Props for `ComposeWorkspace`.
 *
 * The orchestrator is controlled by an optional `initialDocumentRef` — when
 * supplied, the workspace begins in `loading` state and fetches DOCX bytes
 * immediately; when omitted, it begins in `empty` state and surfaces the
 * Browse + Search empty-state CTAs.
 */
export interface ComposeWorkspaceProps {
  /**
   * Optional initial document pointer. When supplied, the workspace fetches
   * DOCX bytes on mount via `GET /api/compose/documents/{speId}?driveId=…`.
   * When `undefined` or `null`, renders `ComposeEmptyState` (Path A/B picker).
   */
  initialDocumentRef?: ComposeDocumentRef | null;

  /**
   * Optional initial ChatSession id (correlation). When supplied with
   * `initialDocumentRef`, threads through Flow 1/2/5 payloads. Empty string
   * disables Flow 2's "summarize" affordance (matches ComposeToolbar's
   * `sessionId` contract).
   */
  initialSessionId?: string;

  /**
   * BFF base URL (host only, e.g. `https://host.azurewebsites.net`). Required
   * for load/save fetch + threaded through ComposeEditor (heartbeat) and
   * ComposeToolbar (open-in-Word handoff).
   *
   * When empty, the workspace renders but Load/Save/Open-in-Word are all
   * disabled and a MessageBar surfaces the misconfiguration.
   */
  bffBaseUrl: string;

  /**
   * SPE driveId and tenantId — required query params for the BFF Load endpoint
   * (see `ComposeEndpoints.cs Load`). The orchestrator passes them on every
   * load + save call. When either is empty, load is suppressed and an error
   * MessageBar is shown.
   *
   * NOTE: the driveId is the SPE *drive* id (container drive). The
   * `documentRef.speDriveItemId` is the per-item id WITHIN that drive.
   */
  driveId: string;

  /** Microsoft Entra tenant id (multi-tenant scoping per ADR-015 Tier 3). */
  tenantId: string;

  /**
   * Called when the user clicks Browse / open file in the empty state. The
   * host wires this to the SPE picker (Path B per design.md §8). Receives no
   * arguments — the picker handles its own flow and ultimately calls back via
   * the (planned) PaneEventBus `compose_browse_requested` dispatcher.
   *
   * In R1, the host typically dispatches a workspace-channel event so the
   * existing modal-launch UX can be reused. This is currently CONTRACT-ONLY
   * (no concrete receiver until Phase 5 / R2); the empty-state click only
   * dispatches the event and the host listens.
   */
  onBrowseRequested?: () => void;

  /**
   * Called when the user clicks Search for Document in the empty state.
   * Host wires this to the existing Spaarke Document picker (Path A).
   * Same contract pattern as `onBrowseRequested`.
   */
  onSearchRequested?: () => void;

  /**
   * Called once the workspace has mounted into the DOM. Host can use this as
   * a signal that the embedded surface is alive (per
   * LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md §6 lifecycle hooks).
   */
  onComposeMount?: () => void;

  /**
   * Called when the workspace is about to unmount. Host should release any
   * external resources allocated for this surface (per the same contract).
   */
  onComposeUnmount?: () => void;

  /** Optional className passed to the root container (for host styling). */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
    overflow: 'hidden',
  },
  toolbarSlot: {
    flexShrink: 0,
  },
  bannerStack: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
  editorSlot: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  loadingState: {
    display: 'flex',
    flex: 1,
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `ComposeWorkspace` — the workspace-level orchestrator.
 *
 * Renders one of:
 *   - `ComposeEmptyState` (status === 'empty')
 *   - Spinner with "Loading document…" caption (status === 'loading')
 *   - ComposeToolbar + ComposeEditor (status === 'loaded' | 'saving')
 *   - MessageBar with the error + retry affordance (status === 'error')
 *
 * Wires the three R1 PaneEventBus subscribers (Flow 1, 2, 5) per the locked
 * COMPOSE_FLOW_RECEIVER_MATRIX. Flow 1 + 2 receivers are LOG-only per the
 * matrix (R2 wires real behaviour); Flow 5 receiver stages a pending
 * insertion and surfaces the user-confirm UX (BINDING per Spike #2 §10.3).
 *
 * Threading:
 *   - bffBaseUrl  → ComposeToolbar (open-in-Word), ComposeEditor (heartbeat),
 *                   load/save fetch.
 *   - documentRef → ComposeEditor (heartbeat keying + Flow 1/2 dispatch),
 *                   ComposeToolbar (Open-in-Word + Summarize dispatch).
 *   - sessionId   → ComposeEditor (Flow 1/2 sessionId), ComposeToolbar
 *                   (Summarize sessionId), subscribers' correlation.
 */
export function ComposeWorkspace(props: ComposeWorkspaceProps): React.JSX.Element {
  const styles = useStyles();
  const {
    initialDocumentRef,
    initialSessionId,
    bffBaseUrl,
    driveId,
    tenantId,
    onBrowseRequested,
    onSearchRequested,
    onComposeMount,
    onComposeUnmount,
    className,
  } = props;

  const [state, dispatch] = React.useReducer(reducer, INITIAL_STATE);

  // Imperative editor ref for save (TipTap → DOCX bytes via the W4-045
  // ComposeEditorHandle.serialize() method).
  const editorRef = React.useRef<ComposeEditorHandle | null>(null);

  // Stable PaneEventBus dispatch (used by Flow 5 stub behaviour + the
  // observer-callback wiring out of ComposeToolbar).
  const busDispatch = useDispatchPaneEvent();

  // -------------------------------------------------------------------------
  // Mount/Unmount host hooks per LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT §6
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    onComposeMount?.();
    return () => {
      onComposeUnmount?.();
    };
    // Intentionally fire-once: the contract is "mounted into DOM" / "about to
    // unmount". Mid-life prop changes don't re-fire these hooks.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // -------------------------------------------------------------------------
  // Kick off the initial load if initialDocumentRef is supplied
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    if (!initialDocumentRef) return;
    if (state.status !== 'empty' && state.status !== 'error') return;
    if (!initialDocumentRef.speDriveItemId) return;

    dispatch({
      kind: 'requestLoad',
      documentRef: initialDocumentRef,
      sessionId: initialSessionId ?? '',
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialDocumentRef?.speDriveItemId]);

  // -------------------------------------------------------------------------
  // BFF Load — GET /api/compose/documents/{speId}?driveId&tenantId&documentRecordId&displayName
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    if (state.status !== 'loading') return;
    if (!state.documentRef) return;
    if (!bffBaseUrl) {
      dispatch({
        kind: 'loadFailed',
        errorMessage: 'BFF base URL is not configured. Cannot load document.',
      });
      return;
    }
    if (!driveId || !tenantId) {
      dispatch({
        kind: 'loadFailed',
        errorMessage:
          'SPE drive id and tenant id are required to load Compose documents. ' +
          'Check the host configuration.',
      });
      return;
    }

    const ac = new AbortController();
    const docRef = state.documentRef;

    (async () => {
      try {
        const qs = new URLSearchParams({
          driveId,
          tenantId,
        });
        if (docRef.sprkDocumentId) {
          qs.set('documentRecordId', docRef.sprkDocumentId);
        }
        if (docRef.fileName) {
          qs.set('displayName', docRef.fileName);
        }

        const url = `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(
          docRef.speDriveItemId
        )}?${qs.toString()}`;

        const response = await authenticatedFetch(url, {
          method: 'GET',
          signal: ac.signal,
        });

        if (!response.ok) {
          // Defensive: surface a user-friendly message; do NOT log document
          // content (Tier 3). Status alone is Tier 1 safe.
          const msg =
            response.status === 404
              ? 'Document not found. It may have been deleted or moved.'
              : response.status === 403
                ? 'You do not have permission to open this document.'
                : `Failed to load document (HTTP ${response.status}).`;
          dispatch({ kind: 'loadFailed', errorMessage: msg });
          return;
        }

        const payload = (await response.json()) as {
          documentSpeId: string;
          driveId: string;
          sessionId: string;
          documentRecordId?: string;
          content: number[];
          eTag?: string;
          fileName?: string;
          size: number;
          correlationId?: string;
        };

        // The BFF returns content as a base64-decoded number[] (per
        // ComposeEndpoints.Load → result.Content.ToArray()). Convert back to
        // an ArrayBuffer for TipTap/mammoth.
        const bytes = new Uint8Array(payload.content ?? []);
        if (ac.signal.aborted) return;
        dispatch({
          kind: 'loadSucceeded',
          docxBytes: bytes.buffer,
          etag: payload.eTag ?? null,
          sessionId: payload.sessionId ?? '',
          sprkDocumentId: payload.documentRecordId,
          fileName: payload.fileName,
        });
      } catch (err) {
        if (ac.signal.aborted) return;
        // Network/parse errors are Tier 1 safe to surface (we DO NOT include
        // any document content in the message).
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'loadFailed',
          errorMessage: `Failed to load document: ${message}`,
        });
      }
    })();

    return () => ac.abort();
    // The reducer transition into 'loading' is the only trigger; subsequent
    // state changes (loaded/error) do NOT re-fire this effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.status, state.documentRef?.speDriveItemId, bffBaseUrl, driveId, tenantId]);

  // -------------------------------------------------------------------------
  // BFF Check-out acquisition + multi-tab probe
  // -------------------------------------------------------------------------
  //
  // Task 050 (Spike #3 §9 LOCKED — Path A approved 2026-06-29) — checkout acquisition.
  // Task 051 (Spike #3 §1 + §9; FR-16) — multi-tab conflict UX via /checkout-status probe.
  //
  // Per Spike #3 §1 (TL;DR) + §9 task direction summary, R1 reuses the existing
  // `DocumentCheckoutService.CheckoutAsync` via the existing endpoint
  // `POST /api/documents/{documentId}/checkout` (mapped in
  // `src/server/api/Sprk.Bff.Api/Api/DocumentOperationsEndpoints.cs:30`).
  // The endpoint is documentId-keyed (Dataverse GUID), NOT speDriveItemId-keyed.
  //
  // Trigger conditions (all required):
  //   1. status === 'loaded' — load just succeeded (or save just promoted)
  //   2. documentRef.sprkDocumentId is present — required by the endpoint;
  //      Path B ephemeral docs (no sprk_documents row yet) cannot acquire
  //      a Dataverse-side lock until first-Save promotion creates the row.
  //      Per spike §9 + Spike #3 §5 PromoteEphemeralAsync algorithm, the
  //      saveSucceeded reducer captures the post-promotion sprkDocumentId,
  //      naturally retriggering this effect on the next render.
  //   3. checkoutStatus === 'idle' — single-shot per documentRef.sprkDocumentId
  //      (no auto-retry on conflict/failure; user can refresh to retry).
  //
  // Endpoint response semantics (from DocumentCheckoutService.CheckoutAsync):
  //   200 OK — current user holds the lock. Idempotent for same user (re-mounts,
  //            multi-tab same-user) — DocumentCheckoutService.CheckoutAsync
  //            line 121-152 detects existing same-user checkout and returns the
  //            existing lock. So same-user multi-tab from THIS code path lands
  //            in `acquired`. Task 051 detects the multi-tab case via a separate
  //            checkout-status probe before mount.
  //   404 Problem — document not found in Dataverse. Treated as `failed`
  //                 (non-fatal — editor remains usable; user can still edit
  //                 and Save, which will trigger promotion if needed).
  //   409 Json (DocumentLockedError) — locked by a DIFFERENT user. Render
  //             conflict banner with locked-by info (R1 scope); task 051 owns
  //             the richer "Go-to-other / Force-close" UX.
  //   5xx Problem — network / server error. Treated as `failed`.
  //
  // ADR-028 (Spaarke Auth v2): `authenticatedFetch` from `@spaarke/auth` is
  // the only acceptable mechanism — never raw fetch with manual Bearer header.
  //
  // ADR-015 Tier 3: log Tier-1 metadata only (status, correlationId, locked-by
  // name). NEVER log document content, lock token internals, or other Tier 3.
  //
  // Failure mode (degraded but recoverable): if the checkout call fails for
  // any reason, the editor is still usable; the heartbeat (W4-045 ComposeEditor)
  // will surface a separate banner if its own keep-alive fails 3x consecutively.
  // We do NOT block editing on checkout success — Spike #3 §6 rationale: the
  // Spaarke-layer Compose multi-tab UX (task 051) is the authoritative
  // conflict detector, not the Dataverse lock; failing-closed would degrade
  // R1 UX without proportionate safety gain.
  // Forward declaration — `runCheckout` is defined below as a useCallback. We
  // pass it via ref-stable function reference to the effect that orchestrates
  // probe + acquire. This avoids re-creating the effect on every render.
  const checkoutAcquireFnRef = React.useRef<((sprkDocumentId: string) => Promise<void>) | null>(null);

  React.useEffect(() => {
    // Guard: only fire when loaded AND checkout has not yet been attempted
    // for the current documentRef. `'idle'` covers the initial post-load case;
    // `'skipped'` covers the Path B → Path A transition (post-Save-promotion:
    // sprkDocumentId just became available, so we retry the acquisition).
    // `'acquired'` / `'conflict'` / `'same-user-conflict'` / `'failed'` /
    // `'cancelled'` are terminal — no auto-retry.
    if (state.status !== 'loaded') return;
    if (state.checkoutStatus !== 'idle' && state.checkoutStatus !== 'skipped') return;
    if (!state.documentRef) return;

    // BFF config sanity — same guard as load; defensive against misconfigured host.
    if (!bffBaseUrl) {
      dispatch({
        kind: 'checkoutFailed',
        failureMessage: 'BFF base URL is not configured. Lock could not be acquired.',
      });
      return;
    }

    // Path B ephemeral docs: no sprkDocumentId means the document hasn't been
    // promoted to a sprk_documents row yet. The existing checkout endpoint is
    // documentId-keyed; we cannot call it. Mark `skipped` and rely on the next
    // Save (which triggers Spike #3 §5 PromoteEphemeralAsync); the new
    // sprkDocumentId arrives via saveSucceeded → reducer updates documentRef
    // → this effect re-runs with checkoutStatus === 'idle' (because the
    // documentRef key in dependencies changed) and acquires the lock then.
    //
    // NOTE: reducer.saveSucceeded does NOT currently reset checkoutStatus to
    // 'idle'. The dependency on `state.documentRef?.sprkDocumentId` below is
    // what re-evaluates this effect; the guard above (`!== 'idle'`) will
    // prevent re-fire UNLESS we reset on Path-B-to-Path-A transition. To keep
    // R1 changes minimal, the saveSucceeded path will see `checkoutStatus`
    // still set to 'skipped'; we additionally check `'skipped'` as a valid
    // pre-condition to retry. This avoids touching the saveSucceeded reducer.
    const sprkDocumentId = state.documentRef.sprkDocumentId;
    if (!sprkDocumentId) {
      // Path B ephemeral: no sprkDocumentId. Only dispatch on the idle →
      // skipped transition; if already 'skipped' (e.g., re-render with
      // unchanged documentRef), no-op to avoid an infinite reducer loop
      // (useReducer compares by reference, so dispatching an action that
      // returns a structurally-identical-but-new state object still re-renders).
      if (state.checkoutStatus === 'idle') {
        dispatch({ kind: 'checkoutSkipped' });
      }
      return;
    }

    const ac = new AbortController();

    // ─────────────────────────────────────────────────────────────────────────
    // Task 051 (Spike #3 §1 + §9): Multi-tab probe BEFORE checkout.
    //
    // DocumentCheckoutService.CheckoutAsync returns 200 OK (idempotent) for
    // same-user re-checkout (DocumentCheckoutService.cs:121-152). This means
    // calling /checkout directly from a second tab of the same user would
    // silently succeed with no warning, and both tabs would end up sharing
    // the same lock state — a confusing UX.
    //
    // The /checkout-status endpoint returns CheckoutStatusInfo:
    //   {
    //     isCheckedOut: bool,
    //     checkedOutBy: { id, name, email? } | null,
    //     checkedOutAt: ISO string | null,
    //     isCurrentUser: bool                ← KEY FIELD
    //   }
    //
    // The server already maps Azure AD OID → Dataverse systemuserid via
    // LookupDataverseUserIdAsync (DocumentCheckoutService.cs:442-455). The
    // frontend therefore does NOT need to resolve the current user's
    // systemuserid itself — it just reads `isCurrentUser`.
    //
    // Decision tree:
    //   probe → response.isCheckedOut === true AND response.isCurrentUser === true
    //         → SAME USER, ANOTHER SESSION → render ComposeConflictDialog (FR-16)
    //   probe → response.isCheckedOut === false (no lock) OR
    //          (response.isCheckedOut === true AND response.isCurrentUser === false)
    //         → either no conflict (proceed normally) OR cross-user 409 case
    //           (the existing /checkout call will return 409; existing code path
    //           handles it). EITHER WAY, proceed to /checkout.
    //   probe → fails (network, 404, 5xx) → log + proceed to /checkout. The
    //           probe is a UX enhancement, not a hard correctness gate; failing
    //           to probe should not block the user.
    // ─────────────────────────────────────────────────────────────────────────

    const probeUrl = buildBffApiUrl(
      bffBaseUrl,
      `/documents/${encodeURIComponent(sprkDocumentId)}/checkout-status`
    );

    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] SPE check-out probe requested', {
      sprkDocumentId,
      sessionId: state.sessionId,
    });
    dispatch({ kind: 'checkoutProbeRequested' });

    (async () => {
      // ── Step 1: Probe checkout-status ───────────────────────────────────
      let probeIsCurrentUser = false;
      let probeCheckedOutAt: string | null = null;
      let probeSucceeded = false;
      try {
        const probeResponse = await authenticatedFetch(probeUrl, {
          method: 'GET',
          signal: ac.signal,
        });
        if (ac.signal.aborted) return;

        if (probeResponse.ok) {
          probeSucceeded = true;
          try {
            const probeBody = (await probeResponse.json()) as {
              isCheckedOut?: boolean;
              checkedOutBy?: { id?: string; name?: string } | null;
              checkedOutAt?: string | null;
              isCurrentUser?: boolean;
            };
            probeIsCurrentUser =
              probeBody.isCheckedOut === true && probeBody.isCurrentUser === true;
            probeCheckedOutAt = probeBody.checkedOutAt ?? null;
            // eslint-disable-next-line no-console
            console.info('[ComposeWorkspace] SPE check-out probe result', {
              sprkDocumentId,
              isCheckedOut: probeBody.isCheckedOut,
              isCurrentUser: probeBody.isCurrentUser,
              // Intentionally NOT logged: checkedOutBy.name (Tier 1 OK but
              // unnecessary noise here; logged in the conflict dispatch below).
            });
          } catch {
            // Body parse failure — treat as probe-failed and fall through to
            // direct checkout. Defensive: server returns valid JSON.
            probeSucceeded = false;
          }
        } else {
          // 404 / 401 / 5xx — probe failed but non-fatal. Log Tier-1 safe
          // (status code only) and fall through to direct checkout. The
          // /checkout call will surface the same error if applicable.
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out probe non-OK', {
            sprkDocumentId,
            status: probeResponse.status,
          });
        }
      } catch (err) {
        if (ac.signal.aborted) return;
        // Network error on probe. Tier-1 safe message; fall through to
        // direct checkout (the call may succeed if the network blip was
        // transient).
        const message = err instanceof Error ? err.message : String(err);
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] SPE check-out probe error', {
          sprkDocumentId,
          error: message,
        });
      }

      if (ac.signal.aborted) return;

      // ── Step 2: Branch on probe result ──────────────────────────────────
      if (probeSucceeded && probeIsCurrentUser) {
        // SAME USER, ANOTHER SESSION → render ComposeConflictDialog.
        // The dialog buttons trigger:
        //   - Force-close → handleForceCloseOtherSession (defined below):
        //                   POST /discard, then re-run checkout.
        //   - Go-to-other → handleGoToOtherSession (defined below):
        //                   post BroadcastChannel focus message + cancel.
        //   - Cancel      → handleConflictCancelled (defined below):
        //                   transition to 'cancelled' status.
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] SPE check-out same-user multi-tab conflict detected', {
          sprkDocumentId,
          checkedOutAt: probeCheckedOutAt,
        });
        dispatch({
          kind: 'checkoutSameUserConflict',
          checkedOutAt: probeCheckedOutAt,
        });
        return;
      }

      // ── Step 3: No same-user conflict → proceed with /checkout ──────────
      // Delegate to the ref-stable checkout function. This keeps the
      // /checkout HTTP call logic in one place (also reusable from the
      // force-close path below).
      const acquireFn = checkoutAcquireFnRef.current;
      if (acquireFn) {
        await acquireFn(sprkDocumentId);
      }
    })();

    return () => ac.abort();
    // Dependencies: re-evaluate whenever the loaded document's sprkDocumentId
    // changes (Path B promotion case) or status transitions. The 'idle' guard
    // above prevents double-fire on the same documentRef.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.status, state.documentRef?.sprkDocumentId, bffBaseUrl]);

  // -------------------------------------------------------------------------
  // Checkout acquisition function — extracted for reuse by:
  //   (a) the probe orchestration effect above (normal post-load path)
  //   (b) the force-close handler below (post-discard re-checkout path)
  //
  // Per ADR-028 uses `authenticatedFetch` from `@spaarke/auth`.
  // Per ADR-015 Tier 3: log status + correlationId only, never doc content.
  // -------------------------------------------------------------------------
  const runCheckout = React.useCallback(
    async (sprkDocumentId: string): Promise<void> => {
      if (!bffBaseUrl) return;
      const ac = new AbortController();
      const url = buildBffApiUrl(
        bffBaseUrl,
        `/documents/${encodeURIComponent(sprkDocumentId)}/checkout`
      );

      // eslint-disable-next-line no-console
      console.info('[ComposeWorkspace] SPE check-out requested', {
        sprkDocumentId,
        sessionId: state.sessionId,
      });
      dispatch({ kind: 'checkoutRequested' });

      try {
        const response = await authenticatedFetch(url, {
          method: 'POST',
          signal: ac.signal,
        });

        if (ac.signal.aborted) return;

        if (response.ok) {
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out acquired', {
            sprkDocumentId,
            status: response.status,
          });
          dispatch({ kind: 'checkoutAcquired' });
          return;
        }

        if (response.status === 409) {
          // 409 — DocumentLockedError JSON body (per CheckoutModels.cs:49).
          // Cross-user only — same-user multi-tab is handled by the probe
          // above and never reaches this branch under normal conditions.
          let lockedBy: ComposeCheckoutLockedByInfo = {
            id: '',
            name: 'Unknown user',
            checkedOutAt: null,
          };
          try {
            const body = (await response.json()) as {
              error?: string;
              detail?: string;
              checkedOutBy?: { id?: string; name?: string; email?: string | null };
              checkedOutAt?: string | null;
            };
            lockedBy = {
              id: body.checkedOutBy?.id ?? '',
              name: body.checkedOutBy?.name ?? 'Another user',
              checkedOutAt: body.checkedOutAt ?? null,
            };
          } catch {
            // Body parse failure — fall through with default unknown-user info.
          }
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out conflict', {
            sprkDocumentId,
            lockedByName: lockedBy.name,
            checkedOutAt: lockedBy.checkedOutAt,
          });
          dispatch({ kind: 'checkoutConflict', lockedBy });
          return;
        }

        const failureMessage =
          response.status === 404
            ? 'This document is not yet recorded in Spaarke. The lock will be acquired after first save.'
            : response.status === 403
              ? 'You do not have permission to lock this document.'
              : `Could not acquire document lock (HTTP ${response.status}). You may continue editing — changes will save normally.`;
        dispatch({ kind: 'checkoutFailed', failureMessage });
      } catch (err) {
        if (ac.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'checkoutFailed',
          failureMessage: `Could not acquire document lock: ${message}`,
        });
      }
    },
    [bffBaseUrl, state.sessionId]
  );

  // Keep the ref pointing to the latest runCheckout so the probe effect can
  // call it without re-creating itself on every render.
  React.useEffect(() => {
    checkoutAcquireFnRef.current = runCheckout;
  }, [runCheckout]);

  // -------------------------------------------------------------------------
  // Task 051 — Multi-tab BroadcastChannel signaling + handlers
  // -------------------------------------------------------------------------
  //
  // Each Compose tab registers a BroadcastChannel keyed by sprkDocumentId
  // so that:
  //   - "Go to that session" can post a `focus-me` message; the original
  //     tab (which is also listening on the same channel) calls
  //     `window.focus()` to bring itself forward.
  //   - "Force-close other session and open here" posts a `force-closed`
  //     message AFTER the discard call succeeds; the original tab can use
  //     this signal to unmount its editor + show a "session ended in
  //     another tab" notice.
  //
  // Per the BroadcastChannel API, channels are scoped to the same origin —
  // they do NOT cross browser profiles or incognito windows. For R1 this is
  // acceptable; cross-profile multi-tab is rare in legal-ops workflows.
  //
  // The original tab cannot be focused via JavaScript across documents in
  // most browsers WITHOUT a user-initiated gesture in the receiving tab.
  // `window.focus()` works in practice for same-origin tabs in Chromium and
  // Firefox if the page is the active document; if it doesn't focus, the
  // user simply sees the dialog dismiss in THIS tab and must switch tabs
  // manually. R1 best-effort.

  const conflictChannel = React.useMemo(() => {
    // Use sprkDocumentId as the channel key. Same-document multi-tab = same
    // channel; different-document multi-tab = different channels = no
    // cross-talk.
    if (typeof BroadcastChannel === 'undefined') return null; // jsdom / older browsers
    const id = state.documentRef?.sprkDocumentId;
    if (!id) return null;
    return new BroadcastChannel(`compose:lock:${id}`);
  }, [state.documentRef?.sprkDocumentId]);

  // Listen for force-closed / focus-me messages from sibling tabs.
  React.useEffect(() => {
    if (!conflictChannel) return;
    const handler = (ev: MessageEvent) => {
      const data = ev.data as { type?: string; sessionId?: string } | undefined;
      if (!data || typeof data !== 'object' || !data.type) return;
      // Don't react to own messages (BroadcastChannel does this by default,
      // but defensive in case of polyfill quirks).
      if (data.sessionId && data.sessionId === state.sessionId) return;

      if (data.type === 'focus-me') {
        // Another tab is asking THIS tab to come forward.
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] BroadcastChannel focus-me received');
        try {
          window.focus();
        } catch {
          // Browser blocked the focus — no-op.
        }
      } else if (data.type === 'force-closed') {
        // Another tab took the lock via force-close. We must unmount our
        // editor (we no longer hold the lock).
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] BroadcastChannel force-closed received');
        dispatch({ kind: 'checkoutCancelled' });
        // The host's empty-state will surface the "session ended elsewhere"
        // banner via state.checkoutStatus === 'cancelled' (R1 surfaces it as
        // an informational MessageBar in the banner stack).
      }
    };
    conflictChannel.addEventListener('message', handler);
    return () => {
      conflictChannel.removeEventListener('message', handler);
    };
  }, [conflictChannel, state.sessionId]);

  // Cleanup channel on unmount or document change.
  React.useEffect(() => {
    return () => {
      conflictChannel?.close();
    };
  }, [conflictChannel]);

  // -------------------------------------------------------------------------
  // Task 051 — Conflict dialog handlers
  // -------------------------------------------------------------------------

  /**
   * "Go to that session" — FR-16 verbatim.
   *
   * Posts a `focus-me` message on the BroadcastChannel and transitions to
   * 'cancelled' (this tab no longer attempts to hold the lock). The
   * original tab focuses itself if it's listening.
   */
  const handleGoToOtherSession = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Go to that session');
    try {
      conflictChannel?.postMessage({ type: 'focus-me', sessionId: state.sessionId });
    } catch {
      // Channel close races etc. — best-effort.
    }
    dispatch({ kind: 'checkoutCancelled' });
  }, [conflictChannel, state.sessionId]);

  /**
   * "Force-close other session and open here" — FR-16 verbatim.
   *
   * 1. POST /api/documents/{sprkDocumentId}/discard to release the lock.
   *    Per DocumentCheckoutService.DiscardAsync, only the lock owner (which
   *    is the current user) can discard — returns 200 OK.
   * 2. On success, post a `force-closed` message on BroadcastChannel so the
   *    original tab can unmount itself.
   * 3. Call runCheckout to acquire a fresh lock in THIS tab.
   * 4. On discard failure, transition to 'failed' so the user can retry.
   */
  const handleForceCloseOtherSession = React.useCallback(async (): Promise<void> => {
    const sprkDocumentId = state.documentRef?.sprkDocumentId;
    if (!sprkDocumentId || !bffBaseUrl) {
      dispatch({
        kind: 'checkoutFailed',
        failureMessage: 'Cannot force-close: missing document id or BFF configuration.',
      });
      return;
    }

    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Force-close other session', {
      sprkDocumentId,
    });
    dispatch({ kind: 'checkoutDiscarding' });

    const discardUrl = buildBffApiUrl(
      bffBaseUrl,
      `/documents/${encodeURIComponent(sprkDocumentId)}/discard`
    );

    try {
      const discardResponse = await authenticatedFetch(discardUrl, { method: 'POST' });
      if (!discardResponse.ok) {
        // 403 NotAuthorized would mean we mistook another user's lock for
        // our own — defensive. 400 NotCheckedOut means the lock was already
        // released between probe and discard — race-but-OK; proceed to
        // checkout.
        if (discardResponse.status === 400) {
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] Discard 400 — lock already released, proceeding');
        } else {
          const failureMessage =
            discardResponse.status === 403
              ? 'You do not have permission to release this lock.'
              : `Could not force-close other session (HTTP ${discardResponse.status}).`;
          dispatch({ kind: 'checkoutFailed', failureMessage });
          return;
        }
      }

      // eslint-disable-next-line no-console
      console.info('[ComposeWorkspace] Discard succeeded, posting force-closed message', {
        sprkDocumentId,
      });
      try {
        conflictChannel?.postMessage({
          type: 'force-closed',
          sessionId: state.sessionId,
        });
      } catch {
        // Best-effort.
      }

      // Now acquire a fresh lock in this tab.
      await runCheckout(sprkDocumentId);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      dispatch({
        kind: 'checkoutFailed',
        failureMessage: `Could not force-close other session: ${message}`,
      });
    }
  }, [state.documentRef?.sprkDocumentId, state.sessionId, bffBaseUrl, conflictChannel, runCheckout]);

  /**
   * "Cancel — close this tab" — third option (non-FR-16 escape hatch).
   *
   * The user chose neither action. We transition to 'cancelled'; the host's
   * banner stack surfaces an informational message. The editor remains
   * visible (state.status === 'loaded') but checkout is unresolved — the
   * user can navigate away or refresh to retry.
   */
  const handleConflictCancelled = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Cancel');
    dispatch({ kind: 'checkoutCancelled' });
  }, []);

  // -------------------------------------------------------------------------
  // BFF Save — POST /api/compose/documents/{speId}/save
  // -------------------------------------------------------------------------
  const triggerSave = React.useCallback(async (): Promise<void> => {
    if (state.status !== 'loaded') return;
    if (!state.documentRef || !editorRef.current) return;
    if (!bffBaseUrl || !driveId || !tenantId) {
      dispatch({
        kind: 'saveFailed',
        errorMessage: 'Cannot save — BFF base URL or SPE configuration missing.',
      });
      return;
    }

    dispatch({ kind: 'requestSave' });
    try {
      const bytes = await editorRef.current.serialize();
      const url = `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(
        state.documentRef.speDriveItemId
      )}/save`;

      const response = await authenticatedFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          driveId,
          tenantId,
          sessionId: state.sessionId,
          content: Array.from(new Uint8Array(bytes)),
          documentRecordId: state.documentRef.sprkDocumentId ?? null,
          displayName: state.documentRef.fileName ?? null,
        }),
      });

      if (!response.ok) {
        const msg =
          response.status === 403
            ? 'You do not have permission to save this document.'
            : `Failed to save document (HTTP ${response.status}).`;
        dispatch({ kind: 'saveFailed', errorMessage: msg });
        return;
      }

      const payload = (await response.json()) as {
        documentSpeId: string;
        documentRecordId?: string;
        eTag?: string;
        size: number;
        wasPromotedThisSave: boolean;
      };

      dispatch({
        kind: 'saveSucceeded',
        sprkDocumentId: payload.documentRecordId,
        etag: payload.eTag ?? null,
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      dispatch({ kind: 'saveFailed', errorMessage: `Save failed: ${message}` });
    }
  }, [
    state.status,
    state.documentRef,
    state.sessionId,
    bffBaseUrl,
    driveId,
    tenantId,
  ]);

  // Keyboard shortcut: Ctrl/Cmd+S → save (per common editor convention).
  // The contract doesn't require this; it's a reasonable default that
  // doesn't conflict with browser-level shortcuts in normal Compose use.
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        void triggerSave();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [state.status, triggerSave]);

  // -------------------------------------------------------------------------
  // PaneEventBus subscribers — Flow 1, 2, 5 (R1 WIRED per matrix)
  // -------------------------------------------------------------------------

  /**
   * Flow 1 subscriber — `compose_selection_changed` on `context`.
   * R1: LOG only (Tier 1 safe metadata; never log selectionText).
   * R2: drives precedent / playbook / history lookup in Context pane.
   */
  usePaneEvent('context', (event: ContextPaneEvent) => {
    // Additive discriminant per ADR-030; narrow before reading.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const e = event as unknown as { type?: string };
    if (e.type !== 'compose_selection_changed') return;
    const narrowed = event as unknown as ComposeWorkspaceToContextFlow;
    // Tier-safe log: documentRef, sessionId, timestamp — NOT selectionText.
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Flow 1 (selection_changed) observed', {
      sessionId: narrowed.sessionId,
      timestamp: narrowed.timestamp,
      speId: narrowed.documentRef?.speDriveItemId,
    });
  });

  /**
   * Flow 2 subscriber — `compose_selection_offer` on `conversation`.
   * R1: LOG only. R2: ConversationPane renders Explain/Replace/Compare/Draft
   * action menu bound to the JPS scope.
   */
  usePaneEvent('conversation', (event: ConversationPaneEvent) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const e = event as unknown as { type?: string };
    if (e.type !== 'compose_selection_offer') return;
    const narrowed = event as unknown as ComposeWorkspaceToAssistantFlow;
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Flow 2 (selection_offer) observed', {
      sessionId: narrowed.sessionId,
      timestamp: narrowed.timestamp,
      jpsScope: narrowed.jpsScope,
      speId: narrowed.documentRef?.speDriveItemId,
    });
  });

  /**
   * Flow 5 subscriber — `compose_assistant_insert` on `workspace`.
   * R1: LOG + stage as `pendingAssistantInsert`. The user-confirm UX is
   * surfaced as a non-blocking MessageBar with an "Insert" action; on
   * confirm the host can drive insertion through ComposeEditor's commands.
   *
   * R1 BINDING (Spike #2 §10.3): even though the dispatcher may set
   * `requireUserConfirm: false`, R1 honors a forced manual-confirm gate to
   * avoid auto-injection UX risk pre-R2 actions. R2 will respect the flag.
   *
   * The actual insertion (TipTap commands) is NOT wired in R1 — the
   * MessageBar acknowledgment is sufficient for the contract validation;
   * R2 wires insertion via editor.commands.insertContent(...).
   */
  usePaneEvent('workspace', (event: WorkspacePaneEvent) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const e = event as unknown as { type?: string };
    if (e.type !== 'compose_assistant_insert') return;
    const narrowed = event as unknown as ComposeAssistantToWorkspaceFlow;
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Flow 5 (assistant_insert) observed', {
      sessionId: narrowed.sessionId,
      timestamp: narrowed.timestamp,
      sourceNodeId: narrowed.sourceNodeId,
      insertMode: narrowed.insertMode,
      // Intentionally NOT logged: contentHtml (Tier 3).
    });
    dispatch({ kind: 'pendingAssistantInsert', payload: narrowed });
  });

  // -------------------------------------------------------------------------
  // Empty-state handlers — additive workspace-channel dispatch
  // -------------------------------------------------------------------------

  const handleBrowseRequested = React.useCallback((): void => {
    // Fire the host callback first (preferred contract — host owns picker).
    onBrowseRequested?.();
    // Additionally, dispatch an additive event so any sibling pane that
    // subscribes to "compose_browse_requested" on workspace channel can react.
    // The shape is intentionally lean (no Tier 3 payload).
    busDispatch(
      'workspace',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      {
        type: 'compose_browse_requested',
        timestamp: new Date().toISOString(),
      } as any
    );
  }, [onBrowseRequested, busDispatch]);

  const handleSearchRequested = React.useCallback((): void => {
    onSearchRequested?.();
    busDispatch(
      'workspace',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      {
        type: 'compose_search_requested',
        timestamp: new Date().toISOString(),
      } as any
    );
  }, [onSearchRequested, busDispatch]);

  // -------------------------------------------------------------------------
  // Editor-side callbacks
  // -------------------------------------------------------------------------

  const handleDirtyChange = React.useCallback((_dirty: boolean): void => {
    // The reducer doesn't track a dirty flag separately — `state.status` is
    // the source of truth (the editor's own dirty bit is the in-flight signal
    // before a save is triggered). The host typically uses dirty state to
    // show a "Unsaved changes" indicator; that affordance is deferred to R2.
  }, []);

  const handleImportWarnings = React.useCallback(
    (warnings: Array<{ type: string; message: string }>): void => {
      dispatch({ kind: 'importWarnings', warnings });
    },
    []
  );

  // -------------------------------------------------------------------------
  // Toolbar observer callback — log compose-summarize dispatches
  // -------------------------------------------------------------------------
  const handleComposeSummarizeRequest = React.useCallback(
    (payload: ComposeSummarizeRequestEvent): void => {
      // The toolbar already dispatched on the bus; this is a courtesy hook so
      // the orchestrator can surface a toast or focus the Assistant pane in
      // R2. R1: log Tier-1 safe metadata only.
      // eslint-disable-next-line no-console
      console.info('[ComposeWorkspace] compose-summarize dispatched', {
        sessionId: payload.sessionId,
        timestamp: payload.timestamp,
        documentId: payload.documentRef.documentId,
      });
    },
    []
  );

  // -------------------------------------------------------------------------
  // Editor doc-ref shape (shared lib has its own narrower interface)
  // -------------------------------------------------------------------------
  const editorDocRef: ComposeEditorDocumentRef | undefined = state.documentRef
    ? {
        speDriveItemId: state.documentRef.speDriveItemId,
        sprkDocumentId: state.documentRef.sprkDocumentId,
        fileName: state.documentRef.fileName,
        containerId: state.documentRef.containerId,
      }
    : undefined;

  // -------------------------------------------------------------------------
  // Toolbar documentId (Open-in-Word handoff)
  // -------------------------------------------------------------------------
  // Per ComposeToolbar contract: documentId can be either SPE id (pre-promotion)
  // or sprk_documentid (post-promotion); the open-links BFF endpoint resolves
  // both shapes (per Spike #3 §2.3).
  const toolbarDocumentId =
    state.documentRef?.sprkDocumentId ?? state.documentRef?.speDriveItemId ?? '';

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  const showEditor = state.status === 'loaded' || state.status === 'saving';

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="region"
      aria-label={state.documentRef?.fileName ?? 'Compose workspace'}
      data-compose-workspace-status={state.status}
      data-compose-checkout-status={state.checkoutStatus}
      data-testid="compose-workspace"
    >
      {/* Empty state — Path A/B picker */}
      {state.status === 'empty' ? (
        <ComposeEmptyState
          onBrowseRequested={handleBrowseRequested}
          onSearchRequested={handleSearchRequested}
        />
      ) : null}

      {/* Loading spinner — while the BFF Load is in flight */}
      {state.status === 'loading' ? (
        <div
          className={styles.loadingState}
          role="status"
          aria-live="polite"
          data-testid="compose-workspace-loading"
        >
          <Spinner size="medium" />
          <Text size={300}>Loading document…</Text>
        </div>
      ) : null}

      {/* Loaded / Saving — toolbar + editor + banners */}
      {showEditor ? (
        <>
          <div className={styles.toolbarSlot}>
            <ComposeToolbar
              documentId={toolbarDocumentId}
              fileName={state.documentRef?.fileName}
              sessionId={state.sessionId}
              bffBaseUrl={bffBaseUrl}
              disabled={state.status === 'saving'}
              onComposeSummarizeRequest={handleComposeSummarizeRequest}
            />
          </div>

          {/* Banner stack — import warnings + save errors + pending assistant insert + SPE check-out status */}
          {(state.importWarnings.length > 0 ||
            state.errorMessage ||
            state.pendingAssistantInsert ||
            state.checkoutStatus === 'conflict' ||
            state.checkoutStatus === 'failed' ||
            state.checkoutStatus === 'cancelled') && (
            <div className={styles.bannerStack}>
              {state.errorMessage ? (
                <MessageBar
                  intent="error"
                  data-testid="compose-workspace-error-banner"
                  aria-live="polite"
                >
                  <MessageBarBody>
                    <MessageBarTitle>Save error</MessageBarTitle>
                    {state.errorMessage}
                  </MessageBarBody>
                </MessageBar>
              ) : null}
              {/*
                Task 050 (Spike #3 §9): SPE check-out conflict banner.
                Fires when 409 returned (cross-user only — same-user idempotent
                re-checkout returns 200). Task 051 swaps this static banner for
                the richer "Go-to-other / Force-close" multi-tab UX dialog.
              */}
              {state.checkoutStatus === 'conflict' && state.checkoutLockedBy ? (
                <MessageBar
                  intent="warning"
                  data-testid="compose-workspace-checkout-conflict-banner"
                  aria-live="polite"
                >
                  <MessageBarBody>
                    <MessageBarTitle>Document is checked out</MessageBarTitle>
                    {state.checkoutLockedBy.checkedOutAt
                      ? `Locked by ${state.checkoutLockedBy.name} since ${new Date(state.checkoutLockedBy.checkedOutAt).toLocaleString()}. You can view the document but changes cannot be saved until the lock is released.`
                      : `Locked by ${state.checkoutLockedBy.name}. You can view the document but changes cannot be saved until the lock is released.`}
                  </MessageBarBody>
                </MessageBar>
              ) : null}
              {/*
                Task 050: SPE check-out non-fatal failure banner (404 / 5xx /
                network). Editor remains usable; this is informational. Heartbeat
                (W4-045) reports its own keep-alive failures separately.
              */}
              {state.checkoutStatus === 'failed' && state.checkoutFailureMessage ? (
                <MessageBar
                  intent="info"
                  data-testid="compose-workspace-checkout-failed-banner"
                  aria-live="polite"
                >
                  <MessageBarBody>
                    <MessageBarTitle>Lock not acquired</MessageBarTitle>
                    {state.checkoutFailureMessage}
                  </MessageBarBody>
                </MessageBar>
              ) : null}
              {/*
                Task 051: Multi-tab conflict cancelled banner.
                Surfaces when the user dismissed the ComposeConflictDialog via
                "Cancel — close this tab" OR another tab took the lock via a
                force-closed BroadcastChannel message. Informational — the
                editor is still mounted but no lock is held by THIS tab.
              */}
              {state.checkoutStatus === 'cancelled' ? (
                <MessageBar
                  intent="info"
                  data-testid="compose-workspace-checkout-cancelled-banner"
                  aria-live="polite"
                >
                  <MessageBarBody>
                    <MessageBarTitle>This session is no longer active</MessageBarTitle>
                    This document is open in another Compose session. Refresh
                    this page to attempt to acquire the lock again, or close
                    this tab.
                  </MessageBarBody>
                </MessageBar>
              ) : null}
              {state.importWarnings.length > 0 ? (
                <MessageBar
                  intent="warning"
                  data-testid="compose-workspace-import-warning-banner"
                  aria-live="polite"
                >
                  <MessageBarBody>
                    <MessageBarTitle>
                      Document opened with {state.importWarnings.length} simplification(s)
                    </MessageBarTitle>
                    Some advanced features may not be preserved on save.
                  </MessageBarBody>
                </MessageBar>
              ) : null}
              {state.pendingAssistantInsert ? (
                <MessageBar
                  intent="info"
                  data-testid="compose-workspace-pending-assistant-banner"
                  aria-live="polite"
                >
                  <MessageBarBody>
                    <MessageBarTitle>Assistant draft ready</MessageBarTitle>
                    A draft from the Assistant is staged for insertion. (R2 wires
                    the insert action; R1 acknowledges receipt only.)
                  </MessageBarBody>
                </MessageBar>
              ) : null}
            </div>
          )}

          <div className={styles.editorSlot}>
            <ComposeEditor
              ref={editorRef}
              docxBytes={state.docxBytes}
              documentRef={editorDocRef}
              bffBaseUrl={bffBaseUrl}
              sessionId={state.sessionId}
              onDirtyChange={handleDirtyChange}
              onImportWarnings={handleImportWarnings}
            />
          </div>
        </>
      ) : null}

      {/*
        Task 051: Multi-tab conflict dialog (FR-16 verbatim labels).

        Rendered when `state.checkoutStatus === 'same-user-conflict'` —
        i.e., the /checkout-status probe revealed that THIS user holds the
        lock from another session (CheckoutStatusInfo.IsCurrentUser === true).

        The dialog is non-dismissible (alert modalType); user MUST choose one
        of: Force-close (POST /discard then re-checkout) / Go-to-other
        (BroadcastChannel focus-me + cancel here) / Cancel (close this tab).

        Per Spike #3 §6 ADR Path A: the underlying lock substrate is
        Dataverse-side `DocumentCheckoutService`, NOT SPE-native checkOut.
        The server's GetCheckoutStatusAsync already computes IsCurrentUser
        via LookupDataverseUserIdAsync — the frontend just reads the flag.
      */}
      <ComposeConflictDialog
        open={state.checkoutStatus === 'same-user-conflict'}
        documentDisplayName={state.documentRef?.fileName}
        conflictingSessionOpenedAt={state.sameUserConflictInfo?.checkedOutAt ?? null}
        onGoToOtherSession={handleGoToOtherSession}
        onForceCloseOtherSession={() => {
          void handleForceCloseOtherSession();
        }}
        onCancel={handleConflictCancelled}
      />

      {/* Error state — load failed; no document loaded */}
      {state.status === 'error' ? (
        <div
          className={styles.bannerStack}
          data-testid="compose-workspace-error-empty"
          role="alert"
        >
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Cannot load document</MessageBarTitle>
              {state.errorMessage ?? 'An unknown error occurred.'}
            </MessageBarBody>
          </MessageBar>
        </div>
      ) : null}
    </div>
  );
}

export default ComposeWorkspace;
