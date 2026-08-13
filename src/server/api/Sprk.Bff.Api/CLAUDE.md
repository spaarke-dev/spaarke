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
│           └── AgentToolCatalogProjector.cs # Closed-catalog tool projection (ADR-039)
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

## Auth (Spaarke Auth v2 — [ADR-028](../../../../.claude/adr/ADR-028-spaarke-auth-architecture.md))

**Server outbound (canonical)**: Graph + Dataverse use `DefaultAzureCredential` (managed identity) when `Graph__ManagedIdentity__Enabled=true`. `ClientSecretCredential` is local-dev fallback only.

**Three auth paths** in the BFF:

| Path | When | Mechanism |
|---|---|---|
| **OBO** (delegated) | User-initiated operation acting on behalf of the caller (e.g., user opens a doc) | User token exchanged for downstream Graph token. Still requires `BFF-API-ClientSecret` (confidential client per OAuth spec). |
| **Managed Identity** (app-only, canonical) | Background jobs, system-level container ops, polling, indexing — no acting user | `DefaultAzureCredential` resolves the App Service's system-assigned MI. Mailbox-scoped Graph (`Mail.*`) ALSO requires Exchange `ApplicationAccessPolicy` scoping the MI to allowed mailboxes (Phase C). |
| **Named API key schemes** | Inbound from trusted external systems (BuilderAdmin, Rag) | `AuthenticationHandler<>` per-scheme with `CryptographicOperations.FixedTimeEquals` constant-time compare. |

### OBO flow (delegated path)

```
PCF Control / Code Page         BFF API                      Graph API
    |                              |                            |
    |-- Token A (user) ---------->|                            |
    |  via authenticatedFetch     |                            |
    |                              |-- OBO Exchange ---------->|
    |                              |<-- Token B (graph) -------|
    |                              |                            |
    |                              |-- Graph Call (Token B) -->|
    |<-- Response ----------------|<-- Response --------------|
```

**Token Scopes:**
- Client requests: `api://{bff-client-id}/SDAP.Access`
- BFF exchanges for: `FileStorageContainer.Selected`, `Files.Read.All` (per operation)

### Client contract (read this when authoring PCFs, Code Pages, or Office Add-ins)

Per ADR-028, clients use `useAuth()` + `authenticatedFetch` from `@spaarke/auth`. The BFF is on the server side of this contract — endpoint handlers receive validated JWT, do OBO exchange when needed, return data. Do NOT add `accessToken: string` props to client components or require clients to send custom headers; clients use the `@spaarke/auth` standard contract.

### Auth-related infrastructure files in this module

- `Infrastructure/Graph/GraphClientFactory.cs` — Graph client construction (OBO + MI cascade per `Graph__ManagedIdentity__Enabled`)
- `Services/GraphTokenCache.cs` — Server-side OBO token cache (Redis, ADR-009)
- `Infrastructure/Auth/` — Webhook HMAC validation, named API key schemes (Phase C)
- `Middleware/AuditEnrichmentMiddleware.cs` — Per-request enrichment with `oid`, `appid`, `obo`, `tenantId`, `correlationId`

**Operator setup**: New environments follow [`docs/guides/auth-deployment-setup.md`](../../../../docs/guides/auth-deployment-setup.md) — 10-section runbook including §3 App Service settings, §5 MI Graph permission grants, §6 Dataverse Application User, §7 Exchange ApplicationAccessPolicy (required for Email/Communication modules).

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

