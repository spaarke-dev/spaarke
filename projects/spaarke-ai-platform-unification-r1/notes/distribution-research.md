# Distribution Research: Standalone Access, PWA, and Teams Tab

> **Created**: 2026-05-16
> **Purpose**: Inform decisions about how to surface `sprk_spaarkeai` outside the model-driven app shell — standalone direct access, PWA installation, and Teams personal tab embedding.
> **Context**: `sprk_spaarkeai` is a React 19 Code Page (Vite single-file build, Fluent v9, `@spaarke/auth` bootstrap, BFF API at `spe-api-dev-67e2xz.azurewebsites.net`).

---

## Topic 1: Custom Page vs Web Resource for Standalone Access

### What "standalone" means for `sprk_spaarkeai`

"Standalone" means surfacing the AI Code Page outside the normal MDA navigation context — either as a full-page experience launched from a command bar, a deep link, or a direct URL. The Code Page already exists as a Dataverse web resource (`sprk_spaarkeai`). The question is which hosting mechanism provides the best auth story and UX.

### Option A: Web Resource opened via `Xrm.Navigation.navigateTo` (current approach, in-app)

`Xrm.Navigation.navigateTo` with `pageType: "webresource"` opens the HTML web resource either inline or as a center/side dialog within the model-driven app shell. This is what task 041-launch-points.poml targets for the workspace command bar launch point.

**Xrm / context availability inside navigateTo dialog:**
- The `Xrm` object is NOT natively available in HTML web resources. The doc is explicit: "scripts containing `Xrm.*` methods aren't supported in HTML web resources."
- `parent.Xrm.*` works only when the web resource is loaded as a form embedded resource (not as a sitemap page or navigateTo target in all cases).
- For standalone context info (org URL, user info), the web resource must include `<script src="ClientGlobalContext.js.aspx">` and call `GetGlobalContext()` — this returns the same object as `Xrm.Utility.getGlobalContext()`.
- `Xrm.WebApi` is NOT available inside HTML web resources via this mechanism.
- **For `sprk_spaarkeai` this is irrelevant**: the Code Page does not use Xrm directly. It uses the `@spaarke/auth` bootstrap with MSAL.js to call the BFF API. The org URL, record ID, and entity name are passed as query string parameters (`?matterId=`, `?documentId=`) by the launch point script, not extracted from Xrm inside the web resource.

**Auth token flow in navigateTo dialog:**
- The web resource is rendered in an iframe within the Unified Interface shell. The Dataverse MDA now runs in a single iframe context (Unified Interface), so the web resource itself is a nested iframe.
- The `@spaarke/auth` bootstrap uses MSAL.js redirect flow or silent/popup acquisition. In a nested iframe, redirect-based auth is problematic because `window.location` manipulation by the MDA shell can interfere (confirmed by Hajek research: "if you used redirect, the user could lose context of the window").
- **Recommendation**: Use `acquireTokenSilent` with fallback to `acquireTokenPopup` (never redirect) in the Code Page when it runs inside a navigateTo dialog. The `@spaarke/auth` bootstrap already uses this pattern for Code Pages.
- Third-party cookie blocking: In modern browsers without third-party cookies (Chrome post-cookie deprecation, Safari), `acquireTokenSilent` may fail the first time. The popup fallback handles this. In Microsoft Edge with an Entra-authenticated work session, the auth broker injects tokens via SSO headers, bypassing cookie requirements — this is the common enterprise case.

**Rendering mode:**
- `target: 2` (dialog) is the standard pattern for the workspace command bar launch. Width/height are configurable (percentage or pixel).
- `target: 1` (inline, replacing the current page) is also valid but loses the ability for the user to navigate back to where they were.

**Verdict for in-app launch**: Solid, well-supported approach. This is what task 041 implements and it remains the right choice for the within-MDA launch points.

---

### Option B: Direct URL access (`https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai`)

