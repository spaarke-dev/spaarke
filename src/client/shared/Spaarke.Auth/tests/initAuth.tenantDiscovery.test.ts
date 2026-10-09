/**
 * initAuth() tenant discovery (#1453).
 *
 * A B2B guest signed in against `/organizations` lands in their HOME tenant,
 * where Spaarke's single-tenant app does not exist (AADSTS700016). initAuth must
 * therefore find the environment's tenant — config/Xrm/window/cache, then the
 * `sprk_TenantId` env var, then the BFF's `/api/config/client` — and, inside a
 * Dataverse host, fail closed rather than fall back to `/organizations`.
 */

import type { IAuthConfig } from '../src/types';

const ENV_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const BFF_TENANT = 'b221a95e-6abc-4434-aecc-e48338a1b2f2';
const XRM_TENANT = 'c221a95e-6abc-4434-aecc-e48338a1b2f2';

function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds, tid: ENV_TENANT })).toString('base64');
  return `${header}.${payload}.sig`;
}
const freshJwt = (): string => makeJwt(Math.floor(Date.now() / 1000) + 60 * 60);

/** Authorities of every PublicClientApplication constructed, in order. */
let pcaAuthorities: string[] = [];
const msalClearCache = jest.fn(() => Promise.resolve());
/** acquireTokenSilent behaviour for every PCA; tests can hold a call in flight. */
const mockSilent = jest.fn(
  (): Promise<unknown> => Promise.resolve({ accessToken: freshJwt(), expiresOn: new Date(Date.now() + 3600_000) })
);

jest.mock('@azure/msal-browser', () => ({
  PublicClientApplication: jest.fn().mockImplementation((cfg: { auth: { authority: string } }) => {
    pcaAuthorities.push(cfg.auth.authority);
    return {
      initialize: jest.fn(() => Promise.resolve()),
      handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
      getAllAccounts: jest.fn(() => [{ username: 'user@tenant.onmicrosoft.com', tenantId: ENV_TENANT }]),
      acquireTokenSilent: mockSilent,
      ssoSilent: jest.fn(),
      acquireTokenPopup: jest.fn(),
      clearCache: msalClearCache,
      logoutPopup: jest.fn(() => Promise.resolve()),
    };
  }),
}));

interface FetchPlan {
  envTenant?: string; // value of sprk_TenantId; undefined = no definition
  bffTenant?: string; // tenantId from /api/config/client; undefined = 500
  hang?: boolean; // never answer; reject only when the request's signal aborts
}

function installFetch(plan: FetchPlan): jest.Mock {
  const fetchMock = jest.fn(async (url: string, init?: { signal?: AbortSignal }) => {
    if (plan.hang) {
      return new Promise((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
      });
    }
    const json = (body: unknown, status = 200) => ({
      ok: status >= 200 && status < 300,
      status,
      statusText: '',
      json: async () => body,
    });
    if (url.includes('/environmentvariabledefinitions')) {
      return json({
        value:
          plan.envTenant === undefined
            ? []
            : [{ environmentvariabledefinitionid: 'def-1', schemaname: 'sprk_TenantId', defaultvalue: null }],
      });
    }
    if (url.includes('/environmentvariablevalues')) {
      return json({ value: [{ _environmentvariabledefinitionid_value: 'def-1', value: plan.envTenant }] });
    }
    if (url.endsWith('/api/config/client')) {
      return plan.bffTenant === undefined ? json({}, 500) : json({ tenantId: plan.bffTenant });
    }
    throw new Error(`unexpected fetch ${url}`);
  });
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = fetchMock;
  return fetchMock;
}

function installXrm(tenantId?: string): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    Utility: {
      getGlobalContext: () => ({
        getClientUrl: () => 'https://org.crm.dynamics.com',
        organizationSettings: { tenantId },
      }),
    },
  };
}

const baseCfg: IAuthConfig = {
  clientId: 'client-A',
  bffApiScope: 'api://bff/user_impersonation',
  bffBaseUrl: 'https://bff.example.com',
  proactiveRefresh: false,
};

