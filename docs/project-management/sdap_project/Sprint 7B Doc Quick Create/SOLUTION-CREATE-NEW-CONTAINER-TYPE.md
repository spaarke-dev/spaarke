# Solution: Create New Container Type
## Resolving 403 Error by Creating PCF-Owned Container Type

**Date:** 2025-10-09
**Status:** ✅ **READY TO EXECUTE**
**Approach:** Create new container type instead of modifying existing one

---

## Why This Approach?

### Cannot Change Existing Container Type Owner ❌

**Finding:** Container type ownership is **immutable** in SharePoint Embedded.

**Evidence:**
- Container type and owning application relationship cannot be changed
- Existing container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06` has unknown owner
- PCF app getting 401 Unauthorized when trying to register BFF API
- Microsoft documentation confirms container type ownership is permanent

**Conclusion:** Cannot use existing container type - must create new one.

### Create New Container Type Owned by PCF App ✅

**Approach:**
1. Create new container type with PCF app as owner (via PowerShell)
2. PCF app immediately registers BFF API as guest app
3. Create new containers of this type
4. OBO file upload works instantly

**Benefits:**
- ✅ Complete control over container type
- ✅ BFF API registered immediately
- ✅ No permission hunting needed
- ✅ Clean architecture
- ✅ Works in 5-10 minutes

---

## Step-by-Step Process

### Step 1: Create Container Type (PowerShell)

**Requirement:** SharePoint Administrator or Global Administrator role

**Script:** `Create-ContainerType-PowerShell.ps1`

**Options:**

#### Option A: Trial Container Type (Recommended for Testing)
```powershell
.\scripts\Create-ContainerType-PowerShell.ps1 -Trial
```

**Pros:**
- No billing setup required
- Quick to create
- Perfect for development/testing
- No Azure subscription needed

**Cons:**
- Limited to 1 per tenant
- May have usage limitations

#### Option B: Standard Container Type (Production)
```powershell
.\scripts\Create-ContainerType-PowerShell.ps1
```

**Pros:**
- Production-ready
- Scalable
- Up to 25 per tenant

**Cons:**
- Requires Azure subscription
- Needs billing configuration
- Requires resource group setup

**Recommendation:** Use **Trial** for initial testing, migrate to Standard later if needed.

### Step 2: Register BFF API with New Container Type

After container type is created, the script will output the new Container Type ID.

**Run:**
```powershell
.\scripts\Register-BffApiWithContainerType.ps1 -ContainerTypeId "[NEW_CONTAINER_TYPE_ID]"
```

**This will:**
- Register PCF app (owner) with full permissions
- Register BFF API (guest) with WriteContent delegated
- Enable OBO file upload immediately

**Expected:** HTTP 200 OK (registration succeeds)

### Step 3: Create Test Container

Use Graph API or your application to create a container of the new type:

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containers
Authorization: Bearer [PCF-app-token]
Content-Type: application/json

{
  "displayName": "Spaarke Inc (New)",
  "description": "Test container for OBO flow",
  "containerTypeId": "[NEW_CONTAINER_TYPE_ID]"
}
```

**Save the container ID** from the response.

### Step 4: Grant Yourself Permissions

Add yourself as owner of the new container:

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containers/{containerId}/permissions
Authorization: Bearer [admin-token]
Content-Type: application/json

{
  "roles": ["owner"],
  "grantedToV2": {
    "user": {
      "userPrincipalName": "ralph.schroeder@spaarke.com"
    }
  }
}
```

### Step 5: Test OBO Upload

```http
PUT https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{NEW_CONTAINER_ID}/files/test.txt
Authorization: Bearer [Token-A-from-PCF]
Content-Type: text/plain

This is a test file!
```

**Expected:** HTTP 200 OK with file metadata (no more 403!) ✅

---

## What About Existing Container?

**Existing Container:**
- ID: `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`
- Name: "Spaarke Inc"
- Created: 10/08/2025
- Issue: Unknown owner, cannot register BFF API

**Options:**

### Option 1: Leave It As-Is
- Use new container type going forward
- Existing container remains accessible via original owning app
- No data migration needed
- Two container types coexist

### Option 2: Migrate Data
If the existing container has important data:

```powershell
# 1. List files in old container (using original owning app or user token)
GET /containers/{OLD_CONTAINER_ID}/drive/root/children

# 2. Download each file
GET /containers/{OLD_CONTAINER_ID}/drive/items/{itemId}/content

# 3. Upload to new container
PUT /api/obo/containers/{NEW_CONTAINER_ID}/files/{filename}
```

### Option 3: Find Original Owner
If you absolutely must use the existing container:
- Contact SharePoint admin
- Query container types: `Get-SPOContainerType`
- Find owning app ID for container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
- Use that app to register BFF API

**Recommendation:** Use Option 1 (leave as-is, use new container type) - fastest and simplest.

---

## Architecture After Change

### Before (Not Working)
```
PCF Control → Token A → BFF API → OBO Token B → Graph → SPE Container
                                                          ↓
                                                     403 Forbidden
                                                     (Unknown owner,
                                                      BFF not registered)
