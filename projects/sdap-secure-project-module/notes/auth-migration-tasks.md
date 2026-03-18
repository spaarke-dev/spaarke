# Auth Migration Task List — Entra B2B + MSAL

> **Full context**: See `notes/auth-migration-b2b-msal.md`
> **Branch**: `feature/sdap-secure-project-module`
> **Status tracking**: Update checkboxes as tasks complete

---

## Phase 0 — Prerequisites (Admin / Manual — No Code)

These must be done **before any code changes**. Requires Azure portal admin access to main tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`.

- [ ] **P0-1** — Register SPA app in main tenant
  - Platform: Single-page application (SPA)
  - Redirect URIs: `https://sprk-external-workspace.powerappsportals.com` and `http://localhost:3000`
  - Record: Application (client) ID → `VITE_MSAL_CLIENT_ID`

- [ ] **P0-2** — Authorize SPA client on BFF app registration (`SDAP-BFF-SPE-API`)
  - Existing scope to use: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access` (already defined — no new scope needed)
  - Expose an API → Authorized client applications → **+ Add a client application**
  - Client ID: `f306885a-8251-492c-8d3e-34d7b476ffd0` (SPA), check `SDAP.Access`
  - `VITE_MSAL_BFF_SCOPE` = `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access`

- [ ] **P0-3** — Configure Power Pages identity provider
  - Remove / disable Entra External ID (CIAM) provider from portal
  - Add Microsoft Entra (main workforce tenant) as identity provider
  - Set as default — sign-in goes directly to Microsoft login
  - Reference: `notes/phase3-task020-entra-external-id-config.md` (this process is superseded; follow [Power Pages IDP docs](https://learn.microsoft.com/en-us/power-pages/security/authentication/configure-site) instead)

- [ ] **P0-4** — Verify test B2B guest user
  - Confirm `testuser1@spaarke.com` is a B2B guest in the main tenant
  - Confirm Contact record exists in Dataverse with matching email
  - Confirm at least one active `sprk_externalrecordaccess` record exists for that Contact

- [ ] **P0-5** — Update CSP site setting in portal
  - Add `https://login.microsoftonline.com` to `connect-src`
  - Site setting: `HTTP/Content-Security-Policy`
  - Full value: `script-src 'self'; connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net https://login.microsoftonline.com; style-src 'self' 'unsafe-inline'`

---

## Phase 1 — SPA Auth Module Replacement

All changes in `src/client/external-spa/`.

- [ ] **S1-1** — Install MSAL packages
  ```bash
  cd src/client/external-spa
  npm install @azure/msal-browser @azure/msal-react
  ```

- [ ] **S1-2** — Create `src/auth/msal-config.ts`
  - `PublicClientApplication` instance using `MSAL_CLIENT_ID` and `MSAL_TENANT_ID` from config
  - Authority: `https://login.microsoftonline.com/{MSAL_TENANT_ID}`
  - Cache: `sessionStorage`
  - Export `msalInstance` for use in `MsalProvider` and `msal-auth.ts`

- [ ] **S1-3** — Create `src/auth/msal-auth.ts`
  - `acquireBffToken()` — replaces `getPortalToken()`
  - Silent first (`acquireTokenSilent`), falls back to `acquireTokenRedirect` on `InteractionRequiredAuthError`
  - Uses `MSAL_BFF_SCOPE` from config

- [ ] **S1-4** — Update `src/config.ts`
  - Remove `PORTAL_CLIENT_ID` export
  - Add `MSAL_CLIENT_ID`, `MSAL_TENANT_ID`, `MSAL_BFF_SCOPE` exports from `VITE_*` env vars

- [ ] **S1-5** — Update `src/auth/bff-client.ts`
  - Remove import of `getPortalToken`, `clearPortalTokenCache` from `portal-auth`
  - Import `acquireBffToken` from `msal-auth`
  - Replace `getPortalToken()` calls with `acquireBffToken()`
  - Remove manual token cache invalidation on 401 (MSAL handles its own cache)

- [ ] **S1-6** — Delete `src/auth/portal-auth.ts`

- [ ] **S1-7** — Update `src/main.tsx`
  - Import `MsalProvider` from `@azure/msal-react`
  - Import `msalInstance` from `auth/msal-config`
  - Wrap root `<App />` in `<MsalProvider instance={msalInstance}>`

