# Spaarke Office Add-ins Deployment Checklist

> **Version**: 1.2
> **Last Updated**: January 24, 2026
> **Purpose**: Pre-deployment verification for IT Administrators

---

## Overview

Use this checklist before deploying Spaarke Office Add-ins to your organization. Complete all items before proceeding to M365 Admin Center deployment.

---

## Pre-Deployment Checklist

### 1. Infrastructure Prerequisites

#### Azure Resources
- [ ] BFF API App Service deployed and running
  - Resource: `spe-api-prod-*` in `rg-spaarke-prod-westus2`
  - Health check passes: `GET /healthz` returns "Healthy"
- [ ] Static Web Apps deployed for add-in hosting
  - Dev Resource: `spaarke-office-addins` (`icy-desert-0bfdbb61e.6.azurestaticapps.net`)
  - Prod Resource: `spe-office-addins-prod`
  - Taskpane URL accessible: `https://{swa-hostname}/outlook/taskpane.html`
- [ ] Redis Cache provisioned
  - Resource: `spaarke-redis-prod`
  - Connection verified from BFF API
- [ ] Service Bus namespace and queues created
  - Queues: `office-upload-finalization`, `office-profile`, `office-indexing`
- [ ] Application Insights configured
  - Resource: `spe-insights-prod-*`
  - Connection string in BFF API settings

#### Network Access
- [ ] Firewall rules allow access to:
  - `*.azurewebsites.net` (443/HTTPS)
  - `*.azurestaticapps.net` (443/HTTPS)
  - `login.microsoftonline.com` (443/HTTPS)
  - `*.crm.dynamics.com` (443/HTTPS)
  - `*.sharepoint.com` (443/HTTPS)

### 2. Dataverse Configuration

#### Dataverse Tables (Office Add-in Entities)
- [ ] **ProcessingJob table created** (`sprk_processingjob`)
  - Primary key: `sprk_processingjobid` (GUID)
  - Primary name field: `sprk_name` (String, 200 chars)
  - 19 total fields including status, stages, progress, correlation tracking
  - Choice fields: `sprk_jobtype` (7 values), `sprk_status` (5 values)
  - Lookup fields: `sprk_initiatedby` (systemuser), `sprk_document` (sprk_document)
  - Indexes: `sprk_idempotencykey` (alternate key), `sprk_status`
- [ ] **EmailArtifact table created** (`sprk_emailartifact`)
  - Primary key: `sprk_emailartifactid` (GUID)
  - Primary name field: `sprk_name` (String, 400 chars)
  - 15 total fields for email metadata (subject, sender, recipients, dates, etc.)
  - Choice field: `sprk_importance` (Low=0, Normal=1, High=2)
  - Lookup fields: `sprk_document` (sprk_document)
  - Indexes: `sprk_messageid`, `sprk_internetheadershash`
- [ ] **AttachmentArtifact table created** (`sprk_attachmentartifact`)
  - Primary key: `sprk_attachmentartifactid` (GUID)
  - Primary name field: `sprk_name` (String, 260 chars)
  - 8 total fields for attachment metadata (filename, content type, size, etc.)
  - Lookup fields: `sprk_emailartifact`, `sprk_document`
- [ ] **Relationships configured**:
  - [ ] `sprk_processingjob_sprk_document_1n` (ProcessingJob → Document)
  - [ ] `sprk_processingjob_systemuser_1n` (ProcessingJob → SystemUser)
  - [ ] `sprk_emailartifact_sprk_document_1n` (EmailArtifact → Document)
  - [ ] `sprk_attachmentartifact_sprk_emailartifact_1n` (AttachmentArtifact → EmailArtifact)
  - [ ] `sprk_attachmentartifact_sprk_document_1n` (AttachmentArtifact → Document)
- [ ] **Tables added to solution** "Spaarke Office Add In"
- [ ] **Forms created** for each table (basic form with all fields)

**Reference**: See `projects/sdap-office-integration/notes/DATAVERSE-TABLE-SCHEMAS.md` for complete schema definitions.

**Verification**:
```powershell
# Verify tables exist in Dataverse
pac org who
pac entity list --environment {env-id} | Select-String "sprk_processingjob|sprk_emailartifact|sprk_attachmentartifact"
```

