/**
 * authInit.ts
 * Lazy auth initialization for the SPE Admin App.
 *
 * Wraps @spaarke/auth's authenticatedFetch with an ensureAuthInitialized()
 * guard so the first API call triggers initialization automatically.
 *
 * Runtime config (BFF URL, OAuth scope, MSAL client ID) is resolved from
 * Dataverse Environment Variables via resolveRuntimeConfig() — no build-time
 * .env.production values are used for these settings.
 */

import {
  initAuth,
  resolveRuntimeConfig,
  authenticatedFetch as sharedAuthFetch,
} from "@spaarke/auth";

let _initPromise: Promise<void> | null = null;

/**
 * Ensures the @spaarke/auth provider is initialized exactly once.
 * Safe to call multiple times — idempotent.
 *
 * Resolves BFF URL, OAuth scope, and MSAL client ID from Dataverse
 * Environment Variables at runtime via resolveRuntimeConfig().
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
        });
      } catch (err) {
        console.warn("[SpeAdminApp] Auth initialization failed:", err);
        _initPromise = null; // Allow retry on next call
        throw err;
      }
    })();
  }
  return _initPromise;
}

/**
 * Authenticated fetch that initializes auth on first use.
 * Use this instead of importing authenticatedFetch directly from @spaarke/auth.
 */
export async function authenticatedFetch(
  url: string,
  init?: RequestInit,
): Promise<Response> {
  await ensureAuthInitialized();
  return sharedAuthFetch(url, init);
}
