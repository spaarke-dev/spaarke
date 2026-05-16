import type { IAuthConfig } from './types';
import { SpaarkeAuthProvider } from './SpaarkeAuthProvider';
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
export declare function initAuth(config?: IAuthConfig): Promise<SpaarkeAuthProvider>;
/**
 * Get the current auth provider instance.
 * Throws if initAuth() has not been called.
 */
export declare function getAuthProvider(): SpaarkeAuthProvider;
//# sourceMappingURL=initAuth.d.ts.map