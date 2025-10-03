# SharePoint Embedded User Authentication Flow

**Date:** 2025-10-02
**Purpose:** Clarify how users authenticate to SharePoint Embedded and why OBO is needed

---

## Your Question

> "Since we are using SPE and using a registered app to authenticate between our app and SPE, where does the user specifically 'authenticate to SharePoint'? From SPE perspective and container access, isn't the 'user' just our app? Or is there some other way a user accesses SPE such that they need user-specific credentials/login?"

**Short Answer:** Users authenticate to **Azure AD** (not directly to SharePoint), and SPE uses Azure AD user identity + container permissions to enforce access. The "user" is NOT your app - SPE sees the actual end-user through the OBO token.

---

## Two Authentication Patterns in SDAP

### Pattern 1: App-Only (Managed Identity) ❌ User is "the app"

```
┌──────────────┐                    ┌──────────────┐
│  SDAP API    │  Client Credentials│ Azure AD     │
│  (App/MI)    │ ──────────────────>│              │
│              │ <──────────────────│              │
└──────────────┘   App Access Token └──────────────┘
       │
       │ Graph API call with App Token
       ↓
┌──────────────────────────────────────────────────┐
│ SharePoint Embedded (SPE)                        │
│                                                   │
│ Sees: "SDAP Application" (not end-user)         │
│ Permissions: Container-level admin permissions   │
└──────────────────────────────────────────────────┘
```

**SPE perspective:** "The SDAP app is calling me with admin permissions"
**User identity:** Lost - SPE only sees the app
**Use case:** Background jobs, admin operations, container creation

---

### Pattern 2: On-Behalf-Of (OBO) ✅ User is "the real person"

```
┌──────────────┐                    ┌──────────────┐
│ React SPA    │  Interactive       │ Azure AD     │
│ (Browser)    │  Login (OAuth)     │              │
│              │ ──────────────────>│              │
│              │ <──────────────────│              │
└──────────────┘   User Access Token└──────────────┘
       │             (Alice's token)
       │
       │ API call with Alice's token
       ↓
┌──────────────┐                    ┌──────────────┐
│  SDAP API    │  OBO Exchange      │ Azure AD     │
│              │  (Alice's token)   │              │
│              │ ──────────────────>│              │
│              │ <──────────────────│              │
└──────────────┘   Graph Token      └──────────────┘
       │             (Alice → Graph)
       │
       │ Graph API call with Alice's Graph token
       ↓
┌──────────────────────────────────────────────────┐
│ SharePoint Embedded (SPE)                        │
│                                                   │
│ Sees: "Alice Smith (alice@contoso.com)"         │
│ Permissions: Alice's container permissions       │
└──────────────────────────────────────────────────┘
```

**SPE perspective:** "Alice is calling me through the SDAP app"
**User identity:** Preserved - SPE enforces Alice's permissions
**Use case:** User uploads, downloads, edits their files

---

## Step-by-Step: How Users Access SPE

### Step 1: User Signs Into React SPA

```javascript
// React app (browser) - User clicks "Sign In"
import { PublicClientApplication } from "@azure/msal-browser";

const msalConfig = {
  auth: {
    clientId: "your-spa-client-id",
    authority: "https://login.microsoftonline.com/your-tenant",
    redirectUri: "https://yourdomain.com"
  }
};

const msalInstance = new PublicClientApplication(msalConfig);

// User clicks login button
const loginResponse = await msalInstance.loginPopup({
  scopes: ["api://sdap-api/user_impersonation"]
});

// Browser now has Alice's access token
const aliceToken = loginResponse.accessToken;
```

**What happens:**
1. Browser redirects to Azure AD login page
2. Alice enters her credentials: `alice@contoso.com` / `password123`
3. Azure AD validates credentials
4. Browser receives Alice's access token (JWT with her identity)

