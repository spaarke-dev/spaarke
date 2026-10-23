// Teams tab on the multitenant home client through NAA (spaarke-SPA-external-access-platform-r3 task 075; ADR-028
// Amendment A9, Teams amendment; design notes/a9-bff-design.md sections 5, 7 and 12).
//
// In Teams everyone (workforce contacts and licensed users) gets the BFF token from the MULTITENANT client, through NAA,
// on authority https://login.microsoftonline.com/{tid}, where tid is the Teams context tenant (user.tenant.id) - validated
// and LISTED in the directory (homeTenantIds) before any token is requested. The authority is set on EVERY request (a live
// NAA bridge sends the host nothing else). A token for another tenant is never returned. The 058 registration gate runs in
// Teams too: registration_required -> a silent link (the single-tenant A8 client through NAA, Spaarke authority, in-memory)
// -> the profile form; the Spaarke-sign-in popup opens only from the "Link my account" click after
// licensed_user_link_required (owner-confirmed 2026-10-19). No Teams SSO (getAuthToken), no webApplicationInfo.
//
// MSAL, Teams JS and fetch are fakes that RECORD what the code asks for (instance configs, token requests incl. the
// per-request authority, URLs, headers, bodies), so the assertions are about requests, not stub echoes.
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.setConfig({ testTimeout: 60000 });

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const A8_CLIENT_ID = '11111111-2222-3333-4444-555555555555';
const HOME_CLIENT_ID = '66666666-7777-8888-9999-000000000001';
const TID = 'c0c0c0c0-0000-4000-8000-000000000001';
const FABRIKAM_TID = 'f0f0f0f0-0000-4000-8000-000000000002';
const UNLISTED_TID = 'dddddddd-0000-4000-8000-000000000003';

const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
  homeTenantIds: [TID],
};
const FABRIKAM = {
  customerKey: 'fabrikam',
  displayName: 'Fabrikam',
  bffBaseUrl: 'https://fabrikam-bff.test',
  bffAppId: 'bbbbbbbb-0000-0000-0000-00000000000b',
  homeTenantIds: [FABRIKAM_TID],
};
const scopeOf = (appId: string) => `api://${appId}/user_impersonation`;
const authorityOf = (tid: string) => `https://login.microsoftonline.com/${tid}`;
const CONTOSO_SCOPE = scopeOf(CONTOSO.bffAppId);

const JANE = {
  homeAccountId: `jane-oid.${TID}`,
  username: 'jane@contoso.test',
  name: 'Jane Doe',
  tenantId: TID,
  environment: 'login.windows.net',
};
const GUEST_ACCOUNT = {
  homeAccountId: 'guest.spaarke',
  username: 'jane_contoso.test#EXT#@spaarke.test',
  tenantId: SPAARKE_TENANT,
  environment: 'login.windows.net',
};

type Behaviour = {
  accounts?: any[];
  silent?: (req: any) => Promise<any>;
  sso?: (req: any) => Promise<any>;
  popup?: (req: any) => Promise<any>;
};
type Reply = { status: number; json?: unknown; text?: string };

const h = vi.hoisted(() => ({
  // Every token request any fake MSAL instance receives.
  requests: [] as Array<{ instance: string; kind: 'naa' | 'pca'; clientId: string; call: string; request: any }>,
  // Every fake instance created, in order.
  instances: [] as Array<{ kind: 'naa' | 'pca'; name: string; config: any; instance: any }>,
  // Behaviour of the fake instances, keyed by the client id they are built with.
  naa: {} as Record<string, Behaviour>,
  pca: {} as Record<string, Behaviour>,
  // Client ids for which createNestablePublicClientApplication throws (no usable bridge).
  naaCreateFails: new Set<string>(),
  // Every clearCache request any fake receives.
  clears: [] as Array<{ kind: 'naa' | 'pca'; clientId: string; request: any }>,
  fetches: [] as Array<{ url: string; method: string; headers: Record<string, string>; body: string | undefined }>,
  script: {} as Record<string, any[]>,
  unexpected: [] as string[],
  msal: { accounts: [] as any[], instance: {} as any, inProgress: 'none' },
  teamsInitialize: vi.fn(),
  getContext: vi.fn(),
  getAuthToken: vi.fn(),
  naaRecommended: vi.fn(),
  notifyAppLoaded: vi.fn(),
  notifyFailure: vi.fn(),
  notifySuccess: vi.fn(),
  console: [] as string[],
}));

function makeFake(kind: 'naa' | 'pca', behaviour: Behaviour, config: any): any {
  const name = `${kind}#${h.instances.length}`;
  const clientId: string = config?.auth?.clientId;
  let active: any = null;
  const accounts: any[] = [...(behaviour.accounts ?? [])];
  const record = (call: string, request: any) => h.requests.push({ instance: name, kind, clientId, call, request });
  const remember = (result: any) => {
    if (result?.account && !accounts.includes(result.account)) accounts.push(result.account);
    return result;
  };
  const instance = {
    name,
    config,
    initialize: vi.fn(async () => undefined),
    handleRedirectPromise: vi.fn(async () => null),
    addEventCallback: vi.fn(() => 'callback-id'),
    getActiveAccount: () => active,
    setActiveAccount: (a: any) => {
      active = a;
    },
    getAllAccounts: (filter?: { tenantId?: string }) =>
      filter?.tenantId ? accounts.filter(a => a.tenantId === filter.tenantId) : accounts,
    acquireTokenSilent: vi.fn(async (req: any) => {
      record('acquireTokenSilent', req);
      return remember(
        behaviour.silent
          ? await behaviour.silent(req)
          : {
              accessToken: `home-token:${req.scopes[0]}`,
              tenantId: accounts[0]?.tenantId,
              idTokenClaims: { tid: TID },
              account: accounts[0] ?? null,
            }
      );
    }),
    ssoSilent: vi.fn(async (req: any) => {
      record('ssoSilent', req);
      if (!behaviour.sso) throw new Error('ssoSilent not configured');
      return remember(await behaviour.sso(req));
    }),
    acquireTokenPopup: vi.fn(async (req: any) => {
      record('acquireTokenPopup', req);
      if (!behaviour.popup) throw new Error('popup not configured');
      return remember(await behaviour.popup(req));
    }),
    acquireTokenRedirect: vi.fn(async (req: any) => {
      record('acquireTokenRedirect', req);
    }),
    loginRedirect: vi.fn(async (req: any) => {
      record('loginRedirect', req);
    }),
    // The real NAA controller throws on clearCache (NestedAppAuthController.clearCache -> unsupported). The standard
    // instance's account form removes that account from its own store.
    clearCache: vi.fn(async (request?: { account?: any }) => {
      h.clears.push({ kind, clientId, request });
      if (kind === 'naa') throw new Error('Nested app auth does not support clearCache');
      const index = request?.account ? accounts.indexOf(request.account) : -1;
      if (index >= 0) accounts.splice(index, 1);
    }),
  };
  h.instances.push({ kind, name, config, instance });
  return instance;
}

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    constructor(config: any) {
      return makeFake('pca', h.pca[config?.auth?.clientId] ?? {}, config);
    }
  }
  return {
    ...actual,
    PublicClientApplication: FakePublicClientApplication,
    createNestablePublicClientApplication: async (config: any) => {
      const clientId = config?.auth?.clientId;
      if (h.naaCreateFails.has(clientId) || !h.naa[clientId]) throw new Error('NAA bridge unavailable');
      return makeFake('naa', h.naa[clientId], config);
    },
  };
});

vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: React.ReactNode }) => children,
  useMsal: () => h.msal,
}));

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: h.teamsInitialize,
    getContext: h.getContext,
    notifyAppLoaded: h.notifyAppLoaded,
    notifySuccess: h.notifySuccess,
    notifyFailure: h.notifyFailure,
    registerOnThemeChangeHandler: vi.fn(),
    FailedReason: { AuthFailed: 'AuthFailed', Timeout: 'Timeout', Other: 'Other' },
  },
  nestedAppAuth: { isNAAChannelRecommended: h.naaRecommended },
  // Present so a regression that calls Teams SSO is observable; the code under test must never call it.
  authentication: { getAuthToken: h.getAuthToken },
}));

// The shared App tree (only main.tsx's bootstrap is under test there): a marker.
vi.mock('../src/App', () => ({
  App: ({ teamsHost }: { teamsHost?: boolean }) =>
    React.createElement('div', { 'data-testid': 'app', 'data-teams': String(!!teamsHost) }, 'app mounted'),
}));

function stubEnv(overrides: Partial<Record<string, string>> = {}): void {
  const values: Record<string, string> = {
    VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: HOME_CLIENT_ID,
    VITE_TEAMS_MSAL_CLIENT_ID: A8_CLIENT_ID,
    VITE_TEAMS_MSAL_TENANT_ID: SPAARKE_TENANT,
    VITE_TEAMS_MSAL_BFF_SCOPE: scopeOf('99999999-8888-7777-6666-555555555555'),
    VITE_BACKENDS: JSON.stringify([CONTOSO, FABRIKAM]),
    ...overrides,
  } as Record<string, string>;
  for (const [key, value] of Object.entries(values)) vi.stubEnv(key, value);
}

