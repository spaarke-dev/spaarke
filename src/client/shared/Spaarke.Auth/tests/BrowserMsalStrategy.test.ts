/**
 * Tests for BrowserMsalStrategy — the MSAL.js acquisition strategy.
 *
 * Mocks `@azure/msal-browser` entirely so tests can exercise the silent →
 * ssoSilent → popup cascade, JWT-exp validation, and logout flow without
 * a real MSAL instance or network.
 */

import type { IAuthConfig } from '../src/types';

/** Build a synthetic JWT with the given `exp` claim (seconds since epoch). */
function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds })).toString('base64');
  return `${header}.${payload}.sig`;
}

function freshJwt(): string {
  return makeJwt(Math.floor(Date.now() / 1000) + 60 * 60); // exp 1h from now
}

function expiredJwt(): string {
  return makeJwt(Math.floor(Date.now() / 1000) + 2 * 60); // exp 2min from now (within 5-min buffer)
}

const mockAccount = { username: 'user@tenant.onmicrosoft.com' };

const mockInstance = {
  initialize: jest.fn(() => Promise.resolve()),
  handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
  getAllAccounts: jest.fn(() => [mockAccount]),
  acquireTokenSilent: jest.fn(),
  ssoSilent: jest.fn(),
  acquireTokenPopup: jest.fn(),
  clearCache: jest.fn(() => Promise.resolve()),
  logoutPopup: jest.fn(() => Promise.resolve()),
};

jest.mock('@azure/msal-browser', () => ({
  PublicClientApplication: jest.fn().mockImplementation(() => mockInstance),
}));

import { BrowserMsalStrategy } from '../src/strategies/BrowserMsalStrategy';

const baseConfig: Required<IAuthConfig> = {
  clientId: 'test-client-id',
  authority: 'https://login.microsoftonline.com/tenant-guid',
  redirectUri: 'http://localhost/',
  bffApiScope: 'api://bff-app-id/user_impersonation',
  bffBaseUrl: 'http://localhost/api',
  proactiveRefresh: false,
  requireXrm: false,
};

