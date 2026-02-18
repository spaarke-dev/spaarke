# SDAP BFF API Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (BFF API section)
> **Last Updated**: February 17, 2026
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

## Document Upload Architecture

### SPE Container Model

**Each environment has a single default SPE container.** There are no per-entity (per-Matter, per-Project) containers. All documents across all entity types are stored in the same container.

```
┌─────────────────────────────────────────────────────┐
│  SPE Container (one per environment)                │
│  ID: b!yLRdWEOAdka... (Drive ID format)             │
│                                                     │
│  /emails/2026-02-17_Subject.eml                     │
│  /emails/attachments/{docId}/report.pdf             │
│  /documents/contract-v2.docx                        │
│  /documents/engagement-letter.pdf                   │
│  ...all documents stored flat (ADR-005)             │
└─────────────────────────────────────────────────────┘
         ↑                        ↑
         │                        │
  sprk_graphdriveid        sprk_graphitemid
         │                        │
┌─────────────────────────────────────────────────────┐
│  Dataverse (metadata + relationships)               │
│                                                     │
│  sprk_document → sprk_matter (via parent lookup)    │
│  sprk_document → sprk_project (via parent lookup)   │
│  sprk_document → sprk_event (via parent lookup)     │
└─────────────────────────────────────────────────────┘
```

**Container ID** is stored on parent entities (`sprk_matter.sprk_containerid`, `sprk_project.sprk_containerid`) but all currently point to the **same environment-level container**. This field exists to support potential future multi-container scenarios but is NOT used for per-entity isolation today.

### Container Resolution Strategy

| Upload Flow | Container ID Source | Code Reference |
|-------------|-------------------|----------------|
| **PCF upload** (UniversalQuickCreate) | Read from parent entity's `sprk_containerid` field via form context | PCF `EntityDocumentConfig.containerIdField` |
| **Email-to-Document** (background job) | `EmailProcessingOptions.DefaultContainerId` from config | `EmailToDocumentJobHandler.cs:201` |
| **Office Add-in** (save/upload) | Request `ContainerId` ?? fallback to `DefaultContainerId` | `OfficeService.cs:261` |
| **Email endpoints** | Request `ContainerId` ?? fallback to `DefaultContainerId` | `EmailEndpoints.cs:454` |
| **Upload without parent** (e.g., Create New Matter) | `DefaultContainerId` from config (no parent entity exists yet) | Same pattern as email-to-document |

**Configuration**:
```json
{
  "EmailProcessing": {
    "DefaultContainerId": "b!yLRdWEOAdka..."
  }
}
```

> **Important**: `DefaultContainerId` must be in **Drive ID format** (base64-encoded), not a raw GUID.
> - ❌ `"58dd5db4-8043-4676-..."` (raw GUID — will fail)
> - ✅ `"b!yLRdWEOAdkaWXskuRfByIRiz..."` (Drive ID format)

### "SPE First, Dataverse Second" Pattern (MANDATORY)

All document upload flows MUST follow the **SPE First, Dataverse Second** ordering:

1. **Upload file to SPE** → get `driveId` + `itemId` + `webUrl` back
2. **Create `sprk_document` in Dataverse** → use SPE result to populate `sprk_graphdriveid`, `sprk_graphitemid`, `sprk_filepath`

This ordering is mandatory because:
- SPE upload returns the `graphItemId` and `graphDriveId` needed by the Dataverse record
- Creating the Dataverse record first would result in an orphan record if SPE upload fails
- The document GUID cannot be predicted before SPE upload completes

**Rationale**: If the Dataverse record is created first but the SPE upload fails, we have an orphan `sprk_document` record with no file backing it. The "SPE first" pattern ensures files exist in SPE before metadata is committed to Dataverse.

#### Flow Variant A: PCF Upload (with parent entity)

Standard upload from a form where the parent entity already exists (e.g., uploading a document to an existing Matter).

```
User attaches file in PCF
    │
    ├─ 1. PCF reads parent entity's sprk_containerid
    │
    ├─ 2. PCF calls BFF: PUT /api/containers/{containerId}/files/{path}
    │     → SPE returns: { id, name, webUrl }    ← driveItemId + URL
    │
    ├─ 3. PCF creates sprk_document via Xrm.WebApi.createRecord()
    │     payload: {
    │       sprk_graphitemid: driveItem.id,
    │       sprk_graphdriveid: containerId,
    │       sprk_filepath: driveItem.webUrl,
    │       sprk_filename: file.name,
    │       sprk_filesize: file.size,
    │       "sprk_Matter@odata.bind": "/sprk_matters({matterId})"
    │     }
    │
    └─ 4. (Optional) Queue AI profile + RAG indexing via BFF
```

