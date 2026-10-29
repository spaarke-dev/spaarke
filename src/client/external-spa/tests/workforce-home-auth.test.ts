// Workforce HOME sign-in (spaarke-SPA-external-access-platform-r3 task 058; ADR-028 Amendment A9; design
// notes/a9-bff-design.md section 5 and 7).
//
// A workforce contact signs in in the browser with their COMPANY account on ONE multitenant client (authority
// `organizations`, used for discovery/login only). The token's `tid` picks the directory entry (homeTenantIds) that
// names the customer BFF; only then is the BFF token requested, with authority /{tid} and the entry's scope. An unlisted
// tid shows "not set up" and sends no token and no request. After sign-in the SPA handles registration_required: a
// silent link with the Spaarke-tenant guest token (A8 client, memory cache), else the profile form; a licensed user
// whose silent attempt failed links through a popup that opens only from a click.
//
// MSAL, Teams JS and fetch are fakes that RECORD what the code asks for (instance configs, token requests, URLs,
// headers, bodies), so the assertions are about requests, not stub echoes. Components are rendered with
// React.createElement (this is a .ts test).
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.setConfig({ testTimeout: 60000 });

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const A8_CLIENT_ID = '11111111-2222-3333-4444-555555555555';
const HOME_CLIENT_ID = '66666666-7777-8888-9999-000000000001';
const CIAM_TENANT = '00000000-0000-0000-0000-000000000141';
/** The tenant id of personal Microsoft accounts: never a customer's home directory. */
const CONSUMER_TENANT = '9188040d-6c67-4c5b-b112-36a304b66dad';
const CONTOSO_TID = 'c0c0c0c0-0000-4000-8000-000000000001';
const FABRIKAM_TID = 'f0f0f0f0-0000-4000-8000-000000000002';
const UNLISTED_TID = 'dddddddd-0000-4000-8000-000000000003';

const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
  homeTenantIds: [CONTOSO_TID],
};
const FABRIKAM = {
  customerKey: 'fabrikam',
  displayName: 'Fabrikam',
  bffBaseUrl: 'https://fabrikam-bff.test',
  bffAppId: 'bbbbbbbb-0000-0000-0000-00000000000b',
  homeTenantIds: [FABRIKAM_TID],
};
const scopeOf = (appId: string) => `api://${appId}/user_impersonation`;
const tokenAuthority = (tid: string) => `https://login.microsoftonline.com/${tid}`;
const DISCOVERY_AUTHORITY = 'https://login.microsoftonline.com/organizations';
// Multi-tenant aliases that must never appear as an authority. Assembled so this file, too, never spells the literal.
const COMMON_AUTHORITY = ['https://login.microsoftonline.com', 'common'].join('/');
const CONSUMERS_AUTHORITY = ['https://login.microsoftonline.com', 'consumers'].join('/');
// The retired A2-era scope name, assembled for the same reason (workforce-auth.test.ts does it too).
const RETIRED_SCOPE = ['access', 'as', 'user'].join('_');
const REALM_KEY = 'spaarke.ext.realm';
const INVITE_CODE_STORAGE_KEY = 'spaarke.ext.inviteCode';
const INVITE_CODE = 'A'.repeat(43);

const JANE = {
  homeAccountId: 'jane.contoso',
  username: 'jane@contoso.test',
  name: 'Jane Doe',
  tenantId: CONTOSO_TID,
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
  requests: [] as Array<{ instance: string; call: string; request: any }>,
  // Every standard PublicClientApplication constructed (config + the fake), in order.
  pcas: [] as Array<{ config: any; instance: any }>,
  // Behaviour of the fake instances, keyed by the client id they were built with.
  byClient: {} as Record<string, any>,
  // Every fetch: URL, method, headers and raw body.
  fetches: [] as Array<{ url: string; method: string; headers: Record<string, string>; body: string | undefined }>,
  // Scripted BFF replies, keyed "METHOD /path"; each call takes the next one, the last repeats.
  script: {} as Record<string, any[]>,
  unexpected: [] as string[],
  // What useMsal() returns for the components under test.
  msal: { accounts: [] as any[], instance: {} as any, inProgress: 'none' },
  teamsInitialize: vi.fn(),
  console: [] as string[],
}));

function makeFakeInstance(name: string, behaviour: Behaviour, config: any): any {
  let active: any = null;
  const accounts: any[] = [...(behaviour.accounts ?? [])];
  const record = (call: string, request: any) => h.requests.push({ instance: name, call, request });
  const remember = (result: any) => {
    if (result?.account && !accounts.includes(result.account)) accounts.push(result.account);
    return result;
  };
  return {
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
          : { accessToken: `home-token:${req.scopes[0]}`, account: accounts[0] ?? null }
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
    logoutRedirect: vi.fn(async (req: any) => {
      record('logoutRedirect', req);
    }),
    // Like the real library's account form: removes that account from THIS instance only. The no-account form is
    // never faked as harmless - msal-browser 5.5.0 wipes every "msal" sessionStorage key with it - so tests assert it is
    // never used (expectAccountScopedClears); the real-library proof is tests/guest-token-cache-isolation.test.ts.
    clearCache: vi.fn(async (request?: { account?: any }) => {
      const index = request?.account ? accounts.indexOf(request.account) : -1;
      if (index >= 0) accounts.splice(index, 1);
    }),
  };
}

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    constructor(config: any) {
      const behaviour = (h.byClient[config?.auth?.clientId] as Behaviour) ?? {};
      const instance = makeFakeInstance(`pca#${h.pcas.length}`, behaviour, config);
      h.pcas.push({ config, instance });
      return instance;
    }
  }
  return { ...actual, PublicClientApplication: FakePublicClientApplication };
});

vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: React.ReactNode }) => children,
  useMsal: () => h.msal,
}));

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: h.teamsInitialize,
    getContext: vi.fn(),
    notifyAppLoaded: vi.fn(),
    notifySuccess: vi.fn(),
    notifyFailure: vi.fn(),
    registerOnThemeChangeHandler: vi.fn(),
    FailedReason: { AuthFailed: 'AuthFailed', Timeout: 'Timeout', Other: 'Other' },
  },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => true) },
  authentication: { getAuthToken: vi.fn() },
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

/** Fresh copies of the auth modules (memoized MSAL instances and active-plane seams do not leak between tests). */
async function loadAuth() {
  vi.resetModules();
  const config = await import('../src/config');
  const msalConfig = await import('../src/auth/msal-config');
  const msalAuth = await import('../src/auth/msal-auth');
  const selection = await import('../src/auth/backend-selection');
  const plane = await import('../src/auth/standalone-plane');
  return { config, msalConfig, msalAuth, selection, plane };
}

const bffPath = (url: string) => new URL(url).pathname;

beforeEach(() => {
  h.requests.length = 0;
  h.pcas.length = 0;
  h.byClient = {};
  h.fetches.length = 0;
  h.script = {};
  h.unexpected.length = 0;
  h.console.length = 0;
  h.msal = { accounts: [], instance: {}, inProgress: 'none' };
  h.teamsInitialize.mockReset();
  h.teamsInitialize.mockRejectedValue(new Error('not in Teams'));
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
      const key = `${method} ${bffPath(url)}`;
      const queue = h.script[key];
      if (!queue || queue.length === 0) {
        h.unexpected.push(key);
        return new Response('unexpected', { status: 599 });
      }
      const reply: Reply = queue.length > 1 ? queue.shift() : queue[0];
      const body = reply.json !== undefined ? JSON.stringify(reply.json) : (reply.text ?? '{}');
      return new Response(body, { status: reply.status, headers: { 'Content-Type': 'application/json' } });
    })
  );
  // Every console channel is captured: a token must never reach a log (hard rule).
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
  window.history.replaceState(null, '', '/');
});

/** No BFF token was requested and nothing was fetched: the "not set up" guarantee. */
function expectNoTokenRequestAndNoFetch(): void {
  expect(h.requests, 'token / login requests').toEqual([]);
  expect(h.fetches, 'fetches').toEqual([]);
}

// ---------------------------------------------------------------------------------------------------------------
// AC6: the multitenant assert
// ---------------------------------------------------------------------------------------------------------------

