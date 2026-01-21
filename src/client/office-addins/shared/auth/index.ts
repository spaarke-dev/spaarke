/**
 * NAA (Nested App Authentication) Module
 *
 * Provides authentication services for Office Add-ins using MSAL.js 3.x
 * with Nested App Authentication support.
 *
 * @example
 * ```typescript
 * import { naaAuthService, getBffApiScopes } from '../auth';
 *
 * // Initialize on app startup
 * await naaAuthService.initialize();
 *
 * // Check NAA support
 * if (naaAuthService.isNaaSupported()) {
 *   console.log('Using Nested App Authentication');
 * }
 *
 * // Get access token for API calls
 * const { accessToken } = await naaAuthService.getAccessToken();
 * ```
 */

// Main service
export { naaAuthService, NaaAuthServiceImpl } from './NaaAuthService';

// Types and interfaces
export type {
  INaaAuthService,
  NaaAuthState,
  TokenResult,
} from './NaaAuthService';

// Error types
export { NaaAuthError, NaaAuthErrorCode } from './NaaAuthService';

// Configuration
export {
  DEFAULT_AUTH_CONFIG,
  createNaaMsalConfig,
  createFallbackMsalConfig,
  getBffApiScopes,
  createSilentRequest,
  createPopupRequest,
  TOKEN_CACHE_PREFIX,
  TOKEN_EXPIRY_BUFFER_SECONDS,
} from './authConfig';

export type { NaaAuthConfig } from './authConfig';
