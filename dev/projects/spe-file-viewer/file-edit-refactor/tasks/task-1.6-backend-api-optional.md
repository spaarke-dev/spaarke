# Task 1.6: Update Backend API Response (OPTIONAL)

**Phase**: 1 - Core Functionality
**Priority**: Low (Optional Enhancement)
**Estimated Time**: 30 minutes
**Depends On**: None (backend only)
**Blocks**: None

---

## Objective

**OPTIONAL**: Modify the `/api/documents/{id}/office` endpoint to return structured JSON with permissions instead of redirecting. This enables the PCF to detect read-only access BEFORE loading Office Online.

**Recommendation**: **SKIP THIS TASK** for initial implementation. Office Online automatically enforces read-only mode, so permission pre-detection is not critical for Phase 1.

---

## Context & Knowledge Required

### What You Need to Know
1. **ASP.NET Core Minimal APIs**: TypedResults.Ok() for JSON responses
2. **Microsoft Graph API**: DriveItem permissions facet
3. **SharePoint Permissions**: Role types (owner, write, read)
4. **Current Endpoint Behavior**: Currently redirects with `TypedResults.Redirect()`

### Files to Review Before Starting
- **FileAccessEndpoints.cs**: [c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs#L323-L388)
- **GraphClientFactory**: [c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs)

---

## Decision: Skip or Implement?

### Option A: Skip This Task (Recommended for Phase 1)

**Rationale**:
- Office Online automatically loads in read-only mode if user lacks edit permissions
- Graph API permissions facet can be unreliable or null
- Simplified implementation reduces complexity and deployment risk
- Read-only dialog can still be shown after Office Online loads (detected client-side)

**Implementation**: Use simplified response:
```csharp
return TypedResults.Ok(new
{
    officeUrl = driveItem.WebUrl,
    permissions = new
    {
        canEdit = true,  // Unknown at BFF level
        canView = true,
        role = "unknown"
    },
    correlationId = context.TraceIdentifier
});
```

**Result**: PCF shows read-only dialog for all users (slight UX compromise, but safe).

---

### Option B: Implement Permission Detection (Phase 2 Enhancement)

**If you choose to implement**, follow the steps below.

---

## Implementation Prompt (Option B)

### Step 1: Modify GetOffice Endpoint

**Location**: `c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs`
**Find**: `GetOffice` method (~line 323-388)

**Current Code** (lines 360-387):
```csharp
var driveItem = await graphClient.Drives[document.GraphDriveId!]
    .Items[document.GraphItemId!]
    .GetAsync(requestConfiguration =>
    {
        requestConfiguration.QueryParameters.Select = new[] { "id", "name", "webUrl" };
    }, cancellationToken: ct);

if (string.IsNullOrEmpty(driveItem?.WebUrl))
{
    throw new SdapProblemException(...);
}

logger.LogInformation("Redirecting to Office web app | WebUrl: {WebUrl}", driveItem.WebUrl);

// 5. Redirect to Office web app
return TypedResults.Redirect(driveItem.WebUrl);
```

**Replace With**:
```csharp
// 4. Get Office web app URL using OBO + permissions
var driveItem = await graphClient.Drives[document.GraphDriveId!]
    .Items[document.GraphItemId!]
    .GetAsync(requestConfiguration =>
    {
        // Request additional fields for permissions
        requestConfiguration.QueryParameters.Select = new[] {
            "id", "name", "webUrl", "permissions"
        };
        requestConfiguration.QueryParameters.Expand = new[] { "permissions" };
    }, cancellationToken: ct);

if (string.IsNullOrEmpty(driveItem?.WebUrl))
{
    throw new SdapProblemException(
        "office_url_not_available",
        "Office URL Not Available",
        $"Graph API did not return a webUrl for document {documentId}",
        500
    );
}

// 5. Extract user permissions from driveItem
// Note: Permissions facet may be null or unreliable
var canEdit = driveItem.Permissions?.Any(p =>
    p.Roles?.Contains("write") == true ||
    p.Roles?.Contains("owner") == true
) ?? false;

var canView = driveItem.Permissions?.Any(p =>
    p.Roles?.Contains("read") == true ||
    p.Roles?.Contains("write") == true ||
    p.Roles?.Contains("owner") == true
) ?? true; // Default to true if no permissions returned

var userRole = canEdit ? "editor" : (canView ? "reader" : "unknown");

logger.LogInformation(
    "Office URL retrieved | WebUrl: {WebUrl} | CanEdit: {CanEdit} | CanView: {CanView} | Role: {Role}",
    driveItem.WebUrl, canEdit, canView, userRole
);

// 6. Return structured response (NOT redirect)
return TypedResults.Ok(new
{
    officeUrl = driveItem.WebUrl,
    permissions = new
    {
        canEdit = canEdit,
        canView = canView,
        role = userRole
    },
    correlationId = context.TraceIdentifier
});
```

---

## Validation & Review

### Pre-Commit Checklist

1. **Build API**:
   ```bash
   cd c:/code_files/spaarke/src/api/Spe.Bff.Api
   dotnet build
   ```
   - [ ] No compilation errors

2. **Response Structure**:
   - [ ] Returns JSON (not redirect)
   - [ ] Matches `OfficeUrlResponse` TypeScript interface
   - [ ] Includes officeUrl, permissions, correlationId

3. **Permission Logic**:
   - [ ] Checks for "write" and "owner" roles for canEdit
   - [ ] Checks for "read", "write", "owner" for canView
   - [ ] Defaults to safe values if permissions facet is null

---

## Testing

### Manual API Test

```bash
# Test endpoint with bearer token
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{documentId}/office" \
  -H "Authorization: Bearer {token}" \
  -H "X-Correlation-Id: test-123"
```

**Expected Response**:
```json
{
  "officeUrl": "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?...",
  "permissions": {
    "canEdit": true,
    "canView": true,
    "role": "editor"
  },
  "correlationId": "test-123"
}
```

---

## Acceptance Criteria (If Implementing)

- [x] Endpoint returns JSON (not redirect)
- [x] Response includes officeUrl, permissions, correlationId
- [x] Permission detection checks Roles facet
- [x] Logs include permission details
- [x] Handles null permissions gracefully (defaults to safe values)

---

## Known Limitations

1. **Permissions Facet May Be Null**: Graph API sometimes doesn't return permissions for SPE files
2. **Permission Propagation Delay**: Recently changed permissions may not be reflected immediately
3. **Role Complexity**: SharePoint has many permission levels; simplified to read/write/owner

**Mitigation**: If permissions are unreliable, fall back to Option A (simplified response).

---

## Files Modified (If Implementing)

- `c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs`

---

## Recommendation

**Skip this task for Phase 1**. Implement in Phase 2 if user feedback indicates permission detection is critical.

**Rationale**:
- Office Online enforces permissions automatically
- Graph API permissions can be unreliable
- Adds complexity without significant UX benefit
- Can be added later without breaking changes

---

## Next Task

**Task 2.1**: Build and Package PCF
- Update Solution.xml version to 1.0.4
- Build PCF control and solution package
