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
 * Read a bridge token from the current window or ANY ancestor frame.
 * Returns the token string or null if not found.
 *
 * Walks the FULL frame tree (window → parent → grandparent → ... → top)
 * because in Dataverse, the iframe nesting can be 3-4 levels deep:
 *
 *   MDA Shell (top)
 *     └─ Form iframe
 *          └─ Corporate Workspace (publishes token here)
 *               └─ navigateTo dialog chrome
 *                    └─ DocumentRelationshipViewer (reads token here)
 *
 * The old code only checked window.parent (1 level), which missed the token
 * when the publisher was 2+ frames up — causing unnecessary MSAL popups.
 */
export function readBridgeToken(): string | null {
  if (typeof window === 'undefined') return null;

  // Walk the frame tree: own → parent → grandparent → ... → top
  let current: Window | null = window;
  const visited = new Set<Window>();

  while (current && !visited.has(current)) {
    visited.add(current);

    try {
      const token = (current as Window)[BRIDGE_KEY];
      if (token) return token;
    } catch {
      /* cross-origin — stop walking this branch */
      break;
    }

    // Move to parent frame
    try {
      if (current.parent && current.parent !== current) {
        current = current.parent;
      } else {
        break;
      }
    } catch {
      /* cross-origin — can't access parent */
      break;
    }
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
