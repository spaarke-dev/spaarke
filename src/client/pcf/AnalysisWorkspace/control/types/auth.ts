/**
 * @deprecated Use types from @spaarke/auth shared library instead.
 * Import { IAuthConfig, ITokenResult, TokenSource } from '@spaarke/auth';
 *
 * Authentication Types for Analysis Workspace PCF
 *
 * These types define the authentication provider contract and related data structures
 * for MSAL.js integration in the PCF control.
 */

/**
 * Access token with metadata
 */
export interface AuthToken {
  accessToken: string;
  expiresOn: Date;
  scopes: string[];
  account?: string;
}

/**
 * Authentication error information
 */
export interface AuthError {
  errorCode: string;
  errorMessage: string;
  requiresInteraction: boolean;
  originalError?: unknown;
}

/**
 * Authentication provider interface
 */
export interface IAuthProvider {
  initialize(): Promise<void>;
  getToken(scopes: string[]): Promise<string>;
  clearCache(): void;
  isAuthenticated(): boolean;
}

/**
 * Token cache entry (sessionStorage)
 */
export interface TokenCacheEntry {
  token: string;
  expiresAt: number;
  scopes: string[];
}
