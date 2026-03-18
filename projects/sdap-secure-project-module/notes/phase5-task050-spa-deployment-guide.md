# Phase 5 — SPA Deployment Guide (Task 050)

> **Purpose**: Deploy the Secure Project Workspace SPA to Power Pages as a Code Page (SPA/code site).
> **Prerequisites**: Task 049 (Access Level Enforcement) complete. PAC CLI 1.44.x or later. Power Pages site version 9.8.1.x or later.
> **SPA Location**: `src/client/external-spa/`
> **Build Output**: `src/client/external-spa/dist/index.html` (single self-contained HTML, ~802 kB)
> **Portal Target**: `https://spaarkedev1.powerappsportals.com` (Dataverse env: `https://spaarkedev1.crm.dynamics.com`)

---

## Overview

The Secure Project Workspace SPA is a single-page application (React 18 + Vite) deployed to Power Pages as a **code site** — Microsoft's GA (since Feb 8, 2026, site version 9.8.1.x) capability for hosting a fully client-side SPA within Power Pages.

This is NOT a traditional Dataverse HTML web resource. It uses `pac pages upload-code-site` and is managed as a Power Pages SPA site, not imported via PAC solution import. The build tool is Vite with `vite-plugin-singlefile`, which produces a single `dist/index.html` with all JS and CSS inlined — this is what gets uploaded to Power Pages.

---

## Pre-Deployment Checklist

- [ ] PAC CLI version is 1.44.x or later: `pac --version`
- [ ] Power Pages site is version 9.8.1.x or later (check in Power Pages Studio → Settings)
- [ ] PAC CLI authenticated to target Dataverse env: `pac auth list`
- [ ] `.js` file uploads are unblocked in Power Platform Admin Center (see note below)
- [ ] Phase 3 configuration complete (Tasks 020–023): Entra External ID, web roles, table permissions, CSP, Web API site settings
- [ ] BFF API is deployed and healthy (Task 019): `curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz`
- [ ] `.env.local` file exists in `src/client/external-spa/` with correct values (see Environment Variables section)

### Unblock .js File Uploads (REQUIRED — common gotcha)

Power Platform blocks `.js` file uploads by default. This must be done once per environment before the first SPA upload.

1. Open **Power Platform Admin Center**: https://admin.powerplatform.microsoft.com
2. Navigate to **Environments** → select `spaarkedev1`
3. Go to **Settings** → **Product** → **Privacy + Security**
4. Remove `js` from the **Blocked Attachments** list
5. Save

---

## Environment Variables

Copy `.env.example` to `.env.local` in `src/client/external-spa/` and set these values before building for production:

| Variable | Value (Dev) | Purpose |
|---|---|---|
| `VITE_BFF_API_URL` | `https://spe-api-dev-67e2xz.azurewebsites.net` | BFF API base URL |
| `VITE_PORTAL_URL` | `https://spaarkedev1.powerappsportals.com` | Power Pages portal URL (dev server proxy only) |
| `VITE_PORTAL_CLIENT_ID` | `{GUID of implicit grant flow client}` | OAuth client ID registered in portal site settings |

**Important**: `VITE_PORTAL_CLIENT_ID` must match the value configured in the Power Pages site setting `ImplicitGrantFlow/RegisteredClientId` (set up in Task 022).

`.env.local` is gitignored — never commit it.

---

## Step 1: Build the SPA

```bash
cd src/client/external-spa

# Install dependencies (if not already installed)
npm install

# Build production bundle — outputs dist/index.html
npm run build
```

**Expected output:**

```
vite v5.4.x building for production...
✓ 2109 modules transformed.
[plugin vite:singlefile] Inlining: index-DwdyheO7.js
dist/index.html  801.80 kB │ gzip: 222.01 kB
✓ built in ~3s
```

**Verify build output:**

```bash
ls -la dist/
# Expected: index.html (~802 kB), no other files
# vite-plugin-singlefile inlines all JS/CSS into the single HTML file
```

The `dist/index.html` file is the complete, self-contained deployable artifact.

---

## Step 2: Authenticate PAC CLI to Target Environment

```powershell
# Check if already authenticated
pac auth list

# If not authenticated, create a new profile
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Select the correct profile if multiple exist
pac auth select --index {N}  # replace N with the index from pac auth list
```

For CI/CD pipelines, use service principal authentication:

```powershell
pac auth create `
    --url https://spaarkedev1.crm.dynamics.com `
    --applicationId $(APP_ID) `
    --clientSecret $(CLIENT_SECRET) `
    --tenant $(TENANT_ID)
```

---

## Step 3: Upload SPA to Power Pages

Run from the repository root:

```powershell
pac pages upload-code-site `
    --rootPath "src/client/external-spa" `
    --compiledPath "./dist" `
    --siteName "Spaarke External Portal"
```

Or equivalently, from inside the SPA directory:

```powershell
cd src/client/external-spa

pac pages upload-code-site `
    --rootPath "." `
    --compiledPath "./dist" `
    --siteName "Spaarke External Portal"
```

**What this does:**
- Reads `powerpages.config.json` in `--rootPath` for site name and landing page configuration
- Uploads `dist/index.html` to Power Pages as the SPA entry point
- Maps the site to `defaultLandingPage: "index.html"` (as defined in `powerpages.config.json`)

**`powerpages.config.json` reference:**

```json
{
  "siteName": "Spaarke External Portal",
  "defaultLandingPage": "index.html",
  "compiledPath": "./dist"
}
```

---

## Step 4: Activate the Site (First Deployment Only)

After the first upload, the site appears in **Inactive sites** in Power Pages Studio. It must be activated manually once. Subsequent uploads update the active site automatically.

1. Open **Power Pages Studio**: https://make.powerpages.microsoft.com
2. Select the `spaarkedev1` environment
3. Find **"Spaarke External Portal"** under **Inactive sites**
4. Click **Activate**
5. Confirm activation — the site will appear under **Active sites**

After activation, the portal URL will be confirmed. Note the URL (e.g., `https://spaarkedev1.powerappsportals.com`) — this is the `VITE_PORTAL_URL` value.

> Note: If the site was previously activated (existing Power Pages site), skip this step. Subsequent `pac pages upload-code-site` calls update the active site directly.

---

## Step 5: Verify SPA Loads in Browser

1. Navigate to the portal URL: `https://spaarkedev1.powerappsportals.com`
2. Expected: Browser loads the React SPA shell (not a Liquid-rendered page)
3. Verify the Spaarke Workspace Home page renders (or redirects to login)

**If the page shows a 404 or blank white page:**
- Check that the site was activated (Step 4)
- Check that `.js` uploads are unblocked (Pre-Deployment Checklist)
- Check browser console for CSP violations (see Troubleshooting)

---

## Step 6: Verify Authentication Flow

1. Open the portal URL in a private/incognito browser window
2. Expected: SPA redirects to Entra External ID login page
3. Sign in with a test external user account
4. Expected after login: SPA redirects back to portal and renders Workspace Home with user context

**What to check:**
- The Entra External ID identity provider is active (Task 020)
- The `adx_externalidentity` table has a record for the test user after login
- The portal session cookie is set (check browser DevTools → Application → Cookies)

---

## Step 7: Verify Home Page Loads with User Context

1. After successful login, navigate to `https://spaarkedev1.powerappsportals.com`
2. Expected: Workspace Home page renders with the logged-in user's name and accessible projects
3. Check browser DevTools Network tab:
   - `GET /_api/sprk_projects` returns 200 with project data (Power Pages Web API)
   - `GET /api/v1/external/me` (BFF API) returns 200 with user context

**If no projects are visible:**
- Verify the test user's Contact record has at least one `sprk_externalrecordaccess` record with status "active"
- Verify table permissions (Task 021) are correctly configured for the `Secure Project Participant` web role

---

## Subsequent Deployments (Update Flow)

For any code change after the initial deployment:

```bash
# 1. Rebuild
cd src/client/external-spa
npm run build

# 2. Re-upload (from repo root)
pac pages upload-code-site `
    --rootPath "src/client/external-spa" `
    --compiledPath "./dist" `
    --siteName "Spaarke External Portal"
```

No site reactivation needed. The upload replaces the current active site content.

**Before redeploying**, download the current site config first to avoid accidentally overwriting auth provider settings that were configured via Studio:

```powershell
# Download current site config (do this before overwriting)
pac pages download-code-site `
    --environment "https://spaarkedev1.crm.dynamics.com" `
    --path "./downloaded-site" `
    --webSiteId "{site-guid}" `
    --overwrite
```

> The `{site-guid}` can be found in Power Pages Studio → Site Details, or via:
> ```powershell
> pac pages list
> ```

---

## CI/CD Pipeline (Azure DevOps)

For automated deployment from a pipeline:

```yaml
# azure-pipelines.yml — SPA deployment stage
- stage: DeploySPA
  jobs:
    - job: BuildAndDeploy
      steps:
        - task: NodeTool@0
          inputs:
            versionSpec: '20.x'

        - script: |
            npm ci
            npm run build
          workingDirectory: src/client/external-spa
          displayName: 'Build SPA'

        - script: |
            pac auth create \
              --url $(DATAVERSE_URL) \
              --applicationId $(APP_ID) \
              --clientSecret $(CLIENT_SECRET) \
              --tenant $(TENANT_ID)
            pac pages upload-code-site \
              --rootPath "." \
              --compiledPath "./dist" \
              --siteName "Spaarke External Portal"
          workingDirectory: src/client/external-spa
          displayName: 'Deploy to Power Pages'
          env:
            DATAVERSE_URL: $(DATAVERSE_URL)
            APP_ID: $(APP_ID)
            CLIENT_SECRET: $(CLIENT_SECRET)
            TENANT_ID: $(TENANT_ID)
