# Code Quality & Assurance R4 — Governance That Enforces Itself

> **Portfolio**: [Project #940](https://github.com/spaarke-dev/spaarke/issues/940) under [Epic #427](https://github.com/spaarke-dev/spaarke/issues/427) `[Epic]: Code Quality` · [Board #2](https://github.com/users/spaarke-dev/projects/2) — one Project Issue; phases = capabilities, no per-phase Issues. Status=Active (Planning), Start 2026-09-03.
> **Status**: Initialized 2026-09-04 via `/project-pipeline` — artifacts + 33 tasks. **Execution is AUTONOMOUS**: run wave by wave without per-wave confirmation, stopping only for a true decision (see [plan.md §3.5](plan.md)), a fired escalation trigger, or a red build.
> **Branch**: `work/code-quality-and-assurance-r4` · **Worktree**: `c:/code_files/spaarke-wt-code-quality-and-assurance-r4`
> **PR**: [#935](https://github.com/spaarke-dev/spaarke/pull/935) (draft)

## Quick Links

- [design.md](design.md) — findings, phase definitions, rejected alternatives
- [spec.md](spec.md) — 27 FRs, 7 NFRs, ADR tensions, scope estimate
- [`docs/assessments/ai-native-development-model-2026-09-03.md`](../../docs/assessments/ai-native-development-model-2026-09-03.md) — the parent frame this project cites

## Overview

**r3 hardened the code. r4 hardens the layer that governs the code.** r3 left live forcing-functions behind so quality *holds* as the codebase grows; r4 applies the same move one level up. The governance surface — 49 ADRs, 16 constraints, 94 patterns, 71 skills, 9,750 tests — is authored well, enforced thinly, and decays because nothing fires when it does.

Lineage: **r1** (quality *system*) → **r2** (first structural remediation) → **r3** (multi-surface program, ✅ 2026-08-14) → **r4** (the governance layer itself).

## Two workstreams

| # | Workstream | Objective |
|---|---|---|
| **1** | Don't rebuild, don't diverge | We don't rebuild functionality we already have, and we don't run **the same function in two different code paths** |
| **2** | Enforcement & continuity | Rules we already wrote are enforced where enforceable, verified for accuracy, and maintained without anyone remembering to |

## Phases

Each phase is a **capability** — deployable, functionally complete, independently valuable. **r4 can stop cleanly after any of them.**

| Phase | Capability | FRs |
|---|---|---|
| **P1** | The shared surface is knowable | FR-01 … FR-04 |
| **P2** | Every ADR is routed, accurate, and measured | FR-05 … FR-09 |
| **P3** | The governance surface maintains itself | FR-10 … FR-15 |
| **P4** | Don't rebuild, don't diverge | FR-16 … FR-20 |
| **P5** | Tailored review that actually runs, over a visible test suite | FR-21 … FR-27 |

## Measured baseline (2026-09-03)

- ADR enforcement **7/49**; `.claude/adr/INDEX.md` lists **36 of 49**
- **55 of 71** skills unreviewed for 3+ months; **0/16** constraints and **0/94** patterns carry a *machine-parseable* review stamp — they carry a human-readable `> **Last Reviewed**:` blockquote instead (87/94 and 15/16), so the gap is two competing conventions, not missing dates (measured 2026-09-04; rewrites FR-10)
- **9,750** test methods, **68** `Skip=` occurrences (down from 168 on 2026-08-19), Flaky traits with no expiry
- **Four specified quality cadences, none of which ever ran** — including a complete nightly reviewer committed 2026-03-14 and never wired, while three docs describe a `nightly-quality.yml` that does not exist

## Scope fence

Exactly **one** new CI workflow. No new agent, no new skill, no new POML block, no second home for reuse rules. **No thresholds** on test count, duplication percentage, or file size — count-proxies for judgment questions are the retired God-class ratchet.

Explicitly out of scope: un-packaging any existing shared component; any reviewer sub-agent; remediating the 68 skipped tests ([#794](https://github.com/spaarke-dev/spaarke/issues/794)); merging the known-divergent code paths.

## Estimate

~32–42 tasks — comparable to r3 (35) and `ci-cd-unit-test-remediation-r1` (45). **P1 alone is 3–4 tasks and ships in days**; the commitment is one phase at a time.
