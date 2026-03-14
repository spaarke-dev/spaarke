import type { ITokenResult, ITokenStrategy } from '../types';
import { readBridgeToken } from '../tokenBridge';

/** Reads token from parent/own window bridge global (~0.1ms). */
export class BridgeStrategy implements ITokenStrategy {
  readonly name = 'bridge' as const;

  async tryAcquireToken(): Promise<ITokenResult | null> {
    const token = readBridgeToken();
    if (!token) return null;

    return {
      accessToken: token,
      expiresOn: Date.now() + 55 * 60 * 1000, // Assume 55-min lifetime
      source: 'bridge',
    };
  }
}
