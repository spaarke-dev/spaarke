# CLAUDE.md — AI Resource Activation & Integration (R3)

> **Project**: ai-spaarke-platform-enhancments-r3
> **Branch**: work/ai-resource-activation-r3
> **Created**: 2026-03-04

## Project Context

This project activates underutilized Azure AI resources built in R1 Phases 1-4. The core change is creating a **tiered knowledge architecture**:

- **Skills** (JPS instructions in `sprk_systemprompt`) — Tell the LLM what to do
- **L1 Golden References** (`spaarke-rag-references` index) — Domain language via dedicated RAG index
- **L2 Customer Documents** (`spaarke-knowledge-index-v2`) — Similar prior work via main RAG index
- **L3 Entity Context** (`spaarke-records-index`) — Business entity awareness

## Key Files

| File | Purpose |
|------|---------|
| `Services/Ai/Handlers/GenericAnalysisHandler.cs` | Fix hardcoded `ModelName = "gpt-4o"` → model resolution chain |
| `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Add knowledge retrieval before OpenAI call |
| `Services/Ai/PlaybookOrchestrationService.cs` | Propagate knowledge context through pipeline |
| `Services/Ai/RagService.cs` | Add `SearchReferencesAsync()` for dedicated reference index |
| `Services/Ai/RagIndexingPipeline.cs` | Discovery-index validation + legacy vector cleanup |
| `Services/Ai/ModelSelector.cs` | Verify operation-type routing (already exists) |
| `Services/RecordMatching/DataverseIndexSyncService.cs` | Bulk sync + tenantId field |
| `Services/Ai/ReferenceIndexingService.cs` | NEW — chunk + embed + index knowledge sources |
| `Api/Ai/AdminKnowledgeEndpoints.cs` | NEW — admin endpoints for reference indexing |

## Applicable ADRs

- **ADR-001**: Minimal API for all endpoints; BackgroundService for async
- **ADR-004**: Job contract with idempotency for indexing jobs
- **ADR-008**: Endpoint filters for admin endpoint authorization
- **ADR-009**: Redis caching for knowledge retrieval results
- **ADR-010**: DI minimalism — ≤15 non-framework registrations
- **ADR-013**: AI services extend BFF; no separate microservice
- **ADR-014**: Tenant-scoped cache keys; no content logging
- **ADR-019**: ProblemDetails with errorCode for all errors

## Constraints

- All index queries MUST include `tenantId` filter
- Knowledge retrieval MUST NOT add >500ms to action execution (p95)
- No document content or model responses in logs
- New reference index uses `contentVector3072` only (no legacy 1536-dim)
- Admin endpoints require authorization endpoint filters

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Dedicated `spaarke-rag-references` index | Golden references need guaranteed retrieval; would be buried in 100K+ customer docs |
| Skills ≠ Knowledge | Skills = JPS instructions (what to do); Knowledge = vectorized domain language (improves understanding) |
| Model resolution chain: node → ModelSelector → default | Flexible without breaking existing playbooks |
| Redis session cache for RAG results | Multiple nodes in same playbook query same knowledge; avoid duplicate calls |

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

When user says "work on task X", "continue", "next task", etc.:
1. Check `tasks/TASK-INDEX.md` for next pending task
2. Invoke `task-execute` skill with task file path
3. Let task-execute orchestrate the full protocol
