# ADR-013: AI Architecture (Concise)

> **Status**: Accepted (amended 2026-07-05)
> **Domain**: AI/ML Integration
> **Last Updated**: 2026-07-05 (amendment: capability invocation replaces playbook invocation as the canonical facade verb)
> **Updated By**:
> - 2026-05-20 refinement — refined per [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md); categorical "no separate AI microservice" rule replaced with technical criteria; direct CRUD→AI injection prohibited (must use `Services/Ai/PublicContracts/` facades).
> - 2026-07-01 amendment (Path B per CLAUDE.md §6.5) — `IInvokePlaybookAi` facade widened with optional `userContext` + `document` parameters. Motivating consumer: `spaarkeai-compose-r1`.
> - **2026-07-05 amendment (Path B per CLAUDE.md §6.5 — operator-approved, `spaarke-ai-code-audit-r1` ADR review A-1)** — the canonical invocation verb becomes **capability invocation** (`invoke(bindingId, args)` per the Action + Binding model, canonical AI architecture doc v0.4 §6). `IInvokePlaybookAi` is grandfathered as a legacy shim over capability invocation and retires with its callers per the migration map. ALL boundary rules (BFF-hosted criteria, PublicContracts facade discipline, no direct CRUD→AI injection) carry over UNCHANGED — this amendment corrects the VERB the boundary protects, not the boundary. The stale architecture-map appendix is replaced by a pointer to the canonical doc. Rationale: the playbook-shaped canon steered every compliant consumer toward playbook-centricity (see `projects/spaarke-ai-code-audit-r1/ADR-REVIEW-VS-GREENFIELD.md` §2.1).

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
- **MUST** treat **capability invocation** (`invoke(bindingId, args)`) as the canonical facade verb for new consumers (2026-07-05 amendment). `IInvokePlaybookAi` remains valid ONLY for existing callers during migration — do NOT wire new consumers to it, and do NOT create bypass paths around the facade to reach `IPlaybookOrchestrationService` or the capability executor directly.
- **MUST** update the reflection guard test (`PhaseAVerticalSliceTests.ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface`) with a NAMED allow-list entry + citation when adding NEW types to the facade surface; the guard follows the capability-invocation facade as it lands. Silent bypass is forbidden per CLAUDE.md §6.5.

### ❌ MUST NOT

- **MUST NOT** create a separate AI microservice **without documented evidence** that all four exception criteria are met AND a successor ADR amends this one
- **MUST NOT** call Azure AI services directly from PCF
- **MUST NOT** host AI BFF synthesis/streaming endpoints in Azure Functions (Functions are permitted only for out-of-band integration — see ADR-001)
- **MUST NOT** expose API keys to clients
- **MUST NOT** add new direct CRUD→AI dependencies; new external consumers MUST go through `Services/Ai/PublicContracts/` facades

---

## Architecture Overview

The component architecture lives in the canonical doc — do NOT rely on a
snapshot here (the pre-2026-07-05 appendix had rotted: it listed dead
`Chat/Tools/*` classes and the retiring `AnalysisOrchestrationService`).

**Canonical**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
§4 (three entry paths, session ledger, execution shapes) + §5 (target
components T-01..T-14 with Fulfilled-by mappings). Per-component migration
state: `projects/spaarke-ai-code-audit-r1/OVERLAY-MATRIX.md`.

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
| [ADR-029](ADR-029-bff-publish-hygiene.md) | BFF publish hygiene — codifies publish-debt prevention (linux-x64 framework-dependent, sourcemap exclusion, transitive CVE override pattern, size baseline). Does NOT bind extraction policy — that's this ADR. |
| [Compose redline derived-views](../../docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md) | Worked example of envelope-only ownership: `OutputRouter`/`ChatEndpoints` store + ship the compose payload opaquely and never parse it; all redline views (visual diff, `confidence_band`, offsets) derive client-side at render. |

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
