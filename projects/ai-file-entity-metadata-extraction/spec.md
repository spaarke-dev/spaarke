# AI File Entity Metadata Extraction

> **Project Code**: ai-file-entity-metadata-extraction
> **Status**: Draft Specification
> **Created**: December 9, 2025
> **Author**: Claude Code / Spaarke Team

---

## 1. Executive Summary

This project enhances the existing AI Document Summary feature to provide structured metadata extraction from uploaded files, enabling:
- **Improved readability**: TL;DR bullet-point format alongside prose summaries
- **Enhanced searchability**: AI-extracted keywords for Dataverse Relevance Search
- **Entity extraction**: Automated identification of organizations, people, amounts, dates, and references
- **Document-to-record matching**: Intelligent suggestions linking documents to related Dataverse records (Matters, Projects, Invoices)
- **Email support**: Processing of email files (eml/msg) from server-side sync with entity matching

**Service Naming**: This project renames the existing "Summarize" services to "DocumentIntelligence" services, as these become the gateway to all Azure AI Document Intelligence capabilities.

---

## 2. Business Context

### 2.1 Current State

The Spaarke platform currently provides:
- Document upload via UniversalQuickCreate PCF control
- AI-powered document summarization (prose format, 2-4 paragraphs)
- Storage in SharePoint Embedded (SPE) with metadata in Dataverse
- Basic file association to Dataverse records (manual user selection)

### 2.2 Pain Points

1. **Prose summaries are slow to scan** - Users want quick bullet-point TL;DR format
2. **Search is keyword-dependent** - Users must know exact terms; AI-extracted keywords improve recall
3. **Manual record association** - Users must manually link files to Matters/Projects/Invoices
4. **Email processing gap** - Server-side sync brings emails but no intelligent linking

### 2.3 Target Outcome

Users upload a document or receive an email via server-side sync, and the system:
1. Generates a prose summary AND bullet-point TL;DR
2. Extracts searchable keywords automatically
3. Identifies entities (organizations, people, amounts, dates, references)
4. **User selects target record type(s)** to match against (e.g., "Matters only", "Projects only", or "All")
5. Suggests matching Dataverse records based on extracted entities and selected record type filter
6. Enables Dataverse Relevance Search to find documents by AI-extracted keywords

---

## 3. Functional Requirements

### 3.1 Structured AI Output (Phase 1a)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-1.1 | AI analysis returns structured JSON with summary, TL;DR, keywords, and entities | Must |
| FR-1.2 | TL;DR output is an array of 3-7 bullet points | Must |
| FR-1.3 | Keywords are comma-separated search terms (proper nouns, technical terms, topics) | Must |
| FR-1.4 | Entity extraction identifies: organizations, people, amounts, dates, document type, references | Must |
| FR-1.5 | Document type classification: contract, invoice, proposal, report, letter, memo, email, other | Must |
| FR-1.6 | Fallback gracefully if JSON parsing fails (use raw text as summary) | Must |
| FR-1.7 | Support for existing file types: txt, md, json, csv, xml, html, pdf, docx, doc, images, **eml, msg** | Must |

### 3.2 Dataverse Storage (Phase 1b)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-2.1 | Store prose summary in `sprk_filesummary` field | Must |
| FR-2.2 | Store TL;DR bullets in `sprk_filetldr` field (newline-separated) | Must |
| FR-2.3 | Store keywords in `sprk_filekeywords` field (indexed for Relevance Search) | Must |
| FR-2.4 | Store extracted entities as JSON in `sprk_fileentities` field | Must |
| FR-2.5 | Store analysis timestamp in `sprk_filesummarydate` field | Must |
| FR-2.6 | Enable Dataverse Relevance Search on `sprk_filekeywords` field | Must |

### 3.3 PCF UI Updates (Phase 1b)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-3.1 | Display TL;DR as bullet list in AI Summary panel | Must |
| FR-3.2 | Display keywords as tags/chips below summary | Should |
| FR-3.3 | Display extracted entities in collapsible section | Should |
| FR-3.4 | Maintain streaming display for real-time feedback | Must |

### 3.4 Email File Support (Phase 1b)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-4.1 | Support .eml file type extraction (MIME format) | Must |
| FR-4.2 | Support .msg file type extraction (Outlook format) | Must |
| FR-4.3 | Extract email metadata: From, To, Cc, Subject, Date, Body | Must |
| FR-4.4 | Include email metadata in entity extraction (sender/recipient as people, dates) | Must |
| FR-4.5 | Process email **subject line AND body text** through AI analysis pipeline (subject often contains key context like matter references, client names, or action items not in body) | Must |

