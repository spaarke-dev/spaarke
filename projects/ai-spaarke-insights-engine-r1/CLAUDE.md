# CLAUDE.md — Spaarke Insights Engine (project context)

> **Project-scoped instructions.** Loads when working in `projects/ai-spaarke-insights-engine-r1/` and all related code paths. Companion to root [CLAUDE.md](../../CLAUDE.md).

---

## What this project is

**Spaarke's context production service** — produces, persists, and serves structured contextual claims about the organization's work, with provenance, confidence where applicable, and evidence-sufficiency rules where applicable. Phase 1 ships real Observation production from real documents end-to-end via universal layered ingest, plus one synthesis question (`predict-matter-cost`) over real Observations + SME-authored Precedents.

**Acceptance bar**: a single API call returns either a structurally-honest grounded Inference or a structured `DeclineResponse` — composed from Observations extracted from actual SPE documents. Not infrastructure with mock data.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. Do not read POML files directly and implement manually.

Per root [CLAUDE.md §4](../../CLAUDE.md), the `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check at Step 9.5)
- ✅ Progress recoverable after compaction
- ✅ PCF version bumping + deployment skills invoked correctly

### Trigger phrases that invoke task-execute

| User says | Action |
|---|---|
| "work on task X" | Invoke task-execute with task X POML |
| "continue" / "keep going" / "next task" | Read TASK-INDEX.md, find first 🔲, invoke task-execute |
| "continue with task X" / "resume task X" | Invoke task-execute with task X POML |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Parallel task execution

When tasks can run in parallel (per TASK-INDEX.md parallel groups), each task STILL uses `task-execute`. Pattern: ONE message with MULTIPLE Skill tool invocations (one per task). Sequential invocations waste parallelism.

**Permission boundary** (from root CLAUDE.md §3): sub-agents cannot write to `.claude/` paths. This project has no `.claude/` writes in scope, but be aware if Phase 1.5+ extends to skill authoring.

---

## Applicable ADRs (load before D-P task execution)

| ADR | Why applicable | Tasks that load it |
|---|---|---|
| [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + when Functions are permitted | D-P8 SPE-upload consumer (BackgroundService or Function) | 050 (D-P8) |
| [ADR-008](../../docs/adr/ADR-008-endpoint-filter-authorization.md) — endpoint filter auth | D-P3 admin endpoint, D-P15 ask endpoint | 012 (D-P3 endpoint), 061 (D-P15) |
| [ADR-009](../../docs/adr/ADR-009-redis-first-caching.md) — Redis-first caching | D-P13 playbook execution cache | 023 (D-P13), 024 (Q5 cache helper) |
| [ADR-010](../../docs/adr/ADR-010-di-minimalism.md) — DI minimalism | D-P12 node executor DI registration; all DI work | 022 (D-P12), 042 (facade) |
| [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture | §3.5 facade boundary; D-P9, D-P12, D-P14 | All Zone A tasks |
| [ADR-016](../../docs/adr/ADR-016-ai-cost-rate-limit-and-backpressure.md) — rate limit + cost | D-P15 endpoint | 061 (D-P15) |
| [ADR-019](../../docs/adr/ADR-019-problemdetails.md) — ProblemDetails errors | D-P15 endpoint errors | 061 (D-P15) |

---

## Applicable skills (for `task-execute` to invoke)

| Skill | Used by tasks |
|---|---|
| [task-execute](../../.claude/skills/task-execute/SKILL.md) | All — universal task driver |
| [adr-check](../../.claude/skills/adr-check/SKILL.md) | All code tasks (Step 9.5 quality gate) |
| [code-review](../../.claude/skills/code-review/SKILL.md) | All code tasks (Step 9.5 quality gate) |
| [adr-aware](../../.claude/skills/adr-aware/SKILL.md) | All tasks — auto-loads applicable ADRs |
| [dataverse-create-schema](../../.claude/skills/dataverse-create-schema/SKILL.md) | 011 (D-P3 `sprk_precedent` entity creation) |
| [bff-deploy](../../.claude/skills/bff-deploy/SKILL.md) | 080 (deploy task) |
| [azure-deploy](../../.claude/skills/azure-deploy/SKILL.md) | 010 (D-P2 Bicep deploy) |
| [spe-integration](../../.claude/skills/spe-integration/SKILL.md) | 050 (D-P8 SPE-upload consumer integration) |
| [script-aware](../../.claude/skills/script-aware/SKILL.md) | Auto-loads applicable scripts |

---

## Knowledge docs (loaded by task-execute via tag mapping)

| Knowledge | Tags | Tasks |
|---|---|---|
| [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) | `ai-search`, `vector-search`, `spaarke-insights-index` | 010 (D-P2 schema design), 022 (D-P12 IndexRetrieveNode), 060 (D-P14 cohort retrieval) |
| [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/) | `functions`, `flex-consumption`, `spe-consumer` | 050 (D-P8) |
| [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/) | `dataverse-sync`, `precedent-projection` | 041 (D-P4 Precedent projection), 051 (D-P11 Observation mirror) |

---

## Existing code patterns to follow (per Q5 audit; do NOT reimplement)

| Pattern | File | Used by D-P task | What to reuse |
|---|---|---|---|
| 3-tier retrieval composition | [`AiAnalysisNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs) | 030 (D-P5), 031 (D-P6), 022 (D-P12 IndexRetrieveNode) | Layer 1/2 prompts use this node as host; IndexRetrieveNode extracts the retrieval composition into a reusable helper |
| Dataverse → AI Search sync | [`DataverseIndexSyncService.cs`](../../src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs) | 041 (D-P4 Precedent projection) | Template for status→Confirmed projection |
| Idempotent indexer | [`ReferenceIndexingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs) | 025 (W3.5 refactor), 010 (D-P2), 041 (D-P4), 051 (D-P11) | Parameterize for index name + schema mapper (W3.5); reuse for `spaarke-insights-index` |
| Playbook execution wrapping | [`PlaybookExecutionEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs) | 023 (D-P13 cache wrap), 042 (facade impl), 060 (D-P14 invocation) | Cache wraps `ExecuteBatchAsync`; facade calls into it |
| Node executor pattern | [`Services/Ai/Nodes/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/) (10 existing) | 022 (D-P12) | New 6 nodes follow `INodeExecutor` + `NodeExecutorRegistry` pattern |
| Endpoint registration | [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) + `EndpointMappingExtensions.cs` | 012 (D-P3), 061 (D-P15) | `MapInsightsEndpoints()` + `MapInsightsAdminEndpoints()` |
| Live Facts on read | NEW — no existing `LiveFactResolverService`; pattern is direct `IDataverseService` queries with Redis caching | 022 (D-P12 LiveFactNode) | Build new service following `IDataverseService` consumer patterns |

---

## §3.5 Facade boundary — binding for Zone B code

[SPEC §3.5](SPEC.md) defines two zones in `Sprk.Bff.Api/Services/`:
- **Zone A** (`Services/Ai/`): may import LLM clients, playbook engines, node executors freely
- **Zone B** (everything else): may consume AI ONLY via `Services/Ai/PublicContracts/IInsightsAi`

**Forbidden imports in Zone B** (per SPEC §3.5.4):
- `Microsoft.Extensions.AI.*`, `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.OpenAI.*`
- `Sprk.Bff.Api.Services.Ai.IOpenAiClient`, `IPlaybookService`, `PlaybookExecutionEngine`
- `Sprk.Bff.Api.Services.Ai.Chat.*`, `Sprk.Bff.Api.Services.Ai.Insights.*` (facade only allowed)
- `Sprk.Bff.Api.Services.Ai.Nodes.*` (Zone A node executors)

**Grep verification** (binding for all Zone B tasks):
```bash
grep -rE "IChatClient|IOpenAiClient|IPlaybookService|PlaybookExecutionEngine|Microsoft\.Extensions\.AI|Microsoft\.SemanticKernel|using OpenAI|Azure\.AI\.OpenAI|Services\.Ai\.Chat|Services\.Ai\.Insights[^.P]|Services\.Ai\.Nodes" \
  src/server/api/Sprk.Bff.Api/Services/Insights/ \
  src/server/api/Sprk.Bff.Api/Api/Insights/ \
  src/server/api/Sprk.Bff.Api/Models/Insights/ \
  src/server/api/Sprk.Bff.Api/Services/Jobs/SpeUploadConsumer*.cs
# Expect: zero matches (or only Services.Ai.PublicContracts references)
```

Every PR touching Zone B Insights paths runs this grep before merge (interim manual check until FR-C6 CI gate lands in `sdap-bff-api-remediation-fix` Phase 6 task 082).

---

## Standing constraints (per CLAUDE.md, Spaarke conventions)

- **No new SAS keys**, no new `ClientSecretCredential` (per D-24, D-27; auth uses Managed Identity)
- **DI minimalism** per ADR-010 — node executors register via existing `NodeExecutorRegistry` auto-discovery pattern
- **ProblemDetails** per ADR-019 for all API errors
- **Rate limiting** per ADR-016 on D-P15 endpoint
- **No mock data in Phase 1 acceptance** — D-P16 smoke test uses real fixture documents through real ingest pipeline producing real Observations

---

## Quick links

- [SPEC.md](SPEC.md) — canonical Phase 1 scope (D-P1..D-P17)
- [decisions.md](decisions.md) — 63 numbered decisions; read D-52..D-63 for 2026-05-28 direction
- [plan.md](plan.md) — wave structure W1-W8
- [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) — rationale narrative + Precedent mockup + prompt templates
- [design.md](design.md) §0 — refinement-integration table
- [ai-inventory.md](ai-inventory.md) — DI-anchored existing AI service inventory
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) — task registry + parallel groups + critical path
- [current-task.md](current-task.md) — active task state tracker