The web resource is accessible at a direct Dataverse URL without opening the MDA shell. This URL requires the user to be authenticated with Dataverse (session cookie from a prior MDA login, or interactive browser auth).

**Key characteristics:**
- The page loads standalone in the browser — no parent MDA frame, no `parent.Xrm`, no `ClientGlobalContext.js.aspx` inheritance.
- The `@spaarke/auth` MSAL bootstrap runs in the top-level frame — redirect auth works correctly here (no nested iframe issue).
- `acquireTokenSilent` should succeed if the user has an active Entra session; if not, a redirect to the Entra login page and back completes the flow.
- Entity context must come from URL query params (`?matterId=`, `?documentId=`). The Code Page's `StandaloneAiContext` already reads these from `window.location.search` (per FR-02), so this works without modification.
- The version-stamped URL (`/{version}/WebResources/sprk_spaarkeai`) ensures cache correctness after publish.

**Limitation**: Direct URL requires Dataverse licensing and the user must have web resource access rights. This is appropriate for enterprise internal use, not public-facing scenarios.

**Verdict**: Direct URL access works well for deep-link scenarios (email links, M365 handoff). The `@spaarke/auth` bootstrap is designed for this pattern. No additional work required beyond what task 041 already targets.

---

### Option C: Custom Page (Canvas App page in MDA)

A Custom Page is a canvas-app-based page type added to the model-driven app site map. It provides native MDA navigation integration with Power Fx expressions, canvas connectors, and richer layout flexibility than a web resource.

**Why it is NOT the right choice for `sprk_spaarkeai`:**

1. **Technology mismatch**: Custom Pages are built in Power Apps Studio with Power Fx. `sprk_spaarkeai` is a React 19 TypeScript application with hundreds of existing components, shared libraries, Fluent v9 design system, and BFF API integration. Rebuilding in Power Fx canvas is not feasible.

2. **PCF components inside Custom Pages**: Custom Pages can host PCF controls, but the Code Page codebase is NOT a PCF component — it uses React 19 APIs (bundled, `createRoot`) which are incompatible with PCF (React 16/17, platform-provided). Wrapping the entire application as a PCF would violate ADR-022.

3. **Critical known issue — third-party cookies**: Microsoft explicitly documents: "Custom pages require third-party cookies to be enabled, which is required by the canvas app runtime." As third-party cookie support continues to shrink, this is a structural reliability problem. iOS devices require users to explicitly enable "Allow cross site tracking" (which device management may prohibit).

4. **Session timeout**: Custom pages use a canvas app hosting session that times out after 8 hours, independent of the Unified Interface session. This creates a poor UX for a persistent AI chat interface.

5. **Direct URL access**: There is no stable public URL for a custom page standalone. Community questions confirm this is a pain point. Custom pages are accessed only through the MDA shell navigation.

6. **Connector limit**: "The number of connectors in a model-driven app, across all custom pages, shouldn't exceed 10." This global limit applies to the entire app, not just one page.

7. **Preview limitations**: Custom page in Teams model-driven app is in Public Preview only (as of March 2026 docs). Mobile online is also in Public Preview.

**Verdict**: Custom Page is not viable for `sprk_spaarkeai`. Stick with the Code Page (web resource) approach. The existing architecture is correct.

---

### Summary Comparison

| Dimension | navigateTo (dialog) | Direct URL | Custom Page |
|-----------|---------------------|------------|-------------|
| Technology fit | Excellent (existing Code Page) | Excellent | None (canvas/Power Fx) |
| Auth flow | Silent + popup MSAL (no redirect) | Full MSAL with redirect | Requires 3rd-party cookies |
| Xrm.WebApi | Not needed (BFF API) | Not needed | N/A |
| GetGlobalContext | Not needed (params via query string) | Not needed | N/A |
| Deep link / direct URL | No (MDA shell required) | Yes | No |
| Teams embed | Possible (see Topic 3) | Yes | Preview only, retiring May 2026 |
| Third-party cookie risk | Low (popup fallback) | None (top-frame) | HIGH (required, no fallback) |
| Recommendation | Use for in-app launch points | Use for deep links, M365 handoff | Do not use |

