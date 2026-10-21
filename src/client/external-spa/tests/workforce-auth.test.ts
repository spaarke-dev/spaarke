// Workforce client switch (spaarke-SPA-external-access-platform-r3 task 040, FR-16; ADR-028 Amendment A8).
//
// RETARGETED by task 075 (ADR-028 A9 Teams amendment): the Teams BFF token now comes from the MULTITENANT home client on
// authority /{Teams context tid} (see tests/teams-home-auth.test.ts); the Teams cases below keep their assertions with the
// home client id and the per-request authority. The single-tenant A8 client stays tenant-pinned (guest-token link).
//
// The Teams tab (and the stored-link guest token) signs in through ONE single-tenant Spaarke
// client: authority https://login.microsoftonline.com/{Spaarke tenant id} (never /organizations or /common), scope
// api://{customer BFF app id}/user_impersonation (the A2-era scope is retired). In Teams: NAA first, then an MSAL
// popup that lands on /auth-callback.html; Teams SSO (getAuthToken) and the manifest's webApplicationInfo are gone.
// The CIAM plane is unchanged. The browser "company account" sign-in is NOT on this client any more (ADR-028 A9,
// task 058): it uses a separate multitenant client and is tested in workforce-home-auth.test.ts; the shared
// token-acquisition mechanics below are exercised here with the home plane's shape (signed-in tenant + per-request
// authority).
//
// MSAL and Teams JS are mocked: every MSAL instance the code creates is recorded with its configuration, and every
// token request it makes is recorded with its scopes, so the tests assert what the code ASKS for, not a stub's echo.
import * as fs from 'node:fs';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const CLIENT_ID = '11111111-2222-3333-4444-555555555555';
// The multitenant home client (task 075: the Teams BFF token client). Different from the A8 client above.
const HOME_CLIENT_ID = '66666666-7777-8888-9999-000000000001';
const BFF_SCOPE = 'api://99999999-8888-7777-6666-555555555555/user_impersonation';
const AUTHORITY = `https://login.microsoftonline.com/${SPAARKE_TENANT}`;
// The Teams context tenant of the Teams tests: a customer's directory, NOT Spaarke's (production refuses the Spaarke tid in
// Teams: validateHomeTenantIds), so every Teams authority assertion below is on this tid.
const TEAMS_TID = 'c0c0c0c0-0000-4000-8000-000000000001';
const TEAMS_AUTHORITY = `https://login.microsoftonline.com/${TEAMS_TID}`;
// The retired A2-era scope name, assembled so that this file, too, passes the "nowhere in the SPA" grep.
const RETIRED_SCOPE = ['access', 'as', 'user'].join('_');

type Behaviour = {
  silent?: (req: any) => Promise<any>;
  sso?: (req: any) => Promise<any>;
  popup?: (req: any) => Promise<any>;
};

const h = vi.hoisted(() => ({
  // Every token request any fake MSAL instance receives: which instance, which call, which scopes.
  requests: [] as Array<{ instance: string; call: string; scopes: string[]; request: any }>,
  // Every standard PublicClientApplication constructed (config + the fake), in order.
  pcas: [] as Array<{ config: any; instance: any }>,
  // Every createNestablePublicClientApplication call (config), in order.
  nestableConfigs: [] as any[],
  // Per-test behaviour.
  nestable: null as null | ((config: any) => Promise<any>),
  pcaBehaviour: {} as Record<string, unknown>,
  teamsInitialize: vi.fn(),
  getAuthToken: vi.fn(),
  // teams-js nestedAppAuth.isNAAChannelRecommended(): does the host recommend the NAA channel?
  naaRecommended: vi.fn(),
  // Every getAllAccounts(filter) call any fake instance receives.
  accountQueries: [] as Array<{ instance: string; filter: unknown }>,
}));

