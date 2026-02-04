/**
 * Footer - Side Pane Footer with Save Button and Feedback Messages
 *
 * Displays the footer section of the Event Detail Side Pane with:
 * - Save button (enabled when there are dirty fields)
 * - Success/error message toast
 * - Error rollback actions (retry/discard)
 * - Version information
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/040-implement-save-webapi.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/041-add-optimistic-ui.poml
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Button,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
} from "@fluentui/react-components";
import {
  SaveRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  ArrowUndoRegular,
  ArrowRepeatAllRegular,
  DismissRegular,
  LockClosedRegular,
} from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (Fluent UI v9 makeStyles with semantic tokens)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  footer: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground2,
  },
  messageArea: {
    ...shorthands.padding("8px", "16px"),
  },
  actionsRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("12px", "16px"),
    ...shorthands.gap("12px"),
  },
  saveButton: {
    minWidth: "100px",
  },
  versionText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
  dirtyIndicator: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  dirtyDot: {
    width: "6px",
    height: "6px",
    ...shorthands.borderRadius("50%"),
    backgroundColor: tokens.colorPaletteBlueBorderActive,
  },
  readOnlyIndicator: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("6px"),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  readOnlyIcon: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface FooterProps {
  /** Whether there are unsaved changes */
  isDirty: boolean;
  /** Whether a save operation is in progress */
  isSaving: boolean;
  /** Callback when Save button is clicked */
  onSave: () => void;
  /** Optional: Version string to display */
  version?: string;
  /** Whether the form is in read-only mode (hides save button) */
  isReadOnly?: boolean;
}

/**
 * Message state for success/error feedback
 */
export interface FooterMessage {
  type: "success" | "error";
  title: string;
  message: string;
}

/**
 * Extended message with rollback actions
 */
export interface FooterMessageWithActions extends FooterMessage {
  /** Whether this error supports rollback (has failed fields) */
  hasRollback?: boolean;
}

export interface FooterWithMessageProps extends FooterProps {
  /** Optional message to display (success or error) */
  message?: FooterMessageWithActions | null;
  /** Callback when message is dismissed */
  onDismissMessage?: () => void;
  /** Callback when user wants to retry save */
  onRetry?: () => void;
  /** Callback when user wants to discard changes (rollback) */
  onDiscard?: () => void;
  /** Whether the form is in read-only mode (hides save button) */
  isReadOnly?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const Footer: React.FC<FooterWithMessageProps> = ({
  isDirty,
  isSaving,
  onSave,
  version = "1.0.5",
  message,
  onDismissMessage,
  onRetry,
  onDiscard,
  isReadOnly = false,
}) => {
  const styles = useStyles();

  // Auto-dismiss success messages after 3 seconds
  React.useEffect(() => {
    if (message?.type === "success" && onDismissMessage) {
      const timer = setTimeout(() => {
        onDismissMessage();
      }, 3000);
      return () => clearTimeout(timer);
    }
  }, [message, onDismissMessage]);

  return (
    <footer className={styles.footer}>
      {/* Message Area - Success/Error Feedback */}
      {message && (
        <div className={styles.messageArea}>
          <MessageBar
            intent={message.type === "success" ? "success" : "error"}
            icon={
              message.type === "success" ? (
                <CheckmarkCircleRegular />
              ) : (
                <ErrorCircleRegular />
              )
            }
          >
            <MessageBarBody>
              <MessageBarTitle>{message.title}</MessageBarTitle>
              {message.message}
            </MessageBarBody>
            {/* Rollback actions for error messages */}
            {message.type === "error" && message.hasRollback && (
              <MessageBarActions
                containerAction={
                  <Button
                    appearance="transparent"
                    icon={<DismissRegular />}
                    onClick={onDismissMessage}
                    aria-label="Dismiss"
                    size="small"
                  />
                }
              >
                <Button
                  appearance="transparent"
                  icon={<ArrowRepeatAllRegular />}
                  onClick={onRetry}
                  size="small"
                >
                  Retry
                </Button>
                <Button
                  appearance="transparent"
                  icon={<ArrowUndoRegular />}
                  onClick={onDiscard}
                  size="small"
                >
                  Discard Changes
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
        </div>
      )}

      {/* Actions Row */}
      <div className={styles.actionsRow}>
        {/* Left side: Read-only indicator, Dirty indicator, or version */}
        <div>
          {isReadOnly ? (
            <div className={styles.readOnlyIndicator}>
              <LockClosedRegular className={styles.readOnlyIcon} />
              <Text size={200}>Read-only</Text>
            </div>
          ) : isDirty ? (
            <div className={styles.dirtyIndicator}>
              <span className={styles.dirtyDot} />
              <Text size={200}>Unsaved changes</Text>
            </div>
          ) : (
            <Text className={styles.versionText}>
              EventDetailSidePane v{version}
            </Text>
          )}
        </div>

        {/* Right side: Save button (hidden in read-only mode) */}
        {!isReadOnly && (
          <Button
            className={styles.saveButton}
            appearance="primary"
            icon={isSaving ? <Spinner size="tiny" /> : <SaveRegular />}
            disabled={!isDirty || isSaving}
            onClick={onSave}
            aria-label={isSaving ? "Saving..." : "Save changes"}
          >
            {isSaving ? "Saving..." : "Save"}
          </Button>
        )}
      </div>
    </footer>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Create a success message object
 */
export function createSuccessMessage(
  savedFields: string[]
): FooterMessage {
  const fieldCount = savedFields.length;
  return {
    type: "success",
    title: "Saved",
    message:
      fieldCount === 1
        ? "1 field updated successfully."
        : `${fieldCount} fields updated successfully.`,
  };
}

/**
 * Create an error message object
 */
export function createErrorMessage(error: string): FooterMessageWithActions {
  return {
    type: "error",
    title: "Save Failed",
    message: error,
    hasRollback: false,
  };
}

/**
 * Create an error message with rollback actions
 */
export function createErrorMessageWithRollback(
  error: string
): FooterMessageWithActions {
  return {
    type: "error",
    title: "Save Failed",
    message: error,
    hasRollback: true,
  };
}
