# ENH-003: Flexible Input Model (Unified)

> **Project**: AI Playbook Node Builder R2
> **Status**: Pending
> **Priority**: High
> **Effort**: 3-4 weeks total across all patterns
> **Related**: [design.md](../../projects/x-ai-playbook-node-builder-r2/design.md)

---

## Problem Statement

Currently, playbooks operate on a **single SPE-stored document**:
- API: `POST /api/ai/execute-playbook-stream { playbookId, documentId }`
- Requires Dataverse `sprk_document` record
- Requires file stored in SharePoint Embedded (SPE)

Users need more flexible input options:
1. Analyze with uploaded **knowledge files** as RAG context
2. **Compare** two documents side-by-side
3. Analyze **consolidated** multiple documents together
4. Analyze **ad-hoc files** not yet stored in SPE

---

## NOT IN SCOPE

> **Complex N:N Batch Processing** - Running the same playbook N times on N documents
> with parallel execution, progress tracking, and result aggregation is explicitly
> OUT OF SCOPE. This would require BatchOrchestrationService, job queuing, and
> significant infrastructure. Instead, we use a simpler "merge and analyze once"
> approach for multi-document scenarios.

---

## Supported Input Patterns

| Pattern | Subject | Knowledge | Use Case |
|---------|---------|-----------|----------|
| **A: Subject + Knowledge** | 1 document (SPE) | User uploads (RAG) | "Analyze lease using our standards" |
| **B: Document Comparison** | 2 documents | Optional | "Compare Vendor A vs Vendor B" |
| **C: Consolidated Analysis** | N documents (merged) | Optional | "Analyze all 5 portfolio leases" |
| **D: Ad-Hoc File** | Uploaded file | Optional | "Quick analysis before storing" |

---

## Pattern A: Subject Document + Knowledge Files (RAG-Enhanced)

**Description**: Analyze one subject document with uploaded knowledge files providing RAG context.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    SUBJECT + KNOWLEDGE PATTERN                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────┐         ┌─────────────────────────────────────┐   │
│  │   SUBJECT DOCUMENT  │         │      KNOWLEDGE FILES (uploaded)     │   │
│  │   (Required, SPE)   │         │                                     │   │
│  │                     │         │  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐   │   │
│  │  "Analyze THIS"     │         │  │Std  │ │Policy│ │Prior│ │Bench│   │   │
│  │                     │         │  │Terms│ │Doc  │ │Lease│ │marks│   │   │
│  └──────────┬──────────┘         │  └──┬──┘ └──┬──┘ └──┬──┘ └──┬──┘   │   │
│             │                     │     │      │      │      │       │   │
│             │                     │     └──────┴──────┴──────┘       │   │
│             │                     │              │                    │   │
│             │                     │              ▼                    │   │
│             │                     │     ┌───────────────┐            │   │
│             │                     │     │   Chunk &     │            │   │
│             │                     │     │   Embed       │            │   │
│             │                     │     └───────┬───────┘            │   │
│             │                     │             │                     │   │
│             │                     │             ▼                     │   │
│             │                     │     ┌───────────────┐            │   │
│             │                     │     │  Session      │            │   │
│             │                     │     │  Vector Store │            │   │
│             │                     │     └───────────────┘            │   │
│             │                     └─────────────┬───────────────────┘   │
│             │                                   │                        │
│             │          ┌────────────────────────┘                        │
│             │          │                                                 │
│             ▼          ▼                                                 │
│      ┌─────────────────────────┐                                        │
│      │       PLAYBOOK          │                                        │
│      │                         │                                        │
│      │  Subject: Full text     │                                        │
│      │  Knowledge: RAG query   │ ← "What are standard deposit terms?"   │
│      │           → Retrieved   │ ← Returns relevant chunks              │
│      │             chunks      │                                        │
│      └─────────────────────────┘                                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key Benefit**: LLM focuses on subject document while RAG retrieves relevant context from knowledge files. Scales better than merging all text (only relevant chunks retrieved).

