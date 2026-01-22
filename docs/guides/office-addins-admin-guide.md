# Spaarke Office Add-ins Administrator Guide

> **Version**: 1.0
> **Last Updated**: January 2026
> **Audience**: IT Administrators, System Administrators, DevOps Engineers

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Architecture Overview](#2-architecture-overview)
3. [Prerequisites](#3-prerequisites)
4. [Azure AD App Registrations](#4-azure-ad-app-registrations)
5. [Deployment via Microsoft 365 Admin Center](#5-deployment-via-microsoft-365-admin-center)
6. [Configuration Reference](#6-configuration-reference)
7. [Security Settings and Permissions](#7-security-settings-and-permissions)
8. [Monitoring and Alerting](#8-monitoring-and-alerting)
9. [Troubleshooting Guide](#9-troubleshooting-guide)
10. [Maintenance Procedures](#10-maintenance-procedures)
11. [Rollback Procedures](#11-rollback-procedures)
12. [Support Escalation](#12-support-escalation)

---

## 1. Introduction

### 1.1 Overview

The Spaarke Office Add-ins enable users to save emails, attachments, and documents directly to the Spaarke Document Management System (DMS) from within Microsoft Outlook and Word. This guide provides IT administrators with comprehensive information for deploying, configuring, monitoring, and maintaining these add-ins.

### 1.2 Add-in Summary

| Add-in | Host Application | Manifest Type | Key Capabilities |
|--------|------------------|---------------|------------------|
| **Spaarke Outlook Add-in** | New Outlook, Outlook Web, Classic Outlook | XML | Save emails, save attachments, share documents, grant external access |
| **Spaarke Word Add-in** | Word Desktop, Word Web | XML | Save documents, version management, share documents, grant external access |

> **Note**: XML manifests are recommended for M365 Admin Center Centralized Deployment. See [Manifest Format Requirements](#manifest-format-requirements) for details.

### 1.3 Supported Platforms

| Platform | Outlook Add-in | Word Add-in |
|----------|----------------|-------------|
| New Outlook (Windows) | Supported | N/A |
| New Outlook (Mac) | Supported | N/A |
| Outlook on the Web | Supported | N/A |
| Classic Outlook (Windows) | Not Supported | N/A |
| Word Desktop (Windows) | N/A | Supported |
| Word Desktop (Mac) | N/A | Supported |
| Word on the Web | N/A | Supported |

### 1.4 Version Information

| Component | Version |
|-----------|---------|
| Outlook Add-in | 1.0.0 |
| Word Add-in | 1.0.0 |
| Required Office.js (Outlook) | Mailbox 1.8+ |
| Required Office.js (Word) | WordApi 1.3+ |
| BFF API Version | 1.0.0 |

---

## 2. Architecture Overview

### 2.1 Component Diagram

```
Users (Outlook/Word)
         |
         | Office Add-in (Task Pane)
         | Nested App Authentication (NAA)
         |
         v
+---------------------------+
|     Spaarke BFF API       |
|   (Azure App Service)     |
|                           |
|  /office/* Endpoints      |
|  - POST /office/save      |
|  - GET /office/search     |
|  - POST /office/share     |
|  - GET /office/jobs       |
+---------------------------+
         |
    +---------+---------+
    |         |         |
    v         v         v
+-------+ +-------+ +--------+
|Dataverse|  |SPE   |  |Azure  |
|(Records)|  |Files |  |OpenAI |
+-------+ +-------+ +--------+
```

### 2.2 Data Flow

1. **Save Flow**: User selects content in Office app -> Add-in collects content -> Sends to BFF API -> API queues job -> Workers process (upload to SPE, create Dataverse records, AI processing)

2. **Search Flow**: User types in entity picker -> Add-in queries BFF API -> API searches Dataverse -> Returns matching entities

3. **Share Flow**: User selects documents to share -> Add-in calls BFF API -> API generates links or attachment packages

### 2.3 Key Azure Resources

| Resource | Purpose | Resource Group |
|----------|---------|----------------|
| `spe-api-prod-*` | BFF API hosting | `rg-spaarke-prod-westus2` |
| `spe-office-addins-prod` | Static asset hosting | `rg-spaarke-prod-westus2` |
| `spaarke-redis-prod` | Caching, rate limiting | `rg-spaarke-prod-westus2` |
| `spaarke-servicebus-prod` | Job queue processing | `rg-spaarke-prod-westus2` |
| `spe-insights-prod-*` | Application monitoring | `rg-spaarke-prod-westus2` |

---

## 3. Prerequisites

### 3.1 Infrastructure Prerequisites

Before deploying the add-ins, ensure the following are in place:

- [ ] BFF API deployed to production (App Service running)
- [ ] Static assets deployed to Azure Static Web Apps
- [ ] Azure AD app registrations configured (see Section 4)
- [ ] Redis Cache provisioned and accessible
- [ ] Service Bus namespace and queues created (`office-jobs` queue)
- [ ] Application Insights configured
- [ ] **Dataverse tables created**: `sprk_processingjob`, `sprk_emailartifact`, `sprk_attachmentartifact`
- [ ] **Dataverse security role configured**: "Spaarke Office Add In User" (see Section 7.5)
- [ ] **Users assigned security role**: All Office add-in users

### 3.2 Administrative Access Requirements

| Resource | Required Role |
|----------|---------------|
| Microsoft 365 Admin Center | Global Administrator or Exchange Administrator |
| Azure Portal | Contributor on resource group |
| Azure AD | Application Administrator |
| Dataverse | System Administrator |

### 3.3 Network Requirements

| Endpoint | Protocol | Port | Purpose |
|----------|----------|------|---------|
| `*.azurewebsites.net` | HTTPS | 443 | BFF API |
| `*.azurestaticapps.net` | HTTPS | 443 | Static assets |
| `login.microsoftonline.com` | HTTPS | 443 | Azure AD authentication |
| `*.crm.dynamics.com` | HTTPS | 443 | Dataverse |
| `*.sharepoint.com` | HTTPS | 443 | SharePoint Embedded |

### 3.4 Browser Requirements

For Outlook Web and Word Web:
- Microsoft Edge (Chromium) 79+
- Google Chrome 79+
- Safari 14+ (Mac)
- Firefox 78+

---

## 4. Azure AD App Registrations

### 4.1 Office Add-in App Registration

The Office add-in uses a public client (SPA) registration for Nested App Authentication (NAA).

| Property | Value |
|----------|-------|
| **App Name** | Spaarke Office Add-in |
| **Client ID** | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| **Application Type** | Single-page application (SPA) |
| **Supported Account Types** | Single tenant (organization only) |

#### Redirect URIs

| URI | Purpose |
|-----|---------|
| `brk-multihub://localhost` | NAA broker (required for NAA) |
| `https://spe-office-addins-prod.azurestaticapps.net/taskpane.html` | Dialog API fallback |

#### API Permissions (Delegated)

| Permission | Type | Admin Consent |
|------------|------|---------------|
| `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` | Delegated | Required |
| `User.Read` (Microsoft Graph) | Delegated | Not required |

### 4.2 BFF API App Registration

The BFF API uses a confidential client registration for token validation and On-Behalf-Of (OBO) flow.

| Property | Value |
|----------|-------|
| **App Name** | Spaarke BFF API |
| **Client ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Application Type** | Web API |
| **App ID URI** | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |

#### Exposed API Scopes

| Scope | Description |
|-------|-------------|
| `user_impersonation` | Access Spaarke BFF API on behalf of user |

#### API Permissions (Delegated, for OBO)

| Permission | Type | Purpose |
|------------|------|---------|
| `Files.ReadWrite.All` (Graph) | Delegated | SharePoint Embedded operations |
| `Sites.ReadWrite.All` (Graph) | Delegated | SharePoint Embedded operations |

### 4.3 Granting Admin Consent

Admin consent is required for the `user_impersonation` scope. To grant:

1. Go to Azure Portal > Azure Active Directory > App registrations
2. Select "Spaarke Office Add-in"
3. Navigate to API permissions
4. Click "Grant admin consent for [Tenant Name]"
5. Confirm the consent

**Verification**:
```powershell
# Verify consent status
az ad app permission list-grants --id c1258e2d-1688-49d2-ac99-a7485ebd9995 --query "[].{Scope:scope,ConsentType:consentType}"
```

---

## 5. Deployment via Microsoft 365 Admin Center

### 5.1 Pre-Deployment Checklist

Complete the [Office Add-ins Deployment Checklist](office-addins-deployment-checklist.md) before proceeding.

### 5.2 Deploy Outlook Add-in (XML Manifest)

#### Step 1: Access M365 Admin Center

1. Navigate to: https://admin.microsoft.com
2. Sign in with Global Admin or Exchange Admin credentials
3. Go to: **Settings** > **Integrated apps**

#### Step 2: Upload Manifest

1. Click **Upload custom apps**
2. Select **Office Add-in**
3. Choose **Upload manifest file (.xml)**
4. Browse to: `outlook/manifest-working.xml`
5. Click **Upload**

> **CRITICAL**: Use the `manifest-working.xml` file. Other manifest files may fail validation. See [Manifest Format Requirements](#manifest-format-requirements) for what makes a manifest valid.

#### Step 3: Configure Deployment Scope

| Option | Description | When to Use |
|--------|-------------|-------------|
| **Entire Organization** | All users get add-in | Production rollout |
| **Specific Groups** | Only selected M365 groups | Pilot or phased rollout |
| **Just Me** | Admin testing only | Pre-deployment testing |

#### Step 4: Review and Deploy

1. Review add-in details (name, publisher, permissions)
2. Click **Deploy**
3. Note: Propagation takes 12-24 hours for full organization

### 5.3 Deploy Word Add-in (XML Manifest)

Follow the same process as Outlook:
- Upload the XML manifest file (`word/manifest-working.xml`)
- Select **Upload manifest file (.xml)**

### Manifest Format Requirements

> **CRITICAL**: These requirements were validated through production testing. Non-compliance causes M365 Admin Center validation failures.

#### Common Requirements (All Add-ins)

| Element | Requirement | Example |
|---------|-------------|---------|
| **Version** | Must be 4-part format | `1.0.0.0` (NOT `1.0.0`) |
| **Icon URLs** | Must return HTTP 200 | All icon sizes must be accessible |
| **DefaultLocale** | Required | `en-US` |
| **SupportUrl** | Recommended | `https://spaarke.com/support` |
| **AppDomains** | Required | List all domains the add-in uses |

#### Outlook-Specific Requirements (MailApp)

| Rule | Reason |
|------|--------|
| **NO FunctionFile** | Causes validation failures in M365 Admin Center |
| **Single VersionOverrides V1.0** | Do NOT nest V1.1 inside V1.0 |
| **RuleCollection Mode="Or"** | Use collection, not single Rule |
| **DisableEntityHighlighting** | Must be present |
| **Use MessageReadCommandSurface** | For reading emails |
| **Use MessageComposeCommandSurface** | For composing emails |

#### Pre-Upload Validation Checklist

Before uploading any manifest to M365 Admin Center:

- [ ] Version is 4-part format: `X.X.X.X`
- [ ] All icon URLs return HTTP 200 (test each URL in browser)
- [ ] AppDomains includes all external domains
- [ ] DefaultLocale is set
- [ ] SupportUrl is valid
- [ ] Outlook: NO FunctionFile element
- [ ] Outlook: Single VersionOverrides V1.0 (not nested)
- [ ] Outlook: RuleCollection (not single Rule)
- [ ] Outlook: DisableEntityHighlighting present
- [ ] All resource IDs (resid) have matching definitions in Resources

For detailed manifest structure, see [Office Add-ins Architecture](../architecture/office-outlook-teams-integration-architecture.md#manifest-format-requirements).

### 5.4 Pilot Deployment (Recommended)

For initial rollout, deploy to a pilot group first:

1. **Create M365 Security Group**: `Spaarke Office Add-in Pilot`
2. **Add Pilot Users**: 5-10 users from different departments
3. **Deploy to Pilot Group**: Select "Specific Groups" during deployment
4. **Pilot Period**: 1-2 weeks
5. **Collect Feedback**: Monitor Application Insights, gather user feedback
6. **Expand**: After successful pilot, change scope to "Entire Organization"

### 5.5 Verify Deployment

After deployment, verify in M365 Admin Center:

| Check | Expected Result |
|-------|-----------------|
| Outlook add-in status | "Deployed" |
| Word add-in status | "Deployed" |
| User assignment | Correct scope |
| Permissions | Granted |

---

## 6. Configuration Reference

### 6.1 BFF API Configuration

The BFF API is configured via Azure App Service application settings:

#### Authentication Settings

| Setting | Description | Example Value |
|---------|-------------|---------------|
| `TENANT_ID` | Azure AD tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `API_APP_ID` | BFF API app registration ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `API_CLIENT_SECRET` | BFF API client secret | `@Microsoft.KeyVault(...)` |

#### Office Integration Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `Office__RateLimiting__SavePerMinute` | Max save requests per user per minute | `10` |
| `Office__RateLimiting__QuickCreatePerMinute` | Max quick create requests | `5` |
| `Office__RateLimiting__SearchPerMinute` | Max search requests | `30` |
| `Office__RateLimiting__JobsPerMinute` | Max job status requests | `60` |
| `Office__RateLimiting__SharePerMinute` | Max share requests | `20` |
| `Office__AttachmentLimits__MaxSingleFileMb` | Max single attachment size | `25` |
| `Office__AttachmentLimits__MaxTotalMb` | Max total attachments | `100` |

#### Feature Flags

| Setting | Description | Default |
|---------|-------------|---------|
| `Office__Features__ProfileSummary` | Enable AI profile summary | `true` |
| `Office__Features__RagIndex` | Enable RAG indexing | `true` |
| `Office__Features__DeepAnalysis` | Enable deep analysis | `false` |

### 6.2 Rate Limiting Configuration

Rate limits are per-user per-minute, enforced via Redis:

| Endpoint | Default Limit | Configurable |
|----------|---------------|--------------|
| `POST /office/save` | 10 | Yes |
| `POST /office/quickcreate/*` | 5 | Yes |
| `GET /office/search/*` | 30 | Yes |
| `GET /office/jobs/*` | 60 | Yes |
| `POST /office/share/*` | 20 | Yes |

**Response when rate limited**:
- HTTP Status: `429 Too Many Requests`
- Header: `Retry-After: {seconds}`

### 6.3 Attachment Size Limits

| Limit | Default | Configurable |
|-------|---------|--------------|
| Single file maximum | 25 MB | Yes |
| Total per email | 100 MB | Yes |

Files exceeding limits will receive error code `OFFICE_004` or `OFFICE_005`.

### 6.4 Blocked File Types

The following file extensions are blocked for security:

```
.exe, .dll, .bat, .cmd, .ps1, .vbs, .js, .jar, .msi, .scr, .com, .pif, .reg
```

Blocked files receive error code `OFFICE_006`.

---

## 7. Security Settings and Permissions

### 7.1 Authentication Architecture

The add-ins use **Nested App Authentication (NAA)**, the Microsoft-recommended pattern for Office Add-ins:

1. **Add-in obtains token**: Using MSAL.js 3.x `createNestablePublicClientApplication()`
2. **Silent acquisition first**: Token retrieved from cache if valid
3. **Interactive fallback**: Popup via Office Dialog API if silent fails
4. **Token validated by BFF API**: Azure AD token validation
5. **OBO for downstream calls**: BFF uses On-Behalf-Of for Graph/SPE calls

### 7.2 Token Security

| Aspect | Implementation |
|--------|----------------|
| Token storage | Browser localStorage (MSAL default) |
| Token lifetime | 1 hour (access token), 24 hours (refresh token) |
| Audience validation | BFF API validates `aud` claim |
| Issuer validation | BFF API validates `iss` claim |

### 7.3 Data Security

| Data Type | Storage Location | Encryption |
|-----------|------------------|------------|
| Documents | SharePoint Embedded | At-rest encryption (Microsoft managed) |
| Metadata | Dataverse | At-rest encryption (Microsoft managed) |
| Tokens | Browser localStorage | N/A (tokens are signed) |
| Secrets | Azure Key Vault | Vault-managed encryption |

### 7.4 Network Security

| Requirement | Implementation |
|-------------|----------------|
| HTTPS only | All endpoints require TLS 1.2+ |
| CORS | BFF API restricts to add-in domains |
| Content Security Policy | Static hosting headers |
| API authentication | Bearer token required on all `/office/*` endpoints |

### 7.5 Dataverse Security Roles

Users must have appropriate Dataverse security roles to use the add-in:

| Role | Capabilities |
|------|--------------|
| **Spaarke User** | Create/read documents, search entities |
| **Spaarke Power User** | Above + Quick Create entities |
| **Spaarke Office Add In User** | Office add-in operations, create Office entities |
| **Spaarke Administrator** | Full access |

#### Configuring the "Spaarke Office Add In User" Security Role

The "Spaarke Office Add In User" role provides access to Office add-in-specific Dataverse tables. This role should be assigned to all users who will use the Outlook or Word add-ins.

**Required Tables and Permissions**:

| Table | Permission Level | Operations | Why Required |
|-------|------------------|------------|--------------|
| `sprk_document` | User | Create, Read, Write, Append | Save documents and view existing |
| `sprk_processingjob` | User | Create, Read, Write | Track async job status |
| `sprk_emailartifact` | User | Create, Read, Write | Save email metadata |
| `sprk_attachmentartifact` | User | Create, Read, Write | Save attachment metadata |
| `sprk_matter` | Organization | Read | Associate documents with matters |
| `sprk_project` | Organization | Read | Associate documents with projects |
| `sprk_contact` | Organization | Read | Associate documents with contacts |
| `sprk_account` | Organization | Read | Associate documents with accounts |

**Setup Instructions**:

1. Navigate to Power Platform Admin Center > Your environment > Settings > Users + permissions > Security roles
2. Click "New role" (or edit existing "Spaarke Office Add In User")
3. Select the **Custom Entities** tab
4. For each table listed above, set the permission level:
   - **User level**: Four circles filled (Create, Read, Write, Append)
   - **Organization level**: All five circles filled (Read only for lookup entities)
5. Save the security role
6. Assign to users via Power Platform Admin Center > Users > Manage security roles

**Permission Level Explanation**:

- **User level**: Users can only access records they own or that are shared with them
- **Organization level**: Users can access all records in the organization (for read-only lookups)

**Verification**:

```powershell
# Verify security role configuration
pac admin list --environment {env-id}
pac security-role list --environment {env-id}
```

After configuration, users should be able to:
- Save emails and attachments from Outlook
- Save documents from Word
- View job processing status
- Search and associate documents with entities

### 7.6 Audit Logging

All operations are logged to Application Insights with:
- User identity (from Azure AD token)
- Operation type and timestamp
- Correlation ID for distributed tracing
- Success/failure status
- Associated entity references

---

## 8. Monitoring and Alerting

### 8.1 Application Insights

All add-in telemetry flows to Application Insights:

| Metric Category | Examples |
|-----------------|----------|
| Request metrics | Duration, success rate, error codes |
| Custom metrics | Save counts, search usage, SSE connections |
| Exceptions | Stack traces, error messages |
| Dependencies | SPE, Dataverse, Redis response times |

### 8.2 Key Performance Indicators

| KPI | Target | Alert Threshold |
|-----|--------|-----------------|
| API Availability | > 99.9% | < 99% |
| Save Response Time | < 3 seconds | > 3 seconds (P95) |
| Search Response Time | < 500 ms | > 500 ms (P95) |
| Error Rate | < 1% | > 1% |
| Worker Success Rate | > 95% | < 95% |

### 8.3 Critical Alerts

| Alert | Condition | Severity | Action |
|-------|-----------|----------|--------|
| API Availability Critical | < 99% for 5 min | 0 | Page on-call |
| Save Endpoint Failures | > 10 5xx errors in 5 min | 0 | Page on-call |
| Worker Dead Letter Queue | > 0 messages | 0 | Page on-call |
| Redis Connection Failures | > 0 in 5 min | 0 | Page on-call |

### 8.4 Warning Alerts

| Alert | Condition | Severity | Action |
|-------|-----------|----------|--------|
| Save Latency High | P95 > 3s for 15 min | 2 | Email team |
| Search Latency High | P95 > 500ms for 15 min | 2 | Email team |
| Error Rate Elevated | > 100 4xx in 5 min | 2 | Email team |
| Queue Depth High | > 1000 messages | 2 | Email team |

### 8.5 Dashboard Access

Access the monitoring dashboard:

1. Azure Portal > Dashboards > "SDAP Office Integration"
2. Or directly via Application Insights > Overview

For detailed monitoring procedures, see the [Monitoring Runbook](../../projects/sdap-office-integration/notes/monitoring-runbook.md).

---

## 9. Troubleshooting Guide

### 9.1 Common Issues

#### Add-in Not Visible to Users

| Symptom | Cause | Resolution |
|---------|-------|------------|
| Add-in not in ribbon | Not yet propagated | Wait 12-24 hours after deployment |
| Add-in not visible | User not in deployment scope | Verify user group membership |
| "Add-in not available" | Manifest removed | Re-upload manifest |

#### Authentication Issues

| Symptom | Cause | Resolution |
|---------|-------|------------|
| Sign-in popup blocked | Browser popup blocker | Allow popups for add-in domain |
| "Invalid token" error | Token expired | User signs in again |
| "Access denied" error | Missing permissions | Verify app registration permissions |
| NAA not working | Unsupported client | Falls back to Dialog API automatically |

#### Task Pane Issues

| Symptom | Cause | Resolution |
|---------|-------|------------|
| Blank task pane showing "Loading..." | React bundle not deployed | Rebuild and redeploy: `npm run build` then push to GitHub (triggers deployment) |
| Blank task pane | HTTPS certificate issue | Verify static hosting SSL |
| Task pane won't load | URL mismatch | Check manifest URLs match hosting |
| "We couldn't load the add-in" | Network issue | Check firewall/proxy settings |
| Icons not showing | Asset URLs incorrect | Verify icon URLs resolve |
| Add-in added but not in toolbar | Viewing wrong context | For Outlook: add-in only appears when reading/composing based on manifest ExtensionPoints |

#### Save/Processing Issues

| Symptom | Cause | Resolution |
|---------|-------|------------|
| Save returns 401 | Token expired | Re-authenticate |
| Save returns 403 | No permission | Check Dataverse roles |
| Save returns 429 | Rate limited | Wait and retry |
| Job stuck in "Queued" | Workers not running | Check App Service health |
| Job fails | Processing error | Check Application Insights |

### 9.2 Clearing Office Add-in Cache

**Windows**:
```powershell
# Close all Office applications first
Remove-Item "$env:LOCALAPPDATA\Microsoft\Office\16.0\Wef" -Recurse -Force -ErrorAction SilentlyContinue
```

**Mac**:
```bash
# Close all Office applications first
rm -rf ~/Library/Containers/com.microsoft.Outlook/Data/Library/Caches/Microsoft/Office/16.0/Wef
rm -rf ~/Library/Containers/com.microsoft.Word/Data/Library/Caches/Microsoft/Office/16.0/Wef
```

### 9.3 Diagnostic Commands

```powershell
# Check API health
curl https://spe-api-prod-*.azurewebsites.net/healthz

# Check Office endpoints (requires token)
curl -H "Authorization: Bearer {token}" https://spe-api-prod-*.azurewebsites.net/office/recent

# Check static assets
curl -I https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html

# Stream App Service logs
az webapp log tail --name spe-api-prod-* --resource-group rg-spaarke-prod-westus2
```

### 9.4 Error Code Reference

| Code | Title | Common Cause | Resolution |
|------|-------|--------------|------------|
| OFFICE_001 | Invalid source type | Malformed request | Check request body |
| OFFICE_002 | Invalid association type | Unknown entity type | Use valid entity type |
| OFFICE_003 | Association required | Missing association ID | Select entity before save |
| OFFICE_004 | Attachment too large | File > 25 MB | Save file separately |
| OFFICE_005 | Total size exceeded | Combined > 100 MB | Reduce attachment count |
| OFFICE_006 | Blocked file type | Dangerous extension | Cannot save this file type |
| OFFICE_007 | Association not found | Entity doesn't exist | Verify entity exists |
| OFFICE_008 | Job not found | Invalid job ID | Use valid job ID |
| OFFICE_009 | Access denied | No permission | Check Dataverse roles |
| OFFICE_010 | Cannot create entity | No create permission | Check Quick Create permission |

---

## 10. Maintenance Procedures

### 10.1 Routine Maintenance Tasks

| Task | Frequency | Procedure |
|------|-----------|-----------|
| Check dashboard metrics | Daily | Review SDAP Office Integration dashboard |
| Review alert history | Weekly | Check fired alerts, investigate patterns |
| Certificate renewal | Before expiry | Update SSL certificates if custom domain |
| Secret rotation | Every 6 months | Rotate API client secret in Key Vault |
| Dependency updates | Monthly | Review and update npm/NuGet packages |

### 10.2 Updating the Add-ins

#### Minor Update (Bug fixes, UI changes)

1. Build new version: `npm run build:prod`
2. Deploy static assets to Azure Static Web Apps
3. No manifest change needed if URLs unchanged

#### Major Update (New features, manifest changes)

1. Build new version: `npm run build:prod`
2. Update manifest version number
3. Deploy static assets
4. Upload updated manifest to M365 Admin Center
5. Re-deploy (overwrites existing deployment)

### 10.3 Secret Rotation

**Rotate BFF API Client Secret**:

1. Generate new secret in Azure AD app registration
2. Add new secret to Key Vault
3. Update App Service Key Vault reference (or wait for auto-refresh)
4. Verify API health
5. Delete old secret from Azure AD

```powershell
# Add secret to Key Vault
az keyvault secret set `
  --vault-name spaarke-kv-prod `
  --name API-CLIENT-SECRET `
  --value "{new-secret-value}"

# Verify App Service picks up new secret
az webapp restart --name spe-api-prod-* --resource-group rg-spaarke-prod-westus2
curl https://spe-api-prod-*.azurewebsites.net/healthz
```

### 10.4 Log Retention

| Log Type | Retention | Location |
|----------|-----------|----------|
| Application Insights | 90 days | Azure |
| App Service logs | 30 days | Azure |
| Dataverse audit logs | 365 days | Dataverse |

---

## 11. Rollback Procedures

### 11.1 Immediate Rollback (Remove Add-in)

**Use when**: Add-in is causing critical issues.

1. Go to M365 Admin Center > Settings > Integrated apps
2. Find Spaarke add-in
3. Click ... > Remove
4. Confirm removal
5. Propagation: 12-24 hours (faster: change scope to "Just Me" first)

### 11.2 Version Rollback (Deploy Previous Version)

**Use when**: New version has bugs, previous version worked.

1. Checkout previous release tag in git
2. Build: `npm run build:prod`
3. Deploy static assets
4. If manifest changed, re-upload old manifest to M365 Admin Center

### 11.3 API Rollback (Slot Swap)

**Use when**: API deployment caused issues.

```powershell
# Swap production back to staging (previous version)
az webapp deployment slot swap `
  --resource-group rg-spaarke-prod-westus2 `
  --name spe-api-prod-* `
  --slot staging `
  --target-slot production

# Verify
curl https://spe-api-prod-*.azurewebsites.net/healthz
```

### 11.4 Rollback Decision Tree

```
Is add-in causing Office crashes?
|-- YES --> Immediate Rollback (11.1)
|
+-- NO --> Is add-in functionality broken?
           |-- YES --> Is previous version available?
           |           |-- YES --> Version Rollback (11.2)
           |           +-- NO --> Immediate Rollback (11.1)
           |
           +-- NO --> Is API returning errors?
                      |-- YES --> API Rollback (11.3)
                      +-- NO --> Monitor and investigate
```

---

## 12. Support Escalation

### 12.1 Support Tiers

| Tier | Contact | Response Time | Issues |
|------|---------|---------------|--------|
| L1 | IT Help Desk | 4 hours | User-facing issues, password resets |
| L2 | Spaarke Support | 2 hours | Configuration, deployment issues |
| L3 | Development Team | Same day | Code bugs, critical issues |
| Vendor | Microsoft Support | Per SLA | Azure/M365 platform issues |

### 12.2 Escalation Criteria

**Escalate to L2 when**:
- Issue affects multiple users
- Issue persists after basic troubleshooting
- Configuration change required

**Escalate to L3 when**:
- Bug in add-in code suspected
- API returning unexpected errors
- Data integrity concerns

**Escalate to Microsoft when**:
- Azure/M365 platform issues
- Service outages
- Office.js API bugs

### 12.3 Information to Collect for Support

- User's email address and tenant
- Screenshot of error message
- Steps to reproduce
- Browser and Office version
- Correlation ID (from error response)
- Timestamp of occurrence

---

## Appendix A: Quick Reference Card

### Key URLs

| Resource | URL |
|----------|-----|
| M365 Admin Center | https://admin.microsoft.com |
| Azure Portal | https://portal.azure.com |
| BFF API Health | https://spe-api-prod-*.azurewebsites.net/healthz |
| Static Assets | https://spe-office-addins-prod.azurestaticapps.net |

### Key Commands

```powershell
# Check API health
curl https://spe-api-prod-*.azurewebsites.net/healthz

# Stream logs
az webapp log tail --name spe-api-prod-* --resource-group rg-spaarke-prod-westus2

# Restart API
az webapp restart --name spe-api-prod-* --resource-group rg-spaarke-prod-westus2
```

### Key Contacts

| Role | Contact |
|------|---------|
| On-Call Engineer | PagerDuty rotation |
| Spaarke Support | support@spaarke.com |
| IT Help Desk | helpdesk@organization.com |

---

## Appendix B: Glossary

| Term | Definition |
|------|------------|
| **NAA** | Nested App Authentication - Microsoft's recommended auth pattern for Office Add-ins |
| **SSE** | Server-Sent Events - Real-time updates for job status |
| **SPE** | SharePoint Embedded - Document storage platform |
| **OBO** | On-Behalf-Of flow - Azure AD token exchange |
| **BFF** | Backend for Frontend - API layer serving Office add-ins |
| **DLQ** | Dead Letter Queue - Failed messages in Service Bus |

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | January 2026 | Spaarke Team | Initial release |

---

*For user-facing documentation, see the [Spaarke Office Add-ins User Guide](../product-documentation/office-addins-user-guide.md).*
