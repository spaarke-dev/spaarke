# AI Document Summary - Project Context

## Overview

AI-powered document summarization for Spaarke. When users upload documents via Universal Quick Create, an AI summary is automatically generated and stored in `sprk_filesummary`.

## Key Documents

- [spec.md](spec.md) - Full design specification
- [ADR-013](../../docs/reference/adr/ADR-013-ai-architecture.md) - AI Architecture decision
- [SPAARKE-AI-ARCHITECTURE.md](../../docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md) - Implementation guide

## Design Philosophy

Build focused features, extract shared code later. This is NOT a framework - it's a single-purpose AI tool. When we build the 2nd AI tool, we'll extract common patterns.

## Components to Implement

### BFF (Sprk.Bff.Api)

| File | Purpose |
|------|---------|
| `Api/SummarizeEndpoints.cs` | Streaming + enqueue endpoints |
| `Services/Ai/SummarizeService.cs` | Orchestrates summarization |
| `Services/Ai/TextExtractorService.cs` | Text extraction |
| `Services/Ai/OpenAiClient.cs` | Azure OpenAI wrapper |
| `Jobs/SummarizeJobHandler.cs` | Background processing |

### PCF

| File | Purpose |
|------|---------|
| `AiSummaryPanel/` | Embedded summary display |
| `UniversalQuickCreate/` | Integrate AiSummaryPanel |

### Dataverse

| Field | Entity | Purpose |
|-------|--------|---------|
| `sprk_filesummary` | sprk_document | AI-generated summary |
| `sprk_filesummarystatus` | sprk_document | Processing status |
| `sprk_filesummarydate` | sprk_document | When generated |

## ADR Compliance

- ADR-001: Minimal API endpoints ✓
- ADR-004: Job contract for background ✓
- ADR-007: SpeFileStore for files ✓
- ADR-008: Endpoint filter auth ✓
- ADR-009: Redis caching ✓
- ADR-010: DI minimalism ✓
- ADR-013: AI architecture ✓

## Implementation Phases

1. **Phase 1**: Backend services + endpoints
2. **Phase 2**: AiSummaryPanel + UQC integration
3. **Phase 3**: Dataverse schema + deployment
4. **Phase 4**: Document Intelligence for PDF/DOCX
5. **Phase 5**: Production hardening
