# Architecture Changes: AI Summary and Analysis Unification

> **Project:** AI Summary and Analysis Enhancements
> **Date:** January 2026
> **Status:** In Progress (Phase 2.3)
> **Impact:** Breaking changes to AI analysis architecture

---

## Executive Summary

This project unified two separate AI analysis paths (Document Intelligence and Analysis) into a single **playbook-based orchestration service** with improved authorization, retry resilience, and soft failure handling. The most significant change is the **removal of background AI analysis jobs** due to the new security model requiring user context (OBO authentication).

**Key Impact:** Projects depending on background AI analysis (e.g., `email-to-document-automation`) must adapt to the new user-triggered model or implement alternative approaches.

---

## What Changed

### Removed Components

| Component | Location | Reason |
|-----------|----------|--------|
| `IDocumentIntelligenceService` | `Services/Ai/` | Replaced by unified orchestration service |
| `DocumentIntelligenceService` | `Services/Ai/` | Legacy single-document analysis, duplicated logic |
| `DocumentIntelligenceEndpoints` | `Api/Ai/` | Old endpoint `/api/ai/document-intelligence/analyze` removed |
| `DocumentAnalysisJobHandler` | `Services/Jobs/Handlers/` | **Cannot work with new auth model** (requires user context) |
| `AiAuthorizationFilter` | `Api/Filters/` | Replaced by unified `IAiAuthorizationService` |
| Background job enqueue logic | `EmailToDocumentJobHandler` | Removed call to `EnqueueAiAnalysisJobAsync()` |

### New/Updated Components

| Component | Purpose | Key Features |
|-----------|---------|--------------|
| `AnalysisOrchestrationService` | Unified AI orchestration | Playbook execution, multi-document support, streaming SSE |
| `IAiAuthorizationService` | FullUAC authorization | Checks Read access via `RetrievePrincipalAccess`, handles partial auth |
| `StorageRetryPolicy` | Resilience for storage ops | 3 retries with exponential backoff (2s, 4s, 8s) |
| `DocumentProfileFieldMapper` | Document Profile mapping | Maps AI outputs to `sprk_document` entity fields |
| `IPlaybookService.GetByNameAsync()` | Playbook resolution | Lookup playbooks by name (e.g., "Document Profile") |
| `AnalysisStreamChunk` | Enhanced SSE response | Added `partialStorage`, `storageMessage`, `tokenUsage` fields |

---

## Architectural Principles

### 1. Playbook-Based Analysis

**Before:**
- Two separate code paths: DocumentIntelligenceService (simple) vs. AnalysisOrchestrationService (complex)
- Hardcoded prompts and field mappings
- Limited extensibility

**After:**
- Single orchestration service with playbook configuration
- Playbooks define: tools, prompts, scopes, output types
- Extensible via Dataverse configuration (no code changes)

**Example: Document Profile Playbook**
```json
{
  "name": "Document Profile",
  "trigger": "OnUpload",
  "tools": ["DocumentIntelligence", "FieldExtractor"],
  "outputTypes": ["Summary", "Keywords", "Entities", "DocumentType"],
  "storageStrategy": "DualStorage"
}
```

### 2. User Context Requirement (OBO Authentication)

**Critical Change:** All AI analysis now requires user context for OBO (On-Behalf-Of) authentication.

**Why:**
- **Security:** FullUAC mode validates user has Read access to documents before analysis
- **File Access:** Downloading files from SharePoint Embedded requires user's Graph token
- **Audit:** AI operations are tied to specific users for compliance

**Technical Requirement:**
- All analysis endpoints require `HttpContext` parameter
- Background jobs (no user session) **cannot** call the new service
- Alternative: Service account approach (future consideration)

**Code Example:**
```csharp
// NEW: Requires HttpContext for OBO auth
public interface IAnalysisOrchestrationService
{
    IAsyncEnumerable<AnalysisStreamChunk> ExecutePlaybookAsync(
        PlaybookExecuteRequest request,
        HttpContext httpContext,  // ← User context required
        CancellationToken cancellationToken);
}

// OLD: No user context (could run in background)
public interface IDocumentIntelligenceService
{
    Task<AnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken);
}
```

### 3. OBO Authentication Implementation for Dataverse

