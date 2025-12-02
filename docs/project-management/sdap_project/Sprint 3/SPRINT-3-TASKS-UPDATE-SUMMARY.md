# Sprint 3 Tasks - AccessRights Architecture Update Summary

**Date**: 2025-10-01
**Status**: 🔴 **CRITICAL - ALL TASKS UPDATED FOR ACCESSRIGHTS CONSISTENCY**
**Related Documents**:
- [Task 1.1 REVISED - AccessRights Authorization](Task-1.1-REVISED-AccessRights-Authorization.md)
- [PCF Control Specification](Task-1.1-PCF-Control-Specification.md)
- [Architecture Update Summary](ARCHITECTURE-UPDATE-AccessRights-Summary.md)

---

## Executive Summary

Following clarification of business requirements, **Task 1.1 has been significantly revised** from simple binary access control (Grant/Deny) to **granular permission-based authorization** matching Dataverse's 7 permission types.

**Critical Business Rule**: Users with **Read** access can **preview** files, but need **Write** access to **download** them.

This foundational change impacts **all Sprint 3 tasks**. This document summarizes the required updates to ensure consistency across the sprint.

---

## Task 1.1: Authorization Implementation

### Status: ✅ **FULLY REVISED**

### Changes Made
1. **Complete rewrite** of Task 1.1 → [Task-1.1-REVISED-AccessRights-Authorization.md](Task-1.1-REVISED-AccessRights-Authorization.md)
2. **New PCF control specification** → [Task-1.1-PCF-Control-Specification.md](Task-1.1-PCF-Control-Specification.md)
3. **AccessLevel enum** → **AccessRights [Flags] enum** (7 permission types)
4. **New OperationAccessPolicy** → Maps operations to required rights
5. **New Permissions API endpoints** → For UI integration
6. **Increased effort**: 5-8 days → **8-10 days** (due to UI integration)

### Key Additions
- `OperationAccessPolicy.cs` - Central operation → rights mapping
- `OperationAccessRule.cs` - IAuthorizationRule implementation
- `DocumentCapabilities.cs` - DTO for UI
- `PermissionsEndpoints.cs` - API for querying user capabilities
- PCF Dataset Control specification with conditional buttons

### Action Required
✅ **COMPLETE** - Documentation fully updated and reviewed

---

## Task 1.2: Configuration & Deployment Setup

### Status: ✅ **NO CHANGES REQUIRED**

### Impact Analysis
**MINIMAL** - Configuration task is orthogonal to authorization changes.

### Why No Changes
- Configuration structure remains the same
- Deployment process unchanged
- May need to document new permissions API endpoints in deployment guide

### Action Required
✅ **NONE** - Task can proceed as originally planned

---

## Task 2.1: OboSpeService Real Implementation

### Status: ⚠️ **UPDATED - AUTHORIZATION POLICIES REVISED**

### Impact Level
**HIGH** - File operation endpoints must use granular authorization policies.

### Required Changes

#### Before (Original Task)
```csharp
group.MapGet("/files/{fileName}", DownloadFileAsync)
    .RequireAuthorization("canreadfiles");  // ❌ Too broad
```

#### After (Updated)
```csharp
// Separate policies for each operation
group.MapGet("/files/{fileName}/preview", PreviewFileAsync)
    .RequireAuthorization("canpreviewfiles");  // Read access

group.MapGet("/files/{fileName}/download", DownloadFileAsync)
    .RequireAuthorization("candownloadfiles");  // Write access (NOT Read!)

group.MapPost("/files", UploadFileAsync)
    .RequireAuthorization("canuploadfiles");  // Write + Create

group.MapDelete("/files/{fileName}", DeleteFileAsync)
    .RequireAuthorization("candeletefiles");  // Delete access
```

### Updated Sections

#### New Policy Requirements Table
Added comprehensive table mapping file operations to authorization policies:

| Operation | Endpoint | Authorization Policy | Required AccessRights |
|-----------|----------|---------------------|----------------------|
| Preview file | `GET /files/{fileName}/preview` | `canpreviewfiles` | Read |
| Download file | `GET /files/{fileName}/download` | `candownloadfiles` | Write |
| Upload file | `POST /files` | `canuploadfiles` | Write + Create |
| Replace file | `PUT /files/{fileName}` | `canreplacefiles` | Write |
| Delete file | `DELETE /files/{fileName}` | `candeletefiles` | Delete |

#### Updated Implementation Steps

**Step 1: Add Note About Authorization**
```markdown
**IMPORTANT**: This implementation works in conjunction with Task 1.1's granular
authorization policies. Each file operation endpoint must use the correct
authorization policy:
- Preview: RequireAuthorization("canpreviewfiles")
- Download: RequireAuthorization("candownloadfiles")
- Upload: RequireAuthorization("canuploadfiles")
- Delete: RequireAuthorization("candeletefiles")

Authorization is enforced at the ENDPOINT level (not in OboSpeService itself).
```

**Step 2: Update Endpoint Registration Section**
Added detailed guidance on applying authorization policies when mapping endpoints.

#### Updated Validation Checklist
Added authorization policy validation items:
- [ ] Preview endpoint uses `canpreviewfiles` policy
- [ ] Download endpoint uses `candownloadfiles` policy (Write, NOT just Read)
- [ ] Upload endpoint uses `canuploadfiles` policy
- [ ] Delete endpoint uses `candeletefiles` policy
- [ ] Authorization decisions logged in audit system

### Action Required
✅ **COMPLETE** - Task 2.1 updated with granular authorization guidance

**File Modified**: [Task-2.1-OboSpeService-Real-Implementation.md](Task-2.1-OboSpeService-Real-Implementation.md) - Lines 1-1023

---

## Task 2.2: Dataverse Cleanup

### Status: ⚠️ **CAUTION ADDED**

### Impact Level
**MEDIUM** - Must preserve AccessRights mapping when removing legacy code.

### Required Changes

#### Added Caution Section
```markdown
## ⚠️ CRITICAL: Preserve AccessRights Functionality

**Context**: Task 1.1 implements granular AccessRights authorization matching
Dataverse's 7 permission types (Read, Write, Delete, Create, Append, AppendTo, Share).

**When removing `DataverseService.cs`**:
1. Ensure `DataverseWebApiService.cs` supports `RetrievePrincipalAccess` function
2. Must return granular permission strings: "ReadAccess,WriteAccess,DeleteAccess"
3. Do NOT break the mapping in `DataverseAccessDataSource`
4. Validate that AccessRights enum flags work correctly

**Test Before Removing**:
- User with Read only: Can preview, CANNOT download
- User with Write: Can preview AND download
- User with Delete: Can delete files
```

#### Updated Validation Checklist
Added AccessRights preservation checks:
- [ ] `RetrievePrincipalAccess` still works after cleanup
- [ ] Granular permissions mapped correctly (Read, Write, Delete, etc.)
- [ ] `DataverseAccessDataSource` correctly parses permission strings
- [ ] Integration tests validate AccessRights functionality preserved

### Why This Matters
The `DataverseAccessDataSource` relies on Dataverse Web API to retrieve granular permissions. If the cleanup accidentally removes or breaks this functionality, the entire AccessRights system fails.

### Action Required
✅ **COMPLETE** - Task 2.2 updated with preservation warnings

**File Modified**: [Task-2.2-Dataverse-Cleanup.md](Task-2.2-Dataverse-Cleanup.md) - Added caution section at top

---

## Task 3.1: Background Job Consolidation

### Status: ⚠️ **CONSIDERATION ADDED**

### Impact Level
**LOW-MEDIUM** - Indirect impact on authorization context.

### Required Changes

