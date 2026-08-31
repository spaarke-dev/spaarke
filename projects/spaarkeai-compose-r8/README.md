# Spaarke Compose R8 — Render-on-Save Fidelity Architecture

> **Status**: 🔬 **Investigation (pre-spec, folder-only, execution-gated)** — created 2026-08-18 from
> `spaarkeai-compose-r7` UAT + the proactive hidden-issue audit.
> **Owner**: Ralph Schroeder

## What this is

The project that makes Compose **FAITHFUL** — actually preserving Word formatting/structure (fonts, sizes,
colors, footnotes, cross-reference fields, paragraph spacing, content controls, complex objects) through the
save round-trip, instead of the current thin-content-model whole-body re-author that silently degrades real
legal documents.

This is **not** a widener patch — it is a **render-on-save architecture change**. It exists because R7's UAT +
audit proved the fidelity loss is systemic to the write model, not a set of special-cases.

- **R7** = HONEST + SAFE (surface every loss, never silently drop or mis-place). In flight.
- **R8 (this)** = FAITHFUL (preserve the content through save). Investigation first, then spec, then build.

## Scope tracks

1. **Render-on-save fidelity** (the primary mandate) — preserve Word formatting/structure through save.
2. **Durable session files** (added 2026-08-19 by operator during R7 UAT) — make Assistant chat-session
   uploads usable for the **full 90-day History window** (today content is evicted after ~24h). See
   [`notes/durable-session-files.md`](notes/durable-session-files.md). Parallel track / fast-follow.

## Start here

- [`design.md`](design.md) — **the project design** (2026-08-19): the three tracks, the R4→R6→R8 architectural
  pendulum, four candidate write models + the recommended hypothesis, the Phase-0 proof gate + decision rule, the
  preservation oracle, ADR-049 tension, and five open owner questions.
- [`notes/fidelity-architecture-investigation.md`](notes/fidelity-architecture-investigation.md) — the findings,
  the architectural root cause, candidate fix directions, and the research questions R8 must answer.
- [`notes/durable-session-files.md`](notes/durable-session-files.md) — the durable-files scope add: 90-day
  file availability (aligns to History), grounded code inventory, candidate designs, open questions.
- Evidence base: [`../spaarkeai-compose-r7/notes/uat-issues.md`](../spaarkeai-compose-r7/notes/uat-issues.md)
  (UAT-07a, UAT-15..20, UAT-24/25, and R7 UAT round point 1b for durable files).

## First job (the mandate)

**Full investigation + research to build the CORRECT solution before any build** — decide the write model
(patch-engine vs opaque-rPr carry vs hybrid) with a proof-of-concept round-trip on a real worst-offender
corpus, THEN `/design-to-spec` → `/project-pipeline`. Sequence AFTER R7's honest/safe batch lands.

## Not started

No worktree, no tasks yet — this is a seeded folder. Execution is operator-gated (same pattern as the other
initialize-only projects in [`projects/INDEX.md`](../INDEX.md)).
