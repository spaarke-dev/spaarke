# ADR-013: AI Architecture (Concise)

> **Status**: Accepted
> **Domain**: AI/ML Integration
> **Last Updated**: 2026-05-20
> **Updated By**: Refined per [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — the categorical "no separate AI microservice" rule is replaced with technical criteria. Bulk of AI still lives in BFF for documented latency + transactional reasons; specific exceptions are now permitted.

---

## Decision

**Default: extend `Sprk.Bff.Api` with AI endpoints in-process.** The bulk of AI synthesis, chat, RAG, safety, capability routing, session persistence, and orchestration lives in BFF because these workloads have **tight latency budgets (<50ms routing, <100ms RAG, <500ms streaming TTFB) and transactional coupling** (streaming + retroactive safety annotation + Cosmos session writes share one request lifecycle) that a service boundary would break.

**Exceptions** (separate deployable is permitted) when ALL of the following hold:
1. The workload has **no latency coupling** with BFF synthesis (no <500ms TTFB requirement against BFF state)
2. The workload has **no transactional coupling** with BFF session/safety/audit state
3. The workload has a **bounded, well-defined integration surface** (HTTP contract, MCP tools, etc.)
4. Separating it does **not require duplicating** latency-sensitive components in both processes

Workloads meeting all four:
- Azure Functions for sync/extraction/scheduled work (already permitted by ADR-001; Insights Engine sync pipelines are the canonical example)
- An MCP server (e.g., `Sprk.Insights.Mcp`) exposing AI capabilities to external consumers like M365 Copilot — DESIGN-TIME consideration, not pre-decided

**Rationale**: The 2026-05-20 BFF AI extraction assessment found the codebase is structurally AI-dominant (69% LOC, 5.2× churn) but operationally well-justified for unified BFF: 100% of streaming endpoints are AI; routing/safety/session components require in-process coupling. Extracting existing AI code would force either latency degradation, component duplication, or both. Categorical rejection of separation, however, was too strong — specific narrow-scope deployables (Functions, MCP server) ARE permitted when the technical criteria above are met.

---

## Constraints

### ✅ MUST

- **MUST** follow ADR-001 Minimal API patterns for AI endpoints
- **MUST** use endpoint filters for AI authorization (ADR-008)
- **MUST** use Redis caching for expensive AI results (ADR-009)
- **MUST** use Job Contract for background AI work (ADR-004)
- **MUST** access files through SpeFileStore only (ADR-007)
- **MUST** apply rate limiting to all AI endpoints
- **MUST** flow ChatHostContext through the full chat pipeline when provided
- **MUST** use RagSearchOptions boolean filters for knowledge source scoping
- **MUST** keep new AI synthesis/chat/orchestration in BFF unless ALL four exception criteria above are met
- **MUST** route external CRUD-side AI consumers (Finance, Workspace, Jobs, etc.) through documented facade types in `Services/Ai/PublicContracts/` — do not inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types directly into CRUD code

### ❌ MUST NOT

- **MUST NOT** create a separate AI microservice **without documented evidence** that all four exception criteria are met AND a successor ADR amends this one
- **MUST NOT** call Azure AI services directly from PCF
- **MUST NOT** host AI BFF synthesis/streaming endpoints in Azure Functions (Functions are permitted only for out-of-band integration — see ADR-001)
- **MUST NOT** expose API keys to clients
- **MUST NOT** add new direct CRUD→AI dependencies; new external consumers MUST go through `Services/Ai/PublicContracts/` facades

---

## Architecture Overview

```
Sprk.Bff.Api/
├── Api/Ai/
│   ├── ChatEndpoints.cs                  ← /api/ai/chat/* (sessions, messages, playbooks)
│   ├── DocumentIntelligenceEndpoints.cs  ← /api/ai/document-intelligence/*
│   ├── AnalysisEndpoints.cs              ← /api/ai/analysis/*
│   └── RecordMatchEndpoints.cs           ← record matching
├── Models/Ai/Chat/
│   ├── ChatSession.cs                    ← session record with HostContext
│   ├── ChatContext.cs                    ← context + ChatKnowledgeScope
│   └── ChatHostContext.cs                ← entity-aware host context
├── Services/Ai/
│   ├── PublicContracts/                  ← NEW (per sdap-bff-api-remediation-fix Outcome E)
│   │   └── IBffAiPublicContracts.cs      ← facade for external CRUD consumers
│   ├── IRagService.cs / RagService.cs    ← RAG search with boolean filter logic
│   ├── DocumentIntelligenceService.cs    ← summarization/extraction
│   ├── AnalysisOrchestrationService.cs   ← orchestration + SSE
│   ├── TextExtractorService.cs           ← text extraction
│   ├── Jobs/                             ← AI-coupled job handlers (moved from Services/Jobs/)
│   └── Chat/
│       ├── ChatSessionManager.cs          ← session lifecycle
│       ├── IChatContextProvider.cs        ← context resolution interface
│       ├── PlaybookChatContextProvider.cs ← playbook-driven context + entity scope
│       ├── SprkChatAgentFactory.cs        ← agent construction
│       └── Tools/
│           ├── DocumentSearchTools.cs     ← entity-scoped search
│           └── KnowledgeRetrievalTools.cs ← knowledge source-scoped retrieval
└── Services/Jobs/                        ← FRAMEWORK ONLY (dispatcher); AI-coupled handlers moved to Services/Ai/Jobs/
```

---

## Deployment Models

| Model | Description | Resource Isolation |
|-------|-------------|-------------------|
| Model 1 | Spaarke-Hosted SaaS | Shared resources, per-tenant index |
| Model 2 | Customer-Hosted | Dedicated resources per customer |

---

## Decision Criteria for Future Service-Boundary Questions

Before adding new AI functionality, ask:

| Question | Answer → BFF | Answer → Separate Deployable Candidate |
|---|---|---|
| Does it have a TTFB / latency budget against BFF state (<500ms)? | YES | NO |
| Does it write to BFF-managed session/audit/safety state in the same request? | YES | NO |
| Does it require retroactive annotation of streaming responses? | YES | NO |
| Is it event-driven (timer, queue, webhook) with no synchronous user wait? | NO | YES |
| Is it a thin facade (e.g., MCP tools) over an existing well-bounded engine? | (consider) | (consider) |

All four "BFF" answers → BFF. Three or four "Separate" answers + concrete justification → write a successor ADR.

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Minimal API patterns; defines out-of-band Functions permitted scope |
| [ADR-004](ADR-004-job-contract.md) | Async job contract |
| [ADR-007](ADR-007-spefilestore.md) | File access via facade |
| [ADR-008](ADR-008-endpoint-filters.md) | Authorization filters |
| [ADR-009](ADR-009-redis-caching.md) | Caching strategy |
| [ADR-014](ADR-014-ai-caching.md) | AI-specific caching |
| [ADR-015](ADR-015-ai-data-governance.md) | Data governance |
| [ADR-016](ADR-016-ai-rate-limits.md) | Rate limits |
| ADR-029 (forthcoming, BFF publish hygiene) | Codifies publish-debt prevention; does NOT bind extraction policy — that's this ADR |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-013-ai-architecture.md](../../docs/adr/ADR-013-ai-architecture.md)

**Extraction assessment evidence**: [docs/assessments/bff-ai-extraction-assessment-2026-05-20.md](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)

For detailed context including:
- Complete file structure and endpoint registration
- Caching strategy tables
- Authorization filter implementation
- Job handler examples
- Model 1 vs Model 2 configuration
- Azure resource requirements
- Security considerations

---

**Lines**: ~120
