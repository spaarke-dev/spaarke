// A background read must never start an interactive sign-in
// (spaarke-SPA-external-access-platform-r3 task 055 review fix; owner policy: a confirmed defect on a real path is fixed).
//
// The notification bell polls on a timer. When the token cannot be had silently (the CIAM refresh token lapses after 24 h,
// non-sliding), `acquireStandaloneBffToken` used to capture the return-to and call `acquireTokenRedirect`: a TIMER navigated
// the page to Entra with no user action, and a half-typed intake form or an open record was lost. `noInteraction: true` (passed
// ONLY by the poll) turns that into `SignInRequiredError` - no return-to capture, no redirect or popup, no recorded sign-in
// failure (a later user-initiated redirect must still work). Every other caller is unchanged: these tests pin both sides.
//
// MSAL and Teams JS are fakes that record what the code asks for; event names and error classes are msal-browser's real ones.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.setConfig({ testTimeout: 60000 });

const BFF_SCOPE = 'api://99999999-8888-7777-6666-555555555555/user_impersonation';
const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const RETURN_TO_KEY = 'spaarke.ext.returnTo';

const h = vi.hoisted(() => ({
  nestable: null as null | ((config: unknown) => Promise<unknown>),
  popupInstances: [] as unknown[],
  naaRecommended: vi.fn(() => true),
}));

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    constructor() {
      const instance = {
        initialize: vi.fn(async () => undefined),
        getActiveAccount: () => null,
        setActiveAccount: vi.fn(),
        getAllAccounts: () => [],
        acquireTokenSilent: vi.fn(),
        acquireTokenPopup: vi.fn(),
        acquireTokenRedirect: vi.fn(),
        ssoSilent: vi.fn(),
      };
      h.popupInstances.push(instance);
      return instance;
    }
  }
  return {
    ...actual,
    PublicClientApplication: FakePublicClientApplication,
    createNestablePublicClientApplication: async (config: unknown) => {
      if (!h.nestable) throw new Error('NAA not configured');
      return h.nestable(config);
    },
  };
});
vi.mock('@microsoft/teams-js', () => ({
  app: { initialize: vi.fn(async () => undefined) },
  nestedAppAuth: { isNAAChannelRecommended: h.naaRecommended },
}));

const account = {
  homeAccountId: 'A',
  username: 'a@test',
  tenantId: SPAARKE_TENANT,
  environment: 'login.microsoftonline.com',
  localAccountId: 'A',
};

function fakeInstance(cached: unknown[] = [account]) {
  const callbacks: Array<(event: any) => void> = [];
  return {
    getAllAccounts: vi.fn((filter?: { tenantId?: string }) =>
      filter?.tenantId ? (cached as any[]).filter(a => a.tenantId === filter.tenantId) : cached
    ),
    getActiveAccount: vi.fn(() => null),
    setActiveAccount: vi.fn(),
    acquireTokenSilent: vi.fn(),
    acquireTokenRedirect: vi.fn(async () => undefined),
    addEventCallback: vi.fn((cb: (event: any) => void) => {
      callbacks.push(cb);
      return 'cb-id';
    }),
    emit: (event: any) => callbacks.forEach(cb => cb(event)),
  } as any;
}

async function load() {
  vi.resetModules();
  const msal = await import('@azure/msal-browser');
  const msalAuth = await import('../src/auth/msal-auth');
  const interactionRequired = () => new msal.InteractionRequiredAuthError('interaction_required');
  return { msal, msalAuth, interactionRequired };
}

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
  h.nestable = null;
  h.popupInstances.length = 0;
  h.naaRecommended.mockReturnValue(true);
});

describe('browser (CIAM) - a background read', () => {
  it('interaction required: throws SignInRequiredError, starts no redirect, captures no return-to', async () => {
    const { msalAuth, interactionRequired } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    window.history.replaceState({}, '', '/?customer=demo&record=matter:00000000-0000-4000-8000-000000000042');

    const error = await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true }).catch(e => e);

    expect(error).toBeInstanceOf(msalAuth.SignInRequiredError);
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
  });

  it('a timed-out silent renewal is the same: no redirect', async () => {
    const { msal, msalAuth } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(new msal.BrowserAuthError(msal.BrowserAuthErrorCodes.timedOut));

    await expect(
      msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true })
    ).rejects.toBeInstanceOf(msalAuth.SignInRequiredError);
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('no account at all: SignInRequiredError, no login redirect, no return-to', async () => {
    const { msalAuth } = await load();
    const instance = fakeInstance([]);

    await expect(
      msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true })
    ).rejects.toBeInstanceOf(msalAuth.SignInRequiredError);
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
  });

  it('records NO sign-in failure, so a later user-initiated (default) call still redirects', async () => {
    const { msalAuth, interactionRequired } = await load();
    const instance = fakeInstance();
    msalAuth.syncActiveAccountOnInteractiveSignIn(instance);
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());

    await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true }).catch(() => undefined);
    expect(msalAuth.getSignInFailure(instance)).toBeNull();

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('still tries the silent request first, with the unchanged CIAM shape { scopes, account }', async () => {
    const { msalAuth } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockResolvedValue({ accessToken: 'tok' });

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true })).resolves.toBe('tok');
    expect(instance.acquireTokenSilent.mock.calls[0][0]).toStrictEqual({ scopes: [BFF_SCOPE], account });
  });

  it('an unrelated failure (network) is rethrown as it was', async () => {
    const { msalAuth } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(new Error('network down'));

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true })).rejects.toThrow(
      'network down'
    );
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('the error carries only the safe code', async () => {
    const { msalAuth, interactionRequired } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    const error = await msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: true }).catch(e => e);
    expect(error.failure).toEqual({ errorCode: 'interaction_required', aadsts: null });
  });
});

