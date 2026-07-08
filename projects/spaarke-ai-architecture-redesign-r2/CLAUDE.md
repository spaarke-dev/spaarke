# Spaarke AI Architecture Redesign R2 (Core) - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-ai-architecture-redesign-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Ready for Tasks
- **Last Updated**: 2026-07-08
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files (task decomposition = pipeline Step 3)

---

## Quick Reference

### Key Files
- [`design.md`](design.md) - Charter (v0.4) — permanent reference
- [`spec.md`](spec.md) - 51 FRs, browser-UAT-gated
- [`plan.md`](plan.md) - WBS + Component Justification + discovered `file:line` anchors
- [`notes/d-f0-eval-family-spec.md`](notes/d-f0-eval-family-spec.md) - D-F0(e) resourcefulness eval family
- [`notes/policy-v2-origin-classification-decision-tree.md`](notes/policy-v2-origin-classification-decision-tree.md) - Policy v2 + E-1..E-6
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)

### Project Metadata
- **Project Name**: spaarke-ai-architecture-redesign-r2
- **Type**: BFF / AI-platform core (judgment + memory)
- **Complexity**: High

---

## Context Loading Rules

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md + plan.md** for requirements, WBS, and the discovered `file:line` anchors
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

task-execute ensures: knowledge/ADRs loaded, current-task.md tracked, checkpointing every 3 steps, quality gates (code-review + adr-check) at Step 9.5, recoverable progress.

### Parallel Task Execution
Tasks with satisfied dependencies + non-overlapping files → ONE message with MULTIPLE task-execute calls. **`.claude/` tasks are main-session-only (sub-agent write boundary)** — task-create marks them `parallel-safe: false`. MAX 6 agents per wave.

---

## Execution Model & Tiering (Sonnet-5 — CLAUDE.md §8.5)

- **Planning** (design-to-spec, project-pipeline Steps 0–3): Opus 4.8 / Fable 5.
- **Execution**: default **Sonnet 5 @ effort `high`**; per-POML `<model-tier>` + `<effort>`.
- **This project's tiering** (per plan §5): contracts (010–017), gate/completion/trace (030/032/035/036/038), memory/governance (050–058), ADRs (043/065) → **opus/fable**; catalog-row, test-repair, hygiene, docs → **sonnet**. `xhigh` reserved for brownfield/root-cause.
- FULL-rigor gates (code-review + adr-check) stay unconditional + coverage-first.

---

## Key Technical Constraints

- **Build ON ADR-039/040** — no second dispatch protocol, no parallel session cache, no routing outside Bindings.
- **Determinism for side effects; reads are free** (D-F0(b)); D-F0 never weakens a gate/hard block.
- **Store before render** (ADR-040).
- **Structured memory objects, not embeddings** (User/Workspace scopes) — **EXTEND `MatterMemoryService`**, don't greenfield.
- **Publish seams FIRST** (Phase A0) so Compose r2 is never blocked.
- **Triple-twin hoist (task 020) BEFORE any catalog-row task.**
- **Untrusted content can NEVER originate a memory write.**
- **BFF Hygiene (root CLAUDE.md §10)** — load `.claude/constraints/bff-extensions.md` before adding to `Sprk.Bff.Api`; publish-size ≤60 MB per-task; Placement Justification in PRs; use `PublicContracts/` facade.

---

## Decisions Made

- 2026-07-08 — Compose r1 kept separate (executed+closed, not absorbed); Compose r2 is a separate parallel project consuming core seams (already re-based). Core keeps FULL seam set. — operator
- 2026-07-08 — Daily Briefing remediation → separate project (not core Wave 0). — operator
- 2026-07-08 — Memory Service EXTENDS existing `MatterMemoryService`/Cosmos `memory` container (discovery finding); Q4 "new container" reconciled to scope-extension at FR-B-01. — pipeline discovery
- 2026-07-08 — Work IQ: provider interface in scope; researcher spike deferred. — operator

---

## Implementation Notes

*No notes yet* — see plan.md §3 for the discovered `file:line` reuse map.

---

## Deferrals & Issues — tracking obligation

Track deferred work + issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues via `/project-defer-issue-tracking` (`/defer`). CLAUDE.md §11 applies — every entry names a concrete failing behavior/contract. `push-to-github` blocks push on entries without GitHub URLs.

Named deferrals for close (090): Work IQ/Foundry IQ researcher spike + runtime providers; workspace-intelligence goal-tracking subsystem; admin observability dashboards; Spaarke-as-MCP-server outbound surface.

---

## Resources

### Applicable ADRs
039, 040 (binding), 013, 037, 015, 029, 032, 038; standing set 008/009-014/010/016/018/019/028/030/031/036. New candidates: **ADR-041** (Judgment/Confirmation/Completion), **ADR-042** (Memory Architecture/Governance).

### Related Projects
- `spaarkeai-compose-r2` — seam consumer (parallel; BFF+SpaarkeAi)
- `spaarke-ai-architecture-redesign-r1` — predecessor (ADR-039/040 shipped)
- Daily Briefing remediation — separate project (consumes GroundednessCheck threshold→action pattern)

### External Documentation
- `.claude/constraints/bff-extensions.md`, `.claude/constraints/azure-deployment.md`
- `docs/adr/ADR-039-*.md`, `docs/adr/ADR-040-session-ledger.md`
- `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`

---

*This file should be kept updated throughout project lifecycle*