### 3.5 Record Matching Service (Phase 2)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-5.1 | **User can select target record type(s)** for matching (e.g., "Matters", "Projects", "Invoices", or "All"). This filter focuses the AI matching analysis on specific record types. | Must |
| FR-5.2 | Azure AI Search index stores Dataverse record metadata (Matters, Projects, Invoices) | Must |
| FR-5.3 | Index includes: record name, description, keywords, related party names, reference numbers | Must |
| FR-5.4 | Match API accepts extracted entities **and record type filter** and returns ranked record suggestions | Must |
| FR-5.5 | Match results include confidence score and match reasoning | Should |
| FR-5.6 | Support match by: organization name, person name, reference number, date range | Must |
| FR-5.7 | Incremental index sync when Dataverse records change | Should |
| FR-5.8 | **UniversalQuickCreate PCF displays suggested record matches** with one-click association (applies to all file types, not just emails) | Must |
| FR-5.9 | PCF includes record type selector dropdown before/during matching | Must |

### 3.6 Email-to-Matter Matching (Phase 2)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-6.1 | When email file is processed, suggest related Matter based on entities | Must |
| FR-6.2 | Match using: sender/recipient organizations, subject keywords, referenced matter numbers | Must |
| FR-6.3 | Display match suggestions in email processing workflow (future integration point) | Must |
| FR-6.4 | Allow user to confirm or override suggested match | Must |
| FR-6.5 | **When user selects a matched record, populate the corresponding Document lookup field** (e.g., if user selects Matter "XYZ", set `sprk_matter` lookup on the Document record; if Project, set `sprk_project` lookup) | Must |

---

## 4. Technical Architecture

### 4.1 System Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           User Interface                                 │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  UniversalQuickCreate PCF                                        │    │
│  │  - File Upload → AI Analysis → Display Summary/TL;DR/Entities   │    │
│  │  - Record Type Selector (Matters/Projects/Invoices/All)         │    │
│  │  - Show Suggested Record Matches → One-Click Association        │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         Spaarke BFF API                                  │
│  ┌──────────────────────────┐  ┌──────────────────────────────────┐    │
│  │ DocumentIntelligenceService│ │ RecordMatchService               │    │
│  │ - Prompt mgmt              │ │ - Query AI Search                │    │
│  │ - AI streaming             │ │ - Apply record type filter       │    │
│  │ - JSON parsing             │ │ - Rank matches                   │    │
│  │ - Entity extraction        │ │ - Return suggestions             │    │
│  └──────────────────────────┘  └──────────────────────────────────┘    │
│           │                                     │                       │
│           ▼                                     ▼                       │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────┐   │
│  │ TextExtractor    │  │ EmailExtractor   │  │ DataverseIndexer     │   │
│  │ - PDF/DOCX/etc   │  │ - EML/MSG parse  │  │ - Sync records to    │   │
│  │ - Native text    │  │ - Subject+Body   │  │   Azure AI Search    │   │
│  └──────────────────┘  └──────────────────┘  └──────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                    │                                     │
                    ▼                                     ▼
    ┌───────────────────────────┐         ┌───────────────────────────┐
    │     Azure OpenAI          │         │    Azure AI Search        │
    │  - gpt-4o-mini            │         │  - Vector index           │
    │  - Structured JSON output │         │  - Dataverse records      │
    └───────────────────────────┘         │  - Hybrid search          │
                                          │  - Record type filtering  │
                                          └───────────────────────────┘
                                                      │
                                                      ▼
    ┌───────────────────────────┐         ┌───────────────────────────┐
    │       Dataverse           │◄────────│   Index Sync Service      │
    │  - sprk_document records  │         │  - Plugin triggers        │
    │  - sprk_matter records    │         │  - Scheduled refresh      │
    │  - sprk_project records   │         └───────────────────────────┘
    └───────────────────────────┘
