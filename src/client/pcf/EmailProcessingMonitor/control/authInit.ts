/**
 * Authentication initialization for EmailProcessingMonitor PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * The redirect URI is resolved at runtime from Xrm.Utility.getGlobalContext()
 * so the same bundle works against any Dataverse environment.
 *
 * Authority is intentionally NOT specified here — @spaarke/auth resolves a
 * tenant-specific authority via resolveTenantFromXrm() (binding requirement;
 * see feedback_auth-true-sso-requirement.md).
 *
 * @spaarke/auth has NO React dependency — safe for React 16 virtual PCFs.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Frame-walk to read the Dataverse org URL from Xrm.
 * Throws if Xrm isn't reachable.
 */
function resolveClientUrl(): string {
  if (typeof window === 'undefined') {
    throw new Error('[authInit] window is undefined — cannot resolve Xrm context');
  }
  const frames: Window[] = [window];
  try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
  try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top); } catch { /* cross-origin */ }
  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      const ctx = xrm?.Utility?.getGlobalContext?.();
      if (ctx && typeof ctx.getClientUrl === 'function') {
        const url = ctx.getClientUrl() as string;
        if (url) return url;
      }
    } catch { /* cross-origin */ }
  }
  throw new Error('[authInit] Xrm.Utility.getGlobalContext().getClientUrl() not available');
}

/**
 * Initialize @spaarke/auth for EmailProcessingMonitor.
 *
 * @param clientAppId PCF Client Application ID (from manifest property)
 * @param bffAppId BFF API Application ID (from manifest property, for scope construction)
 * @param bffApiUrl BFF API base URL (from manifest property)
 */
export async function initializeAuth(
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string
): Promise<void> {
  console.info('[authInit] Initializing @spaarke/auth for EmailProcessingMonitor...');

  const config: IAuthConfig = {
    clientId: clientAppId,
    // authority intentionally omitted — @spaarke/auth resolves tenant-specific authority
    redirectUri: resolveClientUrl(),
    bffApiScope: `api://${bffAppId}/SDAP.Access`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };

  await initAuth(config);
  console.info('[authInit] @spaarke/auth initialized for EmailProcessingMonitor');
}
