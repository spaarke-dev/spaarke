# Pattern: Feature Module DI Registration

**Use For**: Organizing DI registrations into feature-specific modules
**Task**: Implementing clean DI organization per ADR-010
**Time**: 10 minutes per module

---

## Quick Copy-Paste: Documents Module

```csharp
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Extensions;

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

## Quick Copy-Paste: SpaarkeCore Module

```csharp
using Spaarke.Core.Auth;
using Spaarke.Core.Cache;

namespace Spe.Bff.Api.Extensions;

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

        // Dataverse access (Scoped - per-request queries)
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        return services;
    }
}
```

---

## Quick Copy-Paste: Workers Module

```csharp
using Spe.Bff.Api.Services.Jobs;

namespace Spe.Bff.Api.Extensions;

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

## Usage in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// ---- Configuration Validation ----
builder.Services.AddOptionsWithValidation<GraphOptions>(builder.Configuration);
builder.Services.AddOptionsWithValidation<DataverseOptions>(builder.Configuration);
builder.Services.AddOptionsWithValidation<RedisOptions>(builder.Configuration);

// ---- Authentication & Authorization ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorizationPolicies();

// ---- Feature Modules (ADR-010) ----
builder.Services.AddSpaarkeCore();        // Core auth + cache
builder.Services.AddDocumentsModule();    // SPE operations
builder.Services.AddWorkersModule();      // Background services

// ---- Infrastructure ----
builder.Services.AddHttpClientWithResilience();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.Run();
```

**Line Count**: ~25 lines (vs 680 lines before refactoring)

---

## Lifetime Guidelines (ADR-010)

### Use **Singleton** For:

| Service | Why? |
|---------|------|
| `DataverseServiceClientImpl` | Connection pooling, thread-safe SDK |
| `GraphTokenCache` | Shared cache state |
| `IGraphClientFactory` | Factory pattern, stateless |
| `AuthorizationService` | Stateless rule evaluation |
| `IAuthorizationRule` implementations | Stateless logic |
| `IdempotencyService` | Shared state tracking |

### Use **Scoped** For:

| Service | Why? |
|---------|------|
| `SpeFileStore` | May hold per-request context |
| `UploadSessionManager` | Per-request upload tracking |
| `ResourceAccessHandler` | Per-request authorization |
| `IAccessDataSource` | Per-request Dataverse queries |
| `RequestCache` | Per-request memoization |

### **NEVER** Use Transient:
- ❌ Creates new instance on every injection
- ❌ No benefit over Scoped in ASP.NET Core

---

## Allowed Interfaces (ADR-010)

| Interface | Justification |
|-----------|--------------|
| `IGraphClientFactory` | Factory pattern (creates different client types) |
| `IAccessDataSource` | Dataverse abstraction seam |
| `IAuthorizationRule` | Rule collection pattern |
| `IDistributedCache` | Framework interface |
| `IHttpClientFactory` | Framework interface |

**All others**: Register concrete classes directly (no interface)

---

## Checklist

- [ ] Extension method returns `IServiceCollection`
- [ ] Extension method is `static`
- [ ] Uses correct lifetime (Singleton vs Scoped)
- [ ] Only creates interfaces for allowed patterns
- [ ] Groups related services together
- [ ] XML documentation comment explaining purpose
- [ ] References ADR-010 in comment

---

## File Structure

```
src/api/Spe.Bff.Api/Extensions/
├── SpaarkeCoreExtensions.cs      (Auth + Cache)
├── DocumentsModuleExtensions.cs   (SPE operations)
├── WorkersModuleExtensions.cs     (Background services)
├── DataverseModuleExtensions.cs   (Dataverse connection)
├── AuthorizationExtensions.cs     (Policies)
└── RedisCacheExtensions.cs        (Redis setup)
```

---

## Related Files

- Create in: `src/api/Spe.Bff.Api/Extensions/`
- Used by: `Program.cs`
- Follows: ADR-010 (DI Minimalism and Feature Modules)
