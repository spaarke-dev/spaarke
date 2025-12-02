# Deployment Steps: Document File Viewer Custom API

**Status**: ✅ Plugin Built Successfully
**Next Step**: Register in Dataverse

---

## 📦 What's Ready

✅ **Plugin Code**: [`GetDocumentFileUrlPlugin.cs`](./src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetDocumentFileUrlPlugin.cs)
✅ **Plugin Assembly**: `Spaarke.Dataverse.CustomApiProxy.dll` (compiled)
✅ **Web Resource**: [`sprk_document_file_viewer.html`](./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_document_file_viewer.html)
✅ **Build Status**: 0 errors, 4 warnings (Azure.Identity vulnerability - non-blocking)

**Assembly Location**:
```
C:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll
```

---

## 🚀 Step 1: Create External Service Config Record

The plugin uses your existing `sprk_externalserviceconfig` infrastructure for configuration.

### Option A: Manual (Power Apps)

1. Navigate to **SPAARKE DEV 1** environment
2. Open **External Service Configs** (sprk_externalserviceconfig)
3. Click **+ New**
4. Fill in the following:

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
| **Timeout** | `300` (5 minutes) |
| **Retry Count** | `3` |
| **Retry Delay** | `1000` (1 second) |

5. Click **Save**

### Option B: PowerShell Script

```powershell
# Connect to Dataverse
Import-Module Microsoft.Xrm.Data.PowerShell
$conn = Get-CrmConnection -InteractiveMode

# Create External Service Config
$config = @{
    "sprk_name" = "SDAP_BFF_API"
    "sprk_baseurl" = "https://spe-api-dev-67e2xz.azurewebsites.net/api"
    "sprk_isenabled" = $true
    "sprk_authtype" = 1  # ClientCredentials
    "sprk_tenantid" = "{your-tenant-id}"
    "sprk_clientid" = "{service-principal-client-id}"
    "sprk_clientsecret" = "{service-principal-client-secret}"
    "sprk_scope" = "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
    "sprk_timeout" = 300
    "sprk_retrycount" = 3
    "sprk_retrydelay" = 1000
}

New-CrmRecord -conn $conn -EntityLogicalName "sprk_externalserviceconfig" -Fields $config
```

### 🔑 Where to Get Credentials

