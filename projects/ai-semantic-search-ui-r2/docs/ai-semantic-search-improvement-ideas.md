# AI Semantic Search — Improvement Ideas

> **Created**: February 24, 2026
> **Control Version**: v1.0.39
> **Status**: Living document — updated as improvements are identified and implemented

---

## Implemented in v1.0.39

### 1. Minimum Score Threshold Slider (UI Filter)

**Problem**: All results are returned regardless of relevance. Low-scoring documents (20-30%) clutter results and reduce user confidence.

**Solution**: Horizontal slider (0-100%) in the filter panel. Only results with a similarity score at or above the threshold are displayed. Client-side filtering initially; can be moved server-side for performance.

**Default**: 0% (show all results — backward compatible)

**Impact**: High — immediately improves perceived result quality by hiding noise.

---

### 2. Search Mode Selector (Hybrid / Concept / Keyword)

**Problem**: The default Hybrid RRF mode is great for general queries but users have different needs — sometimes they want exact keyword matches (e.g., a clause number), other times concept matching (e.g., "obligations related to data privacy").

**Solution**: Dropdown in the filter panel with three modes:

| Mode | API Parameter | Behavior |
|------|--------------|----------|
| **Hybrid** (default) | `hybridMode: "rrf"` | Vector + Keyword fused via Reciprocal Rank Fusion |
| **Concept Only** | `hybridMode: "vectorOnly"` | Pure semantic/vector search — matches on meaning, not words |
| **Keyword Only** | `hybridMode: "keywordOnly"` | Pure BM25 text matching — exact word/phrase search |

**Impact**: Medium — gives power users control over search strategy.

---

### 3. Search Info Button (i)

**Problem**: Users don't understand why highlighted text doesn't match their search term, or what the similarity percentage means.

**Solution**: Small (i) info button in the filter panel that opens a popover explaining:
- How semantic search works (meaning-based, not just keyword)
- Why highlights may not contain the search term (semantic captions show relevant passages)
- What the similarity score means (0-100% scale from semantic reranker)
- How to use threshold and mode controls

**Impact**: Low-medium — reduces user confusion and support questions.

---

## Future Improvements (Not Yet Implemented)

### 4. Server-Side Minimum Score Threshold

**Current**: Client-side filtering (results fetched then filtered in UI).

**Improvement**: Pass `minScore` parameter to BFF API so Azure AI Search filters before returning. Reduces data transfer and improves pagination accuracy.

**Effort**: Small — add `MinScore` parameter to `SemanticSearchService.SearchAsync()`, apply as post-filter on `normalizedScore`.

**Priority**: Medium — implement when result volume becomes large enough to matter.

---

### 5. Searchable Metadata Fields

**Current**: Keyword (BM25) search only covers `content`, `fileName`, `knowledgeSourceName`.

**Improvement**: Add `documentType`, `fileType`, and custom metadata fields to the searchable field list in the AI Search index.

**Effort**: Medium — requires index schema update and re-indexing.

**Priority**: Medium — would improve keyword matching for document-type-specific queries.

---

### 6. Query Expansion / Synonym Maps

**Current**: Keyword search uses exact BM25 matching.

**Improvement**: Configure Azure AI Search synonym maps for common legal/business terms (e.g., "NDA" = "Non-Disclosure Agreement", "MSA" = "Master Service Agreement").

**Effort**: Small — Azure AI Search supports synonym maps natively.

**Priority**: Medium — significant improvement for domain-specific queries with low effort.

---

### 7. Highlight Source Differentiation

**Current**: Highlights come from semantic captions (extractive). Users see seemingly random passages highlighted because the semantic reranker identifies meaning-relevant text, not keyword matches.

**Improvement**: Show two types of highlights:
1. **Semantic caption** — labeled "Relevant passage" (from reranker)
2. **Keyword match** — labeled "Keyword match" (from BM25 highlights on `content` field)

**Effort**: Medium — BFF already receives both; needs UI differentiation.

**Priority**: Low — the (i) info button explains the current behavior.

---

### 8. Adjustable Result Page Size

**Current**: Fixed at 5 results per page.

**Improvement**: Let users choose 5 / 10 / 25 results per load. Larger pages reduce round-trips for users who want to scan many results.

**Effort**: Small — change `DEFAULT_OPTIONS.limit` dynamically.

**Priority**: Low.

---

### 9. Saved Search Configurations

**Current**: Filters and mode reset on every session.

**Improvement**: Save user's preferred threshold, mode, and filter settings to Dataverse user settings or browser localStorage.

**Effort**: Medium.

**Priority**: Low — nice-to-have for frequent users.

---

### 10. Re-Index Stale Documents

**Current**: BFF logs show 22+ document IDs returning "Entity Does Not Exist" errors from Dataverse enrichment. The AI Search index contains references to documents that no longer exist in Dataverse.

**Improvement**:
1. Build an index hygiene job that removes orphaned entries
2. Add "last verified" timestamp to index entries
3. Automatic re-index on document create/update/delete events

**Effort**: Medium-Large.

**Priority**: High — directly impacts search accuracy and enrichment reliability.

---

### 11. Faceted Search Counts

**Current**: Filter dropdowns show all possible values but no indication of how many results match each option.

**Improvement**: Use Azure AI Search facets to show result counts next to each filter option (e.g., "Contract (15)", "Amendment (3)").

**Effort**: Medium — requires facet queries in BFF + UI rendering.

**Priority**: Medium — significantly improves filter usability.

---

### 12. Search-as-You-Type / Suggestions

**Current**: User must press Enter or click Search to execute.

**Improvement**: Show typeahead suggestions as the user types, using Azure AI Search's suggest API or autocomplete feature.

**Effort**: Medium — needs new API endpoint + debounced UI.

**Priority**: Low — current explicit search is fine for document search use case.

---

## Technical Reference

### Azure AI Search Configuration

| Setting | Current Value |
|---------|--------------|
| Search index | `spaarke-knowledge-index` |
| Vector field | `contentVector3072` (3072 dimensions) |
| Embedding model | `text-embedding-3-large` |
| Keyword fields | `content`, `fileName`, `knowledgeSourceName` |
| Filterable fields | `createdAt`, `updatedAt`, `documentType`, `fileType`, `tenantId`, `parentEntityType`, `parentEntityId` |
| Semantic config | Semantic reranker with extractive captions |
| Score range | Reranker: 0-4, normalized to 0-1 (displayed as %) |
| Default mode | Hybrid RRF (vector + keyword fusion) |
| Minimum threshold | None (all results returned) |
| Page size | 5 results per request |

### Score Interpretation Guide

| Score Range | Interpretation |
|-------------|---------------|
| 80-100% | Strong semantic match — highly relevant |
| 60-80% | Good match — likely relevant |
| 40-60% | Moderate match — may be relevant |
| 20-40% | Weak match — tangentially related |
| 0-20% | Very weak — likely noise |

### Highlight Behavior

Highlights (yellow text in results) come from Azure AI Search's **semantic captions (extractive)**, not keyword matching. The semantic reranker identifies the most semantically relevant passage in each document, which may not contain the exact search term. For example, searching "manage" may highlight a passage about "oversight responsibilities" because it is semantically related to the concept of management.
