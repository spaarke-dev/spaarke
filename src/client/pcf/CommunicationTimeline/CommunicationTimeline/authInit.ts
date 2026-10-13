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

import { initAuth, isValidTenant } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';
import { getXrm } from '@spaarke/ui-components';

export async function initializeAuth(
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string,
  /** The environment's tenant (sprk_TenantId). Optional: when absent or invalid, @spaarke/auth's discovery decides. */
  tenantId?: string
): Promise<void> {
  const config: IAuthConfig = {
    clientId: clientAppId,
    // Pass the environment's tenant so the authority is tenant-specific (a B2B guest signed in against
    // /organizations lands in their home tenant — #1453). The 2026-05-13 popup regression was a malformed
    // `/undefined` authority, which @spaarke/auth now rejects; isValidTenant() filters bad values here.
    ...(isValidTenant(tenantId) ? { tenantId: tenantId.trim() } : {}),
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
    // Shared cross-frame walker (task 081 / C-8).
    const xrm = getXrm('clientUrl');
    const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
    if (typeof url === 'string' && url.length > 0) return url;
  } catch {
    /* ignore */
  }
  return window.location.origin;
}
