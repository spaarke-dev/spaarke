import type { ITokenResult, ITokenStrategy } from "../types";

/**
 * Acquire token via Xrm platform APIs (Dataverse host).
 *
 * Frame-walk resolution: window → parent → top
 * This covers:
 *   - Code pages loaded directly in Dataverse (window.Xrm)
 *   - Code pages in iframes (window.parent.Xrm)
 *   - Deeply nested iframes (window.top.Xrm)
 */
export class XrmStrategy implements ITokenStrategy {
  readonly name = "xrm" as const;

  private readonly scope: string;

  constructor(scope: string) {
    this.scope = scope;
  }

  async tryAcquireToken(): Promise<ITokenResult | null> {
    const xrm = this._resolveXrm();
    if (!xrm) return null;

    // Strategy 1: Xrm.Utility.getGlobalContext().getCurrentAppProperties()
    // Some Dataverse hosts expose token via app properties
    try {
      const context = xrm.Utility?.getGlobalContext?.();
      if (context?.getCurrentAppProperties) {
        const props = await context.getCurrentAppProperties();
        if (props?.accessToken) {
          return this._buildResult(props.accessToken);
        }
      }
    } catch {
      /* not available in this host */
    }

    // Strategy 2: Use Xrm.WebApi to make a probe call (triggers token acquisition)
    // This is a fallback — not all Xrm hosts expose tokens directly
    // The real token comes from MSAL if Xrm strategies fail

    return null;
  }

  private _resolveXrm(): XrmLike | null {
    if (typeof window === "undefined") return null;

    // Frame-walk: window → parent → top
    const frames: Window[] = [window];
    try {
      if (window.parent !== window) frames.push(window.parent);
    } catch {
      /* cross-origin */
    }
    try {
      if (window.top && window.top !== window && window.top !== window.parent) {
        frames.push(window.top);
      }
    } catch {
      /* cross-origin */
    }

    for (const frame of frames) {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (frame as any).Xrm;
        if (xrm?.Utility?.getGlobalContext) return xrm as XrmLike;
      } catch {
        /* cross-origin */
      }
    }

    return null;
  }

  /** Get the configured scope (used for diagnostics). */
  getScope(): string {
    return this.scope;
  }

  private _buildResult(token: string): ITokenResult {
    return {
      accessToken: token,
      expiresOn: Date.now() + 55 * 60 * 1000,
      source: "xrm",
    };
  }
}

/** Minimal Xrm type for frame-walk resolution. */
interface XrmLike {
  Utility?: {
    getGlobalContext?: () => {
      getCurrentAppProperties?: () => Promise<{ accessToken?: string }>;
      organizationSettings?: { tenantId?: string };
    };
  };
  WebApi?: unknown;
}
