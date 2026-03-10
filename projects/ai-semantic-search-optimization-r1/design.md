# Semantic Search Optimization — Design Document

> **Author**: Ralph Schroeder
> **Date**: March 2026
> **Status**: Draft

---

## Problem Statement

Spaarke has two independent search planes that index overlapping data:

1. **Dataverse Search** (Microsoft-managed Azure AI Search) — powers the model-driven app global search bar and Copilot chat. Indexes Dataverse table rows. No vector search, no scoring profile control, no custom analyzers.

2. **Custom Azure AI Search** (self-managed, `spaarke-search-dev`) — powers the Semantic Search PCF, Record Matching, and RAG pipeline. Full control over index schema, vector embeddings, scoring profiles, BM25 parameters, and semantic configuration.

**Current problems:**

- Users must search in two places to get complete results, creating confusion about which search to trust.
- Only 3 of 10 core entities are indexed in the custom records index (matter, project, invoice) — the other 7 are invisible to custom search.
- The Dataverse search index has no AI enrichment — records with empty or sparse name/description fields return poor results.
- The `spaarke-records-index` has no embeddings populated (`contentVector` always empty), disabling vector similarity search for records.
- Document enrichment (summaries, keywords, entities) exists but never flows back to the search index as searchable metadata.
- The existing enrichment fields (`sprk_filesummary`, `sprk_filekeywords`) are optimized for human reading, not for BM25/semantic ranking.
- The `organizations` and `people` fields in the records index are always empty arrays.

## Solution Overview

Build a **dual-output AI enrichment pipeline** that generates a search-optimized profile for every core entity record, then writes it to both search surfaces simultaneously:

```
Record Created/Updated
    ↓
AI Enrichment Pipeline (BFF API)
    ├── Input: Record fields + related records + linked documents
    ├── AI: Azure OpenAI generates dense search profile (~150 words)
    │       + keywords + entity extraction + embedding
    │
    ├──→ Output 1: Dataverse
    │    └── sprk_searchprofile field (feeds native search + Copilot)
    │
    └──→ Output 2: Azure AI Search (spaarke-records-index)
         └── Enriched fields + contentVector (feeds semantic search + record matching)
```

## Scope

### In Scope

**10 core search entities** (all have `sprk_searchprofile` field added):

| Entity | Currently Indexed? | Has AI Enrichment? |
|--------|-------------------|-------------------|
| `sprk_matter` | Records index only | No |
| `sprk_project` | Records index only | No |
| `sprk_invoice` | Records index only | No |
| `sprk_document` | Knowledge index (chunks) | Yes (summary, keywords, entities) |
| `sprk_communication` | Neither | No |
| `sprk_event` | Neither | No |
| `sprk_budget` | Neither | No |
| `sprk_memo` | Neither | No |
| `sprk_analysis` | Neither | No |
| `sprk_workassignment` | Neither | No |

**Deliverables:**

1. **Search Profile Generation Service** — AI service that generates BM25/semantic-optimized profiles for any entity type.

2. **Dual-output write** — single pipeline writes `sprk_searchprofile` to Dataverse AND enriched fields + embeddings to `spaarke-records-index`.

3. **Index schema expansion** — add the 7 missing entity types to `spaarke-records-index`, add `searchProfile` field, add scoring profile with field boosting.

4. **Sync script enhancement** — extend `Sync-RecordsToIndex.ps1` to support all 10 entity types with enrichment + embeddings.

5. **BFF API admin endpoints** — bulk enrichment endpoint for initial population and on-demand refresh.

6. **Background job handler** — queue-based enrichment triggered by record create/update events.

7. **Quick Find View configuration** — add `sprk_searchprofile` as Find Column on all 10 entities' Quick Find Views.

8. **Semantic Search fan-out** — extend `SemanticSearchService` to optionally search `spaarke-records-index` alongside the knowledge index, returning both document content matches and record matches.

### Out of Scope

