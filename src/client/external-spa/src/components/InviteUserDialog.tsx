import * as React from "react";
import {
  Dialog,
  DialogTrigger,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Field,
  Input,
  Select,
  Text,
  Spinner,
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from "@fluentui/react-components";
import {
  PersonAdd20Regular,
  CheckmarkCircle20Regular,
  Dismiss20Regular,
} from "@fluentui/react-icons";
import { inviteUser, type InviteUserResponse } from "../auth/bff-client";
import { AccessLevel } from "../types";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens — no hard-coded colors, ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  dialogContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
  },
  fieldRow: {
    display: "flex",
    gap: tokens.spacingHorizontalM,
    "& > *": {
      flex: "1 1 0",
    },
  },
  successContent: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    textAlign: "center",
  },
  successIcon: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: "48px",
    lineHeight: "1",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  successDetails: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    width: "100%",
  },
  detailRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  detailLabel: {
    color: tokens.colorNeutralForeground2,
  },
  detailValue: {
    color: tokens.colorNeutralForeground1,
    fontWeight: "600",
  },
  invitationCode: {
    color: tokens.colorBrandForeground1,
    fontFamily: "monospace",
    fontSize: tokens.fontSizeBase300,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function validateEmail(value: string): string | null {
  if (!value.trim()) return "Email address is required.";
  if (!EMAIL_REGEX.test(value.trim())) return "Please enter a valid email address.";
  return null;
}

function accessLevelLabel(level: AccessLevel): string {
  switch (level) {
    case AccessLevel.ViewOnly:
      return "View Only";
    case AccessLevel.Collaborate:
      return "Collaborate";
    case AccessLevel.FullAccess:
      return "Full Access";
    default:
      return "Unknown";
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface InviteUserDialogProps {
  /** The Secure Project record ID the invitation is for */
  projectId: string;
  /** The current user's access level — dialog is only usable for FullAccess users */
  accessLevel: AccessLevel;
  /** Whether the dialog is open */
  isOpen: boolean;
  /** Callback invoked when the dialog is dismissed (cancel or after successful invite) */
  onClose: () => void;
}

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

interface FormState {
  email: string;
  selectedAccessLevel: AccessLevel;
  firstName: string;
  lastName: string;
}

type DialogView = "form" | "success" | "error";

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * InviteUserDialog — allows Full Access external users to invite new external
 * users to the current Secure Project.
 *
 * Features:
 * - Email input with format validation
 * - Access level dropdown (View Only, Collaborate, Full Access)
 * - Optional first/last name fields
 * - Calls POST /api/v1/external-access/invite via bff-client.inviteUser()
 * - Success view shows invitation details (invitation code, expiry)
 * - Error view shows error message with retry option
 *
 * Visibility: Only rendered/accessible for AccessLevel.FullAccess users (ADR-003).
 *
 * Styled exclusively with Fluent UI v9 design tokens (ADR-021).
 */
export const InviteUserDialog: React.FC<InviteUserDialogProps> = ({
  projectId,
  accessLevel,
  isOpen,
  onClose,
}) => {
  const styles = useStyles();

  // Guard: this dialog must never be usable by non-FullAccess users
  if (accessLevel !== AccessLevel.FullAccess) {
    return null;
  }

  // eslint-disable-next-line react-hooks/rules-of-hooks
  const [form, setForm] = React.useState<FormState>({
    email: "",
    selectedAccessLevel: AccessLevel.ViewOnly,
    firstName: "",
    lastName: "",
  });

  // eslint-disable-next-line react-hooks/rules-of-hooks
  const [emailError, setEmailError] = React.useState<string | null>(null);
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const [view, setView] = React.useState<DialogView>("form");
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const [successData, setSuccessData] = React.useState<InviteUserResponse | null>(null);
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

  // Reset form state when dialog opens
  // eslint-disable-next-line react-hooks/rules-of-hooks
  React.useEffect(() => {
    if (isOpen) {
      setForm({
        email: "",
        selectedAccessLevel: AccessLevel.ViewOnly,
        firstName: "",
        lastName: "",
      });
      setEmailError(null);
      setIsSubmitting(false);
      setView("form");
      setSuccessData(null);
      setErrorMessage(null);
    }
  }, [isOpen]);

  // ---------------------------------------------------------------------------
  // Handlers
  // ---------------------------------------------------------------------------

  function handleEmailChange(e: React.ChangeEvent<HTMLInputElement>): void {
    const value = e.target.value;
    setForm((prev) => ({ ...prev, email: value }));
    // Clear validation error as user types
    if (emailError) {
      setEmailError(validateEmail(value));
    }
  }

  function handleAccessLevelChange(e: React.ChangeEvent<HTMLSelectElement>): void {
    setForm((prev) => ({ ...prev, selectedAccessLevel: Number(e.target.value) as AccessLevel }));
  }

  function handleFirstNameChange(e: React.ChangeEvent<HTMLInputElement>): void {
    setForm((prev) => ({ ...prev, firstName: e.target.value }));
  }

  function handleLastNameChange(e: React.ChangeEvent<HTMLInputElement>): void {
    setForm((prev) => ({ ...prev, lastName: e.target.value }));
  }

  async function handleSubmit(): Promise<void> {
    // Validate email before submission
    const emailValidationError = validateEmail(form.email);
    if (emailValidationError) {
      setEmailError(emailValidationError);
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const response = await inviteUser({
        email: form.email.trim(),
        projectId,
        accessLevel: form.selectedAccessLevel,
        firstName: form.firstName.trim() || undefined,
        lastName: form.lastName.trim() || undefined,
      });

      setSuccessData(response);
      setView("success");
    } catch (err: unknown) {
      const message =
        err instanceof Error
          ? err.message
          : "An unexpected error occurred. Please try again.";
      setErrorMessage(message);
      setView("error");
    } finally {
      setIsSubmitting(false);
    }
  }

  function handleRetry(): void {
    setView("form");
    setErrorMessage(null);
  }

  function handleClose(): void {
    onClose();
  }

  // ---------------------------------------------------------------------------
  // Render helpers
  // ---------------------------------------------------------------------------

  function renderFormView(): React.ReactNode {
    return (
      <>
        <DialogContent>
          <div className={styles.dialogContent}>
            <Text>
              Invite an external user to collaborate on this project. They will receive an
              email with a link to accept the invitation and set up their account.
            </Text>

            <Field
              label="Email address"
              required
              validationState={emailError ? "error" : "none"}
              validationMessage={emailError ?? undefined}
            >
              <Input
                type="email"
                placeholder="colleague@example.com"
                value={form.email}
                onChange={handleEmailChange}
                disabled={isSubmitting}
                autoFocus
              />
            </Field>

            <div className={styles.fieldRow}>
              <Field label="First name">
                <Input
                  placeholder="Jane"
                  value={form.firstName}
                  onChange={handleFirstNameChange}
                  disabled={isSubmitting}
                />
              </Field>
              <Field label="Last name">
                <Input
                  placeholder="Smith"
                  value={form.lastName}
                  onChange={handleLastNameChange}
                  disabled={isSubmitting}
                />
              </Field>
            </div>

            <Field label="Access level" required>
              <Select
                value={String(form.selectedAccessLevel)}
                onChange={handleAccessLevelChange}
                disabled={isSubmitting}
              >
                <option value={String(AccessLevel.ViewOnly)}>View Only</option>
                <option value={String(AccessLevel.Collaborate)}>Collaborate</option>
                <option value={String(AccessLevel.FullAccess)}>Full Access</option>
              </Select>
            </Field>
          </div>
        </DialogContent>

        <DialogActions>
          <Button
            appearance="primary"
            icon={isSubmitting ? <Spinner size="tiny" /> : <PersonAdd20Regular />}
            onClick={handleSubmit}
            disabled={isSubmitting}
          >
            {isSubmitting ? "Sending invitation..." : "Send invitation"}
          </Button>
          <DialogTrigger disableButtonEnhancement>
            <Button appearance="secondary" onClick={handleClose} disabled={isSubmitting}>
              Cancel
            </Button>
          </DialogTrigger>
        </DialogActions>
      </>
    );
  }

  function renderSuccessView(): React.ReactNode {
    return (
      <>
        <DialogContent>
          <div className={styles.successContent}>
            <div className={styles.successIcon} aria-hidden="true">
              <CheckmarkCircle20Regular style={{ width: "48px", height: "48px" }} />
            </div>

            <Text size={500} weight="semibold">
              Invitation sent
            </Text>

            <Text>
              An invitation email has been sent to{" "}
              <strong>{form.email}</strong> with{" "}
              <strong>{accessLevelLabel(form.selectedAccessLevel)}</strong> access.
            </Text>

            {successData && (
              <div className={styles.successDetails}>
                {successData.invitationCode && (
                  <div className={styles.detailRow}>
                    <Text size={200} className={styles.detailLabel}>
                      Invitation code
                    </Text>
                    <Text size={200} className={styles.invitationCode}>
                      {successData.invitationCode}
                    </Text>
                  </div>
                )}
                {successData.expiryDate && (
                  <div className={styles.detailRow}>
                    <Text size={200} className={styles.detailLabel}>
                      Expires
                    </Text>
                    <Text size={200} className={styles.detailValue}>
                      {new Date(successData.expiryDate).toLocaleDateString(undefined, {
                        year: "numeric",
                        month: "long",
                        day: "numeric",
                      })}
                    </Text>
                  </div>
                )}
              </div>
            )}
          </div>
        </DialogContent>

        <DialogActions>
          <Button appearance="primary" onClick={handleClose}>
            Done
          </Button>
        </DialogActions>
      </>
    );
  }

  function renderErrorView(): React.ReactNode {
    return (
      <>
        <DialogContent>
          <div className={styles.dialogContent}>
            <MessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Invitation failed</MessageBarTitle>
                {errorMessage ??
                  "An unexpected error occurred while sending the invitation."}
              </MessageBarBody>
            </MessageBar>

            <Text>
              The invitation to <strong>{form.email}</strong> could not be sent. Please
              check the details and try again. If the problem persists, contact your
              system administrator.
            </Text>
          </div>
        </DialogContent>

        <DialogActions>
          <Button appearance="primary" onClick={handleRetry}>
            Try again
          </Button>
          <Button appearance="secondary" onClick={handleClose}>
            Cancel
          </Button>
        </DialogActions>
      </>
    );
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  const dialogTitle =
    view === "success"
      ? "Invitation sent"
      : view === "error"
        ? "Invitation failed"
        : "Invite external user";

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(_, data) => {
        if (!data.open && !isSubmitting) {
          handleClose();
        }
      }}
    >
      <DialogSurface aria-label={dialogTitle}>
        <DialogBody>
          <DialogTitle
            action={
              <DialogTrigger disableButtonEnhancement>
                <Button
                  appearance="subtle"
                  aria-label="Close dialog"
                  icon={<Dismiss20Regular />}
                  onClick={handleClose}
                  disabled={isSubmitting}
                />
              </DialogTrigger>
            }
          >
            {dialogTitle}
          </DialogTitle>

          {view === "form" && renderFormView()}
          {view === "success" && renderSuccessView()}
          {view === "error" && renderErrorView()}
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default InviteUserDialog;
