/**
 * Token bridge utilities for parent-to-child iframe token sharing.
 *
 * When a parent page (e.g., LegalWorkspace) opens a child page as an iframe dialog,
 * the parent can publish its token so the child skips MSAL initialization (~0.1ms vs 500-1300ms).
 */

const BRIDGE_KEY = '__SPAARKE_BFF_TOKEN__';

/**
 * Publish a token on the current window for child iframes to read.
 * Call this from the parent page after acquiring a token.
 */
export function publishToken(token: string): void {
  if (typeof window !== 'undefined') {
    (window as Window)[BRIDGE_KEY] = token;
  }
}

/**
 * Read a bridge token from the current window or parent frames.
 * Returns the token string or null if not found.
 *
 * Resolution order:
 *   1. window.__SPAARKE_BFF_TOKEN__ (own frame)
 *   2. window.parent.__SPAARKE_BFF_TOKEN__ (parent iframe)
 */
export function readBridgeToken(): string | null {
  if (typeof window === 'undefined') return null;

  // 1. Own frame
  const ownToken = (window as Window)[BRIDGE_KEY];
  if (ownToken) return ownToken;

  // 2. Parent frame (may throw on cross-origin)
  try {
    if (window.parent !== window) {
      const parentToken = (window.parent as Window)[BRIDGE_KEY];
      if (parentToken) return parentToken;
    }
  } catch {
    /* cross-origin — swallow */
  }

  return null;
}

/**
 * Clear the bridge token from the current window.
 * Call on logout or dialog close.
 */
export function clearBridgeToken(): void {
  if (typeof window !== 'undefined') {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any)[BRIDGE_KEY];
  }
}
