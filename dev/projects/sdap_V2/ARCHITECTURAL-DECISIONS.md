# Architectural Decision Records (ADRs) for SDAP V2

**Last Updated**: 2025-10-13
**Status**: Active - Target Architecture
**Purpose**: Coding guidelines for SDAP refactoring to simplified, maintainable architecture

---

## Table of Contents

- [ADR-007: SPE Storage Seam Minimalism](#adr-007-spe-storage-seam-minimalism)
- [ADR-009: Caching Policy – Redis-First](#adr-009-caching-policy--redis-first)
- [ADR-010: DI Minimalism and Feature Modules](#adr-010-di-minimalism-and-feature-modules)
- [Quick Reference: File Paths](#quick-reference-file-paths)
- [Quick Reference: Configuration](#quick-reference-configuration)

---

## ADR-007: SPE Storage Seam Minimalism

### Context

Previously, the SPE storage layer had 6 layers of abstraction (`IResourceStore` → `SpeResourceStore` → `ISpeService` → `OboSpeService` → `IGraphClientFactory` → `GraphServiceClient`). This made debugging difficult and added no value.

### Decision

Use a **single, focused SPE storage facade** named `SpeFileStore` that encapsulates all Graph/SPE calls. **Do NOT create** a generic `IResourceStore` interface or intermediate abstractions.

### Target Implementation

**File Location**: `src/api/Spe.Bff.Api/Storage/SpeFileStore.cs`

```csharp
/// <summary>
/// Single SPE storage facade following ADR-007: SPE Storage Seam Minimalism.
/// Encapsulates all Graph SDK calls for SharePoint Embedded operations.
/// </summary>
public class SpeFileStore
{
    private readonly IGraphClientFactory _graphFactory;
    private readonly ILogger<SpeFileStore> _logger;

    public SpeFileStore(
        IGraphClientFactory graphFactory,
        ILogger<SpeFileStore> logger)
    {
        _graphFactory = graphFactory;
        _logger = logger;
    }

    /// <summary>
    /// Upload file to SPE container using OBO token.
    /// </summary>
    /// <param name="containerId">SPE container ID (driveId)</param>
    /// <param name="fileName">File name (e.g., "document.pdf")</param>
    /// <param name="content">File content stream</param>
    /// <param name="userToken">User access token for OBO exchange</param>
    /// <returns>File metadata DTO (never Graph SDK types)</returns>
    public async Task<FileUploadResult> UploadFileAsync(
        string containerId,
        string fileName,
        Stream content,
        string userToken)
    {
        // Get Graph client with OBO token (user context preserved)
        var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

        // Direct Graph SDK call - uses beta endpoint for SPE
        var driveItem = await graphClient
            .Storage
            .FileStorage
            .Containers[containerId]
            .Drive
            .Root
            .ItemWithPath(fileName)
            .Content
            .Request()
            .PutAsync<DriveItem>(content);

        _logger.LogInformation(
            "Uploaded file {FileName} to container {ContainerId}, ItemId: {ItemId}",
            fileName, containerId, driveItem.Id);

        // ALWAYS return SDAP DTO, NEVER Graph SDK types
        return new FileUploadResult
        {
            ItemId = driveItem.Id,
            Name = driveItem.Name,
            Size = driveItem.Size ?? 0,
            WebUrl = driveItem.WebUrl,
            CreatedDateTime = driveItem.CreatedDateTime
        };
    }

    /// <summary>
    /// Download file from SPE container.
    /// </summary>
    public async Task<Stream> DownloadFileAsync(
        string containerId,
        string itemId,
        string userToken)
    {
        var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

        var stream = await graphClient
            .Storage
            .FileStorage
            .Containers[containerId]
            .Drive
            .Items[itemId]
            .Content
            .Request()
            .GetAsync();

        _logger.LogInformation(
            "Downloaded file {ItemId} from container {ContainerId}",
            itemId, containerId);

        return stream;
    }

    /// <summary>
    /// Create new SPE container (admin operation).
    /// </summary>
    public async Task<ContainerDto> CreateContainerAsync(
        string containerName,
        string userToken)
    {
        var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

        var container = await graphClient
            .Storage
            .FileStorage
            .Containers
            .Request()
            .AddAsync(new FileStorageContainer
            {
                DisplayName = containerName,
                ContainerTypeId = Guid.Parse(_defaultContainerTypeId)
            });

        return new ContainerDto
        {
            Id = container.Id,
            DisplayName = container.DisplayName,
            CreatedDateTime = container.CreatedDateTime
        };
    }
}
```

### Enforcement Rules

| Rule | ✅ DO | ❌ DON'T |
|------|------|---------|
| **Class Type** | Concrete class only | Don't create `ISpeFileStore` interface |
| **Return Types** | Return DTOs (`FileUploadResult`, `ContainerDto`) | NEVER return `DriveItem`, `Drive`, Graph SDK types |
| **Injection** | Endpoints inject `SpeFileStore` directly | Don't inject through interface |
| **Dependencies** | Inject `IGraphClientFactory` only | Don't create intermediate service layers |
| **Lifetime** | Register as **Scoped** | Don't use Singleton or Transient |

### Architecture Comparison

**❌ OLD (6 layers)**:
```
OBOEndpoints.cs
  ↓ injects IResourceStore
SpeResourceStore.cs (implements IResourceStore)
  ↓ injects ISpeService
OboSpeService.cs (implements ISpeService)
  ↓ injects IGraphClientFactory
GraphClientFactory.cs
  ↓ creates GraphServiceClient
```

**✅ NEW (3 layers)**:
```
OBOEndpoints.cs
  ↓ injects SpeFileStore (concrete)
SpeFileStore.cs
  ↓ injects IGraphClientFactory
GraphClientFactory.cs
  ↓ creates GraphServiceClient
```

**Result**: 50% fewer layers, clearer dependencies, easier debugging.

---

## ADR-009: Caching Policy – Redis-First

### Context

Authentication overhead was killing performance: every request performed OBO token exchange (~200ms). With 1000 requests/hour, that's 3.3 minutes of waiting time just for token exchanges.

### Decision

Use **distributed cache (Redis)** as the only cross-request cache. Add tiny per-request cache to collapse duplicate reads within one request. **Do NOT** implement hybrid L1+L2 cache.

### Target Implementation

**File Location**: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`

```csharp
/// <summary>
/// Caches OBO tokens to reduce Azure AD load following ADR-009: Redis-First Caching.
/// Target: 95% cache hit rate, 97% reduction in auth latency.
/// </summary>
public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;

    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Compute SHA256 hash of user token for cache key.
    /// </summary>
    public string ComputeTokenHash(string userToken)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Get cached Graph token by user token hash.
    /// </summary>
    /// <returns>Cached token or null if cache miss</returns>
    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";
        var cachedToken = await _cache.GetStringAsync(cacheKey);

        if (cachedToken != null)
        {
            _logger.LogDebug("Cache HIT for token hash {Hash}", tokenHash.Substring(0, 8));
        }
        else
        {
            _logger.LogDebug("Cache MISS for token hash {Hash}", tokenHash.Substring(0, 8));
        }

        return cachedToken;
    }

    /// <summary>
    /// Cache Graph token with TTL.
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token</param>
    /// <param name="graphToken">Graph API token from OBO exchange</param>
    /// <param name="expiry">TTL (typically 55 minutes)</param>
    public async Task SetTokenAsync(string tokenHash, string graphToken, TimeSpan expiry)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        await _cache.SetStringAsync(cacheKey, graphToken,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            });

        _logger.LogDebug("Cached token for hash {Hash} with TTL {TTL}",
            tokenHash.Substring(0, 8), expiry);
    }
}
```

### Integration with GraphClientFactory

**File Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

```csharp
public class GraphClientFactory : IGraphClientFactory
{
    private readonly IConfidentialClientApplication _cca;
    private readonly GraphTokenCache _tokenCache;
    private readonly ILogger<GraphClientFactory> _logger;

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Step 1: Check cache first (95% hit rate expected)
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
        var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedToken != null)
        {
            // Cache hit: ~5ms
            return CreateGraphClientWithToken(cachedToken);
        }

        // Step 2: Cache miss - perform OBO exchange (~200ms)
        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[] {
                "https://graph.microsoft.com/Sites.FullControl.All",
                "https://graph.microsoft.com/Files.ReadWrite.All"
            },
            userAssertion: new UserAssertion(userAccessToken)
        ).ExecuteAsync();

        _logger.LogInformation("OBO token exchange completed, caching token");

        // Step 3: Cache token with 55-minute TTL (5-minute buffer before 1-hour expiration)
        await _tokenCache.SetTokenAsync(
            tokenHash,
            result.AccessToken,
            TimeSpan.FromMinutes(55));

        return CreateGraphClientWithToken(result.AccessToken);
    }

    private GraphServiceClient CreateGraphClientWithToken(string accessToken)
    {
        var tokenCredential = new SimpleTokenCredential(accessToken);
        var authProvider = new AzureIdentityAuthenticationProvider(
            tokenCredential,
            scopes: new[] { "https://graph.microsoft.com/.default" });

        return new GraphServiceClient(_httpClient, authProvider);
    }
}
```

### Cache Strategy

| Item to Cache | Cache Key Pattern | TTL | Why? |
|---------------|------------------|-----|------|
| **Graph OBO Tokens** | `sdap:graph:token:{SHA256(userToken)}` | **55 minutes** | Token valid for 60 min, 5-min buffer |
| **Authorization Snapshots** | `sdap:access:user:{userId}:v1` | **5 minutes** | User permissions change infrequently |
| **Document Metadata** | `sdap:document:{documentId}:metadata:v1` | **10 minutes** | Reduce Dataverse queries |

### What NOT to Cache

| Item | Why NOT to Cache? |
|------|-------------------|
| **Authorization Decisions** | Security risk - permissions must be real-time |
| **User Identity** | Token already contains identity |
| **File Content** | Too large for Redis, use CDN instead |

### Performance Impact

**Without Caching** (Current State):
- Every request: 200ms OBO exchange
- 1000 requests/hour: **3.3 minutes** waiting

**With Caching** (Target):
- Cache hit (95%): 5ms
- Cache miss (5%): 200ms
- Average latency: **~15ms** (95% × 5ms + 5% × 200ms)
- **97% reduction** in auth latency

### Enforcement Rules

| Rule | ✅ DO | ❌ DON'T |
|------|------|---------|
| **Cache Implementation** | Use `IDistributedCache` (Redis) | Don't create hybrid L1/L2 cache |
| **Cache Keys** | Include version (`:v1`) | Don't use unversioned keys |
| **TTL** | Set explicit expiration | Don't cache indefinitely |
| **Scope** | Cache cross-request data only | Don't cache per-request data |

---

## ADR-010: DI Minimalism and Feature Modules

### Context

Program.cs had 680+ lines of scattered DI registrations. Impossible to understand what services were registered, lifetimes were inconsistent, and too many unnecessary interfaces existed.

### Decision

1. **Register concretes** unless a genuine seam exists (factory pattern, test isolation)
2. **Use feature-module extension methods** to group related registrations
3. **Target: ~20 lines** of DI code in Program.cs

### Target Implementation

#### Program.cs (Simplified)

**File Location**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// ---- Configuration Validation ----
builder.Services.AddOptionsWithValidation<GraphOptions>(builder.Configuration);
builder.Services.AddOptionsWithValidation<DataverseOptions>(builder.Configuration);
builder.Services.AddOptionsWithValidation<ServiceBusOptions>(builder.Configuration);
builder.Services.AddOptionsWithValidation<RedisOptions>(builder.Configuration);

// ---- Authentication & Authorization ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorizationPolicies(); // Extension method

// ---- Feature Modules (ADR-010) ----
builder.Services.AddSpaarkeCore();        // Core auth + cache
builder.Services.AddDocumentsModule();    // SPE operations
builder.Services.AddWorkersModule();      // Background services

// ---- Infrastructure ----
builder.Services.AddHttpClientWithResilience(); // Polly policies
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Middleware pipeline...
app.Run();
```

