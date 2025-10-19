# Container Type Registration Guide
## Registering BFF API with SharePoint Embedded Container Type

**Purpose:** Grant the BFF API application permission to access SPE containers on behalf of users

**Status:** 🟡 READY TO EXECUTE

---

## Quick Reference

| Item | Value |
|------|-------|
| **Container Type ID** | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` |
| **BFF API App ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Required Permission** | `WriteContent` (delegated) |
| **Registration Endpoint** | `/_api/v2.1/storageContainerTypes/{id}/applicationPermissions` |

---

## Prerequisites

Before starting, you need:

### 1. Tenant Information
- **Tenant ID:** `a221a95e-6abc-4434-aecc-e48338a1b2f2` ✅
- **Tenant SharePoint URL:** `https://[tenant].sharepoint.com` ❓ (need to obtain)

To find SharePoint URL:
```powershell
# Option 1: Azure Portal
# Go to Azure AD → Overview → look for "Primary domain"
# SharePoint URL is typically: https://[domain].sharepoint.com

# Option 2: PowerShell
Connect-AzAccount -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2
$domain = (Get-AzTenant -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2).Domains | Where-Object {$_.Type -eq "Managed"} | Select-Object -First 1
Write-Host "SharePoint URL: https://$($domain.Name.Split('.')[0]).sharepoint.com"
```

### 2. Owning Application Credentials

The **owning application** is the Azure AD app that originally created/owns the container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06`.

**How to identify owning application:**

```powershell
# Install PnP PowerShell if not already installed
Install-Module -Name PnP.PowerShell -Scope CurrentUser

# Connect to SharePoint Admin
$tenantUrl = "https://[tenant]-admin.sharepoint.com"
Connect-PnPOnline -Url $tenantUrl -Interactive

# List container types to find owning app
$containerTypes = Get-PnPContainerType
$ourType = $containerTypes | Where-Object {$_.ContainerTypeId -eq "8a6ce34c-6055-4681-8f87-2f4f9f921c06"}

Write-Host "Container Type: $($ourType.DisplayName)"
Write-Host "Owning Application ID: $($ourType.OwningApplicationId)"
```

**Required from owning app:**
- Application (Client) ID
- Client Secret OR Certificate
- Must have service principal in tenant
- Must have admin consent for `Container.Selected` (SharePoint resource, not Graph!)

### 3. Authentication Token

Need token with:
- **Audience:** SharePoint resource (`https://[tenant].sharepoint.com`)
- **Permission:** `Container.Selected` (app-only)
- **Authenticated as:** Owning application

---

## Step-by-Step Registration

### Step 1: Acquire Token (Owning Application)

Using PowerShell with MSAL.PS:

```powershell
# Install MSAL.PS if needed
Install-Module -Name MSAL.PS -Scope CurrentUser

# Configuration
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$clientId = "[OWNING_APP_CLIENT_ID]"  # From prerequisites
$clientSecret = "[OWNING_APP_CLIENT_SECRET]"  # From prerequisites
$tenantName = "[TENANT_NAME]"  # e.g., "spaarkedev1"
$resource = "https://$tenantName.sharepoint.com"

# Get token
$secureSecret = ConvertTo-SecureString $clientSecret -AsPlainText -Force
$token = Get-MsalToken `
    -ClientId $clientId `
    -ClientSecret $secureSecret `
    -TenantId $tenantId `
    -Scopes "$resource/.default"

$accessToken = $token.AccessToken
Write-Host "Token acquired successfully"
Write-Host "Expires: $($token.ExpiresOn)"
```

**Alternative: Using Azure CLI**

```bash
# Login as owning application
az login --service-principal \
    --username [OWNING_APP_CLIENT_ID] \
    --password [OWNING_APP_CLIENT_SECRET] \
    --tenant a221a95e-6abc-4434-aecc-e48338a1b2f2

# Get token for SharePoint
az account get-access-token \
    --resource "https://[tenant].sharepoint.com" \
    --query accessToken \
    --output tsv
```

### Step 2: Register BFF API Application

Using PowerShell:

```powershell
# Configuration
$containerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
$bffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"
$sharepointUrl = "https://$tenantName.sharepoint.com"

# Build registration payload
$registrationBody = @{
    value = @(
        @{
            appId = $bffApiAppId
            delegated = @("WriteContent")
            appOnly = @()
        }
    )
} | ConvertTo-Json -Depth 3

# API endpoint
$endpoint = "$sharepointUrl/_api/v2.1/storageContainerTypes/$containerTypeId/applicationPermissions"

# Register application
$response = Invoke-RestMethod `
    -Uri $endpoint `
    -Method Put `
    -Headers @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    } `
    -Body $registrationBody

Write-Host "Registration successful!" -ForegroundColor Green
$response | ConvertTo-Json -Depth 3
```

**Alternative: Using cURL**

