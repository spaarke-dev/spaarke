# Service Registration Pattern

> **Domain**: BFF API / Dependency Injection
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-010

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` | Core services (auth, cache) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/DocumentsModule.cs` | Document/SPE services |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkersModule.cs` | Background workers |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Module registration |

---

## Pattern Structure

### Module Extension Method

```csharp
public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // Singleton: Stateless infrastructure
        services.AddSingleton<CacheMetrics>();
        services.AddSingleton<GraphTokenCache>();

        // Scoped: Per-request services
        services.AddScoped<ContainerOperations>();
        services.AddScoped<DriveItemOperations>();
        services.AddScoped<SpeFileStore>();

        // Interface registration (pointing to concrete)
        services.AddScoped<ISpeFileOperations>(sp =>
            sp.GetRequiredService<SpeFileStore>());

        return services;
    }
}
```

### Registration in Program.cs

```csharp
// src/server/api/Sprk.Bff.Api/Program.cs (lines 85-210)

builder.Services.AddSpaarkeCore();      // Auth, RequestCache
builder.Services.AddDocumentsModule();  // SPE operations
builder.Services.AddWorkersModule(builder.Configuration);  // Background jobs
```

---

## Lifetime Guidelines

| Lifetime | Use For | Examples |
|----------|---------|----------|
| **Singleton** | Stateless, thread-safe, connection pooling | `GraphClientFactory`, `DataverseService`, `OpenAiClient`, `CacheMetrics` |
| **Scoped** | Per-request state, HttpContext dependencies | `SpeFileStore`, `AuthorizationService`, `RequestCache` |
| **Transient** | Lightweight, stateless utilities | Rarely used |

---

## Configuration Options Pattern

```csharp
// Validated on startup (fail-fast)
builder.Services
    .AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Custom validator
builder.Services.AddSingleton<IValidateOptions<DocumentIntelligenceOptions>,
    DocumentIntelligenceOptionsValidator>();
```

---

## Conditional Registration

```csharp
// Feature flag check
var documentIntelligenceEnabled = builder.Configuration
    .GetValue<bool>("DocumentIntelligence:Enabled");

if (documentIntelligenceEnabled)
{
    builder.Services.AddSingleton<AiTelemetry>();
    builder.Services.AddSingleton<OpenAiClient>();
    builder.Services.AddScoped<DocumentIntelligenceService>();
    Console.WriteLine("✓ Document Intelligence services enabled");
}
else
{
    Console.WriteLine("⚠ Document Intelligence services disabled");
}
```

---

## HttpClient Configuration

```csharp
// Named HttpClient with resilience
builder.Services.AddHttpClient<IAccessDataSource, DataverseAccessDataSource>(
    (sp, client) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var dataverseUrl = config["Dataverse:ServiceUrl"];

        client.BaseAddress = new Uri($"{dataverseUrl.TrimEnd('/')}/api/data/v9.2");
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
```

---

## ADR-010 Compliance

**Rule**: ≤15 non-framework DI registrations

Track by counting lines in module files that call:
- `services.AddSingleton<T>()`
- `services.AddScoped<T>()`
- `services.AddTransient<T>()`

---

## Related Patterns

- [Background Workers](background-workers.md) - Worker registration
- [Endpoint Definition](endpoint-definition.md) - Service injection in handlers

---

**Lines**: ~115

