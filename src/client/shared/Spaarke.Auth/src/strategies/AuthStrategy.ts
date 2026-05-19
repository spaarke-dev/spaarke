import type { TokenResult } from '../types';

/**
 * AuthStrategy — pluggable token acquisition interface (v2 architecture).
 *
 * Each Spaarke surface picks the strategy that matches its host environment:
 *   - BrowserMsalStrategy  → Dataverse PCFs + Code Pages (MSAL.js + localStorage)
 *   - OfficeNaaStrategy    → Outlook + Word Add-ins (Office NAA, added in task 080)
 *
 * SpaarkeAuthProvider is strategy-agnostic; it composes a cache layer in front of
 * whichever strategy is passed in. This replaces the pre-v2 6-strategy cascade
 * (Cache → SessionStorage → Bridge → Xrm → MsalSilent → MsalPopup) with a clean
 * single-strategy delegation.
 */
export interface AuthStrategy {
  /** Identifier for diagnostic logging (e.g. 'browser-msal', 'office-naa'). */
  readonly name: string;

  /**
   * Acquire a fresh access token. Implementations are responsible for their own
   * silent / fallback logic (e.g. acquireTokenSilent → ssoSilent → popup).
   *
   * MUST return a token whose `expiresOn` is in the future. Strategies that cannot
   * acquire a token return `{ accessToken: '', expiresOn: 0 }` rather than throwing,
   * so the caller can log a uniform "all acquisition exhausted" diagnostic.
   */
  acquire(): Promise<TokenResult>;

  /** Clear any strategy-local cached state (used on logout / 401 retry). */
  clearCache(): void;

  /**
   * Sign the user out at the identity-provider layer.
   *
   * BrowserMsalStrategy: `MSAL.logoutPopup` — clears the refresh token from
   * `localStorage` AND ends the Entra session (kills the session cookie). After
   * this completes, both `acquireTokenSilent` and `ssoSilent` will fail until
   * the user re-authenticates.
   *
   * OfficeNaaStrategy (task 080): TBD — Office NAA broker logout.
   *
   * Implementations should swallow non-fatal errors and at minimum cascade to
   * `clearCache()` so the local cache state matches the user-intended outcome.
   */
  logout(): Promise<void>;
}
