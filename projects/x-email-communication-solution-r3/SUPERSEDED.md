# ⛔ SUPERSEDED — absorbed into email-communication-solution-r4

> **Date**: 2026-07-14
> **Status**: This project was fully designed and decomposed into 79 tasks but **never executed** — no code landed on `master` (the R3 merge PRs were scaffolding + task decomposition only; no `<EmailComposer />`, no `sprk_emailcomposer` Code Page, no ADR-033).

## What happened

R3's scope (client-side send consolidation) has been **merged into the unified project [`email-communication-solution-r4`](../email-communication-solution-r4/)**. R4 was independently drafted, then discovered R3's overlapping design; because R3 had produced no code and the two projects collide on four shared surfaces (the `sprk_communication` schema, the Communication ADR, the Code Page, and the server send-path changes), they were consolidated into one project to avoid manual coordination and to enable server/client parallel execution.

## These artifacts remain as REFERENCE INPUT (do not execute)

- `design.md` §5–§10 — the `<EmailComposer />` engine, wrapper, Code Page URL contract, and wave-by-wave migration detail are **directly reusable** and are cited by R4's design.md §9 (waves W2, W4, W6). Do not rewrite; cite.
- `spec.md`, `plan.md`, `tasks/` — reference for the send-side task authoring; **superseded** by R4's regenerated task decomposition.
- Empirical findings in `CLAUDE.md` (verified 2026-06-05 file states) — still useful pre-flight data for the send-side waves.

## Do NOT

- Do not run `task-execute` on any `tasks/*.poml` in this folder.
- Do not create ADR-033 from here — the unified Communication ADR is authored in R4 wave W0.

**Canonical project**: [`projects/email-communication-solution-r4/design.md`](../email-communication-solution-r4/design.md)
</content>
