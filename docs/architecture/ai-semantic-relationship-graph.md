# AI Semantic Relationship Graph — Architecture

> **Last Updated**: February 23, 2026
> **Status**: Production (Dev Environment)
> **Components**: BFF API, PCF Control, Code Page, Azure AI Search, Dataverse

---

## 1. Overview

The Document Relationship Graph is a multi-modal document discovery system that combines **structural relationships** (Dataverse lookups) with **semantic similarity** (Azure AI Search vector search) to build interactive force-directed graph visualizations. Users can explore how documents relate to each other through shared context (same matter, project, email thread) and content similarity (vector embeddings).

### Key Capabilities

| Capability | Description |
|------------|-------------|
| **Structural Discovery** | Documents linked via Dataverse lookups (matter, project, invoice, email) |
| **Semantic Discovery** | Content-similar documents via vector cosine similarity |
| **Hub Topology** | Parent entity hubs (Matter, Project, Invoice, Email) organize structural relationships |
| **Direct Edges** | Semantic relationships connect directly from source to related documents |
| **Dual Frontend** | PCF control (form-embedded) and Code Page (dialog) share the same API |
| **Multi-Tenant** | Tenant isolation on all queries via `tenantId` filter |

### System Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│  Dataverse Form (sprk_document)                                     │
│                                                                     │
│  ┌──────────────────────┐     ┌──────────────────────────────────┐  │
│  │  SemanticSearchControl│     │  DocumentRelationshipViewer PCF  │  │
│  │  (PCF v1.0.23)       │     │  (PCF v1.0.31)                   │  │
│  │                      │     │  Tab: "Find Similar"              │  │
│  │  [Find Similar] btn ─┼──┐  │  context.page.entityId → docId   │  │
│  └──────────────────────┘  │  └───────────────┬──────────────────┘  │
│                            │                  │                     │
└────────────────────────────┼──────────────────┼─────────────────────┘
                             │                  │
                    Opens iframe dialog         │
                             │                  │
                             ▼                  │
              ┌──────────────────────────┐      │
              │  Code Page (HTML)        │      │
              │  sprk_documentrelation-  │      │
              │  shipviewer              │      │
              │  React 19 + @xyflow v12  │      │
              │  URL: ?data=documentId=  │      │
              │  {guid}&tenantId={tid}   │      │
              └────────────┬─────────────┘      │
                           │                    │
                           ▼                    ▼
              ┌──────────────────────────────────────┐
              │  BFF API                              │
              │  GET /api/ai/visualization/           │
              │      related/{documentId}             │
              │                                       │
              │  Authorization: Bearer {MSAL token}   │
              │  ?tenantId=...&threshold=0.65          │
              │   &limit=25&depth=1                    │
              └──────┬──────────────┬─────────────────┘
                     │              │
          ┌──────────▼───┐   ┌─────▼──────────────┐
          │  Dataverse    │   │  Azure AI Search    │
          │  Web API      │   │  spaarke-knowledge  │
          │               │   │  -index-v2          │
          │  Structural   │   │                     │
          │  relationships│   │  documentVector3072  │
          │  (lookups)    │   │  (3072 dims, cosine) │
          └───────────────┘   └─────────────────────┘
