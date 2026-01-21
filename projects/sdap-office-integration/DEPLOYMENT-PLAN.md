# SDAP Office Integration - Deployment Plan

> **Status**: Ready for Deployment
> **Created**: 2026-01-21
> **Code Status**: All 56 tasks complete, merged with master
> **PR**: #146 - feat(office-integration): SDAP Office Integration - Outlook and Word Add-ins

---

## Overview

This document outlines the **manual deployment steps** required to deploy the SDAP Office Integration to production. All code has been developed and tested. The following steps configure Azure resources, deploy services, and publish add-ins.

---

## Prerequisites

### Required Access
- [ ] Azure Portal admin access (Contributor role on `spe-infrastructure-westus2` resource group)
- [ ] Azure CLI installed and authenticated (`az login`)
- [ ] Power Platform CLI installed (`pac auth create`)
- [ ] Dataverse environment admin access (`https://spaarkedev1.crm.dynamics.com`)
- [ ] M365 Global Admin or Teams Service Admin (for add-in deployment)
- [ ] GitHub repository write access (for pushing code)

### Required Information
- [ ] Tenant ID: `{your-tenant-id}`
- [ ] Spaarke Client App ID (existing): `{spaarke-client-app-id}`
- [ ] BFF API App ID (existing): `{bff-api-app-id}`
- [ ] Azure OpenAI endpoint: `https://spaarke-openai-dev.openai.azure.com/`
- [ ] Key Vault name: `spaarke-spekvcert`

---

## Phase 1: Azure AD App Registrations

### Task 1.1: Create Office Add-in App Registration

**Purpose**: Separate app registration for Office add-ins with NAA authentication

**Steps**:
1. Navigate to Azure Portal → Azure Active Directory → App Registrations
2. Click **New registration**
3. Configure:
   - Name: `Spaarke Office Add-in (Dev)`
   - Supported account types: `Accounts in this organizational directory only`
   - Redirect URIs:
     - Platform: **Single-page application (SPA)**
     - URIs:
       - `https://localhost:3000` (dev)
       - `https://spe-api-dev-67e2xz.azurewebsites.net/office/auth-callback` (prod)
4. Click **Register**
5. Note the **Application (client) ID** → Save as `OFFICE_ADDIN_CLIENT_ID`

**Configure API Permissions**:
1. Go to **API Permissions** tab
2. Click **Add a permission** → **My APIs** → Select your **BFF API app registration**
3. Select **Delegated permissions**
4. Check: `user_impersonation` (or the scope you exposed in BFF API)
5. Click **Add permissions**
6. Click **Grant admin consent** for the tenant

**Configure Authentication**:
1. Go to **Authentication** tab
2. Under **Implicit grant and hybrid flows**, check:
   - ✅ Access tokens (used for implicit flows)
   - ✅ ID tokens (used for implicit and hybrid flows)
3. Under **Allow public client flows**: Set to **No**
4. Click **Save**

**Configure Token Configuration** (Optional - add claims):
1. Go to **Token configuration** tab
2. Click **Add optional claim**
3. Token type: **ID**
4. Claims: `email`, `family_name`, `given_name`
5. Click **Add**

**Reference**: Task 001 documentation in `projects/sdap-office-integration/tasks/001-create-addin-app-registration.poml`

---

### Task 1.2: Update BFF API App Registration for OBO

**Purpose**: Enable On-Behalf-Of (OBO) flow for BFF API to call Graph API on behalf of add-in users

**Steps**:
1. Navigate to Azure Portal → Azure Active Directory → App Registrations
2. Find your **BFF API app registration** (e.g., `Spaarke BFF API (Dev)`)
3. Go to **Expose an API** tab
4. Verify `api://{BFF_API_CLIENT_ID}/user_impersonation` scope exists
   - If missing, create it:
     - Scope name: `user_impersonation`
     - Who can consent: **Admins and users**
     - Admin consent display name: `Access BFF API as user`
     - Admin consent description: `Allow the Office add-in to access BFF API on behalf of the signed-in user`
