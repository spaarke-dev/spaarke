import type { ITokenResult, ITokenStrategy } from '../types';
/** Reads token from parent/own window bridge global (~0.1ms). */
export declare class BridgeStrategy implements ITokenStrategy {
  readonly name: 'bridge';
  tryAcquireToken(): Promise<ITokenResult | null>;
}
//# sourceMappingURL=BridgeStrategy.d.ts.map
