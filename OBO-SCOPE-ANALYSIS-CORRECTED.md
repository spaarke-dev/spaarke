# OBO Scope Analysis - Corrected Understanding

**Date**: 2025-10-16
**Purpose**: Explain current OBO behavior and what will change with scope addition
**Focus**: Microsoft Graph scopes ONLY (not SharePoint Online REST API scopes)

---

## Important Clarification

**SharePoint Online (SPO) Scopes**:
- Resource ID: `00000003-0000-0ff1-ce00-000000000000`
- Used for: SharePoint REST API (`_api/...` endpoints)
- **NOT used in this analysis** - only relevant for Container Type registration

**Microsoft Graph Scopes**:
- Resource ID: `00000003-0000-0000-c000-000000000000`
- Used for: Graph API calls (`/drives/...`, `/sites/...`, etc.)
- **THIS is what we're analyzing**

---

## Current Code Behavior

### Step 1: PCF Control Acquires Token A

**PCF Control** uses MSAL.js to get token for BFF API:

```typescript
const tokenRequest = {
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"]
};
const authResult = await msalInstance.acquireTokenSilent(tokenRequest);
```

**Token A Properties**:
```json
{
  "aud": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",  // BFF API
  "appid": "170c98e1-d486-4355-bcbe-170454e0207c",      // PCF Client
  "oid": "c74ac1af-ff3b-46fb-83e7-3063616e959c",        // User: Ralph
  "scp": "user_impersonation"                            // Scope for BFF API
}
```

**Purpose**: Allows PCF to call BFF API on behalf of the user.

---

### Step 2: BFF API Validates Token A

**Location**: `Program.cs` - JWT Bearer middleware

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
```

**Validation**:
- ✅ Signature: Valid (signed by Azure AD)
- ✅ Audience: `api://1e40baad...` (matches BFF API)
- ✅ Issuer: Azure AD tenant
- ✅ Not expired

**Result**: User is authenticated as `ralph.schroeder@spaarke.com`

---

### Step 3: BFF API Performs OBO Token Exchange (CURRENT CODE)

**Location**: `GraphClientFactory.cs` lines 150-156

```csharp
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
    },
    new UserAssertion(userAccessToken)  // Token A
).ExecuteAsync();
```

**What Happens**:
1. BFF API sends Token A to Azure AD
2. BFF API says: "I want to call Graph API on behalf of this user"
3. BFF API requests these scopes:
   - `Sites.FullControl.All` - Full control of SharePoint sites
   - `Files.ReadWrite.All` - Read/write files in OneDrive/SharePoint

**Azure AD Checks**:
- ✅ Is Token A valid? YES
- ✅ Does BFF API (`1e40baad...`) have permission `Sites.FullControl.All`? YES (delegated, admin consented)
- ✅ Does BFF API have permission `Files.ReadWrite.All`? YES (delegated, admin consented)
- ✅ Does user have these permissions? YES (user can access SharePoint/OneDrive)

**Azure AD Issues Token B**:
```json
{
  "aud": "https://graph.microsoft.com",                 // Graph API
  "appid": "1e40baad-e065-4aea-a8d4-4b7ab273458c",      // BFF API (changed!)
  "oid": "c74ac1af-ff3b-46fb-83e7-3063616e959c",        // User: Ralph (preserved)
  "scp": "Sites.FullControl.All Files.ReadWrite.All"    // ONLY these 2 scopes
}
```

**Key Point**: Token B does NOT contain `FileStorageContainer.Selected` because it was NOT requested.

---

### Step 4: BFF API Calls Graph API (CURRENT CODE)

**Location**: `UploadSessionManager.cs` (or similar)

```csharp
var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

// Upload file to SharePoint Embedded container
var uploadedItem = await graphClient.Drives[containerId].Root
    .ItemWithPath(path)
    .Content
    .PutAsync(content);
```

**Graph API Call**:
```http
PUT https://graph.microsoft.com/beta/drives/{containerId}/root:/{filename}:/content
Authorization: Bearer {Token B}
```

**What Graph API Validates**:
1. ✅ Is Token B valid? YES (signature, expiration)
2. ✅ Is audience correct? YES (`https://graph.microsoft.com`)
3. ✅ Does token have required scope? **CHECKING...**

---

### Step 5: Graph API Checks if Drive is SharePoint Embedded Container

**Graph API Logic** (internal):
```
IF drive.type == "SharePointEmbedded":
    required_scope = "FileStorageContainer.Selected"

    IF required_scope NOT IN token.scp:
        RETURN 403 Forbidden
        ERROR: "Insufficient privileges to complete the operation"
    END IF

    IF token.appid NOT IN container_type.registered_apps:
        RETURN 403 Forbidden
        ERROR: "Application is not registered for this container type"
    END IF
END IF
```

