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
import { MSAL_BFF_SCOPE } from '../config';
import { captureReturnTo, consumeReturnTo } from '../auth/msal-auth';

interface AuthGuardProps {
  children: React.ReactNode;
}

export const AuthGuard: React.FC<AuthGuardProps> = ({ children }) => {
  // In mock mode, skip MSAL entirely — render children as if authenticated.
  if (import.meta.env.VITE_DEV_MOCK === 'true') {
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
      void instance.loginRedirect({
        scopes: [MSAL_BFF_SCOPE],
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