#### Added Authorization Context Section
```markdown
## Authorization Context for Background Jobs

**Context**: Task 1.1 implements granular AccessRights authorization.

**Consideration**: Background jobs may need to run with different authorization contexts:
1. **System Jobs**: Run with elevated privileges (bypass user authorization)
2. **User Jobs**: Run "as user" with user's AccessRights enforced
3. **Delegated Jobs**: Use specific service principal with its own rights

**Design Decision Required**:
- Which jobs bypass authorization vs. enforce it?
- How to pass user context to jobs (job metadata)?
- Should jobs check permissions before processing?

**Example**:
```csharp
public class DocumentProcessingJob
{
    public async Task ProcessAsync(JobContext context, CancellationToken ct)
    {
        // Does this job need user's AccessRights?
        if (context.RequiresUserAuthorization)
        {
            var userRights = await _accessDataSource.GetUserAccessAsync(
                context.UserId, context.ResourceId, ct);

            if (!OperationAccessPolicy.HasRequiredRights(userRights.AccessRights, "process_document"))
            {
                throw new UnauthorizedAccessException("Insufficient rights to process document");
            }
        }

        // ... perform job
    }
}
```

**Action Items**:
1. Document which background jobs require authorization
2. Add authorization context to job metadata
3. Update job processing to check rights when needed
```

### Why This Matters
If background jobs don't respect authorization, they could perform operations the user isn't allowed to do. For example, a file processing job might download a file on behalf of a user who only has Read (preview) access.

### Action Required
✅ **COMPLETE** - Task 3.1 updated with authorization context guidance

**File Modified**: [Task-3.1-Background-Job-Consolidation.md](Task-3.1-Background-Job-Consolidation.md) - Added authorization section

---

## Task 3.2: SpeFileStore Refactoring

### Status: ⚠️ **CLARIFICATION ADDED**

### Impact Level
**MEDIUM-HIGH** - Must clarify where authorization happens.

### Required Changes

#### Added Authorization Clarification Section
```markdown
## 🔐 Authorization Pattern Clarification

**Context**: Task 1.1 implements granular AccessRights authorization at the **ENDPOINT level**.

**Critical Design Principle**:
> **Authorization happens at ENDPOINT level, NOT in domain services.**

### Before Refactoring
```csharp
// OboFileEndpoints.cs
app.MapGet("/files/{fileName}/download", async (string fileName, OboSpeService spe) =>
{
    // ✅ Authorization enforced HERE via policy
    return await spe.DownloadFileAsync(...);
})
.RequireAuthorization("candownloadfiles");  // ← Enforced at endpoint

// OboSpeService.cs
public async Task<Stream> DownloadFileAsync(...)
{
    // ❌ NO authorization check here - already enforced at endpoint
    return await graph.Drives[driveId].Items[itemId].Content.GetAsync();
}
```

### After Refactoring (SpeFileStore Split)
```csharp
// File endpoints (Spe.Bff.Api/Api/FileEndpoints.cs)
app.MapGet("/files/{fileName}/download", async (string fileName, DriveItemOperations ops) =>
{
    // ✅ Authorization enforced HERE via policy
    return await ops.DownloadContentAsync(...);
})
.RequireAuthorization("candownloadfiles");  // ← Still enforced at endpoint

// DriveItemOperations.cs (domain service)
public async Task<Stream?> DownloadContentAsync(string driveId, string itemId, ...)
{
    // ❌ NO authorization check here
    // Authorization already checked by endpoint policy
    // This service just performs the operation

    var graphClient = _factory.CreateAppOnlyClient();
    return await graphClient.Drives[driveId].Items[itemId].Content.GetAsync();
}
```

### Key Principles
1. **Endpoints** use `.RequireAuthorization("policy-name")` to enforce access
2. **Domain services** (ContainerOperations, DriveItemOperations, UploadSessionManager)
   assume authorization already checked
3. **No duplicate checks** - authorization logic lives in OperationAccessRule
4. **Clean separation** - services focus on business logic, not security

### Why This Design?
- **Single Responsibility**: Services handle operations, authorization handlers handle security
- **DRY Principle**: Authorization logic not duplicated across services
- **Testability**: Can test services without mocking authorization
- **Performance**: Authorization checked once at entry point, not multiple times
```

#### Updated Validation Checklist
Added authorization pattern checks:
- [ ] Authorization policies applied at endpoint level
- [ ] Domain services do NOT contain authorization checks
- [ ] OperationAccessRule used for all authorization decisions
- [ ] No duplicate authorization logic in services

