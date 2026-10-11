/**
 * Standalone-MSAL configuration for the Spaarke collaboration hosts.
 *
 * ONE shared standalone-MSAL module with a PLUGGABLE authority (ADR-028 Amendment A2):
 * the same module serves the external SPA (Entra External ID / CIAM authority) and the
 * workforce plane (browser "My organization" and the Teams tab). The authority is selected by
 * config — NOT by forking the module.
 *
 * Workforce plane (ADR-028 Amendment A7, task 040 / FR-16): ONE single-tenant Spaarke public client
 * in Spaarke's tenant, authority pinned to `https://login.microsoftonline.com/{Spaarke tenant id}`.
 * `/organizations`, `/common` and `/consumers` are refused here (fail-safe), as is any workforce BFF
 * scope other than `api://{customer BFF app id}/user_impersonation`.
 *
 * A1/A2 invariants (preserved for EVERY authority):
 *   - direct `PublicClientApplication` (NOT @spaarke/auth, which is Xrm-bound + MSAL v3)
 *   - per-tab `sessionStorage` cache isolation (multi-tenant guest isolation)
 *   - broker-only: the acquired token authenticates to the BFF; never exchanged downstream
 *
 * The CIAM path is byte-for-byte unchanged (FR-15): the SPA continues to consume the
 * exported `msalInstance` singleton, built against the CIAM authority from config.ts. The
 * workforce shape is selected by standalone-plane.ts (browser) and msal-auth.ts (Teams).
 *
 * Authentication model (CIAM / A1): external users are LOCAL accounts in a separate CIAM
 * external tenant (spaarkeextid), NOT B2B guests in the workforce tenant. The SPA
 * authenticates against the CIAM authority (*.ciamlogin.com); the BFF validates the
 * resulting tokens via its "Ciam" scheme. Flow: authorization code + PKCE.
 *
 * See: .claude/adr/ADR-028-spaarke-auth-architecture.md (Amendments A1, A2 and A7)
 * See: docs/architecture/external-access-spa-architecture.md
 */

import { PublicClientApplication, type Configuration } from '@azure/msal-browser';
import { MSAL_CLIENT_ID, MSAL_AUTHORITY, isTenantId, isUserImpersonationScope, readOptionalEnv } from '../config';

/**
 * Which Entra authority plane a standalone-MSAL instance targets.
 *   - 'ciam'      → Entra External ID (*.ciamlogin.com); the external SPA's partner plane (A1)
 *   - 'workforce' → workforce Entra (login.microsoftonline.com) pinned to Spaarke's tenant; the
 *                   browser "My organization" plane and the Teams tab (A7 — never multitenant)
 */
export type AuthorityKind = 'ciam' | 'workforce';

/** Authority kinds this module knows how to resolve — used for fail-safe validation. */
const KNOWN_AUTHORITY_KINDS: readonly AuthorityKind[] = ['ciam', 'workforce'];

/**
 * Well-known workforce authority host. `login.microsoftonline.com` is a DEFAULT MSAL cloud
 * authority, so — unlike the CIAM `*.ciamlogin.com` host — it does NOT need to be declared in
 * `knownAuthorities`.
 */
export const WORKFORCE_AUTHORITY_HOST = 'login.microsoftonline.com';

/**
 * Path of the static MSAL popup redirect page (`public/auth-callback.html`). msal-browser v5 completes
 * a popup (or a silent iframe) only when the redirect page runs its redirect bridge
 * (`broadcastResponseToMainFrame`); that page does, the SPA's own index page does not.
 */
export const POPUP_REDIRECT_PATH = '/auth-callback.html';

/** The popup redirect URI for the current origin: `{origin}/auth-callback.html`. */
export function popupRedirectUri(): string {
  return `${window.location.origin}${POPUP_REDIRECT_PATH}`;
}

/** The tenant-pinned workforce authority for a tenant id (A7). Throws on anything but a tenant GUID. */
export function workforceAuthority(tenantId: string): string {
  if (!isTenantId(tenantId)) {
    throw new Error(
      `[CollabAuth] The workforce authority must be pinned to Spaarke's tenant id (a GUID), not '${tenantId}'. ` +
        'organizations / common / consumers are refused (ADR-028 A7).'
    );
  }
  return `https://${WORKFORCE_AUTHORITY_HOST}/${tenantId}`;
}