function makeFakeInstance(name: string, behaviour: Behaviour, config: any): any {
  let active: any = null;
  const accounts: any[] = [];
  const record = (call: string, req: any) =>
    h.requests.push({ instance: name, call, scopes: req?.scopes, request: req });
  const remember = (result: any) => {
    if (result?.account && !accounts.includes(result.account)) accounts.push(result.account);
    return result;
  };
  return {
    name,
    config,
    initialize: vi.fn(async () => undefined),
    getActiveAccount: () => active,
    setActiveAccount: (a: any) => {
      active = a;
    },
    getAllAccounts: (filter?: { tenantId?: string }) => {
      h.accountQueries.push({ instance: name, filter });
      return filter?.tenantId ? accounts.filter(a => a.tenantId === filter.tenantId) : accounts;
    },
    acquireTokenSilent: vi.fn(async (req: any) => {
      record('acquireTokenSilent', req);
      if (!behaviour.silent) throw new Error('silent not configured');
      return remember(await behaviour.silent(req));
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
  };
}

vi.mock('@azure/msal-browser', async importOriginal => {
  const actual = await importOriginal<typeof import('@azure/msal-browser')>();
  class FakePublicClientApplication {
    constructor(config: any) {
      const behaviour = (h.pcaBehaviour as Behaviour) ?? {};
      const instance = makeFakeInstance(`pca#${h.pcas.length}`, behaviour, config);
      h.pcas.push({ config, instance });
      return instance;
    }
  }
  return {
    ...actual,
    PublicClientApplication: FakePublicClientApplication,
    createNestablePublicClientApplication: async (config: any) => {
      h.nestableConfigs.push(config);
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
  // Present so a regression that calls Teams SSO is observable; the code under test must never call it.
  authentication: { getAuthToken: h.getAuthToken },
}));

function stubWorkforceEnv(overrides: Partial<Record<string, string>> = {}): void {
  const values: Record<string, string> = {
    VITE_TEAMS_MSAL_CLIENT_ID: CLIENT_ID,
    VITE_TEAMS_MSAL_TENANT_ID: SPAARKE_TENANT,
    VITE_TEAMS_MSAL_BFF_SCOPE: BFF_SCOPE,
    VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: HOME_CLIENT_ID,
    ...overrides,
  } as Record<string, string>;
  for (const [key, value] of Object.entries(values)) vi.stubEnv(key, value);
}

// Teams config (A9): the multitenant client, and the Teams context tid (a customer's tenant, never Spaarke's).
const teamsCfg = { clientId: HOME_CLIENT_ID, tenantId: TEAMS_TID, bffScope: BFF_SCOPE };
// The single-tenant A8 client config (the guest-token link; tenant-pinned to Spaarke).
const a8Cfg = { clientId: CLIENT_ID, tenantId: SPAARKE_TENANT, bffScope: BFF_SCOPE };
// A Teams token result modelled on NAA (msal-browser 5.5.0): `tenantId` and the account come from the HOST's account, the
// token's own tenant is the `tid` claim.
const token = (n: string) => ({
  accessToken: `token-${n}`,
  tenantId: TEAMS_TID,
  idTokenClaims: { tid: TEAMS_TID },
  account: { homeAccountId: n, username: `${n}@contoso.test`, tenantId: TEAMS_TID },
});
const failing = (message: string) => async () => {
  throw new Error(message);
};

/** Fresh copies of the auth modules, so memoized MSAL instances and the Teams path do not leak between tests. */
async function loadAuth() {
  vi.resetModules();
  const config = await import('../src/config');
  const msalConfig = await import('../src/auth/msal-config');
  const msalAuth = await import('../src/auth/msal-auth');
  return { config, msalConfig, msalAuth };
}

beforeEach(() => {
  h.requests.length = 0;
  h.pcas.length = 0;
  h.nestableConfigs.length = 0;
  h.nestable = null;
  h.pcaBehaviour = {};
  h.teamsInitialize.mockReset();
  h.teamsInitialize.mockResolvedValue(undefined);
  h.getAuthToken.mockReset();
  h.getAuthToken.mockResolvedValue('teams-sso-token');
  h.naaRecommended.mockReset();
  h.naaRecommended.mockReturnValue(true);
  h.accountQueries.length = 0;
  // The Teams strategy builds its MSAL configuration for the multitenant home client (task 075).
  vi.stubEnv('VITE_WORKFORCE_HOME_MSAL_CLIENT_ID', HOME_CLIENT_ID);
});

afterEach(() => {
  vi.unstubAllEnvs();
});

/** Every token request recorded asks for exactly one scope, and it is the user_impersonation scope. */
function expectOnlyUserImpersonationRequests(): void {
  expect(h.requests.length).toBeGreaterThan(0);
  for (const r of h.requests) {
    expect(r.scopes).toHaveLength(1);
    expect(r.scopes[0]).toMatch(/\/user_impersonation$/);
  }
}

// ---------------------------------------------------------------------------------------------------------------
// The workforce authority and scope (both hosts)
// ---------------------------------------------------------------------------------------------------------------

describe('Workforce authority is pinned to the Spaarke tenant (A8)', () => {
  it('builds https://login.microsoftonline.com/{tenant id} with the configured client id', async () => {
    const { msalConfig } = await loadAuth();
    const descriptor = msalConfig.workforceAuthorityConfig(a8Cfg);
    expect(descriptor).toEqual({ kind: 'workforce', clientId: CLIENT_ID, bffScope: BFF_SCOPE, authority: AUTHORITY });

    const built = msalConfig.buildMsalConfiguration(descriptor);
    expect(built.auth.authority).toBe(AUTHORITY);
    expect(built.auth.clientId).toBe(CLIENT_ID);
    expect(built.auth.knownAuthorities).toEqual([]);
    expect(built.cache?.cacheLocation).toBe('sessionStorage');
  });

  it.each(['organizations', 'common', 'consumers', '', 'spaarke.onmicrosoft.com'])(
    'refuses the tenant %j — no multi-tenant alias, no non-GUID tenant',
    async tenantId => {
      const { msalConfig } = await loadAuth();
      expect(() => msalConfig.workforceAuthorityConfig({ ...a8Cfg, tenantId })).toThrow(/tenant/i);
    }
  );

  it.each([
    'https://login.microsoftonline.com/organizations',
    'https://login.microsoftonline.com/common',
    'https://login.microsoftonline.com/consumers',
    `https://login.microsoftonline.com/${SPAARKE_TENANT}/v2.0`,
    `https://evil.example/${SPAARKE_TENANT}`,
    `http://login.microsoftonline.com/${SPAARKE_TENANT}`,
  ])('the builder refuses a hand-made workforce authority %s', async authority => {
    const { msalConfig } = await loadAuth();
    expect(() =>
      msalConfig.buildMsalConfiguration({ kind: 'workforce', clientId: CLIENT_ID, bffScope: BFF_SCOPE, authority })
    ).toThrow(/tenant-pinned/);
  });

  it('the builder refuses a workforce scope other than .../user_impersonation', async () => {
    const { msalConfig } = await loadAuth();
    for (const bffScope of [
      `api://99999999-8888-7777-6666-555555555555/${RETIRED_SCOPE}`,
      'api://99999999-8888-7777-6666-555555555555/SDAP.Access',
      'https://graph.microsoft.com/.default',
    ]) {
      expect(() =>
        msalConfig.buildMsalConfiguration({ kind: 'workforce', clientId: CLIENT_ID, bffScope, authority: AUTHORITY })
      ).toThrow(/user_impersonation/);
    }
  });

  it('the multitenant authority kind no longer exists', async () => {
    const { msalConfig } = await loadAuth();
    expect(() =>
      msalConfig.buildMsalConfiguration({
        kind: 'workforce-multitenant' as never,
        clientId: CLIENT_ID,
        bffScope: BFF_SCOPE,
        authority: 'https://login.microsoftonline.com/organizations',
      })
    ).toThrow(/Unknown authority kind/);
  });
});

describe('Workforce configuration comes from Spaarke-owned env config and fails loud', () => {
  it('reads the client id, Spaarke tenant id and user_impersonation scope', async () => {
    stubWorkforceEnv();
    const { config } = await loadAuth();
    expect(config.getWorkforceEnvConfig()).toEqual({
      clientId: CLIENT_ID,
      tenantId: SPAARKE_TENANT,
      bffScope: BFF_SCOPE,
    });
  });

  it.each([
    ['a missing tenant id', { VITE_TEAMS_MSAL_TENANT_ID: '' }, /VITE_TEAMS_MSAL_TENANT_ID/],
    [
      'an unsubstituted tenant id',
      { VITE_TEAMS_MSAL_TENANT_ID: '#{TEAMS_MSAL_TENANT_ID}#' },
      /VITE_TEAMS_MSAL_TENANT_ID/,
    ],
    ['the organizations tenant', { VITE_TEAMS_MSAL_TENANT_ID: 'organizations' }, /tenant id/],
    ['the common tenant', { VITE_TEAMS_MSAL_TENANT_ID: 'common' }, /tenant id/],
    [
      'the retired A2-era scope',
      { VITE_TEAMS_MSAL_BFF_SCOPE: `api://99999999-8888-7777-6666-555555555555/${RETIRED_SCOPE}` },
      /user_impersonation/,
    ],
    ['a missing client id', { VITE_TEAMS_MSAL_CLIENT_ID: '' }, /VITE_TEAMS_MSAL_CLIENT_ID/],
  ])('refuses %s', async (_label, overrides, message) => {
    stubWorkforceEnv(overrides);
    const { config } = await loadAuth();
    expect(() => config.getWorkforceEnvConfig()).toThrow(message);
  });
});

describe('Browser workforce plane is no longer built on the single-tenant A8 client (A9, task 058)', () => {
  it('resolveStandalonePlane refuses the workforce realm and builds no MSAL instance (prepareWorkforceHomePlane prepares it)', async () => {
    stubWorkforceEnv();
    vi.resetModules();
    const plane = await import('../src/auth/standalone-plane');
    expect(() => plane.resolveStandalonePlane('workforce')).toThrow(/prepareWorkforceHomePlane/);
    // (The CIAM singleton is built when msal-config loads; no instance exists for the A8 workforce client.)
    expect(h.pcas.filter(p => p.config.auth.clientId === CLIENT_ID)).toEqual([]);
  });

  it('the A8 client stays tenant-pinned: the builder still refuses organizations for it, although a multitenant client now exists', async () => {
    stubWorkforceEnv({ VITE_WORKFORCE_HOME_MSAL_CLIENT_ID: '66666666-7777-8888-9999-000000000001' });
    const { msalConfig } = await loadAuth();
    expect(() =>
      msalConfig.buildMsalConfiguration({
        kind: 'workforce',
        clientId: CLIENT_ID,
        bffScope: BFF_SCOPE,
        authority: 'https://login.microsoftonline.com/organizations',
      })
    ).toThrow(/tenant-pinned/);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Teams: NAA first, then the MSAL popup; never Teams SSO
// ---------------------------------------------------------------------------------------------------------------

describe('Teams workforce sign-in', () => {
  it('uses NAA first, on the Teams-tenant authority (multitenant client), and does not build the popup fallback when NAA succeeds', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: async () => token('naa') }, config);

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-naa');

    expect(h.nestableConfigs).toHaveLength(1);
    expect(h.nestableConfigs[0].auth.authority).toBe(TEAMS_AUTHORITY);
    expect(h.nestableConfigs[0].auth.clientId).toBe(HOME_CLIENT_ID);
    expect(h.nestableConfigs[0].auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    // Only the CIAM singleton was built as a standard PCA; no popup fallback instance.
    expect(h.pcas.filter(p => p.config.auth.authority === TEAMS_AUTHORITY)).toHaveLength(0);
    expect(h.requests.map(r => `${r.instance}:${r.call}`)).toEqual(['naa:ssoSilent']);
    expectOnlyUserImpersonationRequests();
    expect(h.getAuthToken).not.toHaveBeenCalled();
    expect(msalAuth.getTeamsWorkforceSignedInInstance()?.name).toBe('naa');
  });

  it('falls back to acquireTokenPopup (via /auth-callback.html) when NAA fails — never to Teams SSO', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: failing('NAA bridge error') }, config);
    h.pcaBehaviour = { popup: async () => token('popup') };

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-popup');

    // NAA was attempted first, then the popup.
    expect(h.requests.map(r => `${r.instance.startsWith('pca') ? 'pca' : r.instance}:${r.call}`)).toEqual([
      'naa:ssoSilent',
      'pca:acquireTokenPopup',
    ]);
    const popupPca = h.pcas.find(p => p.config.auth.authority === TEAMS_AUTHORITY);
    expect(popupPca).toBeDefined();
    expect(popupPca!.config.auth.clientId).toBe(HOME_CLIENT_ID);
    expect(popupPca!.config.auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    expect(popupPca!.config.cache.cacheLocation).toBe('sessionStorage');
    expect(popupPca!.instance.initialize).toHaveBeenCalled();
    // No cached account: the popup asks the user to pick one (OfficeNaaStrategy._tryAcquireWith).
    expect(popupPca!.instance.acquireTokenPopup).toHaveBeenCalledWith({
      scopes: [BFF_SCOPE],
      authority: TEAMS_AUTHORITY,
      loginHint: undefined,
      prompt: 'select_account',
    });
    expectOnlyUserImpersonationRequests();
    expect(h.getAuthToken).not.toHaveBeenCalled();
    expect(msalAuth.getTeamsWorkforceSignedInInstance()).toBe(popupPca!.instance);
  });

  it('falls back to the popup when the NAA instance cannot be created at all', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = failing('createNestablePublicClientApplication failed');
    h.pcaBehaviour = { popup: async () => token('popup') };

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-popup');
    expect(h.getAuthToken).not.toHaveBeenCalled();
  });

  it('after the popup has signed the user in, later calls use that account silently instead of failing NAA again', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: failing('NAA bridge error') }, config);
    h.pcaBehaviour = { popup: async () => token('popup'), silent: async () => token('popup-silent') };

    await msalAuth.acquireTeamsWorkforceBffToken(teamsCfg);
    h.requests.length = 0;

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-popup-silent');
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenSilent']);
    expect(h.requests[0].request.account).toMatchObject({ homeAccountId: 'popup' });
    expectOnlyUserImpersonationRequests();
  });

  it('throws TeamsWorkforceAuthError carrying both failures when NAA and the popup fail — no getAuthToken', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: failing('NAA bridge error') }, config);
    h.pcaBehaviour = { popup: failing('popup_window_error') };

    const error = await msalAuth.acquireTeamsWorkforceBffToken(teamsCfg).then(
      () => null,
      (e: unknown) => e
    );
    expect(error).toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    const cause = (error as { cause: { naaError: Error; popupError: Error } }).cause;
    expect(cause.naaError.message).toBe('NAA bridge error');
    expect(cause.popupError.message).toBe('popup_window_error');
    expect(h.getAuthToken).not.toHaveBeenCalled();
  });

  it('a forced refresh (the 401 retry) reaches the NAA silent call; the first calls carry no forceRefresh', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config =>
      makeFakeInstance('naa', { sso: async () => token('naa'), silent: async () => token('naa-fresh') }, config);

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-naa');
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-naa-fresh');
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg, { forceRefresh: true })).resolves.toBe(
      'token-naa-fresh'
    );

    const silent = h.requests.filter(r => r.call === 'acquireTokenSilent');
    expect(silent).toHaveLength(2);
    expect(silent[0].request).not.toHaveProperty('forceRefresh');
    expect(silent[1].request).toMatchObject({ scopes: [BFF_SCOPE], forceRefresh: true });
  });

  it('fails loud outside a Teams host', async () => {
    const { msalAuth } = await loadAuth();
    h.teamsInitialize.mockRejectedValue(new Error('no host'));
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).rejects.toBeInstanceOf(
      msalAuth.TeamsWorkforceAuthError
    );
    expect(h.nestableConfigs).toHaveLength(0);
  });

  // Generous timeout: the adapter pulls in the shared UI library's theme module, which is slow to transform cold.
  it(
    'the host adapter hands MsalProvider the instance that signed the user in (popup fallback)',
    { timeout: 60000 },
    async () => {
      stubWorkforceEnv();
      vi.resetModules();
      h.nestable = async config => makeFakeInstance('naa', { sso: failing('NAA bridge error') }, config);
      h.pcaBehaviour = { popup: async () => token('popup'), silent: async () => token('popup-silent') };
      const { createTeamsHostAdapter } = await import('../src/host/TeamsHostAdapter');
      const msalAuth = await import('../src/auth/msal-auth');

      const adapter = createTeamsHostAdapter(TEAMS_TID);
      await adapter.initialize();

      const popupPca = h.pcas.find(p => p.config.auth.authority === TEAMS_AUTHORITY);
      expect(adapter.getMsalInstance()).toBe(popupPca!.instance);
      // The active BFF acquirer is the Teams strategy: a BFF call resolves a user_impersonation token.
      await expect(msalAuth.acquireActiveBffToken()).resolves.toBe('token-popup-silent');
      expectOnlyUserImpersonationRequests();
      expect(h.getAuthToken).not.toHaveBeenCalled();
    }
  );
});

