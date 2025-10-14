# Anti-Pattern: Captive Dependency (Lifetime Mismatch)

**Avoid**: Injecting Scoped service into Singleton
**Violates**: DI lifetime hierarchy rules
**Common In**: DI registrations, service constructors

---

## ❌ WRONG: Scoped Captured by Singleton

```csharp
// Singleton service
public class GraphTokenCache // Registered as Singleton
{
    private readonly RequestCache _requestCache; // Scoped service!

    public GraphTokenCache(
        IDistributedCache cache,
        RequestCache requestCache) // ⚠️ PROBLEM: Scoped injected into Singleton
    {
        _requestCache = requestCache;
    }

    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        // _requestCache is now effectively a singleton
        // Will be same instance across ALL requests
        return await _requestCache.GetAsync(tokenHash);
    }
}

// DI registration
services.AddSingleton<GraphTokenCache>(); // Singleton
services.AddScoped<RequestCache>(); // Scoped - CAPTURED!
```

### Why This Is Wrong

| Problem | Impact |
|---------|--------|
| **Captive Dependency** | Scoped service becomes effectively singleton |
| **Stale Data** | Request 1 data visible to Request 2 |
| **Memory Leaks** | Scoped resources never released |
| **Thread Safety Issues** | Scoped service not designed for concurrent access |
| **Hard to Debug** | Symptoms appear intermittently |

### Real-World Consequences

```csharp
// Request 1 (User A)
var token = await cache.GetTokenAsync("hash-123");
// RequestCache now contains User A's data

// Request 2 (User B) - Different user, same RequestCache instance!
var token = await cache.GetTokenAsync("hash-456");
// User B sees User A's cached data! 🔥 SECURITY ISSUE
```

---

## ✅ CORRECT: Follow Lifetime Hierarchy

### Rule: Lifetime Hierarchy

```
Singleton
    ↓ Can inject
Singleton

Scoped
    ↓ Can inject
Singleton OR Scoped

Transient (avoid)
    ↓ Can inject
Singleton OR Scoped OR Transient
```

### ✅ Correct Example 1: Singleton → Singleton

```csharp
// Singleton can inject Singleton ✅
public class GraphTokenCache // Singleton
{
    private readonly IDistributedCache _cache; // Singleton (framework)
    private readonly ILogger<GraphTokenCache> _logger; // Singleton

    public GraphTokenCache(
        IDistributedCache cache, // ✅ OK: Singleton → Singleton
        ILogger<GraphTokenCache> logger) // ✅ OK: Singleton → Singleton
    {
        _cache = cache;
        _logger = logger;
    }
}

// DI registration
services.AddSingleton<GraphTokenCache>();
services.AddStackExchangeRedisCache(...); // Singleton
```

---

### ✅ Correct Example 2: Scoped → Singleton + Scoped

```csharp
// Scoped can inject Singleton OR Scoped ✅
public class SpeFileStore // Scoped
{
    private readonly IGraphClientFactory _graphFactory; // Singleton
    private readonly ILogger<SpeFileStore> _logger; // Singleton
    private readonly RequestCache _requestCache; // Scoped

    public SpeFileStore(
        IGraphClientFactory graphFactory, // ✅ OK: Scoped → Singleton
        ILogger<SpeFileStore> logger, // ✅ OK: Scoped → Singleton
        RequestCache requestCache) // ✅ OK: Scoped → Scoped
    {
        _graphFactory = graphFactory;
        _logger = logger;
        _requestCache = requestCache;
    }
}

// DI registration
services.AddScoped<SpeFileStore>();
services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
services.AddScoped<RequestCache>();
```

---

## When Singleton Needs Scoped Data: Use Factory Pattern

### ❌ Wrong: Direct Injection

```csharp
public class BackgroundJobProcessor : BackgroundService // Singleton
{
    private readonly DataverseServiceClientImpl _dataverse; // Scoped!

    public BackgroundJobProcessor(
        DataverseServiceClientImpl dataverse) // ❌ CAPTURED!
    {
        _dataverse = dataverse;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // _dataverse is now singleton - connection never recycled
    }
}
```

### ✅ Correct: Service Provider Factory

```csharp
public class BackgroundJobProcessor : BackgroundService // Singleton
{
    private readonly IServiceProvider _serviceProvider; // Inject service provider

    public BackgroundJobProcessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Create new scope for each job
            using var scope = _serviceProvider.CreateScope();

            // Resolve scoped service from scope
            var dataverse = scope.ServiceProvider
                .GetRequiredService<DataverseServiceClientImpl>();

            // Use scoped service
            await ProcessJobAsync(dataverse);

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
```

---

## Real-World Examples

### ❌ Example 1: Authorization Handler Captures Scoped Service

```csharp
// WRONG - Handler is Singleton, captures Scoped service
public class ResourceAccessHandler : AuthorizationHandler<ResourceAccessRequirement>
{
    private readonly AuthorizationService _authService; // Scoped!

    public ResourceAccessHandler(
        AuthorizationService authService) // ❌ CAPTURED
    {
        _authService = authService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceAccessRequirement requirement)
    {
        // _authService is singleton now - stale data!
        var result = await _authService.EvaluateAsync(...);
    }
}

// DI registration
services.AddSingleton<IAuthorizationHandler, ResourceAccessHandler>();
services.AddScoped<AuthorizationService>(); // CAPTURED!
```

### ✅ Example 1 Fixed: Make Handler Scoped

