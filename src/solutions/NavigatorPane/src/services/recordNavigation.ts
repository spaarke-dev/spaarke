/**
 * recordNavigation — the ONE place the Navigator decides HOW to open an
 * `entityrecord` target (spaarke-side-pane-navigation-history-r1, UAT).
 *
 * Most records open via the OOB main form (`Xrm.Navigation.navigateTo`
 * `pageType:'entityrecord'`). The single exception is `sprk_communication`
 * (Email/message records): those open in the dedicated **Email code page**
 * (`sprk_emailpage`) in single-record mode, NOT the generic Communication main
 * form — the Email code page renders the shared `EmailWorkspace` surface
 * (`.eml` body, compose, connections), which is the intended reading
 * experience for an email (UAT feedback: Recent/Bookmarks/Search email links
 * previously dropped the user on the raw OOB form).
 *
 * Reused by RecentTab.tsx, BookmarksTab.tsx, and QuickSwitcher.tsx — the three
 * places a stored `entityrecord` target is opened — so the routing rule lives
 * in ONE place (CLAUDE.md §11) rather than being duplicated three times.
 *
 * Host-context only (project constraint): navigates via `Xrm.Navigation`,
 * never a constructed raw URL for a logical target. The Email code page hand-
 * off uses `pageType:'webresource'` + `data` — the SAME Pattern-B launch
 * envelope `EmailPage/src/main.tsx`'s `resolveEmailLaunch` already parses
 * (`<communicationId>&single=1` → single-record view).
 */

import { normalizeGuid } from '@spaarke/ui-components/services/navigator/navItemRepository';
import type { XrmContext } from '@spaarke/ui-components';

/** Communication (Email/message) records route to the Email code page. */
export const COMMUNICATION_ENTITY = 'sprk_communication';

/** Deployed web-resource name of the Email code page (see EmailPage build rename). */
export const EMAIL_CODE_PAGE = 'sprk_emailpage';

/**
 * Open an `entityrecord` target. `sprk_communication` records route to the
 * Email code page (`sprk_emailpage`) in single-record mode; every other entity
 * opens via the OOB main form. No-ops when `Xrm.Navigation` is unavailable or
 * the id is missing. Never a raw URL for a logical target (project MUST).
 */
export function openEntityRecord(
  xrm: XrmContext,
  entityLogicalName: string,
  entityId: string | null | undefined
): void {
  const navigation = xrm.Navigation;
  if (!navigation || !entityId) return;

  if (entityLogicalName === COMMUNICATION_ENTITY) {
    // Pattern B: the Email code page reads the bare communication id (+ folded
    // `&single=1` single-record flag) off the `data` param. `single=1` opens
    // just this email (no surrounding list) — see EmailPage resolveEmailLaunch.
    void navigation.navigateTo({
      pageType: 'webresource',
      webresourceName: EMAIL_CODE_PAGE,
      data: `${normalizeGuid(entityId)}&single=1`,
    });
    return;
  }

  void navigation.navigateTo({
    pageType: 'entityrecord',
    entityName: entityLogicalName,
    entityId,
  });
}