**Line Count**: ~25 lines (vs 680 lines before)

---

#### SpaarkeCore Module

**File Location**: `src/api/Spe.Bff.Api/Extensions/SpaarkeCoreExtensions.cs`

```csharp
/// <summary>
/// Registers core authorization and caching services (ADR-010: DI Minimalism).
/// </summary>
public static class SpaarkeCoreExtensions
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // Authorization (Singleton - stateless rule evaluation)
        services.AddSingleton<AuthorizationService>();
        services.AddSingleton<IAuthorizationRule, OperationAccessRule>();
        services.AddSingleton<IAuthorizationRule, TeamMembershipRule>();

        // Authorization handler (Scoped - per-request context)
        services.AddScoped<IAuthorizationHandler, ResourceAccessHandler>();

        // Request cache (Scoped - per-request memoization)
        services.AddScoped<RequestCache>();

        // Dataverse access (uses IHttpClientFactory for auth)
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        return services;
    }
}
```

---

#### Documents Module

**File Location**: `src/api/Spe.Bff.Api/Extensions/DocumentsModuleExtensions.cs`

```csharp
/// <summary>
/// Registers SPE file operations and Graph API services (ADR-010: DI Minimalism).
/// </summary>
public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE file store (Scoped - may hold per-request context)
        services.AddScoped<SpeFileStore>();

        // Graph client factory (Singleton - factory pattern, stateless)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Token cache (Singleton - shared cache, no per-request state)
        services.AddSingleton<GraphTokenCache>();

        // Upload session manager (Scoped - per-request context)
        services.AddScoped<UploadSessionManager>();

        return services;
    }
}
```

