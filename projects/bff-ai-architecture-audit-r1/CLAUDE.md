# CLAUDE.md — BFF AI Architecture Audit r1 — project context

> **Project-scoped instructions.** Loads when working in `projects/bff-ai-architecture-audit-r1/`.
> **Status**: 🆕 initiated 2026-06-04. Audit methodology + scope pending owner discussion in `design.md`.

---

## What this project is

A **dedicated architectural audit** of all AI infrastructure in `Sprk.Bff.Api`. Triggered by `ai-spaarke-insights-engine-r3` design discussion surfacing 4 parallel intent-classification systems + 4 near-identical lookup services + various other accumulated parallel infrastructure across 5+ project cycles.

This is **NOT** a feature project. No code ships from this audit. Outputs are:
1. Inventory (data)
2. Canonical-architecture decisions (decisions)
3. Migration plan for downstream projects (recommendations)

Downstream projects (r3, R5, future r4) consume the audit's decisions and do the actual implementation work.

---

## Predecessor context

| r2 closed | 2026-06-04 (Phase 1.5 ✅; 14/15 SCs met) |
| r3 paused | 2026-06-04 (Option C choice: audit FIRST) — only Wave 2 paused; Wave 1 cleanup proceeds independently |
| Primary input | [`notes/initial-findings.md`](notes/initial-findings.md) |

---

## 🚨 MANDATORY: Task Execution Protocol

When the audit produces task POMLs, the `task-execute` skill applies per root CLAUDE.md §4. Until then, the work is methodology + discovery via grep/Read/Glob — main-session driven, not task-driven.

---

## What the audit is + isn't

| IS | IS NOT |
|---|---|
| Systematic inventory of AI services | Refactoring work (that comes after, in downstream projects) |
| Canonical-architecture decisions per category | Code changes |
| Migration plan with effort estimates | Implementation of migrations |
| r3 + r4 scope guidance | Lock-in for any downstream project (those still own their scope) |
| Process recommendation re: future audits | Mandate for how downstream projects work |

---

## Audit categories (per `notes/initial-findings.md`)

1. **Intent classification** — 4 parallel systems
2. **Lookup services** — 4 near-identical DRY violations
3. **Search services** — 4 distinct-but-uncoordinated
4. **Cache patterns** — 11+ direct `IMemoryCache` usages, diverse TTL needs
5. **Prompt builders** — 3+ patterns
6. **Possibly more discovered during audit** — TBD

For each category, audit decides: canonical service, deprecation candidates, migration plan, effort.

---

## Methodology (to be refined in design.md)

Likely structure:

1. **Inventory phase** (~3d) — systematic file-by-file walk of `Services/Ai/`; grep for each service's consumers; classify state (active / deprecated / unused)
2. **Per-category analysis** (~3d) — for each of categories 1-6 above (and any newly discovered), apply: read code, identify canonical candidate, document tradeoffs
3. **Owner review** (~0.5d total but spread across iterations) — present per-category findings; owner picks canonical
4. **Migration planning** (~2d) — work-item sizing per service/category; identify cross-team coordination needs
5. **Decision documentation** (~2d) — decision records (DR-###) in `decisions/`; final report in `notes/canonical-architecture-decisions.md`

Total: ~2 weeks.

---

## Key principles (likely; refined in design.md)

- **Honest assessment over scope minimization** — if a service is deprecated and nobody knows, surface it
- **Don't propose deletion without consumer mapping** — empirical "no one uses this" before "delete this"
- **Cross-project coordination is part of the work** — SprkChat, Insights, R5, playbook-builder teams may all have ownership
- **Audit doesn't decide scope for downstream projects** — it gives them ground truth + recommendations; they own their roadmaps

---

## Predecessor artifacts to consult

Before any audit work, READ:

1. [`notes/initial-findings.md`](notes/initial-findings.md) — what we already know
2. `projects/ai-spaarke-insights-engine-r3/design.md` §2 — what r3 was about to lock based on incomplete information
3. `projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md` §3.1 — R5↔Insights reuse mandate (the precedent for what this audit produces at BFF-wide scope)
4. `projects/ai-sprk-chat-platform-enhancement-r2/` — completed SprkChat r2 project (shipped CapabilityRouter consumer, PlaybookDispatcher)

---

## Quick links

- [README.md](README.md) — project overview
- [notes/initial-findings.md](notes/initial-findings.md) — primary input
- [design.md](design.md) — audit scope + methodology (pending)
- [current-task.md](current-task.md) — active task tracker

---

*Skeleton authored 2026-06-04 by main session after Option C decision. Solidifies as design.md methodology firms up.*