5. Click **Save**

**Add API Permissions** (for OBO flow):
1. Go to **API Permissions** tab
2. Click **Add a permission** → **Microsoft APIs** → **Microsoft Graph**
3. Select **Delegated permissions**
4. Add the following permissions:
   - `User.Read` (read user profile)
   - `Files.ReadWrite.All` (SharePoint Embedded operations)
   - `Sites.ReadWrite.All` (SharePoint Embedded containers)
   - `Mail.ReadWrite` (for Outlook email access)
5. Click **Add permissions**
6. Click **Grant admin consent** for the tenant

**Configure Certificates & Secrets**:
1. Go to **Certificates & secrets** tab
2. Click **New client secret**
3. Description: `Office Add-in OBO - Dev`
4. Expires: `24 months` (or per your org policy)
5. Click **Add**
6. **IMPORTANT**: Copy the secret **Value** immediately → Save as `BFF_API_CLIENT_SECRET`

**Reference**: Task 002 documentation in `projects/sdap-office-integration/tasks/002-update-bff-app-registration.poml`

---

### Task 1.3: Store Secrets in Azure Key Vault

**Purpose**: Securely store app secrets for runtime configuration

**Steps**:
```powershell
# Authenticate to Azure
az login

# Set Key Vault name
$keyVaultName = "spaarke-spekvcert"

# Store Office Add-in Client ID
az keyvault secret set `
  --vault-name $keyVaultName `
  --name "office-addin-client-id" `
  --value "{OFFICE_ADDIN_CLIENT_ID}"

# Store BFF API Client Secret (for OBO flow)
az keyvault secret set `
  --vault-name $keyVaultName `
  --name "bff-api-client-secret" `
  --value "{BFF_API_CLIENT_SECRET}"
```

**Verify**:
```powershell
az keyvault secret list --vault-name $keyVaultName --query "[?contains(name,'office')].name"
```

---

## Phase 2: Dataverse Schema Deployment

### Task 2.1: Deploy Solution to Dataverse

**Purpose**: Create EmailArtifact, AttachmentArtifact, and ProcessingJob tables

**Solution Location**: `src/solutions/OfficeIntegration/`

**Steps**:
1. Authenticate to Dataverse:
   ```powershell
   pac auth create `
     --url https://spaarkedev1.crm.dynamics.com `
     --deviceCode
   ```

2. Build the solution (if not already built):
   ```powershell
   cd src/solutions/OfficeIntegration
   msbuild /t:build /restore
   ```

3. Import solution to Dataverse:
   ```powershell
   pac solution import `
     --path bin/Debug/OfficeIntegration.zip `
     --async `
     --force-overwrite
   ```

4. Monitor import status:
   ```powershell
   pac solution list
   ```

**Verify**:
1. Navigate to https://make.powerapps.com
2. Select **Spaarke Dev** environment
3. Go to **Tables** → Verify:
   - ✅ `sprk_emailartifact` exists
   - ✅ `sprk_attachmentartifact` exists
   - ✅ `sprk_processingjob` exists
4. Check **Relationships** on Document table:
   - ✅ `sprk_document_emailartifact` (1:N)
   - ✅ `sprk_document_attachmentartifact` (1:N)
   - ✅ `sprk_document_processingjob` (1:N)

**Reference**: Task 015 documentation in `projects/sdap-office-integration/tasks/015-deploy-dataverse-solution.poml`

---

### Task 2.2: Configure Security Roles

**Purpose**: Grant Office Add-in users access to new tables

**Steps**:
1. Navigate to https://make.powerapps.com → **Spaarke Dev** environment
2. Go to **Settings** → **Users + permissions** → **Security roles**
3. Select **Basic User** role (or your custom user role)
4. Go to **Custom Entities** tab
5. For each table, grant permissions:
   - `sprk_emailartifact`: Create, Read, Write, Delete (User level)
   - `sprk_attachmentartifact`: Create, Read, Write, Delete (User level)
   - `sprk_processingjob`: Create, Read, Write (User level)
