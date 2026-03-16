/**
 * Configuration for the Secure Project Workspace SPA.
 *
 * In production (Power Pages): values are injected via window globals
 * by the Power Pages site settings or derived from known constants.
 *
 * In local development: values are loaded from .env.local via Vite's
 * import.meta.env mechanism.
 *
 * See: docs/architecture/power-pages-spa-guide.md
 */

/** BFF API base URL — reads from env in dev, window global in production */
export const BFF_API_URL: string =
  (import.meta.env.VITE_BFF_API_URL as string | undefined) ??
  "https://spe-api-dev-67e2xz.azurewebsites.net";

/**
 * Portal client ID for the OAuth implicit grant flow.
 * Must match the `ImplicitGrantFlow/RegisteredClientId` Power Pages site setting.
 */
export const PORTAL_CLIENT_ID: string =
  (import.meta.env.VITE_PORTAL_CLIENT_ID as string | undefined) ?? "";

/** App version — update on each release */
export const APP_VERSION = "1.0.0";
