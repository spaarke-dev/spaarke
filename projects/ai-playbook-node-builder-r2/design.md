# AI Playbook Node Builder R2 - Design Document

> **Project**: AI Playbook Node Builder R2
> **Status**: Design
> **Created**: January 16, 2026
> **Last Updated**: January 16, 2026 (ENH-005: test execution architecture)

---

## Overview

This project enhances the Playbook Builder PCF control and the underlying orchestration service to support more sophisticated playbook design and execution capabilities.

---

## Architecture Decisions

### Edge/Connector Behavior

**Decision**: Edges define **execution order only**, not data access restrictions.

| Aspect | Behavior |
|--------|----------|
| Edges mean | "Run this node after connected node completes" |
| Data access | Open - any node can read any previous output by variable name |
| Visual purpose | Shows flow/sequence, not data pipes |

**Rationale**: Simpler mental model for users. All outputs accumulate in a shared dictionary (`nodeOutputs`) accessible to all subsequent nodes.

```
┌────────┐      ┌────────┐      ┌────────┐
│ Node A │─────▶│ Node B │─────▶│ Node C │
└────────┘      └────────┘      └────────┘
    │               │               │
    ▼               ▼               ▼
    ═══════════════════════════════════
           nodeOutputs Dictionary
      (all outputs available to all nodes)
    ═══════════════════════════════════
```

---

## Required Enhancements

### ENH-001: Assemble Output Node Type

**Priority**: High
**Status**: Pending

#### Problem Statement

Currently, the `Deliver Output` node must handle both:
1. Consolidating outputs from previous nodes
2. Generating and delivering documents (Word, PDF, Email)

This conflates two distinct responsibilities and limits flexibility.

#### Proposed Solution

Add a new **Assemble Output** node type that provides explicit control over output consolidation before delivery.

#### Use Cases

| Use Case | Description |
|----------|-------------|
| **Selective inclusion** | Include only specific outputs in final report (not all) |
| **Transformation** | Restructure/format outputs before delivery |
| **Template mapping** | Explicitly map outputs to template sections |
| **Validation** | Check completeness/quality before document generation |
| **Aggregation** | Compute cross-output metrics (e.g., overall risk score from multiple analyses) |
| **Conditional assembly** | Include/exclude sections based on conditions |

#### Node Configuration

**Properties Panel:**

```
┌─────────────────────────────────────────────────────────────┐
│              Assemble Output - Properties                   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Label:           [Compile Lease Analysis      ]            │
│                                                             │
│  Output Variable: [compiledReport              ]            │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  SECTION MAPPING                                            │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  Template:        [TPL-LEASE-SUMMARY-001      ▼]            │
│                                                             │
│  Section Mappings:                                          │
│  ┌─────────────────┬──────────────────┬──────────────────┐ │
│  │ Template Section│ Source Variable  │ Transform        │ │
│  ├─────────────────┼──────────────────┼──────────────────┤ │
│  │ tldr            │ tldrSummary      │ none             │ │
│  │ compliance      │ complianceAnal...│ none             │ │
│  │ parties         │ partiesExtract...│ none             │ │
│  │ financial       │ financialTerms   │ formatCurrency   │ │
│  │ riskSummary     │ [computed]       │ aggregateRisk    │ │
│  └─────────────────┴──────────────────┴──────────────────┘ │
│  [+ Add Section Mapping]                                    │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  VALIDATION                                                 │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  ☑ Require all mapped sections                             │
│  ☑ Fail if any source is missing                           │
│  ☐ Allow partial output                                     │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  COMPUTED FIELDS                                            │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  ┌─────────────────┬────────────────────────────────────┐  │
│  │ Field Name      │ Expression                         │  │
│  ├─────────────────┼────────────────────────────────────┤  │
│  │ overallRiskScore│ max(compliance.provisions[].risk)  │  │
│  │ totalIssues     │ count(compliance.provisions)       │  │
│  └─────────────────┴────────────────────────────────────┘  │
│  [+ Add Computed Field]                                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

#### Canvas JSON Schema

```json
{
  "id": "node_assemble_011",
  "type": "assembleOutput",
  "position": { "x": 400, "y": 1250 },
  "data": {
    "label": "Compile Lease Analysis",
    "type": "assembleOutput",
    "outputVariable": "compiledReport",
    "templateId": "TPL-LEASE-SUMMARY-001",
    "sectionMappings": [
      {
        "templateSection": "tldr",
        "sourceVariable": "tldrSummary",
        "transform": null
      },
      {
        "templateSection": "compliance",
        "sourceVariable": "complianceAnalysis",
        "transform": null
      },
      {
        "templateSection": "riskSummary",
        "sourceVariable": null,
        "computed": true,
        "expression": "aggregateRisk(complianceAnalysis, liabilityAnalysis)"
      }
    ],
    "computedFields": [
      {
        "name": "overallRiskScore",
        "expression": "max(complianceAnalysis.provisions[*].variancePercentage)"
      },
      {
        "name": "totalRedFlags",
        "expression": "count(complianceAnalysis.provisions[?riskCoefficient=='Red'])"
      }
    ],
    "validation": {
      "requireAllMappings": true,
      "failOnMissing": true,
      "allowPartial": false
    }
  }
}
```

#### Orchestration Handler

```csharp
// AssembleOutputHandler.cs (new file)

