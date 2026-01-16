# SPE Permission Security Enhancement

> **Document Type**: Design Specification
> **Status**: Draft - Ready for Project Planning
> **Created**: 2026-01-15
> **Author**: Spaarke Engineering
> **Priority**: Medium (Required before Production)

---

## Executive Summary

This document describes a security gap in the current SPE (SharePoint Embedded) file access architecture and proposes a defense-in-depth solution combining Azure AD security groups with BFF-level Dataverse permission validation.

---

## Problem Statement

### Current Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CURRENT FILE ACCESS FLOW                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌─────────┐ │
│  │   User       │     │  Dataverse   │     │    BFF       │     │   SPE   │ │
│  │  (Browser)   │     │   (CRM)      │     │    API       │     │ Storage │ │
│  └──────┬───────┘     └──────┬───────┘     └──────┬───────┘     └────┬────┘ │
│         │                    │                    │                   │      │
│         │  1. View Document  │                    │                   │      │
│         │    Record          │                    │                   │      │
│         │ ─────────────────► │                    │                   │      │
│         │                    │                    │                   │      │
│         │  2. Dataverse      │                    │                   │      │
│         │    validates       │                    │                   │      │
│         │    row-level       │                    │                   │      │
│         │    security        │                    │                   │      │
│         │ ◄───────────────── │                    │                   │      │
│         │                    │                    │                   │      │
│         │  3. Request file   │                    │                   │      │
│         │    (download/      │                    │                   │      │
│         │     preview)       │                    │                   │      │
│         │ ──────────────────────────────────────► │                   │      │
│         │                    │                    │                   │      │
│         │                    │  4. BFF validates  │                   │      │
│         │                    │     document       │                   │      │
│         │                    │     EXISTS         │                   │      │
│         │                    │     (app-only)     │                   │      │
│         │                    │ ◄────────────────► │                   │      │
│         │                    │                    │                   │      │
│         │                    │                    │  5. Fetch file    │      │
│         │                    │                    │     (app-only)    │      │
│         │                    │                    │ ─────────────────►│      │
│         │                    │                    │                   │      │
│         │  6. Return file    │                    │ ◄─────────────────│      │
│         │ ◄────────────────────────────────────── │                   │      │
│         │                    │                    │                   │      │
│                                                                              │
│  ⚠️  GAP: Step 4 only checks document EXISTS, not user's permission to it   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### The Security Gap

| Step | What Happens | Security Check |
|------|--------------|----------------|
| 1-2 | User views Document record in Dataverse | ✅ Dataverse row-level security |
| 3 | User calls BFF API with documentId | ✅ User must be authenticated |
| 4 | BFF queries Dataverse for document | ⚠️ **Uses app-only auth** |
| 5-6 | BFF fetches file from SPE | ✅ App-only auth to SPE |

**The Problem**: In step 4, the BFF queries Dataverse using **app-only authentication** (client credentials), not the user's identity. This means:

- BFF verifies the document record **exists** in Dataverse
- BFF does **NOT** verify the **calling user** has permission to read that specific Document record
- If a user knows (or guesses) a document ID they shouldn't have access to, they can download the file

### Attack Scenario

```
1. Malicious user has Dataverse access but NOT to Document record X
2. User somehow obtains Document ID (GUID) for record X
   - URL sharing, logs, guessing, etc.
3. User crafts direct API call:
   GET /api/documents/{documentId}/download
   Authorization: Bearer {valid-bff-token}
4. BFF validates:
   - ✅ Token is valid (user is authenticated)
   - ✅ Document exists in Dataverse (app-only query)
   - ❌ Does NOT check if THIS USER can read THIS document
5. BFF returns file content → Security breach
```

### Risk Assessment

| Factor | Rating | Notes |
|--------|--------|-------|
| **Likelihood** | Low | Requires knowing specific document ID |
| **Impact** | Medium-High | Unauthorized file access |
| **Exploitability** | Low | Can't exploit via normal UI |
| **Current Mitigation** | Partial | Users only see records they have Dataverse access to |
| **Production Risk** | **High** | Unacceptable for production deployment |

---

## Solution Options

### Option A: Pure App-Only Authentication

**Approach**: Never grant users direct SPE permissions. All file access goes through BFF proxy.

| Pros | Cons |
|------|------|
| Strongest security | ❌ Breaks "Open in Desktop" (Word/Excel authenticate as user) |
| Simplest permission model | ❌ Breaks "Open in Web" direct editing |
| No SPE permission management | Higher BFF load (all files proxied) |

