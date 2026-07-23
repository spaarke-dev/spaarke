# ⛔ SUPERSEDED — absorbed into email-communication-solution-r4

> **Date**: 2026-07-14
> **Status**: This project was fully designed and decomposed into 79 tasks but **never executed** — no code landed on `master` (the R3 merge PRs were scaffolding + task decomposition only; no `<EmailComposer />`, no `sprk_emailcomposer` Code Page, no ADR-033).

## What happened

R3's scope (client-side send consolidation) has been **merged into the unified project [`email-communication-solution-r4`](../email-communication-solution-r4/)**. R4 was independently drafted, then discovered R3's overlapping design; because R3 had produced no code and the two projects collide on four shared surfaces (the `sprk_communication` schema, the Communication ADR, the Code Page, and the server send-path changes), they were consolidated into one project to avoid manual coordination and to enable server/client parallel execution.

> **2026-07-16 (R4 W8, task 080/081)**: The R3 project body (`design.md`, `spec.md`, `plan.md`, `CLAUDE.md`, `current-task.md`, `tasks/`, `README.md`, `notes/`) has been **deleted** — its reusable send-side content was already copied verbatim into R4's reference folder. This tombstone is retained to record where R3 went. Per the owner directive, maintaining a superseded document body causes confusion and overhead; the reference copies are the single source.

## The reusable R3 content lives in R4's reference folder

- [`../email-communication-solution-r4/reference/r3-send-side-design.md`](../email-communication-solution-r4/reference/r3-send-side-design.md) — the absorbed R3 send-side design (`<EmailComposer />` engine, wrappers, Code Page URL contract, wave-by-wave migration). Self-contained; cited by R4's `design.md` §9.
- [`../email-communication-solution-r4/reference/r3-send-side-plan.md`](../email-communication-solution-r4/reference/r3-send-side-plan.md) — the absorbed R3 send-side plan/WBS.

## Do NOT

- Do not attempt to restore or execute the deleted R3 `tasks/*.poml` — they are superseded by R4's regenerated task decomposition.
- Do not create ADR-033 from here — the unified Communication ADR is **ADR-045**, authored in R4 wave W0.

**Canonical project**: [`projects/email-communication-solution-r4/design.md`](../email-communication-solution-r4/design.md)
