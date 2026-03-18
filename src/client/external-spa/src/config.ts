/**
 * Configuration for the Secure Project Workspace SPA.
 *
 * Authentication: Entra B2B + MSAL authorization code flow + PKCE.
 * External users are B2B guest accounts in the main Spaarke workforce tenant.
 * They authenticate with their existing Microsoft 365 credentials (SSO).
 *
 * Values are loaded from environment files:
 *   .env.development — local dev / dev environment
 *   .env.production  — production build
 *
 * See: docs/architecture/power-pages-spa-guide.md
 * See: notes/auth-migration-b2b-msal.md
 */

/** BFF API base URL */
export const BFF_API_URL: string =
  (import.meta.env.VITE_BFF_API_URL as string | undefined) ??
  "https://spe-api-dev-67e2xz.azurewebsites.net";

/**
 * MSAL client ID for the SPA app registration (spaarke-external-access-SPA).
 * App registration: f306885a-8251-492c-8d3e-34d7b476ffd0
 * Tenant: a221a95e-6abc-4434-aecc-e48338a1b2f2 (main Spaarke workforce tenant)
 */
export const MSAL_CLIENT_ID: string =
  (import.meta.env.VITE_MSAL_CLIENT_ID as string | undefined) ??
  "f306885a-8251-492c-8d3e-34d7b476ffd0";

/**
 * Entra tenant ID (main Spaarke workforce tenant).
 * External users are B2B guests in this tenant — SSO with Microsoft 365 credentials.
 */
export const MSAL_TENANT_ID: string =
  (import.meta.env.VITE_MSAL_TENANT_ID as string | undefined) ??
  "a221a95e-6abc-4434-aecc-e48338a1b2f2";

/**
 * BFF API OAuth scope for token acquisition.
 * Defined on SDAP-BFF-SPE-API app registration (1e40baad-e065-4aea-a8d4-4b7ab273458c).
 */
export const MSAL_BFF_SCOPE: string =
  (import.meta.env.VITE_MSAL_BFF_SCOPE as string | undefined) ??
  "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access";

/** App version — update on each release */
export const APP_VERSION = "1.0.0";