private async Task<NodeResult> ExecuteAssembleNodeAsync(
    PlaybookNode node,
    Dictionary<string, JsonElement> allOutputs)
{
    var config = node.Data;
    var assembledSections = new Dictionary<string, object>();

    // 1. Validate required inputs exist
    if (config.Validation.RequireAllMappings)
    {
        foreach (var mapping in config.SectionMappings.Where(m => !m.Computed))
        {
            if (!allOutputs.ContainsKey(mapping.SourceVariable))
            {
                if (config.Validation.FailOnMissing)
                    throw new PlaybookExecutionException($"Missing required output: {mapping.SourceVariable}");
            }
        }
    }

    // 2. Map sections from source variables
    foreach (var mapping in config.SectionMappings)
    {
        if (mapping.Computed)
        {
            // Evaluate computed expression
            var value = EvaluateExpression(mapping.Expression, allOutputs);
            assembledSections[mapping.TemplateSection] = value;
        }
        else
        {
            var sourceData = allOutputs[mapping.SourceVariable];

            // Apply transform if specified
            if (!string.IsNullOrEmpty(mapping.Transform))
            {
                sourceData = ApplyTransform(sourceData, mapping.Transform);
            }

            assembledSections[mapping.TemplateSection] = sourceData;
        }
    }

    // 3. Calculate computed fields
    var computedFields = new Dictionary<string, object>();
    foreach (var field in config.ComputedFields)
    {
        computedFields[field.Name] = EvaluateExpression(field.Expression, allOutputs);
    }

    // 4. Build final compiled report
    var compiledReport = new
    {
        metadata = new
        {
            reportId = Guid.NewGuid(),
            generatedAt = DateTime.UtcNow,
            templateId = config.TemplateId,
            playbookId = _currentPlaybookId
        },
        sections = assembledSections,
        computed = computedFields
    };

    return new NodeResult
    {
        NodeId = node.Id,
        OutputVariable = config.OutputVariable,
        Data = JsonSerializer.SerializeToElement(compiledReport)
    };
}
```

#### UI Component (PCF)

**New files needed:**

```
src/client/pcf/PlaybookBuilderHost/control/
├── components/
│   ├── Nodes/
│   │   └── AssembleOutputNode.tsx    ← New node visual
│   └── Properties/
│       └── AssembleOutputProperties.tsx  ← New properties panel
```

#### Implementation Tasks

- [ ] Add `assembleOutput` to node type enum
- [ ] Create `AssembleOutputNode.tsx` component
- [ ] Create `AssembleOutputProperties.tsx` panel
- [ ] Add node to palette with icon
- [ ] Update canvas store to handle new node type
- [ ] Add orchestration handler in C#
- [ ] Add expression evaluator for computed fields
- [ ] Add transform functions (formatCurrency, aggregateRisk, etc.)
- [ ] Update canvas JSON schema validation
- [ ] Write unit tests
- [ ] Update documentation

---

### ENH-002: Start Node (Implicit)

**Priority**: Medium
**Status**: Pending

#### Description

Consider whether playbooks need an explicit "Start" node or if execution begins implicitly from nodes with no incoming edges.

**Current behavior**: Unclear - needs investigation.

**Options**:
- A) Explicit Start node required (user drags it onto canvas)
- B) Implicit start - nodes with no incoming edges execute first
- C) Document trigger defines start (e.g., "on document upload")

---

### ENH-003: Flexible Input Model (Unified)

**Priority**: High
**Status**: Pending
**Effort**: Medium (3-4 weeks total across all patterns)

#### Problem Statement

Currently, playbooks operate on a **single SPE-stored document**:
- API: `POST /api/ai/execute-playbook-stream { playbookId, documentId }`
- Requires Dataverse `sprk_document` record
- Requires file stored in SharePoint Embedded (SPE)

Users need more flexible input options:
1. Analyze with uploaded **knowledge files** as RAG context
2. **Compare** two documents side-by-side
3. Analyze **consolidated** multiple documents together
4. Analyze **ad-hoc files** not yet stored in SPE

#### NOT IN SCOPE

> **Complex N:N Batch Processing** - Running the same playbook N times on N documents
> with parallel execution, progress tracking, and result aggregation is explicitly
> OUT OF SCOPE. This would require BatchOrchestrationService, job queuing, and
> significant infrastructure. Instead, we use a simpler "merge and analyze once"
> approach for multi-document scenarios.

#### Supported Input Patterns

| Pattern | Subject | Knowledge | Use Case |
|---------|---------|-----------|----------|
| **A: Subject + Knowledge** | 1 document (SPE) | User uploads (RAG) | "Analyze lease using our standards" |
| **B: Document Comparison** | 2 documents | Optional | "Compare Vendor A vs Vendor B" |
| **C: Consolidated Analysis** | N documents (merged) | Optional | "Analyze all 5 portfolio leases" |
| **D: Ad-Hoc File** | Uploaded file | Optional | "Quick analysis before storing" |

---

#### Pattern A: Subject Document + Knowledge Files (RAG-Enhanced)

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

#### Pattern B: Document Comparison

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

#### Pattern C: Consolidated Multi-Document Analysis

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

#### Pattern D: Ad-Hoc File Analysis

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

#### Unified API Design

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

#### Unified Input Model (C#)

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

#### Knowledge File Processing

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

#### UI Concept: Run Analysis Dialog

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

#### Output Handling by Input Type

| Input Type | Analysis Output | Document Fields | Generated Report |
|------------|-----------------|-----------------|------------------|
| SPE Document | `sprk_analysisoutput` record | Updated on `sprk_document` | Saved to SPE |
| Ad-hoc File | Returned in response | N/A | Temp URL (24hr) |
| Multiple SPE | `sprk_analysisoutput` per? | TBD | Single consolidated report |
| Comparison | `sprk_analysisoutput` | N/A (comparison) | Comparison report |

---

#### Security Considerations

| Concern | Mitigation |
|---------|------------|
| Unauthorized file analysis | Require authentication, rate limiting |
| Large file uploads | Max file size (50MB), max 10 documents |
| Temp storage abuse | 24hr TTL, per-user quotas |
| Sensitive data in temp | Encryption at rest, secure delete |
| Cost control | Track token usage, enforce limits |
| Context overflow | Validate total text size before execution |

---

#### Implementation Tasks

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

#### Effort Estimate (Revised)

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

### ENH-004: Parallel Execution Visualization

**Priority**: Low
**Status**: Pending

#### Description

Visual indication in the builder when nodes will execute in parallel vs. sequential.

**Idea**: Nodes at same "level" (no dependencies between them) highlighted as parallel group.

---

### ENH-005: AI-Assisted Playbook Builder

**Priority**: High
**Status**: Design
**Effort**: 3-4 weeks

#### Problem Statement

Building playbooks visually is powerful but requires users to:
1. Understand the node types and their purposes
2. Know how to configure Actions, Skills, Knowledge, and Tools
3. Manually create and link scope records in Dataverse
4. Arrange nodes and connect edges appropriately

Users would benefit from **conversational AI assistance** to build playbooks through natural language while seeing results update in real-time on the visual canvas.

#### Architecture Decision: NOT M365 Copilot

**Decision**: Build a custom AI assistant embedded in the PlaybookBuilderHost PCF, NOT an M365 Copilot plugin.

**Rationale**:

| Factor | M365 Copilot | Embedded Modal (Chosen) |
|--------|--------------|------------------------|
| Canvas integration | Indirect (API calls, page refresh) | Direct state access |
| Real-time updates | Requires polling/refresh | Immediate via Zustand |
| User experience | Context switch to Copilot | Side-by-side with canvas |
| Deployment flexibility | Power Platform only | Any React host |
| Development control | Microsoft's UX constraints | Full control |
| Authentication | AAD through Copilot | Existing PCF auth context |

**M365 Copilot Strategy**:
> M365 Copilot should be reserved for **Spaarke-wide AI capabilities** directly tied to Power Apps/Dataverse platform features (e.g., "Show my recent documents", "What matters need attention?"). Feature-specific AI like the Playbook Builder should use tightly-integrated custom implementations.

#### Proposed Solution

**Floating AI Modal** within the PlaybookBuilderHost PCF that:
1. Accepts natural language instructions
2. Generates/modifies canvas JSON in real-time
3. Creates Dataverse scope records (Actions, Skills, Knowledge, Tools)
4. Links scopes to playbook via N:N tables
5. Shows conversational history and explanations

#### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PlaybookBuilderHost PCF                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌────────────────────────────────────┐  ┌────────────────────────────────┐ │
│  │          CANVAS AREA               │  │      AI ASSISTANT MODAL        │ │
│  │                                    │  │      (Floating/Resizable)      │ │
│  │  ┌──────┐      ┌──────┐           │  │                                │ │
│  │  │ Node │─────▶│ Node │           │  │  ┌────────────────────────┐   │ │
│  │  └──────┘      └──────┘           │  │  │ Chat History           │   │ │
│  │      │                             │  │  │                        │   │ │
│  │      ▼                             │  │  │ User: Create a lease   │   │ │
│  │  ┌──────┐                          │  │  │       analysis playbook│   │ │
│  │  │ Node │                          │  │  │                        │   │ │
│  │  └──────┘                          │  │  │ AI: I'll create 5      │   │ │
│  │                                    │  │  │     nodes for lease... │   │ │
│  │                                    │  │  │                        │   │ │
│  │                                    │  │  └────────────────────────┘   │ │
│  │                                    │  │                                │ │
│  │                                    │  │  ┌────────────────────────┐   │ │
│  │                                    │  │  │ [Type a message...]    │   │ │
│  │                                    │  │  │                   [▶]  │   │ │
│  │                                    │  │  └────────────────────────┘   │ │
│  └────────────────────────────────────┘  └────────────────────────────────┘ │
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────────┤
│  │                        PROPERTIES PANEL                                   │
│  │  [Selected node configuration...]                                         │
│  └──────────────────────────────────────────────────────────────────────────┘
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### AI Agent Capabilities

The AI assistant can interact with:

**1. Canvas State (via Zustand store)**
- Add/remove/modify nodes
- Create/delete edges
- Update node configurations
- Rearrange layout

**2. Dataverse Entity Tables**
- `sprk_aianalysisplaybook` (main playbook record)
- `sprk_aianalysisaction` (Action system prompts)
- `sprk_aianalysisskill` (Skill prompt fragments)
- `sprk_aianalysisknowledge` (Knowledge RAG sources)
- `sprk_aianalysistool` (Tool handlers)
- `sprk_aianalysisoutput` (Output field mappings)

**3. N:N Link Tables**
- `sprk_aianalysisplaybook_action`
- `sprk_aianalysisplaybook_skill`
- `sprk_aianalysisplaybook_knowledge`
- `sprk_aianalysisplaybook_tool`
- `sprk_aianalysisplaybook_output`

#### Data Flow

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          AI ASSISTANT DATA FLOW                             │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User Message: "Add a compliance analysis node that checks lease terms     │
│                 against our standard terms document"                        │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  POST /api/ai/build-playbook-canvas (SSE Stream)                     │  │
│  │                                                                       │  │
│  │  Request:                                                             │  │
│  │  {                                                                    │  │
│  │    "playbookId": "guid",                                              │  │
│  │    "currentCanvas": { nodes: [...], edges: [...] },                   │  │
│  │    "message": "Add a compliance analysis node...",                    │  │
│  │    "conversationHistory": [...]                                       │  │
│  │  }                                                                    │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  BFF API - AiPlaybookBuilderService                                  │  │
│  │                                                                       │  │
│  │  1. Analyze request + current canvas state                           │  │
│  │  2. Determine required operations:                                    │  │
│  │     - Canvas changes (nodes, edges)                                   │  │
│  │     - New scope records needed                                        │  │
│  │     - N:N links to create                                             │  │
│  │  3. Generate operations via LLM                                       │  │
│  │  4. Execute Dataverse operations (create records, links)             │  │
│  │  5. Stream canvas patch + explanation                                 │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  SSE Response Stream:                                                 │  │
│  │                                                                       │  │
│  │  event: thinking                                                      │  │
│  │  data: {"message": "Analyzing your request..."}                      │  │
│  │                                                                       │  │
│  │  event: dataverse_operation                                           │  │
│  │  data: {"operation": "create", "entity": "sprk_aianalysisaction",    │  │
│  │         "record": {...}, "id": "new-action-guid"}                    │  │
│  │                                                                       │  │
│  │  event: canvas_patch                                                  │  │
│  │  data: {"addNodes": [...], "addEdges": [...]}                        │  │
│  │                                                                       │  │
│  │  event: message                                                       │  │
│  │  data: {"content": "I've added a Compliance Analysis node..."}       │  │
│  │                                                                       │  │
│  │  event: done                                                          │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  PCF Client Processing                                                │  │
│  │                                                                       │  │
│  │  1. aiAssistantStore receives stream events                          │  │
│  │  2. On canvas_patch: Apply to canvasStore (nodes/edges update)       │  │
│  │  3. On message: Append to chat history                               │  │
│  │  4. React Flow re-renders with new nodes                             │  │
│  │  5. User sees changes in real-time                                   │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└────────────────────────────────────────────────────────────────────────────┘
```

