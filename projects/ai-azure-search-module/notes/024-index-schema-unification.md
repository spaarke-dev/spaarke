# Index Schema Unification: RAG & Record Matching

> **Created**: 2026-01-10
> **Status**: Analysis Complete - Pending Implementation
> **Priority**: High (Blocking visualization feature)

---

## Executive Summary

During testing of the Document Relationship Visualization PCF control, we discovered a fundamental schema mismatch between the RAG index and the visualization requirements. This investigation revealed broader architectural gaps that need to be addressed to support both **File-to-File similarity** and **File-to-Record matching** use cases.

**Key Decisions Made**:
1. Standardize on **3072-dimension embeddings** across all indexes
2. Extend RAG index schema to support **any Dataverse record type** with associated Files
3. Support **orphan Files** (no Dataverse record) with `sourceRecordId = null`

---

## 1. Root Cause Investigation

### 1.1 How the Issue Was Discovered

The Document Relationship Visualization PCF control was deployed and tested. The following sequence of errors occurred:

| Step | Error | Root Cause | Fix Applied |
|------|-------|------------|-------------|
| 1 | 401 Unauthorized | PCF not passing access token to BFF API | Added MSAL authentication to PCF |
| 2 | 500 Internal Server Error | `KnowledgeDeploymentService` using hardcoded index name constant | Changed to use `_options.SharedIndexName` |
| 3 | 500 Internal Server Error | `Analysis__SharedIndexName` set to `spaarke-records-index` in Azure | Investigated schema mismatch |
| 4 | **Schema Mismatch** | Records index has different schema than RAG index | **This document** |

### 1.2 The Schema Mismatch

When the VisualizationService attempted to query `spaarke-records-index`, it failed because:

**VisualizationService expects (RAG-style schema)**:
```
documentId, documentName, documentVector (1536 dims), tenantId
```

**spaarke-records-index has (Record Matching schema)**:
```
dataverseRecordId, recordName, contentVector (3072 dims), dataverseEntityName
```

### 1.3 Fundamental Discovery

This investigation revealed two **distinct but related systems** that were conflated:

| System | Index | Purpose | Vector Source |
|--------|-------|---------|---------------|
| **Tool 1: File-to-File** | RAG Index | Find similar Files, visualize relationships | File content embeddings |
| **Tool 2: File-to-Record** | Records Index | Match Files to Dataverse records (Matters, Projects) | Record metadata embeddings |

---

## 2. Terminology Clarification

| Term | Definition | Storage Location |
|------|------------|------------------|
| **File** | Actual file content (PDF, DOCX, email, etc.) | SharePoint Embedded (SPE) |
| **Document** | `sprk_document` Dataverse record (metadata about a File) | Dataverse |
| **Record** | Any Dataverse entity (Matter, Project, Account, Document, etc.) | Dataverse |
| **Orphan File** | File in SPE with no associated Dataverse record | SPE only |

---

## 3. Current State Analysis

### 3.1 RAG Index (`spaarke-knowledge-index`)

**Purpose**: Store File content for RAG search and File-to-File similarity

**Current Schema**:
| Field | Type | Purpose | Issue |
|-------|------|---------|-------|
| `id` | String | Chunk ID | OK |
| `documentId` | String | Link to `sprk_document` | **Limited to one entity type** |
| `documentName` | String | Display name | OK |
| `contentVector` | 1536 dims | Chunk embedding | **Dimension mismatch with Records index** |
| `documentVector` | 1536 dims | Doc-level embedding | **Dimension mismatch** |
| `tenantId` | String | Tenant isolation | OK |

**Gaps**:
1. `documentId` only supports `sprk_document` - cannot reference other entity types
2. No field to identify entity type
3. Cannot represent orphan Files (no Dataverse record)
4. 1536 dimensions vs 3072 in Records index

### 3.2 Records Index (`spaarke-records-index`)

**Purpose**: Store Dataverse record metadata for File-to-Record matching

**Current Schema**:
| Field | Type | Purpose |
|-------|------|---------|
| `id` | String | Record ID |
| `recordType` | String | Entity logical name |
| `recordName` | String | Display name |
| `recordDescription` | String | Description field |
| `organizations` | Collection | Extracted orgs |
| `people` | Collection | Extracted people |
| `referenceNumbers` | Collection | Case numbers, etc. |
| `contentVector` | 3072 dims | Metadata embedding |
| `dataverseRecordId` | String | Dataverse GUID |
| `dataverseEntityName` | String | Entity type |

**Status**: Schema is appropriate for Record Matching use case.