6. Click **Save**

**Reference**: Task 014 documentation in `projects/sdap-office-integration/tasks/014-configure-security-roles.poml`

---

## Phase 3: BFF API Deployment

### Task 3.1: Update BFF API Configuration

**Purpose**: Add Office module configuration to App Service

**Steps**:
1. Navigate to Azure Portal → App Services → `spe-api-dev-67e2xz`
2. Go to **Configuration** → **Application settings**
3. Add/update the following settings:

| Name | Value | Source |
|------|-------|--------|
| `AzureAd:TenantId` | `{your-tenant-id}` | Azure AD |
| `AzureAd:ClientId` | `{BFF_API_CLIENT_ID}` | App registration |
| `AzureAd:ClientSecret` | `@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/bff-api-client-secret/)` | Key Vault reference |
| `Office:AddinClientId` | `@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/office-addin-client-id/)` | Key Vault reference |
| `Office:IdempotencyEnabled` | `true` | Feature flag |
| `Office:RateLimitingEnabled` | `true` | Feature flag |
| `Office:DuplicateDetection:Enabled` | `true` | Feature flag |
| `Office:DuplicateDetection:LookbackDays` | `90` | Config |

4. Click **Save** → **Continue**
5. App Service will restart automatically

**Verify Key Vault Access**:
```powershell
# Check if App Service managed identity has Key Vault access
az keyvault show --name spaarke-spekvcert --query properties.accessPolicies
```

If missing, grant access:
```powershell
# Get App Service principal ID
$principalId = az webapp identity show `
  --name spe-api-dev-67e2xz `
  --resource-group spe-infrastructure-westus2 `
  --query principalId -o tsv

# Grant Key Vault access
az keyvault set-policy `
  --name spaarke-spekvcert `
  --object-id $principalId `
  --secret-permissions get list
```

---

### Task 3.2: Deploy BFF API Code

**Purpose**: Deploy updated BFF API with Office endpoints

