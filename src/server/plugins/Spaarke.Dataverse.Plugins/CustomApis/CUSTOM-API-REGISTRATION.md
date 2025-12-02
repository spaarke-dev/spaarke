# Custom API Registration Guide

## sprk_GetDocumentFileUrl Custom API

**Purpose**: Server-side proxy for getting SharePoint Embedded file URLs without client-side authentication.

**Plugin Class**: `Spaarke.Dataverse.Plugins.CustomApis.GetDocumentFileUrlPlugin`

---

## Registration Steps

### Step 1: Register the Plugin Assembly

1. Build the plugin project:
   ```bash
   cd c:/code_files/spaarke/src/plugins/Spaarke.Dataverse.Plugins
   dotnet build -c Release
   ```

2. Register the assembly using Plugin Registration Tool or PAC CLI:
   ```bash
   pac plugin push --assembly bin/Release/net462/Spaarke.Dataverse.Plugins.dll
   ```

---

### Step 2: Create Custom API Record

Use the following script or manually create in Power Apps:

#### **Custom API (customapi) Record**

| Field | Value |
|-------|-------|
| **Unique Name** | `sprk_GetDocumentFileUrl` |
| **Name** | `Get Document File URL` |
| **Display Name** | `Get Document File URL` |
| **Description** | `Server-side proxy for getting SharePoint Embedded file URLs from SDAP BFF API` |
| **Binding Type** | Entity (1) |
| **Bound Entity Logical Name** | `sprk_document` |
| **Is Function** | Yes (true) |
| **Is Private** | No (false) |
| **Allowed Custom Processing Step Type** | None (0) - synchronous only |
| **Execute Privilege Name** | (leave empty - uses entity privileges) |

#### PowerShell Script to Create Custom API:

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

New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
```

---

### Step 3: Create Input Parameters

#### **Input Parameter 1: EndpointType**

| Field | Value |
|-------|-------|
| **Unique Name** | `EndpointType` |
| **Name** | `EndpointType` |
| **Display Name** | `Endpoint Type` |
| **Description** | `Type of file URL to retrieve: "preview", "content", or "office"` |
| **Type** | String (0) |
| **Is Optional** | No (false) |
| **Custom API** | sprk_GetDocumentFileUrl |

```powershell
# Create EndpointType parameter
$param1 = @{
    "uniquename" = "EndpointType"
    "name" = "EndpointType"
    "displayname" = "Endpoint Type"
    "description" = "Type of file URL to retrieve: preview, content, or office"
    "type" = 0  # String
    "isoptional" = $false
    "customapiid@odata.bind" = "/customapis(uniquename='sprk_GetDocumentFileUrl')"
}

