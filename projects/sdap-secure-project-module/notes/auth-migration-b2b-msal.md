# Auth Migration: Portal Implicit Grant → Entra B2B + MSAL

> **Status**: ✅ COMPLETE — Implemented and deployed (March 2026)
> **Decision Date**: March 2026
> **Decision Authority**: External architecture review
> **Replaces**: Task 020 (CIAM/Entra External ID), portal implicit grant flow

## Completion Summary

All implementation phases completed and deployed to dev:

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 0: Prerequisites (P0-1, P0-2) | ✅ Done | SPA app reg created, BFF scope confirmed |
| Phase 1: SPA MSAL auth | ✅ Done | MSAL installed, msal-config.ts, msal-auth.ts, AuthGuard.tsx, main.tsx, App.tsx all updated; portal-auth.ts deleted |
| Phase 2: BFF auth filter | ✅ Done | ExternalCallerAuthorizationFilter now reads HttpContext.User claims; PortalTokenValidator deleted |
| Phase 3: BFF data endpoints | ✅ Done | ExternalDataService + 8 routes (GET projects, documents, events, contacts, orgs; POST/PATCH events) |
| Phase 4: Deploy | ✅ Done | BFF deployed (65 MB, health check passed); SPA deployed to sprk_externalworkspace |

### Manual Steps Still Required (P0-3, P0-4, P0-5)

These require admin portal access — implementation is blocked until these are done:

