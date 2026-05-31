# CLAUDE.md — Spaarke Insights Engine Phase 1.5 (r2) — project context

> **Project-scoped instructions.** Loads when working in `projects/ai-spaarke-insights-engine-r2/` and related code paths. Companion to root [CLAUDE.md](../../CLAUDE.md).

---

## What this project is

**Phase 1.5 of the Spaarke Insights Engine** — lifts r1's plumbing prototype (one playbook, code-defined universal-ingest, single-entity matter scope, litigation-biased classification) into a usable multi-tenant, multi-practice-area, multi-entity insights platform with both pre-authored playbook and ad-hoc RAG consumption paths, prompts that SMEs can iterate without code deploys, and a Spaarke Assistant integration.

**Acceptance bar**: `predict-matter-cost@v1` end-to-end works live on Spaarke Dev (real Inference / real Decline — NOT defensive scaffold); per-practice-area routing demonstrably works for ≥3 areas; multi-entity subjects (`project:`, `invoice:`) resolve; `/api/insights/search` (NEW) ships with RAG flow; Spaarke Assistant invokes either path via intent classifier; SMEs iterate prompts in `sprk_analysisaction.sprk_systemprompt` without code deploys.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. Do not read POML files directly and implement manually.

Per root [CLAUDE.md §4](../../CLAUDE.md), `task-execute` ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check at Step 9.5)
- ✅ Progress recoverable after compaction

### Trigger phrases that invoke task-execute

| User says | Action |
|---|---|
| "work on task X" | Invoke task-execute with task X POML |
| "continue" / "keep going" / "next task" | Read TASK-INDEX.md, find first 🔲, invoke task-execute |
| "continue with task X" / "resume task X" | Invoke task-execute with task X POML |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Parallel task execution

When multiple tasks within a wave can run in parallel (per TASK-INDEX.md parallel groups), each task STILL uses `task-execute`. Pattern: ONE message with MULTIPLE Skill tool invocations (one per task).

**Permission boundary** (from root CLAUDE.md §3): sub-agents cannot write to `.claude/` paths. Wave A doc-refresh tasks (010, 011) may touch `.claude/patterns/` — these MUST run sequentially in the main session.

---

## Terminology (load-bearing — locked in spec.md)

Earlier drafts conflated JPS-the-schema with the code that runs it. The terms below are precise:

- **JPS** (JSON Prompt Schema) — schema/data format for analysis actions and playbooks. JPS is data, NOT code. Lives in Dataverse on `sprk_analysisaction.sprk_systemprompt` and `sprk_playbook` rows.
- **`PlaybookExecutionEngine`** — the code component in `Sprk.Bff.Api` that executes JPS-defined work. (Earlier drafts loosely called this "the JPS engine.")
- **`INodeExecutor`** — code-side handler for a specific analysis-action TYPE. Phase 1.5 contributes new ones (LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, GroundingVerifyNode, DeclineToFindNode, ReturnInsightArtifactNode).
- **`sprk_analysisaction`** — existing JPS dispatch + prompt row. **IS the prompt-bearing primitive** (carries `sprk_systemprompt` JSON with `$schema`, `instruction { role, task, constraints, context }`, `input`, `parameters`). r1 already uses this for non-Insights actions. Phase 1.5 retires `.txt` files by populating this field. **No new `sprk_prompt` entity.**
- **`sprk_playbook` + `sprk_configjson`** — JPS playbook definition with per-playbook config blob (cost cap, thresholds, inline prompt templates owned by exactly one playbook).

---

## Applicable ADRs (load before each wave's tasks)

