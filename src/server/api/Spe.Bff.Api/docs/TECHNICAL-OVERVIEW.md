# SDAP BFF API - Technical Overview

**Purpose:** Backend-for-Frontend (BFF) API for SharePoint Document Access Platform (SDAP)
**Stack:** .NET 8 Minimal API, Azure App Service
**Last Updated:** December 3, 2025

---

## Table of Contents

1. [Architecture](#architecture)
2. [Critical Issues & Resolutions](#critical-issues--resolutions)
3. [Deployment Guide](#deployment-guide)
4. [API Endpoints](#api-endpoints)
5. [Configuration](#configuration)
6. [Troubleshooting](#troubleshooting)
7. [Monitoring](#monitoring)

---

## Architecture

### System Overview

The SDAP BFF API is a .NET 8 Minimal API that mediates between Power Apps PCF controls and backend services (Dataverse, Microsoft Graph API, SharePoint Embedded). It provides secure file operations with proper authorization and error handling.

```
┌──────────────────────────────────────────────────────────────────────┐
│                         Client Layer                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐     │
│  │ UniversalQuick  │  │ SpeFileViewer   │  │ Office.js       │     │
│  │ Create (PCF)    │  │ (PCF)           │  │ Add-ins         │     │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘     │
│           │                     │                     │              │
│           └─────────────────────┴─────────────────────┘              │
│                                 │                                    │
└─────────────────────────────────┼────────────────────────────────────┘
                                  │ HTTPS + Bearer Token
                                  ↓
┌──────────────────────────────────────────────────────────────────────┐
│                      SDAP BFF API (.NET 8)                           │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  Minimal API Endpoints                                         │  │
│  │  • /api/documents/{id}/preview                                 │  │
│  │  • /api/documents/{id}/content                                 │  │
│  │  • /api/documents/{id}/office?mode=view|edit                   │  │
│  │  • /api/containers/{id}/files/{path} (Upload)                  │  │
│  │  • /health, /api/ping                                          │  │
│  └───────────────┬────────────────────────────────────────────────┘  │
│                  │                                                    │
│  ┌───────────────▼────────────────────────────────────────────────┐  │
│  │  Core Services                                                  │  │
│  │  ├── IAccessDataSource (Dataverse Document Resolution)         │  │
│  │  ├── ISpeFileStore (Graph API File Operations)                 │  │
│  │  ├── IDocumentAuthorizationService (UAC)                       │  │
│  │  ├── IUploadSessionManager (Chunked Uploads)                   │  │
│  │  └── IDriveItemOperations (Graph Operations)                   │  │
│  └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────┬────────────────────────────┘
                           │              │
                 ┌─────────▼───┐    ┌────▼──────────┐
                 │  Dataverse  │    │ Microsoft     │
                 │  Web API    │    │ Graph API     │
                 └─────────────┘    └───────────────┘
```

### Key Components

#### 1. **Access Data Source** (`IAccessDataSource`)

**Purpose:** Resolves Dataverse Document GUIDs to SharePoint Embedded pointers

**Flow:**
```
Document GUID → Query Dataverse → Extract (DriveId, ItemId) → Return Pointers
```

**Data Model:**

| Dataverse Field | Type | Purpose |
|----------------|------|---------|
| `sprk_documentid` | GUID | Primary key (Dataverse Document ID) |
| `sprk_graphdriveid` | String | SharePoint Embedded Drive ID (`b!...`) |
| `sprk_graphitemid` | String | SharePoint Embedded Item ID (`01...`) |

**Implementation:** `DataverseAccessDataSource.cs`
- Validates Drive ID format (starts with `b!`, length > 20)
- Validates Item ID format (alphanumeric, length > 20)
- Throws `DocumentNotFoundException` if document doesn't exist
- Throws `MappingMissingException` if SPE pointers are missing/invalid

#### 2. **SPE File Store** (`ISpeFileStore`)

**Purpose:** Interacts with Microsoft Graph API for file operations

**Operations:**
- `GetPreviewUrlAsync(driveId, itemId)` - Generate Office preview URL
- `GetContentUrlAsync(driveId, itemId)` - Generate direct download URL
- `GetOfficeViewerUrlAsync(driveId, itemId, mode)` - Generate Office 365 web viewer URL
- `UploadFileAsync(containerId, file, options)` - Upload file (small or chunked)

**Authentication:** Managed Identity (UAMI) with Graph API permissions:
- `Files.Read.All` (Application)
- `Files.ReadWrite.All` (Application)

#### 3. **Upload Session Manager** (`IUploadSessionManager`)

**Purpose:** Handles large file uploads using chunked upload sessions

**Strategy:**
- **Small files (< 4MB):** Single PUT request
- **Large files (≥ 4MB):** Chunked upload with 320 KB chunks

**Flow:**
```
1. Create upload session → POST /drives/{driveId}/upload-session
2. Upload chunks → Multiple PUT requests with Content-Range headers
3. Monitor progress → Server returns 202 (Accepted) per chunk, 200/201 when complete
```

#### 4. **Authorization Service** (`IDocumentAuthorizationService`)

**Purpose:** User Access Control (UAC) for document permissions

**Checks:**
- User has access to parent Matter/Project
- Document visibility rules (public vs. private)
- Row-level security (RLS) policies

---

## Critical Issues & Resolutions

### Issue #1: 404 "Document Not Found" Error (Resolved)

#### Problem Statement

**Symptom:** All file preview requests returned 404 "Document not found"

**Root Cause:** The BFF API received Document GUIDs from PCF controls but was attempting to use them directly with Graph API instead of:
1. Querying Dataverse to get the SharePoint Embedded pointers
2. Using those pointers (`sprk_graphdriveid`, `sprk_graphitemid`) to call Graph API

**Impact:** PCF controls could upload files but could not preview, download, or view them.

#### Resolution

**Implementation Date:** November 20, 2025

**Solution Architecture:**
```
┌─────────┐   Document GUID    ┌──────────────┐
│   PCF   │ ─────────────────> │  BFF API     │
└─────────┘                     │              │
                                │  1. Query    │──┐
                                │  Dataverse   │  │
                                └──────────────┘  │
                                        │         │
                                        ↓         ↓
┌──────────────┐              ┌───────────────────────┐
│   Dataverse  │ <────────── │ Get sprk_graphdriveid │
│              │              │ Get sprk_graphitemid  │
└──────────────┘              └───────────────────────┘
                                        │
                                        ↓
                                ┌──────────────┐
                                │  2. Call     │
                                │  Graph API   │
                                └──────────────┘
                                        │
                                        ↓
                                ┌──────────────┐
                                │  3. Return   │
                                │  Preview URL │
                                └──────────────┘
```

**Changes Made:**

1. **Added `IAccessDataSource` Interface** (`Infrastructure/Data/IAccessDataSource.cs`)
   - `GetSpePointersAsync(documentId)` method
   - Custom exceptions: `DocumentNotFoundException`, `MappingMissingException`

2. **Implemented `DataverseAccessDataSource`** (`Infrastructure/Data/DataverseAccessDataSource.cs`)
   - Queries `sprk_document` entity by GUID
   - Extracts and validates `sprk_graphdriveid` and `sprk_graphitemid`
   - Returns tuple `(string DriveId, string ItemId)`

3. **Updated `FileAccessEndpoints`** (`Api/FileAccessEndpoints.cs`)
   - Added resolution step before Graph API calls
   - Improved error handling with specific error codes
   - Added correlation ID tracking

4. **Updated `SpeFileStore`** (`Infrastructure/Graph/SpeFileStore.cs`)
   - Changed signature to accept `(driveId, itemId)` instead of `documentId`
   - Updated all Graph API calls to use SPE pointers

5. **Registered in DI Container** (`Program.cs`)
   ```csharp
   builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
   ```

**Error Codes Introduced:**

| Code | HTTP Status | Meaning | User Message |
|------|-------------|---------|--------------|
| `document_not_found` | 404 | Document record doesn't exist | "Document not found. It may have been deleted." |
| `mapping_missing_sprk_graphdriveid` | 404 | Drive ID missing/invalid | "File is still uploading. Please try again in a moment." |
| `mapping_missing_sprk_graphitemid` | 404 | Item ID missing/invalid | "File is still uploading. Please try again in a moment." |
| `storage_not_found` | 404 | File deleted from SharePoint | "File not found in storage. It may have been deleted." |
| `access_denied` | 403 | UAC permission denied | "You do not have permission to access this file." |

**Testing:**
- ✅ Unit tests for `DataverseAccessDataSource` (valid documents, missing pointers)
- ✅ Integration tests (end-to-end preview flow)
- ✅ Correlation ID tracking verified

**Result:** File preview, download, and Office viewer operations now work correctly.

---

## Deployment Guide

### Prerequisites

#### Azure Resources Required

- **Resource Group:** `rg-sdap-{environment}`
- **App Service:** `app-sdap-bff-{environment}` (.NET 8, Linux)
- **Service Bus Namespace:** `sb-sdap-{environment}` (Standard SKU)
- **Service Bus Queue:** `document-events`
- **Azure Cache for Redis:** `redis-sdap-{environment}` (Standard tier for staging/prod)
- **User-Assigned Managed Identity (UAMI):** `mi-sdap-{environment}`
- **Key Vault:** `kv-sdap-{environment}`
- **Application Insights:** `ai-sdap-{environment}`

#### App Registrations Required

**A. BFF API App Registration**
- **Name:** `SDAP-BFF-API-{environment}`
- **API Permissions:**
  - Microsoft Graph: `Files.Read.All`, `Files.ReadWrite.All` (Application)
  - Dynamics CRM: `user_impersonation` (Delegated)
- **Expose an API:** `api://sdap-bff-api`
- **Secrets:** Store in Key Vault

**B. Dataverse App Registration**
- **Name:** `SDAP-Dataverse-Client-{environment}`
- **API Permissions:** Dynamics CRM: `user_impersonation` (Delegated)
- **Secrets:** Store in Key Vault

---

### Step-by-Step Deployment

#### Step 1: Create Azure Resources

```bash
# Set variables
ENVIRONMENT="dev"  # or staging, prod
LOCATION="eastus"
RG_NAME="rg-sdap-$ENVIRONMENT"
SUBSCRIPTION_ID="your-subscription-id"

# Create resource group
az group create --name $RG_NAME --location $LOCATION

# Create Service Bus
az servicebus namespace create \
  --name "sb-sdap-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --location $LOCATION \
  --sku Standard

az servicebus queue create \
  --name "document-events" \
  --namespace-name "sb-sdap-$ENVIRONMENT" \
  --resource-group $RG_NAME

# Create Redis (Optional - staging/production only)
az redis create \
  --name "redis-sdap-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --location $LOCATION \
  --sku Standard \
  --vm-size C1

# Create User-Assigned Managed Identity
az identity create \
  --name "mi-sdap-$ENVIRONMENT" \
  --resource-group $RG_NAME

# Get Managed Identity details
MI_CLIENT_ID=$(az identity show --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query clientId -o tsv)
MI_PRINCIPAL_ID=$(az identity show --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query principalId -o tsv)

echo "Managed Identity Client ID: $MI_CLIENT_ID"
echo "Managed Identity Principal ID: $MI_PRINCIPAL_ID"

# Create Key Vault
az keyvault create \
  --name "kv-sdap-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --location $LOCATION

# Grant Managed Identity access to Key Vault
az keyvault set-policy \
  --name "kv-sdap-$ENVIRONMENT" \
  --object-id $MI_PRINCIPAL_ID \
  --secret-permissions get list

# Create App Service Plan
az appservice plan create \
  --name "plan-sdap-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --sku B1 \
  --is-linux

# Create App Service
az webapp create \
  --name "app-sdap-bff-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --plan "plan-sdap-$ENVIRONMENT" \
  --runtime "DOTNETCORE:8.0"

# Assign Managed Identity to App Service
az webapp identity assign \
  --name "app-sdap-bff-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --identities "/subscriptions/$SUBSCRIPTION_ID/resourcegroups/$RG_NAME/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-sdap-$ENVIRONMENT"

# Create Application Insights
az monitor app-insights component create \
  --app "ai-sdap-$ENVIRONMENT" \
  --location $LOCATION \
  --resource-group $RG_NAME \
  --application-type web
```

#### Step 2: Store Secrets in Key Vault

```bash
# BFF API secrets
az keyvault secret set \
  --vault-name "kv-sdap-$ENVIRONMENT" \
  --name "Graph-ClientSecret" \
  --value "{your-bff-api-secret}"

az keyvault secret set \
  --vault-name "kv-sdap-$ENVIRONMENT" \
  --name "Dataverse-ClientSecret" \
  --value "{your-dataverse-secret}"

# Service Bus connection string
SB_CONN_STRING=$(az servicebus namespace authorization-rule keys list \
  --resource-group $RG_NAME \
  --namespace-name "sb-sdap-$ENVIRONMENT" \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv)

az keyvault secret set \
  --vault-name "kv-sdap-$ENVIRONMENT" \
  --name "ServiceBus-ConnectionString" \
  --value "$SB_CONN_STRING"

# Redis connection string (if enabled)
if [ "$ENVIRONMENT" != "dev" ]; then
  REDIS_KEY=$(az redis list-keys \
    --name "redis-sdap-$ENVIRONMENT" \
    --resource-group $RG_NAME \
    --query primaryKey -o tsv)

  REDIS_CONN_STRING="redis-sdap-$ENVIRONMENT.redis.cache.windows.net:6380,password=$REDIS_KEY,ssl=True,abortConnect=False"

  az keyvault secret set \
    --vault-name "kv-sdap-$ENVIRONMENT" \
    --name "Redis-ConnectionString" \
    --value "$REDIS_CONN_STRING"
fi
```

#### Step 3: Configure App Service Settings

```bash
# Set Key Vault reference configuration
az webapp config appsettings set \
  --name "app-sdap-bff-$ENVIRONMENT" \
  --resource-group $RG_NAME \
  --settings \
    "Graph__TenantId={your-tenant-id}" \
    "Graph__ClientId={bff-api-client-id}" \
    "Graph__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Graph-ClientSecret/)" \
    "Graph__ManagedIdentity__Enabled=true" \
    "Graph__ManagedIdentity__ClientId=$MI_CLIENT_ID" \
    "Graph__Scopes__0=https://graph.microsoft.com/.default" \
    "Dataverse__EnvironmentUrl=https://your-env.crm.dynamics.com" \
    "Dataverse__ClientId={dataverse-client-id}" \
    "Dataverse__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Dataverse-ClientSecret/)" \
    "Dataverse__TenantId={your-tenant-id}" \
    "ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/ServiceBus-ConnectionString/)" \
    "ServiceBus__QueueName=document-events" \
    "ServiceBus__MaxConcurrentCalls=5" \
    "Redis__Enabled=true" \
    "Redis__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Redis-ConnectionString/)" \
    "Redis__InstanceName=sdap:" \
    "Authorization__Enabled=true" \
    "ASPNETCORE_ENVIRONMENT=Production"
```

#### Step 4: Grant Graph API Permissions to Managed Identity

```powershell
# PowerShell script to grant Graph permissions
Connect-MgGraph -Scopes "Application.ReadWrite.All", "AppRoleAssignment.ReadWrite.All"

$graphAppId = "00000003-0000-0000-c000-000000000000"  # Microsoft Graph
$miObjectId = "{managed-identity-principal-id}"

$graphServicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"

# Grant Files.Read.All
$appRole = $graphServicePrincipal.AppRoles | Where-Object { $_.Value -eq "Files.Read.All" }
New-MgServicePrincipalAppRoleAssignment \
  -ServicePrincipalId $miObjectId \
  -PrincipalId $miObjectId \
  -AppRoleId $appRole.Id \
  -ResourceId $graphServicePrincipal.Id

# Grant Files.ReadWrite.All
$appRole = $graphServicePrincipal.AppRoles | Where-Object { $_.Value -eq "Files.ReadWrite.All" }
New-MgServicePrincipalAppRoleAssignment \
  -ServicePrincipalId $miObjectId \
  -PrincipalId $miObjectId \
  -AppRoleId $appRole.Id \
  -ResourceId $graphServicePrincipal.Id
```

#### Step 5: Build Application

```bash
cd c:/code_files/spaarke/src/server/api/Spe.Bff.Api

# Disable Directory.Packages.props temporarily (CPM issue)
if [ -f "../../Directory.Packages.props" ]; then
    mv "../../Directory.Packages.props" "../../Directory.Packages.props.disabled"
fi

# Build for Release
dotnet publish -c Release -o ./publish

# Restore Directory.Packages.props
if [ -f "../../Directory.Packages.props.disabled" ]; then
    mv "../../Directory.Packages.props.disabled" "../../Directory.Packages.props"
fi
```

**Expected Output:**
```
Spe.Bff.Api -> c:\code_files\spaarke\src\server\api\Spe.Bff.Api\publish\
```

#### Step 6: Create Deployment Package

```bash
# Navigate to publish directory
cd publish

# Create zip package (Git Bash)
tar -czf ../spe-bff-api-deployment.tar.gz *

# Or use PowerShell
# Compress-Archive -Path * -DestinationPath ../spe-bff-api-deployment.zip -Force

cd ..
```

**Verify Package:**
```bash
ls -lh spe-bff-api-deployment.tar.gz
```

Expected: File size ~50-100 MB

#### Step 7: Deploy to Azure App Service

```bash
# Set variables
RESOURCE_GROUP="rg-sdap-$ENVIRONMENT"
APP_NAME="app-sdap-bff-$ENVIRONMENT"
ZIP_FILE="spe-bff-api-deployment.tar.gz"

# Deploy via Azure CLI (recommended)
az webapp deploy \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME \
    --src-path $ZIP_FILE \
    --type zip

# Or use config-zip (older command)
# az webapp deployment source config-zip \
#     --resource-group $RESOURCE_GROUP \
#     --name $APP_NAME \
#     --src $ZIP_FILE
```

**Expected Output:**
```json
{
  "active": true,
  "deployer": "ZipDeploy",
  "complete": true,
  "status": 4
}
```

#### Step 8: Verify Deployment

**Check App Service Status:**
```bash
az webapp show \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME \
    --query state
```

Expected: `"Running"`

**Check Deployment Logs:**
```bash
az webapp log tail \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME
```

Look for:
```
Application started. Press Ctrl+C to shut down.
Hosting environment: Production
Content root path: /home/site/wwwroot
Now listening on: http://[::]:8080
```

**Test Health Endpoint:**
```bash
curl https://app-sdap-bff-{environment}.azurewebsites.net/health
```

Expected:
```json
{
  "status": "Healthy",
  "timestamp": "2025-12-03T15:30:00Z"
}
```

**Test File Access Endpoints (With Auth):**
```bash
# Get access token
TOKEN="your-bearer-token"

# Test preview endpoint
curl -X GET "https://app-sdap-bff-{environment}.azurewebsites.net/api/documents/{documentId}/preview" \
  -H "Authorization: Bearer $TOKEN"
```

Expected: 200 OK with `previewUrl` in response

---

### Environment-Specific Configuration

| Setting | Development | Staging | Production |
|---------|-------------|---------|------------|
| **Redis** | Disabled (in-memory) | Enabled (Standard) | Enabled (Standard+) |
| **Service Bus Concurrency** | 2 | 5 | 10+ |
| **Authorization** | Optional | Enabled | Required |
| **Logging Level** | Debug | Information | Warning |
| **Managed Identity** | Disabled (client secret) | Enabled | Required |
| **High Availability** | Single instance | Single instance | Multiple instances |

---

## API Endpoints

### File Access Endpoints

#### 1. Get Preview URL

**Endpoint:** `GET /api/documents/{documentId}/preview`

**Purpose:** Generate Office preview URL for embedding in iframes

**Request:**
```http
GET /api/documents/ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5/preview
Authorization: Bearer {token}
X-Correlation-Id: {uuid}
```

**Response (200 OK):**
```json
{
  "previewUrl": "https://contoso.sharepoint.com/_layouts/15/Doc.aspx?...",
  "documentId": "ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "documentInfo": {
    "name": "contract.pdf",
    "size": 2048576
  }
}
```

**Error Responses:**
- `404 document_not_found` - Document doesn't exist
- `404 mapping_missing_sprk_graphdriveid` - File still uploading
- `403 access_denied` - User lacks permission
- `401 unauthorized` - Invalid/expired token

#### 2. Get Content URL

**Endpoint:** `GET /api/documents/{documentId}/content`

**Purpose:** Generate direct download URL

**Request:**
```http
GET /api/documents/ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5/content
Authorization: Bearer {token}
```

**Response (200 OK):**
```json
{
  "downloadUrl": "https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}/content",
  "documentId": "ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5",
  "correlationId": "..."
}
```

#### 3. Get Office Viewer URL

**Endpoint:** `GET /api/documents/{documentId}/office?mode=view|edit`

**Purpose:** Generate Microsoft 365 web viewer URL

**Request:**
```http
GET /api/documents/ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5/office?mode=view
Authorization: Bearer {token}
```

**Response (200 OK):**
```json
{
  "officeViewerUrl": "https://word-view.officeapps.live.com/wv/...",
  "mode": "view",
  "documentId": "ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5"
}
```

#### 4. Upload File

**Endpoint:** `PUT /api/containers/{containerId}/files/{filePath}`

**Purpose:** Upload file (small or chunked)

**Request (Small File < 4MB):**
```http
PUT /api/containers/01AZJL5PN6Y2GOVW7725BZO354PWSELRRZ/files/document.pdf
Content-Type: application/octet-stream
Authorization: Bearer {token}

[file binary data]
```

**Response (201 Created):**
```json
{
  "driveId": "b!yLRdWEOAdkaWXskuRfByIRiz...",
  "itemId": "01LBYCMX76QPLGITR47BB355T4G2CVDL2B",
  "name": "document.pdf",
  "size": 2048576
}
```

### Health & Diagnostics

#### Health Endpoint

**Endpoint:** `GET /health`

**Response:**
```json
{
  "status": "Healthy",
  "timestamp": "2025-12-03T15:30:00Z"
}
```

#### Ping Endpoint

**Endpoint:** `GET /api/ping`

**Response:**
```json
{
  "message": "pong",
  "timestamp": "2025-12-03T15:30:00Z"
}
```

---

## Configuration

### Required App Settings

```bash
# Azure AD / Graph API
Graph__TenantId={tenant-id}
Graph__ClientId={client-id}
Graph__ClientSecret=@Microsoft.KeyVault(SecretUri=...)
Graph__ManagedIdentity__Enabled=true
Graph__ManagedIdentity__ClientId={uami-client-id}
Graph__Scopes__0=https://graph.microsoft.com/.default

# Dataverse
Dataverse__EnvironmentUrl=https://org.crm.dynamics.com
Dataverse__ClientId={client-id}
Dataverse__ClientSecret=@Microsoft.KeyVault(SecretUri=...)
Dataverse__TenantId={tenant-id}

# Service Bus
ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=...)
ServiceBus__QueueName=document-events
ServiceBus__MaxConcurrentCalls=5

# Redis (Optional)
Redis__Enabled=true
Redis__ConnectionString=@Microsoft.KeyVault(SecretUri=...)
Redis__InstanceName=sdap:

# Authorization
Authorization__Enabled=true

# Application Insights
APPLICATIONINSIGHTS_CONNECTION_STRING={connection-string}

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

### CORS Configuration

**Azure Portal → App Service → CORS:**
- `https://org.crm.dynamics.com`
- `https://org.crm4.dynamics.com` (or your Dataverse region)
- `https://make.powerapps.com` (for testing)

**Or via CLI:**
```bash
az webapp cors add \
    --resource-group $RG_NAME \
    --name $APP_NAME \
    --allowed-origins "https://org.crm.dynamics.com" "https://org.crm4.dynamics.com"
```

---

## Troubleshooting

### Build Issues

#### Error: NU1008 (Central Package Management)

**Error:**
```
error NU1008: Projects that use central package version management should not define the version...
```

**Solution:**
```bash
# Disable Directory.Packages.props before building
mv Directory.Packages.props Directory.Packages.props.disabled

# Build
dotnet publish -c Release

# Restore
mv Directory.Packages.props.disabled Directory.Packages.props
```

### Deployment Issues

#### Deployment Shows "409 Conflict"

**Cause:** Previous deployment still in progress or locked

**Solution:**
```bash
# Stop app service
az webapp stop --resource-group $RG_NAME --name $APP_NAME

# Wait 30 seconds
sleep 30

# Deploy again
az webapp deploy --resource-group $RG_NAME --name $APP_NAME --src-path deploy.zip --type zip

# Start app service
az webapp start --resource-group $RG_NAME --name $APP_NAME
```

### Runtime Issues

#### 401 Unauthorized

**Possible Causes:**
1. Missing or invalid Bearer token
2. Token expired
3. Azure AD app registration misconfigured

**Debug Steps:**
```bash
# Check Azure AD configuration
az ad app show --id {client-id}

# Verify API permissions
az ad app permission list --id {client-id}

# Test with fresh token
TOKEN=$(az account get-access-token --resource https://app-sdap-bff-{env}.azurewebsites.net --query accessToken -o tsv)
curl -X GET "https://app-sdap-bff-{env}.azurewebsites.net/api/documents/{id}/preview" \
  -H "Authorization: Bearer $TOKEN"
```

#### 403 Forbidden (Graph API)

**Symptom:**
```json
{
  "status": 500,
  "title": "Graph API Error",
  "detail": "Failed to generate preview: Forbidden"
}
```

**Cause:** Service principal/UAMI lacks Graph API permissions

**Solution:**
```bash
# Grant Graph API permissions to service principal/UAMI
az ad app permission add \
    --id {client-id} \
    --api 00000003-0000-0000-c000-000000000000 \
    --api-permissions 01d4889c-1287-42c6-ac1f-5d1e02578ef6=Scope

# Grant admin consent
az ad app permission admin-consent --id {client-id}
```

#### 404 Document Not Found

**Possible Causes:**
1. Document doesn't exist in Dataverse
2. SPE pointers (`sprk_graphdriveid`, `sprk_graphitemid`) missing or invalid
3. File deleted from SharePoint

**Debug Steps:**
1. Query Dataverse directly to verify document exists
2. Check that `sprk_graphdriveid` and `sprk_graphitemid` are populated
3. Verify correlation ID in logs for detailed error information

#### Configuration Validation Fails

**Error:** `Configuration validation failed. Application cannot start.`

**Solutions:**
- Check application logs for specific validation errors
- Verify all Key Vault references are correct
- Ensure Managed Identity has access to Key Vault
- Check that all required configuration sections exist

---

## Monitoring

### Application Insights Queries

#### Preview Endpoint Usage
```kusto
requests
| where url contains "/api/documents/" and url contains "/preview"
| summarize count() by resultCode, bin(timestamp, 1h)
| render timechart
```

#### Error Rate
```kusto
requests
| where url contains "/api/documents/"
| summarize ErrorRate = countif(resultCode >= 400) * 100.0 / count() by bin(timestamp, 5m)
| render timechart
```

#### Average Response Time
```kusto
requests
| where url contains "/api/documents/"
| summarize avg(duration) by operation_Name, bin(timestamp, 5m)
| render timechart
```

#### Correlation ID Tracking
```kusto
traces
| where customDimensions has "CorrelationId"
| extend CorrelationId = tostring(customDimensions.CorrelationId)
| where CorrelationId == "{your-correlation-id}"
| project timestamp, message, severityLevel
| order by timestamp asc
```

### Key Metrics to Monitor

- **Request rate** (requests/minute)
- **Error rate** (5xx errors)
- **Response time** (P95, P99)
- **Dependency failures** (Graph API, Dataverse, Service Bus)
- **Authorization failures** (403 responses)
- **Configuration validation** (startup failures)

### Recommended Alerts

Create alerts for:
- High error rate (>5% 5xx responses)
- Slow response times (>2s P95)
- Dependency failures
- Application restarts (configuration validation failures)

---

## Security Checklist

- [ ] All secrets stored in Key Vault
- [ ] Managed Identity used for production
- [ ] No secrets in appsettings.json (committed to git)
- [ ] Least privilege permissions granted
- [ ] CORS properly configured (no AllowAnyOrigin in production)
- [ ] Authorization enabled in production
- [ ] HTTPS enforced
- [ ] Application Insights configured
- [ ] Network rules configured (if using VNet)

---

## Rollback Plan

1. **Stop App Service:**
   ```bash
   az webapp stop --name $APP_NAME --resource-group $RG_NAME
   ```

2. **Revert to previous deployment:**
   ```bash
   # If using deployment slots
   az webapp deployment slot swap \
     --name $APP_NAME \
     --resource-group $RG_NAME \
     --slot staging \
     --target-slot production

   # Or redeploy previous version
   az webapp deployment source config-zip \
     --name $APP_NAME \
     --resource-group $RG_NAME \
     --src previous-deploy.zip
   ```

3. **Verify health endpoint:**
   ```bash
   curl https://app-sdap-bff-{environment}.azurewebsites.net/health
   ```

4. **Resume traffic:**
   ```bash
   az webapp start --name $APP_NAME --resource-group $RG_NAME
   ```

---

## Next Steps for Senior Developers

### Making Code Changes

1. **Local Development Setup:**
   - Install .NET 8 SDK
   - Configure `appsettings.Development.json` with dev environment secrets
   - Use User Secrets for local credentials: `dotnet user-secrets set "Graph:ClientSecret" "..."`

2. **Common Extension Points:**
   - Add new endpoint: Create in `Api/` folder, register in `Program.cs`
   - Add new Graph operation: Extend `ISpeFileStore` interface
   - Add new authorization rule: Extend `IDocumentAuthorizationService`
   - Add new Dataverse query: Extend `IAccessDataSource`

3. **Testing:**
   - Unit tests: `Spe.Bff.Api.Tests/`
   - Integration tests: Require dev Dataverse environment
   - Use correlation IDs for debugging

4. **Deployment:**
   - Follow deployment guide above
   - Test in dev environment first
   - Monitor Application Insights after deployment
   - Be prepared to rollback if issues arise

### Related Documentation

- **Architecture Decision Records (ADRs):** `docs/adr/`
- **API Client Documentation:** PCF controls in `src/client/pcf/`
- **Dataverse Schema:** `src/dataverse/`
- **Azure Resources:** `docs/infrastructure/` (if exists)

---

**Document Version:** 1.0
**Last Updated:** December 3, 2025
**Maintained By:** Development Team
