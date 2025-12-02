# Next Steps: Container Type Registration

**Date:** 2025-10-09
**Status:** Ready for you to execute
**Blocker:** BFF API needs registration with container type to fix 403 error

---

## What We Discovered

### ✅ Confirmed Information

1. **Container Type ID:** `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
2. **Container ID:** `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`
3. **Container Name:** "Spaarke Inc"
4. **Container Created:** 10/08/2025
5. **Likely Owning App:** PCF app (`170c98e1-d486-4355-bcbe-170454e0207c` - "Spaarke DSM-SPE Dev 2")

### ❌ What Didn't Work

- Tried to register BFF API using PCF app credentials → Got **401 Unauthorized**
- This means PCF app lacks `Container.Selected` app-only permission

---

## Option 1: Grant Container.Selected Permission to PCF App (Recommended)

This is the cleanest approach if PCF app is indeed the owning application.

### Steps

1. **Go to Azure Portal**
   - Navigate to: https://portal.azure.com
   - Go to **Azure Active Directory** → **App Registrations**
   - Search for: "Spaarke DSM-SPE Dev 2"
   - Or use App ID: `170c98e1-d486-4355-bcbe-170454e0207c`

2. **Add SharePoint Permission**
   - Click **API Permissions** (left menu)
   - Click **+ Add a permission**
   - Select **SharePoint**
   - Choose **Application permissions** (not Delegated!)
   - Find and select: **`Container.Selected`**
   - Click **Add permissions**

3. **Grant Admin Consent**
   - Back on API Permissions page
   - Click **✓ Grant admin consent for [Spaarke]**
   - Confirm the action
   - Wait for status to show "Granted"

4. **Run Registration Script**
   ```powershell
   cd c:\code_files\spaarke
   .\scripts\Register-BffApiWithContainerType.ps1
   ```

   > **Note:** Per Microsoft documentation ([KM-SPE-WEB-APPLICATION-CONTAINER-TYPE-REGISTRATION.md](../../../docs/KM-SPE-WEB-APPLICATION-CONTAINER-TYPE-REGISTRATION.md)), the registration API requires `Container.Selected` app-only permission and should use client credentials flow. Our script uses client secret authentication, which works for development. Microsoft recommends certificate-based authentication for production environments.

5. **If Successful:**
   - Restart BFF API: `az webapp restart --name spe-api-dev-67e2xz --resource-group [rg]`
   - Test OBO upload - should work immediately!

---

## Option 2: Find the Actual Owning Application (If Option 1 Fails)

If PCF app is not the owning application, you need SharePoint admin access to query container types.

### Method A: PowerShell with PnP

```powershell
# Install PnP PowerShell (if not installed)
Install-Module -Name PnP.PowerShell -Scope CurrentUser

# Connect to SharePoint Admin Center
Connect-PnPOnline -Url "https://spaarke-admin.sharepoint.com" -Interactive

# List all container types
$containerTypes = Get-PnPContainerType

# Find ours
$ourType = $containerTypes | Where-Object {$_.ContainerTypeId -eq "8a6ce34c-6055-4681-8f87-2f4f9f921c06"}

# Get owning app ID
Write-Host "Owning Application ID: $($ourType.OwningApplicationId)"
```

### Method B: Contact SharePoint Admin

If you don't have SharePoint admin permissions:
- Contact your SharePoint administrator (or admin-dev@spaarke.com if that account has admin rights)
- Ask them to run the PowerShell above
- Get the `OwningApplicationId`
- Then repeat Option 1 with the correct owning app

---

## Option 3: Use BFF API as Its Own Owning App (Alternative Architecture)

If finding/using the owning app is too complex, you could restructure:

### Steps

1. **Create a new container type** owned by BFF API
2. **Migrate container** to new container type (if possible)
3. **BFF API can then register itself** and other apps

This is more work but gives you full control going forward.

---

## Quick Decision Tree

```
Can you access Azure Portal and grant permissions to PCF app?
│
├─ YES → Do Option 1 (5 minutes)
│        ├─ Works? → DONE! ✅
│        └─ 401 still? → PCF app isn't owner, do Option 2
│
└─ NO → Do you have SharePoint admin access?
         ├─ YES → Do Option 2 Method A
         │        Get owning app ID, then Option 1 with that app
         │
         └─ NO → Contact SharePoint admin (Option 2 Method B)
                  OR consider Option 3 (new container type)
```

---

## What Happens After Registration

Once BFF API is registered with the container type:

1. **No code changes needed** - Everything already in place
2. **Restart app service** - Clears MSAL token cache
3. **Test immediately** - OBO upload should work

### Test Command

```powershell
# Get Token A (from PCF app via Postman or browser)
$tokenA = "[your-token-here]"

# Test upload
Invoke-RestMethod -Uri "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/registration-test.txt" `
    -Method Put `
    -Headers @{"Authorization" = "Bearer $tokenA"; "Content-Type" = "text/plain"} `
    -Body "Test file after registration"

# Expected: HTTP 200 OK with file metadata
# Was getting: HTTP 403 Forbidden
```

---

## Why This Will Fix the 403 Error

**Current State:**
```
Token B → appid: BFF API → SPE checks registration → NOT FOUND → 403 Forbidden
```

**After Registration:**
```
Token B → appid: BFF API → SPE checks registration → FOUND with WriteContent → Check user → User is owner → ✅ 200 OK
```

---

## My Recommendation

**Start with Option 1** - It's the fastest path:

1. Grant `Container.Selected` to PCF app in Azure Portal (2 minutes)
2. Run registration script (30 seconds)
3. Restart BFF API (30 seconds)
4. Test (30 seconds)

**Total time: ~4 minutes**

If that doesn't work (still get 401), then PCF app isn't the owning application and you'll need Option 2.

---

## Files Created for You

All scripts are in `c:\code_files\spaarke\scripts\`:

| Script | Purpose |
|--------|---------|
| `Register-BffApiWithContainerType.ps1` | Register BFF API (ready to run after permission grant) |
| `Get-ContainerMetadata.ps1` | Query container info via Graph |
| `Get-ContainerMetadata-PCFApp.ps1` | Query using PCF app credentials |
| `Find-ContainerTypeOwner.ps1` | Find owning app (requires SharePoint admin) |
| `Decode-SharePointPermissions.ps1` | Decode permission IDs |

---

## Questions?

**Q: Why can't we just give BFF API Container.Selected and make it self-register?**
A: Only the **owning application** can modify container type registrations. It's like apartment building management - only the owner can add new tenants to the approved list.

**Q: What if I can't grant admin consent?**
A: You need someone with Global Administrator or Privileged Role Administrator role in Azure AD. Check with your IT admin or whoever manages your Azure tenant.

**Q: Will this affect production?**
A: No - this is Dev environment only. You'll need to repeat the process in each environment (Test, Prod) with their respective container types.

**Q: Is there any risk?**
A: No - granting `Container.Selected` to the PCF app (which likely already owns the container type) just formalizes what it should already have. It's a missing permission, not a new capability.

---

## Ready to Execute

**Your action item:** Go to Azure Portal and grant `Container.Selected` app-only permission to the PCF app ("Spaarke DSM-SPE Dev 2").

Then run:
```powershell
.\scripts\Register-BffApiWithContainerType.ps1
```

**Expected output:** "✅ REGISTRATION SUCCESSFUL!"

**Then:** Restart BFF API and test upload - 403 should be gone!

Let me know the result!
