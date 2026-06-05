# Spec — Spaarke AI Assistant: new AI Search index + SPE container

> **Status**: Draft — pending team review
> **Date**: 2026-05-28

## 1. Azure AI Search index — `spaarke-file-index`

### 1.1 Service + naming

| Setting | Value |
|---|---|
| Search service | `spaarke-search-dev` (existing, no change) |
| Index name | **`spaarke-file-index`** |
| Replicas / partitions | Same as current `spaarke-knowledge-index-v2` |
| API version | `2024-07-01` |

### 1.2 Field schema

25 retained from existing index (with one critical correction) + 3 new fields = **28 total**. Every field that any current AI consumer reads, writes, filters, searches, sorts, facets, or projects.

| # | Field | Type | F | S | O | Fc | R | K | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | `id` | Edm.String | . | . | . | . | Y | **Y** | Primary key. Format: `{documentId}_k_{chunkIndex}` per RagIndexingPipeline. |
| 2 | `tenantId` | Edm.String | **Y** | . | . | **Y** | Y | . | ADR-014 tenant isolation. EVERY query filters on this. |
| 3 | `deploymentId` | Edm.String | **Y** | . | . | **Y** | Y | . | `sprk_aiknowledgedeployment` record id; nullable. |
| 4 | `deploymentModel` | Edm.String | **Y** | . | . | **Y** | Y | . | Shared / Dedicated / CustomerOwned. |
| 5 | `knowledgeSourceId` | Edm.String | **Y** | . | . | **Y** | Y | . | Used with `search.in()` for multi-source scope. |
| 6 | `knowledgeSourceName` | Edm.String | . | **Y** | **Y** | . | Y | . | Display + sort. Analyzer: standard.lucene. |
| 7 | `documentId` | Edm.String | **Y** | . | . | . | Y | . | Source `sprk_document` GUID; nullable for orphan files. |
| 8 | `speFileId` | Edm.String | **Y** | . | . | . | Y | . | SPE file id; always populated. |
| 9 | `documentName` | Edm.String | . | **Y** | **Y** | . | Y | . | Display + sort. Analyzer: standard.lucene. |
| 10 | `fileName` | Edm.String | . | **Y** | **Y** | . | Y | . | Display + sort. Analyzer: standard.lucene. |
| 11 | `documentType` | Edm.String | **Y** | . | . | **Y** | Y | . | contract, policy, procedure, ... (legacy). |
| 12 | `fileType` | Edm.String | **Y** | . | . | **Y** | Y | . | pdf, docx, xlsx, ... (modern). |
| 13 | `chunkIndex` | Edm.Int32 | . | . | **Y** | . | Y | . | Position within document. |
| 14 | `chunkCount` | Edm.Int32 | . | . | . | . | Y | . | Total chunks for UI pagination. |
| 15 | `content` | Edm.String | . | **Y** | . | . | Y | . | Primary search field. Analyzer: standard.lucene. Used for highlighting. |
| 16 | `contentVector3072` | Collection(Edm.Single) | . | (vector) | . | . | Y | . | 3072-dim chunk vector. Profile: `knowledge-vector-profile-3072`. |
| 17 | `documentVector3072` | Collection(Edm.Single) | . | (vector) | . | . | Y | . | 3072-dim document-level vector (L2-normalized average of chunks). |
| 18 | `metadata` | Edm.String | . | . | . | . | Y | . | JSON extensibility. |
| 19 | `tags` | Collection(Edm.String) | **Y** | **Y** | . | **Y** | Y | . | OR/AND/NOT semantics via `tags/any(...)`. |
| 20 | `createdAt` | Edm.DateTimeOffset | **Y** | . | **Y** | . | Y | . | Indexed timestamp. |
| 21 | `updatedAt` | Edm.DateTimeOffset | **Y** | . | **Y** | . | Y | . | Last update timestamp. |
| 22 | `parentEntityType` | Edm.String | **Y** | **Y** | **Y** | **Y** | Y | . | matter, project, invoice, account, contact. |
| 23 | `parentEntityId` | Edm.String | **Y** | . | . | . | Y | . | Parent entity GUID. |
| 24 | `parentEntityname` | Edm.String | **Y** | **Y** | **Y** | **Y** | Y | . | Note: lowercase 'name' (existing convention; do not rename — code reads it as `parentEntityname`). |
| 25 | **`privilege_group_ids`** | Collection(Edm.String) | **Y** ← FIX | . | . | . | Y | . | **THE FIX** — currently `filterable: false` in v2 index. AAD group IDs for ACL trimming. Empty = public. |
| 26 | `containerId` | Edm.String | **Y** | . | . | **Y** | Y | . | **NEW** — SPE container scoping. Enables querying for files in a specific container (e.g., the new Spaarke Dev Container 2 only). |
| 27 | `lastModified` | Edm.DateTimeOffset | **Y** | . | **Y** | . | Y | . | **NEW** — staleness detection. Tracks the source file's lastModifiedDateTime, distinct from index `updatedAt`. |
| 28 | `sourceSystem` | Edm.String | **Y** | . | . | **Y** | Y | . | **NEW** — origin tracking (SharePointEmbedded, Outlook, Manual). For multi-source future. |

