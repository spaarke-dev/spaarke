# Phase 7 — Final Deployment & Validation Guide

> **Task**: 075
> **Phase**: 7 — Testing, Deployment & Wrap-Up
> **Created**: 2026-03-16
> **Status**: Ready for execution
> **Environment**: Dev (`https://spaarkedev1.crm.dynamics.com` / `https://spaarkedev1.powerappsportals.com`)

---

## Overview

This guide consolidates the deployment steps for all project phases into a single ordered runbook. All prior incremental deployment guides are referenced here; this document is the authoritative checklist for a full end-to-end deployment of the Secure Project & External Access Platform.

All prior phases produced detailed per-task deployment notes. This guide aggregates them in the correct sequence and adds the final validation checklist that covers the complete system working as a cohesive unit.

**Estimated total time**: 6–8 hours for a first-time deployment; 2–3 hours for redeployment of all components.

---

## Prerequisites — Global

Before starting any deployment phase, verify these global prerequisites:

### Tools

- [ ] Azure CLI authenticated: `az account show` → confirms subscription `spe-infrastructure-westus2`
- [ ] PAC CLI version 1.44.x or later: `pac --version`
- [ ] PAC CLI authenticated to dev: `pac auth list` → active profile points to `https://spaarkedev1.crm.dynamics.com`
- [ ] .NET 8 SDK installed: `dotnet --version`
- [ ] Node.js 20.x installed: `node --version`
- [ ] Access to Power Apps maker portal: `https://make.powerapps.com`
- [ ] Access to Power Platform Admin Center: `https://admin.powerplatform.microsoft.com`
- [ ] Access to Power Pages Studio: `https://make.powerpages.microsoft.com`
- [ ] Access to Azure portal: `https://portal.azure.com`
- [ ] Access to Entra admin center: `https://entra.microsoft.com`

### Environment Facts

| Resource | Value |
|----------|-------|
| Dataverse URL | `https://spaarkedev1.crm.dynamics.com` |
| Power Pages portal URL | `https://spaarkedev1.powerappsportals.com` |
| Azure App Service (BFF API) | `spe-api-dev-67e2xz` |
| BFF API URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Resource Group | `spe-infrastructure-westus2` |
| Key Vault | `spaarke-spekvcert` |
| Dataverse Solution | `SpaarkeCore` |
| SPA location | `src/client/external-spa/` |
| BFF API location | `src/server/api/Sprk.Bff.Api/` |

---

## Phase 1: Dataverse Schema Deployment

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase1-deployment-guide.md`
**Estimated time**: ~2 hours (maker portal configuration + export/import)

### Phase 1 Checklist

- [ ] **Step 1**: Create `sprk_externalrecordaccess` table in maker portal
  - Table name: `External Record Access`, logical name: `sprk_externalrecordaccess`
  - Add all fields per `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md`
  - Choice field `sprk_accesslevel`: View Only (100000000), Collaborate (100000001), Full Access (100000002)
  - Add to SpaarkeCore solution

- [ ] **Step 2**: Add secure project fields to `sprk_project`
  - `sprk_issecure` — Boolean, default: No
  - `sprk_securitybuid` — Lookup → BusinessUnit
  - `sprk_externalaccountid` — Lookup → Account
  - Per `src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md`
  - Business Rule: Lock `sprk_issecure` after creation
  - Add all three fields to `sprk_project` main form (Secure Project Configuration section)

- [ ] **Step 3**: Configure views and subgrid
  - Create "Active Participants" view (default)
  - Create "By Project" view
  - Create "By Contact" view
  - Create "Expiring Access" view
  - Add "External Participants" subgrid to `sprk_project` main form
  - Create Quick Create form for `sprk_externalrecordaccess`

- [ ] **Step 4**: Export SpaarkeCore solution as Managed
  - Save to `src/solutions/SpaarkeCore/bin/Release/SpaarkeCore_managed.zip`

- [ ] **Step 5**: Import via PAC CLI

  ```powershell
  pac solution import `
      --path src/solutions/SpaarkeCore/bin/Release/SpaarkeCore_managed.zip `
      --publish-changes `
      --force-overwrite `
      --environment https://spaarkedev1.crm.dynamics.com
  ```

- [ ] **Step 6**: Verify tables exist

  ```powershell
  pac data export `
      --entity sprk_externalrecordaccess `
      --select sprk_externalrecordaccessid,sprk_name,sprk_contactid,sprk_projectid,sprk_accesslevel `
      --output-directory ./exports

  pac data export `
      --entity sprk_project `
      --select sprk_projectid,sprk_name,sprk_issecure,sprk_securitybuid,sprk_externalaccountid `
      --output-directory ./exports
  ```

### Phase 1 Verification

- [ ] `sprk_externalrecordaccess` table exists with all fields
- [ ] `sprk_project` has `sprk_issecure`, `sprk_securitybuid`, `sprk_externalaccountid` fields
- [ ] All views created and subgrid appears on project form
- [ ] Solution imported and published without errors

---

## Phase 2: BFF API Deployment

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase2-task019-bff-api-deployment-guide.md`
**Estimated time**: ~1 hour

