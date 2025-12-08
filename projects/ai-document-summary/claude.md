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

### Completed (Code Implementation)
1. **Phase 1**: Infrastructure & Configuration ✅
2. **Phase 2**: Text Extraction Service ✅
3. **Phase 3**: Summarization Service ✅
4. **Phase 4**: API Endpoints ✅
5. **Phase 5**: Dataverse Schema ✅
6. **Phase 6**: Frontend Integration ✅
7. **Phase 7**: Document Intelligence (PDF/DOCX/Images) ✅
8. **Phase 8**: Production Hardening ✅

### Remaining (Deployment & Testing)
9. **Phase 10**: Deployment - Deploy BFF API, configure Key Vault, deploy Dataverse/PCF
10. **Phase 11**: Functional Testing - API tests, PCF tests, UAT
11. **Phase 12**: Wrap-up - Final documentation, cleanup

## Deployment Targets

| Component | Target | Notes |
|-----------|--------|-------|
| BFF API | Azure App Service (`sprk-bff-api`) | New AI endpoints, services |
| Secrets | Azure Key Vault | `ai-openai-*`, `ai-docintel-*` |
| Dataverse | Spaarke solution | 3 new fields on `sprk_document` |
| PCF | Universal Quick Create solution | Updated with AI Summary checkbox |

## Key Vault Secrets Required

| Secret | Purpose |
|--------|---------|
| `ai-openai-endpoint` | Azure OpenAI resource URL |
| `ai-openai-key` | Azure OpenAI API key |
| `ai-docintel-endpoint` | Document Intelligence endpoint (optional) |
| `ai-docintel-key` | Document Intelligence key (optional) |
