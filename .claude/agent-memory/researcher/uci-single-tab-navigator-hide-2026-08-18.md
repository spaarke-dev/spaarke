---
name: uci-single-tab-navigator-hide-2026-08-18
description: Why a single-tab MDA form still shows the tab name ("General") in UCI and the ONLY supported way to hide it (setTabNavigatorVisible)
metadata:
  type: project
---

# Hiding the single-tab "General" pivot in Unified Interface

**Question (2026-08-18, smart-todo-r5 / sprk_todo "To Do main form"):** single-tab form still renders "General" as a tab header/pivot despite tab `ShowLabel:false` in formjson AND `showlabel="false"` in formxml; opened as `navigateTo` target:2 dialog. Can it be hidden, how?

## Verdict
- **YES, it can be hidden — the ONE supported method is the Client API call `formContext.ui.headerSection.setTabNavigatorVisible(false)` in an OnLoad handler.** UCI-only, documented on Learn (ms.date 2022, updated 2024-12).
- The **tab-navigator pivot is separate chrome from the tab's own `ShowLabel`/Label property.** `ShowLabel=false` (and blanking Label) governs the tab's inline label *inside the body*, NOT the top navigator pivot. The `showlabel` formxml attribute is legacy/deprecated and does NOT drive UCI pivot rendering — this is why the empirical fiddling had no effect.
- In UCI a single-tab form renders the tab NAME as a navigator pivot **by design**. There is no form-designer checkbox and no formjson/formxml attribute that removes it; only the runtime Client API method does.
- **Modal vs full-page: NO differential rendering of the tab navigator.** navigateTo target:2 hosts the same form component; the pivot shows in both. OnLoad fires in the dialog too, so `setTabNavigatorVisible(false)` works in the modal. (The dialog's own outer chrome — title bar / command bar — is separate and largely not removable via form config, but that is NOT the "General" pivot.)
- **Direct Web API PATCH of formjson is NOT a supported path.** formjson is platform-generated from formxml/publish; editing it directly is undocumented and can be regenerated/ignored. Not the sanctioned fix.
- Related: `setContentType("singleComponent")` (or formjson `ContentType:singleComponent`) is the full-bleed single-PCF variant that also auto-hides section/component labels — only relevant if the whole tab is one component.

## Sources
- Learn: setTabNavigatorVisible (Client API) — https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/formcontext-ui-headersection/settabnavigatorvisible ("supported only on Unified Interface"; false = hide tab navigator) — MOST authoritative
- Diana Birkelbach (MVP): Single-Component Tabs in MDA — https://dianabirkelbach.wordpress.com/2021/04/28/single-component-tabs-in-model-driven-forms/ (shows setTabNavigatorVisible(false) + singleComponent auto-hides labels)
- Learn: setTabNavigatorVisible + "Design forms for efficiency" positioning
- WebSearch corroboration: showlabel formxml attr deprecated; setDisplayState hides individual tabs but not the pivot label

## Open questions
- Whether an OnLoad setTabNavigatorVisible(false) causes a brief flash of the pivot before it hides in the dialog (cosmetic; unverified). If flash is unacceptable, singleComponent content-type may render cleaner.