// ---------------------------------------------------------------------------------------------------------------
// The CIAM plane is unchanged
// ---------------------------------------------------------------------------------------------------------------

describe('CIAM plane is unchanged', () => {
  it('builds the CIAM singleton exactly as before (CIAM authority, knownAuthorities, origin redirect, sessionStorage)', async () => {
    const { msalConfig, msalAuth } = await loadAuth();
    expect(msalConfig.ciamAuthorityConfig).toEqual({
      kind: 'ciam',
      clientId: import.meta.env.VITE_MSAL_CLIENT_ID,
      authority: import.meta.env.VITE_MSAL_AUTHORITY,
      bffScope: import.meta.env.VITE_MSAL_BFF_SCOPE,
    });
    expect(msalConfig.buildMsalConfiguration(msalConfig.ciamAuthorityConfig)).toEqual({
      auth: {
        clientId: import.meta.env.VITE_MSAL_CLIENT_ID,
        authority: import.meta.env.VITE_MSAL_AUTHORITY,
        knownAuthorities: [new URL(import.meta.env.VITE_MSAL_AUTHORITY).host],
        redirectUri: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
      },
      cache: { cacheLocation: 'sessionStorage' },
    });
    const ciamPca = h.pcas.find(p => p.instance === msalConfig.msalInstance);
    expect(ciamPca?.config.auth.authority).toBe(import.meta.env.VITE_MSAL_AUTHORITY);
    expect(msalAuth.getActiveLoginScope()).toBe(import.meta.env.VITE_MSAL_BFF_SCOPE);
  });

  it('the browser CIAM plane returns the CIAM singleton and scope', async () => {
    vi.resetModules();
    const plane = await import('../src/auth/standalone-plane');
    const msalConfig = await import('../src/auth/msal-config');
    const resolved = plane.resolveStandalonePlane('ciam');
    expect(resolved.instance).toBe(msalConfig.msalInstance);
    expect(resolved.bffScope).toBe(import.meta.env.VITE_MSAL_BFF_SCOPE);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Shipped files: no Teams SSO, no retired scope, no webApplicationInfo, a static popup redirect page
// ---------------------------------------------------------------------------------------------------------------

const SPA_ROOT = path.resolve(__dirname, '..');

function listFiles(dir: string, skip: (rel: string) => boolean): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    const rel = path.relative(SPA_ROOT, full).replace(/\\/g, '/');
    if (skip(rel)) continue;
    if (entry.isDirectory()) out.push(...listFiles(full, skip));
    else out.push(full);
  }
  return out;
}

describe('Shipped files', () => {
  it('no source file calls Teams getAuthToken or imports Teams JS authentication', () => {
    const offenders = listFiles(path.join(SPA_ROOT, 'src'), () => false)
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

  it('the retired A2-era scope name appears nowhere in the external SPA', () => {
    const retired = new RegExp(RETIRED_SCOPE, 'i');
    const offenders = listFiles(SPA_ROOT, rel => rel === 'node_modules' || rel === 'dist' || rel.startsWith('.git'))
      .filter(f => !/\.(png|ico|jpg|jpeg|gif|webp|woff2?|ttf|eot)$/i.test(f))
      .filter(f => retired.test(fs.readFileSync(f, 'utf8')))
      .map(f => path.relative(SPA_ROOT, f));
    expect(offenders).toEqual([]);
  });

  it('the Teams manifest declares no webApplicationInfo', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(SPA_ROOT, 'appPackage/manifest.json'), 'utf8'));
    expect(manifest).not.toHaveProperty('webApplicationInfo');
  });

  it('the SWA serves /auth-callback.html as a static page, not the index.html fallback', () => {
    const swa = JSON.parse(fs.readFileSync(path.join(SPA_ROOT, 'staticwebapp.config.json'), 'utf8'));
    expect(swa.navigationFallback.exclude).toContain('/auth-callback.html');
    expect(fs.existsSync(path.join(SPA_ROOT, 'public/auth-callback.html'))).toBe(true);
  });

  it("the popup page runs the installed msal-browser's redirect bridge verbatim and creates no MSAL instance", () => {
    const html = fs.readFileSync(path.join(SPA_ROOT, 'public/auth-callback.html'), 'utf8').replace(/\r\n/g, '\n');
    const inlined =
      /<!-- msal-redirect-bridge:begin -->\s*<script>\n([\s\S]*?)\n\s*<\/script>\s*<!-- msal-redirect-bridge:end -->/.exec(
        html
      );
    expect(inlined).not.toBeNull();
    const installed = fs
      .readFileSync(
        path.join(SPA_ROOT, 'node_modules/@azure/msal-browser/lib/redirect-bridge/msal-redirect-bridge.min.js'),
        'utf8'
      )
      .replace(/\r\n/g, '\n')
      .trim();
    expect(inlined![1].trim()).toBe(installed);
    // Outside comments, the page only invokes the bridge: it never builds its own MSAL instance.
    const code = html.replace(/<!--[\s\S]*?-->/g, '');
    expect(code).toContain('bridge.broadcastResponseToMainFrame()');
    expect(code).not.toMatch(/PublicClientApplication/);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Completion fixes (2026-10-10): silent renewal bridge page + interactive fallback, Teams error diagnostics,
// CI workflow / config alignment with A8
// ---------------------------------------------------------------------------------------------------------------

describe('Browser silent renewal: CIAM requests are unchanged on the wire; the workforce iframe retry uses the bridge page', () => {
  const callbackUri = () => `${window.location.origin}/auth-callback.html`;
  const CIAM_BACKEND = {
    plane: 'ciam',
    bffScope: 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation',
  } as const;
  const WORKFORCE_BACKEND = {
    plane: 'workforce',
    bffScope: 'api://bbbbbbbb-0000-0000-0000-00000000000b/user_impersonation',
  } as const;
  const OTHER_TENANT = '7052feba-bfc4-43e0-b09e-65014b429131';
  /** The CIAM tenant of the test build (vitest.config.ts VITE_MSAL_TENANT_ID). */
  const CIAM_TENANT = '00000000-0000-0000-0000-000000000141';
  // A9: the browser workforce plane is the signed-in user's OWN directory (the home tenant), not the Spaarke tenant.
  const HOME_TENANT = 'c0c0c0c0-0000-4000-8000-000000000001';
  const HOME_AUTHORITY = `https://login.microsoftonline.com/${HOME_TENANT}`;
  const account = { homeAccountId: 'u', username: 'u@contoso.test', tenantId: HOME_TENANT };

  function browserInstance(silent: (req: any) => Promise<any>, accounts: any[] = [account]) {
    return {
      getAllAccounts: vi.fn((filter?: { tenantId?: string }) =>
        filter?.tenantId ? accounts.filter(a => a.tenantId === filter.tenantId) : accounts
      ),
      acquireTokenSilent: vi.fn(async (req: any) => silent(req)),
      acquireTokenRedirect: vi.fn(async () => undefined),
    } as any;
  }

  /** Error msal-browser raises when the refresh token has expired (silently resolvable by iframe). */
  async function refreshTokenExpired() {
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    return new InteractionRequiredAuthError('refresh_token_expired');
  }

  describe('CIAM plane (acquireBffToken)', () => {
    /** A partner account: its tenant is the CIAM tenant (vitest.config.ts VITE_MSAL_TENANT_ID). */
    const ciamAccount = { homeAccountId: 'p', username: 'p@partner.test', tenantId: CIAM_TENANT };

    /** acquireBffToken() against the CIAM singleton, with its MSAL calls replaced by spies. */
    async function ciam(silent: (req: any) => Promise<any>, accounts: any[] = [ciamAccount]) {
      const { msalAuth, msalConfig } = await loadAuth();
      const stub = browserInstance(silent, accounts);
      Object.assign(msalConfig.msalInstance, stub);
      return { msalAuth, instance: msalConfig.msalInstance as any };
    }

    it('sends exactly { scopes, account } on the silent call: no redirectUri, no cacheLookupPolicy', async () => {
      const { msalAuth, instance } = await ciam(async () => ({ accessToken: 'tok' }));
      await expect(msalAuth.acquireBffToken()).resolves.toBe('tok');
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenSilent.mock.calls[0][0]).toStrictEqual({
        scopes: [import.meta.env.VITE_MSAL_BFF_SCOPE],
        account: ciamAccount,
      });
      // MSAL is asked for every account (no tenant filter on the wire); the CIAM predicate is applied in the SPA.
      expect(instance.getAllAccounts).toHaveBeenCalledWith();
      expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    });

    it('falls back to the interactive redirect on InteractionRequiredAuthError — after ONE silent call', async () => {
      const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
      const { msalAuth, instance } = await ciam(async () => {
        throw new InteractionRequiredAuthError('login_required');
      });
      await expect(msalAuth.acquireBffToken()).rejects.toThrow(/redirecting to login/);
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenSilent.mock.calls[0][0]).not.toHaveProperty('redirectUri');
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).toMatchObject({
        scopes: [import.meta.env.VITE_MSAL_BFF_SCOPE],
        account: ciamAccount,
      });
      // The interactive redirect keeps the instance's own (origin) redirect URI: no per-request override.
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).not.toHaveProperty('redirectUri');
    });

    it('falls back to the interactive redirect when msal’s own silent iframe times out (timed_out)', async () => {
      const { BrowserAuthError, BrowserAuthErrorCodes } = await import('@azure/msal-browser');
      const { msalAuth, instance } = await ciam(async () => {
        throw new BrowserAuthError(BrowserAuthErrorCodes.timedOut);
      });
      await expect(msalAuth.acquireBffToken()).rejects.toThrow(/redirecting to login/);
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenSilent.mock.calls[0][0]).not.toHaveProperty('redirectUri');
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    });

    it('still rethrows an unrelated failure without redirecting', async () => {
      const { msalAuth, instance } = await ciam(async () => {
        throw new Error('network down');
      });
      await expect(msalAuth.acquireBffToken()).rejects.toThrow('network down');
      expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    });

    it('a forced refresh (the 401 retry) adds exactly forceRefresh: true to the silent call - nothing else changes', async () => {
      const { msalAuth, instance } = await ciam(async () => ({ accessToken: 'tok-fresh' }));
      await expect(msalAuth.acquireActiveBffToken(CIAM_BACKEND, { forceRefresh: true })).resolves.toBe('tok-fresh');
      expect(instance.acquireTokenSilent.mock.calls[0][0]).toStrictEqual({
        scopes: [CIAM_BACKEND.bffScope],
        account: ciamAccount,
        forceRefresh: true,
      });
      // ... and a normal call through the same seam sends no forceRefresh key at all.
      await msalAuth.acquireActiveBffToken(CIAM_BACKEND);
      expect(instance.acquireTokenSilent.mock.calls[1][0]).toStrictEqual({
        scopes: [CIAM_BACKEND.bffScope],
        account: ciamAccount,
      });
    });

    it('the browser CIAM plane carries only the CIAM account filter (no tenant filter, no iframe redirect URI, no authority)', async () => {
      vi.resetModules();
      const plane = await import('../src/auth/standalone-plane');
      const msalAuth = await import('../src/auth/msal-auth');
      const resolved = plane.resolveStandalonePlane('ciam');
      expect(resolved.acquireOptions).toStrictEqual({ accountFilter: msalAuth.isCiamAccount });
      expect(resolved.accountFilter).toBe(msalAuth.isCiamAccount);
    });

    // #2166: MSAL's account list is shared by every client in the tab. A company account left by the home plane
    // ("Sign in with your company account" -> "not set up" -> "choose a different sign-in" -> Partner) was used for the
    // CIAM token, and msal-browser refused it with authority_mismatch.
    it('#2166: a cached company account alone is not a partner sign-in - no silent call with it; the CIAM sign-in redirect starts', async () => {
      const { msalAuth, instance } = await ciam(async () => ({ accessToken: 'must-not-be-used' }), [account]);
      await expect(msalAuth.acquireBffToken()).rejects.toThrow(/No authenticated account/);
      expect(instance.acquireTokenSilent).not.toHaveBeenCalled();
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).toStrictEqual({
        scopes: [import.meta.env.VITE_MSAL_BFF_SCOPE],
      });
    });

    it('#2166: with a company account cached FIRST, the partner account is the one used', async () => {
      const { msalAuth, instance } = await ciam(async () => ({ accessToken: 'tok' }), [account, ciamAccount]);
      await expect(msalAuth.acquireBffToken()).resolves.toBe('tok');
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenSilent.mock.calls[0][0].account).toBe(ciamAccount);
    });

    it('#2166: the plane seam (applyStandalonePlane) filters the same way, also on the 401 forced refresh', async () => {
      const { msalAuth, instance } = await ciam(async () => ({ accessToken: 'tok' }), [account, ciamAccount]);
      const plane = await import('../src/auth/standalone-plane');
      plane.applyStandalonePlane(plane.resolveStandalonePlane('ciam'));
      await expect(msalAuth.acquireActiveBffToken(CIAM_BACKEND)).resolves.toBe('tok');
      await expect(msalAuth.acquireActiveBffToken(CIAM_BACKEND, { forceRefresh: true })).resolves.toBe('tok');
      expect(instance.acquireTokenSilent.mock.calls.map((c: any[]) => c[0].account)).toEqual([
        ciamAccount,
        ciamAccount,
      ]);
      expect(instance.acquireTokenSilent.mock.calls[1][0]).toStrictEqual({
        scopes: [CIAM_BACKEND.bffScope],
        account: ciamAccount,
        forceRefresh: true,
      });
    });

    it('isCiamAccount: only the CIAM tenant (any case); another GUID, a non-GUID or no tenant is not a partner account', async () => {
      const { msalAuth } = await loadAuth();
      expect(msalAuth.isCiamAccount({ tenantId: CIAM_TENANT })).toBe(true);
      expect(msalAuth.isCiamAccount({ tenantId: CIAM_TENANT.toUpperCase() })).toBe(true);
      expect(msalAuth.isCiamAccount({ tenantId: HOME_TENANT })).toBe(false);
      expect(msalAuth.isCiamAccount({ tenantId: OTHER_TENANT })).toBe(false);
      expect(msalAuth.isCiamAccount({ tenantId: 'organizations' })).toBe(false);
      expect(msalAuth.isCiamAccount({})).toBe(false);
    });
  });

  describe('workforce browser plane', () => {
    const options = () => ({ tenantId: HOME_TENANT, silentRedirectUri: callbackUri(), authority: HOME_AUTHORITY });

    it('a forced refresh (the 401 retry) adds only forceRefresh: true to the first silent call; the plane seam passes it through', async () => {
      const { msalAuth } = await loadAuth();
      const plane = await import('../src/auth/standalone-plane');
      const { CacheLookupPolicy } = await import('@azure/msal-browser');
      const instance = browserInstance(async () => ({ accessToken: 'tok-fresh' }));
      plane.applyStandalonePlane({
        realm: 'workforce',
        instance,
        bffScope: WORKFORCE_BACKEND.bffScope,
        home: true,
        acquireOptions: options(),
      } as any);

      await expect(msalAuth.acquireActiveBffToken(WORKFORCE_BACKEND, { forceRefresh: true })).resolves.toBe(
        'tok-fresh'
      );
      expect(instance.acquireTokenSilent.mock.calls[0][0]).toStrictEqual({
        scopes: [WORKFORCE_BACKEND.bffScope],
        account,
        cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
        forceRefresh: true,
        authority: HOME_AUTHORITY,
      });
      await msalAuth.acquireActiveBffToken(WORKFORCE_BACKEND);
      expect(instance.acquireTokenSilent.mock.calls[1][0]).toStrictEqual({
        scopes: [WORKFORCE_BACKEND.bffScope],
        account,
        cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
        authority: HOME_AUTHORITY,
      });
    });

    it('first silent call has NO redirectUri (refresh-token grant is unchanged); success makes exactly one call', async () => {
      const { msalAuth } = await loadAuth();
      const { CacheLookupPolicy } = await import('@azure/msal-browser');
      const instance = browserInstance(async () => ({ accessToken: 'tok' }));
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).resolves.toBe('tok');
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      const request = instance.acquireTokenSilent.mock.calls[0][0];
      expect(request).not.toHaveProperty('redirectUri');
      expect(request).toStrictEqual({
        scopes: [BFF_SCOPE],
        account,
        cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
        authority: HOME_AUTHORITY,
      });
      expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    });

    it('retries ONCE through the bridge iframe (policy Skip + redirectUri) only when the refresh token is expired', async () => {
      const { msalAuth } = await loadAuth();
      const { CacheLookupPolicy } = await import('@azure/msal-browser');
      const expired = await refreshTokenExpired();
      const instance = browserInstance(async req => {
        if (req.cacheLookupPolicy === CacheLookupPolicy.Skip) return { accessToken: 'tok-iframe' };
        throw expired;
      });
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).resolves.toBe('tok-iframe');

      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(2);
      const [first, second] = instance.acquireTokenSilent.mock.calls.map((c: any[]) => c[0]);
      expect(first).not.toHaveProperty('redirectUri');
      expect(second).toStrictEqual({
        scopes: [BFF_SCOPE],
        account,
        cacheLookupPolicy: CacheLookupPolicy.Skip,
        redirectUri: callbackUri(),
        authority: HOME_AUTHORITY,
      });
      expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    });

    it.each([
      [
        'no_tokens_found',
        () => import('@azure/msal-browser').then(m => new m.InteractionRequiredAuthError('no_tokens_found')),
      ],
      [
        'invalid_grant (a plain AuthError)',
        () => import('@azure/msal-browser').then(m => new m.AuthError('invalid_grant')),
      ],
      [
        'token_refresh_required',
        () => import('@azure/msal-browser').then(m => new m.AuthError('token_refresh_required')),
      ],
    ])('also retries through the iframe for %s', async (_label, makeError) => {
      const { msalAuth } = await loadAuth();
      const { CacheLookupPolicy } = await import('@azure/msal-browser');
      const error = await makeError();
      const instance = browserInstance(async req => {
        if (req.cacheLookupPolicy === CacheLookupPolicy.Skip) return { accessToken: 'tok-iframe' };
        throw error;
      });
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).resolves.toBe('tok-iframe');
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(2);
    });

    it('a plain InteractionRequiredAuthError (login_required) goes straight to the redirect: no iframe retry', async () => {
      const { msalAuth } = await loadAuth();
      const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
      const instance = browserInstance(async () => {
        throw new InteractionRequiredAuthError('login_required');
      });
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).rejects.toThrow(
        /redirecting to login/
      );
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).toMatchObject({
        scopes: [BFF_SCOPE],
        account,
        authority: HOME_AUTHORITY,
      });
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).not.toHaveProperty('redirectUri');
    });

    it('falls back to the redirect when the iframe retry times out (the bridge never answered)', async () => {
      const { msalAuth } = await loadAuth();
      const { BrowserAuthError, BrowserAuthErrorCodes, CacheLookupPolicy } = await import('@azure/msal-browser');
      const expired = await refreshTokenExpired();
      const instance = browserInstance(async req => {
        if (req.cacheLookupPolicy === CacheLookupPolicy.Skip)
          throw new BrowserAuthError(BrowserAuthErrorCodes.timedOut);
        throw expired;
      });
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).rejects.toThrow(
        /redirecting to login/
      );
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(2);
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    });

    it('falls back to the redirect when the iframe retry says interaction is required (SSO session gone)', async () => {
      const { msalAuth } = await loadAuth();
      const { InteractionRequiredAuthError, CacheLookupPolicy } = await import('@azure/msal-browser');
      const expired = await refreshTokenExpired();
      const instance = browserInstance(async req => {
        if (req.cacheLookupPolicy === CacheLookupPolicy.Skip) throw new InteractionRequiredAuthError('login_required');
        throw expired;
      });
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).rejects.toThrow(
        /redirecting to login/
      );
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    });

    it('still rethrows an unrelated failure without an iframe retry or a redirect', async () => {
      const { msalAuth } = await loadAuth();
      const instance = browserInstance(async () => {
        throw new Error('network down');
      });
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).rejects.toThrow('network down');
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect).not.toHaveBeenCalled();
    });

    it('only counts accounts of the signed-in tenant: a cached CIAM account is not a workforce sign-in', async () => {
      const { msalAuth } = await loadAuth();
      const ciamAccount = { homeAccountId: 'c', username: 'c@partner.test', tenantId: OTHER_TENANT };
      const instance = browserInstance(async () => ({ accessToken: 'must-not-be-used' }), [ciamAccount]);

      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).rejects.toThrow(
        /No authenticated account/
      );

      expect(instance.getAllAccounts).toHaveBeenCalledWith({ tenantId: HOME_TENANT });
      expect(instance.acquireTokenSilent).not.toHaveBeenCalled();
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).toStrictEqual({
        scopes: [BFF_SCOPE],
        authority: HOME_AUTHORITY,
      });
    });

    it('picks the signed-in tenant account when a CIAM account is cached next to it', async () => {
      const { msalAuth } = await loadAuth();
      const ciamAccount = { homeAccountId: 'c', username: 'c@partner.test', tenantId: OTHER_TENANT };
      const instance = browserInstance(async () => ({ accessToken: 'tok' }), [ciamAccount, account]);
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).resolves.toBe('tok');
      expect(instance.acquireTokenSilent.mock.calls[0][0].account).toBe(account);
    });

    it("AuthGuard / portal-user account lists are scoped to the plane: the home plane keeps only the signed-in tenant's accounts, CIAM only CIAM accounts (#2166)", async () => {
      stubWorkforceEnv();
      vi.resetModules();
      const plane = await import('../src/auth/standalone-plane');
      const msalAuth = await import('../src/auth/msal-auth');
      const otherTenantAccount = { homeAccountId: 'c', tenantId: OTHER_TENANT };
      const homeAccount = { homeAccountId: 's', tenantId: HOME_TENANT.toUpperCase() };

      plane.applyStandalonePlane({
        realm: 'workforce',
        instance: browserInstance(async () => ({ accessToken: 'unused' })),
        bffScope: WORKFORCE_BACKEND.bffScope,
        home: true,
        accountFilter: msalAuth.isWorkforceHomeAccount,
        acquireOptions: options(),
      } as any);
      expect(msalAuth.accountsForActivePlane([otherTenantAccount, homeAccount])).toEqual([homeAccount]);
      expect(msalAuth.accountsForActivePlane([otherTenantAccount])).toEqual([]);

      plane.applyStandalonePlane(plane.resolveStandalonePlane('ciam'));
      const partnerAccount = { homeAccountId: 'p', tenantId: CIAM_TENANT };
      expect(msalAuth.accountsForActivePlane([homeAccount, partnerAccount, otherTenantAccount])).toEqual([
        partnerAccount,
      ]);
      // A company account alone does not count as signed in, so AuthGuard starts the CIAM sign-in.
      expect(msalAuth.accountsForActivePlane([homeAccount])).toEqual([]);
    });
  });
});

