# External Access SPA Architecture

> **Last Updated**: July 20, 2026
> **Last Reviewed**: 2026-07-20
> **Reviewed By**: spaarke-SPA-external-access-platform-r1 (task 042 — rewrite to the shipped SWA + CIAM platform)
> **Status**: Current
> **Purpose**: Architecture of the Secure Project Workspace — a React 18 SPA for external stakeholders hosted on Azure Static Web Apps and authenticated with Microsoft Entra External ID (CIAM)

---

## Overview

The External Access SPA is a React 18 single-page application that gives external stakeholders — law firm attorneys, clients, and advisers — a secure workspace for accessing Secure Projects. It is the external-facing complement to the internal Corporate Workspace (LegalWorkspace).

The SPA is hosted on **Azure Static Web Apps (SWA)**. External users are **local accounts in a dedicated Microsoft Entra External ID (CIAM) tenant** (`spaarkeextid`) — **not** Entra B2B guests in the Spaarke workforce tenant. They authenticate against the CIAM authority (`*.ciamlogin.com`) via MSAL authorization code + PKCE and receive access tokens scoped to the BFF API. The SPA calls the BFF (`Sprk.Bff.Api`) for all data and business logic — there is no direct access to Dataverse or SharePoint Embedded from the browser.

The external portal is a **pure BFF broker** (ADR-028 Amendment A1): the external user's token authenticates **only** to the BFF and is never exchanged for a downstream Graph/SPE/Dataverse token — there is **no OBO on the external path**. All external-surface SPE and Dataverse access is **app-only / managed identity**. Consequently, **no workforce Entra B2B guest is ever created** for an external user.

The SPA source lives at `src/client/external-spa/`. It is built with Vite and deployed as a static site to SWA (multi-file `dist/`, clean-URL routing), replacing the retired Power Pages web-resource (`sprk_externalworkspace`, historical).

> **Documented exception to [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)**: This SPA intentionally uses MSAL directly with `sessionStorage` rather than `@spaarke/auth` with `localStorage` (the internal v2 contract). Rationale: the external threat model differs from internal users (shared/kiosk devices possible, shorter session expectations, a separate CIAM identity). Do NOT migrate this SPA to `@spaarke/auth`. Do NOT replicate this `sessionStorage` + direct-MSAL pattern in internal Spaarke surfaces.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| Entry point | `src/client/external-spa/src/main.tsx` | MSAL initialization, React 18 `createRoot`, `MsalProvider` wrapping |
| Root shell | `src/client/external-spa/src/App.tsx` | `FluentProvider` (v9 with light/dark cascade), **`BrowserRouter`**, `AuthGuard`, routes, in-app 404 view |
| Home page | `src/client/external-spa/src/pages/WorkspaceHomePage.tsx` | Project list with access levels via `useExternalContext()` |
| Project page | `src/client/external-spa/src/pages/ProjectPage.tsx` | Tabbed project view (Documents, To-dos, Contacts) |
| MSAL config | `src/client/external-spa/src/auth/msal-config.ts` | `PublicClientApplication` instance, CIAM authority + `knownAuthorities`, `sessionStorage` cache |
| BFF client | `src/client/external-spa/src/auth/bff-client.ts` | `bffApiCall()` with Bearer token attachment |
| Auth guard | `src/client/external-spa/src/components/AuthGuard.tsx` | Redirects unauthenticated users to CIAM login via MSAL |
| Config | `src/client/external-spa/src/config.ts` | `BFF_API_URL`, `MSAL_CLIENT_ID`, `MSAL_AUTHORITY`, `MSAL_TENANT_ID`, `MSAL_BFF_SCOPE` (all env-injected) |
| SWA runtime config | `src/client/external-spa/staticwebapp.config.json` | `navigationFallback` rewrite + `globalHeaders` (CSP/Referrer/nosniff) |

---

## Data Flow

