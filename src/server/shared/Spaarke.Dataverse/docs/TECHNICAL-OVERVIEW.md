# Spaarke.Dataverse - Technical Overview

**Purpose**: Dataverse integration for SharePoint Document Access Platform (SDAP)
**Architecture**: Dual Implementation (ServiceClient SDK + REST/Web API)
**Authentication**: ClientSecret Credential (Azure Identity)
**Status**: Production-Ready

---

## Overview

Spaarke.Dataverse provides a unified `IDataverseService` interface with two implementation choices:

1. **DataverseServiceClientImpl** ✅ **Production (Currently Used)**
   - Uses Microsoft.PowerPlatform.Dataverse.Client SDK
   - Registered as Singleton for connection reuse
   - Eliminates 500ms initialization overhead per request
   - Thread-safe with internal connection pooling

2. **DataverseWebApiService** ⚠️ **Alternative (Available)**
   - Uses Dataverse REST/OData API
   - HttpClient-based, lightweight dependencies
   - Modern .NET patterns

3. **DataverseAccessDataSource** ✅ **Production**
   - Queries user access permissions for authorization
   - Used by Spaarke.Core.Auth.AuthorizationService
   - Returns AccessSnapshot with AccessRights flags

---

## Architecture

### Current Production Setup

```
┌─────────────────────────────────────────────┐
│      Application Layer (BFF API)            │
│  - Document Endpoints                       │
│  - File Preview/Download Endpoints          │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Inject IDataverseService (Singleton)
┌─────────────────────────────────────────────┐
│    DataverseServiceClientImpl               │
│  - ServiceClient SDK wrapper                │
│  - Singleton lifetime (connection reuse)    │
│  - ~500ms initialization (one-time)         │
│  - Thread-safe, internal connection pool    │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Uses
┌─────────────────────────────────────────────┐
│   Microsoft.PowerPlatform.Dataverse.Client  │
│         ServiceClient (SDK)                 │
│  - SOAP/OData hybrid                        │
│  - Connection pooling                       │
│  - Automatic token management               │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Authenticates via
┌─────────────────────────────────────────────┐
│      ClientSecretCredential                 │
│  (Azure.Identity)                           │
│  - TENANT_ID, API_APP_ID, API_CLIENT_SECRET │
│  - Token caching (5 min)                    │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Calls
┌─────────────────────────────────────────────┐
│      Dataverse API                          │
│  https://your-env.crm.dynamics.com          │
└─────────────────────────────────────────────┘


┌─────────────────────────────────────────────┐
│    Spaarke.Core.Auth.AuthorizationService   │
│  (Authorization Engine)                     │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Queries user access
┌─────────────────────────────────────────────┐
│      DataverseAccessDataSource              │
│  - GetUserAccessAsync()                     │
│  - Returns AccessSnapshot                   │
│  - Implements IAccessDataSource             │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Uses ServiceClient or HttpClient
┌─────────────────────────────────────────────┐
│      Dataverse (User Access Queries)        │
│  - sprk_documentaccess table                │
│  - Team memberships                         │
│  - Permission grants/denies                 │
└─────────────────────────────────────────────┘
```

---

## Components

### 1. DataverseServiceClientImpl (Production)

