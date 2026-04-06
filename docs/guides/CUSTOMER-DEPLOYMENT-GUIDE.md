# Spaarke Document Intelligence - Customer Deployment Guide

> **Version**: 2.1
> **Date**: 2026-04-05
> **Audience**: IT Administrators and Technical Staff

---

## Quick Start Checklist

Before you begin, ensure you have:

- [ ] Azure subscription with Contributor access
- [ ] Power Platform environment with System Administrator access
- [ ] Azure CLI installed (version 2.50+)
- [ ] Power Platform CLI installed (latest version)
- [ ] Customer's Azure AD Tenant ID
- [ ] 45-90 minutes for initial deployment

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture & Configuration Strategy](#architecture--configuration-strategy)
3. [Build Once, Deploy Anywhere](#build-once-deploy-anywhere)
4. [Prerequisites](#prerequisites)
5. [Deployment Steps](#deployment-steps)
6. [Dataverse Environment Variables](#dataverse-environment-variables)
7. [Validate Deployed Environment](#validate-deployed-environment)
8. [Configuration](#configuration)
9. [Verification](#verification)
10. [Troubleshooting](#troubleshooting)
11. [Maintenance](#maintenance)
12. [Appendix](#appendix)

---

## Overview

Spaarke Document Intelligence enables intelligent document analysis, management, and search within Dynamics 365 environments. It provides:

- **AI-Powered Analysis**: Automated document summarization, entity extraction, and classification
- **Contract Review**: Clause analysis with risk identification
- **RAG Search**: Semantic search across document collections with vector embeddings
- **Office Add-ins**: Save emails and Word documents directly from Office apps
- **Export Capabilities**: Export analysis results to Word, PDF, or email

### System Components

| Component | Purpose | Deployment Model |
|-----------|---------|------------------|
| **Azure AI Services** | Document processing, embeddings, and analysis | Dedicated per customer |
| **BFF API** | Backend service connecting all components | Dedicated App Service per customer |
| **Power Platform Solution** | Dataverse entities and UI components | Customer's Power Platform environment |
| **Office Add-ins** | Outlook and Word integration | Deployed to customer's Microsoft 365 |
| **Service Bus** | Async job processing (AI profiling, RAG indexing) | Dedicated per customer |

### Estimated Deployment Time

| Step | Time |
|------|------|
| Azure Infrastructure (Bicep) | 20-30 minutes |
| App Service Configuration | 15-20 minutes |
| Power Platform Solution | 10-15 minutes |
| Office Add-ins (optional) | 10-15 minutes |
| Configuration & Verification | 15-20 minutes |
| **Total** | **70-100 minutes** |

---

## Architecture & Configuration Strategy

### Deployment Model: Dedicated Infrastructure Per Customer

**Each customer receives:**
- **Separate Azure App Service** with customer-specific configuration
- **Separate Azure AI resources** (OpenAI, Document Intelligence, AI Search, Service Bus)
- **Separate Dataverse environment**
- **Isolated data** (no multi-tenancy in the application code)

**Why this model?**
- ✅ Complete security and compliance isolation
- ✅ Independent scaling per customer
- ✅ Simplified code (no runtime tenant resolution)
- ✅ Customer-specific Azure quotas and limits

### Configuration Architecture: Options Pattern + Placeholders

Spaarke uses **placeholder-based configuration** to support customer-specific deployments:

```
appsettings.template.json (placeholders: #{TENANT_ID}#)
         ↓
Azure App Service Configuration (customer-specific values)
         ↓
Azure Key Vault (secrets: connection strings, API keys)
         ↓
IOptions<T> injection (runtime configuration)
```

**Key Principles:**
1. **No hardcoded values** in source code
2. **Placeholders** in template files (e.g., `#{TENANT_ID}#`)
3. **Customer values** injected via Azure App Service Settings
4. **Secrets** stored in Azure Key Vault
5. **Fail-fast validation** on startup (invalid configuration prevents app start)

### Configuration Classes

All customer-specific settings are defined as strongly-typed options classes:

| Options Class | Purpose | Customer-Specific Values |
|---------------|---------|-------------------------|
| `GraphOptions` | Microsoft Graph API | TenantId, ClientId, ClientSecret |
| `DataverseOptions` | Dataverse API | TenantId, ServiceUrl, ClientId, ClientSecret |
| `AzureOpenAiOptions` | Azure OpenAI | Endpoint, ApiKey, DeploymentNames |
| `DocumentIntelligenceOptions` | Document Intelligence | Endpoint, ApiKey |
| `AiSearchOptions` | AI Search | Endpoint, AdminKey, IndexName |
| `ServiceBusOptions` | Service Bus | ConnectionString |
| `AnalysisOptions` | AI Analysis | CustomerTenantId, PromptFlowEndpoint |

---

## Build Once, Deploy Anywhere

Spaarke uses an **environment-agnostic build** strategy. All client-side components — PCF controls, Code Pages, legacy JS webresources, and Office Add-ins — resolve their configuration at **runtime** from Dataverse Environment Variables. No environment-specific values are baked into any build artifact.

**What this means for you:**

- The same solution ZIP file (`Spaarke_DocumentIntelligence_managed.zip`) deploys to dev, UAT, staging, and production without modification.
- After importing the solution, you set 7 Dataverse Environment Variables specific to your environment (see [Dataverse Environment Variables](#dataverse-environment-variables) below).
- The `Provision-Customer.ps1` script sets all 7 variables automatically during onboarding.
- After any configuration change, run `Validate-DeployedEnvironment.ps1` to confirm the environment is correctly configured (see [Validate Deployed Environment](#validate-deployed-environment) below).

**How runtime resolution works:**

At startup, each client component calls `resolveRuntimeConfig()` (from the `@spaarke/auth` shared library), which:
1. Queries the Dataverse Web API to read all `sprk_*` Environment Variables
2. Caches the result in memory for the session lifetime
3. Throws a clear error if any required variable is missing (no silent dev fallbacks)

---

## Prerequisites

### 1. Required Permissions

| Resource | Required Access |
|----------|----------------|
| Azure Subscription | Contributor role or higher |
| Power Platform | System Administrator security role |
| Azure AD | Application Administrator (for app registrations) |

### 2. Required Information

Collect these values before starting deployment:

| Information | Example | Where to Find |
|-------------|---------|---------------|
| **Customer Tenant ID** | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Azure Portal > Azure Active Directory > Overview |
| **Customer Tenant Name** | `contoso.onmicrosoft.com` | Azure Portal > Azure Active Directory > Overview |
| **Dataverse Org Name** | `contoso` | Power Platform Admin Center > Environments |
| **Dataverse URL** | `https://contoso.crm.dynamics.com` | Power Platform Admin Center > Environments |
| **Azure Region** | `eastus` or `westus2` | Customer preference (same as Dataverse region) |

### 3. Required Tools

Install these tools before starting:

#### Azure CLI

```bash
# Windows (PowerShell as Administrator)
winget install Microsoft.AzureCLI

# Verify installation
az --version
# Expected: version 2.50 or higher
```

#### Power Platform CLI

```bash
# Windows (PowerShell as Administrator)
winget install Microsoft.PowerAppsCLI

# Verify installation
pac --version
```

### 4. Verify Access

Before proceeding, verify your access:

```bash
# Login to Azure
az login

# Set correct subscription
az account set --subscription "YOUR-SUBSCRIPTION-NAME"

# Verify subscription access
az account show

# Login to Power Platform
pac auth create --url https://YOUR-ORG.crm.dynamics.com
```

---

## Deployment Steps

### Step 1: Create Azure AD App Registrations

Create two app registrations (one for API, one for Dataverse client):

#### 1.1 Create BFF API App Registration

```bash
# Create app registration
az ad app create \
  --display-name "Spaarke API - CUSTOMER-NAME" \
  --sign-in-audience AzureADMyOrg

# Note the Application (client) ID from output
# Save as: API_APP_ID
```

#### 1.2 Configure API Permissions

In Azure Portal:
1. Navigate to **Azure Active Directory** > **App registrations** > Select your API app
2. Go to **API permissions** > **Add a permission**
3. Add these Microsoft Graph permissions (Application):
   - `Files.Read.All`
   - `Sites.Read.All`
   - `User.Read.All`
4. Click **Grant admin consent**

#### 1.3 Create Client Secret

```bash
az ad app credential reset \
  --id <API_APP_ID> \
  --append \
  --display-name "BFF API Secret"

# Note the 'password' value from output
# Save as: API_CLIENT_SECRET (store securely!)
```

#### 1.4 Create Dataverse Client App Registration

Repeat the process for Dataverse client:

```bash
az ad app create \
  --display-name "Spaarke Dataverse Client - CUSTOMER-NAME" \
  --sign-in-audience AzureADMyOrg

# Note the Application (client) ID
# Save as: DATAVERSE_CLIENT_ID
```

Add Dataverse permissions:
- Navigate to **API permissions** > **Add a permission** > **Dynamics CRM**
- Select **user_impersonation** (Delegated)
- Grant admin consent

---

### Step 2: Deploy Azure Infrastructure

#### 2.1 Create Resource Group

```bash
# Choose naming pattern: rg-spaarke-{customer}-{region}
az group create \
  --name rg-spaarke-contoso-eastus \
  --location eastus
```

#### 2.2 Deploy Infrastructure via Bicep

Contact your Spaarke representative to receive the deployment template (`spaarke-infrastructure.bicep`), then run:

```bash
az deployment group create \
  --resource-group rg-spaarke-contoso-eastus \
  --template-file spaarke-infrastructure.bicep \
  --parameters \
    customerId=contoso \
    environment=prod \
    location=eastus \
    tenantId=<CUSTOMER_TENANT_ID>
```

This deploys:
- Azure OpenAI Service
- Document Intelligence
- AI Search
- Service Bus (with queues: `office-upload-finalization`, `office-profile`, `office-indexing`)
- Storage Account (for SharePoint Embedded)
- App Service + App Service Plan
- Application Insights
- Azure Key Vault

#### 2.3 Note Deployed Resource Names

After deployment completes, note these resource names:

```bash
# List all resources in the resource group
az resource list --resource-group rg-spaarke-contoso-eastus --output table

# Save these names for configuration:
# - Azure OpenAI name (e.g., spaarke-openai-prod)
# - AI Search name (e.g., spaarke-search-prod)
# - App Service name (e.g., spe-api-prod-abc123)
# - Key Vault name (e.g., spaarke-kv-prod)
# - Service Bus namespace (e.g., spaarke-servicebus-prod)
```

---

### Step 3: Configure Azure Key Vault

Store secrets in Key Vault (never in App Service settings directly):

#### 3.1 Store API Client Secret

```bash
az keyvault secret set \
  --vault-name spaarke-kv-prod \
  --name BFF-API-ClientSecret \
  --value "<API_CLIENT_SECRET>"
```

#### 3.2 Store Dataverse Client Secret

```bash
az keyvault secret set \
  --vault-name spaarke-kv-prod \
  --name Dataverse-ClientSecret \
  --value "<DATAVERSE_CLIENT_SECRET>"
```

#### 3.3 Store Service Bus Connection String

```bash
# Get Service Bus connection string
az servicebus namespace authorization-rule keys list \
  --resource-group rg-spaarke-contoso-eastus \
  --namespace-name spaarke-servicebus-prod \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv

# Store in Key Vault
az keyvault secret set \
  --vault-name spaarke-kv-prod \
  --name ServiceBus-ConnectionString \
  --value "<CONNECTION_STRING>"
```

#### 3.4 Store Azure OpenAI Key

```bash
# Get Azure OpenAI key
az cognitiveservices account keys list \
  --resource-group rg-spaarke-contoso-eastus \
  --name spaarke-openai-prod \
  --query key1 -o tsv

# Store in Key Vault
az keyvault secret set \
  --vault-name spaarke-kv-prod \
  --name AzureOpenAi-ApiKey \
  --value "<OPENAI_KEY>"
```

---

### Step 4: Configure App Service Settings

Configure the App Service with customer-specific values:

#### 4.1 Set Application Settings

```bash
# Replace placeholders with actual customer values
az webapp config appsettings set \
  --name spe-api-prod-abc123 \
  --resource-group rg-spaarke-contoso-eastus \
  --settings \
    TENANT_ID="<CUSTOMER_TENANT_ID>" \
    API_APP_ID="<API_APP_ID>" \
    Graph__TenantId="<CUSTOMER_TENANT_ID>" \
    Graph__ClientId="<API_APP_ID>" \
    Graph__ClientSecret="@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/BFF-API-ClientSecret)" \
    Dataverse__TenantId="<CUSTOMER_TENANT_ID>" \
    Dataverse__ServiceUrl="https://contoso.crm.dynamics.com" \
    Dataverse__ClientId="<DATAVERSE_CLIENT_ID>" \
    Dataverse__ClientSecret="@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/Dataverse-ClientSecret)" \
    ServiceBus__ConnectionString="@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/ServiceBus-ConnectionString)" \
    AzureOpenAi__Endpoint="https://spaarke-openai-prod.openai.azure.com/" \
    AzureOpenAi__ApiKey="@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/AzureOpenAi-ApiKey)" \
    Analysis__CustomerTenantId="<CUSTOMER_TENANT_ID>"
```

#### 4.2 Enable Key Vault Access

Enable managed identity and grant Key Vault access:

```bash
# Enable system-assigned managed identity
az webapp identity assign \
  --name spe-api-prod-abc123 \
  --resource-group rg-spaarke-contoso-eastus

# Get the principal ID
PRINCIPAL_ID=$(az webapp identity show \
  --name spe-api-prod-abc123 \
  --resource-group rg-spaarke-contoso-eastus \
  --query principalId -o tsv)

# Grant Key Vault access
az keyvault set-policy \
  --name spaarke-kv-prod \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

---

### Step 5: Deploy BFF API

Contact your Spaarke representative to receive the API deployment package, then deploy:

#### 5.1 Deploy via Azure CLI

```bash
# Using the provided deployment package
az webapp deployment source config-zip \
  --resource-group rg-spaarke-contoso-eastus \
  --name spe-api-prod-abc123 \
  --src Sprk.Bff.Api.zip
```

#### 5.2 Verify API Health

```bash
# Wait 30-60 seconds for app to start, then check health
curl https://spe-api-prod-abc123.azurewebsites.net/healthz
# Expected response: "Healthy"

curl https://spe-api-prod-abc123.azurewebsites.net/ping
# Expected response: "pong"
```

If the API fails to start, check logs:

```bash
az webapp log tail \
  --name spe-api-prod-abc123 \
  --resource-group rg-spaarke-contoso-eastus
```

**Common startup failures:**
- ❌ **Invalid configuration** → Check App Service settings match required format
- ❌ **Key Vault access denied** → Verify managed identity has Key Vault permissions
- ❌ **Missing secrets** → Verify all secrets exist in Key Vault

---

### Step 6: Deploy Power Platform Solution

#### 6.1 Authenticate to Power Platform

```bash
pac auth create --url https://contoso.crm.dynamics.com
```

#### 6.2 Import the Solution

```bash
pac solution import \
  --path Spaarke_DocumentIntelligence_managed.zip \
  --activate-plugins \
  --publish-changes
```

#### 6.3 Verify Import

```bash
pac solution list
```

You should see `Spaarke Document Intelligence` in the list with status "Installed".

---

### Step 7: Set Dataverse Environment Variables

After solution import, set the 7 required Dataverse Environment Variables. See the full [Dataverse Environment Variables](#dataverse-environment-variables) section for details on each variable.

#### 7.1 Automated (Recommended) — Provision-Customer.ps1

If using the automated provisioning script, environment variables are set automatically in Step 8 of the pipeline:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "contoso" `
    -DisplayName "Contoso Legal" `
    -TenantId "<CUSTOMER_TENANT_ID>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

#### 7.2 Manual — Power Platform Admin Center (UI)

1. Open **Power Platform Admin Center** (https://admin.powerplatform.microsoft.com)
2. Navigate to **Environments** > Select your environment
3. Open **Solutions** > **Spaarke Document Intelligence**
4. Click **Environment Variables**
5. Set all 7 variables per the [Dataverse Environment Variables](#dataverse-environment-variables) table below

#### 7.3 Manual — Power Platform CLI

```powershell
pac env var set --name sprk_BffApiBaseUrl --value "https://spe-api-prod-abc123.azurewebsites.net/api"
pac env var set --name sprk_BffApiAppId --value "api://bff-api-prod-app-id"
pac env var set --name sprk_MsalClientId --value "<MSAL_CLIENT_ID>"
pac env var set --name sprk_TenantId --value "<CUSTOMER_TENANT_ID>"
pac env var set --name sprk_AzureOpenAiEndpoint --value "https://spaarke-openai-prod.openai.azure.com/"
pac env var set --name sprk_ShareLinkBaseUrl --value "https://app.spaarke.com/share"
pac env var set --name sprk_SharePointEmbeddedContainerId --value "<SPE_CONTAINER_ID>"
```

After setting variables (manually or automatically), run the validation script (see [Validate Deployed Environment](#validate-deployed-environment)) to confirm all variables are correctly configured.

---

### Step 8: Deploy Office Add-ins (Optional)

If customer requires Office integration:

#### 8.1 Deploy to Microsoft 365 Admin Center

1. Sign in to **Microsoft 365 Admin Center** (https://admin.microsoft.com)
2. Navigate to **Settings** > **Integrated apps**
3. Click **Upload custom apps**
4. Upload the provided manifest files:
   - `outlook-addin-manifest.xml`
   - `word-addin-manifest.xml`
5. Assign to users or groups

#### 8.2 Verify Add-in Deployment

Users should see:
- **Outlook Web App**: "Save to Spaarke" button in email ribbon
- **Word Online**: "Save to Spaarke" button in Home ribbon

---

## Dataverse Environment Variables

These 7 variables are defined as part of the Spaarke Dataverse solution (in the solution XML). After solution import, their **values** must be set per environment. The `Provision-Customer.ps1` script sets them automatically; for manual deployments, use Step 7.2 or 7.3 above.

| Variable | Schema Name | Purpose | Example Value |
|----------|-------------|---------|---------------|
| BFF API Base URL | `sprk_BffApiBaseUrl` | Base URL for all BFF API calls from client components | `https://spe-api-prod-abc123.azurewebsites.net/api` |
| BFF API App ID | `sprk_BffApiAppId` | Azure AD App Registration ID for BFF API (used as OAuth scope audience) | `api://bff-api-prod-app-id` |
| MSAL Client ID | `sprk_MsalClientId` | MSAL Client ID for Dataverse-hosted SPAs (Code Pages, External SPA) | `12345678-1234-1234-1234-123456789012` |
| Tenant ID | `sprk_TenantId` | Azure AD Tenant ID for authentication | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Azure OpenAI Endpoint | `sprk_AzureOpenAiEndpoint` | Azure OpenAI service endpoint for AI features | `https://spaarke-openai-prod.openai.azure.com/` |
| Share Link Base URL | `sprk_ShareLinkBaseUrl` | Base URL for generating shareable document links | `https://app.spaarke.com/share` |
| SPE Container ID | `sprk_SharePointEmbeddedContainerId` | SharePoint Embedded container ID for document storage | `b1c2d3e4-f5a6-7890-abcd-ef1234567890` |

> **All 7 variables are required.** Client components will throw a clear configuration error at startup if any variable is missing or empty. There are no silent fallbacks to dev values.

### Where Values Come From

| Variable | Value Source |
|----------|-------------|
| `sprk_BffApiBaseUrl` | App Service URL from Azure deployment (`/api` suffix required) |
| `sprk_BffApiAppId` | App Registration Application ID (or URI like `api://<guid>`) |
| `sprk_MsalClientId` | App Registration Application ID for the Dataverse-hosted SPA |
| `sprk_TenantId` | Customer's Azure AD Tenant ID |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI resource endpoint from Azure Portal |
| `sprk_ShareLinkBaseUrl` | Base URL for your deployed external SPA or share endpoint |
| `sprk_SharePointEmbeddedContainerId` | SPE container ID from `Provision-Customer.ps1` output or Azure Portal |

### Canonical Configuration Reference

`Provision-Customer.ps1` writes an `environment-config.json` file to the customer's configuration folder after successful provisioning. This file is the **canonical reference** for all customer-specific configuration values:

```json
{
  "customerId": "contoso",
  "dataverseUrl": "https://contoso.crm.dynamics.com",
  "bffApiUrl": "https://spe-api-prod-abc123.azurewebsites.net/api",
  "bffApiAppId": "api://bff-api-prod-app-id",
  "msalClientId": "12345678-1234-1234-1234-123456789012",
  "tenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "azureOpenAiEndpoint": "https://spaarke-openai-prod.openai.azure.com/",
  "shareLinkBaseUrl": "https://app.spaarke.com/share",
  "speContainerId": "b1c2d3e4-f5a6-7890-abcd-ef1234567890"
}
```

---

## Validate Deployed Environment

After any deployment or configuration change, run the validation script to confirm the environment is correctly configured:

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://contoso.crm.dynamics.com"
```

Or specify the BFF API URL explicitly:

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 `
    -DataverseUrl "https://contoso.crm.dynamics.com" `
    -BffApiUrl "https://spe-api-prod-abc123.azurewebsites.net/api"
```

**Location**: `scripts/Validate-DeployedEnvironment.ps1`

**What it checks (4 categories):**

| Category | Checks Performed | Pass Criteria |
|----------|-----------------|---------------|
| **Env Vars** | All 7 Dataverse Environment Variables exist and have non-empty values | All 7 variables present with values |
| **BFF API** | `GET /healthz` and `GET /ping` return HTTP 200 | Both endpoints healthy |
| **CORS** | Preflight OPTIONS request includes Dataverse origin in `Access-Control-Allow-Origin` | Dataverse URL allowed |
| **Dev Leakage** | Scans env var values for dev-only identifiers (known dev URLs, GUIDs, hostnames) | No dev values detected |

**Expected output (all checks passing):**

```
====================================================================
  RESULTS SUMMARY
====================================================================

  [PASS] Env Vars            Pass: 7  Fail: 0  Warn: 0
  [PASS] BFF API             Pass: 2  Fail: 0  Warn: 0
  [PASS] CORS                Pass: 1  Fail: 0  Warn: 0
  [PASS] Dev Leakage         Pass: 6  Fail: 0  Warn: 0

  Total:  16 checks
  Pass:   16

  VERDICT: PASSED - All checks successful. Environment is correctly configured.
```

**When to run:**
- After initial solution import and environment variable configuration
- After solution upgrades
- After manual environment variable changes
- Whenever verifying production readiness before go-live

---

## Configuration

### Security Role Assignment

Assign users to the appropriate security roles:

| Role | Who Should Have It | Permissions |
|------|-------------------|-------------|
| **Spaarke AI Analysis User** | All users who need to run analyses | Read documents, create analyses, view results |
| **Spaarke AI Analysis Admin** | Administrators who manage playbooks | All user permissions + manage playbooks, configure settings |
| **Spaarke Office User** | Users saving from Office apps | Create documents via Office add-ins |

To assign roles:
1. Open **Power Platform Admin Center**
2. Navigate to **Environments** > Select your environment
3. Click **Settings** > **Users + permissions** > **Users**
4. Select users and click **Manage security roles**
5. Assign **Spaarke** roles

---

## Verification

Follow these steps to verify successful deployment:

### Test 1: API Health Check

```bash
# Check API is running
curl https://spe-api-prod-abc123.azurewebsites.net/healthz
# Expected: "Healthy"

# Check ping endpoint
curl https://spe-api-prod-abc123.azurewebsites.net/ping
# Expected: "pong"
```

**Pass Criteria**: Both endpoints return expected responses

### Test 2: Configuration Validation

The API validates configuration on startup. Check Application Insights or App Service logs:

```bash
az webapp log tail \
  --name spe-api-prod-abc123 \
  --resource-group rg-spaarke-contoso-eastus
```

**Pass Criteria**: No configuration errors in logs (look for "Application started successfully")

### Test 3: Access Dynamics 365

1. Open your Dynamics 365 environment (https://contoso.crm.dynamics.com)
2. Navigate to **Documents** area
3. Open a Document record

**Pass Criteria**: Document form loads without errors

### Test 4: Create Document Analysis

1. Open a Document record with an attached file
2. Click **+ New Analysis** button in the ribbon
3. The Analysis Builder dialog should open

**Pass Criteria**: Analysis Builder dialog opens and loads configuration options

### Test 5: Run Analysis End-to-End

1. In Analysis Builder, select analysis options
2. Click **Execute Analysis**
3. Wait for analysis to complete (typically 30-60 seconds)

**Pass Criteria**:
- ✅ Analysis completes without errors
- ✅ Results display in Analysis Workspace
- ✅ Document metadata populated (summary, keywords)

### Test 6: Test Office Add-in (if deployed)

#### Outlook Test

1. Open Outlook Web App (https://outlook.office.com)
2. Open an email
3. Click **Save to Spaarke** button
4. Select destination and options
5. Click **Save**

**Pass Criteria**:
- ✅ Email saves successfully
- ✅ Document created in Dataverse
- ✅ Email body and attachments stored

#### Word Test

1. Open Word Online
2. Create or open a document
3. Click **Save to Spaarke** button
4. Select destination and options
5. Click **Save**

**Pass Criteria**:
- ✅ Word document saves successfully
- ✅ Document created in Dataverse
- ✅ DOCX file stored in SharePoint Embedded

### Test 7: Verify Background Workers

Check that async workers are processing jobs:

```bash
# Check Service Bus queue message counts
az servicebus queue show \
  --resource-group rg-spaarke-contoso-eastus \
  --namespace-name spaarke-servicebus-prod \
  --name office-upload-finalization \
  --query "countDetails"

# Expected: activeMessageCount should be 0 (messages processed)
# Expected: deadLetterMessageCount should be 0 (no failures)
```

**Pass Criteria**: Messages are being processed (not accumulating in queues)

---

## Troubleshooting

### Common Issues

#### Issue: API returns 500 on startup

**Symptom**: Health check fails with 500 Internal Server Error

**Solution**:
1. Check App Service logs for configuration errors:
   ```bash
   az webapp log tail --name spe-api-prod-abc123 --resource-group rg-spaarke-contoso-eastus
   ```
2. Common causes:
   - Missing App Service setting (e.g., `Graph__TenantId`)
   - Invalid Key Vault reference format
   - Managed identity not granted Key Vault access
3. Verify all required settings are present:
   ```bash
   az webapp config appsettings list --name spe-api-prod-abc123 --resource-group rg-spaarke-contoso-eastus
   ```

#### Issue: API returns 401 Unauthorized

**Symptom**: API calls from Power Platform fail with 401 error

**Solution**:
1. Verify the Azure AD app registration is configured correctly
2. Check that the user has the appropriate security role in Dataverse
3. Ensure authentication token is being passed in requests
4. Check CORS settings allow Dataverse origin:
   ```bash
   az webapp cors show --name spe-api-prod-abc123 --resource-group rg-spaarke-contoso-eastus
   ```

#### Issue: Analysis Builder won't open

**Symptom**: Clicking "New Analysis" does nothing

**Solution**:
1. Ensure the document record is saved (not a new unsaved record)
2. Clear browser cache (Ctrl+Shift+R)
3. Check browser console for errors (F12 > Console)
4. Verify environment variable `sprk_BffApiBaseUrl` is set correctly
5. Test API connectivity from browser:
   ```
   https://spe-api-prod-abc123.azurewebsites.net/healthz
   ```

#### Issue: Office Add-in not appearing

**Symptom**: "Save to Spaarke" button not visible in Office apps

**Solution**:
1. Verify add-in is deployed in Microsoft 365 Admin Center
2. Check add-in is assigned to the user
3. Clear Office cache:
   - Outlook Web App: Clear browser cache
   - Word Online: Clear browser cache
4. Check add-in manifest URL is accessible
5. Verify user has Office 365 license

#### Issue: Email saves but no AI summary generated

**Symptom**: Email saves successfully but Document fields (summary, keywords) remain empty

**Solution**:
1. Check if AI processing is enabled in save options
2. Verify Azure OpenAI endpoint is accessible from App Service
3. Check Service Bus messages are being processed:
   ```bash
   az servicebus queue show --name office-profile --namespace-name spaarke-servicebus-prod --query "countDetails"
   ```
4. Check App Service logs for worker errors:
   ```bash
   az webapp log tail --name spe-api-prod-abc123 --resource-group rg-spaarke-contoso-eastus | grep "ProfileSummaryWorker"
   ```
5. Verify `Analysis__CustomerTenantId` setting matches customer tenant ID

#### Issue: Documents not indexed in Azure AI Search

**Symptom**: Search doesn't return saved documents

**Solution**:
1. Verify RAG indexing is enabled in save options
2. Check indexing queue is processing:
   ```bash
   az servicebus queue show --name office-indexing --namespace-name spaarke-servicebus-prod --query "countDetails"
   ```
3. Check App Service logs for indexing worker errors:
   ```bash
   az webapp log tail --name spe-api-prod-abc123 --resource-group rg-spaarke-contoso-eastus | grep "IndexingWorker"
   ```
4. Verify AI Search index exists:
   ```bash
   az search index show --service-name spaarke-search-prod --name spaarke-search-index
   ```

### Checking Logs

#### Azure API Logs (Real-time)

```bash
# Stream live logs
az webapp log tail \
  --name spe-api-prod-abc123 \
  --resource-group rg-spaarke-contoso-eastus
```

#### Azure API Logs (Application Insights)

1. Open Azure Portal
2. Navigate to Application Insights resource
3. Click **Logs** under Monitoring
4. Query recent errors:
   ```kusto
   traces
   | where timestamp > ago(1h)
   | where severityLevel >= 3
   | order by timestamp desc
   ```

#### Power Platform Logs

1. Open Power Platform Admin Center
2. Navigate to **Environments** > Select environment
3. Click **Analytics** > **Dataverse analytics**

### Getting Help

If issues persist after troubleshooting:

1. **Collect Diagnostic Information**:
   - Screenshot of the error
   - Steps to reproduce
   - Browser console output (F12 > Console)
   - API logs from Application Insights
   - Service Bus queue status

2. **Contact Spaarke Support**:
   - Email: support@spaarke.com
   - Include collected diagnostic information
   - Reference customer deployment ID

---

## Post-Deployment Checklist

Use this checklist to confirm everything is working:

- [ ] Azure infrastructure deployed successfully
- [ ] All secrets stored in Key Vault
- [ ] App Service configured with customer-specific settings
- [ ] API health check passes (`/healthz` returns "Healthy")
- [ ] Power Platform solution imported
- [ ] All 7 Dataverse Environment Variables configured (see [Dataverse Environment Variables](#dataverse-environment-variables))
- [ ] `Validate-DeployedEnvironment.ps1` passes all 16 checks (see [Validate Deployed Environment](#validate-deployed-environment))
- [ ] `environment-config.json` generated and saved as canonical config reference
- [ ] Security roles assigned to users
- [ ] Test document analysis completed successfully
- [ ] Office add-ins deployed (if applicable)
- [ ] Email save tested (if Office add-ins deployed)
- [ ] AI summary generation verified
- [ ] RAG indexing verified (documents appear in search)
- [ ] Background workers processing messages
- [ ] Users trained on basic operations

---

## Maintenance

### Regular Monitoring

| Check | Frequency | How |
|-------|-----------|-----|
| API Health | Daily | `curl https://YOUR-API/healthz` (automate with monitoring tool) |
| Service Bus Queues | Daily | Check for message buildup or dead letters |
| Error Logs | Weekly | Azure Portal > Application Insights > Failures |
| Usage Metrics | Monthly | Application Insights dashboard |
| Key Vault Access | Monthly | Verify managed identity permissions |

### Monitoring Queries (Application Insights)

#### Check API Availability

```kusto
requests
| where timestamp > ago(24h)
| summarize
    TotalRequests = count(),
    FailedRequests = countif(success == false),
    AvgDuration = avg(duration)
| project
    TotalRequests,
    FailedRequests,
    SuccessRate = (TotalRequests - FailedRequests) * 100.0 / TotalRequests,
    AvgDuration
```

#### Check Worker Errors

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Worker" or message contains "ProfileSummary" or message contains "Indexing"
| where severityLevel >= 3
| summarize count() by message
| order by count_ desc
```

### Updates

Spaarke provides updates via:
1. **Managed solution packages** (for Power Platform)
2. **API deployment packages** (for Azure App Service)

#### Updating the BFF API

```bash
# Deploy new version
az webapp deployment source config-zip \
  --resource-group rg-spaarke-contoso-eastus \
  --name spe-api-prod-abc123 \
  --src Sprk.Bff.Api-v2.0.zip

# Verify health after deployment
curl https://spe-api-prod-abc123.azurewebsites.net/healthz
```

#### Updating Power Platform Solution

```bash
pac solution import \
  --path Spaarke_DocumentIntelligence_managed_v2.0.zip \
  --activate-plugins \
  --publish-changes \
  --upgrade
```

---

## Appendix

### A. Complete Configuration Reference

#### App Service Settings (Required)

| Setting Name | Example Value | Description |
|-------------|---------------|-------------|
| `TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Customer's Azure AD tenant ID |
| `API_APP_ID` | `12345678-1234-1234-1234-123456789012` | API app registration client ID |
| `Graph__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Customer's tenant ID (for Graph API) |
| `Graph__ClientId` | `12345678-1234-1234-1234-123456789012` | API app registration client ID |
| `Graph__ClientSecret` | `@Microsoft.KeyVault(SecretUri=...)` | Key Vault reference to client secret |
| `Dataverse__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Customer's tenant ID |
| `Dataverse__ServiceUrl` | `https://contoso.crm.dynamics.com` | Dataverse environment URL |
| `Dataverse__ClientId` | `87654321-4321-4321-4321-210987654321` | Dataverse client app ID |
| `Dataverse__ClientSecret` | `@Microsoft.KeyVault(SecretUri=...)` | Key Vault reference to Dataverse secret |
| `ServiceBus__ConnectionString` | `@Microsoft.KeyVault(SecretUri=...)` | Key Vault reference to Service Bus connection |
| `AzureOpenAi__Endpoint` | `https://spaarke-openai-prod.openai.azure.com/` | Azure OpenAI endpoint |
| `AzureOpenAi__ApiKey` | `@Microsoft.KeyVault(SecretUri=...)` | Key Vault reference to OpenAI key |
| `Analysis__CustomerTenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Customer's tenant ID (for AI analysis) |

#### Key Vault Secrets (Required)

| Secret Name | Description |
|------------|-------------|
| `BFF-API-ClientSecret` | API app registration client secret |
| `Dataverse-ClientSecret` | Dataverse client app secret |
| `ServiceBus-ConnectionString` | Service Bus connection string |
| `AzureOpenAi-ApiKey` | Azure OpenAI API key |
| `Redis-ConnectionString` | Redis cache connection string (if using Redis) |
| `AppInsights-ConnectionString` | Application Insights connection string |

### B. Azure Resources Deployed

| Resource Type | Name Pattern | Purpose |
|---------------|--------------|---------|
| Resource Group | `rg-spaarke-{customer}-{region}` | Container for all resources |
| Azure OpenAI | `spaarke-openai-{env}` | AI model hosting (GPT-4, embeddings) |
| Document Intelligence | `spaarke-docintel-{env}` | Document OCR and analysis |
| AI Search | `spaarke-search-{env}` | Vector search and RAG indexing |
| Service Bus | `spaarke-servicebus-{env}` | Async job queue (3 queues) |
| App Service Plan | `spaarke-asp-{env}` | Hosting plan for App Service |
| App Service | `spe-api-{env}-{suffix}` | BFF API hosting |
| Storage Account | `spaarkestr{env}{suffix}` | SharePoint Embedded file storage |
| Key Vault | `spaarke-kv-{env}` | Secret storage |
| Application Insights | `spaarke-appinsights-{env}` | Monitoring and diagnostics |

### C. Service Bus Queues

| Queue Name | Purpose | Consumer |
|-----------|---------|----------|
| `office-upload-finalization` | Finalize file uploads, create Dataverse records | UploadFinalizationWorker |
| `office-profile` | Generate AI document profile (summary, keywords) | ProfileSummaryWorker |
| `office-indexing` | Index documents in Azure AI Search for RAG | IndexingWorkerHostedService |

### D. Dataverse Environment Variables (Complete Reference)

All 7 variables are required. See the [Dataverse Environment Variables](#dataverse-environment-variables) section for full descriptions.

| Variable Name | Example Value | Description |
|--------------|---------------|-------------|
| `sprk_BffApiBaseUrl` | `https://spe-api-prod-abc123.azurewebsites.net/api` | BFF API base URL for all client-to-BFF calls |
| `sprk_BffApiAppId` | `api://bff-api-prod-app-id` | Azure AD App Registration ID for BFF API (OAuth scope audience) |
| `sprk_MsalClientId` | `12345678-1234-1234-1234-123456789012` | MSAL Client ID for Dataverse-hosted SPAs |
| `sprk_TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Azure AD Tenant ID |
| `sprk_AzureOpenAiEndpoint` | `https://spaarke-openai-prod.openai.azure.com/` | Azure OpenAI service endpoint |
| `sprk_ShareLinkBaseUrl` | `https://app.spaarke.com/share` | Base URL for shareable document links |
| `sprk_SharePointEmbeddedContainerId` | `b1c2d3e4-f5a6-7890-abcd-ef1234567890` | SPE container ID for document storage |

### E. Key URLs

| Service | URL Pattern |
|---------|-------------|
| API Base URL | `https://spe-api-{env}-{suffix}.azurewebsites.net/api` |
| Health Check | `https://spe-api-{env}-{suffix}.azurewebsites.net/healthz` |
| Ping Endpoint | `https://spe-api-{env}-{suffix}.azurewebsites.net/ping` |
| Dynamics 365 | `https://{org}.crm.dynamics.com` |
| Azure Portal | `https://portal.azure.com` |
| Power Platform Admin | `https://admin.powerplatform.microsoft.com` |
| Microsoft 365 Admin | `https://admin.microsoft.com` |

### F. Contact Information

| Type | Contact |
|------|---------|
| Technical Support | support@spaarke.com |
| Sales Inquiries | sales@spaarke.com |
| Documentation | docs.spaarke.com |

---

*Last Updated: March 20, 2026*
*Version: 2.1*
*Document Owner: Spaarke Engineering Team*