**Alice's Token Contains:**
```json
{
  "aud": "api://sdap-api",
  "iss": "https://sts.windows.net/tenant-id/",
  "oid": "alice-user-id",
  "upn": "alice@contoso.com",
  "name": "Alice Smith",
  "roles": ["User"],
  "scp": "user_impersonation"
}
```

---

### Step 2: React SPA Calls SDAP API with Alice's Token

```javascript
// React app makes API call
const uploadFile = async (file) => {
  const response = await fetch("https://sdap-api.com/api/obo/containers/abc/files/doc.pdf", {
    method: "PUT",
    headers: {
      "Authorization": `Bearer ${aliceToken}`,  // Alice's token from Step 1
      "Content-Type": "application/pdf"
    },
    body: file
  });
};
```

**SDAP API receives:**
- HTTP request with `Authorization: Bearer <alice-token>`
- Token has Alice's identity (`oid`, `upn`, `name`)

---

### Step 3: SDAP API Validates Alice's Token

```csharp
// src/api/Spe.Bff.Api/Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Middleware validates:
// 1. Token signature (signed by Azure AD)
// 2. Token audience (aud = "api://sdap-api")
// 3. Token expiration (exp > now)
// 4. Token issuer (iss = Azure AD)
```

**Result:** `HttpContext.User.Identity.Name = "alice@contoso.com"`

---

### Step 4: SDAP API Exchanges Alice's Token for Graph Token (OBO)

```csharp
// src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
{
    // Exchange Alice's SDAP token for Alice's Graph token
    var graphToken = await _confidentialClientApp
        .AcquireTokenOnBehalfOf(
            scopes: new[] { "https://graph.microsoft.com/.default" },
            userAssertion: new UserAssertion(userAccessToken))  // Alice's token
        .ExecuteAsync();

    // graphToken.AccessToken is now Alice's Graph token
    var authProvider = new DelegateAuthenticationProvider((request) =>
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", graphToken.AccessToken);
        return Task.CompletedTask;
    });

    return new GraphServiceClient(authProvider);
}
```

**What OBO does:**
1. SDAP sends Alice's token + SDAP's client secret to Azure AD
2. Azure AD validates both
3. Azure AD issues NEW token: Alice's Graph token
4. Graph token has Alice's identity but for Graph API audience

**Alice's Graph Token Contains:**
```json
{
  "aud": "https://graph.microsoft.com",
  "iss": "https://sts.windows.net/tenant-id/",
  "oid": "alice-user-id",
  "upn": "alice@contoso.com",
  "name": "Alice Smith",
  "scp": "Files.ReadWrite.All FileStorageContainer.Selected"
}
```

---

### Step 5: SDAP Calls Graph API with Alice's Graph Token

```csharp
// src/api/Spe.Bff.Api/Services/OboSpeService.cs

public async Task<DriveItem?> UploadSmallAsync(
    string userBearer,  // Alice's SDAP token
    string containerId,
    string path,
    Stream content,
    CancellationToken ct)
{
    // Exchange Alice's SDAP token for Alice's Graph token
    var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

    // Call Graph API as Alice
    var uploadedItem = await graph.Drives[driveId].Root
        .ItemWithPath(path)
        .Content
        .PutAsync(content, cancellationToken: ct);

    return uploadedItem;
}
```

**Graph API Request:**
```http
PUT https://graph.microsoft.com/v1.0/drives/{driveId}/root:/doc.pdf:/content
Authorization: Bearer <alice-graph-token>
Content-Type: application/pdf

<file contents>
```

---

### Step 6: SharePoint Embedded Enforces Alice's Permissions

