/**
 * GitHub #1571 (spaarkeai-word-add-in-r1 task 136, fix round 2): under NAA, msal-browser 3.30.0 drops `forceRefresh`
 * (the NAA silent path reads only `claims` and `cacheLookupPolicy`; `NestedAppAuthAdapter.toNaaTokenRequest` does not
 * map it), so the Office host answers a forced silent request with its cached copy of the token the server refused.
 * The doubles here do the same: the NAA silent call returns ONE token whatever `forceRefresh` says.
 *
 *   - After a 401 (`requestFreshToken(sentToken)`), the automatic retry (silent only, no gesture) does not use the refused
 *     token and never calls `acquireTokenPopup`: it returns no token, and the host shows its sign-in prompt.
 *   - A Sign in click then goes on to `acquireTokenPopup` (the broker's interactive call) instead of "succeeding" with
 *     the refused token.
 *   - The record is cleared by a different token, `clearCache()` and `logout()`.
 *
 * Same mocks as OfficeNaaStrategy.freshTokenAndGuest.test.ts.
 */

import type { Configuration } from '@azure/msal-browser';
import type { IAuthConfig } from '../src/types';

const T2 = '22222222-2222-2222-2222-222222222222';

let serial = 0;
/** A distinct, unexpired JWT each call. */
function freshJwt(): string {
  serial += 1;
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, n: serial })).toString(
    'base64'
  );
  return `${header}.${payload}.sig`;
}

function account(tenantId: string, username: string) {
  return {
    homeAccountId: `${username}-home`,
    tenantId,
    username,
    localAccountId: `${username}-local`,
    environment: 'login',
  };
}

function makeInstance() {
  return {
    initialize: jest.fn(() => Promise.resolve()),
    handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
    getAllAccounts: jest.fn((): unknown[] => []),
    acquireTokenSilent: jest.fn(),
    acquireTokenPopup: jest.fn(),
    clearCache: jest.fn(() => Promise.resolve()),
    logoutPopup: jest.fn(() => Promise.resolve()),
  };
}

let naaInstance = makeInstance();

const mockCreateNestable = jest.fn(async (config: Configuration) => {
  config.system?.loggerOptions?.loggerCallback?.(
    2,
    '[Mon, 03 Nov 2026 14:00:00 GMT] : [] : @azure/msal-browser@3.30.0 : Info - Nested App Auth Bridge available: true',
    false
  );
  return naaInstance;
});

jest.mock('@azure/msal-browser', () => ({
  createNestablePublicClientApplication: (config: Configuration) => mockCreateNestable(config),
  PublicClientApplication: function () {
    throw new Error('the standard client is not expected in these tests');
  },
}));

import { OfficeNaaStrategy } from '../src/strategies/OfficeNaaStrategy';

function strategy(): OfficeNaaStrategy {
  return new OfficeNaaStrategy(
    {
      clientId: 'test-client-id',
      authority: `https://login.microsoftonline.com/${T2}`,
      tenantId: T2,
      redirectUri: 'http://localhost/',
      bffApiScope: 'api://bff-app-id/user_impersonation',
      bffBaseUrl: 'http://localhost/api',
      proactiveRefresh: false,
      requireXrm: false,
      requireSilentOnly: true,
      requireTenantAuthority: false,
    } as Required<IAuthConfig>,
    { fallbackRedirectUri: 'https://addins.example.com/auth-callback.html' }
  );
}

function installNaaHost(): void {
  (globalThis as unknown as { Office: unknown }).Office = {
    onReady: (cb: () => void) => cb(),
    PlatformType: { PC: 'PC', Mac: 'Mac', OfficeOnline: 'OfficeOnline' },
    HostType: { Outlook: 'Outlook', Word: 'Word' },
    context: {
      host: 'Outlook',
      diagnostics: { platform: 'OfficeOnline', version: '16.0.19328.20000', host: 'Outlook' },
      requirements: { isSetSupported: (name: string, v: string) => name === 'NestedAppAuth' && v === '1.1' },
    },
  };
}