describe('The multitenant authority assert accepts organizations / a tenant GUID ONLY for the multitenant client', () => {
  it('accepts organizations and a tenant-GUID authority for the multitenant client id', async () => {
    stubEnv();
    const { msalConfig } = await loadAuth();
    expect(() => msalConfig.assertWorkforceHomeAuthority(DISCOVERY_AUTHORITY, HOME_CLIENT_ID)).not.toThrow();
    expect(() => msalConfig.assertWorkforceHomeAuthority(tokenAuthority(CONTOSO_TID), HOME_CLIENT_ID)).not.toThrow();
    // Client ids compare case-insensitively.
    expect(() =>
      msalConfig.assertWorkforceHomeAuthority(DISCOVERY_AUTHORITY, HOME_CLIENT_ID.toUpperCase())
    ).not.toThrow();
  });

  it('refuses organizations and a tenant GUID for the A8 client id (the single-tenant client stays tenant-pinned)', async () => {
    stubEnv();
    const { msalConfig } = await loadAuth();
    expect(() => msalConfig.assertWorkforceHomeAuthority(DISCOVERY_AUTHORITY, A8_CLIENT_ID)).toThrow(/multitenant/i);
    expect(() => msalConfig.assertWorkforceHomeAuthority(tokenAuthority(CONTOSO_TID), A8_CLIENT_ID)).toThrow(
      /multitenant/i
    );
    // ... and through the builder: the A8 workforce kind still refuses organizations, exactly as before.
    expect(() =>
      msalConfig.buildMsalConfiguration({
        kind: 'workforce',
        clientId: A8_CLIENT_ID,
        bffScope: scopeOf('99999999-8888-7777-6666-555555555555'),
        authority: DISCOVERY_AUTHORITY,
      })
    ).toThrow(/tenant-pinned/);
    // The home kind with the A8 client id is refused too.
    expect(() =>
      msalConfig.buildMsalConfiguration({
        kind: 'workforceHome',
        clientId: A8_CLIENT_ID,
        bffScope: '',
        authority: DISCOVERY_AUTHORITY,
      })
    ).toThrow(/multitenant/i);
  });

  it.each([
    ['the common alias', COMMON_AUTHORITY],
    ['the consumers alias', CONSUMERS_AUTHORITY],
    ['a non-GUID tenant', 'https://login.microsoftonline.com/contoso.onmicrosoft.com'],
    ['a v2.0 suffix', `${tokenAuthority(CONTOSO_TID)}/v2.0`],
    ['another host', `https://evil.example/${CONTOSO_TID}`],
    ['plain http', `http://login.microsoftonline.com/${CONTOSO_TID}`],
    ['no path', 'https://login.microsoftonline.com'],
  ])('refuses %s for BOTH clients (the multitenant client id and the A8 client id)', async (_label, authority) => {
    stubEnv();
    const { msalConfig } = await loadAuth();
    expect(() => msalConfig.assertWorkforceHomeAuthority(authority, HOME_CLIENT_ID)).toThrow();
    expect(() => msalConfig.assertWorkforceHomeAuthority(authority, A8_CLIENT_ID)).toThrow();
  });

  it('fails closed when the multitenant client id is not configured, and for an unknown client id', async () => {
    stubEnv({ VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: '' });
    const unset = await loadAuth();
    expect(() => unset.msalConfig.assertWorkforceHomeAuthority(DISCOVERY_AUTHORITY, HOME_CLIENT_ID)).toThrow();
    vi.unstubAllEnvs();
    stubEnv();
    const { msalConfig } = await loadAuth();
    expect(() =>
      msalConfig.assertWorkforceHomeAuthority(DISCOVERY_AUTHORITY, '99999999-0000-0000-0000-000000000009')
    ).toThrow();
  });

  it('builds the home MSAL configuration on organizations: session cache, origin redirect, no BFF scope, no known authorities', async () => {
    stubEnv();
    const { msalConfig } = await loadAuth();
    const descriptor = msalConfig.workforceHomeAuthorityConfig({ clientId: HOME_CLIENT_ID });
    expect(descriptor).toEqual({
      kind: 'workforceHome',
      clientId: HOME_CLIENT_ID,
      bffScope: '',
      authority: DISCOVERY_AUTHORITY,
    });
    const built = msalConfig.buildMsalConfiguration(descriptor);
    expect(built.auth.authority).toBe(DISCOVERY_AUTHORITY);
    expect(built.auth.clientId).toBe(HOME_CLIENT_ID);
    expect(built.auth.knownAuthorities).toEqual([]);
    expect(built.auth.redirectUri).toBe(window.location.origin);
    expect(built.cache?.cacheLocation).toBe('sessionStorage');
    // The discovery instance carries no BFF scope: a descriptor that names one is refused.
    expect(() => msalConfig.buildMsalConfiguration({ ...descriptor, bffScope: scopeOf(CONTOSO.bffAppId) })).toThrow(
      /discovery/i
    );
  });

  it('honours an explicit cache location (the guest-token instance keeps its cache in memory), defaulting to the session cache', async () => {
    stubEnv();
    const { msalConfig } = await loadAuth();
    const descriptor = msalConfig.workforceAuthorityConfig({
      clientId: A8_CLIENT_ID,
      tenantId: SPAARKE_TENANT,
      bffScope: scopeOf(CONTOSO.bffAppId),
    });
    expect(msalConfig.buildMsalConfiguration(descriptor).cache?.cacheLocation).toBe('sessionStorage');
    expect(msalConfig.buildMsalConfiguration(descriptor, { cacheLocation: 'memoryStorage' }).cache?.cacheLocation).toBe(
      'memoryStorage'
    );
  });

  it('the token authority is https://login.microsoftonline.com/{tid}; organizations / aliases / non-GUIDs are refused', async () => {
    stubEnv();
    const { msalConfig } = await loadAuth();
    expect(msalConfig.workforceHomeTokenAuthority(CONTOSO_TID)).toBe(tokenAuthority(CONTOSO_TID));
    for (const bad of ['organizations', 'common', 'consumers', '', 'contoso.onmicrosoft.com']) {
      expect(() => msalConfig.workforceHomeTokenAuthority(bad)).toThrow(/tenant/i);
    }
  });
});

describe('Multitenant client configuration comes from Spaarke-owned env config and fails loud', () => {
  it('reads the multitenant client id', async () => {
    stubEnv();
    const { config } = await loadAuth();
    expect(config.getWorkforceHomeClientConfig()).toEqual({ clientId: HOME_CLIENT_ID });
  });

  it.each([
    ['missing', { VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: '' }, /VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/],
    [
      'an unsubstituted placeholder',
      { VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: '#{WORKFORCE_HOME_MSAL_CLIENT_ID}#' },
      /VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/,
    ],
    ['not a GUID', { VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: 'organizations' }, /GUID/],
    [
      'equal to the A8 client id',
      { VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: A8_CLIENT_ID.toUpperCase() },
      /VITE_TEAMS_MSAL_CLIENT_ID/,
    ],
  ])('refuses a client id that is %s', async (_label, overrides, message) => {
    stubEnv(overrides);
    const { config } = await loadAuth();
    expect(() => config.getWorkforceHomeClientConfig()).toThrow(message);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// AC1 / AC2: tid -> directory entry -> backend
// ---------------------------------------------------------------------------------------------------------------

describe('tid -> directory entry (homeTenantIds) -> backend', () => {
  it('a directory entry keeps its home tenant ids (lower-cased); a malformed list is refused', async () => {
    stubEnv();
    const { selection } = await loadAuth();
    expect(selection.validateBackend({ ...CONTOSO, homeTenantIds: [CONTOSO_TID.toUpperCase()] }).homeTenantIds).toEqual(
      [CONTOSO_TID]
    );
    expect(selection.validateBackend({ ...CONTOSO, homeTenantIds: undefined })).not.toHaveProperty('homeTenantIds');
    for (const bad of ['c0c0c0c0', [42], ['organizations'], [CONTOSO_TID, 'common']]) {
      expect(() => selection.validateBackend({ ...CONTOSO, homeTenantIds: bad })).toThrow(/homeTenantIds/);
    }
  });

  it('an entry that lists Spaarke tenant or the CIAM tenant is refused at validation (must-fire); other tenants and plain entries still validate (must-not-fire)', async () => {
    stubEnv();
    const { selection } = await loadAuth();
    for (const reserved of [
      SPAARKE_TENANT,
      SPAARKE_TENANT.toUpperCase(),
      CIAM_TENANT,
      CONSUMER_TENANT,
      CONSUMER_TENANT.toUpperCase(),
    ]) {
      expect(() => selection.validateBackend({ ...CONTOSO, homeTenantIds: [CONTOSO_TID, reserved] })).toThrow(
        /never a customer's home directory/
      );
    }
    expect(selection.validateBackend({ ...CONTOSO, homeTenantIds: [CONTOSO_TID, FABRIKAM_TID] }).homeTenantIds).toEqual(
      [CONTOSO_TID, FABRIKAM_TID]
    );
    expect(selection.validateBackend({ ...CONTOSO, homeTenantIds: undefined })).not.toHaveProperty('homeTenantIds');
  });

  it('a VITE_BACKENDS list naming the Spaarke tenant fails closed; a directory record naming it is dropped, never matched', async () => {
    stubEnv({ VITE_BACKENDS: JSON.stringify([{ ...CONTOSO, homeTenantIds: [CONTOSO_TID, SPAARKE_TENANT] }]) });
    const { selection } = await loadAuth();
    expect(await selection.resolveHomeBackendSelection(SPAARKE_TENANT, { search: '' })).toMatchObject({ kind: 'none' });
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '' })).toMatchObject({ kind: 'none' });

    vi.unstubAllEnvs();
    stubEnv();
    const clean = await loadAuth();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const directory = {
      resolveWorkforce: async () => [],
      resolveByKey: async () => null,
      resolveByHomeTenant: async () => [{ ...CONTOSO, homeTenantIds: [CONTOSO_TID, SPAARKE_TENANT] }] as any,
    };
    for (const tid of [SPAARKE_TENANT, CONTOSO_TID]) {
      expect(await clean.selection.resolveHomeBackendSelection(tid, { directory, search: '' })).toEqual({
        kind: 'notSetUp',
      });
    }
    warn.mockRestore();
  });

  it('a listed tid selects the entry that lists it, and only that entry', async () => {
    stubEnv();
    const { selection } = await loadAuth();
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'contoso', bffBaseUrl: CONTOSO.bffBaseUrl, bffAppId: CONTOSO.bffAppId },
    });
    expect(await selection.resolveHomeBackendSelection(FABRIKAM_TID.toUpperCase(), { search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'fabrikam' },
    });
  });

  it('an unlisted tid is "not set up" - and so is the Spaarke tenant, the CIAM tenant and a non-GUID', async () => {
    stubEnv();
    const { selection } = await loadAuth();
    for (const tid of [UNLISTED_TID, SPAARKE_TENANT, CIAM_TENANT, 'organizations', '']) {
      expect(await selection.resolveHomeBackendSelection(tid, { search: '' })).toEqual({ kind: 'notSetUp' });
    }
  });

  it('?customer= may only choose among the entries that list the tid; it never selects another entry', async () => {
    stubEnv({
      VITE_BACKENDS: JSON.stringify([
        CONTOSO,
        { ...FABRIKAM, customerKey: 'contoso-eu', homeTenantIds: [CONTOSO_TID, FABRIKAM_TID] },
        FABRIKAM,
      ]),
    });
    const { selection } = await loadAuth();
    // Two entries list the contoso tid: no key -> pick; a key among them -> selected.
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '' })).toMatchObject({
      kind: 'pick',
      options: [{ customerKey: 'contoso' }, { customerKey: 'contoso-eu' }],
    });
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '?customer=contoso-eu' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'contoso-eu' },
    });
    // A key naming an entry that does not list the tid is ignored (here: back to the pick list), never selected.
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '?customer=fabrikam' })).toMatchObject({
      kind: 'pick',
    });
    // ... and it cannot make an unlisted tid "set up".
    expect(await selection.resolveHomeBackendSelection(UNLISTED_TID, { search: '?customer=contoso' })).toEqual({
      kind: 'notSetUp',
    });
  });

  it("the tab's stored customer key is honoured only among the tid-filtered entries", async () => {
    stubEnv({ VITE_BACKENDS: JSON.stringify([CONTOSO, { ...FABRIKAM, homeTenantIds: [CONTOSO_TID] }]) });
    const { selection } = await loadAuth();
    selection.activateBackend('workforce', { ...FABRIKAM });
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'fabrikam' },
    });
    // A stored key for an entry that does not list the tid does not apply.
    expect(await selection.resolveHomeBackendSelection(UNLISTED_TID, { search: '' })).toEqual({ kind: 'notSetUp' });
  });

  it('the one-entry VITE_BFF_API_URL fallback lists no home tenant, so every tid is "not set up"', async () => {
    stubEnv({ VITE_BACKENDS: '', VITE_BFF_API_URL: 'https://dev-bff.test' });
    const { selection } = await loadAuth();
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { search: '' })).toEqual({ kind: 'notSetUp' });
  });

  it('a directory that cannot be read fails closed (none), not "not set up" and not a default backend', async () => {
    stubEnv();
    const { selection } = await loadAuth();
    const failing = {
      resolveWorkforce: async () => {
        throw new Error('directory down');
      },
      resolveByKey: async () => null,
    };
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { directory: failing, search: '' })).toEqual({
      kind: 'none',
      reason: 'directory down',
    });
  });

  it("prefers the directory's home-tenant lookup when it has one, and still re-checks the tid on what it returns", async () => {
    stubEnv();
    const { selection } = await loadAuth();
    const resolveByHomeTenant = vi.fn(async () => [CONTOSO, FABRIKAM]);
    const directory = { resolveWorkforce: vi.fn(async () => []), resolveByKey: async () => null, resolveByHomeTenant };
    expect(await selection.resolveHomeBackendSelection(CONTOSO_TID, { directory, search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'contoso' },
    });
    expect(resolveByHomeTenant).toHaveBeenCalledWith(CONTOSO_TID);
    expect(directory.resolveWorkforce).not.toHaveBeenCalled();
  });

  it('landingCustomerName names the customer for ?customer={key} from the configured entries, else null', async () => {
    stubEnv();
    const { selection } = await loadAuth();
    expect(selection.landingCustomerName('?customer=contoso')).toBe('Contoso');
    expect(selection.landingCustomerName('?customer=unknown')).toBeNull();
    expect(selection.landingCustomerName('')).toBeNull();
    expect(selection.landingCustomerName('?customer=https://evil.example')).toBeNull();
  });
});

