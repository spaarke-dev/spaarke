/**
 * GitHub #1571 review, fix round 1 (spaarkeai-word-add-in-r1 task 136): the add-in's AuthService
 * - raises "sign-in required" when the provider has no token, so every request the pane makes reaches the same
 *   sign-in prompt (F2); listeners can unsubscribe;
 * - before a 401 retry (`clearCache()`), makes the strategy's next acquire ask Entra for a new token instead of MSAL's
 *   cached copy of the refused one (F1).
 *
 * Same doubles as AuthService.signInGesture.test.ts: the real SpaarkeAuthProvider, a recording OfficeNaaStrategy.
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
  requestFreshToken: jest.Mock;
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
    requestFreshToken = jest.fn();
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

describe('AuthService — the sign-in-required signal and the forced refresh (#1571 review)', () => {
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

  it('getAccessToken() with no token raises "sign-in required" and returns null', async () => {
    await authService.initialize(CONFIG);
    const listener = jest.fn();
    const unsubscribe = authService.onSignInRequired!(listener);

    const token = await authService.getAccessToken();

    expect(token).toBeNull();
    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });

  it('a token is not a reason to ask for sign-in', async () => {
    await authService.initialize(CONFIG);
    lastStrategy().acquire.mockResolvedValue({ accessToken: freshJwt(), expiresOn: Date.now() + 3600_000 });
    const listener = jest.fn();
    const unsubscribe = authService.onSignInRequired!(listener);

    expect(await authService.getAccessToken()).toBeTruthy();
    expect(listener).not.toHaveBeenCalled();
    unsubscribe();
  });

  it('notifySignInRequired() reaches every listener until it unsubscribes', async () => {
    const first = jest.fn();
    const second = jest.fn();
    const unsubscribeFirst = authService.onSignInRequired!(first);
    const unsubscribeSecond = authService.onSignInRequired!(second);

    authService.notifySignInRequired!();
    unsubscribeFirst();
    authService.notifySignInRequired!();

    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(2);
    unsubscribeSecond();
  });

  it('clearCache() (before a 401 retry) makes the next acquire ask for a new token', async () => {
    await authService.initialize(CONFIG);
    const strategy = lastStrategy();

    authService.clearCache();

    expect(strategy.requestFreshToken).toHaveBeenCalledTimes(1);
  });
});
