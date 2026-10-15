---
name: navigateto-dialog-chrome-dark-mode-2026-10-05
description: navigateTo target:2 dialog title bar is host chrome; no hide option; MDA has no supported dark mode; sidePanes hideHeader is the only documented header-off switch
metadata:
  type: reference
---

## 2026-10-05: navigateTo dialog "white header" vs dark mode
**Question**: Can the title bar/close button of `Xrm.Navigation.navigateTo` target:2 dialogs (web resource / custom page) be themed dark or hidden?
**Findings**: navigationOptions is exactly 5 fields: target, width, height, position, title (navigateto doc, ms.date 2026-04-09) — no hideHeader/showHeader. The title bar is host (UCI) chrome rendered from `title`; the doc does not literally say "outside the iframe" (inferred). Microsoft states "Switching themes or enabling dark mode isn't supported at this time" (modern-fluent-design, updated 2026-09-15); Modern theme XML (CustomTheme/AppHeaderColors) has no dark mode and themes the app header + accent slots only. Spaarke's MDA dark mode is the UNDOCUMENTED `flags=themeOption%3Ddarkmode` URL flag (ThemeEnforcer PCF, docs/architecture/ui-dialog-shell-architecture.md) — so dialog chrome staying white is a gap in an unsupported flag, not something fixable via API. `Xrm.App.sidePanes.createPane({hideHeader:true})` is the only documented header-off switch (non-modal). Host DOM access is unsupported (supported-customizations, 2026-08-10), yet Spaarke already reads `[data-id="navbar-container"]` via parent DOM (themeStorage.ts).
**Sources**:
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto
- https://learn.microsoft.com/en-us/power-apps/user/modern-fluent-design
- https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/modern-theme-overrides
- https://learn.microsoft.com/en-us/power-apps/user/appearance-settings (density only, preview)
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app/xrm-app-sidepanes/createpane
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/supported-customizations
**Open questions**: whether the undocumented darkmode flag darkens dialog chrome in any build (needs live probe); no public 2026 wave 2 dark-mode release-plan item found.