- Replacing Dataverse search with custom search (not possible — can't override global search bar)
- Dataverse plugin triggers (too complex for v1 — use admin endpoints + background jobs instead)
- Power Automate flow triggers (same — defer to v2)
- Changes to the document knowledge index (`spaarke-knowledge-index-v2`) — document chunking pipeline is separate
- Graph visualization enhancements (existing feature, not part of this optimization)
- Find Similar feature changes (existing feature, not part of this optimization)
- LlamaParse enablement (separate concern)

## Technical Approach

### 1. Search Profile Generation

The `sprk_searchprofile` field is a purpose-built search surface optimized for BM25 + semantic ranking. It differs from existing enrichment fields:

| Field | Purpose | Optimized For |
|-------|---------|--------------|
| `sprk_filesummary` | Human-readable summary | Reading comprehension (long, narrative) |
| `sprk_filetldr` | Quick glance | Brevity (too short for search) |
| `sprk_filekeywords` | Tag list | Filtering (no prose, no context) |
| **`sprk_searchprofile`** | **Search ranking** | **BM25 field-length normalization + semantic comprehension** |

**Generation prompt design principles (grounded in BM25 formula):**

- **150-200 words max** — shorter fields get higher per-term BM25 scores due to `fieldLen/avgFieldLen` normalization
- **Dense prose, no filler** — every word must carry information (no "Furthermore...", "In addition...")
- **All entity names included** — parties, attorneys, clients, opposing counsel, judges
- **Reference numbers included** — exact-match search targets (high IDF score because they're unique)
- **Domain terminology and synonyms** — rare terms score disproportionately high via IDF
- **Related record context** — cross-entity discoverability ("Related to Project PRJ-2026-0142")
- **No term repetition beyond 3x** — k1 saturation means additional occurrences barely help
- **Record type stated in natural language** — "This is a patent infringement litigation matter" aids semantic ranking

**Per-entity prompt templates:**

Each entity type needs a tailored prompt because the relevant context differs:

| Entity | Key Context for Profile |
|--------|----------------------|
| Matter | Type, parties, practice area, attorney, client, status, related docs summary |
| Project | Name, type, client, team, deliverables, status, related matters |
| Invoice | Vendor, amount, line items summary, parent matter/project, payment status |
| Document | Type, summary (from existing `sprk_filesummary`), parent entity, key entities |
| Communication | Subject, from/to parties, regarding record, key topics, direction |
| Event | Name, type, due date context, priority, regarding record, description |
| Budget | Name, amount, utilization, parent matter/project, compliance status |
| Memo | Title, body summary, regarding event/record, author |
| Analysis | Document analyzed, playbook used, key findings summary |
| Work Assignment | Description, assignee, due date, parent matter/project, status |

### 2. Index Schema Changes

**Expand `spaarke-records-index` to support all 10 entities:**

Add entity configurations to `DataverseIndexSyncService.cs` and `Sync-RecordsToIndex.ps1` for: `sprk_communication`, `sprk_event`, `sprk_budget`, `sprk_memo`, `sprk_analysis`, `sprk_workassignment`, `sprk_document`.

**Add new fields to index schema:**

```json
{
  "name": "searchProfile",
  "type": "Edm.String",
  "searchable": true,
  "analyzer": "standard.lucene"
},
{
  "name": "parentEntityType",
  "type": "Edm.String",
  "filterable": true,
  "facetable": true
},
{
  "name": "parentEntityId",
  "type": "Edm.String",
  "filterable": true
}
```

**Add scoring profile:**

```json
{
  "scoringProfiles": [{
    "name": "record-relevance",
    "text": {
      "weights": {
        "recordName": 5,
        "referenceNumbers": 4,
        "searchProfile": 3,
        "keywords": 2,
        "organizations": 2,
        "people": 2,
        "recordDescription": 1
      }
    }
  }],
  "defaultScoringProfile": "record-relevance"
}
```

### 3. Enrichment Service Architecture

```csharp
public interface IRecordEnrichmentService
{
    /// Generate search profile and write to both Dataverse + AI Search
    Task<EnrichmentResult> EnrichRecordAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct);

    /// Bulk enrich all records of a given entity type
    Task<BulkEnrichmentResult> BulkEnrichAsync(
        string entityLogicalName,
        DateTimeOffset? modifiedSince = null,
        CancellationToken ct = default);
}
```

**Enrichment flow per record:**

1. Fetch record from Dataverse (all relevant fields)
2. Fetch related records (parent matter/project, linked documents, recent communications)
3. Call Azure OpenAI with entity-specific prompt → get search profile text
4. Generate embedding (text-embedding-3-large, 3072 dims) from search profile
5. Write `sprk_searchprofile` to Dataverse via PATCH
6. Write enriched document to `spaarke-records-index` (mergeOrUpload)
7. Track completion in telemetry

**Cost management:**
- Azure OpenAI calls: ~$0.002 per record (gpt-4o-mini for profile generation)
- Embedding calls: ~$0.0001 per record (text-embedding-3-large)
- For 31 existing records: ~$0.07 total
- For 500 records: ~$1.05 total
- Rate limiting: max 10 concurrent enrichments

### 4. Admin Endpoints

```
POST /api/ai/admin/enrich/records
  Body: {
    entityTypes: ["sprk_matter", "sprk_project"],  // optional, default all
    modifiedSince: "2026-01-01T00:00:00Z",         // optional, default all
    dryRun: false
  }
  → Returns: { jobId, recordsQueued, estimatedDuration }

GET /api/ai/admin/enrich/records/{jobId}/status
  → Returns: { completed, failed, remaining, errors[] }

POST /api/ai/admin/enrich/records/{entityType}/{recordId}
  → Enrich single record (synchronous)
  → Returns: { searchProfile, chunksIndexed, embeddingDimensions }
```

### 5. Background Job Handler

```csharp
public class RecordEnrichmentJobHandler : IJobHandler
{
    public const string JobTypeName = "RecordEnrichment";
    // Processes individual record enrichment from Service Bus
    // Idempotency key: enrich-{entityName}-{recordId}
    // Same pattern as RagIndexingJobHandler
}
```

### 6. Sync Script Enhancement

Extend `Sync-RecordsToIndex.ps1` to:
- Support all 10 entity types
- Read `sprk_searchprofile` from Dataverse and map to index `searchProfile` field
- Always generate embeddings from `searchProfile` text (not optional)
- Populate `organizations` and `people` from extracted entities or lookup expansion
- Set `parentEntityType` and `parentEntityId` for child records

### 7. Dataverse Quick Find Configuration

For each entity, configure the Quick Find View:

| Column | Find (Searchable) | View (Displayed) |
|--------|-------------------|------------------|
| Primary Name (e.g., `sprk_mattername`) | ✅ | ✅ |
| Reference Number (e.g., `sprk_matternumber`) | ✅ | ✅ |
| `sprk_searchprofile` | ✅ | ❌ (hidden search surface) |
| Description | ❌ (redundant with profile) | ✅ |

This is a manual Dataverse configuration step — must be done in Power Apps maker portal for each entity.

### 8. Semantic Search Fan-Out (Optional / Phase 2)

Extend `SemanticSearchService` to query both indexes:

```csharp
// Current: searches knowledge index only (document chunks)
// Proposed: optionally fan-out to records index
var knowledgeResults = await SearchKnowledgeIndexAsync(query, ct);
var recordResults = await SearchRecordsIndexAsync(query, ct);  // NEW
return MergeAndDeduplicateResults(knowledgeResults, recordResults);
```

This lets the Semantic Search PCF return both "documents containing this topic" AND "matters/projects related to this topic" in a unified result set.

Add `sprk_communication`, `sprk_event`, `sprk_budget`, `sprk_memo`, `sprk_analysis`, `sprk_workassignment`, `sprk_document` to the `ValidEntityTypes` list in `SemanticSearchEndpoints.cs`.

## Entity-Specific Enrichment Details

### Matters

**Input fields**: `sprk_mattername`, `sprk_matternumber`, `sprk_matterdescription`, practice area lookup, assigned attorney lookup, client lookup, status, related document summaries

**Profile example:**
```
Patent infringement litigation involving Monte Rosa Therapeutics AG regarding
Targeted Protein Degradation technology. Patent application PAT-863412 in
publication stage. Practice area: Intellectual Property. Attorney: Sarah Chen.
Client: Monte Rosa Therapeutics AG. Key issues: patent claim scope, prior art
analysis, prosecution strategy for pharmaceutical biotechnology. Related project
PRJ-2026-0142. Three invoices totaling $47,500. Jurisdiction: USPTO. Status: Active.
```

### Documents

**Input fields**: `sprk_documentname`, `sprk_filename`, `sprk_filesummary` (existing), `sprk_filetldr` (existing), `sprk_filekeywords` (existing), `sprk_extractorganization`, `sprk_extractpeople`, `sprk_extractdates`, `sprk_extractfees`, `sprk_extractreference`, parent matter/project name

**Profile generation**: For documents, the search profile is synthesized from existing enrichment fields rather than re-analyzing the document content. This avoids redundant AI calls — the existing `ProfileSummaryJobHandler` already extracts summary/keywords/entities.

**Profile example:**
```
Non-Disclosure Agreement between ACME Corporation and Pinnacle Industries.
Executed January 2026, expires January 2028. Mutual confidentiality obligations
covering proprietary manufacturing processes. Key parties: ACME Corporation
(disclosing), Pinnacle Industries (receiving). Attorney: James Park. Parent
matter: LIT-2025-0847 Meridian Corp patent dispute. Document type: NDA.
Contains: confidentiality terms, non-compete clause, dispute resolution via
arbitration, governing law New York. File: ACME-Pinnacle-NDA-2026.pdf, 12 pages.
```

### Communications (Emails)

**Input fields**: `sprk_subject`, `sprk_from`, `sprk_to`, `sprk_cc`, `sprk_body` (truncated), `sprk_sentat`, `sprk_direction`, `sprk_regardingmatter`/project/invoice lookups, `sprk_attachmentcount`

**Profile example:**
```
Incoming email from opposing counsel Baker McKenzie regarding settlement
negotiation for patent matter LIT-2025-0847 Meridian Corp v Pinnacle Industries.
From: david.wong@bakermckenzie.com. To: sarah.chen@firm.com. Subject: Re:
Settlement Framework Proposal. Discusses revised damages calculation, licensing
terms, and proposed mediation timeline. Two attachments: revised settlement
agreement draft and damages expert report. Received March 2026. Matter:
LIT-2025-0847. Priority correspondence flagged for immediate review.
```

### Events (Tasks/Deadlines)

**Input fields**: `sprk_eventname`, `sprk_description`, `sprk_duedate`, `sprk_priority`, `statuscode`, regarding record lookups

### Invoices

**Input fields**: `sprk_invoicename`, `sprk_invoicenumber`, `sprk_invoicedescription`, amounts, parent matter lookup, vendor

### Projects, Budgets, Memos, Analyses, Work Assignments

Similar pattern — pull all available fields + parent record context → generate dense prose profile.

## Phasing

### Phase 1: Foundation (Core Pipeline)
- Create `RecordEnrichmentService` with OpenAI integration
- Build entity-specific prompt templates for all 10 types
- Add admin endpoint for single-record enrichment
- Extend `Sync-RecordsToIndex.ps1` for all 10 entity types
- Update `spaarke-records-index` schema (add `searchProfile`, `parentEntityType`, `parentEntityId`, scoring profile)
- Enrich existing records (27 matters, 4 projects, plus whatever exists for other entities)

### Phase 2: Scale + Automation
- Add bulk enrichment admin endpoint with background job handler
- Add `RecordEnrichmentJobHandler` for queue-based async processing
- Integrate enrichment into existing document `ProfileSummaryJobHandler` flow (dual-write)
- Populate `organizations` and `people` index fields from extracted entities

### Phase 3: Search Unification
- Extend `SemanticSearchService` for records-index fan-out
- Add all 10 entity types to `ValidEntityTypes` in semantic search
- Update Semantic Search PCF to display record results alongside document results
- Configure Dataverse Quick Find Views for all 10 entities

## Dependencies

| Dependency | Status | Notes |
|-----------|--------|-------|
| `sprk_searchprofile` field on all 10 entities | ✅ Created | User confirmed field added in Dataverse |
| Azure OpenAI access (gpt-4o-mini) | ✅ Available | `spaarke-openai-dev` |
| Azure OpenAI embeddings (text-embedding-3-large) | ✅ Available | Same endpoint |
| AI Search admin key access | ✅ Available | Used by existing scripts |
| Dataverse access via CLI | ✅ Available | Used by `Sync-RecordsToIndex.ps1` |
| BFF API deployment pipeline | ✅ Available | `scripts/Deploy-BffApi.ps1` |
| Service Bus job infrastructure | ✅ Available | Used by `RagIndexingJobHandler` |

## Success Criteria

1. All 10 entity types have AI-generated search profiles in Dataverse (`sprk_searchprofile` populated)
2. All 10 entity types indexed in `spaarke-records-index` with enriched fields + embeddings
3. Dataverse global search returns relevant results for topical queries (not just name/number matches)
4. Custom semantic search returns both document AND record matches in unified results
5. Search profile generation takes < 5 seconds per record (including embedding)
6. Bulk enrichment processes 100 records in < 10 minutes
7. Scoring profile in records index boosts name matches 5x over description matches

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| AI-generated profiles contain hallucinated content | Users find incorrect information via search | Use gpt-4o-mini with strict factual-only prompt; validate output contains only terms from input fields |
| Azure OpenAI rate limiting during bulk enrichment | Bulk job fails or takes too long | Bounded concurrency (10 max), exponential backoff, batch processing with progress tracking |
| `sprk_searchprofile` field causes Dataverse search index bloat | Exceeds 1,000-field limit across entities | One field per entity = 10 additional Find Columns (well within budget) |
| Stale search profiles (record updated but profile not regenerated) | Search results don't reflect latest data | Phase 2 adds automatic re-enrichment on record update via job handler |
| Two search surfaces still return different results | User confusion persists | Phase 3 semantic search fan-out unifies results; position native search as "quick lookup", custom search as "intelligence tool" |

---

*This design document is the input for `/design-to-spec` transformation into an AI-optimized specification.*
