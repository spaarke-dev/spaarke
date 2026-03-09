# Spike Report: spaarke-records-index Field Coverage

> **Task**: R3-002 — Investigate spaarke-records-index Field Coverage
> **Date**: 2026-02-24
> **Author**: Claude Code
> **Status**: Complete

---

## Summary

The `spaarke-records-index` is an Azure AI Search index that stores Dataverse entity records (Matters, Projects, Invoices) for the Record Matching feature. This investigation examined the index schema, the pipeline that populates it, and how well it covers the fields needed for the Semantic Search UI R3 grid columns and `RecordSearchResponse` model.

**Key Finding**: The index contains **12 fields** that are shared across all entity types. It does **not** contain domain-specific fields (e.g., Matter Type, Practice Area, Invoice Amount, Vendor, Project Status). These fields are absent from both the index schema and the sync pipeline. Most planned grid columns for Matters, Projects, and Invoices will need a **post-search Dataverse lookup** to populate domain-specific fields, or the grid columns must be revised to match available data.

**Recommendation**: **GO** for Phase 2 Task 013 with the hybrid approach: search the index for relevance ranking, then enrich results with Dataverse metadata for domain-specific columns (same pattern used by `SemanticSearchService.EnrichResultsWithDataverseMetadataAsync`).

---

## 1. Index Schema (Definitive Source)

The canonical index schema is defined in:
`infrastructure/ai-search/spaarke-records-index.json`

### 1.1 Complete Field List

| # | Field Name | Type | Searchable | Filterable | Sortable | Facetable | Analyzer |
|---|-----------|------|------------|------------|----------|-----------|----------|
| 1 | `id` | Edm.String (KEY) | No | No | No | No | — |
| 2 | `recordType` | Edm.String | No | Yes | Yes | Yes | — |
| 3 | `recordName` | Edm.String | Yes | No | Yes | No | standard.lucene |
| 4 | `recordDescription` | Edm.String | Yes | No | No | No | standard.lucene |
| 5 | `organizations` | Collection(Edm.String) | Yes | Yes | No | Yes | standard.lucene |
| 6 | `people` | Collection(Edm.String) | Yes | Yes | No | Yes | standard.lucene |
| 7 | `referenceNumbers` | Collection(Edm.String) | Yes | Yes | No | No | keyword |
| 8 | `keywords` | Edm.String | Yes | No | No | No | standard.lucene |
| 9 | `contentVector` | Collection(Edm.Single) | Yes (vector) | No | No | No | 3072-dim HNSW cosine |
| 10 | `lastModified` | Edm.DateTimeOffset | No | Yes | Yes | No | — |
| 11 | `dataverseRecordId` | Edm.String | No | Yes | No | No | — |
| 12 | `dataverseEntityName` | Edm.String | No | Yes | No | Yes | — |

### 1.2 Semantic Configuration

| Setting | Value |
|---------|-------|
| Configuration name | `default-semantic-config` |
| Title field | `recordName` |
| Content fields | `recordDescription`, `keywords` |
| Keyword fields | `organizations`, `people`, `referenceNumbers` |

### 1.3 Vector Search Configuration

| Setting | Value |
|---------|-------|
| Algorithm | HNSW |
| Dimensions | 3072 |
| Metric | cosine |
| Profile | `default-vector-profile` |
| m | 4 |
| efConstruction | 400 |
| efSearch | 500 |

---

## 2. C# Model Mapping (SearchIndexDocument)

The C# model in `src/server/api/Sprk.Bff.Api/Services/RecordMatching/SearchIndexDocument.cs` maps 1:1 to the JSON index schema:

