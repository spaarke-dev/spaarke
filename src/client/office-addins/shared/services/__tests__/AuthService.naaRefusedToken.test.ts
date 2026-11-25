/**
 * GitHub #1571 (spaarkeai-word-add-in-r1 task 136, fix round 2): the whole sign-in stack under NAA — the real
 * AuthService, SpaarkeAuthProvider, OfficeNaaStrategy and authenticatedJsonFetch, with only `@azure/msal-browser`
 * doubled. The NAA double hands back the SAME token whatever `forceRefresh` says, as msal-browser 3.30.0 does under NAA
 * (its NAA silent path drops `forceRefresh`), so nothing here assumes a forced refresh yields a new token.
 *
 *   - A 401 survives the automatic retry: the retry does not resend the refused token, the broker's interactive call
 *     (`acquireTokenPopup`) is never made without a click, and the pane gets its "sign-in required" signal.
 *   - The Sign in click then reaches `acquireTokenPopup` and the pane uses the token it returned.
 */

import path from 'path';
import type { Configuration } from '@azure/msal-browser';

const TENANT = '22222222-2222-2222-2222-222222222222';

let serial = 0;
function freshJwt(): string {
  serial += 1;
  const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, n: serial })}.sig`;
}

const ACCOUNT = {
  homeAccountId: 'user-home',
  tenantId: TENANT,
  username: 'user@spaarke',
  localAccountId: 'user-local',
  environment: 'login',
};

/** The NAA host's current token. */
const mockHost = { token: '' };

const mockNaa = {
  initialize: jest.fn(() => Promise.resolve()),
  handleRedirectPromise: jest.fn(() => Promise.resolve(null)),
  getAllAccounts: jest.fn(() => [ACCOUNT]),
  // The Office host's cache: one token, whatever forceRefresh says.
  acquireTokenSilent: jest.fn(async () => ({ accessToken: mockHost.token, expiresOn: null, account: ACCOUNT })),
  acquireTokenPopup: jest.fn(),
  clearCache: jest.fn(() => Promise.resolve()),
  logoutPopup: jest.fn(() => Promise.resolve()),
};

// The NAA double replaces msal-browser wherever it resolves from: the Spaarke.Auth source imports it from its own
// package when that has node_modules installed, else from this package. (jest.mock with a fixed name would miss the
// first.) The modules under test are required after the doubles are registered.
const msalDouble = () => ({
  createNestablePublicClientApplication: async (config: Configuration) => {
    config.system?.loggerOptions?.loggerCallback?.(
      2,
      '@azure/msal-browser@3.30.0 : Info - Nested App Auth Bridge available: true',
      false
    );
    return mockNaa;
  },
  PublicClientApplication: function () {
    throw new Error('the standard client is not expected in this test');
  },
});
const strategyDir = path.resolve(__dirname, '../../../../shared/Spaarke.Auth/src/strategies');
for (const resolved of new Set([
  require.resolve('@azure/msal-browser'),
  require.resolve('@azure/msal-browser', { paths: [strategyDir] }),
])) {
  jest.doMock(resolved, msalDouble);
}

/* eslint-disable @typescript-eslint/no-var-requires */
const { authService } = require('../AuthService') as typeof import('../AuthService');
const { authenticatedJsonFetch } = require('../authenticatedJsonFetch') as typeof import('../authenticatedJsonFetch');
/* eslint-enable @typescript-eslint/no-var-requires */

function respond(status: number): Response {
  return { ok: status >= 200 && status < 300, status } as unknown as Response;
}

function authorizationOf(call: unknown[]): string | undefined {
  return ((call[1] as RequestInit).headers as Record<string, string>).Authorization;
}

const fetchMock = jest.fn();
const originalFetch = global.fetch;

describe('AuthService under NAA — the host hands back the refused token (#1571 fix round 2)', () => {
  let saved: Pick<Console, 'warn' | 'info' | 'error'>;

  beforeEach(async () => {
    jest.clearAllMocks();
    saved = { warn: console.warn, info: console.info, error: console.error };
    console.warn = jest.fn();
    console.info = jest.fn();
    console.error = jest.fn();
    (globalThis as unknown as { Office: unknown }).Office = {
      onReady: (cb: () => void) => cb(),
      PlatformType: { OfficeOnline: 'OfficeOnline' },
      HostType: { Outlook: 'Outlook', Word: 'Word' },
      context: {
        host: 'Outlook',
        diagnostics: { platform: 'OfficeOnline', version: '16.0.19328.20000', host: 'Outlook' },
        requirements: { isSetSupported: (name: string, v: string) => name === 'NestedAppAuth' && v === '1.1' },
      },
    };
    fetchMock.mockReset();
    global.fetch = fetchMock as unknown as typeof fetch;
    mockHost.token = freshJwt();
    await authService.initialize({
      clientId: 'addin-client-id',
      tenantId: TENANT,
      bffApiClientId: 'bff-client-id',
      fallbackRedirectUri: 'https://addins.example.com/auth-callback.html',
    });
  });

  afterEach(async () => {
    await authService.signOut();
    Object.assign(console, saved);
    delete (globalThis as unknown as { Office?: unknown }).Office;
    sessionStorage.clear();
  });

  afterAll(() => {
    global.fetch = originalFetch;
  });

  it('the automatic retry does not resend the refused token, opens no prompt, and asks the pane to sign in', async () => {
    const refused = (await authService.getAccessToken())!;
    expect(refused).toBe(mockHost.token);
    const signInRequired = jest.fn();
    const unsubscribe = authService.onSignInRequired!(signInRequired);
    fetchMock.mockResolvedValue(respond(401));

    await authenticatedJsonFetch('https://bff/api/office/x', {}, refused, {
      getRetryToken: async () => (await authService.getAccessToken()) ?? '',
    });
    unsubscribe();

    // The refused token is not resent; with no token silently the retry is not sent at all (fix round 3).
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(authorizationOf(fetchMock.mock.calls[0]!)).toBe(`Bearer ${refused}`);
    expect(mockNaa.acquireTokenPopup).not.toHaveBeenCalled();
    expect(signInRequired).toHaveBeenCalled();
  });

  it('Sign in after the 401 reaches acquireTokenPopup, and the pane then uses the token it returned', async () => {
    const refused = (await authService.getAccessToken())!;
    fetchMock.mockResolvedValue(respond(401));
    await authenticatedJsonFetch('https://bff/api/office/x', {}, refused, {
      getRetryToken: async () => (await authService.getAccessToken()) ?? '',
    });

    const brokered = freshJwt();
    mockNaa.acquireTokenPopup.mockResolvedValue({ accessToken: brokered, expiresOn: null, account: ACCOUNT });
    await authService.signIn();

    expect(mockNaa.acquireTokenPopup).toHaveBeenCalledTimes(1);
    expect(await authService.getAccessToken()).toBe(brokered);
  });
});
