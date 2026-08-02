/**
 * ComposeConflictDialog.tsx — multi-tab conflict modal for Compose (FR-16).
 *
 * Project:   spaarkeai-compose-r1
 * Task:      051 — Multi-tab conflict UX: Go-to-other / Force-close (Wave 7, FULL rigor)
 * Phase:     Phase 5 (W7 — SPE check-out lock UX)
 * FR:        FR-16 verbatim button labels
 *
 * Purpose:
 *   Renders a Fluent v9 modal dialog when the same user opens the same document
 *   in a second Compose tab/session. Two action buttons per FR-16 verbatim:
 *     1. "Go to that session"
 *     2. "Force-close other session and open here"
 *
 *   The dialog is the visual surface; the trigger logic (probe-on-mount
 *   `GET /api/documents/{id}/checkout-status` followed by same-user check)
 *   lives in `ComposeWorkspace.tsx`. The discard call (force-close) and the
 *   BroadcastChannel signaling (go-to-other) are wired by the host via
 *   callbacks passed through this component's props.
 *
 * ADR Tension resolution (BINDING — applied here):
 *   Per Spike #3 §6 + §1 (Path A approved post-Wave-0 2026-06-29), the
 *   conflict detection mechanism is Dataverse-side `DocumentCheckoutService`,
 *   NOT SPE-native `checkOut/checkIn`. The server's `GetCheckoutStatusAsync`
 *   already returns `CheckoutStatusInfo.IsCurrentUser: bool` (computed via
 *   `LookupDataverseUserIdAsync` mapping the caller's Azure AD OID to
 *   Dataverse `systemuserid`). The frontend therefore needs NO client-side
 *   systemuserid resolution — it just reads the flag. This is a major
 *   simplification vs. the original POML §3 framing (which assumed the
 *   frontend would do the comparison).
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `CommandHelpPanel.tsx` is the closest Fluent v9 Dialog pattern
 *     in SpaarkeAi (slash-command help). It is purely informational — no
 *     destructive actions, no two-button choice with semantic distinction.
 *     `ManageWorkspacesPane.tsx` and `ConversationPane.tsx` use Dialog/Drawer
 *     for management UX, not for conflict resolution.
 *   - Extension: not viable — conflict dialog has a SPECIFIC contract (FR-16
 *     verbatim labels, two distinct actions with different consequences,
 *     warning-intent visual treatment, dismiss-via-host-callback semantics).
 *     Inlining the dialog into ComposeWorkspace would bloat that file (already
 *     ~1300 LOC) and mix conflict-UX concerns with the document state machine.
 *   - Cost-of-doing-nothing: FR-16 fails. Without the conflict dialog, a user
 *     opening the same document in two Compose tabs sees the dataverse-side
 *     idempotent-checkout silently lock both tabs to the same lock (per
 *     DocumentCheckoutService.cs:121-152) with NO indication that another tab
 *     is open. No escape hatch — the user cannot reclaim the "other" tab's
 *     position or force-close it.
 *
 * Constraints honored (BINDING):
 *   - ADR-021 Fluent v9 + semantic tokens only — no hex literals; `tokens.*`
 *     for all spacing, color, font-weight; dark-mode parity preserved.
 *   - ADR-022 React 19 — functional component + hooks; no class components.
 *   - ADR-028 Spaarke Auth v2 — this component does NO auth work; it dispatches
 *     callbacks to the host (ComposeWorkspace), which uses `authenticatedFetch`.
 *   - ADR-015 Tier 3 — props are Tier 1 only (display name, ISO timestamp);
 *     this file does NOT log document content, lock token internals, or any
 *     Tier 3 data.
 *   - ADR-030 — this dialog does NOT dispatch PaneEventBus events itself; the
 *     host owns event-bus side effects (BroadcastChannel posting is host-level).
 *   - CLAUDE.md §3 — no `.claude/` writes.
 *   - CLAUDE.md §6.5 — no NEW ADR tensions surfaced (the existing Path A
 *     exception in Spike #3 §6 covers the lock-substrate choice; this dialog
 *     is the user-facing surface for that choice's conflict UX).
 *   - CLAUDE.md §11 — extends existing checkout lifecycle via a new dialog
 *     surface; justified above.
 *   - FR-16 — button labels are VERBATIM: "Go to that session" + "Force-close
 *     other session and open here".
 *
 * Dismissal semantics:
 *   - The dialog is **non-dismissible** via Escape/background-click. The user
 *     MUST choose one of the two actions or cancel out via the third button
 *     ("Cancel — close this tab"). This is intentional — silently dismissing
 *     would leave the user in an ambiguous state where they think they have a
 *     fresh lock but actually share the previous tab's lock.
 *   - The "Cancel — close this tab" button signals the host that the user
 *     chose neither force-close nor go-to-other; the host should typically
 *     unmount the editor (the conflict is unresolved, no lock acquired here).
 *
 * P2 re-base (spaarke-modal-system, task 040 — supersedes the task-031 interim
 * `ModalWindowControls`/local-`isMaximized` wiring):
 *   - Envelope is now the shared `SprkModal` shell (`size="xs"`, `dismiss="alert"`,
 *     `maximizable={false}`) — the same envelope contract the `ConfirmModal`
 *     preset establishes. The literal `<ConfirmModal>` wrapper component was
 *     NOT used directly because its `ConfirmModalProps` only expose a single
 *     Cancel + a single Confirm action; this dialog has THREE simultaneous
 *     actions (force-close / go-to-other / cancel). Per the task-040 escalation
 *     guidance, hosting the third action via `SprkModal`'s public `footer` slot
 *     is legitimate composition (NOT forking) — `ConfirmModal` itself is only a
 *     thin config of the same `SprkModal` shell. `SprkModal` and `ConfirmModal`
 *     are both unmodified by this change.
 *   - Button set, labels, and left-to-right order are UNCHANGED (Force-close /
 *     Go-to-other / Cancel, right-aligned as a group) — no button was moved to
 *     `footerStart` because doing so would have reordered the three actions
 *     relative to each other, which the task-040 constraint (preserve exact
 *     button order) forbids here (unlike a plain Cancel/Confirm pair, none of
 *     these three is a "the rest are secondary to this one" Cancel analog).
 *   - Maximize/restore is retired for this dialog (the `ConfirmModal` contract
 *     is non-maximizable) — the local `isMaximized` state + `surfaceMaximized`
 *     style from the task-031 interim wiring are removed as dead code.
 *   - Known gap (reported, not fixed here — preset limitation, not a
 *     task-040 defect): `SprkModal`'s header renders `title` as plain text, not
 *     via Fluent's `DialogTitle`, so there is no `aria-labelledby` wiring the
 *     dialog's accessible name to the title (same limitation already shipped
 *     in the `ConfirmModal`/`SprkModal` test suites from task 005/009). The
 *     `alertdialog` role and focus-trap/ESC-blocking behavior are unaffected.
 *
 * @see projects/spaarkeai-compose-r1/tasks/051-spe-multi-tab-conflict-ux.poml
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-3-spe-checkout-promotion.md §1 + §9
 * @see projects/spaarke-modal-system/tasks/040-rebase-confirms.poml
 * @see src/server/api/Sprk.Bff.Api/Models/CheckoutModels.cs (CheckoutStatusInfo)
 * @see src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:429-457
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx (host)
 */

