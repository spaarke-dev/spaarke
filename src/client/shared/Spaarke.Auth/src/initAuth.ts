import type { IAuthConfig } from './types';
import { AuthError } from './errors';
import { SpaarkeAuthProvider } from './SpaarkeAuthProvider';
import { resolveTenantSync } from './config';
import { discoverTenantId } from './resolveRuntimeConfig';
import { tenantFromAuthority } from './tenant';

let _provider: SpaarkeAuthProvider | null = null;

/**
 * Serializes provider selection. Tenant discovery is async, so two concurrent
 * initAuth() calls (one code page embedding another) could otherwise both see
 * "no provider yet" and build two MSAL instances — the exact 2026-08-10 defect
 * the coalescing below exists to prevent.
 */
let _selection: Promise<unknown> = Promise.resolve();

/**
 * Initialize the @spaarke/auth provider. Call once at app startup.
 *
 * When neither the config nor the host yields a tenant synchronously, the
 * environment's tenant is discovered first (`sprk_TenantId` env var, then the
 * BFF's `/api/config/client`) so the MSAL authority is tenant-specific (#1453).
 * Inside a Dataverse host with no resolvable tenant this throws an `AuthError`
 * (`tenant_unresolved`) rather than signing in against `/organizations`.
 *
 * @param config Optional configuration overrides
 * @returns The initialized SpaarkeAuthProvider
 *
 * @example
 * ```ts
 * import { initAuth, authenticatedFetch } from '@spaarke/auth';
 *
 * // Basic initialization
 * await initAuth();
 *
 * // With options
 * await initAuth({ proactiveRefresh: true });
 * await initAuth({ requireXrm: true });
 *
 * // Use authenticated fetch anywhere
 * const response = await authenticatedFetch('/api/documents/123/preview-url');
 * ```
 */
export async function initAuth(config?: IAuthConfig): Promise<SpaarkeAuthProvider> {
  // A duplicate init that will coalesce anyway needs no network and must not
  // queue behind another init's tenant discovery.
  const immediate = coalesceWithoutDiscovery(config);
  if (immediate) return immediate;

  const run = _selection.then(() => selectProvider(config));
  _selection = run.catch(() => undefined);
  const { provider, created } = await run;

  // Eagerly acquire a token to warm the cache (new providers only — a coalesced
  // call returns the existing provider as before).
  if (created) {
    await provider.getAccessToken();
  }

  return provider;
}

/**
 * The existing provider when a duplicate init would coalesce onto it without any
 * discovery: no clientId requested, or the same clientId on a provider that
 * already has a tenant authority. Otherwise null.
 */
function coalesceWithoutDiscovery(config?: IAuthConfig): SpaarkeAuthProvider | null {
  if (!_provider) return null;
  const existing = _provider.getConfig();
  const requestedClientId = config?.clientId;
  const coalesces =
    !requestedClientId || (requestedClientId === existing.clientId && !!tenantFromAuthority(existing.authority));
  if (!coalesces) return null;
  logCoalesced(existing.clientId);
  return _provider;
}

function logCoalesced(clientId: string): void {
  console.info(
    `[Spaarke.initAuth] Duplicate init coalesced — reusing existing provider for clientId ${
      clientId ? clientId.substring(0, 8) + '...' : '(default)'
    }`
  );
}

async function selectProvider(config?: IAuthConfig): Promise<{ provider: SpaarkeAuthProvider; created: boolean }> {
  // Idempotency guard (2026-08-10): when ONE code page embeds another that ALSO
  // bootstraps @spaarke/auth, both share this single module singleton and each
  // fires initAuth() — e.g. SpaarkeAi embeds LegalWorkspaceApp. The prior
  // dispose-and-replace behaviour was a defect in that scenario:
  //   (a) it left the FIRST provider's in-flight interactive acquisition running,
  //       so the SECOND provider's acquisition collided as MSAL
  //       `interaction_in_progress`; and
  //   (b) whichever init landed last won the singleton — which could be one built
  //       from a not-yet-resolved config (authority `/organizations`), breaking
  //       ssoSilent even though the other provider had the correct tenant.
  // A duplicate init for the SAME clientId must therefore COALESCE to the existing
  // provider, never spin up a second MSAL instance against the shared localStorage
  // cache. A genuinely different app (different clientId) still replaces, so
  // multi-tenant / host re-init is preserved. Single-init consumers (every PCF,
  // wizard, and standalone code page) never reach this branch — behaviour for
  // them is unchanged.
  //
  // One exception (#1453): an existing provider on a NON-tenant authority (the
  // /organizations fallback, possible only outside a Dataverse host) is replaced
  // when a later init for the same clientId resolves a valid tenant — otherwise
  // the degraded provider would lock out the correctly-tenanted one for the
  // life of the page.
  if (_provider) {
    const existing = _provider.getConfig();
    const requestedClientId = config?.clientId;
    if (!requestedClientId || requestedClientId === existing.clientId) {
      if (requestedClientId && !tenantFromAuthority(existing.authority)) {
        const old = _provider;
        const upgraded = await withDiscoveredTenant(config);
        if (upgraded.tenant) {
          // Let an acquisition the old provider has in flight (possibly an open
          // sign-in popup) finish first: the new MSAL instance shares the clientId,
          // and starting its own interactive request now would fail with
          // `interaction_in_progress`.
          await old.whenIdle();
          const next = new SpaarkeAuthProvider(upgraded.config);
          console.info(
            `[Spaarke.initAuth] Replacing provider on non-tenant authority ${existing.authority} ` +
              `with tenant ${upgraded.tenant.substring(0, 8)}...`
          );
          // Keeps the shared MSAL cache, and forwards anyone still holding `old`.
          old.supersede(next);
          _provider = next;
          return { provider: next, created: true };
        }
      }
      logCoalesced(existing.clientId);
      return { provider: _provider, created: false };
    }
  }

  const { config: effective } = await withDiscoveredTenant(config);
  // Build first: if construction throws (e.g. tenant_unresolved), the previous
  // provider stays in place untouched.
  const next = new SpaarkeAuthProvider(effective);
  if (_provider) {
    // Genuinely different app (different clientId) — dispose the old instance
    // (cleans up its broadcast listener + proactive-refresh interval) and replace.
    _provider.dispose();
  }
  _provider = next;
  return { provider: next, created: true };
}

/**
 * Return `config` with a discovered `tenantId` when the synchronous chain in
 * resolveConfig() would find none. Skips discovery when there is no client ID
 * at all (resolveConfig throws for that anyway).
 */
async function withDiscoveredTenant(config?: IAuthConfig): Promise<{ config?: IAuthConfig; tenant: string }> {
  const sync = resolveTenantSync(config, false);
  if (sync) return { config, tenant: sync.tenant };

  const hasClientId = !!config?.clientId || (typeof window !== 'undefined' && !!window.__SPAARKE_MSAL_CLIENT_ID__);
  if (!hasClientId) return { config, tenant: '' };

  const bffBaseUrl = config?.bffBaseUrl ?? (typeof window !== 'undefined' ? window.__SPAARKE_BFF_URL__ : undefined);
  const tenant = await discoverTenantId(bffBaseUrl);
  return tenant ? { config: { ...config, tenantId: tenant }, tenant } : { config, tenant: '' };
}

/**
 * Get the current auth provider instance.
 * Throws if initAuth() has not been called.
 */
export function getAuthProvider(): SpaarkeAuthProvider {
  if (!_provider) {
    throw new AuthError('Auth not initialized. Call initAuth() before using authenticatedFetch().', 'not_initialized');
  }
  return _provider;
}
