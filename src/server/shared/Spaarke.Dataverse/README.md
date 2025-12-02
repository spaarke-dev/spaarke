# Spaarke.Dataverse - Dataverse Integration

**Architecture**: REST/Web API Approach
**Authentication**: Azure Managed Identity
**Status**: Production-Ready

---

## Implementation Strategy

This library uses the **Dataverse Web API (REST)** approach for all Dataverse operations, as specified in **ADR-010: DI Minimalism**.

### Why Web API Instead of ServiceClient SDK?

| Aspect | Web API (✅ Our Choice) | ServiceClient SDK (❌ Legacy) |
|--------|------------------------|-------------------------------|
| **Dependencies** | HttpClient only | System.ServiceModel (WCF) |
| **.NET Compatibility** | Native .NET 8.0 | .NET Framework heritage |
| **Async Support** | Native async/await | Wrapper-based async |
| **Performance** | IHttpClientFactory pooling | Heavy SDK overhead |
| **Debugging** | HTTP traffic visible | SDK abstraction hides details |
| **Package Size** | Lightweight (< 1 MB) | Heavy SDK (10+ MB) |
| **Maintenance** | Direct API control | SDK versioning dependencies |

**Key Decision**: Modern .NET applications should use Web API for Dataverse integration unless specific SDK features (e.g., plugins, workflow assemblies) are required.

---

## Architecture

```
┌─────────────────────────────────────────────┐
│         Application Layer                   │
│  (Controllers, Services, Background Jobs)   │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Inject IDataverseService
┌─────────────────────────────────────────────┐
│      DataverseWebApiService                 │
│  - Create/Read/Update/Delete Documents      │
│  - Query with OData filters                 │
│  - Batch operations support                 │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Uses
┌─────────────────────────────────────────────┐
│       DataverseWebApiClient                 │
│  - OData query building                     │
│  - HTTP request construction                │
│  - Response deserialization                 │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Configured with
┌─────────────────────────────────────────────┐
│        IHttpClientFactory                   │
│  - Connection pooling                       │
│  - Automatic token injection                │
│  - Base URL configuration                   │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Authenticates via
┌─────────────────────────────────────────────┐
│      DefaultAzureCredential                 │
│  - User-Assigned Managed Identity (prod)    │
│  - Visual Studio (local dev)                │
│  - Azure CLI (local dev)                    │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Calls
┌─────────────────────────────────────────────┐
│      Dataverse REST API (OData v4)          │
│  https://your-env.crm.dynamics.com          │
│  /api/data/v9.2/sprk_documents              │
└─────────────────────────────────────────────┘
```

---

## Authentication

### Production (Azure App Service)
Uses **User-Assigned Managed Identity**:
- No client secrets stored in configuration
- Automatic token acquisition and refresh
- Per-request token with 5-minute refresh window
- Scoped to `https://your-env.crm.dynamics.com/.default`

**Configuration**:
```json
{
  "ManagedIdentity": {
    "ClientId": "guid-of-user-assigned-identity"
  },
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com"
  }
}
```

### Local Development
Uses **DefaultAzureCredential** chain:
1. Visual Studio Azure Service Authentication
2. Azure CLI (`az login`)
3. Environment variables (fallback)

**Setup**:
```bash
# Login with Azure CLI
az login

# Or use Visual Studio: Tools > Options > Azure Service Authentication
```

---

## API Structure

All operations use **OData v4** conventions:

### Document Operations

| Operation | HTTP Method | Endpoint | Body |
|-----------|-------------|----------|------|
| **Create** | POST | `/api/data/v9.2/sprk_documents` | JSON entity |
| **Read** | GET | `/api/data/v9.2/sprk_documents(guid)` | N/A |
| **Update** | PATCH | `/api/data/v9.2/sprk_documents(guid)` | JSON partial |
| **Delete** | DELETE | `/api/data/v9.2/sprk_documents(guid)` | N/A |
| **Query** | GET | `/api/data/v9.2/sprk_documents?$filter=...` | N/A |

### Container Operations

| Operation | Endpoint |
|-----------|----------|
| **Get Container** | `/api/data/v9.2/sprk_containers(guid)` |
| **Query Containers** | `/api/data/v9.2/sprk_containers?$filter=...` |

### OData Query Parameters

```http
GET /api/data/v9.2/sprk_documents?
  $filter=sprk_name eq 'MyDoc'&
  $select=sprk_documentid,sprk_name,sprk_description&
  $orderby=createdon desc&
  $top=50&
  $skip=0
```

---

## Configuration

### Required Settings

```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-env.crm.dynamics.com",
    "ClientId": "your-app-registration-id",
    "ClientSecret": "managed-via-keyvault-reference",
    "TenantId": "your-tenant-id"
  },
  "ManagedIdentity": {
    "ClientId": "guid-of-user-assigned-identity"
  }
}
```

### Key Vault Integration (Production)

```json
{
  "Dataverse": {
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/DataverseClientSecret)"
  }
}
```