**Verdict**: ❌ Not viable - Breaks key user workflows.

### Option B: Dataverse Permission Validation in BFF (RECOMMENDED)

**Approach**: Before returning file/URL, verify the calling user can read the Document record in Dataverse using their delegated permissions.

| Pros | Cons |
|------|------|
| Full functionality preserved | Requires Dataverse OBO capability |
| Defense-in-depth security | Extra API call per request (~50-100ms) |
| Works with SPE security groups | More complex implementation |
| Proper authorization model | |

**Verdict**: ✅ Recommended - Proper security without breaking functionality.

### Option C: Accept Implicit Trust

**Approach**: Trust that if user is authenticated, they should have access.

| Pros | Cons |
|------|------|
| No code changes | ❌ Security gap remains |
| Fastest performance | ❌ Violates least-privilege principle |
| | ❌ Not acceptable for production |

**Verdict**: ❌ Not acceptable for production.

---

## Recommended Solution: Option B + SPE Security Groups

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        ENHANCED FILE ACCESS FLOW                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌─────────┐ │
│  │   User       │     │  Dataverse   │     │    BFF       │     │   SPE   │ │
│  │  (Browser)   │     │   (CRM)      │     │    API       │     │ Storage │ │
│  └──────┬───────┘     └──────┬───────┘     └──────┬───────┘     └────┬────┘ │
│         │                    │                    │                   │      │
│         │  1. Request file   │                    │                   │      │
│         │ ──────────────────────────────────────► │                   │      │
│         │                    │                    │                   │      │
│         │                    │  2. NEW: Query     │                   │      │
│         │                    │     Dataverse as   │                   │      │
│         │                    │     USER (OBO)     │                   │      │
│         │                    │ ◄─────────────────►│                   │      │
│         │                    │                    │                   │      │
│         │                    │  3. If 404/403:    │                   │      │
│         │                    │     DENY access    │                   │      │
│         │                    │                    │                   │      │
│         │                    │  4. If success:    │                   │      │
│         │                    │     fetch file     │                   │      │
│         │                    │                    │ ─────────────────►│      │
│         │                    │                    │                   │      │
│         │  5. Return file    │                    │ ◄─────────────────│      │
│         │ ◄────────────────────────────────────── │                   │      │
│         │                    │                    │                   │      │
│                                                                              │
│  ✅ Step 2 validates user's Dataverse permission BEFORE returning file      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Two-Part Implementation

#### Part 1: SPE Security Group (Quick Win - 15 min)

This enables full functionality (Preview, Open in Web, Open in Desktop) for authorized users.

```
Azure AD Security Group                    SPE Container
┌─────────────────────────┐               ┌────────────────────┐
│ "Spaarke SPE Documents" │ ───write───►  │  Documents         │
│                         │  permission   │  Container         │
│  - User A               │               │                    │
│  - User B               │               │  All documents     │
│  - User C               │               │  share same        │
│  ...                    │               │  permissions       │
└─────────────────────────┘               └────────────────────┘
```

**Setup Steps**:
1. Create Azure AD Security Group
2. Grant group `write` permission to SPE container
3. Add users to group as part of onboarding

#### Part 2: BFF Permission Validation (Security Enhancement - 2-4 hours)

This closes the security gap by validating Dataverse permissions before serving files.

```
User Request                    BFF Validation
     │                              │
     ▼                              ▼
┌─────────────┐            ┌──────────────────┐
│ Bearer      │            │ 1. Extract user  │
│ Token       │───────────►│    from token    │
│ (BFF scope) │            │                  │
└─────────────┘            │ 2. Exchange for  │
                           │    Dataverse     │
                           │    OBO token     │
                           │                  │
                           │ 3. Query:        │
                           │    GET /sprk_    │
                           │    documents(id) │
                           │    AS USER       │
                           │                  │
                           │ 4. If 404/403:   │
                           │    DENY          │
                           │                  │
                           │ 5. If success:   │
                           │    ALLOW         │
                           └──────────────────┘
```

---

## Component-Level Implementation Details

### Part 1: SPE Security Group Setup

#### 1.1 Create Azure AD Security Group

**Location**: Azure Portal → Azure Active Directory → Groups

| Setting | Value |
|---------|-------|
| Group type | Security |
| Group name | `Spaarke SPE Document Users` |
| Group description | Users with access to Spaarke SPE document containers |
| Azure AD roles | No |
| Membership type | Assigned |