/**
 * Fail-safe check of a workforce authority: `https://login.microsoftonline.com/{tenant GUID}` and nothing
 * else. Refuses `/organizations`, `/common`, `/consumers`, other hosts and extra path segments.
 */
function assertTenantPinnedWorkforceAuthority(authority: string): void {
  let url: URL | null = null;
  try {
    url = new URL(authority);
  } catch {
    url = null;
  }
  const segments = url ? url.pathname.split('/').filter(Boolean) : [];
  const pinned =
    url !== null &&
    url.protocol === 'https:' &&
    url.host === WORKFORCE_AUTHORITY_HOST &&
    segments.length === 1 &&
    isTenantId(segments[0]);
  if (!pinned) {
    throw new Error(
      `[CollabAuth] Workforce authority '${authority}' is not tenant-pinned. Expected ` +
        `https://${WORKFORCE_AUTHORITY_HOST}/{Spaarke tenant id}; organizations / common are refused (ADR-028 A7).`
    );
  }
}

/**
 * Config-driven descriptor the module resolves at instantiation time. Choosing a different
 * `kind` selects a different authority WITHOUT forking the module.
 */
export interface StandaloneMsalConfig {
  /** Which authority plane to target. */
  kind: AuthorityKind;
  /** SPA app-registration client id for the target authority. */
  clientId: string;
  /**
   * Full authority URL, e.g. `https://spaarkeextid.ciamlogin.com/{tid}` (CIAM) or
   * `https://login.microsoftonline.com/{Spaarke tenant id}` (workforce, A7).
   */
  authority: string;
  /**
   * BFF API OAuth scope acquired against this authority (broker-only — used only to call the BFF). Required and
   * checked for the workforce kind. Since task 041 the scope actually requested comes from the active backend
   * (`backend-selection.ts`); for CIAM this field is informational and may be empty (the MSAL instance does not
   * depend on it).
   */
  bffScope: string;
}

/** Per-instance overrides for {@link buildMsalConfiguration}. */
export interface MsalConfigurationOptions {
  /**
   * Redirect URI for this instance. Defaults to the origin (the browser redirect flow returns to the
   * SPA). The Teams instances pass {@link popupRedirectUri} so a popup or silent iframe lands on the
   * redirect-bridge page.
   */
  redirectUri?: string;
}

/**
 * Build the MSAL `Configuration` for a standalone collaboration-host instance.
 *
 * Fails SAFE (throws) on an unknown/unconfigured authority kind or missing required field —
 * it never silently falls back to the wrong authority (acceptance criterion 5). A workforce
 * config must also carry a tenant-pinned authority and a `/user_impersonation` scope (A7).
 *
 * CIAM authorities (*.ciamlogin.com) are non-default (B2C-style) authorities, so their host
 * MUST be declared in `knownAuthorities` for MSAL to trust the OIDC metadata endpoint. The
 * workforce authority (login.microsoftonline.com) is a default MSAL cloud, so it needs no
 * `knownAuthorities` entry.
 */
