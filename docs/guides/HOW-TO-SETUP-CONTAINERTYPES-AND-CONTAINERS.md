# HOW TO: Setup SharePoint Embedded Container Types and Containers

**Version:** 2.0
**Last Updated:** 2026-04-05
**Author:** Claude Code Session
**Status:** Production Guide

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Container Type Architecture](#container-type-architecture)
4. [Step-by-Step Setup Guide](#step-by-step-setup-guide)
5. [Business Unit Container Provisioning](#business-unit-container-provisioning)
6. [Critical Pitfalls & Solutions](#critical-pitfalls--solutions)
7. [Scripts Reference](#scripts-reference)
8. [Verification & Testing](#verification--testing)
9. [Troubleshooting](#troubleshooting)

---

## Overview

SharePoint Embedded (SPE) uses **Container Types** and **Containers** to organize file storage:

- **Container Type**: A template/blueprint that defines how containers behave (owned by one application)
- **Container**: An instance of a Container Type that actually stores files (like a SharePoint site)
- **Business Unit**: Each Dataverse business unit gets one SPE container, referenced by `sprk_containerid`

### Key Concepts

```
┌─────────────────────────────────────────────────────────────┐
│                      Container Type                          │
│  - Owned by ONE Azure AD Application (BFF API app)          │
│  - Owning app has full delegated + full appOnly permissions  │
│  - Container Type ID stored in Key Vault                     │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ creates instances
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                         Container                            │
│  - One per Business Unit                                     │
│  - Stores actual files (documents, uploads)                  │
│  - Container ID stored in businessunit.sprk_containerid      │
└─────────────────────────────────────────────────────────────┘
```

### Application Roles

In Spaarke's architecture, the **BFF API app** is the single owning application:

| Role | Application | Permissions |
|------|-------------|-------------|
| **Owning App** | BFF API (`spaarke-bff-api-{env}`) | `full` delegated + `full` appOnly |

The BFF API performs all server-side SPE operations: container creation, file upload/download (OBO flow), and metadata queries. There is no separate "guest app" — the BFF API owns the container type and accesses containers directly.

> **History**: In dev (October 2025), a PCF app (`170c98e1`) was the owning app and BFF API was a guest. This model is retired. For production, BFF API is the owning app.

---

## Prerequisites

### Required Information

Before starting, gather:

```yaml
# BFF API Application (Owning App)
BFF_APP_ID: "<spaarke-bff-api-{env}-client-id>"
BFF_APP_NAME: "Spaarke BFF API"
# Retrieve secret from Key Vault:
#   az keyvault secret show --vault-name <name> --name <secret> --query value -o tsv

# Tenant
TENANT_ID: "<your-azure-ad-tenant-id>"
SHAREPOINT_DOMAIN: "<tenant>.sharepoint.com"  # e.g., "spaarke.sharepoint.com"

# Container Type (generated during creation)
CONTAINER_TYPE_ID: "<guid>"  # Stored in Key Vault as Spe--ContainerTypeId
```

### Required Permissions

The BFF API app registration needs:

| Permission | Type | Resource | Purpose |
|-----------|------|----------|---------|
| `FileStorageContainer.Selected` | Application | Microsoft Graph | Create/access SPE containers |
| `Container.Selected` | Application | SharePoint | Register app with container types |

Grant admin consent for both permissions in Azure Portal > App Registrations > API Permissions.

### Required Tools

- **PowerShell 7+**
- **Azure CLI** (`az`)
- **SharePoint Online Management Shell** (for container type creation via PowerShell)
- **PnP PowerShell** (optional, for discovery scripts)

---

## Container Type Architecture

### Ownership Model

The BFF API is the **owning application** for the container type. This means:

1. BFF API authenticates with **client credentials** (client secret via Key Vault)
2. BFF API can create containers, manage permissions, and perform all CRUD operations
3. No separate "guest" registration is needed — the owner has full access
4. Container type management (registration) uses the SharePoint API, not Graph API

### Registration Flow

```
1. Create Container Type (SPO Management Shell or Graph API)
   └── Output: ContainerTypeId (GUID)

2. Register Owning App with Container Type (SharePoint API)
   └── PUT /_api/v2.1/storageContainerTypes/{id}/applicationPermissions
   └── Body: { appId: BFF_APP_ID, delegated: ["full"], appOnly: ["full"] }

3. Store ContainerTypeId in Key Vault
   └── Spe--ContainerTypeId = <guid>

4. Configure BFF API App Setting
   └── Spe__ContainerTypeId = @Microsoft.KeyVault(...)
```

### Container-to-Business-Unit Mapping

Each **Dataverse business unit** has exactly one SPE container:

```
Business Unit (Dataverse)          SPE Container (SharePoint Embedded)
┌──────────────────────────┐       ┌──────────────────────────┐
│ Root BU                  │──────>│ "Root BU Documents"      │
│ sprk_containerid = abc123│       │ Container ID: abc123     │
└──────────────────────────┘       └──────────────────────────┘

┌──────────────────────────┐       ┌──────────────────────────┐
│ Child BU                 │──────>│ "Child BU Documents"     │
│ sprk_containerid = def456│       │ Container ID: def456     │
└──────────────────────────┘       └──────────────────────────┘
```

**How containers are resolved at runtime:**
- Document Upload Wizard: `AssociateToStep.tsx` → `resolveBusinessUnitContainerId()` → reads user's BU → gets `sprk_containerid`
- Legal Workspace: `xrmProvider.ts` → `getSpeContainerIdFromBusinessUnit()` → same lookup

---

## Step-by-Step Setup Guide

### Phase 1: Create Container Type

**[HUMAN]** Requires SharePoint Administrator or Global Administrator. One-time operation per environment.

**Option A: PowerShell (recommended)**

```powershell
.\scripts\Create-ContainerType-PowerShell.ps1 `
    -ContainerTypeName "Spaarke Document Storage" `
    -OwningAppId "<bff-api-client-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

**Option B: Graph API**

```powershell
.\scripts\Create-NewContainerType.ps1 `
    -OwningAppId "<bff-api-client-id>" `
    -OwningAppSecret "<secret-from-key-vault>" `
    -TenantId "<tenant-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

**Output**: A new `ContainerTypeId` (GUID). Save this for subsequent steps.

### Phase 2: Register Owning App with Container Type

**[AI/HUMAN]** Register the BFF API as the owning app:

```powershell
.\scripts\Register-BffApiWithContainerType.ps1 `
    -ContainerTypeId "<container-type-id>" `
    -OwningAppId "<bff-api-client-id>" `
    -OwningAppSecret "<secret-from-key-vault>" `
    -TenantId "<tenant-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

This calls `PUT /_api/v2.1/storageContainerTypes/{id}/applicationPermissions` to register the app with `full` delegated and `full` appOnly permissions.

### Phase 3: Store Container Type ID

**[AI]** Store in Key Vault and configure App Service:

```powershell
# Store in Key Vault
az keyvault secret set `
    --vault-name <platform-key-vault> `
    --name "Spe--ContainerTypeId" `
    --value "<container-type-id>"

# Configure BFF API app setting
az webapp config appsettings set `
    -g <resource-group> `
    -n <app-service-name> `
    --settings "Spe__ContainerTypeId=@Microsoft.KeyVault(VaultName=<vault>;SecretName=Spe--ContainerTypeId)"
```

### Phase 4: Verify Setup

```powershell
# Check container type registration
.\scripts\Check-ContainerType-Registration.ps1 `
    -ContainerTypeId "<container-type-id>" `
    -SharePointDomain "<tenant>.sharepoint.com" `
    -OwningAppId "<bff-api-client-id>"

# Test SharePoint token and API access
.\scripts\Test-SharePointToken.ps1 `
    -ClientId "<bff-api-client-id>" `
    -ClientSecret "<secret>" `
    -TenantId "<tenant-id>" `
    -ContainerTypeId "<container-type-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"

# Restart BFF API to pick up new config
az webapp restart --name <app-service-name> --resource-group <resource-group>
```

---

## Business Unit Container Provisioning

### During Initial Customer Provisioning

`Provision-Customer.ps1` Step 8 automatically:
1. Retrieves ContainerTypeId from Key Vault
2. Creates an SPE container via Graph API
3. Finds the root business unit in Dataverse
4. Sets `sprk_containerid` on the root BU

### When Adding New Business Units

Run the standalone script when a new BU is created:

```powershell
.\scripts\New-BusinessUnitContainer.ps1 `
    -BusinessUnitId "<bu-guid>" `
    -BusinessUnitName "New Business Unit" `
    -ContainerTypeId "<container-type-id>" `
    -DataverseUrl "https://<env>.crm.dynamics.com"
```

### Automation Options

ADR-002 prohibits Dataverse plugins, so automatic container creation on BU creation uses alternatives:

| Method | Trigger | Complexity |
|--------|---------|------------|
| **Power Automate flow** | BU record created in Dataverse | Medium — calls Graph API + updates BU |
| **BFF API endpoint** | Ribbon button / command bar action | Medium — new endpoint + UI button |
| **Manual script** | Operator runs `New-BusinessUnitContainer.ps1` | Low — current approach |

---

## Critical Pitfalls & Solutions

### Pitfall #1: Incorrect @odata.bind Syntax for Lookups

**Symptom:** First Document record has Matter lookup set but Document Name is NULL; subsequent records have Document Name set but Matter lookup is NULL.

**Root Cause:** Using navigation property name (e.g., `sprk_MatterId`) instead of lookup field name (e.g., `sprk_matter`) in `@odata.bind`.

```typescript
// WRONG - Uses navigation property
recordData[`sprk_MatterId@odata.bind`] = `/sprk_matters(${matterId})`;

// CORRECT - Uses actual lookup field name
recordData[`sprk_matter@odata.bind`] = `/sprk_matters(${matterId})`;
```

### Pitfall #2: Document Name NULL on First Record

**Root Cause:** Combination of wrong `@odata.bind` field name and Dataverse API quirk where required fields may not be set when `@odata.bind` is malformed.

**Solution:** Set `sprk_documentname` explicitly BEFORE adding parent relationship:

```typescript
const recordData: Record<string, unknown> = {
    ...request.formData,
    sprk_documentname: uniqueFileName,  // Set explicitly first
    sprk_filename: uniqueFileName,
    sprk_graphitemid: itemId,
    sprk_graphdriveid: driveId
};

// THEN add parent relationship
if (parentEntityName && parentRecordId) {
    const lookupField = getLookupFieldName(parentEntityName);
    recordData[`${lookupField}@odata.bind`] = `/${entitySetName}(${parentRecordId})`;
}
```

### Pitfall #3: Container Type Not Found (404)

**Symptom:** `404 Not Found` when accessing `/storage/fileStorage/containerTypes/{id}`

**Solution:**
1. Verify container type exists: `.\scripts\Find-ContainerTypeOwner.ps1 -ContainerTypeId <id> -SharePointDomain <domain>`
2. Check the BFF API app has `FileStorageContainer.Selected` permission
3. If creating via Graph API, use the beta endpoint: `https://graph.microsoft.com/beta/storage/fileStorage/containerTypes`

### Pitfall #4: Lookup Fields in formData

**Symptom:** `Invalid property 'sprk_matter' was found in entity 'Microsoft.Dynamics.CRM.sprk_document'`

**Solution:** Filter lookup fields when extracting form data:

```typescript
attributes.forEach((attr: any) => {
    const name = attr.getName();
    const value = attr.getValue();
    const attributeType = attr.getAttributeType();

    if (name !== 'sprk_fileuploadmetadata' &&
        value !== null &&
        attributeType !== 'lookup') {  // Filter lookups — handled via @odata.bind
        formData[name] = value;
    }
});
```

---

## Scripts Reference

### Active Scripts

| Script | Purpose | Key Parameters |
|--------|---------|----------------|
| `Create-ContainerType-PowerShell.ps1` | Create container type via SPO Management Shell | `-OwningAppId`, `-SharePointDomain` |
| `Create-NewContainerType.ps1` | Create container type + register app via Graph/SP API | `-OwningAppId`, `-OwningAppSecret`, `-TenantId`, `-SharePointDomain` |
| `Register-BffApiWithContainerType.ps1` | Register owning app with container type | `-ContainerTypeId`, `-OwningAppId`, `-OwningAppSecret`, `-TenantId`, `-SharePointDomain` |
| `Check-ContainerType-Registration.ps1` | Verify container type registration status | `-ContainerTypeId`, `-SharePointDomain` |
| `Find-ContainerTypeOwner.ps1` | Discover which app owns a container type (PnP) | `-ContainerTypeId`, `-SharePointDomain` |
| `Find-ContainerTypeOwner-AzCli.ps1` | Same as above, using Azure CLI | `-ContainerTypeId`, `-TenantId`, `-SharePointDomain` |
| `Get-ContainerMetadata.ps1` | Get metadata for a specific container | `-ContainerId`, `-SharePointDomain` |
| `Set-ContainerId.ps1` | Set DefaultContainerId app setting | `-ContainerId`, `-AppServiceName`, `-ResourceGroup` |
| `Test-SharePointToken.ps1` | Test token validity and API access | `-ClientId`, `-ClientSecret`, `-TenantId`, `-ContainerTypeId`, `-SharePointDomain` |
| `New-BusinessUnitContainer.ps1` | Create container for a business unit | `-BusinessUnitId`, `-BusinessUnitName`, `-ContainerTypeId`, `-DataverseUrl` |

All scripts use **mandatory parameters** — no hardcoded IDs, secrets, or domains. Secrets should be retrieved from Key Vault:

```powershell
$secret = az keyvault secret show --vault-name <name> --name <secret> --query value -o tsv
```

### Archived Scripts (`scripts/_archive/`)

Scripts that used the retired PCF app (`170c98e1`) or hardcoded secrets have been archived. They are preserved for reference but should not be used.

---

## Verification & Testing

### Test 1: Verify Container Type Registration

```powershell
.\scripts\Check-ContainerType-Registration.ps1 `
    -ContainerTypeId "<id>" `
    -SharePointDomain "<tenant>.sharepoint.com" `
    -OwningAppId "<bff-api-client-id>"
```

**Expected:** BFF API app listed with `full` delegated and `full` appOnly permissions.

### Test 2: Upload File via BFF API (OBO Flow)

```http
PUT https://<bff-api>/api/containers/{containerId}/files/{fileName}
Authorization: Bearer {user-token}
Content-Type: application/octet-stream

[file binary data]
```

**Success:** HTTP 200 with file metadata (id, name, size, createdDateTime).

### Test 3: Verify Business Unit Container

```powershell
# Check BU has sprk_containerid set
$dvToken = az account get-access-token --resource <dataverse-url> --query accessToken -o tsv
Invoke-RestMethod `
    -Uri "<dataverse-url>/api/data/v9.2/businessunits(<bu-id>)?`$select=name,sprk_containerid" `
    -Headers @{ Authorization = "Bearer $dvToken" }
```

---

## Troubleshooting

### 403 Forbidden during registration

1. Verify BFF API app has `Container.Selected` (SharePoint) and `FileStorageContainer.Selected` (Graph) permissions
2. Verify admin consent has been granted
3. Check the app is the actual owning app of the container type: `.\scripts\Find-ContainerTypeOwner.ps1`

### Container Type Not Found (404)

1. Verify container type ID is correct
2. Check if it was created in the correct tenant
3. List all container types: `.\scripts\Find-ContainerTypeOwner-AzCli.ps1`

### BU has no sprk_containerid

1. Run `New-BusinessUnitContainer.ps1` to create a container and assign it
2. If `Provision-Customer.ps1` Step 8 failed partially, check the provisioning log for the container ID and assign manually

### Files upload but no Document records created

1. Check browser console for JavaScript errors
2. Verify PCF control version is current
3. Check if `sprk_containerid` is set on the user's business unit

---

## Change Log

| Date | Version | Changes |
|------|---------|---------|
| 2025-10-09 | 1.0 | Initial documentation from Sprint 4 lessons learned |
| 2026-03-14 | 2.0 | Major rewrite: BFF API as owning app (removed PCF app model), parameterized all scripts, added business unit container provisioning, removed certificate auth section (uses client secret via Key Vault), removed AI assistant prompts section |

---

## Related Documentation

- [PRODUCTION-DEPLOYMENT-GUIDE.md Section 19](./PRODUCTION-DEPLOYMENT-GUIDE.md#19-sharepoint-embedded-spe-setup) — Production SPE setup steps
- [PCF-DEPLOYMENT-GUIDE.md](./PCF-DEPLOYMENT-GUIDE.md) — PCF deployment guide

---

**Document Version:** 2.0
**Status:** Production Ready
**Last Validated:** 2026-03-14
