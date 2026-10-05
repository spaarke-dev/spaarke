import * as React from 'react';
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
} from '@fluentui/react-components';
import { PersonAdd20Regular, CheckmarkCircle20Regular, Dismiss20Regular } from '@fluentui/react-icons';
import { grantAccessAsContact, problemMessage, type ContactGrantResponse } from '../auth/bff-client';
import { AccessLevel } from '../types';
import { canInvite, grantableLevels } from '../hooks/useAccessLevel';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens — no hard-coded colors, ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  dialogContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
  },
  successContent: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    textAlign: 'center',
  },
  successIcon: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: '48px',
    lineHeight: '1',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  notices: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    width: '100%',
    textAlign: 'left',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function validateEmail(value: string): string | null {
  if (!value.trim()) return 'Email address is required.';
  if (!EMAIL_REGEX.test(value.trim())) return 'Please enter a valid email address.';
  return null;
}

export function accessLevelLabel(level: AccessLevel | number | null | undefined): string {
  switch (level) {
    case AccessLevel.ViewOnly:
      return 'View Only';
    case AccessLevel.Collaborate:
      return 'Collaborate';
    case AccessLevel.FullAccess:
      return 'Full Access';
    default:
      return 'Unknown';
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface InviteUserDialogProps {
  /** The record the grant is for — 'project' (the one detail page with a Contacts tab), 'matter' or 'workassignment'. */
  recordType?: string;
  /** The record id */
  projectId: string;
  /** The current user's access level on the record — the dialog is usable for Collaborate and Full Access. */
  accessLevel: AccessLevel;
  /** Whether the dialog is open */
  isOpen: boolean;
  /** Callback invoked when the dialog is dismissed (cancel or after a successful grant) */
  onClose: () => void;
  /** Called after a grant was written, so the caller can refresh its list of issued grants. */
  onGranted?: (result: ContactGrantResponse) => void;
}

interface FormState {
  email: string;
  selectedAccessLevel: AccessLevel;
}

type DialogView = 'form' | 'success' | 'error';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * InviteUserDialog — contact-side Grant Access (unified-access-control-r2 task 140, owner C4 / Q1 / Q2).
 *
 * A contact holding Collaborate or Full Access gives a colleague OF THEIR OWN ORGANIZATION access to this record:
 * - the colleague is named by email and must already be an active contact of the caller's organization in the system
 *   (a person who is not yet one is refused by the server, with its message shown verbatim);
 * - only levels at or below the caller's own are offered (the server caps anyway, and says so — "narrowed");
 * - the grant lasts 90 days, never beyond the caller's own grant (the server says when it shortened it).
 *
 * Posts to POST /api/v1/external/contact-grants via bff-client.grantAccessAsContact(). There is no organization-wide
 * option, by rule. Visibility: Collaborate and Full Access only — the server is the security boundary (ADR-008).
 *
 * Styled exclusively with Fluent UI v9 design tokens (ADR-021).
 */
export const InviteUserDialog: React.FC<InviteUserDialogProps> = ({
  recordType = 'project',
  projectId,
  accessLevel,
  isOpen,
  onClose,
  onGranted,
}) => {
  const styles = useStyles();
  const levels = grantableLevels(accessLevel);

  const [form, setForm] = React.useState<FormState>({ email: '', selectedAccessLevel: AccessLevel.ViewOnly });
  const [emailError, setEmailError] = React.useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [view, setView] = React.useState<DialogView>('form');
  const [result, setResult] = React.useState<ContactGrantResponse | null>(null);
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

  // Reset form state when the dialog opens
  React.useEffect(() => {
    if (isOpen) {
      setForm({ email: '', selectedAccessLevel: AccessLevel.ViewOnly });
      setEmailError(null);
      setIsSubmitting(false);
      setView('form');
      setResult(null);
      setErrorMessage(null);
    }
  }, [isOpen]);

  // Guard: never usable by a caller who may not grant (View Only, or no access)
  if (!canInvite(accessLevel)) {
    return null;
  }

  function handleEmailChange(e: React.ChangeEvent<HTMLInputElement>): void {
    const value = e.target.value;
    setForm(prev => ({ ...prev, email: value }));
    if (emailError) {
      setEmailError(validateEmail(value));
    }
  }

  function handleAccessLevelChange(e: React.ChangeEvent<HTMLSelectElement>): void {
    setForm(prev => ({ ...prev, selectedAccessLevel: Number(e.target.value) as AccessLevel }));
  }

  async function handleSubmit(): Promise<void> {
    const emailValidationError = validateEmail(form.email);
    if (emailValidationError) {
      setEmailError(emailValidationError);
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const response = await grantAccessAsContact({
        recordType,
        recordId: projectId,
        granteeEmail: form.email.trim(),
        accessLevel: form.selectedAccessLevel,
      });
      setResult(response);
      setView('success');
      onGranted?.(response);
    } catch (err: unknown) {
      setErrorMessage(problemMessage(err, 'An unexpected error occurred. Nothing was granted; please try again.'));
      setView('error');
    } finally {
      setIsSubmitting(false);
    }
  }

  function renderFormView(): React.ReactNode {
    return (
      <>
        <DialogContent>
          <div className={styles.dialogContent}>
            <Text>
              Give a colleague from your organization access to this project. You can give them up to your own level of
              access ({accessLevelLabel(accessLevel)}). Their access lasts 90 days, and never longer than yours.
            </Text>

            <Field
              label="Colleague's email address"
              required
              validationState={emailError ? 'error' : 'none'}
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

            <Field label="Access level" required>
              <Select
                value={String(form.selectedAccessLevel)}
                onChange={handleAccessLevelChange}
                disabled={isSubmitting}
                aria-label="Access level"
              >
                {levels.map(level => (
                  <option key={level} value={String(level)}>
                    {accessLevelLabel(level)}
                  </option>
                ))}
              </Select>
            </Field>
          </div>
        </DialogContent>

        <DialogActions>
          <DialogTrigger disableButtonEnhancement>
            <Button appearance="secondary" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
          </DialogTrigger>
          <Button
            appearance="primary"
            icon={isSubmitting ? <Spinner size="tiny" /> : <PersonAdd20Regular />}
            onClick={handleSubmit}
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Granting access...' : 'Grant access'}
          </Button>
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
              <CheckmarkCircle20Regular style={{ width: '48px', height: '48px' }} />
            </div>

            <Text size={500} weight="semibold">
              Access granted
            </Text>

            <Text>
              <strong>{form.email}</strong> now has <strong>{accessLevelLabel(result?.grantedAccessLevel)}</strong>{' '}
              access{result?.expiryDate ? ` until ${result.expiryDate}` : ''}.
            </Text>

            <div className={styles.notices}>
              {result?.narrowed && (
                <MessageBar intent="warning">
                  <MessageBarBody>
                    You can give at most your own access ({accessLevelLabel(accessLevel)}), so they were given{' '}
                    {accessLevelLabel(result.grantedAccessLevel)} instead of{' '}
                    {accessLevelLabel(form.selectedAccessLevel)}.
                  </MessageBarBody>
                </MessageBar>
              )}
              {result?.expiryNarrowed && (
                <MessageBar intent="info">
                  <MessageBarBody>
                    Their access ends on {result.expiryDate}, when your own access to this project ends.
                  </MessageBarBody>
                </MessageBar>
              )}
            </div>
          </div>
        </DialogContent>

        <DialogActions>
          <Button appearance="primary" onClick={onClose}>
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
                <MessageBarTitle>Access not granted</MessageBarTitle>
                {errorMessage}
              </MessageBarBody>
            </MessageBar>
          </div>
        </DialogContent>

        <DialogActions>
          <Button appearance="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button appearance="primary" onClick={() => setView('form')}>
            Try again
          </Button>
        </DialogActions>
      </>
    );
  }

  const dialogTitle =
    view === 'success' ? 'Access granted' : view === 'error' ? 'Access not granted' : 'Grant a colleague access';

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(_, data) => {
        if (!data.open && !isSubmitting) {
          onClose();
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
                  onClick={onClose}
                  disabled={isSubmitting}
                />
              </DialogTrigger>
            }
          >
            {dialogTitle}
          </DialogTitle>

          {view === 'form' && renderFormView()}
          {view === 'success' && renderSuccessView()}
          {view === 'error' && renderErrorView()}
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default InviteUserDialog;
