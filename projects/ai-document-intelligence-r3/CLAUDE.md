# CLAUDE.md - AI Document Intelligence R3

> **Project**: AI Document Intelligence R3 - AI Implementation
> **Status**: Ready for Implementation
> **Last Updated**: December 25, 2025

---

## Quick Context

R3 implements advanced AI capabilities and production readiness:
- **Hybrid RAG** with 3 deployment models
- **Tool Framework** for dynamic AI tools
- **Playbook System** for reusable configurations
- **Multi-format Export** (DOCX, PDF, Email, Teams)
- **Production Deployment** with monitoring

## Prerequisites

**R1 and R2 must be complete before starting R3:**
- R1: All API infrastructure, services, and Azure resources
- R2: All PCF controls deployed, custom pages working

## Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions for core API |
| ADR-009 | Redis-first caching | Cache embeddings and RAG results |
| ADR-013 | AI Architecture | Tool framework pattern, extend BFF |
| ADR-014 | AI Evaluation | Evaluation pipeline for AI quality |
| ADR-015 | AI Observability | Application Insights telemetry |
| ADR-016 | AI Security | Secure AI tool execution |

## Key Services to Extend

```
src/server/api/Sprk.Bff.Api/Services/Ai/
├── ScopeResolverService.cs      # EXTEND with RAG integration
├── WorkingDocumentService.cs    # EXTEND with SPE integration
├── AnalysisContextBuilder.cs    # Complete (from R1)
└── AnalysisOrchestrationService.cs  # Complete (from R1)
```

## New Services to Create

| Service | Purpose |
|---------|---------|
| IKnowledgeDeploymentService | Manage RAG deployment models |
| IRagService | Hybrid search with semantic ranking |
| IAnalysisToolHandler | Interface for dynamic tools |
| EntityExtractor | Extract entities from documents |
| ClauseAnalyzer | Analyze contract clauses |
| DocumentClassifier | Classify document types |

## Knowledge Resources

### Guides
- `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md` - AI tool framework
- `docs/ai-knowledge/patterns/streaming-endpoints.md` - SSE patterns
- `docs/ai-knowledge/patterns/distributed-cache.md` - Redis patterns

### Azure Resources
- AI Foundry Hub (deployed)
- Azure AI Search (needs RAG enhancement)
- Azure OpenAI (embedding model)

## Skills to Use

| Skill | When to Use |
|-------|-------------|
| `dataverse-deploy` | Deploying playbook forms |
| `adr-check` | Validating AI architecture compliance |
| `push-to-github` | Committing completed phases |

## Phase Overview

| Phase | Tasks | Focus |
|-------|-------|-------|
| 1 | 001-008 | Hybrid RAG Infrastructure |
| 2 | 010-015 | Tool Framework |
| 3 | 020-024 | Playbook System |
| 4 | 030-036 | Export Services |
| 5 | 040-048 | Production Readiness |

## Critical Paths

1. **RAG Critical Path**: 001 → 002 → 003 → 004 → 005 → 006/007 → 008
2. **Tool Path**: 010 → 011 → 012/013/014 → 015
3. **Production Path**: All phases → 040 → 041 → 042 → 043 → 044 → 045 → 046 → 047 → 048

## Success Metrics

- [ ] Hybrid RAG < 500ms P95 latency
- [ ] All 3 deployment models working
- [ ] Playbooks save/load correctly
- [ ] All export formats functional
- [ ] 100+ concurrent analyses pass load test
- [ ] Production deployment healthy
- [ ] Customer deployment guide validated

## Context Recovery

If resuming work, check:
1. `current-task.md` for active task state
2. `tasks/TASK-INDEX.md` for overall progress
3. Git status for uncommitted changes

---

*AI Document Intelligence R3 - AI Implementation*