### Recommendation for `sprk_spaarkeai`

Maintain the existing web resource (Code Page) architecture. For in-MDA launch points (task 041), use `Xrm.Navigation.navigateTo` with `pageType: "webresource"`. For deep links and M365 Copilot handoff, use the direct Dataverse URL with entity context in query params. Ensure the `@spaarke/auth` bootstrap never uses redirect flow when it detects it is running inside an iframe (`window.parent !== window`); use `acquireTokenPopup` as the fallback.

---

## Topic 2: PWA (Progressive Web App)

### Can the model-driven app itself be a PWA?

No. As of May 2026, Microsoft model-driven apps do not support PWA installation. The 2026 Wave 1 release plan confirms no PWA feature for model-driven apps — the wave focuses on modern look GA, search improvements, and offline canvas apps (not model-driven). There is no `manifest.json` or service worker generated by the MDA runtime.

The GitHub project "PowerProgressive" (community-built) demonstrates a workaround: wrap the Power Apps URL in an iframe inside a custom PWA shell. This is unsupported and fragile.

**Power Apps for Windows**: Microsoft offers a native Windows Store app for running Power Apps (canvas and model-driven) on Windows. This is not a PWA — it is a packaged Electron-like application. It provides a somewhat better desktop experience than browser tabs but lacks PWA-specific capabilities (push notifications, offline, installability from the web).

### PWA for Power Pages (not model-driven apps)

Power Pages (the portal/public-facing product) does support PWA natively via the Set up workspace in Power Pages Design Studio. Features include:
- Install from browser to home screen on Android/iOS/Windows/Chrome
- Offline mode (specific pages can be made read-only offline)
- App store distribution via PWABuilder packaging

This is completely separate from model-driven apps and is not applicable to `sprk_spaarkeai`.

### Can `sprk_spaarkeai` be a standalone PWA if deployed to Azure Static Web Apps?

Yes — this is the viable PWA path. If the Code Page is extracted from Dataverse and deployed as a standalone React SPA to Azure Static Web Apps (SWA), it can have a full PWA implementation:

- `manifest.json` with `"display": "standalone"`, icons, theme colors, `start_url`
- Service worker for asset caching (Workbox-generated)
- "Add to Home Screen" / "Install" prompt in browsers

**Auth considerations for a SWA-hosted PWA:**
- MSAL.js runs in the top-level frame — redirect and popup auth both work.
- SWA built-in authentication (EasyAuth) conflicts with MSAL.js: do NOT enable SWA's built-in Microsoft Entra authentication if using MSAL.js directly — they interfere. Use MSAL.js only.
- The BFF API token flow remains identical: MSAL acquires Entra token → sent as Bearer to `spe-api-dev-67e2xz.azurewebsites.net`.

**Service worker and auth tokens:**
- Service workers must NOT cache auth tokens or Authorization headers. Configure the Workbox fetch handler to use `NetworkOnly` for all BFF API requests (`/api/*` pattern).
- Static assets (JS bundles, CSS) can be cached normally with a cache-first strategy.
- Be aware: the Vite single-file build for Dataverse web resources produces one large inline HTML file. For a SWA-hosted PWA, use the normal Vite build (separate JS/CSS chunks) to enable effective asset caching by the service worker.

**Dataverse session consideration:**
- If the SWA-hosted PWA calls Dataverse Web API directly (not just the BFF), the Entra token must have the Dataverse resource scope (`https://spaarkedev1.crm.dynamics.com/.default`). For `sprk_spaarkeai`, all Dataverse access goes through the BFF API, so this is not a concern at the SPA level.

**Trade-offs of SWA deployment vs Dataverse web resource:**