describe('initAuth — tenant discovery (#1453)', () => {
  let info: typeof console.info;
  let warn: typeof console.warn;
  let error: typeof console.error;

  beforeEach(() => {
    jest.resetModules();
    pcaAuthorities = [];
    msalClearCache.mockClear();
    mockSilent.mockClear();
    mockSilent.mockImplementation(() =>
      Promise.resolve({ accessToken: freshJwt(), expiresOn: new Date(Date.now() + 3600_000) })
    );
    localStorage.clear();
    info = console.info;
    warn = console.warn;
    error = console.error;
    console.info = jest.fn();
    console.warn = jest.fn();
    console.error = jest.fn();
  });

  afterEach(() => {
    console.info = info;
    console.warn = warn;
    console.error = error;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any).Xrm;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (globalThis as any).fetch;
    delete window.__SPAARKE_TENANT_ID__;
    jest.useRealTimers();
  });

  it('resolves the tenant from sprk_TenantId when neither config nor Xrm has one', async () => {
    installXrm(undefined);
    const fetchMock = installFetch({ envTenant: ENV_TENANT, bffTenant: BFF_TENANT });
    const { initAuth } = await import('../src/initAuth');

    const provider = await initAuth(baseCfg);

    expect(pcaAuthorities).toEqual([`https://login.microsoftonline.com/${ENV_TENANT}`]);
    expect(provider.getConfig().authority).toBe(`https://login.microsoftonline.com/${ENV_TENANT}`);
    expect(fetchMock.mock.calls.some(([u]) => String(u).endsWith('/api/config/client'))).toBe(false);
  });

  it('falls back to /api/config/client when sprk_TenantId is missing or invalid', async () => {
    installXrm(undefined);
    installFetch({ envTenant: 'undefined', bffTenant: BFF_TENANT });
    const { initAuth } = await import('../src/initAuth');

    await initAuth(baseCfg);

    expect(pcaAuthorities).toEqual([`https://login.microsoftonline.com/${BFF_TENANT}`]);
  });

  it('caches the /api/config/client answer (the endpoint is limited to 10/min/IP)', async () => {
    installFetch({ bffTenant: BFF_TENANT }); // outside Xrm: no env-var lookup
    const { fetchBffClientTenant, clearRuntimeConfigCache } = await import('../src/resolveRuntimeConfig');

    const [a, b] = await Promise.all([
      fetchBffClientTenant('https://bff.example.com/api'),
      fetchBffClientTenant('https://bff.example.com'),
    ]);
    clearRuntimeConfigCache(); // drops the in-memory request; the persisted answer remains
    const c = await fetchBffClientTenant('https://bff.example.com');

    expect([a, b, c]).toEqual([BFF_TENANT, BFF_TENANT, BFF_TENANT]);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect(((globalThis as any).fetch as jest.Mock).mock.calls).toHaveLength(1);
  });

  it('rejects a reserved tenant from /api/config/client', async () => {
    installFetch({ bffTenant: 'organizations' });
    const { fetchBffClientTenant } = await import('../src/resolveRuntimeConfig');
    expect(await fetchBffClientTenant('https://bff.example.com')).toBe('');
  });

  it('in a Dataverse host with no resolvable tenant, throws AuthError (never /organizations)', async () => {
    installXrm(undefined);
    installFetch({ envTenant: undefined, bffTenant: undefined });
    const { initAuth, getAuthProvider } = await import('../src/initAuth');
    const { AuthError } = await import('../src/errors');

    const result = initAuth(baseCfg);
    await expect(result).rejects.toBeInstanceOf(AuthError);
    await expect(result).rejects.toMatchObject({ code: 'tenant_unresolved' });
    expect(pcaAuthorities).toEqual([]);
    expect(() => getAuthProvider()).toThrow(/not initialized/);
  });

  it('outside a Dataverse host without requireTenantAuthority, falls back as before with console.error', async () => {
    installFetch({ bffTenant: undefined });
    const { initAuth } = await import('../src/initAuth');

    await initAuth(baseCfg);

    expect(pcaAuthorities).toEqual(['https://login.microsoftonline.com/organizations']);
    expect(console.error).toHaveBeenCalledWith(expect.stringContaining('falling back to /organizations'));
  });

  it('outside a Dataverse host with requireTenantAuthority, throws', async () => {
    installFetch({ bffTenant: undefined });
    const { initAuth } = await import('../src/initAuth');
    await expect(initAuth({ ...baseCfg, requireTenantAuthority: true })).rejects.toMatchObject({
      code: 'tenant_unresolved',
    });
  });

  it('members: a config with a valid tenant does no discovery and uses that tenant', async () => {
    installXrm(XRM_TENANT);
    const fetchMock = installFetch({ envTenant: ENV_TENANT, bffTenant: BFF_TENANT });
    const { initAuth } = await import('../src/initAuth');

    await initAuth({ ...baseCfg, tenantId: ENV_TENANT });

    expect(pcaAuthorities).toEqual([`https://login.microsoftonline.com/${ENV_TENANT}`]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('createCodePageAuthInitializer without tenantId gets the discovered tenant too', async () => {
    installXrm(undefined);
    installFetch({ envTenant: ENV_TENANT });
    const { createCodePageAuthInitializer } = await import('../src/createCodePageAuthInitializer');

    const init = createCodePageAuthInitializer({
      clientId: 'client-A',
      bffBaseUrl: 'https://bff.example.com',
      bffApiScope: 'api://bff/user_impersonation',
      proactiveRefresh: false,
      logLabel: 'test',
    });
    await init.ensureAuthInitialized();

    expect(pcaAuthorities).toEqual([`https://login.microsoftonline.com/${ENV_TENANT}`]);
  });

  it('replaces a provider built on /organizations when a later init (same clientId) resolves a tenant', async () => {
    installFetch({ bffTenant: undefined });
    const { initAuth, getAuthProvider } = await import('../src/initAuth');

    const degraded = await initAuth(baseCfg); // outside Dataverse → /organizations
    const upgraded = await initAuth({ ...baseCfg, tenantId: ENV_TENANT });

    expect(upgraded).not.toBe(degraded);
    expect(getAuthProvider()).toBe(upgraded);
    expect(upgraded.getConfig().authority).toBe(`https://login.microsoftonline.com/${ENV_TENANT}`);
    expect(pcaAuthorities).toEqual([
      'https://login.microsoftonline.com/organizations',
      `https://login.microsoftonline.com/${ENV_TENANT}`,
    ]);
    // Same clientId shares the MSAL cache: replacing must not wipe it.
    expect(msalClearCache).not.toHaveBeenCalled();
  });

  it('still coalesces a duplicate init onto a tenant-specific provider', async () => {
    const { initAuth } = await import('../src/initAuth');
    const p1 = await initAuth({ ...baseCfg, tenantId: ENV_TENANT });
    const p2 = await initAuth({ ...baseCfg, tenantId: BFF_TENANT });
    expect(p2).toBe(p1);
    expect(pcaAuthorities).toHaveLength(1);
  });

  it('two concurrent inits for the same clientId build ONE provider (discovery is async)', async () => {
    installXrm(undefined);
    installFetch({ envTenant: ENV_TENANT });
    const { initAuth } = await import('../src/initAuth');

    const [p1, p2] = await Promise.all([initAuth(baseCfg), initAuth(baseCfg)]);

    expect(p2).toBe(p1);
    expect(pcaAuthorities).toHaveLength(1);
  });

  it('a failed init does not block a later one', async () => {
    installXrm(undefined);
    installFetch({ envTenant: undefined, bffTenant: undefined });
    const { initAuth } = await import('../src/initAuth');

    await expect(initAuth(baseCfg)).rejects.toMatchObject({ code: 'tenant_unresolved' });
    const provider = await initAuth({ ...baseCfg, tenantId: ENV_TENANT });

    expect(provider.getConfig().authority).toBe(`https://login.microsoftonline.com/${ENV_TENANT}`);
  });
  describe('discovery cannot stall initAuth (F3)', () => {
    it('gives up on an unanswered sprk_TenantId and /api/config/client after 8 s each', async () => {
      jest.useFakeTimers();
      installXrm(undefined);
      installFetch({ hang: true });
      const { initAuth } = await import('../src/initAuth');

      const pending = initAuth(baseCfg);
      const outcome = expect(pending).rejects.toMatchObject({ code: 'tenant_unresolved' });
      await jest.advanceTimersByTimeAsync(8_001); // env-var query aborts
      await jest.advanceTimersByTimeAsync(8_001); // /api/config/client aborts
      await outcome;
    });

    it('does not repeat a failed sprk_TenantId lookup within 60 s', async () => {
      installXrm(undefined);
      const fetchMock = installFetch({ envTenant: undefined, bffTenant: undefined });
      const { discoverTenantId } = await import('../src/resolveRuntimeConfig');
      const envCalls = () =>
        fetchMock.mock.calls.filter(([u]) => String(u).includes('/environmentvariabledefinitions')).length;

      const now = Date.now();
      const clock = jest.spyOn(Date, 'now').mockReturnValue(now);
      try {
        await discoverTenantId();
        await discoverTenantId();
        expect(envCalls()).toBe(1);

        clock.mockReturnValue(now + 61_000);
        await discoverTenantId();
        expect(envCalls()).toBe(2);
      } finally {
        clock.mockRestore();
      }
    });

    it('a duplicate init onto a tenant provider returns at once, even while another init is discovering', async () => {
      const { initAuth } = await import('../src/initAuth');
      const p1 = await initAuth({ ...baseCfg, tenantId: ENV_TENANT });

      installFetch({ hang: true });
      void initAuth({ ...baseCfg, clientId: 'client-B' }).catch(() => undefined); // queued, stuck in discovery

      const p2 = await initAuth({ ...baseCfg, tenantId: ENV_TENANT });
      expect(p2).toBe(p1);
    });
  });

  it("replacement waits for the old provider's in-flight acquisition, then forwards old holders to the new provider", async () => {
    installFetch({ bffTenant: undefined });
    const { initAuth, getAuthProvider } = await import('../src/initAuth');
    const degraded = await initAuth(baseCfg); // /organizations

    // An interactive acquisition on the old provider is still open.
    let release: (v: unknown) => void = () => undefined;
    mockSilent.mockImplementationOnce(() => new Promise(resolve => (release = resolve)));
    degraded.clearCache();
    const oldAcquire = degraded.getAccessToken();
    await Promise.resolve();

    const upgrading = initAuth({ ...baseCfg, tenantId: ENV_TENANT });
    for (let i = 0; i < 10; i++) await Promise.resolve();
    expect(pcaAuthorities).toHaveLength(1); // no second MSAL instance while the first is busy

    release({ accessToken: freshJwt(), expiresOn: new Date(Date.now() + 3600_000) });
    await oldAcquire;
    const upgraded = await upgrading;

    expect(pcaAuthorities).toEqual([
      'https://login.microsoftonline.com/organizations',
      `https://login.microsoftonline.com/${ENV_TENANT}`,
    ]);
    expect(getAuthProvider()).toBe(upgraded);
    // Code still holding the old instance now reaches the tenant provider.
    expect(degraded.getConfig().authority).toBe(`https://login.microsoftonline.com/${ENV_TENANT}`);
    expect(await degraded.getAccessToken()).toBe(await upgraded.getAccessToken());
  });

  describe('tenant precedence (one order everywhere — tenant.ts)', () => {
    it('the published env-var tenant wins over a different Xrm tenant', async () => {
      installXrm(XRM_TENANT);
      window.__SPAARKE_TENANT_ID__ = ENV_TENANT;
      const { initAuth } = await import('../src/initAuth');
      await initAuth(baseCfg);
      expect(pcaAuthorities).toEqual([`https://login.microsoftonline.com/${ENV_TENANT}`]);
    });

    it('resolveRuntimeConfig: sprk_TenantId wins over a different Xrm tenant', async () => {
      installXrm(XRM_TENANT);
      const fetchMock = installFetch({ envTenant: ENV_TENANT });
      fetchMock.mockImplementation(async (url: string) => {
        const json = (body: unknown) => ({ ok: true, status: 200, statusText: '', json: async () => body });
        if (url.includes('/environmentvariabledefinitions')) {
          return json({
            value: ['sprk_BffApiBaseUrl', 'sprk_BffApiAppId', 'sprk_MsalClientId', 'sprk_TenantId'].map((n, i) => ({
              environmentvariabledefinitionid: `def-${i}`,
              schemaname: n,
              defaultvalue: ['https://bff.example.com/api', 'bff-app', 'client-A', ENV_TENANT][i],
            })),
          });
        }
        return json({ value: [] });
      });
      const { resolveRuntimeConfig } = await import('../src/resolveRuntimeConfig');
      expect((await resolveRuntimeConfig()).tenantId).toBe(ENV_TENANT);
    });
  });
});