New-CrmRecord -conn $conn -EntityLogicalName "customapirequestparameter" -Fields $param1
```

---

### Step 4: Create Output Parameters

#### **Output Parameter 1: FileUrl**

| Field | Value |
|-------|-------|
| **Unique Name** | `FileUrl` |
| **Name** | `FileUrl` |
| **Display Name** | `File URL` |
| **Description** | `The ephemeral file URL (expires in 5-10 minutes)` |
| **Type** | String (0) |
| **Custom API** | sprk_GetDocumentFileUrl |

#### **Output Parameter 2: FileName**

| Field | Value |
|-------|-------|
| **Unique Name** | `FileName` |
| **Name** | `FileName` |
| **Display Name** | `File Name` |
| **Type** | String (0) |
| **Custom API** | sprk_GetDocumentFileUrl |

#### **Output Parameter 3: FileSize**

| Field | Value |
|-------|-------|
| **Unique Name** | `FileSize` |
| **Name** | `FileSize` |
| **Display Name** | `File Size` |
| **Description** | `File size in bytes` |
| **Type** | Integer (1) |
| **Custom API** | sprk_GetDocumentFileUrl |

#### **Output Parameter 4: ContentType**

| Field | Value |
|-------|-------|
| **Unique Name** | `ContentType` |
| **Name** | `ContentType` |
| **Display Name** | `Content Type` |
| **Description** | `MIME type (e.g., application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document)` |
| **Type** | String (0) |
| **Custom API** | sprk_GetDocumentFileUrl |

#### **Output Parameter 5: ExpiresAt**

| Field | Value |
|-------|-------|
| **Unique Name** | `ExpiresAt` |
| **Name** | `ExpiresAt` |
| **Display Name** | `Expires At` |
| **Description** | `When the file URL expires (UTC)` |
| **Type** | DateTime (2) |
| **Custom API** | sprk_GetDocumentFileUrl |

```powershell
# Create output parameters
$outputs = @(
    @{
        "uniquename" = "FileUrl"
        "name" = "FileUrl"
        "displayname" = "File URL"
        "description" = "The ephemeral file URL (expires in 5-10 minutes)"
        "type" = 0  # String
    },
    @{
        "uniquename" = "FileName"
        "name" = "FileName"
        "displayname" = "File Name"
        "type" = 0  # String
    },
    @{
        "uniquename" = "FileSize"
        "name" = "FileSize"
        "displayname" = "File Size"
        "description" = "File size in bytes"
        "type" = 1  # Integer
    },
    @{
        "uniquename" = "ContentType"
        "name" = "ContentType"
        "displayname" = "Content Type"
        "description" = "MIME type"
        "type" = 0  # String
    },
    @{
        "uniquename" = "ExpiresAt"
        "name" = "ExpiresAt"
        "displayname" = "Expires At"
        "description" = "When the file URL expires (UTC)"
        "type" = 2  # DateTime
    }
)

foreach ($output in $outputs) {
    $output["customapiid@odata.bind"] = "/customapis(uniquename='sprk_GetDocumentFileUrl')"
    New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $output
}
```

---

### Step 5: Register Plugin Step

Register the plugin to execute on the Custom API:

| Field | Value |
|-------|-------|
| **Message** | `sprk_GetDocumentFileUrl` |
| **Primary Entity** | `sprk_document` |
| **Event Pipeline Stage** | Main Operation (30) |
| **Execution Mode** | Synchronous |
| **Plugin Type** | `Spaarke.Dataverse.Plugins.CustomApis.GetDocumentFileUrlPlugin` |

Using Plugin Registration Tool:
1. Right-click assembly → **Register New Step**
2. Message: `sprk_GetDocumentFileUrl`
3. Primary Entity: `sprk_document`
4. Stage: Main Operation (30)
5. Mode: Synchronous

---

## Configuration Requirements

### Azure AD App Registration

The plugin needs credentials to call SDAP BFF API. You need to:

1. **Create or use existing service principal**:
   - App registration in Azure AD
   - Client ID + Client Secret
   - API permission: Access to SDAP BFF API scope

2. **Store credentials in Dataverse Secure Configuration or Key Vault**:
   ```json
   {
     "SdapApi": {
       "BaseUrl": "https://spe-api-dev-67e2xz.azurewebsites.net/api",
       "ClientId": "{client-id}",
       "ClientSecret": "{client-secret}",
       "TenantId": "{tenant-id}",
       "Scope": "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
     }
   }
   ```

3. **Update plugin code** to read configuration:
   - Modify `GetAccessToken()` method
   - Implement token acquisition from Azure AD

---

## Testing the Custom API

### Test with Xrm.WebApi (JavaScript)

```javascript
// In web resource or browser console
const documentId = "550e8400-e29b-41d4-a716-446655440000";