**Use Cases**:
- Analyze lease against company standard terms
- Review contract using policy documents as reference
- Compare document against industry benchmarks

---

## Pattern B: Document Comparison

**Description**: Compare two (or more) subject documents side-by-side.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DOCUMENT COMPARISON PATTERN                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────┐         ┌─────────────────────┐                   │
│  │    DOCUMENT A       │         │    DOCUMENT B       │                   │
│  │    (Subject 1)      │         │    (Subject 2)      │                   │
│  │                     │         │                     │                   │
│  │  "Vendor A Lease"   │   VS    │  "Vendor B Lease"   │                   │
│  │                     │         │                     │                   │
│  └──────────┬──────────┘         └──────────┬──────────┘                   │
│             │                               │                               │
│             └───────────────┬───────────────┘                               │
│                             │                                               │
│                             ▼                                               │
│             ┌───────────────────────────────────┐                           │
│             │  Merged Text with Document Labels │                           │
│             │                                   │                           │
│             │  ══════════════════════════════   │                           │
│             │  DOCUMENT A: Vendor A Lease       │                           │
│             │  ══════════════════════════════   │                           │
│             │  [Full text of Document A]        │                           │
│             │                                   │                           │
│             │  ══════════════════════════════   │                           │
│             │  DOCUMENT B: Vendor B Lease       │                           │
│             │  ══════════════════════════════   │                           │
│             │  [Full text of Document B]        │                           │
│             └───────────────┬───────────────────┘                           │
│                             │                                               │
│                             ▼                                               │
│                   ┌─────────────────┐                                      │
│                   │    PLAYBOOK     │  ← Comparison-aware prompts          │
│                   └────────┬────────┘                                      │
│                            │                                                │
│                            ▼                                                │
│                   ┌─────────────────┐                                      │
│                   │  COMPARISON     │                                      │
│                   │  REPORT         │                                      │
│                   │  - Side by side │                                      │
│                   │  - Differences  │                                      │
│                   │  - Recommenda-  │                                      │
│                   │    tions        │                                      │
│                   └─────────────────┘                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key Benefit**: LLM sees both documents in full context, can directly compare provisions.

**Use Cases**:
- Compare vendor proposals
- Review lease amendment against original
- Evaluate competing contract options

---

## Pattern C: Consolidated Multi-Document Analysis

**Description**: Analyze multiple documents together as a single consolidated input.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    CONSOLIDATED ANALYSIS PATTERN                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌───────┐  ┌───────┐  ┌───────┐  ┌───────┐  ┌───────┐                   │
│   │ Doc 1 │  │ Doc 2 │  │ Doc 3 │  │ Doc 4 │  │ Doc 5 │                   │
│   └───┬───┘  └───┬───┘  └───┬───┘  └───┬───┘  └───┬───┘                   │
│       │          │          │          │          │                         │
│       └──────────┴──────────┴──────────┴──────────┘                         │
│                             │                                               │
│                             ▼                                               │
│             ┌───────────────────────────────────┐                           │
│             │     Text Extraction (parallel)    │                           │
│             └───────────────┬───────────────────┘                           │
│                             │                                               │
│                             ▼                                               │
│             ┌───────────────────────────────────┐                           │
│             │          MERGE TEXT               │                           │
│             │  ═══════════════════════════════  │                           │
│             │  DOCUMENT 1: Lease_Property_A     │                           │
│             │  ═══════════════════════════════  │                           │
│             │  [Text...]                        │                           │
│             │                                   │                           │
│             │  ═══════════════════════════════  │                           │
│             │  DOCUMENT 2: Lease_Property_B     │                           │
│             │  ═══════════════════════════════  │                           │
│             │  [Text...]                        │                           │
│             │  ... (repeated for each doc)      │                           │
│             └───────────────┬───────────────────┘                           │
│                             │                                               │
│                             ▼                                               │
│                   ┌─────────────────┐                                      │
│                   │    PLAYBOOK     │  ← Single execution                  │
│                   │   (runs ONCE)   │  ← LLM sees all docs                 │
│                   └────────┬────────┘                                      │
│                            │                                                │
│                            ▼                                                │
│                   ┌─────────────────┐                                      │
│                   │  CROSS-DOCUMENT │                                      │
│                   │     ANALYSIS    │                                      │
│                   │  - Portfolio    │                                      │
│                   │    summary      │                                      │
│                   │  - Common       │                                      │
│                   │    issues       │                                      │
│                   │  - Totals       │                                      │
│                   └─────────────────┘                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key Benefit**: Simple implementation (merge + run once), no batch orchestration needed.

