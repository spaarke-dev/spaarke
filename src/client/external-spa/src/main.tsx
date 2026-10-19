import * as React from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MsalProvider } from '@azure/msal-react';
import type { IPublicClientApplication } from '@azure/msal-browser';
import { Button, FluentProvider, Spinner, Text, tokens } from '@fluentui/react-components';
import { resolveCodePageTheme } from '@spaarke/ui-components/utils/themeStorage';
import { msalInstance } from './auth/msal-config';
import { extractAuthDiagnostics } from './auth/auth-diagnostics';
import {
  detectTeamsHost,
  createTeamsHostAdapter,
  notifyTeamsAppLoaded,
  notifyTeamsFailureOther,
  readTeamsPreAuthContext,
  type ITeamsHostAdapter,
} from './host/TeamsHostAdapter';
import { getStoredRealm, setStoredRealm, clearStoredRealm, type Realm } from './auth/realm';
import { syncActiveAccountOnInteractiveSignIn, TeamsGuestAccountError } from './auth/msal-auth';
import {
  resolveStandalonePlane,
  applyStandalonePlane,
  clearStaleTeamsInteractionLock,
  pickedKeyListsTenant,
  prepareWorkforceHomePlane,
  type StandalonePlane,
} from './auth/standalone-plane';
import {
  activateBackend,
  chooseBackend,
  clearActiveBackend,
  landingCustomerName,
  resolveBackendSelection,
  resolveHomeBackendSelection,
  type Backend,
} from './auth/backend-selection';
import { RealmChooser } from './components/auth/RealmChooser';
import { CustomerPicker } from './components/auth/CustomerPicker';
import { TeamsAuthError } from './components/auth/TeamsAuthError';
import { TeamsGuestAccount, TEAMS_GUEST_HEADING } from './components/auth/TeamsGuestAccount';
import { OrganizationNotSetUp } from './components/auth/OrganizationNotSetUp';
import { App } from './App';
import { isDevMockEnabled } from './dev-mock';
import { captureInviteCode, clearInviteCode } from './auth/invite-code';

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
  if (isDevMockEnabled()) {
    // Dev mock: existing behavior — CIAM instance, no chooser, no MSAL initialize (AuthGuard
    // short-circuits in mock mode). Kept as a separate branch so StandaloneBootstrap's hooks stay
    // unconditional (rules of hooks). The configured CIAM backend is activated when it resolves to exactly one
    // (task 041), so pages that read the active backend do not throw; with several configured backends the first is
    // activated (mock mode has no picker); with none, mock mode runs without one.
    const selection = await resolveBackendSelection('ciam');
    if (selection.kind === 'selected') activateBackend('ciam', selection.backend);
    else if (selection.kind === 'pick') activateBackend('ciam', selection.options[0]);
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
 *   - no stored realm            → <RealmChooser> ("Partner" | "company account")
 *   - realm chosen (Partner)     → run-time backend selection for the CIAM plane (task 041, FR-17): one backend (or a
 *                                  valid `?customer=` / stored key) is activated; several and none active →
 *                                  <CustomerPicker>; none → fail closed (the error screen below; no request is made)
 *   - realm chosen (company)     → the workforce HOME plane (ADR-028 A9, task 058): sign in to the person's own directory
 *                                  first, then the signed-in tenant id picks the backend. No account yet → the app mounts
 *                                  so AuthGuard starts the sign-in; a tenant no directory entry lists →
 *                                  <OrganizationNotSetUp> (no token request, no BFF call); several entries → <CustomerPicker>
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
  // The signed-in company directory is listed by no customer (A9): the sign-in instance, kept for "Sign out".
  const [notSetUp, setNotSetUp] = React.useState<IPublicClientApplication | null>(null);
  // "Work for {customer}?" on the landing, for a `?customer={key}` link that names a configured customer.
  const customerName = React.useMemo(() => landingCustomerName(), []);

  React.useEffect(() => {
    if (!realm) return;
    let cancelled = false;
    void (async () => {
      try {
        if (realm === 'workforce') {
          // The company-account sign-in (A9): the plane depends on the signed-in tenant, so it is prepared as one step.
          const prepared = await prepareWorkforceHomePlane({ pickedKey: pickedKey ?? undefined });
          if (cancelled) return;
          if (prepared.kind === 'pick') {
            setPickOptions(prepared.options);
            return;
          }
          if (prepared.kind === 'notSetUp') {
            setNotSetUp(prepared.instance);
            return;
          }
          applyStandalonePlane(prepared.plane);
          setPlane(prepared.plane);
          return;
        }
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
        // Before initialize(): the redirect response is handled there, and the account it signs in must become the
        // active one (so "Sign in again" with a different identity switches the account the SPA acquires tokens for).
        syncActiveAccountOnInteractiveSignIn(resolved.instance);
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
    clearInviteCode(); // task 185
    clearActiveBackend();
    setError(null);
    setPlane(null);
    setPickOptions(null);
    setPickedKey(null);
    setNotSetUp(null);
    setRealm(null);
  }, []);

  // No plane chosen yet → the explicit home-realm chooser (FR-03).
  if (!realm) {
    return (
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <RealmChooser onChoose={handleChoose} customerName={customerName} />
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

  // The company directory is listed by no customer (A9): nothing was requested, nothing is active.
  if (notSetUp) {
    return (
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <OrganizationNotSetUp instance={notSetUp} onChooseDifferent={handleResetRealm} />
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
 * Teams tab bootstrap (ADR-028 A9 Teams amendment; task 075). Delegates ALL Teams-specific concerns (app.initialize,
 * context, theme, deep-link, auth-strategy selection) to the host adapter - this function only sequences "read the Teams
 * context tenant, find it in the directory, initialize, then render the SAME App" and handles the fail-loud paths.
 *
 * The backend is chosen by the Teams context tenant id (`user.tenant.id`) BEFORE anything is requested: a missing or
 * invalid tid, or one no directory entry lists, shows the Teams "not set up" screen with zero token requests and zero BFF
 * calls (the same guarantee as the browser home plane).
 */
async function bootstrapTeams(root: Root): Promise<void> {
  // Teams never redirects, so an interaction lock found at page load is a popup the iframe reload interrupted: remove it
  // before any MSAL instance is used, or every popup (guest link, BFF fallback, "Sign in") fails until Teams restarts.
  clearStaleTeamsInteractionLock();
  const { tenantId } = await readTeamsPreAuthContext();
  if (!tenantId) {
    renderTeamsNotSetUp(root);
    return;
  }
  const selection = await resolveHomeBackendSelection(tenantId);
  if (selection.kind === 'none') {
    renderBackendUnavailable(root, selection.reason);
    return;
  }
  if (selection.kind === 'notSetUp') {
    renderTeamsNotSetUp(root);
    return;
  }
  if (selection.kind === 'pick') {
    renderTeamsCustomerPicker(root, tenantId, selection.options);
    return;
  }
  activateBackend('workforce', selection.backend);
  await bootstrapTeamsAuth(root, tenantId);
}

/** Teams: the customer picker (several entries list the tenant, none active); the choice continues the bootstrap. */
function renderTeamsCustomerPicker(root: Root, tenantId: string, options: Backend[]): void {
  // The host's spinner stops once there is a screen to show (app.initialize() already ran in detectTeamsHost()).
  notifyTeamsAppLoaded();
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <CustomerPicker
          options={options}
          onChoose={key => {
            void (async () => {
              try {
                chooseBackend('workforce', options, key);
                // Re-check the picked key against the tenant (as the browser does): a key that does not list it never
                // reaches a token request.
                if (!(await pickedKeyListsTenant(tenantId, key))) {
                  clearActiveBackend();
                  renderTeamsNotSetUp(root);
                  return;
                }
              } catch (err) {
                renderBackendUnavailable(root, err instanceof Error ? err.message : String(err));
                return;
              }
              await bootstrapTeamsAuth(root, tenantId);
            })();
          }}
        />
      </FluentProvider>
    </React.StrictMode>
  );
}

/** Teams: the tenant is not set up - missing, invalid or listed by no customer. A message only; nothing was requested. */
function renderTeamsNotSetUp(root: Root): void {
  // Same signals as a failed start (loaded: a screen is showing; failed: Other), so Teams stops its spinner and records it.
  notifyTeamsAppLoaded();
  notifyTeamsFailureOther('This organization is not set up for Spaarke.');
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <OrganizationNotSetUp variant="teams" />
      </FluentProvider>
    </React.StrictMode>
  );
}

/** Teams: no backend could be resolved — fail closed with an explicit screen (no token is requested). */
function renderBackendUnavailable(root: Root, reason: string): void {
  // Same signals as a failed start: loaded (a screen is showing) and failed (Other), so Teams stops its spinner and
  // records the failure instead of waiting on a tab that will never authenticate.
  notifyTeamsAppLoaded();
  notifyTeamsFailureOther(reason);
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
async function bootstrapTeamsAuth(root: Root, tenantId: string): Promise<void> {
  const adapter = createTeamsHostAdapter(tenantId);

  try {
    await adapter.initialize();
  } catch (err) {
    if (err instanceof TeamsGuestAccountError) {
      renderTeamsGuestAccount(root);
      return;
    }
    console.error('[SecureProjectWorkspace] Teams workforce authentication failed:', err);
    renderTeamsAuthError(root, adapter, err);
    return;
  }

  renderTeamsApp(root, adapter);
}

/**
 * Teams: the signed-in account is a B2B guest of the Teams tenant (found right after the token was acquired, in bootstrap or
 * in recovery). A message only, and NO BFF call: the customer BFF would refuse the guest token (401) and a reload would loop.
 */
function renderTeamsGuestAccount(root: Root): void {
  // Same signals as the not-set-up screen: loaded (a screen is showing) and failed (Other).
  notifyTeamsAppLoaded();
  notifyTeamsFailureOther(TEAMS_GUEST_HEADING);
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <TeamsGuestAccount />
      </FluentProvider>
    </React.StrictMode>
  );
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
            try {
              await adapter.signInWithPopup();
            } catch (signInError) {
              // A guest of the Teams tenant: another sign-in loops into the same 401, so the guest screen replaces this one.
              if (signInError instanceof TeamsGuestAccountError) {
                renderTeamsGuestAccount(root);
                return;
              }
              throw signInError;
            }
            renderTeamsApp(root, adapter);
          }}
        />
      </FluentProvider>
    </React.StrictMode>
  );
}

async function main() {
  // Task 185: FIRST, before any MSAL work — store the invite link's code and strip it from the URL (auth/invite-code.ts).
  captureInviteCode();
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
