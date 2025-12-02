# Task 2.2: Dataverse Cleanup - Consolidate to Single Web API Implementation

**Priority:** MEDIUM (Sprint 3, Phase 2)
**Estimated Effort:** 1-2 days
**Status:** IMPROVES MAINTAINABILITY
**Dependencies:** None

---

## Context & Problem Statement

The codebase contains **two competing Dataverse implementations**, creating confusion and maintenance burden:

1. **DataverseService.cs** (461 lines): Uses `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient`
   - Legacy WCF-based approach
   - Requires System.ServiceModel dependencies
   - Uses synchronous SDK patterns
   - File: `C:\code_files\spaarke\src\shared\Spaarke.Dataverse\DataverseService.cs`

2. **DataverseWebApiService.cs** (340 lines): Uses REST/Web API via HttpClient
   - Modern .NET 8.0 compatible
   - Async-first design
   - Uses IHttpClientFactory
   - No WCF dependencies
   - File: `C:\code_files\spaarke\src\shared\Spaarke.Dataverse\DataverseWebApiService.cs`

**Both implement the same `IDataverseService` interface**, but only **DataverseWebApiService** should be used going forward per ADR-010 (DI Minimalism) and modern .NET best practices.

---

## Goals & Outcomes

### Primary Goals
1. Archive or delete `DataverseService.cs` (ServiceClient-based implementation)
2. Ensure all DI registrations point to `DataverseWebApiService`
3. Document the decision to use REST/Web API over ServiceClient
4. Remove any references to the old implementation
5. Clean up unused NuGet packages (if ServiceClient dependencies are removed)

### Success Criteria
- [ ] `DataverseService.cs` archived or deleted
- [ ] All DI registrations use `DataverseWebApiService`
- [ ] No references to `ServiceClient` remain in active code
- [ ] Documentation updated explaining Web API strategy
- [ ] NuGet packages cleaned up (optional)
- [ ] All tests updated to use Web API implementation

### Non-Goals
- Migrating existing data or state (implementations are functionally equivalent)
- Adding new Dataverse features (Sprint 4+)
- Performance optimization (Sprint 4+)

---

## Architecture & Design

### Current State (Sprint 2)
```
┌──────────────────────────┐
│   IDataverseService      │ ← Interface
└────────┬─────────────────┘
         │
         ├──────────────────────────────┐
         │                              │
         v                              v
┌─────────────────────┐      ┌──────────────────────────┐
│ DataverseService    │      │ DataverseWebApiService   │
│ (ServiceClient SDK) │      │ (REST/Web API)           │
│ - 461 lines         │      │ - 340 lines              │
│ - WCF dependencies  │      │ - HttpClient-based       │
│ - Synchronous       │      │ - Async-first            │
└─────────────────────┘      └──────────────────────────┘
         │                              │
         v                              v
┌─────────────────────┐      ┌──────────────────────────┐
│ ServiceClient       │      │ IHttpClientFactory       │
│ (Power Platform)    │      │ + DefaultAzureCredential │
└─────────────────────┘      └──────────────────────────┘
```

### Target State (Sprint 3)
```
┌──────────────────────────┐
│   IDataverseService      │ ← Interface
└────────┬─────────────────┘
         │
         v
┌──────────────────────────┐
│ DataverseWebApiService   │ ← Single implementation
│ (REST/Web API)           │
│ - HttpClient-based       │
│ - Async-first            │
│ - .NET 8.0 native        │
└────────┬─────────────────┘
         │
         v
┌──────────────────────────┐
│ IHttpClientFactory       │
│ + DefaultAzureCredential │
│ + Managed Identity       │
└──────────────────────────┘
```

---

## Relevant ADRs

### ADR-010: DI Minimalism
- **Prefer Web API over SDK**: Modern .NET HttpClient-based approach
- **IHttpClientFactory**: Proper HTTP client lifecycle management
- **Single Implementation**: One service per interface, no ambiguity

### ADR-002: No Heavy Plugins
- **Lightweight Dependencies**: Avoid heavy SDKs when REST API suffices
- **Explicit Over Magic**: Clear HTTP calls over abstracted SDK layers

---

## Implementation Steps

### Step 1: Verify Current DI Registration

**Check File**: Search for DI registrations in Program.cs or extension methods

**Expected Registration** (verify this exists):
```csharp
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>();
```

**Or**:
```csharp
builder.Services.AddScoped<IDataverseService, DataverseWebApiService>();
```

**Action**:
- Confirm no registration for `DataverseService` exists
- If found, remove it

---

### Step 2: Archive DataverseService.cs

**Option A: Delete** (if confident no references exist)
```bash
# Move to archive first (safer)
mkdir -p src/shared/Spaarke.Dataverse/_archive
mv src/shared/Spaarke.Dataverse/DataverseService.cs src/shared/Spaarke.Dataverse/_archive/
```

**Option B: Rename and Mark Obsolete** (transitional approach)
```csharp
// At top of DataverseService.cs
[Obsolete("Use DataverseWebApiService instead. This implementation will be removed in Sprint 4.")]
public class DataverseService : IDataverseService, IDisposable
{
    // ... existing code
}
```

