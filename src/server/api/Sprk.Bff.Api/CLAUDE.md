# CLAUDE.md - Sprk.Bff.Api Module

> **Last Updated**: March 4, 2026
>
> **Purpose**: Module-specific instructions for the Spaarke BFF (Backend-for-Frontend) API.
>
> **See also**: [SDAP System Overview](../../../docs/architecture/sdap-overview.md) for full platform architecture and component model.

## Module Overview

**Sprk.Bff.Api** is the unified .NET 8 Minimal API serving as the backend for the **SDAP** (Spaarke Data & AI Platform). It provides 7 functional domains:

- **SPE / Documents**: SharePoint Embedded file operations, OBO token exchange, container management
- **AI Platform**: Chat (SSE), document analysis, RAG search, playbooks, knowledge bases, semantic search
- **Office Add-ins**: Outlook/Word document save, entity search, sharing
- **Email / Communication**: Email-to-document automation, outbound communications
- **Finance Intelligence**: Invoice classification, field extraction, financial aggregation
- **Workspace / Portfolio**: Portfolio analytics, priority scoring, briefing generation
- **Background Processing**: 13+ async job handlers via Azure Service Bus

**Scale**: 120+ endpoints, 99+ DI registrations, 13+ background job types.

## Key Files

```
Sprk.Bff.Api/
├── Program.cs                 # Entry point, DI configuration, middleware
├── Api/
│   ├── Ai/
│   │   ├── ChatEndpoints.cs               # /api/ai/chat/* — session, message, playbook discovery
│   │   ├── DocumentIntelligenceEndpoints.cs
│   │   ├── AnalysisEndpoints.cs
│   │   └── SemanticSearchEndpoints.cs
│   ├── DocumentEndpoints.cs
│   ├── ContainerEndpoints.cs
│   └── HealthEndpoints.cs
├── Models/Ai/Chat/
│   ├── ChatSession.cs                     # Session record (includes HostContext)
│   ├── ChatContext.cs                     # ChatContext + ChatKnowledgeScope
│   └── ChatHostContext.cs                 # Entity-aware host context record
├── Services/
│   ├── SpeFileStore.cs                    # SPE operations facade (ADR-007)
│   ├── AuthorizationService.cs
│   ├── GraphClientFactory.cs
│   └── Ai/
│       ├── IRagService.cs                 # RAG search with extended filter options
│       ├── RagService.cs                  # OData filter builder (search.in, boolean logic)
│       ├── ScopeResolverService.cs        # Resolves knowledge source IDs from playbook
│       └── Chat/
│           ├── ChatSessionManager.cs      # Session lifecycle + HostContext storage
│           ├── IChatContextProvider.cs     # Context resolution interface
│           ├── PlaybookChatContextProvider.cs # Playbook-driven context + entity scope
│           ├── SprkChatAgentFactory.cs     # Agent construction with context
│           └── Tools/
│               ├── DocumentSearchTools.cs  # Entity-scoped search discovery
│               └── KnowledgeRetrievalTools.cs # Knowledge source-scoped retrieval
├── Filters/                               # Endpoint filters for auth (ADR-008)
│   └── DocumentAuthorizationFilter.cs
└── appsettings.json                       # Configuration template
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

## AI Chat System

The Chat system provides playbook-driven conversational AI with entity-scoped RAG search.

### Chat Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/ai/chat/sessions` | Create session (accepts `HostContext`) |
| POST | `/api/ai/chat/sessions/{id}/switch` | Switch playbook/document context |
| POST | `/api/ai/chat/sessions/{id}/messages` | Send message (SSE streaming) |
| GET | `/api/ai/chat/playbooks` | List available playbooks (pre-session) |

### Key Models

- **ChatHostContext**: Record describing where SprkChat is embedded (EntityType, EntityId, WorkspaceType). Validates against `ParentEntityContext.EntityTypes`.
- **ChatKnowledgeScope**: Carries knowledge source IDs, entity scope, and inline content for tool construction.
- **RagSearchOptions**: Extended with `ExcludeKnowledgeSourceIds`, `RequiredTags`, `ExcludeTags`, `ParentEntityType`, `ParentEntityId` for boolean filter logic.

### Pipeline Flow

```
ChatEndpoints → ChatSessionManager → SprkChatAgentFactory
  → PlaybookChatContextProvider → ChatKnowledgeScope
    → DocumentSearchTools / KnowledgeRetrievalTools → RagService → Azure AI Search
```

HostContext flows through every layer. When null, search remains tenant-wide (backward compatible).

**See**: [SPAARKE-AI-ARCHITECTURE.md Section 18](../../../../docs/guides/SPAARKE-AI-ARCHITECTURE.md#18-sprkchat-system--conversational-ai-with-rag-scoping-2026-02-24)

---

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Use `SpeFileStore` for all SPE operations | Inject `GraphServiceClient` into endpoints |
| Use endpoint filters for authorization | Use global authorization middleware |
| Return `ProblemDetails` for errors | Return raw exception messages |
| Log with structured properties | Use string interpolation in logs |
| Keep endpoints thin (delegate to services) | Put business logic in endpoints |

---

## Package Management

### Microsoft.Graph and Kiota Packages

The BFF API uses Microsoft.Graph SDK which depends on Kiota packages. **All Kiota packages must be the same version** to avoid assembly binding errors at runtime.

#### Required Packages (must be same version)

```xml
<!-- Microsoft Graph SDK -->
<PackageReference Include="Microsoft.Graph" Version="5.99.0" />

<!-- Kiota packages - ALL must match -->
<PackageReference Include="Microsoft.Kiota.Abstractions" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Authentication.Azure" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Http.HttpClientLibrary" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Form" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Json" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Multipart" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Text" Version="1.21.1" />
```

#### Why This Matters

Microsoft.Graph pulls Kiota packages as transitive dependencies. If you only update direct refs (Abstractions, Authentication.Azure), the transitive packages stay at older versions, causing:

```
FileNotFoundException: Could not load file or assembly
'Microsoft.Kiota.Abstractions, Version=1.17.1.0'
```

#### When Updating Kiota

1. Update **ALL** Kiota package references to the same version
2. Verify with `dotnet list package --include-transitive | grep -i kiota`
3. Build and test locally before deploying

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
