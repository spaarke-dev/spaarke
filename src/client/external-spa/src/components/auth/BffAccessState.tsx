/**
 * BffAccessState — what a page shows when the BFF refuses the caller (spaarke-SPA-external-access-platform-r3 task 042,
 * FR-18, #1566):
 *   - 401 (after bff-client's one forced-refresh retry) or a failed Teams token acquisition → the SIGN-IN state: the
 *     token is not accepted (or could not be had), so the caller must sign in again. Browser: an MSAL login redirect
 *     for the active plane's scope (the same request AuthGuard makes, plus `prompt: 'select_account'` so the
 *     identity provider does not silently hand back the session whose token was just refused), returning to this
 *     page. Teams: a reload, which re-runs the Teams bootstrap (NAA, then the popup screen) — a redirect inside the
 *     Teams iframe is never allowed (NFR-04). If the sign-in cannot even be started (no active backend, MSAL
 *     refusing), the bar says so instead of leaving a button that does nothing.
 *   - 403 → the DENIED state: signed in, but not allowed here. No action beyond going back.
 * Neither state shows a user — no mock, default or cached identity.
 *
 * Fluent v9 semantic tokens only (ADR-021); mirrors ProjectPage's denied MessageBar.
 */
import * as React from 'react';
import { Button, MessageBar, MessageBarActions, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import { useMsal } from '@azure/msal-react';
import { TeamsWorkforceAuthError, captureReturnTo, getActiveLoginScope } from '../../auth/msal-auth';
import { ApiError } from '../../types';

/** The two refusals a page renders as a state of its own. */
export type BffAccessFailure = 'signin' | 'denied';

/**
 * 401 → 'signin', 403 → 'denied', a failed Teams workforce token acquisition → 'signin' (the host could not sign the
 * caller in; "Sign in again" re-runs the Teams sign-in), anything else → null (the caller's own error handling applies).
 */
export function accessFailureOf(err: unknown): BffAccessFailure | null {
  if (err instanceof TeamsWorkforceAuthError) return 'signin';
  if (!(err instanceof ApiError)) return null;
  if (err.statusCode === 401) return 'signin';
  if (err.statusCode === 403) return 'denied';
  return null;
}

const SIGN_IN_UNAVAILABLE =
  'Sign-in could not be started from here. Reload the page, or open the link again from your invitation. If this keeps happening, contact the record team.';

export interface BffAccessStateProps {
  failure: BffAccessFailure;
  /** True inside the Teams host: sign-in again reloads the tab instead of redirecting. */
  teamsHost?: boolean;
  /** Denied-state sentence naming what the caller cannot do (defaults to a generic one). */
  deniedMessage?: string;
}

export const BffAccessState: React.FC<BffAccessStateProps> = ({ failure, teamsHost = false, deniedMessage }) => {
  const { instance } = useMsal();

  // Set when the sign-in could not be started (e.g. getActiveLoginScope() throws BackendSelectionError: no active
  // backend), so the button never does nothing.
  const [signInError, setSignInError] = React.useState<string | null>(null);

  const handleSignIn = React.useCallback(() => {
    setSignInError(null);
    try {
      if (teamsHost) {
        window.location.reload();
        return;
      }
      captureReturnTo();
      // select_account: the cached session's token was just refused, so do not let the IdP sign the same session back
      // in without asking.
      void Promise.resolve(instance.loginRedirect({ scopes: [getActiveLoginScope()], prompt: 'select_account' })).catch(
        () => setSignInError(SIGN_IN_UNAVAILABLE)
      );
    } catch {
      setSignInError(SIGN_IN_UNAVAILABLE);
    }
  }, [instance, teamsHost]);

  if (failure === 'signin') {
    return (
      <MessageBar intent="warning" data-testid="bff-access-signin">
        <MessageBarBody>
          <MessageBarTitle>Sign in required</MessageBarTitle>
          Your sign-in has expired or is not accepted here. Sign in again to continue.
          {signInError && (
            <div data-testid="bff-access-signin-error" role="alert">
              {signInError}
            </div>
          )}
        </MessageBarBody>
        <MessageBarActions>
          <Button appearance="primary" onClick={handleSignIn}>
            Sign in again
          </Button>
        </MessageBarActions>
      </MessageBar>
    );
  }

  return (
    <MessageBar intent="warning" data-testid="bff-access-denied">
      <MessageBarBody>
        <MessageBarTitle>Access denied</MessageBarTitle>
        {deniedMessage ??
          'You do not have permission to do this. If you believe this is an error, please contact the record team.'}
      </MessageBarBody>
    </MessageBar>
  );
};

export default BffAccessState;