**Required settings** — full canonical inventory in [`docs/guides/auth-deployment-setup.md`](../../../../docs/guides/auth-deployment-setup.md) §3:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "{tenant-id}",
    "ClientId": "{bff-client-id}",
    "ClientSecret": "{bff-client-secret}"  // OBO ONLY (confidential client per OAuth spec). Graph + Dataverse use Managed Identity per ADR-028 when Graph__ManagedIdentity__Enabled=true. ClientSecret is fallback for local dev.
  },
  "Graph": {
    "ManagedIdentity": {
      "Enabled": "true"  // CANONICAL in Azure environments per ADR-028
    }
  },
  "SharePointEmbedded": {
    "ContainerTypeId": "{container-type-id}"
  },
  "Communication": {
    "WebhookSigningKey": "{kv-ref}",     // HMAC-SHA256 for Graph subscription webhooks
    "WebhookClientState": "{kv-ref}"     // Graph-native subscription validation
  },
  "EmailProcessing": {
    "WebhookSigningKey": "{kv-ref}"      // HMAC-SHA256 for Dataverse Service Endpoint webhooks
  }
}
```

> **Auth v2 (ADR-028) note**: `BFF-API-ClientSecret` (Key Vault) is retained ONLY for OBO. After Phase C, Graph + Dataverse app-only access uses `DefaultAzureCredential` (MI). When provisioning a new environment, follow the full auth runbook before setting `Graph__ManagedIdentity__Enabled=true` (especially §5 MI Graph permission grants and §7 Exchange ApplicationAccessPolicy if Email/Communication enabled).

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
catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
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
    → AgentToolCatalogProjector (sprk_analysistool rows → DocumentSearchHandler /
      KnowledgeRetrievalHandler / ...) → RagService → Azure AI Search
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

> **Updated 2026-08-13 (dotnet-10-upgrade-r1 task 033)**: bumped `Microsoft.Graph` 5.105.0 → 6.5.0.
> Kiota is now a **transitive-only** dependency (pulled via `Microsoft.Graph.Core 4.0.1`) at
> **2.0.0** — the 7 direct `Microsoft.Kiota.*` `PackageReference`s that previously pinned the
> version-match invariant have been **deleted**. See
> `projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md` for the full call-site
> sizing and `notes/kiota-cve-finding.md` for the CVE history (CVE-2026-44503 /
> GHSA-7j59-v9qr-6fq9, High — fixed at Kiota ≥1.22.0; transitive 2.0.0 is well above that floor).

The BFF API uses the Microsoft.Graph SDK, which depends on Kiota packages. **All resolved Kiota
assemblies must be the same version** to avoid assembly binding errors at runtime — this is now
satisfied **transitively** by Graph 6.x, not by direct pins.

#### Required Package (direct)

```xml
<!-- Microsoft Graph SDK v6.x — pulls Microsoft.Graph.Core 4.0.1 + transitive
     Microsoft.Kiota.* 2.0.x. Do NOT add direct Microsoft.Kiota.* PackageReferences
     unless a genuine transitive-version conflict forces one (document the reason inline
     if you do). -->
<PackageReference Include="Microsoft.Graph" Version="6.5.0" />
```

#### Why No Direct Kiota Pins Anymore

Previously, 7 direct `Microsoft.Kiota.*` pins existed solely to float the transitive graph to
1.22.0 to clear CVE-2026-44503 while staying on `Microsoft.Graph 5.x`. Under `Microsoft.Graph
6.5.0`, the transitive Kiota version (2.0.0) is already above that CVE floor, so the pins became
pure maintenance burden (7 lines to keep in lockstep on every Graph bump) with no remaining
purpose. Deleting them does **not** reintroduce the historical assembly-binding-conflict risk —
that risk was from *partial* direct updates (e.g., bumping `Abstractions` but not
`Serialization.Json`); with zero direct pins, NuGet resolves every Kiota assembly to the single
version Graph.Core's own dependency graph specifies.

#### If a Transitive Kiota Conflict Ever Forces a Direct Pin

1. Confirm the conflict is real: `dotnet list package --include-transitive | grep -i kiota` —
   look for more than one distinct Kiota version across the resolved graph.
2. If forced to pin, pin **ALL** `Microsoft.Kiota.*` references to the SAME version (never a
   partial set) and add an inline comment explaining which conflict required it.
3. Re-verify with `dotnet list package --include-transitive | grep -i kiota` before committing.
4. Build and test locally before deploying.

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
