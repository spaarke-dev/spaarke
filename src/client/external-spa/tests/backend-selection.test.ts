// Run-time backend selection on both planes (spaarke-SPA-external-access-platform-r3 task 041; spec FR-17, NFR-02;
// ADR-028 A8).
//
// One build serves every customer backend. The BFF URL and the scope api://{bffAppId}/user_impersonation come from
// Spaarke-owned data (the T240c directory, dev config until it ships); a link may carry only ?customer={key}, which
// chooses among configured/directory keys or is ignored. These tests drive the real selection module, the real
// bff-client, the real grid client and the real plane acquirers; MSAL, Teams JS and fetch are fakes that RECORD what
// the code asks for (URL, Authorization header, token scopes), so the assertions are about requests, not stub echoes.
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const CLIENT_ID = '11111111-2222-3333-4444-555555555555';
/** The CIAM tenant of the test build (vitest.config.ts VITE_MSAL_TENANT_ID): a partner account's tenant (#2166). */
const CIAM_TID = '00000000-0000-0000-0000-000000000141';

const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
  ciamBffAppId: 'cccccccc-0000-0000-0000-00000000000c',
};
const FABRIKAM = {
  customerKey: 'fabrikam',
  displayName: 'Fabrikam',
  bffBaseUrl: 'https://fabrikam-bff.test',
  bffAppId: 'bbbbbbbb-0000-0000-0000-00000000000b',
};
const scopeOf = (appId: string) => `api://${appId}/user_impersonation`;
const EVIL = 'https://evil.example';

type Behaviour = {
  silent?: (req: any) => Promise<any>;
  sso?: (req: any) => Promise<any>;
  popup?: (req: any) => Promise<any>;
};

const h = vi.hoisted(() => ({
  // Every token request any fake MSAL instance receives.
  requests: [] as Array<{ instance: string; call: string; scopes: string[] }>,
  pcas: [] as Array<{ config: any; instance: any }>,
  nestable: null as null | ((config: any) => Promise<any>),
  pcaBehaviour: {} as Record<string, unknown>,
  // Accounts every new fake instance starts with.
  accounts: [] as any[],
  teamsInitialize: vi.fn(),
  naaRecommended: vi.fn(),
  // Every fetch: the URL and the Authorization header.
  fetches: [] as Array<{ url: string; authorization: string | undefined; method: string | undefined }>,
  fetchStatus: [] as number[],
}));

function makeFakeInstance(name: string, behaviour: Behaviour, config: any): any {
  let active: any = null;
  const accounts: any[] = [...h.accounts];
  const record = (call: string, req: any) => h.requests.push({ instance: name, call, scopes: req?.scopes });
  const remember = (result: any) => {
    if (result?.account && !accounts.includes(result.account)) accounts.push(result.account);
    return result;
  };
  // NAA-honest: the account is the host's; the token's own tenant is the tid claim (msal-browser 5.5.0).
  const tokenFor = (req: any) => ({
    accessToken: `token:${req.scopes[0]}`,
    idTokenClaims: { tid: accounts[0]?.tenantId },
    account: accounts[0] ?? null,
  });
  return {
    name,
    config,
    initialize: vi.fn(async () => undefined),
    handleRedirectPromise: vi.fn(async () => null),
    addEventCallback: vi.fn(() => null),
    getActiveAccount: () => active,
    setActiveAccount: (a: any) => {
      active = a;
    },
    getAllAccounts: (filter?: { tenantId?: string }) =>
      filter?.tenantId ? accounts.filter(a => a.tenantId === filter.tenantId) : accounts,
    acquireTokenSilent: vi.fn(async (req: any) => {
      record('acquireTokenSilent', req);
      return remember(behaviour.silent ? await behaviour.silent(req) : tokenFor(req));
    }),
    ssoSilent: vi.fn(async (req: any) => {
      record('ssoSilent', req);
      return remember(behaviour.sso ? await behaviour.sso(req) : tokenFor(req));
    }),
    acquireTokenPopup: vi.fn(async (req: any) => {
      record('acquireTokenPopup', req);
      return remember(behaviour.popup ? await behaviour.popup(req) : tokenFor(req));
    }),
    acquireTokenRedirect: vi.fn(async (req: any) => {
      record('acquireTokenRedirect', req);
    }),
    loginRedirect: vi.fn(async (req: any) => {
      record('loginRedirect', req);
    }),
  };
}

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    constructor(config: any) {
      const instance = makeFakeInstance(`pca#${h.pcas.length}`, (h.pcaBehaviour as Behaviour) ?? {}, config);
      h.pcas.push({ config, instance });
      return instance;
    }
  }
  return {
    ...actual,
    PublicClientApplication: FakePublicClientApplication,
    createNestablePublicClientApplication: async (config: any) => {
      if (!h.nestable) throw new Error('NAA not configured');
      return h.nestable(config);
    },
  };
});

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: h.teamsInitialize,
    getContext: vi.fn(async () => ({
      app: { theme: 'default', locale: 'en-us', host: { clientType: 'web' } },
      page: { subPageId: '' },
    })),
    notifyAppLoaded: vi.fn(),
    notifySuccess: vi.fn(async () => undefined),
    notifyFailure: vi.fn(),
    registerOnThemeChangeHandler: vi.fn(),
    FailedReason: { AuthFailed: 'AuthFailed' },
  },
  nestedAppAuth: { isNAAChannelRecommended: h.naaRecommended },
  authentication: { getAuthToken: vi.fn() },
}));

function stubWorkforceEnv(): void {
  // Task 075: the Teams strategy uses the multitenant home client (a different application from the A8 client).
  vi.stubEnv('VITE_WORKFORCE_HOME_MSAL_CLIENT_ID', '66666666-7777-8888-9999-000000000001');
  vi.stubEnv('VITE_TEAMS_MSAL_CLIENT_ID', CLIENT_ID);
  vi.stubEnv('VITE_TEAMS_MSAL_TENANT_ID', SPAARKE_TENANT);
  vi.stubEnv('VITE_TEAMS_MSAL_BFF_SCOPE', scopeOf('99999999-8888-7777-6666-555555555555'));
}

