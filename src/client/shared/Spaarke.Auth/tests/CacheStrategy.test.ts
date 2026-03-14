import { CacheStrategy } from '../src/strategies/CacheStrategy';

describe('CacheStrategy', () => {
  it('returns null when cache is empty', async () => {
    const strategy = new CacheStrategy();
    const result = await strategy.tryAcquireToken();
    expect(result).toBeNull();
  });

  it('returns cached token when valid', async () => {
    const strategy = new CacheStrategy();
    const futureExpiry = Date.now() + 60 * 60 * 1000; // 1 hour from now
    strategy.store('cached-token', futureExpiry);

    const result = await strategy.tryAcquireToken();
    expect(result).not.toBeNull();
    expect(result!.accessToken).toBe('cached-token');
    expect(result!.source).toBe('cache');
  });

  it('returns null when token is expired (within buffer)', async () => {
    const strategy = new CacheStrategy();
    // Expires in 2 minutes — within 5-minute buffer
    const nearExpiry = Date.now() + 2 * 60 * 1000;
    strategy.store('expiring-token', nearExpiry);

    const result = await strategy.tryAcquireToken();
    expect(result).toBeNull();
  });

  it('clears cache', async () => {
    const strategy = new CacheStrategy();
    strategy.store('to-clear', Date.now() + 60 * 60 * 1000);
    strategy.clear();

    const result = await strategy.tryAcquireToken();
    expect(result).toBeNull();
  });
});