// ---------------------------------------------------------------------------------------------------------------
// AC1 / AC2: the plane (browser sign-in, token requests)
// ---------------------------------------------------------------------------------------------------------------

describe("Workforce home plane: discovery on organizations, BFF token on /{tid} with the entry's scope", () => {
  async function prepare(accounts: any[], options: Record<string, unknown> = { search: '' }) {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts };
    const mods = await loadAuth();
    const result = await mods.plane.prepareWorkforceHomePlane(options as any);
    return { ...mods, result };
  }

  it("listed tid: the BFF token is requested with authority /{tid} and api://{that entry's bffAppId}/user_impersonation", async () => {
    const { plane, msalAuth, selection, result } = await prepare([JANE]);
    expect(result.kind).toBe('plane');
    const { plane: resolved } = result as any;
    expect(resolved.realm).toBe('workforce');
    expect(resolved.bffScope).toBe(scopeOf(CONTOSO.bffAppId));
    // The tenant filter, the bridge page (iframe retry) and the per-request authority all come from the signed-in tid.
    expect(resolved.acquireOptions).toEqual({
      tenantId: CONTOSO_TID,
      silentRedirectUri: `${window.location.origin}/auth-callback.html`,
      authority: tokenAuthority(CONTOSO_TID),
    });
    expect(selection.getActiveBackend()).toMatchObject({
      plane: 'workforce',
      customerKey: 'contoso',
      bffBaseUrl: CONTOSO.bffBaseUrl,
      bffScope: scopeOf(CONTOSO.bffAppId),
    });

    plane.applyStandalonePlane(resolved);
    const { CacheLookupPolicy } = await import('@azure/msal-browser');
    await expect(msalAuth.acquireActiveBffToken(selection.getActiveBackend())).resolves.toBe(
      `home-token:${scopeOf(CONTOSO.bffAppId)}`
    );

    const tokenRequests = h.requests.filter(r => r.call === 'acquireTokenSilent');
    expect(tokenRequests).toHaveLength(1);
    expect(tokenRequests[0].request).toStrictEqual({
      scopes: [scopeOf(CONTOSO.bffAppId)],
      account: JANE,
      cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
      authority: tokenAuthority(CONTOSO_TID),
    });
    // Nothing else was asked of anyone: no login, no redirect, no other scope.
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenSilent']);
  });

  it('the plane is built on the multitenant client with authority organizations (discovery), never the A8 client', async () => {
    const { result } = await prepare([JANE]);
    expect(result.kind).toBe('plane');
    const home = h.pcas.filter(p => p.config.auth.clientId === HOME_CLIENT_ID);
    expect(home).toHaveLength(1);
    expect(home[0].config.auth.authority).toBe(DISCOVERY_AUTHORITY);
    expect(home[0].config.auth.redirectUri).toBe(window.location.origin);
    expect(home[0].config.cache.cacheLocation).toBe('sessionStorage');
    expect((result as any).plane.instance).toBe(home[0].instance);
    expect(home[0].instance.initialize).toHaveBeenCalled();
    // The A8 client is not built for the browser sign-in at all (it is used only for the guest-token link).
    expect(h.pcas.filter(p => p.config.auth.clientId === A8_CLIENT_ID)).toEqual([]);
  });

  it("another tenant's user is routed to the other entry's backend and scope", async () => {
    const { plane, selection, result } = await prepare([{ ...JANE, tenantId: FABRIKAM_TID, homeAccountId: 'f' }]);
    expect(result.kind).toBe('plane');
    expect((result as any).plane.bffScope).toBe(scopeOf(FABRIKAM.bffAppId));
    plane.applyStandalonePlane((result as any).plane);
    expect(selection.getActiveBackend()).toMatchObject({ customerKey: 'fabrikam', bffBaseUrl: FABRIKAM.bffBaseUrl });
  });

  it('the login redirect asks for OIDC scopes only - the BFF scope is not requested before the tid is known', async () => {
    const { plane, msalAuth, selection, result } = await prepare([]);
    expect(result.kind).toBe('plane');
    plane.applyStandalonePlane((result as any).plane);
    expect(msalAuth.getActiveLoginScope()).toBe('openid');
    expect(msalAuth.isWorkforceHomePlaneActive()).toBe(true);
    // No tid, no backend, no token request - and the acquirer refuses instead of guessing a scope.
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
    await expect(msalAuth.acquireActiveBffToken()).rejects.toThrow();
    expect(h.requests).toEqual([]);
    expect(h.fetches).toEqual([]);
  });

  it('the real AuthGuard starts the sign-in with exactly the OIDC scope on the discovery instance', async () => {
    const { plane, result } = await prepare([]);
    plane.applyStandalonePlane((result as any).plane);
    h.msal = { accounts: [], instance: (result as any).plane.instance, inProgress: 'none' };
    const { render } = await import('@testing-library/react');
    const { MemoryRouter } = await import('react-router-dom');
    const { AuthGuard } = await import('../src/components/AuthGuard');
    render(
      React.createElement(MemoryRouter, null, React.createElement(AuthGuard, { teamsHost: false }, 'signed-in content'))
    );
    const { waitFor } = await import('@testing-library/react');
    await waitFor(() => expect(h.requests.filter(r => r.call === 'loginRedirect')).toHaveLength(1));
    const login = h.requests.find(r => r.call === 'loginRedirect')!;
    expect(login.request.scopes).toEqual(['openid']);
    expect(login.request).not.toHaveProperty('authority');
  });

  it('unlisted tid: not set up - zero token requests, zero fetches, no active backend', async () => {
    const { selection, result } = await prepare([{ ...JANE, tenantId: UNLISTED_TID }]);
    expect(result.kind).toBe('notSetUp');
    expectNoTokenRequestAndNoFetch();
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
  });

  it('a Spaarke-tenant sign-in (an employee) is not a home customer: not set up, nothing requested', async () => {
    const { result } = await prepare([{ ...JANE, tenantId: SPAARKE_TENANT }]);
    expect(result.kind).toBe('notSetUp');
    expectNoTokenRequestAndNoFetch();
  });

  it('a CIAM account cached in the same tab is not a workforce sign-in: the plane signs in instead', async () => {
    const { msalAuth, plane, result } = await prepare([
      { homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TENANT, environment: 'test.ciamlogin.com' },
    ]);
    expect(result.kind).toBe('plane');
    plane.applyStandalonePlane((result as any).plane);
    // No home account -> the sign-in plane (OIDC login scope, no backend); the CIAM account does not count.
    expect((result as any).plane.bffScope).toBe('');
    expect(msalAuth.accountsForActivePlane([{ tenantId: CIAM_TENANT }])).toEqual([]);
    expectNoTokenRequestAndNoFetch();
  });

  it("only the signed-in tenant's accounts count once the tid is known", async () => {
    const { msalAuth, plane, result } = await prepare([JANE]);
    plane.applyStandalonePlane((result as any).plane);
    const other = { homeAccountId: 'x', tenantId: FABRIKAM_TID };
    const same = { homeAccountId: 'j', tenantId: CONTOSO_TID.toUpperCase() };
    expect(msalAuth.accountsForActivePlane([other, same])).toEqual([same]);
  });

  it("the redirect fallback keeps the /{tid} authority and the entry's scope (never organizations + a BFF scope)", async () => {
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = {
      accounts: [JANE],
      silent: async () => {
        throw new InteractionRequiredAuthError('consent_required');
      },
    };
    const { plane, msalAuth, selection } = await loadAuth();
    const result = (await plane.prepareWorkforceHomePlane({ search: '' })) as any;
    plane.applyStandalonePlane(result.plane);
    await expect(msalAuth.acquireActiveBffToken(selection.getActiveBackend())).rejects.toThrow(/redirecting to login/);
    const redirect = h.requests.find(r => r.call === 'acquireTokenRedirect')!;
    expect(redirect.request).toStrictEqual({
      scopes: [scopeOf(CONTOSO.bffAppId)],
      account: JANE,
      authority: tokenAuthority(CONTOSO_TID),
    });
  });

  it('several entries list the tid: the plane asks for a pick; the picked key continues with that backend', async () => {
    stubEnv({ VITE_BACKENDS: JSON.stringify([CONTOSO, { ...FABRIKAM, homeTenantIds: [CONTOSO_TID] }]) });
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] };
    const { plane, selection } = await loadAuth();
    const first = (await plane.prepareWorkforceHomePlane({ search: '' })) as any;
    expect(first.kind).toBe('pick');
    expect(first.options.map((o: any) => o.customerKey)).toEqual(['contoso', 'fabrikam']);
    expect(h.requests).toEqual([]);
    selection.chooseBackend('workforce', first.options, 'fabrikam');
    const second = (await plane.prepareWorkforceHomePlane({ pickedKey: 'fabrikam' })) as any;
    expect(second.kind).toBe('plane');
    expect(second.plane.bffScope).toBe(scopeOf(FABRIKAM.bffAppId));
  });

  it('a picked key whose entry does not list the signed-in tid is "not set up": the active backend is cleared, nothing is requested', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] }; // JANE signs in from CONTOSO_TID; FABRIKAM lists only FABRIKAM_TID
    const { plane, selection } = await loadAuth();
    selection.chooseBackend('workforce', [CONTOSO, FABRIKAM], 'fabrikam');
    const result = (await plane.prepareWorkforceHomePlane({ pickedKey: 'fabrikam' })) as any;
    expect(result.kind).toBe('notSetUp');
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
    expectNoTokenRequestAndNoFetch();
  });

  it('a picked key whose entry lists the signed-in tid still continues, even with a different ?customer= key in the URL', async () => {
    stubEnv({ VITE_BACKENDS: JSON.stringify([CONTOSO, { ...FABRIKAM, homeTenantIds: [CONTOSO_TID] }]) });
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] };
    const { plane, selection } = await loadAuth();
    selection.chooseBackend('workforce', [CONTOSO, { ...FABRIKAM, homeTenantIds: [CONTOSO_TID] }], 'fabrikam');
    window.history.replaceState(null, '', '/?customer=contoso');
    const result = (await plane.prepareWorkforceHomePlane({ pickedKey: 'fabrikam' })) as any;
    expect(result.kind).toBe('plane');
    expect(result.plane.bffScope).toBe(scopeOf(FABRIKAM.bffAppId));
  });

  it('the multitenant client is not configured: preparation fails loud before any account or request is touched', async () => {
    stubEnv({ VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: '' });
    const { plane } = await loadAuth();
    await expect(plane.prepareWorkforceHomePlane({ search: '' })).rejects.toThrow(/VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/);
    expectNoTokenRequestAndNoFetch();
    expect(h.pcas.filter(p => p.config.auth.clientId === HOME_CLIENT_ID)).toEqual([]);
  });

  // msal-browser 5.5.0 keeps ONE interaction lock per tab (sessionStorage `msal.interaction.status`, value
  // `{"clientId":"<owner>","type":"..."}`). A reload while the A8 guest popup is open leaves the A8 lock behind and every
  // later home redirect would throw interaction_in_progress. The browser A8 instance never redirects, so an A8 lock at
  // page load is stale by construction.
  describe('a stale A8 (guest popup) interaction lock at page load', () => {
    const LOCK_KEY = 'msal.interaction.status';
    const lock = (clientId: string) => JSON.stringify({ clientId, type: 'signin' });

    it('is removed before the home instance starts - and nothing else in the tab is touched', async () => {
      sessionStorage.setItem(LOCK_KEY, lock(A8_CLIENT_ID));
      sessionStorage.setItem('msal.2.account.keys', '["k"]');
      sessionStorage.setItem(`msal.2.token.keys.${HOME_CLIENT_ID}`, '{}');
      sessionStorage.setItem(REALM_KEY, 'workforce');
      await prepare([JANE]);
      expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
      expect(sessionStorage.getItem('msal.2.account.keys')).toBe('["k"]');
      expect(sessionStorage.getItem(`msal.2.token.keys.${HOME_CLIENT_ID}`)).toBe('{}');
      expect(sessionStorage.getItem(REALM_KEY)).toBe('workforce');
    });

    it('is matched case-insensitively on the client id', async () => {
      sessionStorage.setItem(LOCK_KEY, lock(A8_CLIENT_ID.toUpperCase()));
      await prepare([JANE]);
      expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
    });

    it.each([
      ['the home client', HOME_CLIENT_ID],
      ['the CIAM client', '00000000-0000-0000-0000-000000000140'],
      ['any other client', '99999999-0000-0000-0000-000000000009'],
    ])('a lock held by %s is kept', async (_label, owner) => {
      sessionStorage.setItem(LOCK_KEY, lock(owner));
      await prepare([JANE]);
      expect(sessionStorage.getItem(LOCK_KEY)).toBe(lock(owner));
    });

    it('an unparsable lock value is left to msal-browser (not removed here)', async () => {
      sessionStorage.setItem(LOCK_KEY, 'not json');
      await prepare([JANE]);
      expect(sessionStorage.getItem(LOCK_KEY)).toBe('not json');
    });

    it('no lock at all is a no-op', async () => {
      await prepare([JANE]);
      expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
    });
  });

  it('the browser workforce plane is no longer built from the A8 client: resolveStandalonePlane refuses it', async () => {
    stubEnv();
    const { plane } = await loadAuth();
    expect(() => plane.resolveStandalonePlane('workforce')).toThrow(/prepareWorkforceHomePlane/);
    expect(h.pcas.filter(p => p.config.auth.clientId === A8_CLIENT_ID)).toEqual([]);
  });

  it('the CIAM plane is untouched: the singleton, the CIAM scope, no home flag, no account filter', async () => {
    stubEnv();
    const { plane, msalConfig, msalAuth, selection } = await loadAuth();
    selection.activateBackend('ciam', CONTOSO);
    const resolved = plane.resolveStandalonePlane('ciam');
    expect(resolved.instance).toBe(msalConfig.msalInstance);
    expect(resolved).not.toHaveProperty('home');
    plane.applyStandalonePlane(resolved);
    expect(msalAuth.isWorkforceHomePlaneActive()).toBe(false);
    expect(msalAuth.getActiveLoginScope()).toBe(resolved.bffScope);
    const account = { homeAccountId: 'c', tenantId: CIAM_TENANT };
    expect(msalAuth.accountsForActivePlane([account])).toEqual([account]);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// The Spaarke-tenant guest token (hard rules)
// ---------------------------------------------------------------------------------------------------------------

describe('Guest (Spaarke-tenant) token acquisition for the stored link', () => {
  const SCOPE = scopeOf(CONTOSO.bffAppId);
  const guestAccount = {
    homeAccountId: 'g',
    username: 'jane_contoso.test#EXT#@spaarke.test',
    tenantId: SPAARKE_TENANT,
  };

  async function guest(behaviour: Behaviour) {
    stubEnv();
    h.byClient[A8_CLIENT_ID] = behaviour;
    return loadAuth();
  }
  const guestPcas = () => h.pcas.filter(p => p.config.auth.clientId === A8_CLIENT_ID);
  /** Every clearCache call named an account (never the all-keys form), and the instance holds no account afterwards. */
  const expectAccountScopedClears = (instance: any, accounts: unknown[]) => {
    expect(instance.clearCache.mock.calls.map((c: any[]) => c[0])).toEqual(accounts.map(account => ({ account })));
    expect(instance.getAllAccounts()).toEqual([]);
  };

  it('silent: A8 client, Spaarke-tenant authority, memory cache, bridge-page redirect; ssoSilent with the home account as loginHint; cache cleared after', async () => {
    const { msalAuth } = await guest({ sso: async () => ({ accessToken: 'guest-token-1', account: guestAccount }) });
    await expect(msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toBe(
      'guest-token-1'
    );

    expect(guestPcas()).toHaveLength(1);
    const { config, instance } = guestPcas()[0];
    expect(config.auth.authority).toBe(tokenAuthority(SPAARKE_TENANT));
    expect(config.auth.clientId).toBe(A8_CLIENT_ID);
    expect(config.cache.cacheLocation).toBe('memoryStorage');
    expect(config.auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    expect(h.requests.map(r => r.call)).toEqual(['ssoSilent']);
    expect(h.requests[0].request).toStrictEqual({ scopes: [SCOPE], loginHint: 'jane@contoso.test' });
    expectAccountScopedClears(instance, [guestAccount]);
    // The token is held in memory only: nowhere in browser storage, the URL or a log.
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('guest-token-1');
    expect(JSON.stringify({ ...localStorage })).not.toContain('guest-token-1');
    expect(window.location.href).not.toContain('guest-token-1');
    expect(h.console.join('\n')).not.toContain('guest-token-1');
  });

  it('silent failure of any kind yields null (the caller shows the profile form); the cache is never wiped with the all-keys clear', async () => {
    const { msalAuth } = await guest({
      sso: async () => {
        throw new Error('interaction_required');
      },
    });
    await expect(msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE)).resolves.toBeNull();
    expectAccountScopedClears(guestPcas()[0].instance, []); // nothing was cached, so nothing to clear - and no all-keys clear
    expect(h.requests.map(r => r.call)).toEqual(['ssoSilent']);
  });

  it('popup: acquireTokenPopup with the loginHint on the same memory-cache instance; the cache is cleared after', async () => {
    const { msalAuth } = await guest({
      sso: async () => {
        throw new Error('interaction_required');
      },
      popup: async () => ({ accessToken: 'guest-token-2', account: guestAccount }),
    });
    await msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', SCOPE);
    await expect(msalAuth.acquireSpaarkeGuestToken('popup', 'jane@contoso.test', SCOPE)).resolves.toBe('guest-token-2');

    // One memoized instance served both calls.
    expect(guestPcas()).toHaveLength(1);
    expect(h.requests.map(r => r.call)).toEqual(['ssoSilent', 'acquireTokenPopup']);
    expect(h.requests[1].request).toStrictEqual({ scopes: [SCOPE], loginHint: 'jane@contoso.test' });
    expectAccountScopedClears(guestPcas()[0].instance, [guestAccount]);
    expect(h.console.join('\n')).not.toContain('guest-token-2');
  });

  it('a closed popup is "cancelled"; any other popup failure is "failed"; neither leaves the cache filled', async () => {
    const { BrowserAuthError, BrowserAuthErrorCodes } = await import('@azure/msal-browser');
    const { msalAuth } = await guest({
      popup: async () => {
        throw new BrowserAuthError(BrowserAuthErrorCodes.userCancelled);
      },
    });
    const cancelled = await msalAuth.acquireSpaarkeGuestToken('popup', 'jane@contoso.test', SCOPE).then(
      () => null,
      (e: unknown) => e
    );
    expect(cancelled).toBeInstanceOf(msalAuth.GuestLinkTokenError);
    expect((cancelled as any).reason).toBe('cancelled');
    expectAccountScopedClears(guestPcas()[0].instance, []);

    h.byClient[A8_CLIENT_ID] = {
      popup: async () => {
        throw new Error('popup_window_error');
      },
    };
    const second = await loadAuth();
    const failed = await second.msalAuth.acquireSpaarkeGuestToken('popup', 'jane@contoso.test', SCOPE).then(
      () => null,
      (e: unknown) => e
    );
    expect((failed as any).reason).toBe('failed');
  });

  it('refuses a scope that is not api://{app id}/user_impersonation without asking MSAL for anything', async () => {
    const { msalAuth } = await guest({ sso: async () => ({ accessToken: 'x', account: guestAccount }) });
    await expect(
      msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', 'https://graph.microsoft.com/.default')
    ).resolves.toBeNull();
    await expect(
      msalAuth.acquireSpaarkeGuestToken('popup', 'jane@contoso.test', 'https://graph.microsoft.com/.default')
    ).rejects.toBeInstanceOf(msalAuth.GuestLinkTokenError);
    expect(h.requests).toEqual([]);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// The workforce home gate, the registration form, the link (AC3 - AC5)
// ---------------------------------------------------------------------------------------------------------------

const ME_OK: Reply = { status: 200, json: { contactId: 'c-1', email: 'jane@contoso.test', projects: [] } };
const REG_REQUIRED: Reply = {
  status: 403,
  json: {
    title: 'Forbidden',
    status: 403,
    detail: 'Registration is required before this account can sign in.',
    reasonCode: 'sdap.access.registration_required',
    givenName: 'Jane',
    familyName: 'Doe',
    email: 'jane@contoso.test',
  },
};
const problem = (status: number, reasonCode: string, extra: Record<string, unknown> = {}): Reply => ({
  status,
  json: { title: 'x', status, detail: `detail for ${reasonCode}`, reasonCode, errorCode: reasonCode, ...extra },
});
const LINKED: Reply = { status: 200, json: { systemUserId: 's-1', outcome: 'linked' } };
const REGISTERED: Reply = { status: 201, json: { contactId: 'c-2', systemUserId: null, outcome: 'created' } };

const ME = 'GET /api/v1/external/me';
const REGISTRATION = 'POST /api/v1/external/registration';
const LINK = 'POST /api/v1/external/workforce/link';

describe('Workforce home gate: registration_required -> silent link -> profile form', () => {
  const guestAccount = {
    homeAccountId: 'g',
    username: 'jane_contoso.test#EXT#@spaarke.test',
    tenantId: SPAARKE_TENANT,
  };

  /** A signed-in contoso user on the home plane, the gate rendered over a marker child. */
  async function renderGate(options: { guest?: Behaviour } = {}) {
    stubEnv();
    sessionStorage.setItem(INVITE_CODE_STORAGE_KEY, JSON.stringify({ code: INVITE_CODE, customerKey: null }));
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] };
    h.byClient[A8_CLIENT_ID] = options.guest ?? {};
    const mods = await loadAuth();
    const prepared = (await mods.plane.prepareWorkforceHomePlane({ search: '' })) as any;
    mods.plane.applyStandalonePlane(prepared.plane);
    h.msal = {
      accounts: prepared.plane.instance.getAllAccounts(),
      instance: prepared.plane.instance,
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
    return { ...mods, ...rtl, view, onReady };
  }

  const bodies = (key: string) =>
    h.fetches.filter(f => `${f.method} ${bffPath(f.url)}` === key).map(f => (f.body ? JSON.parse(f.body) : undefined));
  const calls = () => h.fetches.map(f => `${f.method} ${bffPath(f.url)}`);

  /** Fill and submit the profile form. */
  async function fillAndSubmit(
    rtl: Awaited<ReturnType<typeof renderGate>>,
    values: { first?: string; last?: string; department?: string } = {}
  ) {
    const { screen, fireEvent } = rtl;
    if (values.first !== undefined)
      fireEvent.change(await screen.findByLabelText(/first name/i), { target: { value: values.first } });
    if (values.last !== undefined)
      fireEvent.change(screen.getByLabelText(/last name/i), { target: { value: values.last } });
    if (values.department !== undefined)
      fireEvent.change(screen.getByLabelText(/department/i), { target: { value: values.department } });
    fireEvent.click(screen.getByRole('button', { name: /^continue$/i }));
  }

  it('already registered: /me answers, the workspace shows, no link attempt, the guest client is never built', async () => {
    h.script = { [ME]: [ME_OK] };
    const g = await renderGate();
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
    expect(calls()).toEqual([ME]);
    expect(h.pcas.filter(p => p.config.auth.clientId === A8_CLIENT_ID)).toEqual([]);
    expect(h.requests.filter(r => r.call === 'ssoSilent')).toEqual([]);
    expect(g.onReady).toHaveBeenLastCalledWith(true);
    // The home token (the entry's scope) went to the entry's BFF, with no invite-code header (that is CIAM-only).
    expect(h.fetches[0].url).toBe(`${CONTOSO.bffBaseUrl}/api/v1/external/me`);
    expect(h.fetches[0].headers.Authorization).toBe(`Bearer home-token:${scopeOf(CONTOSO.bffAppId)}`);
    expect(JSON.stringify(h.fetches[0].headers).toLowerCase()).not.toContain('invite');
  });

  it('registration_required + silent success: link POST with the guest token in the body, then /me; the workspace shows', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [LINKED] };
    const g = await renderGate({
      guest: { sso: async () => ({ accessToken: 'guest-token-xyz', account: guestAccount }) },
    });
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();

    expect(calls()).toEqual([ME, LINK, ME]);
    // The silent acquisition used the home account as loginHint and the entry's scope.
    const sso = h.requests.find(r => r.call === 'ssoSilent')!;
    expect(sso.request).toStrictEqual({ scopes: [scopeOf(CONTOSO.bffAppId)], loginHint: 'jane@contoso.test' });
    // The guest token is in the link BODY and nowhere else; the Authorization header is the HOME token.
    expect(bodies(LINK)).toEqual([{ guestAccessToken: 'guest-token-xyz' }]);
    for (const f of h.fetches) {
      expect(f.url).toBe(`${CONTOSO.bffBaseUrl}${bffPath(f.url)}`);
      expect(f.url).not.toContain('guest-token-xyz');
      expect(f.headers.Authorization).toBe(`Bearer home-token:${scopeOf(CONTOSO.bffAppId)}`);
      expect(JSON.stringify(f.headers)).not.toContain('guest-token-xyz');
      // The invite code is stored in this tab, yet no home-token call carries it (X-Spaarke-Invite-Code is CIAM-only).
      expect(JSON.stringify(f.headers).toLowerCase()).not.toContain('invite');
    }
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('guest-token-xyz');
    expect(JSON.stringify({ ...localStorage })).not.toContain('guest-token-xyz');
    expect(h.console.join('\n')).not.toContain('guest-token-xyz');
    // The guest instance was cleared after use, and nobody registered.
    const guestClear = h.pcas.find(p => p.config.auth.clientId === A8_CLIENT_ID)!.instance.clearCache;
    expect(guestClear).toHaveBeenCalled();
    for (const call of guestClear.mock.calls) expect(call[0]).toHaveProperty('account'); // never the all-keys form
    expect(calls()).not.toContain(REGISTRATION);
    expect(g.onReady).toHaveBeenCalledWith(false);
    expect(g.onReady).toHaveBeenLastCalledWith(true);
  });

  it('registration_required + silent failure: the profile form, prefilled, email read-only; nothing is linked or registered yet', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderGate(); // the guest client cannot sign in silently
    const first = (await g.screen.findByLabelText(/first name/i)) as HTMLInputElement;
    expect(first.value).toBe('Jane');
    expect((g.screen.getByLabelText(/last name/i) as HTMLInputElement).value).toBe('Doe');
    expect((g.screen.getByLabelText(/department/i) as HTMLInputElement).value).toBe('');
    const email = g.screen.getByLabelText(/email/i) as HTMLInputElement;
    expect(email.value).toBe('jane@contoso.test');
    expect(email.readOnly).toBe(true);
    expect(g.screen.queryByTestId('workspace')).toBeNull();
    expect(calls()).toEqual([ME]);
    expect(g.onReady).toHaveBeenLastCalledWith(false);
  });

  it('the form submits first name, last name and department (trimmed) and, once registered, re-reads /me and shows the workspace', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [REGISTRATION]: [REGISTERED] };
    const g = await renderGate();
    await fillAndSubmit(g, { first: '  Jane ', last: 'Doe', department: ' Legal ' });
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
    expect(calls()).toEqual([ME, REGISTRATION, ME]);
    const [body] = bodies(REGISTRATION);
    expect(body).toEqual({ firstName: 'Jane', lastName: 'Doe', department: 'Legal' });
    // The body is closed: no email, no tenant, no owner - those come from the token only.
    expect(Object.keys(body).sort()).toEqual(['department', 'firstName', 'lastName']);
    expect(JSON.stringify(h.fetches[1].headers).toLowerCase()).not.toContain('invite');
  });

  it('client-side validation: required names, at most 50 / 50 / 100 characters - nothing is sent', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderGate();
    await fillAndSubmit(g, { first: '', last: 'Doe' });
    expect(await g.screen.findByText(/first name is required/i)).toBeTruthy();
    await fillAndSubmit(g, { first: 'J'.repeat(51), last: 'Doe' });
    expect(await g.screen.findByText(/first name must be at most 50/i)).toBeTruthy();
    await fillAndSubmit(g, { first: 'Jane', last: 'D'.repeat(51) });
    expect(await g.screen.findByText(/last name must be at most 50/i)).toBeTruthy();
    await fillAndSubmit(g, { last: 'Doe', department: 'L'.repeat(101) });
    expect(await g.screen.findByText(/department must be at most 100/i)).toBeTruthy();
    expect(calls()).toEqual([ME]);
  });

  it('licensed_user_link_required: a "Link my account" button; the popup opens only on the click, then the link POST and /me', async () => {
    h.script = {
      [ME]: [REG_REQUIRED, ME_OK],
      [REGISTRATION]: [problem(403, 'sdap.access.licensed_user_link_required')],
      [LINK]: [LINKED],
    };
    const g = await renderGate({
      guest: { popup: async () => ({ accessToken: 'guest-token-popup', account: guestAccount }) },
    });
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
    const button = await g.screen.findByRole('button', { name: /link my account/i });
    expect(h.requests.filter(r => r.call === 'acquireTokenPopup')).toEqual([]); // not before the click
    g.fireEvent.click(button);
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();

    const popup = h.requests.find(r => r.call === 'acquireTokenPopup')!;
    expect(popup.request).toStrictEqual({ scopes: [scopeOf(CONTOSO.bffAppId)], loginHint: 'jane@contoso.test' });
    expect(calls()).toEqual([ME, REGISTRATION, LINK, ME]);
    expect(bodies(LINK)).toEqual([{ guestAccessToken: 'guest-token-popup' }]);
    expect(h.console.join('\n')).not.toContain('guest-token-popup');
  });

  it('popup cancelled: the page stays with an explanation, no registration POST and no link POST follow', async () => {
    const { BrowserAuthError, BrowserAuthErrorCodes } = await import('@azure/msal-browser');
    h.script = {
      [ME]: [REG_REQUIRED],
      [REGISTRATION]: [problem(403, 'sdap.access.licensed_user_link_required')],
    };
    const g = await renderGate({
      guest: {
        popup: async () => {
          throw new BrowserAuthError(BrowserAuthErrorCodes.userCancelled);
        },
      },
    });
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
    g.fireEvent.click(await g.screen.findByRole('button', { name: /link my account/i }));
    expect(await g.screen.findByText(/closed before it finished/i)).toBeTruthy();
    expect(g.screen.getByRole('button', { name: /link my account/i })).toBeTruthy(); // can try again
    expect(g.screen.queryByTestId('workspace')).toBeNull();
    // Exactly the one registration POST that was refused; no second one, no link.
    expect(calls()).toEqual([ME, REGISTRATION]);
  });

  it('409 existing_record_found: "We found your existing record" with the email; confirming resubmits with confirmExistingRecord true', async () => {
    h.script = {
      [ME]: [REG_REQUIRED, ME_OK],
      [REGISTRATION]: [
        problem(409, 'sdap.access.existing_record_found'),
        { status: 200, json: { contactId: 'c-9', outcome: 'bound' } },
      ],
    };
    const g = await renderGate();
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe', department: 'Legal' });
    expect(await g.screen.findByText(/we found your existing record/i)).toBeTruthy();
    expect(g.screen.getByText(/jane@contoso\.test/)).toBeTruthy();
    // The first submit did not claim a confirmation.
    expect(bodies(REGISTRATION)[0].confirmExistingRecord).not.toBe(true);
    expect(calls()).toEqual([ME, REGISTRATION]);

    g.fireEvent.click(g.screen.getByRole('button', { name: /confirm/i }));
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
    expect(calls()).toEqual([ME, REGISTRATION, REGISTRATION, ME]);
    expect(bodies(REGISTRATION)[1]).toEqual({
      firstName: 'Jane',
      lastName: 'Doe',
      department: 'Legal',
      confirmExistingRecord: true,
    });
  });

  it('409 -> confirm -> 503: "Try again" resends WITH confirmExistingRecord true (not back to the confirm screen)', async () => {
    h.script = {
      [ME]: [REG_REQUIRED, ME_OK],
      [REGISTRATION]: [
        problem(409, 'sdap.access.existing_record_found'),
        problem(503, 'sdap.access.registration_unavailable'),
        { status: 200, json: { contactId: 'c-9', outcome: 'bound' } },
      ],
    };
    const g = await renderGate();
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe', department: 'Legal' });
    expect(await g.screen.findByText(/we found your existing record/i)).toBeTruthy();
    g.fireEvent.click(g.screen.getByRole('button', { name: /confirm/i }));
    expect(await g.screen.findByText(/something went wrong/i)).toBeTruthy();

    g.fireEvent.click(g.screen.getByRole('button', { name: /try again/i }));
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
    expect(calls()).toEqual([ME, REGISTRATION, REGISTRATION, REGISTRATION, ME]);
    expect(bodies(REGISTRATION).map(b => b.confirmExistingRecord === true)).toEqual([false, true, true]);
  });

  it('registration_contact_conflict: an administrator must resolve the record; no further request', async () => {
    h.script = { [ME]: [REG_REQUIRED], [REGISTRATION]: [problem(403, 'sdap.access.registration_contact_conflict')] };
    const g = await renderGate();
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
    expect(await g.screen.findByText(/an administrator must resolve your record/i)).toBeTruthy();
    expect(g.screen.queryByTestId('workspace')).toBeNull();
    expect(calls()).toEqual([ME, REGISTRATION]);
  });

  it('registration_email_missing: tells the person to contact their administrator', async () => {
    h.script = { [ME]: [REG_REQUIRED], [REGISTRATION]: [problem(403, 'sdap.access.registration_email_missing')] };
    const g = await renderGate();
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
    expect(await g.screen.findByText(/did not include an email address/i)).toBeTruthy();
  });

  it.each([
    ['registration_owner_unconfigured (500)', problem(500, 'sdap.access.registration_owner_unconfigured')],
    ['registration_unavailable (503)', problem(503, 'sdap.access.registration_unavailable')],
    ['a bare 502', { status: 502, text: 'bad gateway' } as Reply],
  ])('%s: a generic error with a retry that submits again', async (_label, failure) => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [REGISTRATION]: [failure, REGISTERED] };
    const g = await renderGate();
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
    expect(await g.screen.findByText(/something went wrong/i)).toBeTruthy();
    // The generic message never echoes a server reason code.
    expect(g.screen.queryByText(/sdap\.access/)).toBeNull();
    g.fireEvent.click(g.screen.getByRole('button', { name: /try again/i }));
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
    expect(calls()).toEqual([ME, REGISTRATION, REGISTRATION, ME]);
  });

  it('a link refused because the guest has no systemuser falls through to the profile form (not licensed)', async () => {
    h.script = { [ME]: [REG_REQUIRED], [LINK]: [problem(403, 'sdap.access.link.no_systemuser')] };
    const g = await renderGate({
      guest: { sso: async () => ({ accessToken: 'guest-token-ns', account: guestAccount }) },
    });
    expect(await g.screen.findByLabelText(/first name/i)).toBeTruthy();
    expect(calls()).toEqual([ME, LINK]);
  });

  it('any other link refusal asks for an administrator and does not register', async () => {
    h.script = { [ME]: [REG_REQUIRED], [LINK]: [problem(403, 'sdap.access.link.link_collision')] };
    const g = await renderGate({
      guest: { sso: async () => ({ accessToken: 'guest-token-lc', account: guestAccount }) },
    });
    expect(await g.screen.findByText(/an administrator must resolve/i)).toBeTruthy();
    expect(g.screen.queryByLabelText(/first name/i)).toBeNull();
    expect(calls()).toEqual([ME, LINK]);
  });

  it.each([
    ['registration_home_identity_required (403)', problem(403, 'sdap.access.registration_home_identity_required')],
    ['an unrecognised 403 deny code', problem(403, 'sdap.access.some_new_deny')],
    ['a 403 with no readable body', { status: 403, text: 'forbidden' } as Reply],
    ['an unrecognised 409', problem(409, 'sdap.access.some_other_conflict')],
  ])(
    '%s: an administrator message and NO retry (a deliberate refusal never succeeds on a retry)',
    async (_label, deny) => {
      h.script = { [ME]: [REG_REQUIRED], [REGISTRATION]: [deny] };
      const g = await renderGate();
      await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
      expect(await g.screen.findByText(/an administrator must resolve your record/i)).toBeTruthy();
      expect(g.screen.queryByRole('button', { name: /try again/i })).toBeNull();
      expect(g.screen.queryByText(/something went wrong/i)).toBeNull();
      expect(g.screen.queryByText(/sdap\.access/)).toBeNull();
      expect(calls()).toEqual([ME, REGISTRATION]);
    }
  );

  it('a link 500 sdap.access.link.environment (the BFF cannot verify guest tokens) asks for an administrator: no retry', async () => {
    h.script = { [ME]: [REG_REQUIRED], [LINK]: [problem(500, 'sdap.access.link.environment')] };
    const g = await renderGate({
      guest: { sso: async () => ({ accessToken: 'guest-token-env', account: guestAccount }) },
    });
    expect(await g.screen.findByText(/an administrator must resolve/i)).toBeTruthy();
    expect(g.screen.queryByRole('button', { name: /try again/i })).toBeNull();
    expect(g.screen.queryByLabelText(/first name/i)).toBeNull();
    expect(calls()).toEqual([ME, LINK]);
  });

  it('a link 500 environment from the POPUP link is the administrator message too (no "try again in a moment")', async () => {
    h.script = {
      [ME]: [REG_REQUIRED],
      [REGISTRATION]: [problem(403, 'sdap.access.licensed_user_link_required')],
      [LINK]: [problem(500, 'sdap.access.link.environment')],
    };
    const g = await renderGate({
      guest: { popup: async () => ({ accessToken: 'guest-token-env2', account: guestAccount }) },
    });
    await fillAndSubmit(g, { first: 'Jane', last: 'Doe' });
    g.fireEvent.click(await g.screen.findByRole('button', { name: /link my account/i }));
    expect(await g.screen.findByText(/an administrator must resolve/i)).toBeTruthy();
    expect(g.screen.queryByText(/not available right now/i)).toBeNull();
    expect(g.screen.queryByRole('button', { name: /link my account/i })).toBeNull();
  });

  it('any OTHER link 5xx keeps the retry (silent: the gate retry; it is not an administrator matter)', async () => {
    h.script = { [ME]: [REG_REQUIRED, ME_OK], [LINK]: [{ status: 503, text: 'down' }] };
    const g = await renderGate({
      guest: { sso: async () => ({ accessToken: 'guest-token-503', account: guestAccount }) },
    });
    expect(await g.screen.findByText(/something went wrong/i)).toBeTruthy();
    expect(g.screen.getByRole('button', { name: /try again/i })).toBeTruthy();
    expect(g.screen.queryByText(/an administrator must resolve/i)).toBeNull();
  });

  it('a /me failure that is not a registration question (5xx) shows a generic error with a retry', async () => {
    h.script = { [ME]: [{ status: 503, text: 'down' }, ME_OK] };
    const g = await renderGate();
    expect(await g.screen.findByText(/something went wrong/i)).toBeTruthy();
    expect(g.screen.queryByTestId('workspace')).toBeNull();
    g.fireEvent.click(g.screen.getByRole('button', { name: /try again/i }));
    expect(await g.screen.findByTestId('workspace')).toBeTruthy();
  });

  it('a /me 403 that is not registration_required is the denied state (no form, no link)', async () => {
    h.script = { [ME]: [problem(403, 'sdap.access.principal_not_resolved')] };
    const g = await renderGate();
    expect(await g.screen.findByTestId('bff-access-denied')).toBeTruthy();
    expect(calls()).toEqual([ME]);
  });

  // Task 075 (A9 Teams amendment): the Teams tab is the home plane too, so the gate is ACTIVE there (it used to be skipped
  // for a Teams host flag). Its Teams behaviour is tested in tests/teams-home-auth.test.ts; here only the CIAM plane is left.
  it('the gate does nothing outside the home plane: the CIAM browser plane renders the children at once, with no /me probe', async () => {
    // CIAM: applying the CIAM plane clears the home-plane flag.
    const { plane, selection } = await loadAuth();
    selection.activateBackend('ciam', CONTOSO);
    plane.applyStandalonePlane(plane.resolveStandalonePlane('ciam'));
    const rtl = await import('@testing-library/react');
    const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
    const { WorkforceHomeGate } = await import('../src/components/auth/WorkforceHomeGate');
    rtl.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(
          WorkforceHomeGate,
          {} as any,
          React.createElement('div', { 'data-testid': 'ciam-workspace' }, 'workspace')
        )
      )
    );
    expect(await rtl.screen.findByTestId('ciam-workspace')).toBeTruthy();
    expect(h.fetches).toEqual([]);
  });

  it('the registration form is a labelled form with a real heading (accessible shell)', async () => {
    h.script = { [ME]: [REG_REQUIRED] };
    const g = await renderGate();
    expect(await g.screen.findByRole('heading', { level: 1 })).toBeTruthy();
    expect(g.screen.getByLabelText(/first name/i).getAttribute('maxlength')).toBe('50');
    expect(g.screen.getByLabelText(/last name/i).getAttribute('maxlength')).toBe('50');
    expect(g.screen.getByLabelText(/department/i).getAttribute('maxlength')).toBe('100');
  });
});

