---
name: office-addin-launch-ms-word-uri-2026-10-03
description: How an Office.js task pane can launch an ms-word: Office URI to open an SPE file in desktop Word; openBrowserWindow rejects non-http(s); platform detection via Office.context.platform.
metadata:
  type: reference
---

# Launching ms-word: Office URI from an add-in task pane (2026-10-03)

**Question**: Can openBrowserWindow / window.open launch `ms-word:` from a Word/Outlook task pane, how to detect desktop vs web, and which to use?

**Findings**:
- DOCUMENTED: `openBrowserWindow(url)` param = "full URL ... including protocol (http or https)... Other protocols like mailto aren't supported." Issue #2820 (ms-excel:ofe via openBrowserWindow, Mac) closed "Resolution: by design".
- DOCUMENTED: OpenBrowserWindowApi 1.1 is **Not supported** on Office on the web and new Outlook; only Excel/PPT/Word on Win/Mac/iPad + classic Outlook Win/Mac (req-set page 2025-10-17). Outlook restricted-permission add-ins see it undefined (#2261 won't fix).
- No MS doc covers window.open / anchor-click of custom protocols from a task pane. Community evidence: #6926 (2026-09, Win Word 2608) anchor `link.click()` with `ms-word:ofv|u|` from an add-in DID launch desktop Word (bug was only that ofv opened editable). #6211 (2025-10) ms-word: on Mac works only first time.
- DOCUMENTED: `Office.context.platform` (and `Office.context.diagnostics.platform`, and `Office.onReady` info.platform) -> `Office.PlatformType`: PC, Mac, OfficeOnline, iOS, Android, Universal. Outlook web + new Outlook Windows return OfficeOnline.
- Repo note: `DesktopUrlBuilder.FromMime` emits ABBREVIATED `ms-word:https://...` (doc says abbreviated implies ofv); comment says ofe|u| blocked for SPE contentstorage (Restricted Sites zone).

**Sources**:
- https://learn.microsoft.com/en-us/javascript/api/office/office.ui (openBrowserWindow)
- https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/open-browser-window-api-requirement-sets
- https://learn.microsoft.com/en-us/javascript/api/office/office.context ; .../office.platformtype
- https://learn.microsoft.com/en-us/office/client-developer/office-uri-schemes
- https://github.com/OfficeDev/office-js/issues/2820 , /6926 , /6211 , /2261 , /3084

**Open questions**: Live probe of anchor-click `ms-word:` from WebView2 (Win), WKWebView (Mac), and the Outlook pane; whether new Outlook (WebView2-hosted web app) honours protocol launch.