### 3.3 Vector Dimension Analysis

| Embedding Model | Dimensions | Quality | Cost | Use Case |
|-----------------|------------|---------|------|----------|
| text-embedding-3-small | 1536 | Good | Low | Current RAG |
| text-embedding-3-large | 3072 | Better | Medium | Current Records |
| text-embedding-3-large (max) | 3072 | Best available | Medium | **Recommended standard** |

**Note**: 3072 is the maximum dimension for OpenAI's current embedding models. There is no higher standard available. The text-embedding-3-large model at 3072 dimensions provides the best quality-to-cost ratio for enterprise use.

---

## 4. Target State Design

### 4.1 Unified Embedding Standard

**Decision**: Standardize on **3072-dimension embeddings** using `text-embedding-3-large`

**Rationale**:
- Highest quality available from OpenAI
- Enables direct vector comparison between RAG and Records indexes
- Future-proof (already at max dimensions)
- Cost increase is acceptable for enterprise quality

**Impact**:
- All existing RAG embeddings must be regenerated
- `DocumentIntelligenceOptions.EmbeddingModel` default changes to `text-embedding-3-large`
- Embedding cache keys change (model is part of hash)

### 4.2 Extended RAG Index Schema (Simplified)

**Architecture Clarification** (per HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md):

There is **no direct relationship** between Dataverse records (Project, Invoice, etc.) and SPE Files. The relationship is always:

```
Dataverse Record (Matter, Project, Invoice)
        │
        │ 1:N relationship
        ▼
    sprk_document (Document record)
        │
        │ ContainerId + file metadata
        ▼
    SharePoint Embedded (File)
```

This means:
- `documentId` always references `sprk_document` (or null for orphan Files)
- Parent entity (Matter, Project) is derived from `sprk_document` lookup fields at runtime
- No need for `sourceEntityName` field in the index

**New Schema** (`spaarke-knowledge-index` v2):

| Field | Type | Change | Purpose |
|-------|------|--------|---------|
| `id` | String | - | Chunk ID |
| `documentId` | String | **NULLABLE** | Link to `sprk_document` (null for orphan Files) |
| `speFileId` | String | **NEW** | SharePoint Embedded file ID (always populated) |
| `fileName` | String | **RENAMED** from `documentName` | File display name |
| `fileType` | String | **NEW** | Extension: pdf, docx, msg, etc. |
| `contentVector` | 3072 dims | **CHANGED** | Chunk embedding (was 1536) |
| `documentVector` | 3072 dims | **CHANGED** | File-level embedding (was 1536) |
| `tenantId` | String | - | Tenant isolation |
| `chunkIndex` | Int32 | - | Position in file |
| `chunkCount` | Int32 | - | Total chunks |
| `content` | String | - | Chunk text |
| `metadata` | String | - | JSON blob |
| `createdAt` | DateTimeOffset | - | Timestamp |
| `updatedAt` | DateTimeOffset | - | Timestamp |

**Key Changes**:
1. `documentId` made nullable (for orphan Files)
2. Added `speFileId` for direct SPE reference (always populated)
3. Added `fileType` for visualization icon selection
4. Changed vector dimensions from 1536 → 3072
5. **NOT adding** `sourceEntityName` - parent entity derived at runtime from `sprk_document` lookups

**Future Extension**: If direct Record-to-File relationships are added later, can add `sourceEntityName` field without breaking existing schema.

### 4.3 Orphan File Support

Files uploaded to SPE with no associated `sprk_document` record:

```json
{
  "id": "chunk-abc-001",
  "documentId": null,
  "speFileId": "spe-file-guid-12345",
  "fileName": "uploaded-document.pdf",
  "fileType": "pdf",
  "contentVector": [/* 3072 floats */],
  "documentVector": [/* 3072 floats */],
  "tenantId": "tenant-guid"
}
```

**Visualization Behavior**:
- Orphan Files displayed with generic "File" icon
- No "Open in Dataverse" action available
- "Open in SharePoint" action uses `speFileId`

### 4.4 Visualization Display Logic

| documentId | fileType | Display As | Icon | Actions |
|------------|----------|------------|------|---------|
| `{guid}` | pdf | "PDF Document" | PDF icon | Open in Dataverse, Open in SPE |
| `{guid}` | docx | "Word Document" | Word icon | Open in Dataverse, Open in SPE |
| `{guid}` | msg | "Email" | Mail icon | Open in Dataverse, Open in SPE |
| `{guid}` | xlsx | "Excel Spreadsheet" | Excel icon | Open in Dataverse, Open in SPE |
| `null` | any | "File" | Generic file icon | Open in SPE only |

