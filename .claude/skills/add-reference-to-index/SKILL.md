---
description: Index golden reference documents into the spaarke-rag-references AI Search index
tags: [ai, rag, knowledge, indexing, ai-search]
techStack: [powershell, azure-ai-search, azure-openai]
appliesTo: ["scripts/ai-search/", "add reference", "index reference", "golden reference"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# add-reference-to-index

> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: AI Search index is live infrastructure. A known-good rebuild loop would require keeping a stable test document + expected chunk count + expected embedding dimensions in sync with whatever the OpenAI deployment is producing. Maintenance cost would exceed value.

## Purpose

Index golden reference documents (.md, .docx, .pdf) into the `spaarke-rag-references` AI Search index for L1 knowledge retrieval. Creates a Dataverse catalog record (delivery type: RAG Index) and chunks + embeds + uploads content.

## When to Use

- User says "add reference to index", "index reference document", "add golden reference", "index knowledge source"
- User wants to add a new golden reference document for AI playbook knowledge
- User wants to re-index or update an existing reference document
- Explicitly invoked with `/add-reference-to-index`

## Prerequisites

- `az login` (authenticated to Azure)
- Azure AI Search admin key access (via `az search admin-key show`)
- Azure OpenAI API access (via `az cognitiveservices account keys`)
- `sprk_knowledgedeliverytype` field exists on `sprk_analysisknowledge` entity
  - If not: run `scripts/ai-search/Setup-KnowledgeDeliveryType.ps1` first

## Scripts

| Script | Purpose |
|--------|---------|
| `scripts/ai-search/Add-ReferenceToIndex.ps1` | Index a single document |
| `scripts/ai-search/Index-AllReferences.ps1` | Batch index all KNW-*.md files |
| `scripts/ai-search/Setup-KnowledgeDeliveryType.ps1` | Create Dataverse field (one-time setup) |

## Procedure

### Step 1: Identify the File

Determine the file to index:
- User provides a file path
- User describes content and you locate the KNW-*.md file
- For new documents, help user create the file with proper metadata header

**Expected metadata header for .md files:**
```markdown
# KNW-XXX — Document Title

> **External ID**: KNW-XXX
> **Content Type**: Reference
> **Tenant**: system
> **Domain**: legal
> **Keywords**: keyword1, keyword2, keyword3
> **Created**: YYYY-MM-DD
```

### Step 2: Preview (Dry Run)

Run dry run to show what will be indexed:

```powershell
.\scripts\ai-search\Add-ReferenceToIndex.ps1 -FilePath "path\to\file.md" -DryRun
```

Report to user:
- File format and size
- Extracted metadata (ID, name, domain, tags)
- Estimated chunk count
- Confirm before proceeding

### Step 3: Execute Indexing

```powershell
# Single file
.\scripts\ai-search\Add-ReferenceToIndex.ps1 -FilePath "path\to\file.md"

# With explicit parameters (for non-.md files)
.\scripts\ai-search\Add-ReferenceToIndex.ps1 `
    -FilePath "path\to\document.docx" `
    -KnowledgeSourceId "KNW-011" `
    -Name "New Reference Document" `
    -Domain "legal" `
    -Tags @("contracts", "compliance")

# Batch all KNW files
.\scripts\ai-search\Index-AllReferences.ps1
```

### Step 4: Verify

Query the index to confirm chunks were created:

```bash
# Get AI Search admin key
SEARCH_KEY=$(az search admin-key show --service-name spaarke-search-dev --resource-group spe-infrastructure-westus2 --query 'primaryKey' -o tsv)

# Search for the indexed source
curl -s -X POST \
  "https://spaarke-search-dev.search.windows.net/indexes/spaarke-rag-references/docs/search?api-version=2024-07-01" \
  -H "api-key: $SEARCH_KEY" \
  -H "Content-Type: application/json" \
  -d '{"search":"*","filter":"knowledgeSourceId eq '\''KNW-001'\''","select":"id,knowledgeSourceName,chunkIndex,chunkCount","top":5}'
```

### Step 5: Report Results

Report to user:
- Number of chunks indexed
- Embedding dimensions (3072)
- Dataverse record ID (if created)
- Index name and source ID

## Supported File Formats

| Format | Extraction Method |
|--------|------------------|
| `.md` | Direct text read; auto-parses KNW metadata headers |
| `.docx` | Open XML extraction via System.IO.Compression |
| `.pdf` | Azure Document Intelligence (spaarke-docintel-dev) |

## Key Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-FilePath` | (required) | Path to .md, .docx, or .pdf file |
| `-KnowledgeSourceId` | auto-detect | Override the source ID |
| `-Name` | auto-detect | Override the display name |
| `-Domain` | "legal" | Domain classification |
| `-Tags` | [] | Additional tags |
| `-KnowledgeTypeId` | (none) | GUID of content category lookup |
| `-SkipDataverse` | false | Skip Dataverse catalog record |
| `-DryRun` | false | Preview without indexing |

## Architecture Context

**Two dimensions of knowledge classification:**

| Dimension | Field | Values |
|-----------|-------|--------|
| Content Category | `sprk_KnowledgeTypeId` (lookup) | Standards, Regulations, Best Practices, Templates, Taxonomy |
| Delivery Mechanism | `sprk_knowledgedeliverytype` (choice) | Inline (100000000), Document (100000001), RAG Index (100000002) |

**How RagIndex records work in the RAG process:**
```
Playbook Action Node
    ↓ linked to knowledge sources
ScopeResolverService loads records
    ↓
For each knowledge record:
    ├── DeliveryType = Inline  → inject sprk_content into prompt
    └── DeliveryType = RagIndex → query spaarke-rag-references
                                 → hybrid search retrieves chunks
                                 → inject matched chunks into prompt
```

## Error Handling

| Error | Cause | Fix |
|-------|-------|-----|
| "Failed to get access token" | Not logged in to Azure | Run `az login` |
| "Failed to get AI Search admin key" | No access to search service | Check permissions on spe-infrastructure-westus2 |
| "Failed to get Azure OpenAI API key" | No access to OpenAI resource | Check permissions |
| "sprk_knowledgedeliverytype query failed" | Field not created yet | Run `Setup-KnowledgeDeliveryType.ps1` |

## Related

- `scripts/seed-data/Deploy-Knowledge.ps1` — Seeds inline knowledge records
- `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs` — BFF API indexing service
- `infrastructure/ai-search/spaarke-rag-references.json` — Index schema definition

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Re-indexing duplicates chunks instead of replacing them | Script not invoked with `-ReplaceExisting` flag, OR a prior partial-failed run left orphan chunks | Always pass `-ReplaceExisting` for known existing docs. To clean orphans, query the index for the source doc's `parent_id` and delete all matching chunks before re-indexing. |
| Embeddings are produced but document is unsearchable | Search index `analyzer` setting doesn't match the embedding model's expected tokenization | Verify `infrastructure/ai-search/spaarke-rag-references.json` `analyzer` matches the deployed embedding model. After config change, re-index any documents added BEFORE the analyzer change. |
| Token count exceeds embedding model context window mid-chunk | Chunk size in script not aligned to model limit; long unbroken text in source doc | Reduce `-ChunkSize` parameter (default 1500 tokens; 1024 safer for `text-embedding-3-small`). Long sections may need manual paragraph splits. |
| Dataverse catalog record (`sprk_knowledge`) created but search index empty | Script failed AFTER catalog write but BEFORE embedding upload; idempotent re-run skips the catalog step | Delete the orphan `sprk_knowledge` record before re-running, OR pass `-ForceReindex` to bypass the existence check. |