describe('Teams NAA step is skipped on a bridge-less host; a cancelled sign-in opens no second popup', () => {
  it('skips NAA entirely (no nestable instance, no 10 s ssoSilent) when the host does not recommend the NAA channel', async () => {
    const { msalAuth } = await loadAuth();
    h.naaRecommended.mockReturnValue(false);
    h.nestable = async config => makeFakeInstance('naa', { sso: async () => token('naa') }, config);
    h.pcaBehaviour = { popup: async () => token('popup') };

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-popup');

    expect(h.nestableConfigs).toHaveLength(0);
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenPopup']);
    expect(h.getAuthToken).not.toHaveBeenCalled();
  });

  it('still tries NAA when the host cannot answer isNAAChannelRecommended (it throws)', async () => {
    const { msalAuth } = await loadAuth();
    h.naaRecommended.mockImplementation(() => {
      throw new Error('not initialized');
    });
    h.nestable = async config => makeFakeInstance('naa', { sso: async () => token('naa') }, config);
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-naa');
  });

  it('opens no popup fallback when NAA fails with user_cancelled; the error says which path failed', async () => {
    const { msalAuth } = await loadAuth();
    const { BrowserAuthError, BrowserAuthErrorCodes } = await import('@azure/msal-browser');
    h.nestable = async config =>
      makeFakeInstance(
        'naa',
        {
          sso: async () => {
            throw new BrowserAuthError(BrowserAuthErrorCodes.userCancelled);
          },
        },
        config
      );
    h.pcaBehaviour = { popup: async () => token('popup') };

    const error = await msalAuth.acquireTeamsWorkforceBffToken(teamsCfg).then(
      () => null,
      (e: unknown) => e
    );

    expect(error).toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    expect(
      (error as { cause: { naaError: { errorCode: string }; popupError: unknown } }).cause.naaError.errorCode
    ).toBe('user_cancelled');
    expect((error as { cause: { popupError: unknown } }).cause.popupError).toBeNull();
    // No window was opened. (The standard popup instance is built ahead of time so the "Sign in" click does not
    // wait on initialize(), but nothing is asked of it.)
    expect(h.requests.map(r => r.call)).toEqual(['ssoSilent']);
    const prewarmed = h.pcas.filter(p => p.config.auth.authority === TEAMS_AUTHORITY);
    expect(prewarmed).toHaveLength(1);
    expect(prewarmed[0].instance.acquireTokenPopup).not.toHaveBeenCalled();
  });

  it('opens no popup fallback when NAA fails with user_canceled (ClientAuthError, the shape NAA emits for a dismissed Teams prompt)', async () => {
    const { msalAuth } = await loadAuth();
    const { ClientAuthError, ClientAuthErrorCodes } = await import('@azure/msal-browser');
    // NestedAppAuthAdapter.fromBridgeError maps BridgeStatusCode.UserCancel to exactly this error.
    expect(ClientAuthErrorCodes.userCanceled).toBe('user_canceled');
    h.nestable = async config =>
      makeFakeInstance(
        'naa',
        {
          sso: async () => {
            throw new ClientAuthError(ClientAuthErrorCodes.userCanceled);
          },
        },
        config
      );
    h.pcaBehaviour = { popup: async () => token('popup') };

    const error = await msalAuth.acquireTeamsWorkforceBffToken(teamsCfg).then(
      () => null,
      (e: unknown) => e
    );

    expect(error).toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
    expect(
      (error as { cause: { naaError: { errorCode: string }; popupError: unknown } }).cause.naaError.errorCode
    ).toBe('user_canceled');
    expect((error as { cause: { popupError: unknown } }).cause.popupError).toBeNull();
    expect(h.requests.map(r => r.call)).toEqual(['ssoSilent']);
    expect(h.requests.some(r => r.call === 'acquireTokenPopup')).toBe(false);
  });

  it('a non-cancel NAA failure still falls back to the popup (regression guard)', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: failing('NAA bridge error') }, config);
    h.pcaBehaviour = { popup: async () => token('popup') };
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-popup');
  });
});