#### Flow Variant B: Background Job Upload (without parent entity)

Used by email-to-document, Office add-in finalization, and future flows where the parent entity may not exist at upload time.

```
Background job receives work item
    │
    ├─ 1. Resolve container: DefaultContainerId from config
    │
    ├─ 2. Resolve drive: SpeFileStore.ResolveDriveIdAsync(containerId)
    │
    ├─ 3. Upload to SPE: SpeFileStore.UploadSmallAsync(driveId, path, stream)
    │     → Returns FileHandleDto { Id, Name, Size, WebUrl }
    │
    ├─ 4. Create sprk_document in Dataverse:
    │     IDataverseService.CreateDocumentAsync(request)
    │     → Parent lookup set if parent exists, NULL if orphan allowed
    │
    ├─ 5. Update sprk_document with SPE metadata:
    │     IDataverseService.UpdateDocumentAsync(documentId, {
    │       GraphItemId = fileHandle.Id,
    │       GraphDriveId = driveId,
    │       FilePath = fileHandle.WebUrl,
    │       FileName, FileSize, MimeType
    │     })
    │
    └─ 6. Mark job as processed (idempotency key)
```

#### Flow Variant C: Upload Without Parent Entity (Create New Entity)

Used when files are uploaded before the parent entity is created (e.g., Create New Matter dialog where user attaches files before the Matter record exists).

```
User fills form + attaches files in dialog
    │
    ├─ 1. Upload files to SPE using DefaultContainerId
    │     → Files now exist in SPE (no Dataverse records yet)
    │     → Store SPE results (driveId, itemId, webUrl) in client state
    │
    ├─ 2. Create parent entity (e.g., sprk_matter) in Dataverse
    │     → Returns new Matter ID
    │
    ├─ 3. Create sprk_document records linking files to new parent:
    │     → sprk_graphitemid = stored driveItem.id
    │     → sprk_graphdriveid = DefaultContainerId
    │     → sprk_filepath = stored driveItem.webUrl
    │     → "sprk_Matter@odata.bind" = "/sprk_matters({newMatterId})"
    │
    └─ 4. Queue AI text extraction + profile + RAG indexing via BFF
```

**Key point**: No file moves are needed. Files stay in the same container permanently. The `sprk_document` record's parent lookup is what associates files with the entity — not the container location.

### All Upload Flows Summary

| Flow | Trigger | Auth | Container Source | Parent Entity | Dataverse Timing |
|------|---------|------|-----------------|---------------|------------------|
| **PCF upload** | User action (form) | OBO (user token) | Parent's `sprk_containerid` | Exists | Synchronous |
| **Email-to-document** | Webhook → job queue | App-only (client credentials) | `DefaultContainerId` config | Optional | Asynchronous (background) |
| **Office Add-in** | User save action | OBO (user token) | Request ?? `DefaultContainerId` | Optional | Asynchronous (Service Bus worker) |
| **Create New Entity** | Dialog submit | OBO (user token) | `DefaultContainerId` config | Created after upload | Synchronous (two-phase) |

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

## Authorization Service Pattern (AI Analysis)

**Purpose**: Validate user has Read access to documents before executing AI analysis.

### IAiAuthorizationService Interface

```csharp
// IAiAuthorizationService.cs
public interface IAiAuthorizationService
{
    /// <summary>
    /// Validates user has Read access to specified documents.
    /// </summary>
    /// <param name="user">ClaimsPrincipal with user identity</param>
    /// <param name="documentIds">Documents to authorize</param>
    /// <param name="httpContext">Required for OBO token extraction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AuthorizationResult with success status and authorized document IDs</returns>
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        IReadOnlyList<Guid> documentIds,
        HttpContext httpContext,  // Required for OBO authentication
        CancellationToken cancellationToken = default);
}

public record AuthorizationResult(
    bool Success,
    string? Reason,
    IReadOnlyList<Guid> AuthorizedDocumentIds);
```

### AiAuthorizationService Implementation

