/**
 * Token bridge utilities for parent-to-child iframe token sharing.
 *
 * When a parent page (e.g., LegalWorkspace) opens a child page as an iframe dialog,
 * the parent can publish its token so the child skips MSAL initialization (~0.1ms vs 500-1300ms).
 */
/**
 * Publish a token on the current window for child iframes to read.
 * Call this from the parent page after acquiring a token.
 */
export declare function publishToken(token: string): void;
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
export declare function readBridgeToken(): string | null;
/**
 * Clear the bridge token from the current window.
 * Call on logout or dialog close.
 */
export declare function clearBridgeToken(): void;
//# sourceMappingURL=tokenBridge.d.ts.map
