/**
 * ensureNavigatorPane — registers the global Navigator side pane from the
 * SpaarkeAi home page (spaarke-side-pane-navigation-history-r1 task 086).
 *
 * WHY here (owner decision 2026-08-14): modern UCI has no reliable global
 * app-load JS hook. The retired hidden-`Mscrm.GlobalTab`-enable-rule bootstrap
 * (`sprk_SidePaneManager`) no longer fires on the current UCI, and per-form
 * OnLoad handlers are disallowed by the project (NFR-02). SpaarkeAi is the
 * universal home page, so registering the pane here — the same host-launch
 * pattern EventsPage uses for CalendarSidePane — docks it when the user lands
 * on home. Because `Xrm.App.sidePanes` panes are APP-LEVEL (not page-scoped)
 * and this one is created `canClose:false` + `alwaysRender:true`, it persists
 * (and its capture poll keeps running) across all subsequent navigation.
 *
 * The pane CONTENT is the deployed `sprk_NavigatorPane.html` webresource; this
 * function only creates the app pane and navigates it there. Registration logic
 * mirrors the `sprk_SidePaneManager` bootstrap (kept as a small self-contained
 * duplicate rather than cross-importing the NavigatorPane solution's plain-JS
 * WR — different deployment contexts).
 *
 * Idempotent + resilient: a module-level guard prevents duplicate starts; a
 * short backoff retry covers the case where `Xrm.App.sidePanes` is not ready at
 * first mount. NEVER throws — a failure here must not break the SpaarkeAi shell.
 */

import { getXrm } from "@spaarke/ui-components";

const PANE_ID = "sprk-navigator";
const PANE_TITLE = "Navigator";
const WEBRESOURCE = "sprk_NavigatorPane.html";
const PANE_WIDTH = 420;
const MAX_ATTEMPTS = 12;

let _started = false;

interface SidePane {
  navigate?: (input: { pageType: string; webresourceName: string }) => Promise<unknown>;
}
interface SidePanesApi {
  createPane?: (opts: Record<string, unknown>) => Promise<SidePane>;
  getPane?: (id: string) => SidePane | undefined;
}

function getSidePanes(): SidePanesApi | null {
  try {
    const xrm = getXrm() as unknown as { App?: { sidePanes?: SidePanesApi } } | undefined;
    const sp = xrm?.App?.sidePanes;
    return sp && typeof sp.createPane === "function" ? sp : null;
  } catch {
    return null;
  }
}

function paneExists(sp: SidePanesApi): boolean {
  try {
    return typeof sp.getPane === "function" ? !!sp.getPane(PANE_ID) : false;
  } catch {
    return false;
  }
}

/**
 * Create + navigate the app-level Navigator pane once. Safe to call on every
 * SpaarkeAi mount — the module guard + `getPane` check make repeat calls no-ops.
 */
export function ensureNavigatorPane(): void {
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
        .createPane?.({
          paneId: PANE_ID,
          title: PANE_TITLE,
          imageSrc: "WebResources/sprk_navigatorstar.svg", // rail launcher icon (star)
          canClose: false, // always-available docked pane (spec MUST)
          width: PANE_WIDTH,
          isSelected: false, // start collapsed; alwaysRender keeps capture alive
          alwaysRender: true, // load-bearing: pane JS runs while collapsed (NFR-05)
        })
        .then((pane) => pane?.navigate?.({ pageType: "webresource", webresourceName: WEBRESOURCE }))
        .catch(() => {
          /* non-fatal — pane simply not registered this session */
        });
    } catch {
      /* non-fatal */
    }
  };

  tryRegister();
}