```csharp
// AiAuthorizationService.cs
public class AiAuthorizationService : IAiAuthorizationService
{
    private readonly IAccessDataSource _accessDataSource;
    private readonly ILogger<AiAuthorizationService> _logger;

    public async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        IReadOnlyList<Guid> documentIds,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        // Extract user's bearer token for OBO authentication
        string? userAccessToken = null;
        try
        {
            userAccessToken = TokenHelper.ExtractBearerToken(httpContext);
            _logger.LogDebug("[AI-AUTH] Extracted user bearer token for OBO");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "[AI-AUTH] Failed to extract bearer token");
            return AuthorizationResult.Denied("Missing or invalid authorization token");
        }

        // Query authorization via Dataverse (uses OBO)
        var result = await _accessDataSource.GetUserAccessAsync(
            user,
            documentIds,
            userAccessToken,  // Pass token for OBO exchange
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "[AI-AUTH] Authorization DENIED: {Reason}",
                result.Reason);
            return AuthorizationResult.Denied(result.Reason);
        }

        _logger.LogInformation(
            "[AI-AUTH] Authorization GRANTED: {Count} documents",
            result.AuthorizedDocumentIds.Count);

        return result;
    }
}
```

### AnalysisAuthorizationFilter (Endpoint Filter)

**Pattern**: Use endpoint filters for resource-level authorization (ADR-008).

```csharp
// AnalysisAuthorizationFilter.cs
public class AnalysisAuthorizationFilter : IEndpointFilter
{
    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<AnalysisAuthorizationFilter> _logger;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Verify user identity claims
        var userId = user.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        // Extract document IDs from request
        var documentIds = ExtractDocumentIds(context);
        if (documentIds.Count == 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "No document identifier found in request");
        }

        // Authorize via AiAuthorizationService
        var result = await _authorizationService.AuthorizeAsync(
            user,
            documentIds,
            context.HttpContext,  // Pass HttpContext for OBO token extraction
            context.HttpContext.RequestAborted);

        if (!result.Success)
        {
            _logger.LogWarning(
                "[ANALYSIS-AUTH] Document access DENIED: {Reason}",
                result.Reason);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: result.Reason ?? "Access denied to one or more documents");
        }

        _logger.LogDebug(
            "[ANALYSIS-AUTH] Document access GRANTED: {Count} documents",
            documentIds.Count);

        return await next(context);
    }
}
```

### TokenHelper Pattern

```csharp
// TokenHelper.cs
public static class TokenHelper
{
    /// <summary>
    /// Extracts bearer token from Authorization header.
    /// </summary>
    public static string ExtractBearerToken(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers["Authorization"].ToString();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            throw new UnauthorizedAccessException("Missing or invalid Authorization header");
        }

        return authHeader["Bearer ".Length..].Trim();
    }
}
```

### Using Authorization Filter in Endpoints

```csharp
// AnalysisEndpoints.cs
public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
{
    app.MapPost("/api/ai/analysis/execute",
        async (AnalysisExecuteRequest request,
               HttpContext httpContext,
               IAnalysisOrchestrationService orchestrationService,
               CancellationToken ct) =>
    {
        // Authorization already performed by filter
        // HttpContext available for OBO operations (file download)

        await foreach (var chunk in orchestrationService.ExecutePlaybookAsync(
            request,
            httpContext,  // Pass for OBO
            ct))
        {
            // Stream analysis results
            yield return chunk;
        }
    })
    .AddEndpointFilter<AnalysisAuthorizationFilter>();  // ← Apply filter
}
```

### Authorization Flow Diagram

```
PCF Control
    │
    ├─→ Acquires token (MSAL.js)
    │   Scope: api://1e40baad-.../user_impersonation
    │
    ├─→ POST /api/ai/analysis/execute
    │   Headers: Authorization: Bearer {user_bff_token}
    │   Body: { documentIds: [...], playbookId: "..." }
    │
BFF API
    │
    ├─→ AnalysisAuthorizationFilter.InvokeAsync()
    │   ├─→ Extract document IDs from request
    │   ├─→ Extract user's oid claim
    │   ├─→ Call IAiAuthorizationService.AuthorizeAsync()
    │   │
    │   └─→ AiAuthorizationService
    │       ├─→ TokenHelper.ExtractBearerToken(httpContext)
    │       ├─→ IAccessDataSource.GetUserAccessAsync(user, documentIds, userToken, ct)
    │       │
    │       └─→ DataverseAccessDataSource
    │           ├─→ MSAL OBO: User token → Dataverse token
    │           ├─→ Set OBO token on HttpClient headers
    │           ├─→ GET /systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'
    │           ├─→ GET /sprk_documents({id})?$select=sprk_documentid (for each doc)
    │           └─→ Return AuthorizationResult
    │
    ├─→ If authorized: Proceed to AnalysisOrchestrationService
    ├─→ If denied: Return 403 Forbidden
    │
AnalysisOrchestrationService
    │
    └─→ Execute AI analysis with user context (file download, processing)
```