**Current Token B Scopes**: `Sites.FullControl.All Files.ReadWrite.All`

**Required Scope**: `FileStorageContainer.Selected`

**Result**: ❌ **403 FORBIDDEN** - Insufficient privileges

---

### Step 6: Error Propagation

**Graph SDK Behavior**:
```csharp
// Microsoft.Graph SDK throws ServiceException
throw new ServiceException(
    statusCode: HttpStatusCode.Forbidden,
    message: "Insufficient privileges to complete the operation",
    headers: responseHeaders
);
```

**Where Exception Occurs**:
- Inside `graphClient.Drives[containerId].Root.ItemWithPath(path).Content.PutAsync()`
- This is DEEP in the Graph SDK
- May occur during middleware processing

**Why HTTP 500 Instead of 403**:
- Exception may not be caught properly
- ASP.NET Core middleware crashes
- IIS returns generic 500 error page

**Why No Detailed Logs**:
- Exception occurs before application logging layer
- Graph SDK exception handling bypasses normal flow
- Detailed error mode now enabled should capture it

---

## Proposed Code Change Behavior

### Step 3: BFF API Performs OBO Token Exchange (AFTER CHANGE)

```csharp
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All",
        "https://graph.microsoft.com/FileStorageContainer.Selected"  // ADD THIS LINE
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

**What Changes**:
1. BFF API sends Token A to Azure AD (same as before)
2. BFF API says: "I want to call Graph API on behalf of this user" (same as before)
3. BFF API requests these scopes:
   - `Sites.FullControl.All` (same as before)
   - `Files.ReadWrite.All` (same as before)
   - `FileStorageContainer.Selected` **(NEW)**

**Azure AD Checks** (same as before, plus one more):
- ✅ Is Token A valid? YES
- ✅ Does BFF API have permission `Sites.FullControl.All`? YES
- ✅ Does BFF API have permission `Files.ReadWrite.All`? YES
- ✅ **Does BFF API have permission `FileStorageContainer.Selected`? YES** (user confirmed already granted)
- ✅ Does user have these permissions? YES

**Azure AD Issues Token B** (UPDATED):
```json
{
  "aud": "https://graph.microsoft.com",
  "appid": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "oid": "c74ac1af-ff3b-46fb-83e7-3063616e959c",
  "scp": "Sites.FullControl.All Files.ReadWrite.All FileStorageContainer.Selected"  // NOW INCLUDES THIS
}
```

---

### Step 5: Graph API Checks if Drive is SharePoint Embedded Container (AFTER CHANGE)

**Graph API Logic** (internal):
```
IF drive.type == "SharePointEmbedded":
    required_scope = "FileStorageContainer.Selected"

    IF required_scope NOT IN token.scp:
        RETURN 403 Forbidden  // WON'T HAPPEN ANYMORE
    END IF

    IF token.appid NOT IN container_type.registered_apps:
        RETURN 403 Forbidden  // Still could fail here if not registered
    END IF

    // Check user permissions on container
    IF user NOT IN container.allowed_users:
        RETURN 403 Forbidden
    END IF

    // ALL CHECKS PASSED
    RETURN 200 OK - allow file operation
END IF
```

**New Token B Scopes**: `Sites.FullControl.All Files.ReadWrite.All FileStorageContainer.Selected`

**Required Scope**: `FileStorageContainer.Selected`

**Result**: ✅ **Scope check PASSES**

**Next Check**: Is app registered in Container Type?
- User confirmed: YES (already done in previous work)
- If YES: ✅ Proceed to user permission check
- If NO: ❌ Still get 403 (but different error message)

**Final Check**: Does user have access to container?
- User (ralph.schroeder@spaarke.com) must have permissions
- If YES: ✅ **File upload succeeds - HTTP 201 Created**
- If NO: ❌ 403 (but this is correct behavior - user shouldn't have access)

---

## Summary: What Changes

### Current Behavior (Without FileStorageContainer.Selected)

```
PCF Control
  ↓ Token A (aud: BFF API, scp: user_impersonation)
BFF API validates Token A ✅
  ↓ OBO Exchange requests: Sites.FullControl.All, Files.ReadWrite.All
Azure AD
  ↓ Token B (aud: Graph, scp: Sites.FullControl.All Files.ReadWrite.All) ❌ MISSING SCOPE
Graph API
  ↓ Checks Token B for FileStorageContainer.Selected
  ❌ MISSING - Returns 403 Forbidden
BFF API
  ❌ ServiceException thrown
  ❌ Crashes middleware
  ❌ IIS returns HTTP 500
```

### New Behavior (With FileStorageContainer.Selected)

```
PCF Control
  ↓ Token A (aud: BFF API, scp: user_impersonation)