/** Fresh copies of the modules (memoized MSAL instances and active-plane seams do not leak between tests). */
async function load() {
  vi.resetModules();
  const msalAuth = await import('../src/auth/msal-auth');
  const selection = await import('../src/auth/backend-selection');
  const plane = await import('../src/auth/standalone-plane');
  return { msalAuth, selection, plane };
}

const bffPath = (url: string) => new URL(url).pathname;

/**
 * A token result modelled on NAA (msal-browser 5.5.0, NestedAppAuthAdapter.fromNaaTokenResponse): `tenantId` and `account` are
 * the Teams HOST's account (a Teams user in the Teams context tenant TID), while the token's own tenant is the `tid` claim of
 * the id token. So `tokenTenant` (the tenant that ISSUED the token) lands in the claims, never in `tenantId`.
 */
const grant = (name: string, tokenTenant: string, account: any = undefined) => {
  const host = account ?? { homeAccountId: `acct-${name}`, username: `${name}@test`, tenantId: TID, environment: 'x' };
  return {
    accessToken: `tok-${name}`,
    tenantId: host.tenantId,
    idTokenClaims: { tid: tokenTenant },
    account: host,
  };
};

const b64url = (value: unknown) =>
  btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
/** An unsigned JWT-shaped access token whose payload carries `tid` (the code only decodes it, it never verifies it). */
const jwtFor = (claims: Record<string, unknown>) => `${b64url({ alg: 'none' })}.${b64url(claims)}.sig`;

const home = () => h.requests.filter(r => r.clientId === HOME_CLIENT_ID);
const calls = () => h.fetches.map(f => `${f.method} ${bffPath(f.url)}`);
const bodies = (key: string) =>
  h.fetches.filter(f => `${f.method} ${bffPath(f.url)}` === key).map(f => (f.body ? JSON.parse(f.body) : undefined));

beforeEach(() => {
  h.requests.length = 0;
  h.instances.length = 0;
  h.naa = {};
  h.pca = {};
  h.naaCreateFails.clear();
  h.clears.length = 0;
  h.fetches.length = 0;
  h.script = {};
  h.unexpected.length = 0;
  h.console.length = 0;
  h.msal = { accounts: [], instance: {}, inProgress: 'none' };
  for (const fn of [
    h.teamsInitialize,
    h.getContext,
    h.getAuthToken,
    h.naaRecommended,
    h.notifyAppLoaded,
    h.notifyFailure,
  ]) {
    fn.mockReset();
  }
  h.notifySuccess.mockReset();
  h.teamsInitialize.mockResolvedValue(undefined);
  h.naaRecommended.mockReturnValue(true);
  h.getContext.mockResolvedValue({
    app: { theme: 'default', locale: 'en-us', host: { clientType: 'web' } },
    page: { subPageId: '' },
    user: { tenant: { id: TID } },
  });
  localStorage.clear();
  sessionStorage.clear();
  window.history.replaceState(null, '', '/');
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase();
      h.fetches.push({
        url,
        method,
        headers: (init?.headers ?? {}) as Record<string, string>,
        body: typeof init?.body === 'string' ? init.body : undefined,
      });
      const queue = h.script[`${method} ${bffPath(url)}`];
      if (!queue || queue.length === 0) {
        h.unexpected.push(`${method} ${bffPath(url)}`);
        return new Response('unexpected', { status: 599 });
      }
      const reply: Reply = queue.length > 1 ? (queue.shift() as Reply) : queue[0];
      const body = reply.json !== undefined ? JSON.stringify(reply.json) : (reply.text ?? '{}');
      return new Response(body, { status: reply.status, headers: { 'Content-Type': 'application/json' } });
    })
  );
  for (const level of ['log', 'info', 'warn', 'error', 'debug'] as const) {
    vi.spyOn(console, level).mockImplementation((...args: unknown[]) => {
      h.console.push(args.map(a => (typeof a === 'string' ? a : JSON.stringify(a))).join(' '));
    });
  }
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------------------
// AC1: the BFF token - multitenant client, NAA, authority /{tid} on every request, then the popup
// ---------------------------------------------------------------------------------------------------------------

describe('Teams BFF token: multitenant client through NAA on authority /{tid} (AC1)', () => {
  const cfg = { clientId: HOME_CLIENT_ID, tenantId: TID, bffScope: CONTOSO_SCOPE };

  beforeEach(() => stubEnv());

  it('NAA first: the nestable instance is the multitenant client on /{tid}, and ssoSilent carries the per-request authority', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID) };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-naa');

    const naa = h.instances.find(i => i.kind === 'naa')!;
    expect(naa.config.auth.clientId).toBe(HOME_CLIENT_ID);
    expect(naa.config.auth.authority).toBe(authorityOf(TID));
    expect(naa.config.auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    expect(home().map(r => `${r.kind}:${r.call}`)).toEqual(['naa:ssoSilent']);
    expect(home()[0].request).toStrictEqual({ scopes: [CONTOSO_SCOPE], authority: authorityOf(TID) });
    // The single-tenant A8 client is not used for the BFF token, and no popup instance was built.
    expect(h.instances.filter(i => i.config.auth.clientId === A8_CLIENT_ID)).toEqual([]);
    expect(h.instances.filter(i => i.kind === 'pca' && i.config.auth.clientId === HOME_CLIENT_ID)).toEqual([]);
    expect(h.getAuthToken).not.toHaveBeenCalled();
  });

  it('later calls (a cached account) and a forced refresh carry the per-request authority too', async () => {
    h.naa[HOME_CLIENT_ID] = {
      sso: async () => grant('naa', TID),
      silent: async () => grant('naa-fresh', TID),
    };
    const { msalAuth } = await load();

    await msalAuth.acquireTeamsWorkforceBffToken(cfg);
    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-naa-fresh');
    await msalAuth.acquireTeamsWorkforceBffToken(cfg, { forceRefresh: true });

    const silent = home().filter(r => r.call === 'acquireTokenSilent');
    expect(silent).toHaveLength(2);
    for (const r of silent) expect(r.request.authority).toBe(authorityOf(TID));
    expect(silent[1].request.forceRefresh).toBe(true);
  });

  it('NAA says interaction is required: the NAA (brokered) popup carries the authority as well', async () => {
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    h.naa[HOME_CLIENT_ID] = {
      accounts: [JANE],
      silent: async () => {
        throw new InteractionRequiredAuthError('interaction_required');
      },
      popup: async () => grant('naa-popup', TID, JANE),
    };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-naa-popup');
    const popup = home().find(r => r.call === 'acquireTokenPopup')!;
    expect(popup.kind).toBe('naa');
    expect(popup.request).toStrictEqual({ scopes: [CONTOSO_SCOPE], authority: authorityOf(TID) });
  });

  it('NAA failure -> the standard popup instance: same client, same /{tid} authority on the config and on the request', async () => {
    h.naa[HOME_CLIENT_ID] = {
      sso: async () => {
        throw new Error('NAA bridge error');
      },
    };
    h.pca[HOME_CLIENT_ID] = { popup: async () => grant('popup', TID) };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-popup');

    expect(home().map(r => `${r.kind}:${r.call}`)).toEqual(['naa:ssoSilent', 'pca:acquireTokenPopup']);
    const pca = h.instances.find(i => i.kind === 'pca' && i.config.auth.clientId === HOME_CLIENT_ID)!;
    expect(pca.config.auth.authority).toBe(authorityOf(TID));
    expect(pca.config.auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    expect(home()[1].request).toStrictEqual({
      scopes: [CONTOSO_SCOPE],
      authority: authorityOf(TID),
      loginHint: undefined,
      prompt: 'select_account',
    });
    expect(h.getAuthToken).not.toHaveBeenCalled();
  });

  it('a cached popup account: the silent attempt before the popup carries the authority', async () => {
    h.naaRecommended.mockReturnValue(false);
    h.pca[HOME_CLIENT_ID] = { accounts: [JANE], silent: async () => grant('silent', TID, JANE) };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-silent');
    expect(home().map(r => r.call)).toEqual(['acquireTokenSilent']);
    expect(home()[0].request.authority).toBe(authorityOf(TID));
  });

  it('the user closes the NAA prompt: no second popup is opened', async () => {
    const { BrowserAuthError, BrowserAuthErrorCodes } = await import('@azure/msal-browser');
    h.naa[HOME_CLIENT_ID] = {
      sso: async () => {
        throw new BrowserAuthError(BrowserAuthErrorCodes.userCancelled);
      },
    };
    h.pca[HOME_CLIENT_ID] = { popup: async () => grant('popup', TID) };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    expect(home().map(r => r.call)).toEqual(['ssoSilent']);
  });

  it('a background read (noInteraction): SignInRequiredError, no popup, no redirect', async () => {
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    h.naa[HOME_CLIENT_ID] = {
      accounts: [JANE],
      silent: async () => {
        throw new InteractionRequiredAuthError('interaction_required');
      },
    };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg, { noInteraction: true })).rejects.toBeInstanceOf(
      msalAuth.SignInRequiredError
    );
    expect(h.requests.filter(r => r.call === 'acquireTokenPopup')).toEqual([]);
  });

  it('never redirects: no acquireTokenRedirect / loginRedirect call in any ladder step', async () => {
    h.naa[HOME_CLIENT_ID] = {
      sso: async () => {
        throw new Error('NAA bridge error');
      },
    };
    h.pca[HOME_CLIENT_ID] = {
      popup: async () => {
        throw new Error('popup blocked');
      },
    };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    expect(h.requests.filter(r => r.call === 'acquireTokenRedirect' || r.call === 'loginRedirect')).toEqual([]);
  });

  it('the authority is only ever a tenant GUID (no multi-tenant alias) on instances and requests', async () => {
    h.naa[HOME_CLIENT_ID] = {
      sso: async () => {
        throw new Error('NAA bridge error');
      },
    };
    h.pca[HOME_CLIENT_ID] = { popup: async () => grant('popup', TID) };
    const { msalAuth } = await load();
    await msalAuth.acquireTeamsWorkforceBffToken(cfg);

    const alias = /\/(common|organizations|consumers)(\/|$)/;
    for (const i of h.instances.filter(i => i.config.auth.clientId === HOME_CLIENT_ID)) {
      expect(i.config.auth.authority).not.toMatch(alias);
    }
    for (const r of home()) expect(r.request.authority).not.toMatch(alias);
  });

  it('the A8 client id is refused as the Teams BFF client (the multitenant client only): nothing is requested', async () => {
    const { msalAuth } = await load();
    await expect(
      msalAuth.acquireTeamsWorkforceBffToken({ clientId: A8_CLIENT_ID, tenantId: TID, bffScope: CONTOSO_SCOPE })
    ).rejects.toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    expect(h.requests).toEqual([]);
    expect(h.instances.filter(i => i.config.auth.clientId === A8_CLIENT_ID)).toEqual([]);
  });

  it.each([['organizations'], ['common'], [''], ['not-a-guid'], [`${TID}/v2.0`]])(
    'a tid that is not a GUID (%j) reaches no MSAL instance and no request',
    async tenantId => {
      h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID) };
      const { msalAuth } = await load();
      const instancesBefore = h.instances.length; // the CIAM singleton, built when msal-config loaded

      await expect(
        msalAuth.acquireTeamsWorkforceBffToken({ clientId: HOME_CLIENT_ID, tenantId, bffScope: CONTOSO_SCOPE })
      ).rejects.toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
      expect(h.requests).toEqual([]);
      expect(h.instances).toHaveLength(instancesBefore);
    }
  );
});

