# EXTERNAL ACCESS SPA — DEVELOPER GUIDE

> **Audience**: Engineers building or extending the Secure Project Workspace SPA
> **Last Updated**: 2026-07-20
> **Applies To**: `src/client/external-spa/`
> **Architecture Reference**: [`docs/architecture/external-access-spa-architecture.md`](../architecture/external-access-spa-architecture.md)

---

## Overview

The External Access SPA is a React 18 + Fluent UI v9 single-page application hosted on **Azure Static Web Apps (SWA)**. External users authenticate against **Microsoft Entra External ID (CIAM)** via MSAL (authorization code + PKCE) and access their assigned Secure Projects. All data flows through the BFF API — no direct Dataverse or SPE calls from the browser, and the external token is never exchanged downstream (broker-only, ADR-028 Amendment A1).

> **Retired (historical)**: This SPA formerly ran inside a **Power Pages** web resource with **Entra B2B guest** identity and `HashRouter`. That hosting/identity model and `Deploy-ExternalWorkspaceSpa.ps1` are decommissioned. Historical references below are labeled as retired.

---

## Quick Start

### Prerequisites

- Node.js 18+ and npm
- Azure CLI authenticated (for local BFF calls)
- `.env.development` present (committed, safe values); override locally with `.env.local` (gitignored)

### Run Locally

```bash
cd src/client/external-spa
npm install --legacy-peer-deps
npm run dev
# SPA at http://localhost:3000
```

Ensure `http://localhost:3000` is a registered SPA redirect URI on the CIAM SPA app registration.

### Build

```bash
npm run build
# Output: dist/ (multi-file static site for SWA)
```

### Deploy

Deployment is via the GitHub Actions workflow `.github/workflows/deploy-external-spa.yml` (`workflow_dispatch`) — it builds with the CIAM `VITE_*` env, stages `staticwebapp.config.json` into `dist/`, and uploads to SWA via `Azure/static-web-apps-deploy`. See [Deployment](#deployment).

---

## Environment Variables

Defined in `.env.development` (dev) or `.env.production` (CI/CD token/placeholder substitution). Override locally with `.env.local`. `config.ts` throws loudly if a required variable is missing or still contains an un-substituted `#{...}#` placeholder — there are **no hardcoded fallbacks**.

| Variable | Description | Dev value |
|----------|-------------|-----------|
| `VITE_BFF_API_URL` | BFF API base URL | `https://spaarke-bff-dev.azurewebsites.net` |
| `VITE_MSAL_AUTHORITY` | CIAM MSAL authority URL | `https://spaarkeextid.ciamlogin.com/7052feba-bfc4-43e0-b09e-65014b429131` |
| `VITE_MSAL_CLIENT_ID` | CIAM SPA app registration client ID | `bd57e54e-b339-4500-b55c-e451009fd907` |
| `VITE_MSAL_TENANT_ID` | CIAM external tenant ID | `7052feba-bfc4-43e0-b09e-65014b429131` |
| `VITE_MSAL_BFF_SCOPE` | BFF API OAuth scope (CIAM) | `api://4a4d5126-91b0-4865-8e3a-134b7209013e/SDAP.Access` |

> The SPA runs on SWA, **not** inside a Dataverse web resource — Xrm context is unavailable, so `@spaarke/auth`'s `resolveRuntimeConfig()` is not used. Values are injected at build time (the deploy workflow's `env:` block overrides the `.env.production` placeholders).

---

## Project Structure

```
src/client/external-spa/
├── staticwebapp.config.json    # SWA runtime config (navigationFallback + security headers)
├── src/
│   ├── main.tsx            # Entry point — MSAL init + createRoot + MsalProvider
│   ├── App.tsx             # FluentProvider + BrowserRouter + AuthGuard + Routes + in-app 404
│   ├── config.ts           # Env variable exports (throws if missing/placeholder)
│   ├── auth/
│   │   ├── msal-config.ts  # MSAL instance (CIAM authority + knownAuthorities + sessionStorage)
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
│   │   ├── ProjectPage.tsx         # Tabbed project detail view
│   │   ├── DocumentUploadPage.tsx
│   │   ├── PlaybookLibraryPage.tsx
│   │   └── SettingsPage.tsx
│   ├── components/
│   │   ├── AppHeader.tsx     # Header with user name + dark mode toggle
│   │   ├── AuthGuard.tsx     # MSAL auth gate — triggers CIAM login redirect if not authenticated
│   │   └── ErrorBoundary.tsx # Graceful error display
│   └── types/
│       └── index.ts          # AccessLevel enum, ApiError, PortalUser
├── vite.config.ts
├── tsconfig.json
└── package.json
```

