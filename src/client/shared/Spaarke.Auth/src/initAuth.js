import { AuthError } from './errors';
import { SpaarkeAuthProvider } from './SpaarkeAuthProvider';
let _provider = null;
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
export async function initAuth(config) {
    // Dispose previous instance if re-initializing
    if (_provider) {
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
export function getAuthProvider() {
    if (!_provider) {
        throw new AuthError('Auth not initialized. Call initAuth() before using authenticatedFetch().', 'not_initialized');
    }
    return _provider;
}
//# sourceMappingURL=initAuth.js.map