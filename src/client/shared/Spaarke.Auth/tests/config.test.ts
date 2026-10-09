import { resolveConfig } from '../src/config';
import { AuthError } from '../src/errors';

const TENANT = 'a221a95e-1234-5678-90ab-cdef01234567';
const OTHER_TENANT = 'bc3aa7f4-1234-5678-90ab-cdef01234567';

/** Install a fake Xrm on the window (a Dataverse host) with the given organizationSettings.tenantId. */
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

describe('resolveConfig', () => {
  // Pre-v2 the package shipped hardcoded default clientId / bffApiScope as
  // dev fallbacks. v2 removed those defaults to force explicit configuration
  // via Dataverse env vars / runtime resolution (commit 9e480d75) — these
  // tests cover the post-v2 contract.

  let errorSpy: jest.SpyInstance;
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    errorSpy.mockRestore();
    warnSpy.mockRestore();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any).Xrm;
    delete window.__SPAARKE_TENANT_ID__;
    localStorage.clear();
  });

  it('throws when clientId is not configured', () => {
    expect(() => resolveConfig()).toThrow(/MSAL Client ID not configured/);
  });

  it('uses user-provided clientId; non-overridden fields take internal defaults', () => {
    const config = resolveConfig({ clientId: 'custom-id' });

    expect(config.clientId).toBe('custom-id');
    expect(config.bffApiScope).toBe(''); // No default — Dataverse env var supplies this in production
    expect(config.proactiveRefresh).toBe(false);
    expect(config.requireXrm).toBe(false);
    // Outside a Dataverse host (test env) with no tenant anywhere, the degraded
    // /organizations fallback is kept — and logged as an error (#1453).
    expect(config.requireTenantAuthority).toBe(false);
    expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
    expect(errorSpy).toHaveBeenCalledWith(expect.stringContaining('falling back to /organizations'));
  });

  it('user-config overrides take precedence', () => {
    const config = resolveConfig({
      clientId: 'custom-id',
      proactiveRefresh: true,
      requireXrm: true,
    });

    expect(config.clientId).toBe('custom-id');
    expect(config.proactiveRefresh).toBe(true);
    expect(config.requireXrm).toBe(true);
  });

  it('reads clientId from window global when no user config supplied', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = 'from-window';
    try {
      const config = resolveConfig();
      expect(config.clientId).toBe('from-window');
    } finally {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      delete (window as any).__SPAARKE_MSAL_CLIENT_ID__;
    }
  });

  it('builds tenant-specific authority from tenantId', () => {
    const config = resolveConfig({
      clientId: 'custom-id',
      tenantId: TENANT,
    });
    expect(config.authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    expect(config.tenantId).toBe(TENANT);
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it('explicit authority overrides tenantId', () => {
    const config = resolveConfig({
      clientId: 'custom-id',
      authority: 'https://login.microsoftonline.com/explicit.onmicrosoft.com',
      tenantId: OTHER_TENANT,
    });
    expect(config.authority).toBe('https://login.microsoftonline.com/explicit.onmicrosoft.com');
    expect(config.tenantId).toBe('explicit.onmicrosoft.com');
  });

  it('outside a Dataverse host, falls back to /organizations when neither authority nor tenantId provided', () => {
    const config = resolveConfig({ clientId: 'custom-id' });
    expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
    expect(config.tenantId).toBe('');
    expect(errorSpy).toHaveBeenCalled();
  });

  it('ignores whitespace-only tenantId', () => {
    const config = resolveConfig({
      clientId: 'custom-id',
      tenantId: '   ',
    });
    expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
  });

  it('defensively ignores a non-string tenantId (e.g. accidental Promise) without throwing', () => {
    const config = resolveConfig({
      clientId: 'custom-id',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      tenantId: Promise.resolve('guid') as any,
    });
    expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
    expect(config.tenantId).toBe('');
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('tenantId is not a string'), 'object');
  });

  it('prefers user config over window global', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = 'from-window';
    try {
      const config = resolveConfig({ clientId: 'from-user' });
      expect(config.clientId).toBe('from-user');
    } finally {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      delete (window as any).__SPAARKE_MSAL_CLIENT_ID__;
    }
  });

  describe('tenant validation (#1453; the 2026-05-13 /undefined regression)', () => {
    it.each(['undefined', 'null', 'common', 'organizations', 'consumers', 'ORGANIZATIONS', '', 'not a tenant'])(
      'rejects tenantId %p — it never becomes the authority',
      bad => {
        const config = resolveConfig({ clientId: 'custom-id', tenantId: bad });
        // The only possible outcome outside Dataverse is the logged fallback; the bad
        // value itself is never used as the tenant segment.
        expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
        expect(config.tenantId).toBe('');
        expect(errorSpy).toHaveBeenCalledWith(expect.stringContaining('falling back to /organizations'));
      }
    );

    it.each([
      'https://login.microsoftonline.com/undefined',
      'https://login.microsoftonline.com/common',
      'https://login.microsoftonline.com/consumers/',
      'https://login.microsoftonline.com/',
      'https://login.microsoftonline.com',
      'not a url',
      `http://login.microsoftonline.com/${TENANT}`,
      `https://login.microsoftonline.com/${TENANT}/oauth2/v2.0/authorize`,
    ])('rejects malformed or non-tenant authority %p and falls through to tenantId', authority => {
      const config = resolveConfig({ clientId: 'custom-id', authority, tenantId: TENANT });
      expect(config.authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
      expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('authority has no valid tenant'), authority);
    });

    it('accepts an authority with a trailing slash or /v2.0', () => {
      expect(resolveConfig({ clientId: 'c', authority: `https://login.microsoftonline.com/${TENANT}/` }).tenantId).toBe(
        TENANT
      );
      expect(
        resolveConfig({ clientId: 'c', authority: `https://login.microsoftonline.com/${TENANT}/v2.0` }).tenantId
      ).toBe(TENANT);
    });
  });

  describe('synchronous discovery chain', () => {
    it('uses Xrm organizationSettings.tenantId when config has none', () => {
      installXrm(TENANT);
      const config = resolveConfig({ clientId: 'custom-id' });
      expect(config.authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    });

    it('uses window.__SPAARKE_TENANT_ID__ when Xrm has no tenant', () => {
      installXrm('');
      window.__SPAARKE_TENANT_ID__ = TENANT;
      const config = resolveConfig({ clientId: 'custom-id' });
      expect(config.authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    });

    it('uses the persisted runtime config (__spaarke_rtc__) last', () => {
      localStorage.setItem(
        '__spaarke_rtc__',
        JSON.stringify({
          bffBaseUrl: 'https://bff.example.com',
          bffOAuthScope: 'api://x/user_impersonation',
          msalClientId: 'custom-id',
          tenantId: TENANT,
          _ts: Date.now(),
        })
      );
      const config = resolveConfig({ clientId: 'custom-id' });
      expect(config.authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    });

    it('rejects a garbage Xrm or window tenant and keeps looking', () => {
      installXrm('undefined');
      window.__SPAARKE_TENANT_ID__ = 'organizations';
      localStorage.setItem(
        '__spaarke_rtc__',
        JSON.stringify({
          bffBaseUrl: 'https://bff.example.com',
          bffOAuthScope: 'api://x/user_impersonation',
          msalClientId: 'custom-id',
          tenantId: TENANT,
          _ts: Date.now(),
        })
      );
      expect(resolveConfig({ clientId: 'custom-id' }).authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    });

    it('the env-var-derived tenant (window / runtime cache) wins over a different Xrm tenant', () => {
      installXrm(OTHER_TENANT);
      window.__SPAARKE_TENANT_ID__ = TENANT;
      expect(resolveConfig({ clientId: 'custom-id' }).authority).toBe(`https://login.microsoftonline.com/${TENANT}`);

      delete window.__SPAARKE_TENANT_ID__;
      localStorage.setItem(
        '__spaarke_rtc__',
        JSON.stringify({
          bffBaseUrl: 'https://bff.example.com',
          bffOAuthScope: 'api://x/user_impersonation',
          msalClientId: 'custom-id',
          tenantId: TENANT,
          _ts: Date.now(),
        })
      );
      expect(resolveConfig({ clientId: 'custom-id' }).authority).toBe(`https://login.microsoftonline.com/${TENANT}`);
    });

    it('an explicit tenantId wins over Xrm (members: unchanged)', () => {
      installXrm(OTHER_TENANT);
      expect(resolveConfig({ clientId: 'custom-id', tenantId: TENANT }).authority).toBe(
        `https://login.microsoftonline.com/${TENANT}`
      );
    });
  });

  describe('requireTenantAuthority', () => {
    it('inside a Dataverse host with no tenant, throws AuthError instead of using /organizations', () => {
      installXrm(undefined);
      let thrown: unknown;
      try {
        resolveConfig({ clientId: 'custom-id' });
      } catch (err) {
        thrown = err;
      }
      expect(thrown).toBeInstanceOf(AuthError);
      expect((thrown as AuthError).code).toBe('tenant_unresolved');
      expect(errorSpy).not.toHaveBeenCalledWith(expect.stringContaining('falling back to /organizations'));
    });

    it.each(['undefined', 'null', 'organizations', 'common', 'consumers', ''])(
      'inside a Dataverse host, tenantId %p throws rather than becoming the authority',
      bad => {
        installXrm(undefined);
        expect(() => resolveConfig({ clientId: 'custom-id', tenantId: bad })).toThrow(AuthError);
      }
    );

    it('inside a Dataverse host defaults to true', () => {
      installXrm(TENANT);
      expect(resolveConfig({ clientId: 'custom-id' }).requireTenantAuthority).toBe(true);
    });

    it('outside a Dataverse host, throws when explicitly required', () => {
      expect(() => resolveConfig({ clientId: 'custom-id', requireTenantAuthority: true })).toThrow(AuthError);
    });

    it('inside a Dataverse host, can be explicitly disabled (old fallback, logged)', () => {
      installXrm(undefined);
      const config = resolveConfig({ clientId: 'custom-id', requireTenantAuthority: false });
      expect(config.authority).toBe('https://login.microsoftonline.com/organizations');
      expect(errorSpy).toHaveBeenCalled();
    });
  });
});