```

### After (Working)
```
PCF Control → Token A → BFF API → OBO Token B → Graph → New SPE Container
                                                          ↓
                                                     200 OK
                                                     (PCF owns type,
                                                      BFF registered)
```

**Key Change:** Using container of PCF-owned container type where BFF API is properly registered.

---

## Configuration Updates Needed

### 1. PCF Control Configuration

If your PCF control hardcodes the container ID, you'll need to update it to use the new container:

**Before:**
```typescript
const CONTAINER_ID = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50";
```

**After:**
```typescript
const CONTAINER_ID = "[NEW_CONTAINER_ID]";  // From Step 3
```

**Better:** Make it configurable per environment:
```typescript
const CONTAINER_ID = config.get("spe.containerId");
```

### 2. Environment Variables

Update App Service configuration:

```bash
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group [rg-name] \
  --settings \
    SPE_CONTAINER_TYPE_ID="[NEW_CONTAINER_TYPE_ID]" \
    SPE_DEFAULT_CONTAINER_ID="[NEW_CONTAINER_ID]"
```

### 3. Documentation

Update any documentation that references the old container type ID or container ID.

---

## Testing Checklist

After creating new container type and container:

- [ ] Container type created successfully (PowerShell)
- [ ] Container type ID saved
- [ ] BFF API registered with container type (HTTP 200)
- [ ] Test container created
- [ ] Container ID saved
- [ ] User permissions granted on container
- [ ] OBO upload test succeeds (HTTP 200, not 403)
- [ ] File appears in container
- [ ] PCF control updated with new container ID (if needed)
- [ ] Application Insights shows success logs
- [ ] No 403 errors in logs

---

## Rollback Plan

If something goes wrong:

### Delete Container Type
```powershell
Connect-SPOService -Url "https://spaarke-admin.sharepoint.com"
Remove-SPOContainerType -ContainerTypeId "[NEW_CONTAINER_TYPE_ID]"
```

### Continue Using Old Approach
- Existing container still works for direct access
- Can continue investigation of original owner
- BFF API workarounds still available

**No Data Loss:** Creating new container type doesn't affect existing containers.

---

## Timeline Estimate

| Step | Duration | Cumulative |
|------|----------|------------|
| Install SharePoint Online PowerShell | 2 min | 2 min |
| Run container type creation script | 1 min | 3 min |
| Register BFF API | 30 sec | 3.5 min |
| Create test container | 30 sec | 4 min |
| Grant user permissions | 30 sec | 4.5 min |
| Test OBO upload | 1 min | 5.5 min |
| Update PCF config (if needed) | 2 min | 7.5 min |

**Total:** ~5-8 minutes from start to working OBO upload

---

## Success Criteria

✅ **OBO file upload returns HTTP 200 OK** (was 403 Forbidden)

✅ **Application Insights logs show:**
```
[INFO] OBO token exchange successful
[INFO] OBO token scopes: FileStorageContainer.Selected ...
[INFO] OBO upload successful - DriveItemId: {id}
```

✅ **File appears in container** when queried via Graph API

✅ **PCF control can upload files** without errors

---

## Next Steps After Success

### 1. Environment Parity

Repeat the process for other environments:

**Test Environment:**
```powershell
.\scripts\Create-ContainerType-PowerShell.ps1 -ContainerTypeName "Spaarke Docs (Test)" -Trial
```

**Production Environment:**
```powershell
.\scripts\Create-ContainerType-PowerShell.ps1 -ContainerTypeName "Spaarke Docs (Prod)"
# Then configure billing:
Add-SPOContainerTypeBilling -ContainerTypeId "[PROD_TYPE_ID]" ...
```

### 2. Remove Debug Logging

Clean up temporary debugging code in [GraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs):

- Remove Token B full JWT logging (line ~155)
- Remove excessive token claims logging
- Keep error logging only

### 3. Documentation

Update project documentation:
- Record new container type ID in environment config
- Document container type creation process
- Add to deployment/setup guides

### 4. Monitor

Watch Application Insights for:
- OBO upload success rate
- Any remaining 403 errors
- Performance metrics

---

## Alternative: If PowerShell Fails

If you cannot run PowerShell or lack SharePoint Administrator permissions:

**Contact IT/Admin:**
- Ask them to run `Create-ContainerType-PowerShell.ps1`
- Or ask them to create container type via SharePoint Admin Center UI
- Provide: Owning App ID (`170c98e1-d486-4355-bcbe-170454e0207c`)

**Wait for Container Type API:**
Microsoft is working on Graph API for container type management (currently PowerShell-only).

---

## Summary

**Problem:** Cannot register BFF API with existing container type (unknown owner, immutable)

**Solution:** Create NEW container type owned by PCF app

**Steps:**
1. Run PowerShell script (requires SharePoint admin)
2. Register BFF API with new container type
3. Create test container
4. Test OBO upload

**Result:** 403 error resolved, OBO file upload works

**Time:** 5-8 minutes

**Ready to execute:** ✅ YES

---

**Run this command to get started:**
```powershell
.\scripts\Create-ContainerType-PowerShell.ps1 -Trial
```
