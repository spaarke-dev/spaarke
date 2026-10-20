// No mock identity or mock data outside dev mode, and no default user on a 401 / 403 (spaarke-SPA-external-access-platform-r3
// task 042, FR-18, #1566). `import.meta.env.DEV` is stubbed to `false` to stand for a built bundle (every `vite build`
// replaces it with the literal false); the mock flag is stubbed on to prove it no longer opens mock mode there.
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

const h = vi.hoisted(() => ({ loginRedirect: vi.fn(async () => undefined) }));

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: vi.fn(),
    getContext: vi.fn(),
    notifyAppLoaded: vi.fn(),
    notifySuccess: vi.fn(),
    notifyFailure: vi.fn(),
  },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => false) },
  authentication: { getAuthToken: vi.fn() },
}));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: { loginRedirect: h.loginRedirect, logoutRedirect: vi.fn() },
    accounts: [],
    inProgress: 'none',
  }),
}));

const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};
const MOCK_IDENTITY = /Jane Smith|externalfirm|\(Mock\)|Sam Rivera|Dana Okafor|Alex Reyes/;
const SLOW = { timeout: 60000 };

let fetchUrls: string[];
let nextStatus: number;
let networkFault: boolean;
let refusalBody: string;

beforeEach(() => {
  fetchUrls = [];
  nextStatus = 200;
  networkFault = false;
  refusalBody = 'refused';
  h.loginRedirect.mockClear();
  localStorage.clear();
  sessionStorage.clear();
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      fetchUrls.push(url);
      if (networkFault) throw new TypeError('Failed to fetch');
      return new Response(
        nextStatus < 300
          ? JSON.stringify({ displayName: 'Live User', email: 'live@x.test', plane: 'ciam', entitlements: [] })
          : refusalBody,
        { status: nextStatus, headers: { 'Content-Type': 'application/json' } }
      );
    })
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

/** A fresh module graph with an active CIAM backend and a fake acquirer. */
async function load() {
  vi.resetModules();
  const selection = await import('../src/auth/backend-selection');
  const msalAuth = await import('../src/auth/msal-auth');
  msalAuth.setActiveBffTokenAcquirer(async () => 'token');
  selection.activateBackend('ciam', { ...CONTOSO });
  return { selection, msalAuth };
}

/** Stand-in for a built bundle that was given the mock flag. */
function builtBundleWithMockFlag() {
  vi.stubEnv('DEV', false);
  vi.stubEnv('VITE_DEV_MOCK', 'true');
}

describe('the mock switch (src/dev-mock.ts)', () => {
  it('is on only in dev mode with the flag', async () => {
    const { isDevMockEnabled } = await import('../src/dev-mock');
    vi.stubEnv('VITE_DEV_MOCK', 'true');
    expect(isDevMockEnabled()).toBe(true);
    vi.stubEnv('DEV', false);
    expect(isDevMockEnabled()).toBe(false);
    vi.stubEnv('DEV', true);
    vi.stubEnv('VITE_DEV_MOCK', 'false');
    expect(isDevMockEnabled()).toBe(false);
  });

  it('honours the exact value "true" only - the rule vite.config.ts refuses a build on', async () => {
    const { isDevMockEnabled } = await import('../src/dev-mock');
    for (const value of ['TRUE', 'True', ' true', '1', 'yes', '']) {
      vi.stubEnv('VITE_DEV_MOCK', value);
      expect(isDevMockEnabled(), JSON.stringify(value)).toBe(false);
    }
  });
});

describe('a built bundle given VITE_DEV_MOCK=true has no mock path', () => {
  it(
    'bffApiCall goes to the active backend (no canned mock response)',
    async () => {
      builtBundleWithMockFlag();
      await load();
      const { bffApiCall } = await import('../src/auth/bff-client');
      const body = await bffApiCall<{ displayName: string }>('/api/v1/external/me');
      expect(fetchUrls).toEqual(['https://contoso-bff.test/api/v1/external/me']);
      expect(JSON.stringify(body)).not.toMatch(MOCK_IDENTITY);
    },
    SLOW
  );

  it(
    'the App header shows no mock user (no MSAL account → no user at all)',
    async () => {
      builtBundleWithMockFlag();
      await load();
      vi.doMock('../src/pages/WorkspaceHomePage', () => ({ WorkspaceHomePage: () => null }));
      try {
        const { App } = await import('../src/App');
        // The browser host: the header (and its account button / welcome band) renders; in Teams it is hidden.
        render(React.createElement(App, { teamsHost: false }));
        expect(await screen.findByText(/^v/)).toBeTruthy(); // the footer: the app rendered
        expect(document.body.textContent).not.toMatch(MOCK_IDENTITY);
      } finally {
        vi.doUnmock('../src/pages/WorkspaceHomePage');
      }
    },
    SLOW
  );

  it(
    'the /me client calls the live endpoint (no dev persona)',
    async () => {
      builtBundleWithMockFlag();
      await load();
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      const me = await fetchMeEntitlements(false);
      expect(fetchUrls).toEqual(['https://contoso-bff.test/api/v1/external/me/entitlements']);
      expect(me.displayName).toBe('Live User');
    },
    SLOW
  );
});