### Why This Matters
Developers might mistakenly add authorization checks inside `DriveItemOperations` or other services, creating duplication and complexity. This clarification ensures authorization stays at the correct layer.

### Action Required
✅ **COMPLETE** - Task 3.2 updated with authorization layer guidance

**File Modified**: [Task-3.2-SpeFileStore-Refactoring.md](Task-3.2-SpeFileStore-Refactoring.md) - Added authorization pattern section

---

## Task 4.1: Centralized Resilience

### Status: ✅ **NO CHANGES REQUIRED**

### Impact Analysis
**NONE** - Polly resilience policies are orthogonal to authorization.

### Why No Changes
- Retry logic operates at HTTP transport layer
- Authorization happens at application layer
- No interaction between resilience and AccessRights

### Action Required
✅ **NONE** - Task can proceed as originally planned

---

## Task 4.2: Testing Improvements

### Status: ⚠️ **UPDATED - ACCESSRIGHTS TEST SCENARIOS ADDED**

### Impact Level
**HIGH** - Must add comprehensive tests for AccessRights system.

### Required Changes

#### Added AccessRights Testing Section
```markdown
## 🔐 AccessRights Authorization Testing (Task 1.1)

**Context**: Task 1.1 implements granular AccessRights (Read/Write/Delete/Create/Append/AppendTo/Share).

### Required Test Scenarios

#### Unit Tests for OperationAccessPolicy
```csharp
using Xunit;
using FluentAssertions;
using Spaarke.Core.Auth;

public class OperationAccessPolicyTests
{
    [Theory]
    [InlineData(AccessRights.Read, "preview_file", true)]   // Read can preview
    [InlineData(AccessRights.Read, "download_file", false)] // Read CANNOT download
    [InlineData(AccessRights.Write, "download_file", true)] // Write can download
    [InlineData(AccessRights.Write, "upload_file", false)]  // Write alone can't upload
    [InlineData(AccessRights.Write | AccessRights.Create, "upload_file", true)] // Write + Create can upload
    [InlineData(AccessRights.Delete, "delete_file", true)]  // Delete can delete
    [InlineData(AccessRights.Share, "share_document", true)] // Share can share
    public void HasRequiredRights_VariousScenarios_ReturnsExpectedResult(
        AccessRights userRights,
        string operation,
        bool expectedAllowed)
    {
        // Act
        var result = OperationAccessPolicy.HasRequiredRights(userRights, operation);

        // Assert
        result.Should().Be(expectedAllowed);
    }

    [Fact]
    public void GetRequiredRights_KnownOperation_ReturnsCorrectRights()
    {
        // Act
        var rights = OperationAccessPolicy.GetRequiredRights("download_file");

        // Assert
        rights.Should().Be(AccessRights.Write);
    }

    [Fact]
    public void GetMissingRights_InsufficientRights_ReturnsMissing()
    {
        // Arrange
        var userRights = AccessRights.Read; // User only has Read

        // Act
        var missing = OperationAccessPolicy.GetMissingRights(userRights, "download_file");

        // Assert
        missing.Should().Be(AccessRights.Write); // Missing Write
    }
}
```

#### Integration Tests for Authorization
```csharp
public class AuthorizationIntegrationTests
{
    [Fact]
    public async Task GetPermissions_ReadOnlyUser_ReturnsCorrectCapabilities()
    {
        // Arrange: User has Read only
        var snapshot = new AccessSnapshot
        {
            UserId = "test-user",
            ResourceId = "test-doc",
            AccessRights = AccessRights.Read
        };

        // Act
        var response = await GetDocumentPermissionsAsync("test-doc", snapshot);

        // Assert
        response.CanPreview.Should().BeTrue();   // Has Read
        response.CanDownload.Should().BeFalse(); // Missing Write
        response.CanUpload.Should().BeFalse();   // Missing Write + Create
        response.CanReplace.Should().BeFalse();  // Missing Write
        response.CanDelete.Should().BeFalse();   // Missing Delete
        response.CanShare.Should().BeFalse();    // Missing Share
    }