**Parent Entity Resolution** (runtime, not stored):
```
User clicks Document node in visualization
        │
        ▼
Query Dataverse: GET /sprk_documents({documentId})?$select=sprk_matter,sprk_project
        │
        ▼
Display: "Document belongs to Matter: ABC-2024-001"
```

This keeps the index schema simple while still supporting rich visualization.

---

## 5. Use Case Flows (Target State)

### 5.1 Tool 1: File-to-File Similarity (Visualization)

```
User opens sprk_document form
           │
           ▼
PCF loads with documentId
           │
           ▼
GET /api/ai/visualization/related/{documentId}
           │
           ▼
VisualizationService:
  1. Query RAG index: sourceRecordId == documentId
  2. Get source File's documentVector (3072 dims)
  3. Vector search: find similar Files
  4. Return: similar Files with their sourceRecordId, sourceEntityName
           │
           ▼
PCF renders graph:
  - Source node: current Document
  - Related nodes: Files (with associated Records or orphan)
  - Edges: similarity scores
```

### 5.2 Tool 2: File-to-Record Matching

```
User uploads email File to SPE
           │
           ▼
POST /api/ai/rag/index (chunks with 3072-dim vectors)
           │
           ▼
System computes documentVector (average of chunks)
           │
           ▼
POST /api/ai/matching/suggest
           │
           ▼
RecordMatchingService:
  1. Get File's documentVector (3072 dims)
  2. Query Records index: vector search (3072 dims) ← COMPATIBLE!
  3. Return: suggested Records (Matters, Projects, Accounts)
           │
           ▼
UI shows: "This email may belong to Matter XYZ (85% match)"
```

---

## 6. Migration Plan

### Phase 1: Schema Updates (Non-Breaking)

**Add new fields** to existing RAG index without removing old ones:

1. Add `sourceRecordId` (copy from `documentId`)
2. Add `sourceEntityName` (default: `sprk_document`)
3. Add `fileType` (extract from fileName)
4. Add `speFileId` (populate from ingestion metadata)

**Estimated Effort**: 1-2 days

### Phase 2: Embedding Model Migration

**Re-embed all content** with text-embedding-3-large (3072 dims):

1. Update `DocumentIntelligenceOptions.EmbeddingModel` default
2. Add `contentVector3072` and `documentVector3072` fields
3. Run background job to re-embed all existing chunks
4. Validate quality of new embeddings

**Estimated Effort**: 2-3 days (includes background job runtime)

### Phase 3: Service Updates

**Update services** to use new schema:

1. `RagService`: Use new field names, 3072-dim vectors
2. `VisualizationService`: Query by `sourceRecordId`, handle `sourceEntityName`
3. `KnowledgeDocument` model: Add new properties
4. PCF: Handle multiple entity types, orphan Files

**Estimated Effort**: 2-3 days

### Phase 4: Cutover

**Switch to new schema**:

1. Update index alias (if using)
2. Remove deprecated fields (`documentId`, `documentName`, 1536-dim vectors)
3. Update configuration: `Analysis__SharedIndexName` → correct index
4. Full regression testing

**Estimated Effort**: 1 day

### Phase 5: Records Index Alignment (Optional)

If needed, ensure Records index is fully compatible:

1. Verify 3072-dim vectors are correctly populated
2. Add any missing fields for parity
3. Test File-to-Record matching end-to-end

**Estimated Effort**: 1-2 days

---

## 7. Task Breakdown

### Immediate Fix (Unblock Visualization)

| Task | Description | Priority |
|------|-------------|----------|
| **T-001** | Fix `Analysis__SharedIndexName` in Azure to point to correct RAG index | Critical |
| **T-002** | Verify RAG index exists with current schema | Critical |
| **T-003** | Test visualization with existing 1536-dim vectors | High |

### Phase 1 Tasks: Schema Extension

| Task | Description | Estimate |
|------|-------------|----------|
| **T-010** | Create RAG index v2 schema JSON | 2 hours |
| **T-011** | Add migration fields to existing index | 4 hours |
| **T-012** | Update `KnowledgeDocument` model with new fields | 2 hours |
| **T-013** | Update `RagService.IndexDocumentAsync` for new fields | 4 hours |
| **T-014** | Backfill `sourceEntityName` for existing documents | 2 hours |

### Phase 2 Tasks: Embedding Migration

