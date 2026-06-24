# Task Index — Spaarke DevOps Project Tracking (r1)

> **Last Updated**: 2026-06-23
> **Total Tasks**: 38 (16 complete · 22 pending)
> **Status**: 🔄 Phase 1 ✅ · Phase 2 ✅ (9 /devops-* skills + verify gate) · Phase 3 next (active backfill)

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| ⛔ | Blocked |
| 🔁 | Needs retry (parallel-batch failure) |

---

## Critical-Path Summary

- **Phase gates (sequential)**: 008 → 019 → 022 → 039 → 042 → 053 → 090
- **Load-bearing tasks**: 010 (`/devops-portfolio-setup` codifies Phase 1), 014 (`/devops-project-start` — THE BLESSED HANDOFF), 016 (`/devops-project-sync` — called by 5 hooks)
- **Sub-Agent Write Boundary**: All Phase 2, Phase 4, and task 052 modify `.claude/skills/` or root `CLAUDE.md` — main session only (parallel-safe: false)
- **Parallel-safe phases**: Phase 1 schema (P1-schema), Phase 6 docs (P6-docs)
- **Total estimated effort**: ~60 hours sequential / ~40-50 hours with maximum parallelism

---

## Phase 1: Foundation — Project #2 schema + Epics

> Hand-driven `gh` commands; Phase 2 task 010 codifies this into idempotent `/devops-portfolio-setup` skill.

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 001 | Extend Project #2 Type field with `Project` option | 1h | none | P1-schema | ✅ |
| 002 | Add 6 new custom fields to Project #2 | 2h | 001 | P1-schema | ✅ |
| 003 | Create 7 repository labels | 1h | none | P1-schema | ✅ |
| 004 | Land 3 issue templates (epic.yml, project.yml, idea.yml) | 2h | none | P1-schema | ✅ |
| 005 | Create 12 initial Epic Issues per §4.6 taxonomy | 2h | 003, 004 | none | ✅ |
| 008 | **Phase 1 verify gate** — Foundation schema complete | 1h | 001, 002, 003, 004, 005 | none | ✅ |