#### PCF Component Architecture

**New Stores (Zustand):**

```typescript
// aiAssistantStore.ts
interface AiAssistantState {
  isOpen: boolean;
  messages: ChatMessage[];
  isStreaming: boolean;

  // Actions
  toggleModal: () => void;
  sendMessage: (message: string) => Promise<void>;
  applyCanvasPatch: (patch: CanvasPatch) => void;
  clearHistory: () => void;
}

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: Date;
  canvasOperations?: CanvasOperation[];  // What changes were made
}

interface CanvasPatch {
  addNodes?: PlaybookNode[];
  removeNodeIds?: string[];
  updateNodes?: Partial<PlaybookNode>[];
  addEdges?: PlaybookEdge[];
  removeEdgeIds?: string[];
}
```

**New Components:**

```
src/client/pcf/PlaybookBuilderHost/control/
├── components/
│   ├── AiAssistant/
│   │   ├── AiAssistantModal.tsx       ← Floating modal container
│   │   ├── ChatHistory.tsx            ← Message list with scroll
│   │   ├── ChatInput.tsx              ← Text input with send button
│   │   ├── OperationFeedback.tsx      ← Shows "Creating node..." etc.
│   │   └── index.ts
│   └── ...
├── services/
│   └── AiPlaybookService.ts           ← API client for /api/ai/build-playbook-canvas
├── stores/
│   └── aiAssistantStore.ts            ← Modal state, chat history
└── ...
```