describe('Teams error screen: a "Sign in" button runs the popup path on click (user gesture)', () => {
  const renderScreen = async (onSignIn: () => Promise<void>) => {
    const React = await import('react');
    const { render } = await import('@testing-library/react');
    const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
    const { TeamsAuthError } = await import('../src/components/auth/TeamsAuthError');
    return render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(TeamsAuthError, {
          message: 'Teams workforce token acquisition failed',
          diagnostics: 'top: x',
          onSignIn,
        })
      )
    );
  };

  it('does nothing until clicked, then calls onSignIn exactly once, synchronously inside the click', async () => {
    const { screen, fireEvent } = await import('@testing-library/react');
    const onSignIn = vi.fn(() => new Promise<void>(() => undefined)); // pending: the popup is open
    await renderScreen(onSignIn);

    expect(onSignIn).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(onSignIn).toHaveBeenCalledTimes(1); // same tick as the click: the popup keeps the user gesture
  });

  it('shows the new failure and keeps the button when the popup fails again', async () => {
    const { screen, fireEvent, waitFor } = await import('@testing-library/react');
    await renderScreen(async () => {
      throw new Error('popup_window_error: blocked');
    });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(screen.getByText(/popup_window_error: blocked/, { selector: 'p, span' })).toBeTruthy());
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeTruthy();
  });

  it(
    'clicking drives the real popup path: the adapter opens acquireTokenPopup on the standard instance, then serves tokens',
    { timeout: 60000 },
    async () => {
      stubWorkforceEnv();
      vi.resetModules();
      h.nestable = async config => makeFakeInstance('naa', { sso: failing('NAA bridge error') }, config);
      // The bootstrap popup is blocked (no user gesture); the click-driven one succeeds.
      let popupAttempts = 0;
      h.pcaBehaviour = {
        popup: async () => {
          popupAttempts += 1;
          if (popupAttempts === 1) throw new Error('popup_window_error');
          return token('popup-click');
        },
        silent: async () => token('popup-silent'),
      };
      const { createTeamsHostAdapter } = await import('../src/host/TeamsHostAdapter');
      const msalAuth = await import('../src/auth/msal-auth');
      const adapter = createTeamsHostAdapter(TEAMS_TID);
      await expect(adapter.initialize()).rejects.toBeInstanceOf(msalAuth.TeamsWorkforceAuthError);
      expect(popupAttempts).toBe(1);
      expect(() => adapter.getMsalInstance()).toThrow();

      const React = await import('react');
      const { render, screen, fireEvent, waitFor } = await import('@testing-library/react');
      const { FluentProvider, webLightTheme } = await import('@fluentui/react-components');
      const { TeamsAuthError } = await import('../src/components/auth/TeamsAuthError');
      const signedIn = vi.fn();
      render(
        React.createElement(
          FluentProvider,
          { theme: webLightTheme },
          React.createElement(TeamsAuthError, {
            message: 'failed',
            diagnostics: 'top: x',
            onSignIn: async () => {
              await adapter.signInWithPopup();
              signedIn();
            },
          })
        )
      );

      expect(popupAttempts).toBe(1); // nothing happens without a click
      fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
      await waitFor(() => expect(signedIn).toHaveBeenCalledTimes(1));

      expect(popupAttempts).toBe(2);
      const popupPca = h.pcas.find(p => p.config.auth.authority === TEAMS_AUTHORITY);
      expect(adapter.getMsalInstance()).toBe(popupPca!.instance);
      expect(adapter.isInitialized()).toBe(true);
      // The recovery path wires the Teams tab as the home plane exactly like initialize() does (review F3): the registration
      // gate flag, the signed-in-tenant account filter and the Teams guest-link host. The Teams tid is NOT Spaarke's tenant.
      expect(TEAMS_TID).not.toBe(SPAARKE_TENANT);
      expect(msalAuth.isWorkforceHomePlaneActive()).toBe(true);
      const own = { homeAccountId: 'own', tenantId: TEAMS_TID };
      const spaarkeGuest = { homeAccountId: 'spaarke', tenantId: SPAARKE_TENANT };
      const noTenant = { homeAccountId: 'none' };
      expect(msalAuth.accountsForActivePlane([own, spaarkeGuest, noTenant])).toEqual([own]);
      // The guest-link host is 'teams': the silent guest token goes through a NAA (nestable) instance of the A8 client; the
      // browser host would build none.
      const nestableBefore = h.nestableConfigs.length;
      await msalAuth.acquireSpaarkeGuestToken('silent', 'jane@contoso.test', BFF_SCOPE);
      expect(h.nestableConfigs.slice(nestableBefore).map(c => c.auth.clientId)).toEqual([CLIENT_ID]);
      // The active BFF acquirer is the Teams strategy now (popup path: silent from the signed-in account).
      await expect(msalAuth.acquireActiveBffToken()).resolves.toBe('token-popup-silent');
      expect(h.getAuthToken).not.toHaveBeenCalled();
    }
  );

  it('the click path never runs acquireTokenSilent before acquireTokenPopup, even with a cached account (user gesture)', async () => {
    const { msalAuth } = await loadAuth();
    h.pcaBehaviour = { popup: async () => token('popup'), silent: async () => token('silent') };
    // First click signs in (no account yet); the second click has a cached account, where the old ladder
    // would have gone through acquireTokenSilent (up to ~10 s in an iframe) before opening the popup.
    await expect(msalAuth.signInTeamsWorkforceViaPopup(teamsCfg)).resolves.toBe('token-popup');
    await expect(msalAuth.signInTeamsWorkforceViaPopup(teamsCfg)).resolves.toBe('token-popup');
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenPopup', 'acquireTokenPopup']);
    expect(h.requests[1].request).toMatchObject({ loginHint: 'popup@contoso.test' });
  });

  it('the bootstrap (non-click) popup path still tries the cached account silently first', async () => {
    const { msalAuth } = await loadAuth();
    h.naaRecommended.mockReturnValue(false);
    h.pcaBehaviour = { popup: async () => token('popup'), silent: async () => token('silent') };
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-popup');
    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-silent');
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenPopup', 'acquireTokenSilent']);
  });

  it('signInTeamsWorkforceViaPopup uses the standard instance only: no NAA instance is built', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: async () => token('naa') }, config);
    h.pcaBehaviour = { popup: async () => token('popup') };
    await expect(msalAuth.signInTeamsWorkforceViaPopup(teamsCfg)).resolves.toBe('token-popup');
    expect(h.nestableConfigs).toHaveLength(0);
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenPopup']);
  });
});