### Phase 2 Pre-Deployment Configuration

Add these app settings to the Azure App Service **before** deploying (one-time configuration):

```bash
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings \
    "PowerPages__BaseUrl=https://spaarkedev1.powerappsportals.com" \
    "PowerPages__SecureProjectParticipantWebRoleId={web-role-guid-from-task-021}"
```

> The `SecureProjectParticipantWebRoleId` GUID comes from the "Secure Project Participant" web role created in Phase 3, Task 021. If Phase 3 has not been configured yet, add this setting after Task 021 is complete.

### Phase 2 Deployment Steps

- [ ] **Step 1**: Clean build

  ```bash
  dotnet build src/server/api/Sprk.Bff.Api/ --configuration Release
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2**: Run unit tests

  ```bash
  dotnet test tests/Sprk.Bff.Api.Tests/ --configuration Release
  ```

  Expected: All tests pass (65 unit tests from Phase 2 tasks 010-017).

- [ ] **Step 3**: Run integration tests

  ```bash
  dotnet test tests/Sprk.Bff.Api.IntegrationTests/ --configuration Release
  ```

  Expected: All integration tests pass (41 integration tests from Phase 2 task 018).

- [ ] **Step 4**: Deploy to Azure

  ```bash
  # Using deployment script (preferred):
  pwsh scripts/Deploy-BffApi.ps1

  # OR manual:
  dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o ./publish

  az webapp deploy \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz \
    --src-path ./publish \
    --type zip
  ```

- [ ] **Step 5**: Verify health endpoints

  ```bash
  curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
  # Expected: {"status":"Healthy"}

  curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
  # Expected: pong
  ```

- [ ] **Step 6**: Verify new endpoints respond (401 = endpoint exists, auth required)

  ```bash
  curl -i https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external/me
  # Expected: HTTP 401

  curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/grant \
    -H "Content-Type: application/json" -d "{}"
  # Expected: HTTP 401

  curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/revoke \
    -H "Content-Type: application/json" -d "{}"
  # Expected: HTTP 401

  curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/invite \
    -H "Content-Type: application/json" -d "{}"
  # Expected: HTTP 401

  curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/close-project \
    -H "Content-Type: application/json" -d "{}"
  # Expected: HTTP 401
  ```

### Phase 2 Verification

- [ ] Build clean (0 errors, 0 warnings)
- [ ] All 65 unit tests pass
- [ ] All 41 integration tests pass
- [ ] Health check returns `{"status":"Healthy"}`
- [ ] All 5 external access endpoints return HTTP 401 (not 404)

---

## Phase 3: Power Pages Configuration

**Estimated time**: ~4–5 hours (manual steps in portals)

### Task 020: Configure Entra External ID Identity Provider

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase3-task020-entra-external-id-config.md`

- [ ] Entra External ID tenant exists (or created) with correct tenant type
- [ ] `SignUpSignIn` user flow configured with email + given_name + family_name + oid claims
- [ ] App registration `Spaarke Power Pages Portal` created in external tenant
- [ ] Redirect URI `https://spaarkedev1.powerappsportals.com/signin-oidc` registered
- [ ] Logout URI `https://spaarkedev1.powerappsportals.com/Account/Login/LogOff` registered
- [ ] Client secret created and stored in Key Vault:

  ```bash
  az keyvault secret set \
    --vault-name spaarke-spekvcert \
    --name "EntraExternalId-PowerPages-ClientSecret" \
    --value "<secret-value>"
  ```

