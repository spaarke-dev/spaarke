# External Access SPA Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Architecture of the Secure Project Workspace — a React 18 SPA for external stakeholders hosted on Power Pages

---

## Overview

The External Access SPA is a React 18 single-page application hosted on Power Pages that gives external stakeholders — law firm attorneys, clients, and advisers — a secure workspace for accessing Secure Projects. It is the external-facing complement to the internal Corporate Workspace (LegalWorkspace).

External users are **Azure AD B2B guests** in the main Spaarke workforce tenant. They authenticate with their existing Microsoft 365 credentials (SSO) via MSAL authorization code + PKCE and receive access tokens scoped to the BFF API. The SPA calls the BFF (`Sprk.Bff.Api`) for all data and business logic — there is no direct access to Dataverse from the browser.

The SPA source lives at `src/client/external-spa/`. It is built with Vite, inlined into a single HTML file via `vite-plugin-singlefile`, and deployed as a Dataverse web resource (`sprk_externalworkspace`).

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| Entry point | `src/client/external-spa/src/main.tsx` | MSAL initialization, React 18 `createRoot`, `MsalProvider` wrapping |
| Root shell | `src/client/external-spa/src/App.tsx` | `FluentProvider` (v9 with dark mode), `HashRouter`, `AuthGuard`, routes |
| Home page | `src/client/external-spa/src/pages/WorkspaceHomePage.tsx` | Project list with access levels via `useExternalContext()` |
| Project page | `src/client/external-spa/src/pages/ProjectPage.tsx` | Tabbed project view (Documents, Events, Tasks, Contacts) |
| MSAL config | `src/client/external-spa/src/auth/msal-config.ts` | `PublicClientApplication` instance, tenant/client IDs, sessionStorage cache |
| BFF client | `src/client/external-spa/src/auth/bff-client.ts` | `bffApiCall()` with Bearer token attachment |
| Auth guard | `src/client/external-spa/src/components/AuthGuard.tsx` | Redirects unauthenticated users via MSAL |
| Config | `src/client/external-spa/src/config.ts` | `BFF_API_URL`, `MSAL_CLIENT_ID`, `MSAL_BFF_SCOPE` |

---

## Data Flow

1. Browser loads SPA from Power Pages (serves the inlined HTML/JS bundle)
2. `main.tsx` calls `msalInstance.initialize()` — processes any in-flight auth code redirect response
3. `AuthGuard` checks MSAL `accounts[]` — triggers redirect to Entra B2B login if empty
4. After login, MSAL stores tokens in `sessionStorage` (per-tab isolation)
5. `WorkspaceHomePage` mounts — `useExternalContext()` calls `GET /api/v1/external/me`
6. `acquireBffToken()` uses `acquireTokenSilent()` with redirect fallback on `InteractionRequiredAuthError`
7. BFF `ExternalCallerAuthorizationFilter` validates JWT, resolves Dataverse Contact by `preferred_username` claim, loads project participations from Redis (60s TTL, Dataverse fallback)
8. Response includes `contactId`, `email`, and `projects[]` with access levels
9. User navigates to project — `ProjectPage` loads documents, events, contacts via BFF
10. All data routes through BFF API — no direct `/_api/` calls to Dataverse

---

## Identity Model: Entra B2B

External users are **Azure AD B2B guest accounts** in the main Spaarke workforce tenant (`a221a95e-6abc-4434-aecc-e48338a1b2f2`). They authenticate with their existing Microsoft 365 credentials — no new account creation, SSO if already signed in.

**Auth flow**: Authorization code + PKCE — tokens stored in `sessionStorage` — every BFF call attaches Bearer token — `ExternalCallerAuthorizationFilter` resolves Contact by email — loads project participations from Redis (60s TTL, falls back to Dataverse `sprk_externalrecordaccess`).

**App registrations**:

| App | Purpose | App ID |
|-----|---------|--------|
| `spaarke-external-access-SPA` | SPA client (public, PKCE) | `f306885a-8251-492c-8d3e-34d7b476ffd0` |
| `SDAP-BFF-SPE-API` | BFF API resource | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |

**Limitation**: External users without Microsoft accounts cannot authenticate. Non-Microsoft users would require a B2C configuration.

---

## Three-Plane Access Model

| Plane | What It Controls | Who Manages It |
|-------|-----------------|----------------|
| **Plane 1 — Power Pages** | Dataverse record access via parent-chain table permissions | Automatic (cascades from participation record) |
| **Plane 2 — SPE Files** | SharePoint Embedded container membership | BFF-managed via Graph API on grant/revoke |
| **Plane 3 — AI Search** | Azure AI Search query scope | BFF constructs `search.in` filter at query time from active participations |

**Parent-chain model** (Plane 1): Creating one `sprk_externalrecordaccess` record + assigning the web role grants the contact access to the parent project and all child records (documents, events) automatically. Revoking = deactivating that record. No per-record grants needed.

**Access level enforcement**: Access level (`ViewOnly`, `Collaborate`, `FullAccess`) is embedded in the `/me` response. Client-side capability flags (`canUpload`, `canDownload`, etc.) are UX-only. Actual enforcement is server-side in the BFF via `ExternalCallerAuthorizationFilter` and per-endpoint access checks.

---

