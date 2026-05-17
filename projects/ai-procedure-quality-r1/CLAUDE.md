# CLAUDE.md — AI Procedure Quality R1

> **Project**: ai-procedure-quality-r1
> **Type**: AI-procedure hardening + GitHub Actions hardening (NO application source changes)
> **Branch**: `work/ai-procedure-quality-r1`
> **Created**: 2026-05-14

## Project Context

This project is **meta** — it modifies the procedure surface (`.claude/`, `CLAUDE.md`, `.github/workflows/`) that the agent itself uses. Take this seriously:

1. Every change is reversible via `.claude/archive/<date>/`.
2. Two human-gate points enforce review before destructive changes.
3. The project's success measure is whether the recurring cadence stays valuable, not whether the one-shot audits complete.

## Key Constraints

1. **No application source-code changes** — anything under `src/` is off-limits except for skills' code pointers (read-only verification).
2. **Reversibility** — every removal/replacement goes to `.claude/archive/<date>/` first. Never `rm` from git history.
3. **High-volume CI/CD posture** — pre-commit checks <3s, GHA gates <30s. Heavyweight runs go on schedule, not per-commit.
4. **Honesty in audits** — do NOT fabricate problems to justify work. If a skill is already in good shape, the audit says so and no refinement happens.
5. **Reference exemplars are opt-in** — skills declare `exemplar: <path>` OR `exemplar: none-too-volatile` with rationale. Do not mandate exemplars on volatile references; the maintenance cost would exceed the value.
6. **Forward-only CHANGELOG** — no back-fill from history.
7. **Researcher subagent reads spaarke/knowledge/ first** — parallel project is building that directory; cross-reference but don't depend on its completion.
8. **Two human gates** — Phase 2b (skills refinements) requires sign-off on `AUDIT-FINDINGS-SKILLS.md`. Phase 3b (CLAUDE.md rewrite) requires sign-off on target outline in `AUDIT-FINDINGS-CLAUDEMD.md`.
9. **Parallel execution within phases** — Phase 0, 1, 4a, 4b are designed for parallel sub-agents. Max concurrency: 6 agents per wave (per /project-pipeline rules).
10. **Build verification between waves** — even though we're not modifying application source, run `dotnet build src/server/api/Sprk.Bff.Api/` between parallel waves as a discipline check (per /project-pipeline rules).

## Permission Boundary (Critical)

**Sub-agents launched via the Agent tool cannot write to `.claude/` paths.** This is intentional. The canonical pattern for this project:

1. Sub-agents **read and audit** `.claude/` files in parallel (the inventory work in Phase 0, the per-skill audits in Phase 2a)
2. Sub-agents **return findings** to the main session as structured output
3. The **main session applies fixes** using Edit/Write tools

When task-create generates tasks that modify `.claude/`, it MUST mark them `parallel-safe: false`. Sub-agents will hit "Edit denied on `.claude/...`" if accidentally dispatched — that's expected behavior, not a bug.

See root [`CLAUDE.md`](../../CLAUDE.md) §3 "Sub-Agent Write Boundary".

## Document Type Directories

| Type | Directory | Notes |
|---|---|---|
| Project artifacts | `projects/ai-procedure-quality-r1/` | This folder |
| Audit findings | `.claude/AUDIT-FINDINGS-*.md` | Created in Phases 2a + 3a; reviewed by human |
| Archive | `.claude/archive/2026-05-14/` | Everything removed/replaced |
| Validators | `scripts/quality/*.ps1` | New + existing |
| GHA workflows | `.github/workflows/*.yml` | Audited + new (procedure-quality, actionlint) |
| Notification procedure | `docs/procedures/github-notification-routing.md` | Phase 4b deliverable |

## Working Pattern for Each Task

1. Read the task's POML from `tasks/`
2. Read the spec's relevant functional requirement (F-1 through F-20)
3. Read any `.claude/AUDIT-FINDINGS-*.md` files relevant (after Phase 2a/3a complete)
4. Apply the change(s) per the task's `<acceptance-criteria>`
5. Update `current-task.md` Quick Recovery section
6. Mark task complete in `TASK-INDEX.md`

## Failure Modes to Watch For (from 2026-05-14 evidence)

These are the failure modes that *justified this project*. The agent should be especially alert to them while executing this project's tasks:

1. **Skill says X but X is wrong** — verify any prescription against a fresh empirical test (e.g., the `build:prod` issue). Don't trust a skill's "NEVER" or "ALWAYS" claim without checking.
2. **Schema malformation silently accepted** — when editing settings.json / hooks / workflow YAML, validate against the published schema before committing.
3. **Defaults sized for old behavior** — when tuning a timeout or retry, verify against current behavior, not historical assumptions.
4. **0-second workflow failures are workflow-startup failures** — not test failures. Look for action version mismatches first.

These should be reflected in `.claude/FAILURE-MODES.md` (Phase 1 deliverable).

## Applicable Skills (loaded per task)

- `task-execute` — loads each task's full context (knowledge files, ADRs, patterns)
- `context-handoff` — checkpoint every 3 steps or 60% context (per root CLAUDE.md)
- `code-review` — at the end, on the PR
- `adr-check` — verify no ADRs are violated
- `push-to-github` — commit/push convention
- `merge-to-master` — at wrap-up

## 🚨 MANDATORY: Task Execution Protocol

When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually. See root [CLAUDE.md](../../CLAUDE.md) for full protocol.

## References

- [spec.md](spec.md) — 20 functional requirements
- [plan.md](plan.md) — phase breakdown + parallel groups
- [README.md](README.md) — project overview
- [design.md](design.md) — claude.ai consultation input
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) — task registry + parallel groups
- [notes/additional-best-practices.md](notes/additional-best-practices.md) — reasoning record
- Root [CLAUDE.md](../../CLAUDE.md) — repository-wide standards (the file this project will eventually rewrite)