```

---

## Power Pages Site Configuration Reference

These settings must be in place (configured in Tasks 020–023) before the SPA will function. Included here for completeness and verification during deployment.

### Identity Provider (Task 020)

| Setting | Value |
|---|---|
| Provider type | Microsoft Entra External ID |
| Tenant | `{entra-external-id-tenant}.onmicrosoft.com` |
| Client ID | `{app-registration-client-id}` |
| Auto-create contact | Enabled |
| Claim mappings | email → `emailaddress1`, given_name → `firstname`, family_name → `lastname` |

### Implicit Grant Flow — BFF API OAuth (Task 022)

| Site Setting | Value |
|---|---|
| `ImplicitGrantFlow/RegisteredClientId` | `{client-id — matches VITE_PORTAL_CLIENT_ID}` |
| `Connector/ImplicitGrantFlowEnabled` | `True` |
| `ImplicitGrantFlow/TokenExpirationTime` | `900` (15 minutes; max 3600) |

### Web API — Tables Enabled for SPA (Task 022)

| Site Setting | Value |
|---|---|
| `Webapi/sprk_project/enabled` | `true` |
| `Webapi/sprk_project/fields` | `sprk_name,sprk_description,sprk_referencenumber,sprk_issecure,...` |
| `Webapi/sprk_document/enabled` | `true` |
| `Webapi/sprk_document/fields` | `sprk_name,sprk_documenttype,sprk_summary,...` |
| `Webapi/sprk_event/enabled` | `true` |
| `Webapi/sprk_event/fields` | `sprk_name,sprk_duedate,sprk_status,...` |
| `Webapi/sprk_externalrecordaccess/enabled` | `true` |
| `Webapi/sprk_externalrecordaccess/fields` | `sprk_name,sprk_accesslevel,sprk_status,...` |

### Content Security Policy (Task 023)

```
HTTP/Content-Security-Policy = script-src 'self'; connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net; style-src 'self' 'unsafe-inline'
```

### Web Roles (Task 021)

| Role | Purpose |
|---|---|
| `Secure Project Participant` | Assigned to all contacts with active `sprk_externalrecordaccess` records |
| `Authenticated Users` | Base role for all logged-in contacts |

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `pac pages upload-code-site` fails with auth error | PAC CLI not authenticated | Run `pac auth create --url https://spaarkedev1.crm.dynamics.com` |
| Upload fails with "extension not allowed" for .js | `.js` not unblocked in PPAC | Unblock `.js` in Privacy + Security settings (Pre-Deployment Checklist) |
| Site shows 404 after upload | Site not activated | Activate in Power Pages Studio (Step 4) |
| Browser shows blank white page | CSP blocking script execution | Check browser console for CSP errors; update `HTTP/Content-Security-Policy` site setting |
| Authentication loop / redirect fails | Entra External ID misconfigured | Verify Task 020 settings; check redirect URIs include the portal URL |
| `/_api/...` returns 401 with `MissingPortalSessionCookie` | No authenticated portal session | User must log in through Entra External ID first |
| `/_api/...` returns 403 with `TablePermissionCreateIsMissing` | Web role lacks permission | Check Task 021 table permission chain for `Secure Project Participant` role |
| BFF API returns 401 from SPA | `VITE_PORTAL_CLIENT_ID` mismatch | Ensure `.env.local` `VITE_PORTAL_CLIENT_ID` matches `ImplicitGrantFlow/RegisteredClientId` site setting |
| Home page shows no projects | User missing `sprk_externalrecordaccess` record | Create a test access record for the test Contact in Dataverse |
| `npm run build` fails with "Cannot find module" | `node_modules` missing | Run `npm install` in `src/client/external-spa/` first |
| `pac pages upload-code-site` command not found | PAC CLI outdated | Upgrade to PAC CLI 1.44.x or later: `npm install -g @microsoft/powerplatform-cli` |

---

## Architecture Notes

The SPA communicates with two API surfaces:

1. **Power Pages Web API** (`/_api/...`) — OData 4.0 proxy to Dataverse, scoped by table permissions. Used for lightweight reads (project list, contact info, events, tasks). Authenticated via portal session cookie + CSRF token.

2. **BFF API** (`https://spe-api-dev-67e2xz.azurewebsites.net`) — Used for SPE file operations, AI features, playbook execution, email, and all business logic that requires elevated permissions. Authenticated via portal-issued OAuth token from `/_services/auth/token`.

The SPA is served as a single `index.html` from the Power Pages CDN. All routing is client-side (React Router). Unauthenticated users are redirected to the Entra External ID login page by Power Pages before the SPA loads.

---

*Deployment guide for task 050 | Phase 5 SPA deployment | 2026-03-16*