**Critical Security Fix:** Implemented proper On-Behalf-Of (OBO) token exchange for Dataverse authorization checks.

**Problem Context:**
Initial implementation of FullUAC authorization was failing with 403 Forbidden errors despite users having System Administrator privileges. Investigation revealed two critical bugs in the OBO authentication flow.

#### Bug #1: Missing HttpClient Authorization Header

**Symptom:**
```
[UAC-DIAG] Failed to lookup Dataverse user: Unauthorized
```

**Root Cause:**
`DataverseAccessDataSource` was obtaining OBO token from MSAL but never setting it on HttpClient headers. The flow was:
1. Get OBO token via `GetDataverseTokenViaOBOAsync()`
2. Call `LookupDataverseUserIdAsync()` which uses `_httpClient.GetAsync()`
3. HttpClient still had old service principal token (or no token)
4. Result: 401 Unauthorized from Dataverse API

**Fix (DataverseAccessDataSource.cs:161-165):**
```csharp
if (!string.IsNullOrEmpty(userAccessToken))
{
    _logger.LogDebug("[UAC-DIAG] Using OBO authentication for user context");
    dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

    // CRITICAL: Set the OBO token on HttpClient headers for all subsequent API calls
    _httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", dataverseToken);

    _logger.LogDebug("[UAC-DIAG] Set OBO token on HttpClient authorization header");
}
```

#### Bug #2: RetrievePrincipalAccess Unavailable with OBO Tokens