| C# Property | JSON Field | Index Field | Notes |
|-------------|-----------|-------------|-------|
| `Id` | `id` | `id` | Format: `{entityName}_{recordId}` |
| `RecordType` | `recordType` | `recordType` | Entity logical name: `sprk_matter`, `sprk_project`, `sprk_invoice` |
| `RecordName` | `recordName` | `recordName` | Display name of the record |
| `RecordDescription` | `recordDescription` | `recordDescription` | Description/notes |
| `Organizations` | `organizations` | `organizations` | Collection of org names |
| `People` | `people` | `people` | Collection of person names |
| `ReferenceNumbers` | `referenceNumbers` | `referenceNumbers` | Collection (matter #, invoice #, PO #) |
| `Keywords` | `keywords` | `keywords` | Searchable keywords text |
| `ContentVector` | `contentVector` | `contentVector` | 3072-dim vector |
| `LastModified` | `lastModified` | `lastModified` | DateTimeOffset |
| `DataverseRecordId` | `dataverseRecordId` | `dataverseRecordId` | Original GUID |
| `DataverseEntityName` | `dataverseEntityName` | `dataverseEntityName` | Same as RecordType |

---

## 3. Pipeline: How Records Are Populated

The sync pipeline is implemented in `DataverseIndexSyncService.cs`. It fetches records from Dataverse and transforms them to `SearchIndexDocument`.

### 3.1 Supported Entity Types

| Entity | EntitySetName | Select Fields | Reference Field |
|--------|--------------|---------------|-----------------|
| `sprk_matter` | `sprk_matters` | `sprk_matterid, sprk_mattername, sprk_description, sprk_matternumber, modifiedon, _sprk_client_value` | `sprk_matternumber` |
| `sprk_project` | `sprk_projects` | `sprk_projectid, sprk_projectname, sprk_description, sprk_projectnumber, modifiedon` | `sprk_projectnumber` |
| `sprk_invoice` | `sprk_invoices` | `sprk_invoiceid, sprk_invoicename, sprk_description, sprk_invoicenumber, modifiedon, _sprk_matter_value` | `sprk_invoicenumber` |

### 3.2 Field Population Analysis

The `TransformToDocument()` method in the sync service reveals exactly what gets populated:

| Index Field | Populated From | Notes |
|-------------|---------------|-------|
| `id` | `{entityName}_{recordId}` | Computed |
| `recordType` | Entity logical name (hardcoded) | e.g., `sprk_matter` |
| `recordName` | `sprk_mattername` / `sprk_projectname` / `sprk_invoicename` | Always populated |
| `recordDescription` | `sprk_description` | May be null |
| `organizations` | **NOT POPULATED** | Empty list (see TODO in code) |
| `people` | **NOT POPULATED** | Empty list (see TODO in code) |
| `referenceNumbers` | `sprk_matternumber` / `sprk_projectnumber` / `sprk_invoicenumber` | Single value in list |
| `keywords` | Concatenation of name + reference number | Auto-generated |
| `contentVector` | **NOT POPULATED** | No embedding generation in sync pipeline |
| `lastModified` | `modifiedon` | Always populated |
| `dataverseRecordId` | Record GUID | Always populated |
| `dataverseEntityName` | Entity logical name | Always populated |

### 3.3 Critical Gaps in Pipeline

1. **`organizations` is always empty** — Code has a TODO: "In future, expand lookups to get organization/people names"
2. **`people` is always empty** — Same TODO as organizations
3. **`contentVector` is not populated** — The sync pipeline does not generate embeddings, meaning **vector/hybrid search will not work** against this index as currently deployed. Only keyword search is functional.
4. **Only basic fields are fetched** — The Dataverse queries do not retrieve domain-specific fields (matter type, practice area, invoice amount, vendor, status, etc.)

---

## 4. Grid Column Coverage by Domain

### 4.1 Matters Domain

| Grid Column (spec FR-03) | Needed Field | Index Field | Status | Notes |
|--------------------------|-------------|-------------|--------|-------|
| checkbox | (UI only) | — | N/A | Selection UI, no data needed |
| Matter Name | Record display name | `recordName` | **AVAILABLE** | Populated from `sprk_mattername` |
| Similarity (%) | Search score | (computed) | **AVAILABLE** | From Azure AI Search score, normalized 0-1 |
| Matter Number | Reference number | `referenceNumbers[0]` | **AVAILABLE** | Populated from `sprk_matternumber` |
| Matter Type | Dataverse option set | **MISSING** | **MISSING** | Not in index. Requires Dataverse lookup for `sprk_mattertype` |
| Practice Area | Dataverse option set | **MISSING** | **MISSING** | Not in index. Requires Dataverse lookup for `sprk_practicearea` |
| Organizations | Org names array | `organizations` | **EMPTY** | Field exists in index but pipeline populates it as empty `[]` |
| Modified | Last modified date | `lastModified` | **AVAILABLE** | Populated from `modifiedon` |

**Summary**: 4 of 6 data columns available. 2 columns MISSING (Matter Type, Practice Area). 1 column technically AVAILABLE but EMPTY (Organizations).

### 4.2 Projects Domain

| Grid Column (spec FR-03) | Needed Field | Index Field | Status | Notes |
|--------------------------|-------------|-------------|--------|-------|
| checkbox | (UI only) | — | N/A | Selection UI |
| Project Name | Record display name | `recordName` | **AVAILABLE** | Populated from `sprk_projectname` |
| Similarity (%) | Search score | (computed) | **AVAILABLE** | From search score |
| Status | Project status | **MISSING** | **MISSING** | Not in index. Requires Dataverse lookup for `sprk_projectstatus` or `statuscode` |
| Parent Matter | Parent matter name | **MISSING** | **MISSING** | Not in index. Requires Dataverse lookup (expand `_sprk_matter_value`) |
| Modified | Last modified date | `lastModified` | **AVAILABLE** | Populated from `modifiedon` |

**Summary**: 3 of 4 data columns available. 2 columns MISSING (Status, Parent Matter).

### 4.3 Invoices Domain

| Grid Column (spec FR-03) | Needed Field | Index Field | Status | Notes |
|--------------------------|-------------|-------------|--------|-------|
| checkbox | (UI only) | — | N/A | Selection UI |
| Invoice (number + vendor) | Composite display | `recordName` + `referenceNumbers[0]` | **PARTIAL** | Name available; invoice number in referenceNumbers; vendor name MISSING |
| Similarity (%) | Search score | (computed) | **AVAILABLE** | From search score |
| Amount (currency) | Invoice amount | **MISSING** | **MISSING** | Not in index. Requires Dataverse lookup for `sprk_invoiceamount` |
| Vendor | Vendor name | **MISSING** | **MISSING** | Not in index. Requires Dataverse lookup for vendor lookup/name |
| Parent Matter | Parent matter name | **MISSING** | **MISSING** | Not in index. `_sprk_matter_value` is in Dataverse select but NOT mapped to index |
| Date | Invoice date | **MISSING** | **MISSING** | Not in index. Only `lastModified` (modifiedon) is available, not the business date |

**Summary**: 1 of 5 data columns available. 4 columns MISSING (Amount, Vendor, Parent Matter, Invoice Date). Invoice Number partially available via `referenceNumbers`.

---

## 5. RecordSearchResponse Field Coverage

Per spec.md, the `RecordSearchResponse` should return these fields per result:

| Response Field | Source | Status | Notes |
|---------------|--------|--------|-------|
| `recordId` | `dataverseRecordId` | **AVAILABLE** | GUID from index |
| `recordType` | `recordType` | **AVAILABLE** | Entity logical name (sprk_matter, etc.) |
| `recordName` | `recordName` | **AVAILABLE** | Display name |
| `recordDescription` | `recordDescription` | **AVAILABLE** | Description text (may be null) |
| `confidenceScore` | Azure Search score | **AVAILABLE** | Computed from search results (normalize to 0-1) |
| `matchReasons` | AI-generated explanation | **DERIVED** | Must be computed by the service (same pattern as `RecordMatchService.CalculateConfidenceScore`) |
| `organizations` | `organizations` | **SCHEMA AVAILABLE / DATA EMPTY** | Field exists in index but sync pipeline does not populate it |
| `people` | `people` | **SCHEMA AVAILABLE / DATA EMPTY** | Field exists in index but sync pipeline does not populate it |
| `keywords` | `keywords` | **AVAILABLE** | Auto-generated from name + reference number |
| `createdAt` | **MISSING** | **MISSING** | Index only has `lastModified`. Would need Dataverse lookup for `createdon` |
| `modifiedAt` | `lastModified` | **AVAILABLE** | From `modifiedon` |

**Summary**: 7 of 11 fields available from index. `matchReasons` must be computed. `organizations` and `people` exist in schema but are empty. `createdAt` is not in the index.

---

## 6. Additional Filterable Fields

The following fields support OData filtering in the index:

| Field | Filterable | Sortable | Facetable | Use Case |
|-------|-----------|----------|-----------|----------|
| `recordType` | Yes | Yes | Yes | Filter by entity type (sprk_matter, sprk_project, sprk_invoice) |
| `organizations` | Yes | No | Yes | Filter by organization name (if populated) |
| `people` | Yes | No | Yes | Filter by person name (if populated) |
| `referenceNumbers` | Yes | No | No | Filter by exact reference number |
| `lastModified` | Yes | Yes | No | Date range filtering |
| `dataverseRecordId` | Yes | No | No | Lookup specific record |
| `dataverseEntityName` | Yes | No | Yes | Same as recordType |

**Note**: `organizations` and `people` facets will return nothing meaningful until the sync pipeline is updated to populate these fields.

---

## 7. Existing RecordMatchService Usage

The existing `RecordMatchService` (used for document-to-record matching) already queries the `spaarke-records-index` using **keyword-only search** (no vector search):

- Uses `SearchQueryType.Full` (Lucene full-text)
- Selects specific fields: `id`, `recordType`, `recordName`, `recordDescription`, `organizations`, `people`, `referenceNumbers`, `keywords`, `dataverseRecordId`, `dataverseEntityName`
- Filters by `recordType` field
- Does NOT use vector search (no embedding generation)
- Returns `RecordMatchSuggestion` with `ConfidenceScore` and `MatchReasons` computed post-search

The new `RecordSearchService` for Task 013 should follow this pattern for keyword search but add vector search capability when the pipeline is updated with embeddings.

---

## 8. Gap Analysis

### 8.1 Critical Gaps

| Gap | Impact | Severity | Recommended Resolution |
|-----|--------|----------|----------------------|
| **No vector embeddings populated** | Hybrid/vector search will not work; keyword-only until pipeline updated | HIGH | For R3: use keyword-only search. Log issue for pipeline team to add embedding generation to sync service. |
| **Organizations always empty** | Organizations column shows nothing; facet filtering returns no results | MEDIUM | Post-search Dataverse lookup OR pipeline enhancement to expand `_sprk_client_value` lookups |
| **People always empty** | People column shows nothing; facet filtering returns no results | MEDIUM | Post-search Dataverse lookup OR pipeline enhancement |
| **No domain-specific fields** (Matter Type, Practice Area, Status, Amount, Vendor, etc.) | Most grid columns cannot be populated from index alone | HIGH | Post-search Dataverse batch lookup (recommended for R3) |
| **No createdAt field** | Cannot show creation date in results | LOW | Use `lastModified` as fallback, or add Dataverse lookup for `createdon` |

### 8.2 Pipeline Enhancement Opportunities (Future)

These improvements would make the index self-sufficient for grid display (no post-search Dataverse lookup needed):

1. **Add entity-specific fields to index schema**: `matterType`, `practiceArea`, `projectStatus`, `invoiceAmount`, `vendorName`, `invoiceDate`, `parentMatterName`
2. **Expand lookup values in sync pipeline**: Resolve `_sprk_client_value` to organization name, expand `_sprk_matter_value` to parent matter name
3. **Generate embeddings**: Add `IOpenAiClient` embedding generation to `DataverseIndexSyncService.TransformToDocument()`
4. **Add `createdAt` field**: Map `createdon` from Dataverse to index

---

## 9. Recommended Approach for RecordSearchService (Task 013)

### 9.1 Hybrid Approach (Recommended for R3)

1. **Search Phase**: Query `spaarke-records-index` using keyword search (BM25 with Lucene full-text). Vector search will degrade gracefully (no embeddings = no vector results, but keyword still works).

2. **Enrichment Phase**: For each search result, perform a Dataverse batch lookup to fetch domain-specific fields:
   - Matters: `sprk_mattertype`, `sprk_practicearea`, `_sprk_client_value` (expand to name)
   - Projects: `statuscode`, `_sprk_matter_value` (expand to name)
   - Invoices: `sprk_invoiceamount`, vendor lookup, `_sprk_matter_value` (expand to name), `sprk_invoicedate`

3. **Caching**: Cache Dataverse enrichment results in Redis with entity-specific TTL (entity metadata changes infrequently).

4. **Pattern Precedent**: This is the same pattern used by `SemanticSearchService.EnrichResultsWithDataverseMetadataAsync()` which enriches document search results with `createdBy`, `summary`, and `tldr` from Dataverse.

### 9.2 Enrichment Performance Estimate

- Typical search returns 10-50 results
- Batch Dataverse query for N records: ~100-300ms (single $filter + $select)
- Redis cache hit: <5ms
- Expected total enrichment overhead: 100-300ms first call, <5ms cached

### 9.3 RecordSearchService Architecture

```
Client Request
    |
    v
RecordSearchService.SearchAsync()
    |
    +-- 1. Generate embedding (IOpenAiClient) — graceful fallback if contentVector empty
    +-- 2. Build OData filter (recordType + organizations + people + referenceNumbers + lastModified)
    +-- 3. Execute search (SearchClient against spaarke-records-index)
    +-- 4. Map raw results to RecordSearchResult[] (available fields only)
    +-- 5. Compute matchReasons (from field overlap analysis)
    +-- 6. Enrich with Dataverse metadata (batch lookup, cached)
    +-- 7. Return RecordSearchResponse
```

---

## 10. Go/No-Go Recommendation

### **GO** — Proceed with Phase 2 Task 013

**Rationale**:
- The index provides sufficient fields for search relevance ranking (`recordName`, `recordDescription`, `keywords`, `referenceNumbers`)
- The `recordType` field enables filtering by entity type (matters, projects, invoices)
- Domain-specific grid columns can be populated via post-search Dataverse enrichment (proven pattern from SemanticSearchService)
- The RecordSearchResponse core fields (`recordId`, `recordType`, `recordName`, `recordDescription`, `confidenceScore`, `keywords`, `modifiedAt`) are all available from the index
- Vector search is non-functional today but the service can be designed to support it when the pipeline is updated

**Conditions**:
1. Task 013 must implement Dataverse enrichment for domain-specific grid columns
2. Task 013 should use keyword-only search as default mode (not hybrid) until embeddings are populated
3. Task 031 (domain-specific grid columns) should be adjusted to account for enrichment latency and mark domain-specific columns as "enriched" vs "indexed"
4. A separate backlog item should be created for pipeline enhancement (embedding generation + field expansion)

**Risk if not addressed later**:
- Without pipeline enhancement, every search incurs a Dataverse roundtrip for domain-specific fields
- Without embeddings, semantic similarity ranking is limited to BM25 keyword matching only

---

## Appendix A: File References

| File | Purpose |
|------|---------|
| `infrastructure/ai-search/spaarke-records-index.json` | Index schema definition (12 fields) |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/SearchIndexDocument.cs` | C# model mapping to index |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/RecordMatchService.cs` | Existing keyword search against this index |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs` | Pipeline that populates the index |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/IRecordMatchService.cs` | Interface + request/response models |
| `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` | Configuration (default index name) |
| `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs` | Knowledge index model (different index) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs` | Reference pattern for enrichment |
| `docs/guides/SPAARKE-AI-ARCHITECTURE.md` | AI architecture documentation |

## Appendix B: parentEntityType Field Values

The `spaarke-records-index` uses `recordType` (not `parentEntityType` which is on the knowledge-index). Valid values populated by the sync pipeline:

| Value | Entity | Dataverse Table |
|-------|--------|-----------------|
| `sprk_matter` | Matter | `sprk_matter` |
| `sprk_project` | Project | `sprk_project` |
| `sprk_invoice` | Invoice | `sprk_invoice` |

**Note**: The index does not currently contain `account` or `contact` records, only the three entity types above.

## Appendix C: Index vs Knowledge-Index Comparison

| Feature | `spaarke-records-index` | `knowledge-index` |
|---------|------------------------|-------------------|
| Purpose | Entity record search | Document chunk search |
| Granularity | One document per record | Multiple chunks per document |
| Vector field | `contentVector` (3072-dim) | `contentVector3072` (3072-dim) |
| Vector populated? | **No** (pipeline gap) | **Yes** |
| Tenant isolation | None (no tenantId field) | `tenantId` field filtered |
| Semantic config | `default-semantic-config` | `knowledge-semantic-config` |
| Entity scoping | `recordType` field | `parentEntityType` + `parentEntityId` |
| Enrichment needed | Yes (domain-specific fields) | Yes (createdBy, summary, tldr) |

**Important difference**: The records-index does NOT have a `tenantId` field for tenant isolation. This is a security consideration for Task 013 — the service must ensure appropriate Dataverse-level security is applied post-search, or the index needs a tenantId field added.
