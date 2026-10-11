/**
 * AuthGuard — MSAL-aware authentication gate for the Secure Project Workspace SPA.
 *
 * Renders children only when an authenticated account is present and MSAL has
 * finished processing any in-progress interaction (e.g., auth code redirect).
 *
 * If no account is found and no interaction is in progress, triggers a login redirect
 * to Entra (B2B guest login with Microsoft 365 credentials) — UNLESS the redirect sign-in just failed (Entra returned
 * an error such as `access_denied`): then the gate shows <SignInFailed> and starts no redirect of its own, so a declined
 * sign-in cannot loop. Only the "Try again" click starts another redirect.
 *
 * Place this inside <MsalProvider> (already set up in main.tsx), wrapping all
 * authenticated routes in App.tsx.
 *
 * See: notes/auth-migration-b2b-msal.md
 */

import * as React from 'react';
import { useNavigate } from 'react-router-dom';
import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { Spinner } from '@fluentui/react-components';
import {
  accountsForActivePlane,
  captureReturnTo,
  consumeReturnTo,
  getActiveLoginScope,
  getSignInFailure,
  safeSignInErrorCode,
  subscribeSignInFailure,
  type SignInFailure,
} from '../auth/msal-auth';
import { SignInFailed } from './auth/SignInFailed';
import { isDevMockEnabled } from '../dev-mock';

interface AuthGuardProps {
  children: React.ReactNode;
  /**
   * True when running inside the Teams host. In Teams, authentication is completed during bootstrap
   * (TeamsHostAdapter.selectAuthStrategy acquires a BFF-valid workforce token — via NAA, then the
   * MSAL popup fallback (ADR-028 A7; no Teams SSO) — BEFORE <App> is rendered), so this guard must NOT
   * run the MSAL account gate below. See the early return for the full rationale.
   */
  teamsHost?: boolean;
}

export const AuthGuard: React.FC<AuthGuardProps> = ({ children, teamsHost = false }) => {
  // In mock mode, skip MSAL entirely — render children as if authenticated.
  if (isDevMockEnabled()) {
    return <>{children}</>;
  }

  // Inside the Teams host, the caller is already authenticated by the time this guard mounts:
  // main.tsx only renders <App> after TeamsHostAdapter.initialize() resolves, which means a
  // BFF-valid workforce token was acquired (NAA, else the MSAL popup fallback — ADR-028 A7) and the
  // active BFF token acquirer is wired. Both paths leave the signed-in account in the MSAL cache of
  // the instance handed to <MsalProvider>, but the MSAL account gate below must still never run in
  // Teams: if it ever fired instance.loginRedirect(), a full-page redirect inside the Teams iframe is
  // blocked (NFR-04 — never redirect inside Teams), leaving a blank tab. So in Teams, render children
  // directly; bootstrap has already guaranteed authentication. (In a plain browser the gate applies.)
  if (teamsHost) {
    return <>{children}</>;
  }

  return <MsalAccountGate>{children}</MsalAccountGate>;
};

const SIGN_IN_NOT_STARTED = 'Sign-in could not be started. Reload the page and try again.';

/**
 * The MSAL account gate itself — split out of AuthGuard by unified-access-control-r2 task 140 so that its hooks are
 * never called after AuthGuard's early returns (react-hooks/rules-of-hooks; behaviour unchanged: the mock and Teams
 * paths render children without MSAL, every other path runs exactly the gate below).
 */
