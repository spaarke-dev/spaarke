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
  /**
   * Invalidate the in-memory token cache so the next `getAccessToken()` call
   * re-acquires from the provider/strategy rather than returning a
   * possibly-stale cached token. Does NOT log the user out (mirrors
   * `SpaarkeAuthProvider.clearCache()`, not `clearAllCaches()`/`logout()`).
   *
   * Used by `ApiClient`'s single-retry-on-401 path (task 040 / FR-B0) before
   * re-acquiring a token for the retry attempt — mirrors `@spaarke/auth`'s
   * `authenticatedFetch`, which calls `provider.clearCache()` on a 401 before
   * its own retry.
   *
   * @param refusedToken The token the server just answered 401 for (#1571 fix round 3). The strategy records it as
   *   refused only when it is still the token it last issued; with no argument nothing is recorded.
   */
  clearCache(refusedToken?: string): void;
  /**
   * #1571 review F2: subscribe to "sign-in required" — raised when the provider could not acquire a token silently,
   * or when a request was still refused (401) after its single retry. The pane shows its sign-in prompt: nothing else
   * can renew the session, because the only interactive sign-in is a user's click. Returns the unsubscribe. Optional
   * so test doubles need not implement it.
   */
  onSignInRequired?(listener: () => void): () => void;
  /** #1571 review F2: raise "sign-in required" (a request's 401 that survived its retry). */
  notifySignInRequired?(): void;
  /**
   * Task 113 (dev-only Diagnostics view): how this pane signed in — the MSAL authority and whether the Office
   * broker (NAA) or the popup fallback is in use. Optional so test doubles need not implement it.
   */
  getSignInDiagnostics?(): SignInDiagnostics;
}

/** Task 113: see {@link IAuthService.getSignInDiagnostics}. */
export interface SignInDiagnostics {
  authority: string | null;
  naaActive: boolean;
}

/** #1571 review F2: what a Sign in click that produced no token tells the user. */
export const SIGN_IN_INCOMPLETE_MESSAGE =
  'Sign-in did not complete. If a pop-up was blocked, allow pop-ups for this site and try again.';

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
  private authority: string | null = null;
  private readonly signInRequiredListeners = new Set<() => void>();

  async initialize(config: AuthConfig): Promise<void> {
    const bffApiClientId = config.bffApiClientId || '1e40baad-e065-4aea-a8d4-4b7ab273458c';

    const resolved = resolveConfig({
      clientId: config.clientId,
      bffApiScope: `api://${bffApiClientId}/user_impersonation`,
      proactiveRefresh: true,
      // #1571: every acquire the provider drives (start-up, proactive refresh, a 401 retry, each BFF call) is silent;
      // the only interactive acquire is `signIn()`, from the Sign in click.
      requireSilentOnly: true,
      ...(config.tenantId ? { tenantId: config.tenantId } : {}),
      ...(config.redirectUri ? { redirectUri: config.redirectUri } : {}),
    });

    this.authority = resolved.authority;
    this.strategy = new OfficeNaaStrategy(resolved, {
      fallbackRedirectUri: config.fallbackRedirectUri || `${window.location.origin}/auth-callback.html`,
    });

    if (this.provider) {
      this.provider.dispose();
    }
    this.provider = new SpaarkeAuthProvider(resolved, this.strategy);

    // Eagerly acquire (mirrors `initAuth()`'s warm-cache behavior) so
    // `isAuthenticated()`/`getAccount()` reflect a signed-in state immediately
    // after `initialize()` when a cached account/session is available. SILENT only
    // (#1571): no click led here, so no popup and no broker prompt — a popup
    // without a user gesture is blocked on Office on the web. When nothing can be
    // had silently the pane shows Sign in, whose click runs `signIn()`.
    await this.provider.getAccessToken();

    console.info(`[AuthService] Initialized via @spaarke/auth (naa=${this.strategy.isNaaActive()})`);
  }

  /**
   * Sign in from a user's click — the ONLY interactive path (#1571). Call it from
   * the click handler, never from a timer or a load. `OfficeNaaStrategy.acquireInteractive()`
   * tries silent acquisition first, then `acquireTokenPopup` (routed through the
   * Office broker under NAA — not a real popup; a genuine popup only under the
   * standard-client path). The provider's cache is then filled with the token the
   * click returned (the strategy hands it to the provider's next acquire), not with
   * a second request.
   *
   * Throws an Error with {@link SIGN_IN_INCOMPLETE_MESSAGE} when the click produced
   * no token (the user closed the prompt, or a pop-up blocker stopped it), so the
   * pane can say so instead of showing nothing.
   */
  async signIn(): Promise<void> {
    if (!this.provider || !this.strategy) {
      throw new Error('AuthService not initialized');
    }
    this.provider.clearCache();
    const interactive = await this.strategy.acquireInteractive();
    if (!interactive.accessToken) {
      throw new Error(SIGN_IN_INCOMPLETE_MESSAGE);
    }
    const token = await this.provider.getAccessToken();
    if (!token) {
      throw new Error(SIGN_IN_INCOMPLETE_MESSAGE);
    }
  }

  async signOut(): Promise<void> {
    if (!this.provider) {
      return;
    }
    await this.provider.logout();
  }

  getSignInDiagnostics(): SignInDiagnostics {
    return { authority: this.authority, naaActive: this.strategy?.isNaaActive() ?? false };
  }

  /** The account the strategy signs in with (#1571: in the authority's tenant, never simply the first cached). */
  getAccount(): AccountInfo | null {
    return this.strategy?.getAccount() ?? null;
  }

  async getAccessToken(_scopes?: string[]): Promise<string | null> {
    if (!this.provider) {
      return null;
    }
    const token = await this.provider.getAccessToken();
    if (!token) {
      // #1571 review F2: no token silently. Only the user's Sign in click can renew the session.
      this.notifySignInRequired();
      return null;
    }
    return token;
  }

  onSignInRequired(listener: () => void): () => void {
    this.signInRequiredListeners.add(listener);
    return () => {
      this.signInRequiredListeners.delete(listener);
    };
  }

  notifySignInRequired(): void {
    for (const listener of [...this.signInRequiredListeners]) {
      try {
        listener();
      } catch (err) {
        console.warn('[AuthService] a sign-in-required listener failed:', err);
      }
    }
  }

  isAuthenticated(): boolean {
    return this.provider?.isAuthenticated() ?? false;
  }

  clearCache(refusedToken?: string): void {
    this.provider?.clearCache();
    // #1571 review F1: this runs before a 401 retry, and MSAL's cached token is the one the server just refused, so
    // the re-acquire asks for a new one (still silently). The token the request sent is passed on, so a late 401 for
    // an older token never marks a newer, good one as refused (fix round 3).
    this.strategy?.requestFreshToken(refusedToken);
  }
}

// Export singleton instance
export const authService: IAuthService = new AuthService();
