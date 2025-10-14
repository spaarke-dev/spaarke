# Anti-Patterns to Avoid During Refactoring

## 1. Interface Proliferation ❌

### DON'T Create Interfaces "Just Because"
```csharp
// ❌ WRONG: Unnecessary interface
public interface ISpeFileStore { }
public class SpeFileStore : ISpeFileStore { }

// Register:
services.AddScoped<ISpeFileStore, SpeFileStore>(); // Adds no value
WHY: Premature Abstraction

You Aren't Gonna Need It (YAGNI)
No plans for multiple implementations
Makes code harder to navigate (jump to definition goes to interface, not implementation)
Adds ceremony without benefit

DO: Register Concretes Directly
csharp// ✅ CORRECT
public class SpeFileStore { }

// Register:
services.AddScoped<SpeFileStore>(); // Simple, direct

2. God Services ❌
DON'T Create Omnibus Services
csharp// ❌ WRONG: Service that does everything
public class DocumentService
{
    public async Task UploadFile() { }
    public async Task DownloadFile() { }
    public async Task DeleteFile() { }
    public async Task CreateContainer() { }
    public async Task ListContainers() { }
    public async Task CreateDataverseRecord() { }
    public async Task UpdateDataverseRecord() { }
    public async Task DeleteDataverseRecord() { }
    public async Task SendNotification() { }
    public async Task LogActivity() { }
    // ... 50 more methods
}
WHY: Violates Single Responsibility

Hard to test (too many dependencies)
Hard to understand (too many concerns)
Hard to maintain (changes affect many callers)

DO: Focused Services with Clear Boundaries
csharp// ✅ CORRECT: Focused services
public class SpeFileStore // SPE operations only
{
    public async Task UploadFileAsync() { }
    public async Task DownloadFileAsync() { }
}

public class DataverseServiceClientImpl // Dataverse operations only
{
    public async Task CreateRecordAsync() { }
    public async Task UpdateRecordAsync() { }
}

3. Pass-Through Services ❌
DON'T Create Wrapper Services That Add No Value
csharp// ❌ WRONG: Unnecessary wrapper
public interface IDocumentService { }
public class DocumentService : IDocumentService
{
    private readonly SpeFileStore _fileStore;
    private readonly DataverseServiceClientImpl _dataverse;
    
    public async Task UploadDocument(...)
    {
        // Just calling through to file store, no added logic
        return await _fileStore.UploadFileAsync(...);
    }
}
WHY: Adds Indirection Without Value

Extra layer to debug through
No business logic added
Just delegating to underlying service
"Ravioli code" - too many small pieces

DO: Call Services Directly from Endpoints
csharp// ✅ CORRECT: Endpoint calls services directly
public static async Task<IResult> UploadFile(
    SpeFileStore fileStore, // Direct injection
    DataverseServiceClientImpl dataverse)
{
    var file = await fileStore.UploadFileAsync(...);
    var record = await dataverse.CreateRecordAsync(...);
    return Results.Ok(record);
}

4. Mixing Lifetimes Incorrectly ❌
DON'T Inject Scoped into Singleton
csharp// ❌ WRONG: Captive dependency
public class SingletonService // Registered as Singleton
{
    private readonly ScopedService _scoped; // Injected scoped service
    
    public SingletonService(ScopedService scoped)
    {
        _scoped = scoped; // PROBLEM: Scoped captured by Singleton
    }
}
WHY: Captive Dependency Anti-Pattern

Scoped service becomes effectively singleton
Can cause stale data issues
Can cause memory leaks
Hard to diagnose bugs

DO: Follow Lifetime Hierarchy
csharp// ✅ CORRECT: Singleton can inject Singleton
public class SingletonService
{
    private readonly AnotherSingletonService _singleton;
}

// ✅ CORRECT: Scoped can inject Singleton or Scoped
public class ScopedService
{
    private readonly SingletonService _singleton;
    private readonly AnotherScopedService _scoped;
}

// ✅ CORRECT: Scoped needs Singleton data - use factory
public class ScopedService
{
    public ScopedService(IServiceProvider serviceProvider)
    {
        // Resolve scoped service on-demand
        var scoped = serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<SomeService>();
    }
}

5. Configuration Magic Strings ❌
DON'T Use Hardcoded Strings
csharp// ❌ WRONG: Magic strings everywhere
var url = configuration["Dataverse:ServiceUrl"];
var clientId = configuration["AzureAd:ClientId"];
var secret = configuration["AzureAd:ClientSecret"];
WHY: Typos, No IntelliSense, No Compile-Time Safety

Typo in string → runtime error
No IDE support
Hard to refactor

DO: Use Options Pattern
csharp// ✅ CORRECT: Strongly-typed configuration
public class DataverseOptions
{
    public const string SectionName = "Dataverse";
    
    public string ServiceUrl { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
}

// Registration
builder.Services.AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Usage
public class SomeService
{
    public SomeService(IOptions<DataverseOptions> options)
    {
        var url = options.Value.ServiceUrl; // Strongly-typed, IntelliSense
    }
}

6. Mixing Business Logic in Endpoints ❌
DON'T Put Complex Logic in Endpoint Handlers
csharp// ❌ WRONG: Business logic in endpoint
private static async Task<IResult> UploadFile(...)
{
    // 50 lines of validation logic
    if (file.Size > MAX_SIZE) { }
    if (!allowedExtensions.Contains(ext)) { }
    if (user.Quota < file.Size) { }
    
    // 50 lines of upload logic
    var stream = new MemoryStream();
    await file.CopyToAsync(stream);
    var graphClient = ...;
    
    // 50 lines of Dataverse logic
    var record = new Entity("sprk_document");
    record["sprk_name"] = file.Name;
    
    // Total: 150 lines in one method
}
WHY: Untestable, Hard to Maintain

Can't unit test without HTTP context
Hard to reuse logic
Violates separation of concerns

DO: Thin Endpoints, Logic in Services
csharp// ✅ CORRECT: Thin endpoint
private static async Task<IResult> UploadFile(
    FileUploadRequest request,
    SpeFileStore fileStore,
    DocumentService documentService,
    ILogger logger)
{
    try
    {
        var file = await fileStore.UploadFileAsync(request);
        var record = await documentService.CreateDocumentRecordAsync(file);
        
        logger.LogInformation("Document created: {RecordId}", record.Id);
        return Results.Ok(record);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Upload failed");
        return Results.Problem();
    }
}

7. Leaking Infrastructure Types ❌
DON'T Return Framework/SDK Types from Services
csharp// ❌ WRONG: Graph SDK type leaked
public async Task<DriveItem> UploadFileAsync(...)
{
    return await graphClient.Drives[driveId]...;
}

// Caller is now coupled to Graph SDK
public static async Task<IResult> Endpoint(SpeFileStore store)
{
    DriveItem item = await store.UploadFileAsync(...); // Requires Microsoft.Graph reference
}
WHY: Tight Coupling to Infrastructure

Can't swap Graph SDK version without breaking callers
Callers need to reference Graph SDK
Hard to test (need to mock SDK types)

DO: Return Domain DTOs
csharp// ✅ CORRECT: Domain DTO
public record FileHandleDto
{
    public string DriveId { get; init; }
    public string ItemId { get; init; }
    public string Name { get; init; }
}

public async Task<FileHandleDto> UploadFileAsync(...)
{
    var driveItem = await graphClient.Drives[driveId]...;
    
    return new FileHandleDto
    {
        DriveId = driveId,
        ItemId = driveItem.Id,
        Name = driveItem.Name
    };
}

8. Hybrid Caching ❌
DON'T Create Multi-Tier Cache Without Proof of Need
csharp// ❌ WRONG: Premature optimization
public class HybridCacheService
{
    private readonly IMemoryCache _l1Cache;
    private readonly IDistributedCache _l2Cache;
    
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        // Check L1
        if (_l1Cache.TryGetValue(key, out T value))
            return value;
        
        // Check L2
        var cached = await _l2Cache.GetStringAsync(key);
        if (cached != null)
        {
            var deserialized = JsonSerializer.Deserialize<T>(cached);
            _l1Cache.Set(key, deserialized); // Backfill L1
            return deserialized;
        }
        
        // Fetch and populate both caches
        // ... complex coherence logic
    }
}
WHY: Complexity Without Measured Benefit

Cache coherence is hard (stale data risk)
Adds complexity
ADR-009 explicitly rejects this
Profile first, optimize later

DO: Redis-First, Measure Before Adding L1
csharp// ✅ CORRECT: Simple distributed cache
public class TokenCacheService
{
    private readonly IDistributedCache _cache;
    
    public async Task<string?> GetTokenAsync(string key)
    {
        return await _cache.GetStringAsync($"sdap:token:{key}");
    }
}

// IF profiling shows Redis latency is a problem (>5% of request time),
// THEN consider adding L1 with very short TTL (1-5 seconds).
// Document decision in new ADR.