```
┌──────────────────────────────────────────────────────────┐
│ SharePoint Embedded (SPE) - Inside Microsoft's Cloud    │
│                                                           │
│ 1. Receives Graph API call with Alice's Graph token     │
│ 2. Validates token (signed by Azure AD)                 │
│ 3. Extracts Alice's identity: oid = "alice-user-id"     │
│ 4. Queries container permissions:                       │
│    - Container ID: abc                                   │
│    - Does Alice have "Write" permission? (checks SPE DB) │
│                                                           │
│ 5. Permission check result:                              │
│    ✅ Alice is a "Member" → Allow upload                │
│    ❌ Alice is not listed → Deny (403 Forbidden)        │
└──────────────────────────────────────────────────────────┘
```

**SPE Permission Model:**
- Each container has an **access control list (ACL)**
- ACL stored in SPE's backend (not in your app)
- ACL entries: `{ userId: "alice-user-id", role: "Member" }`
- Roles: Owner, Member, Viewer, etc.

**How permissions are managed:**
```csharp
// Your app can add/remove users from container ACL:
await graph.Storage.FileStorage.Containers[containerId]
    .Permissions
    .PostAsync(new Permission
    {
        GrantedToIdentities = new List<Identity>
        {
            new Identity { User = new User { Id = "alice-user-id" } }
        },
        Roles = new List<string> { "write" }
    });
```

---

## Key Insight: The User Never "Logs Into SharePoint"

### What Actually Happens:

1. **User logs into Azure AD** (via your React SPA)
2. **Azure AD issues tokens** with user identity
3. **Tokens flow through:** SPA → SDAP API → Graph API → SPE
4. **SPE trusts Azure AD tokens** and enforces permissions based on user identity in token

### Users DON'T:
- ❌ Log into SharePoint directly
- ❌ Enter SharePoint credentials
- ❌ See SharePoint login page
- ❌ Need SharePoint licenses (SPE is separate)

### Users DO:
- ✅ Log into Azure AD (once, in your React SPA)
- ✅ Get Azure AD tokens with their identity
- ✅ Have permissions managed in SPE containers (by your app)

---

## The Difference: App vs User Access

### Scenario: Alice tries to upload a file to Container ABC

#### With App-Only (MI) Authentication:
```csharp
// SDAP API uses Managed Identity
var graph = _factory.CreateAppOnlyClient();  // App token, not Alice's

var uploaded = await graph.Drives[driveId].Root
    .ItemWithPath("alice-doc.pdf")
    .Content
    .PutAsync(content);

// ✅ Upload succeeds (app has admin permissions)
// ❌ Alice's permissions NOT checked
// ❌ Audit log shows "SDAP App" uploaded, not Alice
```

**Result:** Alice can upload to containers she shouldn't access (security breach)

---

#### With OBO Authentication:
```csharp
// SDAP API uses Alice's token via OBO
var graph = await _factory.CreateOnBehalfOfClientAsync(aliceToken);  // Alice's token

var uploaded = await graph.Drives[driveId].Root
    .ItemWithPath("alice-doc.pdf")
    .Content
    .PutAsync(content);

// If Alice has "Member" role → ✅ Upload succeeds
// If Alice has "Viewer" role → ❌ 403 Forbidden
// Audit log shows "Alice Smith" uploaded
```

**Result:** Alice's permissions properly enforced

---

## Real-World Example: Three Users, One Container

### Container Setup (via your admin app):
```csharp
Container "ProjectX" (ID: abc-123)
├─ Alice (Role: Owner)    → Full access
├─ Bob (Role: Member)     → Read/Write access
└─ Charlie (Role: Viewer) → Read-only access
```

### Alice Uploads a File (OBO):
```javascript
// React SPA: Alice is logged in
fetch("/api/obo/containers/abc-123/files/doc.pdf", {
  method: "PUT",
  headers: { "Authorization": `Bearer ${aliceToken}` }
});

// ✅ Success: Alice is Owner
```

### Bob Uploads a File (OBO):
```javascript
// React SPA: Bob is logged in
fetch("/api/obo/containers/abc-123/files/doc.pdf", {
  method: "PUT",
  headers: { "Authorization": `Bearer ${bobToken}` }
});

// ✅ Success: Bob is Member
```

