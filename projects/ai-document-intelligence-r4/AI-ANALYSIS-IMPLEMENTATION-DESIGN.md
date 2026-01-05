# AI Analysis Implementation Design

> **Version**: 1.2
> **Date**: January 4, 2026
> **Status**: Draft
> **Purpose**: Define the systematic process for implementing Playbooks and Scopes
> **Related**:
> - [AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md](../../docs/architecture/AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md) - What to implement (Playbook recipes)
> - [ai-dataverse-entity-model.md](ai-dataverse-entity-model.md) - Detailed Dataverse entity definitions
> **Lineage**: R1 (foundation) → R2 (refactoring) → R3 (RAG/Playbooks) → R4 (Scope implementation)

---

## Table of Contents

1. [Overview](#overview)
2. [Existing Code Inventory](#existing-code-inventory)
3. [Dataverse Entity Model](#dataverse-entity-model)
4. [Implementation Sequence](#implementation-sequence)
5. [Phase 1: Dataverse Entity Validation](#phase-1-dataverse-entity-validation)
6. [Phase 2: Seed Data Population](#phase-2-seed-data-population)
7. [Phase 3: Tool Handler Implementation](#phase-3-tool-handler-implementation)
8. [Phase 4: Service Layer Extension](#phase-4-service-layer-extension)
9. [Phase 5: Playbook Assembly](#phase-5-playbook-assembly)
10. [Phase 6: UI/PCF Enhancement](#phase-6-uipcf-enhancement)
11. [Testing Strategy](#testing-strategy)
12. [Rollout Plan](#rollout-plan)

---

## Overview

This document describes HOW to implement the Playbook system defined in [AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md](AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md).

### Implementation Goals

1. **Systematic scope creation**: Build reusable Actions, Skills, Knowledge, and Tools
2. **Dataverse-first storage**: All configurations in Dataverse (not code)
3. **No-code playbook assembly**: Domain experts can create playbooks without engineering
4. **Incremental rollout**: Start with core playbooks, expand based on usage

### Architecture Summary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           IMPLEMENTATION LAYERS                              │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 6: UI/PCF Enhancement                                                │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Playbook      │  │ Scope         │  │ Analysis      │                   │
│  │ Selector      │  │ Selector      │  │ Workspace     │                   │
│  │ (ENHANCE)     │  │ (NEW)         │  │ (ENHANCE)     │                   │
│  └───────────────┘  └───────────────┘  └───────────────┘                   │
├─────────────────────────────────────────────────────────────────────────────┤
│  PHASE 4: Service Layer Extension                                           │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Playbook      │  │ Scope         │  │ Analysis      │                   │
│  │ Service       │  │ Resolver      │  │ Orchestration │                   │
│  │ (EXISTS)      │  │ (EXISTS)      │  │ (EXTEND)      │                   │
│  └───────────────┘  └───────────────┘  └───────────────┘                   │
├─────────────────────────────────────────────────────────────────────────────┤
│  PHASE 3: Tool Handlers                                                     │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Entity        │  │ Clause        │  │ Document      │                   │
│  │ Extractor     │  │ Analyzer      │  │ Classifier    │                   │
│  │ (EXISTS)      │  │ (EXISTS)      │  │ (EXISTS)      │                   │
│  └───────────────┘  └───────────────┘  └───────────────┘                   │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Summary       │  │ Risk          │  │ Date          │                   │
│  │ Handler       │  │ Detector      │  │ Extractor     │                   │
│  │ (NEW)         │  │ (NEW)         │  │ (NEW)         │                   │
│  └───────────────┘  └───────────────┘  └───────────────┘                   │
├─────────────────────────────────────────────────────────────────────────────┤
│  PHASE 2: Seed Data Population                                              │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Type Lookups  │  │ Base Skills   │  │ Knowledge     │                   │
│  │ (POPULATE)    │  │ & Actions     │  │ Sources       │                   │
│  └───────────────┘  └───────────────┘  └───────────────┘                   │
├─────────────────────────────────────────────────────────────────────────────┤
│  PHASE 1: Dataverse Entity Validation                                       │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Tables        │  │ N:N           │  │ Type Lookup   │                   │
│  │ (EXIST)       │  │ Relationships │  │ Tables        │                   │
│  │               │  │ (VERIFY)      │  │ (EXIST)       │                   │
│  └───────────────┘  └───────────────┘  └───────────────┘                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Existing Code Inventory

**CRITICAL**: Before implementing any component, verify it doesn't already exist. The R3 project created substantial infrastructure.

> **Full Inventory**: See [projects/ai-document-intelligence-r4/CODE-INVENTORY.md](../ai-document-intelligence-r4/CODE-INVENTORY.md)

### Existing BFF API Services (DO NOT RECREATE)

| File | Purpose | Status |
|------|---------|--------|
| `Services/Ai/PlaybookService.cs` | Playbook CRUD operations | ✅ Complete |
| `Services/Ai/IPlaybookService.cs` | Playbook service interface | ✅ Complete |
| `Services/Ai/ScopeResolverService.cs` | Load scopes by ID/playbook | ✅ Complete |
| `Services/Ai/IScopeResolverService.cs` | Scope resolver interface | ✅ Complete |
| `Services/Ai/AnalysisOrchestrationService.cs` | Main analysis orchestrator | ✅ Complete |
| `Services/Ai/IAnalysisOrchestrationService.cs` | Orchestration interface | ✅ Complete |
| `Services/Ai/AnalysisContextBuilder.cs` | Build analysis context | ✅ Complete |
| `Services/Ai/WorkingDocumentService.cs` | Working document storage | ✅ Complete |
| `Services/Ai/RagService.cs` | Hybrid RAG search | ✅ Complete |
| `Services/Ai/KnowledgeDeploymentService.cs` | RAG deployment routing | ✅ Complete |
| `Services/Ai/EmbeddingCache.cs` | Redis embedding cache | ✅ Complete |
| `Services/Ai/OpenAiClient.cs` | OpenAI API wrapper | ✅ Complete |
| `Services/Ai/ToolHandlerRegistry.cs` | Handler discovery/resolution | ✅ Complete |

### Existing Tool Handlers (DO NOT RECREATE)

| File | Purpose | Status |
|------|---------|--------|
| `Services/Ai/Tools/EntityExtractorHandler.cs` | AI entity extraction | ✅ Complete |
| `Services/Ai/Tools/ClauseAnalyzerHandler.cs` | Contract clause analysis | ✅ Complete |
| `Services/Ai/Tools/DocumentClassifierHandler.cs` | Document categorization | ✅ Complete |

### Existing API Endpoints (DO NOT RECREATE)

| File | Endpoints | Status |
|------|-----------|--------|
| `Api/Ai/AnalysisEndpoints.cs` | `/api/ai/analysis/*` (5 endpoints) | ✅ Complete |
| `Api/Ai/RagEndpoints.cs` | `/api/ai/rag/*` (6 endpoints) | ✅ Complete |
| `Api/Ai/PlaybookEndpoints.cs` | `/api/ai/playbooks/*` (8 endpoints) | ✅ Complete |

### Existing Models (DO NOT RECREATE)

| File | Purpose | Status |
|------|---------|--------|
| `Models/Ai/PlaybookDto.cs` | Playbook DTOs (Save, Response, Query, Sharing) | ✅ Complete |
| `Models/Ai/KnowledgeDocument.cs` | RAG knowledge index model | ✅ Complete |
| `Models/Ai/AnalysisResult.cs` | Analysis result model | ✅ Complete |

### Existing PCF Controls (ENHANCE, NOT RECREATE)

| Control | Location | Status |
|---------|----------|--------|
| AnalysisBuilder | `src/client/pcf/AnalysisBuilder/` | ✅ Complete - needs enhancement |
| AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` | ✅ Complete - needs enhancement |

### Unit Tests (DO NOT RECREATE)

| Test File | Coverage | Status |
|-----------|----------|--------|
| `AnalysisOrchestrationServiceTests.cs` | Orchestration | ✅ Complete |
| `PlaybookServiceTests.cs` | Playbook CRUD | ✅ Complete |
| `RagServiceTests.cs` | RAG search | ✅ Complete |
| `EntityExtractorHandlerTests.cs` | Entity extraction | ✅ Complete |
| `ClauseAnalyzerHandlerTests.cs` | Clause analysis | ✅ Complete |
| `DocumentClassifierHandlerTests.cs` | Classification | ✅ Complete |
| `ToolHandlerRegistryTests.cs` | Handler registry | ✅ Complete |

### What Needs to Be Created (NEW) (ADD TO CODE-INVENTORY.md)

| Component | Type | Purpose |
|-----------|------|---------|
| `SummaryHandler.cs` | Tool Handler | Generate document summaries |
| `RiskDetectorHandler.cs` | Tool Handler | Identify document risks |
| `ClauseComparisonHandler.cs` | Tool Handler | Compare to standard terms |
| `DateExtractorHandler.cs` | Tool Handler | Extract/normalize dates |
| `FinancialCalculatorHandler.cs` | Tool Handler | Financial calculations |
| Seed data scripts | Data | Populate Actions, Skills, Knowledge, Tools |
| Playbook recipes | Data | Create starter playbooks (PB-001 to PB-010) |
| Scope endpoint extensions | API | List Skills/Knowledge/Tools/Actions |
| PlaybookSelector enhancement | PCF | Playbook selection UI |

---

## Dataverse Entity Model

### Entities in Spaarke_DocumentIntelligence Solution

**Source**: `infrastructure/dataverse/solutions/Spaarke_DocumentIntelligence_extracted/solution.xml`

#### Core Analysis Entities (ALL EXIST)

| Entity | Schema Name | Purpose | Status |
|--------|-------------|---------|--------|
| Analysis | `sprk_analysis` | Analysis session records | ✅ Exists |
| Analysis Action | `sprk_analysisaction` | Individual AI operations | ✅ Exists |
| Analysis Skill | `sprk_analysisskill` | Reusable analysis bundles | ✅ Exists |
| Analysis Knowledge | `sprk_analysisknowledge` | RAG context sources | ✅ Exists |
| Analysis Tool | `sprk_analysistool` | Handler implementations | ✅ Exists |
| Analysis Playbook | `sprk_analysisplaybook` | Playbook definitions | ✅ Exists |
| Analysis Output | `sprk_analysisoutput` | Analysis output records | ✅ Exists |
| Analysis Chat Message | `sprk_analysischatmessage` | Chat history | ✅ Exists |
| Analysis Working Version | `sprk_analysisworkingversion` | Working document storage | ✅ Exists |
| Analysis Email Metadata | `sprk_analysisemailmetadata` | Email export metadata | ✅ Exists |

#### Type/Category Lookup Entities (ALL EXIST)

| Entity | Schema Name | Purpose | Status |
|--------|-------------|---------|--------|
| AI Skill Type | `sprk_aiskilltype` | Categorize skills | ✅ Exists |
| AI Tool Type | `sprk_aitooltype` | Categorize tools | ✅ Exists |
| AI Knowledge Type | `sprk_aiknowledgetype` | Categorize knowledge | ✅ Exists |
| Analysis Action Type | `sprk_analysisactiontype` | Categorize actions | ✅ Exists |
| AI Output Type | `sprk_outputtypes` | Output format types | ✅ Exists |
| AI Retrieval Mode | `sprk_airetrievalmode` | RAG retrieval modes | ✅ Exists |

#### RAG/Knowledge Entities (ALL EXIST)

| Entity | Schema Name | Purpose | Status |
|--------|-------------|---------|--------|
| AI Knowledge Deployment | `sprk_aiknowledgedeployment` | RAG deployment config | ✅ Exists |
| AI Knowledge Source | `sprk_aiknowledgesource` | Knowledge source config | ✅ Exists |

#### Supporting Entities

| Entity | Schema Name | Purpose | Status |
|--------|-------------|---------|--------|
| Document | `sprk_document` | Document records | ✅ Exists |
| Matter Type Ref | `sprk_mattertype_ref` | Matter type reference | ✅ Exists |

### Entity Relationships (from customizations.xml)

| Relationship | Type | Purpose | Status |
|--------------|------|---------|--------|
| `sprk_aiskilltype_analysisskill` | 1:N | SkillType → Skill | ✅ Exists
| `sprk_aitooltype_analysistool` | 1:N | ToolType → Tool | ✅ Exists
| `sprk_aiknowledgetype_analysisknowledge` | 1:N | KnowledgeType → Knowledge | ✅ Exists
| `sprk_aiknowledgetype_aiknowledgesource` | 1:N | KnowledgeType → KnowledgeSource | ✅ Exists
| `sprk_aioutputtype_analysisplaybook` | 1:N | OutputType → Playbook | ✅ Exists
| `sprk_aioutputtype_analysisoutput` | 1:N | OutputType → Output | ✅ Exists
| `sprk_aiknowledgesource_aiknowledgedeployment` | 1:N | KnowledgeSource → Deployment | ✅ Exists
| `sprk_aiknowledgesource_analysisknowledge` | 1:N | KnowledgeSource → Knowledge | ✅ Exists
| `sprk_airetrievalmode_aiknowledgesource` | 1:N | RetrievalMode → KnowledgeSource | ✅ Exists

### N:N Relationships (CONFIRMED)

All N:N relationships exist in Dataverse (verified in customizations.xml):

| Relationship | Entities | Intersection Table | Status |
|--------------|----------|-------------------|--------|
| `sprk_analysisplaybook_action` | Playbook ↔ Action | `sprk_analysisplaybook_action` | ✅ Exists |
| `sprk_playbook_skill` | Playbook ↔ Skill | `sprk_playbook_skill` | ✅ Exists |
| `sprk_playbook_knowledge` | Playbook ↔ Knowledge | `sprk_playbook_knowledge` | ✅ Exists |
| `sprk_playbook_tool` | Playbook ↔ Tool | `sprk_playbook_tool` | ✅ Exists |

---

## Implementation Sequence

### Dependency Order

```
Phase 1: Dataverse Entities (FOUNDATION)
    │
    ├── 1.1 Create Type Lookup tables (categories)
    ├── 1.2 Create main Scope tables (Action, Skill, Knowledge, Tool)
    ├── 1.3 Create Playbook table
    └── 1.4 Create N:N relationship tables
    │
    ▼
Phase 2: Seed Data (CONTENT)
    │
    ├── 2.1 Populate Type Lookup tables with categories
    ├── 2.2 Create Action records (ACT-001 through ACT-008)
    ├── 2.3 Create Tool records (TL-001 through TL-008)
    ├── 2.4 Create Knowledge records (KNW-001 through KNW-010)
    └── 2.5 Create Skill records (SKL-001 through SKL-010)
    │
    ▼
Phase 3: Tool Handlers (CODE)
    │
    ├── 3.1 Implement IAiToolHandler interface handlers
    ├── 3.2 Create handler registry
    └── 3.3 Unit test each handler
    │
    ▼
Phase 4: Service Layer (ORCHESTRATION)
    │
    ├── 4.1 Implement PlaybookService (CRUD for playbooks)
    ├── 4.2 Implement ScopeResolverService (load scopes by ID/playbook)
    ├── 4.3 Extend AnalysisOrchestrationService for playbook execution
    └── 4.4 Create API endpoints
    │
    ▼
Phase 5: Playbook Assembly (RECIPES)
    │
    ├── 5.1 Create starter playbooks (PB-001 through PB-010)
    ├── 5.2 Link scopes to playbooks via N:N relationships
    └── 5.3 Validate playbook configurations
    │
    ▼
Phase 6: UI/PCF (USER EXPERIENCE)
    │
    ├── 6.1 Playbook selector UI (choose playbook for document)
    ├── 6.2 Playbook builder UI (create/edit playbooks)
    └── 6.3 Analysis workspace integration
```

---

## Phase 1: Dataverse Entity Validation

**Note**: All entities already exist in Dataverse. Phase 1 is validation only.

### 1.1 Verify Entity Fields Match Code Models

Cross-reference Dataverse entities with C# models in `IScopeResolverService.cs`:

| C# Model | Dataverse Entity | Fields to Verify |
|----------|------------------|------------------|
| `AnalysisSkill` | `sprk_analysisskill` | Id, Name, Description, PromptFragment, Category |
| `AnalysisKnowledge` | `sprk_analysisknowledge` | Id, Name, Description, Type, Content, DocumentId, DeploymentId |
| `AnalysisTool` | `sprk_analysistool` | Id, Name, Description, Type, HandlerClass, Configuration |
| `AnalysisAction` | `sprk_analysisaction` | Id, Name, Description, SystemPrompt, SortOrder |

### 1.2 N:N Relationships (CONFIRMED)

All required relationships exist for Playbook → Scope linking:

| Relationship Name | Entity 1 | Entity 2 | Status |
|-------------------|----------|----------|--------|
| `sprk_analysisplaybook_action` | sprk_analysisplaybook | sprk_analysisaction | ✅ Exists |
| `sprk_playbook_skill` | sprk_analysisplaybook | sprk_analysisskill | ✅ Exists |
| `sprk_playbook_knowledge` | sprk_analysisplaybook | sprk_analysisknowledge | ✅ Exists |
| `sprk_playbook_tool` | sprk_analysisplaybook | sprk_analysistool | ✅ Exists |

**Verification performed**: Confirmed in `customizations.xml` with intersection tables defined.

### 1.3 Verify Type Lookup Tables Have Data

| Table | Expected Categories |
|-------|---------------------|
| `sprk_analysisactiontype` | Extraction, Classification, Summarization, Analysis, Comparison |
| `sprk_aiskilltype` | Document Analysis, Contract Specific, Compliance, Risk Analysis, Financial |
| `sprk_aiknowledgetype` | Standards, Regulations, Best Practices, Templates, Taxonomy |
| `sprk_aitooltype` | Entity Extraction, Classification, Analysis, Calculation |

**If empty:** Populate in Phase 2 seed data.

### 1.4 Document Entity Schema (For Reference)

> **Detailed field definitions**: See [ai-dataverse-entity-model.md](ai-dataverse-entity-model.md)

Key fields required for implementation:

#### sprk_analysisskill Fields

| Field | Schema Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | `sprk_name` | String (200) | Skill display name |
| Description | `sprk_description` | Memo (4000) | What the skill does |
| Prompt Fragment | `sprk_promptfragment` | Memo (100000) | System prompt content |
| Skill Type | `sprk_skilltypeid` | Lookup | Reference to `sprk_aiskilltype` |
| Category | `sprk_category` | Picklist | Tone (0), Style (1), Format (2), Expertise (3) |

#### sprk_analysistool Fields

| Field | Schema Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | `sprk_name` | String (200) | Tool display name |
| Description | `sprk_description` | Memo (4000) | What the tool does |
| Tool Type | `sprk_tooltypeid` | Lookup | Reference to `sprk_aitooltype` |
| Handler Class | `sprk_handlerclass` | String (200) | C# handler class name |
| Configuration | `sprk_configuration` | Memo (100000) | JSON configuration |

#### sprk_analysisknowledge Fields

| Field | Schema Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | `sprk_name` | String (200) | Knowledge source name |
| Description | `sprk_description` | Memo (4000) | Description |
| Knowledge Type | `sprk_knowledgetypeid` | Lookup | Reference to `sprk_aiknowledgetype` |
| Knowledge Source | `sprk_knowledgesourceid` | Lookup | Reference to `sprk_aiknowledgesource` |
| Content | `sprk_content` | Memo (100000) | Inline content for rules/templates |
| Document | `sprk_documentid` | Lookup | Reference to `sprk_document` |

#### sprk_analysisaction Fields

| Field | Schema Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | `sprk_name` | String (200) | Action name (e.g., "Summarize Document") |
| Description | `sprk_description` | Memo (4000) | User-facing description |
| Action Type | `sprk_actiontypeid` | Lookup | Reference to `sprk_analysisactiontype` |
| System Prompt | `sprk_systemprompt` | Memo (100000) | Base prompt template for the AI |
| Sort Order | `sprk_sortorder` | Integer (0-10000) | Display order in the UI |

#### sprk_analysisplaybook Fields

| Field | Schema Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | `sprk_name` | String (200) | Playbook name |
| Description | `sprk_description` | Memo (4000) | Playbook description |
| Output Type | `sprk_outputtypeid` | Lookup | Reference to `sprk_aioutputtype` |
| Is Public | `sprk_ispublic` | Boolean | Visible to all users (default: No) |

---

## Phase 2: Seed Data Population

### 2.1 Type Lookups (Categories)

Populate category tables first:

```
sprk_analysisactiontype:
├── Extraction       (Sort: 1)
├── Classification   (Sort: 2)
├── Summarization    (Sort: 3)
├── Analysis         (Sort: 4)
└── Comparison       (Sort: 5)

sprk_aiskilltype:
├── Document Analysis (Sort: 1)
├── Contract Specific (Sort: 2)
├── Compliance        (Sort: 3)
├── Risk Analysis     (Sort: 4)
└── Financial         (Sort: 5)

sprk_aiknowledgetype:
├── Standards        (Sort: 1)
├── Regulations      (Sort: 2)
├── Best Practices   (Sort: 3)
├── Templates        (Sort: 4)
└── Taxonomy         (Sort: 5)

sprk_aitooltype:
├── Entity Extraction (Sort: 1)
├── Classification    (Sort: 2)
├── Analysis          (Sort: 3)
└── Calculation       (Sort: 4)
```

### 2.2 Actions

Create action records (these map to specific AI operations):

| ID | Name | System Prompt (Summary) | Handler |
|----|------|-------------------------|---------|
| ACT-001 | Extract Entities | "Extract named entities including parties, dates, amounts..." | EntityExtractorHandler |
| ACT-002 | Analyze Clauses | "Identify and analyze contract clauses by category..." | ClauseAnalyzerHandler |
| ACT-003 | Classify Document | "Classify this document into the appropriate type..." | DocumentClassifierHandler |
| ACT-004 | Summarize Content | "Generate a concise summary focusing on key points..." | SummaryHandler |
| ACT-005 | Detect Risks | "Identify potential risks including legal, financial..." | RiskDetectorHandler |
| ACT-006 | Compare Clauses | "Compare these clauses against the standard terms..." | ClauseComparisonHandler |
| ACT-007 | Extract Dates | "Extract and normalize all date references..." | DateExtractorHandler |
| ACT-008 | Calculate Values | "Calculate and validate all monetary amounts..." | FinancialCalculatorHandler |

### 2.3 Tools

Create tool records (these reference C# handler classes):

| ID | Name | Handler Class | Configuration |
|----|------|---------------|---------------|
| TL-001 | Entity Extractor | `EntityExtractorHandler` | `{"entity_types": [...], "confidence_threshold": 0.7}` |
| TL-002 | Clause Analyzer | `ClauseAnalyzerHandler` | `{"clause_categories": [...], "risk_threshold": 0.6}` |
| TL-003 | Document Classifier | `DocumentClassifierHandler` | `{"taxonomy": "standard", "confidence_threshold": 0.8}` |
| TL-004 | Summary Generator | `SummaryHandler` | `{"max_length": 500, "format": "paragraph"}` |
| TL-005 | Risk Detector | `RiskDetectorHandler` | `{"risk_categories": [...], "threshold": 0.5}` |
| TL-006 | Clause Comparator | `ClauseComparisonHandler` | `{"similarity_threshold": 0.7}` |
| TL-007 | Date Extractor | `DateExtractorHandler` | `{"date_format": "ISO8601"}` |
| TL-008 | Financial Calculator | `FinancialCalculatorHandler` | `{"currency": "USD"}` |

### 2.4 Knowledge Sources

Create knowledge records:

| ID | Name | Type | Content/Reference |
|----|------|------|-------------------|
| KNW-001 | Standard Contract Terms | RagIndex | Deployment: `contract-standards-index` |
| KNW-002 | Regulatory Guidelines | RagIndex | Deployment: `regulatory-index` |
| KNW-003 | Best Practices | Inline | "When reviewing contracts, always check..." |
| KNW-004 | Risk Categories | Inline | JSON taxonomy of risk categories |
| KNW-005 | Defined Terms | Inline | Standard legal definitions |
| KNW-006 | NDA Standards | RagIndex | Deployment: `nda-standards-index` |
| KNW-007 | Lease Standards | RagIndex | Deployment: `lease-standards-index` |
| KNW-008 | Employment Standards | RagIndex | Deployment: `employment-standards-index` |
| KNW-009 | SLA Benchmarks | Inline | Industry SLA benchmarks |
| KNW-010 | DD Checklist | Inline | Due diligence checklist categories |

### 2.5 Skills

Create skill records (bundles of actions):

| ID | Name | Prompt Fragment | Included Actions |
|----|------|-----------------|------------------|
| SKL-001 | Contract Analysis | "Perform comprehensive contract analysis..." | ACT-001, ACT-002, ACT-004, ACT-005 |
| SKL-002 | Invoice Processing | "Extract and validate invoice data..." | ACT-001, ACT-003, ACT-008 |
| SKL-003 | NDA Review | "Analyze NDA for scope, term, exclusions..." | ACT-001, ACT-002, ACT-004, ACT-005 |
| SKL-004 | Lease Review | "Review lease terms, rent, obligations..." | ACT-001, ACT-002, ACT-004, ACT-005, ACT-007 |
| SKL-005 | Employment Contract | "Analyze employment agreement terms..." | ACT-001, ACT-002, ACT-004, ACT-005 |
| SKL-006 | SLA Analysis | "Extract SLA metrics and evaluate..." | ACT-001, ACT-002, ACT-004 |
| SKL-007 | Compliance Check | "Verify regulatory compliance..." | ACT-002, ACT-005 |
| SKL-008 | Executive Summary | "Generate brief overview..." | ACT-003, ACT-004 |
| SKL-009 | Risk Assessment | "Identify and categorize risks..." | ACT-005, ACT-006 |
| SKL-010 | Clause Comparison | "Compare clauses to standards..." | ACT-002, ACT-006 |

---

## Phase 3: Tool Handler Implementation

### 3.1 Handler Interface

All handlers implement `IAiToolHandler`:

```csharp
public interface IAiToolHandler
{
    string ToolId { get; }
    string Name { get; }

    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}

public record ToolExecutionContext(
    string DocumentContent,
    string Configuration,
    ResolvedScopes Scopes,
    Dictionary<string, object> Parameters);

public record ToolResult(
    bool Success,
    object? Output,
    string? Error,
    TimeSpan ExecutionTime);
```

### 3.2 Handler Implementations

**Priority order (implement first to last):**

| Priority | Handler | Complexity | Notes |
|----------|---------|------------|-------|
| 1 | `SummaryHandler` | Low | Core capability, uses GPT directly |
| 2 | `EntityExtractorHandler` | Medium | Structured output parsing |
| 3 | `DocumentClassifierHandler` | Low | Classification with confidence |
| 4 | `RiskDetectorHandler` | Medium | Risk taxonomy mapping |
| 5 | `ClauseAnalyzerHandler` | High | Section detection + analysis |
| 6 | `ClauseComparisonHandler` | High | Requires RAG context |
| 7 | `DateExtractorHandler` | Low | Date normalization |
| 8 | `FinancialCalculatorHandler` | Medium | Calculation validation |

### 3.3 Handler Registry

```csharp
public class ToolHandlerRegistry
{
    private readonly Dictionary<string, IAiToolHandler> _handlers;

    public IAiToolHandler GetHandler(string toolId)
    {
        if (!_handlers.TryGetValue(toolId, out var handler))
            throw new InvalidOperationException($"Handler not found: {toolId}");
        return handler;
    }

    public IEnumerable<IAiToolHandler> GetHandlers(IEnumerable<AnalysisTool> tools)
    {
        foreach (var tool in tools)
        {
            var handlerType = tool.HandlerClass ?? GetDefaultHandler(tool.Type);
            yield return _handlers[handlerType];
        }
    }
}
```

---

## Phase 4: Service Layer Extension

**Note**: Core services already exist. This phase extends them for playbook-based execution.

### 4.1 Existing Services (DO NOT RECREATE)

| Service | File | Status |
|---------|------|--------|
| PlaybookService | `Services/Ai/PlaybookService.cs` | ✅ Complete |
| ScopeResolverService | `Services/Ai/ScopeResolverService.cs` | ✅ Complete |
| AnalysisOrchestrationService | `Services/Ai/AnalysisOrchestrationService.cs` | ✅ Needs extension |
| ToolHandlerRegistry | `Services/Ai/ToolHandlerRegistry.cs` | ✅ Complete |

### 4.2 Extensions Required

#### 4.2.1 Add Scope List Endpoints (NEW)

Add to `Api/Ai/ScopeEndpoints.cs` (new file):

```csharp
// Scope listing endpoints
app.MapGet("/api/ai/scopes/skills", ListSkills);
app.MapGet("/api/ai/scopes/knowledge", ListKnowledge);
app.MapGet("/api/ai/scopes/tools", ListTools);
app.MapGet("/api/ai/scopes/actions", ListActions);
```

#### 4.2.2 Extend AnalysisOrchestrationService

Add playbook-based execution method:

```csharp
public async Task<AnalysisResult> ExecutePlaybookAsync(
    Guid playbookId,
    string documentContent,
    AnalysisContext context,
    CancellationToken cancellationToken)
{
    // 1. Load playbook
    var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);

    // 2. Resolve all scopes
    var scopes = await _scopeResolver.ResolvePlaybookScopesAsync(playbookId, cancellationToken);

    // 3. Build system prompt from Skills' PromptFragments
    var systemPrompt = BuildSystemPrompt(scopes.Skills);

    // 4. Execute tools based on playbook configuration
    var results = await ExecuteToolsAsync(scopes.Tools, context, cancellationToken);

    // 5. Return combined result
    return CombineResults(playbook, results);
}
```

### 4.3 Existing API Endpoints (DO NOT RECREATE)

| File | Endpoints | Status |
|------|-----------|--------|
| `PlaybookEndpoints.cs` | 8 endpoints for playbook CRUD/sharing | ✅ Complete |
| `AnalysisEndpoints.cs` | 5 endpoints for analysis execution | ✅ Complete |
| `RagEndpoints.cs` | 6 endpoints for RAG search/index | ✅ Complete |

---

## Phase 5: Playbook Assembly

### 5.1 Create Starter Playbooks

Using the definitions from [AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md](AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md#playbook-definitions), create:

| Playbook | Priority | Skills | Actions | Knowledge | Tools |
|----------|----------|--------|---------|-----------|-------|
| PB-001 Quick Review | 1 (MVP) | SKL-008 | ACT-001, ACT-003, ACT-004 | KNW-005 | TL-001, TL-003, TL-004 |
| PB-002 Full Contract | 1 (MVP) | SKL-001, SKL-009, SKL-010 | ACT-001-006 | KNW-001, KNW-003, KNW-004 | TL-001-006 |
| PB-003 NDA Review | 2 | SKL-003, SKL-009 | ACT-001, ACT-002, ACT-004, ACT-005 | KNW-001, KNW-003, KNW-006 | TL-001, TL-002, TL-004, TL-005 |
| PB-010 Risk Scan | 1 (MVP) | SKL-009 | ACT-001, ACT-005 | KNW-004 | TL-001, TL-005 |

**MVP Playbooks** (implement first):
1. PB-001 Quick Review - Universal starting point
2. PB-010 Risk Scan - Fast risk identification
3. PB-002 Full Contract - Comprehensive analysis

### 5.2 Linking Process

For each playbook:

```
1. Create sprk_analysisplaybook record
   → Insert PB-001 "Quick Document Review"

2. Associate Skills via N:N
   → INSERT INTO sprk_playbook_skill (playbookid, skillid)
   → VALUES ({PB-001}, {SKL-008})

3. Associate Knowledge via N:N
   → INSERT INTO sprk_playbook_knowledge (playbookid, knowledgeid)
   → VALUES ({PB-001}, {KNW-005})

4. Associate Tools via N:N
   → INSERT INTO sprk_playbook_tool (playbookid, toolid)
   → VALUES ({PB-001}, {TL-001})
   → VALUES ({PB-001}, {TL-003})
   → VALUES ({PB-001}, {TL-004})
```

### 5.3 Validation

Each playbook must pass validation:

```csharp
public async Task<PlaybookValidationResult> ValidateAsync(SavePlaybookRequest request)
{
    var errors = new List<string>();

    // 1. Must have at least one Skill
    if (request.SkillIds is not { Length: > 0 })
        errors.Add("Playbook must have at least one skill");

    // 2. Skills must exist
    foreach (var skillId in request.SkillIds ?? [])
    {
        var skill = await GetSkillAsync(skillId);
        if (skill == null)
            errors.Add($"Skill {skillId} not found");
    }

    // 3. Tools must have handlers
    foreach (var toolId in request.ToolIds ?? [])
    {
        var tool = await GetToolAsync(toolId);
        if (!_handlerRegistry.HasHandler(tool.HandlerClass))
            errors.Add($"No handler for tool {toolId}");
    }

    return errors.Any()
        ? PlaybookValidationResult.Failure(errors.ToArray())
        : PlaybookValidationResult.Success();
}
```

---

## Phase 6: UI/PCF Implementation

### 6.1 Playbook Selector

**Component**: `PlaybookSelector` PCF

**Purpose**: Allow users to select a playbook for document analysis

**Features**:
- Show suggested playbooks based on document type
- Filter by matter type
- Show playbook description, estimated time, complexity
- "Quick" vs "Full" analysis toggle

```
┌─────────────────────────────────────────────────────────────────┐
│  Select Analysis Playbook                                        │
├─────────────────────────────────────────────────────────────────┤
│  Document: Contract_AcmeCorp_2026.pdf                            │
│  Detected Type: CONTRACT                                         │
│                                                                  │
│  Suggested Playbooks:                                            │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ ○ Quick Document Review           ~30 sec   Low             ││
│  │   Rapid triage with key info extraction                     ││
│  ├─────────────────────────────────────────────────────────────┤│
│  │ ● Full Contract Analysis          ~3 min    High            ││
│  │   Complete review with risk identification                  ││
│  ├─────────────────────────────────────────────────────────────┤│
│  │ ○ Risk-Focused Scan               ~45 sec   Low             ││
│  │   Rapid risk identification only                            ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                  │
│  [Cancel]                                    [Start Analysis]    │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 Playbook Builder

**Component**: `PlaybookBuilder` PCF (Future - not MVP)

**Purpose**: Allow domain experts to create custom playbooks

**Features**:
- Drag-and-drop skill selection
- Knowledge source configuration
- Output format selection
- Preview and test

### 6.3 Analysis Workspace Integration

Update `AnalysisWorkspace` PCF to:
1. Show playbook selection before analysis
2. Display playbook name during/after analysis
3. Allow switching playbooks for re-analysis

---

## Testing Strategy

### Unit Tests

| Component | Test Coverage |
|-----------|---------------|
| Tool Handlers | Each handler with mock inputs |
| PlaybookService | CRUD operations with mock Dataverse |
| ScopeResolver | Scope resolution with test data |
| Validation | Playbook validation rules |

### Integration Tests

| Scenario | Test |
|----------|------|
| Full playbook execution | Load playbook → resolve scopes → execute → validate output |
| Scope resolution | Verify N:N relationships load correctly |
| Handler registry | All handlers registered and executable |

### E2E Tests

| Scenario | Steps |
|----------|-------|
| Quick Review | Upload document → select PB-001 → verify summary output |
| Full Contract | Upload contract → select PB-002 → verify complete analysis |
| Custom Playbook | Create playbook → add scopes → execute → validate |

---

## Rollout Plan

### Phase 1: Internal Alpha

- Deploy to dev environment
- Seed MVP playbooks (PB-001, PB-002, PB-010)
- Internal testing by engineering team

### Phase 2: Beta (Select Customers)

- Deploy to staging/production
- Enable for pilot customers
- Collect feedback on playbook effectiveness
- Iterate on output formats

### Phase 3: GA

- Enable for all customers
- Playbook builder UI (if prioritized)
- Customer success playbook templates
- Documentation and training

---

## Open Questions

1. **Should Skills have N:N with Actions?**
   - Currently Skills reference Actions in their prompt fragment
   - Alternative: Explicit Skill → Action relationship table

2. **How to handle playbook versioning?**
   - Option A: Create new playbook record for each version
   - Option B: Version field with history tracking

3. **Sharing permissions for playbooks?**
   - Currently: Owner + Public flag
   - Future: Team sharing via Dataverse security roles?

4. **Knowledge source management?**
   - Who creates/maintains RAG indexes?
   - How to keep inline content updated?

---

## Changelog

| Version | Date | Change |
|---------|------|--------|
| 1.2 | 2026-01-04 | Integrated detailed entity schema from ai-dataverse-entity-model.md, added field definitions for all scope entities including Action and Playbook, confirmed N:N relationships exist |
| 1.1 | 2026-01-04 | Added Existing Code Inventory section, Dataverse Entity Model section, updated Phase 1 to "Validation" and Phase 4 to "Extension" to reflect existing infrastructure |
| 1.0 | 2026-01-04 | Initial design document created |