---

#### Workers Module

**File Location**: `src/api/Spe.Bff.Api/Extensions/WorkersModuleExtensions.cs`

```csharp
/// <summary>
/// Registers background services and job processing (ADR-010: DI Minimalism).
/// </summary>
public static class WorkersModuleExtensions
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services)
    {
        // Background processors (Hosted Service - singleton lifetime)
        services.AddHostedService<DocumentEventProcessor>();
        services.AddHostedService<ServiceBusJobProcessor>();

        // Idempotency service (Singleton - shared state)
        services.AddSingleton<IdempotencyService>();

        return services;
    }
}
```

---

#### Dataverse Module

**File Location**: `src/api/Spe.Bff.Api/Extensions/DataverseModuleExtensions.cs`

```csharp
/// <summary>
/// Registers Dataverse connection (ADR-010: DI Minimalism).
/// Uses Singleton for connection pooling and thread-safety.
/// </summary>
public static class DataverseModuleExtensions
{
    public static IServiceCollection AddDataverseModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Dataverse client (Singleton - connection pooling, thread-safe)
        services.AddSingleton<DataverseServiceClientImpl>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;

            var connectionString =
                $"AuthType=ClientSecret;" +
                $"Url={options.ServiceUrl};" +
                $"ClientId={options.ClientId};" +
                $"ClientSecret={options.ClientSecret};" +
                $"RequireNewInstance=false;"; // Enable connection pooling

            return new DataverseServiceClientImpl(connectionString);
        });

        return services;
    }
}
```

