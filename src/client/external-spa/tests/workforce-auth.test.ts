// Workforce client switch (spaarke-SPA-external-access-platform-r3 task 040, FR-16; ADR-028 Amendment A7).
//
// The workforce plane (browser "My organization" and the Teams tab) signs in through ONE single-tenant Spaarke
// client: authority https://login.microsoftonline.com/{Spaarke tenant id} (never /organizations or /common), scope
// api://{customer BFF app id}/user_impersonation (the A2-era scope is retired). In Teams: NAA first, then an MSAL
// popup that lands on /auth-callback.html; Teams SSO (getAuthToken) and the manifest's webApplicationInfo are gone.
// The CIAM plane is unchanged.
//
// MSAL and Teams JS are mocked: every MSAL instance the code creates is recorded with its configuration, and every
// token request it makes is recorded with its scopes, so the tests assert what the code ASKS for, not a stub's echo.
import * as fs from 'node:fs';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const CLIENT_ID = '11111111-2222-3333-4444-555555555555';
const BFF_SCOPE = 'api://99999999-8888-7777-6666-555555555555/user_impersonation';
const AUTHORITY = `https://login.microsoftonline.com/${SPAARKE_TENANT}`;
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
    ...overrides,
  } as Record<string, string>;
  for (const [key, value] of Object.entries(values)) vi.stubEnv(key, value);
}