describe('BrowserMsalStrategy', () => {
  let originalWarn: typeof console.warn;
  let originalInfo: typeof console.info;
  let originalError: typeof console.error;

  beforeEach(() => {
    // Reset all mocks between tests
    Object.values(mockInstance).forEach((fn) => {
      if (typeof fn === 'function' && 'mockClear' in fn) fn.mockClear();
    });
    mockInstance.getAllAccounts.mockReturnValue([mockAccount]);
    // Suppress diagnostic logs during tests
    originalWarn = console.warn;
    originalInfo = console.info;
    originalError = console.error;
    console.warn = jest.fn();
    console.info = jest.fn();
    console.error = jest.fn();
  });

  afterEach(() => {
    console.warn = originalWarn;
    console.info = originalInfo;
    console.error = originalError;
  });

  it('acquire(): returns token from acquireTokenSilent on success (happy path)', async () => {
    const token = freshJwt();
    mockInstance.acquireTokenSilent.mockResolvedValueOnce({
      accessToken: token,
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });

    const strategy = new BrowserMsalStrategy(baseConfig);
    const result = await strategy.acquire();

    expect(result.accessToken).toBe(token);
    expect(mockInstance.acquireTokenSilent).toHaveBeenCalledWith({
      scopes: [baseConfig.bffApiScope],
      account: mockAccount,
    });
    expect(mockInstance.ssoSilent).not.toHaveBeenCalled();
    expect(mockInstance.acquireTokenPopup).not.toHaveBeenCalled();
  });

  it('acquire(): falls through to ssoSilent when acquireTokenSilent fails', async () => {
    const token = freshJwt();
    mockInstance.acquireTokenSilent.mockRejectedValueOnce(new Error('silent failed'));
    mockInstance.ssoSilent.mockResolvedValueOnce({
      accessToken: token,
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });

    const strategy = new BrowserMsalStrategy(baseConfig);
    const result = await strategy.acquire();

    expect(result.accessToken).toBe(token);
    expect(mockInstance.ssoSilent).toHaveBeenCalledWith({
      scopes: [baseConfig.bffApiScope],
      loginHint: mockAccount.username,
    });
    expect(mockInstance.acquireTokenPopup).not.toHaveBeenCalled();
  });

  it('acquire(): falls through to popup when both silent paths fail', async () => {
    const token = freshJwt();
    mockInstance.acquireTokenSilent.mockRejectedValueOnce(new Error('silent failed'));
    mockInstance.ssoSilent.mockRejectedValueOnce(new Error('sso failed'));
    mockInstance.acquireTokenPopup.mockResolvedValueOnce({
      accessToken: token,
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });

    const strategy = new BrowserMsalStrategy(baseConfig);
    const result = await strategy.acquire();

    expect(result.accessToken).toBe(token);
    expect(mockInstance.acquireTokenPopup).toHaveBeenCalled();
  });

  it('acquire(): returns empty when all three mechanisms fail', async () => {
    mockInstance.acquireTokenSilent.mockRejectedValueOnce(new Error('silent failed'));
    mockInstance.ssoSilent.mockRejectedValueOnce(new Error('sso failed'));
    mockInstance.acquireTokenPopup.mockRejectedValueOnce(new Error('popup blocked'));

    const strategy = new BrowserMsalStrategy(baseConfig);
    const result = await strategy.acquire();

    expect(result).toEqual({ accessToken: '', expiresOn: 0 });
  });

  it('acquire(): rejects a near-expiry token (JWT exp within 5-min buffer) and falls through', async () => {
    const stale = expiredJwt();
    const fresh = freshJwt();
    // Silent returns near-expiry → rejected by _validate → fall through to ssoSilent
    mockInstance.acquireTokenSilent.mockResolvedValueOnce({
      accessToken: stale,
      expiresOn: new Date(Date.now() + 60 * 60 * 1000), // MSAL thinks it's fresh but JWT says otherwise
    });
    mockInstance.ssoSilent.mockResolvedValueOnce({
      accessToken: fresh,
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });

    const strategy = new BrowserMsalStrategy(baseConfig);
    const result = await strategy.acquire();

    expect(result.accessToken).toBe(fresh);
    expect(mockInstance.ssoSilent).toHaveBeenCalled();
  });

  it('acquire(): skips acquireTokenSilent when no cached accounts exist', async () => {
    mockInstance.getAllAccounts.mockReturnValueOnce([]);
    const token = freshJwt();
    mockInstance.ssoSilent.mockResolvedValueOnce({
      accessToken: token,
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });

    const strategy = new BrowserMsalStrategy(baseConfig);
    const result = await strategy.acquire();

    expect(result.accessToken).toBe(token);
    expect(mockInstance.acquireTokenSilent).not.toHaveBeenCalled();
    expect(mockInstance.ssoSilent).toHaveBeenCalled();
  });

  it('logout(): calls MSAL.logoutPopup with the cached account', async () => {
    const strategy = new BrowserMsalStrategy(baseConfig);
    // Force MSAL initialization by triggering acquire once (any path)
    mockInstance.acquireTokenSilent.mockResolvedValueOnce({
      accessToken: freshJwt(),
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });
    await strategy.acquire();

    await strategy.logout();

    expect(mockInstance.logoutPopup).toHaveBeenCalledWith({ account: mockAccount });
  });

  it('logout(): falls back to clearCache when logoutPopup throws', async () => {
    const strategy = new BrowserMsalStrategy(baseConfig);
    mockInstance.acquireTokenSilent.mockResolvedValueOnce({
      accessToken: freshJwt(),
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });
    await strategy.acquire();

    mockInstance.logoutPopup.mockRejectedValueOnce(new Error('popup blocked'));

    await strategy.logout();

    expect(mockInstance.clearCache).toHaveBeenCalled();
  });

  it('clearCache(): is a no-op when MSAL has not been initialized', () => {
    const strategy = new BrowserMsalStrategy(baseConfig);
    // Never call acquire — instance stays null
    expect(() => strategy.clearCache()).not.toThrow();
    expect(mockInstance.clearCache).not.toHaveBeenCalled();
  });

  it('getMsalInstance(): returns null before init and the instance after', async () => {
    const strategy = new BrowserMsalStrategy(baseConfig);
    expect(strategy.getMsalInstance()).toBeNull();

    mockInstance.acquireTokenSilent.mockResolvedValueOnce({
      accessToken: freshJwt(),
      expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    });
    await strategy.acquire();

    expect(strategy.getMsalInstance()).toBe(mockInstance);
  });
});
