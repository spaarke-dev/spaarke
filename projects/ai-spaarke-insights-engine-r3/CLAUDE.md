# CLAUDE.md — Spaarke Insights Engine Phase 2 (r3) — project context

> **Project-scoped instructions.** Loads when working in `projects/ai-spaarke-insights-engine-r3/` and related code paths. Companion to root [CLAUDE.md](../../CLAUDE.md).
> **Status**: 🆕 design phase (initiated 2026-06-04). This file is a skeleton — sections solidify as `design.md` focus areas are selected.

---

## What this project is

**Phase 2 of the Spaarke Insights Engine** — builds on r2's Phase 1.5 substrate (hybrid playbook + RAG, 2D taxonomy, multi-entity subjects, Spaarke Assistant tool-call contract v1.1). Specific scope TBD per [`design.md`](design.md).

**Acceptance bar**: TBD — derives from `design.md` once focus areas are selected. r2's single explicit Phase 2 carry-forward is SC-15 (SME calibration ≥50 obs w/ measurable improvement) — substrate is shipped; calibration loop is r3 candidate.

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

When multiple tasks within a wave can run in parallel (per TASK-INDEX.md parallel groups), each task STILL uses `task-execute`. Pattern: ONE message with MULTIPLE Skill / Agent tool invocations (one per task).

Wave D + E + F precedent: Round 1 spike → Round 2 parallel impl → Round 3 docs. 0 stuck-agent incidents in Waves E + F (lesson from Wave D task 032 12-hour hang held — `feedback-detect-stuck-subagents` memory).

**Permission boundary** (from root CLAUDE.md §3): sub-agents cannot write to `.claude/` paths. Tasks touching `.claude/patterns/` MUST run sequentially in the main session.

---

## Terminology (carried from r2 — load-bearing)

Same as r2:

- **JPS** (JSON Prompt Schema) — schema/data format for analysis actions and playbooks. JPS is data, NOT code. Lives in Dataverse on `sprk_analysisaction.sprk_systemprompt` and `sprk_playbook` rows.
- **`PlaybookExecutionEngine`** — the code component in `Sprk.Bff.Api` that executes JPS-defined work.
- **`INodeExecutor`** — code-side handler for a specific analysis-action TYPE.
- **`sprk_analysisaction`** — JPS dispatch + prompt row (carries `sprk_systemprompt` JSON with `$schema`, `instruction`, `input`, `parameters`).
- **`sprk_playbook` + `sprk_configjson`** — JPS playbook definition with per-playbook config blob.
- **`IInsightsAi`** — Zone B-safe facade for Insights consumption (v1.1 surface includes `AnswerQuestionAsync`, `SearchAsync`, `AssistantQueryAsync`, `AssistantQueryStreamAsync`, `RunIngestAsync`).
- **Spaarke Assistant tool-call contract** — locked at v1.1; r3 changes require minor-version bump (see [`r2/design-e3-tool-call-contract.md`](../ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md)).

---

## Applicable ADRs (carried from r2; refined per r3 focus areas)

> Solidifies once `design.md` focus areas are selected. Likely r3-wave-1 (Tier 1 cleanup) ADRs:

| ADR | Why applicable | Likely r3 wave |
|---|---|---|
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) | DI minimalism — Tier 1.1 `NullInsightsAi` adds 1 reg in compound-OFF branch | wave 1 |
| [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) — refined 2026-05-20 | AI facade boundary — Tier 1.1 closes asymmetric reg flagged by Wave E adr-check | wave 1 |
| [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) | BFF Null-Object Kill-Switch — Tier 1.1 `NullInsightsAi` is canonical P3 Fail-fast pattern application | wave 1 |
| (others) | TBD per focus areas | — |

---

## Applicable skills (carried from r2)

| Skill | Used by |
|---|---|
| [task-execute](../../.claude/skills/task-execute/SKILL.md) | All — universal task driver |
| [adr-aware](../../.claude/skills/adr-aware/SKILL.md) | All — auto-loads applicable ADRs |
| [adr-check](../../.claude/skills/adr-check/SKILL.md) | All FULL-rigor tasks (Step 9.5) |
| [code-review](../../.claude/skills/code-review/SKILL.md) | All FULL-rigor tasks (Step 9.5) |
| [bff-deploy](../../.claude/skills/bff-deploy/SKILL.md) | Post-wave deployment |
| [script-aware](../../.claude/skills/script-aware/SKILL.md) | Auto-loads applicable scripts |

---

## Knowledge docs (carried from r2)

| Knowledge | Tags | Likely r3 use |
|---|---|---|
| [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) | `ai-search`, `vector-search`, `spaarke-insights-index` | Tier 1.4 telemetry, Tier 2.5 embedding classifier |
| [knowledge/agent-framework/](../../knowledge/agent-framework/) | `intent-classification`, `tool-calls`, `streaming` | Tier 2.1 clarification, Tier 2.5 embedding classifier |

---

## Predecessor artifacts to consult first

