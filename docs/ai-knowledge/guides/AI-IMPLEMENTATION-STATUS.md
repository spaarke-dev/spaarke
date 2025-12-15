# AI Implementation Status

> **Version**: 1.2
> **Date**: December 15, 2025
> **Status**: Current
> **Author**: Spaarke Engineering

---

## Overview

This document describes the **actual deployed state** of Spaarke's AI capabilities. For strategic vision and future roadmap, see [SPAARKE-AI-STRATEGY.md](../../reference/architecture/SPAARKE-AI-STRATEGY.md).

---

## Quick Status Summary

| Phase | Feature | Status | Environment |
|-------|---------|--------|-------------|
| 1a | Document Analysis + Streaming | Deployed | Dev |
| 1b | Dataverse Integration | Deployed | Dev |
| 1b | Email Metadata Extraction | Deployed | Dev |
| 2 | Record Matching (AI Search) | Deployed | Dev |
| 2 | PCF Record Suggestions UI | Deployed | Dev |
| 3 | AI Foundry Hub + Project | Deployed | Dev |
| 3 | Prompt Flow Templates | Created | Dev |
| 3 | Evaluation Pipeline Config | Created | Dev |
| 3 | Scope System (Actions, Skills, Knowledge) | Deployed | Dev |
| 3 | Tool Handler Framework | Deployed | Dev |
| 3 | Seed Data (Actions, Skills, Knowledge) | Deployed | Dev |
| Future | RAG Chat | Not Started | - |
| Future | Vector Embeddings | Not Started | - |

---

## 1. Azure Resources

### 1.1 Deployed Azure Services

| Service | Resource Name | SKU | Region |
|---------|--------------|-----|--------|
| Azure OpenAI | spaarke-openai-dev | Standard | West US 2 |
| Document Intelligence | spaarke-docintel-dev | Standard | West US 2 |
| Azure AI Search | spaarke-search-dev | Standard (S1) | West US 2 |
| AI Foundry Hub | sprkspaarkedev-aif-hub | Basic | West US 2 |
| AI Foundry Project | sprkspaarkedev-aif-proj | N/A | West US 2 |
| AI Foundry Storage | sprkspaarkedevaifsa | Standard LRS | West US 2 |
| AI Foundry Key Vault | sprkspaarkedev-aif-kv | Standard | West US 2 |
| AI Foundry App Insights | sprkspaarkedev-aif-insights | Per GB | West US 2 |
| AI Foundry Log Analytics | sprkspaarkedev-aif-logs | Per GB | West US 2 |

### 1.2 Azure OpenAI Deployments

| Deployment Name | Model | Version | Purpose |
|-----------------|-------|---------|---------|
| gpt-4o-mini | gpt-4o-mini | 2024-07-18 | Document summarization |

**Not Deployed**:
- Embedding model (text-embedding-3-large) - planned for RAG
- gpt-4o - higher quality option (available but not deployed)

### 1.3 Azure AI Search Configuration

```
Endpoint: https://spaarke-search-dev.search.windows.net
Index: spaarke-records-index
Semantic Search: Enabled (standard tier)
```

**Index Schema Fields**:
| Field | Type | Searchable | Filterable |
|-------|------|------------|------------|
| id | string (key) | - | - |
| recordType | string | Yes | Yes |
| recordName | string | Yes | No |
| recordDescription | string | Yes | No |
| organizations | Collection(string) | Yes | No |
| people | Collection(string) | Yes | No |
| referenceNumbers | Collection(string) | Yes | Yes |
| keywords | string | Yes | No |
| dataverseRecordId | string | No | Yes |
| dataverseEntityName | string | No | Yes |

### 1.4 AI Foundry Infrastructure

AI Foundry provides a managed platform for prompt engineering, evaluation, and MLOps.

