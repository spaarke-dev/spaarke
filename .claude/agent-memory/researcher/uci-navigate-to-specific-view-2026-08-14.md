---
name: uci-navigate-to-specific-view-2026-08-14
description: How to reliably open a specific system (savedquery) or personal (userquery) view from client JS in a modern UCI model-driven app; navigateTo viewId unreliability + main.aspx URL workaround
metadata:
  type: reference
---

# Opening a SPECIFIC view (saved/personal) from client JS in UCI (2026-08-14)

**navigateTo entitylist viewType is a STRING, NOT a number.** In `Xrm.Navigation.navigateTo({pageType:"entitylist", entityName, viewId, viewType})`, `viewType` = `"savedquery"` (system view) or `"userquery"` (personal view). The numeric codes 1039/4230 are ONLY for the main.aspx URL, not for navigateTo.

**main.aspx URL viewtype codes (Learn-confirmed):** `viewtype=1039` = system view (viewid is a `savedquery` id); `viewtype=4230` = personal view (viewid is a `userquery` id). URL shape: `main.aspx?appid={guid}&pagetype=entitylist&etn={logicalname}&viewid=%7b{guid}%7d&viewtype={1039|4230}`. `viewid` REQUIRED for views. `etn` (logical name) not `etc`.

**navigateTo viewId reliability = POOR in practice.** Docs claim viewId is honored, but (a) a community/PCF thread + real-world reports say entitylist opens the DEFAULT view/control and there's "no parameter to change it"; (b) UCI keeps a STICKY per-table last-selected view — Learn explicitly states even the URL approach still shows the view selector and "remembers the user's most recent selection" so the requested view can be overridden after reopen. So requesting a non-default (esp. userquery) view via navigateTo commonly falls back to default.

**Reliable approach = build main.aspx URL + openUrl.** Get appid from `Xrm.Utility.getGlobalContext().getCurrentAppProperties()` (async → `app.appId`) or parse `getCurrentAppUrl()` (already returns `.../main.aspx?appid=<guid>`). Base org URL from `getClientUrl()`. Then `Xrm.Navigation.openUrl(url)`. GUID brackets encode `{`→`%7b`, `}`→`%7d`. Even this is subject to the sticky-selector caveat but is the most reliable supported lever.

**Sources:**
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto (viewType = string savedquery/userquery; entitylist opens inline only)
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/open-forms-views-dialogs-reports-url (MOST authoritative: viewtype 1039/4230, appid/appname params, sticky-view-selector note)
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-utility/getglobalcontext/getcurrentappproperties (appId, uniqueName, url, welcomePageId...)
- Community PCF thread threadid=9b2cdbdb-11ff-4d1c-ad8f-c08d259ad4ab (navigateTo cannot force non-default view/control)

**Open questions:** Whether the sticky-selector override can be defeated (no documented API to clear a user's remembered per-table view).
