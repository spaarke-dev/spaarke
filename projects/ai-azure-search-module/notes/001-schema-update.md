# Task 001: Azure AI Search Schema Update

> **Date**: 2026-01-09
> **Task**: 001-update-index-schema
> **Status**: Completed

---

## Summary

Added `documentVector` field to the `spaarke-knowledge-index` Azure AI Search index to enable document-level vector similarity search.

## Changes Made

### Index Schema Update

**File Modified**: `infrastructure/ai-search/spaarke-knowledge-index.json`

**New Field Added**:
```json
{
  "name": "documentVector",
  "type": "Collection(Edm.Single)",
  "searchable": true,
  "filterable": false,
  "sortable": false,
  "facetable": false,
  "dimensions": 1536,
  "vectorSearchProfile": "knowledge-vector-profile"
}
```

### Field Configuration

| Property | Value | Notes |
|----------|-------|-------|
| Field Name | `documentVector` | Document-level embedding (vs chunk-level `contentVector`) |
| Type | `Collection(Edm.Single)` | Float array for embedding storage |
| Dimensions | 1536 | Matches text-embedding-3-small output |
| Vector Profile | `knowledge-vector-profile` | HNSW algorithm with cosine similarity |
| Searchable | true | Enables vector similarity search |

### Vector Search Configuration

Uses existing profile `knowledge-vector-profile` with algorithm `hnsw-knowledge`:
- **Algorithm**: HNSW (Hierarchical Navigable Small World)
- **m**: 4 (graph connectivity)
- **efConstruction**: 400 (index build quality)
- **efSearch**: 500 (search quality/speed tradeoff)
- **metric**: cosine (similarity measure)

## Azure Resources

| Resource | Value |
|----------|-------|
| Search Service | `spaarke-search-dev` |
| Index Name | `spaarke-knowledge-index` |
| Endpoint | `https://spaarke-search-dev.search.windows.net/` |
| Resource Group | `spe-infrastructure-westus2` |

## Verification

Schema update verified via Azure AI Search REST API:
- Field `documentVector` present in index schema
- Dimensions: 1536
- Vector search profile: `knowledge-vector-profile`

## Purpose

The `documentVector` field stores document-level embeddings for the AI Search & Visualization Module:
- Enables finding semantically similar documents
- Supports the "Find Related" visualization feature
- Complements existing `contentVector` (chunk-level) for different use cases

## Related Tasks

- **Task 003**: Will implement VisualizationService using this field
- **Task 006**: Will backfill existing documents with documentVector embeddings

---

*Completed: 2026-01-09*