---

## Authentication

### Model: Entra External ID (CIAM) + MSAL Authorization Code + PKCE

External users are **local accounts in a dedicated CIAM external tenant** (`spaarkeextid`) — **not** Entra B2B guests in the workforce tenant. They authenticate against the CIAM authority (`*.ciamlogin.com`) and set/reset their password via SSPR ("Forgot password").

MSAL (`@azure/msal-browser`) handles the full OAuth 2.0 authorization code + PKCE flow:

1. `msalInstance.initialize()` in `main.tsx` — processes any in-flight auth redirect.
2. `AuthGuard` checks `useMsal().accounts[]` — triggers redirect login if empty.
3. Before the redirect, the intended deep-link route is captured in per-tab `sessionStorage` and restored after auth (with an open-redirect guard restoring only in-app relative paths).
4. After login, MSAL stores tokens in `sessionStorage`.
5. Every BFF call: `acquireBffToken()` → silent acquisition → `Authorization: Bearer` header.

### App Registrations (CIAM tenant)

| App | ID | Notes |
|-----|-----|-------|
| External SPA public client | `bd57e54e-b339-4500-b55c-e451009fd907` | SPA platform, PKCE, implicit disabled |
| BFF API | `4a4d5126-91b0-4865-8e3a-134b7209013e` | `api://4a4d5126-…/SDAP.Access`; `requestedAccessTokenVersion: 2` (aud = client-id GUID) |

The BFF validates these tokens with its second `Ciam` JWT scheme, pinned to the `/api/v1/external` group.

### Key Auth Files

**`msal-config.ts`** — creates the singleton `PublicClientApplication`:
- `authority`: the config-driven CIAM authority (`MSAL_AUTHORITY`).
- `knownAuthorities`: the CIAM authority **host** (e.g. `spaarkeextid.ciamlogin.com`) — required because `*.ciamlogin.com` is a non-default (B2C-style) authority MSAL must be told to trust.
- `redirectUri` / `postLogoutRedirectUri`: `window.location.origin` (works for the SWA origin and localhost).
- `cacheLocation`: `"sessionStorage"` — tokens per-tab, not shared across tabs.

> **Why `sessionStorage` (intentional ADR-028 exception):** Internal Spaarke surfaces use `cacheLocation: 'localStorage'` for cross-tab SSO ([canonical reference](../../.claude/patterns/auth/spaarke-sso-binding.md)). The External SPA is intentionally different — it's an external portal often accessed from shared/kiosk workstations, so per-tab isolation prevents token leakage when one person closes a tab and another opens a new one. Do NOT migrate the External SPA to `@spaarke/auth` or switch `cacheLocation` to `localStorage`.
>
> Note: `@azure/msal-browser` v5 removed `storeAuthStateInCookie` from `CacheOptions` (it is now a per-request option, defaulting off), so it is no longer set here.

**`msal-auth.ts`** — `acquireBffToken()`:
- Tries `acquireTokenSilent` first (cached or refresh token).
- Falls back to `acquireTokenRedirect` on `InteractionRequiredAuthError` (MFA, consent, session expired).

**`bff-client.ts`** — `bffApiCall<T>(path, options)`:
- Calls `acquireBffToken()` and injects `Authorization: Bearer {token}`.
- Retries once on 401.
- Throws `ApiError(statusCode, message)` on non-2xx.

### Adding MSAL to a New Page or Component

