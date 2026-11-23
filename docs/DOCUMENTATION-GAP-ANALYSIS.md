# Documentation Gap Analysis: Document Intelligence Service Rename

> **Date**: 2026-04-05
> **Context**: Code-level rename of AI Summarize → Document Intelligence services completed  
> **Status**: Documentation updates completed, gap analysis complete

---

## Summary

All documentation has been successfully updated to reflect the service rename from "Summarize" to "DocumentIntelligence". The following analysis identifies remaining documentation enhancements needed for comprehensive coverage.

---

## Files Updated ✅

### API Documentation
- ✅ `docs/guides/ai-document-summary.md`
  - Updated title to "AI Document Intelligence - Developer Guide"
  - Changed all endpoint paths: `/api/ai/summarize/*` → `/api/ai/document-intelligence/*`
  - Updated configuration sections to use `DocumentIntelligence` namespace
  - Fixed config class references: `Ai` → `DocumentIntelligence`

### Architecture Documentation  
- ✅ `docs/reference/adr/ADR-013-ai-architecture.md`
  - Updated file tree: `AiOptions.cs` → `DocumentIntelligenceOptions.cs`
  - Changed configuration section: `"AiOptions"` → `"DocumentIntelligence"`

- ✅ `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md`
  - Updated all class references: `AiOptions` → `DocumentIntelligenceOptions`
  - Fixed service file tree references
  - Updated constructor signatures and dependency injection examples
  - Corrected class definition header from `AiOptions.cs` to `DocumentIntelligenceOptions.cs`

- ✅ `docs/reference/architecture/SPAARKE-AI-STRATEGY.md`
  - Updated endpoint listing in architecture diagrams

### Project Documentation
- ✅ `projects/ai-document-summary/README.md`
  - Updated all 3 endpoint references
  - Changed workflow descriptions
  - Updated endpoint paths in examples

- ✅ `projects/ai-document-summary/spec.md`
  - Updated architecture diagram: `SummarizeService` → `DocumentIntelligenceService`
  - Changed file structure: `SummarizeEndpoints.cs` → `DocumentIntelligenceEndpoints.cs`
  - Updated service class files: `SummarizeService` → `DocumentIntelligenceService`, `SummarizeJobHandler` → `DocumentAnalysisJobHandler`
  - Changed model files: `SummarizeRequest` → `DocumentAnalysisRequest`, `SummarizeResponse` → `DocumentAnalysisResponse`, `SummarizeChunk` → `AnalysisChunk`
  - Updated endpoint definitions (3 endpoints)
  - Changed job type constant: `"ai-summarize"` → `"ai-analyze"`
  - Updated MapGroup route in examples
  - Fixed configuration class references in OpenAiClient

- ✅ `projects/ai-document-summary/plan.md`
  - Updated all service file names
  - Changed model class names in code examples
  - Updated method signatures: `SummarizeStreamingAsync` → `AnalyzeStreamingAsync`

---

## Code References Verified ✅

The following **code-level** changes were already completed (documentation now matches):

### Service Classes
- `SummarizeService.cs` → `DocumentIntelligenceService.cs`
- `ISummarizeService.cs` → `IDocumentIntelligenceService.cs`  
- `SummarizeJobHandler.cs` → `DocumentAnalysisJobHandler.cs`
- `SummarizeEndpoints.cs` → `DocumentIntelligenceEndpoints.cs`

### Models
- `SummarizeRequest.cs` → `DocumentAnalysisRequest.cs`
- `SummarizeResult.cs` → `AnalysisResult.cs`
- `SummarizeChunk.cs` → `AnalysisChunk.cs`
- `SummarizeJobContract.cs` → `DocumentAnalysisJobContract.cs`
- `SummarizeResponse.cs` → `DocumentAnalysisResponse.cs`
- `SummarizeStatus.cs` → `AnalysisStatus.cs`
- `SummarizeMetadata.cs` → `AnalysisMetadata.cs`

### Test Files  
- `SummarizeServiceTests.cs` → `DocumentIntelligenceServiceTests.cs`
- `SummarizeEndpointsTests.cs` → `DocumentIntelligenceEndpointsTests.cs`
- `SummarizeJobHandlerTests.cs` → `DocumentAnalysisJobHandlerTests.cs`
- `SummarizeRequestValidatorTests.cs` → `DocumentAnalysisRequestValidatorTests.cs`
- `SummarizeIntegrationTests.cs` → `DocumentIntelligenceIntegrationTests.cs`

### Configuration
- `AiOptions.cs` → `DocumentIntelligenceOptions.cs`
- Configuration section: `"Ai"` → `"DocumentIntelligence"`

### API Endpoints
- `/api/ai/summarize/stream` → `/api/ai/document-intelligence/analyze`
- `/api/ai/summarize/enqueue` → `/api/ai/document-intelligence/enqueue`
- `/api/ai/summarize/enqueue-batch` → `/api/ai/document-intelligence/enqueue-batch`

### Job Types
- Job type constant: `"ai-summarize"` → `"ai-analyze"`

---

## Documentation Gaps Identified 🔍

### 1. Missing: Structured Output Format Documentation

**Gap**: `ai-document-summary.md` describes summarization but doesn't document the **structured JSON output** format that DocumentIntelligenceService now returns.

