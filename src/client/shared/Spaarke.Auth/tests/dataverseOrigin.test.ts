/**
 * @jest-environment-options {"url": "https://org.crm.dynamics.com/WebResources/sprk_page.html"}
 */

/**
 * A page served from the Dataverse domain but with no reachable Xrm (e.g. a code
 * page opened full-screen) is still a Dataverse host (#1453): it must not fall
 * back to /organizations, and it can query sprk_TenantId on its own origin.
 */

import { isDataverseHost, isDataverseOrigin } from '../src/tenant';
import { resolveConfig } from '../src/config';
import { clearRuntimeConfigCache, discoverTenantId } from '../src/resolveRuntimeConfig';

const TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

describe('Dataverse origin without Xrm', () => {
  let warn: typeof console.warn;
  let info: typeof console.info;

  beforeEach(() => {
    warn = console.warn;
    info = console.info;
    console.warn = jest.fn();
    console.info = jest.fn();
    clearRuntimeConfigCache();
  });

  afterEach(() => {
    console.warn = warn;
    console.info = info;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (globalThis as any).fetch;
  });

  it('is detected from the hostname', () => {
    expect(window.location.hostname).toBe('org.crm.dynamics.com');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((window as any).Xrm).toBeUndefined();
    expect(isDataverseOrigin()).toBe(true);
    expect(isDataverseHost()).toBe(true);
  });

  it('resolveConfig fails closed instead of using /organizations', () => {
    expect(() => resolveConfig({ clientId: 'c' })).toThrow(expect.objectContaining({ code: 'tenant_unresolved' }));
  });

  it('discoverTenantId queries sprk_TenantId on window.location.origin', async () => {
    const fetchMock = jest.fn(async (url: string) => ({
      ok: true,
      status: 200,
      statusText: '',
      json: async () =>
        url.includes('/environmentvariabledefinitions')
          ? { value: [{ environmentvariabledefinitionid: 'd', schemaname: 'sprk_TenantId', defaultvalue: TENANT }] }
          : { value: [] },
    }));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (globalThis as any).fetch = fetchMock;

    expect(await discoverTenantId()).toBe(TENANT);
    expect(String(fetchMock.mock.calls[0][0])).toMatch(
      /^https:\/\/org\.crm\.dynamics\.com\/api\/data\/v9\.2\/environmentvariabledefinitions/
    );
  });
});
