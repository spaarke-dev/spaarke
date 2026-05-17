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
/**
 * Resolve the Azure AD tenant ID synchronously.
 *
 * Safe to call from click handlers — both MSAL and Xrm are fully
 * available long before any user interaction can trigger this.
 *
 * @returns Tenant ID GUID string, or empty string if not resolvable.
 */
export declare function resolveTenantIdSync(): string;
//# sourceMappingURL=resolveTenantIdSync.d.ts.map
