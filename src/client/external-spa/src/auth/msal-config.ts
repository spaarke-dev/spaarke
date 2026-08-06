/**
 * Standalone-MSAL configuration for the Spaarke collaboration hosts.
 *
 * ONE shared standalone-MSAL module with a PLUGGABLE authority (ADR-028 Amendment A2):
 * the same module serves the external SPA (Entra External ID / CIAM authority) and the
 * Teams collaboration host (workforce Entra, multitenant). The authority is selected by
 * config — NOT by forking the module.
 *
 * A1/A2 invariants (preserved for EVERY authority):
 *   - direct `PublicClientApplication` (NOT @spaarke/auth, which is Xrm-bound + MSAL v3)
 *   - per-tab `sessionStorage` cache isolation (multi-tenant guest isolation)
 *   - broker-only: the acquired token authenticates to the BFF; never exchanged downstream
 *
 * The CIAM path is byte-for-byte unchanged (FR-15): the SPA continues to consume the
 * exported `msalInstance` singleton, built against the CIAM authority from config.ts. The
 * workforce-multitenant shape is provided here but NOT wired to a consumer — tasks 011
 * (Teams SSO/NAA strategy) and 012 (host adapter) select it.
 *
 * Authentication model (CIAM / A1): external users are LOCAL accounts in a separate CIAM
 * external tenant (spaarkeextid), NOT B2B guests in the workforce tenant. The SPA
 * authenticates against the CIAM authority (*.ciamlogin.com); the BFF validates the
 * resulting tokens via its "Ciam" scheme. Flow: authorization code + PKCE.
 *
 * See: .claude/adr/ADR-028-spaarke-auth-architecture.md (Amendments A1 + A2)
 * See: docs/architecture/external-access-spa-architecture.md
 */

import { PublicClientApplication, type Configuration } from '@azure/msal-browser';
import { MSAL_CLIENT_ID, MSAL_AUTHORITY, MSAL_BFF_SCOPE } from '../config';

/**
 * Which Entra authority plane a standalone-MSAL instance targets.
 *   - 'ciam'                  → Entra External ID (*.ciamlogin.com); the external SPA (A1)
 *   - 'workforce-multitenant' → workforce Entra (login.microsoftonline.com, multitenant);
 *                               the Teams collaboration host (A2)
 */
export type AuthorityKind = 'ciam' | 'workforce-multitenant';

/** Authority kinds this module knows how to resolve — used for fail-safe validation. */
const KNOWN_AUTHORITY_KINDS: readonly AuthorityKind[] = ['ciam', 'workforce-multitenant'];

/**
 * Well-known workforce (multitenant) authority host. `login.microsoftonline.com` is a
 * DEFAULT MSAL cloud authority, so — unlike the CIAM `*.ciamlogin.com` host — it does NOT
 * need to be declared in `knownAuthorities`.
 */
export const WORKFORCE_AUTHORITY_HOST = 'login.microsoftonline.com';

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
   * `https://login.microsoftonline.com/organizations` (workforce-multitenant).
   */
  authority: string;
  /** BFF API OAuth scope acquired against this authority (broker-only — used only to call the BFF). */
  bffScope: string;
}

/**
 * Build the MSAL `Configuration` for a standalone collaboration-host instance.
 *
 * Fails SAFE (throws) on an unknown/unconfigured authority kind or missing required field —
 * it never silently falls back to the wrong authority (acceptance criterion 5).
 *
 * CIAM authorities (*.ciamlogin.com) are non-default (B2C-style) authorities, so their host
 * MUST be declared in `knownAuthorities` for MSAL to trust the OIDC metadata endpoint. The
 * workforce authority (login.microsoftonline.com) is a default MSAL cloud, so it needs no
 * `knownAuthorities` entry.
 */
export function buildMsalConfiguration(config: StandaloneMsalConfig): Configuration {
  if (!KNOWN_AUTHORITY_KINDS.includes(config.kind)) {
    throw new Error(
      `[CollabAuth] Unknown authority kind '${config.kind as string}'. ` +
        `Expected one of: ${KNOWN_AUTHORITY_KINDS.join(', ')}. ` +
        'Refusing to instantiate MSAL against an unresolved authority.'
    );
  }

  if (!config.clientId || !config.authority || !config.bffScope) {
    throw new Error(
      `[CollabAuth] Incomplete standalone-MSAL config for kind '${config.kind}': ` +
        'clientId, authority and bffScope are all required.'
    );
  }

  const knownAuthorities = config.kind === 'ciam' ? [new URL(config.authority).host] : [];

  return {
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      knownAuthorities,
      /**
       * Redirect URI must match a registered SPA redirect URI on the target app
       * registration (the host origin in production, http://localhost:3000 in local dev).
       */
      redirectUri: window.location.origin,
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
export function createStandaloneMsalInstance(config: StandaloneMsalConfig): PublicClientApplication {
  return new PublicClientApplication(buildMsalConfiguration(config));
}

/**
 * The external SPA's CIAM authority descriptor, sourced from config.ts (Amendment A1).
 * This is the DEFAULT collaboration authority; the SPA entry point (main.tsx) consumes the
 * `msalInstance` singleton built from it, so existing behavior is unchanged (FR-15).
 */
export const ciamAuthorityConfig: StandaloneMsalConfig = {
  kind: 'ciam',
  clientId: MSAL_CLIENT_ID,
  authority: MSAL_AUTHORITY,
  bffScope: MSAL_BFF_SCOPE,
};

/**
 * Build the workforce-multitenant authority descriptor for the Teams collaboration host
 * (Amendment A2). NOT wired to a consumer here — tasks 011 (Teams SSO/NAA strategy) and 012
 * (host adapter) select it and supply the workforce app-registration values (the SPA's
 * config.ts intentionally carries only CIAM values). Authority defaults to the multitenant
 * `/organizations` endpoint (work/school accounts; no personal MSAs).
 */
export function workforceAuthorityConfig(params: {
  clientId: string;
  bffScope: string;
  /** Override the default multitenant authority (e.g. a tenant-pinned authority). */
  authority?: string;
}): StandaloneMsalConfig {
  return {
    kind: 'workforce-multitenant',
    clientId: params.clientId,
    bffScope: params.bffScope,
    authority: params.authority ?? `https://${WORKFORCE_AUTHORITY_HOST}/organizations`,
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
