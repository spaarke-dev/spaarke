/**
 * @deprecated This file is FULLY DEPRECATED and retained only as a tombstone.
 *
 * All MSAL configuration is now resolved at runtime from Dataverse environment variables:
 *   - sprk_MsalClientId -> CLIENT_ID
 *   - sprk_TenantId -> TENANT_ID
 *   - sprk_BffApiAppId -> BFF_APP_ID (for scope construction)
 *   - sprk_BffApiBaseUrl -> BFF API URL
 *
 * Redirect URI is derived from Xrm.Utility.getGlobalContext().getClientUrl().
 *
 * See:
 *   - authInit.ts -- initializes @spaarke/auth with runtime config
 *   - index.ts -- resolves env vars via shared/utils/environmentVariables.ts
 *   - @spaarke/auth -- shared MSAL library (authenticatedFetch, initAuth)
 *
 * DO NOT import from this file. It will be removed in a future cleanup.
 */

export {};
