# God-class ratchet — RETIRED (2026-08-20)

> **This gate no longer exists.** `GodClassGuardTests` (the hard CI ratchet on `src/server` file LOC) was
> **removed** on 2026-08-20. It gated on **line count** — the wrong instrument for a gradual, judgment-laden
> signal — froze existing large files at arbitrary values, and blocked normal feature work on active files
> (Compose, Chat) with a hard build failure that had to be silenced by hand-bumping a waiver.

## What replaced it

**File size is a symptom; complexity is the concern.** A large *cohesive, single-responsibility* file can be the
right design; a small tangled one can be worse. So size is now **observed**, and **complexity is evaluated by
humans where the work is authored** (per ADR-038's "coverage = observation, never a gate", applied to size):

- **Standard**: [`docs/standards/COMPONENT-COMPLEXITY.md`](../../../docs/standards/COMPONENT-COMPLEXITY.md) — evaluate
  complexity/cohesion (responsibilities, coupling, ctor deps, branching), not LOC; when a large file is legitimate;
  decompose when responsibilities diverge.
- **Root rule**: `CLAUDE.md` §11.5 (Component complexity).
- **Authoring**: `task-create` §3.5.6 (component-complexity check).
- **Review**: `code-review` maintainability dimension (complexity *direction*, not size).
- **Observation (non-blocking)**: `scripts/report-large-server-files.ps1` — lists large `src/server` files to feed
  the deliberate decomposition backlog; **never fails a build**.
- **Deliberate decomposition** of the worst offenders: the RED-1 / RED-2 / RED-4-C seed projects.

Historical references to this file elsewhere in the repo (RED analyses, design docs, handoffs) describe the
now-removed gate; they are left as record.