```typescript
import { useMsal } from "@azure/msal-react";

const { accounts } = useMsal();
const account = accounts[0]; // AuthGuard ensures this is present on protected routes

import { bffApiCall } from "../auth/bff-client";
const result = await bffApiCall<MyType>("/api/v1/external/my-endpoint");
```

---

## Data Access

### API Clients

**`auth/bff-client.ts`** — BFF calls including user context:
```typescript
import { getExternalUserContext } from "../auth/bff-client";

const ctx = await getExternalUserContext();
// { contactId, email, projects: [{ projectId, accessLevel }] }
```

**`api/web-api-client.ts`** — project data reads (despite the name, all calls go to the BFF):
```typescript
import { getProjects, getProjectById, getDocuments, getTodos, createTodo } from "../api/web-api-client";

const projects = await getProjects();
const project  = await getProjectById(projectId);
const docs     = await getDocuments(projectId);
const todos    = await getTodos(projectId);
```

> **Contract note**: the external data surface exposes **to-dos** (`sprk_todo`), not events — the event-based routes were replaced (`smart-todo-decoupling-r3`, FR-29).

### Document Download

Documents are downloaded by `documentId` — the browser never receives Graph pointers:

```typescript
// GET /api/v1/external/projects/{projectId}/documents/{documentId}/content
// Authorized -> application/octet-stream (attachment). Unauthorized -> 403, no bytes.
```

The BFF enforces project access **and** document→project scoping before any storage read, then streams **app-only** (broker-only). Do not attempt to fetch or expose `driveId`/`driveItemId` client-side — they are resolved server-side only.

---

## Hooks

### `useExternalContext()`

Loads the authenticated user's context on mount — the Dataverse Contact ID, email, and accessible project list.

```typescript
const { context, isLoading, error, refresh } = useExternalContext();
// context.contactId — Dataverse Contact GUID
// context.email — from the CIAM token (first-login/display)
// context.projects — [{ projectId, accessLevel }]
```

### `useAccessLevel(projectId)`

Resolves the user's access level for a project and exposes capability flags.

```typescript
const { accessLevel, canUpload, canDownload, canCreate, canUseAi, canInvite, isLoading } =
  useAccessLevel(projectId);
// accessLevel: AccessLevel.ViewOnly | Collaborate | FullAccess
```

**Important**: Client-side capability flags are UX only. Security is enforced server-side in the BFF via `ExternalCallerContext.GetEffectiveRights` (ViewOnly → Read; Collaborate → Read+Create+Write; FullAccess → +Delete).

---

## Routing

**`BrowserRouter`** is used (clean URLs). Deep links resolve because SWA `navigationFallback` rewrites unmatched routes to `/index.html`. Unknown paths render an **in-app 404 view** (not a silent redirect home).

```typescript
// App.tsx (inside BrowserRouter)
<Routes>
  <Route path="/"                                   element={<WorkspaceHomePage />} />
  <Route path="/project/:id"                        element={<ProjectPage />} />
  <Route path="/playbooks/:entityType/:entityId"    element={<PlaybookLibraryPage />} />
  <Route path="/upload"                             element={<DocumentUploadPage />} />
  <Route path="/settings"                           element={<SettingsPage ... />} />
  <Route path="*"                                    element={<NotFoundView />} />
</Routes>

// Resulting URLs (clean, no hash):
// https://green-dune-0c4f1221e.7.azurestaticapps.net/
// https://green-dune-0c4f1221e.7.azurestaticapps.net/project/{id}
```

> Never use `BrowserRouter` **without** the SWA `navigationFallback` rewrite — direct navigation to a deep path would 404. `HashRouter` is retired (it was a Power Pages single-file constraint).

---

## UI Standards

- **Fluent UI v9** exclusively (ADR-021) — `@fluentui/react-components`.
- No hard-coded colors — use `tokens.colorNeutral*`, `tokens.colorBrand*`, etc.
- Light/dark theming via the shared 4-level cascade (`resolveCodePageTheme` from `@spaarke/ui-components`) — localStorage > URL flags > navbar DOM > system preference. `FluentProvider` toggles `webLightTheme` / `webDarkTheme`.
- `makeStyles` / `mergeClasses` for all component styles.
- Shared components from `@spaarke/ui-components` where applicable.

