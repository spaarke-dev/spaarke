/**
 * GitHub #1571 (spaarkeai-word-add-in-r1 task 136): OfficeNaaStrategy honours `requireSilentOnly` (no interactive call,
 * the contract's no-token result), keeps the interactive step for an explicit `acquireInteractive()` (a user's click),
 * and chooses the cached account by the authority's tenant — never "the first one cached".
 *
 * Same mocks as OfficeNaaStrategy.test.ts: `@azure/msal-browser` and the `Office` global, with the bridge's availability
 * reported the way MSAL reports it (an Info log line).
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

describe('OfficeNaaStrategy — silent only, the user gesture and the account choice (#1571)', () => {
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

  describe('requireSilentOnly', () => {
    it('under NAA: a failed silent step makes no interactive call and returns the no-token result', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockRejectedValue(
        new realMsal.InteractionRequiredAuthError('interaction_required')
      );

      const token = await strategy({ requireSilentOnly: true }).acquire();

      expect(token).toEqual({ accessToken: '', expiresOn: 0 });
      expect(popupCalls(naaInstance)).toBe(0);
      expect(mockPca).not.toHaveBeenCalled();
    });

    it('on the standard popup client with nothing cached: no popup at all', async () => {
      installOffice('popup');

      const token = await strategy({ requireSilentOnly: true }).acquire();

      expect(token.accessToken).toBe('');
      expect(popupCalls(pcaInstance)).toBe(0);
      expect(pcaInstance.acquireTokenSilent).not.toHaveBeenCalled();
    });

    it('an Entra refusal on the silent step does not switch to the popup client (that needs an interactive attempt)', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockRejectedValue(
        new realMsal.ServerError('invalid_request', 'AADSTS700046: Invalid Reply Address.')
      );

      const s = strategy({ requireSilentOnly: true });
      await s.acquire();

      expect(mockPca).not.toHaveBeenCalled();
      expect(popupCalls(naaInstance)).toBe(0);
      expect(s.isNaaActive()).toBe(true);
    });

    it('a silent token is still returned', async () => {
      installOffice('naa');
      const jwt = freshJwt();
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: jwt, expiresOn: null });

      const token = await strategy({ requireSilentOnly: true }).acquire();

      expect(token.accessToken).toBe(jwt);
      expect(popupCalls(naaInstance)).toBe(0);
    });

    it('under NAA with no cached account the silent step asks the Office host (no account), so start-up still signs in', async () => {
      installOffice('naa');
      const jwt = freshJwt();
      naaInstance.acquireTokenSilent.mockResolvedValue({
        accessToken: jwt,
        expiresOn: null,
        account: account(T2, 'user@spaarke'),
      });

      const token = await strategy({ requireSilentOnly: true }).acquire();

      expect(token.accessToken).toBe(jwt);
      expect(naaInstance.acquireTokenSilent).toHaveBeenCalledWith(expect.objectContaining({ account: undefined }));
      expect(popupCalls(naaInstance)).toBe(0);
    });
  });

  describe('acquireInteractive (the Sign in click)', () => {
    it('runs the interactive step even when the strategy is silent only', async () => {
      installOffice('popup');
      const jwt = freshJwt();
      pcaInstance.acquireTokenPopup.mockResolvedValue({ accessToken: jwt, expiresOn: null, account: account(T2, 'u') });

      const token = await strategy({ requireSilentOnly: true }).acquireInteractive();

      expect(token.accessToken).toBe(jwt);
      expect(popupCalls(pcaInstance)).toBe(1);
    });

    it('keeps the shipped NAA-to-popup fallback: an Entra refusal of the broker prompt switches once, and the popup answers', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      naaInstance.acquireTokenSilent.mockRejectedValue(
        new realMsal.InteractionRequiredAuthError('interaction_required')
      );
      naaInstance.acquireTokenPopup.mockRejectedValue(
        new realMsal.ServerError('invalid_request', 'AADSTS700046: Invalid Reply Address.')
      );
      pcaInstance.getAllAccounts.mockReturnValue([account(T2, 'user@spaarke')]);
      pcaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      const s = strategy({ requireSilentOnly: true });
      const token = await s.acquireInteractive();

      expect(token.accessToken).not.toBe('');
      expect(mockPca).toHaveBeenCalledTimes(1);
      expect(s.isNaaActive()).toBe(false);
    });

    it('after an interactive sign-in, the silent acquire uses the account the prompt returned', async () => {
      installOffice('popup');
      const chosen = account(T2, 'second@spaarke', 'home-2');
      pcaInstance.acquireTokenPopup.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null, account: chosen });
      // Two accounts of the authority's tenant are cached: nothing but the page's own sign-in tells them apart.
      pcaInstance.getAllAccounts.mockReturnValue([account(T2, 'first@spaarke', 'home-1'), chosen]);
      pcaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      const s = strategy({ requireSilentOnly: true });
      await s.acquireInteractive();
      await s.acquire();

      expect(pcaInstance.acquireTokenSilent).toHaveBeenLastCalledWith(expect.objectContaining({ account: chosen }));
    });
  });

  describe('which cached account', () => {
    it('with accounts of T1 and T2 cached and a T2 authority, the T2 account is used (whatever the order)', async () => {
      installOffice('popup');
      const home = account(T1, 'guest@home');
      const spaarke = account(T2, 'guest@spaarke');
      pcaInstance.getAllAccounts.mockReturnValue([home, spaarke]);
      pcaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      const s = strategy();
      await s.acquire();

      expect(pcaInstance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(pcaInstance.acquireTokenSilent.mock.calls[0]![0].account).toBe(spaarke);
      expect(s.getAccount()).toBe(spaarke);
    });

    it('with no account of the authority tenant cached, no other account is used — not silently, not as a login hint', async () => {
      installOffice('popup');
      const home = account(T1, 'guest@home');
      pcaInstance.getAllAccounts.mockReturnValue([home]);
      pcaInstance.acquireTokenPopup.mockResolvedValue(null);

      const s = strategy();
      await s.acquire();

      expect(pcaInstance.acquireTokenSilent).not.toHaveBeenCalled();
      expect(popupCalls(pcaInstance)).toBe(1);
      const request = pcaInstance.acquireTokenPopup.mock.calls[0]![0];
      expect(request.loginHint).toBeUndefined();
      expect(request.prompt).toBe('select_account');
      expect(s.getAccount()).toBeNull();
    });

    it('under NAA with only another tenant account cached, the silent step names no account (the host answers)', async () => {
      installOffice('naa');
      naaInstance.getAllAccounts.mockReturnValue([account(T1, 'guest@home')]);
      naaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      await strategy({ requireSilentOnly: true }).acquire();

      expect(naaInstance.acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(naaInstance.acquireTokenSilent.mock.calls[0]![0].account).toBeUndefined();
    });

    it('two accounts of the authority tenant and nothing to tell them apart: neither is picked', async () => {
      installOffice('popup');
      pcaInstance.getAllAccounts.mockReturnValue([account(T2, 'a@spaarke'), account(T2, 'b@spaarke')]);

      const s = strategy({ requireSilentOnly: true });
      await s.acquire();

      expect(pcaInstance.acquireTokenSilent).not.toHaveBeenCalled();
      expect(s.getAccount()).toBeNull();
    });

    it('negative: a single account of the authority tenant signs in exactly as before (silent, that account)', async () => {
      installOffice('popup');
      const only = account(T2, 'user@spaarke');
      pcaInstance.getAllAccounts.mockReturnValue([only]);
      const jwt = freshJwt();
      pcaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: jwt, expiresOn: null });

      const s = strategy();
      const token = await s.acquire();

      expect(token.accessToken).toBe(jwt);
      expect(pcaInstance.acquireTokenSilent).toHaveBeenCalledWith(
        expect.objectContaining({ account: only, forceRefresh: false })
      );
      expect(popupCalls(pcaInstance)).toBe(0);
    });

    it('tenant ids compare case-insensitively', async () => {
      installOffice('popup');
      const upper = account(T2.toUpperCase(), 'user@spaarke');
      pcaInstance.getAllAccounts.mockReturnValue([account(T1, 'guest@home'), upper]);
      pcaInstance.acquireTokenSilent.mockResolvedValue({ accessToken: freshJwt(), expiresOn: null });

      await strategy().acquire();

      expect(pcaInstance.acquireTokenSilent.mock.calls[0]![0].account).toBe(upper);
    });

    it('sign-out with no account of the authority tenant still clears the local cache', async () => {
      installOffice('popup');
      pcaInstance.getAllAccounts.mockReturnValue([account(T1, 'guest@home')]);
      const s = strategy({ requireSilentOnly: true });
      await s.acquire();

      await s.logout();

      expect(pcaInstance.logoutPopup).not.toHaveBeenCalled();
      expect(pcaInstance.clearCache).toHaveBeenCalled();
    });
  });
});