```csharp
// CORRECT - Handler is Scoped, can inject Scoped service
public class ResourceAccessHandler : AuthorizationHandler<ResourceAccessRequirement>
{
    private readonly AuthorizationService _authService;
    private readonly ILogger<ResourceAccessHandler> _logger;

    public ResourceAccessHandler(
        AuthorizationService authService, // ✅ Scoped → Scoped
        ILogger<ResourceAccessHandler> logger) // ✅ Scoped → Singleton
    {
        _authService = authService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceAccessRequirement requirement)
    {
        var result = await _authService.EvaluateAsync(...);

        if (result.Authorized)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}

// DI registration
services.AddScoped<IAuthorizationHandler, ResourceAccessHandler>(); // ✅ Scoped
services.AddScoped<AuthorizationService>(); // ✅ Scoped
```

---

### ❌ Example 2: Hosted Service Captures HttpClient

```csharp
// WRONG - BackgroundService is Singleton
public class DocumentEventProcessor : BackgroundService // Singleton
{
    private readonly HttpClient _httpClient; // Should be per-request!

    public DocumentEventProcessor(
        IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("DocumentAPI"); // ❌ Created once
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Using same HttpClient instance forever
            // DNS changes not picked up, connections not recycled
            await _httpClient.GetAsync("https://api.example.com/documents");
        }
    }
}
```

### ✅ Example 2 Fixed: Create Client Per Request

```csharp
// CORRECT - Create HttpClient when needed
public class DocumentEventProcessor : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DocumentEventProcessor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory; // ✅ Factory is Singleton
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Create new client for each request
            using var httpClient = _httpClientFactory.CreateClient("DocumentAPI");

            await httpClient.GetAsync("https://api.example.com/documents");

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
```

---

## Detecting Captive Dependencies

### Tool: Scrutor (NuGet Package)

```csharp
// In Program.cs
builder.Services.Scan(scan => scan
    .FromAssemblyOf<Program>()
    .AddClasses()
    .AsSelf()
    .WithScopedLifetime()
    .VerifyNoCaptives()); // ✅ Throws on captive dependencies at startup
```

### Manual Detection

```csharp
// Check DI registrations
// Pattern: Singleton depends on Scoped

// ❌ RED FLAG:
services.AddSingleton<ServiceA>(); // Singleton
services.AddScoped<ServiceB>(); // Scoped
// If ServiceA constructor has ServiceB parameter → CAPTIVE!

// Check constructor:
public class ServiceA // Singleton
{
    public ServiceA(ServiceB serviceB) { } // ServiceB is Scoped → CAPTIVE!
}
```

---

## Lifetime Guidelines (ADR-010)

### Use **Singleton** For:

| Service | Why Singleton? |
|---------|---------------|
| `DataverseServiceClientImpl` | Connection pooling, thread-safe |
| `GraphTokenCache` | Shared cache state |
| `IGraphClientFactory` | Factory pattern, stateless |
| `AuthorizationService` | **WAIT - Should be Scoped!** |
| `IAuthorizationRule` | Stateless logic |

### Use **Scoped** For:

| Service | Why Scoped? |
|---------|-------------|
| `SpeFileStore` | Per-request context |
| `UploadSessionManager` | Per-request tracking |
| `ResourceAccessHandler` | Per-request authorization |
| `IAccessDataSource` | Per-request Dataverse queries |
| `RequestCache` | Per-request memoization |

### **NEVER** Use Transient:
- Creates new instance on **every injection** (not per-request)
- If service injected 3 times, 3 instances created
- No benefit over Scoped in ASP.NET Core

---

## How to Fix Captive Dependencies

### Step 1: Identify the Captive

```bash
# Check if Singleton injects Scoped
grep -A 20 "AddSingleton<YourService>" Program.cs
# Look at YourService constructor
# Check if any parameters are registered as Scoped
```

### Step 2: Choose Fix Strategy

| Strategy | When to Use |
|----------|-------------|
| **Make Singleton Scoped** | If service doesn't need to be Singleton |
| **Make Scoped Singleton** | If scoped service is truly stateless |
| **Use Service Provider Factory** | If Singleton needs per-operation scope |
| **Remove Dependency** | If dependency not actually needed |

### Step 3: Apply Fix

```csharp
// Option 1: Change parent to Scoped
services.AddScoped<YourService>(); // Was Singleton

// Option 2: Change dependency to Singleton
services.AddSingleton<YourDependency>(); // Was Scoped

// Option 3: Use factory pattern
public YourService(IServiceProvider serviceProvider)
{
    using var scope = serviceProvider.CreateScope();
    var dependency = scope.ServiceProvider.GetRequiredService<YourDependency>();
}
```

---

## Checklist: Avoid Captive Dependencies

- [ ] Singleton services only inject Singleton dependencies
- [ ] Scoped services can inject Singleton or Scoped
- [ ] Background services use service provider factory for Scoped dependencies
- [ ] HttpClient created per-request (not once in constructor)
- [ ] Authorization handlers are Scoped (if they inject Scoped services)
- [ ] Verified with Scrutor or manual inspection

---

## Related Patterns

- **DI organization**: See [di-feature-module.md](di-feature-module.md)
- **Lifetime guidelines**: ADR-010 in [../ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md)
- **Background services**: See [service-background-processor.md](service-background-processor.md)

---

## Quick Reference

```
❌ DON'T: Inject Scoped into Singleton
❌ DON'T: Store HttpClient in field of Singleton
❌ DON'T: Use Transient lifetime (use Scoped instead)

✅ DO: Follow lifetime hierarchy (Singleton → Singleton, Scoped → Any)
✅ DO: Use IServiceProvider factory pattern when Singleton needs Scoped
✅ DO: Verify DI lifetimes with Scrutor
✅ DO: Make authorization handlers Scoped
```

**Rule of Thumb**: When in doubt, use Scoped (safer than Singleton, better than Transient)