| ADR | Why applicable | Tasks |
|---|---|---|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api-and-workers.md) — Minimal API + BackgroundService | New `/api/insights/search` endpoint (Wave E1) | 040 |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filter-authorization.md) — Endpoint filters for auth | Both Insights endpoints | 040, 042 |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism (≤15 non-framework) | Wave D5 resolvers + Wave E classifier + RAG | 034, 041, 042 |
| [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) — AI Architecture (facade refined 2026-05-20) | All Wave C/D/E — CRUD code consumes Insights ONLY via `IInsightsAi`; never inject `IOpenAiClient`, `IPlaybookService`, etc. directly | All BFF code tasks |
| [ADR-027](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-mgmt.md) — Solution management | Wave D1 schema → managed solution path | 030 |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 | New endpoint auth filter | 040 |
| [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — Publish hygiene + CVE override | Any wave adding NuGet packages | 020, 040 |

---

## Applicable skills

| Skill | Used by |
|---|---|
| [task-execute](../../.claude/skills/task-execute/SKILL.md) | All — universal task driver |
| [adr-aware](../../.claude/skills/adr-aware/SKILL.md) | All — auto-loads applicable ADRs |
| [adr-check](../../.claude/skills/adr-check/SKILL.md) | All FULL-rigor tasks (Step 9.5) |
| [code-review](../../.claude/skills/code-review/SKILL.md) | All FULL-rigor tasks (Step 9.5) |
| [dataverse-create-schema](../../.claude/skills/dataverse-create-schema/SKILL.md) | Wave D1 (030) |
| [dataverse-deploy](../../.claude/skills/dataverse-deploy/SKILL.md) | Wave B (004), C (020), D (030) deploys |
| [dataverse-mcp-usage](../../.claude/skills/dataverse-mcp-usage/SKILL.md) | Wave B1 (001), Wave D1 (030), Wave C2 (021) prompt rows |
| [bff-deploy](../../.claude/skills/bff-deploy/SKILL.md) | Post-Wave C/E deployment |
| [script-aware](../../.claude/skills/script-aware/SKILL.md) | Auto-loads applicable scripts |

---

## Knowledge docs

| Knowledge | Tags | Tasks |
|---|---|---|
| [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) | `ai-search`, `vector-search`, `spaarke-insights-index` | 035 (D6 migration), 040 (E1 RAG) |
| [knowledge/agent-framework/](../../knowledge/agent-framework/) | `intent-classification`, `tool-calls` | 041 (E2), 042 (E3) |

---

## Existing code patterns to reuse (do NOT reimplement)

| Pattern | File | Used by |
|---|---|---|
| Insights node executors (Phase 1 — LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, etc.) | [`src/server/api/Sprk.Bff.Api/Services/Insights/Graph/`](../../src/server/api/Sprk.Bff.Api/Services/Insights/Graph/) | Wave C/D extend |
| Existing JPS-action-row prompt pattern (e.g., "Classify Document") in `sprk_analysisaction.sprk_systemprompt` | Spaarke Dev Dataverse → Analysis Action: "Classify Document" | Wave B1, C2, D2, D3 |
| Endpoint pattern (`/api/insights/ask`) | Sprk.Bff.Api/Endpoints (r1) | Wave E1 (040) |
| DI registration | `InsightsServiceCollectionExtensions.cs` (r1) | All wave additions |
| `IInsightsAi` facade | `Services/Ai/PublicContracts/IInsightsAi.cs` (r1) | Wave E1, D5 extend |
| `Deploy-Playbook.ps1` | `scripts/Deploy-Playbook.ps1` (r1) | Wave B3, B4, C1 |
| `PlaybookExecutionEngine` | r1 — code unchanged by Insights; Wave A5 may surface engine patches | Wave C1 |

---

## BFF placement (per CLAUDE.md §10 — binding)

This project is an **extension of r1's BFF work**. The 2026-05-20 BFF AI extraction assessment validated that Insights work stays in `Sprk.Bff.Api` (operationally justified). Phase 1.5 component placement:

| Component | Placement | Rationale |
|---|---|---|
| `POST /api/insights/search` endpoint (E1) | `Sprk.Bff.Api/Endpoints/` | Follows existing `/ask` pattern |
| Universal-ingest JPS playbook (C1) | Dataverse | Playbook is data, not code |
| Per-entity `ILiveFactResolver` (D5) | `Sprk.Bff.Api/Services/Ai/Insights/` | Tightly coupled to Dataverse + JPS context |
| Intent classifier (E2) | `Sprk.Bff.Api/Services/Ai/Insights/` | Reuses `IOpenAiClient` + facade plumbing |
| RAG retriever (E1 internal) | `Sprk.Bff.Api/Services/Ai/Insights/` | Reuses `spaarke-insights-index` substrate + AI Search client |
| `IInsightsAi` facade extensions (E1, D5) | `Sprk.Bff.Api/Services/Ai/PublicContracts/` | Canonical CRUD ↔ AI seam |
| Schema additions (D1) | Dataverse managed solution | Standard promotion path per ADR-027 |
| Prompts (C2) | Dataverse — `sprk_analysisaction.sprk_systemprompt` | Existing JPS primitive (r1 already uses); no new entity |

**Decision is load-bearing** — re-evaluate if a future phase adds non-BFF Insights consumers.

`.claude/constraints/bff-extensions.md` MUST be loaded by each Wave C/D/E task adding endpoints, services, DI registrations, or NuGet packages.

---

## §3.5 Facade boundary — binding for Zone B code (carried from r1)

[Spaarke §3.5](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) defines two zones in `Sprk.Bff.Api/Services/`:
- **Zone A** (`Services/Ai/`): may import LLM clients, playbook engines, node executors freely
- **Zone B** (everything else): may consume AI ONLY via `Services/Ai/PublicContracts/IInsightsAi`

**Forbidden imports in Zone B**: `Microsoft.Extensions.AI.*`, `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.OpenAI.*`, `Services.Ai.IOpenAiClient`, `IPlaybookService`, `PlaybookExecutionEngine`, `Services.Ai.Chat.*`, `Services.Ai.Insights.*` (facade only), `Services.Ai.Nodes.*`.

Every PR touching Zone B Insights paths runs the grep before merge.

---

## Standing constraints

- **No new `sprk_prompt` entity** — Phase 1.5 prompts live in `sprk_analysisaction.sprk_systemprompt` (PR-1 clarification in spec.md)
- **Practice areas sourced from `sprk_practicearea_ref` table** — never hardcoded; the table IS the source of truth (PA-1 clarification)
- **No new SAS keys / `ClientSecretCredential`** (Managed Identity per r1 D-24, D-27)
- **DI minimalism per ADR-010** — node executors register via existing `NodeExecutorRegistry` auto-discovery; if Wave D5/E adds >15 non-framework registrations, consolidate via registry pattern
- **ProblemDetails per ADR-019** for all API errors
- **Wave B sequenced first** per owner direction WB-1; documented in spec.md and plan.md
- **r1 wrap-up is NOT in r2 scope** per owner direction R1-1

---

## Quick links

- [spec.md](spec.md) — canonical Phase 1.5 implementation spec
- [design.md](design.md) — owner-facing design (corrections + 9 architectural decisions)
- [plan.md](plan.md) — wave structure + parallel groups + critical path
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) — task registry + status
- [current-task.md](current-task.md) — active task state tracker
- [r1 project](../ai-spaarke-insights-engine-r1/) — Phase 1 (predecessor; shipped + deployed)
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — Spaarke-wide arch doc
- [`docs/guides/INSIGHTS-ENGINE-GUIDE.md`](../../docs/guides/INSIGHTS-ENGINE-GUIDE.md) — operator/developer guide
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF additions binding constraint