1. Browser loads the SPA from the SWA origin (`green-dune-0c4f1221e.7.azurestaticapps.net`). Deep links resolve because SWA `navigationFallback` rewrites unmatched routes to `/index.html`.
2. `main.tsx` calls `msalInstance.initialize()` — processes any in-flight auth code redirect response.
3. `AuthGuard` checks MSAL `accounts[]` — triggers redirect to the CIAM login if empty. The intended deep-link route is captured (per-tab `sessionStorage`) before the redirect and restored after auth (with an open-redirect guard restoring only in-app relative paths).
4. After login, MSAL stores tokens in `sessionStorage` (per-tab isolation).
5. `WorkspaceHomePage` mounts — `useExternalContext()` calls `GET /api/v1/external/me`.
6. `acquireBffToken()` uses `acquireTokenSilent()` with redirect fallback on `InteractionRequiredAuthError`.
7. The BFF validates the CIAM JWT via its `Ciam` scheme, then `ExternalCallerAuthorizationFilter` resolves the Dataverse Contact by the stable CIAM `oid` claim (`Contact.sprk_externalobjectid`) and loads project participations from Redis (60s TTL, Dataverse fallback).
8. The `/me` response includes `contactId`, `email`, and `projects[]` with access levels (`ViewOnly`/`Collaborate`/`FullAccess`).
9. User navigates to a project — `ProjectPage` loads documents, to-dos, contacts, and organizations via the BFF.
10. All data routes through the BFF API — no direct calls to Dataverse or SPE from the browser.

---

## Identity Model: Microsoft Entra External ID (CIAM)

External users are **local accounts in a dedicated Entra External ID (CIAM) tenant**, distinct from the Spaarke workforce tenant. This model **supersedes the retired Entra B2B guest model** (ADR-028 Amendment A1).

| Item | Value |
|------|-------|
| CIAM external tenant | `spaarkeextid` (`spaarkeextid.onmicrosoft.com`) |
| CIAM tenant ID | `7052feba-bfc4-43e0-b09e-65014b429131` |
| CIAM authority (host) | `spaarkeextid.ciamlogin.com` (declared in MSAL `knownAuthorities` — a non-default authority) |
| SPA MSAL authority | `https://spaarkeextid.ciamlogin.com/7052feba-bfc4-43e0-b09e-65014b429131` |
| BFF issuer validated | `https://spaarkeextid.ciamlogin.com/7052feba-bfc4-43e0-b09e-65014b429131/v2.0` |
| Auth flow | Authorization code + PKCE |

**App registrations (in the CIAM tenant):**

| App | Purpose | App ID |
|-----|---------|--------|
| External SPA public client | SPA client (public, PKCE) | `bd57e54e-b339-4500-b55c-e451009fd907` |
| BFF API | Protected web API (scope `SDAP.Access`) | `4a4d5126-91b0-4865-8e3a-134b7209013e` |
| CIAM Graph provisioner | App-only user provisioning (`User.ReadWrite.All`) | `e63e6eb1-be25-4214-80a8-a6d609034bb9` |

The BFF API app exposes App ID URI `api://4a4d5126-91b0-4865-8e3a-134b7209013e` with scope `SDAP.Access`. It sets `requestedAccessTokenVersion: 2`, so the access-token `aud` is the **client-id GUID** (`4a4d5126-…`), which the BFF validates as `Ciam:Audience`.

**Contact resolution by stable `oid`**: `ExternalCallerAuthorizationFilter` resolves the CIAM caller to a Dataverse Contact by the immutable `oid` claim, stored on `Contact.sprk_externalobjectid` (String/100). Email (`preferred_username` / `upn` / `email`) is a **first-login fallback only** — it then binds the `oid` onto the Contact. Once an `oid` is bound, a mismatched email neither redirects resolution nor grants access. A token carrying neither `oid` nor a usable email is rejected (401).

**Broker-only invariant**: The external user's token is used only to authenticate to the BFF and is never exchanged downstream (no OBO on the external path). All external SPE + Dataverse reads are app-only / managed identity, and no per-external-user workforce B2B guest is provisioned.

---

## BFF Authentication Schemes (additive)

The BFF runs **two** JWT bearer schemes side by side (`Infrastructure/DI/AuthorizationModule.cs`):

| Scheme | Tenant / authority | Applies to |
|--------|--------------------|-----------|
| Workforce default (`AddMicrosoftIdentityWebApi`) | Workforce Entra tenant | Internal surfaces, incl. the `/api/v1/external-access` management group |
| `Ciam` (`AuthSchemes.Ciam`) | Entra External ID (`*.ciamlogin.com`) | The `/api/v1/external` group only, pinned via the named policy `AuthPolicies.CiamExternal` |