| | Dataverse Web Resource | Azure Static Web App (PWA) |
|--|------------------------|---------------------------|
| Deployment | `pac pcf push` / solution import | `az staticwebapp` / GitHub Actions |
| Auth | MSAL in iframe/frame (popup fallback) | MSAL top-frame (redirect or popup) |
| PWA support | None | Full (manifest, service worker, install) |
| Offline support | None | Possible with service worker |
| Dataverse security | Web resource access rights | Entra token + BFF authorization |
| ALM / solutions | Included in solution | Separate infrastructure |
| URL | `...crm.dynamics.com/WebResources/sprk_spaarkeai` | `https://spaarke-ai.azurestaticapps.net` |
| Teams tab | Possible (see Topic 3) | Yes (contentUrl points to SWA URL) |

### Recommendation for `sprk_spaarkeai` PWA

**For R1 scope**: PWA is not required and is out of scope. The Dataverse web resource deployment meets R1 requirements.

**If PWA is a future requirement** (desktop install, offline mode, push notifications), the recommended path is to deploy the Code Page to Azure Static Web Apps alongside the existing Dataverse web resource deployment — the same Vite build with a different output configuration. The SWA-hosted version becomes the PWA entry point while the Dataverse web resource continues to serve the in-MDA launch points.

Do not attempt to retrofit a service worker or `manifest.json` onto the Dataverse-hosted web resource — it is not technically possible (Dataverse controls the HTTP response headers; Content-Security-Policy set by Dataverse will block service worker registration).

---

## Topic 3: Teams Tab for the `sprk_spaarkeai` Web Resource

### Critical: Model-driven app embedding in Teams is retiring

As of May 1, 2026, Microsoft retired the Power Apps Teams tab feature that let model-driven apps be pinned directly as Teams channel tabs via the Power Apps tab integration. The official documentation now reads: "This feature is deprioritized and will not be delivered. New model-driven apps can no longer be added to Teams. Model-driven apps that are embedded in Teams will continue to function until May 1, 2026. After that date, these apps will no longer work within the Teams experience."

This means the historic approach of pinning a model-driven app as a Teams tab using the built-in Power Apps integration is dead. The alternative is to build a custom Teams personal tab app that embeds the web resource (or SWA-hosted) URL.

### Building a custom Teams personal tab for `sprk_spaarkeai`

A Teams personal tab is an iframe-based web app registered in a Teams app manifest. The `contentUrl` points to the hosted app (Dataverse web resource URL or SWA URL). This is the correct approach for surfacing `sprk_spaarkeai` in Teams.

**Teams app manifest configuration (static tab):**

```json
{
  "staticTabs": [
    {
      "entityId": "spaarkeai",
      "name": "Spaarke AI",
      "contentUrl": "https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai",
      "websiteUrl": "https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai",
      "scopes": ["personal"]
    }
  ],
  "webApplicationInfo": {
    "id": "<entra-app-client-id>",
    "resource": "api://<domain>/<client-id>"
  }
}
```

Use `staticTabs` (personal scope) for a persistent, non-configurable entry point. Use `configurableTabs` if the tab needs to be added to specific channels with per-channel configuration (not needed for the AI chat use case).

**Recommended: Teams Toolkit (now Microsoft 365 Agents Toolkit) vs manual manifest**

As of 2026, Teams Toolkit has been renamed to Microsoft 365 Agents Toolkit (v5 in VS Code). For Spaarke's scenario (existing React app to be embedded, not a greenfield Teams-first app), manual manifest configuration is more appropriate than Agents Toolkit scaffolding, which generates a new project structure. Use the Teams Developer Portal (`https://dev.teams.microsoft.com`) to configure and upload the app manifest.

The `@microsoft/teams-js` (TeamsJS) SDK v2.19.0 or later is required for new app submissions as of 2026. It must be included in the Code Page for auth and Teams context access.

---

### SSO authentication flow for the Teams tab

When `sprk_spaarkeai` runs as a Teams personal tab, it runs inside a Teams iframe. The standard Teams SSO flow is:

