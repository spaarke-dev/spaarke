# Agent Framework Knowledge Base — R1

> **Status**: ⏸️ **PARKED — assessment landed; SPEC re-scoping pending** (2026-06-03). The blocking assessment has landed at [`docs/assessments/agent-framework-fit-assessment-2026-06-03.md`](../../docs/assessments/agent-framework-fit-assessment-2026-06-03.md). Before executing task 001, the SPEC should be re-scoped per [`UNBLOCK-RECOMMENDATION.md`](./UNBLOCK-RECOMMENDATION.md) — this file recommends which curation tasks to prioritize, de-prioritize, or rescope based on the per-surface verdicts (1 ADOPT · 5 PARTIAL · 4 DON'T ADOPT).
> **Why parked**: building skills + patterns first would encode a commitment to Agent Framework before Spaarke has decided whether (and where) to adopt `Microsoft.Agents.AI` proper on top of its existing `Microsoft.Extensions.AI` usage. The assessment answered fit-for-purpose per Spaarke surface; this project's SPEC needs refinement to match the assessment's adoption boundaries (de-prioritize curation for DON'T ADOPT surfaces; deepen curation for S5B + the shared middleware lift).
> **How to resume**: see [`UNBLOCK-RECOMMENDATION.md`](./UNBLOCK-RECOMMENDATION.md) §"How to resume knowledge-r1."
> **Owner**: Ralph Schroeder
> **Branch**: `work/coding-knowledge-base-setup-r1` (or split to a dedicated `work/agent-framework-knowledge-r1` worktree if executing in parallel with other work)
> **Created**: 2026-06-03
> **Parked**: 2026-06-03
> **Assessment landed**: 2026-06-03 — see [`docs/assessments/agent-framework-fit-assessment-2026-06-03.md`](../../docs/assessments/agent-framework-fit-assessment-2026-06-03.md) + [`UNBLOCK-RECOMMENDATION.md`](./UNBLOCK-RECOMMENDATION.md)

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