**PowerShell Alternative**:
```powershell
# Azure AD PowerShell
New-AzureADGroup -DisplayName "Spaarke SPE Document Users" `
                 -Description "Users with access to Spaarke SPE document containers" `
                 -MailEnabled $false `
                 -SecurityEnabled $true `
                 -MailNickName "spaarke-spe-users"
```

#### 1.2 Grant Container Permission

**Graph API Call**:
```http
POST https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/permissions
Content-Type: application/json
Authorization: Bearer {app-only-token}

{
  "roles": ["write"],
  "grantedToV2": {
    "group": {
      "id": "{securityGroupId}"
    }
  }
}
```

**Container ID**: Retrieve from existing SPE configuration or:
```http
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers?$filter=containerTypeId eq '{containerTypeId}'
```

#### 1.3 Add Users to Group

**Azure Portal**: Groups → Spaarke SPE Document Users → Members → Add members

**PowerShell**:
```powershell
Add-AzureADGroupMember -ObjectId {groupId} -RefObjectId {userId}
```

**As Part of User Onboarding**: Include in provisioning scripts/workflow.

---

### Part 2: BFF Permission Validation

#### 2.1 New Components Required

| Component | File | Purpose |
|-----------|------|---------|
| `IDataverseUserClient` | `Services/IDataverseUserClient.cs` | Interface for user-context Dataverse queries |
| `DataverseUserClient` | `Services/DataverseUserClient.cs` | Implementation using OBO token |
| `DataverseOboTokenProvider` | `Services/DataverseOboTokenProvider.cs` | Acquires Dataverse tokens via OBO |
| Updated endpoints | `Api/FileAccessEndpoints.cs` | Add permission validation |

#### 2.2 Token Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           OBO TOKEN EXCHANGE FLOW                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐  │
│  │   Client    │    │    BFF      │    │  Azure AD   │    │  Dataverse  │  │
│  │  (Browser)  │    │    API      │    │   (Entra)   │    │   Web API   │  │
│  └──────┬──────┘    └──────┬──────┘    └──────┬──────┘    └──────┬──────┘  │
│         │                  │                  │                  │          │
│    1. Request with         │                  │                  │          │
│       BFF access token     │                  │                  │          │
│  ─────────────────────────►│                  │                  │          │
│                            │                  │                  │          │
│                            │  2. OBO token    │                  │          │
│                            │     request      │                  │          │
│                            │  ─────────────► │                  │          │
│                            │                  │                  │          │
│                            │  scope:          │                  │          │
│                            │  https://{org}.  │                  │          │
│                            │  crm.dynamics.   │                  │          │
│                            │  com/.default    │                  │          │
│                            │                  │                  │          │
│                            │  3. Dataverse    │                  │          │
│                            │     access token │                  │          │
│                            │ ◄───────────────│                  │          │
│                            │                  │                  │          │
│                            │  4. Query document as user         │          │
│                            │ ──────────────────────────────────►│          │
│                            │                  │                  │          │
│                            │  5. 200 OK (user has access)       │          │
│                            │     or 404/403 (no access)         │          │
│                            │ ◄──────────────────────────────────│          │
│                            │                  │                  │          │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 2.3 Interface Definition

```csharp
// File: src/server/api/Sprk.Bff.Api/Services/IDataverseUserClient.cs

namespace Sprk.Bff.Api.Services;

/// <summary>
/// Dataverse client that executes queries as the authenticated user (OBO).
/// Used for permission validation - verifies user can access specific records.
/// </summary>
public interface IDataverseUserClient
{
    /// <summary>
    /// Checks if the current user has read access to a specific Document record.
    /// Returns true if user can read the record, false otherwise.
    /// </summary>
    /// <param name="documentId">The Document record GUID</param>
    /// <param name="userAccessToken">The user's BFF access token (for OBO exchange)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if user has read access, false if 403/404</returns>
    Task<bool> CanUserReadDocumentAsync(
        string documentId,
        string userAccessToken,
        CancellationToken ct = default);
}
```

#### 2.4 Implementation