**Limitations**:
- Context window limits (~300 pages with GPT-4o 128K)
- Set reasonable max document count (e.g., 10 documents)

**Use Cases**:
- Portfolio analysis (all leases for a property group)
- Due diligence document review
- Compliance audit across document set

---

## Pattern D: Ad-Hoc File Analysis

**Description**: Analyze uploaded files not yet stored in SPE/Dataverse.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       AD-HOC FILE ANALYSIS                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│        ┌─────────────┐                                                     │
│        │   Browser   │                                                     │
│        │  File Input │                                                     │
│        └──────┬──────┘                                                     │
│               │ Upload                                                      │
│               ▼                                                             │
│        ┌─────────────────────────────────────────┐                         │
│        │     POST /api/ai/analyze               │                         │
│        │     Content-Type: multipart/form-data   │                         │
│        │     - subjectFile: <binary>             │                         │
│        │     - playbookId: "PB-LEASE-001"        │                         │
│        │     - knowledgeFiles: [<binary>, ...]   │  (optional)            │
│        └─────────────────────────────────────────┘                         │
│               │                                                             │
│               ▼                                                             │
│        ┌─────────────────────────────────────────┐                         │
│        │         BFF API                         │                         │
│        │  1. Store in temp blob (24hr TTL)       │                         │
│        │  2. Extract text                        │                         │
│        │  3. Process knowledge files (if any)    │                         │
│        │  4. Execute playbook                    │                         │
│        │  5. Return results                      │                         │
│        └─────────────────────────────────────────┘                         │
│               │                                                             │
│               ▼                                                             │
│        ┌─────────────────────────────────────────┐                         │
│        │         Response (SSE Stream)           │                         │
│        │  - Analysis results                     │                         │
│        │  - tempFileId (for optional save)       │                         │
│        │  - reportDownloadUrl (24hr TTL)         │                         │
│        └─────────────────────────────────────────┘                         │
│               │                                                             │
│               ▼                                                             │
│        ┌─────────────────────────────────────────┐                         │
│        │  User Options:                          │                         │
│        │  • Download report                      │                         │
│        │  • Save file to SPE + create Document   │                         │
│        │  • Discard (auto-cleanup after 24hr)    │                         │
│        └─────────────────────────────────────────┘                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key Benefit**: Quick analysis without committing to storage. Great for previews and demos.

**Use Cases**:
- Preview analysis before uploading
- Analyze email attachments
- Demo/trial for prospects
- One-off analysis

---

## Unified API Design

**Single Endpoint for All Patterns:**

```
POST /api/ai/execute-playbook-stream
Content-Type: multipart/form-data (if files) or application/json (if IDs only)

Request Body:
{
  "playbookId": "PB-LEASE-001",
  "mode": "single" | "withKnowledge" | "comparison" | "consolidated",

  // Subject document(s) - at least one required
  "subjectDocumentId": "guid",              // Single SPE document
  "subjectDocumentIds": ["guid", "guid"],   // Multiple SPE documents
  "subjectFile": <multipart binary>,        // Ad-hoc upload

  // Knowledge files - optional, for RAG context
  "knowledgeFiles": [<multipart binary>, ...],
  "knowledgeDocumentIds": ["guid", ...],    // Existing SPE docs as knowledge

  // Options
  "options": {
    "generateReport": true,
    "reportFormat": "pdf" | "docx",
    "saveKnowledgeFiles": false             // Persist for reuse
  }
}
```

**Mode Behaviors:**