export function buildMsalConfiguration(
  config: StandaloneMsalConfig,
  options: MsalConfigurationOptions = {}
): Configuration {
  if (!KNOWN_AUTHORITY_KINDS.includes(config.kind)) {
    throw new Error(
      `[CollabAuth] Unknown authority kind '${config.kind as string}'. ` +
        `Expected one of: ${KNOWN_AUTHORITY_KINDS.join(', ')}. ` +
        'Refusing to instantiate MSAL against an unresolved authority.'
    );
  }

  if (!config.clientId || !config.authority || (config.kind === 'workforce' && !config.bffScope)) {
    throw new Error(
      `[CollabAuth] Incomplete standalone-MSAL config for kind '${config.kind}': ` +
        'clientId and authority are required (and bffScope for the workforce kind).'
    );
  }

  if (config.kind === 'workforce') {
    assertTenantPinnedWorkforceAuthority(config.authority);
    if (!isUserImpersonationScope(config.bffScope)) {
      throw new Error(
        `[CollabAuth] Workforce BFF scope '${config.bffScope}' is refused: only ` +
          'api://{customer BFF app id}/user_impersonation is allowed (ADR-028 A7).'
      );
    }
  }

  const knownAuthorities = config.kind === 'ciam' ? [new URL(config.authority).host] : [];

  return {
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      knownAuthorities,
      /**
       * Redirect URI must match a registered SPA redirect URI on the target app
       * registration (the host origin in production, http://localhost:3000 in local dev;
       * `{origin}/auth-callback.html` for the Teams instances).
       */
      redirectUri: options.redirectUri ?? window.location.origin,
      postLogoutRedirectUri: window.location.origin,
    },
    cache: {
      /**
       * sessionStorage: tokens survive page refresh but not a new tab/window — per-tab
       * isolation. This is the documented ADR-028 A1/A2 exception for the collaboration
       * hosts (multi-tenant guest isolation); do NOT switch to localStorage or migrate to
       * @spaarke/auth.
       *
       * Note: msal-browser v5 removed `storeAuthStateInCookie` from CacheOptions (it is now
       * a per-request option and defaults to off), so it is no longer set here.
       */
      cacheLocation: 'sessionStorage',
    },
  };
}

/** Instantiate a standalone `PublicClientApplication` for the given authority config. */
export function createStandaloneMsalInstance(
  config: StandaloneMsalConfig,
  options: MsalConfigurationOptions = {}
): PublicClientApplication {
  return new PublicClientApplication(buildMsalConfiguration(config, options));
}

/**
 * The external SPA's CIAM authority descriptor, sourced from config.ts (Amendment A1).
 * This is the DEFAULT collaboration authority; the SPA entry point (main.tsx) consumes the
 * `msalInstance` singleton built from it, so existing behavior is unchanged (FR-15).
 *
 * `bffScope` is informational (task 041): the CIAM scope a request asks for comes from the active backend
 * (`backend-selection.ts`, `api://{customer BFF app id}/user_impersonation`). It mirrors the dev fallback's
 * `VITE_MSAL_BFF_SCOPE` and is empty when that is unset (a `VITE_BACKENDS` build).
 */
export const ciamAuthorityConfig: StandaloneMsalConfig = {
  kind: 'ciam',
  clientId: MSAL_CLIENT_ID,
  authority: MSAL_AUTHORITY,
  bffScope: readOptionalEnv('VITE_MSAL_BFF_SCOPE') ?? '',
};

/**
 * Build the workforce authority descriptor (ADR-028 Amendment A7, superseding A2's multitenant
 * default): the single-tenant Spaarke client with authority
 * `https://login.microsoftonline.com/{tenantId}`. Used by the browser "My organization" plane
 * (standalone-plane.ts) and the Teams tab (msal-auth.ts), which read the values from config.ts
 * `getWorkforceEnvConfig()`. There is no authority override and no multitenant default: a non-GUID
 * tenant (e.g. `organizations`) throws.
 */
export function workforceAuthorityConfig(params: {
  clientId: string;
  /** Spaarke's tenant id (a GUID). */
  tenantId: string;
  /** `api://{customer BFF app id}/user_impersonation`. */
  bffScope: string;
}): StandaloneMsalConfig {
  return {
    kind: 'workforce',
    clientId: params.clientId,
    bffScope: params.bffScope,
    authority: workforceAuthority(params.tenantId),
  };
}

/**
 * Singleton MSAL instance for the external SPA (CIAM authority — the default).
 * Must be passed to MsalProvider in main.tsx.
 * Call `msalInstance.initialize()` before rendering (see main.tsx).
 *
 * Behaviorally identical to the pre-A2 singleton: same CIAM authority, same knownAuthorities
 * (the CIAM host), same sessionStorage cache. FR-15 (no CIAM regression) holds by construction.
 */
export const msalInstance = createStandaloneMsalInstance(ciamAuthorityConfig);
