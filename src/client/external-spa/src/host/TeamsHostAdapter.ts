/**
 * Teams host adapter — the Teams-tab implementation of the collaboration-host adapter seam.
 *
 * Modeled on the SHAPE of `src/client/office-addins/shared/adapters/IHostAdapter.ts`: a per-host
 * isolation interface (`getHostType`, `initialize`, `isInitialized`) so the shared collaboration-core
 * feature components (WorkspaceHomePage, ProjectPage, DocumentLibrary, ...) render IDENTICALLY under
 * every host, with all divergence confined to this file + the bootstrap branch in `main.tsx` (design.md
 * D1 — "shared core + thin host adapter"). This adapter does NOT copy Office's content-oriented
 * methods (`getBody`/`getAttachments`/`insertLink`/...) — those are Outlook/Word-specific. The Teams
 * collaboration tab's per-host concerns are different, per design.md's "Host adapter seam" section:
 * bootstrap, context, framing/theme, and navigation/deep-linking.
 *
 * SCOPE (task 012 constraints — see the task POML):
 *   - Bootstrap + host-detection wiring ONLY. This adapter SELECTS — it does NOT implement — the
 *     Teams workforce auth strategy (NAA, then an MSAL popup; ADR-028 A9 Teams amendment, the multitenant client) in `../auth/msal-auth.ts`
 *     (`acquireTeamsWorkforceBffToken` / `getTeamsWorkforceSignedInInstance`). No token-acquisition
 *     algorithm lives in this file (ADR-028 A2).
 *   - Theme MUST use Fluent v9 semantic tokens (ADR-021). This adapter feeds the Teams-provided theme
 *     INTO the EXISTING `@spaarke/ui-components` theme cascade (`setUserThemePreference` /
 *     `resolveCodePageTheme` / `setupCodePageThemeListener`) rather than forking a second theme
 *     mechanism — `App.tsx` requires ZERO changes to pick up Teams theming.
 *   - Framing/CSP HTTP response headers (`frame-ancestors`) are task 062's scope
 *     (`staticwebapp.config.json`) — this file only performs client-side Teams JS SDK bootstrap
 *     (`app.initialize`, `notifyAppLoaded`/`notifySuccess`/`notifyFailure`).
 *   - Navigation/deep-linking: resolves the Teams-provided sub-page id (if any) into the SAME
 *     sessionStorage restore mechanism the CIAM redirect path already uses
 *     (`captureReturnTo`/`consumeReturnTo` in msal-auth.ts) — AuthGuard.tsx requires ZERO changes.
 *
 * See: docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md §8 (host checklist, reused as the
 * Teams-host acceptance test per design.md — see this adapter's report for the item-by-item mapping,
 * since this SPA does not literally embed LegalWorkspaceApp).
 */

import { app as teamsApp } from '@microsoft/teams-js';
import type { IPublicClientApplication } from '@azure/msal-browser';
import { setUserThemePreference } from '@spaarke/ui-components/utils/themeStorage';
import {
  acquireTeamsWorkforceBffToken,
  captureTeamsDeepLink,
  getTeamsWorkforceGuestVerdict,
  getTeamsWorkforceMsalInstance,
  getTeamsWorkforceSignedInInstance,
  isTeamsGuestAccount,
  isWorkforceHomeAccount,
  setActiveAccountFilter,
  setActiveAccountTenantId,
  setActiveBffTokenAcquirer,
  setGuestLinkHost,
  setWorkforceHomePlaneActive,
  signInTeamsWorkforceViaPopup,
  TeamsGuestAccountError,
  TeamsWorkforceAuthError,
  type TeamsWorkforceAuthConfig,
} from '../auth/msal-auth';
import { getWorkforceHomeClientConfig, isTenantId } from '../config';
import { activeBffScope } from '../auth/backend-selection';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Resolved, host-agnostic context handed back to main.tsx after a successful Teams bootstrap. */
export interface TeamsHostContext {
  /** Mapped to the SPA's existing 'light' | 'dark' theme preference (see mapTeamsTheme below). */
  theme: 'light' | 'dark';
  /** Teams-reported locale (e.g. "en-us"), informational. */
  locale: string;
  /** Teams host client type (e.g. "desktop", "web", "android", "ios"), informational. */
  hostClientType: string;
}

