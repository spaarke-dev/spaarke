---
name: naa-web-newoutlook-fallback-redirect-2026-11-03
description: NAA on OWA / new Outlook / Word web - createNestablePublicClientApplication silently falls back to a standard PCA with the SAME config, so a brk-multihub redirectUri in MSAL config causes AADSTS7000471; detection, popup size, WebView2 cookie clearing
metadata:
  type: reference
---

# NAA on web / new Outlook: fallback behaviour, redirect URI, detection (2026-11-03)

**Question**: Is NAA supported on Outlook web / new Outlook for Windows / Word web; how should `detectNaaSupport` work; why did the 2026-09-03 UAT popup fail with AADSTS7000471; how to cut fallback popup size; where new Outlook keeps WebView2 cookies.

## Findings (label: D = documented, V = verified in MSAL source on disk, I = inferred)

1. **Support (D)**: NestedAppAuth 1.1 requirement-set page (ms.date 2026-03-31): Outlook web "Supported"; Outlook Windows M365 = 2409 (18025.20000), retail 2501 (18429.20132), LTSC 2408 (17932.20222); Mac 16.89; mobile v4.2433.0. GA on web/Windows/Mac for Outlook AND Word. Caveats (D): on the web "only for documents opened from SharePoint Online and OneDrive" (SPE docs unconfirmed - I: probably OK, must live-test); not supported in Outlook.com/Gmail mailboxes; no AAD B2C; approved-client-app CA grant breaks NAA (retired Mar 2026). New Outlook: no row in the table, but the MS Outlook-Add-in-SSO-NAA sample README says "Outlook on Windows (new and classic), Mac, mobile, and on the web" (D). No manifest-type or licence prerequisite documented; sample ships both XML and unified; for Outlook you cannot declare NestedAppAuth in the manifest, only check at runtime (D). Word web additionally needs an SPA redirect for the task-pane page, and a login hint from `Office.auth.getAuthContext().userPrincipalName` + `ssoSilent` ("required" per guide, ms.date 2025-12-15) (D).
2. **Detection (D)**: `Office.context.requirements.isSetSupported("NestedAppAuth","1.1")` is the documented check. The MS sample calls `await Office.onReady()` then `createNestablePublicClientApplication` unconditionally and uses isSetSupported only for UI. The repo's `build >= 13530` desktop threshold is stale vs the table (2409/18025).
3. **createNestable fallback (V, msal-browser 3.30.0 and 5.17.1 source in node_modules)**: calls `window.__initializeNestedAppAuth()` if defined, then `window.nestedAppAuthBridge` GetInitContext; on any throw -> `createStandardPublicClientApplication(configuration)` with the SAME configuration object. Standard PCA uses `auth.redirectUri` as given (default = current page URL). `supportsNestedAppAuth` is deprecated and irrelevant to createNestable (V, Configuration.ts).
4. **Redirect URI in bridge mode is ignored (D/V)**: MSAL issue #7590 (v4.0.2, 2025-02) token request shows `redirect_uri=brk-multihub://<page host:port>`, `brk_client_id=<Outlook web app id>`, `brk_redirect_uri=https://outlook.office.com/mail/oauthRedirect.html` irrespective of config. MS official sample sets `redirectUri` to an https `auth.html` (registered as SPA) alongside `brk-multihub://localhost:3000`. MS staff (office-js #5540, 2025-06): "does not need to pass a redirect URI unless you need a fallback". brk-multihub value is origin only, port included (`brk-multihub://localhost:3000`), no path (#5020).
5. **Root cause of 2026-09-03 (I, strong)**: bridge unavailable at creation -> silent standard-PCA fallback carrying `brk-multihub://...` redirect -> Entra refuses reserved scheme on non-brokered request (AADSTS7000471, not in the public error table). Changing localhost -> hostname CANNOT fix this (any brk- value fails in the standard flow). Fix = put an https redirect (`<origin>/auth-callback.html`, registered SPA) in the MSAL config for BOTH paths; keep `brk-multihub://<host[:port]>` registered in Entra only. Why bridge was unavailable is unproven: candidates = Word web doc not from SPO/OneDrive (SPE?), host quirks on web, mailbox type.
6. **Popup size (D/I)**: login_hint / domain_hint / sid are GET query params; they do not change the Cookie header. "Request Too Long" = request-header growth, almost always the cookie jar for login.microsoftonline.com (MS SharePoint troubleshooting says many sign-in attempts add cookies; D for the mechanism, I for this user). Office dialog = separate browser instance but same WebView profile/cookie jar (I) -> no help. `Office.auth.getAccessToken` (IdentityAPI 1.3; Outlook + Word; legacy SSO, WebApplicationInfo + pre-authorised Office client IDs; MS says "NAA preferred", "doesn't work in all scenarios") avoids MSAL popup but changes token audience/BFF contract -> ADR-028 decision, not a quick fix.
7. **New Outlook WebView2 (D/I)**: `%LocalAppData%\Microsoft\olk\EBWebView\Default\` (Cache, IndexedDB, Local Storage, Service Worker, WebStorage) and `%LocalAppData%\Packages\Microsoft.OutlookForWindows_8wekyb3d8bbwe\` (Learn VDI page, ms.date 2026-04-14). Cookies file is standard Chromium `Default\Network\Cookies` (I). MS-documented clear: close Outlook, `olk.exe --devtools`, Network tab > "Clear browser cache" (Learn clear-cache, ms.date 2026-06-12) - that clears HTTP cache, not cookies. Least destructive for cookies (I): DevTools Application > Cookies, delete only login.microsoftonline.com entries (and inspect Cookie header size on the failing 400 first). Heavy: delete `olk\EBWebView` (third-party InLoox doc; clears ALL add-in caches/sign-ins).

## Sources
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in (2025-12-15)
- https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/nested-app-auth-requirement-sets (2026-03-31)
- https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/faq-nested-app-auth-outlook-legacy-tokens (2025-11-03)
- https://learn.microsoft.com/en-us/javascript/api/office/office.auth (getAuthContext, getAccessToken notes)
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/auth-with-office-dialog-api (2026-01-08)
- https://learn.microsoft.com/en-us/office/dev/add-ins/testing/clear-cache (2026-06-12)
- https://learn.microsoft.com/microsoft-365-apps/outlook/get-started/virtualized-desktop-infrastructure (2026-04-14)
- https://github.com/OfficeDev/Office-Add-in-samples/tree/main/Samples/auth/Outlook-Add-in-SSO-NAA (authConfig.ts, msalconfig.ts, README)
- MSAL source: src/client/office-addins/node_modules/@azure/msal-browser (3.30.0) and src/solutions/EmailPage/node_modules/@azure/msal-browser (5.17.1): app/PublicClientApplication.ts, operatingcontext/NestedAppOperatingContext.ts, naa/BridgeProxy.ts
- GitHub: AzureAD/msal-js #7590, #7280; OfficeDev/office-js #5540, #5020, #5916, #6707, #5840, #6610

## Open questions
- Does Word web count SPE documents as "SharePoint Online" for NAA? Why did the bridge not engage on Outlook web in the 09-03 UAT? (capture MSAL verbose log line "Nested App Auth Bridge available: true|false" + `isSetSupported` result on each host)
- Does `Office.context.diagnostics.platform` really report OfficeOnline for new Outlook (caller says yes; not documented)?
- Exact DevTools cookie-clearing path in new Outlook (popup target visibility) is untested.
- NAA silent for B2B guests / multi-tenant authority still unconfirmed (see b2b-guest-vs-multitenant-home-authority-2026-10-13).