- [ ] Power Pages identity provider configured (Authority, ClientId, ClientSecret, Scope, claim mappings)
- [ ] Claim mappings: `email` → `emailaddress1`, `given_name` → `firstname`, `family_name` → `lastname`

### Task 021: Configure Web Roles and Table Permission Chain

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase3-task021-web-roles-table-permissions.md`

Create these records in Portal Management (`https://spaarkedev1.crm.dynamics.com/main.aspx?appname=MicrosoftPortalApp`):

**Step 1**: Create web role

| Field | Value |
|-------|-------|
| Name | Secure Project Participant |
| Website | Spaarke External Portal |
| Authenticated Users Role | Yes |
| Anonymous Users Role | No |

**Record the web role GUID** — needed for the BFF API app setting (`PowerPages__SecureProjectParticipantWebRoleId`).

**Steps 2-7**: Create table permissions chain

| Name | Table | Access Type | Parent | Permissions |
|------|-------|-------------|--------|-------------|
| ERA - Contact Scope | sprk_externalrecordaccess | Contact | — | Read |
| Project - ERA Parent | sprk_project | Parent | ERA - Contact Scope | Read |
| Document - Project Child | sprk_document | Parent | Project - ERA Parent | Read |
| Event - Project Child (Read) | sprk_event | Parent | Project - ERA Parent | Read, Create, Write |
| Contact - Project Child (Read) | contact | Parent | Project - ERA Parent | Read |
| Contact - Self | contact | Self | — | Read, Write |
| Organization - Global Read | sprk_organization | Global | — | Read |

All permissions must have **Secure Project Participant** web role assigned.

**Step 8**: Clear portal cache
- Power Pages admin center → Portal Actions → Clear cache

- [ ] All 7 table permissions created with correct Access Types
- [ ] All permissions have Secure Project Participant web role assigned
- [ ] Cache cleared after configuration

### Task 022: Configure Power Pages Web API Site Settings

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase3-task022-web-api-site-settings.md`

In Portal Management → Website → Site Settings, create:

| Name | Value |
|------|-------|
| `Webapi/sprk_project/enabled` | `true` |
| `Webapi/sprk_project/fields` | `sprk_projectid,sprk_name,sprk_issecure,sprk_status,sprk_externalaccountid` |
| `Webapi/sprk_document/enabled` | `true` |
| `Webapi/sprk_document/fields` | `sprk_documentid,sprk_name,sprk_filename,sprk_projectid,sprk_matterid,createdon` |
| `Webapi/sprk_event/enabled` | `true` |
| `Webapi/sprk_event/fields` | `sprk_eventid,sprk_name,sprk_startdate,sprk_enddate,sprk_projectid` |
| `Webapi/contact/enabled` | `true` |
| `Webapi/contact/fields` | `contactid,fullname,emailaddress1` |
| `Webapi/sprk_organization/enabled` | `true` |
| `Webapi/sprk_organization/fields` | `sprk_organizationid,sprk_name` |
| `Authentication/Registration/Enabled` | `true` |
| `Authentication/Registration/OpenRegistrationEnabled` | `false` |
| `OAuth/ImplicitGrantEnabled` | `true` |
| `OAuth/ExpiresIn` | `3600` |
| `OAuth/AllowedClientIds` | `*` |

- [ ] All 15 site settings created
- [ ] Portal cache cleared

### Task 023: Configure CSP, CORS, and Security Settings

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase3-task023-csp-cors-security.md`

**CSP** — In Portal Management → Site Settings:

| Name | Value |
|------|-------|
| `HTTP/ContentSecurityPolicy` | `default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net` |

**CORS** — On BFF API (via `CorsModule.cs`):

```csharp
policy.WithOrigins("https://spaarkedev1.powerappsportals.com")
      .AllowAnyMethod()
      .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "RequestVerificationToken")
      .AllowCredentials();
```

After modifying `CorsModule.cs`, redeploy the BFF API (see Phase 2 deployment steps).

**Unblock .js file uploads**:
1. Power Platform Admin Center → Environments → spaarkedev1 → Settings → Product → Privacy + Security
2. Remove `js` from the Blocked Attachments list
3. Save

- [ ] CSP site setting created with `connect-src` including BFF API domain
- [ ] BFF API CORS configured for portal origin (not wildcard)
- [ ] `.js` removed from blocked file types list
- [ ] Portal cache cleared

### Phase 3 Verification