/**
 * Per-host isolation contract for the Teams tab, mirroring the SHAPE of Office add-ins'
 * `IHostAdapter` (`getHostType` / `initialize` / `isInitialized`) — adapted to this host's actual
 * concerns (bootstrap, context, theme, nav) instead of Office's content-access methods.
 */
export interface ITeamsHostAdapter {
  /** Always 'teams' for this adapter — mirrors IHostAdapter.getHostType(). */
  getHostType(): 'teams';
  /**
   * Initialize the adapter: Teams JS `app.initialize()` + context retrieval, theme wiring
   * (initial value + live change listener), deep-link capture, and Teams workforce auth-strategy
   * selection (delegates to `acquireTeamsWorkforceBffToken` — NAA, then an MSAL popup — implements NO auth logic
   * itself). Must be called before `getMsalInstance()`.
   *
   * @throws {TeamsWorkforceAuthError} When Teams workforce token acquisition fails — the caller
   *   (main.tsx) MUST surface a fail-loud sign-in error, never a blank/broken state.
   * @throws {TeamsGuestAccountError} When the signed-in account is a B2B guest of the Teams tenant: main.tsx renders the
   *   guest screen, and nothing is fetched.
   */
  initialize(): Promise<TeamsHostContext>;
  /** True once `initialize()` has resolved successfully — mirrors IHostAdapter.isInitialized(). */
  isInitialized(): boolean;
  /**
   * The authenticated MSAL instance to pass to `<MsalProvider>` so `useMsal()`/`useIsAuthenticated()`
   * (consumed unmodified by App.tsx/AuthGuard.tsx) reflect the signed-in Teams user. Only valid after
   * `initialize()` has resolved.
   */
  getMsalInstance(): IPublicClientApplication;
  /**
   * Recovery after `initialize()` failed on auth: run the MSAL popup sign-in (ADR-028 A9 Teams amendment fallback) and
   * finish the auth wiring `initialize()` would have done. MUST be called from a user gesture (the error
   * screen's "Sign in" button click) so the popup is not blocked; after it resolves the caller renders the
   * app with `getMsalInstance()`.
   *
   * @throws {TeamsWorkforceAuthError} When the popup fails (cancelled, blocked, consent missing).
   * @throws {TeamsGuestAccountError} When the signed-in account is a B2B guest of the Teams tenant (nothing is fetched).
   */
  signInWithPopup(): Promise<void>;
}

// ---------------------------------------------------------------------------
// Host detection
// ---------------------------------------------------------------------------

const TEAMS_INIT_DETECTION_TIMEOUT_MS = 750;

/**
 * Best-effort "are we inside a Teams host?" check for the bootstrap router in main.tsx. Teams JS
 * `app.initialize()` performs a handshake with the parent frame; OUTSIDE Teams there is no host to
 * respond, and per Microsoft's own guidance the promise can hang rather than reject — so detection
 * races it against a short timeout instead of awaiting it directly. A resolved race = Teams host
 * confirmed; a timeout/rejection = assume the standalone browser. This NEVER blocks the standalone
 * boot path beyond `timeoutMs` (acceptance criterion 6 — no broken/blank state outside Teams).
 */
export async function detectTeamsHost(timeoutMs: number = TEAMS_INIT_DETECTION_TIMEOUT_MS): Promise<boolean> {
  if (typeof window === 'undefined') return false;
  try {
    await Promise.race([
      teamsApp.initialize(),
      new Promise<never>((_, reject) => {
        setTimeout(() => reject(new Error('[TeamsHostAdapter] Teams init detection timed out')), timeoutMs);
      }),
    ]);
    return true;
  } catch {
    return false;
  }
}

// ---------------------------------------------------------------------------
// Pre-auth host signals (task 041 — customer picker / "no organization" screens)
// ---------------------------------------------------------------------------