| Mode | Subject Input | Knowledge | Behavior |
|------|--------------|-----------|----------|
| `single` | 1 document | Playbook-configured only | Standard single-doc analysis |
| `withKnowledge` | 1 document | User-provided (RAG) | Subject analyzed with RAG context |
| `comparison` | 2+ documents | Optional | Documents compared side-by-side |
| `consolidated` | 2+ documents | Optional | Documents merged, analyzed as one |

---

## Unified Input Model (C#)

```csharp
public class PlaybookExecutionInput
{
    // === REQUIRED ===
    public Guid PlaybookId { get; set; }
    public AnalysisMode Mode { get; set; } = AnalysisMode.Single;

    // === SUBJECT DOCUMENT(S) ===

    /// Single SPE document (existing)
    public Guid? SubjectDocumentId { get; set; }

    /// Multiple SPE documents (comparison or consolidated)
    public Guid[]? SubjectDocumentIds { get; set; }

    /// Ad-hoc uploaded file (not in SPE)
    public UploadedFile? SubjectFile { get; set; }

    // === KNOWLEDGE FILES (RAG Context) ===

    /// User-uploaded knowledge files (session-scoped)
    public UploadedFile[]? KnowledgeFiles { get; set; }

    /// Existing SPE documents to use as knowledge
    public Guid[]? KnowledgeDocumentIds { get; set; }

    // === OPTIONS ===
    public AnalysisOptions Options { get; set; } = new();
}

public enum AnalysisMode
{
    Single,           // One subject, standard analysis
    WithKnowledge,    // One subject + uploaded knowledge (RAG)
    Comparison,       // Two+ subjects, compare them
    Consolidated      // Multiple subjects, analyze as one
}

public class UploadedFile
{
    public byte[] Content { get; set; }
    public string FileName { get; set; }
    public string ContentType { get; set; }
}

public class AnalysisOptions
{
    public bool GenerateReport { get; set; } = true;
    public string ReportFormat { get; set; } = "pdf";
    public bool SaveKnowledgeFiles { get; set; } = false;
}
```

---

## Knowledge File Processing

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    KNOWLEDGE FILE PROCESSING                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User uploads knowledge files at execution time                            │
│                                                                             │
│       ┌──────────────────────────────────────────────────────────┐         │
│       │  "Company_Standard_Lease_Terms.pdf"                      │         │
│       │  "Industry_Benchmarks_2026.pdf"                          │         │
│       │  "Previous_Approved_Lease.docx"                          │         │
│       └──────────────────────────────────────────────────────────┘         │
│                              │                                              │
│                              ▼                                              │
│       ┌──────────────────────────────────────────────────────────┐         │
│       │  1. Extract text from each file                          │         │
│       │  2. Chunk into ~500 token segments                       │         │
│       │  3. Generate embeddings (text-embedding-ada-002)         │         │
│       │  4. Store in session vector index                        │         │
│       └──────────────────────────────────────────────────────────┘         │
│                              │                                              │
│                              ▼                                              │
│       ┌──────────────────────────────────────────────────────────┐         │
│       │  Session Vector Store (Azure AI Search or in-memory)     │         │
│       │                                                          │         │
│       │  Chunks indexed by session ID:                           │         │
│       │  [0] "Security deposit shall not exceed 2 months..."    │         │
│       │  [1] "Standard escalation is CPI capped at 2.5%..."     │         │
│       │  [2] "Industry average TI allowance is $45/RSF..."      │         │
│       └──────────────────────────────────────────────────────────┘         │
│                              │                                              │
│                              │  During node execution...                   │
│                              ▼                                              │
│       ┌──────────────────────────────────────────────────────────┐         │
│       │  Compliance Analysis Node:                               │         │
│       │                                                          │         │
│       │  Prompt includes: "Use the provided knowledge context"  │         │
│       │                                                          │         │
│       │  RAG Query: "What is the standard security deposit?"    │         │
│       │        │                                                 │         │
│       │        ▼                                                 │         │
│       │  Retrieved: "Security deposit shall not exceed 2        │         │
│       │             months base rent per Company Policy..."     │         │
│       │        │                                                 │         │
│       │        ▼  Injected into prompt context                  │         │
│       │                                                          │         │
│       │  LLM compares subject lease against retrieved standard  │         │
│       └──────────────────────────────────────────────────────────┘         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## UI Concept: Run Analysis Dialog

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         RUN ANALYSIS                                   [X] │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ANALYSIS MODE                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │ ○ Single Document Analysis                                          │  │
│  │ ● Analyze with Knowledge Files (RAG)                                │  │
│  │ ○ Compare Documents                                                 │  │
│  │ ○ Consolidated Analysis (Multiple Documents)                        │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│  SUBJECT DOCUMENT (Required)                                               │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  📄 Lease_AcmeCorp_123Main.pdf                              [Remove]│  │
│  │     Source: Documents > Leases > 2026                               │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│  [Select from Library...]  [Upload File...]                               │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│  KNOWLEDGE FILES (Context for analysis via RAG)                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  📄 Company_Standard_Terms.pdf                              [Remove]│  │
│  │  📄 Industry_Benchmarks_2026.xlsx                           [Remove]│  │
│  │  📄 Approved_Lease_Template.docx                            [Remove]│  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│  [+ Upload Knowledge File...]  [+ Select from Knowledge Library...]        │
│                                                                             │
│  ☐ Save knowledge files to library for future analyses                    │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│  OUTPUT OPTIONS                                                            │
│  ☑ Generate PDF report                                                    │
│  ☑ Generate Word document                                                 │
│  ☐ Send email when complete                                               │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│                              [Cancel]              [▶ Run Analysis]        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Output Handling by Input Type

