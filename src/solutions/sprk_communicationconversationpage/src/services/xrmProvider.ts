/**
 * Xrm Provider — frame-walk utility for accessing Dataverse APIs from
 * a standalone HTML web resource (Custom Page).
 *
 * Mirrors `src/solutions/LegalWorkspace/src/services/xrmProvider.ts` (per
 * ADR-026: standalone HTML web resources use this frame-walk pattern instead
 * of the PCF `context.userSettings` mechanism). Only the single accessor this
 * page needs is ported: the caller's own `systemuserid`, fed into
 * `<ConversationView />`'s `currentUserSystemUserId` prop (FR-02/FR-18
 * sender-identity bubble alignment). This all-mode page has no `regarding`
 * scope of its own and `<ConversationWorkspace />`'s `renderConversation` seam
 * does not forward per-thread `regarding`/`title` metadata (see
 * `ConversationWorkspace.tsx` — `IConversationRendererProps` carries only
 * `threadId`/`authenticatedFetch`/`bffBaseUrl`), so `onOpenRecord`/`title` are
 * intentionally NOT wired here — there is no source data to drive them, and
 * wiring an inert callback would be dead code (root CLAUDE.md §11).
 */

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Locate the Xrm global by walking the frame hierarchy.
 * Priority: current window → parent window → top window.
 * Returns null if Xrm is not available (e.g., local dev server).
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function getXrm(): any | null {
  if (typeof Xrm !== 'undefined' && Xrm?.Utility) {
    return Xrm;
  }
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.Utility) {
      (window as any).Xrm = parentXrm;
      return parentXrm;
    }
  } catch {
    /* cross-origin — swallow */
  }
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const topXrm = (window.top as any)?.Xrm;
    if (topXrm?.Utility) {
      (window as any).Xrm = topXrm;
      return topXrm;
    }
  } catch {
    /* cross-origin — swallow */
  }
  return null;
}

/**
 * Get the current user's GUID (no braces).
 * Equivalent to PCF's `context.userSettings.userId` — feeds
 * `<ConversationView />`'s `currentUserSystemUserId` prop (FR-02/FR-18:
 * own/others bubble alignment is STRICTLY an identity comparison).
 */
export function getUserId(): string {
  const xrm = getXrm();
  if (xrm?.Utility?.getGlobalContext) {
    const ctx = xrm.Utility.getGlobalContext();
    const raw = ctx.getUserId?.() ?? ctx.userSettings?.userId ?? '';
    return raw.replace(/[{}]/g, '');
  }
  console.warn('[CommunicationConversationPage] Unable to resolve userId from Xrm');
  return '';
}
