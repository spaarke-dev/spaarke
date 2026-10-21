// Bootstrap wiring of src/main.tsx for run-time backend selection (spaarke-SPA-external-access-platform-r3 task 041,
// FR-17; review-fix round): the standalone browser bootstrap, the Teams bootstrap's pre-auth screens and the dev-mock
// branch. The real main.tsx runs (it starts itself on import) with the real selection module, picker, plane and host
// adapter; the shared <App> tree is replaced by a marker, MSAL by a passthrough and teams-js by a recording fake, so
// the assertions are about what the bootstrap renders, activates and tells the Teams host.
import * as React from 'react';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, screen } from '@testing-library/react';

const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};
const FABRIKAM = {
  customerKey: 'fabrikam',
  displayName: 'Fabrikam',
  bffBaseUrl: 'https://fabrikam-bff.test',
  bffAppId: 'bbbbbbbb-0000-0000-0000-00000000000b',
};
const REALM_KEY = 'spaarke.ext.realm';
// Task 075: the Teams bootstrap picks the backend by the Teams context tenant id, so the Teams entries list it.
const TEAMS_TID = 'c0c0c0c0-0000-4000-8000-000000000001';
const TEAMS_CONTOSO = { ...CONTOSO, homeTenantIds: [TEAMS_TID] };
const TEAMS_FABRIKAM = { ...FABRIKAM, homeTenantIds: [TEAMS_TID] };

const h = vi.hoisted(() => ({
  teamsInitialize: vi.fn(),
  getContext: vi.fn(),
  notifyAppLoaded: vi.fn(),
  notifyFailure: vi.fn(),
  // Every active-account callback the bootstrap registers on a fake PublicClientApplication.
  addEventCallback: vi.fn(),
}));

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: h.teamsInitialize,
    getContext: h.getContext,
    notifyAppLoaded: h.notifyAppLoaded,
    notifySuccess: vi.fn(),
    notifyFailure: h.notifyFailure,
    registerOnThemeChangeHandler: vi.fn(),
    FailedReason: { AuthFailed: 'AuthFailed', Timeout: 'Timeout', Other: 'Other' },
  },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => true) },
  authentication: { getAuthToken: vi.fn() },
}));

// MSAL: the CIAM singleton only needs initialize(); the provider is a passthrough (the App stub does not use MSAL).
vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    initialize = vi.fn(async () => undefined);
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

// The shared App tree: a marker that records which host it was mounted for.
vi.mock('../src/App', () => ({
  App: ({ teamsHost }: { teamsHost?: boolean }) =>
    React.createElement('div', { 'data-testid': 'app', 'data-teams': String(!!teamsHost) }, 'app mounted'),
}));

/** Start main.tsx fresh (it boots itself on import) and return the SAME module registry's selection module. */
async function boot() {
  vi.resetModules();
  document.body.innerHTML = '<div id="root"></div>';
  await import('../src/main');
  return import('../src/auth/backend-selection');
}