**File**: `DataverseServiceClientImpl.cs`
**Status**: ✅ Currently used in production
**Lifetime**: Singleton (registered in [Program.cs:269-274](../../api/Spe.Bff.Api/Program.cs#L269))

**Purpose**: Primary Dataverse integration using ServiceClient SDK

**Key Characteristics**:
- **Initialization**: ~500ms first call, cached for application lifetime
- **Thread Safety**: ServiceClient is thread-safe, designed for long-lived use
- **Connection Pooling**: Internal connection pooling reduces overhead
- **Performance**: Singleton registration eliminates per-request initialization
- **Authentication**: ClientSecretCredential (same as Graph/SPE)

**Configuration Required**:
```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com"
  },
  "TENANT_ID": "your-tenant-id",
  "API_APP_ID": "your-app-registration-id",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=https://vault.../secrets/ClientSecret)"
}
```

**DI Registration** (Current Production):
```csharp
// Singleton lifetime for ServiceClient connection reuse
// Eliminates 500ms initialization overhead per request
builder.Services.AddSingleton<IDataverseService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>();
    return new DataverseServiceClientImpl(configuration, logger);
});
```

**When to Use**:
- ✅ Production environments (proven stability)
- ✅ When performance is critical (Singleton eliminates overhead)
- ✅ When using SDK-specific features
- ✅ Current default choice

---

### 2. DataverseWebApiService (Alternative)

**File**: `DataverseWebApiService.cs`
**Status**: ⚠️ Available but not currently used
**Lifetime**: Would be Scoped (with HttpClient)

**Purpose**: Alternative Dataverse integration using REST/OData API

**Key Characteristics**:
- **Dependencies**: HttpClient only (lightweight)
- **Debugging**: HTTP traffic visible (easier troubleshooting)
- **Modern**: Native .NET 8.0 async/await patterns
- **Package Size**: Smaller dependency footprint

**Configuration Required**:
```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com",
    "ClientId": "your-app-registration-id",
    "ClientSecret": "@Microsoft.KeyVault(...)",
    "TenantId": "your-tenant-id"
  }
}
```

**DI Registration** (Alternative, not currently used):
```csharp
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>(client =>
{
    var dataverseUrl = builder.Configuration["Dataverse:ServiceUrl"];
    client.BaseAddress = new Uri(dataverseUrl!);
    client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
    client.DefaultRequestHeaders.Add("OData-Version", "4.0");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("Prefer", "return=representation");
});
```

**When to Use**:
- ⚠️ If debugging requires HTTP traffic visibility
- ⚠️ If minimizing package dependencies is critical
- ⚠️ If migrating away from SDK dependencies

**See**: [TECHNICAL-OVERVIEW-WEB-API.md](./TECHNICAL-OVERVIEW-WEB-API.md) for detailed Web API documentation

---

### 3. DataverseAccessDataSource

**File**: `DataverseAccessDataSource.cs`
**Status**: ✅ Production (used by authorization system)
**Purpose**: Query user access permissions and team memberships

**Integration**: Implements `IAccessDataSource` interface used by [Spaarke.Core.Auth.AuthorizationService](../../shared/Spaarke.Core/docs/TECHNICAL-OVERVIEW.md)

**Key Features**:
- Queries Dataverse for user's `AccessRights` (Read/Write/Delete/Create/Append/AppendTo/Share)
- Returns `AccessSnapshot` with permissions and team memberships
- **Fail-Closed Security**: Returns `AccessRights.None` on errors
- Used in authorization decisions for every document operation

**Flow**:
```csharp
// 1. Authorization service needs user permissions
var snapshot = await _accessDataSource.GetUserAccessAsync(userId, documentId);

// 2. DataverseAccessDataSource queries Dataverse
// - sprk_documentaccess table (user-specific permissions)
// - Team memberships
// - Explicit grants/denies

// 3. Returns AccessSnapshot
public class AccessSnapshot
{
    public AccessRights AccessRights { get; init; }  // Read, Write, Delete, etc.
    public IEnumerable<string> TeamMemberships { get; init; }
    public IEnumerable<string> ExplicitGrants { get; init; }
    public IEnumerable<string> ExplicitDenies { get; init; }
}
```

**Security Design**:
- Fail-closed: On query errors, returns `AccessRights.None` (denies access)
- Logged warnings on failures
- No exceptions thrown (graceful degradation)

---

### 4. IDataverseService (Interface)

**File**: `IDataverseService.cs`
**Purpose**: Abstraction for all Dataverse operations
**Implementations**: DataverseServiceClientImpl, DataverseWebApiService

**Operations** (16 methods):

```csharp
public interface IDataverseService
{
    // Document CRUD
    Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default);
    Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default);

    // Access Control
    Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default);

    // Health Checks
    Task<bool> TestConnectionAsync();
    Task<bool> TestDocumentOperationsAsync();

    // Metadata Operations (Phase 7)
    Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default);
    Task<LookupNavigationMetadata> GetLookupNavigationAsync(
        string childEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default);
    Task<string> GetCollectionNavigationAsync(
        string parentEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default);
}
```

**Design Benefits**:
- Switching implementations requires only DI registration change
- Consuming code unchanged (interface abstraction)
- Both implementations guarantee identical behavior

---

## ServiceClient vs Web API Comparison

| Aspect | ServiceClient SDK ✅ **Current** | Web API ⚠️ **Alternative** |
|--------|----------------------------------|---------------------------|
| **Dependencies** | System.ServiceModel (WCF), Dataverse SDK (~10 MB) | HttpClient only (< 1 MB) |
| **.NET Compatibility** | .NET 8.0 supported | Native .NET 8.0 |
| **Initialization** | ~500ms first call, 0ms cached (Singleton) | Negligible (per-request with HttpClient) |
| **Performance** | Excellent with Singleton (connection reuse) | Good (IHttpClientFactory pooling) |
| **Thread Safety** | Thread-safe (designed for long-lived use) | Thread-safe (HttpClient) |
| **Connection Pooling** | Internal SDK pooling | IHttpClientFactory pooling |
| **Debugging** | SDK abstraction hides details | HTTP traffic visible (Fiddler, etc.) |
| **Package Size** | Heavy (10+ MB dependencies) | Lightweight (< 1 MB) |
| **Stability** | Production-proven in SDAP | Alternative option |
| **API Coverage** | Full SDK features | OData v4 operations |
| **Lifetime** | Singleton (one instance for app) | Scoped (per-request with HttpClient) |
| **ADR Alignment** | ADR-010 (Singleton for expensive resources) | ADR-010 (DI minimalism) |

---

## Configuration

### Production Configuration (Current)

**appsettings.json**:
```json
{
  "Dataverse": {
    "ServiceUrl": "https://org12345.crm.dynamics.com"
  },
  "TENANT_ID": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "API_APP_ID": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/DataverseClientSecret)"
}
```

**Azure Configuration** (Key Vault Reference):
```bash
# App Service Configuration
az webapp config appsettings set \
  --name spe-api-dev \
  --resource-group spaarke-rg \
  --settings \
    "Dataverse__ServiceUrl=https://org12345.crm.dynamics.com" \
    "TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
    "API_APP_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
    "API_CLIENT_SECRET=@Microsoft.KeyVault(SecretUri=https://vault.../secrets/ClientSecret)"
```

### Alternative Configuration (Web API)

If switching to Web API implementation:

```json
{
  "Dataverse": {
    "ServiceUrl": "https://org12345.crm.dynamics.com",
    "ClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "ClientSecret": "@Microsoft.KeyVault(...)",
    "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
  }
}
```

---

## Usage Examples

### Document Operations

```csharp
public class DocumentService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IDataverseService dataverseService,  // Singleton instance injected
        ILogger<DocumentService> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public async Task<string> CreateDocumentAsync(string name, string containerId)
    {
        var request = new CreateDocumentRequest
        {
            Name = name,
            ContainerId = containerId,
            Description = "Created via SDAP",
            GraphDriveId = "drive-guid",
            GraphItemId = "item-guid"
        };

        var documentId = await _dataverseService.CreateDocumentAsync(request);
        _logger.LogInformation("Created document {DocumentId} in container {ContainerId}",
            documentId, containerId);

        return documentId;
    }

    public async Task<DocumentEntity?> GetDocumentAsync(string id)
    {
        var document = await _dataverseService.GetDocumentAsync(id);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found", id);
            return null;
        }

        return document;
    }

    public async Task UpdateDocumentMetadataAsync(string id, string newDescription)
    {
        await _dataverseService.UpdateDocumentAsync(id, new UpdateDocumentRequest
        {
            Description = newDescription
        });

        _logger.LogInformation("Updated document {DocumentId} description", id);
    }

    public async Task DeleteDocumentAsync(string id)
    {
        await _dataverseService.DeleteDocumentAsync(id);
        _logger.LogInformation("Deleted document {DocumentId}", id);
    }
}
```

### Access Control Integration

```csharp
public class AuthorizationService
{
    private readonly IAccessDataSource _accessDataSource;

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizationContext context)
    {
        // Query user access from Dataverse
        var snapshot = await _accessDataSource.GetUserAccessAsync(
            context.UserId,
            context.ResourceId
        );

        // snapshot.AccessRights contains: Read, Write, Delete, Create, etc.
        // Use OperationAccessPolicy to check required permissions

        if (HasRequiredPermissions(snapshot.AccessRights, context.Operation))
        {
            return new AuthorizationResult { IsAllowed = true };
        }

        return new AuthorizationResult { IsAllowed = false };
    }
}
```

---

## Performance Considerations

### 1. Singleton ServiceClient Performance

**Metrics** (Production):
- **First Request**: ~500ms initialization (ServiceClient connection setup)
- **Subsequent Requests**: < 10ms overhead (connection reused)
- **Cache Duration**: Application lifetime (Singleton)
- **Thread Safety**: Fully thread-safe, designed for concurrent requests

**Before Singleton** (Scoped lifetime):
```
Request 1: 500ms (init) + 50ms (query) = 550ms total
Request 2: 500ms (init) + 50ms (query) = 550ms total  ← Wasteful reinitialization
Request 3: 500ms (init) + 50ms (query) = 550ms total
```

**After Singleton** (Current):
```
Request 1: 500ms (init) + 50ms (query) = 550ms total
Request 2: 0ms (cached) + 50ms (query) = 50ms total   ← 10x faster
Request 3: 0ms (cached) + 50ms (query) = 50ms total
```

**Result**: 90% latency reduction on all requests after the first

### 2. Token Caching

- Tokens cached by ClientSecretCredential for ~5 minutes
- Automatic refresh before expiration
- Minimal auth overhead per request

### 3. Connection Pooling

- ServiceClient maintains internal connection pool
- Reuses connections across requests
- Automatic handling of transient failures

---

## Testing

### Unit Tests (Mock Interface)

```csharp
public class DocumentServiceTests
{
    [Fact]
    public async Task CreateDocument_ValidRequest_ReturnsDocumentId()
    {
        // Arrange
        var mockDataverseService = new Mock<IDataverseService>();
        mockDataverseService
            .Setup(x => x.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("doc-guid-123");

        var service = new DocumentService(mockDataverseService.Object, Mock.Of<ILogger<DocumentService>>());

        // Act
        var result = await service.CreateDocumentAsync("Test.pdf", "container-guid");

        // Assert
        Assert.Equal("doc-guid-123", result);
        mockDataverseService.Verify(x => x.CreateDocumentAsync(
            It.Is<CreateDocumentRequest>(r => r.Name == "Test.pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

### Integration Tests (Against Test Dataverse)

```csharp
[Collection("Dataverse Integration")]
public class DataverseServiceClientImplIntegrationTests : IAsyncLifetime
{
    private IDataverseService _service;
    private string _testContainerId;
    private List<string> _createdDocumentIds = new();

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json")
            .Build();

        var logger = new Mock<ILogger<DataverseServiceClientImpl>>();
        _service = new DataverseServiceClientImpl(config.Object, logger.Object);

        // Verify connection
        var isConnected = await _service.TestConnectionAsync();
        Assert.True(isConnected, "Cannot connect to test Dataverse environment");
    }

    [Fact]
    public async Task CreateAndRetrieveDocument_Success()
    {
        // Arrange
        var request = new CreateDocumentRequest
        {
            Name = $"Test-{Guid.NewGuid()}.txt",
            ContainerId = _testContainerId,
            Description = "Integration test document"
        };

        // Act - Create
        var docId = await _service.CreateDocumentAsync(request);
        _createdDocumentIds.Add(docId);

        // Act - Retrieve
        var document = await _service.GetDocumentAsync(docId);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(request.Name, document.Name);
        Assert.Equal(request.Description, document.Description);
    }

    public async Task DisposeAsync()
    {
        // Cleanup: Delete all created documents
        foreach (var docId in _createdDocumentIds)
        {
            await _service.DeleteDocumentAsync(docId);
        }
    }
}
```

---

## Switching Between Implementations

Both implementations share `IDataverseService`, making switching straightforward.

### Switch to Web API (From ServiceClient)

**Step 1**: Update DI Registration in [Program.cs](../../api/Spe.Bff.Api/Program.cs)
```csharp
// Replace this:
// builder.Services.AddSingleton<IDataverseService>(sp =>
//     new DataverseServiceClientImpl(configuration, logger));

// With this:
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>(client =>
{
    var dataverseUrl = builder.Configuration["Dataverse:ServiceUrl"];
    client.BaseAddress = new Uri(dataverseUrl!);
    client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
    client.DefaultRequestHeaders.Add("OData-Version", "4.0");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("Prefer", "return=representation");
});
```

**Step 2**: Update Configuration
```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com",
    "ClientId": "your-app-registration-id",
    "ClientSecret": "@Microsoft.KeyVault(...)",
    "TenantId": "your-tenant-id"
  }
}
```

**Step 3**: Optional - Remove ServiceClient Package
```xml
<!-- Can remove from .csproj if not using plugins -->
<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
```

**Important**: No code changes required in consuming services!

### Switch to ServiceClient (From Web API)

**Step 1**: Update DI Registration
```csharp
// Replace HttpClient registration with Singleton
builder.Services.AddSingleton<IDataverseService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>();
    return new DataverseServiceClientImpl(configuration, logger);
});
```

**Step 2**: Update Configuration
```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com"
  },
  "TENANT_ID": "your-tenant-id",
  "API_APP_ID": "your-app-registration-id",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(...)"
}
```

**Step 3**: Add ServiceClient Package
```xml
<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.1.32" />
```

---

## Troubleshooting

### Issue: "Unauthorized" (401) on ServiceClient

**Cause**: Invalid client secret or app registration

**Solution**:
1. Verify `API_APP_ID` matches app registration in Azure AD
2. Check `API_CLIENT_SECRET` is valid and not expired
3. Verify app registration has API permissions for Dataverse
4. Check `TENANT_ID` is correct

**Diagnostic**:
```bash
# Test token acquisition
az account get-access-token --resource=https://your-env.crm.dynamics.com
```

### Issue: "Forbidden" (403) on Specific Operations

**Cause**: Application user in Dataverse lacks security role privileges

**Solution**:
1. Open Dataverse environment > Settings > Security > Security Roles
2. Find security role assigned to application user
3. Ensure role has appropriate privileges on entities:
   - `sprk_document`: Create, Read, Write, Delete
   - `sprk_container`: Create, Read, Write, Delete
   - `sprk_documentaccess`: Read (for authorization)

### Issue: Slow First Request (~500ms)

**Cause**: ServiceClient initialization (expected behavior)

**Solution**: This is normal with Singleton registration
- First request: ~500ms (one-time initialization)
- All subsequent requests: < 10ms overhead
- Connection is cached for application lifetime

**Verify Singleton Registration**:
```csharp
// Correct (Singleton):
builder.Services.AddSingleton<IDataverseService>(...)

