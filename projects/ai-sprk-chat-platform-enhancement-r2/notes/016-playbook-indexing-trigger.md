# 016 — Playbook Embedding Indexing Trigger

## Endpoint

```
POST /api/ai/playbooks/{playbookId}/index
```

- **Auth**: RequireAuthorization (service-to-service via Dataverse plugin with service account token)
- **Body**: None (playbookId in route is sufficient)
- **Response**: `202 Accepted` (immediate, before embedding generation starts)
- **Error**: `503 Service Unavailable` if background service not started

## Expected Call Pattern (for Dataverse Plugin Developer)

The Dataverse plugin should call this endpoint on **post-create** and **post-update** of the `sprk_analysisplaybook` entity.

### Plugin Registration

| Event | Entity | Stage | Mode |
|-------|--------|-------|------|
| Create | sprk_analysisplaybook | Post-Operation | Async |
| Update | sprk_analysisplaybook | Post-Operation | Async |

### Plugin Implementation

```csharp
// In the plugin Execute method:
var playbookId = context.PrimaryEntityId;

// Call BFF API endpoint (fire-and-forget from plugin perspective)
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", serviceAccountToken);

var response = await httpClient.PostAsync(
    $"https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/playbooks/{playbookId}/index",
    null);

// Response is 202 Accepted — don't wait for indexing to complete
```

### Important Notes

1. The endpoint returns 202 immediately — do NOT wait for indexing
2. Eventual consistency: ~5-30 seconds before the playbook appears in search results
3. De-duplication is handled server-side (merge-or-upload with playbookId as key)
4. If the playbook is deleted, a separate `DELETE` operation on the index is needed (use `PlaybookEmbeddingService.DeletePlaybookAsync` directly or create a delete endpoint)

## Architecture

```
Dataverse Plugin (post-create/update)
  → POST /api/ai/playbooks/{id}/index (202 Accepted)
    → Channel<string> (bounded, capacity 100)
      → PlaybookIndexingBackgroundService
        → Fetch playbook from Dataverse (IPlaybookService)
        → Generate embedding (IOpenAiClient, text-embedding-3-large)
        → Upsert into playbook-embeddings index (MergeOrUpload)
```
