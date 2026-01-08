# Document Profile Field Mapping - Investigation Plan

**Status**: Architecture Documentation Complete - Ready for Implementation
**Date**: 2026-01-08
**Context**: Manual testing revealed partial field population issue
**Next Step**: Create docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md after context cleanup

---

## 📋 Playbook Architecture (Complete Explanation)

This section documents the complete playbook architecture for creating the formal architecture document.

### Overview

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

### Component Details

#### 1. Actions (System Prompt Templates)

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

#### 2. Skills (Prompt Fragments)

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

#### 3. Knowledge (RAG Data Sources)

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

#### 4. Tools (Executable Handlers)

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

#### 5. Outputs (Field Mappings)

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

### Execution Flow (Complete)

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

### Data Flow by Output Path

#### Path 1: Analysis Output (Display)

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

#### Path 2: Document Profile Fields (Storage)

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
Extract from Data JSON (line 1382):
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

### Component Interaction Diagram

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

### Critical Implementation Files

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

### ADR Compliance

This architecture follows:
- **ADR-013**: AI Tool Framework with extensible handlers
- **ADR-014**: Dual storage pattern (Analysis Output + Document fields)
- **ADR-015**: Observability (Application Insights logging at each step)
- **ADR-016**: Soft failure handling (partial storage allowed)

---

## 🔍 Current Issue (Context from Screenshots)

After Document Profile playbook execution:
- ✅ **File Summary** (`sprk_FileSummary`) - POPULATED (contains JSON entities)
- ❌ **TL;DR** - Shows "---" (not populated)
- ❌ **File Keywords** - Shows "---" (not populated)
- ❌ **Extract Document Type** - Shows "---" (not populated)

**Screenshot Analysis**:
- File Summary field shows JSON entities array (not formatted summary text)
- Suggests mapping mismatch or extraction issue

---

## 📊 Current Architecture (As Implemented)

### 1. Tool Execution Flow

**Location**: `AnalysisOrchestrationService.ExecutePlaybookAsync()` (lines 1166-1547)

```
Document Upload
    ↓
Playbook: "Document Profile" (PB-011)
    ↓
Tools Execute:
    - EntityExtractorHandler (TL-001)
    - DocumentClassifierHandler (TL-003)
    - SummaryHandler (TL-004)
    ↓
Each tool returns ToolResult:
    - Data: JsonElement (structured output)
    - Summary: string (human-readable text)
    ↓
executedToolResults list collects successful results
```

### 2. Output Extraction Logic

**Location**: `AnalysisOrchestrationService` lines 1358-1491

**EntityExtractorHandler** (lines 1379-1393):
```csharp
if (HandlerId == "EntityExtractorHandler")
{
    // Expects: { "entities": [...], "totalCount": N, "typeCounts": {...} }
    if (root.TryGetProperty("entities", out var entitiesValue))
    {
        structuredOutputs["Entities"] = JsonSerializer.Serialize(entitiesValue);
    }
}
```

**SummaryHandler** (lines 1395-1429):
```csharp
if (HandlerId == "SummaryHandler")
{
    // Expects: { "fullText": "...", "wordCount": N, "sections": {...} }
    if (root.TryGetProperty("fullText", out var fullTextValue))
    {
        structuredOutputs["TL;DR"] = ExtractTldr(summaryText);
        structuredOutputs["Summary"] = summaryText;
        structuredOutputs["Keywords"] = ExtractKeywordsFromSections(sectionsValue);
    }
}
```

**DocumentClassifierHandler** (lines 1431-1448):
```csharp
if (HandlerId == "DocumentClassifierHandler")
{
    // Expects: { "documentType": "...", "confidence": 0.95 }
    if (root.TryGetProperty("documentType", out var docTypeValue))
    {
        structuredOutputs["Document Type"] = docType;
    }
}
```

### 3. Field Mapping

**Location**: `DocumentProfileFieldMapper.GetFieldName()` (lines 18-29)

```csharp
return outputTypeName?.ToLowerInvariant() switch
{
    "tl;dr" => "sprk_tldr",
    "summary" => "sprk_summary",
    "keywords" => "sprk_keywords",
    "document type" => "sprk_documenttype",
    "entities" => "sprk_entities",
    _ => null
};
```

### 4. Dataverse Update

**Location**: `StoreDocumentProfileOutputsAsync()` (lines 1052-1163)

```csharp
// Step 3: Map outputs to sprk_document fields
var fieldMapping = DocumentProfileFieldMapper.CreateFieldMapping(structuredOutputs);
// Returns: { "sprk_tldr": "...", "sprk_summary": "...", etc. }

await _dataverseService.UpdateDocumentFieldsAsync(documentId, fieldMapping, ct);
```

---

## ❓ Critical Questions to Answer

### Question 1: Field Name Mismatch?

**User mentioned**: "sprk_FileSummary"
**Code uses**: "sprk_summary"

**ACTION NEEDED**: Verify actual Dataverse field names:
- What is the API name for "File Summary" field?
- What is the API name for "TL;DR" field?
- What is the API name for "File Keywords" field?
- What is the API name for "Extract Document Type" field?

### Question 2: Which Tools Actually Executed?

**Diagnostic Logs to Check** (Application Insights):

