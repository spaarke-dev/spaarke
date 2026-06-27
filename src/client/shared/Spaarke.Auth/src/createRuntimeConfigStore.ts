/**
 * createRuntimeConfigStore — Singleton runtime-config store factory for Code Pages.
 *
 * Consolidates the four byte-similar solution-local `config/runtimeConfig.ts`
 * implementations (DailyBriefing, LegalWorkspace, SpaarkeAi, Reporting) into a
 * single canonical factory. Each solution instantiates this factory once,
 * configured with knobs for the divergences captured in
 * `projects/spaarke-daily-update-service-r2/notes/runtime-config-divergence.md`.
 *
 * Pattern precedent: `createCodePageAuthInitializer` (R4 task 052) — same
 * factory-with-config-knobs shape that consolidates byte-similar copies while
 * preserving every divergence as an opt-in knob.
 *
 * Usage (per-solution `config/runtimeConfig.ts`):
 *
 * ```ts
 * import { createRuntimeConfigStore } from "@spaarke/auth";
 * const store = createRuntimeConfigStore({
 *   errorLabel: "LegalWorkspace",
 *   lazyTenantResolveWithTelemetry: true,
 *   onLazyTenantResolve: (payload) => trackEvent("TenantIdLazyResolve", payload),
 * });
 * export const {
 *   setRuntimeConfig,
 *   waitForConfig,
 *   getBffBaseUrl,
 *   getBffOAuthScope,
 *   getMsalClientId,
 *   getTenantId,
 * } = store;
 * ```
 *
 * @see resolveRuntimeConfig — the async resolver that produces an `IRuntimeConfig`.
 * @see createCodePageAuthInitializer — sibling factory (auth init).
 *
 * @module createRuntimeConfigStore
 */

import type { IRuntimeConfig } from './resolveRuntimeConfig';
import { resolveTenantIdSync } from './resolveTenantIdSync';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Payload reported by `onLazyTenantResolve` when telemetry mode is enabled.
 *
 * Designed as a `Record<string, string>` (intersection of named fields + open
 * index signature) so callers wiring this to `Application Insights`-style
 * `trackEvent(name, Record<string, string>)` APIs can pass it directly without
 * a cast.
 */
export type RuntimeConfigLazyTenantResolvePayload = {
  /** Whether resolveTenantIdSync() produced a non-empty tenant ID. */
  resolved: string;
  /** Always "true" — the stored value was empty (else we wouldn't lazy-resolve). */
  storedWasEmpty: string;
  /** Best-effort caller stack frame for debugging. */
  caller: string;
} & Record<string, string>;

/** Configuration knobs that capture per-solution divergences. */
export interface RuntimeConfigStoreOptions {
  /**
   * REQUIRED. Caller identity, surfaced in the init-not-ready error message.
   * Examples: "DailyBriefing", "LegalWorkspace", "SpaarkeAi", "Reporting".
   */
  errorLabel: string;

  /**
   * Optional. If true, `setRuntimeConfig` resolves a `waitForConfig()` promise.
   * Consumers awaiting `waitForConfig()` unblock immediately on first
   * `setRuntimeConfig` call. DailyBriefing's auth bootstrap uses this as the
   * `beforeInit` hook for `createCodePageAuthInitializer`.
   *
   * Default: `false`. When `false`, `waitForConfig()` resolves immediately
   * (still safe for callers awaiting it).
   */
  enableWaitForConfig?: boolean;

  /**
   * Optional. If true, `getTenantId()` falls back to `resolveTenantIdSync()`
   * from `@spaarke/auth` when the stored tenantId is empty, caches the result,
   * and emits an `onLazyTenantResolve` payload (if `onLazyTenantResolve` is set).
   *
   * Default: `false`. When `false`, `getTenantId()` returns the stored value
   * (or empty string) without fallback.
   *
   * LegalWorkspace sets this to `true` — its `DocumentCard.tsx` needs tenant
   * ID for URL construction, and tenant ID is occasionally unavailable at
   * bootstrap (intermittent Xrm timing issue).
   */
  lazyTenantResolveWithTelemetry?: boolean;

  /**
   * Optional. Callback fired when `getTenantId()` lazy-resolves a tenant ID
   * via `resolveTenantIdSync()`. Only invoked when
   * `lazyTenantResolveWithTelemetry` is `true`.
   *
   * LegalWorkspace wires this to its `trackEvent("TenantIdLazyResolve", ...)`
   * to keep `@spaarke/auth` free of solution-specific telemetry imports.
   */
  onLazyTenantResolve?: (payload: RuntimeConfigLazyTenantResolvePayload) => void;
}

/** Public store surface returned by the factory. */
export interface RuntimeConfigStore {
  /**
   * Store the resolved runtime config. Called once from `main.tsx` bootstrap
   * after `resolveRuntimeConfig()` resolves. Also sets window globals so that
   * `@spaarke/auth`'s `resolveConfig()` can find them.
   */
  setRuntimeConfig(config: IRuntimeConfig): void;

  /**
   * Resolves once `setRuntimeConfig()` has been called. If
   * `enableWaitForConfig` was false at factory construction, resolves
   * immediately on the first call.
   */
  waitForConfig(): Promise<void>;

  /**
   * BFF API base URL resolved from Dataverse Environment Variables at runtime.
   * HOST ONLY — the `/api` suffix is stripped by `normalizeUrl()` in `@spaarke/auth`.
   * All fetch URLs must include `/api` prefix themselves:
   * `${getBffBaseUrl()}/api/documents/...`.
   *
   * @throws {Error} If `setRuntimeConfig` has not been called yet.
   */
  getBffBaseUrl(): string;

