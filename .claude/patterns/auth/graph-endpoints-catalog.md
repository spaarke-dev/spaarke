# BFF Graph Endpoints Catalog

> **Domain**: Microsoft Graph API / BFF API Surface
> **Last Validated**: 2026-03-09
> **Purpose**: Inventory of all Graph API operations the BFF already supports.
> Check this before adding new Graph features to avoid duplication.

---

## User-Facing OBO Endpoints (Api/OBOEndpoints.cs)

All use `ForUserAsync(ctx, ct)` — user-context operations via On-Behalf-Of flow.

| Method | BFF Route | Graph API Called | Purpose |
|--------|-----------|------------------|---------|
| GET | `/api/obo/containers/{id}/children` | `Drives[driveId].Items` | List children with paging/ordering |
| PUT | `/api/obo/containers/{id}/files/{*path}` | `Drives[driveId].Items[...].Content` | Small file upload (< 4MB) |
| POST | `/api/obo/drives/{driveId}/upload-session` | `Drives[driveId].Items[...].CreateUploadSession` | Large file upload session |
| PUT | `/api/obo/upload-session/chunk` | Upload session URL (direct PUT) | Upload file chunk |
| PATCH | `/api/obo/drives/{driveId}/items/{itemId}` | `Drives[driveId].Items[itemId]` | Rename/move item |
| GET | `/api/obo/drives/{driveId}/items/{itemId}/content` | `Drives[driveId].Items[itemId].Content` | Download (supports Range, ETag) |
| DELETE | `/api/obo/drives/{driveId}/items/{itemId}` | `Drives[driveId].Items[itemId]` | Delete item |

**Rate limits**: `graph-read` and `graph-write` endpoint filter groups.

---

## Document Endpoints (Api/DocumentEndpoints.cs)

| Method | BFF Route | Auth Mode | Graph/Service Called | Purpose |
|--------|-----------|-----------|----------------------|---------|
| GET | `/api/documents/{id}/preview-url` | OBO | `DriveItemOperations` → Graph Drives | Get ephemeral iframe preview URL |
| GET | `/api/documents/{id}/open-links` | OBO | `DriveItemOperations` → Graph Drives | Get web URL + desktop protocol URL |

---

## User Info Endpoints

| Method | BFF Route | Auth Mode | Graph API | Purpose |
|--------|-----------|-----------|-----------|---------|
| GET | `/api/user/me` | OBO | `/me` | Display name, UPN, OID |
| GET | `/api/user/capabilities/{containerId}` | OBO | `Containers[id].Drive` | Read/write/delete permissions check |

**Implementation**: `Infrastructure/Graph/UserOperations.cs`

---

## SPE Container Operations (Internal)

Not directly exposed as endpoints — called by other services via `SpeFileStore` facade.

| Operation | Auth Mode | Graph API | Implementation |
|-----------|-----------|-----------|----------------|
| Create container | App-Only | `Storage.FileStorage.Containers` | `ContainerOperations.cs` |
| Get container drive | App-Only | `Storage.FileStorage.Containers[id].Drive` | `ContainerOperations.cs` |
| List containers | App-Only | `Storage.FileStorage.Containers` | `ContainerOperations.cs` |
| List drive items | App-Only | `Drives[id].Items` (filter by parent) | `DriveItemOperations.cs` |
| Get drive item metadata | App-Only | `Drives[id].Items[itemId]` | `DriveItemOperations.cs` |
| Download file content | OBO | `Drives[id].Items[itemId].Content` | `DriveItemOperations.cs` |

**Note**: SPE container management requires beta Graph API endpoint. File operations work on v1.0.

---

## Communication / Email Operations

| Operation | Auth Mode | Graph API | Implementation |
|-----------|-----------|-----------|----------------|
| Send email (shared mailbox) | App-Only | `Users[mailbox].SendMail` | `CommunicationService.cs` |
| Send email (as user) | OBO | `Me.SendMail` | `CommunicationService.SendAsUserAsync()` |
| Read inbound emails | App-Only | `Users[mailbox].Messages`, `MailFolders` | `InboundEmailService.cs` |
| Create webhook subscription | App-Only | `Subscriptions` (POST) | `GraphSubscriptionManager.cs` |
| Renew webhook subscription | App-Only | `Subscriptions[id]` (PATCH) | `GraphSubscriptionManager.cs` |
| Delete webhook subscription | App-Only | `Subscriptions[id]` (DELETE) | `GraphSubscriptionManager.cs` |

**Webhook lifecycle**: 3-day max subscription TTL, renewed when < 24 hours remaining, checked every 30 minutes by `PeriodicTimer` background service.

---

## Graph Permissions Required (Summary)

| Domain | Delegated (OBO) | Application (App-Only) |
|--------|-----------------|------------------------|
| **SPE Files** | `Files.Read.All`, `Files.ReadWrite.All`, `FileStorageContainer.Selected`, `Sites.FullControl.All` | `Files.Read.All`, `FileStorageContainer.Selected` |
| **Email** | `Mail.Send` | `Mail.Send`, `Mail.Read` |
| **User** | `User.Read` (via OBO) | `User.Read.All` |

Full inventory: [oauth-scopes.md](oauth-scopes.md#complete-graph-permission-inventory)

---

## Error Handling Convention

All Graph-calling code follows the same error handling pattern:

```csharp
try
{
    var graphClient = await _factory.ForUserAsync(ctx, ct);
    // ... Graph operation ...
}
catch (UnauthorizedAccessException)
{
    return Results.Problem(statusCode: 401, title: "Authentication required");
}
catch (ODataError odataError)
{
    return ProblemDetailsHelper.FromGraphException(odataError);
    // Maps to ProblemDetails with graphErrorCode and graphRequestId extensions
}
```

---

## Before Adding a New Graph Feature

1. **Check this catalog** — Does the operation already exist?
2. **Choose auth mode** — See [Auth Mode Decision Guide](oauth-scopes.md#auth-mode-decision-guide)
3. **Follow the architecture** — See [Graph SDK v5](graph-sdk-v5.md#adding-a-new-graph-feature-step-by-step)
4. **Check permissions** — See [OAuth Scopes](oauth-scopes.md#adding-new-graph-permissions)

---

**Lines**: ~120
