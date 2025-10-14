# Anti-Pattern: Interface Proliferation

**Avoid**: Creating interfaces "just because" without genuine need
**Violates**: ADR-010 (DI Minimalism)
**Common In**: Service layer, DI registrations

---

## ❌ WRONG: Unnecessary Interface

```csharp
// Creating interface with no benefit
public interface ISpeFileStore
{
    Task<FileUploadResult> UploadFileAsync(string containerId, string fileName, Stream content);
    Task<Stream> DownloadFileAsync(string containerId, string fileId);
}

public class SpeFileStore : ISpeFileStore
{
    // Implementation...
}

// In DI registration
services.AddScoped<ISpeFileStore, SpeFileStore>();
```

### Why This Is Wrong

| Problem | Impact |
|---------|--------|
| **Premature Abstraction** | YAGNI - You Aren't Gonna Need It |
| **No Multiple Implementations** | No plans for alternative implementations |
| **Navigation Issues** | "Go to Definition" jumps to interface, not implementation |
| **Extra Ceremony** | More files, more complexity, no benefit |
| **Testing Myth** | "Need interface for mocking" - not true with modern mocking frameworks |

---

## ✅ CORRECT: Register Concrete Directly

```csharp
// No interface needed
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

    public async Task<FileUploadResult> UploadFileAsync(
        string containerId,
        string fileName,
        Stream content,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

        var driveItem = await graphClient
            .Storage.FileStorage.Containers[containerId]
            .Drive.Root.ItemWithPath(fileName).Content
            .Request()
            .PutAsync<DriveItem>(content, cancellationToken);

        return new FileUploadResult
        {
            ItemId = driveItem.Id!,
            Name = driveItem.Name!,
            Size = driveItem.Size ?? 0
        };
    }
}

// In DI registration (ADR-010)
services.AddScoped<SpeFileStore>(); // Simple, direct
```

---

## When Interfaces ARE Allowed (ADR-010)

Only create interfaces for these patterns:

| Interface | Justification | Example |
|-----------|--------------|---------|
| `IGraphClientFactory` | **Factory pattern** - Creates different types (OBO vs app-only) | ✅ Allowed |
| `IAccessDataSource` | **Test seam** - Abstract Dataverse for testing | ✅ Allowed |
| `IAuthorizationRule` | **Collection pattern** - Multiple implementations | ✅ Allowed |
| `IDistributedCache` | **Framework interface** - From Microsoft | ✅ Allowed |
| `IHttpClientFactory` | **Framework interface** - From Microsoft | ✅ Allowed |

**Everything else**: Register concrete classes directly

---

## Real-World Example

### ❌ WRONG: Interface for Everything

```csharp
// src/api/Spe.Bff.Api/Storage/ISpeFileStore.cs
public interface ISpeFileStore { }

// src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
public class SpeFileStore : ISpeFileStore { }

// src/api/Spe.Bff.Api/Services/IDocumentService.cs
public interface IDocumentService { }

// src/api/Spe.Bff.Api/Services/DocumentService.cs
public class DocumentService : IDocumentService { }

// src/api/Spe.Bff.Api/Infrastructure/IUploadSessionManager.cs
public interface IUploadSessionManager { }

// src/api/Spe.Bff.Api/Infrastructure/UploadSessionManager.cs
public class UploadSessionManager : IUploadSessionManager { }
```

**Result**: 6 extra files, no benefit, harder navigation

---

### ✅ CORRECT: Concrete Classes

```csharp
// src/api/Spe.Bff.Api/Storage/SpeFileStore.cs
public class SpeFileStore { }

// src/api/Spe.Bff.Api/Services/DocumentService.cs
public class DocumentService { }

// src/api/Spe.Bff.Api/Infrastructure/UploadSessionManager.cs
public class UploadSessionManager { }
```

**Result**: 3 files, clear navigation, simple DI

---

## Testing Without Interfaces

**Myth**: "I need interfaces to mock dependencies"

**Reality**: Modern mocking frameworks mock concrete classes

```csharp
// Using Moq - works with concrete classes
public class SpeFileStoreTests
{
    [Fact]
    public async Task UploadFile_Success()
    {
        // Arrange
        var mockGraphFactory = new Mock<IGraphClientFactory>();
        var mockLogger = new Mock<ILogger<SpeFileStore>>();

        var sut = new SpeFileStore(
            mockGraphFactory.Object,
            mockLogger.Object);

        // Act
        var result = await sut.UploadFileAsync(...);

        // Assert
        Assert.NotNull(result);
    }
}
```

**No interface needed for SpeFileStore** - only for its dependencies (IGraphClientFactory)

---

## How to Fix Existing Code

### Step 1: Identify Unnecessary Interfaces

```bash
# Find interface files
find src/ -name "I*.cs" | grep -v "Infrastructure/Graph/IGraphClientFactory"

# Check if each has multiple implementations
# If only one implementation → candidate for removal
```

### Step 2: Remove Interface

```csharp
// Before
services.AddScoped<ISpeFileStore, SpeFileStore>();

// After (ADR-010)
services.AddScoped<SpeFileStore>();
```

### Step 3: Update Injections

```csharp
// Before
public OBOEndpoints(ISpeFileStore fileStore)

// After
public OBOEndpoints(SpeFileStore fileStore)
```

### Step 4: Delete Interface File

```bash
rm src/api/Spe.Bff.Api/Storage/ISpeFileStore.cs
```

---

## Decision Tree

```
Do I need an interface?
│
├─ Is this a factory pattern? (creates different types)
│  └─ YES → ✅ Create interface (e.g., IGraphClientFactory)
│
├─ Is this a collection pattern? (multiple implementations)
│  └─ YES → ✅ Create interface (e.g., IAuthorizationRule)
│
├─ Is this a framework interface? (from Microsoft)
│  └─ YES → ✅ Use framework interface (e.g., IDistributedCache)
│
├─ Do I have 2+ implementations RIGHT NOW?
│  └─ YES → ✅ Create interface
│
└─ Otherwise
   └─ NO → ❌ Use concrete class
```

**Remember**: YAGNI - You Aren't Gonna Need It

---

## Checklist: Avoid Interface Proliferation

- [ ] Interface has genuine need (factory/collection/framework)
- [ ] Interface has 2+ implementations RIGHT NOW (not future plans)
- [ ] Cannot use concrete class instead
- [ ] Documented why interface is needed

If any checklist item is NO → Use concrete class

---

## Related Patterns

- **Correct approach**: See [di-feature-module.md](di-feature-module.md) for DI examples
- **Allowed interfaces**: ADR-010 in [../ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md)

---

## Quick Reference

```
❌ DON'T: Create interface for every class
✅ DO: Register concrete classes directly
✅ DO: Only create interfaces for factory/collection/framework patterns
✅ DO: Follow ADR-010 allowed interfaces list
```

**Remember**: The best interface is no interface (unless genuinely needed)
