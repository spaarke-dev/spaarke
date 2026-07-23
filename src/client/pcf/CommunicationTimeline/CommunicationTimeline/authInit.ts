/**
 * Auth bootstrap for the CommunicationTimeline PCF (ADR-028).
 *
 * Uses `@spaarke/auth` (`initAuth` + `authenticatedFetch`) — the single shared MSAL
 * provider every Spaarke surface uses. Copied unchanged from
 * `CommunicationMessageActions/authInit.ts` (task 062, itself copied from
 * `CommunicationActions/authInit.ts`, task 044) — no behavior difference for this
 * control. `@spaarke/auth` has no React dependency, so it is safe for React 16 PCF
 * controls. Config values are resolved at runtime from manifest inputs / Dataverse
 * env vars — no hardcoded client id, tenant, or BFF URL.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

export async function initializeAuth(
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string
): Promise<void> {
  const config: IAuthConfig = {
    clientId: clientAppId,
    // authority intentionally omitted — @spaarke/auth resolves the tenant-specific
    // authority via resolveTenantFromXrm(); passing one causes a popup regression.
    redirectUri: dataverseUrl,
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };
  await initAuth(config);
}

/** Resolve the Dataverse org URL for the MSAL redirect URI (frame-walk to Xrm). */
export function resolveDataverseUrl(): string {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;
    const xrm = w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
    const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
    if (typeof url === 'string' && url.length > 0) return url;
  } catch {
    /* ignore */
  }
  return window.location.origin;
}
