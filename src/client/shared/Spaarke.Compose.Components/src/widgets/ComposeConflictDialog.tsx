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
 * @see projects/spaarkeai-compose-r1/tasks/051-spe-multi-tab-conflict-ux.poml
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-3-spe-checkout-promotion.md §1 + §9
 * @see src/server/api/Sprk.Bff.Api/Models/CheckoutModels.cs (CheckoutStatusInfo)
 * @see src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:429-457
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx (host)
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  makeStyles,
  tokens,
  Text,
} from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
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
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `ComposeConflictDialog` — multi-tab conflict modal (FR-16).
 *
 * Renders a non-dismissible Fluent v9 Dialog with three buttons:
 *   - Primary (appearance="primary"): "Force-close other session and open here"
 *   - Secondary: "Go to that session"
 *   - Tertiary (appearance="subtle"): "Cancel — close this tab"
 *
 * The primary action is force-close because it represents the "I want to
 * keep editing here" intent, which is the most common multi-tab case (user
 * forgot they had it open elsewhere and wants to consolidate).
 *
 * The dialog is **non-dismissible** via Escape/background-click — only the
 * three buttons resolve it. This avoids the silent-dismiss failure mode
 * documented in the file header.
 *
 * Accessibility:
 *   - DialogTitle is the dialog's accessible name (Fluent v9 wires this).
 *   - DialogContent has live-region semantics by virtue of role="dialog"
 *     + aria-describedby (Fluent v9 internals).
 *   - Buttons are reachable in tab order; primary gets initial focus per
 *     Fluent v9 Dialog conventions.
 */
export function ComposeConflictDialog(props: ComposeConflictDialogProps): React.JSX.Element {
  const {
    open,
    documentDisplayName,
    conflictingSessionOpenedAt,
    onGoToOtherSession,
    onForceCloseOtherSession,
    onCancel,
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
    <Dialog
      open={open}
      // Non-dismissible — neither Escape nor background-click closes.
      // `modalType="alert"` makes the dialog block interaction with the
      // rest of the page AND disables the default close-on-escape behavior.
      modalType="alert"
      // No onOpenChange handler — the dialog cannot be closed except via
      // the three action buttons. The host controls `open` via reducer.
    >
      <DialogSurface
        // Sized to comfortably fit the heading + paragraph + three buttons
        // without scrolling on a typical Compose pane width.
        data-testid="compose-conflict-dialog"
        aria-labelledby="compose-conflict-dialog-title"
        aria-describedby="compose-conflict-dialog-content"
      >
        <DialogBody>
          <DialogTitle id="compose-conflict-dialog-title">
            This document is open in another Compose session
          </DialogTitle>
          <DialogContent
            id="compose-conflict-dialog-content"
            className={styles.content}
          >
            <Text className={styles.paragraph}>
              You already have{' '}
              {documentDisplayName ? (
                <strong>{documentDisplayName}</strong>
              ) : (
                'this document'
              )}{' '}
              open in another tab or window. Only one editing session per
              document is allowed at a time.
            </Text>
            {openedAtDisplay ? (
              <Text className={styles.meta} data-testid="compose-conflict-opened-at">
                Other session opened: {openedAtDisplay}
              </Text>
            ) : null}
            <Text className={styles.paragraph}>
              Choose how to continue:
            </Text>
          </DialogContent>
          <DialogActions
            // Fluent v9 DialogActions is internally a flex row; we override
            // wrapping behavior for long button labels (FR-16 verbatim labels
            // are long enough to wrap on narrow Compose panes).
            className={styles.actions}
          >
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
            <Button
              appearance="subtle"
              onClick={onCancel}
              data-testid="compose-conflict-cancel-button"
            >
              Cancel — close this tab
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

export default ComposeConflictDialog;
