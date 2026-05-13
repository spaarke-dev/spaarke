# EXTERNAL ACCESS SPA — DEVELOPER GUIDE

> **Audience**: Engineers building or extending the Secure Project Workspace SPA
> **Last Updated**: 2026-03-19
> **Applies To**: `src/client/external-spa/`
> **Architecture Reference**: [`docs/architecture/external-access-spa-architecture.md`](../architecture/external-access-spa-architecture.md)

---

## Overview

The External Access SPA is a React 18 + Fluent UI v9 single-page application hosted on Power Pages. External users authenticate via Entra B2B (MSAL) and access their assigned Secure Projects. All data flows through the BFF API — no direct Dataverse calls from the browser.

---

## Quick Start

### Prerequisites

- Node.js 18+ and npm
- Azure CLI authenticated to `Spaarke SPE Subscription 1`
- `.env.local` created (see Environment Variables below)

### Run Locally

```bash
cd src/client/external-spa
npm install
npm run dev
# SPA at http://localhost:3000
# Proxies /_api/, /_layout/, /_services/ to the live portal
```

### Build

```bash
npm run build
# Output: dist/index.html (single self-contained file, ~800 KB)
```

### Deploy

```bash
npm run build
pwsh -File scripts/Deploy-ExternalWorkspaceSpa.ps1
# Uploads dist/index.html as sprk_externalworkspace web resource
```

---

## Environment Variables

Defined in `.env.development` (dev) or `.env.production` (prod). Override locally with `.env.local` (gitignored).

| Variable | Description | Default |
|----------|-------------|---------|
| `VITE_BFF_API_URL` | BFF API base URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| `VITE_MSAL_CLIENT_ID` | SPA app registration client ID | `f306885a-8251-492c-8d3e-34d7b476ffd0` |
| `VITE_MSAL_TENANT_ID` | Entra tenant ID (main workforce tenant) | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `VITE_MSAL_BFF_SCOPE` | BFF API OAuth scope | `api://1e40baad-.../SDAP.Access` |

All variables have hardcoded defaults in `config.ts` for safety — build succeeds even without `.env.local`.

---

## Project Structure

```
src/client/external-spa/
├── src/
│   ├── main.tsx            # Entry point — MSAL init + createRoot + MsalProvider
│   ├── App.tsx             # FluentProvider + HashRouter + AuthGuard + Routes
│   ├── config.ts           # Environment variable exports
│   ├── auth/
│   │   ├── msal-config.ts  # MSAL instance (singleton PublicClientApplication)
│   │   ├── msal-auth.ts    # acquireBffToken() — silent + redirect fallback
│   │   └── bff-client.ts   # bffApiCall() + typed BFF endpoint wrappers
│   ├── api/
│   │   └── web-api-client.ts   # getProjects/getDocuments/etc. (all via bffApiCall)
│   ├── hooks/
│   │   ├── useExternalContext.ts   # User context: contactId + project access list
│   │   ├── useAccessLevel.ts       # Access level enum + capability flags per project
│   │   └── usePlaybookExecution.ts # AI playbook execution
│   ├── pages/
│   │   ├── WorkspaceHomePage.tsx   # Project list + access levels
│   │   └── ProjectPage.tsx         # Tabbed project detail view
│   ├── components/
│   │   ├── AppHeader.tsx     # Header with user name + dark mode toggle
│   │   ├── AuthGuard.tsx     # MSAL auth gate — triggers login redirect if not authenticated
│   │   └── ErrorBoundary.tsx # Graceful error display
│   └── types/
│       └── index.ts          # AccessLevel enum, ApiError, PortalUser
├── public/index.html
├── vite.config.ts
├── tsconfig.json
└── package.json
```

---

## Authentication

### Model: Entra B2B + MSAL Authorization Code + PKCE

External users are **Azure AD B2B guest accounts** in the main Spaarke workforce tenant. They authenticate with their existing Microsoft 365 credentials — no new password or separate MFA enrollment.

MSAL (`@azure/msal-browser` v3) handles the full OAuth 2.0 authorization code + PKCE flow:

1. `msalInstance.initialize()` in `main.tsx` — processes any in-flight auth redirect
2. `AuthGuard` checks `useMsal().accounts[]` — triggers redirect login if empty
3. After login, MSAL stores tokens in `sessionStorage`
4. Every BFF call: `acquireBffToken()` → silent token acquisition → Bearer header

### App Registrations

| App | ID | Tenant |
|-----|-----|--------|
| `spaarke-external-access-SPA` (SPA client) | `f306885a-8251-492c-8d3e-34d7b476ffd0` | `a221a95e-...` |
| `SDAP-BFF-SPE-API` (BFF resource) | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `a221a95e-...` |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access` | |

**SPA app registration must have:**
- Platform: **Single-page application (SPA)** — NOT web
- Redirect URIs: `https://sprk-external-workspace.powerappsportals.com` + `http://localhost:3000`
- Implicit grant: **disabled** (code flow only)

### Key Auth Files

