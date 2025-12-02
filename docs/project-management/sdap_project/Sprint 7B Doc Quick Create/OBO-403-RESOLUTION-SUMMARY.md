# OBO 403 Forbidden - Resolution Summary

**Date:** 2025-10-09
**Issue:** HTTP 403 when uploading files via OBO endpoint
**Status:** 🔴 **ROOT CAUSE IDENTIFIED** - Ready for resolution

---

## TL;DR

**Problem:** BFF API returns 403 Forbidden when uploading files to SharePoint Embedded

**Root Cause:** BFF API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) is not registered with the SPE container type (`8a6ce34c-6055-4681-8f87-2f4f9f921c06`)

**Solution:** Register BFF API with container type and grant `WriteContent` delegated permission

**Key Insight:** Token B correctly has BFF API's `appid` (this is how OBO works). SPE validates this `appid` and requires explicit registration.

---

## What We Discovered

### The OAuth2 OBO Flow is Working Correctly ✅

```
PCF Control → Token A → BFF API → OBO Exchange → Token B → Microsoft Graph → SPE
```

**Token A (from PCF):**
- ✅ `aud`: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` (BFF API)
- ✅ `appid`: `170c98e1-d486-4355-bcbe-170454e0207c` (PCF app)
- ✅ User identity: `ralph.schroeder@spaarke.com`

**Token B (from OBO):**
- ✅ `aud`: `https://graph.microsoft.com`
- ✅ **`appid`: `1e40baad-e065-4aea-a8d4-4b7ab273458c` (BFF API)** ← This is CORRECT
- ✅ User identity: `ralph.schroeder@spaarke.com` (preserved)
- ✅ Scopes: `FileStorageContainer.Selected`, `Sites.FullControl.All`, etc.
- ✅ `idtyp`: `user` (delegated)

### Why Token B Has BFF API's appid

This is **expected and correct** OAuth2 OBO behavior:

> In OBO flow, Token B identifies the **middle-tier service** (BFF API) as the application making the downstream call, while preserving the **user's identity** from Token A.

From Microsoft docs:
> "The middle-tier service authenticates to the Microsoft identity platform and requests a token to access the downstream API."

The middle-tier's credentials authenticate the OBO request, so its `appid` appears in Token B.

### Why SPE Returns 403

SharePoint Embedded has **application-level permissions** separate from user permissions:

1. ✅ **User check:** Is `ralph.schroeder@spaarke.com` authorized? → YES (owner)
2. ❌ **Application check:** Is BFF API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) registered with container type? → **NO**
3. **Result:** 403 Forbidden (application lacks container type permissions)

From SPE docs:
> "Applications need container type application permissions to access containers of that container type, **even in delegated scenarios**."

---

## What You Were Right About

Your intuition was correct:

> "The BFF API is acting somewhat as an anonymous requester—it's the confidential requesting app that is making the request, and this entire process is validating authority."