```csharp
// File: src/server/api/Sprk.Bff.Api/Services/DataverseUserClient.cs

namespace Sprk.Bff.Api.Services;

/// <summary>
/// Dataverse client using On-Behalf-Of (OBO) flow to execute queries as the user.
/// This enables permission validation without granting app-only elevated access.
/// </summary>
public class DataverseUserClient : IDataverseUserClient
{
    private readonly IConfidentialClientApplication _msalClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseUserClient> _logger;
    private readonly string _dataverseUrl;

    public DataverseUserClient(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<DataverseUserClient> logger)
    {
        _logger = logger;
        _dataverseUrl = configuration["Dataverse:Url"]
            ?? throw new ArgumentException("Dataverse:Url not configured");

        // Configure MSAL for OBO
        _msalClient = ConfidentialClientApplicationBuilder
            .Create(configuration["AzureAd:ClientId"])
            .WithClientSecret(configuration["AzureAd:ClientSecret"])
            .WithAuthority(AzureCloudInstance.AzurePublic, configuration["AzureAd:TenantId"])
            .Build();

        _httpClient = httpClientFactory.CreateClient("DataverseUser");
    }

    public async Task<bool> CanUserReadDocumentAsync(
        string documentId,
        string userAccessToken,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Exchange BFF token for Dataverse token via OBO
            var dataverseToken = await GetDataverseOboTokenAsync(userAccessToken, ct);

            // 2. Query Dataverse as user for the specific document
            var requestUrl = $"{_dataverseUrl}/api/data/v9.2/sprk_documents({documentId})?$select=sprk_documentid";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);

            var response = await _httpClient.SendAsync(request, ct);

            // 3. 200 = user has access, 403/404 = no access
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("User has access to document {DocumentId}", documentId);
                return true;
            }

            if (response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "User denied access to document {DocumentId}. Status: {StatusCode}",
                    documentId, response.StatusCode);
                return false;
            }

            // Unexpected status - log and deny
            _logger.LogError(
                "Unexpected Dataverse response for document {DocumentId}. Status: {StatusCode}",
                documentId, response.StatusCode);
            return false;
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogWarning(ex, "OBO token exchange failed - user consent required");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access to document {DocumentId}", documentId);
            return false;
        }
    }

    private async Task<string> GetDataverseOboTokenAsync(string userAccessToken, CancellationToken ct)
    {
        // OBO flow: exchange BFF token for Dataverse token
        var scopes = new[] { $"{_dataverseUrl}/.default" };

        var userAssertion = new UserAssertion(userAccessToken);
        var result = await _msalClient
            .AcquireTokenOnBehalfOf(scopes, userAssertion)
            .ExecuteAsync(ct);

        return result.AccessToken;
    }
}
```

#### 2.5 Updated Endpoint

```csharp
// File: src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs
// Update GetDownload method

static async Task<IResult> GetDownload(
    string documentId,
    IDataverseService dataverseService,
    IDataverseUserClient dataverseUserClient,  // NEW: inject user client
    SpeFileStore speFileStore,
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    logger.LogInformation("GetDownload called | DocumentId: {DocumentId} | TraceId: {TraceId}",
        documentId, context.TraceIdentifier);

    // 1. Validate document ID format
    if (!Guid.TryParse(documentId, out var docGuid))
    {
        throw new SdapProblemException(
            "invalid_id",
            "Invalid Document ID",
            $"Document ID '{documentId}' is not a valid GUID format",
            400);
    }

    // NEW: 2. Validate user's Dataverse permission
    var userToken = context.Request.Headers["Authorization"]
        .ToString()
        .Replace("Bearer ", "");

    var hasAccess = await dataverseUserClient.CanUserReadDocumentAsync(documentId, userToken, ct);

    if (!hasAccess)
    {
        logger.LogWarning(
            "User denied access to document {DocumentId} | TraceId: {TraceId}",
            documentId, context.TraceIdentifier);

        throw new SdapProblemException(
            "access_denied",
            "Access Denied",
            "You do not have permission to access this document",
            403);
    }

    // 3. Get document entity from Dataverse (app-only - for SPE pointers)
    var document = await dataverseService.GetDocumentAsync(documentId, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404);
    }

    // 4. Validate SPE pointers and download
    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);

    var fileStream = await speFileStore.DownloadFileAsync(
        document.GraphDriveId!,
        document.GraphItemId!,
        ct);

    // ... rest of method unchanged
}
```

#### 2.6 DI Registration

```csharp
// File: src/server/api/Sprk.Bff.Api/Program.cs

// Add to service registration
builder.Services.AddSingleton<IDataverseUserClient, DataverseUserClient>();
builder.Services.AddHttpClient("DataverseUser");
```

#### 2.7 Azure AD App Registration Update

The BFF app registration needs permission to acquire Dataverse tokens on behalf of users.

**Required API Permission**:
- **API**: Dynamics CRM (or your Dataverse environment)
- **Permission**: `user_impersonation` (Delegated)
- **Admin Consent**: Required

---

### Part 2 Alternative: Simplified Token Pass-Through

If full OBO implementation is too complex, a simpler approach:

```csharp
// Simpler: Pass user's existing Dataverse token from client
// Client already has Dataverse token (they're in Dataverse form)
// Add header: X-Dataverse-Token: {user's dataverse token}

static async Task<IResult> GetDownload(
    string documentId,
    [FromHeader(Name = "X-Dataverse-Token")] string? dataverseToken,
    // ... other params
)
{
    if (string.IsNullOrEmpty(dataverseToken))
    {
        throw new SdapProblemException("missing_token", "Missing Token",
            "Dataverse token required for authorization", 401);
    }

    // Validate token and check document access
    var hasAccess = await ValidateDataverseTokenAndAccess(dataverseToken, documentId, ct);
    // ...
}
```

**Trade-off**: Requires client-side changes to pass Dataverse token.

---

## Project Structure

### Files to Create

```
src/server/api/Sprk.Bff.Api/
├── Services/
│   ├── IDataverseUserClient.cs          # NEW: Interface
│   └── DataverseUserClient.cs           # NEW: OBO implementation
```

### Files to Modify

```
src/server/api/Sprk.Bff.Api/
├── Api/
│   └── FileAccessEndpoints.cs           # MODIFY: Add permission validation
├── Program.cs                           # MODIFY: Register new service
└── appsettings.json                     # MODIFY: Add Dataverse URL config
```

### Configuration Changes

```json
// appsettings.json additions
{
  "Dataverse": {
    "Url": "https://spaarkedev1.crm.dynamics.com"
  }
}
```

---

## Testing Plan

### Unit Tests

| Test Case | Expected Result |
|-----------|-----------------|
| User with Dataverse access requests file | 200 OK + file |
| User without Dataverse access requests file | 403 Forbidden |
| Invalid document ID | 400 Bad Request |
| Valid auth but document doesn't exist | 404 Not Found |
| OBO token exchange fails | 403 Forbidden |

### Integration Tests

| Scenario | Steps | Expected |
|----------|-------|----------|
| Happy path | User A has Document access, requests download | File returned |
| Unauthorized | User B lacks Document access, requests download | 403 error |
| Cross-user | User A creates Document, User B (no access) tries | 403 error |

---

## Endpoints to Update

All file access endpoints require permission validation:

| Endpoint | Current Auth | Needs Update |
|----------|--------------|--------------|
| `GET /api/documents/{id}/download` | App-only | ✅ Yes |
| `GET /api/documents/{id}/preview-url` | OBO (Graph) | ✅ Yes |
| `GET /api/documents/{id}/view-url` | OBO (Graph) | ✅ Yes |
| `GET /api/documents/{id}/open-links` | OBO (Graph) | ✅ Yes |
| `POST /api/documents/{id}/checkout` | OBO (Graph) | ✅ Yes |
| `POST /api/documents/{id}/checkin` | OBO (Graph) | ✅ Yes |
| `POST /api/documents/{id}/discard-checkout` | OBO (Graph) | ✅ Yes |

---

## Effort Estimate

| Task | Estimate | Notes |
|------|----------|-------|
| Part 1: SPE Security Group | 15-30 min | One-time Azure setup |
| Part 2: IDataverseUserClient interface | 15 min | |
| Part 2: DataverseUserClient implementation | 1-2 hours | OBO token handling |
| Part 2: Update FileAccessEndpoints | 1 hour | All 7 endpoints |
| Part 2: DI registration + config | 15 min | |
| Part 2: Azure AD permission update | 15 min | Admin consent |
| Part 2: Unit tests | 1 hour | |
| Part 2: Integration testing | 1 hour | |
| **Total Part 1** | **30 min** | |
| **Total Part 2** | **4-6 hours** | |

---

## Rollout Plan

### Phase 1: Immediate (Part 1)
1. Create Azure AD security group
2. Grant group permission to SPE container
3. Add existing users to group
4. Verify Preview/Open in Web/Open in Desktop work

### Phase 2: Before Production (Part 2)
1. Implement DataverseUserClient
2. Update all file access endpoints
3. Test thoroughly
4. Deploy with monitoring

---

## Related Documents

- [ADR-023: Choice Dialog Pattern](../adr/ADR-023-choice-dialog-pattern.md) - UI patterns used in locked document dialogs
- [Auth Standards](../standards/AUTH-STANDARDS.md) - Authentication patterns
- [SPE Integration Guide](../guides/SPE-INTEGRATION.md) - SharePoint Embedded integration

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2026-01-15 | 1.0 | Initial draft | Spaarke Engineering |