/** Configure the dev backend list (VITE_BACKENDS) — the "two configured backends" of FR-17. */
function stubTwoBackends(): void {
  vi.stubEnv('VITE_BACKENDS', JSON.stringify([CONTOSO, FABRIKAM]));
}

async function load() {
  vi.resetModules();
  const selection = await import('../src/auth/backend-selection');
  const msalAuth = await import('../src/auth/msal-auth');
  const bff = await import('../src/auth/bff-client');
  return { selection, msalAuth, bff };
}

/** A recording acquirer: returns a token naming the scope it was asked for. */
function recordingAcquirer() {
  const scopes: Array<string | undefined> = [];
  const acquirer = vi.fn(async (scope?: string) => {
    scopes.push(scope);
    return `token:${scope}`;
  });
  return { acquirer, scopes };
}

beforeEach(() => {
  h.requests.length = 0;
  h.pcas.length = 0;
  h.nestable = null;
  h.pcaBehaviour = {};
  h.accounts = [];
  h.teamsInitialize.mockReset();
  h.teamsInitialize.mockResolvedValue(undefined);
  h.naaRecommended.mockReset();
  h.naaRecommended.mockReturnValue(true);
  h.fetches.length = 0;
  h.fetchStatus = [];
  localStorage.clear();
  sessionStorage.clear();
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const headers = (init?.headers ?? {}) as Record<string, string>;
      h.fetches.push({ url, authorization: headers.Authorization, method: init?.method });
      const status = h.fetchStatus.shift() ?? 200;
      return new Response(status === 200 ? JSON.stringify({ ok: true }) : 'denied', {
        status,
        headers: { 'Content-Type': 'application/json' },
      });
    })
  );
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  window.history.replaceState(null, '', '/');
});

/** No recorded request ever went to, or asked a scope of, anything outside the configured backends. */
function expectNothingEvil(): void {
  for (const f of h.fetches) expect(new URL(f.url).host).not.toContain('evil');
  for (const r of h.requests) for (const s of r.scopes ?? []) expect(s).not.toContain('evil');
}

// ---------------------------------------------------------------------------------------------------------------
// Configuration: backends come only from Spaarke-owned config, and malformed config fails closed
// ---------------------------------------------------------------------------------------------------------------

