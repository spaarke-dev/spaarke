---
name: mda-clickable-form-banner-options-2026-09-30
description: Red clickable per-record banner on MDA form — addGlobalNotification (action link, app-wide, persists across nav, no unload event) vs setFormNotification (text-only documented; undocumented 4th param) vs control RECOMMENDATION vs appnotification vs Notify(); recommendation = PCF MessageBar or global+getPageContext cleanup
metadata:
  type: reference
---

## 2026-09-30: Red, clickable, per-record status banner on a model-driven form (UAC-r2 O1, task 153)

**Question**: Which OOB mechanism gives a persistent RED CLICKABLE banner per record (Secure / No Access) on sprk_project/matter/workassignment/organization/contact forms, without plugins?

**Findings**:
- `Xrm.App.addGlobalNotification({type:2, level:2 /*1 Success,2 Error,3 Warning,4 Info*/, message, showCloseButton?, action:{actionLabel, eventHandler}})` → Promise<id>; `Xrm.App.clearGlobalNotification(id)`. Only documented OOB banner with a clickable action (arbitrary JS fn → navigateTo/BFF fine). APP-WIDE bar above command bar, NOT form-scoped; persists across in-app navigation (community, not in docs); multiple fold into a stack (community). Docs date 2022, silent on mobile/Teams/limits.
- No form OnUnload / navigation-away event exists in the Client API events list (form: OnLoad, Loaded, OnSave; data OnLoad). Cleanup options: `Xrm.Utility.getPageContext()` poll (entityId only returned "if specified by the logic that opened the page") + clear-on-next-OnLoad; or PCF `destroy()` (documented lifecycle, but PCF best-practices say don't depend on formContext — calling global Xrm.App from PCF is not documented as supported).
- `setFormNotification(message, level ERROR|WARNING|INFO, uniqueId)` — documented text-only, form-scoped, persists until cleared, auto-goes with form. Undocumented 4th param `[{Label, Handler}]` adds a button (community thread; UNSUPPORTED).
- `control.addNotification({messages:[...], notificationLevel:'ERROR'|'RECOMMENDATION', uniqueId, actions:[{message, actions:[fn]}]})` — field-level icon + flyout with Apply button; Apply only for RECOMMENDATION (not red); only first message shown. Not a banner.
- `appnotification` = per-user bell/toast (Timed 4s / Hidden), polled at app start + nav (>1 min), not record-scoped; URL actions can't run JS (javascript: blocked) — wrong tool for per-record status. Power Fx `Notify(msg, NotificationType.Error, timeout)` — banner, NO actions, behavior formulas only (button click / custom page), can't fire on form load.
- Recommendation: clickable red banner rendered by a PCF (Fluent MessageBar intent=error) on the form = form-scoped, auto-lifecycle, fully supported; plus OOB setFormNotification text as the top-of-form red signal. addGlobalNotification only if owner insists on the top bar — needs page-context cleanup.

**Sources**:
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app/addglobalnotification
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app/clearglobalnotification
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/formcontext-ui/setformnotification
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/controls/addnotification
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/events
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/reference/xrm-utility/getpagecontext
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications (2026-08-12)
- https://learn.microsoft.com/power-platform/power-fx/reference/function-showerror
- https://learn.microsoft.com/power-apps/developer/component-framework/code-components-best-practices (2026-08-26)
- https://www.marius-wodtke.de/post/other/frontend-notifications/ ; community.dynamics.com thread d2f9cb44 (4th param)

**Open questions**: Live-probe needed — global notification behavior on grid nav, multi-stack UI, mobile app and Teams-embedded MDA; whether getPageContext().input.entityId is populated for form opened via sitemap/grid.