BFF API validates Token A ✅
  ↓ OBO Exchange requests: Sites.FullControl.All, Files.ReadWrite.All, FileStorageContainer.Selected
Azure AD
  ↓ Token B (aud: Graph, scp: Sites.FullControl.All Files.ReadWrite.All FileStorageContainer.Selected) ✅
Graph API
  ↓ Checks Token B for FileStorageContainer.Selected ✅
  ↓ Checks app registration in Container Type ✅ (already done)
  ↓ Checks user permissions on container ✅ (assuming user has access)
  ✅ Returns HTTP 201 Created
BFF API
  ✅ File uploaded successfully
  ✅ Returns FileHandleDto to PCF
PCF Control
  ✅ Shows success message to user
```

---

## Why Sites.FullControl.All Isn't Enough

### Common Misconception

**Code Comment (Line 148-149)**:
```csharp
// Try using Sites.FullControl.All explicitly to bypass FileStorageContainer.Selected restrictions
// Sites.FullControl.All doesn't have app-specific container restrictions
```

**The Assumption**: `Sites.FullControl.All` is a "superpower" scope that includes everything.

### The Reality

**SharePoint Sites != SharePoint Embedded Containers**

| Aspect | SharePoint Sites | SharePoint Embedded Containers |
|--------|------------------|-------------------------------|
| **Service** | Traditional SharePoint Online | New containerized storage (preview) |
| **Graph Endpoint** | `/sites/{site-id}/drives` | `/drives/{container-id}` directly |
| **Required Scope** | `Sites.FullControl.All` | `FileStorageContainer.Selected` |
| **App Registration** | Any app with permission | Must be registered in Container Type |
| **Billing** | Included in M365 license | Separate Azure billing (PAYG) |

**Why Separate Scopes**:
1. **Security Isolation**: Containers are more restricted than sites
2. **Billing Control**: Only registered apps can incur container storage costs
3. **Multi-Tenancy**: Containers designed for ISV scenarios with explicit app registration

**Microsoft's Design Intent**:
- `Sites.FullControl.All` = Broad permission for existing SharePoint workloads
- `FileStorageContainer.Selected` = Explicit opt-in for new container-based storage

**Result**: Having `Sites.FullControl.All` does NOT grant access to SharePoint Embedded Containers.

---

## The One-Line Fix

### Current Code (Lines 150-154)

```csharp
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

### Updated Code (Add One Line)

```csharp
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All",
        "https://graph.microsoft.com/FileStorageContainer.Selected"  // ADD THIS
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

### Prerequisites (User Confirmed Already Done)

✅ **App Registration Permission**:
- BFF API (`1e40baad...`) has `FileStorageContainer.Selected` delegated permission
- Admin consent granted

✅ **Container Type Registration**:
- BFF API registered in Container Type `8a6ce34c...`
- Allows app to access containers of this type

✅ **Graph Endpoint**:
- Already using `/beta/` endpoint (line 207)
- SharePoint Embedded requires beta

### Expected Outcome After Change

1. **OBO Exchange**: Azure AD includes `FileStorageContainer.Selected` in Token B
2. **Graph API Validation**: Scope check passes ✅
3. **App Registration Check**: BFF API is registered ✅
4. **User Permission Check**: User has access ✅ (assuming proper Dataverse permissions)
5. **File Upload**: Succeeds → HTTP 201 Created
6. **PCF Control**: Displays success message

### If It Still Fails After Change

**Possible Remaining Issues**:
1. Container Type registration not actually completed (user said it is, but verify)
2. User lacks permissions in Dataverse (authorization check fails)
3. Container doesn't exist (404 error)
4. Different error unrelated to scopes

**Diagnostic**: With detailed logging enabled, we'll see the actual error message instead of generic HTTP 500.

---

## Questions for User Before Making Change

1. **Should we test upload first to see actual error?**
   - Detailed logging is enabled
   - Will confirm if it's scope-related vs something else

2. **Are you confident Container Type registration is complete?**
   - How was it done? (PowerShell, Postman, script?)
   - Can we verify the registration status?

3. **Do we want to keep `Sites.FullControl.All` and `Files.ReadWrite.All`?**
   - Or only use `FileStorageContainer.Selected`?
   - Recommendation: Keep all three for flexibility

4. **Should we update the code comment too?**
   - Remove the incorrect comment about bypassing restrictions
   - Add correct explanation of why FileStorageContainer.Selected is required

---

**Document Created**: 2025-10-16 05:30 AM
**Status**: Awaiting user approval to make one-line scope addition to GraphClientFactory.cs
**Risk**: LOW - Only adds scope to existing OBO request, app already has permission granted
**Estimated Fix Time**: 2 minutes to change code, 5 minutes to test