describe('msal-browser assumptions the silent-renewal design relies on (re-verify the design on an msal upgrade)', () => {
  // acquireSilent() (msal-auth.ts) sends the first silent call with AccessTokenAndRefreshToken so msal NEVER opens an
  // iframe for it, and only then retries once with Skip + the bridge redirect URI. That split holds only while
  // msal-browser's iframe renewal policy list contains Skip and not AccessTokenAndRefreshToken. The list is not
  // exported, so this reads it from the installed dist, like the auth-callback bridge parity test above.
  it("Skip is in msal-browser's iframe renewal policies; AccessTokenAndRefreshToken is not", () => {
    const source = fs
      .readFileSync(path.join(SPA_ROOT, 'node_modules/@azure/msal-browser/dist/utils/BrowserConstants.mjs'), 'utf8')
      .replace(/\r\n/g, '\n');
    const list = /const iFrameRenewalPolicies = \[([\s\S]*?)\];/.exec(source);
    const hint =
      'msal-browser changed its iframe renewal policies: re-verify acquireSilent() / the workforce silent-renewal design in msal-auth.ts before upgrading.';
    expect(list, hint).not.toBeNull();
    const policies = list![1]
      .split(',')
      .map(p => p.trim())
      .filter(Boolean);
    expect(policies, hint).toContain('CacheLookupPolicy.Skip');
    expect(policies, hint).not.toContain('CacheLookupPolicy.AccessTokenAndRefreshToken');
  });
});

