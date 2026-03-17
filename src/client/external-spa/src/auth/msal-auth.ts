/**
 * MSAL Authentication Module — Secure Project Workspace SPA
 *
 * Provides Azure AD authentication for external users (B2B guests).
 * External users sign in with their own organisation's Microsoft accounts
 * and are provisioned as Azure AD B2B guests in the Spaarke tenant.
 *
 * Token flow:
 *   1. User signs in via MSAL redirect (loginRedirect)
 *   2. MSAL acquires a token for the BFF API scope silently on subsequent calls
 *   3. Token is passed as Bearer in all BFF API requests
 *
 * Configuration required (via .env files):
 *   VITE_MSAL_TENANT_ID   — Spaarke Azure AD tenant ID
 *   VITE_MSAL_CLIENT_ID   — App registration client ID for the external SPA
 *   VITE_BFF_API_SCOPE    — BFF API scope (api://{bff-app-id}/user_impersonation)
 */

import {
  PublicClientApplication,
  Configuration,
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  AccountInfo,
} from "@azure/msal-browser";
import { MSAL_CLIENT_ID, MSAL_TENANT_ID, BFF_API_SCOPE } from "../config";

// ---------------------------------------------------------------------------
// MSAL instance — singleton, initialised once at module load
// ---------------------------------------------------------------------------

const msalConfig: Configuration = {
  auth: {
    clientId: MSAL_CLIENT_ID,
    authority: `https://login.microsoftonline.com/${MSAL_TENANT_ID}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: BrowserCacheLocation.SessionStorage,
    storeAuthStateInCookie: false,
  },
};

export const msalInstance = new PublicClientApplication(msalConfig);

// ---------------------------------------------------------------------------
// Initialise MSAL (must be called before any login/token operations)
// ---------------------------------------------------------------------------

/** Initialise the MSAL instance and handle any redirect promise. */
export async function initializeMsal(): Promise<void> {
  await msalInstance.initialize();
  // Handle the redirect response if returning from a login redirect
  await msalInstance.handleRedirectPromise();
}

// ---------------------------------------------------------------------------
// Authentication helpers
// ---------------------------------------------------------------------------

/** Returns the active account, or null if no user is signed in. */
export function getActiveAccount(): AccountInfo | null {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) return null;
  // Prefer explicitly set active account, fall back to first in list
  return msalInstance.getActiveAccount() ?? accounts[0];
}

/** True if a user is currently signed in. */
export function isAuthenticated(): boolean {
  return getActiveAccount() !== null;
}

/**
 * Initiates the Azure AD login redirect flow.
 * The user will be redirected to Microsoft's login page and back to the SPA.
 */
export async function login(): Promise<void> {
  await msalInstance.loginRedirect({
    scopes: [BFF_API_SCOPE],
    prompt: "select_account",
  });
}

/** Signs the user out and clears the session. */
export async function logout(): Promise<void> {
  const account = getActiveAccount();
  await msalInstance.logoutRedirect({
    account: account ?? undefined,
  });
}

// ---------------------------------------------------------------------------
// Token acquisition
// ---------------------------------------------------------------------------

/**
 * Acquires a Bearer token for the BFF API scope.
 *
 * Tries silent acquisition first. If interaction is required (expired session,
 * MFA prompt, consent needed), falls back to redirect.
 *
 * @throws If no account is signed in and redirect is initiated (SPA will navigate away)
 */
export async function getBffToken(): Promise<string> {
  const account = getActiveAccount();

  if (!account) {
    // No signed-in account — initiate login redirect
    await login();
    // The redirect throws/navigates, but TypeScript doesn't know that
    throw new Error("Redirecting to login...");
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes: [BFF_API_SCOPE],
      account,
    });
    return result.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Silent acquisition failed — need interactive flow
      await msalInstance.acquireTokenRedirect({
        scopes: [BFF_API_SCOPE],
        account,
      });
      throw err; // redirect navigates away, this line is unreachable
    }
    throw err;
  }
}
