# Anti-Pattern: Leaking Infrastructure/SDK Types

**Avoid**: Returning Graph SDK or framework types from services
**Violates**: ADR-007 (SPE Storage Seam Minimalism)
**Common In**: Service methods, API responses

---

## ❌ WRONG: Graph SDK Type Leaked

```csharp
using Microsoft.Graph;

// Service returns Graph SDK type
public class SpeFileStore
{
    public async Task<DriveItem> UploadFileAsync(
        string containerId,
        string fileName,
        Stream content)
    {
        var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(token);

        // Returns Graph SDK type directly
        return await graphClient
            .Storage.FileStorage.Containers[containerId]
            .Drive.Root.ItemWithPath(fileName).Content
            .Request()
            .PutAsync<DriveItem>(content);
    }
}

// Endpoint is now coupled to Graph SDK
public static async Task<IResult> UploadFile(SpeFileStore store)
{
    DriveItem item = await store.UploadFileAsync(...); // Requires Microsoft.Graph reference
    return Results.Ok(item); // Leaks SDK type to API consumer
}
```

### Why This Is Wrong

| Problem | Impact |
|---------|--------|
| **Tight Coupling** | Callers need Microsoft.Graph reference |
| **Versioning Issues** | Can't upgrade SDK without breaking all callers |
| **API Leakage** | Internal implementation details exposed to API consumers |
| **Testing Difficulty** | Must mock Graph SDK types |
| **Swagger Issues** | Graph SDK types don't serialize well to OpenAPI |

---

## ✅ CORRECT: Return Domain DTOs

```csharp
using Spe.Bff.Api.Models; // Domain models, not Graph SDK

// Service returns domain DTO
public class SpeFileStore
{
    public async Task<FileUploadResult> UploadFileAsync(
        string containerId,
        string fileName,
        Stream content,
        string userToken,
        CancellationToken cancellationToken = default)
    {
        var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

        // Call Graph SDK internally
        var driveItem = await graphClient
            .Storage.FileStorage.Containers[containerId]
            .Drive.Root.ItemWithPath(fileName).Content
            .Request()
            .PutAsync<DriveItem>(content, cancellationToken);

        // ⚠️ CRITICAL: Map to DTO before returning
        return new FileUploadResult
        {
            ItemId = driveItem.Id!,
            Name = driveItem.Name!,
            Size = driveItem.Size ?? 0,
            MimeType = driveItem.File?.MimeType,
            WebUrl = driveItem.WebUrl,
            CreatedDateTime = driveItem.CreatedDateTime,
            ETag = driveItem.ETag
        };
    }
}

// Endpoint uses domain DTO
public static async Task<IResult> UploadFile(SpeFileStore store)
{
    FileUploadResult result = await store.UploadFileAsync(...); // No Graph SDK reference needed
    return Results.Ok(result); // Clean domain model in API
}
```

---

