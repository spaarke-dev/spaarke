# SDAP BFF API Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (BFF API section)
> **Last Updated**: December 3, 2025
> **Applies To**: Backend API development, endpoint changes, service layer

---

## TL;DR

ASP.NET Core 8.0 Minimal APIs. Key services: `GraphClientFactory` (OBO token exchange), `UploadSessionManager` (large file chunking), `IDataverseService` (metadata queries). Redis caching with 15-min TTL. Hosted at `spe-api-dev-67e2xz.azurewebsites.net`.

---

## Applies When

- Adding new API endpoints
- Modifying upload logic
- Understanding Graph/Dataverse integration
- Debugging server-side issues
- Working with caching layer

---

## API Endpoints

```csharp
// File Upload
POST /upload/file              // Single file (<4MB)
POST /upload/session           // Large file upload session

// Navigation Metadata (Phase 7)
GET /api/navmap/{childEntity}/{relationship}/lookup      // Lookup property
GET /api/navmap/{childEntity}/{relationship}/collection  // Collection property

// File Access (Phase 8)
GET /api/documents/{id}/preview-url   // SharePoint preview URL
GET /api/documents/{id}/office        // Office Online editor URL

// Health
GET /healthz                   // Returns "Healthy"
```

---

## Key Services

### GraphClientFactory (OBO Token Exchange)

**Purpose**: Exchange user token for Graph API token while preserving user identity.

```csharp
// GraphClientFactory.cs
public class GraphClientFactory : IGraphClientFactory
{
    private readonly IConfidentialClientApplication _confidentialClient;
    
    public async Task<GraphServiceClient> CreateClientAsync(string userAccessToken)
    {
        // On-Behalf-Of token exchange
        var result = await _confidentialClient.AcquireTokenOnBehalfOf(
            scopes: new[] { "https://graph.microsoft.com/.default" },
            userAssertion: new UserAssertion(userAccessToken)
        ).ExecuteAsync();

        return new GraphServiceClient(
            new DelegateAuthenticationProvider(request =>
            {
                request.Headers.Authorization = 
                    new AuthenticationHeaderValue("Bearer", result.AccessToken);
                return Task.CompletedTask;
            }));
    }
}
```

**When to use**: Any Graph API call that needs user's permissions (file upload, read, preview).

### UploadSessionManager (Large Files)

**Purpose**: Handle files >4MB using chunked upload sessions.

```csharp
// UploadSessionManager.cs
public async Task<DriveItem> UploadLargeFileAsync(
    string driveId,
    string fileName,
    Stream fileStream,
    long fileSize)
{
    // 1. Create upload session
    var session = await _graphClient.Drives[driveId]
        .Root
        .ItemWithPath(fileName)
        .CreateUploadSession()
        .PostAsync();

    // 2. Upload in 10MB chunks
    var chunkSize = 10 * 1024 * 1024;  // 10MB
    var provider = new LargeFileUploadTask<DriveItem>(session, fileStream, chunkSize);
    
    var result = await provider.UploadAsync();
    return result.ItemResponse;
}
```

**Thresholds**:
- <4MB: Direct upload via `PUT /content`
- ≥4MB: Upload session with chunking

### IDataverseService (Metadata Queries)

**Purpose**: Query Dataverse for relationship metadata (Phase 7).

```csharp
// IDataverseService.cs
public interface IDataverseService
{
    Task<NavigationPropertyMetadata> GetLookupNavigationPropertyAsync(
        string childEntity,
        string relationshipSchemaName);
        
    Task<NavigationPropertyMetadata> GetCollectionNavigationPropertyAsync(
        string parentEntity,
        string relationshipSchemaName);
        
    Task<DocumentInfo> GetDocumentAsync(string documentId);
}

// DataverseServiceClientImpl.cs
public class DataverseServiceClientImpl : IDataverseService
{
    private readonly ServiceClient _serviceClient;
    
    public DataverseServiceClientImpl(IConfiguration config)
    {
        // Connection string authentication (Microsoft recommended)
        var connectionString = 
            $"AuthType=ClientSecret;" +
            $"Url={config["Dataverse:ServiceUrl"]};" +
            $"ClientId={config["API_APP_ID"]};" +
            $"ClientSecret={config["API_CLIENT_SECRET"]}";

        _serviceClient = new ServiceClient(connectionString);
    }
    
    public async Task<NavigationPropertyMetadata> GetLookupNavigationPropertyAsync(
        string childEntity, string relationshipSchemaName)
    {
        var query = new QueryExpression("relationship")
        {
            ColumnSet = new ColumnSet("referencingentitynavigationpropertyname"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("schemaname", ConditionOperator.Equal,
                                           relationshipSchemaName)
                }
            }
        };

        var results = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        // Extract and return navigation property name
    }
}
```

---

## NavMap Endpoints Pattern