- [ ] External user can complete sign-up via portal login page
- [ ] Contact record created with correct email, firstname, lastname after sign-up
- [ ] `adx_externalidentity` record links to Contact
- [ ] `GET /_api/sprk_projects` returns 200 for authenticated user (scoped data)
- [ ] `GET /_api/sprk_projects` returns 401 for anonymous user
- [ ] `fetch()` to BFF API from portal browser console succeeds (no CSP error)
- [ ] CORS preflight OPTIONS returns `Access-Control-Allow-Origin: https://spaarkedev1.powerappsportals.com`

---

## Phase 4 & 5: SPA Build and Deployment

**Detailed guide**: `projects/sdap-secure-project-module/notes/phase5-task050-spa-deployment-guide.md`
**Estimated time**: ~1 hour

### SPA Pre-Deployment Checklist

- [ ] Phase 3 configuration complete (Tasks 020–023 verified above)
- [ ] BFF API healthy: `curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz`
- [ ] `.js` file uploads unblocked in Power Platform Admin Center (done in Task 023)
- [ ] PAC CLI 1.44.x or later authenticated to `https://spaarkedev1.crm.dynamics.com`

### Environment Variables

Create `src/client/external-spa/.env.local` (gitignored — do not commit):

```
VITE_BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
VITE_PORTAL_URL=https://spaarkedev1.powerappsportals.com
VITE_PORTAL_CLIENT_ID={implicit-grant-client-id-from-task-022}
```

### SPA Deployment Steps

- [ ] **Step 1**: Install dependencies and build

  ```bash
  cd src/client/external-spa

  npm install

  npm run build
  ```

  Expected output:
  ```
  vite v5.4.x building for production...
  ✓ modules transformed.
  dist/index.html  ~802 kB
  ✓ built in ~3s
  ```

  Verify: `dist/index.html` exists (~802 kB), no other files (all JS/CSS inlined).

- [ ] **Step 2**: Authenticate PAC CLI

  ```powershell
  pac auth list
  # Confirm active profile → https://spaarkedev1.crm.dynamics.com

  # If not authenticated:
  pac auth create --url https://spaarkedev1.crm.dynamics.com
  ```

- [ ] **Step 3**: Upload SPA to Power Pages

  ```powershell
  pac pages upload-code-site `
      --rootPath "src/client/external-spa" `
      --compiledPath "./dist" `
      --siteName "Spaarke External Portal"
  ```

- [ ] **Step 4**: Activate site (first deployment only)
  - Open Power Pages Studio: `https://make.powerpages.microsoft.com`
  - Select `spaarkedev1` environment
  - Find "Spaarke External Portal" under Inactive sites
  - Click Activate
  - Note the portal URL

- [ ] **Step 5**: Verify SPA loads

  Navigate to `https://spaarkedev1.powerappsportals.com`
  Expected: React SPA shell loads (not Liquid-rendered page)

- [ ] **Step 6**: Verify authentication flow (InPrivate browser)
  - Portal redirects to Entra External ID login
  - Sign in with test external user
  - After login: SPA renders Workspace Home with user context

- [ ] **Step 7**: Verify home page API calls (browser DevTools Network tab)
  - `GET /_api/sprk_projects` → 200 with project data
  - `GET /api/v1/external/me` (BFF API) → 200 with user context

### Phase 4 & 5 Verification

- [ ] SPA build produces single `dist/index.html` with no errors
- [ ] `pac pages upload-code-site` succeeds
- [ ] Portal loads SPA (not a 404 or blank page)
- [ ] Authentication redirect to Entra External ID works
- [ ] Post-login: Workspace Home renders with user name and project list
- [ ] Network calls to both `/_api/` and BFF API return 200

---

## Phase 6: Internal Tools Deployment

**Estimated time**: ~30 minutes

Phase 6 adds UI components to existing internal Dataverse forms. These are deployed as part of the PCF or Code Page build, not as a separate Dataverse solution export/import.

### Deploy Code Page Updates

If `CreateProjectWizard` or `CloseProjectDialog` Code Pages were modified:

```bash
# Build the affected code pages
cd src/client/code-pages/CreateProjectWizard
npm install && npm run build

cd src/client/code-pages/CloseProjectDialog
npm install && npm run build
```

Then deploy using the `code-page-deploy` skill or:

