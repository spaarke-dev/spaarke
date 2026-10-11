import * as React from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MsalProvider } from '@azure/msal-react';
import { Button, FluentProvider, Spinner, Text, tokens } from '@fluentui/react-components';
import { resolveCodePageTheme } from '@spaarke/ui-components/utils/themeStorage';
import { msalInstance } from './auth/msal-config';
import { extractAuthDiagnostics } from './auth/auth-diagnostics';
import { detectTeamsHost, createTeamsHostAdapter, type ITeamsHostAdapter } from './host/TeamsHostAdapter';
import { getStoredRealm, setStoredRealm, clearStoredRealm, type Realm } from './auth/realm';
import { resolveStandalonePlane, applyStandalonePlane, type StandalonePlane } from './auth/standalone-plane';
import {
  activateBackend,
  chooseBackend,
  clearActiveBackend,
  resolveBackendSelection,
  type Backend,
  type BackendPlane,
} from './auth/backend-selection';
import { RealmChooser } from './components/auth/RealmChooser';
import { CustomerPicker } from './components/auth/CustomerPicker';
import { TeamsAuthError } from './components/auth/TeamsAuthError';
import { App } from './App';

// Power Pages Code Page SPA — React 18 with createRoot (bundled, not platform-provided).
// External users authenticate via Entra B2B — they are guest accounts in the main
// Spaarke workforce tenant and sign in with their existing Microsoft 365 credentials.
// MSAL (authorization code + PKCE) handles all token acquisition.
// See ADR-022 for the Code Page React 18 standard.
// See notes/auth-migration-b2b-msal.md for auth architecture details.
//
// HOST-DETECTION WIRING (task 012 - Teams host adapter + host-detection seam)
// -----------------------------------------------------------------------------
// This entry point branches on detectTeamsHost() BEFORE choosing a bootstrap path. The SAME <App />
// tree (the shared collaboration core - WorkspaceHomePage, ProjectPage, DocumentLibrary, ...) renders
// under EITHER host; only the bootstrap in this file + host/TeamsHostAdapter.ts differs (design.md D1
// - shared core + thin host adapter). Detection is a bounded-timeout race (see
// TeamsHostAdapter.detectTeamsHost) so it NEVER blocks the standalone browser boot path -
// bootstrapStandalone() below is the app's pre-task-012 behavior, unchanged, just extracted into its
// own function so main() can choose between the two (FR-15: no CIAM regression).

function getRootElement(): HTMLElement | null {
  const rootElement = document.getElementById('root');
  if (!rootElement) {
    console.error('[SecureProjectWorkspace] Root element not found');
  }
  return rootElement;
}

/**
 * Standalone browser bootstrap (task 013 — dual-plane home-realm discovery).
 *
 * A browser has no host broker to select the identity plane silently (unlike Teams), so the SAME URL
 * serves both planes via an explicit "My organization / Partner" chooser (spec FR-03, ADR-028 A3).
 * The dev-mock path is preserved unchanged (CIAM instance, no chooser, no MSAL init — localhost has
 * no registered redirect URI). The live path renders <StandaloneBootstrap>, which drives: read stored
 * realm → (chooser if none) → resolve the plane's authority → init MSAL → mount the SAME <App>.
 *
 * CIAM behavior is unchanged beyond the added pre-auth chooser (FR-15/NFR-05): same authority, same
 * per-tab sessionStorage cache, same broker-only acquisition — `applyStandalonePlane('ciam')` sets
 * the acquirer/scope to values identical to the module defaults.
 */
async function bootstrapStandalone(root: Root): Promise<void> {
  if (import.meta.env.VITE_DEV_MOCK === 'true') {
    // Dev mock: existing behavior — CIAM instance, no chooser, no MSAL initialize (AuthGuard
    // short-circuits in mock mode). Kept as a separate branch so StandaloneBootstrap's hooks stay
    // unconditional (rules of hooks). The configured CIAM backend is activated when it resolves to exactly one
    // (task 041), so pages that read the active backend do not throw; otherwise mock mode runs without one.
    const selection = await resolveBackendSelection('ciam');
    if (selection.kind === 'selected') activateBackend('ciam', selection.backend);
    root.render(
      <React.StrictMode>
        <MsalProvider instance={msalInstance}>
          <App teamsHost={false} />
        </MsalProvider>
      </React.StrictMode>
    );
    return;
  }

  root.render(
    <React.StrictMode>
      <StandaloneBootstrap />
    </React.StrictMode>
  );
}