1. **P0-3**: Configure Power Pages identity provider → Entra B2B (main tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`). Remove CIAM provider.
2. **P0-4**: Verify a B2B guest test user exists in the main tenant AND has a Contact record in Dataverse (`spaarkedev1`) with matching `emailaddress1` AND an active `sprk_externalrecordaccess` record.
3. **P0-5**: Update CSP site setting (`HTTP/Content-Security-Policy`) — add `https://login.microsoftonline.com` to `connect-src`.

> **Context for compaction recovery**: Read this file first when resuming this work

---

## Background and Decision Summary

### Why We Are Changing

The original design used the **Power Pages portal implicit grant flow** to obtain tokens for BFF API calls, with **Entra External ID (CIAM)** as the portal identity provider. This approach hit two sequential blockers:

1. **MSAL abandonment** (now resolved): MSAL was the original implementation. It was abandoned because the Power Pages Module Federation (MF) shell intercepts `type="module"` ES module script tags and substitutes React 16, breaking React 18. This was fixed by switching the Vite build to IIFE format with `defer` instead of `type="module"`. **This blocker no longer exists.**

2. **Portal implicit grant flow blocked**: `POST /_services/auth/token` returns `PortalSTS0018` — the portal cannot issue implicit grant tokens without a token-signing certificate, which requires converting the portal to production mode. The portal is currently in trial/development mode.

An external architectural review concluded:

> "The best-practice approach is not 'Power Pages portal implicit grant as the app token system.' It is 'Power Pages as the host and interactive login shell, Entra B2B as the external identity model, and MSAL auth-code-with-PKCE for the SPA's calls to your BFF.'"

### Decision

**Use Entra B2B + MSAL authorization code flow with PKCE.**

- External users are **B2B guest accounts** in the main Spaarke workforce tenant (`a221a95e-6abc-4434-aecc-e48338a1b2f2`)
- They authenticate with their **existing Microsoft 365 credentials** — full SSO, no separate password
- The SPA uses **MSAL.js** to obtain a standard Azure AD access token for the BFF API
- Power Pages remains the host and handles the **portal sign-in experience** via Entra B2B as the identity provider
- All Dataverse data is accessed exclusively through the **BFF API** (managed identity → Dataverse), never via `/_api/*` directly

---

## Architectural Questions Resolved

### Q: Does BFF need explicit `/external/` route prefixes? Is this scalable?

**Yes — as a route group, not per-feature.**

The `/api/v1/external/` path is a **route group** in the BFF with the `ExternalCallerAuthorizationFilter` applied at group level. New external features add routes to the group and automatically inherit authorization. No per-route auth configuration is needed.

```
Route group: /api/v1/external/
  ├── Authorization filter applied once at group entry
  ├── Filter resolves caller's sprk_externalrecordaccess records
  ├── Filter injects ExternalCallerContext into endpoint context
  └── Individual endpoints use injected context to scope data
       ├── GET /projects          → returns only projects caller has access to
       ├── GET /projects/{id}     → verifies caller has access to this project
       └── (new features added here — no auth config required)
```

Internal routes (`/api/v1/documents/`, `/api/v1/projects/`, etc.) remain separate with different auth for internal callers.

### Q: Do Power Pages table permissions matter if all data goes through BFF?

**No — not for the SPA.**

- Power Pages table permissions govern `/_api/*` (the portal's OData proxy)
- The SPA makes **zero direct `/_api/*` calls** — all data comes from BFF endpoints
- The BFF accesses Dataverse via **managed identity** — this bypasses table permissions entirely
- Table permissions are only relevant for portal-rendered Liquid pages (none exist in this project)

**What still matters from Power Pages:**
- Identity provider configuration (Entra B2B as the sign-in IDP)
- CSP headers (`connect-src` must include `login.microsoftonline.com` and the BFF URL)
- Portal site activation (the portal must be active for the SPA to be served)

**What no longer matters:**
- `Webapi/sprk_project/enabled` site settings
- `Webapi/sprk_document/enabled` site settings
- `Webapi/sprk_event/enabled` site settings
- `Webapi/sprk_externalrecordaccess/enabled` site settings
- `ImplicitGrantFlow/RegisteredClientId` site setting
- `Connector/ImplicitGrantFlowEnabled` site setting
- Web roles (`Secure Project Participant`, `Authenticated Users`) — do not affect BFF access

---

## Current State (Before Migration)

### Deployed Components

| Component | Location | Current State |
|-----------|----------|---------------|
| Power Pages code site | `sprk-external-workspace.powerappsportals.com` | Active, SPA loads |
| SPA build format | IIFE + defer | Working — MF conflict resolved |
| SPA auth | `portal-auth.ts` — portal implicit grant | **Broken** — PortalSTS0018 (no cert) |
| BFF API | `spe-api-dev-67e2xz.azurewebsites.net` | Deployed, healthy |
| BFF auth filter | `ExternalCallerAuthorizationFilter` → `PortalTokenValidator` | Deployed but blocked by SPA auth failure |
| Portal identity provider | Entra External ID (CIAM) per task 020 | **Wrong model** — superseded by B2B decision |
| Portal implicit grant client ID | `6e2a4f81-3c7d-4b5e-a912-8f0d1e3c5a7b` | Configured in site settings, now irrelevant |

### SPA Data Architecture (Already Correct)

All Dataverse data calls in the SPA already go through BFF — **zero direct `/_api/*` calls exist**. This was always the design. The `web-api-client.ts` file header reads:

> "All requests use `bffApiCall` from bff-client.ts which handles **Azure AD B2B authentication (MSAL)** and Bearer token injection."

The BFF data endpoints (`GET /api/v1/external/projects`, etc.) are called from the SPA but **not yet implemented in the BFF**. The call sites are in place.

---

## Complete Component Inventory

### Components to DELETE

| File | Reason |
|------|--------|
| `src/client/external-spa/src/auth/portal-auth.ts` | Entire portal implicit grant flow — replaced by MSAL |

### Components to MODIFY (SPA)

| File | Change |
|------|--------|
| `src/client/external-spa/src/auth/bff-client.ts` | Replace `getPortalToken()` / `clearPortalTokenCache()` calls with MSAL `acquireTokenSilent()` |
| `src/client/external-spa/src/config.ts` | Remove `PORTAL_CLIENT_ID`. Add `MSAL_CLIENT_ID`, `MSAL_TENANT_ID`, `MSAL_BFF_SCOPE` |
| `src/client/external-spa/src/main.tsx` | Wrap app in `MsalProvider` from `@azure/msal-react` |
| `src/client/external-spa/.env.development` | Remove `VITE_PORTAL_CLIENT_ID`. Add `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_TENANT_ID`, `VITE_MSAL_BFF_SCOPE` |
| `src/client/external-spa/.env.production.local` | Same as `.env.development` |
| `src/client/external-spa/.env.production` | Add CI placeholders for MSAL vars (replace portal vars) |

### Components to ADD (SPA)

| File | Purpose |
|------|---------|
| `src/client/external-spa/src/auth/msal-config.ts` | MSAL `PublicClientApplication` instance + scopes config |
| `src/client/external-spa/src/auth/msal-auth.ts` | `acquireToken()` helper wrapping MSAL silent + interactive fallback |
| `src/client/external-spa/src/components/AuthGuard.tsx` | MSAL-aware auth gate — shows login prompt if unauthenticated, wraps app content |

### Components to MODIFY (BFF)

| File | Change |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Api/Filters/ExternalCallerAuthorizationFilter.cs` | Remove `PortalTokenValidator` dependency. Read identity from `HttpContext.User` claims (standard Azure AD JWT claims) |
| `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/PortalTokenValidator.cs` | **Delete or convert to no-op** — no longer validates tokens (Azure AD middleware handles this) |
| `src/server/api/Sprk.Bff.Api/appsettings.json` (or template) | Verify `AzureAd.ClientId`, `AzureAd.Audience`, `AzureAd.TenantId = "common"` |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Ensure `AddMicrosoftIdentityWebApi()` is wired; remove any portal-specific token middleware |

### Components to ADD (BFF — Data Endpoints)

All routes are added to the existing `/api/v1/external/` route group with shared `ExternalCallerAuthorizationFilter`.

| New Endpoint | Method | Description |
|--------------|--------|-------------|
| `/api/v1/external/projects` | GET | List all projects the caller has access to |
| `/api/v1/external/projects/{id}` | GET | Single project (verified against caller's access) |
| `/api/v1/external/projects/{id}/documents` | GET | Documents for a project |
| `/api/v1/external/projects/{id}/events` | GET | Events/tasks for a project |
| `/api/v1/external/projects/{id}/contacts` | GET | Contacts associated with a project |
| `/api/v1/external/projects/{id}/organizations` | GET | Organizations associated with a project |
| `/api/v1/external/projects/{id}/events` | POST | Create a new event on a project |
| `/api/v1/external/events/{id}` | PATCH | Update an existing event |

### Power Pages Configuration Changes (Manual)

| Change | Details |
|--------|---------|
| **Remove** CIAM identity provider | Remove Entra External ID (`ciamlogin.com`) from portal identity providers |
| **Add** Entra B2B identity provider | Configure main tenant (`login.microsoftonline.com`) as the portal's sign-in IDP |
| **Update** CSP site setting | Add `https://login.microsoftonline.com` to `connect-src` in `HTTP/Content-Security-Policy` |
| **Remove** implicit grant site settings | `ImplicitGrantFlow/RegisteredClientId`, `Connector/ImplicitGrantFlowEnabled`, `ImplicitGrantFlow/TokenExpirationTime` — no longer needed |
| **Remove** Web API site settings | `Webapi/sprk_project/enabled` etc. — no longer relevant (SPA uses BFF, not `/_api/*`) |

---

## New Component Specifications

### `src/client/external-spa/src/auth/msal-config.ts`

```typescript
// MSAL PublicClientApplication configuration
// Authority: main workforce tenant — B2B guest tokens are issued from here
// Redirect URI: the portal URL where MSAL sends the auth code response
// Scopes: the BFF API scope (api://{BFF_CLIENT_ID}/access_as_external_user)

import { PublicClientApplication, Configuration } from "@azure/msal-browser";
import { MSAL_CLIENT_ID, MSAL_TENANT_ID } from "../config";

export const msalConfig: Configuration = {
  auth: {
    clientId: MSAL_CLIENT_ID,
    authority: `https://login.microsoftonline.com/${MSAL_TENANT_ID}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: "sessionStorage",
    storeAuthStateInCookie: false,
  },
};