**Symptom (after fix #1):**
```
[UAC-DIAG] RetrievePrincipalAccess FAILED: StatusCode=NotFound
Resource not found for the segment 'RetrievePrincipalAccess'
Error code: 0x80060888
```

**Root Cause:**
The `RetrievePrincipalAccess` Dataverse action returned 404 when called with delegated (OBO) tokens. This action appears to only be available with application-level permissions, not user-delegated permissions.

**Fix: Direct Query Authorization Pattern (DataverseAccessDataSource.cs:285-368):**

Replaced `RetrievePrincipalAccess` with direct document query approach:

```csharp
/// <summary>
/// Checks user's access to a specific resource using Dataverse's built-in security.
/// Uses a direct query approach: If the user can retrieve the record, they have Read access.
/// This works with OBO (delegated) tokens where RetrievePrincipalAccess may not be available.
/// </summary>
private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(
    string userId,
    string resourceId,
    string dataverseToken,
    CancellationToken ct)
{
    // APPROACH: Query the document directly using the OBO token.
    // If the query succeeds, the user has at least Read access (Dataverse enforces this).
    // If it fails with 403/404, they don't have access.

    var url = $"sprk_documents({resourceId})?$select=sprk_documentid";

    using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
    {
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
    };

    var response = await _httpClient.SendAsync(requestMessage, ct);

    if (!response.IsSuccessStatusCode)
    {
        // 403 or 404 means no access
        _logger.LogWarning(
            "[UAC-DIAG] Direct query DENIED for user {UserId} on document {DocumentId}: {Status}",
            userId, resourceId, response.StatusCode);
        return new List<PermissionRecord>();
    }

    // Success! User has Read access
    _logger.LogDebug(
        "[UAC-DIAG] Direct query GRANTED for user {UserId} on document {DocumentId}",
        userId, resourceId);

    return new List<PermissionRecord>
    {
        new PermissionRecord(userId, resourceId, AccessRights.Read)
    };
}
```

**Benefits of Direct Query Pattern:**
- ✅ Works with OBO (delegated) tokens
- ✅ Leverages Dataverse's native row-level security
- ✅ Simpler implementation (no complex RetrievePrincipalAccess payload)
- ✅ More efficient (single GET vs POST with complex payload)
- ✅ Easier to debug (standard HTTP status codes)

**Trade-offs:**
- ❌ Can only detect Read access (not Write, Delete, etc.)
- ✅ This is acceptable for AI analysis (only needs Read)

#### Complete OBO Authorization Flow

```
1. User clicks "AI Summary" in PCF control
   ↓
2. PCF sends POST /api/ai/analysis/execute
   Headers: Authorization: Bearer {user_graph_token}
   ↓
3. AnalysisAuthorizationFilter extracts bearer token
   TokenHelper.ExtractBearerToken(httpContext)
   ↓
4. AiAuthorizationService orchestrates authorization
   authService.AuthorizeAsync(user, documentIds, httpContext, ct)
   ↓
5. DataverseAccessDataSource performs OBO exchange
   a. Extract user token from HttpContext
   b. MSAL OBO: User Graph token → Dataverse token
      ConfidentialClientApplicationBuilder
        .AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken))
   c. Set OBO token on HttpClient.DefaultRequestHeaders.Authorization
   ↓
6. Lookup Dataverse user by Azure AD OID
   GET /api/data/v9.2/systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'
   (Uses OBO token set in step 5c)
   ↓
7. Check document access via direct query
   GET /api/data/v9.2/sprk_documents({id})?$select=sprk_documentid
   (Uses OBO token set in step 5c)
   ↓
8. If GET succeeds (200 OK) → User has Read access
   If GET fails (403/404) → User doesn't have access
   ↓
9. Return AuthorizationResult
   Success: true/false
   AuthorizedDocumentIds: Guid[]
   ↓
10. If authorized → Proceed with AI analysis
    If denied → Return 403 Forbidden to PCF
```

#### Configuration Requirements

**Azure AD App Registration (BFF API):**
- App ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- Tenant: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

**Required API Permissions:**
```
Microsoft Graph:
  - Files.Read.All (delegated) - For SPE file download via OBO
  - User.Read (delegated) - For user profile

Dynamics CRM (Dataverse):
  - user_impersonation (delegated) - For OBO token exchange
```

**Client Secret:**
- Created: 2025-12-18
- Expires: 2027-12-18
- First 6 chars: `l8b8Q~J`
- Configuration key: `API_CLIENT_SECRET`
- Used by: GraphClientFactory, DataverseAccessDataSource, PlaybookService

**Storage Locations:**
```
Local Development:
  - User Secrets: dotnet user-secrets set "API_CLIENT_SECRET" "{secret}"
  - appsettings.Development.json.local (gitignored)

Azure App Service (Dev):
  - App Settings > Configuration > Application settings
  - Key: API_CLIENT_SECRET
  - Value: [hidden]

Production (Future):
  - Azure Key Vault
  - App Settings reference: @Microsoft.KeyVault(SecretUri=...)
```

#### Code Changes Required

**1. IAiAuthorizationService Interface (IAiAuthorizationService.cs:50-52)**
```csharp
Task<AuthorizationResult> AuthorizeAsync(
    ClaimsPrincipal user,
    IReadOnlyList<Guid> documentIds,
    HttpContext httpContext,  // NEW: Required for OBO token extraction
    CancellationToken cancellationToken = default);
```

**2. AiAuthorizationService Implementation (AiAuthorizationService.cs:74-84)**
```csharp
// Extract user's bearer token for OBO authentication
string? userAccessToken = null;
try
{
    userAccessToken = TokenHelper.ExtractBearerToken(httpContext);
    _logger.LogDebug("[AI-AUTH] Extracted user bearer token for OBO authentication");
}
catch (UnauthorizedAccessException ex)
{
    _logger.LogError(ex, "[AI-AUTH] Failed to extract bearer token from request");
    return AuthorizationResult.Denied("Missing or invalid authorization token");
}
```

**3. AnalysisAuthorizationFilter Update (AnalysisAuthorizationFilter.cs:139-143)**
```csharp
var result = await _authorizationService.AuthorizeAsync(
    user,
    documentIds,
    context.HttpContext,  // NEW: Pass HttpContext for token extraction
    context.HttpContext.RequestAborted);
```

#### Verification

**Application Insights Queries:**

Successful OBO flow:
```kusto
traces
| where message contains "UAC-DIAG"
| where timestamp > ago(1h)
| project timestamp, message
| order by timestamp desc
```

Expected log sequence:
```
[UAC-DIAG] Using OBO authentication for user context
[UAC-DIAG] Set OBO token on HttpClient authorization header
[UAC-DIAG] Dataverse user lookup successful: {systemuserid}
[UAC-DIAG] Direct query GRANTED for user {userId} on document {documentId}
```

Failed authorization:
```kusto
traces
| where message contains "AI-AUTH"
| where message contains "DENIED"
| project timestamp, message, severityLevel
```

#### Related Documentation

- **Full OBO Pattern**: `.claude/patterns/auth/obo-flow.md` (updated with Dataverse pattern)
- **Azure Resources**: `docs/architecture/auth-azure-resources.md` (includes secret lifecycle)
- **Auth Patterns**: `docs/architecture/sdap-auth-patterns.md` (Dataverse OBO section)
- **API Patterns**: `docs/architecture/sdap-bff-api-patterns.md` (Authorization service patterns)

### 4. Dual Storage with Soft Failure Handling

**Pattern:** Store analysis results in **two locations** with graceful degradation.

**Storage Locations:**
1. **Primary:** `sprk_analysisoutput` entity (generic outputs, always succeeds)
2. **Secondary:** `sprk_document` entity fields (Document Profile mapping, may fail)

**Soft Failure Behavior:**
- If `sprk_document` update fails (permissions, replication lag, network), continue
- Return `partialStorage: true` in SSE response with error message
- PCF displays warning banner: "Analysis complete, but some fields could not be saved"
- User can retry save from PCF UI

**Benefits:**
- AI outputs are never lost (always in `sprk_analysisoutput`)
- UX improvement: Don't fail entire operation on storage issues
- Retry policy handles transient errors (replication lag)

### 4. FullUAC Authorization

**What is FullUAC Mode?**
- Explicit authorization check via `RetrievePrincipalAccess` API
- Validates user has **Read** access to **each document** before analysis
- Handles partial authorization (some docs authorized, others not)

**Implementation:**
```csharp
public interface IAiAuthorizationService
{
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        Guid[] documentIds,
        CancellationToken cancellationToken);
}

public record AuthorizationResult(
    bool Success,
    string? Reason,
    Guid[] AuthorizedDocumentIds);  // Partial auth supported
```

**Why Not Use Standard Auth?**
- Standard auth checks if user can access the **API endpoint**
- FullUAC checks if user can access the **specific documents**
- Documents may have row-level security in Dataverse

---

## Breaking Changes

### 1. Background AI Analysis Removed

**What Changed:**
- `DocumentAnalysisJobHandler` deleted
- Job type `"ai-analyze"` no longer processed
- `EmailToDocumentJobHandler` no longer enqueues AI jobs

**Impact:**
- Email-to-document conversion completes **without** automatic AI analysis
- Users must manually trigger analysis from PCF after email is converted

**Migration Path:**

**Option A: User-Triggered Analysis (Recommended)**
```
1. Email converts to .eml document (existing flow)
2. User opens document in PCF
3. User clicks "Analyze Document" button
4. PCF calls /api/ai/analysis/execute with Document Profile playbook
5. Analysis runs with user's context
```

**Option B: Service Account Background Analysis (Future)**
```
If background analysis is critical:
1. Create service account with Read access to all documents
2. Implement background service with service account auth
3. Call AnalysisOrchestrationService with service account token
4. Handle authorization failures (service account may not have access to all docs)
```

**Configuration Change Required:**
```json
// appsettings.json - Remove or set to false
{
  "EmailProcessing": {
    "AutoEnqueueAi": false  // ← No longer functional
  }
}
```

### 2. API Endpoint Changes

**Removed Endpoint:**
```
DELETE /api/ai/document-intelligence/analyze
```

**New Endpoint:**
```
POST /api/ai/analysis/execute
```

**Request Format Change:**

**Old (DocumentIntelligenceService):**
```json
{
  "documentId": "guid",
  "prompt": "optional custom prompt"
}
```

**New (AnalysisOrchestrationService):**
```json
{
  "documentIds": ["guid1", "guid2"],  // Array (multi-doc support)
  "playbookId": "guid",               // Explicit playbook
  "actionId": "guid",                 // Optional action override
  "additionalContext": "string"       // Optional
}
```

**Response Format Change:**

**Old:**
```json
// AnalysisChunk (simple)
{
  "type": "token",
  "content": "...",
  "summary": "...",
  "structuredResult": { }
}
```

**New:**
```json
// AnalysisStreamChunk (enhanced)
{
  "type": "token",
  "content": "...",
  "summary": "...",
  "structuredResult": { },
  "analysisId": "guid",           // NEW: Analysis record ID
  "tokenUsage": 1234,             // NEW: Token consumption
  "partialStorage": false,        // NEW: Storage status
  "storageMessage": null          // NEW: Error details
}
```

### 3. PCF Control Updates

**File:** `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts`

**Changes Required (Task 021):**
1. Update endpoint URL: `/api/ai/document-intelligence/analyze` → `/api/ai/analysis/execute`
2. Update request format: single `documentId` → array `documentIds`
3. Add playbook resolution: Call `/api/ai/playbooks/by-name/Document%20Profile`
4. Parse new response fields: `partialStorage`, `storageMessage`
5. Display warning banner when `partialStorage: true`

---

## Impact on Dependent Projects

### Email-to-Document Automation Project

**Current Behavior (Before This Project):**
```
1. Email received → Job created (EmailToDocumentJobHandler)
2. Email converted to .eml → Document created in Dataverse
3. AI analysis job enqueued → DocumentAnalysisJobHandler processes
4. Analysis results saved → Document fields populated
```

**New Behavior (After This Project):**
```
1. Email received → Job created (EmailToDocumentJobHandler)
2. Email converted to .eml → Document created in Dataverse
3. ⚠️ AI analysis NOT enqueued (removed)
4. User manually triggers analysis from PCF
```

**Required Actions for Email-to-Document Project:**

1. **Update Configuration**
   ```json
   {
     "EmailProcessing": {
       "AutoEnqueueAi": false  // Set to false or remove
     }
   }
   ```

2. **Update Documentation**
   - Document that AI analysis is now user-triggered
   - Update user guides to show manual analysis trigger
   - Remove references to automatic AI analysis

3. **Consider Alternative Approaches (Optional)**
   - **Option A:** Accept manual trigger (simplest, recommended for MVP)
   - **Option B:** Implement service account background analysis (requires additional auth work)
   - **Option C:** Add "Analyze" button to email notification (UX enhancement)

**Benefits of New Approach:**
- ✅ User control over AI costs (don't analyze spam/irrelevant emails)
- ✅ Better security (user-authorized analysis only)
- ✅ Reduced background job complexity
- ✅ Easier debugging (sync with user session)

**Trade-offs:**
- ❌ Requires user action (not fully automated)
- ❌ Analysis may be delayed or skipped
- ❌ Users must know to trigger analysis

---

## Data Model Changes

### New Entities (Already Existed, Now Used)

| Entity | Purpose | Key Fields |
|--------|---------|------------|
| `sprk_analysisplaybook` | Playbook configurations | `sprk_name`, `sprk_prompt`, `sprk_trigger` |
| `sprk_aioutputtype` | Output type definitions | `sprk_name`, `sprk_targetfield` |
| `sprk_analysisoutput` | Generic output storage | `sprk_analysisid`, `sprk_outputtypeid`, `sprk_content` |

### Updated Entity Fields

**`sprk_document` (Enhanced for Document Profile):**
- No schema changes, existing fields used:
  - `sprk_summary`, `sprk_tldr`, `sprk_keywords`
  - `sprk_extractorganization`, `sprk_extractpeople`, etc.
  - `sprk_filesummarystatus` (now includes "partial" states)

**`sprk_analysis` (New Entity - Created by Orchestration):**
- `sprk_analysisid` (primary key)
- `sprk_playbookid` (lookup to playbook)
- `sprk_actionid` (lookup to action)
- `sprk_status` (pending, completed, failed)
- `sprk_tokenusage` (cost tracking)
- `sprk_workingdocument` (intermediate outputs)

---

## Migration Guide

### For API Consumers

**If you currently call the old endpoint:**

**Before:**
```typescript
const response = await fetch('/api/ai/document-intelligence/analyze', {
  method: 'POST',
  body: JSON.stringify({
    documentId: documentId,
    prompt: customPrompt
  })
});
```

**After:**
```typescript
// Step 1: Get Document Profile playbook ID
const playbookResponse = await fetch('/api/ai/playbooks/by-name/Document%20Profile');
const playbook = await playbookResponse.json();

// Step 2: Execute analysis
const response = await fetch('/api/ai/analysis/execute', {
  method: 'POST',
  body: JSON.stringify({
    documentIds: [documentId],  // Array
    playbookId: playbook.playbookId,
    additionalContext: customPrompt  // Optional
  })
});

// Step 3: Parse enhanced response
const reader = response.body.getReader();
while (true) {
  const { done, value } = await reader.read();
  if (done) break;

  const chunk = JSON.parse(value);

  // NEW: Check for partial storage
  if (chunk.partialStorage) {
    console.warn('Storage failed:', chunk.storageMessage);
    // Show warning to user
  }
}
```

### For Background Job Developers

**If you need to trigger AI analysis from a background job:**

**Problem:** New service requires `HttpContext` (user session)

**Solutions:**

1. **Don't trigger from background** (Recommended)
   - Let users trigger analysis manually
   - Add UI affordances (buttons, notifications)

2. **Service Account Approach** (Complex)
   ```csharp
   // Requires service account with:
   // - Graph API permissions (Files.Read.All)
   // - Dataverse permissions (Read access to documents)

   // Build HttpContext with service account token
   var httpContext = CreateServiceAccountContext(serviceToken);

   // Call orchestration service
   await _orchestrationService.ExecutePlaybookAsync(
       request,
       httpContext,  // Service account context
       ct);
   ```

3. **Deferred Analysis Pattern** (Alternative)
   ```csharp
   // Create "analysis pending" flag on document
   await _dataverseService.UpdateDocumentAsync(documentId, new {
       sprk_analysispending = true
   });

   // User sees pending indicator in PCF
   // User clicks "Analyze Now" button
   // PCF triggers analysis with user context
   ```

---

## Testing Strategy

### Integration Tests

**New Test Coverage (Task 023):**
1. `POST /api/ai/analysis/execute` with Document Profile playbook
2. SSE streaming returns enhanced `AnalysisStreamChunk` format
3. Soft failure handling (partial storage scenarios)
4. FullUAC authorization (authorized vs. unauthorized documents)
5. Playbook resolution by name
6. Multi-document analysis (Phase 2 feature)

### Manual Testing Checklist

- [ ] Upload document via PCF
- [ ] Trigger AI analysis (Document Profile)
- [ ] Verify dual storage (check both `sprk_analysisoutput` and `sprk_document` fields)
- [ ] Test soft failure (deny write permission, verify warning displays)
- [ ] Test authorization failure (analyze document user doesn't have access to)
- [ ] Test retry policy (simulate replication lag)
- [ ] Verify email-to-document conversion still works (without AI analysis)

---

## Future Enhancements

### Planned Features

1. **Multi-Document Analysis** (Phase 2.x)
   - Synthesize across multiple documents
   - Cross-reference detection
   - Comparative analysis

2. **Custom Playbooks** (Phase 3.x)
   - User-defined prompts and tools
   - Playbook sharing and versioning
   - Visual playbook builder

3. **Background Analysis with Service Account** (Future)
   - Optional service account mode
   - Configurable trust boundaries
   - Audit logging for service account operations

4. **Incremental Analysis** (Future)
   - Update only changed fields
   - Differential analysis on document updates
   - Version history tracking

---

## References

### Decision Documents
- [DECISION-BACKWARD-COMPATIBILITY.md](DECISION-BACKWARD-COMPATIBILITY.md) - Why we removed backward compatibility
- `docs/adr/ADR-013-ai-architecture.md` - AI Tool Framework design
- `docs/adr/ADR-008-endpoint-filters.md` - Authorization filter patterns

### Implementation Files
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` - Unified orchestration
- `src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs` - FullUAC implementation
- `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/StorageRetryPolicy.cs` - Retry logic
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs` - Field mapping

### API Documentation
- Endpoint: `POST /api/ai/analysis/execute`
- Endpoint: `GET /api/ai/playbooks/by-name/{name}`
- SSE Response Format: `AnalysisStreamChunk` model

---

## Contact / Questions

**For email-to-document-automation project team:**
- Review this document before integrating with the new analysis architecture
- Consider whether manual analysis trigger is acceptable for your use case
- If background analysis is critical, discuss service account implementation approach

**Questions about:**
- **Authorization model:** See `IAiAuthorizationService` implementation
- **Playbook configuration:** See seed data in `scripts/seed-data/playbooks.json`
- **Soft failure handling:** See `StorageRetryPolicy` and dual storage logic
- **PCF integration:** See Task 021 implementation (in progress)

---

**Last Updated:** 2026-01-07
**Project Status:** Phase 2.3 (in progress)
**Next Review:** After project completion (elaborate for formal architecture docs)
