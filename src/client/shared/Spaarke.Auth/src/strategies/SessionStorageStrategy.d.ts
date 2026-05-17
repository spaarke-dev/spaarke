import type { ITokenResult, ITokenStrategy } from '../types';
export declare class SessionStorageStrategy implements ITokenStrategy {
  readonly name: 'session-storage';
  tryAcquireToken(): Promise<ITokenResult | null>;
  /**
   * Write a token to sessionStorage for cross-iframe sharing.
   * Called from SpaarkeAuthProvider._cacheAndPublish() after ANY successful acquisition.
   */
  store(token: string, expiresOn: number): void;
  /** Clear the sessionStorage token. Called on 401 retry or explicit cache clear. */
  clear(): void;
}
//# sourceMappingURL=SessionStorageStrategy.d.ts.map