const teamsCfg = { clientId: CLIENT_ID, tenantId: SPAARKE_TENANT, bffScope: BFF_SCOPE };
const token = (n: string) => ({
  accessToken: `token-${n}`,
  account: { homeAccountId: n, username: `${n}@spaarke.test`, tenantId: SPAARKE_TENANT },
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

describe('Workforce authority is pinned to the Spaarke tenant (A7)', () => {
  it('builds https://login.microsoftonline.com/{tenant id} with the configured client id', async () => {
    const { msalConfig } = await loadAuth();
    const descriptor = msalConfig.workforceAuthorityConfig(teamsCfg);
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
      expect(() => msalConfig.workforceAuthorityConfig({ ...teamsCfg, tenantId })).toThrow(/tenant/i);
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

describe('Browser "My organization" plane', () => {
  it('builds its MSAL instance on the Spaarke-tenant authority and signs in for the user_impersonation scope', async () => {
    stubWorkforceEnv();
    vi.resetModules();
    const plane = await import('../src/auth/standalone-plane');
    const msalAuth = await import('../src/auth/msal-auth');

    const resolved = plane.resolveStandalonePlane('workforce');
    const created = h.pcas.find(p => p.instance === resolved.instance);
    expect(created).toBeDefined();
    expect(created!.config.auth.authority).toBe(AUTHORITY);
    expect(created!.config.auth.clientId).toBe(CLIENT_ID);
    // The browser plane keeps the redirect flow: it returns to the SPA's origin, not to the popup page.
    expect(created!.config.auth.redirectUri).toBe(window.location.origin);
    expect(resolved.bffScope).toBe(BFF_SCOPE);

    plane.applyStandalonePlane(resolved);
    expect(msalAuth.getActiveLoginScope()).toBe(BFF_SCOPE);

    // A first BFF call with no account starts the sign-in redirect for exactly the user_impersonation scope.
    await expect(msalAuth.acquireActiveBffToken()).rejects.toThrow(/redirecting/);
    expectOnlyUserImpersonationRequests();
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenRedirect']);
  });

  it('refuses to build when the tenant is organizations (never signs a guest into the home tenant)', async () => {
    stubWorkforceEnv({ VITE_TEAMS_MSAL_TENANT_ID: 'organizations' });
    vi.resetModules();
    const plane = await import('../src/auth/standalone-plane');
    expect(() => plane.resolveStandalonePlane('workforce')).toThrow(/tenant id/);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Teams: NAA first, then the MSAL popup; never Teams SSO
// ---------------------------------------------------------------------------------------------------------------

describe('Teams workforce sign-in', () => {
  it('uses NAA first, on the Spaarke-tenant authority, and does not build the popup fallback when NAA succeeds', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: async () => token('naa') }, config);

    await expect(msalAuth.acquireTeamsWorkforceBffToken(teamsCfg)).resolves.toBe('token-naa');

    expect(h.nestableConfigs).toHaveLength(1);
    expect(h.nestableConfigs[0].auth.authority).toBe(AUTHORITY);
    expect(h.nestableConfigs[0].auth.clientId).toBe(CLIENT_ID);
    expect(h.nestableConfigs[0].auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    // Only the CIAM singleton was built as a standard PCA; no popup fallback instance.
    expect(h.pcas.filter(p => p.config.auth.authority === AUTHORITY)).toHaveLength(0);
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
    const popupPca = h.pcas.find(p => p.config.auth.authority === AUTHORITY);
    expect(popupPca).toBeDefined();
    expect(popupPca!.config.auth.clientId).toBe(CLIENT_ID);
    expect(popupPca!.config.auth.redirectUri).toBe(`${window.location.origin}/auth-callback.html`);
    expect(popupPca!.config.cache.cacheLocation).toBe('sessionStorage');
    expect(popupPca!.instance.initialize).toHaveBeenCalled();
    // No cached account: the popup asks the user to pick one (OfficeNaaStrategy._tryAcquireWith).
    expect(popupPca!.instance.acquireTokenPopup).toHaveBeenCalledWith({
      scopes: [BFF_SCOPE],
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

      const adapter = createTeamsHostAdapter();
      await adapter.initialize();

      const popupPca = h.pcas.find(p => p.config.auth.authority === AUTHORITY);
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
// CI workflow / config alignment with A7
// ---------------------------------------------------------------------------------------------------------------

describe('Browser silent renewal: CIAM requests are unchanged on the wire; the workforce iframe retry uses the bridge page', () => {
  const callbackUri = () => `${window.location.origin}/auth-callback.html`;
  const OTHER_TENANT = '7052feba-bfc4-43e0-b09e-65014b429131';
  const account = { homeAccountId: 'u', username: 'u@spaarke.test', tenantId: SPAARKE_TENANT };

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
    /** acquireBffToken() against the CIAM singleton, with its MSAL calls replaced by spies. */
    async function ciam(silent: (req: any) => Promise<any>) {
      const { msalAuth, msalConfig } = await loadAuth();
      const stub = browserInstance(silent);
      Object.assign(msalConfig.msalInstance, stub);
      return { msalAuth, instance: msalConfig.msalInstance as any };
    }

    it('sends exactly { scopes, account } on the silent call: no redirectUri, no cacheLookupPolicy', async () => {
      const { msalAuth, instance } = await ciam(async () => ({ accessToken: 'tok' }));
      await expect(msalAuth.acquireBffToken()).resolves.toBe('tok');
      expect(instance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenSilent.mock.calls[0][0]).toStrictEqual({
        scopes: [import.meta.env.VITE_MSAL_BFF_SCOPE],
        account,
      });
      // The CIAM account list is not tenant-filtered either.
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
        account,
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

    it('the browser CIAM plane carries no acquire options (no tenant filter, no iframe redirect URI)', async () => {
      vi.resetModules();
      const plane = await import('../src/auth/standalone-plane');
      expect(plane.resolveStandalonePlane('ciam').acquireOptions).toBeUndefined();
    });
  });

  describe('workforce browser plane', () => {
    const options = () => ({ tenantId: SPAARKE_TENANT, silentRedirectUri: callbackUri() });

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
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).toMatchObject({ scopes: [BFF_SCOPE], account });
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

    it('the browser workforce plane supplies the tenant filter and the bridge page; its acquirer uses them', async () => {
      stubWorkforceEnv();
      vi.resetModules();
      const plane = await import('../src/auth/standalone-plane');
      const resolved = plane.resolveStandalonePlane('workforce');
      expect(resolved.acquireOptions).toEqual({ tenantId: SPAARKE_TENANT, silentRedirectUri: callbackUri() });
    });

    it('only counts accounts of the Spaarke tenant: a cached CIAM account is not a workforce sign-in', async () => {
      const { msalAuth } = await loadAuth();
      const ciamAccount = { homeAccountId: 'c', username: 'c@partner.test', tenantId: OTHER_TENANT };
      const instance = browserInstance(async () => ({ accessToken: 'must-not-be-used' }), [ciamAccount]);

      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).rejects.toThrow(
        /No authenticated account/
      );

      expect(instance.getAllAccounts).toHaveBeenCalledWith({ tenantId: SPAARKE_TENANT });
      expect(instance.acquireTokenSilent).not.toHaveBeenCalled();
      expect(instance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
      expect(instance.acquireTokenRedirect.mock.calls[0][0]).toStrictEqual({ scopes: [BFF_SCOPE] });
    });

    it('picks the Spaarke-tenant account when a CIAM account is cached next to it', async () => {
      const { msalAuth } = await loadAuth();
      const ciamAccount = { homeAccountId: 'c', username: 'c@partner.test', tenantId: OTHER_TENANT };
      const instance = browserInstance(async () => ({ accessToken: 'tok' }), [ciamAccount, account]);
      await expect(msalAuth.acquireStandaloneBffToken(instance, BFF_SCOPE, options())).resolves.toBe('tok');
      expect(instance.acquireTokenSilent.mock.calls[0][0].account).toBe(account);
    });

    it('AuthGuard / portal-user account lists are scoped to the plane: workforce keeps only Spaarke-tenant accounts, CIAM keeps all', async () => {
      stubWorkforceEnv();
      vi.resetModules();
      const plane = await import('../src/auth/standalone-plane');
      const msalAuth = await import('../src/auth/msal-auth');
      const ciamAccount = { homeAccountId: 'c', tenantId: OTHER_TENANT };
      const spaarkeAccount = { homeAccountId: 's', tenantId: SPAARKE_TENANT.toUpperCase() };

      plane.applyStandalonePlane(plane.resolveStandalonePlane('workforce'));
      expect(msalAuth.accountsForActivePlane([ciamAccount, spaarkeAccount])).toEqual([spaarkeAccount]);
      expect(msalAuth.accountsForActivePlane([ciamAccount])).toEqual([]);

      plane.applyStandalonePlane(plane.resolveStandalonePlane('ciam'));
      expect(msalAuth.accountsForActivePlane([ciamAccount, spaarkeAccount])).toEqual([ciamAccount, spaarkeAccount]);
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
    // No standard popup instance was built or asked to open a window.
    expect(h.pcas.filter(p => p.config.auth.authority === AUTHORITY)).toHaveLength(0);
    expect(h.requests.map(r => r.call)).toEqual(['ssoSilent']);
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
      const adapter = createTeamsHostAdapter();
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
      const popupPca = h.pcas.find(p => p.config.auth.authority === AUTHORITY);
      expect(adapter.getMsalInstance()).toBe(popupPca!.instance);
      expect(adapter.isInitialized()).toBe(true);
      // The active BFF acquirer is the Teams strategy now (popup path: silent from the signed-in account).
      await expect(msalAuth.acquireActiveBffToken()).resolves.toBe('token-popup-silent');
      expect(h.getAuthToken).not.toHaveBeenCalled();
    }
  );

  it('signInTeamsWorkforceViaPopup uses the standard instance only: no NAA instance is built', async () => {
    const { msalAuth } = await loadAuth();
    h.nestable = async config => makeFakeInstance('naa', { sso: async () => token('naa') }, config);
    h.pcaBehaviour = { popup: async () => token('popup') };
    await expect(msalAuth.signInTeamsWorkforceViaPopup(teamsCfg)).resolves.toBe('token-popup');
    expect(h.nestableConfigs).toHaveLength(0);
    expect(h.requests.map(r => r.call)).toEqual(['acquireTokenPopup']);
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

describe('CI workflows and static config agree with A7', () => {
  const REPO_ROOT = path.resolve(SPA_ROOT, '../../..');
  const read = (rel: string) => fs.readFileSync(path.join(REPO_ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

  it('deploy-teams-app.yml asserts the manifest has NO webApplicationInfo', () => {
    const yml = read('.github/workflows/deploy-teams-app.yml');
    expect(yml).toContain(`jq -e 'has("webApplicationInfo") | not' manifest.json`);
    expect(yml).not.toMatch(/\.webApplicationInfo\.(id|resource)\s*!=\s*null/);
  });

  it('deploy-external-spa.yml injects the tenant id and the user_impersonation scope, not the retired scope', () => {
    const yml = read('.github/workflows/deploy-external-spa.yml');
    expect(yml).toMatch(new RegExp(`VITE_TEAMS_MSAL_TENANT_ID: ${SPAARKE_TENANT}`));
    expect(yml).toMatch(/VITE_TEAMS_MSAL_BFF_SCOPE: api:\/\/[0-9a-f-]{36}\/user_impersonation\s*$/m);
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
