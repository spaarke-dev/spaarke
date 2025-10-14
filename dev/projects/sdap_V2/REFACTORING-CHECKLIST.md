# SDAP Refactoring Checklist (AI Vibe Coding Guide)

**Purpose**: Step-by-step refactoring guide with pattern references
**Usage**: Complete phases sequentially, use patterns for implementation
**Last Updated**: 2025-10-13

---

## 🎯 Quick Navigation

| Phase | Focus | Duration | Risk | Pattern Files |
|-------|-------|----------|------|---------------|
| [Phase 1](#phase-1-configuration--critical-fixes) | Config & Critical Fixes | 4-6h | Low | [service-dataverse-connection.md](patterns/service-dataverse-connection.md) |
| [Phase 2](#phase-2-simplify-service-layer) | Service Layer Consolidation | 12-16h | Medium | [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md) |
| [Phase 3](#phase-3-feature-module-pattern) | DI Organization | 4-6h | Low | [di-feature-module.md](patterns/di-feature-module.md) |
| [Phase 4](#phase-4-token-caching) | Token Caching | 4-6h | Low | [service-graph-token-cache.md](patterns/service-graph-token-cache.md) |

**Total Estimated Time**: ~40 hours (1 week sprint)

---

## Phase 1: Configuration & Critical Fixes

**Goal**: Fix app registration config, remove UAMI logic, fix ServiceClient lifetime
**Duration**: 4-6 hours
**Risk**: Low (config-only changes)

### Pre-Flight Checks

```bash
# Create feature branch
- [ ] git checkout -b refactor/phase-1-critical-fixes

# Document baseline
- [ ] Run: dotnet test --collect:"XPlat Code Coverage"
- [ ] Record: Test count, pass rate, coverage %
- [ ] Run: git status (should be clean)

# Verify current state
- [ ] Application starts without errors
- [ ] Health check passes: GET /healthz
- [ ] Current API_APP_ID value: __________ (document current value)
```

### Task 1.1: Fix App Registration Configuration

**Pattern**: [service-dataverse-connection.md](patterns/service-dataverse-connection.md)

```bash
Files to Edit:
- [ ] src/api/Spe.Bff.Api/appsettings.json
- [ ] src/api/Spe.Bff.Api/appsettings.Development.json
```

**Changes**:
```json
// OLD (WRONG)
{
  "API_APP_ID": "170c98e1-...", // ❌ PCF client app ID
  "AzureAd": {
    "ClientId": "170c98e1-..." // ❌ Wrong app
  },
  "Dataverse": {
    "ClientId": "170c98e1-..." // ❌ Wrong app
  }
}

// NEW (CORRECT)
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c", // ✅ BFF API app ID
  "AzureAd": {
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c", // ✅ Correct
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  },
  "Dataverse": {
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c" // ✅ Correct
  }
}
```

**Checklist**:
- [ ] All `ClientId` values match `API_APP_ID`
- [ ] `Audience` matches `api://` + `API_APP_ID`
- [ ] No references to `170c98e1` (old PCF client ID)
- [ ] Changes applied to both `appsettings.json` and `appsettings.Development.json`

### Task 1.2: Remove UAMI Logic

**Pattern**: [service-graph-client-factory.md](patterns/service-graph-client-factory.md)
**Anti-Pattern**: Avoid complexity, keep single auth path

```bash
File to Edit:
- [ ] src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
```

**Changes**:
```csharp
// ❌ REMOVE: UAMI_CLIENT_ID field and logic
private readonly string? _uamiClientId;

// ❌ REMOVE: Constructor parameter
public GraphClientFactory(..., IConfiguration configuration)
{
    _uamiClientId = configuration["UAMI_CLIENT_ID"]; // DELETE THIS
}

// ❌ REMOVE: Conditional logic
if (!string.IsNullOrEmpty(_uamiClientId))
{
    // Managed Identity logic
}
else
{
    // Client secret logic
}

// ✅ KEEP ONLY: Client secret path
_cca = ConfidentialClientApplicationBuilder
    .Create(apiAppId)
    .WithClientSecret(clientSecret)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .Build();
```

**Checklist**:
- [ ] `_uamiClientId` field removed
- [ ] All UAMI conditional branches removed
- [ ] Only client secret authentication remains
- [ ] Constructor simplified (no `IConfiguration` needed for UAMI)
- [ ] No references to `UAMI_CLIENT_ID` in entire solution

### Task 1.3: Fix ServiceClient Lifetime

**Pattern**: [service-dataverse-connection.md](patterns/service-dataverse-connection.md)
**Anti-Pattern**: [anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)

```bash
File to Edit:
- [ ] src/api/Spe.Bff.Api/Program.cs (or Extensions/DocumentsModule.Extensions.cs)
```

**Changes**:
```csharp
// ❌ OLD (WRONG): Scoped lifetime
builder.Services.AddScoped<DataverseServiceClientImpl>();

// ✅ NEW (CORRECT): Singleton lifetime
builder.Services.AddSingleton<DataverseServiceClientImpl>(sp =>
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
```

**Checklist**:
- [ ] Registration changed to `AddSingleton`
- [ ] Connection string includes `RequireNewInstance=false`
- [ ] Options pattern used for configuration
- [ ] No captive dependencies (Singleton doesn't inject Scoped services)

### Validation (Phase 1)

```bash
# Build and test
- [ ] dotnet build (0 warnings)
- [ ] dotnet test (all tests pass)

# Manual testing
- [ ] Start application: dotnet run
- [ ] Test health check: GET https://localhost:5001/healthz
- [ ] Test Dataverse health: GET https://localhost:5001/healthz/dataverse
- [ ] Check logs: No new errors or warnings

# Performance check
- [ ] Health check response time: ____ms (should be <100ms, was ~500ms)
- [ ] No 500ms initialization overhead on each request
```

### Commit

```bash
- [ ] git add .
- [ ] git commit -m "refactor(phase-1): fix app registration config and ServiceClient lifetime

- Fix API_APP_ID to use BFF API app (1e40baad...)
- Remove UAMI_CLIENT_ID logic from GraphClientFactory
- Change DataverseServiceClientImpl to Singleton lifetime
- Enable connection pooling (RequireNewInstance=false)

ADR Compliance: ADR-010 (Singleton for expensive resources)
Performance: Eliminates 500ms initialization overhead per request"
```

---

## Phase 2: Simplify Service Layer

**Goal**: Consolidate SPE storage, remove unnecessary interfaces, simplify authorization
**Duration**: 12-16 hours
**Risk**: Medium (affects multiple endpoints and tests)

### Pre-Flight Checks

```bash
- [ ] Phase 1 validated and committed
- [ ] Create test plan for affected endpoints:
  - [ ] POST /api/obo/upload
  - [ ] GET /api/obo/download/{id}
  - [ ] GET /api/containers
  - [ ] POST /api/upload-sessions
```

### Task 2.1: Create SpeFileStore

**Pattern**: [endpoint-file-upload.md](patterns/endpoint-file-upload.md), [dto-file-upload-result.md](patterns/dto-file-upload-result.md)
**Anti-Pattern**: [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md), [anti-pattern-leaking-sdk-types.md](patterns/anti-pattern-leaking-sdk-types.md)

```bash
Files to Create:
- [ ] src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
- [ ] src/api/Spe.Bff.Api/Models/FileUploadResult.cs
- [ ] src/api/Spe.Bff.Api/Models/FileDownloadResult.cs
```

**Implementation**:

```csharp
// 1. Create DTOs first (5 minutes each)
// Pattern: patterns/dto-file-upload-result.md

public record FileUploadResult
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public long Size { get; init; }
    public string? MimeType { get; init; }
    public string? WebUrl { get; init; }
    public DateTimeOffset? CreatedDateTime { get; init; }
}

// 2. Create SpeFileStore (30 minutes)
// Consolidate logic from SpeResourceStore + OboSpeService

namespace Spe.Bff.Api.Storage;

public class SpeFileStore // ✅ Concrete class, no interface
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<SpeFileStore> _logger;

    public SpeFileStore(
        IGraphClientFactory graphClientFactory,
        ILogger<SpeFileStore> logger)
    {
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    public async Task<FileUploadResult> UploadFileAsync(
        string containerId,
        string fileName,
        Stream content,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        // Consolidate logic from old SpeResourceStore.UploadAsync
        // and OboSpeService.UploadFileAsync

        var graphClient = await _graphClientFactory
            .CreateOnBehalfOfClientAsync(userToken);

        var uploadSession = await graphClient
            .Drives[containerId]
            .Items["root"]
            .ItemWithPath(fileName)
            .Content
            .Request()
            .PutAsync<DriveItem>(content, cancellationToken);

        // ✅ Map to DTO (don't return DriveItem!)
        return new FileUploadResult
        {
            ItemId = uploadSession.Id!,
            Name = uploadSession.Name!,
            Size = uploadSession.Size ?? 0,
            MimeType = uploadSession.File?.MimeType,
            WebUrl = uploadSession.WebUrl,
            CreatedDateTime = uploadSession.CreatedDateTime
        };
    }

    // Add DownloadFileAsync, DeleteFileAsync, etc.
}
```

**Checklist**:
- [ ] `SpeFileStore` created as concrete class (no interface)
- [ ] All methods return DTOs (never `DriveItem`, `Entity`)
- [ ] Consolidates logic from `SpeResourceStore` + `OboSpeService`
- [ ] Injects `IGraphClientFactory` (OK to use interface for factory)
- [ ] Methods: `UploadFileAsync`, `DownloadFileAsync`, `DeleteFileAsync`, `GetFileMetadataAsync`

### Task 2.2: Update Endpoints

**Pattern**: [endpoint-file-upload.md](patterns/endpoint-file-upload.md)

```bash
Files to Edit:
- [ ] src/api/Spe.Bff.Api/Api/OBOEndpoints.cs
- [ ] src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs
- [ ] src/api/Spe.Bff.Api/Api/UploadEndpoints.cs
```

**Changes**:
```csharp
// ❌ OLD
private static async Task<IResult> UploadFile(
    IResourceStore resourceStore, // Interface
    ...)
{
    var result = await resourceStore.UploadAsync(...);
    return Results.Ok(result); // Returns Graph SDK type
}

// ✅ NEW
private static async Task<IResult> UploadFile(
    SpeFileStore fileStore, // Concrete class
    string containerId,
    string fileName,
    HttpRequest request,
    CancellationToken cancellationToken)
{
    var token = ExtractBearerToken(request);
    var result = await fileStore.UploadFileAsync(
        containerId,
        fileName,
        request.Body,
        token,
        cancellationToken);

    return Results.Ok(result); // Returns FileUploadResult DTO
}
```

**Checklist**:
- [ ] All endpoints inject `SpeFileStore` (not `IResourceStore`, `ISpeService`)
- [ ] All endpoint return types are DTOs
- [ ] No `DriveItem`, `Entity`, or SDK types in signatures
- [ ] Error handling updated (use pattern from [error-handling-standard.md](patterns/error-handling-standard.md))

### Task 2.3: Update DI Registrations

**Pattern**: [di-feature-module.md](patterns/di-feature-module.md)

```bash
File to Edit:
- [ ] src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs (or Program.cs)
```

**Changes**:
```csharp
// ❌ OLD
services.AddScoped<IResourceStore, SpeResourceStore>();
services.AddScoped<ISpeService, OboSpeService>();

// ✅ NEW
services.AddScoped<SpeFileStore>(); // Concrete class
```

**Checklist**:
- [ ] `SpeFileStore` registered as concrete class
- [ ] Old interfaces removed from registration
- [ ] Scoped lifetime (creates new instance per request)

### Task 2.4: Update Tests

```bash
Files to Edit:
- [ ] tests/Spe.Bff.Api.Tests/**/*SpeFileStoreTests.cs
- [ ] tests/Spe.Bff.Api.Tests/**/*OBOEndpointsTests.cs
```

**Changes**:
```csharp
// ❌ OLD: Mocking interface
var mockResourceStore = new Mock<IResourceStore>();
mockResourceStore.Setup(x => x.UploadAsync(...)).ReturnsAsync(...);

// ✅ NEW: Mock at infrastructure boundary
var mockGraphFactory = new Mock<IGraphClientFactory>();
mockGraphFactory
    .Setup(x => x.CreateOnBehalfOfClientAsync(It.IsAny<string>()))
    .ReturnsAsync(mockGraphClient);

var fileStore = new SpeFileStore(mockGraphFactory.Object, logger);
var result = await fileStore.UploadFileAsync(...);
```

**Checklist**:
- [ ] Tests mock `IGraphClientFactory` (infrastructure boundary)
- [ ] Tests use real `SpeFileStore` instance
- [ ] No mocks of concrete classes
- [ ] All tests pass

### Task 2.5: Simplify Authorization

**Pattern**: Use `AuthorizationService` from `Spaarke.Core`

```bash
Files to Check:
- [ ] Search for: IDataverseSecurityService, IUacService
- [ ] Verify: No duplicate authorization logic
```

**Changes**:
```csharp
// ❌ OLD: Wrapper services
services.AddScoped<IDataverseSecurityService, DataverseSecurityService>();
services.AddScoped<IUacService, UacService>();

// ✅ NEW: Use AuthorizationService directly
services.AddSingleton<AuthorizationService>(); // From Spaarke.Core
services.AddScoped<IAccessDataSource, DataverseAccessDataSource>(); // Required seam

// Endpoints use AuthorizationService
private static async Task<IResult> UploadFile(
    SpeFileStore fileStore,
    AuthorizationService authz, // Direct injection
    ...)
{
    var canUpload = await authz.IsAuthorizedAsync(userId, "canuploadfiles");
    if (!canUpload) return Results.Forbid();

    var result = await fileStore.UploadFileAsync(...);
    return Results.Ok(result);
}
```

**Checklist**:
- [ ] Wrapper services removed
- [ ] Endpoints use `AuthorizationService` directly
- [ ] `IAccessDataSource` kept (required seam per ADR-003)

### Task 2.6: Delete Obsolete Files

```bash
# ONLY AFTER VALIDATION PASSES

Files to Delete:
- [ ] src/api/Spe.Bff.Api/Services/IResourceStore.cs
- [ ] src/api/Spe.Bff.Api/Services/SpeResourceStore.cs
- [ ] src/api/Spe.Bff.Api/Services/ISpeService.cs
- [ ] src/api/Spe.Bff.Api/Services/OboSpeService.cs
- [ ] src/api/Spe.Bff.Api/Services/IDataverseSecurityService.cs
- [ ] src/api/Spe.Bff.Api/Services/DataverseSecurityService.cs
- [ ] src/api/Spe.Bff.Api/Services/IUacService.cs
- [ ] src/api/Spe.Bff.Api/Services/UacService.cs

# Delete corresponding test files
- [ ] tests/**/*ResourceStore*.cs
- [ ] tests/**/*SpeService*.cs
- [ ] tests/**/*SecurityService*.cs
- [ ] tests/**/*UacService*.cs
```

### Validation (Phase 2)

```bash
# Build and test
- [ ] dotnet build (0 warnings)
- [ ] dotnet test (all tests pass)

# Manual endpoint testing
- [ ] POST /api/obo/upload (upload small file)
- [ ] GET /api/obo/download/{id} (download uploaded file)
- [ ] GET /api/containers (list containers)
- [ ] DELETE /api/files/{id} (delete file)

# Verify DTOs
- [ ] Upload response is FileUploadResult (not DriveItem)
- [ ] Download response includes proper content-type
- [ ] No Graph SDK types in API responses

# Code quality check
- [ ] No references to old interfaces: IResourceStore, ISpeService, IOboSpeService
- [ ] No DriveItem, Entity types in endpoint signatures
- [ ] All services registered as concrete classes (except IGraphClientFactory)
```

### Commit

```bash
- [ ] git add .
- [ ] git commit -m "refactor(phase-2): consolidate SPE storage to SpeFileStore per ADR-007

- Create SpeFileStore concrete class (no interface)
- Consolidate SpeResourceStore + OboSpeService logic
- Create DTOs: FileUploadResult, FileDownloadResult
- Update all endpoints to use SpeFileStore directly
- Remove unnecessary interfaces and wrapper services
- Simplify authorization (use AuthorizationService directly)

ADR Compliance: ADR-007 (SPE Storage Seam Minimalism), ADR-010 (Register Concretes)
Benefits: 50% fewer layers (3 instead of 6), easier to debug and test"

- [ ] git commit -m "refactor(phase-2): remove obsolete storage abstractions

- Delete IResourceStore, SpeResourceStore, ISpeService, OboSpeService
- Delete IDataverseSecurityService, IUacService and implementations
- Remove corresponding test files
- Update test strategy to mock at infrastructure boundaries

Files deleted: 8 service files + tests"
```

---

## Phase 3: Feature Module Pattern

**Goal**: Organize DI registrations into feature modules, simplify Program.cs
**Duration**: 4-6 hours
**Risk**: Low (DI organization only, no logic changes)

### Pre-Flight Checks

```bash
- [ ] Phase 2 validated and committed
- [ ] Review current Program.cs line count: _____ lines
- [ ] Target: Reduce DI registrations to ~20 lines
```

### Task 3.1: Create Feature Modules

**Pattern**: [di-feature-module.md](patterns/di-feature-module.md)
**Anti-Pattern**: [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md), [anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)

```bash
Files to Create:
- [ ] src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs
- [ ] src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
- [ ] src/api/Spe.Bff.Api/Extensions/WorkersModule.Extensions.cs
```

**SpaarkeCore.Extensions.cs** (Authorization, Caching, Core Services):

```csharp
namespace Spe.Bff.Api.Extensions;

public static class SpaarkeCoreExtensions
{
    public static IServiceCollection AddSpaarkeCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Authorization
        services.AddSingleton<AuthorizationService>();
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        // Register all IAuthorizationRule implementations
        services.Scan(scan => scan
            .FromAssemblyOf<IAuthorizationRule>()
            .AddClasses(classes => classes.AssignableTo<IAuthorizationRule>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        // Caching
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
            options.InstanceName = "sdap:";
        });

        services.AddScoped<RequestCache>();

        return services;
    }
}
```

**DocumentsModule.Extensions.cs** (SPE, Graph, Dataverse):

```csharp
namespace Spe.Bff.Api.Extensions;

public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(
        this IServiceCollection services)
    {
        // ✅ SPE Storage (Concrete class)
        services.AddScoped<SpeFileStore>();

        // ✅ Graph API (Factory pattern - OK to use interface)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        services.AddTransient<GraphHttpMessageHandler>();
        services.AddScoped<UploadSessionManager>();

        // ✅ Dataverse (Singleton - expensive resource)
        services.AddSingleton<DataverseServiceClientImpl>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;

            var connectionString =
                $"AuthType=ClientSecret;" +
                $"Url={options.ServiceUrl};" +
                $"ClientId={options.ClientId};" +
                $"ClientSecret={options.ClientSecret};" +
                $"RequireNewInstance=false;";

            return new DataverseServiceClientImpl(connectionString);
        });

        return services;
    }
}
```

**WorkersModule.Extensions.cs** (Background Services):

```csharp
namespace Spe.Bff.Api.Extensions;

public static class WorkersModuleExtensions
{
    public static IServiceCollection AddWorkersModule(
        this IServiceCollection services)
    {
        // Background services
        services.AddHostedService<DocumentEventProcessor>();
        services.AddHostedService<ServiceBusJobProcessor>();

        // Job handlers (Scoped)
        services.AddScoped<DocumentCreatedJobHandler>();
        services.AddScoped<DocumentUpdatedJobHandler>();

        // Idempotency
        services.AddSingleton<IdempotencyService>();

        return services;
    }
}
```

**Checklist**:
- [ ] SpaarkeCore.Extensions.cs created (authorization, caching)
- [ ] DocumentsModule.Extensions.cs created (SPE, Graph, Dataverse)
- [ ] WorkersModule.Extensions.cs created (background services)
- [ ] All services registered as concrete classes (except factories)
- [ ] Lifetime hierarchy followed (Singleton → Singleton, Scoped → Any)
- [ ] No captive dependencies

### Task 3.2: Refactor Program.cs

```bash
File to Edit:
- [ ] src/api/Spe.Bff.Api/Program.cs
```

**Changes**:
```csharp
// ❌ OLD: 80+ lines of scattered registrations
builder.Services.AddScoped<IResourceStore, SpeResourceStore>();
builder.Services.AddScoped<ISpeService, OboSpeService>();
builder.Services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
builder.Services.AddScoped<DataverseServiceClientImpl>();
builder.Services.AddSingleton<AuthorizationService>();
// ... 75 more lines

// ✅ NEW: ~20 lines with feature modules
var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Feature Modules
// ============================================================================
builder.Services.AddSpaarkeCore(builder.Configuration);
builder.Services.AddDocumentsModule();
builder.Services.AddWorkersModule();

// ============================================================================
// Options Pattern (Strongly-Typed Configuration)
// ============================================================================
builder.Services.AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GraphResilienceOptions>()
    .Bind(builder.Configuration.GetSection(GraphResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ============================================================================
// ASP.NET Core Framework Services
// ============================================================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();
// ... middleware and endpoints
```

**Checklist**:
- [ ] Feature modules called at top of DI section
- [ ] Options pattern used for configuration
- [ ] Framework services clearly separated
- [ ] ~20 lines of DI code (75% reduction)
- [ ] Clear comments separating sections

### Validation (Phase 3)

```bash
# Build and test
- [ ] dotnet build (0 warnings)
- [ ] dotnet test (all tests pass)

# Application startup
- [ ] dotnet run (starts without errors)
- [ ] No circular dependency exceptions
- [ ] All health checks pass: GET /healthz

# Verify DI resolution
- [ ] All endpoints resolve dependencies (no DI errors)
- [ ] Background services start successfully
- [ ] Check logs: No DI resolution failures

# Code quality
- [ ] Program.cs DI lines: _____ (target: ~20)
- [ ] All services in feature modules (not scattered)
- [ ] Clear feature boundaries (Core, Documents, Workers)
```

### Commit

```bash
- [ ] git add .
- [ ] git commit -m "refactor(phase-3): implement feature module pattern per ADR-010

- Create SpaarkeCore.Extensions.cs (authorization, caching)
- Create DocumentsModule.Extensions.cs (SPE, Graph, Dataverse)
- Create WorkersModule.Extensions.cs (background services)
- Simplify Program.cs to ~20 lines of DI code

ADR Compliance: ADR-010 (Feature Modules)
Benefits: 75% reduction in Program.cs lines, clear feature boundaries"
```

---

## Phase 4: Token Caching

**Goal**: Implement Redis-based OBO token caching for 97% latency reduction
**Duration**: 4-6 hours
**Risk**: Low (additive change, fallback to non-cached path)

### Pre-Flight Checks

```bash
- [ ] Phase 3 validated and committed
- [ ] Redis connection string configured in appsettings.json
- [ ] Test Redis connection: redis-cli ping (should return PONG)
- [ ] Baseline latency: Measure current OBO exchange time (___ms)
```

### Task 4.1: Create GraphTokenCache

**Pattern**: [service-graph-token-cache.md](patterns/service-graph-token-cache.md)

```bash
File to Create:
- [ ] src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
```

**Implementation** (Copy from pattern file):

```csharp
namespace Spe.Bff.Api.Services;

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

    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        try
        {
            var cachedToken = await _cache.GetStringAsync($"sdap:token:{tokenHash}");

            if (cachedToken != null)
            {
                _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..8]);
            }
            else
            {
                _logger.LogDebug("Cache MISS for token hash {Hash}...", tokenHash[..8]);
            }

            return cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis cache read failed, continuing without cache");
            return null; // Graceful degradation
        }
    }

    public async Task SetTokenAsync(string tokenHash, string token, TimeSpan ttl)
    {
        try
        {
            await _cache.SetStringAsync(
                $"sdap:token:{tokenHash}",
                token,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });

            _logger.LogDebug("Cached token with hash {Hash}..., TTL: {TTL}",
                tokenHash[..8], ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis cache write failed, continuing without cache");
            // Don't throw - caching is optional
        }
    }

    public string ComputeTokenHash(string userToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }
}
```

**Checklist**:
- [ ] `GraphTokenCache` created with Redis backend
- [ ] `GetTokenAsync` with graceful failure (returns null on error)
- [ ] `SetTokenAsync` with 55-minute TTL (5-min buffer)
- [ ] `ComputeTokenHash` uses SHA256
- [ ] Logging for cache hits/misses
- [ ] Error handling doesn't throw (graceful degradation)

### Task 4.2: Integrate Cache with GraphClientFactory

**Pattern**: [service-graph-client-factory.md](patterns/service-graph-client-factory.md)

```bash
File to Edit:
- [ ] src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
```

**Changes**:
```csharp
public class GraphClientFactory : IGraphClientFactory
{
    private readonly GraphTokenCache _tokenCache; // ✨ NEW

    public GraphClientFactory(
        ...,
        GraphTokenCache tokenCache) // ✨ NEW
    {
        _tokenCache = tokenCache;
    }

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // ✨ NEW: Step 1 - Check cache first
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
        var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedToken != null)
        {
            _logger.LogDebug("Using cached Graph token");
            return CreateGraphClientWithToken(cachedToken);
        }

        // ✨ Step 2 - Cache miss, perform OBO exchange
        _logger.LogDebug("Cache miss, performing OBO exchange");

        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[] {
                "https://graph.microsoft.com/Sites.FullControl.All",
                "https://graph.microsoft.com/Files.ReadWrite.All"
            },
            userAssertion: new UserAssertion(userAccessToken))
            .ExecuteAsync();

        // ✨ Step 3 - Cache the token (55-min TTL, 5-min buffer)
        await _tokenCache.SetTokenAsync(
            tokenHash,
            result.AccessToken,
            TimeSpan.FromMinutes(55));

        return CreateGraphClientWithToken(result.AccessToken);
    }

    private GraphServiceClient CreateGraphClientWithToken(string accessToken)
    {
        var authProvider = new DelegateAuthenticationProvider((request) =>
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            return Task.CompletedTask;
        });

        return new GraphServiceClient(authProvider, _httpMessageHandler);
    }
}
```

**Checklist**:
- [ ] `GraphTokenCache` injected into constructor
- [ ] Cache checked before OBO exchange
- [ ] Cache miss performs OBO exchange (existing logic)
- [ ] Token cached with 55-minute TTL
- [ ] Logging added for cache hits/misses
- [ ] Graceful degradation if cache fails

### Task 4.3: Register Cache in DI

```bash
File to Edit:
- [ ] src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
```

**Changes**:
```csharp
public static IServiceCollection AddDocumentsModule(
    this IServiceCollection services)
{
    // Storage
    services.AddScoped<SpeFileStore>();

    // Graph API
    services.AddSingleton<GraphTokenCache>(); // ✨ NEW
    services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

    // ... rest of registrations
}
```

**Checklist**:
- [ ] `GraphTokenCache` registered as Singleton
- [ ] Registered before `GraphClientFactory` (dependency order)
- [ ] No captive dependencies (Singleton → Singleton)

### Task 4.4: Add Cache Metrics (Optional)

```bash
File to Create (optional):
- [ ] src/api/Spe.Bff.Api/Telemetry/CacheMetrics.cs
```

**Implementation**:
```csharp
public class CacheMetrics
{
    private static readonly Counter<long> _cacheHits =
        Meter.CreateCounter<long>("sdap.cache.hits");

    private static readonly Counter<long> _cacheMisses =
        Meter.CreateCounter<long>("sdap.cache.misses");

    public static void RecordHit() => _cacheHits.Add(1);
    public static void RecordMiss() => _cacheMisses.Add(1);
}
```

### Validation (Phase 4)

```bash
# Build and test
- [ ] dotnet build (0 warnings)
- [ ] dotnet test (all tests pass)

# Manual cache testing sequence
- [ ] Clear Redis: redis-cli FLUSHDB
- [ ] Request 1 (user A): POST /api/obo/upload
  - [ ] Check logs: "Cache MISS", "performing OBO exchange"
  - [ ] Record latency: ___ms
- [ ] Request 2 (same user A): POST /api/obo/upload
  - [ ] Check logs: "Cache HIT", "Using cached Graph token"
  - [ ] Record latency: ___ms (should be ~195ms faster)
- [ ] Request 3 (different user B): POST /api/obo/upload
  - [ ] Check logs: "Cache MISS" (different user token)

# Verify Redis contents
- [ ] redis-cli KEYS "sdap:token:*"
- [ ] Should see cached tokens (one per user)
- [ ] redis-cli TTL "sdap:token:{key}"
- [ ] Should see ~3300 seconds (55 minutes)

# Performance validation
- [ ] Cache hit latency: ___ms (target: <10ms overhead)
- [ ] Cache miss latency: ___ms (same as before, ~200ms)
- [ ] After warmup (20 requests): Cache hit rate ___% (target: >90%)

# Graceful degradation test
- [ ] Stop Redis: docker stop redis (or equivalent)
- [ ] Make API request: POST /api/obo/upload
- [ ] Should succeed (fallback to non-cached OBO)
- [ ] Check logs: "Redis cache read failed, continuing without cache"
- [ ] Restart Redis: docker start redis
```

### Commit

```bash
- [ ] git add .
- [ ] git commit -m "refactor(phase-4): implement Graph token caching per ADR-009

- Create GraphTokenCache with Redis backend
- Integrate cache into GraphClientFactory.CreateOnBehalfOfClientAsync
- Add cache hit/miss logging
- Configure 55-minute TTL (5-minute buffer before expiration)
- Implement graceful degradation (fallback to OBO on cache failure)

ADR Compliance: ADR-009 (Redis-First Caching)
Performance: 97% latency reduction (5ms vs 200ms), 95% cache hit rate expected"
```

---

## Final Validation & PR

### Integration Testing

```bash
End-to-End Tests:
- [ ] Upload file: POST /api/obo/upload
  - [ ] Verify file appears in SharePoint container
  - [ ] Verify Dataverse record created
  - [ ] Check response: FileUploadResult DTO

- [ ] Download file: GET /api/obo/download/{id}
  - [ ] Verify correct file content
  - [ ] Verify correct content-type header

- [ ] List containers: GET /api/containers
  - [ ] Verify list of SPE containers

- [ ] Delete file: DELETE /api/files/{id}
  - [ ] Verify file removed from SharePoint
  - [ ] Verify Dataverse record updated

- [ ] Background job: Trigger Service Bus message
  - [ ] Verify DocumentEventProcessor processes message
  - [ ] Check idempotency (duplicate message ignored)

- [ ] Health checks:
  - [ ] GET /healthz (overall health)
  - [ ] GET /healthz/dataverse (Dataverse connection)
  - [ ] GET /healthz/redis (Redis connection)
```

### Performance Testing

```bash
Baseline vs. Refactored Comparison:

Operation: File Upload (small file, 1MB)
- [ ] Baseline latency: _____ms (from Phase 1 notes)
- [ ] Refactored (cold cache): _____ms
- [ ] Refactored (warm cache): _____ms
- [ ] Improvement: _____%

Operation: Dataverse Query
- [ ] Baseline latency: _____ms (with Scoped ServiceClient)
- [ ] Refactored latency: _____ms (with Singleton)
- [ ] Improvement: _____%

Cache Statistics (after 100 requests):
- [ ] Cache hit rate: ____% (target: >90%)
- [ ] Avg hit latency: ____ms (target: <10ms)
- [ ] Avg miss latency: ____ms (should be ~200ms)

Memory Test:
- [ ] Run load test (100 req/sec for 10 minutes)
- [ ] Monitor memory: dotnet-counters monitor --process-id {pid}
- [ ] No memory leaks (stable memory after 5 minutes)
```

### Code Quality Checklist

```bash
Code Review:
- [ ] No TODO comments in production code
- [ ] No commented-out code blocks
- [ ] No magic strings (use constants or Options pattern)
- [ ] Consistent error handling patterns (see [error-handling-standard.md](patterns/error-handling-standard.md))
- [ ] All public methods have XML documentation
- [ ] No regions or #if DEBUG blocks

ADR Compliance Check:
- [ ] ADR-007: SpeFileStore returns DTOs only (no DriveItem, Entity)
- [ ] ADR-007: No generic storage interfaces (specific to SPE)
- [ ] ADR-009: Redis-only caching (no hybrid L1/L2)
- [ ] ADR-009: 55-minute TTL for tokens
- [ ] ADR-010: Concrete classes registered (only 3 interfaces)
- [ ] ADR-010: Feature modules used
- [ ] ADR-010: ~20 lines of DI code in Program.cs

Anti-Pattern Check:
- [ ] No interface proliferation: ✅ [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md)
- [ ] No SDK type leakage: ✅ [anti-pattern-leaking-sdk-types.md](patterns/anti-pattern-leaking-sdk-types.md)
- [ ] No captive dependencies: ✅ [anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)
- [ ] No God services (services have clear, focused responsibilities)
- [ ] No pass-through services (no unnecessary wrappers)
```

### Documentation Updates

```bash
- [ ] Update architecture diagrams:
  - [ ] Dependency flow diagram (3 layers instead of 6)
  - [ ] DI organization diagram (feature modules)

- [ ] Update API documentation:
  - [ ] Endpoint signatures (DTOs instead of SDK types)
  - [ ] Authentication flow
  - [ ] Rate limiting policies

- [ ] Update deployment guide:
  - [ ] Redis connection string required
  - [ ] Key Vault secrets needed
  - [ ] App registration configuration

- [ ] Update configuration reference:
  - [ ] appsettings.json schema
  - [ ] Environment variables
  - [ ] Feature flags (if any)

- [ ] Update pattern library:
  - [ ] Mark patterns as "validated in production"
  - [ ] Add performance metrics to patterns
```

### Pull Request Checklist

```bash
PR Creation:
- [ ] Branch: refactor/adr-compliance
- [ ] Target: main (or develop)
- [ ] Title: "Refactor: ADR Compliance - Simplify architecture and add token caching"

PR Description:
- [ ] Summary of changes (4 phases)
- [ ] Before/after metrics table:
  - Interface count: 10 → 3
  - Call chain depth: 6 → 3
  - Program.cs DI lines: 80+ → ~20
  - Upload latency: 700ms → 150ms (78% faster)
  - Dataverse query: 650ms → 50ms (92% faster)

- [ ] Link to ADRs:
  - ADR-007 (SPE Storage Seam Minimalism)
  - ADR-009 (Redis-First Caching)
  - ADR-010 (DI Minimalism)

- [ ] Link to pattern library: [patterns/README.md](patterns/README.md)
- [ ] Link to anti-patterns: [ANTI-PATTERNS.md](ANTI-PATTERNS.md)
- [ ] Breaking changes: None (internal refactoring only)
- [ ] Migration notes: Update appsettings.json, configure Redis

PR Checklist Template:
```markdown
## Refactoring Summary

This PR refactors the SDAP BFF API to comply with ADR-007, ADR-009, and ADR-010.

### Changes
- ✅ Phase 1: Fixed app registration config, removed UAMI, fixed ServiceClient lifetime
- ✅ Phase 2: Consolidated SPE storage to SpeFileStore, removed 8 unnecessary interfaces
- ✅ Phase 3: Organized DI into feature modules, simplified Program.cs
- ✅ Phase 4: Implemented Redis-based OBO token caching

### Metrics
| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Interface Count | 10 | 3 | 70% reduction |
| Call Chain Depth | 6 layers | 3 layers | 50% reduction |
| Program.cs DI Lines | 80+ | ~20 | 75% reduction |
| File Upload Latency | 700ms | 150ms | 78% faster |
| Dataverse Query | 650ms | 50ms | 92% faster |
| Cache Hit Rate | N/A | 95% | New capability |

### ADR Compliance
- [x] ADR-007: SPE Storage Seam Minimalism
- [x] ADR-009: Redis-First Caching
- [x] ADR-010: DI Minimalism

### Testing
- [x] All unit tests pass (127/127)
- [x] All integration tests pass (23/23)
- [x] Performance tests show expected improvements
- [x] No memory leaks (tested under load for 10 minutes)
- [x] Graceful degradation tested (Redis unavailable)

### Documentation
- [x] Architecture diagrams updated
- [x] API documentation updated
- [x] Deployment guide updated
- [x] Pattern library validated

### Breaking Changes
None - internal refactoring only, API contracts unchanged.

### Migration Notes
1. Update `appsettings.json` with correct `API_APP_ID`
2. Configure Redis connection string
3. Ensure Key Vault secrets exist (BFF-API-ClientSecret, SPRK-DEV-DATAVERSE-URL)
```

Review Request:
- [ ] Tag: @tech-lead, @architecture-team
- [ ] Request: Review ADR compliance and performance metrics
- [ ] Timeline: Target merge date: _____
```

---

## 🎯 Success Criteria Summary

### Code Quality
- ✅ Interface count: 3 (was 10)
- ✅ Concrete class registrations: ~15 (was ~5)
- ✅ Program.cs DI lines: ~20 (was 80+)
- ✅ Call chain depth: 3 layers (was 6)
- ✅ No SDK types in API contracts
- ✅ No unnecessary abstractions

### Performance
- ✅ File upload: <200ms (was 700ms)
- ✅ File download: <150ms (was 500ms)
- ✅ Dataverse query: <100ms (was 650ms)
- ✅ Cache hit rate: >90%
- ✅ Cache hit latency: <10ms

### ADR Compliance
- ✅ ADR-007: Single SPE storage facade (SpeFileStore), returns DTOs only
- ✅ ADR-009: Redis-first caching, 55-minute TTL, no hybrid
- ✅ ADR-010: Concrete classes, feature modules, ~20 DI lines

### Maintainability
- ✅ Easier to understand (3-layer structure)
- ✅ Easier to debug (fewer layers to trace)
- ✅ Easier to test (mock at boundaries only)
- ✅ Easier to extend (clear feature boundaries)

---

## 📚 Related Resources

### Pattern Library
- **[patterns/README.md](patterns/README.md)** - Pattern catalog
- **[patterns/QUICK-CARD.md](patterns/QUICK-CARD.md)** - 30-second lookup
- **[TASK-PATTERN-MAP.md](TASK-PATTERN-MAP.md)** - Task → Pattern mapping
- **[CODE-PATTERNS.md](CODE-PATTERNS.md)** - Full reference (1500+ lines)

### Anti-Patterns
- **[ANTI-PATTERNS.md](ANTI-PATTERNS.md)** - Complete anti-pattern reference
- **[patterns/anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md)** - Don't create unnecessary interfaces
- **[patterns/anti-pattern-leaking-sdk-types.md](patterns/anti-pattern-leaking-sdk-types.md)** - Don't return DriveItem/Entity
- **[patterns/anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)** - Don't inject Scoped into Singleton

### Architecture Documents
- **[TARGET-ARCHITECTURE.md](TARGET-ARCHITECTURE.md)** - Target state
- **[ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md)** - ADRs
- **[CODEBASE-MAP.md](CODEBASE-MAP.md)** - File structure guide
- **[REFACTORING-PLAN.md](REFACTORING-PLAN.md)** - Detailed plan

---

**Last Updated**: 2025-10-13
**Status**: ✅ Ready for execution
**Estimated Duration**: ~40 hours (1 week sprint)
**Risk Level**: Low-Medium (phased approach, validation gates)
