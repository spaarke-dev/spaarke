# Microsoft Agent Framework — Fit Assessment for Spaarke (R1)

> **Project**: agent-framework-fit-assessment-r1
> **Type**: Research + decision document (no code changes; reads code to ground analysis)
> **Created**: 2026-06-03
> **Owner**: Ralph Schroeder
> **Pattern this follows**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — the assessment that refined ADR-013 in 2026-05

---

## 1. Goal

Produce a single written decision document — `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` — that answers, for every current and likely-future Spaarke AI surface:

1. **Should Spaarke adopt Microsoft Agent Framework (`Microsoft.Agents.AI`) here?** Yes / No / Partial — with rationale.
2. **Where is it a good fit, and where is it not?** Decision criteria + per-surface analysis.
3. **How specifically should we deploy and surface its agents** in surfaces where adoption is recommended? In-process BFF / separate MCP server / Function / hosted Foundry Agent / mixed — keyed to ADR-013 + ADR-001 + the BFF-extensions constraint.

The assessment is **decision-grade**, not exploratory — its conclusions are intended to (a) inform a future ADR-013 amendment if warranted, (b) refine or reshape the parked [`agent-framework-knowledge-r1`](../agent-framework-knowledge-r1/) curation project, and (c) set the standard for how all R-series projects approach AI agent work going forward.

## 2. Why this matters now

1. **Spaarke is in a half-adopted state.** Code under `Services/Ai/Chat/` uses `Microsoft.Extensions.AI` primitives (`IChatClient`, `AIFunction`, `ChatResponseUpdate`) directly. That is the **foundation** Agent Framework builds on — not Agent Framework itself. `SprkChatAgent.cs` declares itself an "Agent Framework agent" in its doc-comment but never instantiates `ChatClientAgent` or extends `AIAgent`. The team needs to decide: lift to Agent Framework proper, or stay on raw Extensions.AI?
2. **Multiple competing surfaces are in flight.** JPS playbooks (`AnalysisOrchestrationService`), Foundry Agent Service (in knowledge base for durable / HITL / A2A work), MCP App surface (M365 Copilot), the Insights Engine MCP server, the Builder agent. Each is a candidate for Agent Framework adoption — or for an explicit "no, not here" decision. Without an assessment, every R-series project will re-litigate this question.
3. **The platform is genuinely new** (early 2026 release). Agent Framework subsumes Semantic Kernel + AutoGen. Engineers (and Claude) will reach for SK / AutoGen idioms unless guidance is canonical.
4. **ADR-013 was refined in 2026-05** via the BFF AI extraction assessment. The pattern works. This assessment uses the same shape to refine the picture further.

## 3. Scope and non-goals

### In scope — Spaarke AI surfaces evaluated

Per the scoping decision (2026-06-03), assessment covers **all current + likely-future** AI agent surfaces:

| # | Surface | Current state | Owner ref |
|---|---|---|---|
| S1 | **SprkChat** — conversational agent | In production. `SprkChatAgent` wraps raw `IChatClient` + middleware pipeline (`AgentTelemetry`/`AgentContentSafety`/`AgentCostControl`) + `AIFunctionFactory` tools | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` |
| S2 | **AnalysisOrchestration + JPS playbooks** — deterministic multi-step pipelines | In production. JPS-driven node graph executed by `AnalysisOrchestrationService` over `IPlaybookExecutionEngine` | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` |
| S3 | **Builder agent** — intent classification + tool routing for AI playbook construction | In flight. `BuilderAgentService` + `BuilderToolDefinitions` + `BuilderToolExecutor` | `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/` |
| S4 | **Background AI jobs** — Service Bus-driven AI work (analysis, indexing, summarization) | In production. 13+ job handlers under `Services/Jobs/` and `Services/Ai/Jobs/` | `src/server/api/Sprk.Bff.Api/Services/Jobs/`, `Services/Ai/Jobs/` |
| S5 | **Foundry Agent Service overlap** — durable / HITL / A2A multi-agent work | Curated as a topic (`knowledge/foundry-agent-service/`); no Spaarke production code yet | `knowledge/foundry-agent-service/` |
| S6 | **M365 Copilot / Declarative Agent surface** | Active project (`ai-m365-copilot-integration`); MCP/plugin-based exposure of BFF to Copilot | `projects/ai-m365-copilot-integration/` |
| S7 | **Insights Engine MCP server** | Active project (`ai-spaarke-insights-engine-r1`); exposes AI capability to external consumers | `projects/ai-spaarke-insights-engine-r1/` |
| S8 | **Future agent surfaces** — any pattern not above that emerges during research | TBD | — |