## BFF Data Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/v1/external/me` | User context — contactId, email, project access list |
| `GET` | `/api/v1/external/projects` | All projects the caller has access to |
| `GET` | `/api/v1/external/projects/{id}` | Single project record |
| `GET` | `/api/v1/external/projects/{id}/documents` | Project documents |
| `GET` | `/api/v1/external/projects/{id}/events` | Project events |
| `GET` | `/api/v1/external/projects/{id}/contacts` | Project participants |
| `POST` | `/api/v1/external/projects/{id}/events` | Create event |
| `PATCH` | `/api/v1/external/events/{id}` | Update event |

**Management endpoints** (internal Corporate Workspace, not the external SPA):

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/external-access/grant` | Grant contact access to project |
| `POST` | `/api/v1/external-access/revoke` | Revoke contact access |
| `POST` | `/api/v1/external-access/invite` | Invite new external user |
| `POST` | `/api/v1/external-access/provision-project` | Provision SPE + infrastructure |
| `POST` | `/api/v1/external-access/close-project` | Close project + cascade revoke |

---

## Power Pages Hosting Constraints

| Constraint | Impact |
|-----------|--------|
| Single-file SPA hosting | `HashRouter` required — `BrowserRouter` causes 404 on direct URL navigation |
| No server-side routing | All routes must be hash-based (`#/project/{id}`) |
| Max parent-chain depth ~4 | Spaarke uses 2-3 levels — well within limit |
| No polymorphic parent lookups | Use explicit single-type relationships only |
| Web role assignment has no expiry | Use `sprk_externalrecordaccess.sprk_expirydate` + scheduled deactivation |
| B2B guests require Microsoft account | Non-Microsoft external users would need B2C |

**Build output**: `vite-plugin-singlefile` inlines all JS/CSS into a single `dist/index.html` (~800 KB uncompressed, ~1.1 MB base64-encoded in Dataverse). Within the 5 MB web resource limit.

**Deployment**: `npm run build` followed by `scripts/Deploy-ExternalWorkspaceSpa.ps1` (base64 encode, Dataverse Web API, PublishXml). Not deployed via `pac pages upload-code-site` due to assembly conflict in PAC CLI 1.46.x.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Identity provider | Entra B2B guests (not B2C) | External users already have M365 accounts — SSO, no separate tenant | — |
| Data access path | BFF-only (not Power Pages `/_api/`) | Single auditable path, managed identity auth, no field whitelisting | — |
| Auth grant type | Authorization code + PKCE (not implicit) | Implicit is deprecated; MSAL handles silent refresh and MFA | — |
| SPA routing | HashRouter (not BrowserRouter) | Power Pages single-file hosting returns 404 for pushState paths | — |
| Token storage | sessionStorage (not localStorage) | Per-tab isolation, avoids leakage on shared workstations | — |
| Auth filter pattern | Per-endpoint filter (not global middleware) | `ExternalCallerAuthorizationFilter` follows ADR-008 | ADR-008 |
| Participation cache | Redis 60s TTL | Avoids Dataverse query per BFF call; invalidated on grant/revoke/close | ADR-009 |

---

## Constraints

- **MUST** use HashRouter — BrowserRouter causes 404 on Power Pages
- **MUST** route all data through BFF API — no direct `/_api/` calls to Dataverse
- **MUST** use sessionStorage for token cache — per-tab isolation for shared workstations
- **MUST** enforce access levels server-side in BFF — client-side flags are UX-only
- **MUST NOT** use Power Pages `/_api/` proxy for data access (single data path through BFF)
- **MUST NOT** use `pac pages upload-code-site` for deployment (PAC CLI assembly conflict)

---

## Known Pitfalls

| Pitfall | Symptom | Resolution |
|---------|---------|------------|
| BrowserRouter instead of HashRouter | 404 on direct navigation to `/project/{id}` | Always use `HashRouter` — Power Pages cannot handle pushState routing |
| Implicit grant instead of PKCE | Deprecated flow, token refresh failures | Ensure SPA app registration uses authorization code + PKCE; implicit grant must be disabled |
| Missing `ExternalCallerAuthorizationFilter` on new endpoint | Endpoint accessible without project access verification | Every `/api/v1/external/*` endpoint must have the filter applied per ADR-008 |
| Redis cache not invalidated on revoke | Revoked user retains access for up to 60 seconds | Grant/revoke/close operations must explicitly invalidate the Redis participation cache |
| Stale `dist/index.html` after rebuild | Old SPA version served | Re-run `Deploy-ExternalWorkspaceSpa.ps1` and verify web resource version in Dataverse |
| SPA exceeds 5 MB web resource limit | Deployment fails | Review bundle size; `vite-plugin-singlefile` output should be ~800 KB; check for unnecessary dependencies |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | BFF API | `/api/v1/external/*` endpoints | All data and business logic |
| Depends on | Entra B2B | MSAL authorization code + PKCE | Guest account authentication |
| Depends on | Power Pages | Static file hosting (web resource) | SPA deployment target |
| Depends on | Redis | 60s TTL participation cache | Invalidated on grant/revoke/close |
| Consumed by | External stakeholders | Browser SPA | Attorneys, clients, advisers |
| Managed by | Corporate Workspace | `/api/v1/external-access/*` endpoints | Grant, revoke, invite, provision, close |

---

## Related

- [uac-access-control.md](uac-access-control.md) — Unified Access Control model (three-plane detail)
- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Auth patterns including MSAL ssoSilent for code pages
- [`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`](../guides/EXTERNAL-ACCESS-ADMIN-SETUP.md) — Power Pages config, table permissions, site settings
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF API endpoint patterns

---

*Last Updated: April 5, 2026*
