# Agent Framework Knowledge Base — R1

> **Status**: ⏸️ **PARKED** — blocked on outcome of [`agent-framework-fit-assessment-r1`](../agent-framework-fit-assessment-r1/). Do not execute task 001 until the assessment lands at `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` and its recommendations are reviewed.
> **Why parked**: building skills + patterns first would encode a commitment to Agent Framework before Spaarke has decided whether (and where) to adopt `Microsoft.Agents.AI` proper on top of its existing `Microsoft.Extensions.AI` usage. The assessment answers fit-for-purpose per Spaarke surface; this project's SPEC will be refined based on those conclusions (e.g., surfaces excluded from adoption do NOT get patterns; surfaces added in scope may need additional curation).
> **Owner**: Ralph Schroeder
> **Branch**: `work/coding-knowledge-base-setup-r1` (or split to a dedicated `work/agent-framework-knowledge-r1` worktree if executing in parallel with other work)
> **Created**: 2026-06-03
> **Parked**: 2026-06-03

---

## Goal

Bring `knowledge/agent-framework/` from a thin starter (4 samples, 3 docs, stub NOTES.md) to **full Fluent-V9 parity** so Claude Code can author and modify **Microsoft Agent Framework** code in Spaarke's BFF with the same rigor it has for Fluent UI v9 work.

**Problem solved**: Spaarke is already in production with Agent Framework primitives (`SprkChatAgent` over `IChatClient`, the middleware pipeline, `AIFunction` tool registration) but Claude has thin context — it will reach for AutoGen-style or Semantic-Kernel-style idioms when modifying this code, producing valid-looking but wrong patterns. The platform was released early 2026, after Claude's training cutoff.

## Canonical plan

[`SPEC.md`](./SPEC.md) is the authoritative build spec. This README is a status overview — don't duplicate plan content.

## Execution approach

- **POML-decomposed** — 13 tasks across 6 phases (foundation → docs → commentary → discoverability → skill → verification)
- **Standard `task-execute` per task** — rigor levels are documented in each POML
- **Parallel-safe groups** documented in each POML's `<metadata>` block (Phase 2 docs are parallel-safe; Phase 5 skill creation must run sequentially after patterns)
- **Sub-agent write boundary** respected — sub-agents handle `knowledge/` writes; main session handles all `.claude/` writes (per root CLAUDE.md §3)

## Phase summary

| Phase | Tasks | Purpose |
|---|---|---|
| **1. Curation foundation** | 001, 002 | Refresh provenance + curate Spaarke-aligned .NET samples |
| **2. Reference docs (parallel-safe)** | 003, 004, 005 | Snapshot Microsoft Learn pages |
| **3. Project commentary** | 006, 007 | Community/MVP capture + rewrite NOTES.md from Spaarke code |
| **4. Discoverability** | 008 | Write docs/INDEX.md |
| **5. Skill activation** | 009, 010 | Patterns + SKILL.md (main session — `.claude/` write boundary) |
| **6. Verification + wrap-up** | 011, 012, 013 | Skill output verification + root pointer wiring + sign-off |

Detail per task: [`TASK-INDEX.md`](./TASK-INDEX.md).

## Constraints (summary — full list in [SPEC.md §7](./SPEC.md))

1. No Spaarke runtime edits (read-only on `.cs` files)
2. No invented URLs or sample content; preserve provenance
3. .NET samples only (mirror existing topic curation rule)
4. `.claude/` writes from main session only
5. Stub banner removed on NOTES.md only when both §1 + §2 have substantive Spaarke-grounded content
6. Honest gaps logged in SOURCE.md, not silently skipped

## Risks tracked

| Risk | Mitigation |
|---|---|
| Microsoft Learn URLs 404 / move (already seen on `/concepts/agents`) | Log GAP + find canonical replacement; record substitution in frontmatter |
| Upstream sample churn between refresh and project completion | Pin SHA at task 001; do not re-pull mid-project unless logged |
| Spaarke code shape changes during project | Re-verify file paths at task 007; task 011 as guardrail |
| Skill triggers too aggressively / under-triggers | Verification step (task 011) tunes `appliesTo` / triggers if needed |
| Community content quality is thin (release is new) | Accept low count honestly; document in SOURCE.md GAPs; do not pad |

## How to start work

```text
work on task 001
```

The harness will invoke `task-execute` with `tasks/001-refresh-provenance.poml`. Per root CLAUDE.md §4, every task in this project MUST go through `task-execute` — do NOT read POML files directly.