**Exactly right!** The BFF API IS the requesting application (from SPE's perspective), and SPE validates that this application has been granted permissions.

You also correctly identified:

> "I have not seen in any documentation that the app needs to register with a container."

The docs are not explicit about this for **middle-tier APIs in OBO scenarios**. The registration requirement is primarily documented for **direct client access**. However, SPE validates the `appid` claim regardless of whether it's direct access or OBO.

---

## The Solution

### Quick Version

Execute this API call (as owning application):

```http
PUT https://[tenant].sharepoint.com/_api/v2.1/storageContainerTypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/applicationPermissions
Authorization: Bearer [token-with-Container.Selected]
Content-Type: application/json

{
  "value": [
    {
      "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
      "delegated": ["WriteContent"],
      "appOnly": []
    }
  ]
}
```

### What This Does

Grants the BFF API application:
- **Delegated permission:** `WriteContent`
- **Meaning:** Can create/modify files when acting on behalf of an authorized user
- **Effect:** SPE will allow Token B (with BFF API's `appid`) to upload files as long as user has permissions

### Why This Fixes the Problem

**Before registration:**
```
SPE receives Token B → Checks appid (BFF API) → Not registered → 403 Forbidden
```

**After registration:**
```
SPE receives Token B → Checks appid (BFF API) → Registered with WriteContent → Checks user → User is owner → ✅ Allow upload
```

---

## Implementation Steps

1. **Gather prerequisites** (need from you):
   - Tenant SharePoint URL
   - Owning application credentials
   - Access to container type registration API

2. **Execute registration:**
   - Authenticate as owning application
   - Call registration API (see [CONTAINER-TYPE-REGISTRATION-GUIDE.md](./CONTAINER-TYPE-REGISTRATION-GUIDE.md))
   - Verify registration successful

3. **Test:**
   - No code changes needed!
   - OBO upload should work immediately
   - Test with PCF control

4. **Document:**
   - Add to deployment guide
   - Update environment setup checklist

---

## What Changed Your Mind

Initially, you suspected the BFF API shouldn't need registration because:
> "The BFF API is acting... as the confidential requesting app"

You were right about the architecture, but SPE's permission model requires **both**:
- ✅ User permissions (owner/contributor on specific container)
- ✅ Application permissions (registered with container type)

This is **"defense in depth"** security:
1. User must be authorized → Prevents unauthorized user access
2. Application must be registered → Prevents rogue applications from accessing containers

Even though the BFF API is acting on behalf of an authorized user, SPE still validates the application making the call.

---

## Why This Was Hard to Diagnose

1. **OBO flow is complex** - Multiple tokens, multiple applications
2. **Token B looked "wrong"** - Having BFF API's `appid` seemed like a problem
3. **Documentation gap** - SPE docs don't explicitly cover middle-tier OBO scenarios
4. **Error message is generic** - Graph returns "Access denied" without specifics

The breakthrough came from:
1. ✅ Capturing and analyzing Token B
2. ✅ Understanding OBO flow specification (Token B appid is correct)
3. ✅ Finding SPE documentation on application-level permissions
4. ✅ Connecting the dots: SPE checks `appid` claim for registration

---

## Validation That We're Right

### Evidence Supporting This Diagnosis

1. **OBO flow working perfectly:**
   - Token exchange succeeds
   - Token B has all required scopes
   - User identity preserved

2. **User permissions verified:**
   - `ralph.schroeder@spaarke.com` is owner on container
   - Confirmed via GET `/containers/{id}/permissions`

3. **Postman works with PCF app:**
   - Postman uses PCF app credentials directly
   - PCF app IS registered with container type (user granted permissions)
   - Same container, same user, different app → Works!

4. **Error pattern matches documentation:**
   - Graph returns 403 "Access denied"
   - No specific error code
   - Matches SPE "application lacks permissions" pattern

5. **Microsoft documentation confirms:**
   - "Applications need container type application permissions"
   - "Even in delegated scenarios"
   - Registration API exists specifically for this purpose

### What We Ruled Out

- ❌ OBO token exchange failing → Fixed with client secret
- ❌ Missing Graph scopes → Token B has all required scopes
- ❌ User permissions issue → User is owner
- ❌ Token format issue → Token B is correctly formatted delegated token
- ❌ Graph API endpoint issue → Same endpoint works in Postman
- ❌ Container ID issue → Same container works in Postman

**Only remaining factor:** Different `appid` in token (BFF API vs PCF app)

---

## Confidence Level

**95% confident** this is the root cause:

**Why confident:**
1. All other factors verified working
2. Matches documented SPE permission model
3. Explains why Postman works (different appid)
4. Solution is documented by Microsoft
5. Error pattern matches

**5% uncertainty:**
- Haven't executed the fix yet
- Possible there's an additional undocumented requirement

**Risk mitigation:**
- Registration is reversible (can be removed if wrong)
- Won't affect other functionality
- Can test immediately after registration

---

## Expected Outcome

### After Registration

**OBO upload request:**
```http
PUT /api/obo/containers/{id}/files/test.txt
Authorization: Bearer [Token A]
```

**Expected response:**
```json
{
  "id": "01...",
  "name": "test.txt",
  "size": 123,
  "webUrl": "https://...",
  "createdDateTime": "2025-10-09T..."
}
```

**Application Insights logs:**
```
[INFO] OBO token exchange successful
[INFO] OBO token scopes: FileStorageContainer.Selected Sites.FullControl.All ...
[INFO] OBO upload successful - DriveItemId: {id}
```

**PCF control:**
- File picker works
- Upload succeeds
- Success message displayed
- Files appear in container

---

## Alternative Approaches (If Registration Fails)

If container type registration is not feasible for some reason:

### Option A: Direct Graph Call from PCF
- Remove BFF API from flow
- PCF calls Graph API directly
- **Pros:** PCF app already registered
- **Cons:** Lose BFF benefits (retry, monitoring, caching)

### Option B: App-Only with User Context Header
- BFF uses Managed Identity (app-only)
- Add custom header to track user
- **Pros:** No container type registration needed
- **Cons:** Lose user-level permissions enforcement

### Option C: Hybrid Approach
- Use OBO for user context verification
- Use Managed Identity for actual Graph call
- **Pros:** Both user validation and app permissions
- **Cons:** More complex, two tokens

**Recommendation:** Proceed with container type registration. It's the "right" solution architecturally.

---

## Documentation Created

1. **[OBO-403-ROOT-CAUSE-ANALYSIS.md](./OBO-403-ROOT-CAUSE-ANALYSIS.md)**
   - Comprehensive technical analysis
   - Evidence and diagnosis
   - OAuth2 OBO flow details
   - SPE permission model explanation

2. **[CONTAINER-TYPE-REGISTRATION-GUIDE.md](./CONTAINER-TYPE-REGISTRATION-GUIDE.md)**
   - Step-by-step registration process
   - PowerShell and cURL examples
   - Troubleshooting guide
   - Testing procedures

3. **[OBO-403-RESOLUTION-SUMMARY.md](./OBO-403-RESOLUTION-SUMMARY.md)** (this file)
   - Executive summary
   - Quick reference
   - Key insights

---

## Next Actions for You

### Immediate (to proceed with fix):

1. **Find tenant SharePoint URL:**
   ```powershell
   Connect-AzAccount -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2
   # Look for tenant domain in output
   ```

2. **Identify owning application:**
   ```powershell
   # Use PnP PowerShell or contact SharePoint admin
   Get-PnPContainerType | Where-Object {$_.ContainerTypeId -eq "8a6ce34c-6055-4681-8f87-2f4f9f921c06"}
   ```

3. **Obtain owning app credentials:**
   - Client ID
   - Client Secret or Certificate
   - Must have `Container.Selected` permission

4. **Execute registration:**
   - Follow [CONTAINER-TYPE-REGISTRATION-GUIDE.md](./CONTAINER-TYPE-REGISTRATION-GUIDE.md)
   - Test immediately after

### After successful registration:

1. **Test OBO endpoint** - Should work immediately
2. **Test PCF control** - File upload should work
3. **Update deployment docs** - Add registration to setup guide
4. **Plan for other environments** - Test, Staging, Production

---

## Questions?

**Q: Why doesn't PCF app need registration?**
A: PCF app IS registered (that's what happens when user grants permissions via consent screen). But PCF app never calls Graph directly, so its registration isn't checked.

**Q: Why does Postman work?**
A: Postman uses PCF app credentials and calls Graph directly. Token has PCF app's `appid`, which IS registered.

**Q: Will this affect existing functionality?**
A: No. Only affects OBO upload endpoint. Admin/management endpoints use Managed Identity (different auth path).

**Q: Do we need to modify code?**
A: No code changes needed! Registration is purely configuration.

**Q: What if registration doesn't work?**
A: We can try alternative approaches (see above), but registration should work based on Microsoft docs.

---

**Status:** Awaiting prerequisites to execute registration.

**Confidence:** 95% this will resolve the issue.

**Next Step:** Gather tenant SharePoint URL and owning application credentials.