// Incorrect (Scoped) - Would reinitialize every request:
builder.Services.AddScoped<IDataverseService>(...)
```

### Issue: DataverseAccessDataSource Returns AccessRights.None

**Cause**: Query error or missing access data in Dataverse

**Solution**:
1. Check application logs for warnings from DataverseAccessDataSource
2. Verify `sprk_documentaccess` table has records for the user/document
3. Check user exists in Dataverse
4. Verify query permissions on `sprk_documentaccess` table

**Expected Behavior**: Fail-closed (returns None on errors for security)

---

## Security Best Practices

### 1. Never Store Secrets in Code

✅ **Good**:
```json
{
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=https://vault.../secrets/Secret)"
}
```

❌ **Bad**:
```json
{
  "API_CLIENT_SECRET": "actual-secret-value-here"  // Never do this!
}
```

### 2. Principle of Least Privilege

- Grant minimal Dataverse security roles needed
- Use custom security roles (not System Administrator)
- Separate app registrations for dev/test/prod

### 3. Audit Logging

- Enable Dataverse audit logs for sensitive operations
- Log all CRUD operations in application logs
- Use correlation IDs for request tracing

### 4. Fail-Closed Security

- DataverseAccessDataSource returns `AccessRights.None` on errors
- Authorization service denies access on exceptions
- No graceful fallback to permissive access

---

## ADR Alignment

### ADR-010: DI Minimalism

**Decision**: Use Singleton lifetime for expensive resources like ServiceClient

**Rationale**:
- ServiceClient initialization costs ~500ms
- Thread-safe design supports long-lived instances
- Connection pooling requires persistent connections
- Singleton eliminates per-request overhead

**Implementation**: [Program.cs:269-274](../../api/Spe.Bff.Api/Program.cs#L269)

### ADR-008: Authorization & Security

**Decision**: Resource-level access control via DataverseAccessDataSource

**Implementation**:
- DataverseAccessDataSource queries user permissions from Dataverse
- Returns AccessSnapshot consumed by Spaarke.Core.Auth
- Fail-closed on errors (AccessRights.None)

**Integration**: [Spaarke.Core Authorization](../Spaarke.Core/docs/TECHNICAL-OVERVIEW.md)

---

## References

- **Dataverse ServiceClient**: [Microsoft Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/build-windows-client-applications-xrm-tools)
- **Dataverse Web API**: [Microsoft Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- **Azure Identity (ClientSecretCredential)**: [Microsoft Documentation](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.clientsecretcredential)
- **ADR-010**: [DI Minimalism](../../../../docs/adr/ADR-010-di-minimalism.md)
- **ADR-008**: [Authorization & Security](../../../../docs/adr/ADR-008-authorization-security.md)
- **Spaarke.Core**: [Authorization System](../Spaarke.Core/docs/TECHNICAL-OVERVIEW.md)

---

## Dependencies

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Azure.Core" Version="1.40.0" />
    <PackageReference Include="Azure.Identity" Version="1.12.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.1.32" />
  </ItemGroup>
</Project>
```

