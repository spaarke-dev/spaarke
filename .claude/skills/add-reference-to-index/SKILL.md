# add-reference-to-index

---
description: Index golden reference documents into the spaarke-rag-references AI Search index
tags: [ai, rag, knowledge, indexing, ai-search]
techStack: [powershell, azure-ai-search, azure-openai]
appliesTo: ["scripts/ai-search/", "add reference", "index reference", "golden reference"]
alwaysApply: false
---

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