**Architecture Diagram**:
```
┌─────────────────────────────────────────────────────────────────┐
│                    Azure AI Foundry                             │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────┐   │
│  │          Hub: sprkspaarkedev-aif-hub                     │   │
│  │  ┌─────────────────┐  ┌─────────────────┐               │   │
│  │  │ Key Vault       │  │ Storage Account │               │   │
│  │  │ (Secrets)       │  │ (Artifacts)     │               │   │
│  │  └─────────────────┘  └─────────────────┘               │   │
│  │  ┌─────────────────┐  ┌─────────────────┐               │   │
│  │  │ App Insights    │  │ Log Analytics   │               │   │
│  │  │ (Monitoring)    │  │ (Diagnostics)   │               │   │
│  │  └─────────────────┘  └─────────────────┘               │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                   │
│                              ▼                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │       Project: sprkspaarkedev-aif-proj                   │   │
│  │  ┌─────────────────────────────────────────────────────┐ │   │
│  │  │               Connections                           │ │   │
│  │  │  ┌──────────────────┐  ┌──────────────────┐        │ │   │
│  │  │  │ azure-openai-    │  │ ai-search-       │        │ │   │
│  │  │  │ connection       │  │ connection       │        │ │   │
│  │  │  │ (Managed ID)     │  │ (Managed ID)     │        │ │   │
│  │  │  └────────┬─────────┘  └────────┬─────────┘        │ │   │
│  │  └───────────│─────────────────────│──────────────────┘ │   │
│  │              │                     │                     │   │
│  │  ┌───────────▼─────────────────────▼──────────────────┐ │   │
│  │  │               Prompt Flows                         │ │   │
│  │  │  ┌──────────────────┐  ┌──────────────────┐       │ │   │
│  │  │  │ analysis-execute │  │ analysis-continue│       │ │   │
│  │  │  │ (Doc Analysis)   │  │ (Chat Continue)  │       │ │   │
│  │  │  └──────────────────┘  └──────────────────┘       │ │   │
│  │  └────────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                   │
│                              ▼                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                    Evaluation                            │   │
│  │  Metrics: groundedness, relevance, coherence, fluency    │   │
│  │  Custom: format_compliance, completeness                 │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
           │                               │
           ▼                               ▼
┌─────────────────────┐       ┌─────────────────────┐
│ spaarke-openai-dev  │       │ spaarke-search-dev  │
│ (Azure OpenAI)      │       │ (Azure AI Search)   │
│ gpt-4o-mini         │       │ spaarke-records-    │
│                     │       │ index               │
└─────────────────────┘       └─────────────────────┘
```

**Connections**:

| Connection Name | Type | Target Resource | Auth Method |
|-----------------|------|-----------------|-------------|
| azure-openai-connection | azure_open_ai | spaarke-openai-dev | Managed Identity |
| ai-search-connection | azure_ai_search | spaarke-search-dev | Managed Identity |

**Prompt Flows**:

| Flow Name | Purpose | Inputs | Outputs |
|-----------|---------|--------|---------|
| analysis-execute | Document analysis | document_text, action_type, skills, knowledge_context | analysis_output |
| analysis-continue | Conversational continuation | previous_analysis, user_message, chat_history | continuation_output |

**Evaluation Metrics**:

| Metric | Type | Threshold | Description |
|--------|------|-----------|-------------|
| groundedness | GPT-based | 3.5/5 | Analysis grounded in source document |
| relevance | GPT-based | 3.5/5 | Analysis relevant to document content |
| coherence | GPT-based | 4.0/5 | Logical flow and consistency |
| fluency | GPT-based | 4.0/5 | Language quality and readability |
| format_compliance | Custom | 90% | Output follows markdown/JSON format |
| completeness | Custom | 85% | All required sections present |

**Infrastructure Files**:
```
infrastructure/ai-foundry/
├── bicep/
│   └── ai-foundry-hub.bicep           # Hub + Project deployment
├── connections/
│   ├── azure-openai-connection.yaml   # OpenAI connection config
│   └── ai-search-connection.yaml      # AI Search connection config
├── prompt-flows/
│   ├── analysis-execute/              # Main analysis flow
│   │   ├── flow.dag.yaml
│   │   ├── build_system_prompt.py
│   │   ├── build_user_prompt.py
│   │   ├── generate_analysis.jinja2
│   │   └── requirements.txt
│   └── analysis-continue/             # Continuation flow
│       ├── flow.dag.yaml
│       ├── build_continuation_prompt.py
│       ├── generate_continuation.jinja2
│       └── requirements.txt
├── evaluation/
│   ├── eval-config.yaml               # Metrics configuration
│   ├── metrics/
│   │   ├── format_compliance.py       # Custom format checker
│   │   └── completeness.py            # Custom section checker
│   └── test-data/
│       └── sample-evaluations.jsonl   # Test cases
└── README.md                          # AI Foundry documentation
```

