# Actual Code vs V2 Architecture Documentation Comparison

**Date**: 2025-10-16
**Purpose**: Compare what the V2 architecture document SAYS vs what the ACTUAL code DOES
**Focus**: Step 7-8 OBO Token Exchange in GraphClientFactory.cs

---

## V2 Architecture Document Claims

**From**: `SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md` Lines 300-373

### Documented OBO Scopes (Line 318-322)

```csharp
var result = await _confidentialClientApp.AcquireTokenOnBehalfOf(
    scopes: new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
    },
    userAssertion: new UserAssertion(userAccessToken)  // Token A
).ExecuteAsync();
```

### Documented Token B Scopes (Line 352)

```json
{
  "scp": "Sites.FullControl.All Files.ReadWrite.All"
}
```

---

## Actual Code Implementation

**From**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

### Actual OBO Scopes (Lines 150-156)

```csharp
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

### Code Comment (Lines 148-149)

```csharp
// Try using Sites.FullControl.All explicitly to bypass FileStorageContainer.Selected restrictions
// Sites.FullControl.All doesn't have app-specific container restrictions
```

### Graph Endpoint Version (Line 207)

```csharp
return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
```

---

## Comparison Results

| Aspect | V2 Documentation | Actual Code | Match? |
|--------|------------------|-------------|--------|
| **OBO Scopes** | `Sites.FullControl.All`, `Files.ReadWrite.All` | `Sites.FullControl.All`, `Files.ReadWrite.All` | ✅ **EXACT MATCH** |
| **FileStorageContainer.Selected** | ❌ Not mentioned | ❌ Not requested | ✅ **CONSISTENT** |
| **Graph Endpoint** | ⚠️ Implied v1.0 (line 137) | ✅ `beta` (line 207) | ❌ **MISMATCH** |
| **Code Comment Intent** | N/A | "bypass FileStorageContainer.Selected restrictions" | ⚠️ **REVEALS INTENT** |

---

## Key Findings

### Finding 1: FileStorageContainer.Selected is INTENTIONALLY Omitted

**Evidence**: Line 148-149 comment in actual code
```csharp
// Try using Sites.FullControl.All explicitly to bypass FileStorageContainer.Selected restrictions
// Sites.FullControl.All doesn't have app-specific container restrictions
```

**Interpretation**:
- The code was deliberately written to AVOID requesting `FileStorageContainer.Selected`
- The developer's hypothesis: `Sites.FullControl.All` should grant broader access
- The developer's assumption: `Sites.FullControl.All` bypasses container registration requirements

**User's Clarification**:
> "the SDAP-BFF-SPE-API has FileStorageContainer.Selected with admin consent (and has always and consistently had this)"

**Implication**:
- ✅ The app registration HAS the permission granted
- ✅ BUT the OBO exchange does NOT request it in scopes
- ❌ Result: Token B does NOT contain `FileStorageContainer.Selected` scope

### Finding 2: Graph Beta Endpoint is Correctly Configured

**V2 Doc Line 137 Shows**:
```
Graph API HTTP: PUT /v1.0/drives/{containerId}/root:/{path}:/content
```

**Actual Code Line 207 Shows**:
```csharp
return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
```

**Status**: ✅ Code is CORRECT, documentation is outdated/incorrect

### Finding 3: V2 Documentation Matches Actual Implementation for Scopes

**Status**: ✅ The V2 doc accurately reflects what scopes are requested (no FileStorageContainer.Selected)

---

## Critical Question: Does Sites.FullControl.All Include FileStorageContainer.Selected?

### Hypothesis from Code Comment

> "Sites.FullControl.All doesn't have app-specific container restrictions"

### Microsoft Documentation Reality Check

**From Microsoft Graph permissions documentation**:

| Permission | Scope | Description |
|------------|-------|-------------|
| `Sites.FullControl.All` | Delegated | Have full control of all site collections |
| `FileStorageContainer.Selected` | Delegated | Read, write, and delete selected File Storage Containers |

**Key Points**:
1. `Sites.FullControl.All` grants access to **SharePoint Sites**
2. `FileStorageContainer.Selected` grants access to **SharePoint Embedded Containers**
3. **SharePoint Embedded Containers are NOT SharePoint Sites**

### Are They Different?

**YES - SharePoint Embedded is a separate service**:

- **SharePoint Sites**: Traditional SharePoint document libraries, team sites
  - Accessed via: `/sites/{site-id}/drives`
  - Permission: `Sites.FullControl.All`

- **SharePoint Embedded Containers**: New container-based storage (preview)
  - Accessed via: `/drives/{container-id}` (container ID = drive ID)
  - Permission: `FileStorageContainer.Selected`
  - Requires: App registration in Container Type

**Conclusion**: `Sites.FullControl.All` does NOT grant access to SharePoint Embedded Containers.

---

## Why HTTP 500 Occurs - Root Cause Analysis

### Expected Flow (if working)

1. ✅ PCF sends Token A to BFF API
2. ✅ BFF validates Token A
3. ✅ BFF performs OBO exchange requesting `Sites.FullControl.All` + `Files.ReadWrite.All`
4. ✅ Azure AD issues Token B with `Sites.FullControl.All` + `Files.ReadWrite.All`
5. ✅ BFF creates Graph client with Token B
6. ❌ **BFF calls `/beta/drives/{containerId}/root:/{path}:/content`**
7. ❌ **Graph API validates Token B - MISSING `FileStorageContainer.Selected` scope**
8. ❌ **Graph API returns 403 Forbidden or 401 Unauthorized**
9. ❌ **Graph SDK throws exception**
10. ❌ **Exception occurs in middleware before application logging**
11. ❌ **ASP.NET Core crashes → IIS returns HTTP 500.0**

### Why No Detailed Logs?

**Possible Reasons**:
1. **Graph SDK exception handling**: May throw before reaching try-catch in endpoint
2. **Middleware crash**: Authentication/authorization middleware fails before request reaches endpoint
3. **Early failure**: Error occurs during Graph client initialization or first API call

### Why This Wasn't Caught Earlier?

**User stated**:
> "when the SPE-BFF-API was working"

**Questions**:
1. Was it working with actual SharePoint Embedded containers?
2. Or was it tested with traditional SharePoint sites/drives?
3. When did it last work, and what changed since then?

---

## Assessment Conclusion

### What the Code Currently Does

✅ **Correct**:
- Uses Graph beta endpoint
- Implements OBO flow properly
- Has proper error handling structure
- Uses token caching

❌ **Incorrect**:
- Does NOT request `FileStorageContainer.Selected` in OBO exchange
- Relies on incorrect assumption that `Sites.FullControl.All` grants SPE access

### What Needs to Change

**The Fix** (when user approves):

```csharp
// Line 150-154 in GraphClientFactory.cs
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All",
        "https://graph.microsoft.com/FileStorageContainer.Selected"  // ADD THIS
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