#### BFF API Endpoint

```csharp
// Endpoints/AiPlaybookBuilderEndpoints.cs

app.MapPost("/api/ai/build-playbook-canvas", BuildPlaybookCanvasAsync)
   .RequireAuthorization()
   .Produces<IAsyncEnumerable<ServerSentEvent>>(StatusCodes.Status200OK);

public record BuildPlaybookCanvasRequest
{
    public Guid PlaybookId { get; init; }
    public CanvasState CurrentCanvas { get; init; }
    public string Message { get; init; }
    public ChatMessage[] ConversationHistory { get; init; }
}

public record CanvasState
{
    public PlaybookNode[] Nodes { get; init; }
    public PlaybookEdge[] Edges { get; init; }
}
```

#### Example Interactions

| User Says | AI Does |
|-----------|---------|
| "Create a lease analysis playbook" | Creates 5-6 nodes: TL;DR, Key Terms, Compliance, Risk, Assemble, Deliver |
| "Add a node to extract financial terms" | Adds AI Analysis node with Financial Terms Action |
| "Connect the compliance node to the risk analysis" | Creates edge between specified nodes |
| "Use our standard compliance skill" | Searches Skills, links existing or creates new |
| "The TL;DR should output to document_summary field" | Updates Output node configuration |
| "Remove the email notification" | Deletes specified node and its edges |
| "What does this playbook do?" | Explains current canvas structure |

#### Security Considerations

| Concern | Mitigation |
|---------|------------|
| Unauthorized canvas modification | Same auth as playbook edit |
| Prompt injection | Sanitize user input, validate operations |
| Runaway operations | Limit operations per request (max 10) |
| Invalid canvas state | Validate patch before applying |
| Cost control | Track token usage, rate limit |

#### Project-Style AI Architecture

The AI assistant follows a structured approach similar to how Claude Code handles development projects. This makes the AI more predictable, recoverable, and explainable.

**Parallel to Development Projects:**

| Development Projects | Playbook Builder AI |
|---------------------|---------------------|
| `design.md` (human input) | Upload/paste requirements or chat description |
| `spec.md` | Internal playbook spec (nodes needed, purpose) |
| `plan.md` | Execution plan (order of operations) |
| `tasks/*.poml` | Discrete operations (addNode, linkScope, createEdge) |
| `.claude/skills/` | Building "skills" (how to create node types, select scopes) |
| BFF Tools | Operations (addNode, updateConfig, linkScope) |

**Key Difference**: The spec/plan/tasks are **internal AI resources** (not user-facing). Users see only the chat and real-time canvas updates.

##### Internal Playbook Build Plan

When the AI receives a request, it generates an internal build plan:

```json
{
  "playbookSpec": {
    "name": "Real Estate Lease Analysis",
    "purpose": "Analyze lease agreements for compliance with company standards",
    "documentTypes": ["LEASE"],
    "matterTypes": ["REAL_ESTATE"],
    "estimatedNodes": 8
  },
  "scopeRequirements": {
    "actions": ["ACT-001", "ACT-002", "ACT-004", "ACT-005"],
    "skills": ["SKL-004", "SKL-009"],
    "knowledge": ["KNW-004", "KNW-007"],
    "tools": ["TL-001", "TL-002", "TL-004", "TL-005"]
  },
  "executionPlan": [
    { "step": 1, "op": "createNode", "type": "aiAnalysis", "label": "TL;DR Summary", "outputVar": "tldrSummary" },
    { "step": 2, "op": "createNode", "type": "aiAnalysis", "label": "Extract Parties", "outputVar": "parties" },
    { "step": 3, "op": "createEdge", "from": "step_1", "to": "step_2" },
    { "step": 4, "op": "linkScope", "nodeRef": "step_1", "scopeType": "action", "scopeId": "ACT-004" }
  ]
}
```

**Benefits:**
- Structured reasoning (AI follows a plan, not arbitrary operations)
- Validation checkpoint (can show user high-level plan before executing)
- Recoverability (if interrupted, plan shows remaining steps)
- Explainability (user can ask "why?" and AI references the plan)

##### AI Building "Skills" (Internal Knowledge)

The AI has access to internal "skills" that guide how to build playbooks:

```
playbook-builder-ai/skills/
├── create-analysis-node.md    ← How to configure AI Analysis nodes
├── create-condition-node.md   ← How to add branching logic
├── select-action.md           ← Choose Action based on node purpose
├── select-skills.md           ← Choose Skills based on document type
├── attach-knowledge.md        ← When and how to link Knowledge sources
├── design-output-flow.md      ← Assemble + Deliver patterns
└── common-patterns/
    ├── lease-analysis.md      ← Reference patterns for leases
    ├── contract-review.md     ← Reference patterns for contracts
    └── risk-assessment.md     ← Risk detection node patterns
```