    [Fact]
    public async Task GetPermissions_ReadWriteUser_CanDownloadButNotDelete()
    {
        // Arrange: User has Read + Write
        var snapshot = new AccessSnapshot
        {
            UserId = "test-user",
            ResourceId = "test-doc",
            AccessRights = AccessRights.Read | AccessRights.Write
        };

        // Act
        var response = await GetDocumentPermissionsAsync("test-doc", snapshot);

        // Assert
        response.CanPreview.Should().BeTrue();
        response.CanDownload.Should().BeTrue();  // Has Write
        response.CanReplace.Should().BeTrue();   // Has Write
        response.CanDelete.Should().BeFalse();   // Missing Delete (separate right)
    }

    [Fact]
    public async Task GetPermissions_FullControlUser_CanDoEverything()
    {
        // Arrange: User has all rights
        var snapshot = new AccessSnapshot
        {
            UserId = "admin-user",
            ResourceId = "test-doc",
            AccessRights = AccessRights.Read | AccessRights.Write |
                          AccessRights.Delete | AccessRights.Create |
                          AccessRights.Append | AccessRights.AppendTo |
                          AccessRights.Share
        };

        // Act
        var response = await GetDocumentPermissionsAsync("test-doc", snapshot);

        // Assert
        response.CanPreview.Should().BeTrue();
        response.CanDownload.Should().BeTrue();
        response.CanUpload.Should().BeTrue();
        response.CanReplace.Should().BeTrue();
        response.CanDelete.Should().BeTrue();
        response.CanShare.Should().BeTrue();
    }
}
```

#### WireMock Tests for Permissions API
```csharp
public class PermissionsEndpointWireMockTests
{
    [Fact]
    public async Task GetDocumentPermissions_ValidRequest_ReturnsCapabilities()
    {
        // Arrange
        var documentId = "doc-123";
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/documents/{documentId}/permissions")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(@"{
                    ""documentId"": ""doc-123"",
                    ""userId"": ""user-guid"",
                    ""canPreview"": true,
                    ""canDownload"": true,
                    ""canUpload"": true,
                    ""canReplace"": true,
                    ""canDelete"": false,
                    ""canReadMetadata"": true,
                    ""canUpdateMetadata"": true,
                    ""canShare"": false,
                    ""accessRights"": ""Read, Write""
                }"));

        // Act
        var response = await _httpClient.GetAsync($"/api/documents/{documentId}/permissions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<DocumentCapabilities>();
        content.CanDownload.Should().BeTrue();
        content.CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task PostBatchPermissions_MultipleDocuments_ReturnsAllCapabilities()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath("/api/documents/permissions/batch")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(@"{
                    ""permissions"": [
                        { ""documentId"": ""doc-1"", ""canPreview"": true, ""canDownload"": true },
                        { ""documentId"": ""doc-2"", ""canPreview"": true, ""canDownload"": false },
                        { ""documentId"": ""doc-3"", ""canPreview"": false, ""canDownload"": false }
                    ]
                }"));

        // Act
        var request = new { documentIds = new[] { "doc-1", "doc-2", "doc-3" } };
        var response = await _httpClient.PostAsJsonAsync("/api/documents/permissions/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("doc-1");
        content.Should().Contain("doc-2");
        content.Should().Contain("doc-3");
    }
}
```

#### Real Environment Tests (Manual Validation)
```markdown
**Test with real Dataverse users:**

1. **ralph.schroeder (System Admin)**:
   - [ ] Can preview documents
   - [ ] Can download documents
   - [ ] Can upload documents
   - [ ] Can delete documents
   - [ ] Can share documents
   - [ ] Audit logs show "Read, Write, Delete, Create, Share"

2. **testuser1 (Read Only)**:
   - [ ] Can preview documents
   - [ ] CANNOT download documents (403 Forbidden)
   - [ ] CANNOT upload documents (403 Forbidden)
   - [ ] CANNOT delete documents (403 Forbidden)
   - [ ] Audit logs show "Read" only

3. **testuser1 (Read + Write)**:
   - [ ] Can preview documents
   - [ ] Can download documents
   - [ ] Can upload documents
   - [ ] Can replace documents
   - [ ] CANNOT delete documents (403 Forbidden)
   - [ ] Audit logs show "Read, Write"
```
```

#### Updated Validation Checklist
Added AccessRights test coverage requirements:
- [ ] OperationAccessPolicy unit tests pass (10+ scenarios)
- [ ] AccessRights bitwise operations tested
- [ ] DataverseAccessDataSource parses permission strings correctly
- [ ] Authorization integration tests validate Read/Write/Delete separation
- [ ] Permissions API endpoints tested (single + batch)
- [ ] WireMock tests for permissions API
- [ ] Manual validation with real users (ralph.schroeder, testuser1)

### Why This Matters
The AccessRights system is the **foundation of security**. Comprehensive tests ensure:
1. Read users can't download (only preview)
2. Write users can download
3. Delete is separate from Write
4. Bitwise flag operations work correctly
5. Dataverse permission mapping is accurate

### Action Required
✅ **COMPLETE** - Task 4.2 updated with AccessRights test scenarios

**File Modified**: [Task-4.2-Testing-Improvements.md](Task-4.2-Testing-Improvements.md) - Added AccessRights testing section

---

## Task 4.3: Code Quality & Consistency

### Status: ✅ **NO CHANGES REQUIRED**

### Impact Analysis
**MINIMAL** - AccessRights follows .NET conventions.

### Why No Changes
- `AccessRights` [Flags] enum follows standard .NET pattern
- Code quality already high (comprehensive docs, XML comments)
- No namespace inconsistencies introduced
- TODOs will be in new files, handled by this task

### Action Required
✅ **NONE** - Task can proceed as originally planned

---

## Sprint 3 README Update Required

### Status: ⚠️ **NEEDS UPDATE**

### Required Changes to README.md

#### Update Task 1.1 Entry
```markdown
| # | Task | Priority | Days | File |
|---|------|----------|------|------|
| 1.1 | Authorization Implementation | 🔴 CRITICAL | ~~5-8~~ **8-10** | [Task-1.1-REVISED-AccessRights-Authorization.md](Task-1.1-REVISED-AccessRights-Authorization.md) |
```

#### Add Architecture Note
```markdown
## ⚠️ Critical Architecture Update: Granular AccessRights

**Date**: 2025-10-01

Task 1.1 has been **significantly revised** to implement **granular permission-based authorization**:

### What Changed
- **Before**: Simple binary access (Grant/Deny)
- **After**: Granular permissions matching Dataverse (Read/Write/Delete/Create/Append/AppendTo/Share)

### Business Rule
**Users with Read access can preview files, but need Write access to download them.**

### Impact
- **Task 1.1**: Complete rewrite + PCF control specification (8-10 days)
- **Task 2.1**: Must use granular authorization policies on endpoints
- **Task 3.2**: Authorization enforced at endpoint level (not in services)
- **Task 4.2**: Must add AccessRights test scenarios

See [Architecture Update Summary](ARCHITECTURE-UPDATE-AccessRights-Summary.md) for full details.
```

### Action Required
⚠️ **IN PROGRESS** - Will update Sprint 3 README.md next

---

## Implementation Priority (Updated)

Given AccessRights is **foundational**, here's the recommended order:

### Week 1: Core Authorization (BLOCKING) 🔴
1. **Task 1.1 (Revised)** - Implement AccessRights system
   - AccessRights enum
   - OperationAccessPolicy
   - OperationAccessRule
   - Permissions API endpoints
   - **Status**: Ready to start

### Week 2: Configuration & Endpoint Integration
2. **Task 1.2** - Configuration (can run in parallel with 1.1)
   - **Status**: Can start immediately

3. **Task 2.1 (Updated)** - Apply granular policies to file endpoints
   - **Status**: BLOCKED until 1.1 complete

### Week 3: Refactoring with Authorization Context
4. **Task 2.2 (Updated)** - Dataverse cleanup (preserve AccessRights)
   - **Status**: BLOCKED until 1.1 complete

5. **Task 3.2 (Updated)** - SpeFileStore refactoring (respect authorization layer)
   - **Status**: BLOCKED until 1.1 complete

### Week 4: UI Integration
6. **PCF Control Development** - Build Dataset Control
   - **Status**: BLOCKED until 1.1 permissions API complete

### Week 5-6: Background Jobs, Testing, Cleanup
7. **Task 3.1 (Updated)** - Background job consolidation (add authorization context)
8. **Task 4.2 (Updated)** - Testing improvements (add AccessRights tests)
9. **Task 4.1** - Centralized resilience (no changes)
10. **Task 4.3** - Code quality (no changes)

---

## Files Modified Summary

| File | Change Type | Impact |
|------|-------------|--------|
| Task-1.1-REVISED-AccessRights-Authorization.md | ✅ Complete Rewrite | 1,200+ lines, foundational |
| Task-1.1-PCF-Control-Specification.md | ✅ New File | 800+ lines, UI integration |
| ARCHITECTURE-UPDATE-AccessRights-Summary.md | ✅ New File | 600+ lines, cross-task impact |
| Task-2.1-OboSpeService-Real-Implementation.md | ✅ Updated | Added authorization policies section |
| Task-2.2-Dataverse-Cleanup.md | ✅ Updated | Added AccessRights preservation caution |
| Task-3.1-Background-Job-Consolidation.md | ✅ Updated | Added authorization context guidance |
| Task-3.2-SpeFileStore-Refactoring.md | ✅ Updated | Added authorization layer clarification |
| Task-4.2-Testing-Improvements.md | ✅ Updated | Added AccessRights test scenarios |
| README.md | ⚠️ Pending | Update effort estimates and add architecture note |

---

## Validation Checklist

### Documentation
- [x] Task 1.1 completely revised with AccessRights
- [x] PCF control specification created
- [x] Architecture impact analysis created
- [x] Task 2.1 updated with authorization policies
- [x] Task 2.2 updated with preservation warnings
- [x] Task 3.1 updated with authorization context
- [x] Task 3.2 updated with authorization layer guidance
- [x] Task 4.2 updated with AccessRights tests
- [ ] Sprint 3 README updated (NEXT STEP)

### Consistency
- [x] All tasks reference granular AccessRights (not AccessLevel)
- [x] Operation → rights mapping documented consistently
- [x] Authorization at endpoint level clarified
- [x] UI integration approach documented
- [x] Testing strategy includes AccessRights scenarios

### Completeness
- [x] All 10 Sprint 3 tasks reviewed
- [x] Impact level assessed for each task
- [x] Required changes documented
- [x] No orphaned references to old AccessLevel enum
- [x] PCF control development plan complete

---

## Next Steps

1. ✅ **Complete documentation updates** - DONE
2. ⚠️ **Update Sprint 3 README.md** - IN PROGRESS (this summary)
3. 🔜 **Begin Task 1.1 implementation** - AccessRights core system
4. 🔜 **Communicate to frontend team** - Prepare for permissions API
5. 🔜 **Review with stakeholders** - Ensure business requirements met

---

## Success Metrics

### Documentation Quality
- ✅ All tasks have clear AccessRights guidance
- ✅ No ambiguity about authorization layer
- ✅ AI-directed coding prompts updated
- ✅ Testing scenarios comprehensive

### Consistency
- ✅ Uniform terminology (AccessRights, not AccessLevel)
- ✅ Consistent operation naming (preview_file, download_file, etc.)
- ✅ Authorization pattern applied uniformly

### Completeness
- ✅ All impacted tasks identified and updated
- ✅ PCF control specification complete
- ✅ Testing strategy comprehensive
- ✅ Migration path documented

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Maintained By**: Sprint 3 Task Force
**Status**: ✅ **DOCUMENTATION UPDATES COMPLETE**

**All Sprint 3 tasks are now consistent with the granular AccessRights architecture.**
