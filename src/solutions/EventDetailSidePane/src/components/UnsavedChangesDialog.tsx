/**
 * UnsavedChangesDialog - Prompt for Unsaved Changes
 *
 * Displays a dialog when user tries to close the side pane or switch events
 * while there are unsaved changes. Provides Save, Discard, and Cancel options.
 *
 * @see ADR-021 - Fluent UI v9, dark mode support
 * @see projects/events-workspace-apps-UX-r1/tasks/043-add-unsaved-changes-prompt.poml
 */

import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Button,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { WarningRegular } from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  content: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalM,
  },
  icon: {
    color: tokens.colorPaletteYellowForeground1,
    fontSize: "24px",
    flexShrink: 0,
    marginTop: "2px",
  },
  message: {
    color: tokens.colorNeutralForeground1,
  },
  actions: {
    display: "flex",
    gap: tokens.spacingHorizontalS,
    justifyContent: "flex-end",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type UnsavedChangesAction = "save" | "discard" | "cancel";

export interface UnsavedChangesDialogProps {
  /** Whether the dialog is open */
  open: boolean;

  /** Callback when user selects an action */
  onAction: (action: UnsavedChangesAction) => void;

  /** Whether save is currently in progress */
  isSaving?: boolean;

  /** Custom title (default: "Unsaved Changes") */
  title?: string;

  /** Custom message */
  message?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Dialog for unsaved changes with Save, Discard, and Cancel options.
 *
 * Usage:
 * ```tsx
 * <UnsavedChangesDialog
 *   open={showDialog}
 *   onAction={(action) => {
 *     if (action === "save") handleSave();
 *     else if (action === "discard") handleDiscard();
 *     else handleCancel();
 *   }}
 *   isSaving={isSaving}
 * />
 * ```
 */
export const UnsavedChangesDialog: React.FC<UnsavedChangesDialogProps> = ({
  open,
  onAction,
  isSaving = false,
  title = "Unsaved Changes",
  message = "You have unsaved changes. Would you like to save them before closing?",
}) => {
  const styles = useStyles();

  // Handle dialog close via escape or backdrop click
  const handleOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }) => {
      if (!data.open) {
        onAction("cancel");
      }
    },
    [onAction]
  );

  // Handle button clicks
  const handleSave = React.useCallback(() => {
    onAction("save");
  }, [onAction]);

  const handleDiscard = React.useCallback(() => {
    onAction("discard");
  }, [onAction]);

  const handleCancel = React.useCallback(() => {
    onAction("cancel");
  }, [onAction]);

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent>
            <div className={styles.content}>
              <WarningRegular className={styles.icon} />
              <span className={styles.message}>{message}</span>
            </div>
          </DialogContent>
          <DialogActions className={styles.actions}>
            <Button
              appearance="secondary"
              onClick={handleCancel}
              disabled={isSaving}
            >
              Cancel
            </Button>
            <Button
              appearance="secondary"
              onClick={handleDiscard}
              disabled={isSaving}
            >
              Discard
            </Button>
            <Button
              appearance="primary"
              onClick={handleSave}
              disabled={isSaving}
            >
              {isSaving ? "Saving..." : "Save"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
