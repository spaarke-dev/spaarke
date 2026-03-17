/**
 * Configuration for the Secure Project Workspace SPA.
 *
 * Authentication: Azure AD B2B guest accounts (MSAL.js).
 * External users authenticate via the host tenant's Azure AD and are
 * provisioned as B2B guest accounts via the invite endpoint.
 *
 * Values are loaded from environment files:
 *   .env.development — local dev / dev environment
 *   .env.production  — production build
 *
 * See: docs/architecture/external-access-architecture.md
 */

/** BFF API base URL */
export const BFF_API_URL: string =
  (import.meta.env.VITE_BFF_API_URL as string | undefined) ??
  "https://spe-api-dev-67e2xz.azurewebsites.net";

/** Azure AD tenant ID for the Spaarke tenant */
export const MSAL_TENANT_ID: string =
  (import.meta.env.VITE_MSAL_TENANT_ID as string | undefined) ?? "";

/** Azure AD app registration client ID for the external SPA */
export const MSAL_CLIENT_ID: string =
  (import.meta.env.VITE_MSAL_CLIENT_ID as string | undefined) ?? "";

/**
 * BFF API scope for token acquisition.
 * Format: api://{bff-app-registration-id}/user_impersonation
 */
export const BFF_API_SCOPE: string =
  (import.meta.env.VITE_BFF_API_SCOPE as string | undefined) ?? "";

/** App version — update on each release */
export const APP_VERSION = "1.0.0";
