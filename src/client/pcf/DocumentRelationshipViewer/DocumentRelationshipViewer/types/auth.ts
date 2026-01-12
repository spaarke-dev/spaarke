/**
 * Authentication Types for DocumentRelationshipViewer PCF
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