// Call Custom API using Xrm.WebApi
Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": {
                    "typeName": "mscrm.sprk_document",
                    "structuralProperty": 5  // Entity
                },
                "EndpointType": {
                    "typeName": "Edm.String",
                    "structuralProperty": 1  // PrimitiveType
                }
            },
            operationType: 1,  // Function
            operationName: "sprk_GetDocumentFileUrl"
        };
    },

    // Bound entity (Document record)
    entity: {
        entityType: "sprk_document",
        id: documentId
    },

    // Input parameters
    EndpointType: "content"

}).then(
    function success(result) {
        console.log("File URL:", result.FileUrl);
        console.log("File Name:", result.FileName);
        console.log("File Size:", result.FileSize);
        console.log("Expires At:", result.ExpiresAt);

        // Use the URL
        document.getElementById('fileViewer').src = result.FileUrl;
    },
    function error(err) {
        console.error("Error:", err.message);
    }
);
```

### Test with PowerFx (Canvas App or Custom Page)

```powerfx
Set(
    fileUrlResult,
    'sprk_document'.sprk_GetDocumentFileUrl(
        {
            '@odata.id': Concatenate(
                "https://org.crm.dynamics.com/api/data/v9.2/sprk_documents(",
                varDocumentId,
                ")"
            )
        },
        {
            EndpointType: "content"
        }
    )
);

// Use the result
Set(varFileUrl, fileUrlResult.FileUrl);
Set(varFileName, fileUrlResult.FileName);
```

### Test with Postman

```http
POST https://org.crm.dynamics.com/api/data/v9.2/sprk_documents(550e8400-e29b-41d4-a716-446655440000)/Microsoft.Dynamics.CRM.sprk_GetDocumentFileUrl
Authorization: Bearer {dataverse-token}
Content-Type: application/json

{
  "EndpointType": "content"
}
```

**Response:**
```json
{
  "@odata.context": "https://org.crm.dynamics.com/api/data/v9.2/$metadata#Microsoft.Dynamics.CRM.sprk_GetDocumentFileUrlResponse",
  "FileUrl": "https://spaarke.sharepoint.com/...",
  "FileName": "document.pdf",
  "FileSize": 102400,
  "ContentType": "application/pdf",
  "ExpiresAt": "2025-01-20T15:25:00Z"
}
```

---

## Security Considerations

### User Permissions

- Custom API respects Dataverse entity security
- User must have **Read** privilege on Document (sprk_document) entity
- Plugin runs in **user context** by default
- User Access Control (UAC) is enforced

### Token Security

- ✅ No tokens exposed to client/browser
- ✅ All authentication server-side in plugin
- ✅ Service principal credentials stored securely (Key Vault or Secure Configuration)
- ✅ Short-lived file URLs (5-10 min expiration)
- ✅ Audit trail via plugin tracing

---

## Troubleshooting

### Issue: "Token acquisition not yet implemented"

**Cause**: Plugin code has placeholder for token acquisition

**Fix**: Update `GetAccessToken()` method in plugin:

```csharp
private string GetAccessToken(ITracingService tracingService)
{
    // Get configuration from secure config or Key Vault
    var config = GetSecureConfiguration();  // Implement this

    // Call Azure AD token endpoint
    var tokenEndpoint = $"https://login.microsoftonline.com/{config.TenantId}/oauth2/v2.0/token";

    var tokenRequest = new Dictionary<string, string>
    {
        ["client_id"] = config.ClientId,
        ["client_secret"] = config.ClientSecret,
        ["scope"] = config.Scope,
        ["grant_type"] = "client_credentials"
    };

    using (var httpClient = new HttpClient())
    {
        var response = httpClient.PostAsync(tokenEndpoint,
            new FormUrlEncodedContent(tokenRequest)).GetAwaiter().GetResult();

        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

        return tokenResponse.access_token;
    }
}
```

### Issue: Custom API not appearing in Xrm.WebApi

**Cause**: Custom API not published or registration incomplete

**Fix**:
1. Verify Custom API record exists with correct Unique Name
2. Verify plugin step is registered
3. Publish all customizations
4. Clear browser cache

### Issue: "Failed to get file URL" error

**Cause**: SDAP BFF API not accessible or authentication failed

**Fix**:
1. Check plugin trace logs for detailed error
2. Verify SDAP BFF API is deployed and healthy
3. Verify service principal has correct permissions
4. Test SDAP BFF API directly with Postman

---

## Next Steps

1. **Build and register plugin**
2. **Configure Azure AD app registration**
3. **Update web resource HTML** to call Custom API instead of direct SDAP BFF
4. **Test end-to-end** file viewing

---

**Last Updated**: 2025-01-20
**Status**: Ready for implementation