export const msalInstance = new PublicClientApplication(msalConfig);
```

### `src/client/external-spa/src/auth/msal-auth.ts`

```typescript
// Token acquisition helper for BFF API calls
// Uses acquireTokenSilent first; falls back to acquireTokenRedirect on InteractionRequired
// Replaces getPortalToken() from portal-auth.ts

import { InteractionRequiredAuthError } from "@azure/msal-browser";
import { msalInstance } from "./msal-config";
import { MSAL_BFF_SCOPE } from "../config";

export async function acquireBffToken(): Promise<string> {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) {
    // No account — trigger interactive login
    await msalInstance.acquireTokenRedirect({ scopes: [MSAL_BFF_SCOPE] });
    throw new Error("Redirecting to login");
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes: [MSAL_BFF_SCOPE],
      account: accounts[0],
    });
    return result.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      await msalInstance.acquireTokenRedirect({ scopes: [MSAL_BFF_SCOPE] });
      throw new Error("Redirecting to login");
    }
    throw err;
  }
}
```

### `src/client/external-spa/src/config.ts` (updated shape)

```typescript
export const BFF_API_URL: string = import.meta.env.VITE_BFF_API_URL ?? "https://spe-api-dev-67e2xz.azurewebsites.net";
export const MSAL_CLIENT_ID: string = import.meta.env.VITE_MSAL_CLIENT_ID ?? "";
export const MSAL_TENANT_ID: string = import.meta.env.VITE_MSAL_TENANT_ID ?? "common";
export const MSAL_BFF_SCOPE: string = import.meta.env.VITE_MSAL_BFF_SCOPE ?? "";
export const APP_VERSION = "1.0.0";
// REMOVED: PORTAL_CLIENT_ID
```

### `bff-client.ts` token acquisition change

```typescript
// BEFORE (portal implicit grant):
import { getPortalToken, clearPortalTokenCache } from "./portal-auth";
const token = await getPortalToken();
// on 401: clearPortalTokenCache(); freshToken = await getPortalToken();