### Out of scope

- **Implementing any code changes.** Read-only on `.cs` files; the assessment cites them, doesn't change them.
- **ADR amendments / new ADRs.** Per scoping decision, the deliverable is the assessment document only. Any ADR change is a downstream decision after human review of the assessment.
- **Refining `agent-framework-knowledge-r1` SPEC.** Per scoping decision, deferred to a follow-up action after the assessment lands.
- **Per-surface code refactors.** The assessment recommends — it does not refactor.
- **Cost / licensing analysis** beyond what affects the fit decision (e.g., Foundry SKU costs are noted as inputs to S5 but the assessment does not produce a TCO model).

## 4. Decision criteria framework

Each surface is evaluated against the criteria below. These come directly from ADR-013, ADR-001, [`bff-extensions.md`](../../.claude/constraints/bff-extensions.md), and the platform-design tradeoff space documented in upstream Agent Framework guidance.

### 4a. Technical fit criteria

| Criterion | Question |
|---|---|
| **Conversational vs. deterministic** | Is the surface open-ended chat / agentic with LLM-driven steps? Or is it a fixed-graph pipeline whose steps are known? |
| **Latency budget** | Does the surface have a <500ms TTFB requirement against BFF state? (ADR-013 keep-in-BFF criterion) |
| **State + transactional coupling** | Does the surface share session / audit / safety state with the BFF request lifecycle? (ADR-013 keep-in-BFF criterion) |
| **Durability** | Does the work need to survive process restart, span days, or wait on external HITL signals? (Foundry territory) |
| **Tool composition** | Does the surface need dynamic tool registration + LLM-driven routing? Or are tools statically wired? |
| **External consumer** | Does the surface need to be consumable by external clients (Copilot, MCP consumers, A2A peers)? |
| **Streaming** | Does the surface require SSE / IAsyncEnumerable streaming over LLM tokens? |

### 4b. Agent Framework value criteria

For each surface, what does `Microsoft.Agents.AI` actually add over raw `Microsoft.Extensions.AI`?

| Feature | Value-add over raw Extensions.AI |
|---|---|
| `ChatClientAgent` / `AIAgent` base | Common agent surface for composition, registries, A2A proxies |
| Sessions | Service-side or in-memory state management (vs. roll-your-own ChatHistoryManager) |
| Context providers | Pluggable retrieval-augmented context (vs. ad-hoc context builders) |
| Middleware framework | Standard `IChatClient.AsBuilder().Use*().Build()` composition (Spaarke partially uses this) |
| Structured outputs | Schema-bound response parsing |
| Workflows | Graph-based multi-agent orchestration with checkpoints, HITL, supersteps |
| MCP client | First-class MCP server consumption |
| A2A proxies | First-class remote agent invocation |
| Observability | Built-in OTel source-name conventions |

### 4c. Migration cost criteria

| Criterion | Question |
|---|---|
| **Surface-level code change** | How much of the existing surface needs to be rewritten? |
| **Package / dependency impact** | What does adoption do to the BFF publish-size budget (per [`bff-extensions.md`](../../.claude/constraints/bff-extensions.md))? Any new HIGH-CVE risk? |
| **Test impact** | Does existing test infrastructure (mocks, fakes, integration tests) survive the change? |
| **Team learning curve** | Is the surface idiomatic for engineers familiar with `Microsoft.Extensions.AI` / Semantic Kernel / AutoGen? |
| **Observability impact** | Does the change preserve / improve / degrade the current OTel + Application Insights wiring? |
| **Reversibility** | If adoption goes badly here, is the rollback cheap? |

### 4d. Deployment-model criteria (for surfaces where adoption is recommended)

Per ADR-013, the default deployable is the BFF in-process. Exceptions require all four of:

1. No latency coupling with BFF synthesis
2. No transactional coupling with BFF session/safety/audit state
3. Bounded, well-defined integration surface
4. Separating does not require duplicating latency-sensitive components