```

---

## 2. API Endpoint

### `GET /api/ai/visualization/related/{documentId}`

| Aspect | Detail |
|--------|--------|
| **Auth** | Bearer token (MSAL, `user_impersonation` scope) |
| **Rate Limit** | `ai-batch` policy |
| **Authorization Filter** | `VisualizationAuthorizationFilter` (verifies read access) |
| **P95 Latency Target** | < 500ms for up to 50 nodes |

### Query Parameters

| Parameter | Type | Default | Range | Description |
|-----------|------|---------|-------|-------------|
| `tenantId` | string | *required* | — | Multi-tenant isolation (Dataverse org ID) |
| `threshold` | float | 0.65 | 0.0–1.0 | Minimum cosine similarity for semantic matches |
| `limit` | int | 25 | 1–50 | Maximum related documents per level |
| `depth` | int | 1 | 1–3 | Relationship search depth |
| `includeKeywords` | bool | true | — | Include shared keywords in edges |
| `includeParentEntity` | bool | true | — | Include parent entity info in nodes |
| `documentTypes` | string[] | null | — | Filter by AI Search `documentType` (e.g., "Contract", "Invoice"). Null = all. |
| `relationshipTypes` | string[] | null | — | Filter by relationship type. Null = all 6 types. |

### Response Shape

```json
{
  "nodes": [
    {
      "id": "guid-or-hub-id",
      "type": "source | related | orphan | matter | project | invoice | email",
      "depth": 0,
      "data": {
        "label": "Contract_2024.pdf",
        "documentType": "Contract",
        "fileType": "pdf",
        "speFileId": "...",
        "isOrphanFile": false,
        "similarity": 0.87,
        "extractedKeywords": ["lease", "termination"],
        "createdOn": "2024-01-15T10:00:00Z",
        "modifiedOn": "2024-06-20T14:30:00Z",
        "recordUrl": "https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=...",
        "fileUrl": "https://graph.microsoft.com/v1.0/drives/.../items/...",
        "parentEntityType": "sprk_matter",
        "parentEntityId": "...",
        "parentEntityName": "Acme Corp Lease"
      }
    }
  ],
  "edges": [
    {
      "id": "source-target",
      "source": "...",
      "target": "...",
      "data": {
        "similarity": 0.87,
        "sharedKeywords": ["lease"],
        "relationshipType": "semantic",
        "relationshipLabel": "Content similar"
      }
    }
  ],
  "metadata": {
    "sourceDocumentId": "...",
    "tenantId": "...",
    "totalResults": 12,
    "threshold": 0.65,
    "depth": 1,
    "maxDepthReached": 2,
    "nodesPerLevel": [1, 2, 10],
    "searchLatencyMs": 245,
    "cacheHit": false
  }
}
```

---

## 3. BFF Visualization Pipeline (7 Steps)

The `VisualizationService.GetRelatedDocumentsAsync()` method executes a 7-step pipeline:

```
Step 1          Step 2           Step 3              Step 4
Dataverse       AI Search        Dataverse            AI Search
GetDocument  →  GetSourceDoc  →  GetHardcoded      →  VectorSearch
(source meta)   (get vector)     Relationships        (semantic)
                                 (5 relationship       (cosine sim)
                                  types)
     │               │                │                    │
     └───────┬───────┘                └────────┬───────────┘
             │                                 │
             ▼                                 ▼
          Step 5                            Step 6
          MergeRelationships   →   GetDocumentMetadata
          (deduplicate,             (URLs, keywords,
           hardcoded priority)       timestamps)
                        │
                        ▼
                     Step 7
                     BuildGraphResponseWithHubTopology
                     (nodes + edges + metadata)