```
Search for these log entries in order:

1. "Extracting structured outputs from {ToolCount} tool results"
   → How many tools returned results?

2. "Tool {ToolName} has no structured data"
   → Which tools had null Data?

3. "Extracted Entities output from EntityExtractorHandler: {Length} characters"
   → Did EntityExtractorHandler extraction succeed?

4. "Extracted TL;DR output: {Length} characters"
   → Did TL;DR extraction succeed?

5. "Extracted Summary output: {Length} characters"
   → Did Summary extraction succeed?

6. "Extracted Keywords output: {Length} characters"
   → Did Keywords extraction succeed?

7. "Extracted Document Type output: {DocumentType}"
   → Did DocumentClassifierHandler extraction succeed?

8. "Extracted {OutputCount} structured outputs for Document Profile storage: {OutputTypes}"
   → What was the final count and which output types were included?
```

### Question 3: What's in Analysis Output Tab?

**Need to check**: Does Analysis Output RTF field show:
- ✅ Formatted text (expected): "Found 5 entities: - Organization: MONTE ROSA..."
- ❌ Raw JSON (problem): `{"entities":[...]}`

---

## 🧩 Possible Root Causes

### Hypothesis 1: Only EntityExtractorHandler Executed
**Evidence**: File Summary has JSON entities
**Implication**: SummaryHandler and DocumentClassifierHandler may have failed or not executed
**Check**: Tool execution logs, handler validation failures

### Hypothesis 2: Field Name Mismatch
**Evidence**: User said "sprk_FileSummary" but code uses "sprk_summary"
**Implication**: UpdateDocumentFieldsAsync may be setting wrong field names
**Check**: Dataverse entity metadata, actual field API names

### Hypothesis 3: Tool Data Structure Mismatch
**Evidence**: Extraction expects specific JSON structure (fullText, entities, etc.)
**Implication**: Tools may return different structure than expected
**Check**: Actual tool Data JSON in logs (line 1369 logs raw JSON)

### Hypothesis 4: Wrong Output Being Mapped
**Evidence**: Entities JSON appears in File Summary field
**Implication**: Entities output might be mapped to wrong field
**Check**: Field mapping logic, CreateFieldMapping() return values

---

## 🔧 Diagnostic Steps (Before Making Changes)

### Step 1: Get Application Insights Logs
```
Time range: Last document upload timestamp
Filter: Analysis execution for the uploaded document

Required log entries:
- [Extracting structured outputs]
- [Extracted Entities output]
- [Extracted TL;DR output]
- [Extracted Summary output]
- [Extracted Keywords output]
- [Extracted Document Type output]
- Final: "Extracted {N} structured outputs: {types}"
```

### Step 2: Verify Dataverse Field Names
```
Query sprk_document entity metadata:
- TL;DR field → actual API name?
- File Summary field → actual API name?
- File Keywords field → actual API name?
- Extract Document Type field → actual API name?
- File Entities field → actual API name?
```

### Step 3: Check Analysis Output Tab
```
Navigate to Analysis tab in UI
Check what appears in Analysis Output RTF field:
- Formatted text or JSON?
- If JSON, which tool's output?
```

### Step 4: Review Tool Handler Implementations
```
Verify these handlers are returning expected Data structure:
- EntityExtractorHandler.cs (line 162): resultData = new EntityExtractionResult
- SummaryHandler.cs (line 175): resultData = ParseSummaryResult(summaryText, config)
- DocumentClassifierHandler.cs: What does it return in Data?
```

---

## 🎯 Next Actions (After Investigation)

**DO NOT PROCEED until we have:**

1. ✅ Application Insights logs confirming which tools executed
2. ✅ Actual Dataverse field API names
3. ✅ Confirmation of what's in Analysis Output tab
4. ✅ Understanding of which hypothesis is correct

**Then we can:**
- Fix field name mapping if mismatch found
- Fix extraction logic if tool Data structure is different
- Add error handling if tools are failing
- Update output mapping if wrong outputs going to wrong fields

---

## 📝 Files to Review

**Already Reviewed**:
- `AnalysisOrchestrationService.cs` - Main orchestration and extraction logic
- `DocumentProfileFieldMapper.cs` - Output type → field name mapping
- `EntityExtractorHandler.cs` - Entity extraction tool
- `SummaryHandler.cs` - Summary generation tool
- `playbooks.json` - Document Profile playbook config
- `output-types.json` - Output type definitions

**Need to Review**:
- `DocumentClassifierHandler.cs` - Document type classification tool
- Dataverse entity metadata for `sprk_document`
- Application Insights logs for recent execution

---

## 💾 Context Preservation for Post-Compaction

**Modified Files This Session**:
- `AnalysisOrchestrationService.cs` (+~150 lines)
  - Added executedToolResults list (line 1268)
  - Added output extraction logic (lines 1358-1491)
  - Added ExtractTldr() helper (lines 1400-1441)
  - Added ExtractKeywordsFromSections() helper (lines 1447-1508)

**Build Status**: ✅ Compiles successfully (0 warnings, 0 errors)

**Not Yet Deployed**: Changes only in local build, not pushed to Azure

**Next Session Resume Point**: Wait for user to provide diagnostic information before proceeding with fixes
