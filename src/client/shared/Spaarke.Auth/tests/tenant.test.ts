/**
 * Tenant validation (#1453) and the synchronous tenant lookups built on it.
 *
 * The 2026-05-13 popup regression (ef57fc3f35) was the authority
 * `https://login.microsoftonline.com/undefined`; #1453 is `/organizations`
 * signing B2B guests in to their home tenant. Neither value may ever be
 * accepted as a tenant.
 */

import { isDataverseHost, isValidTenant, normalizeTenant, tenantFromAuthority } from '../src/tenant';

const TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

describe('isValidTenant / normalizeTenant', () => {
  it.each([TENANT, TENANT.toUpperCase(), 'contoso.onmicrosoft.com', 'spaarke.com'])('accepts %p', value => {
    expect(isValidTenant(value)).toBe(true);
  });

  it.each([
    '',
    '   ',
    'undefined',
    'null',
    'common',
    'organizations',
    'Organizations',
    'consumers',
    '9188040d-6c67-4c5b-b112-36a304b66dad', // consumers tenant GUID
    '00000000-0000-0000-0000-000000000000',
    'tenant-guid',
    'contoso',
    'a221a95e',
    'https://login.microsoftonline.com/' + TENANT,
    'contoso.onmicrosoft.com/v2.0',
  ])('rejects %p', value => {
    expect(isValidTenant(value)).toBe(false);
  });

  it.each([undefined, null, 42, {}, Promise.resolve(TENANT)])('rejects non-string %p', value => {
    expect(isValidTenant(value)).toBe(false);
  });

  it('trims surrounding whitespace', () => {
    expect(normalizeTenant(`  ${TENANT} `)).toBe(TENANT);
  });
});

describe('tenantFromAuthority', () => {
  it.each([
    `https://login.microsoftonline.com/${TENANT}`,
    `https://login.microsoftonline.com/${TENANT}/`,
    `https://login.microsoftonline.com/${TENANT}/v2.0`,
    `https://login.microsoftonline.us/${TENANT}`,
  ])('extracts the tenant from %p', authority => {
    expect(tenantFromAuthority(authority)).toBe(TENANT);
  });

  it.each([
    'https://login.microsoftonline.com/organizations',
    'https://login.microsoftonline.com/common/',
    'https://login.microsoftonline.com/consumers/v2.0',
    'https://login.microsoftonline.com/undefined',
    'https://login.microsoftonline.com/null',
    'https://login.microsoftonline.com/',
    'https://login.microsoftonline.com//',
    `http://login.microsoftonline.com/${TENANT}`,
    `https://login.microsoftonline.com/${TENANT}/oauth2`,
    `https://login.microsoftonline.com/${TENANT}?x=1`,
    'login.microsoftonline.com/' + TENANT,
    // B2C-style authorities (tenant + policy path) are not single-tenant workforce authorities.
    'https://contoso.b2clogin.com/tfp/contoso.onmicrosoft.com/B2C_1_signin',
    'https://contoso.b2clogin.com/contoso.onmicrosoft.com/B2C_1_signin',
    'https://login.microsoftonline.com/tfp/contoso.onmicrosoft.com/B2C_1_signin',
    '',
    undefined,
  ])('rejects %p', authority => {
    expect(tenantFromAuthority(authority)).toBeUndefined();
  });
});

describe('fetchBffClientTenant failure backoff', () => {
  beforeEach(() => {
    jest.resetModules();
    localStorage.clear();
  });

  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (globalThis as any).fetch;
  });

  it('does not retry a failed /api/config/client within 60 s, then retries', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
    const fetchMock = jest.fn(async () => ({ ok: false, status: 503, json: async () => ({}) }));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (globalThis as any).fetch = fetchMock;
    const { fetchBffClientTenant } = await import('../src/resolveRuntimeConfig');
    const now = Date.now();
    const clock = jest.spyOn(Date, 'now').mockReturnValue(now);
    try {
      expect(await fetchBffClientTenant('https://bff.example.com')).toBe('');
      clock.mockReturnValue(now + 59_000);
      expect(await fetchBffClientTenant('https://bff.example.com')).toBe('');
      expect(fetchMock).toHaveBeenCalledTimes(1);

      clock.mockReturnValue(now + 61_000);
      await fetchBffClientTenant('https://bff.example.com');
      expect(fetchMock).toHaveBeenCalledTimes(2);
    } finally {
      clock.mockRestore();
      warn.mockRestore();
    }
  });
});

