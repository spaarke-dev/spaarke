# EXTERNAL ACCESS — ADMIN & OPERATIONS SETUP GUIDE

> **Audience**: Azure / Power Platform admins and DevOps engineers configuring the external access environment
> **Last Updated**: 2026-07-21
> **Applies To**: Azure Static Web Apps, Microsoft Entra External ID (CIAM), Dataverse, Azure App Service (BFF)
> **Architecture Reference**: [`docs/architecture/external-access-spa-architecture.md`](../architecture/external-access-spa-architecture.md)

---

## Overview

This guide covers the one-time and recurring configuration required to run the External Access SPA on **Azure Static Web Apps** with **Microsoft Entra External ID (CIAM)** identity. It includes:

- **CIAM tenant + app registrations**: SPA client, BFF API, Graph provisioner
- **BFF API settings**: the second `Ciam` JWT scheme, CORS, download path
- **Azure Static Web Apps**: hosting + deploy workflow + runtime config
- **Onboarding flow**: admin-initiated CIAM provisioning (invite-and-grant), SSPR password set
- **SPE + Dataverse**: broker-only, app-only access
- **Monitoring and troubleshooting**

For SPA development, see [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md).

> **Retired (historical)**: The external portal formerly ran on **Power Pages** with **Entra B2B guests**. That model — Power Pages site, `sprk_externalworkspace` web resource, table permissions / web roles, B2B invitations, and `Deploy-ExternalWorkspaceSpa.ps1` — is **decommissioned** (ADR-028 Amendment A1). Nothing in this guide is Power Pages configuration; historical mentions are labeled as retired.

---

## Environment Reference

| Item | Value |
|------|-------|
| SWA resource (dev) | `swa-spaarke-external-spa-dev` (resource group `rg-spaarke-dev`) |
| SPA live host (dev) | `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| BFF API (dev) | `https://spaarke-bff-dev.azurewebsites.net` |
| Dataverse org | `https://spaarkedev1.crm.dynamics.com` |
| CIAM external tenant | `spaarkeextid` (`spaarkeextid.onmicrosoft.com`) — tenant ID `7052feba-bfc4-43e0-b09e-65014b429131` |
| CIAM authority | `https://spaarkeextid.ciamlogin.com/7052feba-bfc4-43e0-b09e-65014b429131` |
| SPA public client (CIAM) | `bd57e54e-b339-4500-b55c-e451009fd907` |
| BFF API app (CIAM) | `4a4d5126-91b0-4865-8e3a-134b7209013e` — App ID URI `api://4a4d5126-…`, scope `SDAP.Access` |
| CIAM Graph provisioner app | `e63e6eb1-be25-4214-80a8-a6d609034bb9` (`User.ReadWrite.All`, cert credential) |
| Contact ↔ CIAM link column | `Contact.sprk_externalobjectid` (String/100) — stable CIAM `oid` |

---

## Section 1: Microsoft Entra External ID (CIAM) Tenant

External users are **local accounts in a dedicated CIAM external tenant** (`spaarkeextid`), not B2B guests in the workforce tenant.

### 1.1 Tenant + User Flow

1. Provision an Entra External ID (CIAM) tenant (`spaarkeextid`).
2. Configure the sign-in user flow with **`isSignUpAllowed = false`** — R1 is admin-initiated only; there is **no self-service sign-up**.
3. Enable **SSPR with Email OTP** so a provisioned user can set/reset their password via **"Forgot password"** (the onboarding email drives this).

### 1.2 SPA App Registration (CIAM tenant)

- App ID: `bd57e54e-b339-4500-b55c-e451009fd907`
- Platform: **Single-page application (SPA)** — not Web.
- Redirect URIs: the SWA origin (`https://green-dune-0c4f1221e.7.azurestaticapps.net`) and `http://localhost:3000` (local dev). MSAL uses `window.location.origin`.
- Implicit grant: **disabled** (authorization code + PKCE only).
- API permission (delegated): `api://4a4d5126-91b0-4865-8e3a-134b7209013e/SDAP.Access` — grant admin consent.