Before designing or implementing anything in r3, READ these r2 artifacts:

1. [`r2/PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) — 4-tier candidate menu; primary input to r3 design
2. [`r2/notes/lessons-learned.md`](../ai-spaarke-insights-engine-r2/notes/lessons-learned.md) — what worked, what didn't, what to do differently
3. [`r2/design-e3-tool-call-contract.md`](../ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md) v1.1 — the locked contract; r3 changes minor-version bump
4. [`r2/notes/insights-engine-assistant-integration-brief.md`](../ai-spaarke-insights-engine-r2/notes/insights-engine-assistant-integration-brief.md) — R5 consumer brief (v1.1)
5. [`spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md`](../spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md) — R5 ↔ Insights coordination touchpoints (§4.4 + §4.6 resolved at Wave F close)

---

## BFF placement (per CLAUDE.md §10 — binding)

r3 extends r2's BFF work. The 2026-05-20 BFF AI extraction assessment validated that Insights stays in `Sprk.Bff.Api`. r3 component placement TBD per focus areas; reuse r2's placement decisions where applicable:

| Component category | r2 placement | r3 expected |
|---|---|---|
| AI orchestration (IInsightsAi, InsightsOrchestrator, AssistantToolCallHandler) | `Sprk.Bff.Api/Services/Ai/Insights/` (Zone A) | extend in-place; no new Zone |
| Endpoints | `Sprk.Bff.Api/Api/Insights/` (Zone B) | extend in-place |
| Facades / wire DTOs | `Sprk.Bff.Api/Services/Ai/PublicContracts/` + `Models/Insights/` | extend in-place |
| Config options | `Sprk.Bff.Api/Configuration/` | extend in-place |
| Schema additions (if any) | Dataverse unmanaged solution (per ADR-027 + memory `spaarke-unmanaged-solutions`) | continue pattern |
| Prompts | Dataverse `sprk_analysisaction.sprk_systemprompt` — existing JPS primitive | continue pattern |

**`.claude/constraints/bff-extensions.md` MUST be loaded by each r3 task adding endpoints, services, DI registrations, or NuGet packages.** Particularly §F.1 (Asymmetric-Registration Tier 1.5 Anti-Pattern), §F.2 (Fixture-Config-FIRST Inspection Protocol), §F.3 (Empirical-Reproduction-FIRST Protocol).

---

## §3.5 facade boundary — binding for Zone B code (carried from r2)

[Spaarke §3.5](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) defines two zones in `Sprk.Bff.Api/Services/`:
- **Zone A** (`Services/Ai/`): may import LLM clients, playbook engines, node executors freely
- **Zone B** (everything else): may consume AI ONLY via `Services/Ai/PublicContracts/IInsightsAi`

**Forbidden imports in Zone B**: `Microsoft.Extensions.AI.*`, `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.OpenAI.*`, `Services.Ai.IOpenAiClient`, `IPlaybookService`, `PlaybookExecutionEngine`, `Services.Ai.Chat.*`, `Services.Ai.Insights.*` (facade only), `Services.Ai.Nodes.*`.

Every r3 PR touching Zone B Insights paths runs the grep before merge.

---

## Standing constraints (carried from r2; will solidify with design)

- **No new `sprk_prompt` entity** — prompts live in `sprk_analysisaction.sprk_systemprompt` (r2 PR-1 clarification carried forward)
- **Practice areas sourced from `sprk_practicearea_ref` table** — never hardcoded (PA-1 carried forward)
- **No new SAS keys / `ClientSecretCredential`** — Managed Identity per r1 D-24, D-27
- **DI minimalism per ADR-010** — feature-module pattern; r3 additions stay within target
- **ProblemDetails per ADR-019** for all API errors
- **Spaarke uses UNMANAGED solutions** (per memory `spaarke-unmanaged-solutions` — ADR-027 mandate doesn't reflect actual practice)
- **CI hygiene**: `dotnet format whitespace Spaarke.sln --verify-no-changes` before push (PR #336/#337/#339 lessons); run BOTH unit + integration tests in sub-agent quality gates (Wave F lesson)

---

## Quick links

- [README.md](README.md) — project overview + status table
- [design.md](design.md) — Phase 2 design skeleton (focus areas pending)
- [current-task.md](current-task.md) — active task tracker
- [r2 project](../ai-spaarke-insights-engine-r2/) — Phase 1.5 predecessor; shipped 2026-06-04
- [r2 PHASE-2-OUTLINE.md](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) — primary design input
- [r2 lessons-learned.md](../ai-spaarke-insights-engine-r2/notes/lessons-learned.md)
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — Spaarke-wide arch doc
- [`docs/guides/INSIGHTS-ENGINE-GUIDE.md`](../../docs/guides/INSIGHTS-ENGINE-GUIDE.md) — operator/developer guide
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF additions binding constraint

---

*Skeleton authored 2026-06-04 by main session. Solidifies as design.md focus areas are selected.*