**`msal-config.ts`** — creates the singleton `PublicClientApplication`:
- `authority`: main tenant (`https://login.microsoftonline.com/{tenantId}`)
- `redirectUri`: `window.location.origin` (works for both portal and localhost)
- `cacheLocation`: `"sessionStorage"` — tokens per-tab, not shared across tabs

> **Why this is different from internal Spaarke surfaces (intentional and approved 2026-05-13):**
>
> Internal Spaarke PCFs and Code Pages use `cacheLocation: 'localStorage'` + `storeAuthStateInCookie: true` to achieve true SSO across tabs ([canonical reference](../../.claude/patterns/auth/spaarke-sso-binding.md)). The External SPA is intentionally different because it's a B2B portal often accessed from shared/kiosk workstations — per-tab isolation prevents token leakage when a guest closes a tab and a different person opens a new one in the same browser session. The slight UX friction of re-authenticating per-tab is acceptable for the security gain in this context.
>
> Do NOT migrate the External SPA to the internal `@spaarke/auth` library or change `cacheLocation` to `localStorage`. The threat models are different.

**`msal-auth.ts`** — `acquireBffToken()`:
- Tries `acquireTokenSilent` first (uses cached token or refresh token)
- Falls back to `acquireTokenRedirect` on `InteractionRequiredAuthError` (MFA, consent, session expired)
- After redirect: browser returns to the SPA and `msalInstance.initialize()` processes the response

**`bff-client.ts`** — `bffApiCall<T>(path, options)`:
- Calls `acquireBffToken()` and injects `Authorization: Bearer {token}`
- Retries once on 401 (in case token expired between acquisition and request)
- Throws `ApiError(statusCode, message)` on non-2xx

### Adding MSAL to a New Page or Component

```typescript
import { useMsal } from "@azure/msal-react";

// Get current account info
const { accounts } = useMsal();
const account = accounts[0]; // null if not authenticated (AuthGuard should prevent this)

// Make an authenticated BFF call
import { bffApiCall } from "../auth/bff-client";
const result = await bffApiCall<MyType>("/api/v1/external/my-endpoint");
```

### Local Development Auth

The Vite dev server proxies `/_services/` to the live Power Pages portal. However, the current implementation uses MSAL directly (not the portal implicit grant), so local dev authenticates against Entra directly via browser redirect.

Ensure `http://localhost:3000` is in the SPA app registration's redirect URIs.

---

## Data Access

### API Clients

All data access uses two files:

**`auth/bff-client.ts`** — BFF management calls (grant, revoke, invite, user context):
```typescript
import { getExternalUserContext, grantAccess, inviteUser } from "../auth/bff-client";

// Get user context (called by useExternalContext hook)
const ctx = await getExternalUserContext();
// { contactId, email, projects: [{ projectId, accessLevel }] }

// Grant access (called from Corporate Workspace, not external SPA)
await grantAccess({ contactId, projectId, accessLevel: 100000001 });
```

**`api/web-api-client.ts`** — Dataverse entity reads (despite the name, all calls go to the BFF):
```typescript
import { getProjects, getProjectById, getDocuments, getEvents, createEvent } from "../api/web-api-client";

const projects = await getProjects();
const project  = await getProjectById(projectId);
const docs     = await getDocuments(projectId);
const events   = await getEvents(projectId);
const newEvent = await createEvent(projectId, { sprk_name: "Filing deadline", sprk_duedate: "2026-04-15" });
```

### OData Types

Entity shapes are defined in `web-api-client.ts`:

```typescript
ODataProject       // sprk_projectid, sprk_name, sprk_referencenumber, sprk_description, ...
ODataDocument      // sprk_documentid, sprk_name, sprk_documenttype, sprk_summary, ...
ODataEvent         // sprk_eventid, sprk_name, sprk_duedate, sprk_status, sprk_todoflag, ...
ODataContact       // contactid, fullname, emailaddress1, telephone1, jobtitle, ...
ODataOrganization  // accountid, name, websiteurl, address1_city, ...
```

All collection endpoints return `{ value: T[] }` (OData envelope) which `getCollection()` unwraps automatically.

---

## Hooks

### `useExternalContext()`

Loads the authenticated user's context on mount. Returns the user's Dataverse Contact ID, email, and accessible project list.

```typescript
const { context, isLoading, error, refresh } = useExternalContext();
// context.contactId — Dataverse Contact GUID
// context.email — preferred_username from Entra
// context.projects — [{ projectId, accessLevel }]
// refresh() — re-fetches (after access grant/revoke)
```

### `useAccessLevel(projectId)`

Resolves the current user's access level for a specific project and exposes capability flags.

```typescript
const { accessLevel, canUpload, canDownload, canCreate, canUseAi, canInvite, isLoading } =
  useAccessLevel(projectId);

// accessLevel: AccessLevel.ViewOnly | Collaborate | FullAccess
// canUpload, canDownload, canCreate, canUseAi: true for Collaborate/FullAccess
// canInvite: true for FullAccess only

// Usage pattern:
{canUpload && <Button onClick={handleUpload}>Upload Document</Button>}
{canInvite && <InviteUserDialog projectId={projectId} />}
```

**Important**: Client-side capability flags are UX only. Security is enforced server-side in the BFF.

---

## Routing

