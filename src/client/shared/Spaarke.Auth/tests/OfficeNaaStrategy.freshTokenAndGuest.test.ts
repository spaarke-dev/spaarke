/**
 * GitHub #1571 review (spaarkeai-word-add-in-r1 task 136, fix round 1): OfficeNaaStrategy
 *   - asks Entra for a NEW token on the silent step of a Sign in click and after `requestFreshToken()` (a 401 retry),
 *     so the user is never handed MSAL's cached copy of the token the server refused (F1);
 *   - hands the token the Sign in click returned to the next `acquire()`, with no second request (F2);
 *   - matches a cached account on its ID token's `tid` first, so a B2B guest under NAA (whose `tenantId` is the home
 *     tenant) is chosen, while a foreign account stays refused (K4).
 *
 * Same mocks as OfficeNaaStrategy.silentAndAccount.test.ts.
 */

import type { Configuration } from '@azure/msal-browser';
import type { IAuthConfig } from '../src/types';

const T1 = '11111111-1111-1111-1111-111111111111';
const T2 = '22222222-2222-2222-2222-222222222222';

function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds })).toString('base64');
  return `${header}.${payload}.sig`;
}

function freshJwt(): string {
  return makeJwt(Math.floor(Date.now() / 1000) + 3600);
}

function account(tenantId: string, username: string, home = `${username}-home`) {
  return { homeAccountId: home, tenantId, username, localAccountId: `${username}-local`, environment: 'login' };
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
let pcaInstance = makeInstance();
let bridgeLine: 'true' | 'false' = 'true';

const mockCreateNestable = jest.fn(async (config: Configuration) => {
  config.system?.loggerOptions?.loggerCallback?.(
    2,
    `[Mon, 03 Nov 2026 14:00:00 GMT] : [] : @azure/msal-browser@3.30.0 : Info - Nested App Auth Bridge available: ${bridgeLine}`,
    false
  );
  return naaInstance;
});
const mockPca = jest.fn().mockImplementation(() => pcaInstance);

jest.mock('@azure/msal-browser', () => ({
  createNestablePublicClientApplication: (config: Configuration) => mockCreateNestable(config),
  PublicClientApplication: function (this: unknown, config: Configuration) {
    return mockPca(config);
  },
}));

import { OfficeNaaStrategy } from '../src/strategies/OfficeNaaStrategy';

const realMsal = jest.requireActual<typeof import('@azure/msal-browser')>('@azure/msal-browser');

function config(overrides: Partial<IAuthConfig> = {}): Required<IAuthConfig> {
  return {
    clientId: 'test-client-id',
    authority: `https://login.microsoftonline.com/${T2}`,
    tenantId: T2,
    redirectUri: 'http://localhost/',
    bffApiScope: 'api://bff-app-id/user_impersonation',
    bffBaseUrl: 'http://localhost/api',
    proactiveRefresh: false,
    requireXrm: false,
    requireSilentOnly: false,
    requireTenantAuthority: false,
    ...overrides,
  } as Required<IAuthConfig>;
}

const FALLBACK = 'https://addins.example.com/auth-callback.html';

/** An Office host: NAA (Outlook on the web reports OfficeOnline) or the standard popup client (Word on the web). */
function installOffice(kind: 'naa' | 'popup'): void {
  const host = kind === 'naa' ? 'Outlook' : 'Word';
  (globalThis as unknown as { Office: unknown }).Office = {
    onReady: (cb: () => void) => cb(),
    PlatformType: { PC: 'PC', Mac: 'Mac', OfficeOnline: 'OfficeOnline' },
    HostType: { Outlook: 'Outlook', Word: 'Word' },
    context: {
      host,
      diagnostics: { platform: 'OfficeOnline', version: '16.0.19328.20000', host },
      requirements: { isSetSupported: (name: string, v: string) => name === 'NestedAppAuth' && v === '1.1' },
    },
  };
}

function strategy(overrides: Partial<IAuthConfig> = {}): OfficeNaaStrategy {
  return new OfficeNaaStrategy(config(overrides), { fallbackRedirectUri: FALLBACK });
}

function popupCalls(instance: ReturnType<typeof makeInstance>): number {
  return instance.acquireTokenPopup.mock.calls.length;
}

function guest(homeTenant: string, tid: string, username: string) {
  return { ...account(homeTenant, username), idTokenClaims: { tid } };
}

describe('OfficeNaaStrategy — fresh tokens, the sign-in hand-off and guest accounts (#1571 review)', () => {
  let saved: Pick<Console, 'warn' | 'info' | 'error'>;

  beforeEach(() => {
    jest.clearAllMocks();
    bridgeLine = 'true';
    naaInstance = makeInstance();
    pcaInstance = makeInstance();
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

  describe('forced refresh', () => {
    it('the silent step of a Sign in click asks Entra for a new token', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      await strategy({ requireSilentOnly: true }).acquireInteractive();

      expect(naaInstance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(naaInstance.acquireTokenSilent.mock.calls[0][0]).toMatchObject({ forceRefresh: true });
    });

    it('after requestFreshToken() the next acquire forces a refresh, once, and stays silent', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });
      const s = strategy({ requireSilentOnly: true });

      s.requestFreshToken();
      await s.acquire();
      await s.acquire();

      expect(naaInstance.acquireTokenSilent.mock.calls.map(c => c[0].forceRefresh)).toEqual([true, false]);
      expect(popupCalls(naaInstance)).toBe(0);
    });

    it('an ordinary acquire does not force a refresh', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      await strategy({ requireSilentOnly: true }).acquire();

      expect(naaInstance.acquireTokenSilent.mock.calls[0][0]).toMatchObject({ forceRefresh: false });
    });
  });

  describe('the sign-in hand-off', () => {
    it('the next acquire answers with the token the Sign in click returned, without asking MSAL again', async () => {
      installOffice('popup');
      const jwt = freshJwt();
      pcaInstance.acquireTokenPopup.mockResolvedValue({
        accessToken: jwt,
        expiresOn: null,
        account: account(T2, 'user@spaarke'),
      });
      const s = strategy({ requireSilentOnly: true });

      const interactive = await s.acquireInteractive();
      const silentCallsAfterSignIn = pcaInstance.acquireTokenSilent.mock.calls.length;
      const next = await s.acquire();

      expect(interactive.accessToken).toBe(jwt);
      expect(next.accessToken).toBe(jwt);
      expect(pcaInstance.acquireTokenSilent.mock.calls.length).toBe(silentCallsAfterSignIn);
    });

    it('the hand-off is used once: the acquire after it goes to MSAL', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });
      const s = strategy({ requireSilentOnly: true });

      await s.acquireInteractive();
      await s.acquire();
      await s.acquire();

      expect(naaInstance.acquireTokenSilent).toHaveBeenCalledTimes(2);
    });

    it('a failed Sign in click leaves nothing to hand off', async () => {
      installOffice('popup');
      pcaInstance.acquireTokenPopup.mockRejectedValue(new realMsal.BrowserAuthError('popup_window_error'));
      const s = strategy({ requireSilentOnly: true });

      const interactive = await s.acquireInteractive();
      const next = await s.acquire();

      expect(interactive.accessToken).toBe('');
      expect(next.accessToken).toBe('');
    });
  });

  describe('guest accounts under NAA', () => {
    it('a guest whose tenantId is the home tenant but whose ID token tid is the authority tenant is chosen', async () => {
      installOffice('naa');
      const g = guest(T1, T2, 'guest@partner');
      naaInstance.getAllAccounts.mockReturnValue([g]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });
      const s = strategy({ requireSilentOnly: true });

      await s.acquire();

      expect(naaInstance.acquireTokenSilent.mock.calls[0][0].account).toBe(g);
      expect(s.getAccount()).toBe(g);
    });

    it('an account whose ID token tid is a foreign tenant is still refused, whatever its tenantId says', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([guest(T2, T1, 'other@partner')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });
      const s = strategy({ requireSilentOnly: true });

      await s.acquire();

      expect(naaInstance.acquireTokenSilent.mock.calls[0][0].account).toBeUndefined();
      expect(s.getAccount()).toBeNull();
    });
  });
});