---

## 2. BFF API Endpoints

### 2.1 Document Intelligence Endpoints

**Base Path**: `/api/ai/document-intelligence`

| Method | Path | Description | Status |
|--------|------|-------------|--------|
| POST | `/analyze` | Stream document analysis via SSE | Deployed |
| POST | `/enqueue` | Enqueue single document for background analysis | Deployed |
| POST | `/enqueue-batch` | Enqueue up to 10 documents | Deployed |

**Example Request - /analyze**:
```json
POST /api/ai/document-intelligence/analyze
{
  "documentId": "guid",
  "driveId": "container-id",
  "itemId": "spe-item-id"
}
```

**SSE Response Format**:
```
data: {"type":"progress","message":"Extracting text..."}

data: {"type":"summary","content":"First part of summary..."}

data: {"type":"done","result":{...complete structured output...}}
```

### 2.2 Record Matching Endpoints

| Method | Path | Description | Status |
|--------|------|-------------|--------|
| POST | `/match-records` | Find matching Dataverse records | Deployed |
| POST | `/associate-record` | Link document to record | Deployed |

**Example Request - /match-records**:
```json
POST /api/ai/document-intelligence/match-records
{
  "entities": {
    "organizations": ["Acme Corp", "Smith LLC"],
    "people": ["John Smith"],
    "references": ["INV-2024-001"],
    "keywords": ["contract", "services"]
  },
  "recordTypeFilter": "all",
  "maxResults": 5
}
```

**Response**:
```json
{
  "suggestions": [
    {
      "recordId": "guid",
      "recordType": "sprk_matter",
      "recordName": "Smith LLC - Services Agreement",
      "confidenceScore": 0.87,
      "matchReasons": [
        "Organization: Smith LLC",
        "Reference: INV-2024-001 (exact match)"
      ],
      "lookupFieldName": "sprk_matter"
    }
  ],
  "totalMatches": 3
}
```

### 2.3 Admin Endpoints

**Base Path**: `/api/admin/record-matching`

| Method | Path | Description | Status |
|--------|------|-------------|--------|
| POST | `/sync` | Sync Dataverse records to AI Search | Deployed |
| GET | `/status` | Get index sync status | Deployed |

---

## 3. Service Architecture

### 3.1 Key Service Files

```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/
│   ├── DocumentIntelligenceEndpoints.cs    # SSE streaming endpoints
│   └── RecordMatchEndpoints.cs             # Record matching endpoints
├── Api/Admin/
│   └── RecordMatchingAdminEndpoints.cs     # Index sync/status endpoints
├── Configuration/
│   ├── DocumentIntelligenceOptions.cs      # All AI configuration
│   └── DocumentIntelligenceOptionsValidator.cs
├── Services/Ai/
│   ├── IDocumentIntelligenceService.cs
│   ├── DocumentIntelligenceService.cs      # Main analysis orchestrator
│   └── TextExtractorService.cs             # PDF/DOCX/Email extraction
└── Services/RecordMatching/
    ├── IRecordMatchService.cs
    └── RecordMatchService.cs               # AI Search integration
```

### 3.2 Configuration (DocumentIntelligenceOptions)

```json
{
  "DocumentIntelligence": {
    "Enabled": true,
    "StreamingEnabled": true,
    "OpenAiEndpoint": "https://spaarke-openai-dev.openai.azure.com/",
    "OpenAiKey": "<from-keyvault>",
    "SummarizeModel": "gpt-4o-mini",
    "MaxOutputTokens": 1000,
    "Temperature": 0.3,
    "DocIntelEndpoint": "https://westus2.api.cognitive.microsoft.com/",
    "DocIntelKey": "<from-keyvault>",
    "RecordMatchingEnabled": true,
    "AiSearchEndpoint": "https://spaarke-search-dev.search.windows.net",
    "AiSearchKey": "<from-keyvault>",
    "AiSearchIndexName": "spaarke-records-index",
    "StructuredOutputEnabled": true
  }
}
```

### 3.3 Azure App Service Settings