// ---------------------------------------------------------------------------------------------------------------
// The browser bootstrap and the landing (AC2 end to end, wording)
// ---------------------------------------------------------------------------------------------------------------

describe('Browser bootstrap (main.tsx) on the workforce home plane', () => {
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

  beforeEach(() => {
    sessionStorage.setItem(REALM_KEY, 'workforce');
  });

  it('unlisted tid: the "not set up" screen; no app, no token request, no fetch, no active backend', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [{ ...JANE, tenantId: UNLISTED_TID }] };
    const { selection, rtl } = await boot();
    expect(await rtl.screen.findByText('Your organization is not set up for Spaarke')).toBeTruthy();
    expect(rtl.screen.queryByTestId('app')).toBeNull();
    expectNoTokenRequestAndNoFetch();
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
  });

  it('"Sign out" on the not-set-up screen signs the account out and forgets the realm', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [{ ...JANE, tenantId: UNLISTED_TID }] };
    const { rtl } = await boot();
    await rtl.screen.findByText('Your organization is not set up for Spaarke');
    rtl.fireEvent.click(rtl.screen.getByRole('button', { name: /^sign out$/i }));
    await rtl.waitFor(() => expect(h.requests.filter(r => r.call === 'logoutRedirect')).toHaveLength(1));
    expect(sessionStorage.getItem(REALM_KEY)).toBeNull();
  });

  it('the not-set-up text tells the person to sign out to use a different company account (both actions are kept)', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [{ ...JANE, tenantId: UNLISTED_TID }] };
    const { rtl } = await boot();
    await rtl.screen.findByText('Your organization is not set up for Spaarke');
    expect(rtl.screen.getByText(/to use a different company account, select "sign out" first/i)).toBeTruthy();
    expect(rtl.screen.getByRole('button', { name: /^sign out$/i })).toBeTruthy();
    expect(rtl.screen.getByRole('button', { name: /choose a different sign-in/i })).toBeTruthy();
  });

  it('"Choose a different sign-in" on the not-set-up screen returns to the chooser', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [{ ...JANE, tenantId: UNLISTED_TID }] };
    const { rtl } = await boot();
    await rtl.screen.findByText('Your organization is not set up for Spaarke');
    rtl.fireEvent.click(rtl.screen.getByRole('button', { name: /choose a different sign-in/i }));
    expect(await rtl.screen.findByText('Sign in to Spaarke')).toBeTruthy();
    expect(h.fetches).toEqual([]);
  });

  it("listed tid: the app mounts with that entry's backend active on the workforce plane", async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] };
    const { selection, rtl } = await boot();
    const app = await rtl.screen.findByTestId('app');
    expect(app.getAttribute('data-teams')).toBe('false');
    expect(selection.getActiveBackend()).toMatchObject({ plane: 'workforce', customerKey: 'contoso' });
  });

  it('no account yet: the app mounts so the guard can start the OIDC-only sign-in', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [] };
    const { msalAuth, rtl } = await boot();
    expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    expect(msalAuth.getActiveLoginScope()).toBe('openid');
    expectNoTokenRequestAndNoFetch();
  });

  it('several entries list the tid: the customer picker, then the app with the picked backend', async () => {
    stubEnv({ VITE_BACKENDS: JSON.stringify([CONTOSO, { ...FABRIKAM, homeTenantIds: [CONTOSO_TID] }]) });
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] };
    const { selection, rtl } = await boot();
    expect(await rtl.screen.findByText('Choose an organization')).toBeTruthy();
    expect(() => selection.getActiveBackend()).toThrow(/No backend is active/);
    rtl.fireEvent.click(rtl.screen.getByRole('button', { name: 'Fabrikam' }));
    expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    expect(selection.getActiveBackend()).toMatchObject({ plane: 'workforce', customerKey: 'fabrikam' });
  });

  it('the multitenant client is not configured: the sign-in unavailable screen names the missing variable', async () => {
    stubEnv({ VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: '' });
    h.byClient[HOME_CLIENT_ID] = { accounts: [JANE] };
    const { rtl } = await boot();
    expect(await rtl.screen.findByText('Sign-in unavailable')).toBeTruthy();
    expect(rtl.screen.getByText(/VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/)).toBeTruthy();
    expectNoTokenRequestAndNoFetch();
  });
});