**Phase 1 Total**: ~9 hours · **Parallel opportunity**: tasks 001-004 in P1-schema can run in parallel (but they're all main-session-only `gh` invocations; user runs sequentially)

---

## Phase 2: Skills — 9 new `/devops-*` skills

> All tasks in this phase modify `.claude/skills/devops-*/SKILL.md` — Sub-Agent Write Boundary applies. Main session sequential only.

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 010 | Create `/devops-portfolio-setup` (codifies Phase 1) | 3h | 008 | none | ✅ |
| 011 | Create `/devops-epic-create` | 2h | 010 | none | ✅ |
| 012 | Create `/devops-idea-create` | 1.5h | 010 | none | ✅ |
| 013 | Create `/devops-idea-promote` (Path A + Path B) | 3h | 010, 012 | none | ✅ |
| 014 | Create `/devops-project-start` — **THE BLESSED HANDOFF** | 5h | 010, 013 | none | ✅ |
| 015 | Create `/devops-project-register` (inverse of 014) | 3h | 010, 014 | none | ✅ |
| 016 | Create `/devops-project-sync` (idempotent + partial-success) | 3h | 010, 015 | none | ✅ |
| 017 | Create `/devops-portfolio-status` (dashboard + snapshot) | 2.5h | 010 | none | ✅ |
| 018 | Create `/devops-project-archive` (worktree delete + folder retain) | 2.5h | 010, 016 | none | ✅ |
| 019 | **Phase 2 verify gate** — 9 skills smoke-tested end-to-end | 2h | 010-018 | none | ✅ |

**Phase 2 Total**: ~27.5 hours · Sequential only (Sub-Agent Write Boundary)

---

## Phase 3: Active backfill (~20–30 projects)

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 020 | Enumerate active/in-flight projects per F6 | 1.5h | 019 | none | 🔲 |
| 021 | Backfill active projects via `/devops-project-register` | 3h | 020 | none | 🔲 |
| 022 | **Phase 3 verify gate** — backfill complete | 1h | 021 | none | 🔲 |

**Phase 3 Total**: ~5.5 hours · Sequential (rate-limit hygiene per NFR-05)

---

## Phase 4: Automation hooks into 9 existing skills

> All tasks modify `.claude/skills/<existing>/SKILL.md` — Sub-Agent Write Boundary applies. Main session sequential only. Order matters: ordered by impact + risk (highest-value hooks first; risky hooks with confirm-gates last).

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 030 | Hook `/design-to-spec` (FR-16) — post-spec sync + Status=In Progress | 1h | 019, 016 | none | 🔲 |
| 031 | Hook `/project-pipeline` (FR-17) — register-or-sync at start | 1h | 019, 015, 016 | none | 🔲 |
| 032 | Hook `/task-create` (FR-18) — set Task Count | 0.5h | 019, 016 | none | 🔲 |
| 033 | Hook `/task-execute` (FR-19) — increment Tasks Completed | 1h | 019, 016 | none | 🔲 |
| 034 | Hook `/context-handoff` (FR-20) — **HIGHEST VALUE**: always sync | 1h | 019, 016 | none | 🔲 |
| 035 | Hook `/worktree-setup` (FR-21) — link-or-prompt register | 0.5h | 019, 015 | none | 🔲 |
| 036 | Hook `/worktree-sync` (FR-22) — end-of-sync sync | 0.5h | 019, 016 | none | 🔲 |
| 037 | Hook `/repo-cleanup` (FR-23) — archive-candidate prompt | 1h | 019, 018 | none | 🔲 |
| 038 | Hook `/merge-to-master` (FR-24) — PR comment + conditional archive prompt | 1h | 019, 016, 018 | none | 🔲 |
| 039 | **Phase 4 verify gate** — 9 hooks active + no regressions | 2h | 030-038 | none | 🔲 |

**Phase 4 Total**: ~9.5 hours · Sequential only

---

## Phase 5: Polish for shared audience

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 040 | Configure 6 portfolio views on Project #2 | 2h | 022, 039 | none | 🔲 |
| 041 | Audit per-project README pointer blocks across active projects | 1h | 022, 040 | none | 🔲 |
| 042 | **Phase 5 verify gate** — Portfolio Roadmap usable + polish complete | 1h | 040, 041 | none | 🔲 |

**Phase 5 Total**: ~4 hours · Sequential

**Note**: FR-27 (auto-comment Action) and FR-28 (scheduled sync) deferred per F5 default. Revisit if drift visible.

---

## Phase 6: Documentation

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 050 | Extend `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` | 2h | 019, 042 | P6-docs | 🔲 |
| 051 | Extend `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` | 2h | 019, 042 | P6-docs | 🔲 |
| 052 | Update root `CLAUDE.md` §17 Pointers + `.claude/CHANGELOG.md` | 1h | 050, 051 | none | 🔲 |
| 053 | **Phase 6 verify gate** — all doc extensions land cleanly | 1h | 050, 051, 052 | none | 🔲 |

**Phase 6 Total**: ~6 hours · Tasks 050+051 parallel-safe (different files); 052 must follow them (touches root CLAUDE.md)

---

## Wrap-up

| # | Task | Effort | Dependencies | Parallel | Status |
|---|------|--------|--------------|----------|--------|
| 090 | Project wrap-up — graduation criteria audit + lessons-learned + ready for merge | 2h | 053 | none | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| P1-schema | 001, 002, 003, 004 | none | All `gh` CLI; main session sequential in practice (one operator); not delegatable to sub-agents (writes to `.github/`) |
| P6-docs | 050, 051 | 019 (skills exist) + 042 (Phase 5 complete) | `docs/guides/*` + `docs/procedures/*` are different files — true parallel-safe; sub-agent delegation OK because NOT under `.claude/` |

**Note on parallel execution**: Per root CLAUDE.md §3 Sub-Agent Write Boundary, all `.claude/skills/` modifications are main-session-only. This affects Phase 2 (all tasks), Phase 4 (all tasks), and task 052 (root CLAUDE.md). True parallel sub-agent delegation only applies to P6-docs (tasks 050 + 051, which write under `docs/`).

---

## Dependency Graph (text representation)

```
Phase 1 sequence: 001 ─→ 002 ─→ 008
                  003 ─→ 005 ─→ 008
                  004 ─→ 005 ─→ 008

Phase 2 sequence: 008 ─→ 010 ─→ 011, 012, 017
                                012 ─→ 013 ─→ 014 ─→ 015 ─→ 016 ─→ 018
                                                                    └→ 019
                                017 ────────────────────────────────→ 019
                                011 ────────────────────────────────→ 019

Phase 3 sequence: 019 ─→ 020 ─→ 021 ─→ 022

Phase 4 sequence: 019 + 016 + 018 ─→ 030..038 (each independent in source-file terms, but ordered by impact + risk) ─→ 039
                  019 + 015        ─→ 035
                  019 + 018        ─→ 037, 038

Phase 5 sequence: 022 + 039 ─→ 040 ─→ 041 ─→ 042

Phase 6 sequence: 019 + 042 ─→ 050, 051 (parallel-safe) ─→ 052 ─→ 053

Wrap-up:          053 ─→ 090
```

---

## Critical Path

Longest dependency chain (~estimated 50 hours):

```
001 (1h) → 002 (2h) → 005 (2h) → 008 (1h) →
010 (3h) → 011 (2h) → 012 (1.5h) → 013 (3h) → 014 (5h) → 015 (3h) → 016 (3h) → 018 (2.5h) → 019 (2h) →
020 (1.5h) → 021 (3h) → 022 (1h) →
030 (1h) → 031 (1h) → 032 (0.5h) → 033 (1h) → 034 (1h) → 035 (0.5h) → 036 (0.5h) → 037 (1h) → 038 (1h) → 039 (2h) →
040 (2h) → 041 (1h) → 042 (1h) →
050 (2h) → 051 (2h) → 052 (1h) → 053 (1h) →
090 (2h)
```

Critical-path total: ~52 hours sequential. Phase 2 (~27.5h) and Phase 4 (~9.5h) dominate.

---

## High-Risk Items

| Risk | Mitigation | Tasks |
|---|---|---|
| Phase 1 schema change visible on 22 existing items | Additive only; existing items default to `Mixed` or null on new fields | 001, 002 |
| Rate-limit during Phase 3 backfill | Batch of 20 + exponential backoff per NFR-05 | 021 |
| Hook injection regression in existing skills | Additive only; per-hook smoke + Phase 4 verify gate | 030-038, 039 |
| `/devops-project-start` (BLESSED HANDOFF) bugs cascade | Heavy smoke testing in task 014; idempotency test before Phase 3 backfill begins | 014, 019 |
| Concurrent PR `.github/workflows/*.yml` merges block Phase 5 Actions | Phase 5 Actions are optional (FR-27, FR-28); rebase if needed | 040 |

---

## Phase Gate Summary

| Gate | Task | Blocks | Spec Verification |
|---|---|---|---|
| Phase 1 verify | 008 | Phase 2 start | FR-01..FR-05 + SC#1, SC#2 |
| Phase 2 verify | 019 | Phase 3 + 4 start | SC#3, SC#5 + end-to-end smoke |
| Phase 3 verify | 022 | Phase 4 start | SC#4, SC#10 |
| Phase 4 verify | 039 | Phase 5 start | SC#6, SC#12 |
| Phase 5 verify | 042 | Phase 6 start | SC#7, SC#10 |
| Phase 6 verify | 053 | Wrap-up | SC#9, SC#11 |
| Wrap-up | 090 | Merge | All 12 graduation criteria |

---

## Notes on Pipeline Deviations

- **Step 0.5 (master staleness)** skipped during pipeline scaffold: already on feature branch with spec/design committed
- **Step 5 (auto-start)** NOT executed: 38-task TASK-INDEX needs human review before Phase 1 task 001 begins
- **No `<ui-tests>` sections** in any POML: project has zero UI surface
- **No Dataverse MCP schema validation**: project has zero Dataverse entities
- **Deploy/verify tasks reinterpreted**: phase-end "verify" tasks here check GitHub Project state, not Azure deployments
- **Optional Phase 5 FRs (FR-27, FR-28) deferred** per F5 default: revisit only if drift becomes visible

---

*This index is the master tracker. Update task status (🔲 → 🔄 → ✅) as work progresses. `current-task.md` mirrors the active task only.*
