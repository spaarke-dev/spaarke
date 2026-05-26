// Types
export type {
  IAuthConfig,
  IProblemDetails,
  TokenResult,
  AuthenticatedFetchFn,
} from './types';

// Errors
export { AuthError, ApiError } from './errors';

// Config
export { resolveConfig, TOKEN_EXPIRY_BUFFER_MS, PROACTIVE_REFRESH_INTERVAL_MS } from './config';

// Library version (logged on init; surfaces INV-8 bundling-reality regressions)
export { VERSION } from './version';

// Runtime config (Dataverse environment variable resolution)
export { resolveRuntimeConfig, clearRuntimeConfigCache } from './resolveRuntimeConfig';
export type { IRuntimeConfig } from './resolveRuntimeConfig';

// Pluggable auth strategy (v2 — task 010)
export type { AuthStrategy } from './strategies/AuthStrategy';
export { BrowserMsalStrategy } from './strategies/BrowserMsalStrategy';

// Office Add-in strategy (v2 — task 080). Consumed by Outlook + Word Add-ins
// once tasks 081/082 swap their bootstrap to `initAuth({ strategy: new OfficeNaaStrategy(config) })`.
export { OfficeNaaStrategy } from './strategies/OfficeNaaStrategy';
export type { IOfficeNaaConfig } from './strategies/OfficeNaaStrategy';

// Core provider
export { SpaarkeAuthProvider } from './SpaarkeAuthProvider';

// Authenticated fetch
export { authenticatedFetch } from './authenticatedFetch';

// BFF URL builder (use this for ALL BFF API URLs — never hand-concat template literals)
export { buildBffApiUrl } from './buildBffApiUrl';

// Public init API
export { initAuth, getAuthProvider } from './initAuth';

// Function-based React hook (v2 — task 013). Primary public API for Phase B consumers.
export { useAuth } from './useAuth';
export type { UseAuthResult } from './useAuth';

// Synchronous tenant ID resolution (for click handlers — cannot await async getTenantId)
export { resolveTenantIdSync } from './resolveTenantIdSync';