const SLOW = { timeout: 60000 };

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  h.teamsInitialize.mockReset();
  h.notifyAppLoaded.mockReset();
  h.notifyFailure.mockReset();
  h.addEventCallback.mockReset();
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  // Default: not inside Teams (the detection handshake is rejected immediately).
  h.teamsInitialize.mockRejectedValue(new Error('not in Teams'));
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe('Standalone browser bootstrap (main.tsx)', () => {
  beforeEach(() => {
    sessionStorage.setItem(REALM_KEY, 'ciam');
  });

  it(
    'two configured backends: the customer picker appears; choosing one activates it and mounts the app',
    SLOW,
    async () => {
      vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO, FABRIKAM]));
      const selection = await boot();

      expect(await screen.findByText('Choose an organization')).toBeTruthy();
      expect(screen.queryByTestId('app')).toBeNull();
      expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);

      fireEvent.click(screen.getByRole('button', { name: 'Fabrikam' }));

      const app = await screen.findByTestId('app');
      expect(app.getAttribute('data-teams')).toBe('false');
      expect(screen.queryByText('Choose an organization')).toBeNull();
      expect(selection.getActiveBackend()).toMatchObject({
        plane: 'ciam',
        customerKey: 'fabrikam',
        bffBaseUrl: FABRIKAM.bffBaseUrl,
      });
    }
  );

  it('one configured backend: no picker, the backend is activated and the app mounts', SLOW, async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO]));
    const selection = await boot();

    expect(await screen.findByTestId('app')).toBeTruthy();
    expect(screen.queryByText('Choose an organization')).toBeNull();
    expect(selection.getActiveBackend().customerKey).toBe('contoso');
  });

  it('registers the active-account callback on the plane instance before the app mounts', SLOW, async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO]));
    await boot();

    expect(await screen.findByTestId('app')).toBeTruthy();
    expect(h.addEventCallback).toHaveBeenCalledTimes(1);
    expect(typeof h.addEventCallback.mock.calls[0][0]).toBe('function');
  });

  it('no configured backend: fails closed with the sign-in unavailable screen (no app, no backend)', SLOW, async () => {
    vi.stubEnv('VITE_BFF_API_URL', '');
    const selection = await boot();

    expect(await screen.findByText('Sign-in unavailable')).toBeTruthy();
    expect(screen.getByText(/No backend is configured/)).toBeTruthy();
    expect(screen.queryByTestId('app')).toBeNull();
    expect(screen.queryByText('Choose an organization')).toBeNull();
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
  });
});

describe('Dev-mock bootstrap (main.tsx)', () => {
  it(
    'several configured backends: the first is activated, so pages that read the active backend do not throw',
    SLOW,
    async () => {
      vi.stubEnv('VITE_DEV_MOCK', 'true');
      vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO, FABRIKAM]));
      const selection = await boot();

      expect(await screen.findByTestId('app')).toBeTruthy();
      expect(selection.getActiveBackend()).toMatchObject({ plane: 'ciam', customerKey: 'contoso' });
    }
  );
});

describe('Teams bootstrap pre-auth screens (main.tsx)', () => {
  beforeEach(() => {
    // Inside Teams: detectTeamsHost()'s app.initialize() resolves.
    h.teamsInitialize.mockReset();
    h.teamsInitialize.mockResolvedValue(undefined);
    h.getContext.mockReset();
    h.getContext.mockResolvedValue({ app: { theme: 'default' }, user: { tenant: { id: TEAMS_TID } } });
  });

  it(
    'several entries list the Teams tenant: the picker is shown and Teams is told the app loaded (no second initialize)',
    SLOW,
    async () => {
      vi.stubEnv('VITE_BACKENDS', JSON.stringify([TEAMS_CONTOSO, TEAMS_FABRIKAM]));
      await boot();

      expect(await screen.findByText('Choose an organization')).toBeTruthy();
      expect(h.notifyAppLoaded).toHaveBeenCalledTimes(1);
      expect(h.notifyFailure).not.toHaveBeenCalled();
      // app.initialize() ran once, in detectTeamsHost(); the screens do not initialize again.
      expect(h.teamsInitialize).toHaveBeenCalledTimes(1);
    }
  );

  it('no workforce backend: the explicit screen is shown and Teams is told loaded + failed (Other)', SLOW, async () => {
    vi.stubEnv('VITE_BFF_API_URL', '');
    const selection = await boot();

    expect(await screen.findByText('No organization available')).toBeTruthy();
    expect(h.notifyAppLoaded).toHaveBeenCalledTimes(1);
    expect(h.notifyFailure).toHaveBeenCalledTimes(1);
    expect(h.notifyFailure).toHaveBeenCalledWith({
      reason: 'Other',
      message: expect.stringMatching(/No backend is configured/),
    });
    expect(h.teamsInitialize).toHaveBeenCalledTimes(1);
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
  });

  it('a host error while signalling never stops the screen from rendering', SLOW, async () => {
    vi.stubEnv('VITE_BFF_API_URL', '');
    h.notifyAppLoaded.mockImplementation(() => {
      throw new Error('host gone');
    });
    h.notifyFailure.mockImplementation(() => {
      throw new Error('host gone');
    });
    await boot();

    expect(await screen.findByText('No organization available')).toBeTruthy();
  });
});
