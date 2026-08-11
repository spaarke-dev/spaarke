/**
 * Tests for initAuth() idempotency (2026-08-10 fix).
 *
 * When one code page embeds another that also bootstraps @spaarke/auth (e.g.
 * SpaarkeAi embeds LegalWorkspaceApp), both share this single module singleton
 * and each calls initAuth(). The prior dispose-and-replace behaviour spun up a
 * SECOND MSAL PublicClientApplication against the same clientId + shared
 * localStorage → colliding interactive acquisitions (`interaction_in_progress`)
 * and a race over which authority (tenant vs. `/organizations`) won the singleton.
 *
 * A duplicate init for the SAME clientId must COALESCE to the existing provider
 * (no 2nd MSAL instance). A genuinely different clientId still replaces.
 */

import type { IAuthConfig } from '../src/types';

function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds, tid: 'tenant-guid' })).toString('base64');
  return `${header}.${payload}.sig`;
}
const freshJwt = (): string => makeJwt(Math.floor(Date.now() / 1000) + 60 * 60);

/** Count PublicClientApplication constructions to prove no 2nd MSAL instance. */
let pcaCtorCount = 0;

jest.mock('@azure/msal-browser', () => ({
  PublicClientApplication: jest.fn().mockImplementation(() => {
    pcaCtorCount++;
    return {
      initialize: jest.fn(() => Promise.resolve()),
      handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
      getAllAccounts: jest.fn(() => [{ username: 'user@tenant.onmicrosoft.com' }]),
      acquireTokenSilent: jest.fn(() =>
        Promise.resolve({ accessToken: freshJwt(), expiresOn: new Date(Date.now() + 3600_000) })
      ),
      ssoSilent: jest.fn(),
      acquireTokenPopup: jest.fn(),
      clearCache: jest.fn(() => Promise.resolve()),
      logoutPopup: jest.fn(() => Promise.resolve()),
    };
  }),
}));

const cfgA: IAuthConfig = {
  clientId: 'client-A',
  authority: 'https://login.microsoftonline.com/tenant-guid',
  bffApiScope: 'api://bff/user_impersonation',
  bffBaseUrl: 'http://localhost/api',
  proactiveRefresh: false,
};
const cfgB: IAuthConfig = { ...cfgA, clientId: 'client-B' };

describe('initAuth — idempotency by clientId', () => {
  let info: typeof console.info;
  let warn: typeof console.warn;
  let error: typeof console.error;

  beforeEach(() => {
    jest.resetModules();
    pcaCtorCount = 0;
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
  });

  it('coalesces a duplicate init for the same clientId — reuses the provider, no 2nd MSAL instance', async () => {
    const { initAuth, getAuthProvider } = await import('../src/initAuth');
    const p1 = await initAuth(cfgA);
    const p2 = await initAuth(cfgA);
    expect(p2).toBe(p1); // same provider instance
    expect(getAuthProvider()).toBe(p1);
    expect(pcaCtorCount).toBe(1); // exactly ONE PublicClientApplication constructed
  });

  it('coalesces when the second call omits clientId (defaults to existing)', async () => {
    const { initAuth } = await import('../src/initAuth');
    const p1 = await initAuth(cfgA);
    const p2 = await initAuth(); // no config → must not clobber
    expect(p2).toBe(p1);
    expect(pcaCtorCount).toBe(1);
  });

  it('replaces the provider when the clientId genuinely differs', async () => {
    const { initAuth } = await import('../src/initAuth');
    const p1 = await initAuth(cfgA);
    const p3 = await initAuth(cfgB);
    expect(p3).not.toBe(p1); // different app → new provider
    expect(pcaCtorCount).toBe(2);
  });
});
