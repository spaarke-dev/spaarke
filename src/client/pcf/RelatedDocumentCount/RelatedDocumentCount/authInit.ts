/**
 * Authentication initialization for RelatedDocumentCount PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * All configuration values (Client ID, Tenant ID, BFF App ID) are resolved
 * at runtime from Dataverse environment variables via environmentVariables.ts.
 *
 * No hardcoded CLIENT_ID, TENANT_ID, BFF_APP_ID, or redirect URI.
 *
 * @spaarke/auth has NO React dependency — safe for React 16 PCF controls.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';
import {
  getApiBaseUrl,
  getMsalClientId,
  getBffApiAppId,
  getTenantId,
} from '../../shared/utils/environmentVariables';

/**
 * Get the Dataverse client URL from the Xrm global.
 * Used as MSAL redirect URI (must match Azure AD app registration).
 * Falls back to window.location.origin if Xrm is not available (e.g., test harness).
 */
function getClientUrl(): string {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm as
      | {
          Utility?: {
            getGlobalContext?: () => { getClientUrl?: () => string };
          };
        }
      | undefined;
    const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
    if (url) return url.replace(/\/$/, '');
  } catch {
    // Xrm not available (test harness, dev mode)
  }
  return window.location.origin;
}

/**
 * Resolved runtime configuration values returned after auth initialization.
 */
export interface ResolvedAuthConfig {
  /** BFF API base URL resolved from sprk_BffApiBaseUrl env var */
  bffApiUrl: string;
  /** Azure AD Tenant ID resolved from sprk_TenantId env var */
  tenantId: string;
}

/**
 * Initialize @spaarke/auth with PCF-specific configuration.
 *
 * Resolves all auth config from Dataverse environment variables:
 * - sprk_MsalClientId -> MSAL Client (Application) ID
 * - sprk_TenantId -> Azure AD Tenant (Directory) ID
 * - sprk_BffApiAppId -> BFF API Application ID (for scope construction)
 * - sprk_BffApiBaseUrl -> BFF API base URL
 *
 * Redirect URI is resolved from Xrm.Utility.getGlobalContext().getClientUrl().
 *
 * @param webApi - PCF WebAPI instance for querying Dataverse environment variables
 * @returns Resolved config values (bffApiUrl, tenantId) for downstream use
 */
export async function initializeAuth(webApi: ComponentFramework.WebApi): Promise<ResolvedAuthConfig> {
  console.info('[authInit] Initializing @spaarke/auth for RelatedDocumentCount...');

  // Resolve all config from Dataverse environment variables (no hardcoded values)
  const [clientId, tenantId, bffAppId, bffApiUrl] = await Promise.all([
    getMsalClientId(webApi),
    getTenantId(webApi),
    getBffApiAppId(webApi),
    getApiBaseUrl(webApi),
  ]);

  if (!clientId) {
    throw new Error('[authInit] sprk_MsalClientId not configured in Dataverse environment variables.');
  }
  if (!tenantId) {
    throw new Error('[authInit] sprk_TenantId not configured in Dataverse environment variables.');
  }
  if (!bffAppId) {
    throw new Error('[authInit] sprk_BffApiAppId not configured in Dataverse environment variables.');
  }

  const redirectUri = getClientUrl();

  const config: IAuthConfig = {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri,
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
  };

  await initAuth(config);

  console.info('[authInit] @spaarke/auth initialized successfully for RelatedDocumentCount');

  return { bffApiUrl, tenantId };
}