**Service Principal** (if you don't have one):
```bash
# Create new service principal
az ad sp create-for-rbac --name "spaarke-sdap-api-plugin" --role Contributor

# Output will include:
# - appId (use as Client ID)
# - password (use as Client Secret)
# - tenant (use as Tenant ID)
```

**Grant API Permissions**:
```bash
# Get SDAP API App ID
az ad app list --display-name "spe-api-dev-67e2xz" --query "[0].appId" -o tsv

# Grant service principal access to SDAP API
az ad app permission add \
    --id {service-principal-app-id} \
    --api {sdap-api-app-id} \
    --api-permissions {permission-id}=Scope
```

---

## 🚀 Step 2: Register Plugin Assembly

### Using Plugin Registration Tool (Recommended)

1. **Download Plugin Registration Tool** (if not installed):
   ```bash
   pac tool prt
   ```

2. **Launch Plugin Registration Tool**

3. **Connect to Environment**:
   - Click "Create New Connection"
   - Select "Office 365"
   - Enter your credentials
   - Select **SPAARKE DEV 1** environment

4. **Register Assembly**:
   - Click "Register" → "Register New Assembly"
   - Click "..." and browse to:
     ```
     C:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll
     ```
   - **Isolation Mode**: Sandbox
   - **Location**: Database
   - Click "Register Selected Plugins"
   - ✅ **Success**: You should see `Spaarke.Dataverse.CustomApiProxy` in the list

5. **Verify Registration**:
   - Expand the assembly
   - You should see:
     - `Spaarke.Dataverse.CustomApiProxy.BaseProxyPlugin`
     - `Spaarke.Dataverse.CustomApiProxy.GetDocumentFileUrlPlugin` ← This is ours!

---

## 🚀 Step 3: Create Custom API Record

### Option A: Manual (Power Apps - Advanced Settings)

1. Navigate to **Settings** → **Customizations** → **Customize the System**
2. Expand **Entities** → Find **Custom API** (customapi)
3. Click **New**
4. Fill in:

| Field | Value |
|-------|-------|
| **Unique Name** | `sprk_GetDocumentFileUrl` |
| **Name** | `Get Document File URL` |
| **Display Name** | `Get Document File URL` |
| **Description** | `Server-side proxy for getting SharePoint Embedded file URLs` |
| **Binding Type** | Entity (1) |
| **Bound Entity Logical Name** | `sprk_document` |
| **Is Function** | Yes |
| **Is Private** | No |
| **Allowed Custom Processing Step Type** | None (0) |

5. Click **Save**
6. Note the **Custom API ID** (you'll need it for parameters)

### Option B: PowerShell Script

```powershell
# Connect to Dataverse
Import-Module Microsoft.Xrm.Data.PowerShell
$conn = Get-CrmConnection -InteractiveMode

# Create Custom API
$customApi = @{
    "uniquename" = "sprk_GetDocumentFileUrl"
    "name" = "Get Document File URL"
    "displayname" = "Get Document File URL"
    "description" = "Server-side proxy for getting SharePoint Embedded file URLs from SDAP BFF API"
    "bindingtype" = 1  # Entity
    "boundentitylogicalname" = "sprk_document"
    "isfunction" = $true
    "isprivate" = $false
    "allowedcustomprocessingsteptype" = 0  # None (sync only)
}

$customApiId = New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
Write-Host "Custom API created with ID: $customApiId"
```

---

## 🚀 Step 4: Create Custom API Parameters

### Input Parameter: EndpointType

```powershell
# Create EndpointType parameter
$inputParam = @{
    "uniquename" = "EndpointType"
    "name" = "EndpointType"
    "displayname" = "Endpoint Type"
    "description" = "Type of file URL to retrieve: preview, content, or office"
    "type" = 0  # String
    "isoptional" = $false
    "customapiid@odata.bind" = "/customapis({custom-api-id})"  # Replace with ID from Step 3
}

New-CrmRecord -conn $conn -EntityLogicalName "customapirequestparameter" -Fields $inputParam
```

### Output Parameters

```powershell
# Output Parameter 1: FileUrl
$param1 = @{
    "uniquename" = "FileUrl"
    "name" = "FileUrl"
    "displayname" = "File URL"
    "description" = "The ephemeral file URL (expires in 5-10 minutes)"
    "type" = 0  # String
    "customapiid@odata.bind" = "/customapis({custom-api-id})"
}
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param1

# Output Parameter 2: FileName
$param2 = @{
    "uniquename" = "FileName"
    "name" = "FileName"
    "displayname" = "File Name"
    "type" = 0  # String
    "customapiid@odata.bind" = "/customapis({custom-api-id})"
}
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param2

# Output Parameter 3: FileSize
$param3 = @{
    "uniquename" = "FileSize"
    "name" = "FileSize"
    "displayname" = "File Size"
    "description" = "File size in bytes"
    "type" = 1  # Integer
    "customapiid@odata.bind" = "/customapis({custom-api-id})"
}
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param3

# Output Parameter 4: ContentType
$param4 = @{
    "uniquename" = "ContentType"
    "name" = "ContentType"
    "displayname" = "Content Type"
    "description" = "MIME type"
    "type" = 0  # String
    "customapiid@odata.bind" = "/customapis({custom-api-id})"
}
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param4

# Output Parameter 5: ExpiresAt
$param5 = @{
    "uniquename" = "ExpiresAt"
    "name" = "ExpiresAt"
    "displayname" = "Expires At"
    "description" = "When the file URL expires (UTC)"
    "type" = 2  # DateTime
    "customapiid@odata.bind" = "/customapis({custom-api-id})"
}
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $param5
```

---

## 🚀 Step 5: Register Plugin Step

### Using Plugin Registration Tool

1. **In Plugin Registration Tool**, expand your registered assembly
2. **Right-click** on `GetDocumentFileUrlPlugin` → **Register New Step**
3. Fill in:

| Field | Value |
|-------|-------|
| **Message** | `sprk_GetDocumentFileUrl` |
| **Primary Entity** | `sprk_document` |
| **Event Pipeline Stage** | Main Operation (30) |
| **Execution Mode** | Synchronous |
| **Deployment** | Server |
| **User Context** | Calling User |

4. Click **Register New Step**
5. ✅ **Success**: You should see the step under the plugin

---

## 🚀 Step 6: Publish Customizations

```powershell
# Publish all customizations
Publish-CrmAllCustomization -conn $conn
```

Or in Power Apps:
- **Settings** → **Customizations** → **Publish All Customizations**

---

## 🚀 Step 7: Upload Web Resource

### Option A: Manual (Power Apps)

1. Open your solution in **make.powerapps.com**
2. Click **New** → **More** → **Web Resource**
3. Upload file:
   ```
   C:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\src\WebResources\sprk_document_file_viewer.html
   ```
4. Properties:
   - **Name**: `sprk_document_file_viewer`
   - **Display Name**: `Document File Viewer`
   - **Type**: Webpage (HTML)
5. Click **Save**
6. Click **Publish**

### Option B: PAC CLI

```bash
# Add web resource to solution
pac solution add-reference \
    --path c:/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreateSolution

# Or manually import via solution
```

---

## 🚀 Step 8: Add Web Resource to Document Form

1. Open **Document** entity form in **Form Designer**
2. Add new **Section**: "File Preview"
3. Add **Web Resource** control:
   - Web Resource: `sprk_document_file_viewer`
   - Width: **Full width**
   - Height: **600px**
   - Border: **No**
4. Click **Save**
5. Click **Publish**

---

## 🧪 Step 9: Test!

### Test 1: Browser Console Test

1. Open a **Document record** with a file
2. Open **browser console** (F12)
3. Run this code:

```javascript
const documentId = Xrm.Page.data.entity.getId().replace(/[{}]/g, '');

Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": {
                    "typeName": "mscrm.sprk_document",
                    "structuralProperty": 5
                },
                "EndpointType": {
                    "typeName": "Edm.String",
                    "structuralProperty": 1
                }
            },
            operationType: 1,
            operationName: "sprk_GetDocumentFileUrl"
        };
    },
    entity: {
        entityType: "sprk_document",
        id: documentId
    },
    EndpointType: "content"
}).then(
    result => {
        console.log("✅ Success!", result);
        console.log("File URL:", result.FileUrl);
    },
    error => console.error("❌ Error:", error.message)
);
```

**Expected Output**:
```javascript
✅ Success!
{
  FileUrl: "https://spaarke.sharepoint.com/...",
  FileName: "document.pdf",
  FileSize: 102400,
  ContentType: "application/pdf",
  ExpiresAt: "2025-01-21T16:30:00Z"
}
```

### Test 2: Full Integration Test

1. **Upload a file**:
   - Navigate to Matter/Project record
   - Click "Add Documents"
   - Upload PDF or Office file
   - Save and create Document

2. **Open Document record**:
   - File should load automatically in iframe
   - No authentication errors!
   - Office files open in Office Online (editable)
   - PDFs display in browser viewer

3. **Test auto-refresh**:
   - Wait 4 minutes
   - Check console for "Auto-refreshing preview URL"
   - Verify preview stays active

---

## 🐛 Troubleshooting

### Issue: "External service config not found: SDAP_BFF_API"

**Cause**: External Service Config record not created or wrong name

**Fix**:
1. Verify record exists in `sprk_externalserviceconfig`
2. Check `sprk_name` = "SDAP_BFF_API" (exact match)
3. Verify `sprk_isenabled` = true

### Issue: "Failed to acquire access token"

**Cause**: Service principal credentials invalid or missing permissions

**Fix**:
1. Verify ClientId, ClientSecret, TenantId in config
2. Test credentials:
   ```bash
   az login --service-principal \
       --username {client-id} \
       --password {client-secret} \
       --tenant {tenant-id}
   ```
3. Grant API permissions to SDAP BFF API

### Issue: Custom API not appearing in Xrm.WebApi

**Cause**: Custom API not published or parameters missing

**Fix**:
1. Publish all customizations
2. Verify Custom API record exists
3. Verify all 5 output parameters created
4. Clear browser cache

### Issue: Plugin trace shows "Authentication failed"

**Cause**: SDAP BFF API rejecting service principal token

**Fix**:
1. Check SDAP BFF API logs in Azure
2. Verify API accepts the service principal
3. Verify scope matches API configuration

---

## 📊 Audit Trail

All Custom API calls are automatically logged to `sprk_proxyauditlog` with:
- Correlation ID
- User ID
- Request/response payloads (sensitive data redacted)
- Duration
- Success/failure status

**Query audit logs**:
```javascript
Xrm.WebApi.retrieveMultipleRecords(
    "sprk_proxyauditlog",
    "?$filter=sprk_operation eq 'GetDocumentFileUrl'&$orderby=sprk_executiontime desc&$top=10"
).then(result => console.log(result.entities));
```

---

## ✅ Success Checklist

- [ ] External Service Config created (Step 1)
- [ ] Plugin assembly registered (Step 2)
- [ ] Custom API record created (Step 3)
- [ ] Custom API parameters created (Step 4)
- [ ] Plugin step registered (Step 5)
- [ ] All customizations published (Step 6)
- [ ] Web resource uploaded (Step 7)
- [ ] Web resource added to Document form (Step 8)
- [ ] Browser console test passes (Step 9.1)
- [ ] Full integration test passes (Step 9.2)

---

**Deployment Date**: 2025-01-21
**Status**: Ready for deployment
**Estimated Time**: 30-45 minutes

