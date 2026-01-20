# Semantic Search Copilot Integration - Deferred

> **Status**: Deferred (R1 scope is backend API only)
> **Date**: 2026-01-20
> **Related Task**: 031 - Test Copilot tool integration manually

---

## Background

Task 031 was defined to test "Copilot" integration with the semantic search tool. Investigation revealed that **no Copilot UI currently invokes this tool**.

## What Was Built (R1)

### 1. SemanticSearchToolHandler

**Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SemanticSearchToolHandler.cs`

A tool handler implementing `IAnalysisToolHandler` that enables AI-driven document search:

```csharp
public sealed class SemanticSearchToolHandler : IAnalysisToolHandler
{
    public string HandlerId => "SemanticSearchHandler";

    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "search_documents",
        Description: "Search documents in knowledge bases using semantic similarity and keyword matching.",
        ...
    );
}
```

**Tool Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | The search query text |
| `scope` | string | No | "entity" or "documents" (default: "entity") |
| `entity_type` | string | No | Matter, Project, Invoice, Account, Contact |
| `entity_id` | string | No | Parent entity GUID |
| `document_ids` | array | No | Specific document IDs (when scope="documents") |
| `limit` | integer | No | Max results 1-50 (default: 10) |
| `filters` | object | No | documentTypes, fileTypes, tags, dateRange |

### 2. Auto-Registration via Tool Framework

**Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs`

The handler is **auto-registered** via assembly scanning when `AddToolFramework()` is called in Program.cs (line 474):

```csharp
// Discovers and registers all IAnalysisToolHandler implementations
services.AddToolHandlersFromAssembly(Assembly.GetExecutingAssembly());
```

### 3. Semantic Search Service

**Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/`

The underlying search service that powers both:
- Direct API endpoint (`POST /api/ai/search`)
- Tool handler (when invoked via tool framework)

---

## What Does NOT Exist (Deferred to Future)

### No Conversational UI Integration

The Spaarke codebase has **two separate AI systems**:

| System | Service | UI | Uses SemanticSearchToolHandler? |
|--------|---------|-----|--------------------------------|
| Playbook Builder AI | `IAiPlaybookBuilderService` | AiAssistantModal | **No** - Uses OpenAI function calling for canvas ops |
| Document Analysis Tools | `IAnalysisToolHandler` framework | Various document PCFs | **Potentially** - but no "search" UI exists |

### Missing Integration Points

1. **No chat interface** that would invoke `search_documents` tool
2. **No tool discovery UI** to show available tools to users
3. **No conversational context** that passes search results to an LLM for natural language responses

---

## Future Integration Options

### Option A: Add to Playbook Builder AI Assistant

Extend `AiPlaybookBuilderService` to recognize search intents and invoke `SemanticSearchToolHandler`:
- Add `SearchKnowledge` intent to intent classification
- Route search requests through the tool framework
- Format results conversationally

### Option B: Create Standalone Search Copilot

Build a new AI chat interface specifically for document search:
- Similar to AiAssistantModal but for search
- Invokes `search_documents` tool
- Synthesizes results with LLM

### Option C: Integrate with Analysis Workspace

Add search capabilities to the existing Analysis Workspace PCF:
- "Ask questions about documents" feature
- Uses tool framework to invoke search
- Displays results inline

---

## How to Find This Code Later

**Search terms**:
- `SemanticSearchToolHandler` - The tool handler class
- `search_documents` - The tool name (Metadata.Name)
- `IAnalysisToolHandler` - The interface all tool handlers implement
- `AddToolFramework` - Where tools are registered

**Key files**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SemanticSearchToolHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs`

---

## R1 Deliverables (Complete)

What R1 DID deliver:
- [x] `SemanticSearchService` - Core hybrid search with RRF fusion
- [x] `POST /api/ai/search` - Direct API endpoint
- [x] `SemanticSearchToolHandler` - Tool framework integration (ready for future UI)
- [x] Index schema with parent entity fields
- [x] Authorization filter for entity-scoped security

What R1 DEFERRED:
- [ ] Conversational UI invoking the tool
- [ ] Manual Copilot testing (Task 031)

---

*Documented by Claude Code during project wrap-up phase.*