describe('OfficeNaaStrategy — the NAA host hands back the refused token (#1571 fix round 2)', () => {
  let saved: Pick<Console, 'warn' | 'info' | 'error'>;
  let hostToken: string;

  beforeEach(() => {
    jest.clearAllMocks();
    naaInstance = makeInstance();
    installNaaHost();
    naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
    hostToken = freshJwt();
    // The NAA host's cache: the SAME token whatever forceRefresh says (msal-browser 3.30.0 drops it under NAA).
    naaInstance.acquireTokenSilent.mockImplementation(async () => ({ accessToken: hostToken, expiresOn: null }));
    saved = { warn: console.warn, info: console.info, error: console.error };
    console.warn = jest.fn();
    console.info = jest.fn();
    console.error = jest.fn();
  });

  afterEach(() => {
    Object.assign(console, saved);
    delete (globalThis as unknown as { Office?: unknown }).Office;
    sessionStorage.clear();
  });

  it('the automatic retry after a 401 does not use the refused token and never opens a prompt', async () => {
    const s = strategy();
    const first = await s.acquire();
    expect(first.accessToken).toBe(hostToken);

    s.requestFreshToken(hostToken);
    const retry = await s.acquire();

    expect(retry.accessToken).toBe('');
    expect(naaInstance.acquireTokenPopup).not.toHaveBeenCalled();
  });

  it('Sign in after a 401 reaches acquireTokenPopup and its new token is what the next acquire answers with', async () => {
    const s = strategy();
    await s.acquire();
    s.requestFreshToken(hostToken);
    await s.acquire(); // the automatic retry: no token

    const brokered = freshJwt();
    naaInstance.acquireTokenPopup.mockResolvedValue({
      accessToken: brokered,
      expiresOn: null,
      account: account(T2, 'user@spaarke'),
    });
    const signedIn = await s.acquireInteractive();
    const next = await s.acquire();

    expect(naaInstance.acquireTokenPopup).toHaveBeenCalledTimes(1);
    expect(signedIn.accessToken).toBe(brokered);
    expect(next.accessToken).toBe(brokered);
  });

  it('a different silent token after a 401 is used, and clears the record', async () => {
    const s = strategy();
    await s.acquire();
    s.requestFreshToken(hostToken);
    const renewed = freshJwt();
    hostToken = renewed;

    expect((await s.acquire()).accessToken).toBe(renewed);
    expect(naaInstance.acquireTokenPopup).not.toHaveBeenCalled();
  });

  it('clearCache() clears the record: the host token is usable again', async () => {
    const s = strategy();
    await s.acquire();
    s.requestFreshToken(hostToken);
    s.clearCache();

    expect((await s.acquire()).accessToken).toBe(hostToken);
  });

  it('logout() clears the record: the host token is usable again', async () => {
    const s = strategy();
    await s.acquire();
    s.requestFreshToken(hostToken);
    await s.logout();

    expect((await s.acquire()).accessToken).toBe(hostToken);
  });
});

describe('OfficeNaaStrategy — the refused-token record never blocks a good token (#1571 fix round 3)', () => {
  let saved: Pick<Console, 'warn' | 'info' | 'error'>;
  let hostToken: string;

  beforeEach(() => {
    jest.clearAllMocks();
    naaInstance = makeInstance();
    installNaaHost();
    naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
    hostToken = freshJwt();
    // As under NAA: one token, whatever forceRefresh says.
    naaInstance.acquireTokenSilent.mockImplementation(async () => ({ accessToken: hostToken, expiresOn: null }));
    saved = { warn: console.warn, info: console.info, error: console.error };
    console.warn = jest.fn();
    console.info = jest.fn();
    console.error = jest.fn();
  });

  afterEach(() => {
    Object.assign(console, saved);
    delete (globalThis as unknown as { Office?: unknown }).Office;
    sessionStorage.clear();
  });

  it('a late 401 for the old token, after a different token was accepted, does not refuse the good token', async () => {
    // Two requests sent token A and both got a 401. The first retry got B; the second 401 arrives after that.
    const s = strategy();
    const a = (await s.acquire()).accessToken;
    s.requestFreshToken(a);
    const b = freshJwt();
    hostToken = b;
    expect((await s.acquire()).accessToken).toBe(b);

    s.requestFreshToken(a);

    expect((await s.acquire()).accessToken).toBe(b);
  });

  it('requestFreshToken() with no token records nothing', async () => {
    const s = strategy();
    await s.acquire();

    s.requestFreshToken();

    expect((await s.acquire()).accessToken).toBe(hostToken);
  });

  it('a Sign in click the broker answers with the refused token clears the record: later silent acquires get it', async () => {
    const s = strategy();
    const a = (await s.acquire()).accessToken;
    s.requestFreshToken(a);
    expect((await s.acquire()).accessToken).toBe(''); // the automatic retry: no token
    naaInstance.acquireTokenPopup.mockResolvedValue({
      accessToken: a,
      expiresOn: null,
      account: account(T2, 'user@spaarke'),
    });

    expect((await s.acquireInteractive()).accessToken).toBe(a);
    expect((await s.acquire()).accessToken).toBe(a); // the hand-off
    expect((await s.acquire()).accessToken).toBe(a); // a later silent acquire, e.g. the proactive refresh
  });
});
