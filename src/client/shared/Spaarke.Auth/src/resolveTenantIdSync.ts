/**
 * resolveTenantIdSync.ts
 *
 * Synchronous tenant ID resolution for use in click handlers and other
 * synchronous contexts where the async SpaarkeAuthProvider.getTenantId()
 * cannot be awaited.
 *
 * Resolution order (every value validated — a GUID or dotted domain, never
 * 'organizations' / 'common' / 'consumers' / 'undefined' / 'null'):
 *   1. Tenant segment of the provider's authority URL (trailing '/' and '/v2.0'
 *      tolerated; a non-tenant or malformed authority is skipped)
 *   2. `tid` of the cached access token
 *   3. window.__SPAARKE_TENANT_ID__ → persisted runtime config →
 *      Xrm.organizationSettings.tenantId via frame walk ("TENANT PRECEDENCE", tenant.ts)
 *   4. Empty string
 *
 * Inside a Dataverse host the authority is always tenant-specific (#1453 —
 * resolveConfig refuses /organizations there), so step 1 normally answers.
 * Steps 2-3 cover a provider outside Dataverse that fell back to
 * /organizations, and calls made before initAuth() has run.
 *
 * This consolidates the pattern that previously existed independently in:
 *   - DocumentUploadWizard/src/services/nextStepLauncher.ts
 *   - SemanticSearchControl/services/NavigationService.ts
 *   - LegalWorkspace/src/config/runtimeConfig.ts
 */

import { getAuthProvider } from './initAuth';
import { discoverTenantSync } from './config';
import { normalizeTenant, tenantFromAuthority } from './tenant';

/**
 * Resolve the Azure AD tenant ID synchronously.
 *
 * Safe to call from click handlers — both MSAL and Xrm are fully
 * available long before any user interaction can trigger this.
 *
 * @returns Tenant ID string, or empty string if not resolvable.
 */
export function resolveTenantIdSync(): string {
  // 1. Authority URL of the initialized provider.
  try {
    const tenantId = tenantFromAuthority(getAuthProvider().getConfig().authority);
    if (tenantId) return tenantId;
  } catch {
    // Auth provider not yet initialized — try the fallbacks.
  }

  // 2. tid of the cached token — populated after initAuth() acquires a token.
  try {
    const tenantId = normalizeTenant(getAuthProvider().getCachedTenantId());
    if (tenantId) return tenantId;
  } catch {
    // Auth provider not yet initialized — continue to the host chain.
  }

  // 3. Host chain: window.__SPAARKE_TENANT_ID__ → persisted runtime config → Xrm.
  return discoverTenantSync()?.tenant ?? '';
}
