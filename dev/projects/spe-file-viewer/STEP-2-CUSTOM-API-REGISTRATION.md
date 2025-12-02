# Step 2: Custom API Registration

**Phase**: 2 of 5
**Duration**: ~1 hour
**Prerequisites**: Step 1 completed (plugin DLL built)

---

## Overview

Register the Custom API and plugin in Dataverse to make the server-side proxy available to client applications. This involves creating configuration records, registering the plugin assembly, and setting up the Custom API with its parameters.

**What You'll Create**:
1. External Service Config record (SDAP_BFF_API configuration)
2. Plugin assembly registration in Dataverse
3. Custom API record (sprk_GetFilePreviewUrl)
4. 6 Custom API output parameters
5. Plugin step registration

---

## Task 2.1: Create External Service Config Record

**Goal**: Configure SDAP BFF API connection details

### Option A: PowerShell Script (Recommended)

```powershell
# Connect to Dataverse (SPAARKE DEV 1)
Import-Module Microsoft.Xrm.Data.PowerShell

# Interactive login
$conn = Get-CrmConnection -InteractiveMode

# Get credentials (REPLACE THESE VALUES)
$tenantId = "{your-tenant-id}"
$clientId = "{service-principal-client-id}"
$clientSecret = "{service-principal-client-secret}"
$sdapApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"

# Create External Service Config
$config = @{
    "sprk_name" = "SDAP_BFF_API"
    "sprk_baseurl" = "$sdapApiUrl/api"
    "sprk_isenabled" = $true
    "sprk_authtype" = 1  # ClientCredentials (app-only)
    "sprk_tenantid" = $tenantId
    "sprk_clientid" = $clientId
    "sprk_clientsecret" = $clientSecret
    "sprk_scope" = "$sdapApiUrl/.default"
    "sprk_timeout" = 300  # 5 minutes
    "sprk_retrycount" = 3
    "sprk_retrydelay" = 1000  # 1 second
}

$configId = New-CrmRecord -conn $conn -EntityLogicalName "sprk_externalserviceconfig" -Fields $config
Write-Host "External Service Config created with ID: $configId" -ForegroundColor Green
```

### Option B: Manual (Power Apps UI)

1. Navigate to **SPAARKE DEV 1** environment
2. Open **External Service Configs** (sprk_externalserviceconfig table)
3. Click **+ New**
4. Fill in:

| Field | Value |
|-------|-------|
| **Name** | `SDAP_BFF_API` |
| **Base URL** | `https://spe-api-dev-67e2xz.azurewebsites.net/api` |
| **Is Enabled** | `Yes` |
| **Auth Type** | `Client Credentials (1)` |
| **Tenant ID** | `{your-tenant-id}` |
| **Client ID** | `{service-principal-client-id}` |
| **Client Secret** | `{service-principal-client-secret}` |
| **Scope** | `https://spe-api-dev-67e2xz.azurewebsites.net/.default` |
| **Timeout** | `300` |
| **Retry Count** | `3` |
| **Retry Delay** | `1000` |

5. Click **Save**

### Get Service Principal Credentials

If you don't have a service principal yet:

```bash
# Create service principal for SDAP API access
az ad sp create-for-rbac \
    --name "spaarke-sdap-api-plugin" \
    --role Contributor

# Output includes:
# - appId (use as Client ID)
# - password (use as Client Secret)
# - tenant (use as Tenant ID)
```

Grant the service principal access to SDAP BFF API:

```bash
# Grant API permissions (app-only)
# This allows the plugin to call SDAP BFF API with app-only token
az ad app permission add \
    --id {service-principal-app-id} \
    --api {sdap-api-app-id} \
    --api-permissions {permission-id}=Role
```

**Validation**:
```powershell
# Verify config exists
Get-CrmRecords -conn $conn -EntityLogicalName "sprk_externalserviceconfig" `
    -FilterAttribute "sprk_name" -FilterOperator "eq" -FilterValue "SDAP_BFF_API"