**Notes**:
- `Microsoft.PowerPlatform.Dataverse.Client` required for ServiceClient implementation
- Includes System.ServiceModel (WCF) dependencies (~10 MB)
- Web API implementation uses only Azure.Core, Azure.Identity, HttpClient

---

## File Structure

```
Spaarke.Dataverse/
├── DataverseServiceClientImpl.cs       ✅ Production (Singleton, ServiceClient SDK)
├── DataverseWebApiService.cs           ⚠️ Alternative (REST/OData, not used)
├── DataverseWebApiClient.cs            ⚠️ Helper for Web API (not used)
├── DataverseAccessDataSource.cs        ✅ Authorization queries (used by Spaarke.Core)
├── IAccessDataSource.cs                ✅ Interface for access queries
├── IDataverseService.cs                ✅ Shared interface (16 methods)
├── Models.cs                           ✅ DTOs and entities
├── Spaarke.Dataverse.csproj
├── docs/
│   ├── TECHNICAL-OVERVIEW.md           ← This file
│   └── TECHNICAL-OVERVIEW-WEB-API.md   ⚠️ Web API focused documentation (historical)
└── README.md                           ← Brief overview, points to docs/
```

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2025-12-03 | Documentation restructure | Accurate technical overview created |
| 2025-11-25 | ServiceClient registered as Singleton | Performance optimization - eliminates 500ms overhead |
| 2025-09-30 | Added DataverseAccessDataSource | Granular authorization support (Sprint 3 Task 1.1) |
| 2025-09-26 | Added DataverseWebApiService | Alternative REST/OData implementation |
| 2025-09-20 | Initial DataverseServiceClientImpl | Production Dataverse integration using ServiceClient SDK |

---

**Last Updated**: 2025-12-03
**Maintainers**: Spaarke Engineering Team