---

## Adding a New Page

1. Create `src/pages/MyNewPage.tsx`.
2. Add a route in `App.tsx`: `<Route path="/my-route" element={<MyNewPage />} />`.
3. Add navigation in `WorkspaceHomePage.tsx` or `AppHeader.tsx`.
4. All routes are already protected by `AuthGuard` (applied at the shell level).

---

## Adding a New BFF Call

1. Define the typed function in `bff-client.ts` or `web-api-client.ts`:
   ```typescript
   export async function getMyThing(id: string): Promise<MyThingDto> {
     return bffApiCall<MyThingDto>(`/api/v1/external/my-things/${id}`);
   }
   ```
2. Add the corresponding BFF endpoint under the `/api/v1/external` group (`ExternalProjectDataEndpoints.cs`).
3. Apply `AddExternalCallerAuthorizationFilter()` on the new endpoint (the `CiamExternal` policy is applied at the group level).

---

## Error Handling

`bffApiCall` throws `ApiError(statusCode, message)` on non-2xx.

Common status codes from BFF external endpoints:
- `401` — CIAM JWT missing/invalid, or identity claims missing (MSAL will have retried; likely a session issue)
- `403` — Contact not resolvable, or project/document access denied (incl. authz-before-stream denials)
- `404` — Project/record not found (or document content unavailable)
- `500` — BFF internal error (check BFF logs)

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

### Workflow

`.github/workflows/deploy-external-spa.yml` (`workflow_dispatch`) does:

1. `npm install --legacy-peer-deps --no-audit --no-fund` (per root CLAUDE.md §12).
2. `npm run build` with the CIAM `VITE_*` values supplied via the workflow `env:` block (real env vars override the `.env.production` placeholders; all values are non-secret identifiers).
3. Copy `staticwebapp.config.json` into `dist/`.
4. Upload `dist/` via `Azure/static-web-apps-deploy` (`skip_app_build: true`) using the `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` secret.

### Verify Deployment

Navigate to `https://green-dune-0c4f1221e.7.azurestaticapps.net/`. Expected: the React SPA loads and the MSAL CIAM login flow starts (or the workspace displays if already authenticated). Test a direct deep link (e.g. `/project/{id}`) to confirm `navigationFallback` resolves it.

> **Retired (historical)**: the Power Pages web-resource deploy (`scripts/Deploy-ExternalWorkspaceSpa.ps1`, base64 upload to `sprk_externalworkspace`, `PublishXml`) is deleted. Do not use it.

---

## What Is NOT Supported

| Feature | Alternative / Note |
|---------|--------------------|
| Direct Dataverse calls from the browser | BFF API via `bffApiCall` |
| OBO / delegated downstream access on the external path | App-only (broker-only) — no OBO for external users |
| Exposing Graph pointers (`driveId`/`driveItemId`) to the client | Resolved server-side; download by `documentId` |
| Direct-Office features (Word-for-Web co-authoring, desktop open via `webUrl`, user-identity Copilot grounding, Microsoft Search) | Out of scope (ADR-028 A1 limitation E-3 — requires a workforce identity reaching SPE) |
| Server-side rendering / SEO | Client-side only (authenticated content) |
| `HashRouter` (history-less routing) | `BrowserRouter` + SWA `navigationFallback` (retired: HashRouter was a Power Pages constraint) |
| Power Pages Liquid / Basic Forms / `/_api/` | Retired hosting model — React components + BFF API |

---

## Related Resources

- **Architecture Reference**: [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md) — Full system architecture
- **Admin Setup**: [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) — CIAM tenant, SWA, BFF, onboarding config
- **Auth architecture (ADR-028 + Amendment A1)**: [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
- **ADR-021**: Fluent UI v9 design system requirements
- **ADR-022**: React 18 pattern (createRoot, bundled React)
- **ADR-008**: Endpoint filter pattern for per-endpoint authorization
- [MSAL Browser documentation](https://github.com/AzureAD/microsoft-authentication-library-for-js)
- [Azure Static Web Apps configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration)
