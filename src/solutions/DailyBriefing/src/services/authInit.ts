import { initAuth, authenticatedFetch as sharedAuthFetch, getAuthProvider } from "@spaarke/auth";
import { getBffBaseUrl, getBffOAuthScope, getMsalClientId } from "../config/runtimeConfig";

let _initPromise: Promise<void> | null = null;

export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        await initAuth({
          clientId: getMsalClientId(),
          bffBaseUrl: getBffBaseUrl(),
          bffApiScope: getBffOAuthScope(),
          proactiveRefresh: false, // Short-lived dialog
        });
        console.info("[DailyBriefing] @spaarke/auth initialized successfully");
      } catch (err) {
        console.warn("[DailyBriefing] @spaarke/auth initialization failed", err);
        _initPromise = null;
        throw err;
      }
    })();
  }
  return _initPromise;
}

export async function authenticatedFetch(url: string, init?: RequestInit): Promise<Response> {
  await ensureAuthInitialized();
  return sharedAuthFetch(url, init);
}

export async function getTenantId(): Promise<string> {
  await ensureAuthInitialized();
  return getAuthProvider().getTenantId();
}