describe('Teams error screen diagnostics read the { naaError, popupError } cause', () => {
  it('shows both the NAA and the popup failure', async () => {
    const { extractAuthDiagnostics } = await import('../src/auth/auth-diagnostics');
    const text = extractAuthDiagnostics({
      name: 'TeamsWorkforceAuthError',
      message: 'Teams workforce token acquisition failed',
      cause: {
        naaError: { name: 'BrowserAuthError', errorCode: 'nested_app_auth_unavailable', message: 'no bridge' },
        popupError: {
          name: 'BrowserAuthError',
          errorCode: 'popup_window_error',
          message: 'AADSTS50011 reply url mismatch',
        },
      },
    });
    expect(text).toContain('NAA: name=BrowserAuthError');
    expect(text).toContain('nested_app_auth_unavailable');
    expect(text).toContain('Popup: name=BrowserAuthError');
    expect(text).toContain('popup_window_error');
    expect(text).toContain('AADSTS=AADSTS50011');
    expect(text).not.toContain('SSO');
  });

  it('shows (none) for a path that did not run', async () => {
    const { extractAuthDiagnostics } = await import('../src/auth/auth-diagnostics');
    const text = extractAuthDiagnostics({ message: 'x', cause: { naaError: null, popupError: new Error('blocked') } });
    expect(text).toContain('NAA: (none)');
    expect(text).toContain('Popup: name=Error');
  });
});

