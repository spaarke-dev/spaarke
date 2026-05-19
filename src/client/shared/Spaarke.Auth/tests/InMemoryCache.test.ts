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
