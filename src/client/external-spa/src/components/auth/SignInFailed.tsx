/**
 * SignInFailed — the fail-loud screen for a redirect sign-in that did not complete (task 056 review fix).
 *
 * Entra returned an error to the redirect URI instead of a sign-in (e.g. `access_denied` / AADSTS65004 when a new guest
 * declines the "Review permissions" page). The account gate used to start the redirect again on its own, which looped
 * forever; it now shows this screen, and ONLY the "Try again" click starts another redirect (a user gesture).
 *
 * Shows the safe error code only (an MSAL / OIDC identifier and an AADSTS number) — never the raw description, which can
 * carry a tenant id, a correlation id or an address. Both planes (CIAM and workforce browser) use it; Teams never gets
 * here (it does not redirect). Fluent v9 semantic tokens only (ADR-021); the shared `EmptyState` renders the shell
 * (ADR-012) with a real `h1`.
 */
import * as React from 'react';
import { Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { ErrorCircle48Regular } from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import type { SignInFailure } from '../../auth/msal-auth';

export interface SignInFailedProps {
  failure: SignInFailure;
  /** Called from the button's click handler (a user gesture): clear the failure and start the sign-in redirect. */
  onTryAgain: () => void;
  /** Set when the last "Try again" could not start the redirect, so the button never silently does nothing. */
  startError?: string | null;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flex: '1 1 auto',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100dvh',
    boxSizing: 'border-box',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  description: { maxWidth: '28rem' },
  icon: { color: tokens.colorStatusDangerForeground1 },
  code: {
    fontFamily: tokens.fontFamilyMonospace,
    color: tokens.colorNeutralForeground3,
  },
  error: { color: tokens.colorStatusDangerForeground1 },
  footer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    rowGap: tokens.spacingVerticalS,
  },
});

export const SignInFailed: React.FC<SignInFailedProps> = ({ failure, onTryAgain, startError }) => {
  const s = useStyles();
  const buttonRef = React.useRef<HTMLButtonElement>(null);
  // The screen replaces a spinner: move focus to the only action so a keyboard / screen-reader user lands on it.
  React.useEffect(() => {
    buttonRef.current?.focus();
  }, []);

  const code = failure.aadsts ? `${failure.errorCode} (${failure.aadsts})` : failure.errorCode;

  return (
    <div className={s.root} data-testid="signin-failed">
      <EmptyState
        icon={<ErrorCircle48Regular className={s.icon} />}
        heading="Sign-in did not complete"
        headingAs="h1"
        description="You weren’t signed in, so nothing was opened. You can try again, or contact the person who invited you if this keeps happening."
        descriptionClassName={s.description}
        footer={
          <div className={s.footer}>
            <Text size={200} className={s.code} data-testid="signin-failed-code">
              Error code: {code}
            </Text>
            {startError && (
              <Text size={200} className={s.error} role="alert" data-testid="signin-failed-start-error">
                {startError}
              </Text>
            )}
            <Button ref={buttonRef} appearance="primary" onClick={onTryAgain}>
              Try again
            </Button>
          </div>
        }
      />
    </div>
  );
};

export default SignInFailed;