describe('isDataverseHost', () => {
  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any).Xrm;
  });

  it('is false in a plain page (jsdom on localhost)', () => {
    expect(isDataverseHost()).toBe(false);
  });

  it('is true when Xrm.Utility.getGlobalContext is reachable', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = { Utility: { getGlobalContext: () => ({}) } };
    expect(isDataverseHost()).toBe(true);
  });
});

describe('resolveTenantIdSync (#1453)', () => {
  beforeEach(() => {
    jest.resetModules();
    localStorage.clear();
  });

  afterEach(() => {
    delete window.__SPAARKE_TENANT_ID__;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any).Xrm;
  });

  function mockProvider(authority: string, cachedTid = ''): void {
    jest.doMock('../src/initAuth', () => ({
      getAuthProvider: () => ({
        getConfig: () => ({ authority }),
        getCachedTenantId: () => cachedTid,
      }),
    }));
  }

  it.each([
    `https://login.microsoftonline.com/${TENANT}`,
    `https://login.microsoftonline.com/${TENANT}/`,
    `https://login.microsoftonline.com/${TENANT}/v2.0`,
  ])('reads the tenant segment of %p', async authority => {
    mockProvider(authority);
    const { resolveTenantIdSync } = await import('../src/resolveTenantIdSync');
    expect(resolveTenantIdSync()).toBe(TENANT);
  });

  it.each([
    'https://login.microsoftonline.com/consumers',
    'https://login.microsoftonline.com/organizations/',
    'https://login.microsoftonline.com/undefined',
  ])('never returns the segment of %p — falls back to the token tid', async authority => {
    mockProvider(authority, TENANT);
    const { resolveTenantIdSync } = await import('../src/resolveTenantIdSync');
    expect(resolveTenantIdSync()).toBe(TENANT);
  });

  it('falls back to window.__SPAARKE_TENANT_ID__ when the provider has nothing usable', async () => {
    mockProvider('https://login.microsoftonline.com/organizations', 'undefined');
    window.__SPAARKE_TENANT_ID__ = TENANT;
    const { resolveTenantIdSync } = await import('../src/resolveTenantIdSync');
    expect(resolveTenantIdSync()).toBe(TENANT);
  });

  it('returns empty string when nothing is usable', async () => {
    mockProvider('https://login.microsoftonline.com/organizations');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = { Utility: { getGlobalContext: () => ({ organizationSettings: { tenantId: 'null' } }) } };
    const { resolveTenantIdSync } = await import('../src/resolveTenantIdSync');
    expect(resolveTenantIdSync()).toBe('');
  });
});

describe('createRuntimeConfigStore.setRuntimeConfig publishes the tenant (#1453)', () => {
  afterEach(() => {
    delete window.__SPAARKE_TENANT_ID__;
  });

  const runtime = {
    bffBaseUrl: 'https://bff.example.com',
    bffOAuthScope: 'api://bff/user_impersonation',
    msalClientId: 'client-A',
  };

  it('sets window.__SPAARKE_TENANT_ID__ for a valid tenant', async () => {
    const { createRuntimeConfigStore } = await import('../src/createRuntimeConfigStore');
    createRuntimeConfigStore({ errorLabel: 't' }).setRuntimeConfig({ ...runtime, tenantId: TENANT });
    expect(window.__SPAARKE_TENANT_ID__).toBe(TENANT);
  });

  it('publishes the BFF URL under the name resolveConfig reads, without overriding a host value', async () => {
    const { createRuntimeConfigStore } = await import('../src/createRuntimeConfigStore');
    try {
      createRuntimeConfigStore({ errorLabel: 't' }).setRuntimeConfig({ ...runtime, tenantId: TENANT });
      expect(window.__SPAARKE_BFF_URL__).toBe(runtime.bffBaseUrl);

      window.__SPAARKE_BFF_URL__ = 'https://host-set.example.com';
      createRuntimeConfigStore({ errorLabel: 't' }).setRuntimeConfig({ ...runtime, tenantId: TENANT });
      expect(window.__SPAARKE_BFF_URL__).toBe('https://host-set.example.com');
    } finally {
      delete window.__SPAARKE_BFF_URL__;
    }
  });

  it.each(['', 'undefined', 'organizations'])('does not publish %p', async bad => {
    const { createRuntimeConfigStore } = await import('../src/createRuntimeConfigStore');
    createRuntimeConfigStore({ errorLabel: 't' }).setRuntimeConfig({ ...runtime, tenantId: bad });
    expect(window.__SPAARKE_TENANT_ID__).toBeUndefined();
  });
});