These are embedded in the system prompt or loaded dynamically based on context.

##### AI Building "Tools" (Operations)

The AI can execute these discrete operations:

| Tool | Description | Parameters |
|------|-------------|------------|
| `addNode` | Create node on canvas | `type`, `label`, `position`, `config` |
| `removeNode` | Delete node and connected edges | `nodeId` |
| `createEdge` | Connect two nodes | `sourceId`, `targetId` |
| `updateNodeConfig` | Modify node properties | `nodeId`, `config` |
| `linkScope` | Attach existing scope to node | `nodeId`, `scopeType`, `scopeId` |
| `createScope` | Create new Action/Skill/Knowledge in Dataverse | `type`, `data` |
| `searchScopes` | Find existing scopes by name/purpose | `type`, `query` |
| `autoLayout` | Arrange nodes for visual clarity | — |

##### Workflow: Understand → Plan → Confirm → Execute → Refine

```
User: "Build a lease analysis playbook"
          │
          ▼
┌─────────────────────────────────┐
│  1. UNDERSTAND                   │  ← Parse requirements
│     - Document type: Lease       │
│     - Analysis goals identified  │
│     - Load lease-analysis skill  │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  2. PLAN (internal)              │  ← Generate build plan
│     - Identify required scopes   │
│     - Sequence node operations   │
│     - Estimate 8 nodes needed    │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  3. CONFIRM (optional)           │  ← Show high-level plan
│     AI: "I'll create 8 nodes:    │
│         TL;DR, Parties, Terms,   │
│         Compliance, Risk..."     │
│     User approves or adjusts     │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  4. EXECUTE                      │  ← Stream operations
│     - Create nodes (canvas)      │
│     - Link scopes (Dataverse)    │
│     - Real-time canvas updates   │
│     - Progress feedback          │
└─────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────┐
│  5. REFINE                       │  ← Conversation continues
│     User: "Add financial terms"  │
│     User: "Change output format" │
│     (Returns to step 1)          │
└─────────────────────────────────┘
```

##### Design Input Mode

Users can provide requirements in multiple ways:

| Input Method | Description |
|--------------|-------------|
| **Chat** | Natural language description in the modal |
| **Paste** | Paste requirements text into chat |
| **Upload** | Upload a design document (Word, PDF, text) |
| **Reference** | "Build something like [existing playbook]" |

For uploaded documents, the AI extracts requirements before generating the plan:

```
┌─────────────────────────────────────────────────────────────────┐
│  User uploads: "lease-review-requirements.docx"                  │
│                              │                                   │
│                              ▼                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  AI: "I've analyzed your requirements document.           │  │
│  │       Here's what I understood:                           │  │
│  │                                                           │  │
│  │       • Document type: Commercial Lease                   │  │
│  │       • Key analyses needed:                              │  │
│  │         - Extract parties and terms                       │  │
│  │         - Check compliance against company standards      │  │
│  │         - Identify financial obligations                  │  │
│  │         - Flag high-risk provisions                       │  │
│  │       • Output: PDF report with executive summary         │  │
│  │                                                           │  │
│  │       Should I create a playbook with these 8 nodes?"     │  │
│  └───────────────────────────────────────────────────────────┘  │
│                              │                                   │
│                              ▼                                   │
│  User: "Yes, and also add a node for key dates extraction"      │
│                              │                                   │
│                              ▼                                   │
│  (AI updates plan and executes)                                  │
└─────────────────────────────────────────────────────────────────┘
```

##### Conversational UX: Natural Language with Near-Deterministic Interpretation

The AI assistant accepts **natural language input** as the primary interface, with **quick action buttons** as optional suggestions. The key challenge is ensuring the AI interprets user instructions consistently and maps them to the correct resources (scopes, tools, patterns).

**Design Decision**: Hybrid input model—natural language primary, structured options as accelerators.

###### Why Not Fully Structured Commands?

| Approach | Pros | Cons |
|----------|------|------|
| **Numbered commands** (`[1] Build [2] Add`) | Predictable, no ambiguity | Rigid, poor UX, requires training |
| **Natural language only** | Intuitive, flexible | Can be ambiguous, unpredictable |
| **Hybrid (chosen)** | Best of both—intuitive + structured suggestions | Requires robust intent classification |

###### Intent Classification System

The AI classifies user input into **operation intents** before executing:

| Intent Category | Example Inputs | Mapped Operation |
|----------------|----------------|------------------|
| `CREATE_PLAYBOOK` | "Build a lease analysis playbook", "Make me a playbook for contracts" | Generate full build plan |
| `ADD_NODE` | "Add a node to extract dates", "I need a compliance check node" | `addNode` tool |
| `REMOVE_NODE` | "Delete the risk node", "Remove that last node" | `removeNode` tool |
| `CONNECT_NODES` | "Connect compliance to risk", "Link these two together" | `createEdge` tool |
| `CONFIGURE_NODE` | "Change the output variable to partyNames", "Update the prompt" | `updateNodeConfig` tool |
| `LINK_SCOPE` | "Use the standard compliance skill", "Add the lease knowledge" | `linkScope` tool |
| `CREATE_SCOPE` | "Create a new action for financial terms" | `createScope` tool |
| `QUERY_STATUS` | "What does this playbook do?", "Explain the compliance node" | No tool, explain state |
| `MODIFY_LAYOUT` | "Arrange the nodes", "Clean up the layout" | `autoLayout` tool |
| `UNDO` | "Undo that", "Go back", "Revert the last change" | Reverse last operation |
| `UNCLEAR` | Ambiguous input requiring clarification | Clarification loop |

###### Mapping Natural Language to Operations

The LLM uses a structured **intent extraction** prompt that:

1. **Extracts intent category** from the classification taxonomy above
2. **Identifies target entities** (node IDs, scope names, positions)
3. **Determines parameters** (node type, configuration values, etc.)
4. **Validates feasibility** against current canvas state

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      INTENT EXTRACTION PIPELINE                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  User: "Connect the TL;DR summary to the compliance check"                  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 1: CLASSIFY INTENT                                               │  │
│  │  → Category: CONNECT_NODES                                             │  │
│  │  → Confidence: 0.95                                                    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 2: EXTRACT ENTITIES                                              │  │
│  │  → Source node: "TL;DR summary" → resolve to node_001                  │  │
│  │  → Target node: "compliance check" → resolve to node_003               │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 3: VALIDATE AGAINST CANVAS STATE                                 │  │
│  │  → node_001 exists? ✓                                                  │  │
│  │  → node_003 exists? ✓                                                  │  │
│  │  → Edge already exists? ✗ (proceed)                                    │  │
│  │  → Creates cycle? ✗ (safe)                                             │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Step 4: GENERATE OPERATION                                            │  │
│  │  → Tool: createEdge                                                    │  │
│  │  → Parameters: { sourceId: "node_001", targetId: "node_003" }         │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

###### Clarification Loops for Ambiguous Input

When intent or entities cannot be determined with high confidence, the AI asks for clarification:

| Ambiguity Type | Example | AI Response |
|----------------|---------|-------------|
| **Multiple matches** | "Connect the analysis node" (3 nodes have "analysis") | "Which analysis node? I see: [1] Compliance Analysis, [2] Risk Analysis, [3] Financial Analysis" |
| **Missing target** | "Add an edge" | "Where should I connect from and to? You can say 'from X to Y' or select nodes on the canvas." |
| **Unknown scope** | "Use the Smith contract skill" | "I couldn't find a skill named 'Smith contract'. Did you mean: [1] Standard Contract Review, [2] Create new skill?" |
| **Conflicting request** | "Connect A to B" (edge exists) | "A is already connected to B. Did you want to: [1] Remove the existing connection, [2] Add a parallel path?" |

**Clarification UX Pattern:**

```
┌─────────────────────────────────────────────────────────────────┐
│  AI: I found multiple nodes that match "analysis":              │
│                                                                  │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐               │
│  │ Compliance  │ │    Risk     │ │  Financial  │               │
│  │  Analysis   │ │  Analysis   │ │  Analysis   │               │
│  └─────────────┘ └─────────────┘ └─────────────┘               │
│      [1]             [2]              [3]                        │
│                                                                  │
│  Which one did you mean?                                         │
│  ──────────────────────────────────────────────────────────────  │
│  [Type number, name, or describe the one you want...]           │
└─────────────────────────────────────────────────────────────────┘
```

###### Scope Selection: How AI Picks the Right Resources

When the AI needs to link an Action, Skill, Knowledge, or Tool to a node, it uses a **scope selection algorithm**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      SCOPE SELECTION ALGORITHM                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Context: User wants to add "compliance analysis" node                      │
│                                                                              │
│  Step 1: DETERMINE REQUIRED SCOPE TYPES                                      │
│  ────────────────────────────────────────                                    │
│  Node type: aiAnalysis → Requires: Action (required), Skills (optional)     │
│                                                                              │
│  Step 2: SEARCH EXISTING SCOPES (Dataverse query)                           │
│  ────────────────────────────────────────────                                │
│  Query Actions where:                                                        │
│    - name LIKE '%compliance%' OR description LIKE '%compliance%'            │
│    - OR tags contain 'compliance', 'lease', 'contract'                      │
│                                                                              │
│  Results:                                                                    │
│    ├── ACT-002: "Contract Compliance Analysis" (score: 0.92)                │
│    ├── ACT-007: "Policy Compliance Check" (score: 0.78)                     │
│    └── ACT-014: "Regulatory Compliance" (score: 0.65)                       │
│                                                                              │
│  Step 3: RANK BY RELEVANCE                                                   │
│  ────────────────────────────────                                            │
│  Factors:                                                                    │
│    - Semantic similarity to user request                                     │
│    - Document type compatibility (lease → lease-related actions)            │
│    - Usage frequency (popular actions ranked higher)                         │
│    - Recency (recently created/used actions ranked higher)                   │
│                                                                              │
│  Step 4: SELECT OR PROMPT                                                    │
│  ────────────────────────────────                                            │
│  IF top match score > 0.85:                                                  │
│    → Auto-select and inform: "Using 'Contract Compliance Analysis' action"  │
│  ELSE IF multiple close matches:                                             │
│    → Ask user to choose: "Which compliance action fits best?"               │
│  ELSE IF no matches:                                                         │
│    → Offer to create: "No compliance action found. Create one?"             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Scope Metadata Used for Matching:**

| Scope Type | Matching Attributes |
|------------|---------------------|
| **Action** | `name`, `description`, `tags`, `documentTypes`, `matterTypes` |
| **Skill** | `name`, `description`, `tags`, `applicableDocTypes` |
| **Knowledge** | `name`, `description`, `sourceType`, `contentTags` |
| **Tool** | `name`, `description`, `handlerType`, `inputSchema` |

###### Quick Action Buttons (Suggestions, Not Requirements)

The chat UI offers **contextual quick actions** based on canvas state:

```
┌─────────────────────────────────────────────────────────────────┐
│  AI: I've created the TL;DR Summary node. What's next?          │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Suggestions:                                             │   │
│  │  [+ Add Analysis Node]  [+ Add Condition]  [➔ Connect]   │   │
│  │  [🔧 Configure Node]    [📦 Link Scope]    [🗑 Remove]    │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ──────────────────────────────────────────────────────────────  │
│  [Type a message or click a suggestion...]                      │
└─────────────────────────────────────────────────────────────────┘
```

**Button Behavior:**
- Clicking a button **pre-fills** the chat input with a starter prompt
- User can **modify** or **send directly**
- Buttons change based on **canvas state** and **conversation context**

| Canvas State | Suggested Buttons |
|--------------|-------------------|
| Empty canvas | `[🏗 Build from Template]` `[📝 Describe Requirements]` |
| Single node | `[+ Add Node]` `[🔧 Configure]` `[📦 Link Scope]` |
| Multiple nodes, no edges | `[➔ Connect Nodes]` `[🔀 Auto-Layout]` |
| Node selected | `[🔧 Configure]` `[📦 Link Scope]` `[🗑 Remove]` |
| Building complete | `[✓ Validate]` `[▶ Test Run]` `[💾 Save]` |