/** Centered, semantic-token-only container for the pre-auth bootstrap screens (chooser/spinner/error). */
const bootstrapCenterStyle: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  height: '100dvh',
  rowGap: tokens.spacingVerticalM,
  padding: tokens.spacingVerticalXXL,
  textAlign: 'center',
  backgroundColor: tokens.colorNeutralBackground1,
  color: tokens.colorNeutralForeground1,
};

/**
 * Live browser bootstrap component: home-realm discovery → per-context authority → MSAL init → App.
 *
 * State machine (all pre-auth screens are Fluent v9 semantic tokens only — ADR-021):
 *   - no stored realm            → <RealmChooser> ("My organization" | "Partner")
 *   - realm chosen               → run-time backend selection for that plane (task 041, FR-17): one backend (or a
 *                                  valid `?customer=` / stored key) is activated; several and none active →
 *                                  <CustomerPicker>; none → fail closed (the error screen below; no request is made)
 *   - realm chosen, initializing → spinner
 *   - realm chosen, init failed  → fail-loud error + "choose a different sign-in" (criterion 4)
 *   - plane ready                → <MsalProvider instance={plane}><App/></MsalProvider>
 *
 * The chosen realm persists per-tab (sessionStorage) so it survives MSAL's login/token redirect; on
 * reload main() re-runs and this component re-resolves the same plane deterministically.
 */
const StandaloneBootstrap: React.FC = () => {
  const [realm, setRealm] = React.useState<Realm | null>(() => getStoredRealm());
  const [plane, setPlane] = React.useState<StandalonePlane | null>(null);
  const [error, setError] = React.useState<unknown>(null);
  // Customer picker options (several backends known, none active), and the key the user picked from them.
  const [pickOptions, setPickOptions] = React.useState<Backend[] | null>(null);
  const [pickedKey, setPickedKey] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!realm) return;
    let cancelled = false;
    void (async () => {
      try {
        if (!pickedKey) {
          // Backend first (task 041): the plane's scope and every request URL come from the active backend.
          const selection = await resolveBackendSelection(realm);
          if (cancelled) return;
          if (selection.kind === 'pick') {
            setPickOptions(selection.options);
            return;
          }
          if (selection.kind === 'none') throw new Error(selection.reason);
          activateBackend(realm, selection.backend);
        }
        const resolved = resolveStandalonePlane(realm);
        applyStandalonePlane(resolved);
        // MSAL requires explicit initialize() before any token op / render; also processes the
        // auth-code redirect response when returning from the IdP.
        await resolved.instance.initialize();
        if (!cancelled) setPlane(resolved);
      } catch (err) {
        if (!cancelled) setError(err);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [realm, pickedKey]);

  const handleChoose = React.useCallback((chosen: Realm) => {
    setStoredRealm(chosen);
    setRealm(chosen);
  }, []);

  const handlePickCustomer = React.useCallback(
    (key: string) => {
      if (!realm || !pickOptions) return;
      try {
        chooseBackend(realm, pickOptions, key);
        setPickOptions(null);
        setPickedKey(key);
      } catch (err) {
        setError(err);
      }
    },
    [realm, pickOptions]
  );

  const handleResetRealm = React.useCallback(() => {
    clearStoredRealm();
    clearActiveBackend();
    setError(null);
    setPlane(null);
    setPickOptions(null);
    setPickedKey(null);
    setRealm(null);
  }, []);

  // No plane chosen yet → the explicit home-realm chooser (FR-03).
  if (!realm) {
    return (
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <RealmChooser onChoose={handleChoose} />
      </FluentProvider>
    );
  }

  // Plane resolution / MSAL init failed (e.g. workforce env not wired) → fail loud, offer recovery.
  if (error) {
    const message = error instanceof Error ? error.message : 'Sign-in could not be prepared.';
    return (
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <div style={bootstrapCenterStyle}>
          <Text size={500} weight="semibold">
            Sign-in unavailable
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground3, maxWidth: '32rem' }}>
            {message}
          </Text>
          <Button appearance="primary" onClick={handleResetRealm}>
            Choose a different sign-in
          </Button>
        </div>
      </FluentProvider>
    );
  }

  // Several customer backends are known and none is active → the customer picker (FR-17).
  if (pickOptions) {
    return (
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <CustomerPicker options={pickOptions} onChoose={handlePickCustomer} />
      </FluentProvider>
    );
  }

  // Plane chosen, authority resolving / MSAL initializing.
  if (!plane) {
    return (
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <div style={bootstrapCenterStyle}>
          <Spinner size="large" label="Preparing sign-in..." />
        </div>
      </FluentProvider>
    );
  }

  // Plane ready → mount the SAME shared <App> under the selected plane's MSAL instance. App supplies
  // its own FluentProvider; AuthGuard drives the actual sign-in redirect against the plane's scope.
  return (
    <MsalProvider instance={plane.instance}>
      <App teamsHost={false} />
    </MsalProvider>
  );
};