```powershell
pwsh scripts/Deploy-CorporateWorkspace.ps1
```

### Verify Phase 6 Components

- [ ] Create Project Wizard shows "Make this a Secure Project" toggle on Step 1
- [ ] When toggle is ON: wizard shows BU provisioning step
- [ ] On secure project creation: SPE container provisioned, BU created, Account created
- [ ] `sprk_project` record has `sprk_issecure=true`, `sprk_securitybuid`, `sprk_externalaccountid` populated
- [ ] Close Project Dialog is accessible on the project form for internal users
- [ ] Close Project action revokes all active participants

---

## Phase 7: Full Test Suite

**Estimated time**: ~1 hour

### Run All Tests

```bash
# All unit tests
dotnet test tests/ --configuration Release

# Integration tests only
dotnet test tests/Sprk.Bff.Api.IntegrationTests/ --configuration Release

# With coverage report
dotnet test tests/ --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings
```

### Run API Endpoint Validation Script

```powershell
pwsh scripts/Test-SdapBffApi.ps1 -BaseUrl https://spe-api-dev-67e2xz.azurewebsites.net
```

### Expected Test Results

| Test Suite | Count | Expected |
|------------|-------|----------|
| Phase 2 unit tests (ExternalAccess*) | 65 | All pass |
| Phase 2 integration tests | 41 | All pass |
| E2E test specs (Phase 7, tasks 070-074) | 5 specs | All pass |
| Total | 111+ | All pass |

---

## Final Smoke Test — Complete End-to-End Flow

This manual smoke test validates the entire system as a cohesive unit. Perform after all prior phases are deployed and verified.

### Smoke Test Prerequisites

- [ ] At least one existing `sprk_project` record in Dataverse (for access grant testing)
- [ ] At least one `contact` record representing a test external user
- [ ] Test user email not previously registered on the portal (for sign-up flow)

---

### Smoke Test 1: Secure Project Creation

**Actor**: Internal Dataverse user

1. Open the Spaarke Legal Workspace or Corporate Workspace in Dataverse
2. Start Create Project wizard
3. On Step 1, toggle "Make this a Secure Project" → ON
4. Complete the wizard to create the project

**Verify in Dataverse** (`https://spaarkedev1.crm.dynamics.com`):
- [ ] New `sprk_project` record exists with `sprk_issecure = true`
- [ ] `sprk_securitybuid` is populated with the GUID of a newly created Business Unit named `SP-{ProjectRef}`
- [ ] `sprk_externalaccountid` is populated with the GUID of a newly created Account owned by the BU
- [ ] SPE container exists and is accessible (check via BFF API or SPE admin)

**Expected BFF API behavior** (verify via logs or direct call with valid auth):
- `POST /api/v1/external-access/provision` was called during creation
- Response includes `containerId`, `businessUnitId`, `accountId`

---

### Smoke Test 2: Invite External User

**Actor**: Internal Dataverse user with Full Access or who owns the project

1. On the `sprk_project` form, open the External Participants subgrid
2. Click "Invite" (or Quick Create) to add a new external participant
3. Select the test contact, choose access level "Collaborate", set an expiration date
4. Save the participation record

**Expected BFF API call**:

```bash
POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/invite
# Body: { projectId, contactId, accessLevel: "collaborate", ... }
```

**Verify the three-plane provisioning**:
- [ ] `sprk_externalrecordaccess` record created with status Active, `sprk_accesslevel = Collaborate`
- [ ] Contact added to SPE container membership (check SPE admin or via Graph API)
- [ ] Contact assigned `Secure Project Participant` web role in Power Pages
- [ ] Invitation email sent to test contact email address (check email or `sprk_communication` logs)

**Also call the invite endpoint explicitly**:

```bash
POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/invite
Authorization: Bearer {internal-user-token}
Content-Type: application/json

{
  "projectId": "{project-guid}",
  "contactId": "{contact-guid}",
  "accessLevel": "collaborate"
}
```

Expected: HTTP 200 with invitation details including portal registration link.

---

### Smoke Test 3: External User Login and Workspace

**Actor**: Test external user (the invited contact)

1. Open an InPrivate/Incognito browser
2. Navigate to `https://spaarkedev1.powerappsportals.com`
3. Click Sign In → redirected to Entra External ID
4. If first-time: complete sign-up with the invited email address
5. After sign-in: SPA renders Workspace Home

