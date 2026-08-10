/**
 * Tests for SpaarkeAuthProvider proactive-refresh gating (2026-08-10 fix).
 *
 * A background refresh timer must only REFRESH an existing session, never
 * INITIATE one: a cold acquire from the timer would fall through the strategy's
 * silent chain to an interactive `acquireTokenPopup` fired out of any user
 * gesture (ADR-028 INV-5) and, with concurrent callers, collide as MSAL
 * `interaction_in_progress`. The guard is `if (!this.isAuthenticated()) return`.
 */

import type { IAuthConfig, TokenResult } from '../src/types';
import type { AuthStrategy } from '../src/strategies/AuthStrategy';
import { SpaarkeAuthProvider } from '../src/SpaarkeAuthProvider';
import { PROACTIVE_REFRESH_INTERVAL_MS } from '../src/config';

function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds, tid: 'tenant-guid' })).toString('base64');
  return `${header}.${payload}.sig`;
}
const freshJwt = (): string => makeJwt(Math.floor(Date.now() / 1000) + 60 * 60); // exp 1h out

/** Stub strategy whose token is switchable to simulate cold vs. warm session. */
class StubStrategy implements AuthStrategy {
  readonly name = 'stub';
  public token = ''; // '' = cold (no session); a JWT = warm
  acquire = jest.fn(
    async (): Promise<TokenResult> =>
      this.token ? { accessToken: this.token, expiresOn: 0 } : { accessToken: '', expiresOn: 0 }
  );
  clearCache = jest.fn();
  logout = jest.fn(async () => {});
}

const config: IAuthConfig = {
  clientId: 'test-client',
  authority: 'https://login.microsoftonline.com/tenant-guid',
  bffApiScope: 'api://bff/user_impersonation',
  bffBaseUrl: 'http://localhost/api',
  proactiveRefresh: true,
};

describe('SpaarkeAuthProvider — proactive refresh gating', () => {
  let strategy: StubStrategy;
  let provider: SpaarkeAuthProvider;
  let info: typeof console.info;
  let warn: typeof console.warn;
  let error: typeof console.error;

  beforeEach(() => {
    jest.useFakeTimers();
    info = console.info;
    warn = console.warn;
    error = console.error;
    console.info = jest.fn();
    console.warn = jest.fn();
    console.error = jest.fn();
    strategy = new StubStrategy();
    provider = new SpaarkeAuthProvider(config, strategy);
  });

  afterEach(() => {
    provider.dispose();
    jest.useRealTimers();
    console.info = info;
    console.warn = warn;
    console.error = error;
  });

  it('does NOT acquire on the timer when no session exists (never fires a cold background popup)', async () => {
    // No token acquired yet → isAuthenticated() is false.
    strategy.acquire.mockClear();
    await jest.advanceTimersByTimeAsync(PROACTIVE_REFRESH_INTERVAL_MS + 10);
    expect(strategy.acquire).not.toHaveBeenCalled();
  });

  it('DOES refresh on the timer once a session exists', async () => {
    strategy.token = freshJwt();
    await provider.getAccessToken(); // populate cache → isAuthenticated() true
    expect(provider.isAuthenticated()).toBe(true);
    strategy.acquire.mockClear();
    await jest.advanceTimersByTimeAsync(PROACTIVE_REFRESH_INTERVAL_MS + 10);
    expect(strategy.acquire).toHaveBeenCalledTimes(1);
  });
});
