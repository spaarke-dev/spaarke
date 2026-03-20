/**
 * Authentication initialization for UniversalDatasetGrid PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * All configuration values (CLIENT_ID, BFF URL, BFF APP ID) are resolved
 * at runtime from Dataverse Environment Variables via the shared
 * environmentVariables.ts utility. No hardcoded dev values.
 *
 * @spaarke/auth has NO React dependency — safe for React 16 PCF controls.
 *
 * Usage:
 *   import { initializeAuth } from './authInit';
 *   await initializeAuth(context.webAPI);
 *
 * Then use authenticatedFetch() from '@spaarke/auth' for all BFF API calls,
 * or getAuthProvider().getAccessToken() for manual token acquisition.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';
import {
  getEnvironmentVariable,
  getApiBaseUrl,
} from '../../shared/utils/environmentVariables';

// ============================================================================
// Xrm context resolution (for redirect URI)
// ============================================================================

/**
 * Resolve the Dataverse org URL from Xrm.Utility.getGlobalContext().
 * Available pre-auth in all Dataverse web resources.
 */
function resolveClientUrl(): string {
  if (typeof window === 'undefined') {
    throw new Error('[authInit] Cannot resolve Xrm context — window is undefined');
  }

  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) {
      frames.push(window.top);
    }
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      const ctx = xrm?.Utility?.getGlobalContext?.();
      if (ctx && typeof ctx.getClientUrl === 'function') {
        const url = ctx.getClientUrl() as string;
        if (url) return url;
      }
    } catch {
      /* cross-origin */
    }
  }

  throw new Error(
    '[authInit] Xrm.Utility.getGlobalContext().getClientUrl() not available. ' +
    'Ensure the control is running within a Dataverse model-driven app.'
  );
}

// ============================================================================
// Public API
// ============================================================================

/**
 * Initialize @spaarke/auth with PCF-specific configuration resolved at runtime.
 *
 * All configuration values are fetched from Dataverse Environment Variables:
 *   - sprk_MsalClientId   → Azure AD Application (Client) ID
 *   - sprk_BffApiBaseUrl   → BFF API base URL
 *   - sprk_BffApiAppId     → BFF API Application ID (for scope construction)
 *
 * The redirect URI is derived from Xrm.Utility.getGlobalContext().getClientUrl()
 * (the Dataverse org URL, e.g., https://orgname.crm.dynamics.com).
 *
 * Authority uses multi-tenant 'organizations' endpoint (no hardcoded tenant ID).
 *
 * Idempotent: @spaarke/auth handles re-initialization by disposing previous instance.
 *
 * @param webApi - PCF WebAPI instance for querying Dataverse environment variables
 */
export async function initializeAuth(
  webApi: ComponentFramework.WebApi
): Promise<void> {
  console.info('[authInit] Initializing @spaarke/auth for UniversalDatasetGrid...');

  // Resolve redirect URI from Xrm context (Dataverse org URL)
  const redirectUri = resolveClientUrl();
  console.info(`[authInit] Resolved redirectUri: ${redirectUri}`);

  // Resolve config values from Dataverse environment variables
  const [clientId, bffApiUrl, bffAppId] = await Promise.all([
    getEnvironmentVariable(webApi, 'sprk_MsalClientId'),
    getApiBaseUrl(webApi),
    getEnvironmentVariable(webApi, 'sprk_BffApiAppId'),
  ]);

  console.info(
    `[authInit] Resolved: clientId=${clientId.substring(0, 8)}..., ` +
    `bffApiUrl=${bffApiUrl.substring(0, 30)}..., ` +
    `bffAppId=${bffAppId.substring(0, 8)}...`
  );

  const config: IAuthConfig = {
    clientId,
    // Multi-tenant authority — no hardcoded tenant ID
    authority: 'https://login.microsoftonline.com/organizations',
    // CRITICAL: Static redirect URI matching Azure AD app registration
    // Must be the Dataverse org URL, NOT window.location.origin
    redirectUri,
    // Named scope: api://<BFF_APP_ID>/SDAP.Access
    bffApiScope: `api://${bffAppId}/SDAP.Access`,
    bffBaseUrl: bffApiUrl,
    // PCF controls benefit from proactive refresh to avoid token expiry during long sessions
    proactiveRefresh: true,
  };

  await initAuth(config);

  console.info('[authInit] @spaarke/auth initialized successfully for UniversalDatasetGrid');
}

/**
 * Resolved BFF API base URL — cached for use by API clients.
 * Call initializeAuth() first; this function resolves the URL independently.
 *
 * @param webApi - PCF WebAPI instance for querying Dataverse environment variables
 * @returns BFF API base URL
 */
export async function resolveBffApiUrl(
  webApi: ComponentFramework.WebApi
): Promise<string> {
  return getApiBaseUrl(webApi);
}
