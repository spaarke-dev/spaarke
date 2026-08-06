/**
 * Authentication initialization for the TrackingFieldTrio PCF control (task 041,
 * teams-app-r1 — access-grant modal). Adapted from
 * `SemanticSearchControl/authInit.ts` (the established PCF auth-bootstrap
 * pattern, ADR-028).
 *
 * Uses `@spaarke/auth` for centralized MSAL token management — the SAME
 * function-based contract every Spaarke PCF/Code Page uses. All configuration
 * values are resolved at runtime from PCF manifest parameters (which default
 * to empty), falling back to `@spaarke/auth`'s own window-global defaults
 * (`window.__SPAARKE_MSAL_CLIENT_ID__` / `window.__SPAARKE_BFF_URL__`) when a
 * property is left unconfigured — so an environment that already sets those
 * globals (e.g. via a host bootstrap script) needs no manifest configuration.
 *
 * `@spaarke/auth` has NO React dependency — safe to import from this
 * React-16 PCF control.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Initialize `@spaarke/auth` with PCF-specific configuration.
 *
 * @param clientAppId PCF Client Application ID for MSAL authentication (manifest `clientAppId`).
 * @param bffAppId BFF Application ID for scope construction (manifest `bffAppId`).
 * @param bffApiUrl BFF API base URL (manifest `apiBaseUrl`).
 * @param dataverseUrl Dataverse org URL for the MSAL redirect URI (e.g. `https://org.crm.dynamics.com`).
 */
export async function initializeAuth(
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string
): Promise<void> {
  const config: IAuthConfig = {
    // Empty strings fall through to @spaarke/auth's own window-global
    // defaults (documented above) rather than forcing an explicit value.
    clientId: clientAppId || undefined,
    // authority intentionally omitted — @spaarke/auth resolves tenant-specific
    // authority via resolveTenantFromXrm() (frame-walk to
    // Xrm.Utility.getGlobalContext().organizationSettings.tenantId).
    redirectUri: dataverseUrl || undefined,
    bffApiScope: bffAppId ? `api://${bffAppId}/user_impersonation` : undefined,
    bffBaseUrl: bffApiUrl || undefined,
    proactiveRefresh: true,
  };

  await initAuth(config);
}