// ---------------------------------------------------------------------------------------------------------------
// A token for another tenant is never returned (fail closed)
// ---------------------------------------------------------------------------------------------------------------

describe('Teams BFF token: a result whose tenant is not the Teams tid is refused', () => {
  const cfg = { clientId: HOME_CLIENT_ID, tenantId: TID, bffScope: CONTOSO_SCOPE };

  beforeEach(() => stubEnv());

  it('NAA returns another tenant and the popup does too: nothing is returned, the account is not made active, the token is not leaked', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('bad-naa', UNLISTED_TID) };
    h.pca[HOME_CLIENT_ID] = { popup: async () => grant('bad-popup', UNLISTED_TID) };
    const { msalAuth } = await load();

    const error = await msalAuth.acquireTeamsWorkforceBffToken(cfg).then(
      () => null,
      (e: unknown) => e
    );
    expect(error).toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    expect(JSON.stringify(error)).not.toContain('tok-bad');
    expect((error as Error).message).not.toContain('tok-bad');
    for (const i of h.instances) expect(i.instance.getActiveAccount()).toBeNull();
    expect(msalAuth.getTeamsWorkforceSignedInInstance()).toBeNull();
  });

  it('NAA returns another tenant but the popup returns the right one: only the right token is returned', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('bad-naa', UNLISTED_TID) };
    h.pca[HOME_CLIENT_ID] = { popup: async () => grant('good-popup', TID) };
    const { msalAuth } = await load();

    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-good-popup');
    const naa = h.instances.find(i => i.kind === 'naa')!;
    expect(naa.instance.getActiveAccount()).toBeNull(); // the wrong-tenant account was not adopted
  });

  it('a result with no tenant at all is refused', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => ({ accessToken: 'tok-no-tenant', account: null }) };
    h.pca[HOME_CLIENT_ID] = { popup: async () => ({ accessToken: 'tok-no-tenant-2', account: null }) };
    const { msalAuth } = await load();
    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
  });

  it('the comparison ignores case', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID.toUpperCase()) };
    const { msalAuth } = await load();
    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-naa');
  });

  it('a cached popup account whose silent token is for another tenant is not used: it falls to the popup', async () => {
    h.naaRecommended.mockReturnValue(false);
    h.pca[HOME_CLIENT_ID] = {
      accounts: [JANE],
      silent: async () => grant('bad-silent', UNLISTED_TID, JANE),
      popup: async () => grant('good-popup', TID, JANE),
    };
    const { msalAuth } = await load();
    await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-good-popup');
  });

  // Review F4: under NAA, msal-browser 5.5.0 sets result.tenantId (and result.account) from the Teams HOST's account, not from
  // the token. The check must read the TOKEN's tenant: idTokenClaims.tid, else the access token payload's tid.
  describe('the token tenant is the tid claim, not result.tenantId (review F4)', () => {
    it('result.tenantId = the Teams tid (host account) but the token was issued by another tenant: refused', async () => {
      const foreign = { ...grant('host-tid-foreign-token', UNLISTED_TID), tenantId: TID };
      h.naa[HOME_CLIENT_ID] = { sso: async () => foreign };
      h.pca[HOME_CLIENT_ID] = { popup: async () => foreign };
      const { msalAuth } = await load();
      await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(
        msalAuth.TeamsWorkforceAuthError
      );
      for (const i of h.instances) expect(i.instance.getActiveAccount()).toBeNull();
    });

    it('result.tenantId = another tenant but the token (tid claim) was issued by the Teams tenant: accepted', async () => {
      h.naa[HOME_CLIENT_ID] = { sso: async () => ({ ...grant('right-token', TID), tenantId: UNLISTED_TID }) };
      const { msalAuth } = await load();
      await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe('tok-right-token');
    });

    it('no id token claims: the access token payload tid (base64url JSON, unverified) is used', async () => {
      const right = jwtFor({ tid: TID.toUpperCase(), aud: 'api://x' });
      h.naa[HOME_CLIENT_ID] = {
        sso: async () => ({ accessToken: right, tenantId: UNLISTED_TID, account: { ...JANE } }),
      };
      const { msalAuth } = await load();
      await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).resolves.toBe(right);
    });

    it('no id token claims and an access token for another tenant: refused (the host account tenant does not rescue it)', async () => {
      const foreign = { accessToken: jwtFor({ tid: UNLISTED_TID }), tenantId: TID, account: { ...JANE } };
      h.naa[HOME_CLIENT_ID] = { sso: async () => foreign };
      h.pca[HOME_CLIENT_ID] = { popup: async () => foreign };
      const { msalAuth } = await load();
      await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(
        msalAuth.TeamsWorkforceAuthError
      );
    });

    it.each([
      ['an opaque access token', { accessToken: 'opaque-token' }],
      ['a JWT with no tid', { accessToken: jwtFor({ aud: 'api://x' }) }],
      ['a JWT whose tid is not a string', { accessToken: jwtFor({ tid: 42 }) }],
      ['a malformed JWT payload', { accessToken: 'a.!!!.c' }],
      ['an empty id token tid and no usable access token', { accessToken: 'x', idTokenClaims: { tid: '' } }],
    ])(
      'neither source gives a tenant (%s): fail closed even though result.tenantId is the Teams tid',
      async (_l, partial) => {
        const unknown = { tenantId: TID, account: { ...JANE }, ...partial };
        h.naa[HOME_CLIENT_ID] = { sso: async () => unknown };
        h.pca[HOME_CLIENT_ID] = { popup: async () => unknown };
        const { msalAuth } = await load();
        await expect(msalAuth.acquireTeamsWorkforceBffToken(cfg)).rejects.toBeInstanceOf(
          msalAuth.TeamsWorkforceAuthError
        );
      }
    );
  });
});

// ---------------------------------------------------------------------------------------------------------------
// AC2: the Teams bootstrap - tid -> directory, before any token request
// ---------------------------------------------------------------------------------------------------------------