- [ ] **S1-8** — Add `src/components/AuthGuard.tsx`
  - Uses `useIsAuthenticated()` and `useMsal()` from `@azure/msal-react`
  - If not authenticated: shows a "Sign in" button or triggers `loginRedirect` automatically
  - If authenticated: renders children (the app)
  - Wrap `<App />` content with `<AuthGuard>` in `App.tsx` or `main.tsx`

- [ ] **S1-9** — Update env files
  - `.env.development`: remove `VITE_PORTAL_CLIENT_ID`, add `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_TENANT_ID`, `VITE_MSAL_BFF_SCOPE` (values from P0-1/P0-2)
  - `.env.production.local`: same as development (gitignored, local override)
  - `.env.production`: replace portal CI placeholders with MSAL CI placeholders

- [ ] **S1-10** — Build and verify locally
  ```bash
  cd src/client/external-spa
  npm run build
  # Verify: no TypeScript errors, bundle builds clean
  # Verify: no references to portal-auth in compiled output
  # Verify: MSAL_CLIENT_ID, MSAL_BFF_SCOPE present in dist/assets/app.js
  ```

---

## Phase 2 — BFF Auth Filter Update

All changes in `src/server/api/Sprk.Bff.Api/`.

- [ ] **B2-1** — Update `ExternalCallerAuthorizationFilter.cs`
  - Remove `PortalTokenValidator` constructor dependency
  - Remove manual Bearer token extraction from `Authorization` header
  - Read identity from `context.HttpContext.User` (Azure AD middleware has already validated the JWT)
  - Email claim: `preferred_username` or `ClaimTypes.Email` (B2B guest tokens use `preferred_username`)
  - Object ID claim: `oid` (Azure AD object ID of the guest account)

- [ ] **B2-2** — Remove or retire `PortalTokenValidator.cs`
  - If no other code uses it: delete the file
  - Remove DI registration from `Program.cs`

- [ ] **B2-3** — Verify `Program.cs` / `appsettings` Azure AD config
  - Confirm `AddMicrosoftIdentityWebApi()` is called (handles JWT validation)
  - Confirm `AzureAd.TenantId = "common"` (accepts tokens from any tenant — needed for B2B guests)
  - Confirm `AzureAd.ClientId` = BFF app registration client ID (`#{API_APP_ID}#` resolved)
  - Confirm `AzureAd.Audience` = `api://{API_APP_ID}` (matches the scope the SPA requests)

- [ ] **B2-4** — Build and verify
  ```bash
  dotnet build src/server/api/Sprk.Bff.Api/
  # Verify: no compile errors
  ```

---

## Phase 3 — BFF Data Endpoints

All changes in `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/`.

These endpoints use the injected `ExternalCallerContext` to scope queries to only data the caller can access. All registered in the existing `/api/v1/external/` route group.

- [ ] **B3-1** — `GET /api/v1/external/projects`
  - Returns list of projects where caller has an active `sprk_externalrecordaccess` record
  - Uses `ExternalCallerContext.ProjectAccessMap` keys for filtering

- [ ] **B3-2** — `GET /api/v1/external/projects/{id}`
  - Returns single project
  - Verifies project ID is in `ExternalCallerContext.ProjectAccessMap`; returns 403 if not

- [ ] **B3-3** — `GET /api/v1/external/projects/{id}/documents`
  - Returns documents linked to the project
  - Access check: project must be in caller's access map

- [ ] **B3-4** — `GET /api/v1/external/projects/{id}/events`
  - Returns events/tasks linked to the project
  - Access check: project must be in caller's access map

- [ ] **B3-5** — `GET /api/v1/external/projects/{id}/contacts`
  - Returns contacts associated with the project
  - Access check: project must be in caller's access map

- [ ] **B3-6** — `GET /api/v1/external/projects/{id}/organizations`
  - Returns accounts/organizations associated with the project
  - Access check: project must be in caller's access map

- [ ] **B3-7** — `POST /api/v1/external/projects/{id}/events`
  - Creates a new event on the project
  - Access check: caller must have Collaborate or FullAccess level for this project

- [ ] **B3-8** — `PATCH /api/v1/external/events/{id}`
  - Updates an existing event
  - Access check: caller must have Collaborate or FullAccess level; event must belong to an accessible project

- [ ] **B3-9** — Build and verify all endpoints
  ```bash
  dotnet build src/server/api/Sprk.Bff.Api/
  ```

---

## Phase 4 — Deploy and Test