```bash
CONTAINER_TYPE_ID="8a6ce34c-6055-4681-8f87-2f4f9f921c06"
BFF_API_APP_ID="1e40baad-e065-4aea-a8d4-4b7ab273458c"
SHAREPOINT_URL="https://[tenant].sharepoint.com"
ACCESS_TOKEN="[token-from-step-1]"

curl -X PUT "$SHAREPOINT_URL/_api/v2.1/storageContainerTypes/$CONTAINER_TYPE_ID/applicationPermissions" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -d '{
    "value": [
      {
        "appId": "'"$BFF_API_APP_ID"'",
        "delegated": ["WriteContent"],
        "appOnly": []
      }
    ]
  }'
```

**Expected Response:**

```json
{
  "value": [
    {
      "id": "[generated-permission-id]",
      "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
      "appDisplayName": "SPE-BFF-API",
      "delegated": ["WriteContent"],
      "appOnly": []
    }
  ]
}
```

### Step 3: Verify Registration

```powershell
# Query container type permissions
$endpoint = "$sharepointUrl/_api/v2.1/storageContainerTypes/$containerTypeId/applicationPermissions"

$permissions = Invoke-RestMethod `
    -Uri $endpoint `
    -Method Get `
    -Headers @{
        "Authorization" = "Bearer $accessToken"
        "Accept" = "application/json"
    }

# Check for BFF API
$bffPermission = $permissions.value | Where-Object {$_.appId -eq $bffApiAppId}

if ($bffPermission) {
    Write-Host "✅ BFF API is registered" -ForegroundColor Green
    Write-Host "   App ID: $($bffPermission.appId)"
    Write-Host "   Delegated Permissions: $($bffPermission.delegated -join ', ')"
    Write-Host "   App-Only Permissions: $($bffPermission.appOnly -join ', ')"
} else {
    Write-Host "❌ BFF API is NOT registered" -ForegroundColor Red
}
```

---

## Permission Levels Explained

When registering, you can grant different permission levels:

| Permission | Delegated (User Context) | App-Only (No User) | Use Case |
|------------|--------------------------|---------------------|----------|
| `None` | No access | No access | Revoke access |
| `ReadContent` | Read files as user | Read files as app | View files |
| `WriteContent` | Create/update files as user | Create/update files as app | Upload files |
| `Delete` | Delete files as user | Delete files as app | Remove files |
| `Full` | All operations as user | All operations as app | Complete access |

**For our use case:**
- Need: `WriteContent` in `delegated` array
- Reason: BFF API uploads files on behalf of user (OBO flow)

**Why not `appOnly`?**
- BFF API uses OBO flow (delegated context)
- User permissions are checked (owner/contributor/reader)
- `appOnly` would bypass user permissions (not desired)

---

## Testing After Registration

### Test 1: OBO Upload Endpoint

```powershell
# First, get Token A from PCF app (via Postman or browser)
# Token A audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
# Token A appid: 170c98e1-d486-4355-bcbe-170454e0207c

$tokenA = "[paste-token-from-pcf-auth]"
$bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"
$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"

# Test file upload
$testContent = "This is a test file created after container type registration."

Invoke-RestMethod `
    -Uri "$bffApiUrl/api/obo/containers/$containerId/files/registration-test.txt" `
    -Method Put `
    -Headers @{
        "Authorization" = "Bearer $tokenA"
        "Content-Type" = "text/plain"
    } `
    -Body $testContent

Write-Host "✅ Upload successful!" -ForegroundColor Green
```

**Expected Results:**

**Before Registration:**
```json
{
  "status": 403,
  "title": "Forbidden",
  "detail": "Access denied"
}
```

**After Registration:**
```json
{
  "id": "01...",
  "name": "registration-test.txt",
  "size": 64,
  "webUrl": "https://...",
  "createdDateTime": "2025-10-09T...",
  "lastModifiedDateTime": "2025-10-09T..."
}
```

### Test 2: PCF Control Upload

1. Open Dataverse environment (SPAARKE DEV 1)
2. Navigate to Matter entity
3. Click "New" (opens Quick Create form)
4. UniversalQuickCreate control should be visible
5. Select file(s) using file picker
6. Fill required fields (Matter name, etc.)
7. Click "Save"
8. **Expected:** Files upload successfully, success message displayed
9. **Verify:** Check Application Insights logs for success messages

### Test 3: Application Insights Logs

Check for these log messages after upload:

```
✅ Success indicators:
- "OBO token exchange successful"
- "OBO token scopes: [includes FileStorageContainer.Selected]"
- "OBO upload successful - DriveItemId: {itemId}"