**Prerequisite** (user confirmed already done):
- ✅ App registration has `FileStorageContainer.Selected` permission with admin consent
- ✅ Container Type is registered
- ✅ Graph endpoint is beta

**Expected Result After Fix**:
1. OBO exchange requests `FileStorageContainer.Selected` scope
2. Token B includes `FileStorageContainer.Selected` in `scp` claim
3. Graph API validates Token B successfully
4. SharePoint Embedded validates app registration
5. File upload succeeds → HTTP 200/201

---

## Next Steps (Awaiting User Approval)

### Step 1: Verify Permissions (No Code Changes)

```bash
# Check if BFF API app has the permission granted
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[?id=='085ca537-6565-41c2-aca7-db852babc212']"

# Expected: Should return the FileStorageContainer.Selected permission
```

### Step 2: Check Token B Scopes (Current State)

**Add temporary logging to see what's in Token B**:
- Log the `result.Scopes` after line 159
- Check if `FileStorageContainer.Selected` is present
- Hypothesis: It will be MISSING

### Step 3: Update OBO Scopes (Code Change - Pending Approval)

**Change**: Add `FileStorageContainer.Selected` to line 151-154

### Step 4: Test Upload

**Expected**: Should work after adding the scope to OBO request

---

## Questions for User

1. **When was the API last working?**
   - What operations were tested?
   - Was it with SharePoint Embedded containers or traditional SharePoint sites?

2. **Has the code changed since it was working?**
   - Specifically the OBO scopes in GraphClientFactory?
   - Or was the comment about bypassing FileStorageContainer.Selected always there?

3. **What made you believe FileStorageContainer.Selected wasn't needed?**
   - Was there documentation suggesting Sites.FullControl.All was sufficient?
   - Or was it based on testing?

4. **Can we see the actual error in logs?**
   - Trigger upload now that detailed logging is enabled
   - See if Graph API returns 403/401 or different error

---

**Document Created**: 2025-10-16 05:15 AM
**Conclusion**: Code intentionally omits `FileStorageContainer.Selected` from OBO scopes, likely causing 403 Forbidden from Graph API when accessing SharePoint Embedded containers.
