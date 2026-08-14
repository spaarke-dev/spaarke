/**
 * viewNavigation — open a personal (`userquery`) saved view via its REAL
 * `main.aspx` deep-link URL (spaarke-side-pane-navigation-history-r1 UAT fix
 * #5).
 *
 * UAT bug: `Xrm.Navigation.navigateTo({pageType:'entitylist', viewId,
 * viewType:'4230'})` does not reliably honor `viewId` on the CURRENT UCI
 * session — Dataverse silently falls back to the entity's DEFAULT view
 * instead of the requested personal view (a documented client-API gap for
 * `navigateTo`-driven view selection, not a NavigatorPane bug). The reliable,
 * research-confirmed path is a `main.aspx` deep-link URL opened via
 * `Xrm.Navigation.openUrl`:
 *
 *   `{clientUrl}/main.aspx?appid={appId}&pagetype=entitylist&etn={entity}&viewid=%7b{guid}%7d&viewtype=4230`
 *
 * (`4230` = personal `userquery` view type — every view this project's
 * Navigator Views tab / QuickSwitcher search result ever lists comes from
 * `ViewService.getAllUserQueries()`, i.e. is ALWAYS a `userquery`.)
 *
 * `appId` resolution order:
 *   1. `Xrm.Utility.getGlobalContext().getCurrentAppProperties()` (async) —
 *      not on the base `xrmContext.ts` `GlobalContext` shape, so narrowed
 *      locally + cast at the boundary (mirrors this project's established
 *      pattern, e.g. `bookmarkService.ts`'s `XrmUtilityWithPageContext`,
 *      `liveSearchService.ts`'s `XrmUtilityWithEntityMetadata`).
 *   2. If unavailable/throws: parse `appid` off
 *      `getGlobalContext().getCurrentAppUrl()`.
 *   3. If NEITHER resolves an appId (or `getClientUrl()`/`Navigation.openUrl`
 *      are themselves unavailable — e.g. a minimal `Utility` in a unit-test
 *      fake `Xrm`): fall back to the original
 *      `Xrm.Navigation.navigateTo({pageType:'entitylist', viewId,
 *      viewType:'userquery'})` call. `viewType` here is the STRING
 *      `'userquery'` (not `'4230'`) — the documented string form of
 *      `PageInputEntityList.viewType`, distinct from the numeric `4230` the
 *      URL querystring param uses.
 *
 * ONE helper, TWO callers (CLAUDE.md §11 — a real second consumer earns the
 * extraction rather than each duplicating the same URL-building +
 * appId-resolution logic): `ViewsTab.tsx`'s row click, and
 * `QuickSwitcher.tsx`'s Enter/click on a `type:'entitylist'` search result
 * (so a view search hit opens the SAME real view, not the entity default).
 *
 * @see ViewsTab.tsx — Views-tab row click, the primary caller
 * @see QuickSwitcher.tsx — reuses this SAME helper for `entitylist` search results
 * @see navItemRepository.ts — `normalizeGuid` reused here rather than re-declared
 */

import { normalizeGuid } from '@spaarke/ui-components/services/navigator/navItemRepository';
import type { GlobalContext, XrmContext } from '@spaarke/ui-components';

/**
 * `Xrm.Utility.getGlobalContext()`'s async app-properties API — not declared
 * on `xrmContext.ts`'s `GlobalContext` (task 010's minimal shared surface).
 * Narrowed locally and cast at the boundary rather than widening the shared
 * type (ADR-022 "keep the shared surface slim" — mirrors this module's own
 * docblock precedent list above).
 */
interface GlobalContextWithAppProperties {
  getCurrentAppProperties?: () => Promise<{ appId?: string } | undefined>;
}

/** Best-effort `appid` extraction from a `...&appid=...`-shaped app URL. Returns `undefined` on no match. */
function parseAppIdFromUrl(appUrl: string | undefined): string | undefined {
  if (!appUrl) return undefined;
  const match = /[?&]appid=([^&]+)/i.exec(appUrl);
  return match ? decodeURIComponent(match[1]) : undefined;
}

/** Resolve the current app's `appid` — see module docblock for the 2-step resolution order. */
async function resolveAppId(globalContext: GlobalContext): Promise<string | undefined> {
  const withAppProperties = globalContext as GlobalContext & GlobalContextWithAppProperties;
  if (typeof withAppProperties.getCurrentAppProperties === 'function') {
    try {
      const props = await withAppProperties.getCurrentAppProperties();
      if (props?.appId) return props.appId;
    } catch {
      // Fall through to the URL-parse fallback below.
    }
  }

  try {
    return parseAppIdFromUrl(globalContext.getCurrentAppUrl?.());
  } catch {
    return undefined;
  }
}

/**
 * Open a personal (`userquery`) saved view's entity list with that view
 * ACTUALLY selected. See module docblock for the URL shape + fallback chain.
 * Never throws — any failure to resolve `clientUrl`/`appId` falls back to the
 * original `navigateTo` call; a missing `Navigation` altogether is a silent
 * no-op (mirrors every other Navigator navigation helper's "no Navigation ->
 * nothing to do" contract).
 *
 * @param xrm - A freshly-acquired `XrmContext` (never cached — task-001 spike lesson).
 * @param entityLogicalName - The view's entity, e.g. `sprk_matter`.
 * @param viewId - The view's `userqueryid` (braces optional — normalized internally).
 */
export async function openView(xrm: XrmContext, entityLogicalName: string, viewId: string): Promise<void> {
  const navigation = xrm.Navigation;
  const globalContext = xrm.Utility?.getGlobalContext?.();
  const clientUrl = globalContext?.getClientUrl?.();

  if (globalContext && clientUrl && navigation?.openUrl) {
    const appId = await resolveAppId(globalContext);
    if (appId) {
      const guid = normalizeGuid(viewId);
      const url =
        `${clientUrl}/main.aspx?appid=${encodeURIComponent(appId)}&pagetype=entitylist` +
        `&etn=${encodeURIComponent(entityLogicalName)}&viewid=%7b${guid}%7d&viewtype=4230`;
      navigation.openUrl(url);
      return;
    }
  }

  // Fallback — original navigateTo path. STRING 'userquery' (not the URL
  // param's numeric '4230') — see module docblock.
  if (navigation?.navigateTo) {
    await navigation.navigateTo({
      pageType: 'entitylist',
      entityName: entityLogicalName,
      viewId,
      viewType: 'userquery',
    });
  }
}

export default openView;