// AFTER (MSAL):
import { acquireBffToken } from "./msal-auth";
const token = await acquireBffToken();
// on 401: freshToken = await acquireBffToken(); (MSAL handles cache invalidation)
```

### `ExternalCallerAuthorizationFilter.cs` claims change

```csharp
// BEFORE: validates portal JWT via PortalTokenValidator, extracts email from portal claims
var portalClaims = await _tokenValidator.ValidateAsync(bearerToken);
var email = portalClaims.Email;

// AFTER: Azure AD middleware has already validated the JWT. Read standard claims.
var email = context.HttpContext.User.FindFirst("preferred_username")?.Value
         ?? context.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
// B2B guest tokens from main tenant include upn / preferred_username = external user's email
```

---

## Prerequisites Before Writing Code

These must be completed by an administrator before any code changes are made:

### 1. SPA App Registration (new)

Create in Azure portal → main Spaarke tenant (`a221a95e-6abc-4434-aecc-e48338a1b2f2`):

- **Name**: `Spaarke External Workspace SPA`
- **Supported account types**: Accounts in this organizational directory only (single tenant)
- **Platform**: Single-page application (SPA)
- **Redirect URI**: `https://sprk-external-workspace.powerappsportals.com` (and `http://localhost:3000` for dev)
- **Note the Application (client) ID** → this becomes `VITE_MSAL_CLIENT_ID`

### 2. BFF App Registration — Expose API Scope

On the existing BFF app registration (`#{API_APP_ID}#`):

- **Expose an API** → Add scope: `access_as_external_user`
- **Authorized client applications** → Add the SPA client ID
- **Note the full scope URI**: `api://{BFF_APP_ID}/access_as_external_user` → this becomes `VITE_MSAL_BFF_SCOPE`

### 3. Power Pages Identity Provider

In Power Pages admin center → Authentication → Identity providers:
- Remove or disable Entra External ID (CIAM) provider
- Add Microsoft Entra / Azure Active Directory provider using main tenant
- Set as default provider so sign-in goes directly to Microsoft login

### 4. Test B2B Guest User