Plus the agent-specific deployment choices:

| Model | When |
|---|---|
| **In-process BFF** | Default per ADR-013; chat-style + analysis-style; latency-bound |
| **MCP server (e.g., `Sprk.Insights.Mcp`)** | External consumers (Copilot, third-party agents); thin facade over an existing BFF engine |
| **Azure Function (event-driven)** | Sync / extraction / scheduled — already permitted by ADR-001 for non-AI; ADR-013 carve-out for AI sync work |
| **Hosted Foundry Agent** | Durable / HITL / A2A / multi-agent composition with workflow state that needs to survive |

## 5. Deliverable

**One document**: `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md`

Modeled on `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`. Required sections:

1. **Executive summary** — one-page conclusion: which surfaces adopt, which don't, why
2. **Context and scope** — surfaces evaluated, what's in scope / out of scope
3. **Current state inventory** — what each surface currently uses (table)
4. **Agent Framework feature map** — what Agents.AI provides over Extensions.AI; which features Spaarke would actually use
5. **Per-surface decision matrix** — for each of S1–S8, the criteria from §4 applied, recommendation + rationale
6. **Deployment model recommendations** — for adopt-surfaces, deploy how
7. **Migration cost + risks** — aggregated across recommended adoptions
8. **Open questions / human-decision points** — anything the assessment surfaces but does not decide (e.g., "JPS-vs-Workflows is a design call that should go to the architecture group")
9. **Forward-references** — what this assessment unblocks (e.g., refining `agent-framework-knowledge-r1` SPEC; potential ADR-013 amendment)

**Quality bar**: Honest, citation-backed, decision-grade. Mirrors the BFF AI extraction assessment's tone: "structurally X but operationally Y" framing; per-criterion answers with concrete evidence; explicit "we considered X and rejected it because Y" reasoning.

## 6. Execution approach

- **POML-decomposed** — 8 tasks across 6 phases
- **Standard `task-execute` per task**
- **Read-only on `src/`** — assessment cites code, does not change it
- **Synthesis happens in task 006** — analysis tasks (001-005) feed structured findings into the synthesis document
- **Review iteration** in task 007 — explicit guard against assessment-by-intuition; runs adr-check + the senior-AI-dev hat-shift to challenge conclusions
- **Sign-off** in task 008 — also writes the parking-release note for `agent-framework-knowledge-r1` recommending SPEC adjustments

## 7. Phases and tasks

| Phase | Tasks | Purpose |
|---|---|---|
| **0. Primary-source baseline** | 000 | Re-pull microsoft/agent-framework at HEAD; WebFetch live Microsoft Learn pages; sweep Devblogs + GitHub Issues/Discussions for content dated 2026-04-01 onwards; lock the source baseline to current date. **All downstream tasks depend on this — no curated-snapshot-only citations permitted in §4-§7.** |
| **1. Inventory current state** | 001, 002 | Read Spaarke AI code surfaces + non-BFF AI touchpoints; produce structured findings tables |
| **2. Agent Framework feature mapping** | 003 | Map Microsoft.Agents.AI + Workflows surface area against Spaarke needs; every feature claim grounded in live URLs from notes/00 |
| **3. Per-surface decision analysis** | 004 | Apply §4 criteria to each of S1–S8; produce the per-surface decision matrix |
| **4. Deployment + migration** | 005 | For adopt-surfaces: deployment model recommendation + aggregated migration cost / risks |
| **5. Synthesis** | 006 | Write `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` with §10 Sources appendix mandatory |
| **6. Review + sign-off** | 007, 008 | Adversarial review + source recency re-check + final sign-off + unblock note for `agent-framework-knowledge-r1` |

### Primary-source discipline (the project's quality contract)

The user's binding constraint: this is a new area of capability, so sources must be very recent. Translated into project rules:

- **Recency floor**: every primary-source citation in §4-§7 of the assessment document must have fetched-date no older than 60 days from synthesis date. Older citations permitted only for foundational pages that don't rev (overview/concepts), ADRs, Spaarke internal docs — with explicit "stable content" justification inline.
- **No curated-snapshot-only citations**: the `knowledge/agent-framework/` snapshot is pinned at 2026-05-14 SHA — by synthesis date that's 3+ weeks stale. Citations may use the curated snapshot for orientation but EVERY assessment-level claim must trace to a live URL captured by task 000 (or task 003 re-fetch). Task 006 acceptance criteria enforce ≥80% citations dated 2026-04-01 onwards.
- **§10 Sources appendix mandatory** in the assessment document — complete table of every cited primary source with URL + fetched date + section references. Acts as the freshness audit trail for future REFRESH cycles.
- **Adversarial review re-fetches** — task 007 re-WebFetches the top 5 most-cited URLs at review time and treats any material change as a finding (revise conclusion or add §8 open question).

## 8. Acceptance criteria (project-level)

- [ ] `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` exists with all 10 required sections (9 from §5 + §10 Sources appendix)
- [ ] Per-surface decision matrix has a row for each of S1–S7 (S8 is optional if no novel surface surfaced during research)
- [ ] Every recommendation cites concrete evidence — either a `.cs` file with line numbers, an ADR, a constraint doc, or a **primary-source URL with fetched date**
- [ ] **Recency audit**: ≥80% of primary-source citations dated 2026-04-01 onwards; older citations have inline "stable content" justification
- [ ] **§10 Sources appendix** tables every primary source URL + fetched date + referencing section
- [ ] Assessment surfaces at least 3 open questions for human-decision (i.e., the assessment does NOT claim to decide everything autonomously)
- [ ] Adversarial review (task 007) ran AND source recency re-check ran (top 5 cited URLs re-WebFetched at review time)
- [ ] `projects/agent-framework-knowledge-r1/README.md` parking notice is updated with a forward-pointer to the landed assessment
- [ ] A short unblock-recommendation note is written into `projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md` outlining what SPEC changes the assessment implies — but does not edit the SPEC itself (per scoping decision: SPEC refinement is downstream)

## 9. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Assessment biases toward adopting Agent Framework because the work is fun to do | Medium | Task 007 (adversarial review) explicitly considers "what would I write if I were arguing against adoption everywhere"; honest "no, not here" conclusions required where evidence supports them |
| Surfaces missed in scoping (e.g., a non-obvious AI touchpoint elsewhere in BFF) | Low | Task 001 grep-driven inventory of Services/Ai/* + ADR-013 file structure references; if a new surface surfaces, S8 catches it |
| Assessment too abstract to actually guide future projects | Medium | Required citations to concrete `.cs` paths; per-surface deployment recommendation must be specific enough to write a SPEC against |
| ADR-013 amendment question deferred but never resolved | Low | Section 9 "Forward-references" explicitly names the ADR-013 amendment question as an open item even if the assessment doesn't decide it |
| Future Agent Framework versions invalidate assessment conclusions | Medium | Assessment dated; monthly REFRESH-PROCEDURE will flag if upstream changes invalidate any conclusion |

## 10. References

- **Assessment-style template**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)
- **Binding constraints**: [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md), [`.claude/adr/ADR-001-minimal-api.md`](../../.claude/adr/ADR-001-minimal-api.md), [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md), [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)
- **Existing AI curation** (provides upstream Agent Framework context): [`knowledge/agent-framework/`](../../knowledge/agent-framework/), [`knowledge/foundry-agent-service/`](../../knowledge/foundry-agent-service/)
- **Spaarke AI architecture overview**: [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md) (if exists), [`docs/guides/SPAARKE-AI-ARCHITECTURE.md`](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) (referenced by Sprk.Bff.Api CLAUDE.md)
- **Spaarke AI code (read-only — assessment cites these)**: `src/server/api/Sprk.Bff.Api/Services/Ai/`
- **Parked downstream project**: [`projects/agent-framework-knowledge-r1/`](../agent-framework-knowledge-r1/)
- **Active competing projects**: [`projects/ai-m365-copilot-integration/`](../ai-m365-copilot-integration/), [`projects/ai-spaarke-insights-engine-r1/`](../ai-spaarke-insights-engine-r1/)
- **Upstream**: [`microsoft/agent-framework`](https://github.com/microsoft/agent-framework), [`learn.microsoft.com/en-us/agent-framework/`](https://learn.microsoft.com/en-us/agent-framework/)