The `Ciam` scheme is **additive** — it is appended to the existing workforce authentication builder (no third `AddAuthentication`), so the workforce default scheme is preserved for internal surfaces. The `AuthPolicies.CiamExternal` policy pins `AuthenticationSchemes = ["Ciam"]` + `RequireAuthenticatedUser` on the external route group, so a workforce token is rejected on `/api/v1/external/*` and a CIAM token is rejected on the workforce-default `/api/v1/external-access/*`. The default-scheme `PostConfigure<JwtBearerOptions>` audience-merge does **not** apply to the `Ciam` named options.

Cross-tenant provisioning uses `CiamGraphClientFactory` — an app-only MSAL confidential client built `WithCertificate` (Key Vault cert `ciam-graph-provisioner-cert` in `spaarke-spekvcert`, loaded by name, never a plaintext secret) `WithAuthority(ciamAuthority)` + `AcquireTokenForClient`. It is modeled on `SpeAdminTokenProvider.GetOrCreateMsalApp` per ADR-010 (reuse the established cross-tenant pattern).

---

## Three-Plane Access Model

| Plane | What It Controls | Who Manages It |
|-------|-----------------|----------------|
| **Plane 1 — Dataverse records** | Project + child record access via `sprk_externalrecordaccess` participation | BFF (app-only) — the auth filter resolves participations per request |
| **Plane 2 — SPE Files** | SharePoint Embedded document content | BFF-brokered **app-only** streaming (no external identity reaches SPE; no synthetic container membership written) |
| **Plane 3 — AI Search** | Azure AI Search query scope | BFF constructs the query filter at query time from active participations |

**Participation model** (Plane 1): a single active `sprk_externalrecordaccess` record (grantee = the **Contact** person) grants the Contact access to the parent project and its child records. Revoking = deactivating that record.

**Access level enforcement**: The access level (`ViewOnly` = `100000000`, `Collaborate` = `100000001`, `FullAccess` = `100000002`) is embedded in the `/me` response. Client-side capability flags are UX-only. Actual enforcement is server-side in the BFF via `ExternalCallerAuthorizationFilter` and per-endpoint checks (`ExternalCallerContext.HasProjectAccess` / `GetEffectiveRights`). Effective rights: ViewOnly → Read; Collaborate → Read + Create + Write; FullAccess → Read + Create + Write + Delete.

The Redis participation cache (ADR-009, 60s TTL) is invalidated on grant so a new grant is immediately visible.

---

## BFF Data Endpoints (`/api/v1/external` — `CiamExternal` policy + `ExternalCallerAuthorizationFilter`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/v1/external/me` | User context — `contactId`, `email`, project access list |
| `GET` | `/api/v1/external/projects` | All projects the caller has access to |
| `GET` | `/api/v1/external/projects/{id}` | Single project record |
| `GET` | `/api/v1/external/projects/{id}/documents` | Project documents (metadata) |
| `GET` | `/api/v1/external/projects/{id}/documents/{documentId}/content` | **Download** document bytes (authz-before-stream, app-only) |
| `GET` | `/api/v1/external/projects/{id}/todos` | Project to-dos (`sprk_todo`, regarding = project) |
| `POST` | `/api/v1/external/projects/{id}/todos` | Create a to-do (requires ≥ Collaborate) |
| `GET` | `/api/v1/external/projects/{id}/contacts` | Project participants |
| `GET` | `/api/v1/external/projects/{id}/organizations` | Organizations linked to project contacts |
| `PATCH` | `/api/v1/external/todos/{id}` | Update a to-do |

> **Note**: The to-do routes replaced the former event-based routes (`smart-todo-decoupling-r3`, FR-29). The SPA consumes `sprk_todo`, not `sprk_event`.

