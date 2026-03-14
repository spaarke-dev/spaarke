// Types
export type { IAuthConfig, ITokenResult, ITokenStrategy, IProblemDetails, TokenCacheEntry, TokenSource } from './types';

// Errors
export { AuthError, ApiError } from './errors';

// Config
export { resolveConfig, TOKEN_EXPIRY_BUFFER_MS, PROACTIVE_REFRESH_INTERVAL_MS } from './config';

// Token bridge
export { publishToken, readBridgeToken, clearBridgeToken } from './tokenBridge';

// Core provider
export { SpaarkeAuthProvider } from './SpaarkeAuthProvider';

// Authenticated fetch
export { authenticatedFetch } from './authenticatedFetch';

// Public init API
export { initAuth, getAuthProvider } from './initAuth';
