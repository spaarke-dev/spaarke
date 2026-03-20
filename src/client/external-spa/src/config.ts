/**
 * Configuration for the Secure Project Workspace SPA.
 *
 * Authentication: Entra B2B + MSAL authorization code flow + PKCE.
 * External users are B2B guest accounts in the main Spaarke workforce tenant.
 * They authenticate with their existing Microsoft 365 credentials (SSO).
 *
 * Values are loaded from environment files:
 *   .env.development — local dev (committed, safe values)
 *   .env.production  — CI/CD token substitution (#{VAR_NAME}# placeholders)
 *
 * IMPORTANT: This SPA runs on Power Pages, NOT inside a Dataverse web resource.
 * Xrm context is NOT available, so resolveRuntimeConfig() from @spaarke/auth
 * cannot be used. Environment-specific values are injected via CI/CD token
 * substitution into .env.production before the Vite build runs.
 *
 * No hardcoded dev fallbacks — production builds fail loudly if CI/CD
 * substitution has not replaced the #{...}# tokens.
 *
 * See: docs/architecture/power-pages-spa-guide.md
 * See: notes/auth-migration-b2b-msal.md
 */

// ---------------------------------------------------------------------------
// Environment variable helpers
// ---------------------------------------------------------------------------

/**
 * Read a required Vite environment variable. Throws if missing or still
 * contains an un-substituted CI/CD token placeholder (#{...}#).
 */
function requireEnvVar(key: string, label: string): string {
  const value = import.meta.env[key] as string | undefined;

  if (!value) {
    throw new Error(
      `[ExternalSPA] Missing required environment variable '${key}' (${label}). ` +
        "Ensure .env.development exists for local dev, or CI/CD token substitution " +
        "has run against .env.production before the Vite build."
    );
  }

  // Detect un-substituted CI/CD token placeholders like #{BFF_API_URL}#
  if (/^#\{.+\}#$/.test(value)) {
    throw new Error(
      `[ExternalSPA] Environment variable '${key}' (${label}) still contains ` +
        `CI/CD placeholder '${value}'. Token substitution must run before build.`
    );
  }

  return value;
}

// ---------------------------------------------------------------------------
// Exported configuration constants
// ---------------------------------------------------------------------------

/** BFF API base URL — injected via .env.development or CI/CD token substitution. */
export const BFF_API_URL: string = requireEnvVar(
  "VITE_BFF_API_URL",
  "BFF API base URL"
);

/**
 * MSAL client ID for the SPA app registration (spaarke-external-access-SPA).
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_CLIENT_ID: string = requireEnvVar(
  "VITE_MSAL_CLIENT_ID",
  "MSAL SPA client ID"
);

/**
 * Entra tenant ID (main Spaarke workforce tenant).
 * External users are B2B guests in this tenant — SSO with Microsoft 365 credentials.
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_TENANT_ID: string = requireEnvVar(
  "VITE_MSAL_TENANT_ID",
  "Entra tenant ID"
);

/**
 * BFF API OAuth scope for token acquisition.
 * Defined on SDAP-BFF-SPE-API app registration.
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_BFF_SCOPE: string = requireEnvVar(
  "VITE_MSAL_BFF_SCOPE",
  "BFF API OAuth scope"
);

/** App version — update on each release */
export const APP_VERSION = "1.0.0";