**Legend**: F = filterable, S = searchable, O = sortable, Fc = facetable, R = retrievable, K = key. **Bold** = required flag; `.` = false; `Y` = true.

**Removed from v2 → new schema** (intentional drops):
- `contentVector` (1536-dim, legacy) — text-embedding-3-large is canonical per ADR-013/R3 standardization
- `documentVector` (1536-dim, legacy) — same

### 1.3 Vector configuration

```json
"vectorSearch": {
  "algorithms": [
    {
      "name": "knowledge-hnsw",
      "kind": "hnsw",
      "hnswParameters": { "m": 4, "efConstruction": 400, "efSearch": 500, "metric": "cosine" }
    }
  ],
  "profiles": [
    {
      "name": "knowledge-vector-profile-3072",
      "algorithm": "knowledge-hnsw"
    }
  ]
}
```

Single 3072-dim profile. No vectorizer (BFF computes embeddings outside the index via OpenAiClient and writes them at index time — see `RagIndexingPipeline`).

### 1.4 Semantic configuration

```json
"semantic": {
  "configurations": [
    {
      "name": "knowledge-semantic-config",
      "prioritizedFields": {
        "titleField": { "fieldName": "documentName" },
        "prioritizedContentFields": [ { "fieldName": "content" } ],
        "prioritizedKeywordsFields": [ { "fieldName": "tags" } ]
      }
    }
  ]
}
```

Same name as v2 index for code compatibility. `BulkRagIndexingJobHandler` and `RagService` reference `semantic-config` / `knowledge-semantic-config` by name.

### 1.5 CORS, scoring profiles, etc.

- **CORS**: same as v2 (allow `*` for dev; explicit hosts for prod).
- **Scoring profile**: none initially. Add later if specific use cases need re-ranking weights.
- **Suggesters**: none.

## 2. SharePoint Embedded container — `Spaarke Dev Container 2`

### 2.1 Container details (decided 2026-05-28)

| Setting | Value |
|---|---|
| Container display name | **`Spaarke Dev Container 2`** |
| Container type name | `Spaarke PAYGO 1` |
| ContainerTypeId | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` |
| Owning application id | `170c98e1-d486-4355-bcbe-170454e0207c` |
| Classification | Standard |
| Region | eastus |
| Subscription | `Spaarke SPE Subscription 1` (`484bc857-3802-427f-9ea5-ca47b43db0f0`) |
| Resource group | `SharePointEmbedded` |
| SPO Admin URL | `https://spaarke-admin.sharepoint.com` |
| Container id | **TBD — generated at creation** |

### 2.2 Access model