  /**
   * BFF API OAuth scope. Example: "api://1e40baad-.../user_impersonation".
   *
   * @throws {Error} If `setRuntimeConfig` has not been called yet.
   */
  getBffOAuthScope(): string;

  /**
   * MSAL client ID resolved from Dataverse Environment Variables at runtime.
   *
   * @throws {Error} If `setRuntimeConfig` has not been called yet.
   */
  getMsalClientId(): string;

  /**
   * Azure AD tenant ID. Behavior depends on `lazyTenantResolveWithTelemetry`:
   * - false (default): returns the stored value (or empty string).
   * - true: falls back to `resolveTenantIdSync()` when the stored value is
   *   empty; caches the resolved value; emits `onLazyTenantResolve` payload.
   *
   * @throws {Error} If `setRuntimeConfig` has not been called yet.
   */
  getTenantId(): string;
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Create a singleton runtime-config store for a Code Page solution.
 *
 * Each call returns a fresh, independent store — call this ONCE per solution
 * (typically from `src/config/runtimeConfig.ts`) and re-export the store's
 * methods as named exports so consumer modules can keep their existing import
 * statements:
 *
 * ```ts
 * import { getBffBaseUrl, getTenantId } from "../config/runtimeConfig";
 * ```
 *
 * The factory closes over the configuration knobs, so call sites get the exact
 * pre-consolidation behavior with no per-solution forks.
 *
 * @example DailyBriefing (with `waitForConfig` gate, no `getTenantId` divergence):
 * ```ts
 * const store = createRuntimeConfigStore({
 *   errorLabel: "DailyBriefing",
 *   enableWaitForConfig: true,
 * });
 * export const { setRuntimeConfig, waitForConfig, getBffBaseUrl,
 *                getBffOAuthScope, getMsalClientId } = store;
 * ```
 *
 * @example LegalWorkspace (with telemetry-backed lazy tenant resolution):
 * ```ts
 * const store = createRuntimeConfigStore({
 *   errorLabel: "LegalWorkspace",
 *   lazyTenantResolveWithTelemetry: true,
 *   onLazyTenantResolve: (payload) => trackEvent("TenantIdLazyResolve", payload),
 * });
 * export const { setRuntimeConfig, getBffBaseUrl, getBffOAuthScope,
 *                getMsalClientId, getTenantId } = store;
 * ```
 *
 * @example SpaarkeAi (simple tenant-id read, no fallback):
 * ```ts
 * const store = createRuntimeConfigStore({ errorLabel: "SpaarkeAi" });
 * export const { setRuntimeConfig, getBffBaseUrl, getBffOAuthScope,
 *                getMsalClientId, getTenantId } = store;
 * ```
 */
export function createRuntimeConfigStore(options: RuntimeConfigStoreOptions): RuntimeConfigStore {
  const {
    errorLabel,
    enableWaitForConfig = false,
    lazyTenantResolveWithTelemetry = false,
    onLazyTenantResolve,
  } = options;

  let _config: IRuntimeConfig | null = null;

  // waitForConfig() machinery — only meaningful when enableWaitForConfig is true.
  // When disabled, waitForConfig() still resolves correctly (immediately on
  // first setRuntimeConfig); the gate is essentially a no-op for consumers that
  // don't await it.
  let _resolveReady: (() => void) | undefined;
  const _readyPromise = new Promise<void>(resolve => {
    _resolveReady = resolve;
  });
  // When the gate is disabled, immediately resolve so awaiting it never blocks.
  if (!enableWaitForConfig && _resolveReady) {
    _resolveReady();
  }

  function setRuntimeConfig(config: IRuntimeConfig): void {
    _config = config;
    if (typeof window !== 'undefined') {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).__SPAARKE_BFF_BASE_URL__ = config.bffBaseUrl;
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).__SPAARKE_MSAL_CLIENT_ID__ = config.msalClientId;
    }
    // Resolve the gate (no-op if already resolved or disabled).
    if (_resolveReady) {
      _resolveReady();
    }
  }

  function waitForConfig(): Promise<void> {
    return _readyPromise;
  }

  function getConfig(): IRuntimeConfig {
    if (!_config) {
      throw new Error(
        `[${errorLabel}] Runtime config not initialized. ` +
          'Call setRuntimeConfig() in main.tsx before using config getters.'
      );
    }
    return _config;
  }

  function getBffBaseUrl(): string {
    return getConfig().bffBaseUrl;
  }

  function getBffOAuthScope(): string {
    return getConfig().bffOAuthScope;
  }

  function getMsalClientId(): string {
    return getConfig().msalClientId;
  }

  function getTenantId(): string {
    const stored = getConfig().tenantId;
    if (stored) return stored;

    if (!lazyTenantResolveWithTelemetry) {
      // Simple-read mode: return empty string when stored value is empty.
      return '';
    }

    // Lazy-resolve mode: try resolveTenantIdSync() and cache the result.
    const resolved = resolveTenantIdSync();
    if (resolved && _config) {
      _config = { ..._config, tenantId: resolved };
    }

    if (onLazyTenantResolve) {
      try {
        onLazyTenantResolve({
          resolved: String(!!resolved),
          storedWasEmpty: 'true',
          caller: new Error().stack?.split('\n')[2]?.trim().substring(0, 100) ?? 'unknown',
        });
      } catch {
        /* never let a telemetry callback break the getter */
      }
    }

    return resolved;
  }

  return {
    setRuntimeConfig,
    waitForConfig,
    getBffBaseUrl,
    getBffOAuthScope,
    getMsalClientId,
    getTenantId,
  };
}