/**
 * Tell Teams the tab has content to show, so the host's loading spinner stops while the pre-auth customer picker is
 * on screen. `app.initialize()` already ran in `detectTeamsHost()`, so this does NOT initialize again; the adapter's
 * own `initialize()` calls `notifyAppLoaded()` again after the choice (idempotent). Best-effort: a host error here
 * must never stop the screen from rendering.
 */
export function notifyTeamsAppLoaded(): void {
  try {
    teamsApp.notifyAppLoaded();
  } catch (err) {
    console.warn('[TeamsHostAdapter] notifyAppLoaded failed:', err);
  }
}

/**
 * Tell Teams the tab failed to start for a non-auth reason (no customer backend could be resolved — fail closed),
 * so the host does not keep spinning behind the explicit "No organization available" screen. Best-effort.
 */
export function notifyTeamsFailureOther(message: string): void {
  try {
    teamsApp.notifyFailure({ reason: teamsApp.FailedReason.Other, message });
  } catch (err) {
    console.warn('[TeamsHostAdapter] notifyFailure failed:', err);
  }
}

/**
 * The pre-auth Teams context (task 075): the Teams context tenant id and the Teams theme, read BEFORE any backend or token
 * decision. `tenantId` is `context.user.tenant.id` validated as a GUID and lower-cased; it is null when the context cannot
 * be read or the value is missing or not a GUID, and the caller then shows the "not set up" screen with zero token
 * requests. The theme is wired here too, so every pre-auth screen (not set up, customer picker) follows the Teams theme
 * (ADR-021), as the signed-in app does. Never throws.
 */
export async function readTeamsPreAuthContext(): Promise<{ tenantId: string | null }> {
  try {
    const context = await teamsApp.getContext();
    wireTeamsTheme(context.app?.theme);
    const raw = context.user?.tenant?.id;
    return { tenantId: isTenantId(raw) ? raw.toLowerCase() : null };
  } catch (err) {
    console.warn('[TeamsHostAdapter] Teams context could not be read:', err);
    return { tenantId: null };
  }
}

// ---------------------------------------------------------------------------
// Theme mapping
// ---------------------------------------------------------------------------

/**
 * Map the Teams-provided theme string ("default" | "dark" | "contrast" | "glass") to the SPA's
 * existing `ThemePreference`-compatible 'light' | 'dark' value (ADR-021 semantic tokens).
 *
 * 'contrast' (high-contrast) has no dedicated Code Page theme yet — `resolveCodePageTheme()` in
 * `@spaarke/ui-components` only resolves to `webDarkTheme`/`webLightTheme` — so 'contrast' maps to
 * 'dark' as the closer accessible approximation until a future task extends the shared cascade with
 * a dedicated high-contrast theme (ADR-021 lists `teamsHighContrastTheme` as the target). 'glass'
 * (Teams' newer adaptive theme) maps to 'light', matching Teams' own light-leaning default surface.
 */
export function mapTeamsTheme(raw: string): 'light' | 'dark' {
  return raw === 'dark' || raw === 'contrast' ? 'dark' : 'light';
}

/**
 * Feed the Teams-provided theme into the EXISTING `@spaarke/ui-components` cascade (localStorage 'spaarke-theme' key +
 * THEME_CHANGE_EVENT) and follow later changes. teams-js has a single handler slot, so calling this again replaces the
 * handler with an identical one. Returns the mapped initial theme.
 */
function wireTeamsTheme(rawTheme: string | undefined): 'light' | 'dark' {
  const initialTheme = mapTeamsTheme(rawTheme ?? 'default');
  // Theming is cosmetic and must never decide a bootstrap outcome: `setUserThemePreference` throws when storage is blocked,
  // which (inside the tid read) would show "not set up" for a listed tenant. A failure leaves the default theme.
  try {
    setUserThemePreference(initialTheme);
    teamsApp.registerOnThemeChangeHandler((raw: string) => {
      try {
        setUserThemePreference(mapTeamsTheme(raw));
      } catch (err) {
        console.warn('[TeamsHostAdapter] Teams theme change could not be applied:', err);
      }
    });
  } catch (err) {
    console.warn('[TeamsHostAdapter] Teams theme could not be applied:', err);
  }
  return initialTheme;
}