- [ ] **D4-1** — Deploy BFF API
  ```powershell
  .\scripts\Deploy-BffApi.ps1
  ```
  Verify: health check passes, new endpoints return 401 (not 404) for unauthenticated requests

- [ ] **D4-2** — Build and deploy SPA
  ```bash
  cd src/client/external-spa && npm run build
  ```
  ```powershell
  pwsh -File scripts/Deploy-ExternalWorkspaceSpa.ps1
  ```

- [ ] **D4-3** — End-to-end auth test
  - Open `https://sprk-external-workspace.powerappsportals.com` in private browser
  - Expected: portal redirects to Microsoft login (Entra B2B, main tenant)
  - Sign in as `testuser1@spaarke.com` (B2B guest)
  - Expected: redirect back to SPA after login
  - Expected: SPA loads, MSAL has a token, BFF calls succeed

- [ ] **D4-4** — Verify BFF data endpoints with authenticated session
  - Network tab: `GET /api/v1/external/projects` → 200 with project list
  - Network tab: `GET /api/v1/external/me` → 200 with user context
  - Verify: `Authorization: Bearer {token}` header is present on BFF calls
  - Verify: token is a standard Azure AD JWT (decode at jwt.ms — should have `oid`, `preferred_username`, `aud` = BFF app ID)

- [ ] **D4-5** — Verify portal sign-in experience
  - Sign out of portal
  - Return to portal URL — should redirect directly to Microsoft login (not show a portal login page with CIAM branding)
  - Sign in with a different B2B guest account if available

---

## Phase 5 — Cleanup

- [ ] **C5-1** — Remove obsolete portal site settings (optional — they are harmless but misleading)
  - `ImplicitGrantFlow/RegisteredClientId`
  - `Connector/ImplicitGrantFlowEnabled`
  - `ImplicitGrantFlow/TokenExpirationTime`
  - `Webapi/sprk_project/enabled` (and all other Webapi/* settings)

- [ ] **C5-2** — Update `notes/phase3-task020-entra-external-id-config.md`
  - Add note at top: "SUPERSEDED — replaced by B2B + MSAL. See auth-migration-b2b-msal.md"

- [ ] **C5-3** — Update `notes/phase5-task050-spa-deployment-guide.md`
  - Update authentication section to reflect MSAL
  - Update environment variables table
  - Remove references to `VITE_PORTAL_CLIENT_ID` and implicit grant flow

- [ ] **C5-4** — Commit all changes
  ```bash
  # push-to-github skill
  ```

---

## Dependency Order

```
P0-1 → P0-2 → S1-2, S1-3, S1-9 (need client ID and scope)
P0-3 → D4-3 (portal IDP needed for sign-in test)
P0-4 → D4-3 (test user needed)
P0-5 → D4-3 (CSP needed for MSAL redirect)

S1-1 → S1-2, S1-3, S1-5, S1-7, S1-8 (packages needed first)
S1-2 → S1-3, S1-7 (msal-config needed by msal-auth and main.tsx)
S1-4 → S1-2, S1-3 (config vars needed by msal-config/msal-auth)
S1-5 → S1-3, S1-6 (bff-client needs msal-auth before portal-auth deleted)

B2-1 → B2-2 (update filter before removing validator)
B2-3 → B2-1 (verify Azure AD config before testing filter)

B3-* → B2-* (data endpoints rely on working filter)
D4-1 → B2-*, B3-* (must deploy after BFF changes)
D4-2 → S1-* (must deploy after SPA changes)
D4-3 → D4-1, D4-2, P0-3, P0-4, P0-5 (all prereqs needed for full test)
```

---

## Key Values to Collect During Phase 0

| Value | Where to find | Used in |
|-------|--------------|---------|
| SPA client ID | **KNOWN**: `f306885a-8251-492c-8d3e-34d7b476ffd0` (app: `spaarke-external-access-SPA`) | `VITE_MSAL_CLIENT_ID` |
| BFF app ID | **KNOWN**: `1e40baad-e065-4aea-a8d4-4b7ab273458c` (app: `SDAP-BFF-SPE-API`) | `VITE_MSAL_BFF_SCOPE` prefix |
| BFF scope URI | **KNOWN**: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access` (existing scope) | `VITE_MSAL_BFF_SCOPE` |
| Main tenant ID | **KNOWN**: `a221a95e-6abc-4434-aecc-e48338a1b2f2` | `VITE_MSAL_TENANT_ID` |

---

*Task list created: March 2026 | Companion document: auth-migration-b2b-msal.md*