```
1. Page loads → microsoftTeams.app.initialize()
2. Call microsoftTeams.authentication.getAuthToken()
   → Teams silently acquires a Teams-scoped Entra token for the user
3. The Teams token is sent to the BFF API as a Bearer token
4. BFF validates the Teams token and uses OBO (On-Behalf-Of) flow:
   → Exchange Teams token for a Dataverse-scoped token
   → Use the Dataverse token for any Dataverse API calls
5. Or alternatively: use NAA (see below) to acquire tokens directly
```

**Option A: Traditional Teams SSO with OBO (getAuthToken + server-side exchange)**

The `getAuthToken()` API in TeamsJS acquires a token scoped to your Entra app registration. This token has limited permissions (`email`, `profile`, `offline_access`, `openid`) — it is NOT a full Dataverse or Graph access token. To call the BFF API or Dataverse, the server must exchange this token via OBO flow.

Steps:
1. Add `@microsoft/teams-js` to `sprk_spaarkeai` package.
2. In the Code Page bootstrap, before calling `ensureAuthInitialized()`, detect Teams context: `microsoftTeams.app.initialize()`.
3. If in Teams, call `getAuthToken()` to get the Teams identity token.
4. Send this token to the BFF in the Authorization header.
5. BFF uses `IConfidentialClientApplication.AcquireTokenOnBehalfOf()` to exchange it for a token with the correct scopes for Dataverse or downstream APIs.

OBO requires the BFF to have the client secret configured and the Teams Entra app pre-authorized. This is additional server-side complexity.

**Option B: Nested App Authentication (NAA) — preferred for new development**

NAA is a newer protocol (generally available as of 2026) that eliminates the need for the `getAuthToken` + OBO server-side exchange entirely. Instead, MSAL.js itself acts as the token broker, acquiring tokens directly with the correct scopes. Teams hosts the broker.

Key differences from OBO:
- No server-side token exchange needed
- MSAL.js calls `createNestablePublicClientApplication()` instead of the standard `PublicClientApplication`
- A `brk-multihub://<your-domain>` redirect URI is registered on the Entra app
- Tokens are acquired with the correct scopes (including Dataverse) directly from the client
- Works across Teams, Outlook, and Microsoft 365

**NAA MSAL.js configuration:**

```typescript
import { createNestablePublicClientApplication } from "@azure/msal-browser";

const msalConfig = {
  auth: {
    clientId: "your-client-id",
    authority: "https://login.microsoftonline.com/your-tenant-id",
    supportsNestedAppAuth: true
  }
};

const pca = await createNestablePublicClientApplication(msalConfig);
```

**NAA Entra app registration requirement:**
- Add a SPA redirect URI of type: `brk-multihub://spaarkedev1.crm.dynamics.com` (or the SWA domain if hosted there)
- No need to pre-authorize Teams client app IDs
- No need to expose an API or define custom scopes for the token exchange

**NAA limitations:**
- Not available in Power Apps / model-driven app PCF contexts (Teams-only and some Office hosts)
- Not available for non-Entra identity providers
- Requires MSAL.js `@azure/msal-browser` 3.x or later
- The current `@spaarke/auth` bootstrap uses standard `PublicClientApplication` — a Teams-aware code path would need to detect Teams context and switch to `createNestablePublicClientApplication`

**NAA is the recommended approach for the Teams tab** if this distribution channel is pursued. It is simpler, has no server-side OBO complexity, and is the strategic direction Microsoft is investing in.

---

### Third-party cookie issues in the Teams iframe

Teams loads tab content in an iframe. As of May 2026:

- **Desktop Teams (Windows/Mac)**: Works without third-party cookies because Teams uses a browser-based rendering engine (Edge WebView2) where the Microsoft Entra auth cookie is considered first-party within the Teams app boundary. SSO via `getAuthToken()` works silently.
- **Teams Web (browser)**: The Teams web client runs in a browser where the app content is a cross-origin iframe. Third-party cookies are blocked in Safari and progressively in Chrome. `getAuthToken()` still works (it uses a broker mechanism, not iframe cookies), but MSAL.js popup or redirect for MSAL without NAA may fail.
- **iOS Teams**: Apple's third-party cookie blocking is enforced. Apps that rely on cookies for auth in iframe tabs cannot complete authentication on iOS Teams. This is why NAA was created — NAA bypasses the cookie problem entirely.
- **MSAL redirect in Teams iframe**: MSAL.js redirect (`loginRedirect`) is explicitly unsupported in iframes: "Redirects aren't supported for iframes or brokered apps." Only popup (`loginPopup`) or silent (`acquireTokenSilent`) should be used. NAA's broker mechanism is the modern equivalent.

**Conclusion on cookies**: NAA avoids all cookie-related auth issues in Teams iframes. If NAA is not used (OBO approach), configure MSAL.js to use popup-only auth in Teams context and never redirect.

---

### `@microsoft/teams-js` SDK integration with the Code Page

The Code Page needs minimal TeamsJS SDK integration:

```typescript
import * as microsoftTeams from "@microsoft/teams-js";

// In main.tsx bootstrap, before auth initialization:
async function bootstrap() {
  // Detect if running inside Teams
  const isInTeams = window.name === "embedded-page-container"
    || window.parent !== window;

  if (isInTeams) {
    try {
      await microsoftTeams.app.initialize();
      const context = await microsoftTeams.app.getContext();
      // context.user.loginHint contains the user's UPN for account matching
    } catch {
      // Not in Teams, continue with standard MSAL flow
    }
  }

  // Proceed with existing @spaarke/auth bootstrap
  await resolveRuntimeConfig();
  await ensureAuthInitialized(); // needs to use NAA-aware MSAL if in Teams
  // render()
}
```

For NAA, the detection logic changes: `microsoftTeams.app.initialize()` signals Teams to act as the auth broker, and MSAL's `createNestablePublicClientApplication` handles the rest.

---

### Static tab vs configurable tab

| | Static Tab | Configurable Tab |
|--|------------|-----------------|
| Pinning scope | Personal app (left rail) | Channel/group chat |
| Configuration | Fixed in manifest | Users configure per-channel |
| Use case | Always-available personal AI | Per-channel AI contexts |
| Complexity | Low | Medium (requires config page) |

For `sprk_spaarkeai`, **static tab with personal scope** is correct. The AI chat is a personal workflow tool, not a channel-level resource. If future requirements include per-channel AI contexts (e.g., "this channel is scoped to matter X"), a configurable tab can be added later alongside the personal tab.

---

### Dataverse web resource URL in Teams iframe: X-Frame-Options

Dataverse web resources must allow embedding in the Teams iframe. Dataverse Unified Interface currently does not set a restrictive `X-Frame-Options: DENY` on web resource URLs — web resources are designed to be embeddable. However, always test this in the target environment, as tenant-level policies or network security configurations could add frame-blocking headers.

If the Dataverse URL does not work in Teams (frame denied), the SWA-hosted version is the fallback — Azure Static Web Apps allows full control over response headers including `Content-Security-Policy` and `X-Frame-Options`.

---

### Recommended Teams tab implementation path

1. **Use the Dataverse web resource URL** as `contentUrl` if the URL is accessible from Teams clients (enterprise deployment, all users Dataverse-licensed).
2. **Alternatively, use an SWA-hosted URL** for teams tab if the org needs external users or wants PWA capabilities as well (SWA gives full header control, custom domain, PWA support).
3. **Integrate `@microsoft/teams-js` into the Code Page** for Teams context detection and auth.
4. **Implement NAA** (`createNestablePublicClientApplication`) as the Teams-specific auth path. Keep the existing MSAL `PublicClientApplication` path for all non-Teams contexts (direct URL, in-MDA navigateTo).
5. **Add the Teams app manifest** via the Developer Portal, using a static tab with personal scope.
6. **Do not use the Power Apps Teams tab integration** — it was retired May 2026.
7. **Token exchange for the BFF**: With NAA, MSAL acquires tokens with the BFF API scope directly. The BFF does not need OBO changes. With traditional SSO, OBO must be implemented in the BFF.