**Verify Workspace Home**:
- [ ] User's name displays correctly (from claim mapping)
- [ ] Project list shows the invited project (and only that project — data isolation)
- [ ] Project card shows correct project name, status, reference number

**Verify in Dataverse**:
- [ ] `contact` record exists with correct email, firstname, lastname (from Entra External ID claims)
- [ ] `adx_externalidentity` record links the contact to the Entra External ID identity

---

### Smoke Test 4: Access Level Enforcement in SPA

**Actor**: Test external user logged into the portal

#### View Only scenario

If the test user's access level is View Only (100000000):
- [ ] Documents tab shows document list (read-only, no Upload button)
- [ ] Events tab shows calendar (no Create Event button)
- [ ] AI Toolbar is hidden or disabled
- [ ] Cannot trigger document download (attempt to confirm 403 from BFF API)

#### Collaborate scenario

If the test user's access level is Collaborate (100000001):
- [ ] Documents tab shows Upload button (can initiate upload)
- [ ] Events tab shows Create Event button
- [ ] Can create a new event (POST to `/_api/sprk_events` succeeds)
- [ ] AI Toolbar is visible and enabled
- [ ] Cannot invite other external users (Invite button hidden or 403 on API call)

#### Full Access scenario

If the test user's access level is Full Access (100000002):
- [ ] All Collaborate capabilities available
- [ ] Invite External User dialog is accessible
- [ ] Can invite another external user via the SPA dialog

**Verify BFF API authorization enforcement**:

```bash
# As View Only user token — should return 403:
POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/invite
Authorization: Bearer {view-only-user-token}

# Expected: HTTP 403 Forbidden
```

---

### Smoke Test 5: Access Grant and Revoke Cycle

**Actor**: Internal Dataverse user

1. Grant access to the test contact for the test project (if not already done in Smoke Test 2)
2. Verify portal shows the project for the external user (Smoke Test 3)
3. Revoke the access:

   ```bash
   POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/revoke
   Authorization: Bearer {internal-user-token}
   Content-Type: application/json

   {
     "projectId": "{project-guid}",
     "contactId": "{contact-guid}"
   }
   ```

4. Expected response: HTTP 200

**Verify three-plane revocation**:
- [ ] `sprk_externalrecordaccess` record status = Inactive
- [ ] Contact removed from SPE container membership
- [ ] If this was the contact's only access grant: `Secure Project Participant` web role removed
- [ ] External user refreshes portal → project no longer appears in project list

---

### Smoke Test 6: Project Closure

**Actor**: Internal Dataverse user (project owner or system admin)

1. Open CloseProjectDialog on the project form (or call directly):

   ```bash
   POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/close-project
   Authorization: Bearer {internal-user-token}
   Content-Type: application/json

   {
     "projectId": "{project-guid}"
   }
   ```

2. Expected response: HTTP 200 with closure summary

**Verify closure cascade**:
- [ ] All `sprk_externalrecordaccess` records for the project set to Inactive
- [ ] All external contacts removed from SPE container membership
- [ ] SPE container STILL EXISTS (preserved for archival — must not be deleted)
- [ ] `sprk_project` record status set to Inactive or Closed
- [ ] Any external users who previously had access: portal shows no project on refresh

**Critical check**: Confirm container preserved

```bash
# Use Graph API or SPE admin to confirm container still exists:
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}
# Expected: 200 OK with container details (not 404)
```

---

### Smoke Test 7: Data Isolation Verification

**Actor**: Two different external users with access to different projects

1. Sign in as User A (has access to Project Alpha only)
2. Verify: `GET /_api/sprk_projects` returns only Project Alpha
3. Attempt direct access to Project Beta: `GET /_api/sprk_projects({project-beta-id})`
4. Expected: 403 Forbidden or empty result

5. Sign in as User B (has access to Project Beta only)
6. Verify: `GET /_api/sprk_projects` returns only Project Beta
7. Project Alpha is NOT visible

- [ ] Cross-project data isolation confirmed: no user can see another user's project data

---

## Final Validation Checklist

This master checklist summarizes all acceptance criteria from all phases. Use this as the go/no-go gate for production promotion.

### Infrastructure