### 1.3 BFF API App Registration (CIAM tenant)

- App ID: `4a4d5126-91b0-4865-8e3a-134b7209013e`
- Expose an API: Application ID URI `api://4a4d5126-91b0-4865-8e3a-134b7209013e`, scope `SDAP.Access`, authorized client = the SPA (`bd57e54e-…`).
- **Manifest**: set `requestedAccessTokenVersion: 2` so the access-token `aud` is the **client-id GUID** (`4a4d5126-…`). This is exactly what the BFF validates as `Ciam:Audience`.

### 1.4 Graph Provisioner App Registration (CIAM tenant)

- App ID: `e63e6eb1-be25-4214-80a8-a6d609034bb9`
- Microsoft Graph **application** permission: `User.ReadWrite.All` — grant admin consent **in the CIAM tenant** (workforce MI cannot reach this tenant).
- Credential: a **certificate**, private key stored in Key Vault as `ciam-graph-provisioner-cert` in `spaarke-spekvcert`. The BFF (`CiamGraphClientFactory`) loads it by name at runtime — **never a plaintext secret** (NFR-06).

---

## Section 2: BFF API Configuration

The BFF validates CIAM tokens with a **second `Ciam` JWT bearer scheme** that runs alongside the workforce default scheme (`Infrastructure/DI/AuthorizationModule.cs`). The `Ciam` scheme is **additive** — it does not replace the workforce scheme. It is pinned to the `/api/v1/external` route group via the `AuthPolicies.CiamExternal` policy; the internal `/api/v1/external-access` management group stays on the workforce default.

### 2.1 Azure App Service Configuration (`spaarke-bff-dev`)

Add a `Ciam` config section mirroring `AzureAd`, plus the Graph-provisioner sub-section and the portal URL:

| Setting | Value / Notes |
|---------|---------------|
| `Ciam__Instance` | `https://spaarkeextid.ciamlogin.com` |
| `Ciam__TenantId` | `7052feba-bfc4-43e0-b09e-65014b429131` |
| `Ciam__Audience` | `4a4d5126-91b0-4865-8e3a-134b7209013e` (BFF-API client-id GUID; v2 tokens) |
| `Ciam__Domain` | `spaarkeextid.onmicrosoft.com` (local-account identity issuer) |
| `Ciam__GraphProvisioner__ClientId` | `e63e6eb1-be25-4214-80a8-a6d609034bb9` |
| `Ciam__GraphProvisioner__CertificateName` | `ciam-graph-provisioner-cert` (Key Vault) |
| `ExternalAccess__PortalUrl` | `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| `Cors__AllowedOrigins__0` | `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| `Cors__AllowedOrigins__1` | `http://localhost:3000` (local dev) |

The BFF constructs the CIAM validation authority as `{Ciam:Instance}/{Ciam:TenantId}/v2.0` and validates issuer, audience, lifetime, and signing key.

### 2.2 CORS Configuration

The BFF must answer pre-flight OPTIONS requests from the SWA origin. Verify the SWA origin is in `Cors__AllowedOrigins`.

```bash
curl -I -X OPTIONS \
  https://spaarke-bff-dev.azurewebsites.net/api/v1/external/me \
  -H "Origin: https://green-dune-0c4f1221e.7.azurestaticapps.net" \
  -H "Access-Control-Request-Method: GET"
# Expected: HTTP 204 with Access-Control-Allow-Origin header
```

### 2.3 Broker-Only Downstream (no OBO on the external path)

All external-surface SPE + Dataverse access is **app-only / managed identity**. The external user's token authenticates **only** to the BFF and is never exchanged for a downstream Graph/SPE token. No workforce Entra B2B guest is provisioned for any external user. The document-download endpoint streams app-only via `SpeFileStore.DownloadFileAsync` after enforcing authorization (see Section 6).

---

## Section 3: Azure Static Web Apps Hosting

