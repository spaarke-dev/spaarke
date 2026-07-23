/**
 * Configuration for the Secure Project Workspace SPA.
 *
 * Authentication: Entra External ID (CIAM) + MSAL authorization code flow + PKCE
 * (ADR-028 Amendment A1). External users are local accounts in a SEPARATE CIAM
 * external tenant (spaarkeextid) — NOT B2B guests in the workforce tenant. The SPA
 * authenticates against the CIAM authority (*.ciamlogin.com) and acquires tokens
 * for the BFF, which validates them via its second "Ciam" JwtBearer scheme (task 020).
 *
 * Values are loaded from environment files:
 *   .env.development — local dev (committed, safe values)
 *   .env.production  — CI/CD token substitution (#{VAR_NAME}# placeholders)
 *
 * IMPORTANT: This SPA runs on Azure Static Web Apps, NOT inside a Dataverse web
 * resource. Xrm context is NOT available, so resolveRuntimeConfig() from
 * @spaarke/auth cannot be used. Environment-specific values are injected via CI/CD
 * token substitution into .env.production before the Vite build runs.
 *
 * No hardcoded dev fallbacks — production builds fail loudly if CI/CD
 * substitution has not replaced the #{...}# tokens.
 *
 * See: docs/architecture/external-access-spa-architecture.md
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
        'Ensure .env.development exists for local dev, or CI/CD token substitution ' +
        'has run against .env.production before the Vite build.'
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
export const BFF_API_URL: string = requireEnvVar('VITE_BFF_API_URL', 'BFF API base URL');

/**
 * MSAL client ID for the SPA app registration in the CIAM external tenant.
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_CLIENT_ID: string = requireEnvVar('VITE_MSAL_CLIENT_ID', 'CIAM SPA client ID');

/**
 * Full MSAL authority URL for the CIAM external tenant
 * (e.g. https://spaarkeextid.ciamlogin.com/{ciam-tenant-id}). Config-driven so the
 * SPA can retarget per environment / a future Legal Front Door without a code change.
 * Its host is also registered as a knownAuthority in msal-config.ts.
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_AUTHORITY: string = requireEnvVar('VITE_MSAL_AUTHORITY', 'CIAM MSAL authority URL');

/**
 * CIAM external tenant ID (spaarkeextid). Retained for diagnostics / config parity;
 * the authority above already encodes it.
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_TENANT_ID: string = requireEnvVar('VITE_MSAL_TENANT_ID', 'CIAM tenant ID');

/**
 * BFF API OAuth scope for token acquisition — the BFF API app registration exposed
 * in the CIAM tenant (api://{ciam-bff-api-app-id}/{scope}); the token's audience is
 * validated by the BFF "Ciam" scheme (task 020).
 * Injected via .env.development or CI/CD token substitution.
 */
export const MSAL_BFF_SCOPE: string = requireEnvVar('VITE_MSAL_BFF_SCOPE', 'CIAM BFF API OAuth scope');

/** App version — update on each release */
export const APP_VERSION = '1.0.0';