describe('Teams bootstrap (main.tsx): the Teams context tid picks the backend', () => {
  async function boot() {
    vi.resetModules();
    document.body.innerHTML = '<div id="root"></div>';
    await import('../src/main');
    return {
      selection: await import('../src/auth/backend-selection'),
      msalAuth: await import('../src/auth/msal-auth'),
      rtl: await import('@testing-library/react'),
    };
  }

  beforeEach(() => stubEnv());

  /** Nothing was requested for the BFF, nothing was fetched, and no MSAL instance beyond the CIAM singleton was built. */
  function expectZeroTokenRequestsAndZeroBffCalls(): void {
    expect(h.requests).toEqual([]);
    expect(h.fetches).toEqual([]);
    expect(h.instances.filter(i => i.kind === 'naa')).toEqual([]);
    expect(
      h.instances.filter(i => i.config.auth.clientId === HOME_CLIENT_ID || i.config.auth.clientId === A8_CLIENT_ID)
    ).toEqual([]);
    expect(h.getAuthToken).not.toHaveBeenCalled();
  }

  it('AC1 end to end: a listed tid activates its entry and the NAA token request is the multitenant client on /{tid}', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID) };
    const { selection, rtl } = await boot();

    const app = await rtl.screen.findByTestId('app');
    expect(app.getAttribute('data-teams')).toBe('true');
    expect(selection.getActiveBackend()).toMatchObject({
      plane: 'workforce',
      customerKey: 'contoso',
      bffBaseUrl: CONTOSO.bffBaseUrl,
      bffScope: CONTOSO_SCOPE,
    });
    expect(home().map(r => `${r.kind}:${r.call}`)).toEqual(['naa:ssoSilent']);
    expect(home()[0].request).toStrictEqual({ scopes: [CONTOSO_SCOPE], authority: authorityOf(TID) });
    expect(h.getAuthToken).not.toHaveBeenCalled();
  });

  it('AC2: an unlisted tid shows the Teams not-set-up screen with zero token requests and zero BFF calls', async () => {
    h.getContext.mockResolvedValue({ app: { theme: 'default' }, user: { tenant: { id: UNLISTED_TID } } });
    const { selection, rtl } = await boot();

    expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
    expect(rtl.screen.queryByTestId('app')).toBeNull();
    expectZeroTokenRequestsAndZeroBffCalls();
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
  });

  it.each([
    ['no user.tenant in the context', { app: { theme: 'default' }, user: {} }],
    ['no user in the context', { app: { theme: 'default' } }],
    ['a tenant id that is not a GUID', { app: { theme: 'default' }, user: { tenant: { id: 'organizations' } } }],
    ['an empty tenant id', { app: { theme: 'default' }, user: { tenant: { id: '' } } }],
    ['a non-string tenant id', { app: { theme: 'default' }, user: { tenant: { id: 42 } } }],
  ])('AC2: %s -> the same screen, zero token requests, zero BFF calls', async (_label, context) => {
    h.getContext.mockResolvedValue(context);
    const { rtl } = await boot();
    expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
    expectZeroTokenRequestsAndZeroBffCalls();
  });

  it('AC2: a context that cannot be read -> the same screen, zero token requests, zero BFF calls', async () => {
    h.getContext.mockRejectedValue(new Error('host gone'));
    const { rtl } = await boot();
    expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
    expectZeroTokenRequestsAndZeroBffCalls();
  });

  it('the Teams not-set-up screen has no Sign out and no "choose a different sign-in" (no actions at all), a real h1, and shows no tenant id', async () => {
    h.getContext.mockResolvedValue({ app: { theme: 'default' }, user: { tenant: { id: UNLISTED_TID } } });
    const { rtl } = await boot();
    await rtl.screen.findByText('Your organization is not set up for Spaarke');

    expect(rtl.screen.queryAllByRole('button')).toEqual([]);
    expect(document.body.textContent ?? '').not.toMatch(/sign out|different sign-in|sign in/i);
    expect(document.body.textContent ?? '').not.toContain(UNLISTED_TID);
    expect(rtl.screen.getByRole('heading', { level: 1 }).textContent).toBe(
      'Your organization is not set up for Spaarke'
    );
  });

  it('the not-set-up screen tells Teams the app loaded and failed (Other), once each, and the Teams theme is applied', async () => {
    h.getContext.mockResolvedValue({ app: { theme: 'dark' }, user: { tenant: { id: UNLISTED_TID } } });
    const { rtl } = await boot();
    await rtl.screen.findByText('Your organization is not set up for Spaarke');

    expect(h.notifyAppLoaded).toHaveBeenCalledTimes(1);
    expect(h.notifyFailure).toHaveBeenCalledTimes(1);
    expect(h.notifyFailure).toHaveBeenCalledWith({ reason: 'Other', message: expect.stringMatching(/not set up/i) });
    // Follows the Teams theme (ADR-021): the dark Teams theme reached the shared theme cascade before the screen rendered.
    expect(localStorage.getItem('spaarke-theme')).toBe('dark');
  });

  it('a host error while signalling never stops the not-set-up screen from rendering', async () => {
    h.getContext.mockResolvedValue({ app: { theme: 'default' }, user: { tenant: { id: UNLISTED_TID } } });
    h.notifyAppLoaded.mockImplementation(() => {
      throw new Error('host gone');
    });
    h.notifyFailure.mockImplementation(() => {
      throw new Error('host gone');
    });
    const { rtl } = await boot();
    expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
  });

  it('the tid selects the entry that lists it (Fabrikam), not the first entry', async () => {
    h.getContext.mockResolvedValue({
      app: { theme: 'default', locale: 'en-us', host: { clientType: 'web' } },
      page: { subPageId: '' },
      user: { tenant: { id: FABRIKAM_TID } },
    });
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', FABRIKAM_TID) };
    const { selection, rtl } = await boot();
    expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    expect(selection.getActiveBackend().customerKey).toBe('fabrikam');
    expect(home()[0].request.scopes).toEqual([scopeOf(FABRIKAM.bffAppId)]);
    expect(home()[0].request.authority).toBe(authorityOf(FABRIKAM_TID));
  });

  it('the one-entry VITE_BFF_API_URL fallback lists no home directory: every Teams tid is not set up', async () => {
    stubEnv({ VITE_BACKENDS: '' });
    vi.stubEnv('VITE_BFF_API_URL', 'https://fallback-bff.test');
    const { rtl } = await boot();
    expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
    expectZeroTokenRequestsAndZeroBffCalls();
  });

  it('several entries list the tid: the customer picker; choosing one continues with that entry', async () => {
    const OTHER = { ...FABRIKAM, homeTenantIds: [TID] };
    stubEnv({ VITE_BACKENDS: JSON.stringify([CONTOSO, OTHER]) });
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID) };
    const { selection, rtl } = await boot();

    expect(await rtl.screen.findByText('Choose an organization')).toBeTruthy();
    expect(h.requests).toEqual([]); // nothing is requested before the choice
    rtl.fireEvent.click(rtl.screen.getByRole('button', { name: 'Fabrikam' }));

    expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    expect(selection.getActiveBackend().customerKey).toBe('fabrikam');
    expect(home()[0].request.scopes).toEqual([scopeOf(FABRIKAM.bffAppId)]);
  });

  it('pickedKeyListsTenant: true only for a key an entry that lists the tid has', async () => {
    const { plane } = await load();
    expect(await plane.pickedKeyListsTenant(TID, 'contoso')).toBe(true);
    expect(await plane.pickedKeyListsTenant(TID, 'fabrikam')).toBe(false);
    expect(await plane.pickedKeyListsTenant(UNLISTED_TID, 'contoso')).toBe(false);
  });

  it('after a successful sign-in the tab is the home plane: the gate flag, the tid account filter and the Teams guest-link host are set', async () => {
    h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID, JANE) };
    const { msalAuth, rtl } = await boot();
    await rtl.screen.findByTestId('app');

    expect(msalAuth.isWorkforceHomePlaneActive()).toBe(true);
    const other = { homeAccountId: 'x', tenantId: UNLISTED_TID };
    expect(msalAuth.accountsForActivePlane([JANE, other])).toEqual([JANE]);
  });

  // ----- review F1 (A): a stale msal interaction lock at Teams page load -----

  const LOCK_KEY = 'msal.interaction.status';
  const lock = (clientId: string) => JSON.stringify({ clientId, type: 'signin' });

  describe('a stale interaction lock at page load is removed before any MSAL instance is used (review F1)', () => {
    /** The lock as it stands when the first token request of the page load reaches the (fake) MSAL instance. */
    function lockAtFirstTokenRequest(): { seen: () => string | null | undefined } {
      let seen: string | null | undefined;
      h.naa[HOME_CLIENT_ID] = {
        sso: async () => {
          if (seen === undefined) seen = sessionStorage.getItem(LOCK_KEY);
          return grant('naa', TID);
        },
      };
      return { seen: () => seen };
    }

    it.each([
      ['the A8 (guest popup) client', A8_CLIENT_ID],
      ['the multitenant home (BFF popup) client', HOME_CLIENT_ID],
      ['either client, upper-cased in storage', HOME_CLIENT_ID.toUpperCase()],
    ])('a lock owned by %s is gone before the first token request', async (_label, owner) => {
      sessionStorage.setItem(LOCK_KEY, lock(owner));
      const probe = lockAtFirstTokenRequest();
      const { rtl } = await boot();
      await rtl.screen.findByTestId('app');
      expect(probe.seen()).toBeNull();
      expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
    });

    it('a lock owned by another client (CIAM) is left alone, and so is every other key', async () => {
      const CIAM_CLIENT_ID = '12345678-0000-4000-8000-0000000000aa';
      sessionStorage.setItem(LOCK_KEY, lock(CIAM_CLIENT_ID));
      sessionStorage.setItem('msal.token.keys.' + HOME_CLIENT_ID, '{"idToken":[]}');
      sessionStorage.setItem('spaarke.ext.something', 'x');
      const probe = lockAtFirstTokenRequest();
      const { rtl } = await boot();
      await rtl.screen.findByTestId('app');
      expect(probe.seen()).toBe(lock(CIAM_CLIENT_ID));
      expect(sessionStorage.getItem(LOCK_KEY)).toBe(lock(CIAM_CLIENT_ID));
      expect(sessionStorage.getItem('msal.token.keys.' + HOME_CLIENT_ID)).toBe('{"idToken":[]}');
      expect(sessionStorage.getItem('spaarke.ext.something')).toBe('x');
    });

    it('no lock: nothing is written or removed', async () => {
      const probe = lockAtFirstTokenRequest();
      const { rtl } = await boot();
      await rtl.screen.findByTestId('app');
      expect(probe.seen()).toBeNull();
      expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
    });

    it('an unparsable lock is left to msal-browser (no throw, the tab still boots)', async () => {
      sessionStorage.setItem(LOCK_KEY, '{not json');
      lockAtFirstTokenRequest();
      const { rtl } = await boot();
      await rtl.screen.findByTestId('app');
      expect(sessionStorage.getItem(LOCK_KEY)).toBe('{not json');
    });

    it('the helpers: must-fire for the Teams owners, must-not-fire for every other owner', async () => {
      stubEnv();
      const { plane } = await load();
      for (const owner of [A8_CLIENT_ID, HOME_CLIENT_ID]) {
        sessionStorage.setItem(LOCK_KEY, lock(owner));
        expect(plane.clearStaleTeamsInteractionLock()).toBe(true);
        expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
      }
      sessionStorage.setItem(LOCK_KEY, lock('someone-else'));
      expect(plane.clearStaleTeamsInteractionLock()).toBe(false);
      expect(sessionStorage.getItem(LOCK_KEY)).toBe(lock('someone-else'));
      sessionStorage.setItem(LOCK_KEY, JSON.stringify({ type: 'signin' })); // no owner
      expect(plane.clearStaleTeamsInteractionLock()).toBe(false);
      sessionStorage.removeItem(LOCK_KEY);
      expect(plane.clearStaleTeamsInteractionLock()).toBe(false);
      // The browser helper is the same logic for one owner.
      sessionStorage.setItem(LOCK_KEY, lock(A8_CLIENT_ID));
      expect(plane.clearStaleGuestInteractionLock(HOME_CLIENT_ID)).toBe(false);
      expect(plane.clearStaleGuestInteractionLock(A8_CLIENT_ID)).toBe(true);
    });

    it('a client id that is not configured is skipped (the other one still clears)', async () => {
      stubEnv({ VITE_TEAMS_MSAL_CLIENT_ID: '' });
      const { plane } = await load();
      sessionStorage.setItem(LOCK_KEY, lock(A8_CLIENT_ID));
      expect(plane.clearStaleTeamsInteractionLock()).toBe(false); // the A8 client is unknown here
      sessionStorage.setItem(LOCK_KEY, lock(HOME_CLIENT_ID));
      expect(plane.clearStaleTeamsInteractionLock()).toBe(true);
    });
  });

  // ----- review F1 (E): a B2B guest in a listed tenant's Teams -----

  describe('a B2B guest of the Teams tenant gets the guest screen and no BFF call (review F1)', () => {
    const GUEST_HOME_TID = 'ababcdcd-0000-4000-8000-0000000000ab';
    const guestHostAccount = {
      homeAccountId: `guest-oid.${GUEST_HOME_TID}`,
      username: 'jane@home.test',
      tenantId: TID,
      environment: 'login.windows.net',
    };
    const screenText = "This tab can't be used with a guest account";

    function expectGuestScreen(rtl: typeof import('@testing-library/react')): void {
      expect(rtl.screen.getByRole('heading', { level: 1 }).textContent).toBe(screenText);
      expect(document.body.textContent ?? '').toContain('Open Spaarke in Teams signed in to your own organization.');
      expect(rtl.screen.queryByTestId('app')).toBeNull();
      expect(rtl.screen.queryAllByRole('button')).toEqual([]); // nothing browser-only: no Sign in, no Sign out, no chooser
      expect(h.fetches).toEqual([]); // zero BFF calls
    }

    it('bootstrap: the account home tenant differs from the tid -> the guest screen, zero fetches, loaded + failed signals', async () => {
      h.naa[HOME_CLIENT_ID] = { sso: async () => grant('guest', TID, guestHostAccount) };
      const { rtl } = await boot();
      expect(await rtl.screen.findByText(screenText)).toBeTruthy();
      expectGuestScreen(rtl);
      expect(h.notifyAppLoaded).toHaveBeenCalled();
      expect(h.notifyFailure).toHaveBeenCalledTimes(1);
      expect(h.notifyFailure).toHaveBeenCalledWith({
        reason: 'Other',
        message: expect.stringContaining('guest account'),
      });
      // Not shown as a sign-in failure.
      expect(document.body.textContent ?? '').not.toMatch(/sign-in failed/i);
    });

    it('bootstrap: the #EXT# username is the secondary signal (home tenant not readable from homeAccountId)', async () => {
      const ext = { ...JANE, homeAccountId: 'opaque-id', username: 'jane_home.test#EXT#@contoso.onmicrosoft.com' };
      h.naa[HOME_CLIENT_ID] = { sso: async () => grant('guest', TID, ext) };
      const { rtl } = await boot();
      expect(await rtl.screen.findByText(screenText)).toBeTruthy();
      expectGuestScreen(rtl);
    });

    it('bootstrap: a member (home tenant = the tid) is unchanged - the app mounts and the gate probes the BFF as before', async () => {
      h.naa[HOME_CLIENT_ID] = { sso: async () => grant('member', TID, JANE) };
      const { rtl } = await boot();
      expect(await rtl.screen.findByTestId('app')).toBeTruthy();
      expect(rtl.screen.queryByText(screenText)).toBeNull();
      expect(h.notifyFailure).not.toHaveBeenCalled();
    });

    it('bootstrap: a member whose homeAccountId is not oid.tid-shaped is not mistaken for a guest', async () => {
      h.naa[HOME_CLIENT_ID] = { sso: async () => grant('member', TID, { ...JANE, homeAccountId: 'opaque-id' }) };
      const { rtl } = await boot();
      expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    });

    it('recovery: bootstrap fails, the "Sign in" click returns a guest account -> the guest screen replaces the error screen, zero fetches', async () => {
      let attempts = 0;
      h.naa[HOME_CLIENT_ID] = {
        sso: async () => {
          throw new Error('NAA bridge error');
        },
      };
      h.pca[HOME_CLIENT_ID] = {
        popup: async () => {
          attempts += 1;
          if (attempts === 1) throw new Error('popup_window_error');
          return grant('guest-popup', TID, guestHostAccount);
        },
      };
      const { rtl } = await boot();
      const signIn = await rtl.screen.findByRole('button', { name: 'Sign in' });
      expect(h.fetches).toEqual([]);
      rtl.fireEvent.click(signIn);
      expect(await rtl.screen.findByText(screenText)).toBeTruthy();
      expect(attempts).toBe(2);
      expectGuestScreen(rtl);
      expect(rtl.screen.queryByText('Sign-in failed')).toBeNull();
    });

    it('recovery: a member signing in by the click still gets the app (unchanged)', async () => {
      let attempts = 0;
      h.naa[HOME_CLIENT_ID] = {
        sso: async () => {
          throw new Error('NAA bridge error');
        },
      };
      h.pca[HOME_CLIENT_ID] = {
        popup: async () => {
          attempts += 1;
          if (attempts === 1) throw new Error('popup_window_error');
          return grant('member-popup', TID, JANE);
        },
      };
      const { rtl } = await boot();
      rtl.fireEvent.click(await rtl.screen.findByRole('button', { name: 'Sign in' }));
      expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    });

    it('isTeamsGuestAccount: must-fire and must-not-fire', async () => {
      const { msalAuth } = await load();
      const is = msalAuth.isTeamsGuestAccount;
      expect(is({ homeAccountId: `oid.${GUEST_HOME_TID}` }, TID)).toBe(true);
      expect(is({ homeAccountId: `oid.${GUEST_HOME_TID.toUpperCase()}` }, TID)).toBe(true);
      expect(is({ homeAccountId: `oid.${TID}`, username: 'a_b.test#EXT#@x.onmicrosoft.com' }, TID)).toBe(true);
      expect(is({ homeAccountId: `oid.${TID.toUpperCase()}` }, TID)).toBe(false);
      expect(is({ homeAccountId: 'opaque' }, TID)).toBe(false);
      expect(is({ homeAccountId: 'jane.contoso', username: 'jane@contoso.test' }, TID)).toBe(false);
      expect(is({}, TID)).toBe(false);
      expect(is(null, TID)).toBe(false);
      expect(is(undefined, TID)).toBe(false);
    });
  });

  // ----- review F2 (F): theme failure is not "not set up" -----

  describe('a theme failure is not a bootstrap failure (review F2)', () => {
    function blockThemeStorage(): void {
      const original = Storage.prototype.setItem;
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key: string, value: string) {
        if (key === 'spaarke-theme') throw new Error('storage blocked');
        return original.call(this, key, value);
      });
    }

    it('a listed tenant whose theme cannot be stored still gets its backend and the app (not "not set up")', async () => {
      blockThemeStorage();
      h.naa[HOME_CLIENT_ID] = { sso: async () => grant('naa', TID, JANE) };
      const { selection, rtl } = await boot();
      expect(await rtl.screen.findByTestId('app')).toBeTruthy();
      expect(rtl.screen.queryByText('Your organization is not set up for Spaarke')).toBeNull();
      expect(selection.getActiveBackend().customerKey).toBe('contoso');
      expect(h.console.join('\n')).toMatch(/Teams theme could not be applied/);
      expect(h.notifyFailure).not.toHaveBeenCalled();
    });

    it('an unlisted tenant is still not set up when the theme is blocked (the tid read is unaffected)', async () => {
      blockThemeStorage();
      h.getContext.mockResolvedValue({ app: { theme: 'dark' }, user: { tenant: { id: UNLISTED_TID } } });
      const { rtl } = await boot();
      expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
    });

    it('readTeamsPreAuthContext returns the tid when the theme wiring throws', async () => {
      blockThemeStorage();
      const adapterMod = await import('../src/host/TeamsHostAdapter');
      await expect(adapterMod.readTeamsPreAuthContext()).resolves.toEqual({ tenantId: TID });
    });
  });

  // ----- review F1 (D): the Teams sign-in screen hint is no longer the A8 wording -----

  it('the Teams sign-in error screen says "for your organization", not "in Spaarke\'s directory"', async () => {
    const React = await import('react');
    const rtl = await import('@testing-library/react');
    const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
    const { TeamsAuthError } = await import('../src/components/auth/TeamsAuthError');
    rtl.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(TeamsAuthError, { message: 'failed', diagnostics: 'x', onSignIn: async () => undefined })
      )
    );
    const text = document.body.textContent ?? '';
    expect(text).toContain('Sign in with your work account for your organization.');
    expect(text).not.toMatch(/Spaarke's directory/);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// AC3: registration in Teams - NAA silent link -> form; the popup only from the "Link my account" click
// ---------------------------------------------------------------------------------------------------------------

const REG_REQUIRED: Reply = {
  status: 403,
  json: {
    title: 'Forbidden',
    status: 403,
    reasonCode: 'sdap.access.registration_required',
    givenName: 'Jane',
    familyName: 'Doe',
    email: 'jane@contoso.test',
  },
};
const ME_OK: Reply = { status: 200, json: { roles: [], entitlements: {} } };
const LINKED: Reply = { status: 200, json: { systemUserId: 's-1', outcome: 'linked' } };
const REGISTERED: Reply = { status: 201, json: { contactId: 'c-2', systemUserId: null, outcome: 'created' } };
const problem = (status: number, reasonCode: string): Reply => ({
  status,
  json: { title: 'x', status, reasonCode, errorCode: reasonCode },
});
const ME = 'GET /api/v1/external/me';
const REGISTRATION = 'POST /api/v1/external/registration';
const LINK = 'POST /api/v1/external/workforce/link';

describe('Registration in Teams (AC3): the 058 gate runs on the Teams home plane', () => {
  /** The Teams bootstrap's steps (context -> directory -> adapter), then the gate over a marker child. */
  async function renderTeamsGate(
    options: { guestNaa?: Behaviour; guestPopup?: Behaviour; naaRecommended?: boolean } = {}
  ) {
    stubEnv();
    // The home client: the Teams user's account is already on the NAA instance, so BFF tokens come from acquireTokenSilent.
    h.naa[HOME_CLIENT_ID] = { accounts: [JANE] };
    if (options.guestNaa) h.naa[A8_CLIENT_ID] = options.guestNaa;
    h.pca[A8_CLIENT_ID] = options.guestPopup ?? {};
    h.naaRecommended.mockReturnValue(options.naaRecommended ?? true);
    const mods = await load();
    const adapterMod = await import('../src/host/TeamsHostAdapter');
    const { tenantId } = await adapterMod.readTeamsPreAuthContext();
    const selected = await mods.selection.resolveHomeBackendSelection(tenantId as string);
    if (selected.kind !== 'selected') throw new Error(`unexpected selection: ${selected.kind}`);
    mods.selection.activateBackend('workforce', selected.backend);
    const adapter = adapterMod.createTeamsHostAdapter(tenantId as string);
    await adapter.initialize();
    h.msal = {
      accounts: adapter.getMsalInstance().getAllAccounts(),
      instance: adapter.getMsalInstance(),
      inProgress: 'none',
    };

    const rtl = await import('@testing-library/react');
    const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
    const { WorkforceHomeGate } = await import('../src/components/auth/WorkforceHomeGate');
    const onReady = vi.fn();
    const view = rtl.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(
          WorkforceHomeGate,
          { onReady } as any,
          React.createElement('div', { 'data-testid': 'workspace' }, 'workspace')
        )
      )
    );
    return { ...mods, ...rtl, view, onReady, adapter };
  }

  // NAA-honest: the bridge answers with the Teams HOST's account (JANE, the Teams tenant); the token was issued by Spaarke.
  const guestSilent = (): Behaviour => ({ sso: async () => grant('guest-token', SPAARKE_TENANT, JANE) });
  const a8Instances = () => h.instances.filter(i => i.config.auth.clientId === A8_CLIENT_ID);

  async function fillAndSubmit(g: Awaited<ReturnType<typeof renderTeamsGate>>) {
    g.fireEvent.change(await g.screen.findByLabelText(/first name/i), { target: { value: 'Jane' } });
    g.fireEvent.change(g.screen.getByLabelText(/last name/i), { target: { value: 'Doe' } });
    g.fireEvent.click(g.screen.getByRole('button', { name: /^continue$/i }));
  }

  it('already registered: /me answers with the NAA home token, the workspace shows, no guest client is built', async () => {
    h.script = { [ME]: [ME_OK] };
    const g = await renderTeamsGate();
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();

    expect(calls()).toEqual([ME]);
    expect(h.fetches[0].url).toBe(`${CONTOSO.bffBaseUrl}/api/v1/external/me`);
    expect(h.fetches[0].headers.Authorization).toBe(`Bearer home-token:${CONTOSO_SCOPE}`);
    expect(home().every(r => r.kind === 'naa' && r.request.authority === authorityOf(TID))).toBe(true);
    expect(a8Instances()).toEqual([]);
    expect(g.onReady).toHaveBeenLastCalledWith(true);
  });

  it('registration_required -> the silent link through NAA (A8 client, Spaarke authority per request, memory cache) -> link POST -> /me', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
    const g = await renderTeamsGate({ guestNaa: guestSilent() });
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();

    expect(calls()).toEqual([ME, LINK, ME]);
    // The silent guest request: NAA instance of the A8 client, Spaarke authority on the request, the entry's scope. The
    // loginHint is passed anyway (a live NAA bridge drops it; a bridge-less standard instance uses it).
    const guest = h.requests.filter(r => r.clientId === A8_CLIENT_ID);
    expect(guest.map(r => `${r.kind}:${r.call}`)).toEqual(['naa:ssoSilent']);
    expect(guest[0].request).toStrictEqual({
      scopes: [CONTOSO_SCOPE],
      authority: authorityOf(SPAARKE_TENANT),
      loginHint: 'jane@contoso.test',
    });
    const guestNaa = a8Instances().find(i => i.kind === 'naa')!;
    expect(guestNaa.config.auth.authority).toBe(authorityOf(SPAARKE_TENANT));
    expect(guestNaa.config.cache.cacheLocation).toBe('memoryStorage');
    // The guest token is in the link body only - never the Bearer, never a log, never storage.
    expect(bodies(LINK)).toEqual([{ guestAccessToken: 'tok-guest-token' }]);
    for (const f of h.fetches) expect(JSON.stringify(f.headers)).not.toContain('tok-guest-token');
    expect(h.fetches[1].headers.Authorization).toBe(`Bearer home-token:${CONTOSO_SCOPE}`);
    expect(h.console.join('\n')).not.toContain('tok-guest-token');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('tok-guest-token');
    expect(JSON.stringify({ ...localStorage })).not.toContain('tok-guest-token');
    // No popup anywhere on this path.
    expect(h.requests.filter(r => r.call === 'acquireTokenPopup')).toEqual([]);
  });

  it('cleanup is account-scoped and an NAA cleanup throw is caught: the link still completes; no clearCache without an account', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
    const g = await renderTeamsGate({ guestNaa: guestSilent() });
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();

    expect(h.clears.length).toBeGreaterThan(0); // cleanup was attempted (and the fake NAA clearCache threw)
    for (const c of h.clears) expect(c.request?.account).toBeTruthy(); // never the no-account clearCache()
    expect(calls()).toEqual([ME, LINK, ME]);
  });

  it('the guest NAA instance is fresh per call (its memory cache cannot serve a later request)', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
    const g = await renderTeamsGate({ guestNaa: guestSilent() });
    await g.screen.findByTestId('workspace');
    const { msalAuth } = g;
    const before = a8Instances().filter(i => i.kind === 'naa').length;
    await msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', CONTOSO_SCOPE);
    expect(a8Instances().filter(i => i.kind === 'naa').length).toBe(before + 1);
  });

  it('NAA silent failure -> the profile form (no popup, no link POST, no registration POST yet)', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderTeamsGate({
      guestNaa: {
        sso: async () => {
          throw new Error('NAA refused a cross-tenant request');
        },
      },
    });
    const first = (await g.screen.findByLabelText(/first name/i)) as HTMLInputElement;
    expect(first.value).toBe('Jane');
    expect((g.screen.getByLabelText(/email/i) as HTMLInputElement).readOnly).toBe(true);
    expect(calls()).toEqual([ME]);
    expect(h.requests.filter(r => r.call === 'acquireTokenPopup')).toEqual([]);
    expect(g.onReady).toHaveBeenLastCalledWith(false);
  });

  it('the silent step is the only attempt before the form: the popup decision is the single named switch point', async () => {
    const gateMod = await import('../src/components/auth/WorkforceHomeGate');
    const prefill = { givenName: 'Jane', familyName: 'Doe', email: 'jane@contoso.test' };
    expect(gateMod.afterSilentLinkFailure(prefill)).toEqual({ kind: 'register', prefill });
    const src = fs.readFileSync(path.resolve(__dirname, '../src/components/auth/WorkforceHomeGate.tsx'), 'utf8');
    expect(src).not.toMatch(/acquireSpaarkeGuestToken\(\s*'popup'/); // the gate never opens the popup itself
  });

  it('a guest result that is not from the Spaarke tenant is null: the form shows and nothing is linked', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderTeamsGate({ guestNaa: { sso: async () => grant('wrong-tenant', TID, JANE) } });
    expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
    expect(calls()).toEqual([ME]);
    expect(h.console.join('\n')).not.toContain('tok-wrong-tenant');
  });

  it('a guest result with no tenant at all is null too', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderTeamsGate({ guestNaa: { sso: async () => ({ accessToken: 'tok-x', account: null }) } });
    expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
    expect(calls()).toEqual([ME]);
  });

  // Review F4: the guest check reads the TOKEN's tenant (idTokenClaims.tid, else the access token's tid). Under NAA
  // result.tenantId is the Teams HOST account's tenant (here TID), whatever tenant issued the token.
  describe('the guest token tenant is the tid claim, not result.tenantId (review F4)', () => {
    it('result.tenantId = the Teams tid, tid claim = Spaarke: accepted and linked', async () => {
      h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
      const hostTenantIsTeamsTid = grant('guest-token', SPAARKE_TENANT, JANE);
      expect(hostTenantIsTeamsTid.tenantId).toBe(TID); // the NAA-honest shape the real bridge produces (see tests/teams-real-msal)
      const g = await renderTeamsGate({ guestNaa: { sso: async () => hostTenantIsTeamsTid } });
      expect(await g.screen.findByTestId('workspace')).toBeTruthy();
      expect(bodies(LINK)).toEqual([{ guestAccessToken: 'tok-guest-token' }]);
    });

    it('the reverse - result.tenantId = Spaarke, tid claim = the Teams tid: refused, nothing is linked, the form shows', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      const g = await renderTeamsGate({
        guestNaa: { sso: async () => ({ ...grant('wrong-token', TID, JANE), tenantId: SPAARKE_TENANT }) },
      });
      expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
      expect(calls()).toEqual([ME]);
      expect(h.console.join('\n')).toContain('tenant_mismatch');
      expect(h.console.join('\n')).not.toContain('tok-wrong-token');
    });

    it('no id token claims: the access token payload tid (Spaarke) is used and the token is linked', async () => {
      h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
      const token = jwtFor({ tid: SPAARKE_TENANT, aud: 'api://x' });
      const g = await renderTeamsGate({
        guestNaa: { sso: async () => ({ accessToken: token, tenantId: TID, account: JANE }) },
      });
      expect(await g.screen.findByTestId('workspace')).toBeTruthy();
      expect(bodies(LINK)).toEqual([{ guestAccessToken: token }]);
    });

    it('no id token claims and the access token is for the Teams tenant: refused', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      const token = jwtFor({ tid: TID });
      const g = await renderTeamsGate({
        guestNaa: { sso: async () => ({ accessToken: token, tenantId: SPAARKE_TENANT, account: JANE }) },
      });
      expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
      expect(calls()).toEqual([ME]);
    });

    it('no tenant from either source: fail closed (null) even when result.tenantId says Spaarke', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      const g = await renderTeamsGate({
        guestNaa: { sso: async () => ({ accessToken: 'opaque', tenantId: SPAARKE_TENANT, account: JANE }) },
      });
      expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
      expect(calls()).toEqual([ME]);
    });
  });

  // Review F2: every null path leaves a short reason code (never a token or a claim), and the silent attempt is bounded.
  describe('the silent guest link leaves a reason code on every null path and is bounded (review F2)', () => {
    it('a bridge error warns with the error code only: not the message, the token or any claim', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      const g = await renderTeamsGate({
        guestNaa: {
          sso: async () => {
            throw Object.assign(new Error('boom tok-leak jane@contoso.test'), { errorCode: 'bridge_error_x' });
          },
        },
      });
      expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
      const warned = h.console.join('\n');
      expect(warned).toContain('bridge_error_x');
      expect(warned).not.toContain('tok-leak');
      expect(warned).not.toContain('boom');
    });

    it('an error with no code warns with a generic code', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      const g = await renderTeamsGate({
        guestNaa: {
          sso: async () => {
            throw new Error('NAA refused: secret-detail');
          },
        },
      });
      await g.screen.findByLabelText(/first name/i);
      expect(h.console.join('\n')).toMatch(/Teams silent link failed: error/);
      expect(h.console.join('\n')).not.toContain('secret-detail');
    });

    it('a tenant mismatch warns tenant_mismatch and no token is written to the console', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      const g = await renderTeamsGate({ guestNaa: { sso: async () => grant('wrong-tenant', TID, JANE) } });
      await g.screen.findByLabelText(/first name/i);
      expect(h.console.join('\n')).toContain('tenant_mismatch');
      expect(h.console.join('\n')).not.toContain('tok-wrong-tenant');
    });

    it('a bridge that never answers: null after 10 s (fake timers), with the timeout reason', async () => {
      stubEnv();
      h.naa[A8_CLIENT_ID] = { sso: () => new Promise(() => undefined) };
      const { msalAuth } = await load();
      msalAuth.setGuestLinkHost('teams');
      vi.useFakeTimers();
      try {
        const run = msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', CONTOSO_SCOPE);
        let settled = false;
        void run.then(() => {
          settled = true;
        });
        await vi.advanceTimersByTimeAsync(9_999);
        expect(settled).toBe(false);
        await vi.advanceTimersByTimeAsync(1);
        await expect(run).resolves.toBeNull();
        expect(h.console.join('\n')).toContain('timeout');
      } finally {
        vi.useRealTimers();
      }
    });

    it('a late answer after the timeout is discarded, and the abandoned attempt still cleans its instance up', async () => {
      stubEnv();
      let answer: (value: unknown) => void = () => undefined;
      h.naa[A8_CLIENT_ID] = {
        sso: () =>
          new Promise(resolve => {
            answer = resolve;
          }),
      };
      const { msalAuth } = await load();
      msalAuth.setGuestLinkHost('teams');
      vi.useFakeTimers();
      try {
        const run = msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', CONTOSO_SCOPE);
        await vi.advanceTimersByTimeAsync(10_000);
        await expect(run).resolves.toBeNull();
        expect(h.clears).toEqual([]);
        answer(grant('late', SPAARKE_TENANT, JANE));
        await vi.advanceTimersByTimeAsync(0);
        expect(h.clears.length).toBeGreaterThan(0);
        for (const c of h.clears) expect(c.request?.account).toBeTruthy(); // account-scoped cleanup ran for the late result
      } finally {
        vi.useRealTimers();
      }
    });

    it('in the gate: the form shows after 10 s instead of "Linking your account..." forever', async () => {
      h.script = { [ME]: [REG_REQUIRED] };
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        const g = await renderTeamsGate({ guestNaa: { sso: () => new Promise(() => undefined) } });
        expect(await g.screen.findByText('Linking your account...')).toBeTruthy();
        expect(g.screen.queryByLabelText(/first name/i)).toBeNull();
        await vi.advanceTimersByTimeAsync(10_000);
        expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
        expect(calls()).toEqual([ME]);
      } finally {
        vi.useRealTimers();
      }
    });
  });

  // Cheap known-limit fixes.
  it('the standard guest instance is prewarmed when the NAA silent attempt returns null (so the click is not spent initializing)', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderTeamsGate({
      guestNaa: {
        sso: async () => {
          throw new Error('NAA refused');
        },
      },
    });
    await g.screen.findByLabelText(/first name/i);
    const pca = h.instances.filter(i => i.kind === 'pca' && i.config.auth.clientId === A8_CLIENT_ID);
    expect(pca).toHaveLength(1);
    expect(pca[0].instance.initialize).toHaveBeenCalled();
    expect(pca[0].config.cache.cacheLocation).toBe('memoryStorage');
    expect(h.requests.filter(r => r.kind === 'pca' && r.clientId === A8_CLIENT_ID)).toEqual([]); // built, never asked
  });

  it('no prewarm when the silent link produced a token', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
    const g = await renderTeamsGate({ guestNaa: guestSilent() });
    await g.screen.findByTestId('workspace');
    expect(h.instances.filter(i => i.kind === 'pca' && i.config.auth.clientId === A8_CLIENT_ID)).toEqual([]);
  });

  it('the host does not recommend NAA: the silent link is skipped (no hidden iframe), the form shows', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    // Home token path without NAA: the standard instance with the cached account serves it silently.
    const g = await (async () => {
      stubEnv();
      h.pca[HOME_CLIENT_ID] = { accounts: [JANE] };
      h.pca[A8_CLIENT_ID] = {};
      h.naa[A8_CLIENT_ID] = guestSilent();
      h.naaRecommended.mockReturnValue(false);
      const mods = await load();
      const adapterMod = await import('../src/host/TeamsHostAdapter');
      const selected = await mods.selection.resolveHomeBackendSelection(TID);
      if (selected.kind !== 'selected') throw new Error('unexpected');
      mods.selection.activateBackend('workforce', selected.backend);
      const adapter = adapterMod.createTeamsHostAdapter(TID);
      await adapter.initialize();
      h.msal = {
        accounts: adapter.getMsalInstance().getAllAccounts(),
        instance: adapter.getMsalInstance(),
        inProgress: 'none',
      };
      const rtl = await import('@testing-library/react');
      const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
      const { WorkforceHomeGate } = await import('../src/components/auth/WorkforceHomeGate');
      rtl.render(
        React.createElement(
          FluentProvider,
          { theme: webLightTheme },
          React.createElement(WorkforceHomeGate, {} as any, React.createElement('div', null, 'workspace'))
        )
      );
      return rtl;
    })();
    expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
    expect(a8Instances().filter(i => i.kind === 'naa')).toEqual([]);
    expect(h.requests.filter(r => r.clientId === A8_CLIENT_ID)).toEqual([]);
    expect(calls()).toEqual([ME]);
  });

  it('licensed_user_link_required: the "Link my account" click opens the popup on the standard guest instance; then link POST and /me', async () => {
    h.script = {
      [ME]: [REG_REQUIRED, ME_OK],
      [REGISTRATION]: [problem(403, 'sdap.access.licensed_user_link_required')],
      [LINK]: [LINKED],
    };
    const g = await renderTeamsGate({
      guestNaa: {
        sso: async () => {
          throw new Error('NAA refused');
        },
      },
      guestPopup: { popup: async () => grant('guest-popup', SPAARKE_TENANT, GUEST_ACCOUNT) },
    });
    await fillAndSubmit(g);
    const button = await g.screen.findByRole('button', { name: /link my account/i });
    expect(h.requests.filter(r => r.call === 'acquireTokenPopup')).toEqual([]); // not before the click
    g.fireEvent.click(button);
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();

    const popup = h.requests.find(r => r.call === 'acquireTokenPopup')!;
    expect(popup.kind).toBe('pca'); // the standard instance, never NAA
    expect(popup.clientId).toBe(A8_CLIENT_ID);
    expect(popup.request).toStrictEqual({ scopes: [CONTOSO_SCOPE], loginHint: 'jane@contoso.test' });
    const pca = h.instances.find(i => i.kind === 'pca' && i.config.auth.clientId === A8_CLIENT_ID)!;
    expect(pca.config.cache.cacheLocation).toBe('memoryStorage');
    expect(pca.config.auth.authority).toBe(authorityOf(SPAARKE_TENANT));
    expect(calls()).toEqual([ME, REGISTRATION, LINK, ME]);
    expect(bodies(LINK)).toEqual([{ guestAccessToken: 'tok-guest-popup' }]);
    expect(h.console.join('\n')).not.toContain('tok-guest-popup');
    for (const c of h.clears) expect(c.request?.account).toBeTruthy();
  });

  it('no silent attempt can open a popup: the ladder is NAA silent, then the form, then (on a click only) the popup', async () => {
    h.script = { [ME]: [REG_REQUIRED], [REGISTRATION]: [REGISTERED] };
    const g = await renderTeamsGate({
      guestNaa: {
        sso: async () => {
          throw new Error('NAA refused');
        },
      },
    });
    await g.screen.findByLabelText(/first name/i);
    expect(h.requests.map(r => r.call).filter(c => c === 'acquireTokenPopup')).toEqual([]);
    expect(h.requests.filter(r => r.clientId === A8_CLIENT_ID).map(r => `${r.kind}:${r.call}`)).toEqual([
      'naa:ssoSilent',
    ]);
  });

  it('a contact (not a licensed user) registers in Teams: the form posts, then /me shows the workspace; no guest popup, no link', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [REGISTRATION]: [REGISTERED] };
    const g = await renderTeamsGate({
      guestNaa: {
        sso: async () => {
          throw new Error('not a guest');
        },
      },
    });
    await fillAndSubmit(g);
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
    expect(calls()).toEqual([ME, REGISTRATION, ME]);
    expect(h.requests.filter(r => r.call === 'acquireTokenPopup')).toEqual([]);
  });

  it('the A8 client is used in Teams ONLY for the guest link: every A8 request is the guest silent/popup, none is a BFF Bearer', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
    const g = await renderTeamsGate({ guestNaa: guestSilent() });
    await g.screen.findByTestId('workspace');
    for (const f of h.fetches) expect(f.headers.Authorization).toBe(`Bearer home-token:${CONTOSO_SCOPE}`);
    for (const r of h.requests.filter(r => r.clientId === A8_CLIENT_ID))
      expect(r.request.authority).toBe(authorityOf(SPAARKE_TENANT));
  });

  it('CIAM is unchanged: applying the CIAM plane clears the home flag, and the gate does nothing', async () => {
    const { plane, selection } = await load();
    selection.activateBackend('ciam', { ...CONTOSO });
    plane.applyStandalonePlane(plane.resolveStandalonePlane('ciam'));
    const { WorkforceHomeGate, isHomeGateActive } = await import('../src/components/auth/WorkforceHomeGate');
    expect(isHomeGateActive()).toBe(false);
    const rtl = await import('@testing-library/react');
    const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
    rtl.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(WorkforceHomeGate, {} as any, React.createElement('div', { 'data-testid': 'ciam' }, 'x'))
      )
    );
    expect(await rtl.screen.findByTestId('ciam')).toBeTruthy();
    expect(h.fetches).toEqual([]);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// AC5 and the 058 carry-over: shipped files