| Input Type | Analysis Output | Document Fields | Generated Report |
|------------|-----------------|-----------------|------------------|
| SPE Document | `sprk_analysisoutput` record | Updated on `sprk_document` | Saved to SPE |
| Ad-hoc File | Returned in response | N/A | Temp URL (24hr) |
| Multiple SPE | `sprk_analysisoutput` per? | TBD | Single consolidated report |
| Comparison | `sprk_analysisoutput` | N/A (comparison) | Comparison report |

---

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Unauthorized file analysis | Require authentication, rate limiting |
| Large file uploads | Max file size (50MB), max 10 documents |
| Temp storage abuse | 24hr TTL, per-user quotas |
| Sensitive data in temp | Encryption at rest, secure delete |
| Cost control | Track token usage, enforce limits |
| Context overflow | Validate total text size before execution |

---

## Implementation Tasks

**Phase 1: Core Infrastructure (1-2 weeks)**
- [ ] Create `PlaybookExecutionInput` unified model
- [ ] Refactor orchestration to accept unified input
- [ ] Add text merge utility for multi-document
- [ ] Add temp file storage service (Azure Blob)
- [ ] Add file upload endpoint

**Phase 2: Knowledge Files / RAG (1-2 weeks)**
- [ ] Add session-scoped vector store
- [ ] Implement chunk & embed pipeline
- [ ] Integrate RAG retrieval into node execution
- [ ] Add knowledge file persistence option

**Phase 3: UI (1 week)**
- [ ] Create "Run Analysis" dialog component
- [ ] Add mode selection
- [ ] Add file upload with drag-drop
- [ ] Add knowledge file management

**Phase 4: Cleanup & Polish**
- [ ] Temp file cleanup job
- [ ] Rate limiting
- [ ] Documentation
- [ ] Testing

---

## Effort Estimate

| Component | Effort |
|-----------|--------|
| Unified input model | 2-3 days |
| Multi-doc text merge | 1-2 days |
| Ad-hoc file handling | 3-4 days |
| Knowledge file RAG | 5-7 days |
| UI dialog | 3-4 days |
| Testing & polish | 3-4 days |
| **Total** | **3-4 weeks** |

---

## Revision History

| Date | Changes |
|------|---------|
| 2026-01-16 | Initial design (extracted from design.md) |
