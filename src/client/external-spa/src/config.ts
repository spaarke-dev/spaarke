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
// Workforce plane — the single-tenant Spaarke client (ADR-028 A7; task 040, FR-16)
// ---------------------------------------------------------------------------

/**
 * MSAL client id, Spaarke tenant id and BFF scope for the WORKFORCE plane (ADR-028 Amendment A7, which
 * supersedes A2's multitenant client for Model 1). ONE single-tenant public client registered in
 * Spaarke's tenant — not the BFF app registration — serves the workforce plane in BOTH hosts: the Teams
 * tab (NAA, then an MSAL popup) and the standalone browser "My organization" choice (redirect flow).
 *
 * - The authority is pinned to `https://login.microsoftonline.com/{tenantId}`. Only a Spaarke-tenant
 *   authority issues a guest's token with the guest `oid` its `systemuser` row is keyed by;
 *   `/organizations` or `/common` signs a guest into their home tenant (A7 MUST NOT).
 * - The scope is `api://{customer BFF app id}/user_impersonation`; A2's separate Teams scope is retired (A7). It
 *   comes from Spaarke-owned configuration (this env file, until the backend-selection seam of task 041
 *   supplies it at run time), never from a link or query string (FR-17).
 *
 * Read LAZILY (not via `requireEnvVar` at module load like the CIAM constants above) because these are
 * OPTIONAL for a CIAM-only build/deploy. Callers invoke this only after determining the caller is on
 * the workforce plane (Teams host confirmed, or the browser realm chooser picked "My organization"); a
 * missing, placeholder or non-conforming value at that point is a fail-loud error, not a build throw.
 *
 * The env keys keep their `VITE_TEAMS_MSAL_*` names (already wired in CI and `.env.production`) although
 * the browser workforce plane reads them too.
 */
export interface WorkforceEnvConfig {
  /** The single-tenant Spaarke workforce public client id (A7). */
  clientId: string;
  /** Spaarke's Entra tenant id (a GUID) — the only workforce authority (A7). */
  tenantId: string;
  /** The BFF API scope: `api://{customer BFF app id}/user_impersonation`. */
  bffScope: string;
}

/** @deprecated Prefer {@link WorkforceEnvConfig}; retained so existing Teams-host imports keep resolving. */
export type TeamsWorkforceEnvConfig = WorkforceEnvConfig;

function isUnsetOrPlaceholder(value: string | undefined): boolean {
  return !value || /^#\{.+\}#$/.test(value);
}

/** A tenant id is a GUID. `common`, `organizations`, `consumers` and domain names are refused (A7). */
const TENANT_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** True when `value` is a tenant GUID — never a multi-tenant alias such as `organizations` or `common`. */
export function isTenantId(value: string | undefined | null): value is string {
  return !!value && TENANT_ID_PATTERN.test(value);
}

/** The only workforce BFF scope shape A7 allows: `api://{app id}/user_impersonation`. */
const USER_IMPERSONATION_SCOPE_PATTERN = /^api:\/\/[^/\s]+\/user_impersonation$/;

/** True when `scope` is `api://{app id}/user_impersonation` (A7: the only workforce scope). */
export function isUserImpersonationScope(scope: string | undefined | null): scope is string {
  return !!scope && USER_IMPERSONATION_SCOPE_PATTERN.test(scope);
}

/**
 * Read + validate the workforce plane env values. Throws a clear, fail-loud error if any is missing,
 * still an un-substituted CI/CD placeholder, a non-GUID tenant (e.g. `organizations`), or a scope other
 * than `…/user_impersonation`. Callers MUST only invoke this after determining the caller is actually on
 * the workforce plane (Teams host confirmed via `detectTeamsHost()`, or the browser realm chooser picked
 * "My organization").
 */
export function getWorkforceEnvConfig(): WorkforceEnvConfig {
  const clientId = import.meta.env.VITE_TEAMS_MSAL_CLIENT_ID as string | undefined;
  const tenantId = import.meta.env.VITE_TEAMS_MSAL_TENANT_ID as string | undefined;
  const bffScope = import.meta.env.VITE_TEAMS_MSAL_BFF_SCOPE as string | undefined;

  if (isUnsetOrPlaceholder(clientId) || isUnsetOrPlaceholder(tenantId) || isUnsetOrPlaceholder(bffScope)) {
    throw new Error(
      '[ExternalSPA] Missing or unsubstituted workforce environment variables ' +
        '(VITE_TEAMS_MSAL_CLIENT_ID / VITE_TEAMS_MSAL_TENANT_ID / VITE_TEAMS_MSAL_BFF_SCOPE). Required only ' +
        'when running on the workforce plane (inside Teams, or the browser "My organization" realm) — see .env.example.'
    );
  }

  if (!isTenantId(tenantId)) {
    throw new Error(
      `[ExternalSPA] VITE_TEAMS_MSAL_TENANT_ID must be Spaarke's tenant id (a GUID), not '${tenantId}'. ` +
        'The workforce authority is tenant-pinned; organizations/common are refused (ADR-028 A7).'
    );
  }

  if (!isUserImpersonationScope(bffScope)) {
    throw new Error(
      `[ExternalSPA] VITE_TEAMS_MSAL_BFF_SCOPE must be api://{customer BFF app id}/user_impersonation, not '${bffScope}' ` +
        '(ADR-028 A7).'
    );
  }

  return { clientId: clientId as string, tenantId, bffScope };
}

/**
 * Back-compat alias for the Teams host adapter (task 012). Delegates to {@link getWorkforceEnvConfig}
 * — the Teams tab and the browser workforce realm read the SAME single-tenant client values.
 */
export function getTeamsWorkforceEnvConfig(): WorkforceEnvConfig {
  return getWorkforceEnvConfig();
}
