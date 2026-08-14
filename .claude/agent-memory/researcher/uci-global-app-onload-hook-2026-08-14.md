---
name: uci-global-app-onload-hook-2026-08-14
description: Whether a modern UCI model-driven app can run JS on EVERY page load globally (for re-registering an app side pane after full browser refresh); verdict + closest partial options
metadata:
  type: reference
---

# Global "run JS on every page load" hook in UCI (2026-08-14)

**VERDICT: no fully-supported, documented global app-load event.** Learn's "Events in forms and grids" lists every documented client-side event and they are ALL scoped to a form (OnLoad / form-data OnLoad / form Loaded / OnSave), a column (OnChange), or a control (grid/subgrid OnLoad, lookup PreSearch, kbsearch). There is NO application-level / global page-load event in the Client API. Dashboards and the app home have no OnLoad hook.

**The AppModule `<EventHandlers><EventHandler eventname="onload" .../>` trick (edit customizations.xml on the AppModule node) DOES fire app-wide, but it is UNDOCUMENTED and UNSUPPORTED by Microsoft.** Widely used by the community; works, but carries the usual unsupported-customization risk (can break without notice, unsupportable via MS ticket).

**Closest SUPPORTED partial options (all incomplete for a true "every page" bootstrap):**
- Form OnLoad on main forms → covers RECORD pages only.
- Home-grid / subgrid OnLoad → covers list/grid pages only.
- A PCF control placed on forms → still form-scoped, not app-global.
- Sitemap subarea pointing at an HTML web resource → only runs when that subarea is navigated to, not every page.
- The global ribbon/command enable-rule approach regressed and does not fire reliably on current UCI (matches caller's finding).

**Implication for the app side pane (`Xrm.App.sidePanes.createPane`):** it survives in-app client navigation but not a full browser refresh; there is no supported single global hook to re-register it on arbitrary page loads. Practical supported coverage = register from main-form OnLoad + home-grid OnLoad (covers record + list pages); dashboards/home remain gaps. The undocumented AppModule onload is the only true global option and is unsupported.

**Sources:**
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/events-forms-grids (authoritative: full event list, all form/grid/control-scoped, no app-level event)
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/customization-xml-reference (AppModule XML)
- Community (unsupported AppModule onload): xrmtricks.com/2021/05/07 ; skysoftconnections.com JavaScript on App OnLoad

**Open questions:** Whether any 2026 preview introduces an official app-onload event (none found as of 2026-08-14).