### Key Patterns

| Pattern | Purpose |
|---------|---------|
| **HttpContext propagation** | Pass `HttpContext` through call chain for OBO token extraction |
| **TokenHelper.ExtractBearerToken** | Centralized token extraction from Authorization header |
| **Endpoint filters** | Apply authorization at endpoint level (ADR-008 compliance) |
| **Fail-closed security** | Return `AccessRights.None` on authorization errors |
| **Structured logging** | Use `[AI-AUTH]`, `[UAC-DIAG]` prefixes for traceability |

### Related Documentation

- **Auth Patterns**: `docs/architecture/sdap-auth-patterns.md` (Pattern 5: OBO for Dataverse)
- **Azure Resources**: `docs/architecture/auth-azure-resources.md` (OBO Token Exchange section)
- **Architecture Changes**: `projects/ai-summary-and-analysis-enhancements/ARCHITECTURE-CHANGES.md` (OBO bugs fixed)

---

## Email Webhook + Job Handler Pattern

**Purpose**: Process Dataverse email activities via webhook triggers and background jobs using app-only authentication.

### Email Webhook Endpoint Pattern

```csharp
// EmailEndpoints.cs
app.MapPost("/api/v1/emails/webhook-trigger",
    async (HttpContext httpContext,
           IEmailFilterService filterService,
           JobSubmissionService jobSubmission,
           ILogger<Program> logger) =>
{
    // 1. Validate webhook signature (HMAC-SHA256 or WebKey)
    if (!ValidateWebhookAuth(httpContext, _options.WebhookSecret))
    {
        return Results.Unauthorized();
    }

    // 2. Parse Dataverse webhook payload (handles WCF date format)
    var payload = await ParseWebhookPayloadAsync(httpContext);

    // 3. Apply filter rules
    var filterResult = await filterService.EvaluateAsync(payload.EmailId);
    if (filterResult.Action == FilterAction.Exclude)
    {
        return Results.Ok(new { status = "excluded", reason = filterResult.RuleName });
    }

    // 4. Enqueue job for async processing
    await jobSubmission.EnqueueAsync(new JobContract
    {
        JobType = "ProcessEmailToDocument",
        SubjectId = payload.EmailId.ToString(),
        IdempotencyKey = $"Email:{payload.EmailId}:Archive",
        Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
    });

    return Results.Accepted();
});
```

### WCF DateTime Format Handling

**Critical**: Dataverse webhooks send dates in WCF format (`/Date(1234567890000)/`), not ISO 8601.

```csharp
// DataverseWebhookPayload.cs
public class DataverseWebhookPayload
{
    public Guid PrimaryEntityId { get; set; }

    [JsonPropertyName("OperationCreatedOn")]
    [JsonConverter(typeof(NullableWcfDateTimeConverter))]
    public DateTime? OperationCreatedOn { get; set; }
}

// NullableWcfDateTimeConverter.cs
public class NullableWcfDateTimeConverter : JsonConverter<DateTime?>
{
    // Handles: "/Date(1234567890000)/" → DateTime
    // Falls back to ISO 8601 parsing if WCF format not detected
    public override DateTime? Read(ref Utf8JsonReader reader, ...)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value)) return null;

        // WCF format: /Date(milliseconds)/
        var match = Regex.Match(value, @"/Date\((-?\d+)\)/");
        if (match.Success)
        {
            var ms = long.Parse(match.Groups[1].Value);
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }

        return DateTime.Parse(value);
    }
}
```

### Email-to-Document Job Handler Pattern

