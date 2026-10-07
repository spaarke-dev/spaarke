---
name: office-addin-taskpane-width-2026-10-04
description: Can a Word add-in set task pane width? YES at runtime via Office.extensionLifeCycle.taskpane.setWidth (TaskPaneApi 1.1, released); manifest preferredWidth is m365-app-prev only; RequestedWidth is content-add-in only
metadata:
  type: reference
---

## 2026-10-04: Word task pane initial/min/max width
**Question**: Is there any supported way for a Word add-in to set/request task pane default width (manifest XML/unified, Office.js)?
**Findings**:
- YES (runtime): `Office.extensionLifeCycle.taskpane.setWidth(px)` — requirement set **TaskPaneApi 1.1**, documented in the RELEASED `office_release` docs set (common-js). Support: Word + Excel on web; Windows Version 2507 (Build 19029.20004)+; Mac 16.100.4 (Build 25083118)+; NOT iOS; PowerPoint web not supported. Returns void, synchronous.
- Constraints (Learn, Office.TaskPane page updated 2026-08-21): Word web 330–500 px; Windows 86 px–50% of client window; Mac 270 px–50%. Defaults: Word web 330, Windows 320, Mac 270. Out-of-range → **silently not resized, no error**. Older doc text said Windows/Mac min 51 px (superseded).
- Manifest: XML `<RequestedWidth>`/`<RequestedHeight>` under `<DefaultSettings>` = **Content add-ins only** (DefaultSettings table: TaskPane = No). Unified `contentRuntimes.requestedWidth` = content add-ins (32–1000). Unified `runtimes.actions[].taskpane.preferredWidth` exists **only in m365-app-prev (preview schema)**, not 1.30 — "treated as a hint and may be adjusted or ignored by the host. User-resized widths always take precedence."
- No min/max width setting anywhere. `Office.addin.showAsTaskpane()` takes no args. Design guidance page lists Word task pane 329x445 at 1366x768 (descriptive only).
- Spaarke Word add-in (`src/client/office-addins/`) had no setWidth/TaskPaneApi usage as of 2026-10-04.
**Sources**:
- https://learn.microsoft.com/en-us/javascript/api/office/office.taskpane
- https://learn.microsoft.com/en-us/javascript/api/office/office.extensionlifecycle?view=common-js
- https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/task-pane-api-requirement-sets
- https://learn.microsoft.com/en-us/javascript/api/manifest/defaultsettings ; .../manifest/requestedwidth
- https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/extension-runtimes-actions-item-taskpane (prev only)
- https://learn.microsoft.com/en-us/office/dev/add-ins/design/task-pane-add-ins
**Open questions**: Does setWidth persist across sessions or is it overridden by user-resized width? Behavior when called before pane is visible? Not documented — needs a live probe. When does preferredWidth reach a GA schema version?
