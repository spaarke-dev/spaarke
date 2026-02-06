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
  OpenRegular,
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
  /** Event ID for Open button navigation */
  eventId?: string | null;
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
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

// Event modal form GUID for opening full record
const EVENT_MODAL_FORM_ID = "90d2eff7-6703-f111-8407-7ced8d1dc988";

/**
 * Open the full Event record in a modal dialog
 * Uses Xrm.Navigation.navigateTo with target: 2 (modal dialog)
 */
function openEventModal(eventId: string): void {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;

    if (!xrm?.Navigation?.navigateTo) {
      console.warn("[Footer] Xrm.Navigation.navigateTo not available, falling back to openForm");
      // Fallback to openForm if navigateTo not available
      if (xrm?.Navigation?.openForm) {
        xrm.Navigation.openForm({
          entityName: "sprk_event",
          entityId: eventId,
          formId: EVENT_MODAL_FORM_ID,
        });
      } else {
        console.error("[Footer] Cannot open form - Xrm.Navigation not available");
      }
      return;
    }

    // Use navigateTo for modal dialog (target: 2)
    const pageInput = {
      pageType: "entityrecord",
      entityName: "sprk_event",
      entityId: eventId,
      formId: EVENT_MODAL_FORM_ID,
    };

    const navigationOptions = {
      target: 2, // Open in modal dialog
      width: { value: 80, unit: "%" },
      height: { value: 80, unit: "%" },
      position: 1, // Center
    };

    xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
      () => {
        console.log("[Footer] Modal closed");
      },
      (error: unknown) => {
        console.error("[Footer] Error opening modal:", error);
      }
    );
  } catch (error) {
    console.error("[Footer] Exception opening modal:", error);
  }
}

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
  eventId,
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

        {/* Right side: Open and Save buttons */}
        <div style={{ display: "flex", gap: "8px" }}>
          {/* Open button - opens full record in modal */}
          {eventId && (
            <Button
              appearance="subtle"
              icon={<OpenRegular />}
              onClick={() => openEventModal(eventId)}
              aria-label="Open full record"
            >
              Open
            </Button>
          )}

          {/* Save button (hidden in read-only mode) */}
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
