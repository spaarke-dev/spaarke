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
 *   - Bootstrap + host-detection wiring ONLY. This adapter SELECTS — it does NOT implement — task
 *     011's Teams SSO/NAA auth strategy (`acquireTeamsWorkforceBffToken` / `getTeamsWorkforceMsalInstance`
 *     in `../auth/msal-auth.ts`). No token-acquisition algorithm lives in this file (ADR-028 A2).
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
  getTeamsWorkforceMsalInstance,
  setActiveBffTokenAcquirer,
  TeamsWorkforceAuthError,
  type TeamsWorkforceAuthConfig,
} from '../auth/msal-auth';
import { getTeamsWorkforceEnvConfig } from '../config';

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
   * selection (delegates to task 011's `acquireTeamsWorkforceBffToken` — implements NO auth logic
   * itself). Must be called before `getMsalInstance()`.
   *
   * @throws {TeamsWorkforceAuthError} When Teams workforce token acquisition fails — the caller
   *   (main.tsx) MUST surface a fail-loud sign-in error, never a blank/broken state.
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

// ---------------------------------------------------------------------------
// Adapter implementation
// ---------------------------------------------------------------------------

class TeamsHostAdapter implements ITeamsHostAdapter {
  private initialized = false;
  private msalInstance: IPublicClientApplication | null = null;

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
    const initialTheme = mapTeamsTheme(rawTheme);
    setUserThemePreference(initialTheme);
    teamsApp.registerOnThemeChangeHandler((raw: string) => {
      setUserThemePreference(mapTeamsTheme(raw));
    });
    // Note: teams-js's registerOnThemeChangeHandler has no unregister API (single handler slot per
    // page) — the listener lives for the tab's natural lifetime, which matches the page's own.
    return initialTheme;
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
    const cfg: TeamsWorkforceAuthConfig = getTeamsWorkforceEnvConfig();

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

    setActiveBffTokenAcquirer(() => acquireTeamsWorkforceBffToken(cfg));
    this.msalInstance = await getTeamsWorkforceMsalInstance(cfg);
  }
}

/** Factory mirroring the Office add-ins' `HostAdapterFactory.create()` convention. */
export function createTeamsHostAdapter(): ITeamsHostAdapter {
  return new TeamsHostAdapter();
}