// ---------------------------------------------------------------------------
// Adapter implementation
// ---------------------------------------------------------------------------

class TeamsHostAdapter implements ITeamsHostAdapter {
  private initialized = false;
  private msalInstance: IPublicClientApplication | null = null;

  /** @param tenantId The Teams context tenant id (lower-case GUID) the bootstrap already found in the directory. */
  constructor(private readonly tenantId: string) {}

  getHostType(): 'teams' {
    return 'teams';
  }

  isInitialized(): boolean {
    return this.initialized;
  }

  getMsalInstance(): IPublicClientApplication {
    if (!this.msalInstance) {
      throw new Error('[TeamsHostAdapter] getMsalInstance() called before initialize() resolved.');
    }
    return this.msalInstance;
  }

  async signInWithPopup(): Promise<void> {
    const cfg: TeamsWorkforceAuthConfig = teamsWorkforceAuthConfig(this.tenantId);
    try {
      await signInTeamsWorkforceViaPopup(cfg);
    } catch (popupError) {
      throw popupError instanceof TeamsWorkforceAuthError
        ? popupError
        : new TeamsWorkforceAuthError('Teams workforce sign-in (MSAL popup) failed.', { naaError: null, popupError });
    }
    this.assertNotGuest(cfg);
    await this.completeAuthSetup(cfg);
    if (!this.initialized) {
      this.initialized = true;
      void teamsApp.notifySuccess();
    }
  }

  async initialize(): Promise<TeamsHostContext> {
    // detectTeamsHost() (called by main.tsx before this adapter is created) already ran
    // app.initialize() once — teams-js resolves the same underlying promise on repeated calls once
    // initialized, so this is a fast confirmation, not a second handshake.
    await teamsApp.initialize();

    const context = await teamsApp.getContext();
    // Signal to Teams that initial content has loaded — stops the host's own loading spinner as
    // early as the app can usefully show something, per Teams app-quality guidance.
    teamsApp.notifyAppLoaded();

    const initialTheme = this.wireTheme(context.app.theme);
    this.captureNavigation(context.page.subPageId);
    await this.selectAuthStrategy();

    this.initialized = true;
    // Signal successful initialization to Teams (dismisses the host's loading UI definitively).
    void teamsApp.notifySuccess();

    return {
      theme: initialTheme,
      locale: context.app.locale,
      hostClientType: context.app.host.clientType,
    };
  }

  /**
   * Theme (ADR-021 semantic tokens; Teams-provided dark theme). Feeds the Teams-provided theme
   * into the EXISTING `@spaarke/ui-components` cascade (localStorage 'spaarke-theme' key +
   * THEME_CHANGE_EVENT) instead of forking a second theme mechanism. App.tsx's
   * resolveCodePageTheme()/setupCodePageThemeListener() already gives localStorage the HIGHEST
   * priority in its cascade, so this makes Teams' theme authoritative — with ZERO changes to
   * App.tsx (no duplicated feature component). Returns the initial mapped theme for the caller.
   */
  private wireTheme(rawTheme: string): 'light' | 'dark' {
    // Note: teams-js's registerOnThemeChangeHandler has no unregister API (single handler slot per
    // page) - the listener lives for the tab's natural lifetime, which matches the page's own.
    return wireTeamsTheme(rawTheme);
  }

  /**
   * Navigation/deep-linking. Resolves the Teams-provided sub-page id (if any) into the SAME
   * sessionStorage restore mechanism the CIAM redirect path already uses — AuthGuard's existing
   * consumeReturnTo() picks it up post-auth with ZERO AuthGuard changes.
   */
  private captureNavigation(subPageId: string | undefined): void {
    captureTeamsDeepLink(subPageId);
  }

