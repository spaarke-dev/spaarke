# Sprint 3 Architecture Update: Granular AccessRights System
## Summary of Changes & Cross-Task Impact

**Date**: 2025-10-01
**Priority**: 🔴 **CRITICAL - FOUNDATIONAL CHANGE**
**Impact**: All Sprint 3 tasks must align with this approach

---

## Executive Summary

Based on your clarified business requirements, we've **upgraded the authorization architecture** from binary access control (Grant/Deny) to **granular permission-based system** that:

1. **Maps Dataverse's 7 permission types** directly to SPE operations
2. **Enforces operation-level security** (Preview ≠ Download ≠ Delete)
3. **Exposes capabilities to UI** for conditional rendering
4. **Supports PCF Dataset Control** with permission-based buttons

**Critical Business Rule**: Users with **Read** access can **preview** files, but need **Write** access to **download** them.

---

## What Changed

### Before (Original Task 1.1)

```csharp
// Simple binary access
public enum AccessLevel
{
    None,
    Deny,
    Grant  // ❌ Too simple - all operations treated the same
}

// All operations treated equally
if (accessLevel == AccessLevel.Grant)
    return Allow();  // Can do everything!
```

**Problems**:
- Can't distinguish Read vs Write vs Delete
- Can't enforce "preview allowed but not download"
- UI can't show conditional buttons
- Doesn't match Dataverse's granular model

### After (Revised Task 1.1)

```csharp
// Granular permissions matching Dataverse
[Flags]
public enum AccessRights
{
    None         = 0,
    Read         = 1 << 0,   // 0000001
    Write        = 1 << 1,   // 0000010
    Delete       = 1 << 2,   // 0000100
    Create       = 1 << 3,   // 0001000
    Append       = 1 << 4,   // 0010000
    AppendTo     = 1 << 5,   // 0100000
    Share        = 1 << 6    // 1000000
}

// Operation-specific checking
if (OperationAccessPolicy.HasRequiredRights(userRights, "download_file"))
    return Allow();  // Only if user has Write!
```

**Benefits**:
- ✅ Direct 1:1 mapping to Dataverse permissions
- ✅ Operation-level enforcement (preview vs download vs delete)
- ✅ UI can query: "Can this user download this document?"
- ✅ Extensible for future operations
- ✅ Standard .NET [Flags] pattern

---

## Business Rules Implemented

### Permission → Operation Mapping

| User's Dataverse Permission | What They Can Do in UI |
|------------------------------|------------------------|
| **Read** only | Preview file (Office Online viewer) |
| **Read + Write** | Preview + Download + Upload + Replace |
| **Read + Write + Delete** | Preview + Download + Upload + Replace + Delete |
| **Read + Write + Share** | Preview + Download + Upload + Replace + Share |
| **Full Control** | All operations |

### Specific Operation Requirements

| Operation | Required AccessRights | Endpoint |
|-----------|----------------------|----------|
| **Preview File** | Read | `GET /api/documents/{id}/preview` |
| **Download File** | Write (**not** just Read) | `GET /api/documents/{id}/download` |
| **Upload File** | Write + Create | `POST /api/containers/{id}/files` |
| **Replace File** | Write | `PUT /api/documents/{id}/file` |
| **Delete File** | Delete | `DELETE /api/documents/{id}` |
| **Share Document** | Share | `POST /api/documents/{id}/share` |
| **Read Metadata** | Read | `GET /api/documents/{id}/metadata` |
| **Update Metadata** | Write | `PATCH /api/documents/{id}/metadata` |

---

## New API Endpoints for UI Integration

### GET /api/documents/{id}/permissions

**Purpose**: UI queries user's capabilities for conditional rendering

**Request**:
```http
GET /api/documents/abc-123/permissions HTTP/1.1
Authorization: Bearer {token}
```

**Response**:
```json
{
  "documentId": "abc-123",
  "userId": "user-guid",
  "canPreview": true,
  "canDownload": true,
  "canUpload": true,
  "canReplace": true,
  "canDelete": false,
  "canReadMetadata": true,
  "canUpdateMetadata": true,
  "canShare": false,
  "accessRights": "Read, Write"
}
```

**UI Usage**:
```typescript
// PCF Control
const caps = await fetch(`/api/documents/${docId}/permissions`);
previewButton.visible = caps.canPreview;
downloadButton.visible = caps.canDownload;
deleteButton.visible = caps.canDelete;
```

### POST /api/documents/permissions/batch