```

### 4.2 Component Details

#### 4.2.1 Document Intelligence Service (Renamed from SummarizeService)

**Rename Rationale**: The service evolves from simple summarization to a comprehensive document analysis gateway that interfaces with Azure AI Document Intelligence capabilities. The new name better reflects its expanded scope.

**Files to Rename**:
- `SummarizeService.cs` → `DocumentIntelligenceService.cs`
- `ISummarizeService.cs` → `IDocumentIntelligenceService.cs`
- `SummarizeEndpoints.cs` → `DocumentIntelligenceEndpoints.cs`
- Endpoint paths: `/api/ai/summarize/*` → `/api/ai/document-intelligence/*`

**Responsibilities**:
- Send document text to Azure OpenAI with structured output prompt
- Parse JSON response into strongly-typed objects
- Handle fallback when JSON parsing fails
- Stream response for real-time UI feedback
- Support all file types including email (eml/msg)

**Prompt Design**:
```
You are a document analysis assistant. Analyze the following document and return a JSON response with this exact structure:

{
  "summary": "<2-3 sentence professional prose summary of the document's main purpose and content>",
  "tldr": [
    "<key point 1>",
    "<key point 2>",
    "<key point 3 - 7 bullets maximum>"
  ],
  "keywords": "<comma-separated list of searchable terms: proper nouns, technical terms, topics, product names>",
  "entities": {
    "organizations": ["<company, law firm, agency, or organization names mentioned>"],
    "people": ["<person names mentioned>"],
    "amounts": ["<monetary values, quantities, or percentages mentioned>"],
    "dates": ["<specific dates, date ranges, or time periods mentioned>"],
    "documentType": "<contract|invoice|proposal|report|letter|memo|email|agreement|statement|other>",
    "references": ["<matter numbers, case IDs, invoice numbers, PO numbers, reference codes>"]
  }
}

Rules:
- Return ONLY valid JSON, no additional text
- For entities, only include items CLEARLY stated in the document
- If an entity category has no matches, use empty array []
- Keywords should enable search - include abbreviations and full forms
- TL;DR bullets should be actionable insights, not generic statements

Document:
{documentText}
```

#### 4.2.2 Email Extractor Service

**Responsibilities**:
- Parse .eml files (RFC 5322 MIME format)
- Parse .msg files (Microsoft Outlook format)
- Extract: From, To, Cc, **Subject**, Date, Body (plain text + HTML)
- **Combine subject line with body** for AI analysis (subject often contains critical context)
- Pass extracted content to AI analysis pipeline

**Implementation Approach**:
- Use MimeKit library for .eml parsing
- Use MsgReader library for .msg parsing
- Convert HTML body to plain text using HtmlAgilityPack
- **Prepend subject line to body text** for comprehensive analysis

**Email-Specific Prompt Additions**:
```
For email documents, also extract:
- Sender email/name → add to "people"
- Recipients → add to "people"
- Email domain organizations → add to "organizations"
- Subject line keywords → add to "keywords"
- Matter/case references in subject → add to "references"
```

**Email Text Format for AI**:
```
Subject: {emailSubject}

From: {fromAddress}
To: {toAddresses}
Date: {emailDate}

{emailBody}
```

#### 4.2.3 Record Match Service

**Responsibilities**:
- Accept **record type filter** parameter from user (Matters, Projects, Invoices, or All)
- Query Azure AI Search with extracted entities, filtered by record type
- Score and rank matching Dataverse records
- Return top N suggestions with confidence scores

**Matching Algorithm**:
```
Score =
  (org_match_weight × org_match_score) +
  (person_match_weight × person_match_score) +
  (reference_match_weight × reference_match_score) +
  (keyword_match_weight × keyword_similarity)

Weights (configurable):
- Reference number exact match: 0.5 (highest - explicit link)
- Organization name match: 0.25
- Person name match: 0.15
- Keyword similarity: 0.10
```

**Record Type Filter**:
```csharp
// Filter applied to Azure AI Search query
if (recordTypeFilter != "all")
{
    searchOptions.Filter = $"recordType eq '{recordTypeFilter}'";
}
```

#### 4.2.4 Azure AI Search Index

**Index Schema: `spaarke-records-index`**:
```json
{
  "name": "spaarke-records-index",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "recordType", "type": "Edm.String", "filterable": true },
    { "name": "recordName", "type": "Edm.String", "searchable": true },
    { "name": "recordDescription", "type": "Edm.String", "searchable": true },
    { "name": "organizations", "type": "Collection(Edm.String)", "searchable": true },
    { "name": "people", "type": "Collection(Edm.String)", "searchable": true },
    { "name": "referenceNumbers", "type": "Collection(Edm.String)", "searchable": true },
    { "name": "keywords", "type": "Edm.String", "searchable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "vectorSearchProfile": "default" },
    { "name": "lastModified", "type": "Edm.DateTimeOffset", "filterable": true }
  ],
  "vectorSearch": {
    "profiles": [{ "name": "default", "algorithm": "hnsw" }]
  }
}
```

**Indexed Record Types**:
- `sprk_matter` - Legal matters, cases, projects
- `sprk_project` - Business projects
- `sprk_invoice` - Invoices and billing records
- `account` - Organizations/companies
- `contact` - People

#### 4.2.5 Dataverse Index Sync Service

**Responsibilities**:
- Initial bulk load of Dataverse records to Azure AI Search
- Incremental sync when records change
- Generate embeddings for record content

**Sync Triggers**:
1. **Plugin-based** (near real-time): Dataverse plugin posts to Azure Function queue
2. **Scheduled** (fallback): Timer-triggered sync every 15 minutes

---

## 5. Data Models

### 5.1 API Response Models

```csharp
/// <summary>
/// Structured result from AI document analysis.
/// </summary>
public class DocumentAnalysisResult
{
    /// <summary>
    /// 2-3 sentence prose summary.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Bullet-point TL;DR (3-7 items).
    /// </summary>
    public string[] TlDr { get; set; } = [];

    /// <summary>
    /// Comma-separated searchable keywords.
    /// </summary>
    public string Keywords { get; set; } = string.Empty;

    /// <summary>
    /// Extracted named entities.
    /// </summary>
    public ExtractedEntities Entities { get; set; } = new();

    /// <summary>
    /// Raw AI response (for debugging/fallback).
    /// </summary>
    public string? RawResponse { get; set; }

    /// <summary>
    /// Whether JSON parsing succeeded.
    /// </summary>
    public bool ParsedSuccessfully { get; set; }
}

/// <summary>
/// Named entities extracted from document.
/// </summary>
public class ExtractedEntities
{
    public string[] Organizations { get; set; } = [];
    public string[] People { get; set; } = [];
    public string[] Amounts { get; set; } = [];
    public string[] Dates { get; set; } = [];
    public string DocumentType { get; set; } = "other";
    public string[] References { get; set; } = [];
}

/// <summary>
/// Suggested record match from Azure AI Search.
/// </summary>
public class RecordMatchSuggestion
{
    public string RecordId { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public string RecordName { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public string[] MatchReasons { get; set; } = [];

    /// <summary>
    /// The Dataverse lookup field name to populate when this match is selected.
    /// E.g., "sprk_matter" for Matter records, "sprk_project" for Project records.
    /// </summary>
    public string LookupFieldName { get; set; } = string.Empty;
}
```

### 5.2 Dataverse Schema Changes

**Entity: `sprk_document`**

| Field | Type | Description | Indexed |
|-------|------|-------------|---------|
| `sprk_filesummary` | Multi-line Text (4000) | Prose summary from AI | No |
| `sprk_filetldr` | Multi-line Text (4000) | Newline-separated bullet points | No |
| `sprk_filekeywords` | Multi-line Text (4000) | Comma-separated search terms | **Yes** (Relevance Search) |
| `sprk_fileentities` | Multi-line Text (8000) | JSON of ExtractedEntities | No |
| `sprk_documenttype` | Choice | Document type classification | Yes |
| `sprk_filesummarydate` | DateTime | When AI analysis completed | No |
| `sprk_matter` | Lookup | **Existing** - Link to Matter record (populated via matching) | Yes |
| `sprk_project` | Lookup | **Existing** - Link to Project record (populated via matching) | Yes |
| `sprk_invoice` | Lookup | **Existing** - Link to Invoice record (populated via matching) | Yes |

**Choice Values for `sprk_documenttype`**:
- contract (100000000)
- invoice (100000001)
- proposal (100000002)
- report (100000003)
- letter (100000004)
- memo (100000005)
- email (100000006)
- agreement (100000007)
- statement (100000008)
- other (100000009)

**Record Type to Lookup Field Mapping**:
| Record Type | Lookup Field on Document |
|-------------|-------------------------|
| `sprk_matter` | `sprk_matter` |
| `sprk_project` | `sprk_project` |
| `sprk_invoice` | `sprk_invoice` |
| `account` | `sprk_account` |

### 5.3 Configuration Schema

```csharp
public class DocumentIntelligenceOptions  // Renamed from AiOptions
{
    public const string SectionName = "DocumentIntelligence";  // Renamed from "Ai"

    // ... existing options ...

    /// <summary>
    /// Enable structured JSON output with entities.
    /// </summary>
    public bool StructuredOutputEnabled { get; set; } = true;

    /// <summary>
    /// Enable record matching suggestions.
    /// </summary>
    public bool RecordMatchingEnabled { get; set; } = false;

    /// <summary>
    /// Azure AI Search endpoint for record matching.
    /// </summary>
    public string? AiSearchEndpoint { get; set; }

    /// <summary>
    /// Azure AI Search API key.
    /// </summary>
    public string? AiSearchKey { get; set; }

    /// <summary>
    /// Azure AI Search index name.
    /// </summary>
    public string AiSearchIndexName { get; set; } = "spaarke-records-index";

    /// <summary>
    /// Prompt template for structured document analysis.
    /// </summary>
    public string StructuredAnalysisPromptTemplate { get; set; } = "...";

    /// <summary>
    /// Supported email file types.
    /// </summary>
    public Dictionary<string, FileTypeConfig> EmailFileTypes { get; set; } = new()
    {
        [".eml"] = new() { Enabled = true, Method = ExtractionMethod.Email },
        [".msg"] = new() { Enabled = true, Method = ExtractionMethod.Email },
    };

    /// <summary>
    /// Maps record types to their corresponding Document lookup field names.
    /// </summary>
    public Dictionary<string, string> RecordTypeLookupMapping { get; set; } = new()
    {
        ["sprk_matter"] = "sprk_matter",
        ["sprk_project"] = "sprk_project",
        ["sprk_invoice"] = "sprk_invoice",
        ["account"] = "sprk_account",
    };
}
```

---

## 6. API Endpoints

### 6.1 Document Intelligence Endpoint (Renamed)

**Previous**: `POST /api/ai/summarize/stream`
**New**: `POST /api/ai/document-intelligence/analyze`

**Changes**: Response now includes structured data alongside streaming text.

```http
POST /api/ai/document-intelligence/analyze
Content-Type: application/json

{
  "containerId": "...",
  "fileId": "...",
  "options": {
    "includeEntities": true,
    "includeKeywords": true
  }
}

Response (SSE stream):
data: {"type": "text", "content": "Analyzing document..."}
data: {"type": "text", "content": "The document is a contract..."}
data: {"type": "complete", "result": {
  "summary": "...",
  "tldr": [...],
  "keywords": "...",
  "entities": {...}
}}
```

### 6.2 Record Match Endpoint (Phase 2)

```http
POST /api/ai/document-intelligence/match-records
Content-Type: application/json

{
  "entities": {
    "organizations": ["Acme Corp"],
    "people": ["John Smith"],
    "references": ["INV-2024-001"]
  },
  "recordTypeFilter": "sprk_matter",  // Filter: "sprk_matter", "sprk_project", "sprk_invoice", or "all"
  "maxResults": 5
}

Response:
{
  "suggestions": [
    {
      "recordId": "...",
      "recordType": "sprk_matter",
      "recordName": "Acme Corp - Contract Review",
      "confidenceScore": 0.92,
      "matchReasons": ["Organization: Acme Corp", "Reference: INV-2024-001"],
      "lookupFieldName": "sprk_matter"
    }
  ]
}
```

### 6.3 Associate Record Endpoint (Phase 2)

```http
POST /api/ai/document-intelligence/associate-record
Content-Type: application/json

{
  "documentId": "...",
  "recordId": "...",
  "recordType": "sprk_matter",
  "lookupFieldName": "sprk_matter"
}

Response:
{
  "success": true,
  "message": "Document associated with Matter 'Acme Corp - Contract Review'"
}
```

### 6.4 Index Sync Endpoint (Phase 2, Admin)

```http
POST /api/admin/document-intelligence/sync-index
Content-Type: application/json

{
  "fullSync": false,
  "recordTypes": ["sprk_matter"]
}

Response:
{
  "recordsProcessed": 150,
  "recordsIndexed": 148,
  "errors": 2,
  "duration": "00:01:23"
}
```

---

## 7. Implementation Phases

### Phase 1a: Structured AI Output + Service Rename (Week 1-2)

**Scope**: Rename services from "Summarize" to "DocumentIntelligence" and update AI service to return structured JSON.

**Tasks**:
1. **Rename services**:
   - `SummarizeService.cs` → `DocumentIntelligenceService.cs`
   - `ISummarizeService.cs` → `IDocumentIntelligenceService.cs`
   - `SummarizeEndpoints.cs` → `DocumentIntelligenceEndpoints.cs`
   - `AiOptions` → `DocumentIntelligenceOptions`
   - Update endpoint paths: `/api/ai/summarize/*` → `/api/ai/document-intelligence/*`
   - Update DI registrations and configuration section names
2. Update prompt template in `DocumentIntelligenceOptions.cs`
3. Create `DocumentAnalysisResult` and `ExtractedEntities` models
4. Update service to parse JSON response
5. Add fallback logic when JSON parsing fails
6. Update SSE streaming to include structured result at completion
7. Unit tests for JSON parsing, fallback scenarios, and renamed endpoints

**Files Modified**:
- `src/server/api/Sprk.Bff.Api/Configuration/AiOptions.cs` → `DocumentIntelligenceOptions.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/SummarizeService.cs` → `DocumentIntelligenceService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ISummarizeService.cs` → `IDocumentIntelligenceService.cs`
- `src/server/api/Sprk.Bff.Api/Models/Ai/DocumentAnalysisResult.cs` (new)
- `src/server/api/Sprk.Bff.Api/Api/Ai/SummarizeEndpoints.cs` → `DocumentIntelligenceEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Program.cs` (DI updates)

**Acceptance Criteria**:
- [ ] All services renamed from "Summarize" to "DocumentIntelligence"
- [ ] Endpoint paths updated to `/api/ai/document-intelligence/*`
- [ ] AI returns valid JSON with all required fields
- [ ] TL;DR contains 3-7 actionable bullet points
- [ ] Keywords are searchable terms (not generic words)
- [ ] Entities are extracted only when clearly present
- [ ] Fallback works when JSON is malformed

### Phase 1b: Dataverse + PCF Integration + Email Support (Week 2-3)

**Scope**: Store structured data in Dataverse, update PCF UI to display TL;DR and entities, add email file support.

**Tasks**:
1. Add Dataverse fields: `sprk_filetldr`, `sprk_fileentities`, `sprk_documenttype`
2. Enable Relevance Search indexing on `sprk_filekeywords`
3. Update `DocumentRecordService.ts` to save structured fields
4. Update PCF to call renamed endpoints (`/api/ai/document-intelligence/*`)
5. Update `AiSummaryPanel.tsx` to display TL;DR as bullet list
6. Add entity display section (collapsible)
7. Add keyword tags display
8. Add email file type support (.eml, .msg) to extraction service
9. Implement `EmailExtractorService` with **subject line included in analysis**
10. Integration tests

**Files Modified**:
- Dataverse solution: Add new fields to `sprk_document`
- `src/client/pcf/UniversalQuickCreate/control/services/DocumentRecordService.ts`
- `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts` (update endpoint URLs)
- `src/client/pcf/UniversalQuickCreate/control/components/AiSummaryPanel.tsx`
- `src/client/pcf/UniversalQuickCreate/control/components/AiSummaryCarousel.tsx`
- `src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/EmailExtractorService.cs` (new)

**Acceptance Criteria**:
- [ ] PCF uses new `/api/ai/document-intelligence/*` endpoints
- [ ] All structured fields saved to Dataverse
- [ ] PCF displays TL;DR as bullet list
- [ ] Keywords appear as tags
- [ ] Entities visible in collapsible section
- [ ] Email files (.eml, .msg) can be analyzed
- [ ] Email subject line included in AI analysis
- [ ] Dataverse Relevance Search finds documents by AI keywords

### Phase 2: Record Matching Service (Week 4-6)

**Scope**: Azure AI Search index for Dataverse records with matching API, record type filtering, and automatic lookup population.

**Tasks**:
1. Create Azure AI Search resource (if not exists)
2. Define index schema for `spaarke-records-index`
3. Implement `DataverseIndexSyncService` for bulk/incremental sync
4. Implement `RecordMatchService` with **record type filter parameter**
5. Add `/api/ai/document-intelligence/match-records` endpoint
6. Add `/api/ai/document-intelligence/associate-record` endpoint
7. **Update PCF with record type selector dropdown**
8. Update PCF to display suggested matches for all file types
9. Add one-click record association that **populates the correct lookup field**
10. Add Dataverse plugin for real-time index updates (optional)

**Files New**:
- `src/server/api/Sprk.Bff.Api/Services/DocumentIntelligence/RecordMatchService.cs`
- `src/server/api/Sprk.Bff.Api/Services/DocumentIntelligence/DataverseIndexSyncService.cs`
- `src/server/api/Sprk.Bff.Api/Services/DocumentIntelligence/IRecordMatchService.cs`
- `src/server/api/Sprk.Bff.Api/Api/DocumentIntelligence/RecordMatchEndpoints.cs`
- `src/client/pcf/UniversalQuickCreate/control/components/RecordMatchSuggestions.tsx`
- `src/client/pcf/UniversalQuickCreate/control/components/RecordTypeSelector.tsx`
- `infrastructure/bicep/ai-search.bicep`

**Acceptance Criteria**:
- [ ] Azure AI Search index contains Dataverse records
- [ ] User can select target record type (Matters/Projects/Invoices/All)
- [ ] Match API respects record type filter
- [ ] Match API returns ranked suggestions with lookup field names
- [ ] Match reasons explain why records were suggested
- [ ] PCF displays suggestions with confidence scores (for all file types, not just email)
- [ ] User can associate document with suggested record in one click
- [ ] **Selecting a match populates the correct Document lookup field** (e.g., `sprk_matter`)
- [ ] Email files get Matter suggestions based on entities (including subject line)

---

## 8. Non-Functional Requirements

### 8.1 Performance

| Metric | Target |
|--------|--------|
| AI analysis latency (p95) | < 15 seconds |
| Record match latency (p95) | < 500ms |
| Index sync throughput | 100 records/minute |

### 8.2 Reliability

- Fallback to raw summary if JSON parsing fails
- Retry logic for transient Azure OpenAI failures
- Circuit breaker for AI Search unavailability

### 8.3 Security

- Azure AI Search API key stored in Key Vault
- No PII logged from extracted entities
- Record matching respects Dataverse security roles

### 8.4 Scalability

- AI Search index supports up to 100,000 records
- Batch indexing for initial sync
- Rate limiting on match endpoint (10 req/sec/user)

---

## 9. Dependencies

### 9.1 Azure Resources

| Resource | Purpose | Status |
|----------|---------|--------|
| Azure OpenAI | AI analysis | Existing |
| Azure AI Search | Record index | **New (Phase 2)** |
| Azure Key Vault | Secret storage | Existing |

### 9.2 NuGet Packages

| Package | Purpose | Status |
|---------|---------|--------|
| Azure.Search.Documents | AI Search SDK | New |
| MimeKit | EML parsing | New |
| MsgReader | MSG parsing | New |
| HtmlAgilityPack | HTML to text | Existing |

### 9.3 Dataverse Changes

| Change | Impact |
|--------|--------|
| New fields on `sprk_document` | Solution export/import |
| New choice field values | Solution export/import |
| Relevance Search configuration | Admin Center setting |

---

## 10. Risks and Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| JSON parsing failures | Degraded UX | Medium | Robust fallback to raw text |
| Entity extraction inaccuracy | Wrong matches | Medium | Confidence thresholds, user confirmation |
| Azure AI Search costs | Budget | Low | Monitor usage, implement caching |
| Email parsing edge cases | Missing data | Medium | Extensive test coverage |
| Service rename breaking changes | Integration issues | Low | Update all clients in same release |

---

## 11. Out of Scope

- Email server-side sync implementation (handled by separate project)
- Full RAG pipeline for document Q&A
- Multi-language document support
- Historical re-analysis of existing documents (manual trigger only)
- Automatic record association (always requires user confirmation)

---

## 12. Success Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| User satisfaction with TL;DR format | >80% positive | User feedback survey |
| Search success rate improvement | +25% | A/B test with/without keywords |
| Record match acceptance rate | >60% | Click-through on suggestions |
| Time to associate document | -50% | Before/after comparison |

---

## 13. Appendix

### A. Example AI Output

**Input Document**: Contract between Acme Corp and Smith & Associates for legal services.

**AI Response**:
```json
{
  "summary": "This is a legal services agreement between Acme Corporation and Smith & Associates Law Firm, effective January 15, 2025. The contract establishes terms for ongoing legal counsel services at a rate of $450 per hour with a monthly retainer of $5,000.",
  "tldr": [
    "Legal services agreement effective January 15, 2025",
    "Acme Corp retains Smith & Associates for ongoing counsel",
    "$450/hour rate with $5,000 monthly retainer",
    "12-month initial term with auto-renewal",
    "30-day termination notice required"
  ],
  "keywords": "Acme Corporation, Smith Associates, legal services, retainer agreement, hourly rate, contract, counsel, law firm",
  "entities": {
    "organizations": ["Acme Corporation", "Smith & Associates Law Firm"],
    "people": ["John Smith", "Sarah Johnson"],
    "amounts": ["$450 per hour", "$5,000 monthly retainer", "$60,000 annual minimum"],
    "dates": ["January 15, 2025", "12-month term"],
    "documentType": "contract",
    "references": ["Matter #M-2025-0042", "Agreement No. LSA-2025-001"]
  }
}
```

### B. Example Email Analysis

**Input Email** (subject + body):
```
Subject: RE: Matter M-2025-0042 - Acme Corp Contract Review - Action Required

From: john.smith@smithlaw.com
To: sarah.johnson@acme.com
Date: December 5, 2025

Sarah,

Please review the attached contract amendments by EOD Friday. The key changes are:
- Updated payment terms (Net 30 → Net 45)
- Added indemnification clause
- Revised termination notice period

Let me know if you have questions.

Best,
John Smith
Partner, Smith & Associates
```

**AI Response**:
```json
{
  "summary": "Email from John Smith (Smith & Associates) to Sarah Johnson (Acme Corp) requesting review of contract amendments for Matter M-2025-0042 by end of day Friday, with three key changes highlighted.",
  "tldr": [
    "Action required: Review contract amendments by EOD Friday",
    "Matter reference: M-2025-0042 (Acme Corp Contract Review)",
    "Payment terms changed from Net 30 to Net 45",
    "New indemnification clause added",
    "Termination notice period revised"
  ],
  "keywords": "Acme Corporation, Smith Associates, contract review, amendments, M-2025-0042, payment terms, indemnification, termination notice",
  "entities": {
    "organizations": ["Acme Corporation", "Smith & Associates"],
    "people": ["John Smith", "Sarah Johnson"],
    "amounts": [],
    "dates": ["December 5, 2025", "EOD Friday"],
    "documentType": "email",
    "references": ["Matter M-2025-0042"]
  }
}
```

**Matching Result** (with record type filter = "sprk_matter"):
```json
{
  "suggestions": [
    {
      "recordId": "abc-123-def",
      "recordType": "sprk_matter",
      "recordName": "Acme Corp - Contract Review",
      "confidenceScore": 0.95,
      "matchReasons": [
        "Reference: M-2025-0042 (exact match)",
        "Organization: Acme Corporation",
        "Organization: Smith & Associates"
      ],
      "lookupFieldName": "sprk_matter"
    }
  ]
}
```

### C. Dataverse Relevance Search Configuration

To enable keywords for Relevance Search:

1. Navigate to Power Platform Admin Center
2. Select Environment → Settings → Features
3. Enable Dataverse Search
4. Under Searchable Fields, add `sprk_filekeywords` for `sprk_document` entity
5. Wait for index rebuild (may take several hours)

### D. Azure AI Search Index Creation Script

```bash
az search index create \
  --name spaarke-records-index \
  --service-name spaarke-search \
  --resource-group spaarke-rg \
  --fields @index-schema.json
```

### E. Service Rename Migration Checklist

| File | Old Name | New Name |
|------|----------|----------|
| Service class | `SummarizeService.cs` | `DocumentIntelligenceService.cs` |
| Interface | `ISummarizeService.cs` | `IDocumentIntelligenceService.cs` |
| Endpoints | `SummarizeEndpoints.cs` | `DocumentIntelligenceEndpoints.cs` |
| Options class | `AiOptions.cs` | `DocumentIntelligenceOptions.cs` |
| Config section | `"Ai"` | `"DocumentIntelligence"` |
| API path | `/api/ai/summarize/*` | `/api/ai/document-intelligence/*` |
| PCF hook | `useAiSummary.ts` | Update endpoint URLs |

---

*End of Specification*