The SPA is served as a static site from SWA (`swa-spaarke-external-spa-dev`), replacing the retired Power Pages web resource.

### 3.1 Runtime Config (`staticwebapp.config.json`)

Lives at `src/client/external-spa/staticwebapp.config.json` and is staged into `dist/` at deploy time:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": [
      "/assets/*",
      "/*.{css,js,map,ico,png,svg,gif,jpg,jpeg,webp,json,txt,woff,woff2,ttf,eot}"
    ]
  },
  "globalHeaders": {
    "Referrer-Policy": "no-referrer-or-same-origin",
    "Content-Security-Policy": "frame-ancestors 'self'",
    "X-Content-Type-Options": "nosniff"
  }
}
```

`navigationFallback` rewrites unmatched routes to `/index.html` so `BrowserRouter` clean-URL deep links (e.g. `/project/{id}`) resolve instead of 404ing. `globalHeaders` sets the framing/referrer/nosniff security headers.

### 3.2 Deploy Workflow

`.github/workflows/deploy-external-spa.yml` (currently `workflow_dispatch`):

1. `npm install --legacy-peer-deps` (per root CLAUDE.md §12 — not `npm ci`).
2. Build `src/client/external-spa` with the CIAM `VITE_*` build env (see [SPA guide](EXTERNAL-ACCESS-SPA-GUIDE.md#environment-variables)). These `VITE_*` values are non-secret identifiers and override the `.env.production` placeholders at build time.
3. Copy `staticwebapp.config.json` into `dist/`.
4. Upload via `Azure/static-web-apps-deploy` with `skip_app_build: true` and the SWA deploy token secret `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV`.

> **Retired (historical)**: `scripts/Deploy-ExternalWorkspaceSpa.ps1` (base64 web-resource upload) is deleted. Do not use it.

---

## Section 4: Dataverse Schema

### 4.1 `Contact.sprk_externalobjectid` (String/100)

The stable link between a CIAM identity and a Dataverse Contact. Populated by the onboarding flow with the CIAM user's `oid`. The `ExternalCallerAuthorizationFilter` resolves the caller Contact by this field; email is only a first-login fallback that then binds the `oid`.

### 4.2 `sprk_externalrecordaccess` (participation grant)

Unchanged authorization model. A grant is one active record with:
- Grantee = the **Contact** (`sprk_contactid@odata.bind` → `/contacts(...)`) — never a firm/org lookup.
- `sprk_projectid` → the project, `sprk_accesslevel` (see Section 8), `sprk_granteddate`, and `sprk_grantedby` (audited caller).
- Optional `sprk_expirydate` and `sprk_accountid` (record-keeping only; not the grantee).

> Power Pages web roles, table permissions, and the `adx_*` / `mspp_*` built-in tables are **retired** — they are no longer part of this platform.

---

## Section 5: Onboarding Flow (admin-initiated)

There is **no self-service sign-up** in R1 (`isSignUpAllowed = false`). A core user onboards an external attorney; the attorney then sets a password via SSPR.

> **⚠️ R1 reality — API-only, no UI button yet.** In R1 there is **no "Invite to Secure Workspace" button** on the Matter/Project form. Onboarding is done by a Spaarke **core user or admin calling the BFF endpoint directly** (curl / Postman / PowerShell / a thin admin script). The one-click ribbon/form command is deferred to **R2** (backlog item DI-029-01). Until then, follow Section 5.0 below — it is the entire onboarding process.

### 5.0 Onboard a new external user — step-by-step (copy-paste)

This is the complete, current process to give an outside attorney access to a Secure Project. It takes one API call.

**Before you start, gather three things:**

| Input | Where to get it | Example |
|-------|-----------------|---------|
| Attorney **email** | From the requesting core user | `jane.doe@outsidecounsel.com` |
| **Project ID** (GUID) | The `sprk_project` record's ID (open the Project row → the `id` in the URL, or query Dataverse) | `3f2504e0-4f89-41d3-9a0c-0305e82c3301` |
| **Access level** | Decide View / Collaborate / Full (see Section 8) | `100000001` (Collaborate) |

Optional: `FirstName`, `LastName` (used only if the Contact doesn't already exist), `ExpiryDate` (`YYYY-MM-DD`, auto-revoke date), `AccountId` (the attorney's firm, for record-keeping only — it is **never** the grantee).

---

**Step 1 — Get a workforce admin token for the BFF.**

The `/api/v1/external-access/*` management endpoints require a **workforce** (Spaarke staff) Entra token — the same identity a core user signs in with. The simplest way to mint one for a manual/admin call:

```bash
# Resource = the BFF workforce app-id URI (api://{bff-app-id}); find {bff-app-id} in the
# BFF App Service setting AzureAd__ClientId (see auth-deployment-setup.md §3).
TOKEN=$(az account get-access-token \
  --resource "api://{bff-app-id}" \
  --query accessToken -o tsv)
```

(In production this token comes from the signed-in core user's session, not the CLI — the CLI path is for admin/manual onboarding today.)

---

**Step 2 — Call `invite-and-grant` (onboard + grant in one action).**

```bash
curl -sS -X POST \
  https://spaarke-bff-dev.azurewebsites.net/api/v1/external-access/invite-and-grant \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "email":       "jane.doe@outsidecounsel.com",
        "projectId":   "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
        "accessLevel": 100000001,
        "firstName":   "Jane",
        "lastName":    "Doe"
      }'
```

`accessLevel` is one of `100000000` (ViewOnly) · `100000001` (Collaborate) · `100000002` (FullAccess). `email` + `projectId` + `accessLevel` are **required**; `firstName`/`lastName`/`expiryDate`/`accountId` are optional.

**Success response (HTTP 200):**

```json
{
  "contactId":      "b1e2c3d4-....",   // the Dataverse Contact onboarded + granted
  "onboardStatus":  "Provisioned",      // or "AlreadyProvisioned" (idempotent — see 5.3)
  "accessRecordId": "a9f8e7d6-....",   // the sprk_externalrecordaccess grant
  "portalUrl":      "https://green-dune-0c4f1221e.7.azurestaticapps.net"
}
```

`onboardStatus = "Provisioned"` means a brand-new CIAM account was created and the onboarding email was sent. `"AlreadyProvisioned"` means the Contact was already linked to a CIAM identity — no second account, no new email — but the grant was still (re)issued.

---

**Step 3 — What the BFF does for you (automatic, one atomic action).**

1. **Onboard (idempotent)** — resolves or creates the Dataverse Contact by email; if the Contact has no CIAM identity yet, creates a **CIAM local account** (Graph `POST /users`, cross-tenant) with a temporary password + `forceChangePasswordNextSignIn`, and stores the returned `oid` on `Contact.sprk_externalobjectid`. If already linked → skips (idempotent).
2. **Grant** — creates the `sprk_externalrecordaccess` record (grantee = the Contact, audited via `sprk_grantedby`) and invalidates the Redis participation cache so access is visible immediately.
3. **Email** — sends the onboarding email (portal link + "set your password" instruction).

The temporary password is **never** returned to you or logged — the attorney sets their own via SSPR (Step 4).

---

**Step 4 — What the external user does (self-service, no admin action).**

1. Receives the onboarding email.
2. Opens the **portal URL** (`portalUrl` from the response — the SWA site) and clicks **"Forgot password"** → sets a password via SSPR (Email OTP).
3. Signs in against the CIAM authority.
4. The SPA calls `GET /api/v1/external/me` and shows their assigned project(s).

That's it — no further admin step. To **add another project** for the same attorney later, call `invite-and-grant` again (or `/grant`) with the new `projectId`; onboarding is skipped and only the new grant is issued.

---

### 5.1 Core-User "Invite to Secure Workspace" — `POST /api/v1/external-access/invite-and-grant`

`InviteAndGrantExternalUserEndpoint` (workforce-authed admin surface) does, in one action:

1. **Onboard (idempotent)** — resolve or create the Dataverse Contact by email; if the Contact already has an `oid` bound, **skip** account creation (status `AlreadyProvisioned`). Otherwise `CiamUserProvisioningService` calls Graph `POST /users` (cross-tenant, via `CiamGraphClientFactory`) to create a **CIAM local account** — an `identities` block with `signInType = emailAddress`, `issuer = spaarkeextid.onmicrosoft.com`, a generated temporary password, `forceChangePasswordNextSignIn = true`, and `passwordPolicies = DisablePasswordExpiration`. The returned `oid` is persisted to `Contact.sprk_externalobjectid`.
2. **Grant** — create the `sprk_externalrecordaccess` record (grantee = the Contact, audited via `sprk_grantedby`) and invalidate the Redis participation cache so the grant is immediately visible.
3. **Email** — send the onboarding email (`RegistrationEmailService.SendCiamOnboardingEmailAsync` + `CiamOnboardingTemplate.html`) directing the user to the portal and to set a password via "Forgot password".

The temporary password is **never** returned to the caller or logged — it is delivered only via the SSPR set-password flow.

**Sibling endpoints**: `POST /invite` (onboard only) and `POST /grant` (grant only) share the same reusable cores.

### 5.2 What the External User Experiences

1. Receives the onboarding email.
2. Opens the portal URL and clicks **"Forgot password"** → sets a password via SSPR (Email OTP).
3. Signs in against the CIAM authority (authorization code + PKCE).
4. The SPA loads → `GET /api/v1/external/me` returns their project access.
5. The workspace displays their assigned project(s).

### 5.3 Idempotency

Re-invoking `invite` or `invite-and-grant` for an already-provisioned Contact (an `oid` is present) creates **no second CIAM account** and re-sends **no** onboarding email (status `AlreadyProvisioned`). `invite-and-grant` still (re)issues the grant, so re-running is a safe way to retry a failed grant.

### 5.4 Revoking Access — `POST /api/v1/external-access/revoke`

1. Deactivates the `sprk_externalrecordaccess` record (`statecode = 1`).
2. Invalidates the Redis participation cache for the Contact.

The user's **CIAM account is not deleted** — only participation is revoked. On next sign-in they see an empty workspace (no projects listed). No SPE container-membership removal is needed (broker-only — none was written on grant).

---

## Section 6: Document Download (app-only, authz-before-stream)

`GET /api/v1/external/projects/{id}/documents/{documentId}/content` enforces authorization **before** any storage read (NFR-03):

1. **Project access** — the caller must have a participation record for `{id}`, else **403, no bytes, no Graph call**.
2. **Document → project scoping** — the `documentId` must belong to `{id}` (an app-only Dataverse read that resolves no Graph pointer); a mismatch or missing document is a uniform 403.
3. **Only then** does the BFF resolve SPE pointers server-side and stream **app-only** via `SpeFileStore.DownloadFileAsync` (never the OBO path).

Graph pointers (`driveId`/`driveItemId`) are never exposed to the browser; the endpoint is keyed on `documentId`. Content returns as `application/octet-stream` (attachment).

External SPE access is entirely BFF-brokered app-only. There is **no per-external-user SPE container membership** and no `Set-SPOApplication` external-sharing prerequisite for external users on this path.

---

## Section 7: Monitoring and Troubleshooting

### 7.1 Common Issues

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| SPA 404 on deep link `/project/{id}` | SWA `navigationFallback` missing/misconfigured | Ensure `staticwebapp.config.json` rewrites to `/index.html` and was staged into `dist/` |
| MSAL fails on `*.ciamlogin.com` metadata | CIAM host not in `knownAuthorities` | Confirm the CIAM authority host is declared in `msal-config.ts` `knownAuthorities` |
| 401 on all `/api/v1/external/*` calls | `Ciam:Audience` mismatch | Must equal the BFF-API **client-id GUID** (`4a4d5126-…`); confirm `requestedAccessTokenVersion: 2` |
| 403 `contact_not_found` | No Contact resolvable by `oid` (or first-login email) | Confirm onboarding populated `sprk_externalobjectid`; check the Contact exists |
| Empty project list from `/me` | No active participation records | Check `sprk_externalrecordaccess` for `statecode = 0` records for the Contact |
| New grant not visible for ~60s | Redis cache not invalidated | Grant invalidates the cache; verify `tid` claim present for cache key |
| Download returns 403 with no bytes | Authz-before-stream denied (no project access or doc not in project) | Expected for unauthorized callers; verify participation + document→project scoping |
| CORS error in browser console | SWA origin not in BFF CORS allow-list | Add the SWA origin to `Cors__AllowedOrigins` |
| Provisioning fails (Graph `POST /users`) | CIAM Graph provisioner cert/permission | Verify `ciam-graph-provisioner-cert` in Key Vault + `User.ReadWrite.All` consented in the CIAM tenant |

### 7.2 Checking BFF Logs

```bash
# Stream live logs from App Service
az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev

# Filter external access logs
az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev | grep "\[EXT"
```

Key log prefixes:
- `[EXT-AUTH]` — `ExternalCallerAuthorizationFilter` (oid/email Contact resolution, participation loading)
- `[EXT-DOWNLOAD]` — document content endpoint (authz decisions + stream)
- `[EXT-GRANT]` — grant endpoint
- `[EXT-INVITE]` / `[EXT-INVITE-GRANT]` — onboarding + invite-and-grant
- `[CIAM-PROVISION]` — CIAM user creation

### 7.3 Checking Participation Records

```bash
TOKEN=$(az account get-access-token \
  --resource https://spaarkedev1.crm.dynamics.com \
  --query accessToken -o tsv)

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_externalrecordaccesses?\$filter=_sprk_contactid_value eq {contactId} and statecode eq 0&\$select=_sprk_contactid_value,_sprk_projectid_value,sprk_accesslevel,statecode"
```

### 7.4 Audit Trail

| What | Where | Maintained By |
|------|-------|---------------|
| Who granted access + when | `sprk_externalrecordaccess.sprk_grantedby` + `sprk_granteddate` | BFF on grant |
| CIAM user provisioning | App Service logs `[CIAM-PROVISION]` + the user's `oid` on `Contact.sprk_externalobjectid` | BFF / Graph |
| BFF authorization decisions | App Service logs `[EXT-AUTH]` / `[EXT-DOWNLOAD]` | BFF |
| SPE file access | SPE audit logs via Graph API | SharePoint Embedded |

---

## Section 8: Access Levels

The BFF grants one of three access levels on `sprk_externalrecordaccess.sprk_accesslevel`. Enforcement is server-side (`ExternalCallerContext.GetEffectiveRights`); client-side flags are UX-only.

| `sprk_accesslevel` | Access Level | Effective rights (server-side) |
|--------------------|-------------|--------------------------------|
| `100000000` | ViewOnly | Read |
| `100000001` | Collaborate | Read + Create + Write |
| `100000002` | FullAccess | Read + Create + Write + Delete |

**Capability summary:**

| Capability | ViewOnly | Collaborate | FullAccess |
|------------|----------|-------------|------------|
| View project + documents | Yes | Yes | Yes |
| Download documents (app-only) | Yes | Yes | Yes |
| Create / update to-dos | No | Yes | Yes |
| Delete | No | No | Yes |

---

## Related Resources

- **Architecture Reference**: [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md)
- **Developer Guide**: [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md)
- **Auth architecture (ADR-028 + Amendment A1)**: [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
- **UAC Architecture**: [uac-access-control.md](../architecture/uac-access-control.md)
- **Entra External ID (CIAM) overview (MS Learn)**: https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam
- **Create a user (Graph `POST /users`)**: https://learn.microsoft.com/en-us/graph/api/user-post-users
- **Azure Static Web Apps configuration**: https://learn.microsoft.com/en-us/azure/static-web-apps/configuration
