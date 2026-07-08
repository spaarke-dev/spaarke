# spaarke-ai-architecture-redesign-r1

> **Portfolio**: [Project #550](https://github.com/spaarke-dev/spaarke/issues/550) · Parent Epic [#421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421) · Board [Project #2](https://github.com/users/spaarke-dev/projects/2)
> **Created**: 2026-07-05 · **Target**: 2026-08-15
> **Status**: **COMPLETE** 2026-07-08 — 51/51 tasks + 090 wrap-up. Gates: G-P0..G-P3 PASSED (browser UAT, 6 rounds at G-P3); G-P4 GREEN with publish-size AMBER pending operator sign-off ([`notes/g-p4-evidence.md`](notes/g-p4-evidence.md)); G-M **DEFERRED-WITH-EVIDENCE** post-r2 by operator ruling ([`notes/g-m-evidence.md`](notes/g-m-evidence.md), issue [#555](https://github.com/spaarke-dev/spaarke/issues/555)). ADR-039/040 Accepted. Deferrals: [#552–#557](notes/defer-issues.md). Lessons: [`notes/lessons-learned.md`](notes/lessons-learned.md). Successor: [`../spaarke-ai-architecture-redesign-r2/design.md`](../spaarke-ai-architecture-redesign-r2/design.md) (v0.2)

## What this project delivers (the one-liner)

**A working Spaarke Assistant**: drop in a document or type a plain-language
request and reliably get analysis, cited answers over documents AND Dataverse,
records created with confirmation, and drafts — composing across steps in one
conversation. And afterward, new legal capabilities are catalog rows a business
analyst authors, not engineering projects.

## Where this came from

The 2026-07-04/05 strategic pivot + `spaarke-ai-code-audit-r1` (3 steps:
full-estate inventory → converged target design → migration map). All
architecture decisions are **ratified** — this project implements, it does not
re-open design.

| Input | Location |
|---|---|
| **spec.md** (FR/NFR contract — 42 FRs, 11 NFRs) | [`spec.md`](spec.md) |
| **plan.md** (WBS, waves, /goal conditions) | [`plan.md`](plan.md) |
| **Task tracker** (51 POML tasks) | [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) |
| **AI context** | [`CLAUDE.md`](CLAUDE.md) |
| **design.md** (this project's charter — review this) | [`design.md`](design.md) |
| Target architecture (living, canonical) | [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) **v0.4** |
| Frozen audit inputs (inventory, greenfield+Q&A, overlay matrix, ADR review, migration map, MCP research, auditor evidence) | [`notes/audit-inputs/`](notes/audit-inputs/README.md) |
| New governance | ADR-039 (Grounded Execution & Closed Catalogs) + ADR-040 (Session Ledger) — Proposed; promoted to Accepted at phases P1/P0 |

## Shape of the work

Five phases with **user-verifiable UAT gates** (G-P1..G-P4, design.md §2) and
hard cutovers per surface; a continuous Track-B deadwood sweep from day one.
Hot paths: BFF=Y, SpaarkeAi=Y, skill-directives=Y.