---

### Allowed Interfaces (ONLY These)

| Interface | Justification | Example |
|-----------|--------------|---------|
| `IGraphClientFactory` | Factory pattern - creates different client types (OBO vs app-only) | ✅ Allowed |
| `IAccessDataSource` | Dataverse abstraction seam for testing | ✅ Allowed |
| `IAuthorizationRule` | Rule collection pattern (multiple implementations) | ✅ Allowed |
| `IDistributedCache` | Framework interface (StackExchange.Redis) | ✅ Allowed |
| `IHttpClientFactory` | Framework interface (ASP.NET Core) | ✅ Allowed |

### Forbidden Patterns

```csharp
// ❌ DON'T create interfaces just for DI
public interface ISpeFileStore { }
public class SpeFileStore : ISpeFileStore { }

// ❌ DON'T create service wrappers that add no value
public interface IDocumentService { }
public class DocumentService : IDocumentService
{
    private readonly SpeFileStore _store;

    public async Task<FileDto> GetFileAsync(string id)
    {
        // Just passes through to _store - adds no value
        return await _store.GetFileAsync(id);
    }
}

// ❌ DON'T use Transient lifetime
services.AddTransient<SpeFileStore>(); // Creates new instance every injection!
```

---

### Service Lifetime Guidelines

#### Use **Singleton** For:

| Service | Why Singleton? |
|---------|---------------|
| `DataverseServiceClientImpl` | Connection pooling, thread-safe SDK |
| `GraphTokenCache` | Shared cache state across all requests |
| `IGraphClientFactory` | Factory pattern, stateless |
| `AuthorizationService` | Stateless rule evaluation |
| `IAuthorizationRule` implementations | Stateless logic |

#### Use **Scoped** For:

| Service | Why Scoped? |
|---------|-------------|
| `SpeFileStore` | May hold per-request logging context |
| `UploadSessionManager` | Per-request upload tracking |
| `ResourceAccessHandler` | Per-request authorization context |
| `IAccessDataSource` | Per-request Dataverse queries |
| `RequestCache` | Per-request memoization |

#### **NEVER** Use Transient:

| Why NOT Transient? |
|-------------------|
| ❌ Creates new instance on **every injection** (not per-request) |
| ❌ If service injected 3 times in request, 3 instances created |
| ❌ No benefit over Scoped in ASP.NET Core |
| ❌ Performance penalty for no gain |

---

## Quick Reference: File Paths

| Component | File Path |
|-----------|-----------|
| **SpeFileStore** | `src/api/Spe.Bff.Api/Storage/SpeFileStore.cs` |
| **GraphClientFactory** | `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` |
| **GraphTokenCache** | `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs` |
| **DataverseServiceClientImpl** | `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` |
| **AuthorizationService** | `src/shared/Spaarke.Core/Auth/AuthorizationService.cs` |
| **SpaarkeCoreExtensions** | `src/api/Spe.Bff.Api/Extensions/SpaarkeCoreExtensions.cs` |
| **DocumentsModuleExtensions** | `src/api/Spe.Bff.Api/Extensions/DocumentsModuleExtensions.cs` |
| **WorkersModuleExtensions** | `src/api/Spe.Bff.Api/Extensions/WorkersModuleExtensions.cs` |
| **DataverseModuleExtensions** | `src/api/Spe.Bff.Api/Extensions/DataverseModuleExtensions.cs` |

---

## Quick Reference: Configuration

### appsettings.json

```json
{
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=...BFF-API-ClientSecret)",
  "DEFAULT_CT_ID": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",

  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  },

  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=...SPRK-DEV-DATAVERSE-URL)",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=...BFF-API-ClientSecret)"
  },

  "Redis": {
    "Enabled": true,
    "InstanceName": "sdap-prod:",
    "DefaultExpirationMinutes": 60
  },

  "ConnectionStrings": {
    "Redis": "@Microsoft.KeyVault(SecretUri=...Redis-ConnectionString)",
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=...ServiceBus-ConnectionString)"
  }
}
```

---

## Summary: Compliance Checklist

Before merging any PR, verify:

- [ ] **ADR-007**: `SpeFileStore` is concrete class (no interface)
- [ ] **ADR-007**: All methods return DTOs (never Graph SDK types)
- [ ] **ADR-009**: Token caching uses `IDistributedCache` only
- [ ] **ADR-009**: Cache keys include version suffix (`:v1`)
- [ ] **ADR-009**: Cache TTLs explicitly set
- [ ] **ADR-010**: Feature modules used for DI organization
- [ ] **ADR-010**: Services registered as concrete (except allowed interfaces)
- [ ] **ADR-010**: Correct lifetimes (Singleton vs Scoped, never Transient)
- [ ] **ADR-010**: Program.cs under 50 lines of DI code

---

**Related Documents**:
- [SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md](../../SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md) - Complete architecture reference
- [TARGET-ARCHITECTURE.md](./TARGET-ARCHITECTURE.md) - Target architecture diagrams
- [PHASE-1.md](./PHASE-1.md) - Implementation phases

---

**Last Review**: 2025-10-13 by Architecture Team
**Next Review**: After Phase 1 completion