```

### Step 1: Fetch Source Document from Dataverse

```
Dataverse Web API → GET sprk_documents({documentId})
```

Retrieves the source document's metadata including relationship lookup fields (`MatterId`, `ProjectId`, `InvoiceId`, `ParentDocumentId`, `EmailConversationIndex`, `IsEmailArchive`).

If the document is not found, returns an empty graph with a diagnostic message.

### Step 2: Get Source Document from Azure AI Search

```
AI Search → GET index/docs?$filter=documentId eq '{id}' and tenantId eq '{tid}'&$top=1
```

Retrieves the source document's `documentVector3072` embedding (3072 dimensions, text-embedding-3-large). This vector is the query input for semantic similarity search in Step 4.

If the document is not in the AI Search index (not yet indexed), semantic search is **skipped** — only structural relationships are returned.

The `IKnowledgeDeploymentService` routes to the correct index based on tenant deployment model:

| Model | Index Name | Description |
|-------|-----------|-------------|
| **Shared** | `spaarke-knowledge-shared` | Multi-tenant, `tenantId` filter |
| **Dedicated** | `{tenantId}-knowledge` | Per-customer index |
| **CustomerOwned** | Customer-specified | Customer's Azure subscription |

### Step 3: Query Structural Relationships from Dataverse

Discovers documents linked through Dataverse lookup fields. Each relationship type has a guard condition and a Dataverse OData query:

| Type | Guard Condition | OData Query Pattern |
|------|----------------|---------------------|
| `same_email` | `IsEmailArchive == true` or `ParentDocumentId` set | `_sprk_parentdocument_value eq {parentId}` |
| `same_thread` | `EmailConversationIndex` set | `startswith(sprk_emailconversationindex, '{prefix44}')` |
| `same_matter` | `MatterId` set | `_sprk_matter_value eq {matterId}` |
| `same_project` | `ProjectId` set | `_sprk_project_value eq {projectId}` |
| `same_invoice` | `InvoiceId` set | `_sprk_invoice_value eq {invoiceId}` |

All structural relationships return similarity score `1.0` and are capped at 50 documents per type.

### Step 4: Query Semantic Relationships from Azure AI Search

Executes a vector similarity search using the source document's `documentVector3072`:

```
AI Search → POST index/docs/search
{
  "search": "*",
  "vectorQueries": [{
    "vector": [source document's 3072-dim embedding],
    "fields": "documentVector3072",
    "k": 50,          // limit * 2
    "kind": "vector"
  }],
  "filter": "tenantId eq '{tid}' and documentId ne '{sourceId}'",
  "top": 50,
  "select": "id, documentId, speFileId, fileName, fileType, documentType, ..."
}
```

**Conditions for execution:**
1. Source document exists in AI Search index
2. Source document has a non-empty vector embedding
3. `relationshipTypes` filter includes "semantic" (or no filter = all types)

**Result processing:**
- Each result's cosine similarity score is compared against `threshold`
- Below-threshold results are discarded
- Results are deduplicated by `GetUniqueId()` (handles multi-chunk documents)
- Processing stops when `limit` results are collected

### Step 5: Merge and Deduplicate

Combines hardcoded and semantic results with **hardcoded taking priority**:

```
Priority Order (lower number wins):
  1. same_email      (highest)
  2. same_thread
  3. same_matter
  4. same_project
  5. same_invoice
  6. semantic         (lowest)
```

**Algorithm:**
1. Add all hardcoded relationships to result dictionary (keyed by `GetUniqueId()`)
2. For duplicate hardcoded entries, keep the higher-priority relationship type
3. Add semantic relationships only if not already present from hardcoded
4. Exclude the source document itself

**`GetUniqueId()`** returns `DocumentId` (preferred), `SpeFileId` (orphan files), or `Id` (fallback), normalized to **lowercase** for case-insensitive deduplication.

### Step 6: Get Document Metadata from Dataverse

For each related document, fetch:
- **Record URL**: `{orgUrl}/main.aspx?etn=sprk_document&id={documentId}&pagetype=entityrecord`
- **File URL**: `https://graph.microsoft.com/v1.0/drives/{graphDriveId}/items/{graphItemId}`
- **Keywords**: Parsed from comma-separated `sprk_keywords` field
- **Timestamps**: `createdOn`, `modifiedOn`

Orphan files (no Dataverse record) get `spe://{speFileId}` as file URL and no record URL.

### Step 7: Build Graph with Hub Topology

Constructs the final graph structure:

```
Source Document (depth 0)
├── Matter Hub ── Doc A, Doc B          (structural: hub topology)
├── Project Hub ── Doc C                (structural: hub topology)
├── Email Hub ── Attachment 1, 2        (structural: hub topology)
├── Doc D (similarity: 0.91)            (semantic: direct edge)
├── Doc E (similarity: 0.87)            (semantic: direct edge)
└── Doc F (similarity: 0.72)            (semantic: direct edge)
```

**Structural relationships** use a hub-and-spoke topology:
- Source → Hub Node (e.g., "matter-{guid}")
- Related Document → Hub Node

**Semantic relationships** use direct edges:
- Source → Related Document

Hub node IDs follow the pattern: `{type}-{entityId}` (e.g., `matter-abc123`, `email-def456`, `thread-prefix44`).

---

## 4. Azure AI Search Index

### Index: `spaarke-knowledge-index-v2`

#### Key Fields

| Field | Type | Filter | Facet | Purpose |
|-------|------|--------|-------|---------|
| `id` | Edm.String (Key) | — | — | Chunk-level ID: `{documentId}_{chunkIndex}` |
| `tenantId` | Edm.String | Yes | Yes | Multi-tenant isolation |
| `documentId` | Edm.String | Yes | — | Link to `sprk_document` (nullable for orphans) |
| `speFileId` | Edm.String | Yes | — | SharePoint Embedded file ID |
| `fileName` | Edm.String | — | — | File display name (searchable) |
| `documentType` | Edm.String | Yes | Yes | Business classification: Contract, Invoice, Agreement, etc. |
| `fileType` | Edm.String | Yes | Yes | File extension: pdf, docx, msg, xlsx |
| `content` | Edm.String | — | — | Chunk text content (searchable, full-text) |
| `documentVector3072` | Collection(Single) | — | — | **Document-level embedding** (3072 dims) |
| `contentVector3072` | Collection(Single) | — | — | Chunk-level embedding (3072 dims) |
| `parentEntityType` | Edm.String | Yes | Yes | Entity type: matter, project, invoice |
| `parentEntityId` | Edm.String | Yes | — | Parent entity GUID |
| `parentEntityName` | Edm.String | — | — | Parent entity display name (searchable) |
| `tags` | Collection(Edm.String) | Yes | Yes | Categorization tags |
| `createdAt` | DateTimeOffset | Yes | — | Index timestamp (sortable) |
| `updatedAt` | DateTimeOffset | Yes | — | Last update timestamp (sortable) |

#### Vector Configuration

| Setting | Value |
|---------|-------|
| **Embedding Model** | `text-embedding-3-large` (Azure OpenAI) |
| **Dimensions** | 3072 |
| **Algorithm** | HNSW (Hierarchical Navigable Small World) |
| **Distance Metric** | Cosine similarity |
| **HNSW m** | 4 (graph connectivity) |
| **HNSW efConstruction** | 400 (index-time quality) |
| **HNSW efSearch** | 500 (query-time quality) |

#### Visualization vs. Semantic Search

| Aspect | Visualization (this feature) | SemanticSearchControl |
|--------|-----------------------------|-----------------------|
| **Vector Field** | `documentVector3072` | `contentVector3072` |
| **Granularity** | Document-level | Chunk-level |
| **Search Type** | Pure vector (KNN) | Hybrid (RRF: vector + BM25) |
| **Result Unit** | Unique documents | Chunks (grouped by document) |
| **Deduplication** | `GetUniqueId()` | `documentId` grouping |

#### Important: `documentType` vs. `fileType`

The `documentType` field stores **business classification names** (Contract, Invoice, Agreement), not file extensions. The `fileType` field stores extensions (pdf, docx, xlsx).

When filtering by document type in the API, use business names: `?documentTypes=Contract&documentTypes=Invoice`.

---

## 5. Dataverse Integration

### Entity: `sprk_document`

#### Relationship Lookup Fields

| Relationship | Lookup Field | OData Value | Target Entity |
|-------------|-------------|-------------|---------------|
| Same Matter | `sprk_matter` | `_sprk_matter_value` | `sprk_matter` |
| Same Project | `sprk_project` | `_sprk_project_value` | `sprk_project` |
| Same Invoice | `sprk_invoice` | `_sprk_invoice_value` | `sprk_invoice` |
| Email Parent | `sprk_parentdocument` | `_sprk_parentdocument_value` | `sprk_document` |

#### Email-Specific Fields

| Field | Purpose |
|-------|---------|
| `sprk_isemailarchive` | Boolean — true if document is a root .eml email archive |
| `sprk_emailconversationindex` | RFC 2822 conversation threading index (first 44 chars = thread root) |
| `sprk_emailsubject` | Email subject line (used as hub label) |
| `sprk_parentdocument` | Lookup to parent email for attachments |

#### Document Metadata Fields

| Field | Purpose |
|-------|---------|
| `sprk_documentid` | Primary key GUID |
| `sprk_documentname` | Display name |
| `sprk_filename` | File name with extension |
| `sprk_documenttype` | Business classification (Contract, Invoice, etc.) |
| `sprk_keywords` | Comma-separated extracted keywords |
| `sprk_graphitemid` | SharePoint Graph API item ID |
| `sprk_graphdriveid` | SharePoint Graph API drive ID |
| `sprk_spefileid` | SharePoint Embedded file ID |
| `createdon` / `modifiedon` | Timestamps |

---

## 6. Relationship Types

| Type | Priority | Source | Label | Edge Pattern | Score |
|------|----------|--------|-------|-------------|-------|
| `same_email` | 1 (highest) | Dataverse lookup | "From same email" | Document → Email Hub | 1.0 |
| `same_thread` | 2 | Dataverse string prefix | "Same email thread" | Document → Thread Hub | 1.0 |
| `same_matter` | 3 | Dataverse lookup | "Same matter" | Document → Matter Hub | 1.0 |
| `same_project` | 4 | Dataverse lookup | "Same project" | Document → Project Hub | 1.0 |
| `same_invoice` | 5 | Dataverse lookup | "Same invoice" | Document → Invoice Hub | 1.0 |
| `semantic` | 6 (lowest) | AI Search vector | "Content similar" | Source → Document (direct) | 0.0–1.0 |

When a document appears in multiple relationship types, the **highest priority** (lowest number) determines its primary label in the merged result. The document still appears in the graph — only its display label is affected.

---

## 7. Frontend Architecture

### Two-Tier Frontend (ADR-006)

| Aspect | PCF Control | Code Page |
|--------|------------|-----------|
| **Purpose** | Embedded in Dataverse form tab | Dialog opened from SemanticSearchControl |
| **React** | 16 (platform-provided) | 19 (bundled) |
| **Graph Library** | `react-flow-renderer` v10 | `@xyflow/react` v12 |
| **Layout Engine** | `d3-force` v3 | `d3-force` v3 |
| **UI Framework** | Fluent UI v9.46.2 | Fluent UI v9.54.0 |
| **Version** | 1.0.31 | 1.0.0 |
| **Document ID** | `context.page.entityId` | URL param `?data=documentId={guid}` |
| **Tenant ID** | `context.parameters.tenantId` | URL param `tenantId` |
| **Auth** | MSAL SSO (silent → popup fallback) | MSAL SSO (silent → popup fallback) |
| **Filters** | Relationship type checkboxes | Similarity threshold, depth, max nodes, document types |
| **Views** | Graph only | Graph + Grid toggle |
| **Dark Mode** | PCF context → system preference → light | System preference → light |
| **Deployment** | PCF solution ZIP (pac solution import) | Dataverse web resource (HTML) |

### PCF Control: DocumentRelationshipViewer

Placed on the `sprk_document` form's "Find Similar" tab. Gets the record GUID from `context.page.entityId` (runtime API, not in TypeScript types).

**ControlManifest.Input.xml properties:**
- `documentId` (bound, SingleLine.Text) — bound to a text column but **not used for GUID**; the control reads `context.page.entityId` directly
- `tenantId` (input, optional) — Azure AD tenant ID
- `apiBaseUrl` (input, optional) — BFF API base URL (defaults to dev)
- `selectedDocumentId` (output) — emits GUID when user clicks a node

### Code Page: sprk_documentrelationshipviewer

HTML web resource opened as an iframe dialog by `SemanticSearchControl.NavigationService.getFindSimilarUrl()`:

```
{orgUrl}/WebResources/sprk_documentrelationshipviewer
  ?data=documentId%3D{guid}%26tenantId%3D{tid}
```

The Code Page unwraps the `?data=` envelope applied by `Xrm.Navigation.navigateTo()`:

```typescript
const urlParams = new URLSearchParams(window.location.search);
const dataEnvelope = urlParams.get("data");
const params = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : urlParams;
```

### Graph Layout

Both frontends use `d3-force` simulation:

| Force | Configuration | Purpose |
|-------|--------------|---------|
| `forceLink` | distance = `400 * (1 - similarity)` | Higher similarity = shorter edge |
| `forceManyBody` | strength = `-1000` | Node repulsion |
| `forceCenter` | `(0, 0)` | Center gravity |
| `forceCollide` | radius = `100` | Prevent node overlap |
| Source node | `fx=0, fy=0` (fixed) | Anchored at center |

### Node Rendering

Nodes are styled by type:

| Node Type | Visual | Data Source |
|-----------|--------|-------------|
| `source` | Large, highlighted, centered | Source document |
| `related` | Standard size, similarity badge | Related documents |
| `orphan` | Standard, "orphan" indicator | SPE files without Dataverse record |
| `matter` | Hub icon, entity name | Matter entity |
| `project` | Hub icon, entity name | Project entity |
| `invoice` | Hub icon, entity name | Invoice entity |
| `email` | Hub icon, subject line | Email document or thread |

### Relationship Label Mapping (PCF vs. Code Page)

**PCF** uses a **priority system** — each node gets one primary relationship label (highest priority wins):
```typescript
const relationshipPriority = { same_email: 1, same_thread: 2, ..., semantic: 6 };
```

**Code Page** stores an **array of all relationships** per node, with `semantic` preferred as primary for display:
```typescript
const primary = relationships?.find((r) => r.type === "semantic") ?? relationships?.[0];
```

Both approaches display all nodes and edges from the API — the difference is only which label appears on the node tooltip/badge.

---

## 8. Authentication Flow

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Browser      │     │  Azure AD    │     │  BFF API     │
│  (PCF/Code    │     │  (MSAL)      │     │  (Sprk.Bff)  │
│   Page)       │     │              │     │              │
└──────┬───────┘     └──────┬───────┘     └──────┬───────┘
       │                    │                    │
       │ 1. acquireToken    │                    │
       │    Silent()        │                    │
       │───────────────────>│                    │
       │                    │                    │
       │ 2. Token (cached   │                    │
       │    or refreshed)   │                    │
       │<───────────────────│                    │
       │                    │                    │
       │ 3. GET /api/ai/visualization/related/{id}
       │    Authorization: Bearer {token}        │
       │────────────────────────────────────────>│
       │                    │                    │
       │ 4. Graph JSON response                  │
       │<────────────────────────────────────────│
```

**MSAL Configuration:**

| Setting | Value |
|---------|-------|
| Client ID | `170c98e1-d486-4355-bcbe-170454e0207c` (PCF client app) |
| Authority | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Redirect URI | `https://spaarkedev1.crm.dynamics.com` |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |
| Token Fallback | Silent → SSO Silent → Popup |

---

## 9. Performance

### Latency Budget (P95 < 500ms)

| Step | Operation | Estimated Time |
|------|-----------|---------------|
| 1 | Dataverse: Get source document | 10–30ms |
| 2 | AI Search: Get source vector | 5–20ms |
| 3 | Dataverse: Structural relationships | 20–100ms |
| 4 | AI Search: Vector similarity search | 100–300ms |
| 5 | Merge/deduplicate | < 1ms |
| 6 | Dataverse: Metadata per document | 20–50ms × N |
| 7 | Graph construction | 5–10ms |
| **Total** | | **~170–510ms** |

### Limits

| Limit | Value | Reason |
|-------|-------|--------|
| Max nodes total | 100 | Prevent exponential growth |
| Max per level (API) | 50 | Clamped from query param |
| Structural per type | 50 | OData `$top=50` |
| Vector search K | `limit × 2` | Over-fetch to account for dedup |
| Default threshold | 0.65 | Cosine similarity cutoff |

---

## 10. Troubleshooting

### Semantic Results Missing from Code Page

**Symptom**: Code Page dialog shows only structural relationships (Same matter, etc.) but no semantic matches. The PCF control on the form shows both structural and semantic for the same document.

**Root Cause**: The Code Page's filter panel sends `documentTypes` parameter with file extensions (`pdf`, `docx`, `xlsx`, etc.) but the AI Search index stores business type names (`Contract`, `Invoice`, `Agreement`). The BFF builds an OData filter `documentType eq 'pdf'` which matches nothing.

**Fix**: When all document type checkboxes are selected (the default), omit the `documentTypes` parameter entirely. The PCF doesn't send `documentTypes` at all, which is why it works.

**Verification**: Check the browser Network tab for the API call. If `documentTypes=pdf&documentTypes=docx&...` appears in the URL, that's the problem.

### PCF Shows "Select a document" Placeholder

**Symptom**: The PCF control shows the placeholder state even though a document record is open.

**Possible Causes**:
1. **`context.page.entityId` is undefined** — The control may be loading before the page context is available. Check console for `[DocumentRelationshipViewer] documentId=""`.
2. **Form not saved** — New (unsaved) records don't have an entity ID yet.
3. **Wrong context API** — If someone changed the code to use `context.parameters.documentId.raw` or `context.mode.contextInfo.entityId`, revert to `context.page.entityId`. Only `context.page.entityId` reliably returns the record GUID.

### Semantic Search Skipped (No Vector)

**Symptom**: API returns only structural relationships. Metadata shows `diagnosticMessage: "Source document not found in AI Search"` or console shows `[VIZ-DEBUG] Step 4: Source document has NO VECTOR`.

**Possible Causes**:
1. **Document not indexed** — The document exists in Dataverse but hasn't been processed by the AI pipeline yet. Check `sprk_analysisstatus` on the document record.
2. **Indexing failed** — The embedding step may have failed. Check App Service logs for embedding errors.
3. **Wrong tenant ID** — The `tenantId` filter doesn't match what's in the index. Verify with: `GET /api/ai/visualization/debug/{documentId}?tenantId={tid}`.

### Authentication Failures

**Symptom**: "Authentication Error" message bar or `401 Unauthorized` from API.

**Debugging Steps**:
1. **MSAL popup blocked** — Check if browser is blocking the auth popup. Whitelist the Dataverse URL.
2. **Token expired** — MSAL should auto-refresh, but if `acquireTokenSilent` fails and popup is blocked, the token won't refresh. Check console for MSAL errors.
3. **Wrong scope** — Verify the scope matches the BFF API app registration: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`.
4. **CORS** — The BFF must allow the Dataverse origin. Check `Access-Control-Allow-Origin` headers.

### Duplicate Nodes in Graph

**Symptom**: The same document appears multiple times in the graph visualization.

**Root Cause**: Case-sensitive GUID comparison in `GetUniqueId()`. Dataverse returns GUIDs in mixed case; AI Search may return them differently.

**Fix**: `GetUniqueId()` normalizes to lowercase: `return id.ToLowerInvariant()`. Ensure the BFF is deployed with this fix.

### Code Page Not Updating After Rebuild

**Symptom**: Code changes don't appear in the deployed Code Page dialog.

**Debugging Steps**:
1. **Browser cache** — Hard refresh (Ctrl+Shift+R) or clear cache.
2. **Web resource not published** — After PATCH, you must call `PublishXml`. Verify publish succeeded (HTTP 204).
3. **Wrong web resource** — Confirm the web resource name matches: `sprk_documentrelationshipviewer`.
4. **Build not run** — Verify `npm run build` succeeded, then `build-webresource.ps1` generated the HTML.

### Debug Endpoint

Use the debug endpoint to verify document state:

```
GET /api/ai/visualization/debug/{documentId}?tenantId={tid}
```

Returns raw Dataverse document fields, AI Search presence status, and vector availability without executing the full pipeline.

### Console Logging

Both the BFF and frontends emit structured logs:

| Log Prefix | Component |
|------------|-----------|
| `[VIZ-DEBUG]` | BFF VisualizationService (each pipeline step) |
| `[VISUALIZATION]` | BFF endpoint (request/response summary) |
| `[VisualizationApi]` | PCF useVisualizationApi hook |
| `[DocumentRelationshipViewer]` | PCF main component |
| `[App]` | Code Page main component |

---

## 11. Deployment

### PCF Control

```powershell
# 1. Build
cd src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer
node ../../node_modules/pcf-scripts/bin/pcf-scripts.js build --buildMode production

# 2. Copy artifacts to solution
cp out/controls/DocumentRelationshipViewer/bundle.js Solution/Controls/sprk_Spaarke.Controls.DocumentRelationshipViewer/
cp out/controls/DocumentRelationshipViewer/ControlManifest.xml Solution/Controls/sprk_Spaarke.Controls.DocumentRelationshipViewer/

# 3. Pack solution
cd Solution && powershell -File pack.ps1

# 4. Import
pac solution import --path "bin/SpaarkeDocumentRelationshipViewer_v1.0.31.zip" --publish-changes
```

### Code Page (HTML Web Resource)

```powershell
# 1. Build webpack bundle
cd src/client/code-pages/DocumentRelationshipViewer
npm run build

# 2. Generate self-contained HTML
powershell -File build-webresource.ps1

# 3. Deploy to Dataverse via Web API
# (PATCH content as base64, then PublishXml)
```

### BFF API

```bash
# Build and deploy
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release
az webapp deploy --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --src-path publish.zip
```

---

## 12. ADR References

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API + BackgroundService — visualization is a BFF endpoint |
| **ADR-006** | PCF for form controls, Code Pages for dialogs — both used here |
| **ADR-008** | Endpoint filters for auth — `VisualizationAuthorizationFilter` |
| **ADR-013** | AI Architecture — extends BFF, not separate service |
| **ADR-021** | Fluent UI v9 design system — both frontends |
| **ADR-022** | PCF platform libraries — React 16 for PCF, React 19 for Code Page |
