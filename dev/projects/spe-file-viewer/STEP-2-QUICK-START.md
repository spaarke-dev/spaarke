# Step 2: Quick Start Guide

## Prerequisites ✅

- Plugin DLL: `c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll`
- BFF API URL: `https://spe-api-dev-67e2xz.azurewebsites.net`
- Dataverse: `https://spaarkedev1.api.crm.dynamics.com`

---

## Option A: Automated Script (Recommended)

### 1. Get Client Secret

```bash
az keyvault secret show --vault-name spaarke-spekvcert --name BFF-API-ClientSecret --query "value" -o tsv
```

Copy the output.

### 2. Update Script

1. Open: `c:\code_files\spaarke\dev\projects\spe-file-viewer\step2-registration-script.ps1`
2. Find line: `ClientSecret = ""`
3. Paste the secret between the quotes
4. Save file

### 3. Install PowerShell Module (if needed)

```powershell
Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser
```

### 4. Run Script

```powershell
cd c:\code_files\spaarke\dev\projects\spe-file-viewer
.\step2-registration-script.ps1
```

The script will:
- ✅ Create External Service Config automatically
- ⏸️ Pause for you to register plugin assembly (manual)
- ✅ Create Custom API automatically
- ✅ Create all 6 output parameters automatically
- ⏸️ Pause for you to register plugin step (manual)
- ✅ Publish customizations automatically

---

## Option B: Manual Steps

### Task 2.1: Create External Service Config

**PowerShell**:
```powershell
Import-Module Microsoft.Xrm.Data.PowerShell
$conn = Get-CrmConnection -InteractiveMode

# Get client secret first:
# az keyvault secret show --vault-name spaarke-spekvcert --name BFF-API-ClientSecret --query "value" -o tsv

$config = @{
    "sprk_name" = "SDAP_BFF_API"
    "sprk_baseurl" = "https://spe-api-dev-67e2xz.azurewebsites.net/api"
    "sprk_isenabled" = $true
    "sprk_authtype" = 1
    "sprk_tenantid" = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
    "sprk_clientid" = "1e40baad-e065-4aea-a8d4-4b7ab273458c"
    "sprk_clientsecret" = "{PASTE-SECRET-HERE}"
    "sprk_scope" = "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
    "sprk_timeout" = 300
    "sprk_retrycount" = 3
    "sprk_retrydelay" = 1000
}

New-CrmRecord -conn $conn -EntityLogicalName "sprk_externalserviceconfig" -Fields $config
```

### Task 2.2: Register Plugin Assembly

1. **Launch PRT**:
   ```bash
   pac tool prt
   ```

2. **Connect**:
   - Create New Connection → Office 365
   - Select **SPAARKE DEV 1** environment

3. **Register Assembly**:
   - Register → Register New Assembly
   - Browse to: `c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll`
   - Isolation Mode: **Sandbox**
   - Location: **Database**
   - Click **Register Selected Plugins**

4. **Verify**:
   - Expand `Spaarke.Dataverse.CustomApiProxy`
   - Should see `GetFilePreviewUrlPlugin` ✅

### Task 2.3: Create Custom API

**PowerShell**:
```powershell
$customApi = @{
    "uniquename" = "sprk_GetFilePreviewUrl"
    "name" = "Get File Preview URL"
    "displayname" = "Get File Preview URL"
    "description" = "Server-side proxy for getting SharePoint Embedded preview URLs"
    "bindingtype" = 1
    "boundentitylogicalname" = "sprk_document"
    "isfunction" = $true
    "isprivate" = $false
    "allowedcustomprocessingsteptype" = 0
}

$customApiId = New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
Write-Host "Custom API ID: $customApiId"
```

### Task 2.4: Create Output Parameters

**PowerShell** (replace `{CUSTOM-API-ID}` with ID from Task 2.3):
```powershell
$customApiId = "{CUSTOM-API-ID}"

# Parameter 1: PreviewUrl
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields @{
    "uniquename" = "PreviewUrl"; "name" = "PreviewUrl"; "displayname" = "Preview URL"
    "description" = "Ephemeral preview URL (expires in ~10 minutes)"; "type" = 10
    "customapiid@odata.bind" = "/customapis($customApiId)"
}

# Parameter 2: FileName
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields @{
    "uniquename" = "FileName"; "name" = "FileName"; "displayname" = "File Name"
    "description" = "File name for display"; "type" = 10
    "customapiid@odata.bind" = "/customapis($customApiId)"
}

# Parameter 3: FileSize
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields @{
    "uniquename" = "FileSize"; "name" = "FileSize"; "displayname" = "File Size"
    "description" = "File size in bytes"; "type" = 6
    "customapiid@odata.bind" = "/customapis($customApiId)"
}

# Parameter 4: ContentType
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields @{
    "uniquename" = "ContentType"; "name" = "ContentType"; "displayname" = "Content Type"
    "description" = "MIME type"; "type" = 10
    "customapiid@odata.bind" = "/customapis($customApiId)"
}

# Parameter 5: ExpiresAt
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields @{
    "uniquename" = "ExpiresAt"; "name" = "ExpiresAt"; "displayname" = "Expires At"
    "description" = "When the preview URL expires (UTC)"; "type" = 8
    "customapiid@odata.bind" = "/customapis($customApiId)"
}

# Parameter 6: CorrelationId
New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields @{
    "uniquename" = "CorrelationId"; "name" = "CorrelationId"; "displayname" = "Correlation ID"
    "description" = "Request tracking ID for tracing and debugging"; "type" = 10
    "customapiid@odata.bind" = "/customapis($customApiId)"
}
```

### Task 2.5: Register Plugin Step

**In Plugin Registration Tool**:
1. Expand `Spaarke.Dataverse.CustomApiProxy` assembly
2. Right-click `GetFilePreviewUrlPlugin` → **Register New Step**
3. Fill in:
   - **Message**: `sprk_GetFilePreviewUrl`
   - **Primary Entity**: `sprk_document`
   - **Event Pipeline Stage**: `Main Operation (30)`
   - **Execution Mode**: `Synchronous`
4. Click **Register New Step**

### Task 2.6: Publish Customizations

**PowerShell**:
```powershell
Publish-CrmAllCustomization -conn $conn
```

---

## Quick Test

Open any Document record in Dataverse, open browser console (F12), and run:

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
                }
            },
            operationType: 1,
            operationName: "sprk_GetFilePreviewUrl"
        };
    },
    entity: {
        entityType: "sprk_document",
        id: documentId
    }
}).then(
    result => console.log("✅ Success!", result),
    error => console.error("❌ Error:", error.message)
);
```

**Expected**: Should return preview URL, file metadata, and correlation ID.

---

## Common Issues

| Issue | Fix |
|-------|-----|
| "External service config not found" | Verify `sprk_name` = "SDAP_BFF_API" (exact) |
| "Failed to acquire access token" | Check client secret is correct |
| Custom API not appearing | Publish customizations, clear browser cache |
| "BFF API returned 401" | Check service principal permissions |

---

## Next Steps

Once validation passes, proceed to:
- **Step 3**: PCF Control Development
