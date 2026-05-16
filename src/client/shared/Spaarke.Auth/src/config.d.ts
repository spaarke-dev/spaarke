import type { IAuthConfig } from './types';
/** Buffer (ms) before token expiry to consider it stale. */
export declare const TOKEN_EXPIRY_BUFFER_MS: number;
/** Proactive refresh interval (ms). */
export declare const PROACTIVE_REFRESH_INTERVAL_MS: number;
/**
 * Resolve the full config, merging user overrides with defaults and window globals.
 * Throws if required values (clientId, bffApiScope) are not provided via config, window globals,
 * or Dataverse environment variables. No silent fallback to dev values.
 */
export declare function resolveConfig(userConfig?: IAuthConfig): Required<IAuthConfig>;
//# sourceMappingURL=config.d.ts.map