### Charlie Uploads a File (OBO):
```javascript
// React SPA: Charlie is logged in
fetch("/api/obo/containers/abc-123/files/doc.pdf", {
  method: "PUT",
  headers: { "Authorization": `Bearer ${charlieToken}` }
});

// ❌ Fails with 403: Charlie is Viewer (read-only)
```

### Without OBO (App-Only):
```javascript
// All three users call the same endpoint
// SDAP uses app token (no user identity)

// ✅ Alice succeeds (but shouldn't use app permissions)
// ✅ Bob succeeds (correct)
// ✅ Charlie succeeds (SECURITY BREACH - should fail!)
```

---

## When to Use Each Authentication Mode

### Use App-Only (Managed Identity):
- ❌ **Never** for user-initiated operations
- ✅ Background jobs (document event processing)
- ✅ Admin operations (create containers)
- ✅ System maintenance
- ✅ Operations where user identity doesn't matter

### Use OBO (User Token):
- ✅ **Always** for user-initiated operations
- ✅ File uploads/downloads
- ✅ File edits/deletes
- ✅ Browsing files
- ✅ Any operation where SharePoint permissions apply

---

## Answer to Your Question

> "Where does the user specifically 'authenticate to SharePoint'?"

**Answer:** Users authenticate to **Azure AD** (not SharePoint directly). SPE trusts Azure AD tokens and enforces permissions based on the user identity in the token.

> "From SPE perspective and container access, isn't the 'user' just our app?"

**Answer:** Only if you use App-Only (MI) authentication. With OBO:
- SPE sees the **actual end-user** (Alice, Bob, Charlie)
- SPE enforces **user-specific permissions** from container ACL
- SPE audit logs show **real user names**

**The user is NOT "just your app"** when using OBO - that's the whole point of OBO.

---

## Do We Need OBO for SDAP?

### Yes - Absolutely Required

**SDAP Requirements:**
1. Users must upload/download files with their own permissions
2. Users must not access files they don't have permission to
3. Audit logs must show which user performed actions
4. Compliance requirements (who accessed what, when)

**Without OBO:**
- All users get admin permissions (security breach)
- No audit trail of individual users
- Cannot meet compliance requirements
- SharePoint permissions meaningless

**With OBO:**
- Users get their own permissions (secure)
- Full audit trail (Alice uploaded, Bob downloaded)
- Compliance requirements met
- SharePoint permissions enforced

---

## Summary Diagram: Complete Flow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Alice signs into React SPA                               │
│    → Azure AD login page                                     │
│    → Alice enters: alice@contoso.com / password             │
│    → Browser gets: Alice's SDAP token                        │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. React SPA calls SDAP API                                 │
│    → PUT /api/obo/containers/abc/files/doc.pdf              │
│    → Header: Authorization: Bearer <alice-sdap-token>       │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. SDAP API validates Alice's token                         │
│    → Microsoft.Identity.Web validates signature             │
│    → HttpContext.User = Alice                               │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. SDAP API exchanges token (OBO)                           │
│    → Sends: Alice's SDAP token + App secret → Azure AD     │
│    → Receives: Alice's Graph token                          │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. SDAP API calls Graph API                                 │
│    → PUT graph.microsoft.com/drives/.../content             │
│    → Header: Authorization: Bearer <alice-graph-token>      │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. SharePoint Embedded enforces permissions                 │
│    → Extracts: oid = alice-user-id from token               │
│    → Checks: Container ABC ACL for alice-user-id            │
│    → Result: Alice is "Member" → ✅ Allow                   │
│    → Audit: "Alice Smith uploaded doc.pdf at 2:30pm"        │
└─────────────────────────────────────────────────────────────┘
```

**Key Point:** Alice never "logs into SharePoint" - she logs into Azure AD once, and her identity flows through tokens all the way to SPE.

---

**Does this clarify the authentication flow and why OBO is required?**