| Task | Description | Estimate |
|------|-------------|----------|
| **T-020** | Update `DocumentIntelligenceOptions` for 3072-dim model | 1 hour |
| **T-021** | Add 3072-dim vector fields to index | 2 hours |
| **T-022** | Create `EmbeddingMigrationService` background job | 4 hours |
| **T-023** | Run embedding migration (batch processing) | 8 hours (runtime) |
| **T-024** | Validate embedding quality | 2 hours |

### Phase 3 Tasks: Service Updates

| Task | Description | Estimate |
|------|-------------|----------|
| **T-030** | Update `VisualizationService` for new schema | 4 hours |
| **T-031** | Update `IVisualizationService` interface | 1 hour |
| **T-032** | Update visualization API DTOs | 2 hours |
| **T-033** | Update PCF types for multi-entity support | 2 hours |
| **T-034** | Update PCF `DocumentNode` for entity-type icons | 3 hours |
| **T-035** | Add orphan File handling to PCF | 2 hours |
| **T-036** | Unit tests for new scenarios | 4 hours |

### Phase 4 Tasks: Cutover

| Task | Description | Estimate |
|------|-------------|----------|
| **T-040** | Remove deprecated fields from index | 2 hours |
| **T-041** | Update Azure configuration | 1 hour |
| **T-042** | End-to-end regression testing | 4 hours |
| **T-043** | Update documentation | 2 hours |

---

## 8. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Embedding migration takes too long | Delayed launch | Batch processing with progress tracking; run during off-hours |
| 3072-dim vectors increase storage costs | Higher Azure costs | Monitor usage; acceptable for quality improvement |
| Schema change breaks existing integrations | Runtime errors | Parallel fields during migration; gradual cutover |
| Orphan Files create confusion in UI | User confusion | Clear "File" type indicator; no Dataverse actions |

---

## 9. Configuration Changes Required

### Azure App Service Settings

| Setting | Current | Target |
|---------|---------|--------|
| `Analysis__SharedIndexName` | `spaarke-records-index` (WRONG) | `spaarke-knowledge-index` |
| `DocumentIntelligence__EmbeddingModel` | `text-embedding-3-small` | `text-embedding-3-large` |

### Azure AI Search Index

| Index | Action |
|-------|--------|
| `spaarke-knowledge-index` | Extend schema with new fields |
| `spaarke-records-index` | No changes (already 3072-dim) |

---

## 10. Success Criteria

1. **Visualization works**: PCF shows related Files/Documents correctly
2. **Multi-entity support**: Can visualize Files linked to any Dataverse record type
3. **Orphan Files**: Files without Dataverse records are visualized as "File" type
4. **Unified vectors**: Both RAG and Records indexes use 3072-dim embeddings
5. **File-to-Record matching**: Can compare File vectors to Record vectors directly

---

## 11. Next Steps

1. [ ] Review and approve this analysis
2. [ ] Create POML task files for each phase
3. [ ] Execute T-001, T-002, T-003 immediately to unblock visualization
4. [ ] Begin Phase 1 schema extension
5. [ ] Schedule embedding migration (Phase 2) during low-usage period

---

## Appendix A: Index Schema Comparison

### Current RAG Index
```json
{
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true },
    { "name": "documentName", "type": "Edm.String", "searchable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536 },
    { "name": "documentVector", "type": "Collection(Edm.Single)", "dimensions": 1536 }
  ]
}
```

### Target RAG Index (v2 - Simplified)
```json
{
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true },
    { "name": "speFileId", "type": "Edm.String", "filterable": true },
    { "name": "fileName", "type": "Edm.String", "searchable": true },
    { "name": "fileType", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 3072 },
    { "name": "documentVector", "type": "Collection(Edm.Single)", "dimensions": 3072 },
    { "name": "tenantId", "type": "Edm.String", "filterable": true },
    { "name": "chunkIndex", "type": "Edm.Int32", "filterable": true },
    { "name": "chunkCount", "type": "Edm.Int32" },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "metadata", "type": "Edm.String" },
    { "name": "createdAt", "type": "Edm.DateTimeOffset", "filterable": true },
    { "name": "updatedAt", "type": "Edm.DateTimeOffset", "filterable": true }
  ],
  "vectorSearch": {
    "algorithms": [{ "name": "hnsw-algorithm", "kind": "hnsw" }],
    "profiles": [{ "name": "knowledge-vector-profile", "algorithm": "hnsw-algorithm" }]
  }
}
```

**Note**: `documentId` is nullable to support orphan Files. All Files have `speFileId` populated.

---

*Document created: 2026-01-10*
*Author: Claude Code (AI-assisted analysis)*
