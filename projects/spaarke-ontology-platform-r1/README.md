# Spaarke Ontology Platform R1

> **Status**: Ready for implementation — initialized 2026-10-03 by `/project-pipeline`
> **Branch**: `docs/ontology-platform-design` · PR [#1111](https://github.com/spaarke-dev/spaarke/pull/1111) (draft)
> **Worktree**: `c:\code_files\spaarke-wt-spaarke-ontology-platform-r1`
> **Portfolio**: [Project #1227](https://github.com/spaarke-dev/spaarke/issues/1227) · [Board #2](https://github.com/users/spaarke-dev/projects/2) — _auto-registered 2026-10-04 (existence only; Epic/Task Count/Start Date not yet set)_

## What this project builds

The **intelligence layer from the Spaarke data model forward** — a declaration layer over Dataverse that
matches already-present data against the customer's own declared rules and **records what was decided**.

It does **not** build the connector that brings a third-party system's data in, and it does **not** parse
LEDES. Invoice, budget and spend values arrive as computed metrics.

### The one thing that matters

Everything in this project exists to make **one predicate** computable, recordable and re-tunable without a
deployment:

> A communication on this matter was classified as a **fee or scope change** within the window, **and** no
> **budget revision** was recorded in that same window.

Two conjuncts, two sources. An e-billing platform holds the budget and never sees the email; a mail system
sees the email and holds no budget. **Only a system holding both can evaluate the conjunction.**

### The chain

A **Policy** declares a condition → evaluation writes a **Signal** (`sprk_signal`, the trigger) → the Signal
presents as a **Work Item** (the actionable unit) → acting **dispositions** it and writes a **Decision
Record**. A worklist row is a **matter grouping Work Items**.

## Documents — read in this order

| Document | Role |
|---|---|
| [`spec.md`](spec.md) | **The specification.** 44 FRs, 9 NFRs, 11 success criteria. What tasks implement |
| [`plan.md`](plan.md) | **The WBS.** 10 phases, parallel groups, critical path, risks |
| [`design.md`](design.md) | **The decisions.** rev 11 — §10 = 29 settled · §8.0c = D-9..D-12 · §8.3 = evaluation cadence |
| [`CLAUDE.md`](CLAUDE.md) | AI context for this project — load every session |
| [`current-task.md`](current-task.md) | Active task state. Recovery starts here |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Task registry, status, dependencies, waves |

**Notes** (evidence, not decisions): [`notes/schema-draft.md`](notes/schema-draft.md) ·
[`notes/security-roles.md`](notes/security-roles.md) ·
[`notes/mvp-technical-spec.md`](notes/mvp-technical-spec.md) ·
[`notes/reuse-verification-2026-10-02.md`](notes/reuse-verification-2026-10-02.md) ·
[`notes/daily-briefing-ontology-fit.md`](notes/daily-briefing-ontology-fit.md) ·
[`notes/ontology-component-model.md`](notes/ontology-component-model.md) (vocabulary authority) ·
[`notes/defer-issues.md`](notes/defer-issues.md).

## Graduation criteria

The project is complete when all eleven success criteria in [`spec.md`](spec.md) §7 pass. Three of them can
fail the project rather than merely be unfinished:

- **Criterion 2** — the cross-source rule fires with both conjuncts present and does **not** fire when
  either is absent. ⚠️ Seeded data proves the predicate *evaluates*; only data nobody authored to make it
  pass proves it *fires on reality*. **Do not let a green criterion 2 over seeded rows retire the risk.**
- **Criterion 10** — *something happens in the world*: a confirmed Signal produces an Inquiry through the
  gate, carrying an SLA, and the reply resolves it with a disposition queryable per matter and per outside
  firm.
- **Criterion 11** — classifier recall **≥80% on a ≥50-item labelled set**. The predicate is a conjunction,
  so it inherits its weakest input. At 70% recall the differentiated claim silently misses 30% of real
  cases **while every other criterion passes green.**

## Current state

**Dataverse** (`spaarkedev1`, solution `OntologyPlatformSolution`, publisher **Spaarke**):

| Table | OTC | `sprk_` cols | Rows |
|---|---|---|---|
| `sprk_signal` | 11003 | 59 | 0 |
| `sprk_decisionrecord` | 11002 | 22 | 0 |
| `sprk_policy` | 11000 | 17 | 0 |
| `sprk_policyversion` | 11001 | 17 | 0 |
| `sprk_budgetrevision` | 10999 | 15 | 0 |

Both alternate keys **Active**. Auditing on all five **and** at org level. Three roles created, assigned and
privilege-verified (`Spaarke Console User` · `Spaarke Ontology Administrator` · `Spaarke Ontology Service`).

⬜ **Four columns outstanding** — task 001: `sprk_decisionrecord.sprk_action`, and on `sprk_servicerequest`
the `sprk_direction` discriminator, `sprk_disposition` and `sprk_responseduedate`.

## Hot-path declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```

⚠️ **56 of 62 projects in [`projects/INDEX.md`](../INDEX.md) declare a hot path.** Run `/conflict-check`
before **every** BFF PR.

## How to work on this

```
"continue"                → reads TASK-INDEX.md, finds the first 🔲, invokes task-execute
"work on task 001"        → invokes task-execute with that POML
"pick up where we left off" → loads current-task.md
```

**Never read a `.poml` and implement manually** — CLAUDE.md §4 requires `task-execute`, which loads the
ADRs, constraints and knowledge files the task depends on and runs the Step 9.5 quality gates.