import * as React from 'react';
import { Button, makeStyles, tokens, Text } from '@fluentui/react-components';
// Shared canonical modal shell (spaarke-modal-system) — the same envelope the
// `ConfirmModal` preset configures (`size="xs"`, `dismiss="alert"`,
// `maximizable={false}`); composed directly here to host a 3-button footer
// (see the P2 re-base note above).
import { SprkModal } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
  },
  paragraph: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  meta: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  actions: {
    display: 'flex',
    flexDirection: 'row',
    columnGap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * Props for `ComposeConflictDialog`.
 *
 * Triggered by `ComposeWorkspace` when the checkout-status probe detects the
 * SAME user holds the lock from another session (CheckoutStatusInfo
 * .IsCheckedOut === true AND IsCurrentUser === true).
 */
export interface ComposeConflictDialogProps {
  /**
   * Whether the dialog is visible. Host owns the open state via reducer.
   */
  open: boolean;

  /**
   * Display name of the document at conflict (for context in the dialog
   * heading). Tier 1 safe — file name, not document content.
   */
  documentDisplayName?: string;

  /**
   * ISO timestamp of when the conflicting lock was acquired (from the
   * server-side `CheckoutStatusInfo.CheckedOutAt`). Used to render
   * "Opened {locale-formatted timestamp}" — gives the user a recognition
   * cue ("oh, that was the tab I left open yesterday").
   *
   * Optional: if not provided (or null), the timestamp line is omitted.
   */
  conflictingSessionOpenedAt?: string | null;

  /**
   * Called when the user clicks **"Go to that session"** (FR-16 verbatim).
   *
   * Host typically:
   *   1. Posts a `focus-me` message on a per-document BroadcastChannel so the
   *      original tab can `window.focus()` itself.
   *   2. Closes this dialog.
   *   3. Optionally unmounts the editor in THIS tab (since the user chose to
   *      go back to the other one).
   *
   * If the original tab is not listening on the channel (e.g., it was a
   * browser-close-without-cleanup), this is a no-op; the user is left in this
   * tab with the conflict still active — the host should re-present the
   * dialog so they can choose Force-close instead.
   */
  onGoToOtherSession: () => void;

  /**
   * Called when the user clicks **"Force-close other session and open here"**
   * (FR-16 verbatim).
   *
   * Host typically:
   *   1. POSTs to `/api/documents/{documentId}/discard` to release the lock
   *      (this succeeds for the same user per `DiscardAsync`'s
   *      NotAuthorized check — only the lock-owner can discard).
   *   2. After 200 OK, fires a `force-closed` message on the BroadcastChannel
   *      so the original tab can unmount its editor (if it's listening).
   *   3. Proceeds with the normal `POST /checkout` to acquire a fresh lock in
   *      THIS tab.
   *   4. Closes this dialog.
   */
  onForceCloseOtherSession: () => void;

  /**
   * Called when the user clicks **"Cancel — close this tab"** (third option;
   * not in FR-16 verbatim but required for non-dismissible-dialog escape).
   *
   * Host typically unmounts the editor (the conflict is unresolved) and
   * shows an "Open in another tab? Go back to that tab to continue." banner
   * in the empty state.
   */
  onCancel: () => void;

  /**
   * The `--sprk-ui-scale` factor forwarded to the `SprkModal` shell (design
   * §6.9). Optional, backward-compatible; hosts thread this via `useUiScale()`
   * (spaarke-modal-system task 040 low-cost passthrough).
   */
  uiScale?: number;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `ComposeConflictDialog` — multi-tab conflict modal (FR-16).
 *
 * Renders a non-dismissible `SprkModal` (`dismiss="alert"`) with three buttons
 * in the shared footer slot:
 *   - Primary (appearance="primary"): "Force-close other session and open here"
 *   - Secondary: "Go to that session"
 *   - Tertiary (appearance="subtle"): "Cancel — close this tab"
 *
 * The primary action is force-close because it represents the "I want to
 * keep editing here" intent, which is the most common multi-tab case (user
 * forgot they had it open elsewhere and wants to consolidate). None of the
 * three actions is styled as destructive/danger — the original dialog never
 * used danger styling here, and task 040 preserves that intent verbatim.
 *
 * The dialog is **non-dismissible** via Escape/background-click — only the
 * three buttons resolve it. This avoids the silent-dismiss failure mode
 * documented in the file header.
 *
 * Accessibility:
 *   - `SprkModal` sets `aria-modal` + the `alertdialog` role for `dismiss="alert"`.
 *   - Buttons are reachable in tab order; the shell renders the standard
 *     `ModalWindowControls` (×) in the header, routed to `onCancel` via the
 *     shell's own `onClose` contract.
 */
export function ComposeConflictDialog(props: ComposeConflictDialogProps): React.JSX.Element {
  const {
    open,
    documentDisplayName,
    conflictingSessionOpenedAt,
    onGoToOtherSession,
    onForceCloseOtherSession,
    onCancel,
    uiScale,
  } = props;

  const styles = useStyles();

  // Locale-format the timestamp once per render (cheap; Date.parse is sub-
  // millisecond). Defer the format until needed so we don't crash on a bad
  // ISO string (defensive: server should always provide a valid one).
  const openedAtDisplay = React.useMemo<string | null>(() => {
    if (!conflictingSessionOpenedAt) return null;
    try {
      const d = new Date(conflictingSessionOpenedAt);
      if (Number.isNaN(d.getTime())) return null;
      return d.toLocaleString();
    } catch {
      return null;
    }
  }, [conflictingSessionOpenedAt]);

  return (
    <SprkModal
      open={open}
      // × always routes to onCancel — the SAME cancel-equivalent action the
      // third button uses. This dialog's non-dismissible alert semantics are
      // unchanged (× is a deliberate click on the one existing cancel-
      // equivalent action, not a new Escape/backdrop path — `dismiss="alert"`
      // blocks ESC/backdrop entirely).
      onClose={onCancel}
      title="This document is open in another Compose session"
      size="xs"
      dismiss="alert"
      maximizable={false}
      uiScale={uiScale}
      footer={
        <div className={styles.actions}>
          <Button
            appearance="primary"
            onClick={onForceCloseOtherSession}
            data-testid="compose-conflict-force-close-button"
          >
            {/* FR-16 verbatim — do not change wording. */}
            Force-close other session and open here
          </Button>
          <Button
            appearance="secondary"
            onClick={onGoToOtherSession}
            data-testid="compose-conflict-go-to-other-button"
          >
            {/* FR-16 verbatim — do not change wording. */}
            Go to that session
          </Button>
          <Button appearance="subtle" onClick={onCancel} data-testid="compose-conflict-cancel-button">
            Cancel — close this tab
          </Button>
        </div>
      }
    >
      <div data-testid="compose-conflict-dialog" className={styles.content}>
        <Text className={styles.paragraph}>
          You already have {documentDisplayName ? <strong>{documentDisplayName}</strong> : 'this document'} open in
          another tab or window. Only one editing session per document is allowed at a time.
        </Text>
        {openedAtDisplay ? (
          <Text className={styles.meta} data-testid="compose-conflict-opened-at">
            Other session opened: {openedAtDisplay}
          </Text>
        ) : null}
        <Text className={styles.paragraph}>Choose how to continue:</Text>
      </div>
    </SprkModal>
  );
}

export default ComposeConflictDialog;