/**
 * Teams tab bootstrap. Delegates ALL Teams-specific concerns (app.initialize, context, theme,
 * deep-link, auth-strategy selection) to the host adapter - this function only sequences
 * "initialize, then render the SAME App" and handles the fail-loud error path.
 */
async function bootstrapTeams(root: Root): Promise<void> {
  // Backend first (task 041, FR-17): the Teams token scope is the active workforce backend's.
  const selection = await resolveBackendSelection('workforce');
  if (selection.kind === 'none') {
    renderBackendUnavailable(root, selection.reason);
    return;
  }
  if (selection.kind === 'pick') {
    renderTeamsCustomerPicker(root, 'workforce', selection.options);
    return;
  }
  activateBackend('workforce', selection.backend);
  await bootstrapTeamsAuth(root);
}

/** Teams: the customer picker (several workforce backends, none active); the choice continues the bootstrap. */
function renderTeamsCustomerPicker(root: Root, plane: BackendPlane, options: Backend[]): void {
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <CustomerPicker
          options={options}
          onChoose={key => {
            try {
              chooseBackend(plane, options, key);
            } catch (err) {
              renderBackendUnavailable(root, err instanceof Error ? err.message : String(err));
              return;
            }
            void bootstrapTeamsAuth(root);
          }}
        />
      </FluentProvider>
    </React.StrictMode>
  );
}

/** Teams: no backend could be resolved — fail closed with an explicit screen (no token is requested). */
function renderBackendUnavailable(root: Root, reason: string): void {
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <div style={bootstrapCenterStyle}>
          <Text size={500} weight="semibold">
            No organization available
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground3, maxWidth: '32rem' }}>
            {reason}
          </Text>
        </div>
      </FluentProvider>
    </React.StrictMode>
  );
}

/** Teams auth bootstrap once a backend is active: adapter initialize (NAA, then popup) → the SAME App. */
async function bootstrapTeamsAuth(root: Root): Promise<void> {
  const adapter = createTeamsHostAdapter();

  try {
    await adapter.initialize();
  } catch (err) {
    console.error('[SecureProjectWorkspace] Teams workforce authentication failed:', err);
    renderTeamsAuthError(root, adapter, err);
    return;
  }

  renderTeamsApp(root, adapter);
}

/** Render the SAME shared <App> under the Teams-signed-in MSAL instance (after bootstrap or after recovery). */
function renderTeamsApp(root: Root, adapter: ITeamsHostAdapter): void {
  root.render(
    <React.StrictMode>
      <MsalProvider instance={adapter.getMsalInstance()}>
        <App teamsHost={true} />
      </MsalProvider>
    </React.StrictMode>
  );
}

/**
 * Fail-loud error state for a Teams bootstrap failure (acceptance criterion 4 - never silently
 * degrade to a blank/broken tab). The screen (components/auth/TeamsAuthError) has a "Sign in" button that
 * runs the MSAL popup on click - a user gesture, which the bootstrap popup fallback does not have - and, on
 * success, continues into the app without a reload.
 */
function renderTeamsAuthError(root: Root, adapter: ITeamsHostAdapter, err: unknown): void {
  const message = err instanceof Error ? err.message : 'Sign-in to the Spaarke collaboration tab failed.';
  const diagnostics = extractAuthDiagnostics(err);
  // The adapter writes the Teams-provided theme to localStorage BEFORE the auth step that can
  // fail (see TeamsHostAdapter.wireTheme), so resolveCodePageTheme() already reflects the correct
  // Teams theme here even on an auth failure - this error screen is never a jarring light-in-dark
  // mismatch (ADR-021).
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <TeamsAuthError
          message={message}
          diagnostics={diagnostics}
          onSignIn={async () => {
            await adapter.signInWithPopup();
            renderTeamsApp(root, adapter);
          }}
        />
      </FluentProvider>
    </React.StrictMode>
  );
}

async function main() {
  const rootElement = getRootElement();
  if (!rootElement) return;
  const root = createRoot(rootElement);

  const inTeams = await detectTeamsHost();
  if (inTeams) {
    await bootstrapTeams(root);
  } else {
    await bootstrapStandalone(root);
  }
}

void main();