**Purpose**: Performance - get capabilities for multiple documents in one request

**Request**:
```http
POST /api/documents/permissions/batch HTTP/1.1
Content-Type: application/json

{
  "documentIds": ["doc-1", "doc-2", "doc-3"]
}
```

**Response**:
```json
{
  "permissions": [
    { "documentId": "doc-1", "canPreview": true, "canDownload": true, ... },
    { "documentId": "doc-2", "canPreview": true, "canDownload": false, ... },
    { "documentId": "doc-3", "canPreview": false, "canDownload": false, ... }
  ]
}
```

---

## Files Created/Updated

### New Files

| File | Purpose | Lines |
|------|---------|-------|
| `Task-1.1-REVISED-AccessRights-Authorization.md` | Updated task with granular approach | 1,200+ |
| `Task-1.1-PCF-Control-Specification.md` | PCF control spec for UI | 800+ |
| `ARCHITECTURE-UPDATE-AccessRights-Summary.md` | This document | 600+ |
| `src/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs` | Operation → rights mapping | 150 |
| `src/shared/Spaarke.Core/Auth/Rules/OperationAccessRule.cs` | Permission checking rule | 100 |
| `src/api/Spe.Bff.Api/Models/DocumentCapabilities.cs` | DTO for UI | 80 |
| `src/api/Spe.Bff.Api/Api/PermissionsEndpoints.cs` | API for UI | 200 |

### Updated Files

| File | Change |
|------|--------|
| `src/shared/Spaarke.Dataverse/IAccessDataSource.cs` | AccessLevel → AccessRights enum |
| `src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | Map Dataverse rights to AccessRights |
| `src/api/Spe.Bff.Api/Program.cs` | Add granular authorization policies |
| `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` | Register OperationAccessRule |
| `tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs` | Test AccessRights |

---

## Impact on Other Sprint 3 Tasks

### ✅ Task 1.2: Configuration & Deployment Setup
**Impact**: MINIMAL
- No changes needed
- Configuration still works the same
- May need to document new API endpoints

**Action**: None required

---

### ⚠️ Task 2.1: OboSpeService Real Implementation
**Impact**: HIGH - MUST USE CORRECT AUTHORIZATION POLICIES

**Required Changes**:

File endpoints must use new authorization policies:

```csharp
// BEFORE
group.MapGet("/files/{fileName}", DownloadFileAsync)
    .RequireAuthorization("canreadfiles");  // ❌ Wrong - too broad

// AFTER
group.MapGet("/files/{fileName}/preview", PreviewFileAsync)
    .RequireAuthorization("canpreviewfiles");  // ✅ Correct - Read access

group.MapGet("/files/{fileName}/download", DownloadFileAsync)
    .RequireAuthorization("candownloadfiles");  // ✅ Correct - Write access

group.MapPost("/files", UploadFileAsync)
    .RequireAuthorization("canuploadfiles");  // ✅ Correct - Write + Create

group.MapDelete("/files/{fileName}", DeleteFileAsync)
    .RequireAuthorization("candeletefiles");  // ✅ Correct - Delete access
```

**Action Required**: Update Task 2.1 documentation to use correct policies

---

### ⚠️ Task 2.2: Dataverse Cleanup
**Impact**: MEDIUM - MUST PRESERVE AccessRights MAPPING

**Caution**:
- When removing legacy `DataverseService.cs`, ensure `DataverseWebApiService.cs` supports `RetrievePrincipalAccess`
- Must return granular permission strings: "ReadAccess,WriteAccess,DeleteAccess"
- Don't break the mapping in `DataverseAccessDataSource`

**Action Required**: Add note to Task 2.2 to preserve AccessRights functionality

---

### ⚠️ Task 3.1: Background Job Consolidation
**Impact**: LOW - INDIRECT

**Consideration**:
- Background jobs may need to run "as user" or "as system"
- System jobs might need full access regardless of user permissions
- Document which jobs bypass authorization vs. enforce it

**Action Required**: Add authorization context to job processing

---

### ⚠️ Task 3.2: SpeFileStore Refactoring
**Impact**: HIGH - MUST RESPECT AUTHORIZATION

**Required Changes**:

When splitting `SpeFileStore` into focused services, ensure each operation checks authorization:

```csharp
// In ContainerOperations.cs
public async Task CreateContainerAsync(string containerId, CancellationToken ct)
{
    // Authorization already checked by endpoint policy "canmanagecontainers"
    // This service just performs the operation
    // ...
}