#### Security Role Configuration
- [ ] Security role created: "Spaarke Office Add In User"
- [ ] Role added to "Spaarke Office Add In" solution
- [ ] **User-level permissions** configured for Office tables:
  - [ ] `sprk_processingjob`: Create, Read, Write, Append
  - [ ] `sprk_emailartifact`: Create, Read, Write, Append
  - [ ] `sprk_attachmentartifact`: Create, Read, Write, Append
  - [ ] `sprk_document`: Create, Read, Write, Append
- [ ] **Organization-level Read permissions** for lookup entities:
  - [ ] `sprk_matter`: Read
  - [ ] `sprk_project`: Read
  - [ ] `sprk_contact`: Read
  - [ ] `sprk_account`: Read
- [ ] Role assigned to all Office add-in users
- [ ] Service principal (app user) verified to have System Administrator role

**Verification**:
```powershell
# Verify security role exists
pac security-role list --environment {env-id} | Select-String "Spaarke Office Add In User"
```

### 3. Azure AD Configuration

#### Add-in App Registration
- [ ] App registration exists: `Spaarke Office Add-in`
- [ ] Client ID: `c1258e2d-1688-49d2-ac99-a7485ebd9995`
- [ ] Redirect URIs configured:
  - [ ] `brk-multihub://localhost` (reserved for future NAA support)
  - [ ] `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/taskpane.html` (Dev SPA)
  - [ ] `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-dialog.html` (Dev Dialog API)
  - [ ] `https://spe-office-addins-prod.azurestaticapps.net/taskpane.html` (Prod SPA)
  - [ ] `https://spe-office-addins-prod.azurestaticapps.net/auth-dialog.html` (Prod Dialog API)
- [ ] API permissions added:
  - [ ] `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` (delegated)
  - [ ] `User.Read` (delegated)
- [ ] Admin consent granted for organization

#### BFF API App Registration
- [ ] App registration exists: `Spaarke BFF API` (also known as `SDAP-BFF-SPE-API`)
- [ ] Client ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- [ ] App ID URI configured: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- [ ] Scopes exposed:
  - [ ] `SDAP.Access`
  - [ ] `user_impersonation`
- [ ] Client secret stored in Key Vault

#### ⚠️ Authorized Client Applications (CRITICAL)

> **This step is frequently missed and causes 401 Unauthorized errors.** The Office Add-in must be pre-authorized to call the BFF API.

- [ ] **Office Add-in registered as authorized client**:
  1. Go to Azure Portal → App registrations → `SDAP-BFF-SPE-API`
  2. Navigate to **Expose an API** → **Authorized client applications**
  3. Verify `c1258e2d-1688-49d2-ac99-a7485ebd9995` (Spaarke Office Add-in) is listed
  4. If not listed, click **+ Add a client application**:
     - Client ID: `c1258e2d-1688-49d2-ac99-a7485ebd9995`
     - Check both scopes: `SDAP.Access` and `user_impersonation`
     - Click **Add application**

**Verification**:
```powershell
# Check authorized clients
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "api.preAuthorizedApplications[].appId" -o tsv
# Should output: c1258e2d-1688-49d2-ac99-a7485ebd9995
```

### 4. BFF API Configuration

#### App Service Settings
- [ ] Environment set to `Production`
- [ ] `TENANT_ID` configured
- [ ] `API_APP_ID` configured
- [ ] `API_CLIENT_SECRET` references Key Vault
- [ ] Redis connection string configured
- [ ] Service Bus connection string configured
- [ ] Application Insights connection string configured

#### Office-Specific Settings
- [ ] Rate limiting settings configured:
  - `Office__RateLimiting__SavePerMinute`: 10
  - `Office__RateLimiting__SearchPerMinute`: 30
- [ ] Attachment limits configured:
  - `Office__AttachmentLimits__MaxSingleFileMb`: 25
  - `Office__AttachmentLimits__MaxTotalMb`: 100

#### CORS Configuration
- [ ] BFF API allows CORS from Static Web App origins:
  - [ ] `*.azurestaticapps.net` (Azure Static Web Apps)
  - [ ] `*.crm.dynamics.com` (Dataverse)
- [ ] Verify CORS with preflight test:
  ```powershell
  # Test CORS preflight
  curl -X OPTIONS "https://spe-api-dev-67e2xz.azurewebsites.net/office/save" `
    -H "Origin: https://icy-desert-0bfdbb61e.6.azurestaticapps.net" `
    -H "Access-Control-Request-Method: POST" -v
  # Should return: Access-Control-Allow-Origin header
  ```