**Recommendation**: Use Option A (archive) after validating no references remain.

---

### Step 3: Search for References to ServiceClient

**Search Commands**:
```bash
# Search for ServiceClient usage
rg "ServiceClient" --type cs

# Search for DataverseService instantiation
rg "new DataverseService" --type cs

# Search for DI registration
rg "AddScoped.*DataverseService|AddTransient.*DataverseService" --type cs
```

**Expected Results**: Should only find references in archived file or tests

**Action**:
- Remove any references found
- Update tests to use `DataverseWebApiService`

---

### Step 4: Clean Up NuGet Packages

**File**: `src/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj`

**Check for ServiceClient dependencies**:
```xml
<ItemGroup>
  <!-- If these exist and are only used by DataverseService, remove them -->
  <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="..." />
  <PackageReference Include="Microsoft.Xrm.Sdk" Version="..." />
  <PackageReference Include="Microsoft.Crm.Sdk.Messages" Version="..." />
</ItemGroup>
```

**Action**:
- If no other code uses these packages, remove them
- Build and verify no compilation errors

**Keep**:
```xml
<ItemGroup>
  <!-- These are used by DataverseWebApiService -->
  <PackageReference Include="Azure.Identity" Version="..." />
  <PackageReference Include="System.Net.Http.Json" Version="..." />
</ItemGroup>
```

---

### Step 5: Update Documentation

**New File**: `src/shared/Spaarke.Dataverse/README.md`

```markdown
# Spaarke.Dataverse - Dataverse Integration

## Implementation Strategy

This library uses the **Dataverse Web API (REST)** approach for all Dataverse operations.

### Why Web API Instead of ServiceClient SDK?

1. **Modern .NET Compatibility**: Web API uses HttpClient, fully compatible with .NET 8.0
2. **No WCF Dependencies**: Avoids legacy System.ServiceModel and WCF stack
3. **Async-First**: Native async/await support without SDK wrappers
4. **Lightweight**: Smaller dependency footprint
5. **Explicit Control**: Direct HTTP calls provide better debugging and control
6. **IHttpClientFactory**: Proper HTTP client lifecycle management

### Architecture

```
DataverseWebApiService
  └─> IHttpClientFactory
      └─> HttpClient (configured for Dataverse base URL)
          └─> DefaultAzureCredential (managed identity auth)
              └─> Dataverse REST API (OData v4)
```

### Authentication

Uses **User-Assigned Managed Identity** for production:
- No client secrets in configuration
- Automatic token refresh
- Per-request token acquisition with 5-minute refresh window

### API Structure

All operations use OData v4 conventions:
- **Create**: POST /api/data/v9.2/sprk_documents
- **Read**: GET /api/data/v9.2/sprk_documents(guid)
- **Update**: PATCH /api/data/v9.2/sprk_documents(guid)
- **Delete**: DELETE /api/data/v9.2/sprk_documents(guid)
- **Query**: GET /api/data/v9.2/sprk_documents?$filter=...&$select=...

### Configuration

Required settings:
```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com",
    "ClientId": "your-app-registration-id",
    "ClientSecret": "managed-via-keyvault",
    "TenantId": "your-tenant-id"
  }
}
```

### Usage

```csharp
// DI Registration (in Program.cs)
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>();

// Injection
public class MyService
{
    private readonly IDataverseService _dataverse;

