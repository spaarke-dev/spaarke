import { InMemoryCache } from '../src/strategies/InMemoryCache';
import type { AuthStrategy } from '../src/strategies/AuthStrategy';
import type { TokenResult } from '../src/types';

/** Build a synthetic JWT with the given `exp` claim (seconds since epoch). */
function makeJwt(expSeconds: number, extra: Record<string, unknown> = {}): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64');
  const payload = Buffer.from(JSON.stringify({ exp: expSeconds, ...extra })).toString('base64');
  return `${header}.${payload}.signature`;
}

function freshJwt(): string {
  return makeJwt(Math.floor(Date.now() / 1000) + 60 * 60); // exp 1h from now
}

function nearExpiryJwt(): string {
  return makeJwt(Math.floor(Date.now() / 1000) + 2 * 60); // exp 2min from now (within 5min buffer)
}

class StubStrategy implements AuthStrategy {
  readonly name = 'stub';
  acquireCalls = 0;
  clearCacheCalls = 0;
  nextResult: TokenResult = { accessToken: '', expiresOn: 0 };

  async acquire(): Promise<TokenResult> {
    this.acquireCalls++;
    return this.nextResult;
  }

  clearCache(): void {
    this.clearCacheCalls++;
  }
}

describe('InMemoryCache', () => {
  let stub: StubStrategy;
  let cache: InMemoryCache;

  beforeEach(() => {
    stub = new StubStrategy();
    cache = new InMemoryCache(stub);
  });

  it('exposes a composite name including the inner strategy', () => {
    expect(cache.name).toBe('in-memory-cache(stub)');
  });

  it('delegates to inner strategy when cache is empty', async () => {
    const token = freshJwt();
    stub.nextResult = { accessToken: token, expiresOn: Date.now() + 60 * 60 * 1000 };

    const result = await cache.acquire();

    expect(result.accessToken).toBe(token);
    expect(stub.acquireCalls).toBe(1);
  });

  it('returns cached token on subsequent acquires when fresh', async () => {
    const token = freshJwt();
    stub.nextResult = { accessToken: token, expiresOn: Date.now() + 60 * 60 * 1000 };

    await cache.acquire();
    const second = await cache.acquire();

    expect(second.accessToken).toBe(token);
    expect(stub.acquireCalls).toBe(1); // inner called only once
  });

  it('refreshes via inner when cached token is within the 5-minute expiry buffer', async () => {
    // Seed cache with a near-expiry token
    stub.nextResult = { accessToken: nearExpiryJwt(), expiresOn: Date.now() + 2 * 60 * 1000 };
    await cache.acquire();
    // Near-expiry token must not have been cached
    expect(cache.getCachedToken()).toBeNull();

    // Next acquire delegates to inner again with a fresh token
    const freshToken = freshJwt();
    stub.nextResult = { accessToken: freshToken, expiresOn: Date.now() + 60 * 60 * 1000 };
    const result = await cache.acquire();

    expect(result.accessToken).toBe(freshToken);
    expect(stub.acquireCalls).toBe(2);
  });

  it('prefers JWT exp claim over strategy-reported expiresOn', async () => {
    // expiresOn says token is fresh, but JWT exp says it is within the buffer
    const staleJwt = nearExpiryJwt();
    stub.nextResult = { accessToken: staleJwt, expiresOn: Date.now() + 60 * 60 * 1000 };

    await cache.acquire();

    expect(cache.getCachedToken()).toBeNull();
  });

  it('does not cache an empty (failed) acquisition', async () => {
    stub.nextResult = { accessToken: '', expiresOn: 0 };

    const result = await cache.acquire();

    expect(result.accessToken).toBe('');
    expect(cache.getCachedToken()).toBeNull();
  });

  it('clearCache() drops the cached entry and cascades to inner', async () => {
    stub.nextResult = { accessToken: freshJwt(), expiresOn: Date.now() + 60 * 60 * 1000 };
    await cache.acquire();
    expect(cache.getCachedToken()).not.toBeNull();

    cache.clearCache();

    expect(cache.getCachedToken()).toBeNull();
    expect(stub.clearCacheCalls).toBe(1);
  });

  it('invalidate() drops the cached entry without cascading to inner', async () => {
    stub.nextResult = { accessToken: freshJwt(), expiresOn: Date.now() + 60 * 60 * 1000 };
    await cache.acquire();
    expect(cache.getCachedToken()).not.toBeNull();

    cache.invalidate();

    expect(cache.getCachedToken()).toBeNull();
    expect(stub.clearCacheCalls).toBe(0);
  });

  it('getCachedToken() returns null when nothing is cached', () => {
    expect(cache.getCachedToken()).toBeNull();
  });

  describe('in-flight sharing (#1453)', () => {
    /**
     * Inner strategy whose acquisition completes only when released. InMemoryCache
     * starts the inner call one microtask after acquire(), so a release that arrives
     * before the call is held until it starts.
     */
    class HeldStrategy implements AuthStrategy {
      readonly name = 'held';
      calls = 0;
      clearCacheCalls = 0;
      private _release: ((r: TokenResult) => void) | null = null;
      private _early: TokenResult | null = null;
      acquire(): Promise<TokenResult> {
        this.calls++;
        const early = this._early;
        this._early = null;
        if (early) return Promise.resolve(early);
        return new Promise<TokenResult>(resolve => (this._release = resolve));
      }
      release(result: TokenResult): void {
        if (this._release) {
          const release = this._release;
          this._release = null;
          release(result);
        } else {
          this._early = result;
        }
      }
      clearCache(): void {
        this.clearCacheCalls++;
      }
      async logout(): Promise<void> {}
    }

    it('concurrent cache misses share one inner acquisition', async () => {
      const held = new HeldStrategy();
      const shared = new InMemoryCache(held);
      const token = freshJwt();

      const results = Promise.all([shared.acquire(), shared.acquire(), shared.acquire()]);
      held.release({ accessToken: token, expiresOn: Date.now() + 60 * 60 * 1000 });

      expect((await results).map(r => r.accessToken)).toEqual([token, token, token]);
      expect(held.calls).toBe(1);
      expect(shared.getCachedToken()).toBe(token);
    });

    it('a failed shared acquisition is not cached; the next call tries again', async () => {
      const held = new HeldStrategy();
      const shared = new InMemoryCache(held);

      const first = shared.acquire();
      held.release({ accessToken: '', expiresOn: 0 });
      expect((await first).accessToken).toBe('');

      void shared.acquire();
      await Promise.resolve();
      expect(held.calls).toBe(2);
    });

    it('invalidate() keeps sharing the in-flight acquisition (no second popup)', async () => {
      const held = new HeldStrategy();
      const shared = new InMemoryCache(held);
      const token = freshJwt();

      const first = shared.acquire();
      shared.invalidate();
      const second = shared.acquire();
      held.release({ accessToken: token, expiresOn: Date.now() + 60 * 60 * 1000 });

      expect((await second).accessToken).toBe(token);
      expect(await first).toBe(await second);
      expect(held.calls).toBe(1);
    });

    it('an inner strategy that throws synchronously does not leave a stuck rejected acquisition', async () => {
      const token = freshJwt();
      let throwNext = true;
      const throwing: AuthStrategy = {
        name: 'throwing',
        acquire: () => {
          if (throwNext) {
            throwNext = false;
            throw new Error('sync failure');
          }
          return Promise.resolve({ accessToken: token, expiresOn: Date.now() + 60 * 60 * 1000 });
        },
        clearCache: () => undefined,
        logout: async () => undefined,
      };
      const shared = new InMemoryCache(throwing);

      await expect(shared.acquire()).rejects.toThrow('sync failure');
      expect((await shared.acquire()).accessToken).toBe(token);
    });

    it('whenIdle() resolves only after the in-flight acquisition settles', async () => {
      const held = new HeldStrategy();
      const shared = new InMemoryCache(held);
      let idle = false;

      void shared.acquire();
      const waiting = shared.whenIdle().then(() => (idle = true));
      await Promise.resolve();
      expect(idle).toBe(false);

      held.release({ accessToken: '', expiresOn: 0 });
      await waiting;
      expect(idle).toBe(true);
    });

    it('clearCache() (logout) during an acquisition: the late result is not cached', async () => {
      const held = new HeldStrategy();
      const shared = new InMemoryCache(held);

      const pending = shared.acquire();
      shared.clearCache();
      held.release({ accessToken: freshJwt(), expiresOn: Date.now() + 60 * 60 * 1000 });
      await pending;

      expect(shared.getCachedToken()).toBeNull();
      expect(held.clearCacheCalls).toBe(1);
    });
  });

  it('getCachedToken() drops a stale entry on read', async () => {
    // Inject a fresh token, then advance time past its exp + buffer
    const expSeconds = Math.floor(Date.now() / 1000) + 60 * 60;
    stub.nextResult = { accessToken: makeJwt(expSeconds), expiresOn: expSeconds * 1000 };
    await cache.acquire();
    expect(cache.getCachedToken()).not.toBeNull();

    jest.useFakeTimers().setSystemTime((expSeconds + 1) * 1000);
    try {
      expect(cache.getCachedToken()).toBeNull();
    } finally {
      jest.useRealTimers();
    }
  });
});
