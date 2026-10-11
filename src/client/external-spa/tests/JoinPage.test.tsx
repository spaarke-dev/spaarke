// The /join?customer={key} page (spaarke-SPA-external-access-platform-r3 task 056; spec FR-13; Option B — decision
// notes/spikes/fr-12-self-registration-decision.md §4/§7, owner 2026-10-11).
//
// Under Option B the registration-only sign-in is SERVER-SIDE (task 060). The SPA page only validates the key through
// the task-041 seam, sends the browser to the FIXED registration service URL from build configuration, renders the
// service's return, and hands off to the normal workforce sign-in. These tests drive the real page, the real
// registration client and the real backend-selection module; the directory is a recording fake (or the real dev
// configuration), and every full-page navigation is captured by an injected `navigate` spy. The bootstrap tests run
// the real main.tsx with MSAL, teams-js and the shared App replaced (as in main-bootstrap.test.tsx).
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { JoinPage } from '../src/pages/JoinPage';
import {
  JOIN_RESULT_CODES,
  buildRegistrationStartUrl,
  createJoinState,
  getRegistrationServiceUrl,
  isJoinEnabled,
  isJoinPath,
  parseJoinReturn,
  resolveJoinCustomer,
} from '../src/auth/registration-client';
import type { Backend, BackendDirectory } from '../src/auth/backend-selection';

const SERVICE = 'https://register.spaarke.test/join/start';
const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const WORKFORCE_CLIENT = '11111111-2222-3333-4444-555555555555';
const REALM_KEY = 'spaarke.ext.realm';
const EVIL = 'https://evil.example';

const CONTOSO: Backend = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};
const FABRIKAM: Backend = {
  customerKey: 'fabrikam',
  displayName: 'Fabrikam',
  bffBaseUrl: 'https://fabrikam-bff.test',
  bffAppId: 'bbbbbbbb-0000-0000-0000-00000000000b',
};

const h = vi.hoisted(() => ({
  teamsInitialize: vi.fn(),
  notifyAppLoaded: vi.fn(),
  notifyFailure: vi.fn(),
  addEventCallback: vi.fn(),
  msalInitialize: vi.fn(),
}));

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: h.teamsInitialize,
    getContext: vi.fn(),
    notifyAppLoaded: h.notifyAppLoaded,
    notifySuccess: vi.fn(),
    notifyFailure: h.notifyFailure,
    registerOnThemeChangeHandler: vi.fn(),
    FailedReason: { AuthFailed: 'AuthFailed', Timeout: 'Timeout', Other: 'Other' },
  },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => true) },
  authentication: { getAuthToken: vi.fn() },
}));

// MSAL: every instance records initialize() (the join page must never create or initialize one).
vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    initialize = h.msalInitialize;
    getAllAccounts = () => [];
    getActiveAccount = () => null;
    setActiveAccount = () => undefined;
    addEventCallback = h.addEventCallback;
  }
  return { ...actual, PublicClientApplication: FakePublicClientApplication };
});
vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: React.ReactNode }) => children,
}));
vi.mock('../src/App', () => ({
  App: ({ teamsHost }: { teamsHost?: boolean }) =>
    React.createElement('div', { 'data-testid': 'app', 'data-teams': String(!!teamsHost) }, 'app mounted'),
}));

/** A directory that answers the key lookup from a list and records every call. */
function fakeDirectory(backends: Backend[] = [CONTOSO, FABRIKAM]) {
  const calls: string[] = [];
  const directory: BackendDirectory = {
    resolveWorkforce: async () => {
      calls.push('resolveWorkforce');
      return backends;
    },
    resolveByKey: async (key: string) => {
      calls.push(`resolveByKey:${key}`);
      return backends.find(b => b.customerKey === key) ?? null;
    },
  };
  return { directory, calls };
}

function renderJoin(
  search: string,
  opts: { teamsHost?: boolean; directory?: BackendDirectory; dark?: boolean } = {}
): { navigate: ReturnType<typeof vi.fn> } {
  const navigate = vi.fn();
  render(
    <FluentProvider theme={opts.dark ? webDarkTheme : webLightTheme}>
      <JoinPage
        teamsHost={opts.teamsHost ?? false}
        search={search}
        directory={opts.directory ?? fakeDirectory().directory}
        navigate={navigate}
      />
    </FluentProvider>
  );
  return { navigate };
}

function enableJoin(serviceUrl = SERVICE): void {
  vi.stubEnv('VITE_JOIN_ENABLED', 'true');
  vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', serviceUrl);
}