**Management endpoints** (internal Corporate Workspace, workforce default scheme — `/api/v1/external-access`):

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/external-access/invite-and-grant` | Core-user "Invite to Secure Workspace" — idempotent CIAM onboard **+** grant in one action |
| `POST` | `/api/v1/external-access/invite` | Onboard only (idempotent CIAM provision) |
| `POST` | `/api/v1/external-access/grant` | Grant a Contact access to a project |
| `POST` | `/api/v1/external-access/revoke` | Revoke a Contact's access |
| `POST` | `/api/v1/external-access/close-project` | Close project + cascade revoke |
| `POST` | `/api/v1/external-access/provision-project` | Provision SPE + infrastructure |

---

## Document Download — Authz-Before-Stream (NFR-03)

`GET /projects/{id}/documents/{documentId}/content` is the R1 file-content path. Its highest-consequence property is **authorization before any storage read**:

1. **Project access** — the caller must have a participation record for `{id}` (`HasProjectAccess`), else **403 with no bytes and no Graph call**.
2. **Document → project scoping** — the requested `documentId` must belong to `{id}` (an app-only Dataverse read that resolves **no** Graph pointer). A mismatch or non-existent document is a uniform 403 (does not leak document existence).
3. **Only after both checks pass** does the BFF resolve SPE pointers server-side (`DocumentStorageResolver.GetSpePointersAsync`) and stream the content **app-only** via `SpeFileStore.DownloadFileAsync` (`ISpeFileOperations`) — **not** the OBO `DownloadFileAsUserAsync` path.

The endpoint is keyed on `documentId`; **Graph pointers (`driveId`/`driveItemId`) are never added to the client DTO or exposed to the browser** (broker-only). Content is returned as `application/octet-stream` with an attachment filename (no inline rendering of untrusted external content). No synthetic `contact_{guid}` SPE container membership is written on the external path (removed with the broker-only design).

---

## Azure Static Web Apps Hosting

| Item | Value |
|------|-------|
| SWA resource | `swa-spaarke-external-spa-dev` (resource group `rg-spaarke-dev`) |
| Live host | `green-dune-0c4f1221e.7.azurestaticapps.net` |
| Routing | **`BrowserRouter`** (clean URLs) — no HashRouter |
| Deep-link resolution | SWA `navigationFallback.rewrite` → `/index.html` (excludes `/assets/*` and static file extensions) |
| Unknown paths | In-app 404 view (not a silent redirect home) |

**`staticwebapp.config.json`** `globalHeaders`:

```json
"Referrer-Policy": "no-referrer-or-same-origin",
"Content-Security-Policy": "frame-ancestors 'self'",
"X-Content-Type-Options": "nosniff"
```

**Deployment**: `.github/workflows/deploy-external-spa.yml` (`workflow_dispatch`). The workflow installs with `npm install --legacy-peer-deps` (per root CLAUDE.md §12), builds `src/client/external-spa` with the CIAM `VITE_*` build env, stages `staticwebapp.config.json` into `dist/`, and uploads via `Azure/static-web-apps-deploy` (`skip_app_build: true`). This replaces the retired `scripts/Deploy-ExternalWorkspaceSpa.ps1` (deleted — historical).

> **Retired (historical)**: Power Pages hosting of the external SPA — the `sprk_externalworkspace` web resource, the Power Pages site, `HashRouter`, and `Deploy-ExternalWorkspaceSpa.ps1` — is decommissioned. It is retained here only as historical context; it is not current guidance.

---

## Direct-Office Boundary — Out of Scope (E-3)

Per **ADR-028 Amendment A1 limitation E-3**, **direct-Office features for external users** are **permanently out of scope**: Word/Excel/PowerPoint for-Web co-authoring, desktop open via `webUrl`, user-identity Copilot grounding, and Microsoft Search. These require the user's **own workforce identity** reaching SPE (OBO/delegated), which the CIAM-only broker model deliberately does not provide. A future project needing them for external users must reintroduce workforce B2B guests for those users and file a superseding amendment.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Identity provider | Entra External ID (CIAM) local accounts | Azure AD B2C is end-of-sale; CIAM is the successor; broker-only design needs only a BFF-auth identity, no workforce guest | ADR-028 A1 |
| Broker model | App-only downstream (no OBO on external path) | External token authenticates only to the BFF; keeps external identity out of SPE/Graph/Dataverse | ADR-028 A1 |
| Hosting | Azure Static Web Apps | Clean-URL routing + CI/CD; replaces Power Pages web-resource | — |
| Data access path | BFF-only | Single auditable path, app-only auth, no field whitelisting | — |
| Auth grant type | Authorization code + PKCE | Implicit is deprecated; MSAL handles silent refresh and MFA | — |
| SPA routing | BrowserRouter + SWA navigationFallback | Clean URLs; deep links resolve via rewrite; unknown paths render in-app 404 | — |
| Token storage | sessionStorage (not localStorage) | **Intentional divergence from internal surfaces.** Per-tab isolation for shared/kiosk workstations. Internal surfaces use `localStorage` for cross-tab SSO (different threat model). See [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md). | ADR-028 (documented exception) |
| BFF CIAM validation | Second `Ciam` JwtBearer scheme, pinned to `/api/v1/external` | Additive to the workforce default; distinct issuer/audience | ADR-028 A1 |
| Auth filter pattern | Per-endpoint filter (not global middleware) | `ExternalCallerAuthorizationFilter` follows ADR-008 | ADR-008 |
| Participation cache | Redis 60s TTL | Avoids Dataverse query per BFF call; invalidated on grant | ADR-009 |
| Cross-tenant Graph | `CiamGraphClientFactory` (cert in Key Vault) | Workforce MI cannot reach the CIAM tenant; app-only cert per `SpeAdminTokenProvider` pattern | ADR-010 |

---

## Constraints

- **MUST** host on Azure Static Web Apps with `navigationFallback` rewrite; use **`BrowserRouter`** (never `HashRouter`).
- **MUST** authenticate external users against the CIAM authority (`*.ciamlogin.com`) via the `Ciam` scheme; resolve the Contact by stable `oid` (`sprk_externalobjectid`).
- **MUST** keep the external path broker-only — no OBO, all SPE/Dataverse access app-only; never provision a workforce B2B guest for an external user.
- **MUST** enforce Dataverse authorization **before** streaming file content; never expose Graph pointers to the browser.
- **MUST** route all data through the BFF API — no direct Dataverse/SPE calls from the browser.
- **MUST** use `sessionStorage` for the token cache — per-tab isolation. **Do NOT change this to `localStorage`** or migrate to `@spaarke/auth` (threat model differs).
- **MUST** enforce access levels server-side in the BFF — client-side flags are UX-only.

---

## Known Pitfalls

| Pitfall | Symptom | Resolution |
|---------|---------|------------|
| Missing `navigationFallback` with `BrowserRouter` | 404 on direct navigation to `/project/{id}` | `staticwebapp.config.json` must rewrite unmatched routes to `/index.html` (excluding assets) |
| Forgetting `knownAuthorities` for the CIAM host | MSAL rejects the `*.ciamlogin.com` OIDC metadata | Declare the CIAM authority host in MSAL `knownAuthorities` (it is a non-default authority) |
| Wrong token-audience config | 401 on all `/api/v1/external/*` calls | `Ciam:Audience` must be the BFF-API **client-id GUID** (`4a4d5126-…`, v2 tokens) |
| Missing `ExternalCallerAuthorizationFilter` on a new endpoint | Endpoint reachable without participation check | Every `/api/v1/external/*` endpoint applies the filter (ADR-008) |
| Redis cache not invalidated on grant | New grant not visible for up to 60s | Grant operations must invalidate the Redis participation cache |
| Reusing the OBO download path on the external surface | Broker-only invariant violated | Use app-only `SpeFileStore.DownloadFileAsync`, never `DownloadFileAsUserAsync` |
| Resolving Contact by email instead of `oid` | Wrong-Contact resolution / spoofable | Resolve by `oid`; email is a first-login fallback only |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | BFF API | `/api/v1/external/*` endpoints | All data and business logic |
| Depends on | Entra External ID (CIAM) | MSAL authorization code + PKCE | Local-account authentication |
| Depends on | Azure Static Web Apps | Static site hosting | SPA deployment target |
| Depends on | Redis | 60s TTL participation cache | Invalidated on grant/revoke/close |
| Depends on | SharePoint Embedded | App-only content streaming | Broker-only; no external identity reaches SPE |
| Consumed by | External stakeholders | Browser SPA | Attorneys, clients, advisers |
| Managed by | Corporate Workspace | `/api/v1/external-access/*` endpoints | Invite-and-grant, grant, revoke, provision, close |

---

## Related

- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 + **Amendment A1** (CIAM authority, broker-only invariant, E-3 boundary)
- [uac-access-control.md](uac-access-control.md) — Unified Access Control model (three-plane detail)
- [`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`](../guides/EXTERNAL-ACCESS-ADMIN-SETUP.md) — CIAM tenant, SWA, BFF, and onboarding configuration
- [`docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md`](../guides/EXTERNAL-ACCESS-SPA-GUIDE.md) — SPA developer guide
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF API endpoint patterns

---

*Last Updated: July 20, 2026*
