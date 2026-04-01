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
│  │  (PCF)               │     │  Tab: "Find Similar"              │  │
│  │  [Find Similar] btn  │     │                                  │  │
│  └──────────────────────┘     └───────────────┬──────────────────┘  │
│           │ Opens dialog                       │                     │
└───────────┼────────────────────────────────────┼─────────────────────┘
            ▼                                    │
┌────────────────────────┐                       │
│  Code Page (HTML)      │                       │
│  sprk_documentrelation-│                       │
│  shipviewer            │                       │
└────────────┬───────────┘                       │
             │                                   │
             ▼                                   ▼
┌──────────────────────────────────────────────────────┐
│  BFF API                                              │
│  GET /api/ai/visualization/related/{documentId}       │
└──────────┬──────────────┬────────────────────────────┘
           │              │
 ┌─────────▼──────┐  ┌────▼──────────────────┐
 │  Dataverse     │  │  Azure AI Search       │
 │  Web API       │  │  spaarke-knowledge-    │
 │  Structural    │  │  index-v2              │
 │  relationships │  │  (3072-dim, cosine)    │
 └────────────────┘  └────────────────────────┘
```

---

## 2. BFF Visualization Pipeline (7 Steps)

The `VisualizationService.GetRelatedDocumentsAsync()` method executes a 7-step pipeline:

```
Step 1          Step 2           Step 3              Step 4
Dataverse       AI Search        Dataverse            AI Search
GetDocument  →  GetSourceDoc  →  GetStructural      →  VectorSearch
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

### Step 1: Fetch Source Document

Retrieve source document metadata from Dataverse including all relationship lookup fields (MatterId, ProjectId, InvoiceId, ParentDocumentId, EmailConversationIndex).

### Step 2: Get Source Vector from AI Search

Retrieve the source document's `documentVector3072` embedding from Azure AI Search. This vector is the query input for Step 4. If the document is not indexed, semantic search is **skipped** — only structural relationships are returned.

### Step 3: Query Structural Relationships

Discover documents linked through Dataverse lookup fields. Five relationship types, each with a guard condition:

| Type | Guard | Source |
|------|-------|--------|
| `same_email` | IsEmailArchive or ParentDocumentId set | Dataverse lookup |
| `same_thread` | EmailConversationIndex set | String prefix match (44 chars) |
| `same_matter` | MatterId set | Dataverse lookup |
| `same_project` | ProjectId set | Dataverse lookup |
| `same_invoice` | InvoiceId set | Dataverse lookup |

All structural relationships return similarity score `1.0`, capped at 50 documents per type.

### Step 4: Query Semantic Relationships

Execute a vector similarity (KNN) search using the source document's 3072-dimension embedding. Results below the `threshold` (default 0.65) are discarded. Results deduplicated by document ID (not chunk ID).

### Step 5: Merge and Deduplicate

Combines structural and semantic results with **structural taking priority**:

```
Priority Order (lower number wins):
  1. same_email      (highest)
  2. same_thread
  3. same_matter
  4. same_project
  5. same_invoice
  6. semantic         (lowest)
```

When a document appears in multiple relationship types, the highest priority determines its primary label. GUID comparison is case-insensitive (normalized to lowercase).

### Step 6: Get Document Metadata

For each related document, fetch record URL, file URL, extracted keywords, and timestamps from Dataverse.

### Step 7: Build Graph with Hub Topology

```
Source Document (depth 0)
├── Matter Hub ── Doc A, Doc B          (structural: hub topology)
├── Project Hub ── Doc C                (structural: hub topology)
├── Email Hub ── Attachment 1, 2        (structural: hub topology)
├── Doc D (similarity: 0.91)            (semantic: direct edge)
├── Doc E (similarity: 0.87)            (semantic: direct edge)
└── Doc F (similarity: 0.72)            (semantic: direct edge)
```

Structural relationships use hub-and-spoke: Source → Hub → Related Documents.
Semantic relationships use direct edges: Source → Related Document.

---

## 3. Relationship Types

| Type | Priority | Source | Edge Pattern | Score |
|------|----------|--------|-------------|-------|
| `same_email` | 1 (highest) | Dataverse lookup | Document → Email Hub | 1.0 |
| `same_thread` | 2 | String prefix | Document → Thread Hub | 1.0 |
| `same_matter` | 3 | Dataverse lookup | Document → Matter Hub | 1.0 |
| `same_project` | 4 | Dataverse lookup | Document → Project Hub | 1.0 |
| `same_invoice` | 5 | Dataverse lookup | Document → Invoice Hub | 1.0 |
| `semantic` | 6 (lowest) | AI Search vector | Source → Document (direct) | 0.0–1.0 |

---

## 4. Azure AI Search Index

### `spaarke-knowledge-index-v2`

**Vector configuration**: `text-embedding-3-large`, 3072 dimensions, HNSW algorithm, cosine similarity.

**Key design decision — document-level vs chunk-level vectors**: The visualization uses `documentVector3072` (one vector per document), not `contentVector3072` (per-chunk). This enables document-level similarity matching, while SemanticSearchControl uses chunk-level for RAG retrieval.

**Multi-tenant isolation**: OData filter on `tenantId` in all queries.

**`documentType` vs `fileType`**: `documentType` stores business classification names (Contract, Invoice, Agreement), not file extensions. Filter on business names, not extensions.

---

## 5. Two-Tier Frontend (ADR-006 Compliance)

| Aspect | PCF Control | Code Page |
|--------|------------|-----------|
| **Purpose** | Embedded in Dataverse form tab | Dialog opened from SemanticSearchControl |
| **React** | 16 (platform-provided) | 19 (bundled) |
| **Graph Library** | `react-flow-renderer` v10 | `@xyflow/react` v12 |
| **Layout Engine** | `d3-force` v3 | `d3-force` v3 |

Both frontends use `d3-force` simulation: link distance proportional to `1 - similarity` (more similar = shorter edge), node repulsion, center gravity, collision avoidance.

**Relationship label display**: PCF uses priority system (one label per node, highest wins). Code Page stores all relationships per node, preferring semantic as primary.

---

## 6. Performance

### Latency Target: P95 < 500ms for up to 50 nodes

| Step | Estimated Time |
|------|---------------|
| Dataverse: Get source document | 10–30ms |
| AI Search: Get source vector | 5–20ms |
| Dataverse: Structural relationships | 20–100ms |
| AI Search: Vector similarity search | 100–300ms |
| Merge, metadata, graph construction | 25–60ms |
| **Total** | **~170–510ms** |

### Limits

| Limit | Value |
|-------|-------|
| Max nodes total | 100 |
| Max per level (API) | 50 |
| Structural per type | 50 |
| Default similarity threshold | 0.65 |

---

## 7. ADR References

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API + BackgroundService — visualization is a BFF endpoint |
| **ADR-006** | PCF for form controls, Code Pages for dialogs — both used here |
| **ADR-008** | Endpoint filters for auth — `VisualizationAuthorizationFilter` |
| **ADR-013** | AI Architecture — extends BFF, not separate service |
| **ADR-021** | Fluent UI v9 design system — both frontends |
| **ADR-022** | PCF platform libraries — React 16 for PCF, React 19 for Code Page |
