# CLAUDE.md — Agent Framework Fit Assessment R1

> **Project**: agent-framework-fit-assessment-r1
> **Type**: Research + decision document (no code changes; reads code to ground analysis)
> **Created**: 2026-06-03

## Project Context

Produce a single decision document — `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` — that answers, for every current and likely-future Spaarke AI surface, whether `Microsoft.Agents.AI` (Agent Framework proper, distinct from raw `Microsoft.Extensions.AI`) is a fit, where, and how it should be deployed and surfaced.

The assessment pattern mirrors [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md), which refined ADR-013 in 2026-05.

Canonical plan: [`SPEC.md`](./SPEC.md). Per-task instructions: `tasks/*.poml`. Status: [`TASK-INDEX.md`](./TASK-INDEX.md).

This project **blocks** [`projects/agent-framework-knowledge-r1/`](../agent-framework-knowledge-r1/) — that project will not resume until the assessment lands and its SPEC is reviewed against the conclusions.

## Key Constraints

1. **Read-only on `src/`.** The assessment cites Spaarke code, it does not modify it.
2. **Honest, decision-grade analysis.** Conclusions must be backed by concrete evidence — citable `.cs` lines, ADRs, constraint docs, or upstream Agent Framework documentation. No conclusions by intuition.
3. **No ADR amendments / new ADRs.** Per scoping decision (2026-06-03), the deliverable is the assessment document only. ADR changes are downstream of this project.
4. **No refinements to `agent-framework-knowledge-r1` SPEC.** Per scoping decision, deferred. The assessment may write recommendations into a parking-release note but does NOT edit the SPEC.
5. **Microsoft.Extensions.AI ≠ Microsoft.Agents.AI.** The assessment must keep this distinction sharp. Spaarke already uses Extensions.AI (`IChatClient`, `AIFunction`, `ChatResponseUpdate`). The question is whether to adopt Agents.AI on top — and where.
6. **All 8 Spaarke surfaces (S1–S8) must be evaluated** even if the conclusion for some is "not applicable / not in scope for adoption." Surfaces that aren't evaluated leave open questions.
7. **Surface coverage is the integrity test.** Task 001 (inventory) MUST grep across `src/server/api/Sprk.Bff.Api/Services/Ai/` and adjacent paths. If a new surface is discovered during research, it becomes S8 and gets full treatment — do not silently drop.
8. **Adversarial review at task 007 is mandatory.** Specifically required to ask "what would I write if I were arguing AGAINST adoption everywhere" and to weaken any conclusion that doesn't survive the challenge.

## Working Pattern for Each Task

1. Invoke `task-execute` skill with the task POML (per root CLAUDE.md §4)
2. Declare RIGOR LEVEL at task start (mostly STANDARD; task 006 is FULL because the synthesis is the actual deliverable; task 007 is FULL adversarial review)
3. Read task POML `<knowledge>` files first
4. For inventory tasks: read code → produce structured findings tables → write findings to project-local `notes/` directory (NOT to the assessment document yet)
5. For analysis tasks: apply SPEC §4 criteria against the inventory; tabulate per-surface conclusions
6. For synthesis task (006): pull findings from all `notes/` into the canonical assessment document at `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md`
7. For review (007): re-read the assessment as if it were a stranger's PR; run adversarial questions; revise
8. Update `TASK-INDEX.md` row + reset `current-task.md` for next task at completion

## Spaarke AI Surfaces in Scope (from SPEC §3)

| # | Surface | Read these to ground analysis |
|---|---|---|
| S1 | SprkChat conversational agent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs`, `SprkChatAgentFactory.cs` (if exists), `Middleware/Agent*Middleware.cs`, `CompoundIntentDetector.cs`, `Tools/*.cs` |
| S2 | AnalysisOrchestration + JPS playbooks | `Services/Ai/AnalysisOrchestrationService.cs`, `IPlaybookExecutionEngine.cs`, `IPlaybookOrchestrationService.cs`, `Nodes/*Executor.cs`, `ExecutionGraph.cs` |
| S3 | Builder agent | `Services/Ai/Builder/BuilderAgentService.cs`, `BuilderToolDefinitions.cs`, `BuilderToolExecutor.cs`, `BuilderScopeImporter.cs` |
| S4 | Background AI jobs | `Services/Jobs/`, `Services/Ai/Jobs/` (whatever exists), `ServiceBusJobProcessor.cs` |
| S5 | Foundry Agent Service overlap | **Bimodal**: `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` (shipped wrapper, default-OFF per ADR-018) + `knowledge/foundry-agent-service/` (curated canonical durable/HITL surface, no Spaarke code yet) |
| S6 | M365 Copilot / Declarative Agent surface | `projects/ai-m365-copilot-integration/` design + spec docs |
| S7 | Insights Engine MCP server | `projects/ai-spaarke-insights-engine-r1/` design + spec docs |
| S8 | Future / discovered surfaces | Whatever surfaces during task 001 inventory |

## Mandatory Sources to Read When Grounding Per-Surface Analysis (Task 004)

- ADR-013 (refined 2026-05-20) — the keep-in-BFF criteria + the PublicContracts/ facade requirement
- ADR-001 — Minimal API + Functions exception scope
- `.claude/constraints/bff-extensions.md` — BFF-extension governance, publish-size budget
- `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` — the assessment-format precedent
- `knowledge/agent-framework/docs/overview.md`, `agents.md`, `workflows.md` — what Agent Framework actually offers

## Applicable Skills

- `task-execute` — mandatory wrapper for every task (root CLAUDE.md §4)
- `context-handoff` — checkpoint per the proactive checkpointing rules
- `adr-check` — task 007 (adversarial review) runs this against the draft assessment
- `code-review` — task 007 may run this against the assessment as a written deliverable
- `researcher` subagent — optional for upstream Agent Framework deep-dive (task 003), respecting its memory-accumulation behavior

## 🚨 MANDATORY: Task Execution Protocol

When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually. See root CLAUDE.md §4 for the full protocol and rigor-level decision tree.

## References

- [Specification](SPEC.md) — canonical plan
- [Task Index](TASK-INDEX.md) — progress tracker
- [Current Task](current-task.md) — active task state
- Assessment-style template: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)
- Binding constraints: [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md), [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)
- Parked downstream project: [`projects/agent-framework-knowledge-r1/`](../agent-framework-knowledge-r1/)
