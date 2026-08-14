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

// ---------------------------------------------------------------------------
// Workforce plane — multitenant app-registration values (task 012 Teams host + task 013 browser)
// ---------------------------------------------------------------------------

/**
 * MSAL client id + BFF scope for the WORKFORCE plane (ADR-028 Amendments A2 + A3). ONE multitenant
 * app registration (reused `1e40baad-…`, task 061) serves the workforce plane in BOTH hosts — the
 * Teams collaboration tab (silent SSO / NAA, task 012) AND the standalone browser "My organization"
 * home-realm choice (redirect flow, task 013). The values are identical (same authority, same aud);
 * only the acquisition transport differs, so both paths share these env vars.
 *
 * Read LAZILY (not via `requireEnvVar` at module load like the CIAM constants above) because these
 * are OPTIONAL for a CIAM-only build/deploy — a standalone SPA build must still succeed even before
 * CI/CD wires workforce substitution (task 071). Callers invoke this only after determining the
 * caller is on the workforce plane (Teams host confirmed, or the browser realm chooser picked "My
 * organization"); a missing/placeholder value at that point is a fail-loud error, not a build throw.
 */
export interface WorkforceEnvConfig {
  /** Workforce (multitenant) SPA app-registration client id — reused `1e40baad-…` app (task 061). */
  clientId: string;
  /** BFF API OAuth scope for the workforce plane, e.g. `api://{app-id}/access_as_user`. */
  bffScope: string;
}

/** @deprecated Prefer {@link WorkforceEnvConfig}; retained so existing Teams-host imports keep resolving. */
export type TeamsWorkforceEnvConfig = WorkforceEnvConfig;

function isUnsetOrPlaceholder(value: string | undefined): boolean {
  return !value || /^#\{.+\}#$/.test(value);
}

/**
 * Read + validate the workforce plane env values. Throws a clear, fail-loud error if missing or
 * still an un-substituted CI/CD placeholder — callers MUST only invoke this after determining the
 * caller is actually on the workforce plane (Teams host confirmed via `detectTeamsHost()`, or the
 * browser realm chooser picked "My organization").
 */
export function getWorkforceEnvConfig(): WorkforceEnvConfig {
  const clientId = import.meta.env.VITE_TEAMS_MSAL_CLIENT_ID;
  const bffScope = import.meta.env.VITE_TEAMS_MSAL_BFF_SCOPE;

  if (isUnsetOrPlaceholder(clientId) || isUnsetOrPlaceholder(bffScope)) {
    throw new Error(
      '[ExternalSPA] Missing or unsubstituted workforce environment variables ' +
        '(VITE_TEAMS_MSAL_CLIENT_ID / VITE_TEAMS_MSAL_BFF_SCOPE). Required only when running on the ' +
        'workforce plane (inside Teams, or the browser "My organization" realm) — see .env.example.'
    );
  }

  return { clientId: clientId as string, bffScope: bffScope as string };
}

/**
 * Back-compat alias for the Teams host adapter (task 012). Delegates to {@link getWorkforceEnvConfig}
 * — the Teams tab and the browser workforce realm read the SAME multitenant app-registration values.
 */
export function getTeamsWorkforceEnvConfig(): WorkforceEnvConfig {
  return getWorkforceEnvConfig();
}
