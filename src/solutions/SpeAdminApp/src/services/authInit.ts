/**
 * authInit.ts
 * Lazy auth initialization for the SPE Admin App.
 *
 * Wraps @spaarke/auth's authenticatedFetch with an ensureAuthInitialized()
 * guard so the first API call triggers initialization automatically.
 *
 * Pattern is identical to LegalWorkspace/src/services/authInit.ts.
 */

import {
  initAuth,
  authenticatedFetch as sharedAuthFetch,
} from "@spaarke/auth";
import { getBffBaseUrl } from "../config/bffConfig";

let _initPromise: Promise<void> | null = null;

/**
 * Ensures the @spaarke/auth provider is initialized exactly once.
 * Safe to call multiple times — idempotent.
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        await initAuth({ bffBaseUrl: getBffBaseUrl() });
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
