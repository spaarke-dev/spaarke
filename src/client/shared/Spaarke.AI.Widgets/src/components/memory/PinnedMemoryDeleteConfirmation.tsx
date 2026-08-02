/**
 * @spaarke/ai-widgets — PinnedMemoryDeleteConfirmation
 *
 * Fluent v9 confirmation dialog rendered when the user clicks "Delete" on a
 * pinned memory item from the {@link PinnedMemoryListWidget}. Surfaces the
 * cross-session impact of a delete operation: pinned items live across all
 * the user's chat sessions, so removing one removes its influence from EVERY
 * session in which the chat-agent assembly composes pinned items into the
 * system prompt (per R6 task 067 memory composition).
 *
 * Per the POML acceptance criteria for task 070:
 *   > "Delete confirmation shows cross-session impact warning."
 *
 * The dialog is fully controlled by its parent — `open`, `onConfirm`, and
 * `onCancel` are required props. The component does not call the BFF itself;
 * the parent {@link PinnedMemoryListWidget} owns the DELETE side effect and
 * the optimistic list update.
 *
 * Standards:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; Fluent v9 components.
 *   - ADR-021: zero hardcoded colors; Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component + hooks.
 *
 * Task: R6-070 (D-C-24 / D-C-25, Pillar 7, Q7 scope expansion) — PART B.
 *
 * P2 re-base (spaarke-modal-system, task 040 + P2 consolidation — supersedes
 * the task-031 interim `ModalWindowControls`/local-`isMaximized` wiring):
 *   - Now the literal `ConfirmModal` preset (`size="xs"`, `dismiss="alert"`,
 *     non-maximizable, danger Confirm). The initial task-040 re-base composed
 *     `SprkModal` directly because `ConfirmModalProps` had no way to disable
 *     the buttons mid-flight; the P2 consolidation added `busy` to the preset
 *     for exactly this case, so the direct composition (and its verbatim copy
 *     of the danger token class) is retired in favor of the preset.
 *   - `busy={isDeleting}` disables Cancel + Delete and shows the in-flight
 *     spinner; the guarded handlers additionally no-op re-entrant calls
 *     (including the header ×, which routes through `onClose`).
 *   - The header's inline `WarningRegular` icon remains dropped — `SprkModal`'s
 *     `title` is a plain string (design §6.4 one-header contract); the warning
 *     intent is conveyed by the `role="alert"` impact callout in the body.
 */

import React, { useCallback } from 'react';
import { makeStyles, Text, tokens } from '@fluentui/react-components';
// Shared canonical modal preset (spaarke-modal-system): the literal confirm
// family member — danger styling + alert dismiss + busy handled by the preset.
import { ConfirmModal } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export interface PinnedMemoryDeleteConfirmationProps {
  /** Controlled open state. */
  open: boolean;
  /**
   * Title of the pin being deleted. Used in the confirmation body so the user
   * sees what they're about to remove. Truncated visually if very long; the
   * full title is preserved in the `title` HTML attribute.
   */
  pinTitle: string;
  /**
   * Whether the parent is currently performing the delete (BFF call in
   * flight). When `true`, the Delete button is disabled + shows "Deleting…"
   * to prevent double-submit.
   */
  isDeleting?: boolean;
  /** Invoked when the user confirms the delete. */
  onConfirm: () => void;
  /**
   * Invoked when the user cancels (Cancel button OR the header ×). The parent
   * should clear its "pending delete" state.
   */
  onCancel: () => void;
  /**
   * The `--sprk-ui-scale` factor forwarded to the modal shell (design §6.9).
   * Optional, backward-compatible; hosts thread this via `useUiScale()`.
   */
  uiScale?: number;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  pinTitleQuoted: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '320px',
  },
  bodyText: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase300,
  },
  impactCallout: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground3,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    borderLeftWidth: tokens.strokeWidthThicker,
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorPaletteRedBorder2,
  },
  calloutTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  calloutBody: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
  },
});

// ---------------------------------------------------------------------------
// PinnedMemoryDeleteConfirmation
// ---------------------------------------------------------------------------

/**
 * Confirmation dialog for deleting a pinned memory item. Per acceptance
 * criteria, the dialog emphasises the cross-session impact of the action —
 * the pin is shared across every chat session and removal is permanent.
 */
export const PinnedMemoryDeleteConfirmation: React.FC<PinnedMemoryDeleteConfirmationProps> = ({
  open,
  pinTitle,
  isDeleting = false,
  onConfirm,
  onCancel,
  uiScale,
}) => {
  const styles = useStyles();

  const handleConfirm = useCallback(() => {
    if (isDeleting) return;
    onConfirm();
  }, [isDeleting, onConfirm]);

  const handleCancel = useCallback(() => {
    if (isDeleting) return;
    onCancel();
  }, [isDeleting, onCancel]);

  return (
    <ConfirmModal
      open={open}
      // × routes through the SAME guarded handler the Cancel button uses
      // (isDeleting-safe) — `dismiss="alert"` (the preset's fixed contract)
      // also blocks ESC/backdrop, so the non-dismissible semantics hold.
      onClose={handleCancel}
      onConfirm={handleConfirm}
      title="Delete pinned memory?"
      confirmLabel={isDeleting ? 'Deleting…' : 'Delete pin'}
      cancelLabel="Cancel"
      destructive
      busy={isDeleting}
      uiScale={uiScale}
      message={
        <div className={styles.body} data-testid="pinned-memory-delete-confirmation">
          <Text className={styles.bodyText}>
            You are about to delete{' '}
            <span className={styles.pinTitleQuoted} title={pinTitle}>
              &ldquo;{pinTitle}&rdquo;
            </span>
            .
          </Text>

          {/* Cross-session impact callout — emphasised per POML acceptance. */}
          <div className={styles.impactCallout} role="alert" data-testid="pinned-memory-delete-impact">
            <Text className={styles.calloutTitle}>This action affects every chat session.</Text>
            <Text className={styles.calloutBody}>
              This pin is shared across all your chat sessions and will be removed permanently. The assistant will
              stop using it the next time you start a conversation.
            </Text>
          </div>
        </div>
      }
    />
  );
};

PinnedMemoryDeleteConfirmation.displayName = 'PinnedMemoryDeleteConfirmation';

export default PinnedMemoryDeleteConfirmation;
