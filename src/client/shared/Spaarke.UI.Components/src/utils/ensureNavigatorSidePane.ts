/**
 * ensureNavigatorSidePane — the SHARED, reusable code-page registrar for the
 * Navigator side pane (spaarke-side-pane-navigation-history-r1).
 *
 * WHY THIS EXISTS HERE (not duplicated per code page): modern UCI has no
 * reliable global app-load JS hook (see docs/architecture/SPAARKE-SIDE-PANE-
 * NAVIGATION.md §3). Two insertions compensate — an entity command-bar
 * EnableRule (silent auto-dock on that entity's grid/form, wired via the
 * `sprk_SidePaneManager` bootstrap web resource) and, for code pages, this
 * ONE shared function. Every Spaarke code page that wants the Navigator
 * docked on mount imports `ensureNavigatorSidePane` from `@spaarke/ui-components`
 * and calls it once — no per-solution copy to keep in sync.
 *
 * This is the SAME registration logic as the two prior, independently
 * maintained copies it now supersedes as the canonical source:
 *   - `src/solutions/SpaarkeAi/src/ensureNavigatorPane.ts` (now a thin
 *     re-export of this function — see that file's docblock)
 *   - `src/solutions/NavigatorPane/bootstrap/sprk_SidePaneManager.js` (the
 *     plain-JS ribbon/EnableRule bootstrap — different deployment context
 *     (a Dataverse web resource, not an ES module), so it remains a small
 *     self-contained duplicate; this module is the browser-bundle sibling)
 *
 * Behavior:
 *   - Creates ONE app-level pane (`Xrm.App.sidePanes.createPane`) with
 *     `paneId: 'sprk-navigator'`, `canClose: false` (always-available docked
 *     pane), `alwaysRender: true` (keeps the pane's capture poll running
 *     while collapsed), `isSelected: false` (starts collapsed), and the
 *     Navigator's rail icon (`imageSrc: 'WebResources/sprk_navigatorstar.svg'`).
 *   - Navigates the new pane to the `sprk_NavigatorPane.html` web resource.
 *   - Idempotent: a module-level guard plus a `getPane` existence check make
 *     repeat calls (e.g. across component remounts) no-ops.
 *   - Resilient: retries with backoff if `Xrm.App.sidePanes` is not ready yet
 *     at first call (a real race on some cold-load timings).
 *   - NEVER throws — a failure here must not break the hosting code page.
 *
 * Plain function, no React, no `createRoot` — safe to import from React
 * 16/17 consumers per ADR-022 (PCF Platform Libraries / shared-lib React
 * version floor).
 *
 * @see docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md — full reference
 * @see xrmContext.ts — getXrm() + SidePanesApi/CreatePaneOptions/SidePane types
 */

import { getXrm } from './xrmContext';
import type { SidePanesApi } from './xrmContext';

const PANE_ID = 'sprk-navigator';
const PANE_TITLE = 'Navigator';
const WEBRESOURCE = 'sprk_NavigatorPane.html';
const PANE_WIDTH = 420;
const MAX_ATTEMPTS = 12;

let _started = false;

function getSidePanes(): SidePanesApi | null {
  try {
    const xrm = getXrm();
    const sp = xrm?.App?.sidePanes;
    return sp && typeof sp.createPane === 'function' ? sp : null;
  } catch {
    return null;
  }
}

function paneExists(sp: SidePanesApi): boolean {
  try {
    return typeof sp.getPane === 'function' ? !!sp.getPane(PANE_ID) : false;
  } catch {
    return false;
  }
}

/**
 * Create + navigate the app-level Navigator pane once. Safe to call on every
 * mount of the hosting code page — the module guard + `getPane` check make
 * repeat calls no-ops. NEVER throws.
 */
export function ensureNavigatorSidePane(): void {
  if (_started) return;
  _started = true;

  let attempts = 0;
  const tryRegister = (): void => {
    const sp = getSidePanes();
    if (!sp) {
      if (++attempts > MAX_ATTEMPTS) return;
      window.setTimeout(tryRegister, 500 * Math.min(attempts, 6));
      return;
    }
    if (paneExists(sp)) return;
    try {
      void sp
        .createPane({
          paneId: PANE_ID,
          title: PANE_TITLE,
          imageSrc: 'WebResources/sprk_navigatorstar.svg', // rail launcher icon (star)
          canClose: false, // always-available docked pane (spec MUST)
          width: PANE_WIDTH,
          isSelected: false, // start collapsed; alwaysRender keeps capture alive
          alwaysRender: true, // load-bearing: pane JS runs while collapsed (NFR-05)
        })
        .then(pane => pane?.navigate?.({ pageType: 'webresource', webresourceName: WEBRESOURCE }))
        .catch(() => {
          /* non-fatal — pane simply not registered this session */
        });
    } catch {
      /* non-fatal */
    }
  };

  tryRegister();
}
