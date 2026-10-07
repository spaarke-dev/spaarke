/**
 * Xrm Provider — Dataverse API access for a standalone HTML web resource
 * (Custom Page), via the shared cross-frame walker (`getXrm` in
 * @spaarke/ui-components, task 081 / C-8).
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

import { cleanGuid, getXrm } from '@spaarke/ui-components';

/**
 * Get the current user's GUID (no braces).
 * Equivalent to PCF's `context.userSettings.userId` — feeds
 * `<ConversationView />`'s `currentUserSystemUserId` prop (FR-02/FR-18:
 * own/others bubble alignment is STRICTLY an identity comparison).
 */
export function getUserId(): string {
  // Shared cross-frame walker (task 081 / C-8). `any` view: getUserId() is not on the typed GlobalContext.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm: any = getXrm('utility');
  if (xrm?.Utility?.getGlobalContext) {
    const ctx = xrm.Utility.getGlobalContext();
    const raw = ctx.getUserId?.() ?? ctx.userSettings?.userId ?? '';
    return cleanGuid(raw);
  }
  console.warn('[CommunicationConversationPage] Unable to resolve userId from Xrm');
  return '';
}