#### Workers
- [ ] Background workers running (check logs)
- [ ] Service Bus queues processing messages

### 5. Manifest Configuration

> **CRITICAL**: Use the standardized manifest files. These have been validated to work with M365 Admin Center Centralized Deployment.

#### Manifest File Locations

| Add-in | Source File | Build Output |
|--------|-------------|--------------|
| Outlook | `outlook/outlook-manifest.xml` | `dist/outlook/manifest.xml` |
| Word | `word/word-manifest.xml` | `dist/word/manifest.xml` |

> **Note**: Legacy `manifest-working.xml` files have been consolidated into the standardized files above.

#### Outlook Manifest (outlook-manifest.xml)
- [ ] All URLs updated for target environment:
  - [ ] Dev: `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/taskpane.html`
  - [ ] Prod: `https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html`
  - [ ] Icon URLs (16, 32, 64, 80) point to correct static hosting
  - [ ] AppDomain includes hosting URL
- [ ] **Version format is 4-part**: `1.0.0.0` (NOT `1.0.0`)
- [ ] **All icon URLs return HTTP 200** (test each in browser)
- [ ] **NO FunctionFile element** (causes validation failures)
- [ ] **Single VersionOverrides V1.0** (not nested V1.0/V1.1)
- [ ] **RuleCollection Mode="Or"** (not single Rule element)
- [ ] **DisableEntityHighlighting element present**
- [ ] **MessageReadCommandSurface** for reading emails
- [ ] **MessageComposeCommandSurface** for composing emails (optional)
- [ ] Manifest validates: `npx office-addin-manifest validate outlook/outlook-manifest.xml`

#### Word Manifest (word-manifest.xml)
- [ ] All URLs updated for target environment:
  - [ ] Dev: `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/taskpane.html`
  - [ ] Prod: `https://spe-office-addins-prod.azurestaticapps.net/word/taskpane.html`
  - [ ] IconUrl and HighResolutionIconUrl point to correct hosting
  - [ ] AppDomain includes hosting URL
- [ ] **Version format is 4-part**: `1.0.0.0` (NOT `1.0.0`)
- [ ] **All icon URLs return HTTP 200**
- [ ] **PrimaryCommandSurface extension point**
- [ ] Manifest validates: `npx office-addin-manifest validate word/word-manifest.xml`

#### Common Manifest Requirements
- [ ] DefaultLocale is set (e.g., `en-US`)
- [ ] SupportUrl is valid and accessible
- [ ] All resource IDs (resid) have matching definitions in Resources section
- [ ] GUID in `<Id>` element is unique (different from other add-ins in tenant)

#### Manifest Format Decision (V1 vs V2)

**V1 (Current - XML Manifest)**: Use for all production deployments.

| Aspect | Status |
|--------|--------|
| Manifest Format | XML (separate per Office host) |
| Authentication | Dialog API (popup for initial sign-in) |
| M365 Admin Support | Full support |
| Stability | Production-proven |

**V2 (Future - Unified Manifest)**: Not yet available for production.

| Aspect | Status |
|--------|--------|
| Manifest Format | JSON (single file for Office + Teams) |
| Authentication | NAA (Nested App Authentication) |
| M365 Admin Support | Preview only |
| Requirement | NAA must be GA (not yet as of Jan 2026) |

**Current Recommendation**: ✅ Use XML manifests for V1 production deployment.

### 6. Security Review

- [ ] Azure AD permissions are minimal required
- [ ] No secrets in manifest files
- [ ] HTTPS enforced on all endpoints
- [ ] CORS configured correctly on BFF API
- [ ] Content Security Policy headers on static hosting
- [ ] Dataverse security roles configured for users

### 7. Testing

#### Staging Environment
- [ ] Add-in loads in test Outlook account
- [ ] Add-in loads in test Word account
- [ ] Authentication works (Dialog API - popup appears on first sign-in)
- [ ] Subsequent requests use cached token (no popup)
- [ ] Save flow completes successfully
- [ ] Search returns expected results
- [ ] Share flow generates valid links

#### Compatibility Testing
- [ ] New Outlook (Windows) - verified
- [ ] New Outlook (Mac) - verified
- [ ] Outlook Web - verified
- [ ] Word Desktop (Windows) - verified
- [ ] Word Desktop (Mac) - verified
- [ ] Word Web - verified