HashRouter is **mandatory** — Power Pages serves the SPA as a single HTML file and doesn't support history API pushState.

```typescript
// App.tsx
<HashRouter>
  <Routes>
    <Route path="/"            element={<WorkspaceHomePage />} />
    <Route path="/project/:id" element={<ProjectPage />} />
    <Route path="*"            element={<Navigate to="/" replace />} />
  </Routes>
</HashRouter>

// Resulting URLs:
// https://sprk-external-workspace.powerappsportals.com/#/
// https://sprk-external-workspace.powerappsportals.com/#/project/{id}
```

Never use `BrowserRouter` — the portal would return 404 for any path other than the root.

---

## UI Standards

- **Fluent UI v9** exclusively (ADR-021) — `@fluentui/react-components`
- No hard-coded colors — use `tokens.colorNeutral*`, `tokens.colorBrand*` etc.
- Dark mode: `FluentProvider` toggles between `webLightTheme` / `webDarkTheme`
- OS preference is detected via `window.matchMedia("(prefers-color-scheme: dark)")`
- `makeStyles` / `mergeClasses` for all component styles
- Shared components from `@spaarke/ui-components` where applicable

---

## Adding a New Page

1. Create `src/pages/MyNewPage.tsx`
2. Add a route in `App.tsx`:
   ```typescript
   <Route path="/my-route" element={<MyNewPage />} />
   ```
3. Add navigation in `WorkspaceHomePage.tsx` or `AppHeader.tsx`
4. Wrap in `AuthGuard` (already applied at the `App` level — all routes are protected)

---

## Adding a New BFF Call

1. Define the typed function in `bff-client.ts` (management operations) or `web-api-client.ts` (data reads):
   ```typescript
   export async function getMyThing(id: string): Promise<MyThingDto> {
     return bffApiCall<MyThingDto>(`/api/v1/external/my-things/${id}`);
   }
   ```
2. Add the corresponding BFF endpoint in `ExternalProjectDataEndpoints.cs` (or create a new endpoint file)
3. Apply `AddExternalCallerAuthorizationFilter()` on the new endpoint

---

## Error Handling

`bffApiCall` throws `ApiError(statusCode, message)` on non-2xx responses.

Common status codes from BFF external endpoints:
- `401` — JWT missing or invalid (MSAL will have retried; likely a session issue)
- `403` — Contact not found in Dataverse, or project access denied
- `404` — Project/record not found
- `500` — BFF internal error (check BFF logs)

Handle in components:
```typescript
import { ApiError } from "../types";

try {
  const project = await getProjectById(id);
} catch (err) {
  if (err instanceof ApiError) {
    if (err.statusCode === 403) return <div>You do not have permission to access this project.</div>;
    if (err.statusCode === 404) return <div>Project not found.</div>;
  }
  return <div>An unexpected error occurred.</div>;
}
```

---

## Deployment

### Deploy Script

```bash
pwsh -File scripts/Deploy-ExternalWorkspaceSpa.ps1
```

The script:
1. Gets an Azure AD access token (via `az account get-access-token`) scoped to Dataverse
2. Base64-encodes `dist/index.html`
3. Checks if `sprk_externalworkspace` exists in Dataverse
4. Creates (first deploy) or updates (subsequent deploys) the web resource
5. Calls `PublishXml` to activate

Expected output: `[5/5] Publishing... Published`

**Always build first:**
```bash
npm run build && pwsh -File scripts/Deploy-ExternalWorkspaceSpa.ps1
```

### Verify Deployment

Navigate to:
```
https://spaarkedev1.crm.dynamics.com/WebResources/sprk_externalworkspace
```

Expected: React SPA loads, MSAL login flow starts (or workspace displays if already authenticated).

### First-Time Setup

After the web resource is created, it must be surfaced in Power Pages. See [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) for the one-time Power Pages site configuration.

---

## What Is NOT Supported

| Feature | Alternative |
|---------|------------|
| Dataverse Basic/Advanced Forms | Custom React forms in `ProjectPage.tsx` |
| Power Pages Liquid templates | React components |
| Power Pages `/_api/` Web API | BFF API via `bffApiCall` |
| Server-side rendering / SEO | Client-side only (authenticated content, no SEO needed) |
| BrowserRouter (history API) | HashRouter only |
| Multiple HTML entry points | Single `index.html` via `vite-plugin-singlefile` |

---

## Related Resources

- **Architecture Reference**: [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md) — Full system architecture
- **Admin Setup**: [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) — Power Pages configuration
- **Deploy skill**: [`/power-page-deploy`](../../.claude/skills/power-page-deploy/) — Deployment procedure
- **ADR-021**: Fluent UI v9 design system requirements
- **ADR-022**: React 18 Code Page pattern (createRoot, bundled React)
- **ADR-008**: Endpoint filter pattern for per-endpoint authorization
- [MSAL Browser documentation](https://github.com/AzureAD/microsoft-authentication-library-for-js)
- [Power Pages code site GA announcement](https://www.microsoft.com/en-us/power-platform/blog/power-pages/announcing-general-availability-ga-of-building-single-page-applications-on-power-pages/)