describe('Landing wording (RealmChooser)', () => {
  async function boot(search: string) {
    sessionStorage.removeItem(REALM_KEY);
    window.history.replaceState(null, '', `/${search}`);
    vi.resetModules();
    document.body.innerHTML = '<div id="root"></div>';
    await import('../src/main');
    return import('@testing-library/react');
  }

  it('with ?customer={key} the link reads "Work for {customer}? Sign in with your company account"', async () => {
    stubEnv();
    const rtl = await boot('?customer=contoso');
    expect(await rtl.screen.findByText(/Work for Contoso\?/)).toBeTruthy();
    expect(rtl.screen.getByRole('button', { name: /sign in with your company account/i })).toBeTruthy();
    expect(rtl.screen.getByRole('button', { name: /continue as partner/i })).toBeTruthy();
  });

  it('without a known customer it says "your organization"', async () => {
    stubEnv();
    const rtl = await boot('');
    expect(await rtl.screen.findByText(/Work for your organization\?/)).toBeTruthy();
    expect(rtl.screen.queryByText(/Spaarke employee/)).toBeNull();
  });

  it('a customer key that is not configured is never echoed into the page', async () => {
    stubEnv();
    const rtl = await boot('?customer=Evil-Corp');
    expect(await rtl.screen.findByText(/Work for your organization\?/)).toBeTruthy();
    expect(document.body.textContent).not.toContain('Evil-Corp');
  });

  it('choosing the company-account link starts the workforce realm', async () => {
    stubEnv();
    h.byClient[HOME_CLIENT_ID] = { accounts: [] };
    const rtl = await boot('');
    rtl.fireEvent.click(await rtl.screen.findByRole('button', { name: /sign in with your company account/i }));
    expect(await rtl.screen.findByTestId('app')).toBeTruthy();
    expect(sessionStorage.getItem(REALM_KEY)).toBe('workforce');
  });

  it('/join is not a route any more: the landing is the chooser', async () => {
    stubEnv();
    sessionStorage.removeItem(REALM_KEY);
    window.history.replaceState(null, '', '/join?customer=contoso');
    vi.resetModules();
    document.body.innerHTML = '<div id="root"></div>';
    await import('../src/main');
    const rtl = await import('@testing-library/react');
    expect(await rtl.screen.findByText('Sign in to Spaarke')).toBeTruthy();
    expect(rtl.screen.queryByText(/Join Spaarke/)).toBeNull();
  });
});