**NAA advantage summary for `sprk_spaarkeai` specifically:**
- No server-side OBO complexity in the BFF
- Works on iOS Teams (no third-party cookie dependency)
- Uses the same MSAL.js library already in `@spaarke/auth`
- Tokens can include the Dataverse scope directly (`https://spaarkedev1.crm.dynamics.com/.default`)
- The existing BFF auth validation (endpoint filters, Entra token validation) works unchanged

---

## Consolidated Recommendations

| Distribution Channel | Approach | Auth | Priority |
|----------------------|----------|------|----------|
| In-MDA launch (workspace command bar, entity form) | `Xrm.Navigation.navigateTo` with `pageType: "webresource"` | MSAL silent + popup (no redirect) | R1 — task 041 |
| Deep link / email / M365 handoff | Direct Dataverse URL with query params | Full MSAL with redirect (top-frame) | R1 — task 041 |
| Custom Page | Do not use | — | Not applicable |
| PWA (desktop install, offline) | Azure Static Web Apps deployment + manifest.json + service worker | Full MSAL with redirect | Future (not R1) |
| Teams personal tab | Custom Teams app manifest, static tab, contentUrl = Dataverse URL or SWA | NAA (preferred) or TeamsJS getAuthToken + BFF OBO | Future (not R1) |
| Teams channel tab via Power Apps tab | Retired May 2026 | — | Do not pursue |

### Key technical decisions if Teams tab is built

1. Add `@microsoft/teams-js` v2.19.0+ to `sprk_spaarkeai`.
2. Implement a Teams context detection gate in `main.tsx` before the `@spaarke/auth` bootstrap.
3. For NAA: register `brk-multihub://spaarkedev1.crm.dynamics.com` redirect URI on the Spaarke Entra app registration; switch MSAL initialization to `createNestablePublicClientApplication` in Teams context.
4. For OBO fallback: implement `AcquireTokenOnBehalfOf` in the BFF with the Teams Entra app pre-authorized.
5. Never use `loginRedirect` when `window.parent !== window`.
6. Verify Dataverse web resource URL is not blocked by X-Frame-Options before committing to that contentUrl; have SWA fallback ready.

---

## Sources

- [Web Resources in Model-Driven Apps](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/web-resources)
- [Custom Page Overview](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-overview)
- [Custom Page Known Issues](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-issues)
- [navigateTo Client API reference](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto)
- [GetGlobalContext and ClientGlobalContext.js.aspx](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/getglobalcontext-clientglobalcontext.js.aspx)
- [Power Apps 2026 Release Wave 1 Overview](https://learn.microsoft.com/en-us/power-platform/release-plan/2026wave1/power-apps/)
- [Power Pages PWA Overview](https://learn.microsoft.com/en-us/power-pages/configure/progressive-web-apps)
- [Teams Tab SSO Overview](https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/tab-sso-overview)
- [Nested App Authentication (NAA)](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication)
- [Methods to Build Teams Tab App](https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/create-personal-tab)
- [Embed model-driven app as Teams tab (retiring May 2026)](https://learn.microsoft.com/en-us/power-apps/teams/embed-model-driven-teams-tab)
- [Using Entra Authentication in Power Apps PCFs and Client Scripts (Hajek, April 2025)](https://hajekj.net/2025/04/28/using-entra-authentication-in-power-apps-pcfs-and-client-scripts/)
- [Teams Tab on Azure Static Web Apps with SSO (February 2026)](https://oneuptime.com/blog/post/2026-02-16-how-to-deploy-a-teams-tab-application-hosted-on-azure-static-web-apps-with-sso/view)
- [How to handle third-party cookie blocking (Microsoft Entra)](https://learn.microsoft.com/en-us/entra/identity-platform/reference-third-party-cookies-spas)
