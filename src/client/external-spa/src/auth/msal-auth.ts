/**
 * MSAL token acquisition for BFF API calls.
 *
 * Replaces the portal implicit grant flow (portal-auth.ts).
 * Acquires tokens silently via MSAL cache; falls back to redirect on
 * InteractionRequiredAuthError (consent required, MFA prompt, session expired, etc.).
 *
 * Called by bff-client.ts before every BFF API request.
 *
 * See: docs/architecture/power-pages-spa-guide.md — Authentication section
 * See: notes/auth-migration-b2b-msal.md
 */

import { InteractionRequiredAuthError } from "@azure/msal-browser";
import { msalInstance } from "./msal-config";
import { MSAL_BFF_SCOPE } from "../config";

/**
 * Acquire an access token for the BFF API.
 *
 * Strategy:
 *   1. Try silent acquisition (MSAL cache / refresh token).
 *   2. On InteractionRequiredAuthError, trigger a redirect login.
 *   3. Throw after initiating redirect so callers abort the current request.
 *
 * MSAL handles token caching internally — no manual cache management needed
 * (unlike the previous portal implicit grant flow).
 *
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 * @throws If silent acquisition fails for a non-interaction reason (network error, etc.).
 */
export async function acquireBffToken(): Promise<string> {
  const accounts = msalInstance.getAllAccounts();

  if (accounts.length === 0) {
    // No authenticated account — trigger login redirect
    await msalInstance.acquireTokenRedirect({ scopes: [MSAL_BFF_SCOPE] });
    // acquireTokenRedirect navigates away; this throw aborts the current call chain
    throw new Error("No authenticated account — redirecting to login");
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes: [MSAL_BFF_SCOPE],
      account: accounts[0],
    });
    return result.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Token expired, consent needed, MFA required, etc. — trigger redirect
      await msalInstance.acquireTokenRedirect({
        scopes: [MSAL_BFF_SCOPE],
        account: accounts[0],
      });
      throw new Error("Interaction required — redirecting to login");
    }
    throw err;
  }
}
