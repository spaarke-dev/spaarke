# CLAUDE.md - Spe.Bff.Api Module

> **Last Updated**: December 3, 2025
>
> **Purpose**: Module-specific instructions for the SharePoint Embedded BFF (Backend-for-Frontend) API.

## Module Overview

**Spe.Bff.Api** is the .NET 8 Minimal API that provides:
- SharePoint Embedded file operations (upload, download, delete)
- On-Behalf-Of (OBO) token flow for user impersonation
- Container management for SPE storage
- Health checks and observability

## Key Files

```
Spe.Bff.Api/
├── Program.cs              # Entry point, DI configuration, middleware
├── Endpoints/              # Minimal API endpoint definitions
│   ├── DocumentEndpoints.cs
│   ├── ContainerEndpoints.cs
│   └── HealthEndpoints.cs
├── Services/
│   ├── SpeFileStore.cs     # SPE operations facade (ADR-007)
│   ├── AuthorizationService.cs
│   └── GraphClientFactory.cs
├── Filters/                # Endpoint filters for auth (ADR-008)
│   └── DocumentAuthorizationFilter.cs
└── appsettings.json        # Configuration template
```

## Architecture Constraints

### From ADR-007: SpeFileStore Facade
```csharp
// ✅ CORRECT: Use SpeFileStore facade
public class DocumentEndpoints
{
    public static async Task<IResult> GetDocument(
        string id,
        SpeFileStore fileStore)  // Inject concrete facade
    {
        var stream = await fileStore.GetFileContentAsync(id);
        return Results.Stream(stream);
    }
}

// ❌ WRONG: Don't inject GraphServiceClient directly
public class BadEndpoint(GraphServiceClient graph) { }
```

### From ADR-008: Endpoint Filters
```csharp
// ✅ CORRECT: Use endpoint filters for resource authorization
app.MapGet("/obo/drives/{driveId}/items/{itemId}", GetItem)
   .AddEndpointFilter<DocumentAuthorizationFilter>()
   .RequireAuthorization();

// ❌ WRONG: Don't use global middleware for resource checks
app.UseMiddleware<AuthorizationMiddleware>();
```

### From ADR-010: DI Minimalism
```csharp
// ✅ CORRECT: Minimal registrations with concretes
services.AddSingleton<SpeFileStore>();
services.AddSingleton<AuthorizationService>();
services.AddSingleton<GraphClientFactory>();

// ❌ WRONG: Interface for everything
services.AddScoped<ISpeFileStore, SpeFileStore>();  // Unnecessary interface
```

## OBO (On-Behalf-Of) Flow

The API uses OBO to exchange user tokens for Graph API tokens:

```
PCF Control                    BFF API                      Graph API
    |                              |                            |
    |-- Token A (user) ---------->|                            |
    |                              |-- OBO Exchange ---------->|
    |                              |<-- Token B (graph) -------|
    |                              |                            |
    |                              |-- Graph Call (Token B) -->|
    |<-- Response ----------------|<-- Response --------------|
```

**Token Scopes:**
- PCF requests: `api://{bff-client-id}/user_impersonation`
- BFF exchanges for: `FileStorageContainer.Selected`, `Files.Read.All`

## Endpoint Patterns

### Standard Response Format
```csharp
// Success
return Results.Ok(new { id = item.Id, name = item.Name });

// Created
return Results.Created($"/items/{id}", item);

// Error - use ProblemDetails
return Results.Problem(
    detail: "Container not found",
    statusCode: 404,
    title: "Not Found");
```

### Health Check
```csharp
app.MapGet("/healthz", async (SpeFileStore store) =>
{
    var healthy = await store.CanConnectAsync();
    return healthy ? Results.Ok() : Results.StatusCode(503);
});
```

## Testing Guidelines

```csharp
// Unit test pattern
[Fact]
public async Task GetDocument_ReturnsStream_WhenDocumentExists()
{
    // Arrange
    var mockStore = new Mock<SpeFileStore>();
    mockStore.Setup(s => s.GetFileContentAsync("doc-1"))
             .ReturnsAsync(new MemoryStream());
    
    // Act
    var result = await DocumentEndpoints.GetDocument("doc-1", mockStore.Object);
    
    // Assert
    result.Should().BeOfType<StreamHttpResult>();
}
```

## Configuration

**Required settings (via Azure Key Vault or appsettings):**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "{tenant-id}",
    "ClientId": "{bff-client-id}",
    "ClientSecret": "{bff-client-secret}"
  },
  "SharePointEmbedded": {
    "ContainerTypeId": "{container-type-id}"
  }
}
```

## Common Patterns

### Logging with Correlation
```csharp
logger.LogInformation(
    "Processing document {DocumentId} for user {UserId}",
    documentId,
    context.User.Identity?.Name);
```

### Error Handling
```csharp
try
{
    return await fileStore.UploadAsync(file);
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return Results.Problem(
        detail: "Container not found",
        statusCode: 404);
}
catch (Exception ex)
{
    logger.LogError(ex, "Upload failed for {FileName}", file.FileName);
    return Results.Problem(
        detail: "An error occurred during upload",
        statusCode: 500);
}
```

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Use `SpeFileStore` for all SPE operations | Inject `GraphServiceClient` into endpoints |
| Use endpoint filters for authorization | Use global authorization middleware |
| Return `ProblemDetails` for errors | Return raw exception messages |
| Log with structured properties | Use string interpolation in logs |
| Keep endpoints thin (delegate to services) | Put business logic in endpoints |

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
