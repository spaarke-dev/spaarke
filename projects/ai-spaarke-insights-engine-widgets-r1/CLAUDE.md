# CLAUDE.md — Insights Engine Widgets r1 — project context

> **Project-scoped instructions.** Loads when working in `projects/ai-spaarke-insights-engine-widgets-r1/`.
> **Status**: 🆕 initiated 2026-06-10. Design phase.

---

## What this project is

A surface-layer project that builds reusable UI components + JPS playbooks for surfacing Spaarke Insights Engine output as **topic/subject scoped Insight Summary cards** on Spaarke record pages. r1 establishes the framework with **Matter Health** as the first proven topic.

This is **NOT** a platform project. r2 (Insights Engine), the audit (BFF AI architecture), and R5 (Summarize) shipped everything needed. r1 consumes.

---

## What r1 ships

- Reusable `InsightSummaryCard` component pattern in `@spaarke/ai-widgets`
- `matter-health-single` JPS playbook
- Topic registry mechanism
- Matter record page integration of the Matter Health card with sparkle-icon AI narrative
- End-to-end UAT through the existing r2 Insights Engine surface

---

## 🚨 MANDATORY: Task Execution Protocol

When task POMLs are authored, the root `task-execute` skill applies per root CLAUDE.md §4. Until then, work is design + discovery — main-session-driven, not task-driven.

---

## Key constraints

| Source | Binding rule |
|---|---|
| Audit DR-003 + `.claude/patterns/ai/public-contracts-facade.md` | Use existing `IInsightsAi.AnswerQuestionAsync`. Do NOT create new BFF facade for widget invocation — r1 is a consumer. |
| Audit DR-008 + `.claude/patterns/ai/endpoint-di-symmetry.md` | If any DI change is needed (unlikely in r1), follow Endpoint↔DI Symmetry Rule + add Null peer if facade. |
| Audit DR-002 + ADR-009 | Use existing `IInsightsPlaybookExecutionCache` (15-min TTL) for narrative caching. Do NOT add new cache abstractions. |
| Audit canonical prompt pattern (§2.7) | Playbook prompts authored per Spaarke Canonical Prompt Construction Pattern. Co-locate prompt with consumer where appropriate; forbid generic `/Prompts/` dir. |
| r2 multi-entity subject scheme | Use `matter:GUID` for r1. Framework-shape (but don't implement) `matter-collection:` and `cohort:` subjects for r2+. |
| ADR-013 | AI features extend the BFF, not separate services. All AI calls flow through `IInsightsAi.*` facade methods. |
| ADR-032 §F.1 | If r1 adds new conditional DI (unlikely), pair with Null peer + P3 Fail-Fast. |

---

## Working artifacts

| File | Purpose |
|---|---|
| [`README.md`](README.md) | Project overview, status, dependencies |
| [`design.md`](design.md) | Framework + Matter Health design — current iteration |
| [`current-task.md`](current-task.md) | Active task tracker |
| `spec.md` | Implementation spec (derives from design) |
| `plan.md` | Wave breakdown (derives from spec) |
| [`tasks/`](tasks/) | Task POMLs |
| [`notes/`](notes/) | Spikes, handoffs, drafts |
| [`decisions/`](decisions/) | Decision records (DR-###) |

---

## Predecessor + parallel project context

| Project | Relationship to r1 |
|---|---|
| `ai-spaarke-insights-engine-r2` | Predecessor; r2 shipped `IInsightsAi`, multi-entity subjects, SSE, citations — the substrate r1 consumes |
| `bff-ai-architecture-audit-r1` | Codified the patterns r1 follows (PublicContracts facade, Endpoint↔DI Symmetry, Cache Stack) |
| `ai-spaarke-insights-engine-r3` | Phase 2 cleanup project — PAUSED pending R6. r1 ships independently. r3's Tier 2.4 actionable citations would enhance r2+ widgets. |
| `spaarke-ai-platform-unification-r6` | Architectural convergence — in design. R6 Pillar 3 (`IInvokePlaybookAi`) and Pillar 5/6/9 (workspace state + visibility) may inform r2+ widget work but do NOT block r1. |
| `spaarke-ai-platform-unification-r5` (closed) | Possibly shipped the existing sparkle-icon pattern. Worth grep'ing to determine reuse vs net-new. |

---

## Predecessor artifacts to consult

Before any r1 work, READ:

1. [`README.md`](README.md) — this project's intent + scope
2. [`design.md`](design.md) — framework + Matter Health design
3. [`projects/ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) — what r3 deferred + what's available now
4. [`projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md`](../bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md) — binding canonical patterns
5. [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) — facade pattern
6. [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) — DI symmetry rule
7. [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF extension governance

---

## Methodology

1. **Design phase** (current) — refine `design.md` with owner; lock topic registry shape, playbook design, card UX
2. **Spec phase** — derive `spec.md` with concrete FRs / NFRs / acceptance criteria
3. **Plan phase** — break into waves (likely: A=files-index pipeline healthy [parallel stream]; B=framework + playbook + Matter Health single-mode; C=r2+ expansion)
4. **Task POMLs** — author per-wave task files
5. **Implementation** — `task-execute` per task

Total: ~4-5 weeks design through implementation if files-index pipeline lands in parallel.

---

## Quick links

- [README.md](README.md) — project overview
- [design.md](design.md) — framework + Matter Health design
- [current-task.md](current-task.md) — active task tracker

---

*Skeleton authored 2026-06-10 by main session per owner direction. Solidifies as design.md iterates.*