describe('/me refusals are not defaulted to a stand-in user', () => {
  it.each([
    [401, 'signin'],
    [403, 'denied'],
  ] as const)(
    '%s → fetchMeEntitlements rejects with that status',
    async status => {
      await load();
      nextStatus = status;
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      await expect(fetchMeEntitlements(false)).rejects.toMatchObject({ statusCode: status });
    },
    SLOW
  );

  it(
    'any other failure falls back to the plane defaults with NO identity',
    async () => {
      await load();
      nextStatus = 503;
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      const me = await fetchMeEntitlements(true);
      warn.mockRestore();
      expect(me).toEqual({
        displayName: '',
        email: '',
        plane: 'workforce',
        entitlements: ['legal-front-door', 'policy-library'],
      });
    },
    SLOW
  );

  it.each([502, 503, 500])(
    'the browser workforce HOME plane + a %s falls back to the WORKFORCE defaults (not the CIAM ones)',
    async status => {
      const { msalAuth } = await load();
      msalAuth.setWorkforceHomePlaneActive(true);
      nextStatus = status;
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      const me = await fetchMeEntitlements(false); // a browser: teamsHost is false
      warn.mockRestore();
      expect(me).toEqual({
        displayName: '',
        email: '',
        plane: 'workforce',
        entitlements: ['legal-front-door', 'policy-library'],
      });
    },
    SLOW
  );

  it.each([502, 503, 500])(
    'a %s (server failure) falls back to the plane defaults',
    async status => {
      await load();
      nextStatus = status;
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      const me = await fetchMeEntitlements(false);
      warn.mockRestore();
      expect(me).toEqual({ displayName: '', email: '', plane: 'ciam', entitlements: ['assigned-work'] });
    },
    SLOW
  );

  it(
    'a 4xx other than 401/403 rejects - the plane defaults are for server failures only',
    async () => {
      await load();
      nextStatus = 404;
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      await expect(fetchMeEntitlements(false)).rejects.toMatchObject({ statusCode: 404 });
    },
    SLOW
  );

  it(
    'a network fault rejects - no default workspace for a caller the BFF never answered',
    async () => {
      await load();
      networkFault = true;
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      await expect(fetchMeEntitlements(false)).rejects.toThrow('Failed to fetch');
    },
    SLOW
  );

  it.each([
    [
      'TeamsWorkforceAuthError',
      (m: typeof import('../src/auth/msal-auth')) => new m.TeamsWorkforceAuthError('popup blocked'),
    ],
    ['the redirect-in-progress Error', () => new Error('No authenticated account - redirecting to login')],
  ])(
    'a token-acquisition failure (%s) rejects with that error - the BFF is never called, no default workspace',
    async (_label, makeError) => {
      const { msalAuth } = await load();
      const error = makeError(msalAuth);
      msalAuth.setActiveBffTokenAcquirer(async () => {
        throw error;
      });
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      await expect(fetchMeEntitlements(true)).rejects.toBe(error);
      expect(fetchUrls).toEqual([]);
    },
    SLOW
  );

  it(
    'no active backend (BackendSelectionError) rejects - no default workspace',
    async () => {
      vi.resetModules();
      const msalAuth = await import('../src/auth/msal-auth');
      msalAuth.setActiveBffTokenAcquirer(async () => 'token');
      const { BackendSelectionError } = await import('../src/auth/backend-selection');
      const { fetchMeEntitlements } = await import('../src/api/me-client');
      await expect(fetchMeEntitlements(false)).rejects.toBeInstanceOf(BackendSelectionError);
      expect(fetchUrls).toEqual([]);
    },
    SLOW
  );

  it(
    'WorkspaceHomePage: a failed Teams sign-in shows the sign-in state; a network fault shows the load error - never a workspace',
    async () => {
      const { msalAuth } = await load();
      const { WorkspaceHomePage } = await import('../src/pages/WorkspaceHomePage');

      msalAuth.setActiveBffTokenAcquirer(async () => {
        throw new msalAuth.TeamsWorkforceAuthError('popup blocked');
      });
      const first = render(React.createElement(WorkspaceHomePage, { teamsHost: true }));
      expect(await screen.findByTestId('bff-access-signin')).toBeTruthy();
      first.unmount();

      msalAuth.setActiveBffTokenAcquirer(async () => 'token');
      networkFault = true;
      render(React.createElement(WorkspaceHomePage, { teamsHost: false }));
      expect(await screen.findByText('Unable to load your workspace')).toBeTruthy();
      expect(screen.queryByTestId('bff-access-signin')).toBeNull();
      expect(document.body.textContent).not.toMatch(MOCK_IDENTITY);
    },
    SLOW
  );

  it.each([
    [401, 'bff-access-signin'],
    [403, 'bff-access-denied'],
  ] as const)(
    'WorkspaceHomePage: a %s from /me shows the %s state, not a workspace',
    async (status, testId) => {
      await load();
      nextStatus = status;
      const { WorkspaceHomePage } = await import('../src/pages/WorkspaceHomePage');
      render(React.createElement(WorkspaceHomePage, { teamsHost: false }));
      expect(await screen.findByTestId(testId)).toBeTruthy();
      expect(document.body.textContent).not.toMatch(MOCK_IDENTITY);
    },
    SLOW
  );

  it.each([
    ['sdap.access.deny.contact_not_invited', true],
    ['sdap.access.deny.contact_inactive', false],
  ] as const)(
    'WorkspaceHomePage: a 403 %s shows the server guidance in the denied state: %s (task 185, owner Q9)',
    async (reasonCode, showsDetail) => {
      await load();
      nextStatus = 403;
      const detail = 'Open the invitation link from your email in this browser and sign in again.';
      refusalBody = JSON.stringify({ status: 403, title: 'Forbidden', detail, reasonCode });
      const { WorkspaceHomePage } = await import('../src/pages/WorkspaceHomePage');
      render(React.createElement(WorkspaceHomePage, { teamsHost: false }));
      const denied = await screen.findByTestId('bff-access-denied');
      expect(denied.textContent?.includes(detail)).toBe(showsDetail);
      expect(denied.textContent?.includes('You do not have access to this workspace')).toBe(!showsDetail);
    },
    SLOW
  );
});