```csharp
// EmailToDocumentJobHandler.cs
public class EmailToDocumentJobHandler : IJobHandler
{
    public string JobType => "ProcessEmailToDocument";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        // 1. Parse payload
        var payload = ParsePayload(job.Payload);

        // 2. Idempotency check
        if (await _idempotencyService.IsEventProcessedAsync(job.IdempotencyKey, ct))
        {
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        // 3. Acquire processing lock
        await _idempotencyService.TryAcquireProcessingLockAsync(
            job.IdempotencyKey, TimeSpan.FromMinutes(5), ct);

        try
        {
            // 4. Convert email to .eml
            var emlResult = await _emlConverter.ConvertToEmlAsync(
                payload.EmailId, includeAttachments: true, ct);

            // 5. Upload to SPE (app-only auth)
            var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct);
            var fileHandle = await _speFileStore.UploadSmallAsync(driveId, path, stream, ct);

            // 6. Create Dataverse document record
            var documentId = await _dataverseService.CreateDocumentAsync(request, ct);

            // 7. Update with file info (note correct field names)
            var updateRequest = new UpdateDocumentRequest
            {
                FileName = fileName,
                FileSize = emlResult.FileSizeBytes,     // Note: cast to (int)
                MimeType = "message/rfc822",            // Note: sprk_mimetype, not sprk_filetype
                GraphItemId = fileHandle.Id,
                GraphDriveId = driveId,
                FilePath = fileHandle.WebUrl,           // SharePoint URL for links
                // ... email metadata
            };
            await _dataverseService.UpdateDocumentAsync(documentId, updateRequest, ct);

            // 8. Mark as processed
            await _idempotencyService.MarkEventAsProcessedAsync(
                job.IdempotencyKey, TimeSpan.FromDays(7), ct);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        finally
        {
            await _idempotencyService.ReleaseProcessingLockAsync(job.IdempotencyKey, ct);
        }
    }
}
```

### Dataverse Document Field Mappings

**Critical**: Match these field names and types exactly.

| Property | Dataverse Field | Type | Notes |
|----------|-----------------|------|-------|
| `FileName` | `sprk_filename` | Text | |
| `FileSize` | `sprk_filesize` | Whole Number | Cast `(int)` - NOT Int64 |
| `MimeType` | `sprk_mimetype` | Text | NOT `sprk_filetype` |
| `GraphItemId` | `sprk_graphitemid` | Text | |
| `GraphDriveId` | `sprk_graphdriveid` | Text | |
| `FilePath` | `sprk_filepath` | Text | SharePoint WebUrl for "Open" links |
| `HasFile` | `sprk_hasfile` | Boolean | |

### FileHandleDto Properties

The `SpeFileStore.UploadSmallAsync()` returns a `FileHandleDto` with these key properties:

```csharp
public record FileHandleDto(
    string Id,           // Graph item ID (sprk_graphitemid)
    string Name,         // File name
    long? Size,          // File size in bytes
    string? WebUrl       // SharePoint URL (sprk_filepath) ← USE THIS
);
```

**Pattern**: Always set `FilePath = fileHandle.WebUrl` to enable "Open in SharePoint" links in the UI.

---

## Background Workers Pattern (Office Add-ins)

**Purpose**: Process Office Add-in file uploads asynchronously using Service Bus queues and BackgroundService workers.

### IOfficeJobHandler Interface

Office workers implement a common job handler interface for processing stages:

```csharp
// IOfficeJobHandler.cs
public interface IOfficeJobHandler
{
    /// <summary>
    /// Gets the job type this handler processes.
    /// </summary>
    OfficeJobType JobType { get; }

    /// <summary>
    /// Processes a job message.
    /// </summary>
    Task<JobOutcome> ProcessAsync(OfficeJobMessage message, CancellationToken cancellationToken);
}

public record JobOutcome(
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool Retryable = false);

public enum OfficeJobType
{
    UploadFinalization,  // Move files, create records
    Profile,             // AI summary generation
    Indexing,            // RAG search indexing
    DeepAnalysis         // Optional detailed analysis
}
```

### Service Bus Message Processing Pattern

Workers use the Azure Service Bus SDK for queue processing:

```csharp
// UploadFinalizationWorker.cs (example)
public class UploadFinalizationWorker : BackgroundService, IOfficeJobHandler
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusProcessor _processor;

    public OfficeJobType JobType => OfficeJobType.UploadFinalization;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor(
            queueName: _options.UploadFinalizationQueue,
            options: new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 5,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10)
            });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            // 1. Deserialize message
            var message = JsonSerializer.Deserialize<OfficeJobMessage>(args.Message.Body);

            // 2. Check idempotency
            var cacheKey = $"{message.JobId}-upload";
            if (await _cache.GetAsync(cacheKey) != null)
            {
                await args.CompleteMessageAsync(args.Message);
                return; // Already processed
            }

            // 3. Process job stages
            var outcome = await ProcessAsync(message, args.CancellationToken);

            // 4. Complete or abandon message
            if (outcome.IsSuccess)
            {
                await _cache.SetAsync(cacheKey, Array.Empty<byte>(),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) });
                await args.CompleteMessageAsync(args.Message);
            }
            else if (outcome.Retryable)
            {
                await args.AbandonMessageAsync(args.Message);
            }
            else
            {
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: outcome.ErrorCode,
                    deadLetterErrorDescription: outcome.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing message");
            await args.AbandonMessageAsync(args.Message);
        }
    }
}
```

### Worker Registration Pattern

Workers are registered in `OfficeWorkersModule.cs`:

```csharp
// OfficeWorkersModule.cs
public static class OfficeWorkersModule
{
    public static IServiceCollection AddOfficeWorkers(this IServiceCollection services)
    {
        // Register job handlers as singleton (stateless)
        services.AddSingleton<IOfficeJobHandler, UploadFinalizationWorker>();
        services.AddSingleton<IOfficeJobHandler, ProfileSummaryWorker>();

        // Register as BackgroundService (hosted services)
        services.AddHostedService<UploadFinalizationWorker>(sp =>
        {
            var handlers = sp.GetServices<IOfficeJobHandler>();
            return handlers.OfType<UploadFinalizationWorker>().First();
        });

        services.AddHostedService<ProfileSummaryWorker>(sp =>
        {
            var handlers = sp.GetServices<IOfficeJobHandler>();
            return handlers.OfType<ProfileSummaryWorker>().First();
        });

        services.AddHostedService<IndexingWorkerHostedService>();

        return services;
    }

    public static IServiceCollection AddOfficeServiceBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(
            configuration.GetSection("ServiceBus"));

        services.AddSingleton<ServiceBusClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>();
            return new ServiceBusClient(options.Value.ConnectionString);
        });

        return services;
    }
}
```

### Worker Implementations

#### UploadFinalizationWorker

**Purpose**: Processes file uploads, creates Dataverse records, queues downstream jobs.

**Key Operations**:
1. Move files from temp container to permanent SPE location
2. Create EmailArtifact, AttachmentArtifact, Document records
3. Link relationships between entities
4. Queue messages to `office-profile` and `office-indexing` queues

**Dependencies**: IDataverseServiceClient, ISpeFileStore, IDistributedCache, ServiceBusClient, IOptions<GraphOptions>

#### ProfileSummaryWorker

**Purpose**: Generates AI document profile using IAppOnlyAnalysisService.

**Key Operations**:
1. Call `AnalyzeDocumentAsync(documentId, "Document Profile", ct)`
2. AI extracts summary, keywords, document type
3. Update Document with AI-generated metadata
4. Graceful degradation if AI service fails

**Dependencies**: IAppOnlyAnalysisService, IDataverseServiceClient, IDistributedCache, ServiceBusClient

#### IndexingWorkerHostedService

**Purpose**: Indexes documents in Azure AI Search for RAG retrieval.

**Key Operations**:
1. Build FileIndexRequest with DriveId, ItemId, FileName, TenantId
2. Call `IndexFileAppOnlyAsync(request, ct)`
3. Service chunks content, generates embeddings, indexes in AI Search
4. Log indexing results (chunks, duration)

**Dependencies**: IFileIndexingService, IDistributedCache, ServiceBusClient

### Configuration Pattern

Workers use `IOptions<T>` for configuration:

```csharp
// ServiceBusOptions.cs
public class ServiceBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string UploadFinalizationQueue { get; set; } = "office-upload-finalization";
    public string ProfileQueue { get; set; } = "office-profile";
    public string IndexingQueue { get; set; } = "office-indexing";
}

// appsettings.json
{
  "ServiceBus": {
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=...)",
    "UploadFinalizationQueue": "office-upload-finalization",
    "ProfileQueue": "office-profile",
    "IndexingQueue": "office-indexing"
  },
  "Graph": {
    "TenantId": "#{TENANT_ID}#",
    "ClientId": "#{API_CLIENT_ID}#",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=...)"
  }
}
```

### Idempotency Pattern

Workers use Redis distributed cache for idempotency:

```csharp
// Check if job already processed
var cacheKey = $"{jobId}-{stage}"; // e.g., "57e63bd6-...-upload"
var cached = await _cache.GetAsync(cacheKey, ct);
if (cached != null)
{
    _logger.LogInformation("Job {JobId} already processed (idempotency)", jobId);
    return JobOutcome.Success();
}

// Mark as processed (7-day TTL)
await _cache.SetAsync(cacheKey, Array.Empty<byte>(),
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7)
    }, ct);
```

**Pattern**: `{jobId}-{stage}` ensures each stage is idempotent independently.

### Error Handling Pattern

Workers distinguish between retryable and non-retryable errors:

```csharp
try
{
    // Processing logic
    return JobOutcome.Success();
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
{
    // Retryable: throttling
    return JobOutcome.Failure("OFFICE_THROTTLED", ex.Message, retryable: true);
}
catch (ArgumentException ex)
{
    // Non-retryable: validation error
    return JobOutcome.Failure("OFFICE_INVALID_DATA", ex.Message, retryable: false);
}
catch (Exception ex)
{
    // Unknown error: retryable
    _logger.LogError(ex, "Unexpected error processing job {JobId}", message.JobId);
    return JobOutcome.Failure("OFFICE_UNEXPECTED", ex.Message, retryable: true);
}
```

**Graceful Degradation**: AI and indexing failures are logged as warnings but don't fail the job:

```csharp
// AI profile generation (ProfileSummaryWorker)
try
{
    var result = await _analysisService.AnalyzeDocumentAsync(documentId, "Document Profile", ct);
    if (!result.IsSuccess)
    {
        _logger.LogWarning("AI profile generation failed (non-fatal): {Error}", result.ErrorMessage);
    }
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Exception during AI profile generation (non-fatal)");
}
// Job continues regardless of AI outcome
```

### Queue Configuration

Service Bus queues require creation and configuration:

```bash
# Create queues with 7-day TTL
az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name customer-servicebus \
  --name office-upload-finalization \
  --max-delivery-count 5 \
  --default-message-time-to-live P7D \
  --enable-dead-lettering-on-message-expiration true

az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name customer-servicebus \
  --name office-profile \
  --max-delivery-count 3 \
  --default-message-time-to-live P7D

az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name customer-servicebus \
  --name office-indexing \
  --max-delivery-count 3 \
  --default-message-time-to-live P7D
```

### Monitoring Pattern

Application Insights queries for worker health:

```kusto
// Worker processing times
traces
| where customDimensions.Category == "OfficeWorker"
| summarize avg(customDimensions.Duration), max(customDimensions.Duration)
  by customDimensions.WorkerType, bin(timestamp, 1h)

// Failed jobs by error code
traces
| where customDimensions.JobStatus == "Failed"
| summarize count() by customDimensions.ErrorCode, bin(timestamp, 1h)

// Stage completion rates
traces
| where customDimensions.Stage != ""
| summarize completed = countif(customDimensions.StageStatus == "completed"),
            failed = countif(customDimensions.StageStatus == "failed")
            by customDimensions.Stage
```

### Related Documentation

- **Full Architecture**: [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md)
- **Component Interactions**: [sdap-component-interactions.md](sdap-component-interactions.md) Pattern 5B
- **Customer Deployment**: [CUSTOMER-DEPLOYMENT-GUIDE.md](../guides/CUSTOMER-DEPLOYMENT-GUIDE.md) Service Bus configuration

---

## Common Mistakes

| Mistake | Error | Fix |
|---------|-------|-----|
| Missing Application User | AADSTS500011 | Create in Power Platform Admin Center |
| Wrong connection string format | ServiceClient failed | Use `AuthType=ClientSecret;Url=...;ClientId=...;ClientSecret=...` |
| Not using [Authorize] attribute | 401 on valid token | Add `[Authorize]` to endpoint |
| Hardcoded secrets | Security risk | Use Key Vault references |
| Using `sprk_filetype` instead of `sprk_mimetype` | Field not found | Correct field is `sprk_mimetype` |
| FileSize as Int64 | Type mismatch | `sprk_filesize` is Whole Number - cast to `(int)` |
| Not setting `FilePath` | "Open in SharePoint" broken | Set `FilePath = fileHandle.WebUrl` |
| Parsing WCF dates as ISO 8601 | DateTime parse error | Use `WcfDateTimeConverter` for webhook payloads |

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
