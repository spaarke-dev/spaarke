# RAG Indexing Configuration Notes

> **Date**: January 19, 2026
> **Session**: Semantic Search and Document Visualization Fixes

---

## Services Required for RAG Indexing

### Core Settings (Required)

| Setting | Purpose | Example Value |
|---------|---------|---------------|
| `DocumentIntelligence__Enabled` | Master AI switch | `true` |
| `DocumentIntelligence__OpenAiEndpoint` | Azure OpenAI endpoint | `https://spaarke-openai-dev.openai.azure.com/` |
| `DocumentIntelligence__OpenAiKey` | Azure OpenAI API key | (from Key Vault) |
| `DocumentIntelligence__AiSearchEndpoint` | AI Search endpoint | `https://spaarke-search-dev.search.windows.net/` |
| `DocumentIntelligence__AiSearchKey` | AI Search admin key | (from Key Vault) |
| `EmailProcessing__AutoIndexToRag` | Auto-index email attachments | `true` |
| `TENANT_ID` | Tenant ID for index filtering | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |

### For PDF/DOCX Extraction (Optional but recommended)

| Setting | Purpose |
|---------|---------|
| `DocumentIntelligence__DocIntelEndpoint` | Document Intelligence endpoint |
| `DocumentIntelligence__DocIntelKey` | Document Intelligence API key |

---

## Pending Work Items

### 1. Dataverse Tracking Fields

Add these fields to the `sprk_document` entity to track indexing status:

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_searchindexed` | Two Options (Boolean) | Whether document is in AI Search index |
| `sprk_searchindexedon` | DateTime (with time) | When document was last indexed |
| `sprk_searchindexname` | Single Line of Text | Which index contains the document |

**Implementation**: After successful indexing, update the Document record with these fields.

### 2. Manual Indexing Utility

Create an API endpoint and ribbon button to manually trigger RAG indexing:

**API Endpoint**:
- `POST /api/ai/index/{documentId}` - Manually index a document to AI Search
- Should retrieve document's SPE file and run through indexing pipeline
- Update tracking fields on success

**Ribbon Button**:
- Add to Document form command bar
- Label: "Index to Search" or "Add to Knowledge Base"
- Calls the manual indexing endpoint with current document ID
- Shows success/failure notification

---

## Session Fixes Applied

1. **GetDocumentAsync Fix**: Changed from specific ColumnSet fields to `ColumnSet(true)` to avoid "attribute not found" errors when Dataverse schema differs.

2. **Deployed via Kudu**: Manual deployment to `spe-api-dev-67e2xz` App Service.

3. **Direct Relationships**: Now working in DocumentRelationshipViewer PCF.

4. **Semantic Search**: Requires documents to be in AI Search index. Currently `AutoIndexToRag=true` was enabled for future documents.

---

## Debug Scripts Created

- `scripts/debug/Query-IndexDocuments.ps1` - Query AI Search index statistics
- `scripts/debug/Query-ByTenant.ps1` - Query documents by tenant ID
- `scripts/debug/Check-DocumentInIndex.ps1` - Check if specific document is indexed
- `scripts/debug/Test-DebugEndpoint.ps1` - Test the debug endpoint

---

## Index Information

- **Index Name**: `spaarke-knowledge-index-v2`
- **Service**: `spaarke-search-dev`
- **Vector Field**: `documentVector3072` (3072 dimensions, text-embedding-3-large)
- **Tenant IDs in Index**:
  - `a221a95e-6abc-4434-aecc-e48338a1b2f2` (Spaarke Inc.) - 21 documents
  - `dae9d4d3-5d57-4d6e-866a-fd29359f6623` (old test data) - 15 documents