**Required**:
- Document the TL;DR bullet points format
- Document keywords extraction
- Document entity extraction (organizations, people, amounts, dates, references)
- Document matching suggestions for Dataverse records
- Provide JSON schema for structured response

**Suggested Location**: Add new section to `docs/guides/ai-document-summary.md`:
```markdown
## Structured Output Format

### Analysis Result Schema
The DocumentIntelligenceService returns structured JSON with the following format:

{
  "summary": "Prose narrative summary...",
  "tldr": [
    "Key point 1",
    "Key point 2",
    "Key point 3"
  ],
  "keywords": ["finance", "quarterly", "revenue"],
  "entities": {
    "organizations": ["Contoso Corp", "Fabrikam Inc"],
    "people": ["John Smith", "Jane Doe"],
    "amounts": ["$1.2M", "€500K"],
    "dates": ["Q4 2025", "December 15"],
    "references": ["Contract #12345", "Invoice-2025-001"]
  },
  "matchingSuggestions": [
    {
      "recordType": "account",
      "recordId": "guid",
      "confidence": 0.85,
      "matchReason": "Organization name match"
    }
  ]
}
```

### 2. Missing: Configuration Migration Guide

**Gap**: No guide for migrating from old `Ai` configuration section to new `DocumentIntelligence` section.

**Required**:
- Step-by-step migration instructions
- appsettings.json before/after examples
- Breaking changes list
- Backward compatibility notes (if any)

**Suggested Location**: Add section to `docs/guides/ai-document-summary.md` or create `docs/guides/ai-migration-guide.md`

### 3. Missing: Entity Extraction Details

**Gap**: Entity extraction is mentioned but not documented in detail.

**Required**:
- What entity types are extracted?
- What AI prompts are used?
- How are entities normalized?
- How does matching work?
- What is the confidence scoring algorithm?

**Suggested Location**: Add to `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md` under "Entity Extraction Service" section

### 4. Missing: Dataverse Record Matching Logic

**Gap**: `projects/ai-file-entity-metadata-extraction/` mentions "suggest matching Dataverse records" but logic is undocumented.

**Required**:
- How are target record types selected?
- What matching algorithms are used? (exact match, fuzzy match, semantic search?)
- How are confidence scores calculated?
- What Dataverse tables are queried?
- How are permissions handled (user can only see records they have access to)?

**Suggested Location**: Add to `projects/ai-file-entity-metadata-extraction/spec.md` under new "Matching Algorithm" section

### 5. Incomplete: Error Handling Documentation

**Gap**: `ai-document-summary.md` lists some errors but doesn't cover:
- Entity extraction failures
- Matching service failures  
- Structured output validation errors
- Azure AI Search errors (if used for matching)

**Required**: Expand error handling section with these scenarios

### 6. Missing: Performance Benchmarks

**Gap**: No documented performance expectations for DocumentIntelligenceService.

**Required**:
- Typical processing time per document (by file type)
- Entity extraction latency
- Matching service latency
- Token usage estimates
- Rate limit recommendations

**Suggested Location**: Add "Performance" section to `docs/guides/ai-document-summary.md`

### 7. Missing: Testing Guide for DocumentIntelligence Features

**Gap**: Test files renamed but no testing guide for new structured output features.

**Required**:
- How to test entity extraction
- How to test matching suggestions
- How to validate structured output
- Mock data for testing
- Integration test examples

**Suggested Location**: Create `docs/guides/ai-testing-guide.md` or add to `tests/README.md`

### 8. Missing: OpenAPI/Swagger Specification

**Gap**: API endpoints documented in markdown but no OpenAPI spec.

**Required**:
- OpenAPI 3.0 specification for all DocumentIntelligence endpoints
- Request/response schemas
- Authentication requirements
- Rate limits
- Error responses

**Suggested Location**: Create `docs/api/openapi.yaml` and reference from `ai-document-summary.md`

---

## Recommendations

### High Priority 🔴
1. **Document Structured Output Format** - Critical for API consumers
2. **Configuration Migration Guide** - Required for existing deployments
3. **Entity Extraction Details** - Core feature undocumented

### Medium Priority 🟡  
4. **Dataverse Record Matching Logic** - Important for understanding behavior
5. **Expand Error Handling** - Improves developer experience
6. **Testing Guide** - Needed for quality assurance

### Low Priority 🟢
7. **Performance Benchmarks** - Nice to have for capacity planning
8. **OpenAPI Specification** - Useful but markdown docs currently sufficient

---

## Next Steps

1. **Review this analysis** with product team to validate gaps
2. **Prioritize documentation work** based on user needs
3. **Create tasks** for high-priority documentation gaps
4. **Assign owners** for each documentation enhancement
5. **Set deadlines** aligned with feature release schedule

---

## Related Documents

- [SPAARKE-AI-ARCHITECTURE.md](architecture/AI-ARCHITECTURE.md)
- [ai-document-summary.md](guides/ai-document-summary.md)
- [ADR-013-ai-architecture.md](adr/ADR-013-ai-architecture.md)
- [ai-file-entity-metadata-extraction/spec.md](../projects/x-ai-file-entity-metadata-extraction/spec.md)
