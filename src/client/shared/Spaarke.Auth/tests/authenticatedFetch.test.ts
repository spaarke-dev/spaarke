/**
 * authenticatedFetch (#1453): never send a BFF request without a token, and
 * share one acquisition across concurrent cold-cache callers.
 *
 * Before: a failed acquisition still sent the request (no Authorization header);
 * the 401 re-ran the silent → popup chain up to three times, and N concurrent
 * cold-cache fetches each opened their own popup.
 */

import type { TokenResult } from '../src/types';
import type { AuthStrategy } from '../src/strategies/AuthStrategy';
import { SpaarkeAuthProvider } from '../src/SpaarkeAuthProvider';

function makeJwt(expSeconds: number): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds })).toString('base64');
  return `${header}.${payload}.sig`;
}
const freshJwt = (): string => makeJwt(Math.floor(Date.now() / 1000) + 60 * 60);

/** Stub strategy that resolves only when released, to hold acquisitions in flight. */
class GatedStrategy implements AuthStrategy {
  readonly name = 'gated';
  token = '';
  calls = 0;
  private _release: (() => void) | null = null;
  private _gate: Promise<void> = Promise.resolve();

  hold(): void {
    this._gate = new Promise<void>(resolve => (this._release = resolve));
  }
  release(): void {
    this._release?.();
  }

  async acquire(): Promise<TokenResult> {
    this.calls++;
    await this._gate;
    return { accessToken: this.token, expiresOn: 0 };
  }
  clearCache(): void {}
  async logout(): Promise<void> {}
}

let mockProvider: SpaarkeAuthProvider;
jest.mock('../src/initAuth', () => ({
  getAuthProvider: () => mockProvider,
}));

import { authenticatedFetch } from '../src/authenticatedFetch';
import { AuthError } from '../src/errors';

const TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

describe('authenticatedFetch', () => {
  let strategy: GatedStrategy;
  let fetchMock: jest.Mock;
  let info: typeof console.info;
  let warn: typeof console.warn;
  let error: typeof console.error;

  beforeEach(() => {
    info = console.info;
    warn = console.warn;
    error = console.error;
    console.info = jest.fn();
    console.warn = jest.fn();
    console.error = jest.fn();
    strategy = new GatedStrategy();
    mockProvider = new SpaarkeAuthProvider(
      {
        clientId: 'c',
        tenantId: TENANT,
        bffApiScope: 'api://bff/user_impersonation',
        bffBaseUrl: 'https://bff.example.com',
      },
      strategy
    );
    fetchMock = jest.fn(async () => ({ ok: true, status: 200 }));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (globalThis as any).fetch = fetchMock;
  });

  afterEach(() => {
    mockProvider.dispose();
    console.info = info;
    console.warn = warn;
    console.error = error;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (globalThis as any).fetch;
  });

  it('throws AuthError no_token and sends NOTHING when no token can be acquired', async () => {
    strategy.token = ''; // every mechanism failed (popup blocked / requireSilentOnly)

    const result = authenticatedFetch('/api/documents/1/view-url');

    await expect(result).rejects.toBeInstanceOf(AuthError);
    await expect(result).rejects.toMatchObject({ code: 'no_token' });
    expect(fetchMock).not.toHaveBeenCalled();
    expect(strategy.calls).toBe(1); // one attempt, no retry loop through the popup chain
  });

  it('every request it sends carries an Authorization header', async () => {
    strategy.token = freshJwt();

    await authenticatedFetch('/api/documents/1/view-url');

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://bff.example.com/api/documents/1/view-url');
    expect((init.headers as Headers).get('Authorization')).toBe('Bearer ' + strategy.token);
  });

  it('three concurrent cold-cache calls trigger ONE acquisition', async () => {
    strategy.token = freshJwt();
    strategy.hold();

    const calls = [authenticatedFetch('/api/a'), authenticatedFetch('/api/b'), authenticatedFetch('/api/c')];
    await Promise.resolve();
    strategy.release();
    await Promise.all(calls);

    expect(strategy.calls).toBe(1);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    for (const [, init] of fetchMock.mock.calls) {
      expect((init.headers as Headers).get('Authorization')).toBe('Bearer ' + strategy.token);
    }
  });

  it('three concurrent calls whose shared acquisition fails all throw, with one acquisition and no request', async () => {
    strategy.token = '';
    strategy.hold();

    const calls = [authenticatedFetch('/api/a'), authenticatedFetch('/api/b'), authenticatedFetch('/api/c')];
    strategy.release();
    const settled = await Promise.allSettled(calls);

    expect(settled.every(s => s.status === 'rejected')).toBe(true);
    expect(strategy.calls).toBe(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
