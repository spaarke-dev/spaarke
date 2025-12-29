# AI & Document Intelligence Patterns Index

> **Domain**: AI Tool Framework, Document Analysis
> **Last Updated**: 2025-12-19

---

## When to Load

Load these patterns when:
- Implementing new AI analysis features
- Adding document extraction capabilities
- Creating streaming AI endpoints
- Working with Azure OpenAI integration
- Configuring analysis actions/skills/knowledge

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    AI Tool Framework (ADR-013)                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────┐      ┌──────────────────────────────────┐ │
│  │  AiToolAgent    │ SSE  │      AnalysisEndpoints           │ │
│  │  (PCF Control)  │ ──── │  /api/ai/analysis/execute        │ │
│  └─────────────────┘      │  /api/ai/analysis/{id}/continue  │ │
│                           └──────────────────────────────────┘ │
│                                         │                       │
│                                         ▼                       │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │              AnalysisOrchestrationService                  │ │
│  │  1. Resolve scopes (Actions, Skills, Knowledge)           │ │
│  │  2. Download file from SPE                                │ │
│  │  3. Extract text (native/DocIntel/Vision)                 │ │
│  │  4. Build context (system + user prompts)                 │ │
│  │  5. Stream to Azure OpenAI                                │ │
│  │  6. Parse structured output                               │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Available Patterns

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [streaming-endpoints.md](streaming-endpoints.md) | SSE streaming to clients | ~110 |
| [text-extraction.md](text-extraction.md) | Multi-format document extraction | ~100 |
| [analysis-scopes.md](analysis-scopes.md) | Actions, Skills, Knowledge | ~90 |

---

## Canonical Source Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | SSE streaming endpoints |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Main orchestrator |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | Prompt construction |
| `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` | Azure OpenAI with circuit breaker |
| `src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs` | Document extraction |
| `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` | All settings |

---

## Processing Routes

| File Type | Method | Model |
|-----------|--------|-------|
| TXT, MD, JSON, CSV | Native read | gpt-4o-mini |
| PDF, DOCX, DOC | Document Intelligence | gpt-4o-mini |
| PNG, JPG, GIF, TIFF | Vision OCR | gpt-4o (vision) |
| EML, MSG | Email parsing | gpt-4o-mini |

---

## Key Configuration

| Setting | Default | Purpose |
|---------|---------|---------|
| SummarizeModel | gpt-4o-mini | Primary model |
| MaxOutputTokens | 1000 | Response limit |
| Temperature | 0.3 | Deterministic output |
| MaxFileSizeBytes | 10MB | Upload limit |
| MaxConcurrentStreams | 3 | Per-user limit |

---

**Lines**: ~90
