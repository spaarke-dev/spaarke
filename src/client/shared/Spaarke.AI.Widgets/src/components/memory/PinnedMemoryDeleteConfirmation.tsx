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
 */

import React, { useCallback } from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  makeStyles,
  Text,
  tokens,
} from '@fluentui/react-components';
import { WarningRegular } from '@fluentui/react-icons';

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
   * Invoked when the user cancels (Cancel button OR Dialog close via Escape /
   * backdrop click). The parent should clear its "pending delete" state.
   */
  onCancel: () => void;
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
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  warningIcon: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase600,
    flexShrink: 0,
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
}) => {
  const styles = useStyles();

  // Fluent v9 Dialog calls onOpenChange with the new open state; we treat any
  // transition from open -> closed as a cancel unless a confirm is in flight.
  const handleOpenChange = useCallback(
    (_e: unknown, data: { open: boolean }) => {
      if (!data.open && !isDeleting) {
        onCancel();
      }
    },
    [isDeleting, onCancel]
  );

  const handleConfirm = useCallback(() => {
    if (isDeleting) return;
    onConfirm();
  }, [isDeleting, onConfirm]);

  const handleCancel = useCallback(() => {
    if (isDeleting) return;
    onCancel();
  }, [isDeleting, onCancel]);

  return (
    <Dialog open={open} onOpenChange={handleOpenChange} modalType="alert">
      <DialogSurface data-testid="pinned-memory-delete-confirmation">
        <DialogBody>
          <DialogTitle>
            <div className={styles.headerRow}>
              <WarningRegular className={styles.warningIcon} aria-hidden="true" />
              <span>Delete pinned memory?</span>
            </div>
          </DialogTitle>
          <DialogContent>
            <div className={styles.body}>
              <Text className={styles.bodyText}>
                You are about to delete{' '}
                <span className={styles.pinTitleQuoted} title={pinTitle}>
                  &ldquo;{pinTitle}&rdquo;
                </span>
                .
              </Text>

              {/* Cross-session impact callout — emphasised per POML acceptance. */}
              <div
                className={styles.impactCallout}
                role="alert"
                data-testid="pinned-memory-delete-impact"
              >
                <Text className={styles.calloutTitle}>This action affects every chat session.</Text>
                <Text className={styles.calloutBody}>
                  This pin is shared across all your chat sessions and will be removed
                  permanently. The assistant will stop using it the next time you start
                  a conversation.
                </Text>
              </div>
            </div>
          </DialogContent>
          <DialogActions>
            {/* Cancel button uses an explicit onClick rather than DialogTrigger
                so the parent's onCancel fires exactly once. The DialogTrigger
                wrapper would close the Dialog → trigger onOpenChange →
                fire onCancel a SECOND time. Letting onClick own the close
                path keeps the contract single-fire. */}
            <Button
              appearance="secondary"
              onClick={handleCancel}
              disabled={isDeleting}
              data-testid="pinned-memory-delete-cancel"
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleConfirm}
              disabled={isDeleting}
              data-testid="pinned-memory-delete-confirm"
            >
              {isDeleting ? 'Deleting…' : 'Delete pin'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

PinnedMemoryDeleteConfirmation.displayName = 'PinnedMemoryDeleteConfirmation';

export default PinnedMemoryDeleteConfirmation;