| # | Criterion | Status |
|---|-----------|--------|
| 1 | BFF API deployed and `/healthz` returns `{"status":"Healthy"}` | ☐ |
| 2 | All 5 external access endpoints respond (HTTP 401 = endpoint exists) | ☐ |
| 3 | Dataverse `sprk_externalrecordaccess` table exists with all required fields | ☐ |
| 4 | Dataverse `sprk_project` has all three secure project fields | ☐ |
| 5 | SpaarkeCore solution imported and published successfully | ☐ |
| 6 | Power Pages SPA deployed and loads at portal URL | ☐ |
| 7 | `.js` file uploads unblocked in Power Platform Admin Center | ☐ |

### Authentication & Identity

| # | Criterion | Status |
|---|-----------|--------|
| 8 | Entra External ID identity provider configured on Power Pages | ☐ |
| 9 | External user can complete sign-up via portal | ☐ |
| 10 | Contact record created with correct claim mappings after sign-up | ☐ |
| 11 | `adx_externalidentity` record links contact to Entra External ID identity | ☐ |
| 12 | Returning user sign-in does not create duplicate Contact | ☐ |
| 13 | Client secret stored in Key Vault as `EntraExternalId-PowerPages-ClientSecret` | ☐ |

### Power Pages Configuration

| # | Criterion | Status |
|---|-----------|--------|
| 14 | "Secure Project Participant" web role exists | ☐ |
| 15 | Table permission chain configured (7 permissions, all with correct Access Types) | ☐ |
| 16 | All 5 tables enabled in Web API site settings | ☐ |
| 17 | Field whitelists configured for all 5 tables | ☐ |
| 18 | `OAuth/ImplicitGrantEnabled = true` site setting exists | ☐ |
| 19 | CSP `connect-src` includes BFF API domain | ☐ |
| 20 | BFF API CORS allows portal origin (no wildcard) | ☐ |
| 21 | Portal cache cleared after all configuration changes | ☐ |

### BFF API

| # | Criterion | Status |
|---|-----------|--------|
| 22 | All 65 unit tests pass | ☐ |
| 23 | All 41 integration tests pass | ☐ |
| 24 | ExternalCallerAuthorizationFilter validates portal JWT | ☐ |
| 25 | Grant endpoint provisions all three planes (Dataverse, SPE, AI Search) | ☐ |
| 26 | Revoke endpoint deactivates all three planes | ☐ |
| 27 | Invite endpoint creates `sprk_externalrecordaccess` record and sends email | ☐ |
| 28 | Close project endpoint cascades revocation to all participants | ☐ |
| 29 | SPE container preserved on project closure (not deleted) | ☐ |

### SPA — Core

| # | Criterion | Status |
|---|-----------|--------|
| 30 | SPA loads without errors in Chrome, Edge, and Firefox | ☐ |
| 31 | Authentication redirect to Entra External ID works | ☐ |
| 32 | Post-login: SPA renders Workspace Home with user context | ☐ |
| 33 | Workspace Home shows list of accessible projects | ☐ |
| 34 | Project page loads with all tabs (Documents, Events, Tasks, Contacts) | ☐ |

### SPA — Access Level Enforcement

| # | Criterion | Status |
|---|-----------|--------|
| 35 | View Only user: no Upload button, no Create Event, no AI toolbar | ☐ |
| 36 | Collaborate user: can create events, can use AI toolbar | ☐ |
| 37 | Full Access user: can invite external users | ☐ |
| 38 | View Only user BFF API call for restricted action returns 403 | ☐ |

### Data Isolation

| # | Criterion | Status |
|---|-----------|--------|
| 39 | External user sees only their own projects (table permission chain enforced) | ☐ |
| 40 | Direct access to another user's project returns 403 or empty result | ☐ |
| 41 | User with no active access grants sees zero projects | ☐ |
| 42 | Cross-project document isolation: user cannot fetch documents from projects they are not granted | ☐ |

### Internal Tools

| # | Criterion | Status |
|---|-----------|--------|
| 43 | Create Project Wizard shows "Secure Project" toggle | ☐ |
| 44 | Secure project creation provisions BU, SPE container, Account | ☐ |
| 45 | Project record has all three secure fields populated after creation | ☐ |
| 46 | Close Project Dialog accessible on project form | ☐ |
| 47 | Project closure deactivates all participant records | ☐ |

### E2E Tests

