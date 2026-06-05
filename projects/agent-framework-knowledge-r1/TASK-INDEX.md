# Task Index — Agent Framework Knowledge Base R1

> **PROJECT PARKED** ⏸️ — blocked on outcome of [`agent-framework-fit-assessment-r1`](../agent-framework-fit-assessment-r1/). See [`README.md`](./README.md) for context. Do NOT execute task 001 until the assessment lands.
>
> **Legend**: 🔲 pending · ▶️ in progress · ✅ complete · ⚠️ blocked/gap

Canonical plan: [`SPEC.md`](./SPEC.md) · Project conventions: [`CLAUDE.md`](./CLAUDE.md)

---

## Phase progress

| # | Phase | Status | Notes |
|---|---|---|---|
| 1 | Curation foundation (tasks 001-002) | 🔲 | Refresh provenance + curate Spaarke-aligned .NET samples |
| 2 | Reference docs — parallel-safe group B (tasks 003-005) | 🔲 | Snapshot Microsoft Learn pages |
| 3 | Project commentary (tasks 006-007) | 🔲 | Community/MVP capture + Spaarke-grounded NOTES.md rewrite |
| 4 | Discoverability (task 008) | 🔲 | Write docs/INDEX.md |
| 5 | Skill activation (tasks 009-010) | 🔲 | Patterns + SKILL.md (main session only) |
| 6 | Verification + wrap-up (tasks 011-013) | 🔲 | Skill verification + root pointer wiring + sign-off |

## Tasks

| ID | Title | Phase | Rigor | Parallel group | Status | Owner |
|---|---|---|---|---|---|---|
| [001](tasks/001-refresh-provenance.poml) | Refresh upstream provenance + baseline diff | 1 | STANDARD | A | 🔲 | — |
| [002](tasks/002-curate-spaarke-aligned-samples.poml) | Curate Spaarke-aligned .NET samples | 1 | STANDARD | A | 🔲 | — |
| [003](tasks/003-snapshot-core-reference-docs.poml) | Snapshot core reference docs (chat client, tools, providers, structured outputs, sessions) | 2 | STANDARD | B | 🔲 | — |
| [004](tasks/004-snapshot-spaarke-critical-docs.poml) | Snapshot Spaarke-critical docs (middleware, observability, MCP clients) | 2 | STANDARD | B | 🔲 | — |
| [005](tasks/005-snapshot-migration-and-hosted-docs.poml) | Snapshot migration + hosted-agents docs | 2 | STANDARD | B | 🔲 | — |
| [006](tasks/006-capture-community-mvp-content.poml) | Capture community / MVP content (best-effort; release is new) | 3 | STANDARD | C | 🔲 | — |
| [007](tasks/007-rewrite-notes-md.poml) | Rewrite NOTES.md substantively from Spaarke code | 3 | STANDARD | C | 🔲 | — |
| [008](tasks/008-write-docs-index.poml) | Write knowledge/agent-framework/docs/INDEX.md | 4 | MINIMAL | — | 🔲 | — |
| [009](tasks/009-create-pattern-files.poml) | Create .claude/patterns/ai/agent-framework-*.md pointer files | 5 | STANDARD | — | 🔲 | — |
| [010](tasks/010-create-skill-file.poml) | Create .claude/skills/agent-framework-component/SKILL.md | 5 | STANDARD | — | 🔲 | — |
| [011](tasks/011-verify-skill-output.poml) | Verify skill influences agent output on realistic prompts | 6 | FULL | — | 🔲 | — |
| [012](tasks/012-wire-discoverability.poml) | Update root CLAUDE.md, .claude/skills/INDEX.md, REFRESH-LOG | 6 | STANDARD | — | 🔲 | — |
| [013](tasks/013-project-wrap-up.poml) | Project sign-off + completion entry | 6 | MINIMAL | — | 🔲 | — |

## Parallel execution groups

- **Group A** (tasks 001, 002): 002 depends on 001 (needs the pinned SHA). Execute sequentially.
- **Group B** (tasks 003, 004, 005): All three snapshot independent docs. Safe to fan out in parallel via `task-execute` from main session in one message (per root CLAUDE.md §4 — multiple Skill tool calls in one message).
- **Group C** (tasks 006, 007): 006 (community capture) and 007 (NOTES rewrite) are independent in source but share the SOURCE.md GAPs section. Recommend sequential to avoid merge conflicts on SOURCE.md.
- **Tasks 008, 009, 010** sequential — 008 depends on completed docs; 009 informs 010 (SKILL.md references patterns); 010 must run AFTER 009.
- **Tasks 011, 012, 013** sequential — verification informs whether discoverability wiring is final.

## Gaps / blocks log

_None yet — populate as tasks execute. Mirror the format used in `coding-knowledge-base-setup-r1/TASK-INDEX.md` Gaps section._

## Reference

- Canonical plan: [`SPEC.md`](./SPEC.md)
- Project conventions: [`CLAUDE.md`](./CLAUDE.md)
- Status overview: [`README.md`](./README.md)
- Current task state: [`current-task.md`](./current-task.md)
