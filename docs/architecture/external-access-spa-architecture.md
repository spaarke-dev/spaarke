# External Access SPA — Architecture Reference

> **Domain**: Secure Project & External Access Platform (SDAP)
> **Status**: Active — production implementation
> **Last Updated**: 2026-03-19
> **Applies To**: `src/client/external-spa/`, `Sprk.Bff.Api` ExternalAccess endpoints

---

## Overview

The External Access SPA is a React 18 single-page application hosted on Power Pages that gives external stakeholders — law firm attorneys, clients, and advisers — a secure workspace for accessing Secure Projects. It is the external-facing complement to the internal Corporate Workspace.

External users are **Azure AD B2B guests** in the main Spaarke workforce tenant. They authenticate with their existing Microsoft 365 credentials (SSO) via MSAL and receive access tokens scoped to the BFF API. The SPA calls the BFF (`Sprk.Bff.Api`) for all data and business logic — there is no direct access to Dataverse from the browser.

---

## System Context

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  External User (attorney, client, adviser)                                  │
│  Browser — navigates to sprk-external-workspace.powerappsportals.com        │
└──────────────────────────────────┬──────────────────────────────────────────┘
                                   │ HTTPS
                          ┌────────┴────────┐
                          │  Power Pages    │
                          │  Code Site      │
                          │                 │
                          │  Serves SPA as  │
                          │  static HTML/JS │
                          │  (web resource) │
                          └────────┬────────┘
                                   │ React 18 SPA runs in browser
           ┌───────────────────────┴───────────────────────┐
           │                                               │
    MSAL (Entra B2B)                            BFF API calls
    token acquisition                      Bearer JWT token
           │                                               │
  login.microsoftonline.com             ┌──────────────────┴──────────────────┐
  (main Spaarke tenant)                 │  Sprk.Bff.Api (Azure App Service)   │
                                        │                                     │
                                        │  /api/v1/external/*                 │
                                        │  — project data (via Dataverse)     │
                                        │  — events, documents, contacts      │
                                        │                                     │
                                        │  /api/v1/external-access/*          │
                                        │  — grant/revoke access              │
                                        │  — invite users                     │
                                        │  — provision/close projects         │
                                        └──────────────────┬──────────────────┘
                                                           │ Managed Identity
                                                  ┌────────┴────────────────┐
                                                  │  Dataverse              │
                                                  │  (spaarkedev1.crm)      │
                                                  │                         │
                                                  │  sprk_project           │
                                                  │  sprk_document          │
                                                  │  sprk_event             │
                                                  │  sprk_externalrecordaccess │
                                                  │  Contact                │
                                                  └─────────────────────────┘
```

---

## Component Model

### SPA Components

```
src/client/external-spa/src/
│
├── main.tsx                         # Entry point
│   └─ Initializes MSAL, renders MsalProvider + App
│
├── App.tsx                          # Root component
│   ├─ FluentProvider (Fluent UI v9, ADR-021)
│   ├─ Dark mode — OS preference + manual toggle
│   ├─ HashRouter — required for Power Pages single-page hosting
│   ├─ AuthGuard — redirects unauthenticated users via MSAL
│   ├─ AppHeader — user display name, theme toggle
│   └─ Routes:
│       ├─ #/              → WorkspaceHomePage
│       └─ #/project/:id   → ProjectPage
│
├── pages/
│   ├─ WorkspaceHomePage.tsx         # Project list with access levels
│   │   └─ useExternalContext()      # Loads user context + project access
│   └─ ProjectPage.tsx               # Full project view
│       ├─ Tabs: Documents, Events, Tasks, Contacts
│       └─ useAccessLevel(projectId) # Resolves capability flags
│
├── components/
│   ├─ AppHeader.tsx                 # Header bar (user, theme)
│   ├─ AuthGuard.tsx                 # MSAL auth gate — triggers login redirect
│   └─ ErrorBoundary.tsx             # Graceful error display
│
├── auth/
│   ├─ msal-config.ts                # MSAL PublicClientApplication instance
│   ├─ msal-auth.ts                  # acquireBffToken() — silent + redirect fallback
│   └─ bff-client.ts                 # bffApiCall() + typed BFF endpoint wrappers
│
├── api/
│   └─ web-api-client.ts             # getProjects, getDocuments, getEvents, etc.
│                                    # All route through bffApiCall (not /_api/)
│
├── hooks/
│   ├─ useExternalContext.ts         # GET /api/v1/external/me — user + project list
│   ├─ useAccessLevel.ts             # Access level resolution + capability flags
│   └─ usePlaybookExecution.ts       # AI playbook triggering
│
├── types/                           # Shared TypeScript types (AccessLevel enum, ApiError)
└── config.ts                        # BFF_API_URL, MSAL_CLIENT_ID, MSAL_BFF_SCOPE
```

### BFF API Components (ExternalAccess)

```
Sprk.Bff.Api/
│
├── Api/ExternalAccess/
│   ├─ ExternalAccessEndpoints.cs    # Route group mapping (/external, /external-access)
│   ├─ ExternalUserContextEndpoint.cs    # GET /api/v1/external/me
│   ├─ ExternalProjectDataEndpoints.cs   # GET /projects, /projects/{id}, /documents, /events, /contacts
│   ├─ GrantExternalAccessEndpoint.cs    # POST /external-access/grant
│   ├─ RevokeExternalAccessEndpoint.cs   # POST /external-access/revoke
│   ├─ InviteExternalUserEndpoint.cs     # POST /external-access/invite
│   ├─ ProvisionProjectEndpoint.cs       # POST /external-access/provision-project
│   └─ ProjectClosureEndpoint.cs         # POST /external-access/close-project
│
├── Api/Filters/
│   └─ ExternalCallerAuthorizationFilter.cs  # Per-endpoint filter (ADR-008)
│
└── Infrastructure/ExternalAccess/
    ├─ ExternalCallerContext.cs         # Context object passed to handlers via HttpContext.Items
    ├─ ExternalParticipationService.cs  # Contact resolution + participation loading (Redis → Dataverse)
    ├─ ExternalDataService.cs           # Dataverse queries for project data (managed identity)
    └─ SpeContainerMembershipService.cs # SPE container permission management (Graph API)
```

### Component Interaction — Request Lifecycle

```
1. Browser loads SPA (Power Pages serves the HTML/JS bundle)
2. main.tsx calls msalInstance.initialize() — processes any auth redirect response
3. AuthGuard checks MSAL accounts[] — triggers redirect to Entra B2B login if empty
4. After login, MSAL stores tokens in sessionStorage
5. WorkspaceHomePage mounts → useExternalContext() fires
6. acquireBffToken() → msalInstance.acquireTokenSilent() → returns access token
7. GET /api/v1/external/me — Bearer token in Authorization header
8. ExternalCallerAuthorizationFilter (BFF):
     a. ASP.NET Core has already validated the JWT
     b. Extracts preferred_username claim (email)
     c. Resolves Dataverse Contact by email (ExternalParticipationService)
     d. Loads active project participations (Redis → Dataverse, 60s TTL)
     e. Stores ExternalCallerContext on HttpContext.Items
9. ExternalUserContextEndpoint.Handle() returns { contactId, email, projects[] }
10. WorkspaceHomePage renders project list
11. User clicks project → ProjectPage loads project data, documents, events
```

---

## Power Pages Harness

### How the SPA Is Hosted

The SPA is deployed to Dataverse as a **web resource** (`sprk_externalworkspace`) of type `Webpage HTML`. Power Pages serves this file directly when users navigate to the portal URL.

**Why web resource (not code site):**
PAC CLI's `pac pages upload-code-site` has an assembly conflict in version 1.46.x that prevents deployment when no prior code site exists. The Dataverse Web API approach (create/update `webresourceset` + `PublishXml`) is equivalent for a single-page SPA and matches the deployment pattern used by all other Spaarke web resources.

**Build output:**
`vite-plugin-singlefile` inlines all JS and CSS into a single `dist/index.html`. This produces a fully self-contained HTML file (~800 KB) that is base64-encoded and stored as the web resource content.

**Deployment:**
```
npm run build                              # Vite produces dist/index.html
scripts/Deploy-ExternalWorkspaceSpa.ps1    # base64 encode → Dataverse Web API → PublishXml
```

### HashRouter Requirement

Power Pages serves the SPA as a single static file. All navigation must be client-side hash-based:

```
https://sprk-external-workspace.powerappsportals.com/#/               → WorkspaceHomePage
https://sprk-external-workspace.powerappsportals.com/#/project/{id}   → ProjectPage
```

React Router's standard `BrowserRouter` (history API pushState) is **not supported** — the portal would return a 404 for paths like `/project/abc`. `HashRouter` is mandatory.

### Limitations of Power Pages Code Site Mode

| Feature | Available? | Alternative Used |
|---------|-----------|-----------------|
| Liquid templates | No | React components |
| Dataverse Basic/Advanced Forms | No | Custom React forms in ProjectPage.tsx |
| Power Pages Web API (`/_api/`) | Available but unused | BFF API used for all data |
| Server-side rendering / SEO | No | Client-side rendering only |
| Low-code Pages workspace | No | Code-only via PAC CLI / Web API |
| Power Platform Git integration | No | GitHub repo |
| Multi-language (built-in) | No | Client-side only if needed |

**Note on Power Pages Web API**: The `/_api/` proxy to Dataverse IS available for code sites and could be used for lightweight reads. The current implementation routes all data through the BFF instead. See [Design Tradeoffs](#design-tradeoffs) for rationale.

---

## Authentication Architecture

### Identity Model: Entra B2B

External users are **Azure AD B2B guest accounts** in the main Spaarke workforce tenant (`a221a95e-6abc-4434-aecc-e48338a1b2f2`). This means:

- External users authenticate with **their existing Microsoft 365 credentials** — no new account creation
- SSO: if the user is already signed into Microsoft 365, the portal sign-in is seamless (single click)
- Tokens are issued by the main Spaarke tenant, not a separate B2C tenant
- The user's `preferred_username` claim contains their home-tenant UPN (e.g. `alice@contoso.com`)

### App Registrations

| App | Purpose | App ID |
|-----|---------|--------|
| `spaarke-external-access-SPA` | SPA client (public client, PKCE) | `f306885a-8251-492c-8d3e-34d7b476ffd0` |
| `SDAP-BFF-SPE-API` | BFF API resource | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |

**SPA app registration required settings:**
- Platform: Single-page application (SPA)
- Redirect URIs: `https://sprk-external-workspace.powerappsportals.com`, `http://localhost:3000`
- Implicit grant: **disabled** (authorization code + PKCE only)
- API permissions: `api://1e40baad-.../SDAP.Access` (delegated, granted to guest users)

**BFF API app registration required settings:**
- Expose an API: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access`
- Authorized client applications: `f306885a-...` (the SPA)

### Token Flow: Authorization Code + PKCE

```
1. MSAL initializes in browser (msalInstance.initialize())
   └─ Processes any in-flight auth code redirect response

2. AuthGuard calls useMsal() → checks accounts[]
   └─ If empty: msalInstance.acquireTokenRedirect({ scopes: [SDAP.Access] })
   └─ Browser redirects to login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize
   └─ User authenticates (SSO if already signed in to Microsoft 365)
   └─ Entra issues authorization code → browser redirected back to portal
   └─ MSAL exchanges code for tokens (server-side exchange, PKCE verified)
   └─ Access + refresh tokens stored in sessionStorage

3. Every BFF API call:
   acquireBffToken()
   └─ msalInstance.acquireTokenSilent({ scopes: [SDAP.Access], account })
   └─ MSAL returns cached token (if not expired)
   └─ MSAL uses refresh token to get new access token (if expired, transparent)
   └─ On InteractionRequiredAuthError: triggers acquireTokenRedirect (MFA, consent, etc.)

4. BFF validates JWT:
   └─ ASP.NET Core Microsoft.Identity.Web: ValidAudience, ValidIssuer, signature
   └─ ExternalCallerAuthorizationFilter: resolves Contact by preferred_username claim
```

### Token Storage

`sessionStorage` (not `localStorage`). Tokens survive page refresh but are isolated per browser tab. Avoids token leakage across tabs in shared workstation scenarios common in legal environments.

### BFF Token Validation (ASP.NET Core)

```csharp
// Program.cs — registered via Microsoft.Identity.Web
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
```

Configuration in `appsettings.json`:
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c"
  }
}
```

The middleware validates:
- Token signature (against Entra JWKS endpoint)
- Audience (`api://1e40baad-.../SDAP.Access`)
- Issuer (main Spaarke tenant)
- Expiry

### ExternalCallerAuthorizationFilter (ADR-008)

After JWT validation, this per-endpoint filter runs to resolve the caller's Dataverse identity:

```
preferred_username claim (email)
  → ExternalParticipationService.ResolveContactByEmailAsync()
  → Dataverse Contact lookup by emailaddress1
  → Returns contactId (Guid)

contactId
  → ExternalParticipationService.GetParticipationsAsync()
  → Redis cache (60s TTL, keyed by contactId)
  → Fallback: Dataverse sprk_externalrecordaccess query
  → Returns List<ProjectParticipation>

Result stored as ExternalCallerContext on HttpContext.Items
```

Filter returns:
- `401` if `preferred_username` claim is missing
- `403` if no Dataverse Contact matches the email
- Passes through if resolved (empty participation list is allowed)

---

## Data Access Model

### Single Data Path: All Data via BFF

All Dataverse data accessed by the SPA flows through the BFF API. The SPA does NOT call the Power Pages `/_api/` proxy directly.

```
SPA → bffApiCall() → Bearer JWT → BFF API → Managed Identity → Dataverse
```

**Why BFF instead of `/_api/`:**
- Managed identity access (BFF → Dataverse) is auditable and centrally controlled
- BFF can enforce row-level access beyond what Power Pages table permissions provide
- Consistent with how internal workspace components access data
- Enables future AI, SPE, and business logic in the same request
- Power Pages Web API field names must be whitelisted in site settings; BFF has no such constraint

### BFF Data Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/v1/external/me` | User context — contactId, email, project access list |
| `GET` | `/api/v1/external/projects` | All projects the caller has access to |
| `GET` | `/api/v1/external/projects/{id}` | Single project record |
| `GET` | `/api/v1/external/projects/{id}/documents` | Project documents |
| `GET` | `/api/v1/external/projects/{id}/events` | Project events |
| `GET` | `/api/v1/external/projects/{id}/contacts` | Project participants (contacts) |
| `GET` | `/api/v1/external/projects/{id}/organizations` | Participant organizations |
| `POST` | `/api/v1/external/projects/{id}/events` | Create event |
| `PATCH` | `/api/v1/external/events/{id}` | Update event |

All routes in `/api/v1/external/*` require:
1. A valid Azure AD JWT (`RequireAuthorization()`)
2. `ExternalCallerAuthorizationFilter` — resolves Contact + participations

**Management endpoints** (internal use by Corporate Workspace, not the external SPA):

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/external-access/grant` | Grant contact access to project |
| `POST` | `/api/v1/external-access/revoke` | Revoke contact access |
| `POST` | `/api/v1/external-access/invite` | Invite new external user |
| `POST` | `/api/v1/external-access/provision-project` | Provision SPE + infrastructure |
| `POST` | `/api/v1/external-access/close-project` | Close project + cascade revoke |

### Dataverse Field Names (Actual Schema)

The BFF uses managed identity Dataverse access with correct logical field names. These differ from the friendly names visible in the model-driven app:

| Entity | Field (Logical) | Type | Notes |
|--------|----------------|------|-------|
| `sprk_project` | `sprk_projectid` | PK | |
| `sprk_project` | `sprk_projectname` | String | Display name |
| `sprk_project` | `sprk_projectnumber` | String | Reference number |
| `sprk_project` | `sprk_projectdescription` | Memo | Description |
| `sprk_project` | `sprk_issecure` | Boolean | External access enabled |
| `sprk_project` | `statecode` | State | Active = 0 |
| `sprk_document` | `sprk_documentname` | String | Display name |
| `sprk_document` | `sprk_filesummary` | Memo | AI summary |
| `sprk_document` | `_sprk_project_value` | Lookup | Project FK |
| `sprk_event` | `sprk_eventname` | String | Display name |
| `sprk_event` | `sprk_eventstatus` | Choice | Status option set |
| `sprk_event` | `_sprk_regardingproject_value` | Lookup | Project FK |
| `sprk_externalrecordaccess` | `_sprk_contact_value` | Lookup | Contact FK |
| `sprk_externalrecordaccess` | `_sprk_project_value` | Lookup | Project FK |

---

## Access Control Model

### Access Level Resolution

The user's access level for each project is determined at the BFF and embedded in the `/me` response:

```
ExternalParticipationService → Dataverse sprk_externalrecordaccess
→ sprk_accesslevel choice value
→ Serialized as string label: "ViewOnly" | "Collaborate" | "FullAccess"
→ Embedded in ExternalUserContextResponse.projects[].accessLevel
```

The SPA resolves capabilities client-side via `useAccessLevel(projectId)`:

```typescript
// AccessLevel enum → capability flags (UX enforcement only)
ViewOnly:    canUpload=false, canDownload=false, canCreate=false, canUseAi=false, canInvite=false
Collaborate: canUpload=true,  canDownload=true,  canCreate=true,  canUseAi=true,  canInvite=false
FullAccess:  canUpload=true,  canDownload=true,  canCreate=true,  canUseAi=true,  canInvite=true
```

**Client-side enforcement is UX only.** Actual security enforcement is server-side in the BFF via `ExternalCallerAuthorizationFilter` and per-endpoint access checks. The access level labels are used to conditionally show/hide UI controls (upload button, invite button, etc.).

### Project Access Verification (BFF)

For project-specific endpoints (`/projects/{id}/*`), the BFF verifies the caller has access to the requested project before executing the query:

```csharp
// ExternalProjectDataEndpoints — simplified
var callerContext = httpContext.Items[ExternalCallerContext.HttpContextItemsKey];
if (!callerContext.HasProjectAccess(projectId))
    return Results.Problem(statusCode: 403, ...);
// proceed with Dataverse query
```

`HasProjectAccess` checks the caller's `Participations` list (loaded by the filter) for the requested projectId.

---

## Three-Plane Access Model

External access operates across three planes. Understanding which plane handles what prevents misconfigurations and over-engineering.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PLANE 1 — Power Pages (Automatic)                                          │
│                                                                             │
│  Once table permissions are configured and the Contact has the web role:    │
│  • Contact can read their participation records (Level 0, Contact scope)    │
│  • Contact can read accessible projects (Level 1, Parent scope)             │
│  • Contact can read/create documents and events (Level 2, Parent scope)     │
│  • Contact can read reference/lookup data (Global scope)                    │
│                                                                             │
│  No BFF action needed — the parent-chain handles Dataverse record access    │
│  automatically for any /_api/ calls.                                        │
└────────────────────────────┬────────────────────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────────────────────┐
│  PLANE 2 — SharePoint Embedded Files (BFF-managed)                          │
│                                                                             │
│  Trigger: participation record created or deactivated                       │
│  • Grant: BFF adds Contact's Entra UPN to SPE container as Reader/Writer    │
│    via Graph API POST /storage/fileStorage/containers/{id}/permissions      │
│  • Revoke: BFF removes Contact from container                               │
│    via Graph API DELETE /storage/fileStorage/containers/{id}/permissions/{id}│
│                                                                             │
│  External user sharing must be enabled:                                     │
│  Set-SPOApplication -OverrideTenantSharingCapability $true                  │
└────────────────────────────┬────────────────────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────────────────────┐
│  PLANE 3 — AI Search (BFF-managed, query-time)                              │
│                                                                             │
│  Trigger: external user invokes AI search                                   │
│  • BFF reads active participation records for the caller                    │
│  • Constructs Azure AI Search filter at query time:                         │
│    Filter = "project_ids/any(p:search.in(p, '{projectId1},{projectId2}'))"  │
│  • Search results are automatically scoped to accessible projects           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Parent-Chain Table Permission Model

Power Pages table permissions cascade access through a parent-chain. A Contact with an active participation record automatically gets access to the parent project and all child records — no per-record grants needed.

```
Level 0: sprk_externalrecordaccess
         Scope: Contact (Contact.emailaddress1 = authenticated user)
         CRUD: Read only
         Web Roles: secure-project-viewer / collaborator / full

  └── Level 1: sprk_project
               Scope: Parent (via sprk_projectid lookup on participation)
               CRUD: Read only
               Web Roles: all three

        ├── Level 2: sprk_document
        │            Scope: Parent (via _sprk_project_value)
        │            CRUD: Viewer=Read | Collaborator=Read+Create | Full=Read+Create+Write
        │
        ├── Level 2: sprk_event
        │            Scope: Parent (via _sprk_regardingproject_value)
        │            CRUD: Viewer=Read | Collaborator=Read+Create+Write | Full=same
        │
        └── Level 2: sprk_externalrecordaccess (other participants)
                     Scope: Parent (via _sprk_project_value)
                     CRUD: Read only (see who else has access)
```

This model means: granting access = creating one `sprk_externalrecordaccess` record + assigning the web role. Revoking = deactivating that record. The rest cascades automatically.

---

## Key Technical Considerations

### CORS

The BFF API must allow cross-origin requests from the Power Pages portal domain. CORS is configured in `Sprk.Bff.Api` to allow:
- `https://sprk-external-workspace.powerappsportals.com` (production)
- `http://localhost:3000` (local dev)

Methods: `GET, POST, PATCH, DELETE, OPTIONS`
Headers: `Authorization, Content-Type`

### Content Security Policy

Power Pages CSP must allow connections to the BFF API domain:
```
connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net https://login.microsoftonline.com
```

Also required: `frame-ancestors 'self'` to prevent clickjacking.

### Redis Caching (ADR-009)

`ExternalParticipationService` caches participation data in Redis with a 60-second TTL keyed by `contactId`. This avoids a Dataverse query on every BFF request. The cache is invalidated when:
- Access is granted (`/grant`)
- Access is revoked (`/revoke`)
- Project is closed (`/close-project`)

**Important:** After a Core User grants or revokes access, the external user's next BFF call will see stale data for up to 60 seconds. This is acceptable for the portal use case (access changes are not instant).

### Token Expiry and Refresh

MSAL handles token refresh transparently. Access tokens from Entra have a default 1-hour expiry. MSAL uses the refresh token to acquire a new access token silently. If the refresh token is expired (default 24h for B2B guests), MSAL triggers a new interactive login via redirect.

`bff-client.ts` retries once on 401 to handle the edge case where the token expires between `acquireBffToken()` and the HTTP request.

### Build Output Size

`vite-plugin-singlefile` inlines all JS/CSS into `dist/index.html`. Typical build output:
- ~800 KB HTML file (uncompressed)
- ~220 KB gzipped
- Base64-encoded in Dataverse: ~1.1 MB

This is within Dataverse web resource limits (5 MB max per resource).

### Local Development

Vite's dev server proxies `/_api/`, `/_layout/`, and `/_services/` to the live Power Pages portal. This allows local development with hot module reload while using the real portal for authentication.

Note: MSAL must have `http://localhost:3000` registered as a redirect URI on the SPA app registration for local dev to work.

### No Server-Side Rendering

The SPA is purely client-side rendered. There is no SEO value, which is intentional — the workspace is authenticated and not publicly indexed. External users access it via direct URL or invitation email link.

---

## Platform Constraints

Known limitations of the Power Pages + Dataverse platform relevant to this architecture:

| Constraint | Detail | Impact / Mitigation |
|-----------|--------|---------------------|
| **HashRouter required** | Power Pages serves SPA as a single file — no server-side routing | Use `HashRouter`; all navigation is hash-based (`#/project/{id}`) |
| **Max parent-chain depth ~4** | Each level = additional Dataverse query at permission check time | Spaarke uses 2-3 levels — well within limit |
| **No polymorphic parent lookups** | Parent-chain scope doesn't support polymorphic relationship fields | Use explicit single-type relationships only (sprk_projectid, not a polymorphic entity reference) |
| **Table permission scope is schema-level** | Permissions apply to all records of a table, not individual records | Contact scope + parent chain achieves effective record-level scoping |
| **Web role assignment has no expiry** | The N:N relationship between Contact and Web Role has no date fields | Use `sprk_externalrecordaccess.sprk_expirydate` + scheduled deactivation |
| **Teams don't apply to Contacts** | Dataverse Teams contain SystemUser records only | Use Contact scope or Account scope — not teams |
| **Parent scope config: Portal Management only** | Parent-scope table permissions can only be configured in Portal Management app, not Design Studio | Document steps clearly (see admin guide Section 4) |
| **Build output is a single file** | `vite-plugin-singlefile` constraint — SPA must fit in one HTML file | Keep bundle size under ~3 MB; current output ~800 KB |
| **No SSR / SEO** | Client-side rendering only | Intentional — content is authenticated, no public indexing needed |
| **B2B guests require Microsoft account** | Entra B2B only works for users with existing Microsoft 365 accounts | Non-Microsoft users would need a B2C configuration |

---

## Future Considerations

### Power Pages Web Role + Dataverse Security Role Unification

Microsoft is unifying Power Pages web roles with Dataverse security roles. When GA:
- Each web role will have a corresponding Dataverse security role
- External contacts could eventually share the same BU-scoped role model as internal users
- Could simplify the three-plane model — Dataverse security would handle more automatically

**Current status**: Preview (2025 Wave 2). Design the UAC model to work with current table permissions; the new model should be backward-compatible.

### Azure AI Search Native Entra ACL

Document-level access control using Entra tokens at query time:
- Documents indexed with ACL entries (Entra user/group OIDs)
- Search automatically trims results based on the caller's Entra token
- Could eliminate the manual `search.in` filter construction in the BFF

**Current status**: Preview (REST API `2025-05-01-preview`). Requires the caller to hold a valid Entra token. Works for B2B guests (they have Entra identities). Would be a direct improvement to the Plane 3 AI search pattern.

---

## Design Tradeoffs

### BFF-Only Data Access vs. Power Pages Web API

**Decision**: All data goes through the BFF. The Power Pages `/_api/` is not used.

**Rationale**:
- BFF provides a single auditable path for all Dataverse access
- Managed identity (BFF → Dataverse) is more secure than per-user table permissions for sensitive project data
- Avoids maintaining two separate auth paths (MSAL tokens for BFF, session cookies for `/_api/`)
- BFF can validate project access (`HasProjectAccess`) before executing any query
- Power Pages Web API requires per-table site settings maintenance for field whitelists

**Tradeoff**: Slightly more BFF surface area. Events/tasks that could be lightweight `/_api/` calls go through an extra hop.

### Entra B2B vs. Entra External ID (B2C)

**Decision**: Entra B2B guests in the main workforce tenant.

**Rationale**:
- External users typically have existing Microsoft 365 accounts (attorneys, corporate counsel)
- SSO reduces friction — no new credentials, no separate MFA enrollment
- Main tenant management of guests aligns with corporate security policies
- Avoids operating a separate B2C tenant for a relatively small user population

**Tradeoff**: External users without Microsoft accounts cannot authenticate. For non-Microsoft users, a B2C configuration would be needed.

### MSAL vs. Portal Implicit Grant

**Decision**: MSAL authorization code + PKCE, replacing the portal implicit grant flow.

**Rationale**:
- OAuth 2.0 implicit grant is deprecated by IETF and Microsoft
- MSAL handles silent refresh, MFA prompts, and conditional access automatically
- No manual token caching required
- Better security (PKCE prevents code interception)

**Tradeoff**: Requires registered app registrations and Entra admin consent. The portal implicit grant was simpler to configure.

---

## Related Documents

| Document | Purpose |
|----------|---------|
| [external-access-architecture.md](external-access-architecture.md) | Broader UAC architecture (Dataverse security, SPE, AI Search planes) |
| [uac-access-control.md](uac-access-control.md) | Unified Access Control model |
| [`docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md`](../guides/EXTERNAL-ACCESS-SPA-GUIDE.md) | Developer guide — building and extending the SPA |
| [`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`](../guides/EXTERNAL-ACCESS-ADMIN-SETUP.md) | Admin guide — Power Pages configuration, table permissions, site settings |
| [Secure Project Module Design](../../projects/sdap-secure-project-module/design.md) | Product design document |
