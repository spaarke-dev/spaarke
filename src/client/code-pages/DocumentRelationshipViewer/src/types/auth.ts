/**
 * Authentication types for DocumentRelationshipViewer Code Page
 */

export interface AuthToken {
    accessToken: string;
    expiresOn: Date;
    scopes: string[];
    account?: string;
}

export interface AuthError {
    errorCode: string;
    errorMessage: string;
    requiresInteraction: boolean;
    originalError?: unknown;
}

export interface IAuthProvider {
    initialize(): Promise<void>;
    getToken(scopes: string[]): Promise<string>;
    clearCache(): void;
    isAuthenticated(): boolean;
}

export interface TokenCacheEntry {
    token: string;
    expiresAt: number;
    scopes: string[];
}
