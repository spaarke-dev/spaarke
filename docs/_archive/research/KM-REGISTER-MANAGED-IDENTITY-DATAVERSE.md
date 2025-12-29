# Register Azure Managed Identity as Dataverse Application User

**Knowledge Article**: Configuring Azure Web App Managed Identity for Dataverse API Access
**Created**: October 20, 2025
**Last Updated**: October 20, 2025
**Category**: Infrastructure Configuration, Security
**Related ADRs**: ADR-008 (Authentication), ADR-009 (Security)

---

## Overview

This guide explains how to register an Azure Web App's System-Assigned Managed Identity as an Application User in Microsoft Dataverse, enabling server-to-server authentication without storing credentials.

### Why This Is Required

The Spe.Bff.Api uses **Managed Identity authentication** to connect to Dataverse (configured in `DataverseServiceClientImpl.cs`). The Azure Web App has a Managed Identity, but it needs to be registered in Dataverse with appropriate permissions to:

1. Read Entity Definitions metadata (for Phase 7 NavMap API)
2. Create/update Document records
3. Query entity data

### Current Issue

**Error**: `ManagedIdentityCredential authentication failed: No User Assigned or Delegated Managed Identity found`

**Root Cause**: The Web App's Managed Identity exists but lacks Dataverse permissions.

---

## Prerequisites

- **Azure Portal Access**: Contributor or Owner role on the Azure Web App
- **Dataverse Access**: System Administrator role in the target Dataverse environment
- **Azure CLI**: Optional, for command-line steps

---

## Step 1: Verify Web App Managed Identity

### Option A: Azure Portal