| Setting | Description |
|---------|-------------|
| `DocumentIntelligence__Enabled` | Master switch for AI features |
| `DocumentIntelligence__OpenAiEndpoint` | Azure OpenAI endpoint URL |
| `DocumentIntelligence__OpenAiKey` | Azure OpenAI API key |
| `DocumentIntelligence__SummarizeModel` | Deployment name (gpt-4o-mini) |
| `DocumentIntelligence__DocIntelEndpoint` | Document Intelligence endpoint |
| `DocumentIntelligence__DocIntelKey` | Document Intelligence API key |
| `DocumentIntelligence__RecordMatchingEnabled` | Enable record matching |
| `DocumentIntelligence__AiSearchEndpoint` | Azure AI Search endpoint |
| `DocumentIntelligence__AiSearchKey` | Azure AI Search API key |
| `DocumentIntelligence__AiSearchIndexName` | Search index name |

---

## 4. Supported File Types

### 4.1 Text Extraction Methods

| Extension | Method | Library/Service |
|-----------|--------|-----------------|
| .txt, .md, .json, .csv, .xml, .html | Native | Direct read |
| .pdf, .docx, .doc | Document Intelligence | Azure AI Document Intelligence |
| .eml | Email | MimeKit |
| .msg | Email | MsgReader |
| .png, .jpg, .jpeg, .gif, .tiff, .bmp, .webp | Vision OCR | GPT-4 Vision (not yet deployed) |

### 4.2 Extraction Pipeline

```
File Upload → SPE Storage
    ↓
Get File from SPE (SpeFileStore)
    ↓
Determine Extraction Method (by extension)
    ↓
Extract Text:
  - Native: Read bytes as UTF-8
  - Document Intelligence: AnalyzeDocument API
  - Email: Parse MIME structure, extract body + metadata
    ↓
Generate AI Analysis (OpenAI gpt-4o-mini)
    ↓
Stream Response via SSE
    ↓
Save to Dataverse (sprk_document fields)
```

---

## 5. Dataverse Integration

### 5.1 Document Entity Fields (sprk_document)

| Field | Logical Name | Type | Purpose |
|-------|--------------|------|---------|
| Summary | sprk_filesummary | Multiline Text | AI-generated summary |
| TL;DR | sprk_filetldr | Multiline Text | Key points (newline-separated) |
| Keywords | sprk_filekeywords | Text | Searchable terms (comma-separated) |
| Summary Status | sprk_filesummarystatus | Choice | 0=NotStarted, 1=Pending, 2=InProgress, 3=Completed, 4=Failed |
| Extract Organization | sprk_extractorganization | Multiline Text | Organization names |
| Extract People | sprk_extractpeople | Multiline Text | Person names |
| Extract Fees | sprk_extractfees | Multiline Text | Monetary amounts |
| Extract Dates | sprk_extractdates | Multiline Text | Date references |
| Extract Reference | sprk_extractreference | Multiline Text | Reference numbers |
| Extract Document Type | sprk_extractdocumenttype | Text | Raw AI classification |
| Document Type | sprk_documenttype | Choice | Mapped choice value |
| Email Subject | sprk_emailsubject | Text | Email subject line |
| Email From | sprk_emailfrom | Text | Sender address |
| Email To | sprk_emailto | Text | Recipients |
| Email Date | sprk_emaildate | DateTime | Send date |
| Email Body | sprk_emailbody | Multiline Text | Email content |
| Attachments | sprk_attachments | Multiline Text | JSON array of attachment info |

### 5.2 Document Type Mapping

| AI Output | Dataverse Choice | Value |
|-----------|-----------------|-------|
| contract | Contract | 100000000 |
| invoice | Invoice | 100000001 |
| proposal | Proposal | 100000002 |
| report | Report | 100000003 |
| letter | Letter | 100000004 |
| memo | Memo | 100000005 |
| email | Email | 100000006 |
| agreement | Agreement | 100000007 |
| statement | Statement | 100000008 |
| other | Other | 100000009 |

---

## 6. Record Matching

### 6.1 Confidence Scoring Algorithm

The RecordMatchService uses weighted entity matching:

| Entity Type | Weight | Description |
|-------------|--------|-------------|
| Reference Numbers | 50% | Exact match on INV-*, MAT-*, PO-* etc. |
| Organizations | 25% | Fuzzy match on company names |
| People | 15% | Fuzzy match on person names |
| Keywords | 10% | Term overlap matching |

