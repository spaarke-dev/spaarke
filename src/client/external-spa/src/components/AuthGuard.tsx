/**
 * AuthGuard — MSAL-aware authentication gate for the Secure Project Workspace SPA.
 *
 * Renders children only when an authenticated account is present and MSAL has
 * finished processing any in-progress interaction (e.g., auth code redirect).
 *
 * If no account is found and no interaction is in progress, triggers a login redirect
 * to Entra (B2B guest login with Microsoft 365 credentials).
 *
 * Place this inside <MsalProvider> (already set up in main.tsx), wrapping all
 * authenticated routes in App.tsx.
 *
 * See: notes/auth-migration-b2b-msal.md
 */

import * as React from 'react';
import { useNavigate } from 'react-router-dom';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { Spinner } from '@fluentui/react-components';
import { captureReturnTo, consumeReturnTo, getActiveLoginScope } from '../auth/msal-auth';

interface AuthGuardProps {
  children: React.ReactNode;
  /**
   * True when running inside the Teams host. In Teams, authentication is completed during bootstrap
   * (TeamsHostAdapter.selectAuthStrategy acquires a BFF-valid workforce token — via NAA or the Teams
   * SSO getAuthToken fallback — BEFORE <App> is rendered), so this guard must NOT run the MSAL
   * account gate below. See the early return for the full rationale.
   */
  teamsHost?: boolean;
}

export const AuthGuard: React.FC<AuthGuardProps> = ({ children, teamsHost = false }) => {
  // In mock mode, skip MSAL entirely — render children as if authenticated.
  if (import.meta.env.VITE_DEV_MOCK === 'true') {
    return <>{children}</>;
  }

  // Inside the Teams host, the caller is already authenticated by the time this guard mounts:
  // main.tsx only renders <App> after TeamsHostAdapter.initialize() resolves, which means a
  // BFF-valid workforce token was acquired and the active BFF token acquirer is wired. The Teams
  // SSO fallback (authentication.getAuthToken) that desktop relies on returns a RAW bearer token
  // that never lands in the MSAL account cache — so useIsAuthenticated() would be false here even
  // though the user is fully authenticated. Running the MSAL gate below would then fire
  // instance.loginRedirect(), and a full-page redirect inside the Teams iframe is blocked
  // (NFR-04 — never redirect inside Teams), leaving a blank tab. So in Teams, render children
  // directly. (On web, NAA populates the account and the standard gate below applies.)
  if (teamsHost) {
    return <>{children}</>;
  }

  const { instance, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const navigate = useNavigate();

  React.useEffect(() => {
    // Only trigger login when MSAL has finished all in-progress interactions
    // and the user is not authenticated. Prevents duplicate redirect loops.
    if (!isAuthenticated && inProgress === InteractionStatus.None) {
      // Preserve the intended deep link (e.g. an emailed /project/{id}) across the redirect.
      captureReturnTo();
      // Request the BFF scope for the CURRENTLY-SELECTED plane's authority (task 013): CIAM by
      // default (byte-for-byte unchanged), or the workforce scope when the browser realm chooser
      // picked "My organization". A CIAM instance cannot mint a workforce-audience token, so the
      // scope must track the plane the mounted MSAL instance was built for.
      void instance.loginRedirect({
        scopes: [getActiveLoginScope()],
      });
    }
  }, [isAuthenticated, inProgress, instance]);

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
