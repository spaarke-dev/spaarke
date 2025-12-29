# HOW TO: Setup SharePoint Embedded Container Types and Containers

**Version:** 1.0
**Last Updated:** 2025-10-09
**Author:** Claude Code Session
**Status:** Production Guide

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Container Type Architecture](#container-type-architecture)
4. [Step-by-Step Setup Guide](#step-by-step-setup-guide)
5. [Critical Pitfalls & Solutions](#critical-pitfalls--solutions)
6. [Registration Scripts](#registration-scripts)
7. [Verification & Testing](#verification--testing)
8. [Troubleshooting](#troubleshooting)
9. [AI Assistant Prompts](#ai-assistant-prompts)

---

## Overview

SharePoint Embedded (SPE) uses **Container Types** and **Containers** to organize file storage:

- **Container Type**: A template/blueprint that defines how containers behave (owned by one application)
- **Container**: An instance of a Container Type that actually stores files (like a SharePoint site)

### Key Concepts

```
┌─────────────────────────────────────────────────────────────┐
│                      Container Type                          │
│  - Owned by ONE Azure AD Application (PCF app)              │
│  - Can have multiple GUEST applications registered          │
│  - Container Type ID: e.g., d26c1e41-4e5e-4b15-8a4b-...     │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ creates instances
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                         Container                            │
│  - Instance of Container Type                                │
│  - Stores actual files                                       │
│  - Container ID (Drive ID): e.g., b!yLRdWEOAdka...          │
└─────────────────────────────────────────────────────────────┘
```

### Application Roles

1. **Owning Application**: The PCF application that creates and owns the Container Type
2. **Guest Applications**: Other applications (like BFF API) that need access to containers

---

## Prerequisites

### Required Information

Before starting, gather:

```yaml
# PCF Application (Owning App)
PCF_APP_ID: "your-pcf-app-id"
PCF_APP_NAME: "Spaarke PCF Controls"

# BFF API Application (Guest App)
BFF_APP_ID: "your-bff-api-app-id"
BFF_APP_NAME: "Spaarke BFF API"
BFF_CLIENT_SECRET: "your-bff-client-secret"  # ⚠️ DEPRECATED - Use certificate
BFF_CERTIFICATE_THUMBPRINT: "your-cert-thumbprint"  # ✅ REQUIRED

# Container Type
CONTAINER_TYPE_ID: "your-container-type-id"  # Generated during creation
CONTAINER_TYPE_NAME: "SpaarkeDocuments"

# Tenant
TENANT_ID: "your-tenant-id"
ADMIN_UPN: "admin@yourtenant.onmicrosoft.com"
```

### Required Permissions

#### PCF Application (Owning App)
```json
{
  "requiredResourceAccess": [
    {
      "resourceAppId": "00000003-0000-0000-c000-000000000000",
      "resourceAccess": [
        {
          "id": "40dc41bc-0f7e-42ff-89bd-d9516947e474",
          "type": "Scope"  // FileStorageContainer.Selected
        }
      ]
    }
  ]
}
```

#### BFF API Application (Guest App)
```json
{
  "requiredResourceAccess": [
    {
      "resourceAppId": "00000003-0000-0000-c000-000000000000",
      "resourceAccess": [
        {
          "id": "085ca537-6565-41c2-aca7-db852babc212",
          "type": "Scope"  // Container.Selected (delegated)
        }
      ]
    }
  ]
}
```

### Required Tools

- **PowerShell 7+**
- **Azure CLI** or **PowerShell Az module**
- **PAC CLI** (for Container Type creation via Dataverse)
- **Postman** or **curl** (for API testing)
- **.pfx certificate** (for BFF API authentication)

---

## Container Type Architecture

### Authentication Methods

| Operation | Authentication Method | Why |
|-----------|----------------------|-----|
| Container Type Management | ✅ **Certificate** | Microsoft Graph requires certificate auth for Container Type APIs |
| File Upload/Download (OBO) | ✅ **Client Secret or Certificate** | Works with either, but certificate is more secure |
| PCF Control Access | ✅ **User Delegated** | Uses user's token via Xrm.WebApi |

### ⚠️ CRITICAL PITFALL #1: Client Secret vs Certificate

**Problem:**
```powershell
# ❌ THIS WILL FAIL for Container Type management
$body = @{
    client_id     = $BffApiAppId
    client_secret = $BffApiClientSecret
    scope         = "https://graph.microsoft.com/.default"
    grant_type    = "client_credentials"
}
```

**Error:**
```
403 Forbidden: The application does not have permission to perform this operation
```

**Solution:**
```powershell
# ✅ THIS WORKS - Use certificate authentication
$certThumbprint = "YOUR_CERT_THUMBPRINT"
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My\$certThumbprint"

# Build JWT assertion signed with certificate
$token = Get-MsalToken -ClientId $BffApiAppId `
                       -TenantId $TenantId `
                       -ClientCertificate $cert `
                       -Scopes "https://graph.microsoft.com/.default"
```

### Registration Requirements

**Container Type Registration** requires registering BOTH applications:

1. **Owning App (PCF)**: Full permissions (owner)
2. **Guest App (BFF API)**: Limited permissions (read/write content)

```json
{
  "value": [
    {
      "appId": "PCF_APP_ID",
      "delegated": ["full"],
      "appOnly": ["full"]
    },
    {
      "appId": "BFF_APP_ID",
      "delegated": ["ReadContent", "WriteContent"],
      "appOnly": ["none"]
    }
  ]
}
```

---

## Step-by-Step Setup Guide

### Phase 1: Create Container Type (via Dataverse)

**Note:** Container Types should be created through Dataverse, not directly via Graph API.

1. **Create Container entity in Dataverse**

```xml
<!-- src/Entities/sprk_Container/Entity.xml -->
<attribute PhysicalName="sprk_ContainerTypeId">
  <Type>nvarchar</Type>
  <Name>sprk_containertypeid</Name>
  <MaxLength>100</MaxLength>
  <RequiredLevel>required</RequiredLevel>
</attribute>
```

2. **Deploy entity to Dataverse**

```bash
cd src/Entities
pac solution init --publisher-name yourpublisher --publisher-prefix sprk
pac solution add-reference --path ../Entities/sprk_Container
msbuild /t:build /restore
pac solution import --path bin/Release/YourSolution.zip
```

3. **Container Type is automatically created** when you create the first Container record in Dataverse (if using Dataverse integration for SPE)

### Phase 2: Prepare Certificate for BFF API

1. **Generate self-signed certificate** (for development)

```powershell
# Generate certificate
$cert = New-SelfSignedCertificate -Subject "CN=SpeBffApi" `
                                   -CertStoreLocation "Cert:\CurrentUser\My" `
                                   -KeyExportPolicy Exportable `
                                   -KeySpec Signature `
                                   -KeyLength 2048 `
                                   -KeyAlgorithm RSA `
                                   -HashAlgorithm SHA256 `
                                   -NotAfter (Get-Date).AddYears(2)

# Export certificate
$certPath = "C:\Certs\SpeBffApi.pfx"
$certPassword = ConvertTo-SecureString -String "YourPassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $certPath -Password $certPassword

# Get thumbprint
$cert.Thumbprint
```

2. **Upload certificate to Azure AD Application**

```bash
# Via Azure CLI
az ad app credential reset --id $BFF_APP_ID --cert @C:\Certs\SpeBffApi.cer

# Or via Azure Portal:
# Azure AD > App Registrations > [BFF API App] > Certificates & secrets > Upload certificate
```

### Phase 3: Register BFF API with Container Type

Use the PowerShell script below to register the BFF API as a guest application.

**File:** `scripts/Register-BffApi-WithCertificate.ps1`

```powershell
<#
.SYNOPSIS
    Register BFF API with SharePoint Embedded Container Type using certificate authentication
.DESCRIPTION
    Registers the BFF API as a guest application with the Container Type.
    Uses certificate authentication (required for Container Type management APIs).
.PARAMETER ContainerTypeId
    The Container Type ID (GUID)
.PARAMETER BffApiAppId
    The BFF API Application ID (GUID)
.PARAMETER OwningAppId
    The owning PCF Application ID (GUID)
.PARAMETER CertificateThumbprint
    Certificate thumbprint for authentication
.PARAMETER TenantId
    Azure AD Tenant ID
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerTypeId,

    [Parameter(Mandatory=$true)]
    [string]$BffApiAppId,

    [Parameter(Mandatory=$true)]
    [string]$OwningAppId,

    [Parameter(Mandatory=$true)]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory=$true)]
    [string]$TenantId
)

# Load certificate
Write-Host "Loading certificate with thumbprint: $CertificateThumbprint"
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My\$CertificateThumbprint"
if (-not $cert) {
    Write-Error "Certificate not found: $CertificateThumbprint"
    exit 1
}

# Build JWT assertion
Write-Host "Building JWT assertion..."
$now = [System.DateTime]::UtcNow
$exp = $now.AddMinutes(10)

$header = @{
    alg = "RS256"
    typ = "JWT"
    x5t = [Convert]::ToBase64String($cert.GetCertHash())
} | ConvertTo-Json -Compress

$payload = @{
    aud = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
    exp = [int][double]::Parse((Get-Date $exp -UFormat %s))
    iss = $BffApiAppId
    jti = [guid]::NewGuid()
    nbf = [int][double]::Parse((Get-Date $now -UFormat %s))
    sub = $BffApiAppId
} | ConvertTo-Json -Compress

# Encode and sign
$headerEncoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($header)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$payloadEncoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$dataToSign = "$headerEncoded.$payloadEncoded"

$rsa = $cert.PrivateKey
$signature = $rsa.SignData([System.Text.Encoding]::UTF8.GetBytes($dataToSign), [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
$signatureEncoded = [Convert]::ToBase64String($signature).TrimEnd('=').Replace('+', '-').Replace('/', '_')

$jwt = "$headerEncoded.$payloadEncoded.$signatureEncoded"

# Get access token
Write-Host "Getting access token with certificate authentication..."
$tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
$tokenBody = @{
    client_id = $BffApiAppId
    client_assertion_type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
    client_assertion = $jwt
    scope = "https://graph.microsoft.com/.default"
    grant_type = "client_credentials"
}

$tokenResponse = Invoke-RestMethod -Method Post -Uri $tokenUrl -Body $tokenBody -ContentType "application/x-www-form-urlencoded"
$accessToken = $tokenResponse.access_token

# Register applications with Container Type
Write-Host "Registering applications with Container Type..."
$registerUrl = "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/$ContainerTypeId/applications"
$registerBody = @{
    value = @(
        @{
            appId = $OwningAppId
            delegated = @("full")
            appOnly = @("full")
        },
        @{
            appId = $BffApiAppId
            delegated = @("ReadContent", "WriteContent")
            appOnly = @("none")
        }
    )
} | ConvertTo-Json -Depth 3

$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
}

try {
    $response = Invoke-RestMethod -Method Post -Uri $registerUrl -Headers $headers -Body $registerBody
    Write-Host "✅ Successfully registered applications with Container Type" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 10
} catch {
    Write-Error "❌ Failed to register applications: $_"
    Write-Error "Response: $($_.Exception.Response)"
    exit 1
}

# Verify registration
Write-Host "`nVerifying registration..."
$verifyUrl = "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/$ContainerTypeId/applications"
$verifyResponse = Invoke-RestMethod -Method Get -Uri $verifyUrl -Headers $headers

Write-Host "✅ Registered applications:" -ForegroundColor Green
$verifyResponse.value | ForEach-Object {
    Write-Host "  - App ID: $($_.appId)" -ForegroundColor Cyan
    Write-Host "    Delegated: $($_.delegated -join ', ')" -ForegroundColor Gray
    Write-Host "    AppOnly: $($_.appOnly -join ', ')" -ForegroundColor Gray
}
```

**Usage:**

```powershell
.\Register-BffApi-WithCertificate.ps1 `
    -ContainerTypeId "d26c1e41-4e5e-4b15-8a4b-..." `
    -BffApiAppId "your-bff-api-app-id" `
    -OwningAppId "your-pcf-app-id" `
    -CertificateThumbprint "ABC123..." `
    -TenantId "your-tenant-id"
```

### Phase 4: Create Container Instance

Once the Container Type is registered, create a container instance:

```http
POST https://graph.microsoft.com/v1.0/storage/fileStorage/containers
Authorization: Bearer {token}
Content-Type: application/json

{
  "containerTypeId": "d26c1e41-4e5e-4b15-8a4b-...",
  "displayName": "Matter MRT.P0001US01 Documents",
  "description": "Document container for matter MRT.P0001US01"
}
```

**Response:**

```json
{
  "id": "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb...",
  "containerTypeId": "d26c1e41-4e5e-4b15-8a4b-...",
  "displayName": "Matter MRT.P0001US01 Documents",
  "createdDateTime": "2025-10-09T12:00:00Z",
  "status": "active"
}
```

Store the container `id` (Drive ID) in Dataverse `sprk_graphdriveid` field.

---

## Critical Pitfalls & Solutions

### Pitfall #1: 403 Forbidden - Client Secret Authentication

**Symptom:**
```
403 Forbidden when trying to register applications with Container Type
The application does not have permission to perform this operation
```

**Root Cause:**
Container Type management APIs require certificate authentication, not client secret.

**Solution:**
Use the certificate-based PowerShell script above (Phase 3).

**Detection:**
If you see this in your registration attempt:
```powershell
$tokenBody = @{
    client_secret = $secret  # ❌ THIS CAUSES 403
}
```

**Fix:**
Replace with certificate-based JWT assertion (see Phase 3 script).

---

### Pitfall #2: Incorrect @odata.bind Syntax for Lookups

**Symptom:**
- First Document record has Matter lookup set but Document Name is NULL
- Subsequent records have Document Name set but Matter lookup is NULL

**Root Cause:**
Using navigation property name (e.g., `sprk_MatterId`) instead of lookup field name (e.g., `sprk_matter`) in `@odata.bind`.

**Wrong Code:**
```typescript
// ❌ WRONG - Uses navigation property
recordData[`sprk_MatterId@odata.bind`] = `/sprk_matters(${matterId})`;
```

**Correct Code:**
```typescript
// ✅ CORRECT - Uses actual lookup field name
recordData[`sprk_matter@odata.bind`] = `/sprk_matters(${matterId})`;
```

**Files to Check:**
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DataverseRecordService.ts`
- Method: `getLookupFieldName(parentEntityName: string)`

---

### Pitfall #3: Document Name NULL on First Record

**Symptom:**
When uploading multiple files, the first Document record has NULL `sprk_documentname`, but others are correct.

**Root Cause:**
Combination of:
1. Using wrong field name in `@odata.bind` (navigation property vs lookup field)
2. `sprk_documentname` is a required field
3. Dataverse API quirk where required fields may not be set correctly when `@odata.bind` is malformed

**Solution:**
1. Use correct lookup field name in `@odata.bind` (see Pitfall #2)
2. Ensure `sprk_documentname` is explicitly set in `recordData` BEFORE adding parent relationship
3. Log the full `recordData` object before calling `createRecord` to verify

**Code Pattern:**
```typescript
// Build record data FIRST
const recordData: Record<string, unknown> = {
    ...request.formData,
    sprk_documentname: uniqueFileName,  // Set explicitly
    sprk_filename: uniqueFileName,
    sprk_graphitemid: itemId,
    sprk_graphdriveid: driveId
};

// THEN add parent relationship
if (parentEntityName && parentRecordId) {
    const lookupField = getLookupFieldName(parentEntityName);  // sprk_matter
    recordData[`${lookupField}@odata.bind`] = `/${entitySetName}(${parentRecordId})`;
}

// Log before creating
console.log('Record data:', recordData);
await context.webAPI.createRecord('sprk_document', recordData);
```

---

### Pitfall #4: Container Type Not Found

**Symptom:**
```
404 Not Found when trying to access /storage/fileStorage/containerTypes/{id}
```

**Root Cause:**
Container Type was not created through proper channel (should be via Dataverse, not direct Graph API call).

**Solution:**
1. Create Container entity in Dataverse first
2. Container Type is auto-created when first Container record is created
3. Retrieve Container Type ID from Dataverse or Graph API:

```http
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes
Authorization: Bearer {token}
```

---

### Pitfall #5: Lookup Fields in formData

**Symptom:**
```
Invalid property 'sprk_matter' was found in entity 'Microsoft.Dynamics.CRM.sprk_document'
```

**Root Cause:**
Lookup fields are being included in `formData` spread, conflicting with `@odata.bind` syntax.

**Solution:**
Filter lookup fields when extracting form data:

```typescript
// In UniversalQuickCreatePCF.ts
attributes.forEach((attr: any) => {
    const name = attr.getName();
    const value = attr.getValue();
    const attributeType = attr.getAttributeType();

    // Skip lookup fields - they're handled via @odata.bind
    if (name !== 'sprk_fileuploadmetadata' &&
        value !== null &&
        attributeType !== 'lookup') {  // ✅ Filter lookups
        formData[name] = value;
    }
});
```

---

## Registration Scripts

### Complete Registration Script

Save as: `scripts/Register-ContainerType-Complete.ps1`

```powershell
<#
.SYNOPSIS
    Complete Container Type setup and registration
.DESCRIPTION
    1. Verifies certificate exists
    2. Registers BFF API as guest application
    3. Creates test container
    4. Verifies access
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerTypeId,

    [Parameter(Mandatory=$true)]
    [string]$BffApiAppId,

    [Parameter(Mandatory=$true)]
    [string]$OwningAppId,

    [Parameter(Mandatory=$true)]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory=$true)]
    [string]$TenantId,

    [Parameter(Mandatory=$false)]
    [switch]$CreateTestContainer
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Container Type Registration Process" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Verify certificate
Write-Host "`n[1/4] Verifying certificate..." -ForegroundColor Yellow
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $cert) {
    Write-Error "❌ Certificate not found with thumbprint: $CertificateThumbprint"
    exit 1
}
Write-Host "✅ Certificate found: $($cert.Subject)" -ForegroundColor Green

# Step 2: Register applications (using script from Phase 3)
Write-Host "`n[2/4] Registering applications with Container Type..." -ForegroundColor Yellow
& "$PSScriptRoot\Register-BffApi-WithCertificate.ps1" `
    -ContainerTypeId $ContainerTypeId `
    -BffApiAppId $BffApiAppId `
    -OwningAppId $OwningAppId `
    -CertificateThumbprint $CertificateThumbprint `
    -TenantId $TenantId

# Step 3: Create test container (optional)
if ($CreateTestContainer) {
    Write-Host "`n[3/4] Creating test container..." -ForegroundColor Yellow

    # Get token using certificate
    $jwt = # ... (JWT creation code from Phase 3)
    $tokenResponse = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -Body @{
        client_id = $OwningAppId
        client_assertion_type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
        client_assertion = $jwt
        scope = "https://graph.microsoft.com/.default"
        grant_type = "client_credentials"
    }

    $createContainerBody = @{
        containerTypeId = $ContainerTypeId
        displayName = "Test Container $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        description = "Test container created during registration"
    } | ConvertTo-Json

    $container = Invoke-RestMethod -Method Post `
        -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" `
        -Headers @{ Authorization = "Bearer $($tokenResponse.access_token)"; "Content-Type" = "application/json" } `
        -Body $createContainerBody

    Write-Host "✅ Test container created: $($container.id)" -ForegroundColor Green
    Write-Host "   Display Name: $($container.displayName)" -ForegroundColor Gray
    Write-Host "   Container ID (Drive ID): $($container.id)" -ForegroundColor Gray
} else {
    Write-Host "`n[3/4] Skipping test container creation" -ForegroundColor Gray
}

# Step 4: Verify access
Write-Host "`n[4/4] Verifying BFF API can access Container Type..." -ForegroundColor Yellow
# ... (verification code)

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "✅ Container Type setup complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
```

---

## Verification & Testing

### Test 1: Verify Container Type Registration

```http
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/{containerTypeId}/applications
Authorization: Bearer {token}
```

**Expected Response:**
```json
{
  "value": [
    {
      "appId": "pcf-app-id",
      "delegated": ["full"],
      "appOnly": ["full"]
    },
    {
      "appId": "bff-api-app-id",
      "delegated": ["ReadContent", "WriteContent"],
      "appOnly": ["none"]
    }
  ]
}
```

### Test 2: Upload File via BFF API (OBO Flow)

```http
PUT https://your-bff-api.azurewebsites.net/api/obo/containers/{driveId}/files/{fileName}
Authorization: Bearer {user-token}
Content-Type: application/octet-stream

[file binary data]
```

**Success Response:**
```json
{
  "id": "01LBYCMX...",
  "name": "test.pdf",
  "size": 12345,
  "createdDateTime": "2025-10-09T12:00:00Z"
}
```

### Test 3: Verify Multiple Documents Created

1. Upload 3 files via Quick Create form
2. Check browser console for logs:
   - "Processing file 1/3", "2/3", "3/3"
   - "Document record created successfully" (should appear 3 times)
3. Verify in Dataverse:
   - All 3 Document records exist
   - Each has unique `sprk_documentname` (filename)
   - All have `sprk_matter` lookup set
   - All have `sprk_graphdriveid` and `sprk_graphitemid`

---

## Troubleshooting

### Issue: 403 Forbidden during registration

**Check:**
1. Are you using certificate authentication? (not client secret)
2. Is certificate uploaded to Azure AD application?
3. Does certificate have correct permissions?

**Verify:**
```powershell
# Check certificate
$cert = Get-ChildItem "Cert:\CurrentUser\My\$thumbprint"
$cert | Format-List Subject, Thumbprint, NotBefore, NotAfter

# Check app registration
az ad app show --id $BFF_APP_ID --query "keyCredentials"
```

### Issue: Container Type Not Found (404)

**Check:**
1. Was Container Type created via Dataverse?
2. Does Container Type ID exist?

**Verify:**
```http
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes
Authorization: Bearer {token}
```

### Issue: Files upload but no Document records created

**Check:**
1. Browser console for JavaScript errors
2. Network tab for failed API calls
3. PCF logs for "Failed to create document record"

**Debug:**
- Add logging in `DataverseRecordService.createDocument()`
- Check if `context.webAPI.createRecord()` is throwing errors
- Verify field names match Entity.xml

### Issue: First Document has NULL name, others OK

**Solution:**
See [Pitfall #2](#pitfall-2-incorrect-odatabind-syntax-for-lookups) and [Pitfall #3](#pitfall-3-document-name-null-on-first-record).

**Quick Fix:**
1. Use `sprk_matter@odata.bind` (not `sprk_MatterId@odata.bind`)
2. Set `sprk_documentname` before adding `@odata.bind`
3. Deploy updated PCF control

---

## AI Assistant Prompts

### Prompt 1: Setup New Container Type

```
I need to set up a new SharePoint Embedded Container Type for a new Dataverse entity.

Context:
- Entity name: [entity_name]
- PCF App ID: [pcf_app_id]
- BFF API App ID: [bff_api_app_id]
- BFF Certificate Thumbprint: [cert_thumbprint]
- Tenant ID: [tenant_id]

Please:
1. Guide me through creating the Container Type
2. Generate the PowerShell registration script
3. Verify the registration was successful
4. Create a test container instance

Refer to: docs/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md
```

### Prompt 2: Troubleshoot 403 Error

```
I'm getting a 403 Forbidden error when trying to register my BFF API with a Container Type.

Error details:
[paste error message]

Current authentication method:
[client secret / certificate]

Please:
1. Identify the root cause
2. Provide the correct authentication approach
3. Generate the fix (PowerShell script)

Refer to: docs/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md - Pitfall #1
```

### Prompt 3: Fix Multi-File Upload Issues

```
When uploading multiple files via Quick Create, I'm seeing:
- First Document: Matter set ✓, Document Name NULL ✗
- Other Documents: Matter NULL ✗, Document Name set ✓

Please:
1. Identify which pitfall this matches
2. Show me the exact code fix
3. Explain why this happens
4. Deploy the updated PCF control

Refer to: docs/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md - Pitfalls #2 and #3
```

### Prompt 4: Complete New Environment Setup

```
I need to set up SharePoint Embedded in a new environment from scratch.

Environment details:
- Tenant: [tenant_name]
- Admin: [admin_upn]
- Dataverse Org: [org_url]
- Container Type Name: [type_name]

Please:
1. Walk me through the complete setup process
2. Generate all required scripts
3. Verify each step before moving to the next
4. Create test container and upload test file
5. Document any deviations or issues encountered

Refer to: docs/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md - Complete guide
```

---

## Change Log

| Date | Version | Changes |
|------|---------|---------|
| 2025-10-09 | 1.0 | Initial documentation from Sprint 4 lessons learned |

---

## Related Documentation

- [KM-HOW-TO-DEPLOY-TO-DATAVERSE.md](./KM-HOW-TO-DEPLOY-TO-DATAVERSE.md) - PCF deployment guide
- [SPE-1-TO-N-FILE-DOCUMENT-RELATIONSHIP.md](../dev/projects/future-features/SPE-1-TO-N-FILE-DOCUMENT-RELATIONSHIP.md) - Future feature architecture
- [QUICK-CREATE-FILE-UPLOAD-ONLY.md](./QUICK-CREATE-FILE-UPLOAD-ONLY.md) - Quick Create implementation

---

## Summary

**Key Takeaways:**

1. ✅ **Always use certificate authentication** for Container Type management
2. ✅ **Use lookup field names** (e.g., `sprk_matter`) not navigation properties (e.g., `sprk_MatterId`) in `@odata.bind`
3. ✅ **Filter lookup fields** from formData to avoid conflicts
4. ✅ **Set required fields explicitly** before adding `@odata.bind`
5. ✅ **Register both owning and guest applications** with Container Type
6. ✅ **Create Container Types via Dataverse**, not direct Graph API
7. ✅ **Test with multiple files** to catch subtle ordering bugs

**Success Criteria:**

- [ ] Container Type created and visible in Graph API
- [ ] BFF API registered as guest application with correct permissions
- [ ] Certificate authentication working for Container Type APIs
- [ ] File upload via OBO flow successful
- [ ] Multiple Document records created with correct field values
- [ ] Matter lookup set on all records
- [ ] Document Name set to unique filename on all records

---

**Document Version:** 1.0
**Status:** ✅ Production Ready
**Last Validated:** 2025-10-09
