# SDAP Office Integration - Deployment Log

> **Purpose**: Track deployments for the SDAP Office Integration project
> **Last Updated**: 2026-01-20

---

## Table of Contents

1. [Task 081: Production Add-in Deployment](#task-081-production-add-in-deployment)
2. [Task 080: Production BFF API Deployment](#task-080-production-bff-api-deployment)
3. [Task 015: Dataverse Solution Deployment](#task-015-dataverse-solution-deployment)
4. [Task 057: Outlook Add-in Deployment](#task-057-outlook-add-in-deployment)
5. [Task 058: Word Add-in Deployment](#task-058-word-add-in-deployment)

---

# Task 081: Production Add-in Deployment

> **Status**: Ready for Deployment
> **Type**: Office Add-in Production Deployment
> **Task Reference**: Task 081 - Production deployment: Add-ins
> **Date Created**: 2026-01-20

---

## Executive Summary

This section provides comprehensive guidance for deploying the Spaarke Office add-ins (Outlook and Word) to production environments. It covers manifest configuration for production URLs, Microsoft 365 Admin Center deployment procedures, tenant-wide deployment options, verification testing, and rollback procedures.

**Prerequisites**: Task 080 (Production BFF API deployment) must be completed before deploying add-ins, as the add-ins depend on production API endpoints.

---

## 1. Add-in Overview

### Add-in Information

| Property | Outlook Add-in | Word Add-in |
|----------|----------------|-------------|
| **Add-in ID** | `c1258e2d-1688-49d2-ac99-a7485ebd9995` | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| **Name** | Spaarke | Spaarke |
| **Version** | 1.0.0 | 1.0.0 |
| **Manifest Type** | Unified JSON | XML (add-in-only) |
| **Target Hosts** | New Outlook, Outlook Web | Word Desktop, Word Web |
| **Required API** | Mailbox 1.8+ | WordApi 1.3+ |

### Manifest Files

| Environment | Outlook Manifest | Word Manifest |
|-------------|------------------|---------------|
| **Development** | `src/client/office-addins/outlook/manifest.json` | `src/client/office-addins/word/manifest.xml` |
| **Production** | `src/client/office-addins/outlook/manifest.prod.json` | `src/client/office-addins/word/manifest.prod.xml` |

---

## 2. Production URL Configuration

### 2.1 Static Asset Hosting URLs

| Environment | Static Assets URL | API URL |
|-------------|-------------------|---------|
| **Development** | `https://localhost:3000` | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| **Staging** | `https://spe-office-addins-dev.azurestaticapps.net` | `https://spe-api-staging-*.azurewebsites.net` |
| **Production** | `https://spe-office-addins-prod.azurestaticapps.net` | `https://spe-api-prod-*.azurewebsites.net` |

### 2.2 Outlook Production Manifest URLs

Update the following URLs in `manifest.prod.json` for production deployment:

| Element Path | Development Value | Production Value |
|--------------|-------------------|------------------|
| `extensions[0].runtimes[0].code.page` | `https://localhost:3000/outlook/taskpane.html` | `https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html` |
| `extensions[0].runtimes[1].code.page` | `https://localhost:3000/outlook/commands.html` | `https://spe-office-addins-prod.azurestaticapps.net/outlook/commands.html` |
| `extensions[0].runtimes[1].code.script` | `https://localhost:3000/outlook/commands.js` | `https://spe-office-addins-prod.azurestaticapps.net/outlook/commands.bundle.js` |
| All `ribbons[*].groups[*].icons[*].url` | `https://localhost:3000/assets/*` | `https://spe-office-addins-prod.azurestaticapps.net/assets/*` |
| All `ribbons[*].groups[*].controls[*].icons[*].url` | `https://localhost:3000/assets/*` | `https://spe-office-addins-prod.azurestaticapps.net/assets/*` |

### 2.3 Word Production Manifest URLs

Update the following URLs in `manifest.prod.xml` for production deployment:

| Element | Development Value | Production Value |
|---------|-------------------|------------------|
| `IconUrl` | `https://localhost:3000/assets/icon-32.png` | `https://spe-office-addins-prod.azurestaticapps.net/assets/icon-32.png` |
| `HighResolutionIconUrl` | `https://localhost:3000/assets/icon-80.png` | `https://spe-office-addins-prod.azurestaticapps.net/assets/icon-80.png` |
| `AppDomains/AppDomain` | `https://localhost:3000` | `https://spe-office-addins-prod.azurestaticapps.net` |
| `DefaultSettings/SourceLocation` | `https://localhost:3000/word/taskpane.html` | `https://spe-office-addins-prod.azurestaticapps.net/word/taskpane.html` |
| All `bt:Image` elements | `https://localhost:3000/assets/*` | `https://spe-office-addins-prod.azurestaticapps.net/assets/*` |
| `bt:Url id="Taskpane.Url"` | `https://localhost:3000/word/taskpane.html` | `https://spe-office-addins-prod.azurestaticapps.net/word/taskpane.html` |
| `bt:Url id="Commands.Url"` | `https://localhost:3000/word/commands.html` | `https://spe-office-addins-prod.azurestaticapps.net/word/commands.html` |

### 2.4 Manifest Update Script

```powershell
# Production URL replacement script
$prodStaticUrl = "https://spe-office-addins-prod.azurestaticapps.net"

# Update Outlook manifest
$outlookManifest = Get-Content "src/client/office-addins/outlook/manifest.json" -Raw
$outlookManifest = $outlookManifest -replace "https://localhost:3000", $prodStaticUrl
Set-Content "src/client/office-addins/outlook/manifest.prod.json" $outlookManifest

# Update Word manifest
$wordManifest = Get-Content "src/client/office-addins/word/manifest.xml" -Raw
$wordManifest = $wordManifest -replace "https://localhost:3000", $prodStaticUrl
Set-Content "src/client/office-addins/word/manifest.prod.xml" $wordManifest

Write-Host "Production manifests created with URL: $prodStaticUrl"
```

---

## 3. Pre-Deployment Checklist

### 3.1 BFF API Prerequisites

- [x] Task 080 completed (Production BFF API deployed)
- [ ] BFF API health check passes: `GET https://spe-api-prod-*.azurewebsites.net/healthz`
- [ ] Office endpoints accessible: `GET /office/recent` returns 200 or 401
- [ ] Workers running (check Application Insights logs)

### 3.2 Static Asset Prerequisites

- [ ] Production add-in build completed: `npm run build:prod`
- [ ] Static assets deployed to Azure Static Web Apps
- [ ] All asset URLs accessible (icons, taskpane.html, commands.html)
- [ ] HTTPS certificate valid on static hosting

### 3.3 Manifest Prerequisites

- [ ] Production manifests created with correct URLs
- [ ] Manifest validation passes: `npx office-addin-manifest validate manifest.prod.json`
- [ ] Azure AD app registration matches manifest ID
- [ ] API permissions configured correctly

### 3.4 Authentication Prerequisites

- [ ] Azure AD app registration for add-in exists (Client ID: `c1258e2d-1688-49d2-ac99-a7485ebd9995`)
- [ ] NAA broker redirect URI configured: `brk-multihub://localhost`
- [ ] BFF API app ID URI matches `webApplicationInfo.resource`
- [ ] Delegated permissions granted: `api://{bff-api-id}/user_impersonation`, `User.Read`

### 3.5 Communication

- [ ] Deployment window communicated to stakeholders
- [ ] IT admin notified for M365 Admin Center access
- [ ] Rollback plan reviewed with team
- [ ] Support team briefed on new add-in functionality

---

## 4. Deployment Methods

### 4.1 Microsoft 365 Admin Center Centralized Deployment (RECOMMENDED)

**Best for**: Organization-wide deployment with IT management.

**Propagation Time**: 12-24 hours for full organization rollout.

#### Step 1: Build Production Add-in

```powershell
cd src/client/office-addins

# Install dependencies
npm install

# Build for production
npm run build:prod

# Verify build output
Get-ChildItem -Path "dist/" -Recurse | Select-Object FullName
```

**Expected Output:**
```
dist/
├── outlook/
│   ├── taskpane.html
│   ├── taskpane.bundle.js
│   ├── commands.html
│   └── commands.bundle.js
├── word/
│   ├── taskpane.html
│   ├── taskpane.bundle.js
│   ├── commands.html
│   └── commands.bundle.js
├── assets/
│   ├── icon-16.png, icon-32.png, icon-80.png
│   ├── icon-outline.png, icon-color.png
│   ├── save-16.png, save-32.png, save-80.png
│   ├── save-version-16.png, save-version-32.png, save-version-80.png
│   ├── share-16.png, share-32.png, share-80.png
│   └── grant-16.png, grant-32.png, grant-80.png
└── vendors.bundle.js
```

#### Step 2: Deploy Static Assets to Production Hosting

```powershell
# Option A: Azure Static Web Apps (Recommended)
az staticwebapp create `
  --name spe-office-addins-prod `
  --resource-group rg-spaarke-prod-westus2 `
  --source ./dist `
  --location westus2

# Option B: Azure Blob Storage with CDN
az storage blob upload-batch `
  --destination '$web' `
  --source ./dist `
  --account-name spaarkeaddinsprod `
  --overwrite

# Verify deployment
curl https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html
```

#### Step 3: Prepare Production Manifests

```powershell
# Run URL update script (see Section 2.4)
# Then validate manifests

npx office-addin-manifest validate src/client/office-addins/outlook/manifest.prod.json
npx office-addin-manifest validate src/client/office-addins/word/manifest.prod.xml
```

#### Step 4: Deploy Outlook Add-in via M365 Admin Center

1. **Navigate to Admin Center**
   - Go to: https://admin.microsoft.com
   - Sign in with Global Admin or Exchange Admin credentials

2. **Access Integrated Apps**
   - Navigate to: **Settings** > **Integrated apps**
   - Click **Upload custom apps**

3. **Upload Outlook Manifest**
   - Select **Office Add-in**
   - Choose: **Upload manifest file (.json)**
   - Browse to: `src/client/office-addins/outlook/manifest.prod.json`
   - Click **Upload**

4. **Configure Deployment Scope**
   - **Option A - Entire Organization**: All users get the add-in
   - **Option B - Specific Groups**: Select M365 groups for pilot rollout
   - **Option C - Just Me**: Testing only

5. **Review and Deploy**
   - Review add-in details (name, description, permissions)
   - Click **Deploy**
   - Note: Deployment can take 12-24 hours to propagate

#### Step 5: Deploy Word Add-in via M365 Admin Center

1. **Navigate to Admin Center**
   - Same location: **Settings** > **Integrated apps**
   - Click **Upload custom apps**

2. **Upload Word Manifest**
   - Select **Office Add-in**
   - Choose: **Upload manifest file (.xml)** (Word uses XML manifest)
   - Browse to: `src/client/office-addins/word/manifest.prod.xml`
   - Click **Upload**

3. **Configure Deployment Scope**
   - Match Outlook deployment scope for consistency
   - Select same user groups

4. **Review and Deploy**
   - Review add-in details
   - Click **Deploy**

#### Step 6: Verify Deployment Status

```powershell
# Check deployment status in M365 Admin Center
# Navigate to: Settings > Integrated apps > Spaarke
# Status should show: "Deployed"

# Monitor deployment progress
# Note: Full propagation takes 12-24 hours
```

---

### 4.2 Pilot Deployment (Recommended First Step)

**Best for**: Testing with a small group before organization-wide rollout.

#### Pilot Group Setup

1. **Create M365 Security Group**
   - Name: `Spaarke Office Add-in Pilot`
   - Add 5-10 users representing different roles
   - Include at least one user from each target department

2. **Deploy to Pilot Group**
   - Follow M365 Admin Center steps above
   - Select **Specific Groups** > `Spaarke Office Add-in Pilot`

3. **Pilot Testing Period**
   - Recommended: 1-2 weeks
   - Collect feedback via Teams channel or survey
   - Monitor Application Insights for errors

4. **Expand to Organization**
   - After successful pilot, edit deployment
   - Change scope to **Entire Organization**

---

### 4.3 SharePoint App Catalog Deployment (Alternative)

**Best for**: Organizations preferring SharePoint-based distribution.

#### Step 1: Access SharePoint App Catalog

```
1. Go to SharePoint Admin Center: https://{tenant}-admin.sharepoint.com
2. Navigate to: More features > Apps > App Catalog
3. If no catalog exists, create one (requires global admin)
```

#### Step 2: Upload Add-in Manifests

```
1. Navigate to App Catalog site collection
2. Go to: Apps for Office
3. Click: New > Upload
4. Upload: manifest.prod.xml (Word) or manifest.prod.json (Outlook)
5. Repeat for each add-in
```

#### Step 3: Configure Default Deployment

```
1. Select uploaded add-in
2. Click: Deploy
3. Choose scope:
   - Everyone: All users in organization
   - Specific Users/Groups: Selected users only
```

---

## 5. Post-Deployment Verification

### 5.1 Admin Center Verification

| Check | Expected Result | How to Verify |
|-------|-----------------|---------------|
| Outlook add-in listed | Status: Deployed | M365 Admin > Integrated apps |
| Word add-in listed | Status: Deployed | M365 Admin > Integrated apps |
| User assignment | Correct scope | Edit add-in > View assigned users |
| Permissions | Granted | Edit add-in > Permissions tab |

### 5.2 Outlook Add-in Verification

**Test in New Outlook (Desktop):**

```
1. Open New Outlook desktop client
2. Open any email (read mode)
3. Look for "Spaarke" group in ribbon
4. Verify buttons:
   - "Save to Spaarke" visible
5. Click "Save to Spaarke"
6. Verify task pane opens
7. Verify authentication works
```

**Test in Outlook Web:**

```
1. Go to: https://outlook.office.com
2. Open any email (read mode)
3. Click "..." menu or look for Spaarke in ribbon
4. Verify "Save to Spaarke" action available
5. Click to open task pane
6. Verify authentication works
```

**Test Compose Mode:**

```
1. Create new email (compose mode)
2. Look for Spaarke group in ribbon
3. Verify buttons:
   - "Share from Spaarke"
   - "Grant Access"
4. Click each to verify task pane opens
```

### 5.3 Word Add-in Verification

**Test in Word Desktop (Windows):**

```
1. Open Word Desktop
2. Create or open a document
3. Go to Home tab in ribbon
4. Look for "Spaarke" group with buttons:
   - Save to Spaarke
   - Save Version
   - Share
   - Grant Access
5. Click each button to verify task pane opens
6. Test save flow with a test document
```

**Test in Word Desktop (Mac):**

```
1. Open Word Desktop on Mac
2. Same steps as Windows
3. Note: Ribbon layout may differ slightly
```

**Test in Word Web:**

```
1. Go to: https://www.office.com/launch/word
2. Create or open a document
3. Look for Spaarke add-in in ribbon
4. Verify all buttons work
5. Test save workflow
```

### 5.4 End-to-End Flow Verification

| Flow | Steps | Expected Result |
|------|-------|-----------------|
| **Outlook Save** | Save email with attachment to Matter | Job completes, document appears in Spaarke |
| **Word Save** | Save Word document to Project | Job completes, document appears in Spaarke |
| **Share Link** | Insert document link in compose | Link inserted in email body |
| **Quick Create** | Create new Matter from add-in | Matter created, selectable as target |
| **Authentication** | Fresh login scenario | NAA or Dialog fallback works |

### 5.5 Verification Checklist

**Outlook Add-in:**
- [ ] Add-in visible in New Outlook desktop
- [ ] Add-in visible in Outlook Web
- [ ] Read mode: "Save to Spaarke" button works
- [ ] Compose mode: "Share from Spaarke" button works
- [ ] Compose mode: "Grant Access" button works
- [ ] Task pane opens correctly
- [ ] Authentication succeeds (NAA or Dialog fallback)
- [ ] Entity search returns results
- [ ] Save flow creates job and completes
- [ ] Job status updates via SSE/polling

**Word Add-in:**
- [ ] Add-in visible in Word Desktop (Windows)
- [ ] Add-in visible in Word Desktop (Mac)
- [ ] Add-in visible in Word Web
- [ ] "Save to Spaarke" button works
- [ ] "Save Version" button works
- [ ] "Share" button works
- [ ] "Grant Access" button works
- [ ] Task pane opens correctly
- [ ] Authentication succeeds
- [ ] Save flow creates job and completes

---

## 6. Monitoring and Alerting

### 6.1 Application Insights Queries

**Track Add-in Initialization:**

```kusto
customEvents
| where name == "office.addin.initialized"
| project timestamp, customDimensions["host"], customDimensions["hostVersion"], customDimensions["platform"]
| order by timestamp desc
| take 100
```

**Track Save Operations:**

```kusto
customEvents
| where name startswith "office.save"
| project timestamp, name, customDimensions["sourceType"], customDimensions["status"]
| order by timestamp desc
| take 100
```

**Track Authentication:**

```kusto
customEvents
| where name startswith "office.auth"
| project timestamp, name, customDimensions["method"], customDimensions["success"]
| summarize count() by name, tostring(customDimensions["success"])
```

**Track Errors:**

```kusto
exceptions
| where customDimensions["source"] == "office-addin"
| project timestamp, problemId, outerMessage, severityLevel
| order by timestamp desc
| take 50
```

### 6.2 Recommended Alerts

| Alert Name | Condition | Severity | Action |
|------------|-----------|----------|--------|
| Add-in Auth Failures | Auth failures > 10 in 5 min | Warning | Email team |
| Save Flow Failures | Save failures > 5 in 5 min | Critical | Page on-call |
| Add-in Load Failures | Load failures > 20 in 15 min | Warning | Email team |
| SSE Connection Failures | SSE failures > 10 in 5 min | Warning | Email team |

---

## 7. Rollback Procedures

### 7.1 Immediate Rollback (Remove Add-in)

**Use when**: Add-in is causing critical issues for users.

```
1. Go to: https://admin.microsoft.com
2. Navigate to: Settings > Integrated apps
3. Find: Spaarke (Outlook or Word)
4. Click: ... > Remove
5. Confirm removal
6. Add-in will be removed from all users within 12-24 hours
```

**Faster Removal** (for urgent issues):

```
1. In M365 Admin Center, edit add-in deployment
2. Change scope to "Just Me" (admin only)
3. This immediately stops deployment to other users
4. Then remove add-in completely
```

### 7.2 Version Rollback (Deploy Previous Version)

**Use when**: New version has issues, previous version worked.

```powershell
# 1. Checkout previous release tag
git checkout v1.0.0-prev

# 2. Rebuild and redeploy static assets
npm run build:prod
az storage blob upload-batch --destination '$web' --source ./dist --account-name spaarkeaddinsprod --overwrite

# 3. If manifest changed, re-upload to M365 Admin Center
# (Remove current > Upload previous manifest)
```

### 7.3 Partial Rollback (Reduce Deployment Scope)

**Use when**: Issues only affecting some users/clients.

```
1. In M365 Admin Center, edit add-in deployment
2. Change scope from "Entire Organization" to "Specific Groups"
3. Select only unaffected user groups
4. This limits exposure while investigating
```

### 7.4 Rollback Decision Tree

```
Is add-in causing Outlook/Word crashes?
├── YES → Immediate Rollback (7.1)
│         Remove add-in immediately
│
└── NO → Is add-in functionality broken?
         ├── YES → Is previous version available?
         │         ├── YES → Version Rollback (7.2)
         │         └── NO → Immediate Rollback (7.1)
         │
         └── NO → Are only some users affected?
                  ├── YES → Partial Rollback (7.3)
                  │         Reduce deployment scope
                  │
                  └── NO → Monitor and investigate
                           Check logs, gather feedback
```

---

## 8. Troubleshooting

### 8.1 Common Issues

| Issue | Possible Cause | Solution |
|-------|----------------|----------|
| Add-in not visible | Not yet propagated | Wait 12-24 hours after deployment |
| Add-in not visible | User not in deployment scope | Check user group membership |
| Task pane blank | HTTPS certificate issue | Verify static hosting SSL certificate |
| Task pane blank | CORS error | Check API CORS configuration |
| "We couldn't load the add-in" | URL mismatch | Verify manifest URLs match hosting |
| Auth popup blocked | Browser popup blocker | Allow popups for add-in domain |
| Auth fails | App registration issue | Verify Azure AD configuration |
| "Add-in not found" | Manifest removed | Re-upload manifest to Admin Center |
| Icons not showing | Asset URLs incorrect | Verify all icon URLs resolve |
| Works in Web, not Desktop | Cached manifest | Clear Office cache (see below) |

### 8.2 Clearing Office Add-in Cache

**Windows:**

```powershell
# Close all Office applications first
Remove-Item "$env:LOCALAPPDATA\Microsoft\Office\16.0\Wef" -Recurse -Force -ErrorAction SilentlyContinue
# Restart Office applications
```

**Mac:**

```bash
# Close all Office applications first
rm -rf ~/Library/Containers/com.microsoft.Outlook/Data/Library/Caches/Microsoft/Office/16.0/Wef
rm -rf ~/Library/Containers/com.microsoft.Word/Data/Library/Caches/Microsoft/Office/16.0/Wef
```

### 8.3 Diagnostic Commands

```powershell
# Verify static assets are accessible
curl -I https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html

# Verify API health
curl https://spe-api-prod-*.azurewebsites.net/healthz

# Check for console errors in browser
# Open F12 Developer Tools in task pane
# Look for errors in Console tab
```

### 8.4 Support Escalation Path

| Level | Contact | Response Time | Issues |
|-------|---------|---------------|--------|
| L1 | IT Help Desk | 4 hours | User-facing issues, cache clearing |
| L2 | Spaarke Support | 2 hours | Configuration issues, deployment |
| L3 | Development Team | Same day | Code issues, critical bugs |

---

## 9. Deployment History Log

| Date | Version | Add-in | Environment | Method | Status | Notes |
|------|---------|--------|-------------|--------|--------|-------|
| 2026-01-20 | 1.0.0 | Both | Documentation | N/A | Created | Production deployment guide created |
| TBD | 1.0.0 | Outlook | Production | M365 Admin | Pending | Initial production deployment |
| TBD | 1.0.0 | Word | Production | M365 Admin | Pending | Initial production deployment |

---

## 10. Azure AD App Registration Reference

### Add-in App Registration

| Property | Value |
|----------|-------|
| **App Name** | Spaarke Office Add-in |
| **Client ID** | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| **App Type** | Single-page application (SPA) |
| **Redirect URIs** | `brk-multihub://localhost` (NAA), `https://spe-office-addins-prod.azurestaticapps.net/taskpane.html` (fallback) |

### API Permissions (Delegated)

| Permission | Type | Status |
|------------|------|--------|
| `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` | Delegated | Admin consent required |
| `User.Read` (Microsoft Graph) | Delegated | User consent allowed |

### BFF API App Registration Reference

| Property | Value |
|----------|-------|
| **App Name** | Spaarke BFF API |
| **Client ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **App ID URI** | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Scopes Exposed** | `user_impersonation` |

---

## 11. Acceptance Criteria

- [x] Production M365 admin center deployment documented
- [x] Production manifest URL configuration documented
- [x] Tenant-wide deployment procedures documented
- [x] Deployment verification checklist created
- [x] Rollback procedures documented (immediate, version, partial)
- [x] Troubleshooting guide created
- [x] Pilot deployment recommendations included
- [x] Monitoring and alerting requirements documented

---

## Related Documentation

- [Task 080: Production BFF API Deployment](#task-080-production-bff-api-deployment) - API must be deployed first
- [spec.md](../spec.md) - Full project specification
- [Task 057: Outlook Add-in Deployment](#task-057-outlook-add-in-deployment) - Development deployment reference
- [Task 058: Word Add-in Deployment](#task-058-word-add-in-deployment) - Development deployment reference
- [Microsoft Learn: Centralized Deployment](https://learn.microsoft.com/en-us/microsoft-365/admin/manage/centralized-deployment-of-add-ins)
- [Microsoft Learn: Publish Office Add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/publish/publish)

---

*This document should be updated after each production deployment.*

---

# Task 080: Production BFF API Deployment

> **Status**: Ready for Deployment
> **Type**: Azure App Service Production Deployment
> **Task Reference**: Task 080 - Production deployment: BFF API
> **Date Created**: 2026-01-20

---

## Executive Summary

This section provides comprehensive guidance for deploying the SDAP Office Integration BFF API to production Azure environments. It covers configuration requirements, deployment procedures, verification steps, and rollback procedures.

---

## 1. Environment Overview

### Resource Architecture

```
Production Environment
├── Azure App Service (BFF API)
│   └── .NET 8 Minimal API
│       ├── /office/* endpoints (save, search, share, jobs)
│       └── Background workers (upload, profile, indexing)
├── Azure Key Vault (secrets)
├── Azure Redis Cache (caching, rate limiting)
├── Azure Service Bus (job queuing)
├── Azure Application Insights (monitoring)
└── Azure OpenAI (AI features)
```

### Environment Comparison

| Property | Dev | Staging | Production |
|----------|-----|---------|------------|
| **Resource Group** | `spe-infrastructure-westus2` | `spe-infrastructure-westus2` | `rg-spaarke-prod-westus2` |
| **App Service** | `spe-api-dev-67e2xz` | `spe-api-staging-*` | `spe-api-prod-*` |
| **App Service Plan** | B1 (Basic) | S1 (Standard) | P1v3 (Premium) |
| **Key Vault** | `spaarke-spekvcert` | `spaarke-kv-staging` | `spaarke-kv-prod` |
| **Redis** | Basic C0 | Standard C1 | Premium P1 |
| **Region** | West US 2 | West US 2 | West US 2 (Primary) |
| **Auto-scale** | No | No | Yes (2-10 instances) |
| **Deployment Slots** | No | Yes | Yes |

---

## 2. Prerequisites

### Required Tools

```powershell
# Verify installations
az --version          # Azure CLI 2.50+
dotnet --version      # .NET SDK 8.0+
gh --version          # GitHub CLI 2.0+
```

### Required Access

| Resource | Role | Purpose |
|----------|------|---------|
| Azure Subscription | Contributor | Deploy resources |
| Azure AD | Application Administrator | Manage app registrations |
| Azure Key Vault | Key Vault Secrets User | Access secrets |
| GitHub Repository | Write | Trigger deployments |

### Authentication Setup

```powershell
# Azure CLI login
az login
az account set --subscription "Spaarke Production Subscription"
az account show --query "{Name:name, Id:id}" -o table

# GitHub CLI login (for triggering workflows)
gh auth login
gh auth status
```

---

## 3. Production Configuration

### 3.1 App Service Configuration

**Required App Settings:**

| Setting | Value | Source |
|---------|-------|--------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Direct value |
| `TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Direct value |
| `API_APP_ID` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | Direct value |
| `API_CLIENT_SECRET` | `@Microsoft.KeyVault(SecretUri=...)` | Key Vault reference |
| `UAMI_CLIENT_ID` | `{managed-identity-client-id}` | Direct value |

**AI Configuration:**

| Setting | Value | Source |
|---------|-------|--------|
| `Ai__Enabled` | `true` | Direct value |
| `Ai__OpenAiEndpoint` | `https://spaarke-openai-prod.openai.azure.com/` | Direct value |
| `Ai__OpenAiKey` | `@Microsoft.KeyVault(SecretUri=...)` | Key Vault reference |
| `Ai__SummarizeModel` | `gpt-4o-mini` | Direct value |

**Office Integration Specific:**

| Setting | Value | Description |
|---------|-------|-------------|
| `Office__RateLimiting__SavePerMinute` | `10` | Save endpoint rate limit |
| `Office__RateLimiting__QuickCreatePerMinute` | `5` | Quick create rate limit |
| `Office__RateLimiting__SearchPerMinute` | `30` | Search rate limit |
| `Office__RateLimiting__JobsPerMinute` | `60` | Job status rate limit |
| `Office__RateLimiting__SharePerMinute` | `20` | Share endpoint rate limit |
| `Office__AttachmentLimits__MaxSingleFileMb` | `25` | Max attachment size |
| `Office__AttachmentLimits__MaxTotalMb` | `100` | Max total attachments |

### 3.2 Key Vault Secrets Configuration

**Required Secrets:**

| Secret Name | Purpose | Rotation Schedule |
|-------------|---------|-------------------|
| `API-CLIENT-SECRET` | BFF API app registration secret | Every 6 months |
| `openai-api-key` | Azure OpenAI API key | As needed |
| `redis-connection-string` | Redis cache connection | As needed |
| `servicebus-connection-string` | Service Bus connection | As needed |
| `docintel-api-key` | Document Intelligence key | As needed |

**Key Vault Reference Format:**

```
@Microsoft.KeyVault(SecretUri=https://{vault-name}.vault.azure.net/secrets/{secret-name}/)
```

**Example App Setting with Key Vault Reference:**

```powershell
az webapp config appsettings set `
  --name spe-api-prod-{suffix} `
  --resource-group rg-spaarke-prod-westus2 `
  --settings "API_CLIENT_SECRET=@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/API-CLIENT-SECRET/)"
```

### 3.3 Redis Cache Configuration

**Production Redis Settings:**

| Property | Value |
|----------|-------|
| **SKU** | Premium P1 |
| **Family** | P |
| **Capacity** | 1 (6 GB) |
| **TLS** | 1.2 (required) |
| **Cluster** | Disabled (single node) |
| **Geo-replication** | Consider for DR |

**Connection String Format:**

```
{cache-name}.redis.cache.windows.net:6380,password={access-key},ssl=True,abortConnect=False
```

**App Setting:**

```powershell
az webapp config appsettings set `
  --name spe-api-prod-{suffix} `
  --resource-group rg-spaarke-prod-westus2 `
  --settings "Redis__ConnectionString=@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/redis-connection-string/)"
```

### 3.4 Service Bus Configuration

**Production Service Bus Settings:**

| Property | Value |
|----------|-------|
| **SKU** | Standard |
| **Messaging Units** | 1 |
| **TLS** | 1.2 |

**Required Queues for Office Integration:**

| Queue Name | Purpose | Max Delivery Count |
|------------|---------|-------------------|
| `office-upload-finalization` | Email/document upload processing | 3 |
| `office-profile` | Profile summary generation | 3 |
| `office-indexing` | RAG indexing | 3 |

**Connection String:**

Store in Key Vault as `servicebus-connection-string`:

```
Endpoint=sb://{namespace}.servicebus.windows.net/;SharedAccessKeyName={policy-name};SharedAccessKey={key}
```

### 3.5 Application Insights Configuration

**Required Settings:**

| Setting | Value |
|---------|-------|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | From Azure Portal |
| `ApplicationInsightsAgent_EXTENSION_VERSION` | `~3` |

---

## 4. Deployment Methods

### 4.1 Automated (GitHub Actions) - RECOMMENDED

**Production deployments should use automated CI/CD pipelines.**

**Workflow**: `.github/workflows/deploy-to-azure.yml`

**Trigger Production Deployment:**

```powershell
# Option 1: GitHub CLI
gh workflow run deploy-to-azure.yml --field environment=prod

# Option 2: GitHub Web UI
# Navigate to Actions > Deploy SPE Infrastructure to Azure > Run workflow > Select 'prod'
```

**Monitor Deployment:**

```powershell
# Watch deployment progress
gh run watch

# View recent deployment runs
gh run list --workflow=deploy-to-azure.yml --limit 5

# View specific run details
gh run view {run-id}
```

### 4.2 Manual (Azure CLI) - EMERGENCY ONLY

**Use manual deployment only for emergency hotfixes when CI/CD is unavailable.**

```powershell
# 1. Build release
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ./publish

# 2. Package
Compress-Archive -Path './publish/*' -DestinationPath './publish.zip' -Force

# 3. Deploy to staging slot first
az webapp deploy `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-{suffix} `
  --slot staging `
  --src-path ./publish.zip `
  --type zip

# 4. Verify staging
curl https://spe-api-prod-{suffix}-staging.azurewebsites.net/healthz

# 5. Swap to production
az webapp deployment slot swap `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-{suffix} `
  --slot staging `
  --target-slot production
```

---

## 5. Pre-Deployment Checklist

### Code Readiness

- [ ] All unit tests pass (`dotnet test`)
- [ ] All integration tests pass
- [ ] Security review completed (Task 078)
- [ ] Code review approved
- [ ] No critical or high severity vulnerabilities
- [ ] Version numbers updated (if applicable)

### Infrastructure Readiness

- [ ] Production resource group exists
- [ ] App Service Plan created with appropriate tier (P1v3+)
- [ ] App Service created with deployment slots enabled
- [ ] Key Vault created and accessible
- [ ] All secrets stored in Key Vault
- [ ] Redis Cache provisioned and running
- [ ] Service Bus namespace and queues created
- [ ] Application Insights configured
- [ ] Managed identity enabled on App Service
- [ ] Key Vault access policy grants App Service identity access

### Configuration Readiness

- [ ] All required app settings configured
- [ ] Key Vault references verified
- [ ] Connection strings validated
- [ ] CORS settings configured for Office add-ins
- [ ] Rate limiting configured
- [ ] Logging level set to Information (not Debug)

### Communication

- [ ] Deployment window communicated to stakeholders
- [ ] Support team notified
- [ ] Rollback plan reviewed

---

## 6. Deployment Procedure

### Step 1: Pre-Deployment Verification

```powershell
# Verify current production status
curl https://spe-api-prod-{suffix}.azurewebsites.net/healthz
curl https://spe-api-prod-{suffix}.azurewebsites.net/ping

# Note current version for rollback
az webapp show --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2 --query "siteConfig.appSettings" -o table
```

### Step 2: Trigger Deployment

```powershell
# Trigger via GitHub Actions (recommended)
gh workflow run deploy-to-azure.yml --field environment=prod

# Monitor progress
gh run watch
```

### Step 3: Staging Verification

```powershell
# Verify staging slot health
curl https://spe-api-prod-{suffix}-staging.azurewebsites.net/healthz

# Test Office endpoints on staging
curl -X GET "https://spe-api-prod-{suffix}-staging.azurewebsites.net/office/recent" `
  -H "Authorization: Bearer {test-token}"
```

### Step 4: Production Swap

```powershell
# Swap staging to production
az webapp deployment slot swap `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-{suffix} `
  --slot staging `
  --target-slot production

# Verify production
curl https://spe-api-prod-{suffix}.azurewebsites.net/healthz
```

### Step 5: Post-Deployment Validation

See [Section 7: Post-Deployment Verification](#7-post-deployment-verification)

---

## 7. Post-Deployment Verification

### Health Check Verification

```powershell
# Basic health check
$healthResponse = Invoke-RestMethod -Uri "https://spe-api-prod-{suffix}.azurewebsites.net/healthz"
Write-Host "Health: $healthResponse"
# Expected: "Healthy"

# Ping check
$pingResponse = Invoke-RestMethod -Uri "https://spe-api-prod-{suffix}.azurewebsites.net/ping"
Write-Host "Service: $($pingResponse.service)"
# Expected: "Spe.Bff.Api"
```

### Office Endpoints Verification

```powershell
# Test entity search (requires valid auth token)
$token = "Bearer {your-test-token}"
$headers = @{ "Authorization" = $token }

# GET /office/recent
Invoke-RestMethod -Uri "https://spe-api-prod-{suffix}.azurewebsites.net/office/recent" `
  -Headers $headers -Method Get

# GET /office/search/entities
Invoke-RestMethod -Uri "https://spe-api-prod-{suffix}.azurewebsites.net/office/search/entities?q=test&type=Matter,Account&limit=5" `
  -Headers $headers -Method Get
```

### Worker Verification

```powershell
# Check App Service logs for worker startup
az webapp log tail --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2

# Look for:
# - "BackgroundService starting..."
# - "UploadFinalizationWorker started"
# - "ProfileSummaryWorker started"
# - "IndexingWorker started"
```

### Application Insights Verification

1. Navigate to Azure Portal > Application Insights > `spe-insights-prod-*`
2. Check **Live Metrics** for incoming requests
3. Verify no exceptions in **Failures** blade
4. Check **Performance** for response times

**Kusto Query for Recent Requests:**

```kusto
requests
| where timestamp > ago(15m)
| where name contains "/office/"
| summarize count() by name, resultCode
| order by count_ desc
```

---

## 8. Monitoring and Alerting

### Required Alerts

| Alert Name | Condition | Severity | Action |
|------------|-----------|----------|--------|
| API Availability | Availability < 99.9% | Critical | Page on-call |
| Response Time | P95 > 2000ms | Warning | Email team |
| 5xx Errors | Count > 10 in 5 min | Critical | Page on-call |
| 4xx Errors | Count > 100 in 5 min | Warning | Email team |
| CPU Usage | Avg > 80% for 5 min | Warning | Email team |
| Memory Usage | Avg > 85% for 5 min | Warning | Email team |
| Queue Depth | > 1000 messages | Warning | Email team |

### Alert Configuration

```powershell
# Create availability alert
az monitor metrics alert create `
  --name "BFF-API-Availability-Critical" `
  --resource-group rg-spaarke-prod-westus2 `
  --scopes "/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/sites/spe-api-prod-{suffix}" `
  --condition "avg availabilityResults/availabilityPercentage < 99.9" `
  --severity 0 `
  --action "/subscriptions/{sub}/resourceGroups/{rg}/providers/microsoft.insights/actionGroups/CriticalAlerts"
```

---

## 9. Rollback Procedures

### 9.1 Immediate Rollback (Slot Swap)

**Use when production is unhealthy immediately after deployment.**

```powershell
# Swap back to previous version
az webapp deployment slot swap `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-{suffix} `
  --slot staging `
  --target-slot production

# Verify rollback
curl https://spe-api-prod-{suffix}.azurewebsites.net/healthz
```

### 9.2 Planned Rollback (Redeploy Previous Version)

**Use when you need to deploy a specific previous version.**

```powershell
# 1. Find previous successful deployment
gh run list --workflow=deploy-to-azure.yml --status=success --limit 10

# 2. Download artifacts from previous run
gh run download {previous-run-id} --name spe-api-build

# 3. Deploy to staging
az webapp deploy `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-{suffix} `
  --slot staging `
  --src-path ./spe-api-build.zip `
  --type zip

# 4. Verify and swap
curl https://spe-api-prod-{suffix}-staging.azurewebsites.net/healthz

az webapp deployment slot swap `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-{suffix} `
  --slot staging `
  --target-slot production
```

### 9.3 Emergency Rollback (Code Revert)

**Use when you need to revert code changes in the repository.**

```powershell
# 1. Identify the commit to revert to
git log --oneline -10

# 2. Create revert commit
git revert HEAD~n..HEAD  # Revert last n commits

# 3. Push and trigger deployment
git push origin master
gh workflow run deploy-to-azure.yml --field environment=prod
```

### Rollback Decision Tree

```
Is the API returning 5xx errors?
├── YES → Immediate Rollback (Slot Swap)
│         Execute within 5 minutes
│
└── NO → Is functionality broken but API healthy?
         ├── YES → Planned Rollback (Redeploy Previous)
         │         Schedule for next maintenance window
         │
         └── NO → Is this a configuration issue?
                  ├── YES → Fix configuration, no code rollback needed
                  │
                  └── NO → Monitor and investigate
```

---

## 10. Troubleshooting

### Common Issues

| Symptom | Possible Cause | Resolution |
|---------|----------------|------------|
| 401 Unauthorized | Invalid token or wrong audience | Verify app registration and token |
| 403 Forbidden | Missing permissions | Check Azure AD permissions |
| 502 Bad Gateway | App crashed on startup | Check logs for startup errors |
| 503 Service Unavailable | App not started | Restart app service |
| 504 Gateway Timeout | Long-running request | Check for blocking operations |
| Key Vault access denied | Missing access policy | Grant identity Key Vault access |
| Redis connection failed | Wrong connection string | Verify connection string format |

### Diagnostic Commands

```powershell
# Stream live logs
az webapp log tail --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2

# View deployment logs
az webapp log deployment show --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2

# Check app settings
az webapp config appsettings list --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2 -o table

# Check connection strings
az webapp config connection-string list --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2 -o table

# Restart app service
az webapp restart --name spe-api-prod-{suffix} --resource-group rg-spaarke-prod-westus2
```

### Application Insights Queries

**Find errors:**

```kusto
exceptions
| where timestamp > ago(1h)
| project timestamp, problemId, outerMessage, severityLevel
| order by timestamp desc
```

**Check Office endpoint performance:**

```kusto
requests
| where timestamp > ago(1h)
| where name contains "/office/"
| summarize
    avg(duration),
    percentile(duration, 95),
    count()
  by name, resultCode
| order by count_ desc
```

**Track specific correlation ID:**

```kusto
union traces, requests, exceptions
| where timestamp > ago(1h)
| where customDimensions["CorrelationId"] == "{correlation-id}"
| project timestamp, itemType, name, message
| order by timestamp asc
```

---

## 11. Resource Naming Conventions

| Resource Type | Pattern | Production Example |
|---------------|---------|-------------------|
| Resource Group | `rg-spaarke-{env}-{region}` | `rg-spaarke-prod-westus2` |
| App Service | `spe-api-{env}-{suffix}` | `spe-api-prod-abc123` |
| Key Vault | `spaarke-kv-{env}` | `spaarke-kv-prod` |
| Redis Cache | `spaarke-redis-{env}` | `spaarke-redis-prod` |
| Service Bus | `spaarke-sb-{env}` | `spaarke-sb-prod` |
| App Insights | `spe-insights-{env}-{suffix}` | `spe-insights-prod-abc123` |

---

## 12. Azure AD App Registrations

| App | Client ID | Purpose |
|-----|-----------|---------|
| BFF API | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | API validation, OBO flow |
| Office Add-in | `{to-be-created-task-081}` | NAA authentication |
| Dataverse App | `5175798e-f23e-41c3-b09b-7a90b9218189` | PCF control auth |

---

## 13. Deployment History Log

| Date | Version | Environment | Deployer | Status | Notes |
|------|---------|-------------|----------|--------|-------|
| 2026-01-20 | 1.0.0 | Documentation | System | Completed | Production deployment documentation created |

---

## Acceptance Criteria

- [x] Production configuration documented (App Settings, Key Vault, Redis, Service Bus)
- [x] Deployment methods documented (automated GitHub Actions, manual fallback)
- [x] Pre-deployment checklist created
- [x] Post-deployment verification procedures documented
- [x] Monitoring and alerting requirements documented
- [x] Rollback procedures documented (immediate, planned, emergency)
- [x] Troubleshooting guide created

---

## Related Documentation

- [azure-deploy skill](../../../../.claude/skills/azure-deploy/SKILL.md) - Azure deployment procedures
- [auth-azure-resources.md](../../../../docs/architecture/auth-azure-resources.md) - Azure resource details
- [spec.md](../spec.md) - Full project specification
- [security-review.md](security-review.md) - Security review (Task 078)

---

*This document should be updated after each production deployment.*

---

# Task 015: Dataverse Solution Deployment

> **Status**: Completed
> **Type**: Dataverse Solution Deployment
> **Location**: Power Platform CLI + Power Apps Maker Portal

## Overview

Deploy the Office integration Dataverse solution containing the new tables (EmailArtifact, AttachmentArtifact, ProcessingJob), their relationships, and security role updates.

## Prerequisites

Before deployment, ensure these tasks are complete:
- [x] Task 010: EmailArtifact table created
- [x] Task 011: AttachmentArtifact table created
- [x] Task 012: ProcessingJob table created
- [x] Task 013: Relationships and indexes configured
- [x] Task 014: Security roles configured

## Solution Information

| Property | Value |
|----------|-------|
| **Solution Name** | SpaarkeOfficeIntegration |
| **Publisher** | Spaarke (sprk_) |
| **Version** | 1.0.0.0 |
| **Type** | Unmanaged (development) |

## Environment Information

| Environment | URL | Purpose |
|-------------|-----|---------|
| **Dev** | `https://spaarkedev1.crm.dynamics.com` | Development/testing |

## Deployment Log

| Date | Version | Environment | Status | Notes |
|------|---------|-------------|--------|-------|
| 2026-01-20 | 1.0.0.0 | Dev | Completed | Initial deployment with EmailArtifact, AttachmentArtifact, ProcessingJob tables |

---

# Task 057: Outlook Add-in Deployment

> **Status**: See tasks/057-deploy-outlook-addin.poml
> **Type**: Office Add-in Deployment (Unified Manifest)
> **Destination**: Microsoft 365 Admin Center

(Documented in separate task file)

---

# Task 058: Word Add-in Deployment

> **Status**: Ready for Manual Execution
> **Type**: Office Add-in Deployment (XML Manifest)
> **Destination**: Sideloading (Dev) or SharePoint App Catalog (Org-wide)

## Overview

Deploy the Word add-in using XML manifest for development testing and organization-wide distribution.

**Note**: Word add-in uses XML manifest (not unified manifest) because unified manifest is still in preview for Word as of January 2026.

## Prerequisites

Before deployment, ensure these tasks are complete:
- [x] Task 003: Office add-in project structure created
- [x] Task 005: Word XML manifest created
- [x] Task 040-055: Add-in functionality implemented
- [x] Task 056: Add-in unit tests created

## Add-in Information

| Property | Value |
|----------|-------|
| **Add-in ID** | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| **Name** | Spaarke |
| **Version** | 1.0.0 |
| **Manifest Type** | XML (add-in-only) |
| **Host Application** | Word Desktop + Word Web |
| **Required API** | WordApi 1.3+ |

## Files

| File | Purpose |
|------|---------|
| `src/client/office-addins/word/manifest.xml` | XML manifest (development URLs) |
| `src/client/office-addins/dist/` | Production build output |

---

## Step 1: Build Production Version

### 1.1 Install Dependencies (if needed)

```powershell
cd src/client/office-addins
npm install
```

### 1.2 Build for Production

```powershell
cd src/client/office-addins
npm run build
```

**Expected output:**
```
dist/
├── word/
│   ├── taskpane.html
│   ├── taskpane.bundle.js
│   ├── commands.html
│   └── commands.bundle.js
├── assets/
│   ├── icon-16.png
│   ├── icon-32.png
│   ├── icon-80.png
│   ├── save-16.png, save-32.png, save-80.png
│   ├── save-version-16.png, save-version-32.png, save-version-80.png
│   ├── share-16.png, share-32.png, share-80.png
│   └── grant-16.png, grant-32.png, grant-80.png
└── vendors.bundle.js (shared dependencies)
```

### 1.3 Verify Build

```powershell
# Check that all required files exist
Get-ChildItem -Path "dist/word" -Recurse
Get-ChildItem -Path "dist/assets" -Recurse
```

---

## Step 2: Deploy Static Files to Hosting

The add-in static files need to be hosted on HTTPS. Options:

### Option A: Azure Static Web Apps (Recommended for Production)

```bash
# Deploy to Azure Static Web Apps
az staticwebapp create \
  --name spaarke-office-addins-dev \
  --resource-group spe-infrastructure-westus2 \
  --source ./dist \
  --location westus2
```

### Option B: Azure Blob Storage with CDN

```bash
# Upload to Azure Blob Storage
az storage blob upload-batch \
  --destination '$web' \
  --source ./dist \
  --account-name spaarkeaddinsdev \
  --overwrite
```

### Option C: Development - Local Dev Server

For development testing, use the webpack dev server:
```powershell
npm start:word
# Serves at https://localhost:3000
```

**Production URL Pattern**: `https://spaarke-addins-dev.azurestaticapps.net`

---

## Step 3: Update Manifest with Production URLs

### 3.1 Create Production Manifest

Create a copy of the manifest with production URLs:

```powershell
# Copy manifest for production
Copy-Item "word/manifest.xml" "dist/word/manifest.xml"
```

### 3.2 Update URLs in Production Manifest

Replace all occurrences of `https://localhost:3000` with your production URL.

**URLs to update in `dist/word/manifest.xml`:**

| Element | Development | Production |
|---------|-------------|------------|
| IconUrl | `https://localhost:3000/assets/icon-32.png` | `https://{hosting-url}/assets/icon-32.png` |
| HighResolutionIconUrl | `https://localhost:3000/assets/icon-80.png` | `https://{hosting-url}/assets/icon-80.png` |
| AppDomain | `https://localhost:3000` | `https://{hosting-url}` |
| SourceLocation | `https://localhost:3000/word/taskpane.html` | `https://{hosting-url}/word/taskpane.html` |
| All bt:Image elements | `https://localhost:3000/assets/*` | `https://{hosting-url}/assets/*` |
| Taskpane.Url | `https://localhost:3000/word/taskpane.html` | `https://{hosting-url}/word/taskpane.html` |
| Commands.Url | `https://localhost:3000/word/commands.html` | `https://{hosting-url}/word/commands.html` |

**PowerShell script to update URLs:**

```powershell
$hostingUrl = "https://spaarke-addins-dev.azurestaticapps.net"
$manifestPath = "dist/word/manifest.xml"

# Read and replace
$content = Get-Content $manifestPath -Raw
$content = $content -replace "https://localhost:3000", $hostingUrl
Set-Content $manifestPath $content

Write-Host "Updated manifest URLs to: $hostingUrl"
```

### 3.3 Validate Manifest

```powershell
# Use Office Add-in manifest validator
npx office-addin-manifest validate dist/word/manifest.xml
```

---

## Step 4: Configure Deployment Method

Choose ONE of the following deployment methods:

### Option A: Sideloading (Development/Testing)

Best for individual testing during development.

#### Word Desktop (Windows)

1. Open Word Desktop
2. Go to **Insert** > **Add-ins** > **My Add-ins**
3. Click **Upload My Add-in**
4. Browse to `dist/word/manifest.xml`
5. Click **Upload**

#### Word Desktop (Mac)

1. Open Word Desktop
2. Go to **Insert** > **Add-ins** > **My Add-ins**
3. Click **Upload My Add-in** (folder icon)
4. Browse to `dist/word/manifest.xml`
5. Click **Upload**

#### Word Web

1. Open Word Online at https://www.office.com/launch/word
2. Open or create a document
3. Go to **Insert** > **Add-ins**
4. Click **Upload My Add-in**
5. Browse to `dist/word/manifest.xml`
6. Click **Upload**

### Option B: SharePoint App Catalog (Organization-wide)

Best for distributing to specific groups or the entire organization.

#### 4B.1: Create SharePoint App Catalog (if not exists)

1. Go to SharePoint Admin Center: `https://{tenant}-admin.sharepoint.com`
2. Navigate to **More features** > **Apps** > **App Catalog**
3. If no catalog exists, create one

#### 4B.2: Upload Manifest to App Catalog

1. Navigate to the App Catalog site collection
2. Go to **Apps for Office**
3. Click **New** > **Upload**
4. Select `dist/word/manifest.xml`
5. Click **OK**

#### 4B.3: Configure Default Deployment (Optional)

1. In the App Catalog, select the uploaded add-in
2. Click **Deploy**
3. Choose deployment scope:
   - **Everyone**: All users in the organization
   - **Specific Users/Groups**: Selected users or M365 groups

### Option C: Microsoft 365 Admin Center Centralized Deployment

Best for large organizations with IT-managed deployments.

#### 4C.1: Access Admin Center

1. Go to https://admin.microsoft.com
2. Navigate to **Settings** > **Integrated apps**

#### 4C.2: Upload Add-in

1. Click **Upload custom apps**
2. Select **Office Add-in**
3. Choose upload method:
   - **Upload manifest file (.xml)** for Word add-in
4. Browse to `dist/word/manifest.xml`
5. Click **Upload**

#### 4C.3: Configure Deployment

1. **Users**: Select who should have access
   - Entire organization
   - Specific users or groups
   - Just me (testing)
2. **Permissions**: Review requested permissions
3. Click **Deploy**

**Note**: Centralized deployment can take 12-24 hours to propagate to all clients.

---

## Step 5: Configure User Access

### For Sideloading
- Users must upload manifest individually
- Best for development and testing

### For SharePoint App Catalog
- Set deployment scope in catalog
- Users see add-in in **Insert** > **Add-ins** > **Admin Managed**

### For Centralized Deployment
- Assign users/groups in Microsoft 365 Admin Center
- Add-in appears automatically after propagation

---

## Step 6: Verification Testing

### 6.1: Test in Word Desktop (Windows)

1. Open Word Desktop on Windows
2. Create or open a document
3. Go to **Home** tab
4. Look for **Spaarke** group in ribbon with buttons:
   - Save to Spaarke
   - Save Version
   - Share
   - Grant Access
5. Click each button to verify task pane opens
6. Verify authentication works (NAA or Dialog fallback)
7. Test Save workflow with a test document

**Expected behavior:**
- Ribbon buttons visible in Home tab
- Task pane opens on button click
- Authentication prompts correctly
- Save flow initiates job and shows progress

### 6.2: Test in Word Desktop (Mac)

1. Open Word Desktop on Mac
2. Follow same steps as Windows
3. Verify all buttons function correctly

**Note**: Mac may have slightly different ribbon layout.

### 6.3: Test in Word Web

1. Open Word Online at https://www.office.com/launch/word
2. Create or open a document
3. Look for **Spaarke** add-in in ribbon
4. Verify all buttons work
5. Test Save workflow

**Known differences in Word Web:**
- Task pane may have different dimensions
- Some features may behave differently (check console for errors)

### 6.4: Verify All Ribbon Buttons

| Button | Expected Action |
|--------|-----------------|
| **Save to Spaarke** | Opens task pane with save flow |
| **Save Version** | Opens task pane with version save flow |
| **Share** | Opens task pane with share flow |
| **Grant Access** | Opens task pane with grant access flow |

---

## Step 7: Document Deployment

After successful deployment, update this section:

### Deployment Log

| Date | Version | Environment | Method | Status | Notes |
|------|---------|-------------|--------|--------|-------|
| TBD | 1.0.0 | Dev | Sideloading | Pending | Initial development deployment |

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Add-in not visible in ribbon | Manifest not loaded | Re-upload manifest via Insert > Add-ins |
| Task pane shows blank | HTTPS certificate issue | Ensure hosting uses valid HTTPS |
| "We couldn't load the add-in" | URL mismatch | Verify manifest URLs match hosting |
| Auth popup blocked | Browser popup blocker | Allow popups for the hosting domain |
| Add-in works in Web but not Desktop | Cached manifest | Clear Office cache: `%LOCALAPPDATA%\Microsoft\Office\16.0\Wef` |
| Buttons not showing correct icons | Asset URLs incorrect | Verify all bt:Image URLs resolve correctly |

### Clearing Office Add-in Cache (Windows)

```powershell
# Close all Office applications first
Remove-Item "$env:LOCALAPPDATA\Microsoft\Office\16.0\Wef" -Recurse -Force -ErrorAction SilentlyContinue

# Restart Word
```

### Clearing Office Add-in Cache (Mac)

```bash
# Close all Office applications first
rm -rf ~/Library/Containers/com.microsoft.Word/Data/Library/Caches/Microsoft/Office/16.0/Wef
```

---

## Rollback Procedure

### Sideloading
1. In Word, go to **Insert** > **Add-ins** > **My Add-ins**
2. Find Spaarke add-in
3. Click the "..." menu > **Remove**

### SharePoint App Catalog
1. Go to App Catalog site
2. Navigate to **Apps for Office**
3. Select the Spaarke add-in
4. Click **Delete**

### Centralized Deployment
1. Go to Microsoft 365 Admin Center
2. Navigate to **Settings** > **Integrated apps**
3. Find Spaarke Word add-in
4. Click **Remove**

---

## Acceptance Criteria

- [ ] Add-in builds without errors
- [ ] Manifest validates successfully
- [ ] Static files deployed to hosting
- [ ] Manifest URLs updated for production
- [ ] Add-in loads in Word Desktop (Windows)
- [ ] Add-in loads in Word Desktop (Mac)
- [ ] Add-in loads in Word Web
- [ ] All ribbon buttons appear
- [ ] Task pane opens when buttons clicked
- [ ] Authentication works (NAA with Dialog fallback)
- [ ] Save workflow completes successfully
- [ ] Deployment documented in log above

---

## Related Documentation

- [manifest-word.md](manifest-word.md) - Word manifest structure notes
- [001-azure-ad-app-registration.md](001-azure-ad-app-registration.md) - App registration details
- [spec.md](../spec.md) - Full project specification
- [Microsoft Learn: Sideload Office Add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/testing/sideload-office-add-ins-for-testing)
- [Microsoft Learn: Publish to SharePoint App Catalog](https://learn.microsoft.com/en-us/office/dev/add-ins/publish/publish-add-in-vs-code)

---

*After completing this task, Task 071 (E2E test: Word save flow) can begin.*

---

# Task 066: Deploy Workers to Azure

> **Status**: Ready for Manual Execution
> **Type**: Background Worker Deployment
> **Destination**: Azure App Service (spe-api-dev-67e2xz)
> **Date**: 2026-01-20

## Overview

Deploy the Office background workers (UploadFinalizationWorker, IndexingWorker) to the Azure development environment. Workers run as BackgroundService hosted services within the BFF API App Service (per ADR-001 - no Azure Functions).

**Key Point**: Workers are NOT separate deployments. They are part of the BFF API and deploy when the API is deployed.

## Prerequisites

Before deployment, ensure these tasks are complete:
- [x] Task 060: Worker project structure created
- [x] Task 061: Upload finalization worker implemented
- [x] Task 062: Profile summary worker implemented
- [x] Task 063: Indexing worker implemented
- [x] Task 064: Job status update service implemented
- [x] Task 065: Worker unit tests created

## Worker Information

| Worker | Queue Name | Purpose |
|--------|------------|---------|
| **UploadFinalizationWorker** | office-upload-finalization | Finalizes file uploads to SPE, creates Document records |
| **IndexingWorker** | office-indexing | Indexes documents for search |
| **ProfileWorker** | office-profile | AI summary generation |

## Azure Resources

| Resource | Value |
|----------|-------|
| **App Service** | `spe-api-dev-67e2xz` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **API URL** | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| **Service Bus** | (to be configured) |

---

## Step 1: Verify All Unit Tests Pass

Before deploying, ensure all tests pass:

```powershell
cd "c:\code_files\spaarke-wt-SDAP-outlook-office-add-in"
dotnet test --no-restore --verbosity minimal
```

**Expected**: All tests pass (0 failures)

---

## Step 2: Create/Verify Service Bus Queues

The workers require Service Bus queues for job processing. Queue configuration is defined in `infrastructure/bicep/modules/service-bus.bicep`.

### Required Queues

| Queue Name | Purpose |
|------------|---------|
| `office-upload-finalization` | Upload finalization jobs |
| `office-profile` | AI profile summary jobs |
| `office-indexing` | Document indexing jobs |
| `sdap-jobs` | General SDAP jobs |
| `document-indexing` | Legacy indexing (existing) |

### Create Queues via Azure CLI

```powershell
# Variables
$resourceGroup = "spe-infrastructure-westus2"
$serviceBusNamespace = "spaarke-servicebus-dev"  # Or existing namespace name

# Create Service Bus namespace (if not exists)
az servicebus namespace create `
  --resource-group $resourceGroup `
  --name $serviceBusNamespace `
  --location westus2 `
  --sku Standard

# Create Office worker queues
$queues = @("office-upload-finalization", "office-profile", "office-indexing")
foreach ($queue in $queues) {
    az servicebus queue create `
      --resource-group $resourceGroup `
      --namespace-name $serviceBusNamespace `
      --name $queue `
      --default-message-time-to-live P14D `
      --lock-duration PT5M `
      --max-delivery-count 10 `
      --dead-lettering-on-message-expiration true

    Write-Host "Created queue: $queue"
}

# Get connection string
$connectionString = az servicebus namespace authorization-rule keys list `
  --resource-group $resourceGroup `
  --namespace-name $serviceBusNamespace `
  --name RootManageSharedAccessKey `
  --query primaryConnectionString -o tsv

Write-Host "Connection String: $connectionString"
```

### Verify Queues Exist

```powershell
az servicebus queue list `
  --resource-group $resourceGroup `
  --namespace-name $serviceBusNamespace `
  --query "[].name" -o table
```

**Expected output**:
```
office-upload-finalization
office-profile
office-indexing
sdap-jobs
document-indexing
```

---

## Step 3: Configure Connection Strings in App Service

Workers require the Service Bus connection string in App Service configuration.

### Set App Settings

```powershell
# Set Service Bus connection string
az webapp config appsettings set `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --settings "ConnectionStrings__ServiceBus=$connectionString"

# Set Service Bus options
az webapp config appsettings set `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --settings "ServiceBus__ConnectionString=$connectionString" `
              "ServiceBus__QueueName=sdap-jobs" `
              "ServiceBus__MaxConcurrentCalls=5"
```

### Verify Settings

```powershell
az webapp config appsettings list `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --query "[?contains(name, 'ServiceBus')].{name:name,value:value}" -o table
```

**Expected**: Settings show ServiceBus connection string and options configured.

---

## Step 4: Deploy Updated API with Workers

Workers are part of the BFF API. Deploy using the standard API deployment process (see azure-deploy skill).

### Build and Deploy

```powershell
# Navigate to API project
cd src/server/api/Sprk.Bff.Api

# Build release
dotnet publish -c Release -o ./publish

# Create deployment package
Compress-Archive -Path './publish/*' -DestinationPath './publish.zip' -Force

# Deploy to Azure
az webapp deploy `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --src-path ./publish.zip `
  --type zip
```

### Verify Deployment

```powershell
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: Healthy

curl https://spe-api-dev-67e2xz.azurewebsites.net/status
# Expected: {"service":"Sprk.Bff.Api","version":"1.0.1-debug",...}
```

---

## Step 5: Verify Workers Start

Check App Service logs to verify workers registered and started successfully.

### Stream Logs

```powershell
az webapp log tail `
  --name spe-api-dev-67e2xz `
  --resource-group spe-infrastructure-westus2
```

**Expected log messages**:
```
UploadFinalizationWorker starting, listening on queue office-upload-finalization
Job processing configured with Service Bus (queue: sdap-jobs)
```

### Check Application Insights

Query Application Insights for worker startup logs:

```kusto
traces
| where message contains "Worker" and message contains "starting"
| project timestamp, message
| order by timestamp desc
| take 10
```

---

## Step 6: Test Job Processing with Sample Save Request

Test the Office save endpoint to verify workers process jobs correctly.

### Send Test Request

```powershell
# Get an access token (requires Azure AD app permissions)
$token = az account get-access-token `
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c `
  --query accessToken -o tsv

# Submit test save request
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/office/save" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -H "X-Idempotency-Key: test-$(Get-Date -Format 'yyyyMMddHHmmss')" `
  -d '{
    "sourceType": "WordDocument",
    "associationType": "Matter",
    "associationId": "00000000-0000-0000-0000-000000000000",
    "content": {
      "documentUrl": "https://test.sharepoint.com/doc.docx",
      "documentName": "test-document.docx"
    },
    "processing": {
      "profileSummary": false,
      "ragIndex": false
    }
  }'
```

**Expected Response (202 Accepted)**:
```json
{
  "jobId": "guid",
  "documentId": "guid",
  "statusUrl": "/office/jobs/{jobId}",
  "streamUrl": "/office/jobs/{jobId}/stream",
  "status": "Queued",
  "duplicate": false
}
```

### Check Job Status

```powershell
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/office/jobs/{jobId}" `
  -H "Authorization: Bearer $token"
```

---

## Step 7: Verify SSE Job Status Updates

Test that Server-Sent Events stream job status updates correctly.

### Connect to SSE Stream

```powershell
# Use curl to connect to SSE endpoint
curl -N "https://spe-api-dev-67e2xz.azurewebsites.net/office/jobs/{jobId}/stream" `
  -H "Accept: text/event-stream" `
  -H "Authorization: Bearer $token"
```

**Expected events**:
```
event: stage-update
data: {"stage":"FileUploaded","status":"Completed","timestamp":"..."}

event: stage-update
data: {"stage":"RecordsCreated","status":"Completed","timestamp":"..."}

event: job-complete
data: {"status":"Completed","documentId":"guid"}
```

---

## Step 8: Check Dead Letter Queue Handling

Verify that failed messages are moved to dead letter queues after max retries.

### Check Dead Letter Queue

```powershell
# List messages in dead letter queue
az servicebus queue show `
  --resource-group spe-infrastructure-westus2 `
  --namespace-name spaarke-servicebus-dev `
  --name "office-upload-finalization" `
  --query "countDetails.deadLetterMessageCount" -o tsv
```

**Expected**: 0 (no dead letters if all jobs process successfully)

### Verify Dead Letter Configuration

Each queue should have:
- `deadLetteringOnMessageExpiration: true`
- `maxDeliveryCount: 10` (messages move to DLQ after 10 failed attempts)

---

## Step 9: Document Deployment

### Deployment Log Entry

| Date | Version | Environment | Status | Notes |
|------|---------|-------------|--------|-------|
| 2026-01-20 | 1.0.0 | Dev | Pending | Workers deployment with Service Bus queues |

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Workers not starting | Missing Service Bus connection | Verify `ConnectionStrings:ServiceBus` in App Settings |
| "Queue not found" errors | Queues not created | Create queues using Azure CLI (Step 2) |
| Jobs stuck in "Queued" | Worker not processing | Check App Service logs for errors |
| SSE connection fails | Auth header not supported | Use fetch+ReadableStream (not EventSource) |
| Dead letter queue growing | Processing failures | Check Application Insights for error details |

### View Worker Logs

```powershell
# Application Insights query for worker errors
traces
| where message contains "Office" and severityLevel >= 3
| project timestamp, message, severityLevel
| order by timestamp desc
| take 50
```

---

## Acceptance Criteria

- [ ] All unit tests pass
- [ ] Service Bus queues created (office-upload-finalization, office-profile, office-indexing)
- [ ] Connection string configured in App Service
- [ ] API deployed successfully
- [ ] Workers start without errors (verified in logs)
- [ ] Sample job processes successfully
- [ ] SSE receives status updates
- [ ] Dead letter queue handling works
- [ ] Deployment documented

---

## Related Documentation

- [azure-deploy skill](../../../../.claude/skills/azure-deploy/SKILL.md) - Azure deployment procedures
- [jobs.md constraints](../../../../.claude/constraints/jobs.md) - Background job patterns
- [spec.md](../spec.md) - Full project specification
- [ADR-001](../../../../.claude/adr/ADR-001-minimal-api.md) - Minimal API + BackgroundService pattern

---

*After completing this task, Tasks 070-074 (E2E and integration tests) can begin.*