// In DriveItemOperations.cs
public async Task DownloadFileAsync(string driveId, string itemId, CancellationToken ct)
{
    // Authorization already checked by endpoint policy "candownloadfiles"
    // This service just performs the operation
    // ...
}
```

**Principle**: Authorization happens at **endpoint level**, not in domain services.

**Action Required**: Update Task 3.2 to clarify authorization is at endpoint, not service

---

### ✅ Task 4.1: Centralized Resilience
**Impact**: NONE
- Polly policies are orthogonal to authorization
- No changes needed

**Action**: None required

---

### ⚠️ Task 4.2: Testing Improvements
**Impact**: HIGH - MUST TEST ACCESSRIGHTS

**Required Changes**:

Add comprehensive tests for AccessRights:

```csharp
// Test granular permissions
[Theory]
[InlineData(AccessRights.Read, "preview_file", true)]   // Read can preview
[InlineData(AccessRights.Read, "download_file", false)] // Read CANNOT download
[InlineData(AccessRights.Write, "download_file", true)] // Write can download
[InlineData(AccessRights.Delete, "delete_file", true)]  // Delete can delete
public void OperationAccessPolicy_ChecksGranularRights(
    AccessRights userRights,
    string operation,
    bool expectedAllowed)
{
    var result = OperationAccessPolicy.HasRequiredRights(userRights, operation);
    Assert.Equal(expectedAllowed, result);
}

// Test UI capabilities endpoint
[Fact]
public async Task GetPermissions_ReturnsCorrectCapabilities()
{
    // User has Read + Write (but not Delete)
    var snapshot = new AccessSnapshot
    {
        UserId = "test-user",
        ResourceId = "test-doc",
        AccessRights = AccessRights.Read | AccessRights.Write
    };

    var response = await GetPermissions("test-doc", snapshot);

    Assert.True(response.CanPreview);   // Has Read
    Assert.True(response.CanDownload);  // Has Write
    Assert.False(response.CanDelete);   // Missing Delete
}
```

**Action Required**: Update Task 4.2 to include AccessRights test scenarios

---

### ✅ Task 4.3: Code Quality & Consistency
**Impact**: LOW
- AccessRights follows .NET conventions
- [Flags] enum is standard pattern
- No consistency issues

**Action**: None required (already compliant)

---

## Implementation Priority

Given this is **foundational**, here's the recommended order:

### Week 1: Core Authorization (BLOCKING)
1. **Task 1.1 (Revised)** - Implement AccessRights system
   - AccessRights enum
   - OperationAccessPolicy
   - OperationAccessRule
   - Permissions API endpoints
   - **Status**: Can start immediately

### Week 2: Endpoint Integration (DEPENDS ON 1.1)
2. **Task 2.1 (Updated)** - Apply correct authorization policies to file endpoints
   - **Status**: BLOCKED until 1.1 complete

3. **Task 1.2** - Configuration (can run in parallel with 1.1)
   - **Status**: Can start immediately

### Week 3: Refactoring with Authorization
4. **Task 2.2** - Dataverse cleanup (preserve AccessRights)
   - **Status**: BLOCKED until 1.1 complete

5. **Task 3.2** - SpeFileStore refactoring (respect authorization)
   - **Status**: BLOCKED until 1.1 complete

### Week 4: UI Integration
6. **PCF Control Development** - Build Dataset Control
   - **Status**: BLOCKED until 1.1 permissions API complete

### Week 5-6: Remainder
7. Tasks 3.1, 4.1, 4.2, 4.3 - As originally planned

---

## Migration Path

### Phase 1: Implement Core (Task 1.1 Revised)
- Change AccessLevel to AccessRights
- Implement OperationAccessPolicy
- Implement OperationAccessRule
- Add permissions API endpoints
- Deploy to dev

### Phase 2: Update Endpoints (Task 2.1)
- Apply granular authorization policies to all endpoints
- Test with different user permissions
- Verify audit logs

### Phase 3: UI Integration
- Implement PCF control
- Test conditional buttons
- Deploy to staging

### Phase 4: Production
- Full QA validation
- Performance testing
- Deploy to production

---

## Breaking Changes

### API Changes

**BREAKING**: AccessSnapshot.AccessLevel removed, replaced with AccessSnapshot.AccessRights

**Migration**:
```csharp
// OLD CODE (breaks)
if (snapshot.AccessLevel == AccessLevel.Grant)
    return Allow();

