import type { IAuthConfig } from './types';
import { AuthError } from './errors';
import { SpaarkeAuthProvider } from './SpaarkeAuthProvider';

let _provider: SpaarkeAuthProvider | null = null;

/**
 * Initialize the @spaarke/auth provider. Call once at app startup.
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
  if (_provider) {
    const existingClientId = _provider.getConfig().clientId;
    const requestedClientId = config?.clientId;
    if (!requestedClientId || requestedClientId === existingClientId) {
      console.info(
        `[Spaarke.initAuth] Duplicate init coalesced — reusing existing provider for clientId ${
          existingClientId ? existingClientId.substring(0, 8) + '...' : '(default)'
        }`
      );
      return _provider;
    }
    // Genuinely different app (different clientId) — dispose the old instance
    // (cleans up its broadcast listener + proactive-refresh interval) and replace.
    _provider.dispose();
  }

  _provider = new SpaarkeAuthProvider(config);

  // Eagerly acquire a token to warm the cache
  await _provider.getAccessToken();

  return _provider;
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
