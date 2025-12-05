# CLAUDE.md - Shared .NET Libraries

> **Last Updated**: December 3, 2025
>
> **Purpose**: Module-specific instructions for shared .NET libraries used across the backend.

## Module Overview

This module contains shared .NET libraries:
- **Spaarke.Core** - Core utilities, extensions, and cross-cutting concerns
- **Spaarke.Dataverse** - Dataverse SDK wrappers and helpers

## Key Structure

```
src/server/shared/
├── Spaarke.Core/
│   ├── Extensions/          # Extension methods
│   ├── Utilities/           # Helper classes
│   └── Spaarke.Core.csproj
└── Spaarke.Dataverse/
    ├── Services/            # Dataverse service wrappers
    ├── Models/              # Dataverse entity models
    └── Spaarke.Dataverse.csproj
```

## Design Principles

### Keep Libraries Focused
```csharp
// ✅ CORRECT: Single responsibility
namespace Spaarke.Core.Extensions
{
    public static class StringExtensions
    {
        public static string ToKebabCase(this string value) => /* ... */;
    }
}

// ❌ WRONG: Kitchen sink library
namespace Spaarke.Everything
{
    // Don't put unrelated utilities together
}
```

### No Circular Dependencies
```
Spaarke.Core       <- No dependencies on other Spaarke libraries
Spaarke.Dataverse  <- Can depend on Spaarke.Core
Spe.Bff.Api        <- Can depend on both
```

### From ADR-010: DI Minimalism
```csharp
// ✅ CORRECT: Register concretes
services.AddSingleton<DataverseService>();

// ✅ CORRECT: Use IServiceClient for testing seam only
services.AddSingleton<IServiceClient>(provider => 
    new ServiceClient(connectionString));

// ❌ WRONG: Interface for everything
services.AddScoped<IDataverseService, DataverseService>();  // Unnecessary
```

## Spaarke.Core Patterns

### Extension Methods
```csharp
public static class HttpContextExtensions
{
    public static string? GetCorrelationId(this HttpContext context)
        => context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
           ?? context.TraceIdentifier;
}
```

### Guard Clauses
```csharp
public static class Guard
{
    public static T NotNull<T>(T? value, string paramName) where T : class
        => value ?? throw new ArgumentNullException(paramName);

    public static string NotNullOrEmpty(string? value, string paramName)
        => string.IsNullOrEmpty(value)
            ? throw new ArgumentException("Value cannot be null or empty", paramName)
            : value;
}
```

### Result Pattern
```csharp
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
```

## Spaarke.Dataverse Patterns

### ServiceClient Usage
```csharp
// ✅ CORRECT: Singleton ServiceClient (ADR-010)
public class DataverseService
{
    private readonly IServiceClient _client;

    public DataverseService(IServiceClient client)
    {
        _client = client;  // Injected as singleton
    }

    public async Task<Entity?> GetByIdAsync(string entityName, Guid id)
    {
        return await _client.RetrieveAsync(entityName, id, new ColumnSet(true));
    }
}
```

### Query Helpers
```csharp
public static class QueryExtensions
{
    public static QueryExpression WithColumns(this QueryExpression query, params string[] columns)
    {
        query.ColumnSet = new ColumnSet(columns);
        return query;
    }

    public static QueryExpression WithFilter(this QueryExpression query, string attribute, object value)
    {
        query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
        return query;
    }
}

// Usage
var query = new QueryExpression("account")
    .WithColumns("name", "accountnumber")
    .WithFilter("statecode", 0);
```

### Entity Extensions
```csharp
public static class EntityExtensions
{
    public static T? GetValue<T>(this Entity entity, string attribute)
    {
        if (!entity.Contains(attribute)) return default;
        
        var value = entity[attribute];
        return value switch
        {
            T typedValue => typedValue,
            EntityReference er when typeof(T) == typeof(Guid) => (T)(object)er.Id,
            OptionSetValue osv when typeof(T) == typeof(int) => (T)(object)osv.Value,
            Money money when typeof(T) == typeof(decimal) => (T)(object)money.Value,
            _ => default
        };
    }
}
```

## Testing Guidelines

```csharp
// Unit test with mocked ServiceClient
[Fact]
public async Task GetByIdAsync_ReturnsEntity_WhenExists()
{
    // Arrange
    var mockClient = new Mock<IServiceClient>();
    mockClient.Setup(c => c.RetrieveAsync("account", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
              .ReturnsAsync(new Entity("account") { Id = Guid.NewGuid() });

    var service = new DataverseService(mockClient.Object);

    // Act
    var result = await service.GetByIdAsync("account", Guid.NewGuid());

    // Assert
    result.Should().NotBeNull();
}
```

## Package References

```xml
<!-- Spaarke.Dataverse.csproj -->
<ItemGroup>
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
    <ProjectReference Include="..\Spaarke.Core\Spaarke.Core.csproj" />
</ItemGroup>
```

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Keep libraries focused | Create "utility" kitchen sinks |
| Use extension methods for cross-cutting | Add extension methods to domain objects |
| Make ServiceClient singleton | Create per-request ServiceClient |
| Use Result pattern for operations that can fail | Throw exceptions for expected failures |
| Document public APIs with XML comments | Leave public methods undocumented |

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