    public MyService(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    public async Task CreateDocumentAsync()
    {
        var request = new CreateDocumentRequest
        {
            Name = "My Document",
            ContainerId = "container-guid"
        };

        var docId = await _dataverse.CreateDocumentAsync(request);
    }
}
```

### Migration from ServiceClient (Legacy)

If upgrading from `DataverseService` (ServiceClient-based):
1. No code changes required - interface is identical
2. Update DI registration to use `DataverseWebApiService`
3. Remove ServiceClient NuGet packages
4. Update configuration (no connection string needed)

### Error Handling

Web API returns standard HTTP status codes:
- **200 OK**: Success
- **201 Created**: Entity created
- **204 No Content**: Update/delete success
- **400 Bad Request**: Invalid request payload
- **401 Unauthorized**: Authentication failed
- **403 Forbidden**: Insufficient permissions
- **404 Not Found**: Entity does not exist
- **429 Too Many Requests**: Throttling (respect Retry-After header)
- **500 Internal Server Error**: Dataverse error

### Testing

Use integration tests against test Dataverse environment:
```csharp
[Fact]
public async Task CreateDocument_ValidRequest_ReturnsDocumentId()
{
    var service = new DataverseWebApiService(...);
    var request = new CreateDocumentRequest { Name = "Test" };

    var docId = await service.CreateDocumentAsync(request);

    Assert.False(string.IsNullOrEmpty(docId));
}
```

### Performance Considerations

1. **Token Caching**: Tokens cached for 5 minutes to reduce auth overhead
2. **Connection Pooling**: HttpClient reused via IHttpClientFactory
3. **Batch Operations**: Use OData $batch for multiple operations (future)
4. **Query Optimization**: Use $select to fetch only needed fields

### References

- [Dataverse Web API Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [OData v4 Specification](https://www.odata.org/documentation/)
- ADR-010: DI Minimalism
```

---

### Step 6: Update Tests

**Search for Tests**:
```bash
rg "DataverseService" --type cs tests/
```

**Update Test Registrations**:
```csharp
// Old
services.AddScoped<IDataverseService, DataverseService>();

// New
services.AddHttpClient<IDataverseService, DataverseWebApiService>();
```

**Mock HttpClient in Tests** (if needed):
```csharp
public class DataverseWebApiServiceTests
{
    [Fact]
    public async Task CreateDocument_ValidRequest_ReturnsId()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("https://test.crm.dynamics.com/api/data/v9.2/sprk_documents")
                .Respond("application/json", "{ \"sprk_documentid\": \"test-guid\" }");

        var httpClient = mockHttp.ToHttpClient();
        var service = new DataverseWebApiService(httpClient, mockConfig, mockLogger);

        // Act
        var docId = await service.CreateDocumentAsync(new CreateDocumentRequest { Name = "Test" });

        // Assert
        Assert.Equal("test-guid", docId);
    }
}
```

---

## AI Coding Prompts

### Prompt 1: Archive DataverseService and Verify References
```
Clean up duplicate Dataverse implementation:

Context:
- Two implementations exist: DataverseService.cs and DataverseWebApiService.cs
- Need to keep only DataverseWebApiService
- File to archive: C:\code_files\spaarke\src\shared\Spaarke.Dataverse\DataverseService.cs

Requirements:
1. Search entire codebase for references to DataverseService (not DataverseWebApiService)
2. List all files that reference the old implementation
3. Archive DataverseService.cs to _archive subdirectory
4. Update any DI registrations to use DataverseWebApiService
5. Remove ServiceClient NuGet package references if unused elsewhere

Code Quality:
- Ensure no references remain
- Update tests to use Web API implementation
- Document the change in git commit message

Search Commands:
- rg "DataverseService[^W]" --type cs (exclude DataverseWebApiService)
- rg "ServiceClient" --type cs
- rg "Microsoft.PowerPlatform.Dataverse.Client" --type xml
```

### Prompt 2: Create Dataverse Documentation
```
Create comprehensive README for Dataverse integration:

Context:
- Using REST/Web API approach (DataverseWebApiService)
- Need to document architecture, authentication, usage

Requirements:
1. Create src/shared/Spaarke.Dataverse/README.md
2. Explain why Web API over ServiceClient
3. Document architecture (HttpClient -> Managed Identity -> Dataverse)
4. Show configuration requirements
5. Provide usage examples
6. Document error handling (HTTP status codes)
7. Migration guide from ServiceClient
8. Link to relevant ADRs

Code Quality:
- Clear, concise documentation
- Code examples that compile
- Link to official Microsoft docs
- Follow markdown best practices
```

---

## Testing Strategy

### Validation Tests
1. **DI Resolution**:
   - Verify `IDataverseService` resolves to `DataverseWebApiService`
   - Ensure no ambiguous registrations

2. **Compilation**:
   - Build solution after removing DataverseService
   - Verify no missing references

3. **Integration Tests**:
   - Run existing Dataverse tests
   - All tests should use Web API implementation
   - No tests should reference ServiceClient

### Manual Verification
1. **Search Codebase**:
   - No references to `DataverseService` (old implementation)
   - No references to `ServiceClient`
   - No orphaned NuGet packages

2. **Documentation**:
   - README exists and is comprehensive
   - Architecture diagrams updated

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] `DataverseService.cs` archived or deleted
- [ ] No references to old implementation remain
- [ ] DI registrations use `DataverseWebApiService`
- [ ] ServiceClient NuGet packages removed (if unused)
- [ ] Tests updated to use Web API implementation
- [ ] All tests pass
- [ ] README.md created documenting Web API strategy
- [ ] Architecture diagrams updated
- [ ] Code review completed

---

## Completion Criteria

Task is complete when:
1. Only one Dataverse implementation exists (DataverseWebApiService)
2. All references updated
3. Documentation created
4. Tests pass
5. NuGet packages cleaned up
6. Code review approved

**Estimated Completion: 1-2 days**

---

## Rollback Plan

If issues arise:
1. Restore `DataverseService.cs` from archive
2. Re-add DI registration for old implementation
3. Restore NuGet packages if removed
4. Investigate and fix issues with Web API implementation

---

## Benefits of Consolidation

1. **Reduced Confusion**: Single implementation eliminates "which one to use?"
2. **Easier Maintenance**: One codebase to maintain
3. **Modern Stack**: Web API is .NET 8.0 native
4. **Better Performance**: HttpClient pooling via IHttpClientFactory
5. **Simpler Dependencies**: No WCF or heavy SDK dependencies
6. **Clearer Debugging**: HTTP traffic visible in logs/tools
