/**
 * createCodePageAuthInitializer.ts
 *
 * Factory that produces the canonical {@link CodePageAuthInitializer} triple
 * (`ensureAuthInitialized`, `authenticatedFetch`, `getTenantId`) consumed by
 * Spaarke Code Pages. Replaces the 3 byte-similar solution-local `authInit.ts`
 * copies (DailyBriefing, LegalWorkspace, SpaarkeAi) that drifted into separate
 * codebases. Per FR-20 + ADR-028, `@spaarke/auth` is the canonical entry point;
 * this factory is the canonical *consumption* pattern.
 *
 * Behavior preserved (union of the 3 solution-local copies as of 2026-06-18):
 *   - once-only `_initPromise` (safe to call N times; reuses same promise).
 *   - On failure: `console.warn`, null out `_initPromise` (allow retry), re-throw.
 *   - On success: `console.info` with the supplied `logLabel`.
 *   - Optional `tenantId` (omit for Xrm fallback — DailyBriefing case).
 *   - Configurable `proactiveRefresh` (default `true`; `false` for short-lived dialogs).
 *   - Optional `beforeInit` async hook (e.g. `await waitForConfig()` — DailyBriefing case).
 *
 * @see projects/spaarke-daily-update-service-r2/notes/auth-init-divergence.md
 * @see ADR-028 - Spaarke Auth Architecture
 * @see ADR-010 - DI Minimalism
 */

import { initAuth, getAuthProvider } from './initAuth';
import { authenticatedFetch as sharedAuthFetch } from './authenticatedFetch';

/**
 * Configuration accepted by {@link createCodePageAuthInitializer}.
 *
 * The shape captures the union of behavior across the 3 solution-local
 * `authInit.ts` copies. Optional fields have safe defaults that preserve
 * each solution's exact pre-consolidation call sequence.
 */
export interface CodePageAuthInitConfig {
  /** Azure AD client ID (from runtime config). */
  clientId: string;
  /** BFF API base URL (from runtime config). */
  bffBaseUrl: string;
  /** BFF API scope (from runtime config). */
  bffApiScope: string;
  /**
   * Azure AD tenant GUID (from runtime config).
   *
   * When omitted, `@spaarke/auth` falls back to `resolveDefaultAuthority()`
   * (tries `Xrm.organizationSettings.tenantId` then `/organizations`). Pass
   * explicitly for tenant-specific authority — required to avoid the
   * "Pick an account" popup in iframes / multi-account browsers
   * (INV-3 per `spaarke-sso-binding.md`).
   */
  tenantId?: string;
  /**
   * If true, start a proactive token refresh interval. Default `true`.
   * Set `false` for short-lived dialogs where the proactive interval would
   * outlive the dialog (e.g. DailyBriefing standalone code page).
   */
  proactiveRefresh?: boolean;
  /**
   * Label included in `console.info` / `console.warn` lines. Surfaces caller
   * identity in production logs. Required (no default) so each consumer's
   * log lines remain attributable post-consolidation.
   *
   * Examples: `'DailyBriefing'`, `'authInit'` (LegalWorkspace), `'SpaarkeAi:authInit'`.
   */
  logLabel: string;
  /**
   * Optional async hook invoked BEFORE `initAuth(...)`. Use this when the
   * solution must defer init until a runtime config promise resolves (e.g.
   * DailyBriefing's `await waitForConfig()`). If omitted, init proceeds
   * immediately.
   */
  beforeInit?: () => Promise<void>;
}

/**
 * The triple returned by {@link createCodePageAuthInitializer}. Mirrors the
 * exports that every solution-local `authInit.ts` previously surfaced.
 */
export interface CodePageAuthInitializer {
  /**
   * Initialize the `@spaarke/auth` provider. Safe to call multiple times —
   * returns the same promise. On failure, the promise is null'd out so the
   * next call retries.
   */
  ensureAuthInitialized: () => Promise<void>;
  /**
   * Performs a fetch with BFF Bearer-token authentication. Ensures auth is
   * initialized before delegating to the shared `authenticatedFetch`.
   */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * Resolves the Azure AD tenant ID from the MSAL account / Xrm context.
   * Ensures auth is initialized before delegating to the provider.
   */
  getTenantId: () => Promise<string>;
}

/**
 * Build a Code Page auth initializer for the supplied {@link CodePageAuthInitConfig}.
 *
 * Each call returns a fresh closure with its own `_initPromise`. Callers
 * should construct ONE initializer per Code Page at module load and reuse
 * its exports — matching the singleton pattern used by every pre-consolidation
 * solution-local `authInit.ts`.
 *
 * @example
 * ```ts
 * // src/solutions/LegalWorkspace/src/services/authInit.ts (post-migration)
 * import { createCodePageAuthInitializer } from '@spaarke/auth';
 * import { getBffBaseUrl, getBffOAuthScope, getMsalClientId, getTenantId } from '../config/runtimeConfig';
 *
 * export const { ensureAuthInitialized, authenticatedFetch, getTenantId } =
 *   createCodePageAuthInitializer({
 *     clientId: getMsalClientId(),
 *     tenantId: getTenantId(),
 *     bffBaseUrl: getBffBaseUrl(),
 *     bffApiScope: getBffOAuthScope(),
 *     proactiveRefresh: true,
 *     logLabel: 'authInit',
 *   });
 * ```
 *
 * @example
 * ```ts
 * // src/solutions/DailyBriefing/src/services/authInit.ts (post-migration)
 * import { createCodePageAuthInitializer } from '@spaarke/auth';
 * import { getBffBaseUrl, getBffOAuthScope, getMsalClientId, waitForConfig } from '../config/runtimeConfig';
 *
 * export const { ensureAuthInitialized, authenticatedFetch, getTenantId } =
 *   createCodePageAuthInitializer({
 *     clientId: getMsalClientId(),
 *     bffBaseUrl: getBffBaseUrl(),
 *     bffApiScope: getBffOAuthScope(),
 *     proactiveRefresh: false, // short-lived dialog
 *     logLabel: 'DailyBriefing',
 *     beforeInit: waitForConfig,
 *   });
 * ```
 */
export function createCodePageAuthInitializer(
  config: CodePageAuthInitConfig,
): CodePageAuthInitializer {
  const {
    clientId,
    bffBaseUrl,
    bffApiScope,
    tenantId,
    proactiveRefresh = true,
    logLabel,
    beforeInit,
  } = config;

  let _initPromise: Promise<void> | null = null;

  function ensureAuthInitialized(): Promise<void> {
    if (!_initPromise) {
      _initPromise = (async () => {
        try {
          if (beforeInit) {
            await beforeInit();
          }
          await initAuth({
            clientId,
            bffBaseUrl,
            bffApiScope,
            // Only forward tenantId when provided — preserves DailyBriefing's
            // "omit and let resolveDefaultAuthority fall back to Xrm" behavior.
            ...(tenantId ? { tenantId } : {}),
            proactiveRefresh,
          });
          console.info(`[${logLabel}] @spaarke/auth initialized successfully`);
        } catch (err) {
          console.warn(`[${logLabel}] @spaarke/auth initialization failed`, err);
          _initPromise = null; // Allow retry
          throw err;
        }
      })();
    }
    return _initPromise;
  }

  async function authenticatedFetch(
    url: string,
    init?: RequestInit,
  ): Promise<Response> {
    await ensureAuthInitialized();
    return sharedAuthFetch(url, init);
  }

  async function getTenantId(): Promise<string> {
    await ensureAuthInitialized();
    return getAuthProvider().getTenantId();
  }

  return { ensureAuthInitialized, authenticatedFetch, getTenantId };
}