| # | Criterion | Status |
|---|-----------|--------|
| 48 | E2E 070: Secure project creation flow passes | ☐ |
| 49 | E2E 071: External user invitation and onboarding passes | ☐ |
| 50 | E2E 072: Access level enforcement passes | ☐ |
| 51 | E2E 073: Access revocation across UAC planes passes | ☐ |
| 52 | E2E 074: Project closure cascading passes | ☐ |

---

## Rollback Procedures

### BFF API Rollback

```bash
# Restart App Service (clears bad deployment):
az webapp restart \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz

# View logs for diagnosis:
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

### SPA Rollback

The previous SPA version can be restored by re-uploading the prior build artifact:

```powershell
# Rebuild from last known good commit:
git checkout {last-good-commit} -- src/client/external-spa/

cd src/client/external-spa
npm run build

pac pages upload-code-site `
    --rootPath "src/client/external-spa" `
    --compiledPath "./dist" `
    --siteName "Spaarke External Portal"
```

### Dataverse Solution Rollback

If a schema deployment causes issues, export the previous managed solution from the environment before importing the new version. Use `--force-overwrite` cautiously — schema changes (like removing columns) can cause data loss.

---

## Troubleshooting

### BFF API Issues

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| `/healthz` returns 503 | App Service startup failure | Check `az webapp log tail` for startup errors |
| External access endpoints return 404 | Deployment did not include new endpoints | Rebuild and redeploy; verify endpoint registration in `Program.cs` |
| All calls return 500 | Configuration missing (app settings) | Check `PowerPages__BaseUrl` and `PowerPages__SecureProjectParticipantWebRoleId` are set |
| 401 on authenticated call | Token validation failing | Verify `PowerPages__BaseUrl` matches actual portal URL exactly |

### Power Pages Issues

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| Portal redirects loop | Entra External ID misconfigured | Verify authority URL uses `ciamlogin.com` not `login.microsoftonline.com` |
| User sees all projects (no scoping) | Table permission uses Global instead of Parent chain | Recreate `Project - ERA Parent` with Access Type = Parent |
| 403 on all Web API calls | Web role not assigned to table permissions | Add Secure Project Participant web role to all 7 table permissions |
| `/_api/sprk_projects` returns 404 | Web API not enabled for table | Create `Webapi/sprk_project/enabled = true` site setting |
| SPA shows blank page | CSP blocking script execution or `.js` blocked | Check browser console; unblock `.js` in PPAC Privacy + Security |
| CORS error in browser | BFF API CORS not configured for portal domain | Update `CorsModule.cs` `WithOrigins()`; redeploy BFF API |

### SPA Issues

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| `pac pages upload-code-site` fails with auth error | PAC CLI not authenticated | `pac auth create --url https://spaarkedev1.crm.dynamics.com` |
| Upload fails with "extension not allowed" | `.js` still blocked | Unblock in PPAC Privacy + Security (Power Platform Admin Center) |
| Site shows 404 after upload | Site not activated | Activate in Power Pages Studio under Inactive sites |
| No projects visible after login | User missing ERA records or wrong portal URL | Create test ERA record; verify `VITE_PORTAL_URL` matches actual portal |
| AI toolbar not visible for Collaborate user | Access level enforcement too restrictive | Check `useAccessLevel` hook logic in SPA; verify ERA `sprk_accesslevel` value |

---

## Post-Deployment Notes

1. **Web role GUID**: Record the GUID of the "Secure Project Participant" web role in this file after Task 021 is complete. The BFF API `PowerPages__SecureProjectParticipantWebRoleId` app setting must match.

   `SecureProjectParticipantWebRoleId`: _(fill in after Task 021)_ `__________________`

2. **Portal URL**: Confirm the final portal URL after site activation.

   `Portal URL`: `https://spaarkedev1.powerappsportals.com` _(confirm or update)_

3. **Implicit grant client ID**: Record the client ID configured for `OAuth/ImplicitGrantEnabled`.

   `ImplicitGrantClientId`: _(fill in after Task 022)_ `__________________`

4. **SPE Container naming**: Verify the provisioned container naming convention (`SP-{ProjectRef}`) matches what was implemented in Task 014 (SPE Container Membership Service).

5. **Email notifications**: Verify that the invitation email template in `sprk_communication` is configured and sends correctly. The email content is defined in the `sprk_communication` module — this deployment guide does not include changes to email templates.

---

*Created: 2026-03-16 | Task 075 | Phase 7 final deployment guide | sdap-secure-project-module*
