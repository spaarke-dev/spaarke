import type { ITokenResult, ITokenStrategy } from '../types';
/** In-memory token cache strategy (fastest, no I/O). */
export declare class CacheStrategy implements ITokenStrategy {
  readonly name: 'cache';
  private _entry;
  tryAcquireToken(): Promise<ITokenResult | null>;
  /** Store a token in the cache. Called after any successful acquisition. */
  store(token: string, expiresOn: number): void;
  /** Clear the cache (e.g., on 401 retry). */
  clear(): void;
  /** Return the raw cached token string, or null if empty/expired. */
  getCachedToken(): string | null;
}
//# sourceMappingURL=CacheStrategy.d.ts.map