Invite `testuser1@spaarke.com` as a B2B guest to the main Spaarke tenant (if not already a guest). Verify the user appears in Azure AD guest accounts. Verify there is a Contact record in Dataverse with matching email and an active `sprk_externalrecordaccess` record.

---

## Env File Values (After Migration)

### App Registration Values (confirmed March 2026)

| App | Client ID |
|-----|-----------|
| SPA (`spaarke-external-access-SPA`) | `f306885a-8251-492c-8d3e-34d7b476ffd0` |
| BFF API (`SDAP-BFF-SPE-API`) | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Tenant (main Spaarke workforce) | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| BFF scope (existing `SDAP.Access`) | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access` |

### `.env.development`

```
VITE_BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
VITE_MSAL_CLIENT_ID=f306885a-8251-492c-8d3e-34d7b476ffd0
VITE_MSAL_TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
VITE_MSAL_BFF_SCOPE=api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access
```

### `.env.production` (CI placeholders — committed)

```
VITE_BFF_API_URL=#{BFF_API_URL}#
VITE_MSAL_CLIENT_ID=#{MSAL_CLIENT_ID}#
VITE_MSAL_TENANT_ID=#{MSAL_TENANT_ID}#
VITE_MSAL_BFF_SCOPE=#{MSAL_BFF_SCOPE}#
```

### `.env.production.local` (gitignored — local dev overrides for production build)

```
VITE_BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
VITE_MSAL_CLIENT_ID=f306885a-8251-492c-8d3e-34d7b476ffd0
VITE_MSAL_TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
VITE_MSAL_BFF_SCOPE=api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access
```

---

## BFF Route Group Architecture

The `/api/v1/external/` route group is the authorization boundary for all external callers. New features are added to the group — no per-route auth config needed.

```csharp
// Program.cs — route group definition
var externalGroup = app.MapGroup("/api/v1/external")
    .RequireAuthorization()                    // Azure AD JWT required (Microsoft.Identity.Web)
    .AddExternalCallerAuthorizationFilter();   // Resolves access records, injects ExternalCallerContext

// Endpoints register themselves into the group:
externalGroup.MapGet("/projects", ExternalProjectsEndpoint.Handle);
externalGroup.MapGet("/projects/{id}", ExternalProjectEndpoint.Handle);
externalGroup.MapGet("/projects/{id}/documents", ExternalDocumentsEndpoint.Handle);
// etc.
```

`ExternalCallerAuthorizationFilter`:
1. Reads `preferred_username` or `email` claim from `HttpContext.User` (standard Azure AD JWT claims)
2. Looks up the Contact record in Dataverse by email
3. Loads all active `sprk_externalrecordaccess` records for that Contact
4. Injects `ExternalCallerContext` (contactId + project access map) into the endpoint filter context
5. If no Contact or no access records → returns 403

Each endpoint uses the injected `ExternalCallerContext` to scope its Dataverse query.

---

## What Does NOT Change

- `vite.config.ts` — IIFE format, defer attribute, custom plugins — unchanged
- `web-api-client.ts` — all data calls already go through `bffApiCall` — unchanged
- All page and component files — no auth logic in them — unchanged
- BFF endpoint structure — route paths stay the same
- `appsettings.template.json` `AzureAd` section — already `TenantId: "common"`, already correct for B2B
- Dataverse schema — no changes
- BFF deployed URL — unchanged
- Power Pages portal URL — unchanged
- `sprk_externalrecordaccess` access control model — unchanged (BFF filter still reads these)

---

## Reference Links

- [MSAL auth code + PKCE for SPAs](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)
- [Power Pages — configure identity providers](https://learn.microsoft.com/en-us/power-pages/security/authentication/configure-site)
- [Microsoft.Identity.Web — protect web API](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-web-api-aspnet-core-protect-api)
- [MSAL.js — SPA auth code flow](https://learn.microsoft.com/en-us/entra/identity-platform/tutorial-v2-javascript-auth-code)

---

*Document created: March 2026 | For compaction recovery: this file describes the planned B2B + MSAL auth migration. Read before resuming any implementation in this project.*
