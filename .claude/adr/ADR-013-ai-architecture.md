# ADR-013: AI Architecture (Concise)

> **Status**: Accepted
> **Domain**: AI/ML Integration
> **Last Updated**: 2025-12-18

---

## Decision

Extend **Sprk.Bff.Api** with AI endpoints following established patterns. Use Azure OpenAI, AI Search, and Document Intelligence as foundation.

**Rationale**: Unified BFF approach avoids separate microservice complexity while maintaining ADR compliance.

---

## Constraints

### ✅ MUST

- **MUST** follow ADR-001 Minimal API patterns for AI endpoints
- **MUST** use endpoint filters for AI authorization (ADR-008)
- **MUST** use Redis caching for expensive AI results (ADR-009)
- **MUST** use Job Contract for background AI work (ADR-004)
- **MUST** access files through SpeFileStore only (ADR-007)
- **MUST** apply rate limiting to all AI endpoints

### ❌ MUST NOT

- **MUST NOT** create separate AI microservice
- **MUST NOT** call Azure AI services directly from PCF
- **MUST NOT** use Azure Functions for AI processing
- **MUST NOT** expose API keys to clients

---

## Architecture Overview

```
Sprk.Bff.Api/
├── Api/Ai/
│   ├── DocumentIntelligenceEndpoints.cs  ← /api/ai/document-intelligence/*
│   ├── AnalysisEndpoints.cs              ← /api/ai/analysis/*
│   └── RecordMatchEndpoints.cs           ← record matching
├── Services/Ai/
│   ├── DocumentIntelligenceService.cs    ← summarization/extraction
│   ├── AnalysisOrchestrationService.cs   ← orchestration + SSE
│   └── TextExtractorService.cs           ← text extraction
└── Services/Jobs/Handlers/
    └── DocumentAnalysisJobHandler.cs     ← JobType: "ai-analyze"
```

### Endpoint Patterns

| Endpoint | Purpose | Pattern |
|----------|---------|---------|
| `/analyze` | SSE streaming analysis | Sync + streaming |
| `/enqueue` | Background analysis | Async job (ADR-004) |
| `/enqueue-batch` | Batch processing | Async job (ADR-004) |

**See**: [AI Endpoint Pattern](../patterns/ai/endpoint-registration.md)

---

## Deployment Models

| Model | Description | Resource Isolation |
|-------|-------------|-------------------|
| Model 1 | Spaarke-Hosted SaaS | Shared resources, per-tenant index |
| Model 2 | Customer-Hosted | Dedicated resources per customer |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Minimal API patterns |
| [ADR-004](ADR-004-job-contract.md) | Async job contract |
| [ADR-007](ADR-007-spefilestore.md) | File access via facade |
| [ADR-008](ADR-008-endpoint-filters.md) | Authorization filters |
| [ADR-009](ADR-009-redis-caching.md) | Caching strategy |
| [ADR-014](ADR-014-ai-caching.md) | AI-specific caching |
| [ADR-015](ADR-015-ai-data-governance.md) | Data governance |
| [ADR-016](ADR-016-ai-rate-limits.md) | Rate limits |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-013-ai-architecture.md](../../docs/adr/ADR-013-ai-architecture.md)

For detailed context including:
- Complete file structure and endpoint registration
- Caching strategy tables
- Authorization filter implementation
- Job handler examples
- Model 1 vs Model 2 configuration
- Azure resource requirements
- Security considerations

---

**Lines**: ~100