**Scoring Details**:
- Reference match: 1.0 for exact case-insensitive match
- Organization/People: Jaccard similarity with 0.7 threshold
- Final score normalized to 0-1 range

### 6.2 Supported Record Types

| Entity | Lookup Field | Display Name |
|--------|--------------|--------------|
| sprk_matter | sprk_matter | Matter |
| sprk_project | sprk_project | Project |
| sprk_invoice | sprk_invoice | Invoice |

---

## 7. Scope System (Phase 3)

The Scope System enables configurable AI analysis through Dataverse-managed Actions, Skills, Knowledge, and Tools.

### 7.1 Dataverse Entities

| Entity | Table Name | Purpose | Seed Records |
|--------|------------|---------|--------------|
| Actions | sprk_analysisaction | System prompt templates | 5 |
| Skills | sprk_analysisskill | Instruction fragments added to prompts | 10 |
| Knowledge | sprk_analysisknowledge | Reference materials for context | 5 |
| Knowledge Deployments | sprk_knowledgedeployment | Groups knowledge sources by deployment model | 1 |
| Tools | sprk_analysistool | External tool handlers | 3 |
| Playbooks | sprk_analysisplaybook | Pre-configured scope combinations | 0 (Phase 4) |

### 7.2 KnowledgeType Enum

Knowledge sources are typed to control how they're included in prompts:

| Type | Dataverse Value | Behavior |
|------|-----------------|----------|
| Document | 100000000 | Reference document (inline if has content) |
| Rule | 100000001 | Business rules/guidelines (always inline) |
| Template | 100000002 | Template documents (always inline) |
| RagIndex | 100000003 | RAG index reference (async retrieval, not inline) |

**Code Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs`

```csharp
public enum KnowledgeType
{
    Document = 100000000,
    Rule = 100000001,
    Template = 100000002,
    RagIndex = 100000003
}
```

### 7.3 Prompt Construction (AnalysisContextBuilder)

The `AnalysisContextBuilder` constructs prompts from scope components:

**System Prompt Structure**:
```
{Action.SystemPrompt}

## Instructions
- {Skill[0].PromptFragment}
- {Skill[1].PromptFragment}
...

## Output Format
Provide your analysis in Markdown format with appropriate headings and structure.
```

**User Prompt Structure**:
```
# Document to Analyze
{documentText}

# Reference Materials
## {Knowledge[0].Name} (Rule)
{Knowledge[0].Content}

## {Knowledge[1].Name} (Template)
{Knowledge[1].Content}
...

---
Please analyze the document above according to the instructions.
```

**Code Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs`

### 7.4 Seed Data

**Actions** (5 total):
| Name | System Prompt Summary |
|------|----------------------|
| Summarize Document | AI assistant for document summaries |
| Review Agreement | Legal analyst for contract review |
| Extract Entities | Entity extraction specialist |
| Compare Documents | Document comparison analyst |
| Analyze Risk | Risk assessment specialist |

**Skills** (10 total):
| Name | Prompt Fragment Summary |
|------|------------------------|
| Identify Terms | List all defined terms |
| Extract Dates | Extract key dates and deadlines |
| Identify Parties | Identify all parties mentioned |
| Extract Obligations | List contractual obligations |
| Identify Risks | Highlight potential risks |
| Summarize Sections | Summarize each section |
| Extract Financials | Extract monetary amounts |
| Identify Compliance | Check compliance requirements |
| Compare Clauses | Compare similar clauses |
| Generate Timeline | Create timeline of events |

**Knowledge Sources** (5 total):
| Name | Type |
|------|------|
| Standard Contract Templates | Template |
| Company Policies | Rule |
| Business Writing Guidelines | Rule |
| Legal Reference Materials | Document |
| Example Analyses | RagIndex |

### 7.5 Tool Handler Framework

Extensible tool system for AI-powered analysis:

| Interface | Purpose |
|-----------|---------|
| `IAnalysisToolHandler` | Base interface for tool implementations |
| `EntityExtractor` | Extract structured entities from text |
| `ClauseAnalyzer` | Analyze contract clauses |
| `DocumentClassifier` | Classify document types |

