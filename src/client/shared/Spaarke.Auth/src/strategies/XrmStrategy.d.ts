import type { ITokenResult, ITokenStrategy } from '../types';
/**
 * Acquire token via Xrm platform APIs (Dataverse host).
 *
 * Frame-walk resolution: window → parent → top
 * This covers:
 *   - Code pages loaded directly in Dataverse (window.Xrm)
 *   - Code pages in iframes (window.parent.Xrm)
 *   - Deeply nested iframes (window.top.Xrm)
 */
export declare class XrmStrategy implements ITokenStrategy {
  readonly name: 'xrm';
  private readonly scope;
  constructor(scope: string);
  tryAcquireToken(): Promise<ITokenResult | null>;
  private _resolveXrm;
  /** Get the configured scope (used for diagnostics). */
  getScope(): string;
  private _buildResult;
}
//# sourceMappingURL=XrmStrategy.d.ts.map