// ---------------------------------------------------------------------------------------------------------------

const SPA_ROOT = path.resolve(__dirname, '..');
const REPO_ROOT = path.resolve(SPA_ROOT, '../../..');

function listFiles(dir: string): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...listFiles(full));
    else out.push(full);
  }
  return out;
}

describe('Shipped files (AC4)', () => {
  it('no getAuthToken call and no Teams JS authentication import anywhere in src/', () => {
    const offenders = listFiles(path.join(SPA_ROOT, 'src'))
      .filter(f => /\.(ts|tsx)$/.test(f))
      .filter(f => {
        const text = fs.readFileSync(f, 'utf8');
        return (
          /getAuthToken\s*\(/.test(text) ||
          /import\s*\{[^}]*\bauthentication\b[^}]*\}\s*from\s*'@microsoft\/teams-js'/.test(text)
        );
      })
      .map(f => path.relative(SPA_ROOT, f));
    expect(offenders).toEqual([]);
  });

  it('no webApplicationInfo key in any appPackage JSON file (the env files only mention it in a $comment)', () => {
    const hasKey = (value: unknown): boolean =>
      !!value &&
      typeof value === 'object' &&
      Object.entries(value as Record<string, unknown>).some(([k, v]) => k === 'webApplicationInfo' || hasKey(v));
    const files = listFiles(path.join(SPA_ROOT, 'appPackage')).filter(f => /\.json$/i.test(f));
    expect(files.length).toBeGreaterThanOrEqual(3); // manifest.json, manifest.prod.json and the env files
    const offenders = files
      .filter(f => hasKey(JSON.parse(fs.readFileSync(f, 'utf8'))))
      .map(f => path.relative(SPA_ROOT, f));
    expect(offenders).toEqual([]);
  });

  it('the adapter keeps no unused notifyTeamsSuccess export and no stale /join comment (058 carry-over)', () => {
    const text = fs.readFileSync(path.join(SPA_ROOT, 'src/host/TeamsHostAdapter.ts'), 'utf8');
    expect(text).not.toContain('notifyTeamsSuccess');
    expect(text).not.toContain('/join');
  });

  it('spec.md and design.md no longer describe a /join page (058 carry-over): the profile form at first sign-in', () => {
    const projectDir = path.join(REPO_ROOT, 'projects/spaarke-SPA-external-access-platform-r3');
    for (const file of ['spec.md', 'design.md']) {
      const text = fs.readFileSync(path.join(projectDir, file), 'utf8');
      expect(text, file).not.toContain('external.spaarke.com/join');
      expect(text, file).not.toMatch(/from the join link|new join page|shares a join link/i);
    }
  });

  it('the Teams strategy reads the multitenant client, never the A8 client config, for the BFF token', () => {
    const adapter = fs.readFileSync(path.join(SPA_ROOT, 'src/host/TeamsHostAdapter.ts'), 'utf8');
    expect(adapter).toContain('getWorkforceHomeClientConfig');
    expect(adapter).not.toContain('getWorkforceClientConfig');
  });
});
