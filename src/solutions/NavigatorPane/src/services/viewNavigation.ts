/**
 * viewNavigation — open a personal (`userquery`) saved view IN-APP with that
 * view selected (spaarke-side-pane-navigation-history-r1 UAT fix #5).
 *
 * History of this bug (two wrong attempts before this one):
 *   1. `navigateTo({ …, viewType: '4230' })` — WRONG: `navigateTo`'s
 *      `viewType` is the STRING `'userquery'`/`'savedquery'` (the numeric
 *      `1039`/`4230` codes are only for the `main.aspx?viewtype=` URL param).
 *      The invalid `'4230'` string was ignored → entity DEFAULT view opened.
 *   2. `Xrm.Navigation.openUrl('{clientUrl}/main.aspx?…&viewtype=4230')` —
 *      opened the view in a NEW BROWSER TAB (openUrl's behavior for a full
 *      URL), which is the wrong UX, and still fought the per-table sticky
 *      view selector.
 *
 * This version uses `navigateTo` with the correct STRING `viewType:'userquery'`
 * so it navigates IN-APP (no new tab) to the entity list with the personal
 * view selected. Every view the Navigator Views tab / QuickSwitcher lists comes
 * from `ViewService.getAllUserQueries()`, i.e. is ALWAYS a `userquery` — so
 * `'userquery'` is unconditionally correct here.
 *
 * Known platform caveat (not a NavigatorPane bug): modern UCI keeps a per-user,
 * per-table "last selected view" that CAN override the requested view after a
 * browser reopen, and `navigateTo` view selection is documented as best-effort.
 * If exact-view selection proves unreliable in the target org, that is the
 * platform limitation, not this code.
 *
 * ONE helper, TWO callers (CLAUDE.md §11): `ViewsTab.tsx`'s row click and
 * `QuickSwitcher.tsx`'s Enter/click on a `type:'entitylist'` search result.
 *
 * @see ViewsTab.tsx — Views-tab row click, the primary caller
 * @see QuickSwitcher.tsx — reuses this SAME helper for `entitylist` search results
 * @see navItemRepository.ts — `normalizeGuid` reused here rather than re-declared
 */

import { normalizeGuid } from '@spaarke/ui-components/services/navigator/navItemRepository';
import type { XrmContext } from '@spaarke/ui-components';

/**
 * Navigate IN-APP to a personal (`userquery`) view's entity list with that
 * view selected. Never throws — a missing `Navigation` is a silent no-op
 * (mirrors every other Navigator navigation helper's contract).
 *
 * @param xrm - A freshly-acquired `XrmContext` (never cached — task-001 spike lesson).
 * @param entityLogicalName - The view's entity, e.g. `sprk_matter`.
 * @param viewId - The view's `userqueryid` (braces optional — normalized internally).
 */
export async function openView(xrm: XrmContext, entityLogicalName: string, viewId: string): Promise<void> {
  const navigation = xrm.Navigation;
  if (!navigation?.navigateTo) return;

  await navigation.navigateTo({
    pageType: 'entitylist',
    entityName: entityLogicalName,
    viewId: normalizeGuid(viewId),
    // STRING 'userquery' — the documented PageInputEntityList.viewType value
    // for a personal view (NOT the numeric '4230', which is a URL-only code).
    viewType: 'userquery',
  });
}

export default openView;