1. Navigate to [Azure Portal](https://portal.azure.com)
2. Go to **App Services** → **spe-api-dev-67e2xz**
3. In left menu, click **Identity**
4. Under **System assigned** tab:
   - Status should be **On**
   - Note the **Object (principal) ID**: `56ae2188-c978-4734-ad16-0bc288973f20`

### Option B: Azure CLI

```bash
az webapp identity show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2
```

**Expected Output**:
```json
{
  "principalId": "56ae2188-c978-4734-ad16-0bc288973f20",
  "tenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "type": "SystemAssigned"
}
```

**Important**: Copy the `principalId` - you'll need it in Step 3.

---

## Step 2: Get Azure AD App Registration for Managed Identity

The Managed Identity needs to be represented as an Enterprise Application in Azure AD.

### Option A: Azure Portal

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory**
2. Click **Enterprise Applications** (left menu)
3. Set filter: **Application type** = "Managed Identities"
4. Search for the Principal ID: `56ae2188-c978-4734-ad16-0bc288973f20`
5. Note the **Application ID** from the Overview page

### Option B: Azure CLI

```bash
az ad sp show --id 56ae2188-c978-4734-ad16-0bc288973f20 \
  --query "{appId: appId, displayName: displayName, objectId: id}"
```

**Expected Output**:
```json
{
  "appId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "displayName": "spe-api-dev-67e2xz",
  "objectId": "56ae2188-c978-4734-ad16-0bc288973f20"
}
```

**Important**: Copy the `appId` - this is the **Application ID** needed for Dataverse.

---

## Step 3: Create Application User in Dataverse

### Prerequisites for This Step

- System Administrator access to Dataverse environment
- The **Application ID** from Step 2

### Option A: Power Platform Admin Center (Recommended)

1. Navigate to [Power Platform Admin Center](https://admin.powerplatform.microsoft.com/)
2. Select **Environments** → **SPAARKE DEV 1** (`spaarkedev1.crm.dynamics.com`)
3. Click **Settings** (top menu)
4. Expand **Users + permissions** → Click **Application users**
5. Click **+ New app user**
6. In the **Add an app user** panel:
   - Click **+ Add an app**
   - Search for your **Application ID** or **Display Name** (`spe-api-dev-67e2xz`)
   - Select the app and click **Add**
7. In **Business unit**, select the root business unit (usually same as org name)
8. In **Security roles**:
   - Click **Edit security roles** (pencil icon)
   - Select **System Administrator** (for testing) or create a custom role (for production)
   - Click **Save**
9. Click **Create**

### Option B: PowerShell (Alternative)

```powershell
# Install Microsoft.PowerApps.Administration.PowerShell if not already installed
Install-Module -Name Microsoft.PowerApps.Administration.PowerShell -Force

# Connect to Power Platform
Add-PowerAppsAccount

# Get the environment ID
$environmentName = "spaarkedev1"  # Your environment name
$environments = Get-AdminPowerAppEnvironment
$environment = $environments | Where-Object { $_.DisplayName -like "*$environmentName*" }
$envId = $environment.EnvironmentName

# Create Application User
# Replace with your Application ID from Step 2
$appId = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"

New-PowerAppManagementApp `
  -EnvironmentName $envId `
  -ApplicationId $appId

# Assign System Administrator role (or custom role)
# You'll need to do this in the Power Platform Admin Center UI
# as PowerShell cmdlets for role assignment are limited
```

---

## Step 4: Assign Security Roles

The Application User needs appropriate permissions to:

1. **Read Entity Definitions metadata** (for NavMap API)
2. **Create/Update/Read Documents** (for document management)
3. **Read Matter/Project/Invoice entities** (for lookups)

### Recommended Security Roles

**For Development/Testing**:
- **System Administrator** - Full access (simplest for dev/test)

**For Production** (Principle of Least Privilege):
Create a custom security role with these specific permissions:

#### Entity Permissions (Custom Role)

| Entity | Create | Read | Write | Delete | Append | Append To |
|--------|--------|------|-------|--------|--------|-----------|
| sprk_document | Organization | Organization | Organization | None | Organization | Organization |
| sprk_matter | None | Organization | None | None | None | Organization |
| sprk_project | None | Organization | None | None | None | Organization |
| sprk_invoice | None | Organization | None | None | None | Organization |

#### Miscellaneous Privileges (Required)

- **prvReadEntity** - Read Entity Definitions (CRITICAL for NavMap API)
- **prvReadAttribute** - Read Attribute Metadata (CRITICAL for NavMap API)
- **prvReadRelationship** - Read Relationship Metadata (CRITICAL for NavMap API)

### How to Create Custom Security Role

1. Go to [Power Platform Admin Center](https://admin.powerplatform.microsoft.com/)
2. **Environments** → **SPAARKE DEV 1** → **Settings**
3. **Users + permissions** → **Security roles**
4. Click **+ New role**
5. Name: "SDAP BFF API Service"
6. Configure entity permissions as shown in table above
7. Go to **Customization** tab:
   - Enable **prvReadEntity**
   - Enable **prvReadAttribute**
   - Enable **prvReadRelationship**
8. Click **Save and Close**
9. Return to **Application users** and assign this role to the app user

---

## Step 5: Verify Configuration

### Test 1: Check Application User Status

1. Power Platform Admin Center → **Environments** → **SPAARKE DEV 1**
2. **Settings** → **Users + permissions** → **Application users**
3. Find your application user (should show Display Name: `spe-api-dev-67e2xz`)
4. Verify:
   - **Status**: Enabled
   - **Business unit**: Assigned
   - **Security roles**: Assigned

### Test 2: Test Dataverse Connection (Azure CLI)

```bash
# Test the NavMap API endpoint (should now return 200 OK)
curl -i "https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document/lookup" \
  -H "Authorization: Bearer <VALID_TOKEN>"
```

**Expected Response**:
```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "navigationPropertyName": "sprk_Matter",
  "targetEntity": "sprk_matter",
  "logicalName": "sprk_matter",
  "schemaName": "sprk_Matter",
  "source": "metadata_query",
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document"
}
```

### Test 3: Browser Test (Phase 7 PCF)

1. Navigate to a Matter record in Dataverse
2. Open the "Document Upload" custom page
3. Upload a test document
4. **Browser Console** should show:
   ```
   ✅ [Phase 7] Querying navigation metadata for sprk_matter
   ✅ [Phase 7] Using navigation property: sprk_Matter (source: metadata_query)
   ✅ [DocumentRecordService] Document created successfully
   ```
5. Verify document appears in Matter's document grid

---

## Step 6: Repeat for Production Environment

After successful testing in DEV, repeat Steps 3-5 for the **production environment**:

- **Web App**: `spe-api-prod` (or actual production app name)
- **Dataverse Environment**: Production environment URL
- **Security Role**: Use custom role (NOT System Administrator) for production

---

## Troubleshooting

### Error: "Application not found in directory"

**Cause**: The Managed Identity's Application ID is incorrect or not found in Azure AD.

**Solution**:
1. Verify the Application ID using Azure CLI (Step 2)
2. Ensure you're using the `appId` (Application ID), NOT the `objectId` (Object ID)
3. Check that you're searching in the correct Azure AD tenant

### Error: "Insufficient permissions"

**Cause**: The Application User lacks required security roles.

**Solution**:
1. Verify security role assignment in Power Platform Admin Center
2. Ensure **prvReadEntity**, **prvReadAttribute**, **prvReadRelationship** are enabled
3. Try assigning System Administrator temporarily to isolate permission issues

### Error: "User does not have required privileges"

**Cause**: The security role doesn't have organization-level access.

**Solution**:
1. Edit the security role
2. For each required entity, change access level from "User" to "Organization"
3. Save and test again

### NavMap API Returns 500 After Setup

**Cause**: Configuration may not have propagated yet.

**Solution**:
1. Wait 5-10 minutes for changes to propagate
2. Restart the Azure Web App:
   ```bash
   az webapp restart \
     --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2
   ```
3. Clear browser cache and retry

---

## Security Considerations

### Production Best Practices

1. **Principle of Least Privilege**:
   - Use custom security role with ONLY required permissions
   - Do NOT use System Administrator in production

2. **Audit Logging**:
   - Enable Dataverse audit logging for Application User actions
   - Monitor for unusual activity patterns

3. **Managed Identity Rotation**:
   - Managed Identities don't have passwords/secrets to rotate
   - No certificate management required
   - Azure handles token lifecycle automatically

4. **Environment Isolation**:
   - Use separate Managed Identities for Dev/Test/Prod
   - Never share Application Users across environments

---

## Related Documentation

- [Microsoft: Managed Identities Overview](https://learn.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview)
- [Microsoft: Application Users in Dataverse](https://learn.microsoft.com/en-us/power-platform/admin/manage-application-users)
- [Microsoft: Security Roles and Privileges](https://learn.microsoft.com/en-us/power-platform/admin/security-roles-privileges)
- [Phase 7 Implementation Guide](./PHASE-7-NAVIGATION-METADATA.md)
- [ADR-008: Authentication Strategy](../ADRs/ADR-008-Authentication.md)

---

## Quick Reference

### Key Configuration Values

| Item | Value |
|------|-------|
| **Azure Web App Name** | spe-api-dev-67e2xz |
| **Resource Group** | spe-infrastructure-westus2 |
| **Managed Identity Type** | System-Assigned |
| **Principal ID (Object ID)** | 56ae2188-c978-4734-ad16-0bc288973f20 |
| **Tenant ID** | a221a95e-6abc-4434-aecc-e48338a1b2f2 |
| **Dataverse Environment** | SPAARKE DEV 1 (spaarkedev1.crm.dynamics.com) |

### Required Dataverse Privileges

- prvReadEntity (Read Entity Definitions)
- prvReadAttribute (Read Attribute Metadata)
- prvReadRelationship (Read Relationship Metadata)
- Organization-level access to sprk_document, sprk_matter, sprk_project, sprk_invoice

---

## Change Log

| Date | Change | Author |
|------|--------|--------|
| 2025-10-20 | Initial documentation | Claude Code |

---

**Need Help?**

If you encounter issues not covered in this guide, check:
1. Azure Web App logs: `az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2`
2. Dataverse audit logs in Power Platform Admin Center
3. Application Insights for detailed error traces
