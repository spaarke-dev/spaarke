---
name: outlook-desktop-unified-button-inert-2026-10-06
description: Outlook desktop (classic/new) ribbon button does nothing after Spaarke moved to ONE unified Outlook+Word package (1.1.1) and removed both XML add-ins — ranked causes, button-set fingerprint to tell XML vs unified, cache/diagnostic steps, code.script 404 latent defect.
metadata:
  type: project
---

# Outlook desktop: unified-package button inert (2026-10-06)

**Question**: Owner UAT 2026-10-06 — unified package `e68f3cb1-…` v1.1.1 (Outlook+Word, schema 1.30) opens in Word desktop/web and OWA, but clicking the button in Outlook desktop does nothing. Why?

**Findings**:
- Platform floor (unified-manifest-overview, ms.date 2026-09-24): classic Outlook Win **Version 2307 (Build 16626.20132)+ on M365 subscription**; new Outlook Yes; perpetual Office No; Outlook Mac No. Admin doc (manage-deployment-of-add-ins, 2026-03-16): add-ins "can take **24-72 hours** to appear on the ribbon"; relaunch needed. Owner removed both XML add-ins 2026-10-03 → still inside that window on 10-06.
- **Fingerprint (repo-verified)**: XML Outlook (`5e4d66d0`) read ribbon = *Save to Spaarke* + *Create To Do*; compose = *Save to Spaarke* only. Unified read = + **Quick Save**; compose = **Share from Spaarke** + **Grant Access**. Both groups labelled "Spaarke" — the button set is the only way to tell which manifest the client rendered.
- Classic Outlook cache: delete `%LOCALAPPDATA%\Microsoft\Office\16.0\Wef\` contents AND (unified) `%userprofile%\AppData\Local\Microsoft\Outlook\HubAppFileCache` (clear-cache, 2026-06-12). New Outlook: `olk.exe --devtools` → Network → Clear browser cache. Auto-clear checkbox is NOT available for Outlook.
- Diagnostics: runtime logging `npx office-addin-dev-settings runtime-log --enable C:\temp\addin.txt` (host-level manifest/load errors; search SolutionId); `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--auto-open-devtools-for-tabs` (DevTools opens if the pane's WebView2 starts at all); new Outlook only via `olk.exe --devtools`.
- Open bug **office-js #6768** (2026-06-18): unified-manifest Outlook task-pane button renders but click is a no-op (zero network) in OWA/new Outlook, M365 org, LOB app-catalog deploy; works in classic. Related closed #6471 (stops after hours; curly-brace XML Id fix — N/A to JSON).
- Classic Outlook shows "This add-in could not be started" if the first task-pane request is non-200 (Q&A 4761768, 2025-09). Live probe 2026-10-06: taskpane.html (+?action=createTodo), commands.html, icons all 200.
- **Latent defect**: CommandRuntime `code.script` → `/outlook/commands.js` and `/word/commands.js` **404** (webpack emits `commands.bundle.js`; source `outlook/manifest.json:84`, `word/manifest.json:78`). Runtimes doc: function commands use the browser runtime (code.page) on every platform; JS-only runtime (script) only for Outlook event-based/spam-reporting in classic Win. So not the cause, but remove `script` (no autoRunEvents) or point it at a real file.
- `view` on openPage "isn't supported in Outlook" (actions-item schema, 2026-09-30) — absence is fine. Filtering of nested-requirements nodes happens at install source (admin center), per client.

**Sources**: learn.microsoft.com/office/dev/add-ins/develop/unified-manifest-overview; .../testing/clear-cache; .../testing/runtime-logging; .../testing/debug-add-ins-using-devtools-edge-chromium; .../testing/runtimes; .../develop/requirements-property-unified-manifest; .../develop/create-addin-commands-unified-manifest; learn.microsoft.com/microsoft-365/extensibility/schema/extension-runtimes-actions-item; learn.microsoft.com/microsoft-365/admin/manage/manage-deployment-of-add-ins; github.com/OfficeDev/office-js/issues/6768, /6471; learn.microsoft.com/answers/questions/4761768; repo dist/spaarke/manifest.json, outlook/outlook-manifest.xml, packaging/mergeUnifiedManifest.js.

**Open questions**: owner's Outlook variant + build; which button set is shown; whether classic Outlook's in-client JSON→XML converter tolerates a package carrying a `Document.*` RSC permission (undocumented); whether a mail+document multi-host package has any classic-Outlook-specific defect (no doc or issue found).

Related: [[word-unified-manifest-ga-status-2026-09-30]]