---

## Usage

### DI Registration (in Program.cs)

```csharp
// Register Dataverse service with HttpClient
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

### Service Injection

```csharp
public class DocumentService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IDataverseService dataverseService,
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
            Description = "Created via Web API"
        };

        var documentId = await _dataverseService.CreateDocumentAsync(request);
        _logger.LogInformation("Created document {DocumentId}", documentId);

        return documentId;
    }
}
```

### Example Operations

```csharp
// Create a document
var docId = await _dataverseService.CreateDocumentAsync(new CreateDocumentRequest
{
    Name = "Q4 Report.pdf",
    ContainerId = containerGuid,
    Description = "Quarterly financial report"
});

// Get a document
var document = await _dataverseService.GetDocumentAsync(docId);

// Update a document
await _dataverseService.UpdateDocumentAsync(docId, new UpdateDocumentRequest
{
    Description = "Updated description"
});

// Delete a document
await _dataverseService.DeleteDocumentAsync(docId);

// Query documents by container
var documents = await _dataverseService.GetDocumentsByContainerAsync(containerGuid);
```

---

## Error Handling

### HTTP Status Codes

| Code | Meaning | Action |
|------|---------|--------|
| **200 OK** | Success | Process response |
| **201 Created** | Entity created | Extract entity ID from response |
| **204 No Content** | Update/delete success | No response body |
| **400 Bad Request** | Invalid payload | Check request format |
| **401 Unauthorized** | Auth failed | Verify managed identity permissions |
| **403 Forbidden** | Insufficient permissions | Check Dataverse security roles |
| **404 Not Found** | Entity doesn't exist | Handle as null/empty |
| **429 Too Many Requests** | Throttling | Respect `Retry-After` header |
| **500 Internal Server Error** | Dataverse error | Log and retry with backoff |

### Exception Handling Pattern

```csharp
try
{
    var document = await _dataverseService.GetDocumentAsync(docId);
    if (document == null)
    {
        _logger.LogWarning("Document {DocumentId} not found", docId);
        return Results.NotFound();
    }
    return Results.Ok(document);
}
catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
{
    _logger.LogWarning("Access denied to document {DocumentId}", docId);
    return Results.Forbid();
}
catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
{
    _logger.LogWarning("Dataverse throttling detected, retry after {RetryAfter}s",
        ex.Headers?.RetryAfter?.Delta?.TotalSeconds ?? 60);
    return Results.StatusCode(503); // Service Unavailable - client should retry
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to retrieve document {DocumentId}", docId);
    return Results.Problem("Failed to retrieve document");
}
```

---

## Testing

### Integration Tests (Recommended)

Test against a **dedicated test Dataverse environment**:

```csharp
public class DataverseWebApiServiceIntegrationTests : IAsyncLifetime
{
    private IDataverseService _service;
    private string _testDocumentId;

    public async Task InitializeAsync()
    {
        // Setup test service with test environment configuration
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json")
            .Build();

        var httpClient = new HttpClient();
        _service = new DataverseWebApiService(httpClient, config, logger);
    }

    [Fact]
    public async Task CreateDocument_ValidRequest_ReturnsDocumentId()
    {
        // Arrange
        var request = new CreateDocumentRequest
        {
            Name = $"Test Document {Guid.NewGuid()}",
            ContainerId = TestContainerId
        };

        // Act
        var docId = await _service.CreateDocumentAsync(request);

        // Assert
        Assert.False(string.IsNullOrEmpty(docId));
        _testDocumentId = docId; // Track for cleanup
    }

    public async Task DisposeAsync()
    {
        // Cleanup: Delete test documents
        if (!string.IsNullOrEmpty(_testDocumentId))
        {
            await _service.DeleteDocumentAsync(_testDocumentId);
        }
    }
}
```

### Unit Tests (Mock HttpClient)

Use `MockHttpMessageHandler` to test without hitting Dataverse:

```csharp
public class DataverseWebApiServiceTests
{
    [Fact]
    public async Task GetDocument_ExistingDocument_ReturnsDocument()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("https://test.crm.dynamics.com/api/data/v9.2/sprk_documents(*)")
                .Respond("application/json", JsonSerializer.Serialize(new
                {
                    sprk_documentid = "test-guid",
                    sprk_name = "Test Doc",
                    sprk_description = "Test Description"
                }));

        var httpClient = mockHttp.ToHttpClient();
        var service = new DataverseWebApiService(httpClient, mockConfig, mockLogger);

        // Act
        var document = await service.GetDocumentAsync("test-guid");

        // Assert
        Assert.NotNull(document);
        Assert.Equal("Test Doc", document.Name);
    }
}
```

---

## Performance Considerations

### 1. Token Caching
- Tokens cached by `DefaultAzureCredential` for 5 minutes
- Automatic refresh before expiration
- Minimal auth overhead per request

### 2. Connection Pooling
- `IHttpClientFactory` manages connection lifecycle
- Reuses connections across requests
- Automatic handling of DNS changes

### 3. Query Optimization
Use `$select` to fetch only needed fields:
```http
# Bad: Fetches all fields
GET /api/data/v9.2/sprk_documents(guid)

