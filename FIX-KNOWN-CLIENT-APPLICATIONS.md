# Fix: Add knownClientApplications to SPE-BFF-API

**Root Cause Confirmed**: ✅
- SPE-BFF-API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) has `knownClientApplications: null`
- This prevents the OBO flow from working
- Causes AADSTS50013 error

**Solution**: Add PCF client app to `knownClientApplications` in the manifest

---

## Manual Fix (Azure Portal)

### Step 1: Open App Registration
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Search for and open: **SPE-BFF-API**
   - App ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

### Step 2: Edit Manifest
1. In the left menu, click **Manifest**
2. Find the `knownClientApplications` property (should be near the top, around line 80-90)
3. It currently shows:
   ```json
   "knownClientApplications": [],
   ```

4. Change it to:
   ```json
   "knownClientApplications": [
     "170c98e1-d486-4355-bcbe-170454e0207c"
   ],
   ```

5. Click **Save** at the top

### Step 3: Wait for Propagation
- Changes can take 5-15 minutes to propagate through Azure AD
- No restart of the API is needed

### Step 4: Test
1. Open a Matter record in Dataverse
2. Click "Upload Documents"
3. Select a file and upload
4. Check for success (no 500 error)

---

## Alternative: PowerShell Method

If you prefer PowerShell:

```powershell
# Install Microsoft.Graph module if not already installed
Install-Module Microsoft.Graph -Scope CurrentUser

# Connect to Graph
Connect-MgGraph -Scopes "Application.ReadWrite.All"

# Get the application object
$app = Get-MgApplication -Filter "appId eq '1e40baad-e065-4aea-a8d4-4b7ab273458c'"

# Update knownClientApplications
Update-MgApplication -ApplicationId $app.Id -KnownClientApplications @("170c98e1-d486-4355-bcbe-170454e0207c")

# Verify
$updated = Get-MgApplication -ApplicationId $app.Id
Write-Host "knownClientApplications: $($updated.KnownClientApplications)"
```

---

## What This Does

**Before**:
1. PCF requests token for BFF API ✅
2. Azure AD issues Token A ✅
3. BFF API tries OBO with Token A ❌
4. Azure AD rejects: "I don't trust this client app to act on behalf of users for this API"

**After**:
1. PCF requests token for BFF API ✅
2. Azure AD issues Token A ✅
3. BFF API tries OBO with Token A ✅
4. Azure AD allows: "This client app is in knownClientApplications, OBO is permitted"
5. Token B (Graph token) issued ✅
6. File uploads to SharePoint Embedded ✅

---

## Verification Summary

**Configuration Checked**:
- ✅ App registrations exist (both)
- ✅ API permissions configured (Graph + Dynamics)
- ✅ Admin consent granted (AllPrincipals)
- ✅ Client secret valid until 2027
- ✅ signInAudience: AzureADMyOrg (single tenant)
- ❌ knownClientApplications: null (empty)

**What Needs Fixing**:
- Only the `knownClientApplications` property

**Why This Wasn't Caught Earlier**:
- The Sprint 8 documentation didn't mention this requirement
- The DATAVERSE-AUTHENTICATION-GUIDE.md doesn't cover OBO scenarios
- This is a specific requirement for OBO flow that's easy to miss

---

## Expected Result

After making this change and waiting 5-15 minutes:

**Current Error**:
```
AADSTS50013: Assertion failed signature validation.
[Reason - The key was not found.]
```

**After Fix**:
- File uploads succeed
- API returns 200 OK
- Document records created in Dataverse
- Files visible in SharePoint Embedded

---

## References

**Microsoft Documentation**:
- [knownClientApplications property](https://learn.microsoft.com/en-us/azure/active-directory/develop/reference-app-manifest#knownclientapplications-attribute)
  - "Used in bundled consent scenarios"
  - "Specifies the client applications that are bundled with this resource application"

- [On-Behalf-Of flow](https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-on-behalf-of-flow)
  - Section: "Delegated consent for pre-authorized applications"

**Assessment Document**:
- File: `AUTHENTICATION-FLOW-ASSESSMENT.md`
- Section: "Most Likely Root Cause"
- Lines 290-310
