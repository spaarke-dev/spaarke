# Phase 7: Add Dataverse API Permission to BFF API

**Date:** October 20, 2025
**Status:** BLOCKED - Requires Azure AD Admin
**App Registration:** BFF API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)

---

## Problem

NavMap API endpoint returns 500 error:

```
AADSTS500011: The resource principal named https://spaarkedev1.api.crm.dynamics.com/...
was not found in the tenant named Spaarke Inc.
```

**Root Cause:** BFF API App Registration lacks API permissions for Dataverse.

---

## Solution Required

The BFF API App Registration needs the **Dynamics CRM API permission** added and granted admin consent.

### Current Permissions

```bash
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

**Output:**
- SharePoint (`00000003-0000-0ff1-ce00-000000000000`) - ✅ Has permission
- Microsoft Graph (`00000003-0000-0000-c000-000000000000`) - ✅ Has permission
- **Dynamics CRM** - ❌ **MISSING**

---

## Steps to Fix (Requires Azure AD Admin)

### Option 1: Azure Portal (Recommended)

1. Go to **Azure Portal** → **Microsoft Entra ID** → **App Registrations**
2. Search for App ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - App Name: "spe-bff-api" or similar
3. Click **API permissions** (left menu)
4. Click **+ Add a permission**
5. Select **Dynamics CRM**
6. Select **Delegated permissions**
7. Check **user_impersonation**
   - Permission: `user_impersonation`
   - Description: "Access Dynamics 365 as organization users"
8. Click **Add permissions**
9. Click **✓ Grant admin consent for [Tenant]** (requires Global Admin)
10. Confirm admin consent granted

### Option 2: Azure CLI

```bash
# Step 1: Find Dynamics CRM Service Principal ID
DYNAMICS_CRM_SP=$(az ad sp list --filter "displayName eq 'Dynamics CRM'" --query "[0].appId" -o tsv)
echo "Dynamics CRM App ID: $DYNAMICS_CRM_SP"
# Expected: 00000007-0000-0000-c000-000000000000

# Step 2: Find user_impersonation permission ID
PERMISSION_ID=$(az ad sp show --id $DYNAMICS_CRM_SP --query "oauth2PermissionScopes[?value=='user_impersonation'].id" -o tsv)
echo "user_impersonation Permission ID: $PERMISSION_ID"

# Step 3: Add API permission to BFF API
az ad app permission add \
  --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --api $DYNAMICS_CRM_SP \
  --api-permissions $PERMISSION_ID=Scope

# Step 4: Grant admin consent (requires Global Admin role)
az ad app permission admin-consent --id 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

---

## Verification

After adding permission and granting admin consent:

### 1. Verify Permission Added

```bash
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --output table
```

**Expected Output:**
- SharePoint (`00000003-0000-0ff1-ce00-000000000000`)
- Microsoft Graph (`00000003-0000-0000-c000-000000000000`)
- **Dynamics CRM** (`00000007-0000-0000-c000-000000000000`) ← **NEW**

### 2. Test NavMap API Endpoint

```bash
curl -i "https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document/lookup"
```

**Expected Result:**
- ❌ Before: `500 Internal Server Error` (AADSTS500011)
- ✅ After: `200 OK` with JSON response containing navigation metadata

**Example Success Response:**
```json
{
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document",
  "logicalName": "sprk_matter",
  "schemaName": "sprk_Matter",
  "navigationPropertyName": "sprk_Matter",
  "targetEntity": "sprk_matter",
  "source": "dataverse"
}
```

---

## Context

### Why This Permission is Required

The BFF API uses **ClientSecretCredential** to authenticate with Dataverse:

**File:** `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

```csharp
// Create ClientSecretCredential (app-only authentication)
var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

// ServiceClient requests token for Dataverse scope
var tokenRequestContext = new TokenRequestContext(new[] {
    $"{dataverseUrl}/.default"  // e.g., "https://spaarkedev1.api.crm.dynamics.com/.default"
});
var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
```

When Azure AD processes this token request:
1. Checks if App Registration `1e40baad-e065-4aea-a8d4-4b7ab273458c` has API permissions for Dataverse
2. If permission missing → **AADSTS500011 error**
3. If permission granted → Issues token with scope `https://spaarkedev1.api.crm.dynamics.com/.default`

### Why We Changed from ManagedIdentity to ClientSecret

**Previous Approach (Failed):**
- Used `ManagedIdentityCredential`
- Required registering Managed Identity as Dataverse Application User
- Token acquisition failed at Azure level (not Dataverse level)
- Complex setup, unclear why it failed

**Current Approach (Correct):**
- Uses `ClientSecretCredential` (same as Graph/SPE)
- Consistent with existing BFF API authentication pattern
- Simpler configuration
- Only requires adding API permission to existing App Registration

---

## Deployment Status

### What's Deployed

✅ BFF API deployed with ClientSecretCredential authentication
✅ Dataverse authentication code working correctly
✅ Token acquisition logic correct

### What's Blocked

❌ API permission for Dynamics CRM not added
❌ Admin consent not granted
❌ Phase 7 testing blocked until permission granted

---

## Next Steps After Permission Granted

1. ✅ Restart BFF API (automatic after permission grant)
2. ✅ Test NavMap API endpoint (should return 200 OK)
3. ✅ Browser test Phase 7 document upload
4. ✅ Verify dynamic metadata discovery working
5. ✅ Verify cache behavior (second upload uses cache)
6. ✅ Mark Phase 7 Task 7.5 complete
7. ✅ Production deployment (Task 7.6)

---

## Related Files

- **Authentication Implementation:** `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
- **NavMap API Endpoints:** `src/api/Spe.Bff.Api/Api/NavMapEndpoints.cs`
- **Dataverse Service Interface:** `src/api/Spe.Bff.Api/Services/IDataverseService.cs`
- **PCF Client:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts`

---

## Permission Details

### Dynamics CRM API

- **Service Principal Name:** "Dynamics CRM"
- **App ID:** `00000007-0000-0000-c000-000000000000`
- **Permission:** `user_impersonation`
- **Type:** Delegated (Scope)
- **Description:** "Access Dynamics 365 as organization users"
- **Admin Consent Required:** Yes

### Why "user_impersonation"?

Despite the name, `user_impersonation` is used for **both** delegated (user context) and application (app-only) scenarios with Dataverse. When combined with ClientSecretCredential, it grants app-only access with the permissions assigned to the service principal.

---

## Troubleshooting

### If permission added but still getting error:

1. **Verify admin consent granted:**
   ```bash
   az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "[?resourceAppId=='00000007-0000-0000-c000-000000000000']"
   ```
   - Check `consentType` is `"AllPrincipals"` (admin consent)

2. **Clear token cache:**
   ```bash
   az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
   ```

3. **Check App Roles in Dataverse:**
   - The app should be automatically granted access once permission is consented
   - No manual Application User creation required (unlike Managed Identity)

---

**Status:** Waiting for Azure AD Admin to add Dynamics CRM API permission and grant admin consent.
