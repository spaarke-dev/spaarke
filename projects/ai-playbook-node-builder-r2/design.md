# AI Playbook Node Builder R2 - Design Document

> **Project**: AI Playbook Node Builder R2
> **Status**: Design
> **Created**: January 16, 2026
> **Last Updated**: January 16, 2026 (ENH-005 added)

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

**Phase 4: Polish (0.5-1 week)**
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
| Testing + polish | 3-4 days |
| **Total** | **3-4 weeks** |

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