  /**
   * Auth strategy selection (ADR-028 A2 — select, do not implement). Acquires the Teams workforce
   * token once (so AuthGuard's isAuthenticated check is already true on first render — mirrors
   * LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md §6.1's "mount only after auth/config init is
   * complete" principle; the redirect branch in AuthGuard is never reached inside Teams, NFR-04
   * non-redirect), resolves the authenticated MSAL instance, and re-points bff-client.ts's active
   * token acquirer at the Teams strategy EXACTLY ONCE — no cross-selection; the CIAM default in
   * msal-auth.ts is never touched by the Teams path.
   */
  private async selectAuthStrategy(): Promise<void> {
    const cfg: TeamsWorkforceAuthConfig = teamsWorkforceAuthConfig(this.tenantId);

    try {
      await acquireTeamsWorkforceBffToken(cfg);
    } catch (err) {
      const failure =
        err instanceof TeamsWorkforceAuthError
          ? err
          : new TeamsWorkforceAuthError('Teams workforce bootstrap failed.', err);
      teamsApp.notifyFailure({
        reason: teamsApp.FailedReason.AuthFailed,
        message: failure.message,
      });
      throw failure; // Fail loud (acceptance criterion 4) — main.tsx renders an explicit error state.
    }

    this.assertNotGuest(cfg);
    await this.completeAuthSetup(cfg);
  }

  /**
   * A B2B guest of the Teams tenant cannot use this tab: NAA through `/{tid}` yields a guest token the customer BFF refuses
   * (401), and signing in again would reload into the same 401. Throws {@link TeamsGuestAccountError} BEFORE the BFF
   * acquirer is installed and before anything is fetched; main.tsx renders the guest screen. The account checked is the one
   * the successful acquisition signed in: its verdict is taken from that token result ({@link getTeamsWorkforceGuestVerdict}),
   * never from whichever account a shared cache holds. The cached account is read only if no verdict was recorded.
   */
  private assertNotGuest(cfg: TeamsWorkforceAuthConfig): void {
    const verdict = getTeamsWorkforceGuestVerdict();
    if (verdict) {
      if (verdict.guest) throw new TeamsGuestAccountError(verdict.detail);
      return;
    }
    const instance = getTeamsWorkforceSignedInInstance();
    const account = instance?.getActiveAccount() ?? instance?.getAllAccounts()[0] ?? null;
    if (isTeamsGuestAccount(account, cfg.tenantId)) throw new TeamsGuestAccountError();
  }

  /** Re-point bff-client.ts at the Teams strategy and pick the instance that holds the signed-in account. */
  private async completeAuthSetup(cfg: TeamsWorkforceAuthConfig): Promise<void> {
    // `scope` is the active backend's scope passed by bff-client.ts (the same record as the request URL, task 041);
    // without one, the active workforce backend's scope at call time.
    setActiveBffTokenAcquirer((scope, options) =>
      acquireTeamsWorkforceBffToken({ ...cfg, bffScope: scope ?? activeBffScope('workforce') }, options)
    );
    // The instance that holds the signed-in account: the NAA instance, or the popup fallback's
    // instance when NAA failed (ADR-028 A9 Teams amendment) — so useMsal() reflects the user either way.
    this.msalInstance = getTeamsWorkforceSignedInInstance() ?? (await getTeamsWorkforceMsalInstance(cfg));
    // The Teams tab is the workforce HOME plane too (ADR-028 A9 Teams amendment): the registration gate runs here exactly
    // as in the browser, and only this tenant's accounts count as the signed-in user. The A8 client is used in Teams ONLY
    // for the silent guest-token link, which this flag routes through NAA.
    setActiveAccountTenantId(cfg.tenantId);
    setActiveAccountFilter(isWorkforceHomeAccount);
    setWorkforceHomePlaneActive(true);
    setGuestLinkHost('teams');
  }
}

/**
 * The Teams auth config (ADR-028 A9): the MULTITENANT home client, the Teams context tenant id, and the BFF scope of the
 * active workforce backend (the bootstrap selected it by that tid before `initialize()`). Throws (fail loud) when the client
 * config is missing or no workforce backend is active.
 */
function teamsWorkforceAuthConfig(tenantId: string): TeamsWorkforceAuthConfig {
  return { ...getWorkforceHomeClientConfig(), tenantId, bffScope: activeBffScope('workforce') };
}

/** Factory mirroring the Office add-ins' `HostAdapterFactory.create()` convention. */
export function createTeamsHostAdapter(tenantId: string): ITeamsHostAdapter {
  return new TeamsHostAdapter(tenantId);
}
