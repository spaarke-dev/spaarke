# AI Playbook Architecture

> **Version**: 1.0
> **Last Updated**: January 8, 2026
> **Author**: AI Summary and Analysis Enhancements Team
> **Related ADRs**: ADR-013, ADR-014, ADR-015, ADR-016

---

## Table of Contents

- [Introduction](#introduction)
- [Overview](#overview)
- [Component Details](#component-details)
  - [1. Actions (System Prompt Templates)](#1-actions-system-prompt-templates)
  - [2. Skills (Prompt Fragments)](#2-skills-prompt-fragments)
  - [3. Knowledge (RAG Data Sources)](#3-knowledge-rag-data-sources)
  - [4. Tools (Executable Handlers)](#4-tools-executable-handlers)
  - [5. Outputs (Field Mappings)](#5-outputs-field-mappings)
- [Execution Flow](#execution-flow)
- [Data Flow by Output Path](#data-flow-by-output-path)
- [Component Interaction Diagram](#component-interaction-diagram)
- [Critical Implementation Files](#critical-implementation-files)
- [ADR Compliance](#adr-compliance)
- [Limitations and Future Enhancements](#limitations-and-future-enhancements)

---

## Introduction

The **AI Playbook Architecture** is Spaarke's extensible framework for orchestrating document analysis workflows using Azure OpenAI. This architecture enables composable, reusable analysis capabilities through a **declarative playbook system** that combines prompts, domain knowledge, and executable tools.

### Key Design Principles

1. **Separation of Concerns**: Prompts (Actions/Skills) are decoupled from execution logic (Tools)
2. **Composability**: Playbooks combine multiple actions, skills, and tools to create complex workflows
3. **Extensibility**: New tool handlers can be added without modifying core orchestration logic
4. **Dual Storage**: Results are stored in both human-readable (RTF) and structured (entity fields) formats
5. **Observability**: Application Insights logging at every step for debugging and monitoring

### Use Cases

- **Document Profile**: Auto-generate document metadata on upload (entities, summary, classification)
- **Contract Analysis**: Extract key terms, obligations, and risks from legal documents
- **Compliance Review**: Verify documents against regulatory requirements and internal policies
- **Custom Workflows**: Compose playbooks for domain-specific analysis needs

---

## Overview

A **Playbook** orchestrates AI analysis workflows using 5 scopes that work together:

```
Playbook
├── Actions     → System prompt templates (instructions for LLM behavior)
├── Skills      → Prompt fragments (specialized guidance added to prompts)
├── Knowledge   → RAG data sources (context documents provided to LLM)
├── Tools       → Executable handlers (call LLM, process responses)
└── Outputs     → Field mappings (where to store results in Dataverse)
```

**Key Principle**: Actions and Skills provide prompts. Tools execute them. Outputs map results.

---

## Component Details

### 1. Actions (System Prompt Templates)

**Location**: `scripts/seed-data/actions.json`
**Entity**: `sprk_analysisactions`

**Structure**:
```json
{
  "id": "ACT-004",
  "sprk_name": "Summarize Content",
  "sprk_description": "Generate a concise summary...",
  "sprk_sortorder": 4,
  "actionType": "03 - Summarization",
  "sprk_systemprompt": "You are a document summarization specialist..."
}
```

**Purpose**:
- Defines LLM behavior and response format
- Contains structured prompt template with sections (## Summary Structure, ## Guidelines, etc.)
- Maps to specific analysis types (Extraction, Classification, Summarization, Analysis, Comparison)

**Not Executable**: Actions don't call APIs - they provide instructions that Tools use

**Example Action Types**:
- ACT-001: Extract Entities (extraction specialist)
- ACT-003: Classify Document (classification specialist)
- ACT-004: Summarize Content (summarization specialist)

---

### 2. Skills (Prompt Fragments)

**Location**: `scripts/seed-data/skills.json`
**Entity**: `sprk_analysisskills`

**Structure**:
```json
{
  "id": "SKL-008",
  "sprk_name": "Executive Summary",
  "sprk_description": "Generate a concise, high-level overview...",
  "skillType": "01 - Document Analysis",
  "sprk_promptfragment": "## Executive Summary Instructions\n\n1. One-Paragraph Overview..."
}
```

**Purpose**:
- Adds specialized instructions to the base Action prompt
- Refines behavior for specific document types or analysis contexts
- Combined with Action.SystemPrompt when building prompts

**Composition**: Final Prompt = Action.SystemPrompt + Skill.PromptFragment(s)

**Example Skills**:
- SKL-001: Contract Analysis (comprehensive contract examination)
- SKL-008: Executive Summary (high-level overview generation)

---

### 3. Knowledge (RAG Data Sources)

**Location**: `scripts/seed-data/knowledge.json`
**Entity**: `sprk_analysisknowledge`

**Structure**:
```json
{
  "id": "KNL-001",
  "sprk_name": "Standard Contract Clauses",
  "knowledgeType": "Reference Library",
  "sprk_content": "...",
  "sprk_contenturl": "https://..."
}
```

**Purpose**:
- Provides domain-specific context to the LLM
- Can be embedded text or external documents (RAG retrieval)
- Examples: Standard contract templates, legal definitions, industry benchmarks

**Processing**: Resolved by `IScopeResolver.ResolvePlaybookScopesAsync()` before tool execution

---

### 4. Tools (Executable Handlers)

**Location**: `scripts/seed-data/tools.json` (definitions)
**Handlers**: `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/*.cs` (implementations)

**Tool Definition**:
```json
{
  "id": "TL-004",
  "sprk_name": "Document Summarizer",
  "toolType": "Summary",
  "sprk_handlerclass": "SummaryHandler",
  "sprk_configuration": "{\"format\":\"structured\",\"maxWords\":500}"
}
```

**Tool Handler Interface** (`IAnalysisToolHandler`):
```csharp
public interface IAnalysisToolHandler
{
    string HandlerId { get; }
    ToolHandlerMetadata Metadata { get; }
    IReadOnlyList<ToolType> SupportedToolTypes { get; }

    ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool);
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken ct);
}
```

**Tool Handler Responsibilities**:
1. Build prompt from Action + Skill + document text
2. Call Azure OpenAI via `IOpenAiClient.GetCompletionAsync()`
3. Parse LLM response into structured format
4. Return `ToolResult` with Data (JSON) and Summary (text)

**Example Handlers**:
- `EntityExtractorHandler`: Extracts entities → `EntityExtractionResult`
- `SummaryHandler`: Generates summary → `SummaryResult`
- `DocumentClassifierHandler`: Classifies document type → `ClassificationResult`

**ToolResult Structure**:
```csharp
public record ToolResult
{
    public required string HandlerId { get; init; }        // "SummaryHandler"
    public required Guid ToolId { get; init; }              // TL-004
    public required string ToolName { get; init; }          // "Document Summarizer"
    public required bool Success { get; init; }             // true/false
    public JsonElement? Data { get; init; }                 // Structured JSON output
    public string? Summary { get; init; }                   // Human-readable text
    public double? Confidence { get; init; }                // 0.0-1.0
    public required ToolExecutionMetadata Execution { get; init; }
}
```

**Critical Distinction**:
- `Data`: Structured JSON for extraction/storage (e.g., `{"fullText":"...", "sections":{...}}`)
- `Summary`: Human-readable text for display (e.g., "Found 5 entities: Organization (3), Person (2)...")

---

### 5. Outputs (Field Mappings)

**Location**: Playbook definition (`playbooks.json`)
**Mapper**: `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs`

**Playbook Output Mapping**:
```json
{
  "id": "PB-011",
  "sprk_name": "Document Profile",
  "outputMapping": {
    "tldr": "sprk_document.sprk_tldr",
    "summary": "sprk_document.sprk_summary",
    "keywords": "sprk_document.sprk_keywords",
    "documentType": "sprk_document.sprk_documenttype",
    "entities": "sprk_document.sprk_entities"
  }
}
```

**Field Mapper**:
```csharp
public static string? GetFieldName(string? outputTypeName)
{
    return outputTypeName?.ToLowerInvariant() switch
    {
        "tl;dr" => "sprk_tldr",
        "summary" => "sprk_summary",
        "keywords" => "sprk_keywords",
        "document type" => "sprk_documenttype",
        "entities" => "sprk_entities",
        _ => null
    };
}
```

**Purpose**: Maps extracted output type names to Dataverse field API names

---

## Execution Flow

```
USER: Uploads document to SPE
  ↓
PCF: Calls POST /api/ai/execute-playbook-stream
  ↓
┌──────────────────────────────────────────────────────────────────┐
│ AnalysisOrchestrationService.ExecutePlaybookAsync()             │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Step 1: Load Playbook Configuration                             │
│   ├─ PlaybookService.GetPlaybookAsync(playbookId)              │
│   └─ Returns: Playbook { ActionIds[], SkillIds[], ToolIds[] }  │
│                                                                  │
│ Step 2: Get Document from Dataverse                             │
│   ├─ DataverseService.GetDocumentAsync(documentId)             │
│   └─ Returns: Document { Name, FileName, etc. }                │
│                                                                  │
│ Step 3: Create Analysis Record                                  │
│   ├─ analysisId = Guid.NewGuid()                                │
│   ├─ Analysis { DocumentId, ActionId, Status: "InProgress" }   │
│   └─ _analysisStore[analysisId] = analysis                     │
│                                                                  │
│ Step 4: Resolve Playbook Scopes                                 │
│   ├─ ScopeResolver.ResolvePlaybookScopesAsync(playbookId)      │
│   └─ Returns: { Skills[], Knowledge[], Tools[] }               │
│                                                                  │
│ Step 5: Get Action Definition                                   │
│   ├─ ScopeResolver.GetActionAsync(actionId)                    │
│   └─ Returns: Action { SystemPrompt, Description }             │
│                                                                  │
│ Step 6: Extract Document Text from SPE                          │
│   ├─ ExtractDocumentTextAsync(document, httpContext)           │
│   └─ Returns: string documentText (plain text extracted)       │
│                                                                  │
│ Step 7: Process RAG Knowledge Sources                           │
│   ├─ ProcessRagKnowledgeAsync(knowledge[], documentText)       │
│   └─ Returns: Processed context for LLM                        │
│                                                                  │
│ Step 8: Build Tool Execution Context                            │
│   ├─ Create ToolExecutionContext:                              │
│   │   - AnalysisId, TenantId                                   │
│   │   - DocumentContext { DocumentId, ExtractedText }          │
│   │   - UserContext (additional parameters)                    │
│   └─ Store in analysis.DocumentText, analysis.SystemPrompt     │
│                                                                  │
│ Step 9: Execute Tools from Playbook                             │
│   ├─ foreach (tool in scopes.Tools)                            │
│   │   ├─ Get handler: _toolHandlerRegistry.GetHandlersByType() │
│   │   ├─ Validate: handler.Validate(context, tool)            │
│   │   ├─ Execute: handler.ExecuteAsync(context, tool)         │
│   │   │   ↓                                                     │
│   │   │   ┌────────────────────────────────────────────┐      │
│   │   │   │ Tool Handler (e.g., SummaryHandler)       │      │
│   │   │   ├────────────────────────────────────────────┤      │
│   │   │   │ 1. Build Prompt:                           │      │
│   │   │   │    - Action.SystemPrompt                   │      │
│   │   │   │    + Skill.PromptFragment                  │      │
│   │   │   │    + Document text                         │      │
│   │   │   │                                             │      │
│   │   │   │ 2. Call LLM:                               │      │
│   │   │   │    - IOpenAiClient.GetCompletionAsync()    │      │
│   │   │   │    → Azure OpenAI API                      │      │
│   │   │   │    ← Response (JSON or text)               │      │
│   │   │   │                                             │      │
│   │   │   │ 3. Parse Response:                         │      │
│   │   │   │    - Extract structured data               │      │
│   │   │   │    - Build human-readable summary          │      │
│   │   │   │                                             │      │
│   │   │   │ 4. Return ToolResult:                      │      │
│   │   │   │    Data: JsonElement (structured)          │      │
│   │   │   │    Summary: string (formatted text)        │      │
│   │   │   └────────────────────────────────────────────┘      │
│   │   │                                                        │
│   │   ├─ Collect result: executedToolResults.Add(toolResult)  │
│   │   └─ Stream to client: yield TextChunk(summary)          │
│   │                                                            │
│   └─ After all tools execute:                                 │
│       executedToolResults = [ToolResult, ToolResult, ...]    │
│                                                                │
│ Step 10: Extract Structured Outputs (Document Profile Only)   │
│   ├─ foreach (toolResult in executedToolResults)             │
│   │   ├─ Parse toolResult.Data JSON                          │
│   │   ├─ Map based on HandlerId:                             │
│   │   │   ├─ EntityExtractorHandler → Entities               │
│   │   │   ├─ SummaryHandler → TL;DR, Summary, Keywords       │
│   │   │   └─ DocumentClassifierHandler → Document Type       │
│   │   └─ Populate structuredOutputs dictionary               │
│   │                                                            │
│   └─ structuredOutputs = {                                    │
│         "TL;DR": "This is a...",                              │
│         "Summary": "## Executive Summary\n...",               │
│         "Keywords": "Contract, Agreement, Terms",             │
│         "Document Type": "Service Agreement",                 │
│         "Entities": "[{\"value\":\"Acme\",\"type\":\"Org\"}]" │
│       }                                                        │
│                                                                │
│ Step 11: Map Outputs to Document Fields                       │
│   ├─ DocumentProfileFieldMapper.CreateFieldMapping()          │
│   │   Input: structuredOutputs dictionary                     │
│   │   Output: {                                               │
│   │     "sprk_tldr": "This is a...",                          │
│   │     "sprk_summary": "## Executive Summary...",            │
│   │     "sprk_keywords": "Contract, Agreement...",            │
│   │     "sprk_documenttype": "Service Agreement",             │
│   │     "sprk_entities": "[{...}]"                            │
│   │   }                                                        │
│   │                                                            │
│   └─ DataverseService.UpdateDocumentFieldsAsync()            │
│       └─ PATCH sprk_document(documentId) with field values    │
│                                                                │
│ Step 12: Store Analysis Output (Primary Storage)              │
│   ├─ Build analysisOutput:                                    │
│   │   - RTF formatted text from toolResults.Summary fields   │
│   │   - Includes tool names, sections, structured display    │
│   │                                                            │
│   └─ DataverseService.CreateAnalysisOutputAsync()            │
│       └─ CREATE sprk_analysisoutput record                    │
│           - sprk_output_rtf: Formatted text (for display)     │
│           - sprk_analysisid: Links to analysis                │
│                                                                │
│ Step 13: Complete Analysis                                    │
│   ├─ Update analysis: Status = "Completed"                   │
│   └─ yield Completed(analysisId, tokenUsage)                 │
│                                                                │
└──────────────────────────────────────────────────────────────────┘
  ↓
PCF: Displays streamed results in AiSummaryPanel
  ├─ Analysis Output tab: Shows RTF formatted text
  └─ Document fields: Shows extracted values
```

---

## Data Flow by Output Path

### Path 1: Analysis Output (Display)

```
ToolResult.Summary (string)
  ↓
  "### Entity Extractor
   Found 5 entities:
   - Organization: 3 (Acme Corp, Widget Inc, ...)
   - Person: 2 (John Smith, Jane Doe)"
  ↓
Streamed via SSE: AnalysisStreamChunk.TextChunk()
  ↓
Stored in: sprk_analysisoutput.sprk_output_rtf
  ↓
Displayed in: PCF AiSummaryPanel → Analysis Output tab (RTF field)
```

**Purpose**: Human-readable formatted text for immediate review

### Path 2: Document Profile Fields (Storage)

```
ToolResult.Data (JsonElement)
  ↓
  EntityExtractorHandler: {
    "entities": [
      {"value": "Acme Corp", "type": "Organization", "confidence": 0.95},
      {"value": "John Smith", "type": "Person", "confidence": 0.90}
    ],
    "totalCount": 5,
    "typeCounts": {"Organization": 3, "Person": 2}
  }
  ↓
Extract from Data JSON (AnalysisOrchestrationService.cs:1382):
  if (root.TryGetProperty("entities", out var entitiesValue))
    structuredOutputs["Entities"] = JsonSerializer.Serialize(entitiesValue);
  ↓
Map to field name (DocumentProfileFieldMapper):
  "Entities" → "sprk_entities"
  ↓
Update Document (DataverseService):
  PATCH sprk_document(documentId) { sprk_entities: "[{...}]" }
  ↓
Stored in: sprk_document.sprk_entities
  ↓
Displayed in: Document form → File Entities field
```

**Purpose**: Structured data for downstream processing, reporting, search

---

## Component Interaction Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         Playbook (PB-011)                       │
├─────────────────────────────────────────────────────────────────┤
│ Scopes:                                                         │
│   Actions: [ACT-001, ACT-003, ACT-004]                         │
│   Skills: [SKL-008]                                             │
│   Knowledge: [KNL-001, KNL-002]                                 │
│   Tools: [TL-001, TL-003, TL-004]                               │
│   OutputMapping: { "tldr": "sprk_document.sprk_tldr", ... }   │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│              AnalysisOrchestrationService                       │
└─────────────────────────────────────────────────────────────────┘
         ↓                 ↓                 ↓
    ┌─────────┐      ┌──────────┐     ┌──────────────┐
    │ Actions │      │  Skills  │     │  Knowledge   │
    │ Service │      │  Service │     │  Service     │
    └─────────┘      └──────────┘     └──────────────┘
         │                 │                 │
         └─────────────────┴─────────────────┘
                          ↓
              ┌───────────────────────┐
              │ Build Combined Prompt │
              └───────────────────────┘
                          ↓
         ┌────────────────────────────────────┐
         │   Tool Handler Registry            │
         │   GetHandlersByType(ToolType.*)    │
         └────────────────────────────────────┘
                          ↓
    ┌─────────────────────────────────────────────────┐
    │           Tool Handlers (Execute)               │
    ├─────────────────────────────────────────────────┤
    │  EntityExtractorHandler                         │
    │  SummaryHandler                                 │
    │  DocumentClassifierHandler                      │
    └─────────────────────────────────────────────────┘
                          ↓
              ┌───────────────────────┐
              │   IOpenAiClient       │
              │   GetCompletionAsync  │
              └───────────────────────┘
                          ↓
              ┌───────────────────────┐
              │   Azure OpenAI API    │
              │   (gpt-4o-mini)       │
              └───────────────────────┘
                          ↓
              ┌───────────────────────┐
              │   Parse Response      │
              │   → ToolResult        │
              └───────────────────────┘
                          ↓
         ┌────────────────┴────────────────┐
         ↓                                  ↓
┌────────────────────┐         ┌───────────────────────┐
│ ToolResult.Summary │         │  ToolResult.Data      │
│ (human-readable)   │         │  (structured JSON)    │
└────────────────────┘         └───────────────────────┘
         ↓                                  ↓
┌────────────────────┐         ┌───────────────────────┐
│ Analysis Output    │         │ Extract Outputs       │
│ RTF Field          │         │ (by HandlerId)        │
│ (sprk_output_rtf)  │         └───────────────────────┘
└────────────────────┘                     ↓
                              ┌───────────────────────┐
                              │ DocumentProfileField  │
                              │ Mapper                │
                              └───────────────────────┘
                                          ↓
                              ┌───────────────────────┐
                              │ Update Document       │
                              │ Fields in Dataverse   │
                              │ (sprk_tldr, etc.)     │
                              └───────────────────────┘
```

---

## Critical Implementation Files

| Component | File Path | Key Methods/Properties |
|-----------|-----------|------------------------|
| Orchestrator | `AnalysisOrchestrationService.cs` | `ExecutePlaybookAsync()`, `ExtractTldr()`, `ExtractKeywordsFromSections()` |
| Scope Resolver | `IScopeResolver.cs` / implementation | `ResolvePlaybookScopesAsync()`, `GetActionAsync()` |
| Tool Registry | `IToolHandlerRegistry.cs` / implementation | `GetHandlersByType()`, `GetHandler()` |
| Entity Extractor | `Tools/EntityExtractorHandler.cs` | `ExecuteAsync()`, `BuildSummary()`, `ParseEntitiesFromResponse()` |
| Summary Handler | `Tools/SummaryHandler.cs` | `ExecuteAsync()`, `ParseSummaryResult()`, `ExtractSections()` |
| Classifier | `Tools/DocumentClassifierHandler.cs` | `ExecuteAsync()`, `ParseClassificationResult()` |
| Field Mapper | `DocumentProfileFieldMapper.cs` | `GetFieldName()`, `CreateFieldMapping()` |
| OpenAI Client | `IOpenAiClient.cs` / implementation | `GetCompletionAsync()` |
| Dataverse Service | `IDataverseService.cs` | `GetDocumentAsync()`, `UpdateDocumentFieldsAsync()`, `CreateAnalysisOutputAsync()` |
| Playbook Service | `IPlaybookService.cs` | `GetPlaybookAsync()`, authorization checks |

---

## ADR Compliance

This architecture follows:

- **[ADR-013](../adr/ADR-013-ai-architecture.md)**: AI Tool Framework with extensible handlers
- **[ADR-014](../adr/ADR-014-dual-storage-pattern.md)**: Dual storage pattern (Analysis Output + Document fields)
- **[ADR-015](../adr/ADR-015-ai-observability.md)**: Observability (Application Insights logging at each step)
- **[ADR-016](../adr/ADR-016-soft-failure-handling.md)**: Soft failure handling (partial storage allowed)

---

## Limitations and Future Enhancements

### Current Limitations

#### 1. Document Profile Output Extraction

**Limitation**: Structured output extraction relies on hardcoded handler ID matching and JSON property names.

**Impact**:
- Adding new tool handlers requires code changes to `AnalysisOrchestrationService`
- Renaming tool result properties requires updating extraction logic
- No validation that tool outputs match expected output type schemas

**Mitigation**:
- Document expected JSON structures in tool handler XML comments
- Use Application Insights logs to diagnose extraction failures
- Validate outputs against expected schemas before storage

#### 2. Single LLM Model per Tool

**Limitation**: All tools currently use `gpt-4o-mini` as the default model.

**Impact**:
- Cannot use specialized models for different tasks (e.g., GPT-4 for complex analysis, GPT-3.5 for simple extraction)
- Cannot A/B test different models for quality/cost optimization

**Enhancement Opportunity**:
- Add `ModelName` property to tool configuration
- Allow per-tool model selection in playbook definitions
- Support model fallback chains for resilience

#### 3. Sequential Tool Execution

**Limitation**: Tools execute sequentially in playbook order.

**Impact**:
- Parallel-safe tools (e.g., entity extraction + classification) cannot run concurrently
- Total execution time = sum of individual tool times
- Increased latency for multi-tool playbooks

**Enhancement Opportunity**:
- Add dependency declaration to tool definitions
- Implement parallel execution for independent tools
- Use `Task.WhenAll()` for tools without dependencies

#### 4. Static Playbook Configuration

**Limitation**: Playbooks are defined in seed data and loaded from Dataverse at runtime.

**Impact**:
- Cannot dynamically compose playbooks based on document type
- No conditional tool execution (e.g., "run clause analysis only if document type is contract")
- Difficult to A/B test playbook variations

**Enhancement Opportunity**:
- Implement playbook composition API (combine multiple playbooks)
- Add conditional execution rules (if/then logic based on prior tool results)
- Support user-defined custom playbooks (power users can create their own)

#### 5. Knowledge Source Integration

**Limitation**: RAG knowledge sources are currently placeholder - no actual retrieval implemented.

**Impact**:
- Cannot provide domain-specific context from knowledge bases
- Analysis quality depends solely on prompt engineering
- Missing opportunity for grounding LLM responses in factual data

**Enhancement Opportunity**:
- Implement Azure AI Search integration for semantic search over knowledge docs
- Add vector embeddings for knowledge chunks
- Support external knowledge sources (SharePoint libraries, Azure Blob Storage)

#### 6. Field Mapping Rigidity

**Limitation**: Output-to-field mapping is hardcoded in `DocumentProfileFieldMapper`.

**Impact**:
- Cannot customize field mappings per customer
- Adding new output types requires code deployment
- No support for custom entity fields

**Enhancement Opportunity**:
- Move field mappings to Dataverse configuration entity
- Support custom field mappings via admin UI
- Validate mappings against entity metadata at runtime

### Future Enhancements

#### 1. Streaming Output Extraction

**Current**: All tools must complete before output extraction begins.

**Enhancement**: Extract structured outputs incrementally as tools complete, enabling:
- Partial results displayed faster to users
- Early storage of completed tool outputs
- Better handling of long-running playbooks

#### 2. Tool Result Caching

**Enhancement**: Cache tool results based on document hash + tool configuration:
- Avoid re-running expensive tools on identical documents
- Speed up playbook execution for duplicate uploads
- Reduce Azure OpenAI API costs

#### 3. Playbook Versioning

**Enhancement**: Support multiple playbook versions with gradual rollout:
- Version playbooks in Dataverse (`sprk_version` field)
- A/B test new playbook configurations
- Roll back to previous versions if quality degrades

#### 4. Custom Tool Development UI

**Enhancement**: Allow power users to create custom tools via low-code interface:
- Visual prompt builder for Actions/Skills
- JSON schema editor for output definitions
- Test harness for validating tool behavior

#### 5. Multi-Language Support

**Enhancement**: Extend playbooks to handle non-English documents:
- Detect document language automatically
- Load language-specific Actions/Skills
- Translate outputs to user's preferred language

#### 6. Batch Processing

**Enhancement**: Execute playbooks on multiple documents in parallel:
- Queue-based architecture for bulk uploads
- Progress tracking for batch jobs
- Aggregated reporting across document sets

---

## Related Documentation

- [AI Tool Framework Guide](../guides/SPAARKE-AI-ARCHITECTURE.md)
- [Document Intelligence Integration](../guides/DOCUMENT-INTELLIGENCE-INTEGRATION.md)
- [Azure OpenAI Configuration](auth-AI-azure-resources.md)
- [Dataverse Entity Schema](../dataverse/ENTITY-REFERENCE.md)

---

**Last Review**: January 8, 2026
**Next Review**: March 2026 (or after major architecture changes)