### 8. Monitoring Setup

- [ ] Application Insights alerts configured
- [ ] Action Group created for notifications
- [ ] Email recipients configured
- [ ] Teams webhook configured (optional)
- [ ] Dashboard created: "SDAP Office Integration"

### 9. Documentation and Communication

- [ ] User documentation available
- [ ] Admin documentation available
- [ ] Support team briefed
- [ ] Help desk articles updated
- [ ] Deployment window communicated to stakeholders
- [ ] Pilot group identified (if phased rollout)

### 10. Rollback Plan

- [ ] Previous manifest versions backed up
- [ ] Previous static assets available
- [ ] Slot swap tested (if using deployment slots)
- [ ] Rollback procedure documented
- [ ] Emergency contacts identified

---

## Deployment Checklist

### M365 Admin Center Deployment

#### Outlook Add-in
- [ ] Navigate to M365 Admin Center > Settings > Integrated apps
- [ ] Click "Upload custom apps"
- [ ] Select "Office Add-in"
- [ ] Upload `dist/outlook/manifest.xml` (built from `outlook/outlook-manifest.xml`)
- [ ] Manifest validation passes (no errors)
- [ ] Select deployment scope:
  - [ ] Pilot group (recommended for initial deployment)
  - [ ] Entire organization (after successful pilot)
- [ ] Review permissions and deploy
- [ ] Note deployment timestamp: _______________

#### Word Add-in
- [ ] Navigate to M365 Admin Center > Settings > Integrated apps
- [ ] Click "Upload custom apps"
- [ ] Select "Office Add-in"
- [ ] Upload `dist/word/manifest.xml` (built from `word/word-manifest.xml`)
- [ ] Manifest validation passes (no errors)
- [ ] Select same deployment scope as Outlook
- [ ] Review permissions and deploy
- [ ] Note deployment timestamp: _______________

> **Troubleshooting**: If manifest validation fails, check the [Manifest Format Requirements](office-addins-admin-guide.md#manifest-format-requirements) section.

---

## Post-Deployment Verification

### Immediate Checks (within 1 hour)

- [ ] Deployment status shows "Deployed" in Admin Center
- [ ] API health check still passing
- [ ] No errors in Application Insights
- [ ] Pilot users notified

### After Propagation (12-24 hours)

#### Outlook Add-in Verification
- [ ] Add-in visible in New Outlook desktop
- [ ] Add-in visible in Outlook Web
- [ ] Read mode: "Save to Spaarke" button works
- [ ] Compose mode: "Share from Spaarke" button works
- [ ] Task pane opens correctly
- [ ] Authentication succeeds
- [ ] Save flow completes successfully

#### Word Add-in Verification
- [ ] Add-in visible in Word Desktop (Windows)
- [ ] Add-in visible in Word Desktop (Mac)
- [ ] Add-in visible in Word Web
- [ ] All ribbon buttons appear
- [ ] Task pane opens correctly
- [ ] Authentication succeeds
- [ ] Save flow completes successfully

### Pilot Feedback (1-2 weeks)

- [ ] Pilot users surveyed for feedback
- [ ] Issues documented and addressed
- [ ] Performance metrics reviewed
- [ ] Decision made: expand to organization or address issues

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| IT Administrator | | | |
| Security Reviewer | | | |
| Project Manager | | | |

---

## Emergency Contacts

| Role | Name | Contact |
|------|------|---------|
| On-Call Engineer | | |
| IT Help Desk | | |
| Spaarke Support | | support@spaarke.com |

---

## Checklist Completion Summary

| Section | Items | Completed |
|---------|-------|-----------|
| Infrastructure | 5 | /5 |
| Dataverse | 18 | /18 |
| Azure AD | 11 | /11 |
| BFF API | 7 | /7 |
| Manifests | 9 | /9 |
| Security | 6 | /6 |
| Testing | 9 | /9 |
| Monitoring | 5 | /5 |
| Documentation | 6 | /6 |
| Rollback | 5 | /5 |
| **Total** | **81** | **/81** |

**Checklist completed by**: ____________________
**Date**: ____________________
**Ready for deployment**: [ ] Yes  [ ] No

---

*For detailed procedures, see the [Office Add-ins Administrator Guide](office-addins-admin-guide.md).*