# Good: Fetches only needed fields
GET /api/data/v9.2/sprk_documents(guid)?$select=sprk_name,sprk_description
```

### 4. Batch Operations (Future Enhancement)
```http
POST /api/data/v9.2/$batch
Content-Type: multipart/mixed; boundary=batch_requests

--batch_requests
Content-Type: application/http

GET /api/data/v9.2/sprk_documents(guid1)

--batch_requests
Content-Type: application/http

GET /api/data/v9.2/sprk_documents(guid2)

--batch_requests--
```

### 5. Pagination for Large Datasets
```csharp
public async Task<List<DocumentDto>> GetAllDocumentsAsync(string containerId)
{
    var allDocs = new List<DocumentDto>();
    var skipToken = 0;
    const int pageSize = 50;

    while (true)
    {
        var url = $"/api/data/v9.2/sprk_documents?$filter=_sprk_container_value eq {containerId}&$top={pageSize}&$skip={skipToken}";
        var page = await FetchPageAsync(url);

        if (page.Count == 0) break;

        allDocs.AddRange(page);
        skipToken += pageSize;
    }

    return allDocs;
}
```

---

## Migration from ServiceClient (Legacy)

If upgrading from the old `DataverseService` (ServiceClient-based implementation):

### Step 1: Update DI Registration
```csharp
// Old (❌ Remove)
builder.Services.AddScoped<IDataverseService, DataverseService>();

// New (✅ Use this)
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>();
```

### Step 2: Remove ServiceClient NuGet Packages
```xml
<!-- Remove these from .csproj -->
<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
<PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" />
<PackageReference Include="System.ServiceModel.Primitives" />
<PackageReference Include="System.ServiceModel.Http" />
<PackageReference Include="System.ServiceModel.Security" />
```

### Step 3: Update Configuration
```json
{
  "Dataverse": {
    // Old: ConnectionString (❌ Remove)
    // "ConnectionString": "AuthType=ClientSecret;url=...",

    // New: Individual settings (✅ Use these)
    "ServiceUrl": "https://your-env.crm.dynamics.com",
    "ClientId": "app-registration-guid",
    "ClientSecret": "@Microsoft.KeyVault(...)",
    "TenantId": "tenant-guid"
  }
}
```

### Step 4: No Code Changes Required
The `IDataverseService` interface remains identical - no consuming code needs to change!

---

## Troubleshooting

### Issue: "Unauthorized" (401) in Production
**Cause**: Managed identity not configured or missing Dataverse permissions

**Solution**:
1. Verify managed identity is assigned to App Service
2. Check managed identity has "System User" role in Dataverse
3. Verify `ManagedIdentity:ClientId` in configuration

### Issue: "Forbidden" (403) on Specific Operations
**Cause**: Security role lacks necessary privileges

**Solution**:
1. Open Dataverse environment > Settings > Security > Security Roles
2. Find role assigned to managed identity/app registration
3. Ensure role has Create/Read/Write/Delete on `sprk_document` entity

### Issue: Throttling (429) Errors
**Cause**: Exceeding Dataverse API limits

**Solution**:
1. Implement exponential backoff using `Retry-After` header
2. Use batch operations for bulk updates
3. Cache frequently-accessed data
4. Consider Dataverse API limits: 6000 requests per 5 minutes per user

### Issue: Local Development Authentication Fails
**Cause**: Not logged in to Azure via CLI or Visual Studio

**Solution**:
```bash
# Login with Azure CLI
az login --tenant your-tenant-id

# Or: Tools > Options > Azure Service Authentication in Visual Studio
```

---

## Security Best Practices

1. **Never Store Secrets in Code**
   - Use Azure Key Vault references in configuration
   - Use managed identity in production

2. **Principle of Least Privilege**
   - Grant minimal Dataverse security roles needed
   - Separate managed identities for dev/test/prod

3. **Audit Logging**
   - Enable Dataverse audit logs for sensitive operations
   - Log all CRUD operations in application logs

4. **Data Validation**
   - Validate all inputs before sending to Dataverse
   - Sanitize user input to prevent injection attacks

---

## References

- [Dataverse Web API Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [OData v4 Specification](https://www.odata.org/documentation/)
- [Azure Managed Identity](https://learn.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview)
- **ADR-010: DI Minimalism** - Architecture decision explaining Web API preference
- **ADR-002: No Heavy Plugins** - Avoiding unnecessary SDK dependencies

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2025-10-01 | Removed `DataverseService` (ServiceClient) | Sprint 3 Task 2.2 - Consolidation to Web API only |
| 2025-09-30 | Added `DataverseAccessDataSource` | Sprint 3 Task 1.1 - Granular authorization support |
| 2025-09-26 | Initial `DataverseWebApiService` | Modern .NET 8.0 compatible implementation |