const MsalAccountGate: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { instance, inProgress, accounts } = useMsal();
  // Same rule as msal-react's useIsAuthenticated (an account exists once MSAL has started), but scoped to the
  // selected plane: a CIAM account cached in this tab is not a workforce sign-in (accountsForActivePlane).
  const isAuthenticated = inProgress !== InteractionStatus.Startup && accountsForActivePlane(accounts).length > 0;
  const navigate = useNavigate();
  // A redirect sign-in that came back with an error (recorded by the MSAL event callback registered before initialize()).
  // Scoped to THIS gate's MSAL instance (= plane): a failure recorded for the other plane is not seen here.
  const recordedFailure = React.useSyncExternalStore(
    subscribeSignInFailure,
    () => getSignInFailure(instance),
    () => getSignInFailure(instance)
  );
  // The automatic redirect itself was rejected (interaction_in_progress, a network failure, no active backend…):
  // the safe code only, same screen. Kept apart from the recorded one because it also covers a SYNCHRONOUS throw.
  const [autoStartFailure, setAutoStartFailure] = React.useState<SignInFailure | null>(null);
  const signInFailure = recordedFailure ?? autoStartFailure;
  const [startError, setStartError] = React.useState<string | null>(null);

  React.useEffect(() => {
    // Only trigger login when MSAL has finished all in-progress interactions
    // and the user is not authenticated. Prevents duplicate redirect loops.
    // Never while a failed sign-in is showing: re-firing here is the redirect loop (declined consent → Entra → declined…).
    if (!isAuthenticated && inProgress === InteractionStatus.None && !signInFailure) {
      // Preserve the intended deep link (e.g. an emailed /project/{id}) across the redirect.
      captureReturnTo();
      // Request the BFF scope for the CURRENTLY-SELECTED plane's authority (task 013): CIAM by
      // default (byte-for-byte unchanged), or the workforce scope when the browser realm chooser
      // picked "My organization". A CIAM instance cannot mint a workforce-audience token, so the
      // scope must track the plane the mounted MSAL instance was built for.
      // A rejected start (interaction_in_progress, network failure, …) must not leave the spinner up forever: show the
      // failure screen with its retry. No automatic retry here — that is the loop the failure gate exists to stop.
      try {
        void Promise.resolve(
          instance.loginRedirect({
            scopes: [getActiveLoginScope()],
          })
        ).catch((error: unknown) => setAutoStartFailure(safeSignInErrorCode(error)));
      } catch (error) {
        setAutoStartFailure(safeSignInErrorCode(error));
      }
    }
  }, [isAuthenticated, inProgress, instance, signInFailure]);

  // Signed in: the gate has no failure of its own to show. A failure RECORDED for the instance stays (a failed token
  // redirect, which `acquireStandaloneBffToken` reads so it does not redirect again); it is only ever shown above while
  // there is no account.
  React.useEffect(() => {
    if (isAuthenticated) setAutoStartFailure(null);
  }, [isAuthenticated]);

  // "Try again" on the failure screen: the one place a user gesture starts another redirect.
  const tryAgain = React.useCallback(() => {
    setStartError(null);
    try {
      captureReturnTo();
      void Promise.resolve(instance.loginRedirect({ scopes: [getActiveLoginScope()] })).catch(() =>
        setStartError(SIGN_IN_NOT_STARTED)
      );
    } catch {
      setStartError(SIGN_IN_NOT_STARTED);
    }
  }, [instance]);

  React.useEffect(() => {
    // After authentication completes, restore the intended deep link once (react-router soft
    // nav, replacing history so the back button does not re-enter the login flow). Only safe
    // in-app relative paths are restored (consumeReturnTo validates). No-ops for the default
    // path or when MSAL already landed the user on the intended route.
    if (isAuthenticated && inProgress === InteractionStatus.None) {
      const target = consumeReturnTo();
      const current = window.location.pathname + window.location.search;
      if (target && target !== current) {
        navigate(target, { replace: true });
      }
    }
  }, [isAuthenticated, inProgress, navigate]);

  // While MSAL is processing (startup, redirect callback, silent token refresh, etc.)
  if (inProgress !== InteractionStatus.None) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', flex: '1 1 auto' }}>
        <Spinner size="large" label="Signing in..." />
      </div>
    );
  }

  // Unauthenticated after a FAILED redirect sign-in → fail loud; no automatic redirect while this shows.
  if (!isAuthenticated && signInFailure) {
    return <SignInFailed failure={signInFailure} onTryAgain={tryAgain} startError={startError} />;
  }

  // Unauthenticated + no interaction in progress → login redirect is being triggered
  if (!isAuthenticated) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', flex: '1 1 auto' }}>
        <Spinner size="large" label="Redirecting to sign-in..." />
      </div>
    );
  }

  return <>{children}</>;
};