```csharp
// NavMapEndpoints.cs
public static void MapNavMapEndpoints(this IEndpointRouteBuilder app)
{
    app.MapGet("/api/navmap/{childEntity}/{relationship}/lookup",
        [Authorize]
        async (string childEntity, string relationship,
               IDataverseService dataverse,
               IDistributedCache cache,
               ILogger<Program> logger) =>
    {
        // 1. Check cache first
        var cacheKey = $"navmap:lookup:{childEntity}:{relationship}";
        var cached = await cache.GetStringAsync(cacheKey);
        
        if (cached != null)
        {
            logger.LogInformation("Cache hit: {Key}", cacheKey);
            return Results.Ok(JsonSerializer.Deserialize<object>(cached));
        }

        // 2. Query Dataverse
        var metadata = await dataverse.GetLookupNavigationPropertyAsync(
            childEntity, relationship);

        // 3. Cache for 15 minutes
        await cache.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            });

        return Results.Ok(new { 
            metadata.NavigationPropertyName,
            source = "dataverse" 
        });
    });
}
```

**Response Format**:
```json
{
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document_1n",
  "navigationPropertyName": "sprk_Matter",
  "source": "cache"
}
```

---

## File Access Endpoints Pattern

```csharp
// FileAccessEndpoints.cs
app.MapGet("/api/documents/{documentId}/preview-url",
    [Authorize]
    async (string documentId, HttpContext httpContext,
           IDataverseService dataverse, IGraphClientFactory graphFactory) =>
{
    // 1. Get document from Dataverse
    var document = await dataverse.GetDocumentAsync(documentId);
    
    // 2. Get user token, create Graph client
    var userToken = httpContext.Request.Headers["Authorization"]
        .ToString().Replace("Bearer ", "");
    var graphClient = await graphFactory.CreateClientAsync(userToken);
    
    // 3. Get DriveItem from SharePoint
    var driveItem = await graphClient.Drives[document.GraphDriveId]
        .Items[document.GraphItemId]
        .GetAsync();
    
    // 4. Build preview URL (nb=true hides SharePoint header)
    var previewUrl = driveItem.WebUrl + "?web=1&action=embedview&nb=true";
    
    return Results.Ok(new { previewUrl, documentInfo = new { driveItem.Name } });
});
```

---

## Caching Strategy

### Redis Cache Keys

| Pattern | TTL | Purpose |
|---------|-----|---------|
| `navmap:lookup:{entity}:{relationship}` | 15 min | Lookup navigation property |
| `navmap:collection:{entity}:{relationship}` | 15 min | Collection navigation property |

### Cache Benefits

```
First Request (cache miss):
  PCF → BFF → Dataverse → Cache → Response
  Time: ~2.5 seconds

Subsequent Requests (cache hit):
  PCF → BFF → Cache → Response
  Time: ~0.3 seconds (88% faster!)
```

### Cache Invalidation

```bash
# Manual clear (Azure Portal → Redis → Console)
> DEL navmap:lookup:sprk_document:sprk_matter_document_1n
> QUIT

# Or wait for TTL expiration (15 minutes)
```

---

## Configuration

### appsettings.json Pattern

```json
{
  "TENANT_ID": "from-environment",
  "API_APP_ID": "from-environment", 
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(...)",
  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(...)"
  },
  "Redis": {
    "ConnectionString": "from-environment"
  }
}
```

### Environment Variables

```bash
TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
API_APP_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c
API_CLIENT_SECRET=@Microsoft.KeyVault(SecretUri=.../BFF-API-ClientSecret)
Dataverse__ServiceUrl=@Microsoft.KeyVault(SecretUri=.../SPRK-DEV-DATAVERSE-URL)
```

---

## Deployment

```bash
cd src/api/Spe.Bff.Api

# Build
dotnet clean --configuration Release
dotnet publish --configuration Release --output ./publish

# Package
Compress-Archive -Path publish\* -DestinationPath deployment.zip -Force

# Deploy
az webapp deploy `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --src-path deployment.zip `
  --type zip

# Restart
az webapp restart --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2

# Verify
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"
```

---

## Common Mistakes

| Mistake | Error | Fix |
|---------|-------|-----|
| Missing Application User | AADSTS500011 | Create in Power Platform Admin Center |
| Wrong connection string format | ServiceClient failed | Use `AuthType=ClientSecret;Url=...;ClientId=...;ClientSecret=...` |
| Not using [Authorize] attribute | 401 on valid token | Add `[Authorize]` to endpoint |
| Hardcoded secrets | Security risk | Use Key Vault references |

---

## Debugging

### View Logs
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

### Common Log Patterns
```
# Success
[NavMapEndpoints] Cache hit: navmap:lookup:sprk_document:sprk_matter_document_1n
[FileAccess] GET preview-url for ca5bbb9f-... | Correlation: abc123

# Failure
AADSTS500011: Resource principal not found...
Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseConnectionException...
```

---

## Related Articles

- [sdap-overview.md](sdap-overview.md) - System architecture
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - OBO and auth flows
- [sdap-troubleshooting.md](sdap-troubleshooting.md) - Error resolution

---

*Condensed from BFF API sections of architecture guide*
