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

import * as React from "react";
import { useIsAuthenticated, useMsal } from "@azure/msal-react";
import { InteractionStatus } from "@azure/msal-browser";
import { Spinner } from "@fluentui/react-components";
import { MSAL_BFF_SCOPE } from "../config";

interface AuthGuardProps {
  children: React.ReactNode;
}

export const AuthGuard: React.FC<AuthGuardProps> = ({ children }) => {
  const { instance, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();

  React.useEffect(() => {
    // Only trigger login when MSAL has finished all in-progress interactions
    // and the user is not authenticated. Prevents duplicate redirect loops.
    if (!isAuthenticated && inProgress === InteractionStatus.None) {
      void instance.loginRedirect({
        scopes: [MSAL_BFF_SCOPE],
      });
    }
  }, [isAuthenticated, inProgress, instance]);

  // While MSAL is processing (startup, redirect callback, silent token refresh, etc.)
  if (inProgress !== InteractionStatus.None) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", flex: "1 1 auto" }}>
        <Spinner size="large" label="Signing in..." />
      </div>
    );
  }

  // Unauthenticated + no interaction in progress → login redirect is being triggered
  if (!isAuthenticated) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", flex: "1 1 auto" }}>
        <Spinner size="large" label="Redirecting to sign-in..." />
      </div>
    );
  }

  return <>{children}</>;
};