- Same access model as the existing container — created under the same container type with the same owning application.
- BFF MI (`mi-bff-api-dev`, principal `9fd47efb-7962-492b-ac44-e5ccd0268ebb`) needs `FileStorageContainer.Selected` grant on the new container — same pattern as existing.
- OBO users get per-user access through normal Graph permission flow; no special new grants needed.

### 2.3 Out of scope

- Migrating files from the legacy container. Leave as-is per decision 2026-05-28.
- Per-customer container types (handled by SpeAdmin module — separate concern).

## 3. BFF configuration changes

### 3.1 New / changed app settings (dev only initially)

| Setting | Old value | New value | Notes |
|---|---|---|---|
| `AiSearch__KnowledgeIndexName` | `spaarke-knowledge-index-v2` | **`spaarke-file-index`** | Single switch. Read by `RagService`, `DocumentSearchTools`, `BulkRagIndexingJobHandler`. |
| `SharePointEmbedded__DefaultContainerId` | (not set) | **`<new-container-id>`** | New setting. Populated after container creation in Task 002. |
| `SharePointEmbedded__ContainerTypeId` | (existing) | unchanged: `8a6ce34c-6055-4681-8f87-2f4f9f921c06` | Same container type. |

### 3.2 Legacy paths

- `RagService` reading from `spaarke-rag-references` continues to work — that index name comes from a different setting (`AiSearch__RagReferencesIndexName`).
- Insights Engine background jobs that ingest from the legacy index: unchanged config; legacy index stays around.
- The Bulk RAG indexing job for new documents writes to the new index (driven by `AiSearch__KnowledgeIndexName`).

### 3.3 Code paths that need to read the new container id

Currently the BFF reads container ids per-tenant from `sprk_container` Dataverse records (per-customer storage). The new `SharePointEmbedded__DefaultContainerId` setting needs to be wired into:

- The AI Assistant default-context resolver (when no entity context is present) — TBD: which path exactly? Possibly in `PlaybookChatContextProvider` or `SprkChatAgentFactory`.
- Indexing pipeline default scope — `BulkRagIndexingJobHandler` defaults if not provided per-message.

This is a minor wiring change; spec.md is the canonical reference for the new setting name.

## 4. Verification checklist (Phase F of plan.md)

End-to-end smoke after deployment:

- [ ] `spaarke-file-index` is created and visible via Azure portal Search Service blade.
- [ ] All 28 fields are present with correct flags. `privilege_group_ids` shows `filterable: true`.
- [ ] Semantic + vector profiles registered.
- [ ] `Spaarke Dev Container 2` created in `Spaarke PAYGO 1` container type. Container id captured into `SharePointEmbedded__DefaultContainerId`.
- [ ] BFF MI has access to the new container (test via direct Graph call).
- [ ] BFF `AiSearch__KnowledgeIndexName` flipped to `spaarke-file-index`. BFF restarted.
- [ ] Upload a test document to the new container via the existing upload flow.
- [ ] Verify the document is indexed into `spaarke-file-index` (query the index directly with `api-key` header — should return ≥1 chunk with the right `tenantId`, `containerId`, `privilege_group_ids`).
- [ ] Send "find documents about X" from `sprk_spaarkeai` Code Page. App Insights traces show `tool_call` for `DocumentSearchTools.SearchDocumentsAsync` and the OData filter no longer returns 400. The agent quotes citations from the test document.
- [ ] Legacy paths still work: a direct query to `spaarke-knowledge-index-v2` returns existing documents (untouched).

## 5. Rollback

If something goes wrong:

1. Revert `AiSearch__KnowledgeIndexName` to `spaarke-knowledge-index-v2`. BFF restart.
2. The new index + container remain in place (they don't interfere with anything when not used).
3. Document the issue, decide whether to delete + recreate `spaarke-file-index` with corrections.

No data loss in any rollback scenario because nothing has been migrated.