// ---------------------------------------------------------------------------------------------------------------
// AC7: shipped files
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

describe('Removed hand-off and wording (the 056 registration service, the join page, "Spaarke employee")', () => {
  const sourceFiles = () => listFiles(path.join(SPA_ROOT, 'src')).filter(f => /\.(ts|tsx|d\.ts|css|html)$/.test(f));
  const configFiles = () =>
    ['.env.example', '.env.development', '.env.production', 'src/vite-env.d.ts', 'README.md', 'index.html'].map(f =>
      path.join(SPA_ROOT, f)
    );

  it.each([
    ['"Spaarke employee"', /Spaarke employee/i],
    ['VITE_REGISTRATION_SERVICE_URL', /VITE_REGISTRATION_SERVICE_URL/],
    ['VITE_JOIN_ENABLED', /VITE_JOIN_ENABLED/],
    ['the retired scope name', new RegExp(RETIRED_SCOPE, 'i')],
  ])('%s appears nowhere in src/ or the env files', (_label, pattern) => {
    const offenders = [...sourceFiles(), ...configFiles()]
      .filter(f => fs.existsSync(f))
      .filter(f => pattern.test(fs.readFileSync(f, 'utf8')))
      .map(f => path.relative(SPA_ROOT, f));
    expect(offenders).toEqual([]);
  });

  it('the join page, its test and the /join route are gone', () => {
    expect(fs.existsSync(path.join(SPA_ROOT, 'src/pages/JoinPage.tsx'))).toBe(false);
    expect(fs.existsSync(path.join(SPA_ROOT, 'tests/JoinPage.test.tsx'))).toBe(false);
    const main = fs.readFileSync(path.join(SPA_ROOT, 'src/main.tsx'), 'utf8');
    expect(main).not.toMatch(/isJoinPath|JoinPage|renderJoin/);
    const client = fs.readFileSync(path.join(SPA_ROOT, 'src/auth/registration-client.ts'), 'utf8');
    expect(client).not.toMatch(/assign\(|JOIN_PATH|startRegistration|getRegistrationServiceUrl/);
  });

  it('the registration client calls only the BFF routes (through bffApiCall, so the active backend and token apply)', () => {
    const client = fs.readFileSync(path.join(SPA_ROOT, 'src/auth/registration-client.ts'), 'utf8');
    expect(client).toContain('/api/v1/external/registration');
    expect(client).toContain('/api/v1/external/workforce/link');
    expect(client).toContain('bffApiCall');
    expect(client).not.toMatch(/\bfetch\s*\(/);
  });

  it('the multitenant client id is declared in vite-env.d.ts and the three env files', () => {
    expect(fs.readFileSync(path.join(SPA_ROOT, 'src/vite-env.d.ts'), 'utf8')).toMatch(
      /VITE_WORKFORCE_HOME_MSAL_CLIENT_ID\?: string/
    );
    expect(fs.readFileSync(path.join(SPA_ROOT, '.env.production'), 'utf8')).toMatch(
      /^VITE_WORKFORCE_HOME_MSAL_CLIENT_ID=#\{WORKFORCE_HOME_MSAL_CLIENT_ID\}#/m
    );
    expect(fs.readFileSync(path.join(SPA_ROOT, '.env.example'), 'utf8')).toMatch(/VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/);
    // .env.development only documents it: a value there would outrank .env.local (Vite mode files win), and the dev
    // multitenant client is registered by task 059.
    const dev = fs.readFileSync(path.join(SPA_ROOT, '.env.development'), 'utf8');
    expect(dev).toMatch(/VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/);
    expect(dev).not.toMatch(/^VITE_WORKFORCE_HOME_MSAL_CLIENT_ID=/m);
  });
});

describe('deploy-external-spa.yml carries the multitenant client id and fails closed without it', () => {
  const workflow = () =>
    fs.readFileSync(path.join(REPO_ROOT, '.github/workflows/deploy-external-spa.yml'), 'utf8').replace(/\r\n/g, '\n');
  const code = (text: string) =>
    text
      .split('\n')
      .filter(line => !/^\s*#/.test(line))
      .join('\n');

  it('is set from vars.* in BOTH env blocks (the preflight and the build)', () => {
    const text = code(workflow());
    const line = 'VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: ${{ vars.VITE_WORKFORCE_HOME_MSAL_CLIENT_ID }}';
    expect(text.split(line).length - 1).toBe(2);
    const preflight = text.indexOf('name: Check required environment variables');
    const build = text.indexOf('name: Build (Vite production)');
    expect(text.slice(preflight, build)).toContain(line);
    expect(text.slice(build)).toContain(line);
  });

  it('is required, must be a GUID, and must differ from the A8 client id', () => {
    const text = code(workflow());
    const preflight = text.slice(
      text.indexOf('name: Check required environment variables'),
      text.indexOf('name: Build (Vite production)')
    );
    const required = preflight.slice(preflight.indexOf('for k in '), preflight.indexOf('; do need'));
    expect(required).toContain('VITE_WORKFORCE_HOME_MSAL_CLIENT_ID');
    expect(preflight).toContain('match VITE_WORKFORCE_HOME_MSAL_CLIENT_ID "$guid" "GUID"');
    expect(preflight).toMatch(
      /VITE_WORKFORCE_HOME_MSAL_CLIENT_ID[^\n]*VITE_TEAMS_MSAL_CLIENT_ID|VITE_TEAMS_MSAL_CLIENT_ID[^\n]*VITE_WORKFORCE_HOME_MSAL_CLIENT_ID/
    );
    expect(preflight).toMatch(/must differ from VITE_TEAMS_MSAL_CLIENT_ID/);
    expect(preflight).toContain('exit "$fail"');
  });
});
