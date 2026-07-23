import type { AccountInfo } from '@azure/msal-browser';
import { SpaarkeAuthProvider, OfficeNaaStrategy, resolveConfig } from '@spaarke/auth';

/**
 * Authentication service for Office Add-ins.
 *
 * Auth v2 (email-communication-solution-r4 task 072 / FR-25): composes
 * `@spaarke/auth`'s `SpaarkeAuthProvider` + `OfficeNaaStrategy` — the
 * canonical NAA (Nested App Authentication) strategy for Office Add-ins per
 * ADR-028. This class is a thin, host-agnostic wrapper that preserves the
 * `IAuthService` surface consumed by `App.tsx`, `ApiClient.ts`, and both
 * taskpane entry points (`outlook/taskpane/index.tsx`, `word/taskpane/index.tsx`)
 * so call sites did not need to change.
 *
 * Uses `SpaarkeAuthProvider`'s constructor directly (not the `initAuth()` /
 * `getAuthProvider()` module-singleton convenience wrapper) because `initAuth()`
 * does not currently accept a `strategy` override — only the `SpaarkeAuthProvider`
 * class constructor does (`new SpaarkeAuthProvider(config, strategy)`). Both are
 * public, documented `@spaarke/auth` exports; `SpaarkeAuthProvider`'s own JSDoc
 * explicitly names `OfficeNaaStrategy` as the intended strategy for Office
 * Add-ins, so this is a supported composition, not a workaround.
 *
 * MUST NOT `new PublicClientApplication` / `createNestablePublicClientApplication`
 * directly here (ADR-028) — all MSAL construction lives inside `OfficeNaaStrategy`.
 */

export interface IAuthService {
  initialize(config: AuthConfig): Promise<void>;
  /** Resolves once a token has been acquired (or acquisition has failed). Callers check `isAuthenticated()` / `getAccessToken()` afterward. */
  signIn(): Promise<void>;
  signOut(): Promise<void>;
  getAccount(): AccountInfo | null;
  /**
   * @param scopes Accepted for backward compatibility with existing call sites.
   * Ignored — `@spaarke/auth` acquires a token for the single `bffApiScope`
   * configured at `initialize()` time (fixed per ADR-028's function-based
   * contract; no per-call scope negotiation).
   */
  getAccessToken(scopes?: string[]): Promise<string | null>;
  isAuthenticated(): boolean;
}

export interface AuthConfig {
  clientId: string;
  tenantId?: string;
  bffApiClientId?: string;
  redirectUri?: string;
  fallbackRedirectUri?: string;
}

class AuthService implements IAuthService {
  private provider: SpaarkeAuthProvider | null = null;
  private strategy: OfficeNaaStrategy | null = null;

  async initialize(config: AuthConfig): Promise<void> {
    const bffApiClientId = config.bffApiClientId || '1e40baad-e065-4aea-a8d4-4b7ab273458c';

    const resolved = resolveConfig({
      clientId: config.clientId,
      bffApiScope: `api://${bffApiClientId}/user_impersonation`,
      proactiveRefresh: true,
      ...(config.tenantId ? { tenantId: config.tenantId } : {}),
      ...(config.redirectUri ? { redirectUri: config.redirectUri } : {}),
    });

    this.strategy = new OfficeNaaStrategy(resolved, {
      fallbackRedirectUri: config.fallbackRedirectUri || `${window.location.origin}/auth-callback.html`,
    });

    if (this.provider) {
      this.provider.dispose();
    }
    this.provider = new SpaarkeAuthProvider(resolved, this.strategy);

    // Eagerly acquire (mirrors `initAuth()`'s warm-cache behavior) so
    // `isAuthenticated()`/`getAccount()` reflect a signed-in state immediately
    // after `initialize()` when a cached account/session is available (silent
    // acquisition only — never prompts on startup).
    await this.provider.getAccessToken();

    console.info(`[AuthService] Initialized via @spaarke/auth (naa=${this.strategy.isNaaActive()})`);
  }

  /**
   * Trigger token acquisition. `OfficeNaaStrategy.acquire()` tries silent
   * acquisition first, then `acquireTokenPopup` (routed through the Office
   * broker under NAA — not a real popup; a genuine popup only under the
   * legacy-client fallback path).
   */
  async signIn(): Promise<void> {
    if (!this.provider) {
      throw new Error('AuthService not initialized');
    }
    this.provider.clearCache();
    await this.provider.getAccessToken();
  }

  async signOut(): Promise<void> {
    if (!this.provider) {
      return;
    }
    await this.provider.logout();
  }

  getAccount(): AccountInfo | null {
    const msal = this.strategy?.getMsalInstance();
    if (!msal) return null;
    const accounts = msal.getAllAccounts();
    return accounts.length > 0 ? (accounts[0] ?? null) : null;
  }

  async getAccessToken(_scopes?: string[]): Promise<string | null> {
    if (!this.provider) {
      return null;
    }
    const token = await this.provider.getAccessToken();
    return token || null;
  }

  isAuthenticated(): boolean {
    return this.provider?.isAuthenticated() ?? false;
  }
}

// Export singleton instance
export const authService: IAuthService = new AuthService();