// NEW CODE (works)
if (OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, operation))
    return Allow();
```

### Authorization Policy Changes

**BREAKING**: Policies now operation-specific

**Migration**:
```csharp
// OLD POLICY (too broad)
.RequireAuthorization("canreadfiles")

// NEW POLICIES (granular)
.RequireAuthorization("canpreviewfiles")   // For preview
.RequireAuthorization("candownloadfiles")  // For download
.RequireAuthorization("candeletefiles")    // For delete
```

---

## Testing Validation

Before marking Task 1.1 complete, validate:

### Unit Tests ✅
- [ ] OperationAccessPolicy returns correct rights for each operation
- [ ] OperationAccessPolicy.HasRequiredRights checks bitwise correctly
- [ ] MapDataverseAccessRights parses "ReadAccess,WriteAccess" correctly
- [ ] AccessRights flags combine correctly with bitwise OR

### Integration Tests ✅
- [ ] User with Read only: can preview, cannot download
- [ ] User with Write: can preview and download
- [ ] User with Delete: can delete
- [ ] User with no access: 403 Forbidden
- [ ] Permissions endpoint returns correct capabilities

### Manual Tests ✅
- [ ] ralph.schroeder (admin): can do everything
- [ ] testuser1 (read-only): can preview, cannot download
- [ ] testuser1 (write): can download
- [ ] Audit logs show granular rights (Read, Write, Delete)
- [ ] PCF control shows correct buttons

---

## Documentation Updates Needed

### API Documentation
- [ ] Update OpenAPI spec with permissions endpoints
- [ ] Document operation → required rights mapping
- [ ] Add examples for UI integration

### Developer Documentation
- [ ] Update authorization guide
- [ ] Document OperationAccessPolicy usage
- [ ] Add troubleshooting guide

### UI Documentation
- [ ] PCF control integration guide
- [ ] Power Apps usage examples
- [ ] React/TypeScript examples

---

## Questions & Decisions

### Q1: Should we support custom operations?

**Decision**: Yes, via OperationAccessPolicy
```csharp
// Easy to add new operations
["custom_operation"] = AccessRights.Write | AccessRights.Share
```

### Q2: Should UI cache capabilities?

**Decision**: Yes, with 5-minute TTL
- Performance optimization
- Reduces API calls
- Acceptable staleness for permissions

### Q3: What if Dataverse adds new permission types?

**Decision**: Easy to extend AccessRights enum
```csharp
[Flags]
public enum AccessRights
{
    // ... existing ...
    NewPermission = 1 << 7  // Just add new bit
}
```

---

## Success Metrics

### Security Metrics
- **Audit coverage**: 100% of authorization decisions logged
- **Fail-closed rate**: 100% (all errors deny access)
- **Unauthorized access attempts**: 0 successful

### Performance Metrics
- **Authorization check latency**: P95 < 200ms
- **Permissions API latency**: P95 < 100ms
- **Batch API efficiency**: 1 call for N documents (not N calls)

### UI Metrics
- **Button accuracy**: 100% (buttons match actual permissions)
- **User confusion**: Minimize "why can't I do this?" support tickets
- **Response time**: Buttons update < 500ms on selection change

---

## Rollback Plan

If issues arise:

### Step 1: Identify Issue
- Authorization too restrictive?
- Performance issues?
- UI not rendering correctly?

### Step 2: Quick Fix Options
- **Option A**: Feature flag to revert to simple Grant/Deny temporarily
- **Option B**: Fix specific operation mapping in OperationAccessPolicy
- **Option C**: Disable UI integration, keep backend authorization

### Step 3: Rollback (if needed)
- Revert to pre-AccessRights commit
- Deploy previous version
- Investigate and fix
- Re-deploy when resolved

---

## Next Steps

1. **Review this document** with team - ensure everyone understands changes
2. **Update Sprint 3 README** - reflect new architecture
3. **Start implementing Task 1.1 (Revised)** - core AccessRights system
4. **Update other task documents** - ensure consistency
5. **Communicate to frontend team** - prepare for permissions API

---

## Approvals

**Architecture Review**: [ ] Approved by: _____________ Date: _______
**Security Review**: [ ] Approved by: _____________ Date: _______
**Development Team**: [ ] Approved by: _____________ Date: _______
**Product Owner**: [ ] Approved by: _____________ Date: _______

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Maintained By**: Sprint 3 Task Force
**Status**: 🔴 PENDING APPROVAL - CRITICAL FOR SPRINT SUCCESS
