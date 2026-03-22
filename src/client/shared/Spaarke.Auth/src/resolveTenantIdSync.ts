/**
 * resolveTenantIdSync.ts
 *
 * Synchronous tenant ID resolution for use in click handlers and other
 * synchronous contexts where the async SpaarkeAuthProvider.getTenantId()
 * cannot be awaited.
 *
 * Resolution order:
 *   1. MSAL auth provider config authority URL (most reliable — set at initAuth time)
 *   2. Xrm.organizationSettings.tenantId via frame hierarchy walk (fallback)
 *   3. Empty string
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

  // 2. Xrm.organizationSettings.tenantId via frame hierarchy walk.
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