## DTO Definition (ADR-007)

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Result of file upload operation.
/// NEVER exposes Graph SDK types (DriveItem, Drive, etc.)
/// </summary>
public record FileUploadResult
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public long Size { get; init; }
    public string? MimeType { get; init; }
    public string? WebUrl { get; init; }
    public DateTimeOffset? CreatedDateTime { get; init; }
    public string? ETag { get; init; }
}
```

---

## Common SDK Types to NEVER Return

### Graph SDK Types ❌

| SDK Type | Why Avoid | Return Instead |
|----------|-----------|----------------|
| `DriveItem` | Graph SDK dependency | `FileUploadResult` |
| `Drive` | Graph SDK dependency | `ContainerDto` |
| `User` | Graph SDK dependency | `UserDto` |
| `Permission` | Graph SDK dependency | `PermissionDto` |
| `FileStorageContainer` | Graph SDK dependency | `ContainerDto` |

### Dataverse SDK Types ❌

| SDK Type | Why Avoid | Return Instead |
|----------|-----------|----------------|
| `Entity` | Dataverse SDK dependency | `DocumentDto` |
| `EntityReference` | Dataverse SDK dependency | `EntityReferenceDto` |
| `OptionSetValue` | Dataverse SDK dependency | `int` or enum |
| `Money` | Dataverse SDK dependency | `decimal` |

### Framework Types (Sometimes OK)

| Type | OK to Return? | Notes |
|------|---------------|-------|
| `Stream` | ✅ Yes | Standard .NET type |
| `IFormFile` | ✅ Yes | ASP.NET Core type |
| `JsonDocument` | ✅ Yes | Standard .NET type |
| `HttpClient` | ❌ No | Internal implementation |

---

## Real-World Examples

### ❌ Example 1: Dataverse Entity Leaked

```csharp
// WRONG - Returns Dataverse SDK type
public async Task<Entity> GetDocumentAsync(Guid documentId)
{
    var query = new QueryExpression("sprk_document")
    {
        ColumnSet = new ColumnSet(true),
        Criteria = new FilterExpression
        {
            Conditions =
            {
                new ConditionExpression("sprk_documentid", ConditionOperator.Equal, documentId)
            }
        }
    };

    var results = await _serviceClient.RetrieveMultipleAsync(query);
    return results.Entities.FirstOrDefault(); // Returns Entity!
}
```

### ✅ Example 1 Fixed: Domain DTO

```csharp
// CORRECT - Returns domain DTO
public async Task<DocumentDto?> GetDocumentAsync(Guid documentId)
{
    var query = new QueryExpression("sprk_document")
    {
        ColumnSet = new ColumnSet(true),
        Criteria = new FilterExpression
        {
            Conditions =
            {
                new ConditionExpression("sprk_documentid", ConditionOperator.Equal, documentId)
            }
        }
    };

    var results = await _serviceClient.RetrieveMultipleAsync(query);
    var entity = results.Entities.FirstOrDefault();

    if (entity == null)
        return null;

    // Map to DTO
    return new DocumentDto
    {
        Id = entity.Id,
        Name = entity.GetAttributeValue<string>("sprk_documentname"),
        FileName = entity.GetAttributeValue<string>("sprk_filename"),
        FileSize = entity.GetAttributeValue<long>("sprk_filesize"),
        DriveId = entity.GetAttributeValue<string>("sprk_graphdriveid"),
        ItemId = entity.GetAttributeValue<string>("sprk_graphitemid"),
        CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
    };
}
```

---

### ❌ Example 2: Graph Permission Leaked

```csharp
// WRONG - Returns Graph SDK collection
public async Task<IEnumerable<Permission>> GetFilePermissionsAsync(
    string containerId,
    string fileId)
{
    var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(token);

    var permissions = await graphClient
        .Storage.FileStorage.Containers[containerId]
        .Drive.Items[fileId].Permissions
        .Request()
        .GetAsync();

    return permissions.CurrentPage; // Returns Permission objects!
}
```

### ✅ Example 2 Fixed: Permission DTO

```csharp
// CORRECT - Returns domain DTO
public async Task<IEnumerable<FilePermissionDto>> GetFilePermissionsAsync(
    string containerId,
    string fileId,
    string userToken,
    CancellationToken cancellationToken = default)
{
    var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

    var permissions = await graphClient
        .Storage.FileStorage.Containers[containerId]
        .Drive.Items[fileId].Permissions
        .Request()
        .GetAsync(cancellationToken);

    // Map to DTOs
    return permissions.CurrentPage.Select(p => new FilePermissionDto
    {
        Id = p.Id,
        Roles = p.Roles?.ToList() ?? new List<string>(),
        GrantedTo = p.GrantedToV2?.User?.DisplayName,
        GrantedToId = p.GrantedToV2?.User?.Id,
        InheritedFrom = p.InheritedFrom?.Id
    });
}
```

---

## Mapping Patterns

### Pattern 1: Simple Property Mapping

```csharp
// Graph SDK → DTO
return new FileUploadResult
{
    ItemId = driveItem.Id!,
    Name = driveItem.Name!,
    Size = driveItem.Size ?? 0,
    MimeType = driveItem.File?.MimeType
};
```

### Pattern 2: Nested Object Mapping

```csharp
// Graph SDK with nested objects
return new ContainerDto
{
    Id = container.Id!,
    DisplayName = container.DisplayName!,
    CreatedDateTime = container.CreatedDateTime,
    Owner = container.Owner != null
        ? new OwnerDto
        {
            Id = container.Owner.User?.Id,
            DisplayName = container.Owner.User?.DisplayName
        }
        : null
};
```

### Pattern 3: Collection Mapping

```csharp
// Graph SDK collection → DTO collection
return permissions.Select(p => new PermissionDto
{
    Id = p.Id,
    Roles = p.Roles?.ToList() ?? new List<string>()
}).ToList();
```

---

## How to Fix Existing Code

### Step 1: Identify Leaked Types

```bash
# Search for Graph SDK return types
grep -r "Task<DriveItem>" src/
grep -r "Task<Drive>" src/
grep -r "Task<Entity>" src/

# Search for exposed SDK types in endpoint signatures
grep -r "Results.Ok(driveItem)" src/api/
```

### Step 2: Create DTOs

```csharp
// For each leaked type, create equivalent DTO
// src/api/Spe.Bff.Api/Models/YourDto.cs
public record YourDto
{
    public required string Id { get; init; }
    // Map only properties you need
};
```

### Step 3: Add Mapping in Service

```csharp
// Add mapping code at end of service method
var sdkResult = await ExternalSdkCallAsync();

return new YourDto
{
    Id = sdkResult.Id,
    // Map properties
};
```

### Step 4: Update Method Signature

```csharp
// Before
public async Task<DriveItem> MethodAsync()

// After
public async Task<FileUploadResult> MethodAsync()
```

---

## Benefits of DTOs

| Benefit | Description |
|---------|-------------|
| **Decoupling** | Callers don't need SDK references |
| **Versioning** | Can upgrade SDK without breaking callers |
| **API Stability** | DTO schema is under your control |
| **Swagger** | Clean OpenAPI specs |
| **Testing** | Easy to mock domain types |
| **Performance** | Return only what you need (smaller payload) |

---

## Checklist: Avoid Leaking SDK Types

- [ ] Service methods return DTOs (not SDK types)
- [ ] DTOs defined in `Models/` folder
- [ ] DTOs use `record` keyword for immutability
- [ ] Mapping happens inside service (not in endpoint)
- [ ] No Graph SDK types in endpoint signatures
- [ ] No Dataverse SDK types in endpoint signatures
- [ ] API responses use domain models only

---

## Related Patterns

- **DTO creation**: See [dto-file-upload-result.md](dto-file-upload-result.md)
- **Service implementation**: See [service-graph-client-factory.md](service-graph-client-factory.md)
- **ADR reference**: ADR-007 in [../ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md)

---

## Quick Reference

```
❌ DON'T: Return DriveItem, Entity, or any SDK type
✅ DO: Create domain DTOs in Models/ folder
✅ DO: Map SDK types to DTOs inside service methods
✅ DO: Return DTOs from all service methods
✅ DO: Use DTOs in all endpoint signatures
```

**Rule of Thumb**: If it requires `using Microsoft.Graph` or `using Microsoft.Xrm.Sdk`, don't return it from services