/** The start URL the page navigated to, parsed. */
function startedUrl(navigate: ReturnType<typeof vi.fn>): URL {
  expect(navigate).toHaveBeenCalledTimes(1);
  return new URL(navigate.mock.calls[0][0] as string);
}

/** Land on the intro, start registration, and return the state the page sent to the service. */
async function startRegistration(customerKey = 'contoso'): Promise<string> {
  const { navigate } = renderJoin(`?customer=${customerKey}`);
  fireEvent.click(await screen.findByRole('button', { name: 'Continue with your work account' }));
  const state = startedUrl(navigate).searchParams.get('state') as string;
  document.body.innerHTML = '';
  return state;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
  window.history.replaceState(null, '', '/');
});

// ---------------------------------------------------------------------------
// Flag and configuration
// ---------------------------------------------------------------------------

describe('Feature flag VITE_JOIN_ENABLED (default off)', () => {
  it('flag unset: /join shows "not available" and the key is never looked up', async () => {
    vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', SERVICE);
    const { directory, calls } = fakeDirectory();
    const { navigate } = renderJoin('?customer=contoso', { directory });

    expect(screen.getByText('Joining isn’t available')).toBeTruthy();
    expect(screen.queryByRole('button')).toBeNull();
    await Promise.resolve();
    expect(calls).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each(['false', 'TRUE', '1', 'yes', '', '#{JOIN_ENABLED}#'])('flag "%s" is off', value => {
    vi.stubEnv('VITE_JOIN_ENABLED', value);
    vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', SERVICE);
    expect(isJoinEnabled()).toBe(false);
    renderJoin('?customer=contoso');
    expect(screen.getByText('Joining isn’t available')).toBeTruthy();
  });

  it('flag on but no registration service URL: still "not available" (fail closed)', async () => {
    vi.stubEnv('VITE_JOIN_ENABLED', 'true');
    vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', '');
    const { directory, calls } = fakeDirectory();
    const { navigate } = renderJoin('?customer=contoso', { directory });

    expect(screen.getByText('Joining isn’t available')).toBeTruthy();
    expect(calls).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each([
    ['http (not localhost)', 'http://register.spaarke.test/start'],
    ['a query', `${SERVICE}?redirect=${EVIL}`],
    ['a fragment', `${SERVICE}#x`],
    ['credentials', 'https://user:pw@register.spaarke.test/start'],
    ['not a URL', 'register.spaarke.test'],
    ['javascript:', 'javascript:alert(1)'],
    ['a placeholder', '#{REGISTRATION_SERVICE_URL}#'],
  ])('a service URL with %s is refused', (_label, value) => {
    vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', value);
    expect(getRegistrationServiceUrl()).toBeNull();
  });

  it('accepts an https service URL, and http only on localhost', () => {
    vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', SERVICE);
    expect(getRegistrationServiceUrl()).toBe(SERVICE);
    vi.stubEnv('VITE_REGISTRATION_SERVICE_URL', 'http://localhost:7071/join/start');
    expect(getRegistrationServiceUrl()).toBe('http://localhost:7071/join/start');
  });

  it('isJoinPath matches /join and /join/ only', () => {
    expect(isJoinPath('/join')).toBe(true);
    expect(isJoinPath('/join/')).toBe(true);
    for (const p of ['/', '/joins', '/join/x', '/JOIN', '/project/join']) expect(isJoinPath(p)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Customer key (the task-041 seam)
// ---------------------------------------------------------------------------

describe('Customer key validation', () => {
  beforeEach(() => enableJoin());

  it('an unknown key shows "link not recognised" and makes no service call', async () => {
    const { directory, calls } = fakeDirectory();
    const { navigate } = renderJoin('?customer=initech', { directory });

    expect(await screen.findByText('Link not recognised')).toBeTruthy();
    expect(calls).toEqual(['resolveByKey:initech']);
    expect(screen.queryByRole('button')).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each([
    ['missing', ''],
    ['empty', '?customer='],
    ['URL-shaped', `?customer=${encodeURIComponent(EVIL)}`],
    ['path-shaped', '?customer=..%2Fadmin'],
    ['too long', `?customer=${'a'.repeat(65)}`],
  ])('a %s key shows the same neutral page without a lookup', async (_label, search) => {
    const { directory, calls } = fakeDirectory();
    const { navigate } = renderJoin(search, { directory });

    expect(await screen.findByText('Link not recognised')).toBeTruthy();
    expect(calls).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('the neutral page neither echoes the key nor lists any customer (no oracle)', async () => {
    renderJoin('?customer=initech');
    await screen.findByText('Link not recognised');
    const text = document.body.textContent ?? '';
    for (const word of ['initech', 'Contoso', 'contoso', 'Fabrikam', 'fabrikam']) expect(text).not.toContain(word);
  });

  it('a directory failure, or a record for another key, is the same neutral page', async () => {
    const failing: BackendDirectory = {
      resolveWorkforce: async () => [],
      resolveByKey: async () => {
        throw new Error('directory down');
      },
    };
    expect(await resolveJoinCustomer('?customer=contoso', failing)).toEqual({ kind: 'unknown' });

    const wrong: BackendDirectory = { resolveWorkforce: async () => [], resolveByKey: async () => FABRIKAM };
    expect(await resolveJoinCustomer('?customer=contoso', wrong)).toEqual({ kind: 'unknown' });

    const refused: BackendDirectory = {
      resolveWorkforce: async () => [],
      resolveByKey: async () => ({ ...CONTOSO, bffBaseUrl: 'http://evil.example' }),
    };
    expect(await resolveJoinCustomer('?customer=contoso', refused)).toEqual({ kind: 'unknown' });
  });

  it('with no directory injected, the real dev configuration decides (VITE_BACKENDS)', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO, FABRIKAM]));
    expect(await resolveJoinCustomer('?customer=fabrikam')).toEqual({ kind: 'known', customerKey: 'fabrikam' });
    expect(await resolveJoinCustomer('?customer=initech')).toEqual({ kind: 'unknown' });
  });

  it('with the T240c directory flag on (a stub until it ships), every key is unknown — fail closed', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO]));
    vi.stubEnv('VITE_USE_DIRECTORY_SERVICE', 'true');
    expect(await resolveJoinCustomer('?customer=contoso')).toEqual({ kind: 'unknown' });
  });

  it('a single configured backend does NOT make an unknown key valid (unlike the sign-in default)', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO]));
    expect(await resolveJoinCustomer('?customer=initech')).toEqual({ kind: 'unknown' });
  });
});

// ---------------------------------------------------------------------------
// Start: the fixed registration service URL (X2 / NFR-02)
// ---------------------------------------------------------------------------

describe('Starting registration', () => {
  beforeEach(() => enableJoin());

  it('a known key shows the intro; continuing goes to the FIXED service URL with only customer + state', async () => {
    const { navigate } = renderJoin('?customer=contoso');
    expect(await screen.findByText('Join Spaarke')).toBeTruthy();
    expect(navigate).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Continue with your work account' }));

    const url = startedUrl(navigate);
    expect(`${url.origin}${url.pathname}`).toBe(SERVICE);
    expect([...url.searchParams.keys()].sort()).toEqual(['customer', 'state']);
    expect(url.searchParams.get('customer')).toBe('contoso');
    expect(url.searchParams.get('state')).toMatch(/^[0-9a-f]{32}$/);
  });

  it('a crafted link cannot change the service host, scope or return address', async () => {
    const crafted =
      `?customer=contoso&registration=${encodeURIComponent(EVIL)}&redirect_uri=${encodeURIComponent(EVIL)}` +
      `&return=${encodeURIComponent(EVIL)}&scope=${encodeURIComponent('api://evil/user_impersonation')}` +
      `&authority=${encodeURIComponent('https://login.microsoftonline.com/common')}`;
    // The real address bar carries the crafted link too, so code that read window.location would be caught.
    window.history.replaceState(null, '', `/join${crafted}`);
    const { navigate } = renderJoin(crafted);
    fireEvent.click(await screen.findByRole('button', { name: 'Continue with your work account' }));

    const href = navigate.mock.calls[0][0] as string;
    expect(new URL(href).origin).toBe(new URL(SERVICE).origin);
    expect(href).not.toContain('evil');
    expect(href).not.toContain('common');
    expect([...new URL(href).searchParams.keys()].sort()).toEqual(['customer', 'state']);
  });

  it('the registration-only flow never requests a BFF scope (or any scope / client id) from the SPA', async () => {
    const { navigate } = renderJoin('?customer=contoso');
    fireEvent.click(await screen.findByRole('button', { name: 'Continue with your work account' }));
    const href = decodeURIComponent(navigate.mock.calls[0][0] as string);
    for (const banned of ['scope', 'api://', 'user_impersonation', CONTOSO.bffAppId, 'client_id', 'redirect_uri']) {
      expect(href).not.toContain(banned);
    }
    // No MSAL instance is created or initialised for the join flow.
    expect(h.msalInitialize).not.toHaveBeenCalled();
  });

  it('buildRegistrationStartUrl drops any query on the configured URL and refuses a malformed key', () => {
    const url = new URL(buildRegistrationStartUrl(`${SERVICE}`, 'contoso', 'f'.repeat(32)));
    expect([...url.searchParams.keys()]).toEqual(['customer', 'state']);
    expect(() => buildRegistrationStartUrl(SERVICE, EVIL, 'x')).toThrow(/malformed/);
  });
});

// ---------------------------------------------------------------------------
// Return: outcomes, refusals, state
// ---------------------------------------------------------------------------

describe('Service return (the task-060 contract)', () => {
  beforeEach(() => enableJoin());

  it('registered: the user is told and "Sign in" hands off to the workforce sign-in for that customer', async () => {
    const state = await startRegistration('contoso');
    const { navigate } = renderJoin(`?customer=contoso&state=${state}&result=registered`);

    expect(await screen.findByText('You’re registered')).toBeTruthy();
    expect(navigate).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(navigate).toHaveBeenCalledWith('/?customer=contoso');
    expect(sessionStorage.getItem(REALM_KEY)).toBe('workforce');
  });

  it('already registered: told to sign in, and handed off to sign-in', async () => {
    const state = createJoinState('contoso');
    const { navigate } = renderJoin(`?customer=contoso&state=${state}&result=already_registered`);

    expect(await screen.findByText('You’re already registered — sign in')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(navigate).toHaveBeenCalledWith('/?customer=contoso');
    expect(sessionStorage.getItem(REALM_KEY)).toBe('workforce');
  });

  it('hand-off clears a previously active customer so the link’s key is the one selected', async () => {
    sessionStorage.setItem('spaarke.ext.activeCustomer', 'fabrikam');
    sessionStorage.setItem(REALM_KEY, 'ciam');
    const state = createJoinState('contoso');
    const { navigate } = renderJoin(`?customer=contoso&state=${state}&result=registered`);
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }));

    expect(navigate).toHaveBeenCalledWith('/?customer=contoso');
    expect(sessionStorage.getItem('spaarke.ext.activeCustomer')).toBeNull();
    expect(sessionStorage.getItem(REALM_KEY)).toBe('workforce');
  });

  it.each([
    ['tenant_not_allowed', 'Your company is not set up for access'],
    ['guest_account', 'Use your own company account'],
    ['account_conflict', 'This account can’t join this way'],
  ])('refusal %s shows a clear message and offers no sign-in', async (code, heading) => {
    const state = createJoinState('contoso');
    const { navigate } = renderJoin(`?customer=contoso&state=${state}&result=${code}`);

    expect(await screen.findByText(heading)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(REALM_KEY)).toBeNull();
  });

  it('a guest of another tenant can retry with a different account (a new start, new state)', async () => {
    const state = createJoinState('contoso');
    const { navigate } = renderJoin(`?customer=contoso&state=${state}&result=guest_account`);
    fireEvent.click(await screen.findByRole('button', { name: 'Try a different account' }));
    const url = startedUrl(navigate);
    expect(`${url.origin}${url.pathname}`).toBe(SERVICE);
    expect(url.searchParams.get('state')).not.toBe(state);
  });

  it.each([
    ['consent_required', 'Your company needs to approve Spaarke'],
    ['cancelled', 'Sign-in was cancelled'],
    ['error', 'Something went wrong'],
    ['made_up_code', 'Something went wrong'],
    ['', 'Something went wrong'],
  ])('result "%s" shows its state with "Try again" (no sign-in)', async (code, heading) => {
    const state = createJoinState('contoso');
    renderJoin(`?customer=contoso&state=${state}&result=${code}`);
    expect(await screen.findByText(heading)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
  });

  it('never renders free text from the URL', async () => {
    const state = createJoinState('contoso');
    renderJoin(`?customer=contoso&state=${state}&result=error&message=${encodeURIComponent('Call +1 555 HACKER')}`);
    await screen.findByText('Something went wrong');
    expect(document.body.textContent).not.toContain('HACKER');
  });

  it('a return whose state was not started in this tab shows "start again", not the outcome', async () => {
    createJoinState('contoso');
    const { navigate } = renderJoin(`?customer=contoso&state=${'0'.repeat(32)}&result=registered`);
    expect(await screen.findByText('Let’s start again')).toBeTruthy();
    expect(screen.queryByText('You’re registered')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('a return with no state, or a state started for another customer, also shows "start again"', async () => {
    renderJoin('?customer=contoso&result=registered');
    expect(await screen.findByText('Let’s start again')).toBeTruthy();
    document.body.innerHTML = '';

    const fabrikamState = createJoinState('fabrikam');
    renderJoin(`?customer=contoso&state=${fabrikamState}&result=registered`);
    expect(await screen.findByText('Let’s start again')).toBeTruthy();
  });

  it('the state is one-time: replaying the same return link shows "start again"', async () => {
    const state = createJoinState('contoso');
    renderJoin(`?customer=contoso&state=${state}&result=registered`);
    expect(await screen.findByText('You’re registered')).toBeTruthy();
    document.body.innerHTML = '';

    renderJoin(`?customer=contoso&state=${state}&result=registered`);
    expect(await screen.findByText('Let’s start again')).toBeTruthy();
  });

  it('a return for an unknown key is the neutral page (no outcome)', async () => {
    const state = createJoinState('initech');
    renderJoin(`?customer=initech&state=${state}&result=registered`);
    expect(await screen.findByText('Link not recognised')).toBeTruthy();
  });

  it('removes result and state from the address bar after reading them', async () => {
    window.history.replaceState(null, '', '/join?customer=contoso&state=abc&result=registered');
    const state = createJoinState('contoso');
    renderJoin(`?customer=contoso&state=${state}&result=registered`);
    await screen.findByText('You’re registered');
    expect(window.location.pathname + window.location.search).toBe('/join?customer=contoso');
  });

  it('parseJoinReturn: no result → null; every documented code round-trips; unknown → error', () => {
    expect(parseJoinReturn('?customer=contoso')).toBeNull();
    for (const code of JOIN_RESULT_CODES) {
      expect(parseJoinReturn(`?result=${code}&state=s`)).toEqual({ result: code, state: 's' });
    }
    expect(parseJoinReturn('?result=registered%20')).toEqual({ result: 'error', state: null });
  });
});

// ---------------------------------------------------------------------------
// Teams (browser-only flow; NFR-04)
// ---------------------------------------------------------------------------

describe('Inside the Teams tab', () => {
  it('shows "open in your browser", looks nothing up and never navigates the iframe', async () => {
    enableJoin();
    const { directory, calls } = fakeDirectory();
    const { navigate } = renderJoin('?customer=contoso', { teamsHost: true, directory });

    expect(screen.getByText('Open this link in your browser')).toBeTruthy();
    expect(screen.queryByRole('button')).toBeNull();
    await Promise.resolve();
    expect(calls).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('with the flag off, Teams also shows "not available"', () => {
    renderJoin('?customer=contoso', { teamsHost: true });
    expect(screen.getByText('Joining isn’t available')).toBeTruthy();
  });
});

// ---------------------------------------------------------------------------
// Theming (ADR-021)
// ---------------------------------------------------------------------------

describe('Theming (ADR-021)', () => {
  it('renders under the dark theme', async () => {
    enableJoin();
    renderJoin('?customer=contoso', { dark: true });
    expect(await screen.findByText('Join Spaarke')).toBeTruthy();
  });

  it('the page and the registration client use semantic tokens only and never touch MSAL or a token', () => {
    const root = path.resolve(__dirname, '..');
    const page = fs.readFileSync(path.join(root, 'src/pages/JoinPage.tsx'), 'utf8');
    const client = fs.readFileSync(path.join(root, 'src/auth/registration-client.ts'), 'utf8');
    expect(page).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/);
    for (const src of [page, client]) {
      expect(src).not.toMatch(/from '@azure\/msal-(browser|react)'/);
      expect(src).not.toMatch(/acquireToken|loginRedirect|loginPopup|acquireActiveBffToken|bffApiCall/);
    }
  });
});

// ---------------------------------------------------------------------------
// Bootstrap (main.tsx) and hosting
// ---------------------------------------------------------------------------

/** Start main.tsx fresh at a URL (it boots itself on import); returns the same registry's selection module. */
async function bootAt(url: string) {
  vi.resetModules();
  window.history.replaceState(null, '', url);
  document.body.innerHTML = '<div id="root"></div>';
  await import('../src/main');
  return import('../src/auth/backend-selection');
}

const SLOW = { timeout: 60000 };

describe('Bootstrap: /join is a pre-sign-in page (main.tsx)', () => {
  beforeEach(() => {
    h.teamsInitialize.mockReset();
    h.notifyAppLoaded.mockReset();
    h.notifyFailure.mockReset();
    h.addEventCallback.mockReset();
    h.msalInitialize.mockReset();
    h.msalInitialize.mockResolvedValue(undefined);
    h.teamsInitialize.mockRejectedValue(new Error('not in Teams'));
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO, FABRIKAM]));
    vi.stubEnv('VITE_TEAMS_MSAL_CLIENT_ID', WORKFORCE_CLIENT);
    vi.stubEnv('VITE_TEAMS_MSAL_TENANT_ID', SPAARKE_TENANT);
  });

  it('flag on: the join page renders before the realm chooser and without any MSAL plane', SLOW, async () => {
    enableJoin();
    const selection = await bootAt('/join?customer=contoso');

    expect(await screen.findByText('Join Spaarke')).toBeTruthy();
    expect(screen.queryByText('Sign in to Spaarke')).toBeNull();
    expect(screen.queryByTestId('app')).toBeNull();
    expect(h.msalInitialize).not.toHaveBeenCalled();
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
  });

  it('flag off: /join shows "not available" (not the chooser, not a 404)', SLOW, async () => {
    await bootAt('/join?customer=contoso');

    expect(await screen.findByText('Joining isn’t available')).toBeTruthy();
    expect(screen.queryByText('Sign in to Spaarke')).toBeNull();
    expect(screen.queryByTestId('app')).toBeNull();
  });

  it('inside Teams: the browser-only message, and Teams is told the app loaded', SLOW, async () => {
    enableJoin();
    h.teamsInitialize.mockReset();
    h.teamsInitialize.mockResolvedValue(undefined);
    await bootAt('/join?customer=contoso');

    expect(await screen.findByText('Open this link in your browser')).toBeTruthy();
    expect(h.notifyAppLoaded).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('app')).toBeNull();
  });

  it('hand-off: after "Sign in", the SPA root starts the workforce sign-in for the joined customer', SLOW, async () => {
    enableJoin();
    // The join page's hand-off (registered outcome → "Sign in").
    const state = createJoinState('fabrikam');
    const { navigate } = renderJoin(`?customer=fabrikam&state=${state}&result=registered`, {
      directory: fakeDirectory().directory,
    });
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }));
    const target = navigate.mock.calls[0][0] as string;
    expect(target).toBe('/?customer=fabrikam');

    // The browser loads that address: the bootstrap skips the chooser (realm = workforce), selects the joined
    // customer among two configured backends (no picker), initialises the workforce plane and mounts the app.
    const selection = await bootAt(target);
    expect(await screen.findByTestId('app')).toBeTruthy();
    expect(screen.queryByText('Sign in to Spaarke')).toBeNull();
    expect(screen.queryByText('Choose an organization')).toBeNull();
    expect(h.msalInitialize).toHaveBeenCalledTimes(1);
    expect(selection.getActiveBackend()).toMatchObject({
      plane: 'workforce',
      customerKey: 'fabrikam',
      bffScope: `api://${FABRIKAM.bffAppId}/user_impersonation`,
    });
  });

  it('other paths are unchanged: "/" still shows the realm chooser', SLOW, async () => {
    enableJoin();
    await bootAt('/');
    expect(await screen.findByText('Sign in to Spaarke')).toBeTruthy();
  });
});

describe('Hosting', () => {
  it('the SWA navigation fallback serves /join from index.html (it is not excluded)', () => {
    const config = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../staticwebapp.config.json'), 'utf8')) as {
      navigationFallback: { rewrite: string; exclude: string[] };
    };
    expect(config.navigationFallback.rewrite).toBe('/index.html');
    expect(config.navigationFallback.exclude.some(p => p === '/join' || p.startsWith('/join'))).toBe(false);
  });

  it('the flag is not turned on in any committed env file or the deploy workflow', () => {
    const root = path.resolve(__dirname, '..');
    for (const file of ['.env.development', '.env.production', '.env.example']) {
      const text = fs.readFileSync(path.join(root, file), 'utf8');
      expect(text).not.toMatch(/^\s*VITE_JOIN_ENABLED\s*=\s*true/m);
    }
    const workflow = fs.readFileSync(path.resolve(root, '../../../.github/workflows/deploy-external-spa.yml'), 'utf8');
    expect(workflow).not.toMatch(/VITE_JOIN_ENABLED\s*:\s*['"]?true/);
  });
});