###### Ensuring Near-Deterministic Behavior

To ensure the AI behaves predictably:

**1. Constrained System Prompt**

The system prompt explicitly enumerates:
- All valid intent categories and their mappings
- All available tools and their exact parameters
- Rules for scope selection and matching thresholds
- Required clarification triggers

**2. Canvas State Validation**

Before executing ANY operation, the AI validates:
- Target nodes/edges exist
- Operation won't create invalid state (cycles, orphans)
- Required scopes are available or can be created

**3. Operation Audit Trail**

Every operation is logged with:
- User input (verbatim)
- Classified intent and confidence
- Selected operation and parameters
- Scope selection rationale (if applicable)

```json
{
  "userInput": "Connect the TL;DR to compliance",
  "classification": {
    "intent": "CONNECT_NODES",
    "confidence": 0.94
  },
  "entityResolution": {
    "source": { "input": "TL;DR", "resolved": "node_001", "confidence": 0.98 },
    "target": { "input": "compliance", "resolved": "node_003", "confidence": 0.91 }
  },
  "operation": {
    "tool": "createEdge",
    "params": { "sourceId": "node_001", "targetId": "node_003" }
  },
  "validationPassed": true
}
```

**4. Fallback to Clarification**

If ANY of these thresholds fail, the AI asks for clarification rather than guessing:

| Metric | Threshold | Action if Below |
|--------|-----------|-----------------|
| Intent confidence | < 0.75 | Ask "Did you mean to [A] or [B]?" |
| Entity resolution | < 0.80 | Show matching options |
| Scope match score | < 0.70 | Ask user to select or create |
| Validation | Fails | Explain why and suggest alternatives |

**5. Conversation Context Window**

The AI maintains rolling context of:
- Last 10 operations performed
- Current canvas structure summary
- Active "mode" (building, refining, configuring)
- Referenced scopes in this session

This prevents the AI from "forgetting" recent context and making inconsistent decisions.

###### Error Recovery and Undo

| Scenario | AI Behavior |
|----------|-------------|
| User says "undo" | Reverse last operation, explain what was undone |
| Operation fails (Dataverse error) | Explain failure, suggest retry or alternative |
| User describes impossible operation | Explain why, suggest valid alternatives |
| Session interruption | Preserve canvas state, resume from last valid state |

##### Test Execution Architecture

The AI Builder supports three test execution modes, allowing users to validate playbooks at different stages without polluting production data.

###### Test Execution Modes

| Mode | Playbook Saved? | Document Storage | Creates Records? | Use Case |
|------|-----------------|------------------|------------------|----------|
| **Mock Test** | No | None (sample data) | No | Quick logic validation |
| **Quick Test** | No | Temp blob (24hr TTL) | No | Real document, ephemeral |
| **Production Test** | Yes | SPE file | Yes | Full end-to-end validation |

###### Mode Details

**1. Mock Test (No Document)**

For rapid iteration during playbook design:

```
Canvas JSON ──▶ BFF API ──▶ Execute with sample data ──▶ Results
(in memory)                 (no Document Intelligence)
```

- Playbook canvas JSON sent in request body (not yet saved to Dataverse)
- Synthesized/sample document data based on document type
- No storage, no records created
- **Purpose**: Validate playbook logic, node flow, and condition routing quickly

**2. Quick Test (Upload, Ephemeral)**

For testing with real documents without committing:

```
Canvas JSON ─┐
(in memory)  │
             ├──▶ BFF API ──▶ Temp Blob ──▶ Doc Intel ──▶ Execute
Upload File ─┘               (24hr TTL)                    │
                                                           ▼
                                           Results (not persisted)
```

- User uploads a document file directly in the AI Builder modal
- File stored in **temp blob storage** with 24-hour TTL (same infrastructure as ENH-003 Pattern D)
- Text extracted via Azure Document Intelligence
- Playbook executed against real extracted text
- Results returned but **NOT persisted** to Dataverse
- No `sprk_document`, no `sprk_analysisoutput` records created
- **Purpose**: Test with real documents before committing to save

**3. Production Test (Full Flow)**

For validating the complete production pipeline:

```
Saved Playbook ─┐
(Dataverse)     │
                ├──▶ BFF API ──▶ SPE ──▶ Doc Intel ──▶ Execute
Select/Upload ──┘               File                    │
Document                                                ▼
                                        sprk_document record
                                        sprk_analysisoutput record
```

- Requires playbook to be saved first
- User selects existing SPE document OR uploads (which gets stored in SPE)
- Full flow: SPE storage → Document Intelligence → Playbook execution → Analysis Output record
- **Purpose**: Validate complete end-to-end flow matches production behavior

###### API Endpoint

```
POST /api/ai/test-playbook-execution
Content-Type: multipart/form-data

{
  // Playbook source (one required)
  "playbookId": "guid",                    // If saved playbook
  "canvasJson": { nodes: [], edges: [] },  // If unsaved (in-memory)

  // Test document (required for quick/production, optional for mock)
  "testDocument": <binary>,                // Uploaded file

  // Test options
  "options": {
    "mode": "mock" | "quick" | "production",
    "persistResults": false,               // Default: false for mock/quick
    "sampleDocumentType": "LEASE"          // For mock mode: which sample to use
  }
}

Response (SSE Stream):
  event: node_start
  data: { "nodeId": "node_001", "label": "TL;DR Summary" }

  event: node_output
  data: { "nodeId": "node_001", "output": { ... }, "duration_ms": 2100 }

  event: node_complete
  data: { "nodeId": "node_001", "success": true }

  ... (repeat for each node)

  event: execution_complete
  data: {
    "success": true,
    "nodesExecuted": 11,
    "nodesSkipped": 1,
    "totalDuration_ms": 22900,
    "reportUrl": "https://..." // Temp URL for quick test, persistent for production
  }
```