describe('Dev configuration: backends come only from Spaarke-owned config', () => {
  it('VITE_BACKENDS lists two backends; each plane gets its own app id (the CIAM override when one is given)', async () => {
    stubTwoBackends();
    const { selection } = await load();
    const workforce = await new selection.DevConfigDirectory('workforce').resolveWorkforce();
    expect(workforce).toEqual([
      { customerKey: 'contoso', displayName: 'Contoso', bffBaseUrl: CONTOSO.bffBaseUrl, bffAppId: CONTOSO.bffAppId },
      {
        customerKey: 'fabrikam',
        displayName: 'Fabrikam',
        bffBaseUrl: FABRIKAM.bffBaseUrl,
        bffAppId: FABRIKAM.bffAppId,
      },
    ]);
    const ciam = await new selection.DevConfigDirectory('ciam').resolveWorkforce();
    expect(ciam.map(b => b.bffAppId)).toEqual([CONTOSO.ciamBffAppId, FABRIKAM.bffAppId]);
  });

  it('without VITE_BACKENDS, VITE_BFF_API_URL and the two scopes make ONE backend (customer key "default")', async () => {
    stubWorkforceEnv();
    const { selection } = await load();
    const [workforce] = await new selection.DevConfigDirectory('workforce').resolveWorkforce();
    expect(workforce).toMatchObject({
      customerKey: 'default',
      bffBaseUrl: 'https://bff.test.invalid',
      bffAppId: '99999999-8888-7777-6666-555555555555',
    });
    const [ciam] = await new selection.DevConfigDirectory('ciam').resolveWorkforce();
    // vitest.config.ts: VITE_MSAL_BFF_SCOPE = api://bff.test/user_impersonation
    expect(ciam).toMatchObject({
      customerKey: 'default',
      bffBaseUrl: 'https://bff.test.invalid',
      bffAppId: 'bff.test',
    });
    expect(selection.activeBffScope('ciam')).toBe('api://bff.test/user_impersonation');
  });

  it('the CIAM scope is api://{app id}/user_impersonation: the former SDAP.Access default is refused (fail closed)', async () => {
    vi.stubEnv('VITE_MSAL_BFF_SCOPE', 'api://4a4d5126-91b0-4865-8e3a-134b7209013e/SDAP.Access');
    const { selection, msalAuth } = await load();
    const result = await selection.resolveBackendSelection('ciam', { search: '' });
    expect(result.kind).toBe('none');
    expect((result as { reason: string }).reason).toMatch(/VITE_MSAL_BFF_SCOPE must be api:\/\/.*\/user_impersonation/);
    expect(() => selection.activeBffScope('ciam')).toThrow(/user_impersonation/);
    // The CIAM acquirer requests nothing rather than the refused scope.
    await expect(msalAuth.acquireBffToken()).rejects.toThrow(/user_impersonation/);
    expect(h.requests).toEqual([]);
  });

  it('every resolved scope on either plane is api://{bffAppId}/user_impersonation', async () => {
    stubTwoBackends();
    const { selection } = await load();
    for (const plane of ['workforce', 'ciam'] as const) {
      for (const b of await new selection.DevConfigDirectory(plane).resolveWorkforce()) {
        expect(selection.activateBackend(plane, b).bffScope).toBe(scopeOf(b.bffAppId));
      }
    }
  });

  it('missing config (no VITE_BACKENDS, no VITE_BFF_API_URL): no backend, and a BFF call sends nothing', async () => {
    vi.stubEnv('VITE_BFF_API_URL', '');
    const { selection, bff, msalAuth } = await load();
    const { acquirer } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    const result = await selection.resolveBackendSelection('ciam', { search: '' });
    expect(result).toMatchObject({ kind: 'none', reason: expect.stringMatching(/No backend is configured/) });
    await expect(bff.bffApiCall('/api/v1/external/me')).rejects.toThrow(/No backend is active/);
    expect(acquirer).not.toHaveBeenCalled();
    expect(h.fetches).toEqual([]);
  });

  it.each([
    ['plain http to a non-local host', [{ ...FABRIKAM, bffBaseUrl: 'http://fabrikam-bff.test' }]],
    ['a base URL with a path', [{ ...FABRIKAM, bffBaseUrl: 'https://fabrikam-bff.test/api' }]],
    ['a base URL with a query', [{ ...FABRIKAM, bffBaseUrl: 'https://fabrikam-bff.test?x=1' }]],
    ['a base URL with credentials', [{ ...FABRIKAM, bffBaseUrl: 'https://user:pw@fabrikam-bff.test' }]],
    ['a javascript: URL', [{ ...FABRIKAM, bffBaseUrl: 'javascript:alert(1)' }]],
    ['an app id with a slash', [{ ...FABRIKAM, bffAppId: 'evil/scope' }]],
    ['a URL-shaped customer key', [{ ...FABRIKAM, customerKey: EVIL }]],
    ['a duplicate key', [FABRIKAM, { ...CONTOSO, customerKey: 'fabrikam' }]],
    ['an invalid CIAM app id', [{ ...FABRIKAM, ciamBffAppId: 'api://evil' }]],
  ])('refuses VITE_BACKENDS with %s (no backend is selected)', async (_label, entries) => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify(entries));
    const { selection } = await load();
    const result = await selection.resolveBackendSelection('workforce', { search: '' });
    expect(result.kind).toBe('none');
  });

  it.each([
    ['not JSON', '[{'],
    ['an empty list', '[]'],
    ['an object instead of a list', JSON.stringify(FABRIKAM)],
  ])('refuses VITE_BACKENDS that is %s', async (_label, raw) => {
    vi.stubEnv('VITE_BACKENDS', raw);
    const { selection } = await load();
    expect((await selection.resolveBackendSelection('ciam', { search: '' })).kind).toBe('none');
  });

  it('the T240c directory client is a stub behind VITE_USE_DIRECTORY_SERVICE: enabled, it fails closed (no default)', async () => {
    stubTwoBackends();
    vi.stubEnv('VITE_USE_DIRECTORY_SERVICE', 'true');
    const { selection } = await load();
    expect(selection.createBackendDirectory('workforce')).toBeInstanceOf(selection.DirectoryServiceClient);
    const result = await selection.resolveBackendSelection('workforce', { search: '?customer=fabrikam' });
    expect(result).toMatchObject({ kind: 'none', reason: expect.stringMatching(/T240c/) });
    expect(() => selection.activeBffScope('workforce')).toThrow();
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Selection: the picker, ?customer= and stored keys
// ---------------------------------------------------------------------------------------------------------------

describe('Selection: picker only when several are known and none is active; ?customer= only chooses', () => {
  it.each(['workforce', 'ciam'] as const)(
    '%s: two configured backends and no key → the picker, with both',
    async plane => {
      stubTwoBackends();
      const { selection } = await load();
      const result = await selection.resolveBackendSelection(plane, { search: '' });
      expect(result.kind).toBe('pick');
      expect((result as { options: Array<{ customerKey: string }> }).options.map(o => o.customerKey)).toEqual([
        'contoso',
        'fabrikam',
      ]);
    }
  );

  it('one configured backend → selected without a picker', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([FABRIKAM]));
    const { selection } = await load();
    expect(await selection.resolveBackendSelection('workforce', { search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'fabrikam' },
    });
  });

  it('an active backend (stored for this tab) → no picker, even with two configured', async () => {
    stubTwoBackends();
    const first = await load();
    first.selection.activateBackend('workforce', { ...FABRIKAM });
    const { selection } = await load(); // reload: module state gone, the tab's sessionStorage remains
    expect(await selection.resolveBackendSelection('workforce', { search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'fabrikam' },
    });
  });

  it.each(['workforce', 'ciam'] as const)('%s: ?customer={configured key} selects it', async plane => {
    stubTwoBackends();
    const { selection } = await load();
    expect(
      await selection.resolveBackendSelection(plane, { search: '?customer=fabrikam&record=matter:1' })
    ).toMatchObject({ kind: 'selected', backend: { customerKey: 'fabrikam', bffBaseUrl: FABRIKAM.bffBaseUrl } });
  });

  it('reads ?customer= from the page URL by default', async () => {
    stubTwoBackends();
    window.history.replaceState(null, '', '/?customer=contoso');
    const { selection } = await load();
    expect(await selection.resolveBackendSelection('workforce')).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'contoso' },
    });
  });

  it.each(['workforce', 'ciam'] as const)(
    '%s: a crafted ?customer= key not in config is ignored → the picker',
    async plane => {
      stubTwoBackends();
      const { selection } = await load();
      expect((await selection.resolveBackendSelection(plane, { search: '?customer=unknown-key' })).kind).toBe('pick');
    }
  );

  it('a crafted ?customer= key not in config is ignored → the single default', async () => {
    vi.stubEnv('VITE_BACKENDS', JSON.stringify([FABRIKAM]));
    const { selection } = await load();
    expect(await selection.resolveBackendSelection('ciam', { search: '?customer=unknown-key' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'fabrikam' },
    });
  });

  it('a crafted key overrides nothing: a stored active key stays when ?customer= is unknown', async () => {
    stubTwoBackends();
    const first = await load();
    first.selection.activateBackend('workforce', { ...CONTOSO });
    const { selection } = await load();
    expect(await selection.resolveBackendSelection('workforce', { search: '?customer=nope' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'contoso' },
    });
  });

  it('a tampered stored active key (not configured) is ignored', async () => {
    stubTwoBackends();
    sessionStorage.setItem('spaarke.ext.activeCustomer', 'evil-customer');
    const { selection } = await load();
    expect((await selection.resolveBackendSelection('workforce', { search: '' })).kind).toBe('pick');
  });

  it('URL-shaped values in ANY query parameter never become a request host or a scope', async () => {
    stubTwoBackends();
    const search =
      `?customer=${encodeURIComponent(EVIL)}&bff=${EVIL}&bffBaseUrl=${EVIL}&apiBaseUrl=${EVIL}` +
      `&scope=${encodeURIComponent('api://evil/user_impersonation')}&bffAppId=evil`;
    const { selection, bff, msalAuth } = await load();
    const result = await selection.resolveBackendSelection('workforce', { search });
    expect(result.kind).toBe('pick'); // the URL-shaped key is not a key: ignored
    const options = (result as { options: Array<{ bffBaseUrl: string; bffAppId: string }> }).options;
    expect(options.map(o => o.bffBaseUrl)).toEqual([CONTOSO.bffBaseUrl, FABRIKAM.bffBaseUrl]);

    // Pick one and call the BFF with the crafted query still on the page.
    window.history.replaceState(null, '', `/${search}`);
    selection.activateBackend('workforce', (result as { options: any[] }).options[1]);
    const { acquirer, scopes } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    await bff.bffApiCall('/api/v1/external/me');
    expect(h.fetches.map(f => f.url)).toEqual([`${FABRIKAM.bffBaseUrl}/api/v1/external/me`]);
    expect(scopes).toEqual([scopeOf(FABRIKAM.bffAppId)]);
    expectNothingEvil();
  });

  it('workforce: a key the directory knows only by key lookup (not the caller’s membership) is ignored', async () => {
    const { selection } = await load();
    const directory = {
      resolveWorkforce: vi.fn(async () => [{ ...CONTOSO }]),
      resolveByKey: vi.fn(async (key: string) => (key === 'fabrikam' ? { ...FABRIKAM } : null)),
    };
    expect(
      await selection.resolveBackendSelection('workforce', { directory, search: '?customer=fabrikam' })
    ).toMatchObject({ kind: 'selected', backend: { customerKey: 'contoso' } });
    expect(directory.resolveByKey).not.toHaveBeenCalled();
  });

  it('CIAM: ?customer= resolves through the directory key lookup; a record for a different key is ignored', async () => {
    const { selection } = await load();
    const directory = {
      resolveWorkforce: vi.fn(async () => []),
      resolveByKey: vi.fn(async (key: string) =>
        key === 'fabrikam' ? { ...FABRIKAM } : key === 'contoso' ? { ...FABRIKAM } : null
      ),
    };
    expect(await selection.resolveBackendSelection('ciam', { directory, search: '?customer=fabrikam' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'fabrikam' },
    });
    // 'contoso' answered with fabrikam's record → not a contoso backend → nothing selectable.
    expect(await selection.resolveBackendSelection('ciam', { directory, search: '?customer=contoso' })).toMatchObject({
      kind: 'none',
    });
  });

  it('directory records with an unsafe URL or app id are dropped; a directory failure fails closed', async () => {
    const { selection } = await load();
    const bad = {
      resolveWorkforce: async () => [
        { ...CONTOSO, bffBaseUrl: 'http://evil.example' },
        { ...FABRIKAM, bffAppId: 'evil/x' },
        { ...FABRIKAM, customerKey: 'ok', bffBaseUrl: 'https://ok-bff.test' },
      ],
      resolveByKey: async () => null,
    };
    expect(await selection.resolveBackendSelection('workforce', { directory: bad, search: '' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'ok', bffBaseUrl: 'https://ok-bff.test' },
    });
    const failing = {
      resolveWorkforce: async () => {
        throw new Error('directory down');
      },
      resolveByKey: async () => null,
    };
    expect(await selection.resolveBackendSelection('workforce', { directory: failing, search: '' })).toEqual({
      kind: 'none',
      reason: 'directory down',
    });
  });

  it('the picker can only choose one of the offered backends', async () => {
    stubTwoBackends();
    const { selection } = await load();
    const result = (await selection.resolveBackendSelection('workforce', { search: '' })) as { options: any[] };
    expect(() => selection.chooseBackend('workforce', result.options, 'evil')).toThrow(/not one of the offered/);
    expect(selection.chooseBackend('workforce', result.options, 'fabrikam').bffBaseUrl).toBe(FABRIKAM.bffBaseUrl);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Requests: URL and token scope always come from the same (selected) backend
// ---------------------------------------------------------------------------------------------------------------

describe('Same build, two backends: requests and token scopes go to whichever backend is selected', () => {
  it('bffApiCall and bffApiBlob go to the selected backend with its scope; re-selecting moves both', async () => {
    stubTwoBackends();
    const { selection, bff, msalAuth } = await load();
    const { acquirer, scopes } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);

    selection.activateBackend('workforce', { ...FABRIKAM });
    await bff.bffApiCall('/api/v1/external/me');
    await bff.bffApiBlob('/api/v1/external/projects/1/documents/2/content');

    selection.activateBackend('workforce', { ...CONTOSO });
    await bff.bffApiCall('/api/v1/external/me', { method: 'POST', body: '{}' });

    expect(h.fetches.map(f => [f.url, f.authorization])).toEqual([
      [`${FABRIKAM.bffBaseUrl}/api/v1/external/me`, `Bearer token:${scopeOf(FABRIKAM.bffAppId)}`],
      [
        `${FABRIKAM.bffBaseUrl}/api/v1/external/projects/1/documents/2/content`,
        `Bearer token:${scopeOf(FABRIKAM.bffAppId)}`,
      ],
      [`${CONTOSO.bffBaseUrl}/api/v1/external/me`, `Bearer token:${scopeOf(CONTOSO.bffAppId)}`],
    ]);
    expect(scopes).toEqual([scopeOf(FABRIKAM.bffAppId), scopeOf(FABRIKAM.bffAppId), scopeOf(CONTOSO.bffAppId)]);
  });

  it('the 401 retry stays on the same backend and asks for the same scope', async () => {
    stubTwoBackends();
    const { selection, bff, msalAuth } = await load();
    const { acquirer, scopes } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    selection.activateBackend('ciam', { ...CONTOSO, bffAppId: CONTOSO.ciamBffAppId });
    h.fetchStatus = [401, 200];
    await bff.bffApiCall('/api/v1/external/me');
    expect(h.fetches.map(f => f.url)).toEqual([
      `${CONTOSO.bffBaseUrl}/api/v1/external/me`,
      `${CONTOSO.bffBaseUrl}/api/v1/external/me`,
    ]);
    expect(scopes).toEqual([scopeOf(CONTOSO.ciamBffAppId), scopeOf(CONTOSO.ciamBffAppId)]);
  });

  it('grid reads: a 401 causes exactly one retry, and the retry forces a network refresh of the token', async () => {
    stubTwoBackends();
    const { selection, msalAuth } = await load();
    const { gridDataverseClient } = await import('../src/services/gridDataverseClient');
    const scope = scopeOf(FABRIKAM.bffAppId);
    const acquirer = vi.fn(async (_scope?: string, options?: { forceRefresh?: boolean }) =>
      options?.forceRefresh ? 'fresh-token' : 'cached-token'
    );
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    selection.activateBackend('workforce', { ...FABRIKAM });

    // 401 then 200: one retry, with a forced refresh, and the retried request carries the fresh token.
    h.fetchStatus = [401, 200];
    await gridDataverseClient.retrieveRecord('sprk_matter', '1', ['sprk_name']);
    expect(h.fetches.map(f => f.authorization)).toEqual(['Bearer cached-token', 'Bearer fresh-token']);
    expect(acquirer.mock.calls).toEqual([[scope], [scope, { forceRefresh: true }]]);

    // 401 then 401: still exactly one retry — a second refusal is not retried again.
    h.fetches.length = 0;
    acquirer.mockClear();
    h.fetchStatus = [401, 401, 200];
    await gridDataverseClient.retrieveRecord('sprk_matter', '2', ['sprk_name']).catch(() => undefined);
    expect(h.fetches).toHaveLength(2);
    expect(acquirer.mock.calls).toEqual([[scope], [scope, { forceRefresh: true }]]);
  });

  it('a mismatched origin/scope pair cannot happen: a backend switch during token acquisition does not re-route the request', async () => {
    stubTwoBackends();
    const { selection, bff, msalAuth } = await load();
    selection.activateBackend('workforce', { ...FABRIKAM });
    msalAuth.setActiveBffTokenAcquirer(async scope => {
      // While FABRIKAM's token is being acquired, the active backend changes to CONTOSO.
      selection.activateBackend('workforce', { ...CONTOSO });
      return `token:${scope}`;
    });
    await bff.bffApiCall('/api/v1/external/me');
    // The request kept the URL of the backend whose scope the token was for.
    expect(h.fetches).toHaveLength(1);
    expect(h.fetches[0].url).toBe(`${FABRIKAM.bffBaseUrl}/api/v1/external/me`);
    expect(h.fetches[0].authorization).toBe(`Bearer token:${scopeOf(FABRIKAM.bffAppId)}`);
  });

  it('no active backend → the call rejects before any token or fetch (fail closed)', async () => {
    stubTwoBackends();
    const { bff, msalAuth } = await load();
    const { acquirer } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    await expect(bff.bffApiCall('/api/v1/external/me')).rejects.toThrow(/No backend is active/);
    await expect(bff.bffApiBlob('/api/v1/external/x')).rejects.toThrow(/No backend is active/);
    expect(acquirer).not.toHaveBeenCalled();
    expect(h.fetches).toEqual([]);
  });

  it.each(['@evil.example/api', '//evil.example/api', `${EVIL}/api`, '\\\\evil.example\\api', 'api/v1/external/me'])(
    'a path that is not root-relative on the backend (%s) is refused: no token, no fetch',
    async badPath => {
      stubTwoBackends();
      const { selection, bff, msalAuth } = await load();
      const { acquirer } = recordingAcquirer();
      msalAuth.setActiveBffTokenAcquirer(acquirer);
      selection.activateBackend('workforce', { ...FABRIKAM });
      await expect(bff.bffApiCall(badPath)).rejects.toThrow(/not root-relative|left the backend origin/);
      expect(acquirer).not.toHaveBeenCalled();
      expect(h.fetches).toEqual([]);
    }
  );

  it('grid reads follow the active backend (not the build) and refuse to run without one', async () => {
    stubTwoBackends();
    const { selection, msalAuth } = await load();
    const { gridDataverseClient } = await import('../src/services/gridDataverseClient');
    const { acquirer, scopes } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);

    await expect(gridDataverseClient.retrieveRecord('sprk_matter', '1')).rejects.toThrow(/No backend is active/);
    expect(h.fetches).toEqual([]);

    selection.activateBackend('workforce', { ...FABRIKAM });
    await gridDataverseClient.retrieveRecord('sprk_matter', '1', ['sprk_name']);
    selection.activateBackend('workforce', { ...CONTOSO });
    await gridDataverseClient.retrieveRecord('sprk_matter', '2', ['sprk_name']);

    expect(h.fetches.map(f => new URL(f.url).origin)).toEqual([FABRIKAM.bffBaseUrl, CONTOSO.bffBaseUrl]);
    expect(h.fetches.every(f => new URL(f.url).pathname.startsWith('/api/v1/external/api/dataverse/record/'))).toBe(
      true
    );
    expect(scopes).toEqual([scopeOf(FABRIKAM.bffAppId), scopeOf(CONTOSO.bffAppId)]);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Both planes' token acquirers use the seam
// ---------------------------------------------------------------------------------------------------------------

describe('Both planes acquire tokens for the active backend’s scope', () => {
  // A9 (task 058): the browser workforce plane is the multitenant HOME plane. The sign-in asks for OIDC scopes only; the
  // signed-in tenant (tid) picks the backend, and the BFF token request carries that entry's scope.
  const FABRIKAM_TID = 'fabf0000-0000-4000-8000-00000000000f';
  const workforceAccount = { homeAccountId: 'w', username: 'w@fabrikam.test', tenantId: FABRIKAM_TID };

  it('workforce home plane: the login scope is OIDC-only; the MSAL token request is the signed-in tenant’s backend scope; the BFF call goes to its host', async () => {
    vi.stubEnv('VITE_WORKFORCE_HOME_MSAL_CLIENT_ID', '33333333-2222-3333-4444-555555555555');
    vi.stubEnv(
      'VITE_BACKENDS',
      JSON.stringify([
        { ...CONTOSO, homeTenantIds: ['c0c0c0c0-0000-4000-8000-000000000001'] },
        { ...FABRIKAM, homeTenantIds: [FABRIKAM_TID] },
      ])
    );
    stubWorkforceEnv();
    h.accounts = [workforceAccount];
    const { msalAuth, bff } = await load();
    const plane = await import('../src/auth/standalone-plane');

    const prepared = await plane.prepareWorkforceHomePlane({ search: '' });
    expect(prepared.kind).toBe('plane');
    const resolved = (prepared as { plane: any }).plane;
    expect(resolved.bffScope).toBe(scopeOf(FABRIKAM.bffAppId));
    plane.applyStandalonePlane(resolved);
    expect(msalAuth.getActiveLoginScope()).toBe('openid');

    await bff.bffApiCall('/api/v1/external/me');
    const workforcePca = h.pcas.find(p => p.instance === resolved.instance)!;
    expect(h.requests.filter(r => r.instance === workforcePca.instance.name).map(r => r.scopes)).toEqual([
      [scopeOf(FABRIKAM.bffAppId)],
    ]);
    expect(h.fetches.map(f => [f.url, f.authorization])).toEqual([
      [`${FABRIKAM.bffBaseUrl}/api/v1/external/me`, `Bearer token:${scopeOf(FABRIKAM.bffAppId)}`],
    ]);
  });

  it('CIAM plane uses the same seam: its scope is the selected backend’s api://{CIAM app id}/user_impersonation', async () => {
    stubTwoBackends();
    h.accounts = [{ homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TID }];
    const { selection, msalAuth, bff } = await load();
    const plane = await import('../src/auth/standalone-plane');
    const msalConfig = await import('../src/auth/msal-config');

    const result = await selection.resolveBackendSelection('ciam', { search: '?customer=contoso' });
    expect(result.kind).toBe('selected');
    selection.activateBackend('ciam', (result as { backend: any }).backend);
    const resolved = plane.resolveStandalonePlane('ciam');
    expect(resolved.instance).toBe(msalConfig.msalInstance);
    expect(resolved.bffScope).toBe(scopeOf(CONTOSO.ciamBffAppId));
    plane.applyStandalonePlane(resolved);
    expect(msalAuth.getActiveLoginScope()).toBe(scopeOf(CONTOSO.ciamBffAppId));

    await bff.bffApiCall('/api/v1/external/me');
    await expect(msalAuth.acquireBffToken()).resolves.toBe(`token:${scopeOf(CONTOSO.ciamBffAppId)}`);
    const ciamName = (msalConfig.msalInstance as any).name;
    const ciamScopes = h.requests.filter(r => r.instance === ciamName).flatMap(r => r.scopes);
    expect(ciamScopes).toEqual([scopeOf(CONTOSO.ciamBffAppId), scopeOf(CONTOSO.ciamBffAppId)]);
    expect(ciamScopes.every(s => s.endsWith('/user_impersonation'))).toBe(true);
    expect(h.fetches.map(f => f.url)).toEqual([`${CONTOSO.bffBaseUrl}/api/v1/external/me`]);
  });

  it('the default CIAM acquirer (no plane applied) also follows the active CIAM backend', async () => {
    stubTwoBackends();
    h.accounts = [{ homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TID }];
    const { selection, bff } = await load();
    selection.activateBackend('ciam', { ...FABRIKAM });
    await bff.bffApiCall('/api/v1/external/me');
    expect(h.requests.map(r => r.scopes)).toEqual([[scopeOf(FABRIKAM.bffAppId)]]);
    expect(h.fetches.map(f => f.url)).toEqual([`${FABRIKAM.bffBaseUrl}/api/v1/external/me`]);
  });

  it('acquireBffToken handed to a library as a callback ignores any argument: the scope is always the active backend’s', async () => {
    stubTwoBackends();
    h.accounts = [{ homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TID }];
    const { selection, msalAuth } = await load();
    selection.activateBackend('ciam', { ...FABRIKAM });
    const asCallback = msalAuth.acquireBffToken as unknown as (arg: string) => Promise<string>;
    await expect(asCallback('api://evil/user_impersonation')).resolves.toBe(`token:${scopeOf(FABRIKAM.bffAppId)}`);
    expect(h.requests.map(r => r.scopes)).toEqual([[scopeOf(FABRIKAM.bffAppId)]]);
    expectNothingEvil();
  });

  it.each([
    ['a bare scope string', 'User.Read'],
    ['a user_impersonation scope as a string (the seam takes a backend record, not a string)', scopeOf('evil')],
    ['null', null],
    ['a record with no scope', { plane: 'ciam' }],
    ['a record with a foreign scope', { plane: 'ciam', bffScope: 'api://evil/.default' }],
    ['a record with the retired SDAP.Access scope', { plane: 'ciam', bffScope: 'api://evil/SDAP.Access' }],
    [
      'a record with a scope that only ends in user_impersonation',
      { plane: 'ciam', bffScope: 'https://evil/user_impersonation' },
    ],
  ])('the token seam refuses %s: nothing reaches acquireTokenSilent / NAA', async (_label, arg) => {
    stubTwoBackends();
    h.accounts = [{ homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TID }];
    const { selection, msalAuth } = await load();
    selection.activateBackend('ciam', { ...FABRIKAM });
    const seam = msalAuth.acquireActiveBffToken as unknown as (a: unknown) => Promise<string>;
    await expect(seam(arg)).rejects.toThrow(/no token is requested/);
    expect(h.requests).toEqual([]);
    expectNothingEvil();
  });

  it('the token seam passes a backend snapshot’s user_impersonation scope through to the acquirer (and only that)', async () => {
    stubTwoBackends();
    h.accounts = [{ homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TID }];
    const { selection, msalAuth } = await load();
    const active = selection.activateBackend('ciam', { ...FABRIKAM });
    await expect(msalAuth.acquireActiveBffToken(active)).resolves.toBe(`token:${scopeOf(FABRIKAM.bffAppId)}`);
    expect(h.requests.map(r => r.scopes)).toEqual([[scopeOf(FABRIKAM.bffAppId)]]);
  });

  it('a backend resolved for the workforce plane never yields a CIAM token request (plane mismatch fails closed)', async () => {
    stubTwoBackends();
    h.accounts = [{ homeAccountId: 'c', username: 'c@partner.test', tenantId: CIAM_TID }];
    const { selection, msalAuth } = await load();
    selection.activateBackend('workforce', { ...FABRIKAM });
    await expect(msalAuth.acquireBffToken()).rejects.toThrow(/workforce plane, not ciam/);
    expect(() => msalAuth.getActiveLoginScope()).toThrow(/workforce plane, not ciam/);
    expect(h.requests).toEqual([]);
  });

  it(
    'Teams: the adapter acquires for the selected workforce backend’s scope and the BFF call goes to its host',
    { timeout: 60000 },
    async () => {
      stubTwoBackends();
      stubWorkforceEnv();
      // The NAA host account belongs to the Teams context tenant (task 075: a token for any other tenant is refused).
      h.accounts = [{ homeAccountId: 'n', username: 'n@fabrikam.test', tenantId: SPAARKE_TENANT }];
      h.nestable = async config => makeFakeInstance('naa', {}, config);
      const { selection, msalAuth, bff } = await load();
      const { createTeamsHostAdapter } = await import('../src/host/TeamsHostAdapter');

      selection.activateBackend('workforce', { ...FABRIKAM });
      await createTeamsHostAdapter(SPAARKE_TENANT).initialize();
      await bff.bffApiCall('/api/v1/external/me');
      await expect(msalAuth.acquireActiveBffToken()).resolves.toBe(`token:${scopeOf(FABRIKAM.bffAppId)}`);

      expect(h.requests.map(r => `${r.instance}:${r.scopes.join(',')}`)).toEqual([
        `naa:${scopeOf(FABRIKAM.bffAppId)}`,
        `naa:${scopeOf(FABRIKAM.bffAppId)}`,
        `naa:${scopeOf(FABRIKAM.bffAppId)}`,
      ]);
      expect(h.fetches.map(f => [f.url, f.authorization])).toEqual([
        [`${FABRIKAM.bffBaseUrl}/api/v1/external/me`, `Bearer token:${scopeOf(FABRIKAM.bffAppId)}`],
      ]);
    }
  );
});

// ---------------------------------------------------------------------------------------------------------------
// Known customer keys: localStorage holds keys only, survives reloads, and its absence is harmless
// ---------------------------------------------------------------------------------------------------------------

describe('Known customer keys persist across reloads; storage failures are harmless', () => {
  /** A CIAM directory with no membership lookup (as T240c): backends only by key. */
  const ciamDirectory = () => ({
    resolveWorkforce: vi.fn(async () => []),
    resolveByKey: vi.fn(async (key: string) =>
      key === 'contoso' ? { ...CONTOSO } : key === 'fabrikam' ? { ...FABRIKAM } : null
    ),
  });

  it('activating a backend remembers its KEY only (no URL, scope or token in localStorage)', async () => {
    stubTwoBackends();
    const { selection } = await load();
    selection.activateBackend('ciam', { ...FABRIKAM });
    selection.activateBackend('ciam', { ...CONTOSO });
    const stored = localStorage.getItem('spaarke.ext.knownCustomers') as string;
    expect(JSON.parse(stored)).toEqual(['contoso', 'fabrikam']);
    expect(stored).not.toMatch(/https?:|api:|token|user_impersonation/);
    for (let i = 0; i < localStorage.length; i++) {
      expect(localStorage.getItem(localStorage.key(i) as string)).not.toMatch(/https?:\/\/|api:\/\//);
    }
  });

  it('after a reload (new tab: no active key) the remembered keys come back and drive the picker', async () => {
    const first = await load();
    first.selection.activateBackend('ciam', { ...CONTOSO });
    first.selection.activateBackend('ciam', { ...FABRIKAM });
    sessionStorage.clear(); // a new tab: nothing active

    const { selection } = await load();
    const directory = ciamDirectory();
    const result = await selection.resolveBackendSelection('ciam', { directory, search: '' });
    expect(result.kind).toBe('pick');
    expect((result as { options: any[] }).options.map(o => o.customerKey).sort()).toEqual(['contoso', 'fabrikam']);
    // Each remembered key was only LOOKED UP; the backend data came from the directory.
    expect(directory.resolveByKey).toHaveBeenCalledWith('contoso');
    expect(directory.resolveByKey).toHaveBeenCalledWith('fabrikam');
  });

  it('tampered localStorage values (URLs, non-strings, junk) are dropped', async () => {
    localStorage.setItem('spaarke.ext.knownCustomers', JSON.stringify([EVIL, 42, { url: EVIL }, 'contoso', 'contoso']));
    const { selection } = await load();
    expect(selection.getKnownCustomerKeys()).toEqual(['contoso']);
    localStorage.setItem('spaarke.ext.knownCustomers', '{not json');
    expect(selection.getKnownCustomerKeys()).toEqual([]);
  });

  it('with localStorage AND sessionStorage throwing, selection, activation and requests still work', async () => {
    stubTwoBackends();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError: storage blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('SecurityError: storage blocked');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('SecurityError: storage blocked');
    });
    const { selection, bff, msalAuth } = await load();
    expect(selection.getKnownCustomerKeys()).toEqual([]);

    const pick = await selection.resolveBackendSelection('ciam', { search: '' });
    expect(pick.kind).toBe('pick');
    const chosen = selection.chooseBackend('ciam', (pick as { options: any[] }).options, 'fabrikam');
    expect(chosen.bffBaseUrl).toBe(FABRIKAM.bffBaseUrl);
    expect(await selection.resolveBackendSelection('ciam', { search: '?customer=contoso' })).toMatchObject({
      kind: 'selected',
      backend: { customerKey: 'contoso' },
    });

    const { acquirer } = recordingAcquirer();
    msalAuth.setActiveBffTokenAcquirer(acquirer);
    await bff.bffApiCall('/api/v1/external/me');
    expect(h.fetches.map(f => f.url)).toEqual([`${FABRIKAM.bffBaseUrl}/api/v1/external/me`]);
    expect(() => selection.clearActiveBackend()).not.toThrow();
  });
});

// ---------------------------------------------------------------------------------------------------------------
// The customer picker
// ---------------------------------------------------------------------------------------------------------------

describe('CustomerPicker', () => {
  // Generous timeout: the first render pulls in Fluent and its icon chunks, which are slow to transform cold.
  it(
    'shows one button per offered backend (display names only) and reports the chosen key',
    { timeout: 60000 },
    async () => {
      const { CustomerPicker } = await import('../src/components/auth/CustomerPicker');
      const onChoose = vi.fn();
      render(
        React.createElement(
          FluentProvider,
          { theme: webLightTheme },
          React.createElement(CustomerPicker, {
            options: [
              { customerKey: 'contoso', displayName: 'Contoso' },
              { customerKey: 'fabrikam', displayName: 'Fabrikam' },
            ],
            onChoose,
          })
        )
      );
      expect(screen.getByText('Choose an organization')).toBeTruthy();
      expect(screen.getAllByRole('button').map(b => b.textContent)).toEqual(['Contoso', 'Fabrikam']);
      expect(document.body.textContent).not.toMatch(/https?:|api:\/\//);
      fireEvent.click(screen.getByRole('button', { name: 'Fabrikam' }));
      expect(onChoose).toHaveBeenCalledWith('fabrikam');
    }
  );

  it('uses Fluent semantic tokens only (no hard-coded colours; ADR-021 dark mode)', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../src/components/auth/CustomerPicker.tsx'), 'utf8');
    expect(source).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(source).not.toMatch(/\b(rgb|rgba|hsl)\(/);
    expect(source).not.toMatch(/['"](white|black)['"]/);
    expect(source).toMatch(/tokens\.colorNeutralBackground1/);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Shipped configuration: no baked URL in source, no SDAP.Access default
// ---------------------------------------------------------------------------------------------------------------

describe('Shipped configuration', () => {
  const SPA_ROOT = path.resolve(__dirname, '..');
  const REPO_ROOT = path.resolve(SPA_ROOT, '../../..');

  it('the SDAP.Access CIAM default is gone from the env files and the deploy workflow; dev requests user_impersonation', () => {
    for (const rel of ['.env.development', '.env.example', '.env.production']) {
      expect(fs.readFileSync(path.join(SPA_ROOT, rel), 'utf8')).not.toContain('SDAP.Access');
    }
    const workflow = fs.readFileSync(path.join(REPO_ROOT, '.github/workflows/deploy-external-spa.yml'), 'utf8');
    expect(workflow).not.toContain('SDAP.Access');
    // Task 043: the scope is a GitHub environment variable (dev: external-spa-dev), not a literal in the workflow.
    expect(workflow).toMatch(/^\s*VITE_MSAL_BFF_SCOPE: \$\{\{ vars\.VITE_MSAL_BFF_SCOPE \}\}\s*$/m);
    expect(fs.readFileSync(path.join(SPA_ROOT, '.env.development'), 'utf8')).toMatch(
      /^VITE_MSAL_BFF_SCOPE=api:\/\/[0-9a-f-]{36}\/user_impersonation\s*$/m
    );
  });

  it('EVERY workflow that builds this SPA: no SDAP.Access, and both BFF scopes (CIAM + workforce) come from the matching environment variable', () => {
    const dir = path.join(REPO_ROOT, '.github/workflows');
    const builders = fs
      .readdirSync(dir)
      .filter(name => /\.ya?ml$/.test(name))
      .map(name => ({ name, text: fs.readFileSync(path.join(dir, name), 'utf8') }))
      .filter(({ text }) => text.includes('src/client/external-spa') && /npm run build\b/.test(text));
    // The two known builders: the SWA deploy and the Teams app package gate (a new builder is covered automatically).
    expect(builders.map(b => b.name)).toEqual(
      expect.arrayContaining(['deploy-external-spa.yml', 'deploy-teams-app.yml'])
    );
    for (const { name, text } of builders) {
      expect(text, `${name} still references SDAP.Access`).not.toContain('SDAP.Access');
      for (const key of ['VITE_MSAL_BFF_SCOPE', 'VITE_TEAMS_MSAL_BFF_SCOPE']) {
        const values = [...text.matchAll(new RegExp(`^\\s*${key}:[ \\t]*(.+?)[ \\t]*$`, 'gm'))].map(m => m[1]);
        expect(values.length, `${name} sets no ${key}`).toBeGreaterThan(0);
        for (const value of values) {
          // Task 043: the value is the GitHub environment variable of the same name, never a literal. The scope FORMAT
          // (api://{app id}/user_impersonation) is enforced at run time by the deploy workflow's preflight step and by
          // backend-selection in the SPA.
          expect(value, `${name}: ${key}`).toBe('${{ vars.' + key + ' }}');
        }
      }
      for (const key of ['VITE_TEAMS_MSAL_CLIENT_ID', 'VITE_TEAMS_MSAL_TENANT_ID']) {
        expect(text, `${name} sets no ${key}`).toContain(`${key}: \${{ vars.${key} }}`);
      }
    }
  });

  it('no source file reads the BFF URL env key except the backend selection module (no baked URL elsewhere)', () => {
    const offenders: string[] = [];
    const walk = (dir: string) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (/\.(ts|tsx)$/.test(entry.name) && !entry.name.endsWith('.d.ts')) {
          const text = fs.readFileSync(full, 'utf8');
          const rel = path.relative(SPA_ROOT, full).replace(/\\/g, '/');
          if (
            rel !== 'src/auth/backend-selection.ts' &&
            /readOptionalEnv\('VITE_BFF_API_URL'\)|env\.VITE_BFF_API_URL/.test(text)
          ) {
            offenders.push(rel);
          }
          if (
            /\bBFF_API_URL\b/.test(text.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*/g, '')) &&
            !/VITE_BFF_API_URL/.test(text)
          ) {
            offenders.push(`${rel} (BFF_API_URL)`);
          }
        }
      }
    };
    walk(path.join(SPA_ROOT, 'src'));
    expect(offenders).toEqual([]);
  });
});