**Code Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/`

```csharp
public interface IAnalysisToolHandler
{
    string ToolName { get; }
    Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct);
}
```

---

## 8. PCF Control Integration

### 8.1 UniversalQuickCreate (v3.5.0)

**Location**: `src/client/pcf/UniversalQuickCreate/`

**AI Features**:
- `useAiSummary` hook - consumes SSE streaming
- `AiSummaryPanel` component - displays streaming summary
- `RecordTypeSelector` dropdown - filter match suggestions
- `RecordMatchSuggestions` component - shows ranked matches with one-click association

**Control Version**: 3.5.0 (deployed December 2025)

### 8.2 Key Components

```
UniversalQuickCreate/
├── components/
│   ├── AiSummaryPanel.tsx          # Summary + TL;DR + Keywords display
│   ├── EntityExtractionSection.tsx # Collapsible extracted entities
│   ├── RecordTypeSelector.tsx      # Dropdown: All/Matter/Project/Invoice
│   └── RecordMatchSuggestions.tsx  # Confidence bars + one-click link
└── hooks/
    ├── useAiSummary.ts             # SSE consumption + state
    └── useRecordMatching.ts        # Match API integration
```

---

## 9. What's NOT Implemented

### 9.1 From Strategy Document (Future Work)

| Feature | Strategy Section | Status |
|---------|------------------|--------|
| RAG Chat | Section 3.2 | Not started |
| Vector Embeddings | Section 4.1 | Not started |
| Semantic Document Search | Section 4.2 | Not started |
| AI Foundry Hub + Project | Section 5 | **Deployed** (see Section 1.4) |
| AI Foundry Prompt Flows | Section 5 | **Templates Created** (not deployed to runtime) |
| AI Foundry Evaluation | Section 5 | **Config Created** (not running) |
| Multi-modal Vision OCR | - | Partial (config ready, model not deployed) |
| Background Job Handler | - | Placeholder only |

### 9.2 Architecture Document vs Reality

The [SPAARKE-AI-ARCHITECTURE.md](./SPAARKE-AI-ARCHITECTURE.md) describes a more comprehensive system. Here's what's different:

| Architecture Doc | Reality |
|------------------|---------|
| `AiSearchService` for RAG | `RecordMatchService` for record matching only |
| `AiChatService` for conversations | Not implemented |
| `EmbeddingService` with caching | Not implemented |
| Per-customer search indexes | Single shared index |
| Redis embedding cache | No embedding cache (no embeddings) |
| `AiAuthorizationFilter` | Commented out (TODO: OBO auth) |

---

## 10. Troubleshooting

### 10.1 Common Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| 503 on all AI endpoints | `Enabled=false` or missing config | Check `DocumentIntelligence__Enabled` |
| 404 on match-records | `RecordMatchingEnabled=false` | Enable in app settings |
| Empty analysis results | OpenAI deployment name wrong | Match `SummarizeModel` to Azure deployment |
| PDF extraction fails | Document Intelligence not configured | Add `DocIntelEndpoint` and `DocIntelKey` |

### 10.2 Health Check Commands

```bash
# Check API is running
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping

# Check AI Search index exists
az search index show \
  --name spaarke-records-index \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2

# Check OpenAI deployment
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table

# Check AI Foundry Hub exists
az ml workspace show \
  --name sprkspaarkedev-aif-hub \
  --resource-group spe-infrastructure-westus2 \
  -o table

# Check AI Foundry Project exists
az ml workspace show \
  --name sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2 \
  -o table

# List AI Foundry connections
az ml connection list \
  --workspace-name sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2 \
  -o table
```

---

## 11. Related Documentation

| Document | Purpose |
|----------|---------|
| [SPAARKE-AI-STRATEGY.md](../../reference/architecture/SPAARKE-AI-STRATEGY.md) | Strategic vision and roadmap |
| [SPAARKE-AI-ARCHITECTURE.md](./SPAARKE-AI-ARCHITECTURE.md) | Target architecture (aspirational) |
| [AI Foundry README](../../../infrastructure/ai-foundry/README.md) | AI Foundry infrastructure documentation |
| [AI-SUMMARY-QUICK-REF.md](../../guides/AI-SUMMARY-QUICK-REF.md) | Quick troubleshooting reference |
| [TROUBLESHOOTING-AI-SUMMARY.md](../../guides/TROUBLESHOOTING-AI-SUMMARY.md) | Detailed troubleshooting guide |

---

*Document Owner: Spaarke Engineering*
*Last Updated: December 15, 2025*
