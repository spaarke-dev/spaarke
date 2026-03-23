/**
 * resolveTenantIdSync.ts
 *
 * Synchronous tenant ID resolution for use in click handlers and other
 * synchronous contexts where the async SpaarkeAuthProvider.getTenantId()
 * cannot be awaited.
 *
 * Resolution order:
 *   1. MSAL auth provider config authority URL (tenant-specific authority only)
 *   2. MSAL accounts[0].tenantId — from the JWT, populated after silent auth
 *   3. Xrm.organizationSettings.tenantId via frame hierarchy walk (fallback)
 *   4. Empty string
 *
 * Why step 2 matters: when initAuth() is called without an explicit tenantId,
 * MSAL defaults to the 'organizations' authority. Step 1 filters this out.
 * But after MSAL completes a silent token acquisition, accounts[0].tenantId
 * contains the real tenant GUID extracted from the JWT — step 2 captures this.
 *
 * This consolidates the pattern that previously existed independently in:
 *   - DocumentUploadWizard/src/services/nextStepLauncher.ts
 *   - SemanticSearchControl/services/NavigationService.ts
 *   - LegalWorkspace/src/config/runtimeConfig.ts
 */

import { getAuthProvider } from './initAuth';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Resolve the Azure AD tenant ID synchronously.
 *
 * Safe to call from click handlers — both MSAL and Xrm are fully
 * available long before any user interaction can trigger this.
 *
 * @returns Tenant ID GUID string, or empty string if not resolvable.
 */
export function resolveTenantIdSync(): string {
  // 1. MSAL authority URL — reliable if initAuth() has been called.
  //    Authority format: "https://login.microsoftonline.com/{tenantId}"
  try {
    const authority = getAuthProvider().getConfig().authority ?? '';
    if (authority) {
      const parts = authority.split('/');
      const tenantId = parts[parts.length - 1] ?? '';
      if (tenantId && tenantId !== 'common' && tenantId !== 'organizations') {
        return tenantId;
      }
    }
  } catch {
    // Auth provider not yet initialized — try Xrm fallback.
  }

  // 2. MSAL accounts — populated after initAuth() completes silent token acquisition.
  //    getAllAccounts() is synchronous; tenantId is extracted from the JWT.
  //    This covers the common case where initAuth() uses the 'organizations' authority
  //    (no tenant-specific URL to parse from step 1) but MSAL has already authenticated.
  try {
    const tenantId = getAuthProvider().getCachedTenantId();
    if (tenantId) return tenantId;
  } catch {
    // Auth provider not yet initialized — continue to Xrm fallback.
  }

  // 3. Xrm.organizationSettings.tenantId via frame hierarchy walk.
  if (typeof window !== 'undefined') {
    const frames: Window[] = [window];
    try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

    for (const frame of frames) {
      try {
        const tenantId = (frame as any).Xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId as string | undefined;
        if (tenantId) return tenantId;
      } catch { /* cross-origin */ }
    }
  }

  return '';
}

/* eslint-enable @typescript-eslint/no-explicit-any */
