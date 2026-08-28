/**
 * SpaarkeAi public-config singleton — feature-flag consumer of BFF GET /api/config.
 *
 * Introduced by customer-provisioning-orchestration-r1 task 087 per spec.md FR-36
 * + §7.9 close-pattern: the BFF endpoint returns { bffUrl, msalClientId, tenantId,
 * featureFlags } short-cached (60s + ETag). Browser clients fetch this at
 * bootstrap so per-env changes (especially feature flags) do NOT require a
 * per-surface rebuild + redeploy — one build, N envs.
 *
 * Scope of THIS helper (SpaarkeAi-specific):
 *   - Fetches /api/config once at bootstrap after `setRuntimeConfig(config)` so
 *     the BFF URL is known (chicken-and-egg constraint: to hit /api/config we
 *     need to know the BFF origin).
 *   - Stores the response in a module-scoped variable that survives for the
 *     lifetime of the page. Fetch-once semantics — the BFF caches, and per-env
 *     flag changes take effect on the NEXT page load (per POML acceptance:
 *     "short cache reduces load without preventing cache-bust").
 *   - Exposes `getFeatureFlag(name, defaultValue?)` for consumer code that
 *     wants to gate UI behavior on a runtime flag.
 *
 * Non-goals:
 *   - Does NOT replace `runtimeConfig.ts` (which is the MSAL/bootstrap-critical
 *     store consumed by `@spaarke/auth`). This helper is additive; feature-flag
 *     reads are optional, not blocking.
 *   - Does NOT throw on fetch failure — a network error or missing endpoint
 *     leaves the feature-flag map empty, and `getFeatureFlag(...)` returns the
 *     caller's `defaultValue`. This preserves the "flags are advisory" invariant
 *     from PublicConfigOptions.cs.
 *
 * @see PublicConfigOptions.cs — the server-side Tier-1 options this consumes.
 * @see ConfigEndpoints.cs — the BFF endpoint (/api/config) surfacing the bundle.
 */

/**
 * Wire shape returned by GET /api/config — matches PublicConfigResponse in
 * ConfigEndpoints.cs (camelCase per the endpoint's JSON serializer defaults).
 */
export interface IPublicConfig {
  bffUrl: string;
  msalClientId: string;
  tenantId: string;
  featureFlags: Record<string, boolean>;
}

let cached: IPublicConfig | null = null;

/**
 * Fetch the public runtime config bundle from the BFF once at bootstrap.
 *
 * Called by main.tsx AFTER `setRuntimeConfig(config)` (so bffBaseUrl is known)
 * and BEFORE `ensureAuthInitialized()` (per POML acceptance: consumers fetch
 * /api/config at bootstrap BEFORE MSAL init).
 *
 * Non-blocking on error: a fetch failure leaves the cache null and
 * `getFeatureFlag(name, default)` returns the caller's default. Bootstrap
 * MUST NOT hard-fail on this — the endpoint is a convenience, not critical.
 */
export async function fetchPublicConfig(bffBaseUrl: string): Promise<IPublicConfig | null> {
  if (cached) {
    return cached;
  }

  const url = `${bffBaseUrl.replace(/\/$/, "")}/api/config`;
  try {
    const response = await fetch(url, {
      method: "GET",
      headers: { Accept: "application/json" },
      // Anonymous endpoint — no credentials required.
    });

    if (!response.ok) {
      console.warn(
        `[SpaarkeAi] /api/config returned ${response.status} ${response.statusText}; ` +
          "feature flags will fall back to defaults.",
      );
      return null;
    }

    const data = (await response.json()) as IPublicConfig;
    cached = data;

    console.info(
      `[SpaarkeAi] Public config loaded (${Object.keys(data.featureFlags ?? {}).length} feature flag(s))`,
    );
    return data;
  } catch (err) {
    // Non-fatal — bootstrap MUST NOT hard-fail on this.
    console.warn(
      `[SpaarkeAi] Failed to fetch /api/config from ${url}; feature flags will fall back to defaults.`,
      err,
    );
    return null;
  }
}

/**
 * Read a single feature flag. Returns `defaultValue` (default: `false`) when
 * the flag is absent OR when /api/config has not been fetched successfully.
 */
export function getFeatureFlag(name: string, defaultValue: boolean = false): boolean {
  if (!cached || !cached.featureFlags) {
    return defaultValue;
  }
  const value = cached.featureFlags[name];
  return typeof value === "boolean" ? value : defaultValue;
}

/**
 * Read the full cached bundle. Returns null when /api/config has not been
 * fetched successfully. Prefer `getFeatureFlag(...)` for flag reads.
 */
export function getPublicConfig(): IPublicConfig | null {
  return cached;
}