###### Test Flow in UI

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        TEST EXECUTION FLOW                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  User clicks [▶ Test Run] in AI Assistant modal                            │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Test Options Dialog                                                   │  │
│  │                                                                        │  │
│  │  How would you like to test?                                           │  │
│  │                                                                        │  │
│  │  ○ Mock Test (fastest)                                                │  │
│  │    Use sample lease data to validate flow                              │  │
│  │                                                                        │  │
│  │  ● Quick Test with document                                            │  │
│  │    Upload a document - results not saved                               │  │
│  │    ┌────────────────────────────────────────────────────────┐         │  │
│  │    │  📄 Drop file here or click to browse                  │         │  │
│  │    │     Supports: PDF, DOCX, DOC, TXT                      │         │  │
│  │    └────────────────────────────────────────────────────────┘         │  │
│  │                                                                        │  │
│  │  ○ Production Test (requires save)                                     │  │
│  │    Full SPE flow - creates analysis record                             │  │
│  │    [Save playbook first to enable]                                     │  │
│  │                                                                        │  │
│  │                              [Cancel]  [▶ Run Test]                    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                               │
│                              ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  Test Execution View                                                   │  │
│  │                                                                        │  │
│  │  Testing: Real Estate Lease Agreement Review                          │  │
│  │  Document: Lease_AcmeCorp_2026.pdf                                    │  │
│  │  Mode: Quick Test (ephemeral)                                          │  │
│  │                                                                        │  │
│  │  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ 65%                      │  │
│  │                                                                        │  │
│  │  ✅ TL;DR Summary (2.1s)                                              │  │
│  │  ✅ Extract Parties (1.8s)                                            │  │
│  │  ✅ Key Terms (2.3s)                                                  │  │
│  │  ✅ Financial Terms (2.5s)                                            │  │
│  │  ✅ Key Dates (1.6s)                                                  │  │
│  │  ⏳ Compliance Check...                                                │  │
│  │  ⬚ Risk Assessment                                                    │  │
│  │  ⬚ Liability Analysis                                                 │  │
│  │  ⬚ High Risk? (condition)                                             │  │
│  │  ⬚ Summary                                                            │  │
│  │  ⬚ Compile & Deliver                                                  │  │
│  │                                                                        │  │
│  │                                               [Cancel Test]            │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

###### Why Quick Test is the Recommended Default

| Consideration | Mock Test | Quick Test | Production Test |
|---------------|-----------|------------|-----------------|
| **Speed** | Fastest (~5s) | Medium (~20-30s) | Slowest (~30-60s) |
| **Realism** | Low (sample data) | High (real extraction) | Highest (full flow) |
| **Cleanup needed** | None | None (auto-expires) | Yes (records created) |
| **Tests Doc Intelligence** | No | Yes | Yes |
| **Tests SPE integration** | No | No | Yes |
| **Requires save** | No | No | Yes |

**Recommendation**: Default to **Quick Test** for the best balance of realism and convenience. Users can iterate quickly with real documents without polluting the system.

###### Temp Storage for Quick Test

Quick Test uses the same ephemeral storage infrastructure as ENH-003 Pattern D (Ad-Hoc File Analysis):

| Aspect | Implementation |
|--------|----------------|
| Storage | Azure Blob Storage with 24-hour TTL |
| Container | `test-documents` (separate from production) |
| Naming | `{sessionId}/{timestamp}_{filename}` |
| Cleanup | Azure Blob lifecycle policy auto-deletes after 24 hours |
| Security | Scoped SAS tokens, per-user isolation |
| Max size | 50MB per document |

#### Implementation Tasks

**Phase 1: Infrastructure (1 week)**
- [ ] Create `AiPlaybookBuilderService` in BFF
- [ ] Add `/api/ai/build-playbook-canvas` endpoint
- [ ] Define canvas patch schema
- [ ] Implement Dataverse operation execution

**Phase 2: PCF Components (1 week)**
- [ ] Create `aiAssistantStore`
- [ ] Build `AiAssistantModal` component
- [ ] Build `ChatHistory` and `ChatInput` components
- [ ] Wire up SSE streaming to store
- [ ] Add toolbar button to toggle modal

**Phase 3: AI Integration (1 week)**
- [ ] Design system prompt for canvas building
- [ ] Implement canvas analysis (understand current state)
- [ ] Implement operation generation
- [ ] Add scope record creation/linking
- [ ] Test with real playbook scenarios

**Phase 4: Test Execution (1 week)**
- [ ] Add `/api/ai/test-playbook-execution` endpoint
- [ ] Implement mock test with sample data generation
- [ ] Implement quick test with temp blob storage
- [ ] Integrate Document Intelligence for quick test
- [ ] Build test options dialog in PCF
- [ ] Build test execution progress view
- [ ] Add test result preview/download

**Phase 5: Polish (0.5-1 week)**
- [ ] Error handling and retry
- [ ] Loading states and animations
- [ ] Keyboard shortcuts (Cmd/Ctrl+K to open)
- [ ] Responsive modal sizing
- [ ] Documentation

#### Effort Estimate

| Component | Effort |
|-----------|--------|
| BFF endpoint + service | 3-4 days |
| Dataverse operations | 2-3 days |
| PCF modal + stores | 3-4 days |
| AI prompt engineering | 2-3 days |
| Test execution (3 modes) | 4-5 days |
| Testing + polish | 3-4 days |
| **Total** | **4-5 weeks** |

---

## Reference Documents

- [AI Playbook Architecture](../../docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md)
- [Playbook Real Estate Lease Guide](../../docs/guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md)

---

## Open Questions

1. **Expression language**: What syntax for computed fields? JSONPath? Custom DSL? JavaScript subset?
2. **Transform library**: Pre-built transforms or custom code?
3. **Validation UX**: How to show validation errors in the builder before execution?
4. **Template preview**: Can we preview how outputs will render in the template?

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| 2026-01-16 | AI Architecture Team | Initial design document |
| 2026-01-16 | AI Architecture Team | Added ENH-005: AI-Assisted Playbook Builder |
| 2026-01-16 | AI Architecture Team | ENH-005: Added project-style AI architecture (skills, tools, build plan) |
| 2026-01-16 | AI Architecture Team | ENH-005: Added conversational UX guidance with near-deterministic interpretation |
| 2026-01-16 | AI Architecture Team | ENH-005: Added test execution architecture (mock, quick, production modes) |