```

---

## Task 2.2: Register Plugin Assembly

**Goal**: Upload plugin DLL to Dataverse

### Using Plugin Registration Tool

1. **Launch Plugin Registration Tool**:
   ```bash
   pac tool prt
   ```

2. **Connect to Environment**:
   - Click **Create New Connection**
   - Select **Office 365**
   - Enter credentials
   - Select **SPAARKE DEV 1** environment

3. **Register Assembly**:
   - Click **Register** → **Register New Assembly**
   - Click **...** and browse to:
     ```
     c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll
     ```
   - **Isolation Mode**: Sandbox
   - **Location**: Database
   - Click **Register Selected Plugins**

4. **Verify Registration**:
   - Expand `Spaarke.Dataverse.CustomApiProxy` assembly
   - You should see:
     - `Spaarke.Dataverse.CustomApiProxy.BaseProxyPlugin`
     - `Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin` ✓

**Validation**:
```powershell
# Query registered assemblies
Get-CrmRecords -conn $conn -EntityLogicalName "pluginassembly" `
    -FilterAttribute "name" -FilterOperator "eq" -FilterValue "Spaarke.Dataverse.CustomApiProxy"
```

---

## Task 2.3: Create Custom API Record

**Goal**: Define the Custom API operation

### PowerShell Script (Recommended)

```powershell
# Create Custom API
$customApi = @{
    "uniquename" = "sprk_GetFilePreviewUrl"
    "name" = "Get File Preview URL"
    "displayname" = "Get File Preview URL"
    "description" = "Server-side proxy for getting SharePoint Embedded preview URLs"
    "bindingtype" = 1  # Entity
    "boundentitylogicalname" = "sprk_document"
    "isfunction" = $true
    "isprivate" = $false
    "allowedcustomprocessingsteptype" = 0  # None (sync only)
}

$customApiId = New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
Write-Host "Custom API created with ID: $customApiId" -ForegroundColor Green

# Save ID for next task
$customApiId
```

### Manual (Advanced Settings)

1. Navigate to **Settings** → **Customizations** → **Customize the System**
2. Expand **Entities** → Find **Custom API**
3. Click **New**
4. Fill in:

| Field | Value |
|-------|-------|
| **Unique Name** | `sprk_GetFilePreviewUrl` |
| **Name** | `Get File Preview URL` |
| **Display Name** | `Get File Preview URL` |
| **Description** | `Server-side proxy for getting SharePoint Embedded preview URLs` |
| **Binding Type** | `Entity (1)` |
| **Bound Entity Logical Name** | `sprk_document` |
| **Is Function** | `Yes` |
| **Is Private** | `No` |
| **Allowed Custom Processing Step Type** | `None (0)` |