❌ Failure indicators (if registration incomplete):
- "OBO upload failed - Graph API error"
- "Access denied uploading to container"
```

---

## Troubleshooting

### Problem: "Access denied" on registration API call

**Cause:** Owning application lacks `Container.Selected` permission

**Solution:**
1. Go to Azure Portal → App Registrations → [Owning App]
2. Navigate to "API Permissions"
3. Click "Add a permission"
4. Select "SharePoint" (not Microsoft Graph!)
5. Choose "Application permissions"
6. Select `Container.Selected`
7. Click "Grant admin consent"

### Problem: "Container type not found"

**Cause:** Container type ID incorrect or not registered in tenant

**Solution:**
```powershell
# List all container types in tenant
Connect-PnPOnline -Url "https://[tenant]-admin.sharepoint.com" -Interactive
Get-PnPContainerType | Select-Object ContainerTypeId, DisplayName, OwningApplicationId
```

### Problem: 403 error persists after registration

**Possible causes:**

1. **Token caching in BFF API**
   - Solution: Restart App Service to clear MSAL cache
   ```bash
   az webapp restart --name spe-api-dev-67e2xz --resource-group [resource-group]
   ```

2. **Wrong permission level granted**
   - Solution: Re-run registration with `WriteContent` (not `ReadContent`)

3. **User lacks container permissions**
   - Solution: Verify user has owner/contributor role on specific container
   ```http
   GET https://graph.microsoft.com/beta/storage/fileStorage/containers/{containerId}/permissions
   ```

4. **BFF API app ID mismatch**
   - Verify: Check Token B `appid` claim in Application Insights logs
   - Should be: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

### Problem: Cannot find owning application

**Investigation steps:**

```powershell
# Option 1: Via PnP PowerShell
Connect-PnPOnline -Url "https://[tenant]-admin.sharepoint.com" -Interactive
$type = Get-PnPContainerType | Where-Object {$_.ContainerTypeId -eq "8a6ce34c-6055-4681-8f87-2f4f9f921c06"}
$type.OwningApplicationId

# Option 2: Via Graph API (if container type created via Graph)
$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
$container = Invoke-RestMethod `
    -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers/$containerId" `
    -Headers @{"Authorization" = "Bearer $graphToken"}

$container.containerTypeId  # Should match 8a6ce34c-6055-4681-8f87-2f4f9f921c06
```

---

## Alternative: Using Microsoft 365 CLI

If you prefer the Microsoft 365 CLI tool:

```bash
# Install M365 CLI
npm install -g @pnp/cli-microsoft365

# Login as owning application
m365 login --authType certificate \
    --appId [OWNING_APP_CLIENT_ID] \
    --certificateFile ./cert.pfx \
    --tenant a221a95e-6abc-4434-aecc-e48338a1b2f2

# Register BFF API with container type
m365 spe containertype applicationpermission add \
    --containerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06 \
    --appId 1e40baad-e065-4aea-a8d4-4b7ab273458c \
    --permission WriteContent \
    --grantType Delegated

# Verify registration
m365 spe containertype applicationpermission list \
    --containerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06
```

---

## Environment Checklist

After successful registration in Dev, repeat for other environments:

### Dev Environment ✅
- [ ] Obtain tenant SharePoint URL
- [ ] Identify owning application
- [ ] Acquire authentication token
- [ ] Register BFF API with container type
- [ ] Verify registration
- [ ] Test OBO upload endpoint
- [ ] Test PCF control upload
- [ ] Document process

### Test Environment
- [ ] Identify Test environment container type ID
- [ ] Obtain Test tenant SharePoint URL
- [ ] Register BFF API with Test container type
- [ ] Test and verify

### Production Environment
- [ ] Identify Production container type ID
- [ ] Obtain Production tenant SharePoint URL
- [ ] Register BFF API with Production container type
- [ ] Test and verify

---

## Documentation Updates Needed

After successful registration, update:

1. **Deployment Guide** - Add container type registration step
2. **Environment Setup** - Include in new environment checklist
3. **Architecture Docs** - Note that BFF API requires container type registration
4. **README** - Add to prerequisites section

---

## Related Documents

- [OBO-403-ROOT-CAUSE-ANALYSIS.md](./OBO-403-ROOT-CAUSE-ANALYSIS.md) - Detailed analysis of the issue
- [END-OF-DAY-STATUS-2025-10-08.md](./END-OF-DAY-STATUS-2025-10-08.md) - Previous troubleshooting session
- [KM-V2-OAUTH2-OBO-FLOW.md](../../../docs/KM-V2-OAUTH2-OBO-FLOW.md) - OAuth2 OBO flow documentation

---

## Next Actions

1. **Gather prerequisites** - Tenant SharePoint URL and owning app credentials
2. **Execute registration** - Follow Step-by-Step Registration section
3. **Test thoroughly** - Run all three test scenarios
4. **Update documentation** - Add to deployment guides
5. **Repeat for other environments** - Test, Staging, Production

**Status:** Ready to execute once prerequisites are gathered.