**Option A: GitHub Actions (Recommended)**:
1. Push branch to GitHub (we'll do this in Step 4)
2. GitHub Actions workflow will automatically deploy to Azure App Service
3. Monitor deployment: https://github.com/spaarke-dev/spaarke/actions

**Option B: Manual Deployment**:
```powershell
# Build the project
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ./publish

# Deploy to Azure App Service
az webapp deploy `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --src-path ./publish `
  --type zip `
  --async true
```

**Verify Deployment**:
```powershell
# Test health endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Test Office endpoints (requires authentication)
curl https://spe-api-dev-67e2xz.azurewebsites.net/office/recent `
  -H "Authorization: Bearer {access-token}"
```

**Reference**: Task 035 deployment guide in `projects/sdap-office-integration/tasks/035-deploy-bff-api.poml`

---

### Task 3.3: Verify Background Workers

**Purpose**: Ensure Service Bus workers are processing Office jobs

**Steps**:
1. Navigate to Azure Portal → Service Bus → `sdap-jobs` queue
2. Check **Active messages** count → Should be low (workers processing)
3. Check **Dead-letter messages** → Should be 0 (no failures)

**View Logs**:
```powershell
# Stream App Service logs
az webapp log tail `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz
```

**Look for**:
- ✅ `[UploadFinalizationWorker] Processing job {jobId}`
- ✅ `[ProfileSummaryWorker] Document profiling complete`
- ✅ `[IndexingWorker] Document indexed successfully`

**Reference**: Task 066 deployment in `projects/sdap-office-integration/tasks/066-deploy-workers.poml`

---

## Phase 4: Office Add-in Deployment

### Task 4.1: Update Manifests with Production URLs

**Purpose**: Replace localhost URLs with production endpoints

**Outlook Manifest**: `src/client/office-addins/outlook/manifest.json`

**Changes Needed**:
```json
{
  "authorization": {
    "permissions": {
      "resourceSpecific": [
        {
          "name": "Mail.Read.User",
          "type": "Delegated"
        }
      ]
    }
  },
  "webApplicationInfo": {
    "id": "{OFFICE_ADDIN_CLIENT_ID}",
    "resource": "api://{BFF_API_CLIENT_ID}"
  },
  "extensions": [
    {
      "requirements": { "capabilities": [{ "name": "Mailbox", "minVersion": "1.13" }] },
      "runtimes": [
        {
          "requirements": { "capabilities": [{ "name": "Mailbox", "minVersion": "1.13" }] },
          "id": "runtime",
          "type": "general",
          "code": {
            "page": "https://spe-api-dev-67e2xz.azurewebsites.net/office/taskpane.html"
          }
        }
      ]
    }
  ]
}
```

**Word Manifest**: `src/client/office-addins/word/word-manifest.xml`

**Changes Needed**:
```xml
<SourceLocation DefaultValue="https://spe-api-dev-67e2xz.azurewebsites.net/office/taskpane.html"/>
<bt:Url id="GetStarted.Url" DefaultValue="https://spe-api-dev-67e2xz.azurewebsites.net/office/taskpane.html"/>
<WebApplicationInfo>
  <Id>{OFFICE_ADDIN_CLIENT_ID}</Id>
  <Resource>api://{BFF_API_CLIENT_ID}</Resource>
  <Scopes>
    <Scope>user_impersonation</Scope>
  </Scopes>
</WebApplicationInfo>
```

**Commit Changes**:
```powershell
git add src/client/office-addins/
git commit -m "chore(office): update manifests with production URLs"
```

---

### Task 4.2: Build and Package Add-ins

**Purpose**: Compile TypeScript and bundle assets

**Steps**:
```powershell
# Navigate to add-in directory
cd src/client/office-addins

# Install dependencies (if not already done)
npm install

# Build Outlook add-in
npm run build:outlook

# Build Word add-in
npm run build:word

# Package manifests for deployment
cd outlook
zip -r ../../outlook-addin.zip manifest.json dist/

cd ../word
zip -r ../../word-addin.zip word-manifest.xml dist/
```

**Output**:
- `src/client/office-addins/outlook-addin.zip`
- `src/client/office-addins/word-addin.zip`

---

### Task 4.3: Deploy Outlook Add-in to M365 Admin Center

**Purpose**: Make Outlook add-in available to organization users

**Steps**:
1. Navigate to [M365 Admin Center](https://admin.microsoft.com/)
2. Go to **Settings** → **Integrated apps**
3. Click **Upload custom apps**
4. Select **Upload manifest file**
5. Upload `outlook-addin.zip`
6. Configure deployment:
   - **Availability**: Select users/groups (e.g., "Spaarke Pilot Users")
   - **Deployment**: Deploy now
7. Click **Deploy**

**Centralized Deployment (Alternative)**:
1. Go to **Exchange admin center** → **Add-ins**
2. Click **Add app** → **Add from file**
3. Upload `manifest.json` from Outlook add-in
4. Configure user assignment
5. Click **Deploy**

**Verify**:
1. Open Outlook Web (https://outlook.office.com)
2. Open any email
3. Click **Apps** button → Verify **Spaarke** add-in appears

**Reference**: Task 057 deployment guide in `projects/sdap-office-integration/tasks/057-deploy-outlook-addin.poml`

---

### Task 4.4: Deploy Word Add-in to M365 Admin Center

**Purpose**: Make Word add-in available to organization users

**Steps**:
1. Navigate to [M365 Admin Center](https://admin.microsoft.com/)
2. Go to **Settings** → **Integrated apps**
3. Click **Upload custom apps**
4. Select **Upload manifest file**
5. Upload `word-addin.zip`
6. Configure deployment:
   - **Availability**: Select users/groups (e.g., "Spaarke Pilot Users")
   - **Deployment**: Deploy now
7. Click **Deploy**

**Verify**:
1. Open Word Online (https://office.com/word)
2. Create a new document
3. Go to **Insert** → **Add-ins** → **My Add-ins**
4. Verify **Spaarke** add-in appears

**Reference**: Task 058 deployment guide in `projects/sdap-office-integration/tasks/058-deploy-word-addin.poml`

---

## Phase 5: Testing and Validation

### Task 5.1: Smoke Tests

**Purpose**: Verify basic functionality in production

**Outlook Add-in**:
- [ ] Add-in loads in Outlook Web
- [ ] NAA authentication succeeds silently
- [ ] "Save to Spaarke" button appears in email view
- [ ] Entity picker loads entities from Dataverse
- [ ] Email + attachments save successfully
- [ ] Job status updates via SSE

**Word Add-in**:
- [ ] Add-in loads in Word Online
- [ ] NAA authentication succeeds silently
- [ ] "Save to Spaarke" button appears in task pane
- [ ] Document uploads successfully
- [ ] Version tracking works (save new version)

**API Endpoints**:
```powershell
# Test recent documents endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/office/recent `
  -H "Authorization: Bearer {access-token}"

# Test entity search endpoint
curl "https://spe-api-dev-67e2xz.azurewebsites.net/office/search/entities?q=Smith&type=contact" `
  -H "Authorization: Bearer {access-token}"

# Test SSE endpoint (job status streaming)
curl https://spe-api-dev-67e2xz.azurewebsites.net/office/jobs/{jobId}/stream `
  -H "Authorization: Bearer {access-token}" `
  -H "Accept: text/event-stream"
```

---

### Task 5.2: Integration Tests

**Purpose**: Run automated E2E tests

**Location**: `tests/integration/OfficeIntegration/`

**Steps**:
```powershell
# Set environment variables
$env:TEST_TENANT_ID = "{your-tenant-id}"
$env:TEST_CLIENT_ID = "{OFFICE_ADDIN_CLIENT_ID}"
$env:TEST_BFF_URL = "https://spe-api-dev-67e2xz.azurewebsites.net"
$env:TEST_DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"

# Run integration tests
cd tests/integration/OfficeIntegration
dotnet test --filter "Category=E2E"
```

**Expected Results**:
- ✅ `OutlookSaveFlowTests.SaveEmailWithAttachments_Success`
- ✅ `WordSaveFlowTests.SaveDocument_Success`
- ✅ `ShareFlowTests.InsertDocumentLink_Success`
- ✅ `SseTests.JobStatusUpdates_RealTime`

**Reference**: Tasks 070-074 test plans in `projects/sdap-office-integration/tasks/`

---

### Task 5.3: Performance Validation

**Purpose**: Ensure API meets SLA requirements

**Metrics to Check**:
| Endpoint | Target | Metric |
|----------|--------|--------|
| `POST /office/save` | < 500ms | p95 latency |
| `GET /office/jobs/{id}` | < 200ms | p95 latency |
| `GET /office/search/entities` | < 300ms | p95 latency |
| SSE connection | < 1s | Time to first event |

**Tools**:
- Azure Application Insights → Performance blade
- Azure Monitor → Metrics
- Custom load tests (if available)

**Reference**: Task 077 performance testing in `projects/sdap-office-integration/tasks/077-performance-testing.poml`

---

## Phase 6: Documentation and Training

### Task 6.1: User Documentation

**Location**: `projects/sdap-office-integration/docs/user/`

**Deliverables**:
- [ ] "Getting Started with Spaarke Office Add-ins" guide
- [ ] "Saving Emails to Spaarke" tutorial
- [ ] "Sharing Documents from Spaarke" tutorial
- [ ] FAQ document

**Publish To**:
- Internal wiki (Confluence/SharePoint)
- In-app help links in task pane

**Reference**: Task 082 documentation in `projects/sdap-office-integration/tasks/082-create-user-documentation.poml`

---

### Task 6.2: Admin Documentation

**Location**: `projects/sdap-office-integration/docs/admin/`

**Deliverables**:
- [ ] Deployment runbook (this file)
- [ ] Troubleshooting guide
- [ ] Monitoring and alerting setup
- [ ] Rollback procedures

**Reference**: Task 083 documentation in `projects/sdap-office-integration/tasks/083-create-admin-documentation.poml`

---

## Phase 7: Monitoring and Alerting

### Task 7.1: Configure Application Insights Alerts

**Purpose**: Proactive monitoring for failures

**Alerts to Create**:
1. **Office Endpoint Failures**:
   - Condition: `requests | where url contains "/office/" and resultCode >= 500`
   - Threshold: 10 failures in 5 minutes
   - Action: Email ops team

2. **SSE Connection Failures**:
   - Condition: `customEvents | where name == "SseConnectionFailed"`
   - Threshold: 5 failures in 5 minutes
   - Action: Email ops team

3. **Background Worker Delays**:
   - Condition: Job processing time > 5 minutes
   - Threshold: 3 jobs in 10 minutes
   - Action: Email ops team

**Steps**:
1. Navigate to Azure Portal → Application Insights → `spe-api-dev-67e2xz`
2. Go to **Alerts** → **Create alert rule**
3. Configure each alert above
4. Set action groups (email/SMS/webhook)

**Reference**: Task 084 monitoring setup in `projects/sdap-office-integration/tasks/084-configure-monitoring.poml`

---

### Task 7.2: Create Dashboard

**Purpose**: Real-time visibility into Office integration health

**Metrics to Display**:
- Office endpoint request rate (req/min)
- Office endpoint error rate (%)
- Average job processing time
- SSE active connections count
- Duplicate detection hit rate (%)

**Steps**:
1. Navigate to Azure Portal → Dashboards
2. Create new dashboard: "SDAP Office Integration"
3. Add Application Insights tiles for metrics above
4. Add Service Bus queue depth tile
5. Add App Service CPU/Memory tiles
6. Save and share with team

---

## Rollback Plan

If deployment fails or critical issues arise:

### Phase 1: Immediate Mitigation
1. Disable Office add-ins in M365 Admin Center
2. Stop affected App Service slots
3. Restore previous configuration

### Phase 2: Rollback Steps
```powershell
# Revert App Service deployment to previous version
az webapp deployment slot swap `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --slot staging `
  --target-slot production

# Remove Dataverse solution (if needed)
pac solution delete `
  --solution-name OfficeIntegration
```

### Phase 3: Communication
1. Notify users via email/Teams
2. Document issue in incident log
3. Schedule post-mortem

---

## Checklist

Use this checklist to track deployment progress:

### Azure AD
- [ ] Task 1.1: Office Add-in app registration created
- [ ] Task 1.2: BFF API app registration updated for OBO
- [ ] Task 1.3: Secrets stored in Key Vault

### Dataverse
- [ ] Task 2.1: Solution deployed to Dataverse
- [ ] Task 2.2: Security roles configured

### BFF API
- [ ] Task 3.1: App Service configuration updated
- [ ] Task 3.2: BFF API code deployed
- [ ] Task 3.3: Background workers verified

### Office Add-ins
- [ ] Task 4.1: Manifests updated with production URLs
- [ ] Task 4.2: Add-ins built and packaged
- [ ] Task 4.3: Outlook add-in deployed to M365
- [ ] Task 4.4: Word add-in deployed to M365

### Testing
- [ ] Task 5.1: Smoke tests passed
- [ ] Task 5.2: Integration tests passed
- [ ] Task 5.3: Performance validation passed

### Documentation
- [ ] Task 6.1: User documentation published
- [ ] Task 6.2: Admin documentation published

### Monitoring
- [ ] Task 7.1: Application Insights alerts configured
- [ ] Task 7.2: Dashboard created

---

## Support

**For deployment issues**:
- Slack: #spaarke-devops
- Email: devops@spaarke.com

**For add-in issues**:
- Slack: #spaarke-office-integration
- Email: support@spaarke.com

---

**Last Updated**: 2026-01-21
**Document Owner**: DevOps Team
**Review Frequency**: After each deployment