5. Click **Save**
6. **Note the Custom API ID** (you'll need it for parameters)

**Validation**:
```powershell
# Verify Custom API exists
Get-CrmRecords -conn $conn -EntityLogicalName "customapi" `
    -FilterAttribute "uniquename" -FilterOperator "eq" -FilterValue "sprk_GetFilePreviewUrl"
```

---

## Task 2.4: Create Custom API Parameters

**Goal**: Define output parameters for the Custom API

### Output Parameters (6 total)

**IMPORTANT**: No input parameters - the documentId comes from the bound entity!

#### PowerShell Script (All Parameters)

```powershell
# Use the Custom API ID from Task 2.3
$customApiId = "{paste-id-from-task-2.3}"

# Parameter 1: PreviewUrl
$param1 = @{
    "uniquename" = "PreviewUrl"
    "name" = "PreviewUrl"
    "displayname" = "Preview URL"
    "description" = "Ephemeral preview URL (expires in ~10 minutes)"
    "type" = 10  # String
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
$p1Id = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param1
Write-Host "Created PreviewUrl parameter: $p1Id" -ForegroundColor Green

# Parameter 2: FileName
$param2 = @{
    "uniquename" = "FileName"
    "name" = "FileName"
    "displayname" = "File Name"
    "description" = "File name for display"
    "type" = 10  # String
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
$p2Id = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param2
Write-Host "Created FileName parameter: $p2Id" -ForegroundColor Green

# Parameter 3: FileSize
$param3 = @{
    "uniquename" = "FileSize"
    "name" = "FileSize"
    "displayname" = "File Size"
    "description" = "File size in bytes"
    "type" = 6  # Integer
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
$p3Id = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param3
Write-Host "Created FileSize parameter: $p3Id" -ForegroundColor Green

# Parameter 4: ContentType
$param4 = @{
    "uniquename" = "ContentType"
    "name" = "ContentType"
    "displayname" = "Content Type"
    "description" = "MIME type"
    "type" = 10  # String
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
$p4Id = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param4
Write-Host "Created ContentType parameter: $p4Id" -ForegroundColor Green

# Parameter 5: ExpiresAt
$param5 = @{
    "uniquename" = "ExpiresAt"
    "name" = "ExpiresAt"
    "displayname" = "Expires At"
    "description" = "When the preview URL expires (UTC)"
    "type" = 8  # DateTime
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
$p5Id = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param5
Write-Host "Created ExpiresAt parameter: $p5Id" -ForegroundColor Green

# Parameter 6: CorrelationId
$param6 = @{
    "uniquename" = "CorrelationId"
    "name" = "CorrelationId"
    "displayname" = "Correlation ID"
    "description" = "Request tracking ID for tracing and debugging"
    "type" = 10  # String
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
$p6Id = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param6
Write-Host "Created CorrelationId parameter: $p6Id" -ForegroundColor Green

Write-Host "`nAll 6 output parameters created successfully!" -ForegroundColor Green
```

**Validation**:
```powershell
# Verify all parameters exist
Get-CrmRecords -conn $conn -EntityLogicalName "customapiresponseproperty" `
    -FilterAttribute "customapiid" -FilterOperator "eq" -FilterValue $customApiId
```

---

## Task 2.5: Register Plugin Step

**Goal**: Connect the plugin to the Custom API message

### Using Plugin Registration Tool

1. **In Plugin Registration Tool**, expand your registered assembly
2. **Right-click** on `GetFilePreviewUrlPlugin` → **Register New Step**
3. Fill in:

| Field | Value |
|-------|-------|
| **Message** | `sprk_GetFilePreviewUrl` |
| **Primary Entity** | `sprk_document` |
| **Event Pipeline Stage** | `Main Operation (30)` |
| **Execution Mode** | `Synchronous` |
| **Deployment** | `Server` |
| **User Context** | `Calling User` |

4. Click **Register New Step**
5. ✅ You should see the step under the plugin

### PowerShell Alternative

```powershell
# Get plugin type ID
$pluginType = Get-CrmRecords -conn $conn -EntityLogicalName "plugintype" `
    -FilterAttribute "name" -FilterOperator "eq" `
    -FilterValue "Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin"

# Create SDK Message Processing Step
$step = @{
    "name" = "sprk_GetFilePreviewUrl: sprk_document"
    "plugintypeid@odata.bind" = "/plugintypes($($pluginType.CrmRecords[0].plugintypeid))"
    "sdkmessageid@odata.bind" = "/sdkmessages({guid-for-sprk_GetFilePreviewUrl})"
    "stage" = 30  # Main Operation
    "mode" = 0    # Synchronous
    "rank" = 1
}

$stepId = New-CrmRecord -conn $conn -EntityLogicalName "sdkmessageprocessingstep" -Fields $step
Write-Host "Plugin step registered with ID: $stepId" -ForegroundColor Green
```

**Validation**:
```powershell
# Verify step exists
Get-CrmRecords -conn $conn -EntityLogicalName "sdkmessageprocessingstep" `
    -FilterAttribute "name" -FilterOperator "like" -FilterValue "%sprk_GetFilePreviewUrl%"
```

---

## Task 2.6: Publish Customizations

**Goal**: Make all changes active in the environment

### PowerShell

```powershell
# Publish all customizations
Publish-CrmAllCustomization -conn $conn
Write-Host "All customizations published!" -ForegroundColor Green
```

### Manual (Power Apps)

1. Navigate to **Settings** → **Customizations**
2. Click **Publish All Customizations**
3. Wait for completion (~30 seconds)

**Validation**:
```powershell
# Check publishing status
$pubRequest = Get-CrmRecords -conn $conn -EntityLogicalName "publishxml" `
    -TopCount 1 -OrderBy "createdon" -OrderByDescending
$pubRequest.CrmRecords[0]
```

---

## Validation Checklist

- [ ] **External Service Config**: Record exists with name "SDAP_BFF_API"
- [ ] **Config Enabled**: `sprk_isenabled` = true
- [ ] **Plugin Assembly**: Registered in Dataverse (Database location)
- [ ] **Plugin Class**: `GetFilePreviewUrlPlugin` visible in PRT
- [ ] **Custom API**: Record exists with unique name `sprk_GetFilePreviewUrl`
- [ ] **Binding Type**: Entity (sprk_document)
- [ ] **Output Parameters**: All 6 parameters created
  - PreviewUrl
  - FileName
  - FileSize
  - ContentType
  - ExpiresAt
  - CorrelationId
- [ ] **Plugin Step**: Registered on message `sprk_GetFilePreviewUrl`
- [ ] **Step Configuration**: Stage = 30 (Main Operation), Mode = Synchronous
- [ ] **Customizations Published**: All changes active

---

## Quick Test (Browser Console)

Test the Custom API directly from a Document form:

```javascript
// Open any Document record in Dataverse
// Open browser console (F12)
// Run this code:

const documentId = Xrm.Page.data.entity.getId().replace(/[{}]/g, '');

Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": {
                    "typeName": "mscrm.sprk_document",
                    "structuralProperty": 5
                }
            },
            operationType: 1,  // Function
            operationName: "sprk_GetFilePreviewUrl"
        };
    },
    entity: {
        entityType: "sprk_document",
        id: documentId
    }
}).then(
    result => {
        console.log("✅ Custom API Success!", result);
        console.log("Preview URL:", result.PreviewUrl);
        console.log("Expires At:", result.ExpiresAt);
        console.log("Correlation ID:", result.CorrelationId);
    },
    error => {
        console.error("❌ Custom API Error:", error.message);
    }
);
```

**Expected Output**:
```javascript
✅ Custom API Success!
{
  PreviewUrl: "https://spaarke.sharepoint.com/...",
  FileName: "document.pdf",
  FileSize: 102400,
  ContentType: "application/pdf",
  ExpiresAt: "2025-01-21T16:30:00Z",
  CorrelationId: "abc123-def456-..."
}
```

---

## Common Issues

### Issue: "External service config not found: SDAP_BFF_API"

**Cause**: Config record not created or wrong name

**Fix**:
1. Verify record exists in `sprk_externalserviceconfig`
2. Check `sprk_name` = "SDAP_BFF_API" (exact match, case-sensitive)
3. Verify `sprk_isenabled` = true

### Issue: "Failed to acquire access token"

**Cause**: Service principal credentials invalid

**Fix**:
1. Verify ClientId, ClientSecret, TenantId in config
2. Test credentials:
   ```bash
   az login --service-principal \
       --username {client-id} \
       --password {client-secret} \
       --tenant {tenant-id}
   ```

### Issue: Custom API not appearing in Xrm.WebApi

**Cause**: Custom API not published or parameters missing

**Fix**:
1. Publish all customizations (Task 2.6)
2. Verify all 6 output parameters created
3. Clear browser cache
4. Hard refresh (Ctrl+Shift+R)

### Issue: Plugin trace shows "BFF API returned 401"

**Cause**: SDAP BFF API rejecting service principal token

**Fix**:
1. Check SDAP BFF API logs in Azure
2. Verify service principal has API permissions
3. Verify scope matches API configuration

---

## Next Step

Once Custom API is successfully registered and tested, proceed to **Step 3: PCF Control Development** to create the React-based file viewer control.

**Records Created**:
- 1 External Service Config (sprk_externalserviceconfig)
- 1 Plugin Assembly (pluginassembly)
- 1 Custom API (customapi)
- 6 Custom API Response Properties (customapiresponseproperty)
- 1 SDK Message Processing Step (sdkmessageprocessingstep)