describe('CI workflows and static config agree with A8', () => {
  const REPO_ROOT = path.resolve(SPA_ROOT, '../../..');
  const read = (rel: string) => fs.readFileSync(path.join(REPO_ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

  it('deploy-teams-app.yml asserts the manifest has NO webApplicationInfo', () => {
    const yml = read('.github/workflows/deploy-teams-app.yml');
    expect(yml).toContain(`jq -e 'has("webApplicationInfo") | not' manifest.json`);
    // Task 043: the check runs on the STAGED manifest (dev: manifest.json; prod: the rendered manifest.prod.json).
    expect(yml).toContain('working-directory: src/client/external-spa/appPackage/build/stage');
    expect(yml).not.toMatch(/\.webApplicationInfo\.(id|resource)\s*!=\s*null/);
  });

  it('deploy-external-spa.yml injects the tenant id and the user_impersonation scope from environment variables, not the retired scope', () => {
    const yml = read('.github/workflows/deploy-external-spa.yml');
    // Task 043: values are GitHub environment variables (no literal); the preflight step enforces their format.
    expect(yml).toContain('VITE_TEAMS_MSAL_TENANT_ID: ${{ vars.VITE_TEAMS_MSAL_TENANT_ID }}');
    expect(yml).toContain('VITE_TEAMS_MSAL_BFF_SCOPE: ${{ vars.VITE_TEAMS_MSAL_BFF_SCOPE }}');
    expect(yml).toContain("scope='^api://[0-9a-fA-F-]{36}/user_impersonation$'");
    expect(yml).not.toContain(RETIRED_SCOPE);
  });

  it('VITE_TEAMS_MSAL_TENANT_ID is declared in vite-env.d.ts, .env.production and .env.example', () => {
    expect(fs.readFileSync(path.join(SPA_ROOT, 'src/vite-env.d.ts'), 'utf8')).toMatch(
      /VITE_TEAMS_MSAL_TENANT_ID\?: string/
    );
    expect(fs.readFileSync(path.join(SPA_ROOT, '.env.production'), 'utf8')).toMatch(
      /^VITE_TEAMS_MSAL_TENANT_ID=#\{TEAMS_MSAL_TENANT_ID\}#/m
    );
    expect(fs.readFileSync(path.join(SPA_ROOT, '.env.example'), 'utf8')).toMatch(/VITE_TEAMS_MSAL_TENANT_ID/);
  });

  it('the Teams manifest version is bumped past 1.1.0 (Teams rejects an update with an unchanged version)', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(SPA_ROOT, 'appPackage/manifest.json'), 'utf8'));
    const parts = (v: string) => v.split('.').map(Number);
    const [maj, min, pat] = parts(manifest.version);
    const [bmaj, bmin, bpat] = parts('1.1.0');
    const newer = maj > bmaj || (maj === bmaj && (min > bmin || (min === bmin && pat > bpat)));
    expect(newer).toBe(true);
  });

  it('no stale text: the workflow knows public/ exists, the guide has no Teams-SSO claim, the error screen has no consent claim', () => {
    const wf = read('.github/workflows/deploy-external-spa.yml');
    expect(wf).not.toContain('There is no public/ dir');
    expect(wf).toContain('auth-callback.html');
    const guide = read('docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md');
    expect(guide).not.toContain('Teams SSO included');
    const screens = [
      fs.readFileSync(path.join(SPA_ROOT, 'src/main.tsx'), 'utf8'),
      fs.readFileSync(path.join(SPA_ROOT, 'src/components/auth/TeamsAuthError.tsx'), 'utf8'),
    ].join('\n');
    expect(screens).not.toMatch(/may not have consented/);
    expect(screens).toMatch(/administrator/);
  });

  it('the SWA Referrer-Policy is a valid policy value', () => {
    const swa = JSON.parse(fs.readFileSync(path.join(SPA_ROOT, 'staticwebapp.config.json'), 'utf8'));
    const valid = [
      'no-referrer',
      'no-referrer-when-downgrade',
      'origin',
      'origin-when-cross-origin',
      'same-origin',
      'strict-origin',
      'strict-origin-when-cross-origin',
      'unsafe-url',
    ];
    expect(valid).toContain(swa.globalHeaders['Referrer-Policy']);
  });
});
