/**
 * GitHub #1571 (spaarkeai-word-add-in-r1 task 136): the add-in's AuthService.
 *
 * - The eager acquire in `initialize()` (start-up, no click) is SILENT: the strategy is built with `requireSilentOnly`
 *   and only its silent `acquire()` runs — never `acquireInteractive()`.
 * - `signIn()` (the Sign in click) is the one interactive path.
 * - `getAccount()` reports the account the strategy signs in with, not the first cached MSAL account.
 *
 * The real `SpaarkeAuthProvider` and `resolveConfig` run; only `OfficeNaaStrategy` is a recording double (its own
 * silent-only and account behaviour is pinned in Spaarke.Auth's OfficeNaaStrategy.silentAndAccount.test.ts).
 */

const strategyInstances: FakeStrategy[] = [];

interface FakeStrategy {
  config: { requireSilentOnly?: boolean; authority: string };
  acquire: jest.Mock;
  acquireInteractive: jest.Mock;
  getAccount: jest.Mock;
  getMsalInstance: jest.Mock;
  isNaaActive: jest.Mock;
  clearCache: jest.Mock;
  logout: jest.Mock;
  name: string;
}

function freshJwt(): string {
  const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`;
}

const SPAARKE_ACCOUNT = { username: 'guest@spaarke', tenantId: '22222222-2222-2222-2222-222222222222' };
const HOME_ACCOUNT = { username: 'guest@home', tenantId: '11111111-1111-1111-1111-111111111111' };

jest.mock('@spaarke/auth', () => {
  const actual = jest.requireActual('@spaarke/auth');
  class OfficeNaaStrategy {
    readonly name = 'office-naa';
    config: unknown;
    acquire = jest.fn(async () => ({ accessToken: '', expiresOn: 0 }));
    acquireInteractive = jest.fn(async () => ({ accessToken: '', expiresOn: 0 }));
    getAccount = jest.fn(() => SPAARKE_ACCOUNT);
    // The old AuthService read the first cached MSAL account from here; a guest's home account is listed first.
    getMsalInstance = jest.fn(() => ({ getAllAccounts: () => [HOME_ACCOUNT, SPAARKE_ACCOUNT] }));
    isNaaActive = jest.fn(() => true);
    clearCache = jest.fn();
    logout = jest.fn(async () => undefined);
    constructor(config: unknown) {
      this.config = config;
      strategyInstances.push(this as unknown as FakeStrategy);
    }
  }
  return { ...actual, OfficeNaaStrategy };
});

import { authService } from '../AuthService';

const CONFIG = {
  clientId: 'addin-client-id',
  tenantId: '22222222-2222-2222-2222-222222222222',
  bffApiClientId: 'bff-client-id',
  fallbackRedirectUri: 'https://addins.example.com/auth-callback.html',
};

function lastStrategy(): FakeStrategy {
  return strategyInstances[strategyInstances.length - 1]!;
}

describe('AuthService — no interactive sign-in without a click (#1571)', () => {
  let saved: Pick<Console, 'warn' | 'info' | 'error'>;

  beforeEach(() => {
    jest.useFakeTimers();
    strategyInstances.length = 0;
    saved = { warn: console.warn, info: console.info, error: console.error };
    console.warn = jest.fn();
    console.info = jest.fn();
    console.error = jest.fn();
  });

  afterEach(() => {
    Object.assign(console, saved);
    jest.useRealTimers();
  });

  it('initialize(): the strategy is silent only, and the eager acquire is the silent one', async () => {
    await authService.initialize(CONFIG);

    const strategy = lastStrategy();
    expect(strategy.config.requireSilentOnly).toBe(true);
    expect(strategy.acquire).toHaveBeenCalledTimes(1);
    expect(strategy.acquireInteractive).not.toHaveBeenCalled();
    expect(authService.isAuthenticated()).toBe(false);
  });

  it('signIn() (the Sign in click) runs the interactive acquire, then the pane is signed in', async () => {
    await authService.initialize(CONFIG);
    const strategy = lastStrategy();
    const jwt = freshJwt();
    strategy.acquireInteractive.mockResolvedValue({ accessToken: jwt, expiresOn: Date.now() + 3600_000 });
    // The provider's follow-up acquire is silent; MSAL answers it from what the prompt cached.
    strategy.acquire.mockResolvedValue({ accessToken: jwt, expiresOn: Date.now() + 3600_000 });

    await authService.signIn();

    expect(strategy.acquireInteractive).toHaveBeenCalledTimes(1);
    expect(authService.isAuthenticated()).toBe(true);
  });

  it('signIn() that the user does not complete leaves the pane signed out without a second attempt', async () => {
    await authService.initialize(CONFIG);
    const strategy = lastStrategy();
    strategy.acquire.mockClear();

    await authService.signIn();

    expect(strategy.acquireInteractive).toHaveBeenCalledTimes(1);
    expect(strategy.acquire).not.toHaveBeenCalled();
    expect(authService.isAuthenticated()).toBe(false);
  });

  it('getAccount() is the account the strategy signs in with, not the first cached account', async () => {
    await authService.initialize(CONFIG);

    expect(authService.getAccount()).toBe(SPAARKE_ACCOUNT);
  });
});