describe('browser - every other caller is unchanged', () => {
  it('the default call still captures the return-to and redirects exactly once', async () => {
    const { msalAuth, interactionRequired } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    window.history.replaceState({}, '', '/?customer=demo&record=matter:00000000-0000-4000-8000-000000000042');

    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE)).rejects.toThrow(/redirecting to login/);

    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBe(
      '/?customer=demo&record=matter:00000000-0000-4000-8000-000000000042'
    );
  });

  it('noInteraction: false is the default', async () => {
    const { msalAuth, interactionRequired } = await load();
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(interactionRequired());
    await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, { noInteraction: false })).rejects.toThrow(
      /redirecting to login/
    );
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('the acquirer seam gets the option only when it is set (first-attempt and 401-retry call shapes are unchanged)', async () => {
    const { msalAuth } = await load();
    const acquirer = vi.fn(async () => 'tok');
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    const backend = { plane: 'ciam', bffScope: BFF_SCOPE } as const;

    await msalAuth.acquireActiveBffToken(backend);
    await msalAuth.acquireActiveBffToken(backend, { forceRefresh: true });
    await msalAuth.acquireActiveBffToken(backend, { noInteraction: true });
    await msalAuth.acquireActiveBffToken(backend, { forceRefresh: true, noInteraction: true });
    await msalAuth.acquireActiveBffToken();
    await msalAuth.acquireActiveBffToken(undefined, { noInteraction: true });

    expect(acquirer.mock.calls).toEqual([
      [BFF_SCOPE],
      [BFF_SCOPE, { forceRefresh: true }],
      [BFF_SCOPE, { noInteraction: true }],
      [BFF_SCOPE, { forceRefresh: true, noInteraction: true }],
      [undefined],
      [undefined, { noInteraction: true }],
    ]);
  });
});

describe('the standalone plane seam', () => {
  it('passes noInteraction through (workforce: the silent ladder is unchanged, then no redirect)', async () => {
    const { msal, msalAuth } = await load();
    const plane = await import('../src/auth/standalone-plane');
    const instance = fakeInstance();
    instance.acquireTokenSilent.mockRejectedValue(new msal.InteractionRequiredAuthError('interaction_required'));
    plane.applyStandalonePlane({
      realm: 'workforce',
      instance,
      bffScope: BFF_SCOPE,
      acquireOptions: { tenantId: SPAARKE_TENANT, silentRedirectUri: `${window.location.origin}/auth-callback.html` },
    } as any);
    const backend = { plane: 'workforce', bffScope: BFF_SCOPE } as const;

    await expect(msalAuth.acquireActiveBffToken(backend, { noInteraction: true })).rejects.toBeInstanceOf(
      msalAuth.SignInRequiredError
    );
    expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    // The silent request is the workforce first attempt, unchanged: no redirect URI.
    expect(instance.acquireTokenSilent.mock.calls[0][0]).toStrictEqual({
      scopes: [BFF_SCOPE],
      account,
      cacheLookupPolicy: msal.CacheLookupPolicy.AccessTokenAndRefreshToken,
    });

    // ... and without the option the same plane still redirects.
    await expect(msalAuth.acquireActiveBffToken(backend)).rejects.toThrow(/redirecting to login/);
    expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });
});

describe('Teams - a background read opens no popup', () => {
  // Task 075 (A9): the Teams strategy is built for the multitenant home client, which the config must name.
  const cfg = { clientId: 'cccccccc-0000-0000-0000-00000000000c', tenantId: SPAARKE_TENANT, bffScope: BFF_SCOPE };
  beforeEach(() => {
    vi.stubEnv('VITE_WORKFORCE_HOME_MSAL_CLIENT_ID', cfg.clientId);
  });
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('NAA says interaction is required: SignInRequiredError, no acquireTokenPopup anywhere', async () => {
    const { msal, msalAuth } = await load();
    const naa = {
      getActiveAccount: () => account,
      getAllAccounts: () => [account],
      setActiveAccount: vi.fn(),
      acquireTokenSilent: vi.fn(async () => {
        throw new msal.InteractionRequiredAuthError('interaction_required');
      }),
      acquireTokenPopup: vi.fn(),
      ssoSilent: vi.fn(),
    };
    h.nestable = async () => naa;
    const built = h.popupInstances.length; // the CIAM singleton, built when msal-config loaded

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg, { noInteraction: true })).rejects.toBeInstanceOf(
      msalAuth.SignInRequiredError
    );
    expect(naa.acquireTokenPopup).not.toHaveBeenCalled();
    expect(h.popupInstances).toHaveLength(built); // the popup fallback instance was never even built

    // Without the option, the same state opens the broker popup (unchanged).
    naa.acquireTokenPopup.mockResolvedValue({
      accessToken: 'tok-popup',
      idTokenClaims: { tid: SPAARKE_TENANT },
      account,
    });
    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-popup');
    expect(naa.acquireTokenPopup).toHaveBeenCalledTimes(1);
  });

  it('NAA unavailable, nothing cached for the popup instance: SignInRequiredError, no popup', async () => {
    const { msalAuth } = await load();
    h.nestable = async () => {
      throw new Error('NAA bridge error');
    };
    const built = h.popupInstances.length;

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg, { noInteraction: true })).rejects.toBeInstanceOf(
      msalAuth.SignInRequiredError
    );
    expect(h.popupInstances.length).toBe(built + 1); // the popup fallback instance was built and consulted (silently) only
    const popup = h.popupInstances[h.popupInstances.length - 1] as { acquireTokenPopup: ReturnType<typeof vi.fn> };
    expect(popup.acquireTokenPopup).not.toHaveBeenCalled();
  });
});
