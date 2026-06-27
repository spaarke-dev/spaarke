# Current Task State — scheduled-jobs-migration

> **Last Updated**: 2026-06-22 (project scaffolded)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | scheduled-jobs-migration — migrate the remaining ~17 Tier-1 `BackgroundService` impls to the R3 `Spaarke.Scheduling` framework |
| **Status** | **Not started** (project scaffolded 2026-06-22; design.md drafted with 14–17 job Tier 1 inventory; spec.md is placeholder) |
| **Active task** | NONE — no tasks generated yet |
| **Next Action** | (1) Owner answers Open Questions OQ-1 through OQ-8 in [`design.md`](design.md); (2) run `/design-to-spec projects/scheduled-jobs-migration/design.md`; (3) run `/task-create projects/scheduled-jobs-migration/` |
| **Branch** | TBD — worktree `work/scheduled-jobs-migration` recommended once tasks exist |
| **Predecessor** | `spaarke-platform-foundations-r3` (delivered the framework + 2 reference consumers; reference task POMLs 023 + 085) |

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. Quick Recovery (above) — < 30 seconds
2. Read [`README.md`](README.md) for project overview + graduation criteria
3. Read [`design.md`](design.md) for problem statement, inventory, migration strategy, and Open Questions
4. Read [`CLAUDE.md`](CLAUDE.md) for AI context (ADRs, patterns, BFF binding)
5. If continuing: confirm with owner that OQ-1 through OQ-8 are answered, then proceed with `/design-to-spec`

---

## Workflow Status

### Initialization checklist

- [x] Project directory created at `projects/scheduled-jobs-migration/`
- [x] `README.md` written (overview + graduation criteria)
- [x] `design.md` written (problem, inventory, strategy, recipe, risks, Open Questions)
- [x] `spec.md` placeholder written (to be populated by `/design-to-spec`)
- [x] `CLAUDE.md` written (AI context)
- [x] `current-task.md` written (this file)
- [x] `tasks/` directory created (empty, with `.gitkeep`)
- [ ] Owner has answered Open Questions OQ-1 through OQ-8 in `design.md`
- [ ] `/design-to-spec` has populated `spec.md`
- [ ] `/task-create` has populated `tasks/`
- [ ] Worktree branch `work/scheduled-jobs-migration` created

### Discovery summary (locked at scaffolding)

- **Total BackgroundService impls found** in `src/server/api/Sprk.Bff.Api/`: 28 hits (1 archived, 27 active)
- **Tier 1 (in scope)**: 14 confirmed + 3 pending Phase 0 investigation = **14–17 jobs**
- **Tier 2 (out of scope per ADR-036)**: 3 confirmed + 3 pending Phase 0 = **3–6 jobs**
- **Tier 3 (not migration candidates)**: 1 job (NullMembershipJunctionUpdaterHost)
- See [`design.md` § Discovery](design.md#discovery-current-backgroundservice-inventory) for the full inventory table

### Estimated effort (pre-Phase-0)

- 14–17 jobs × 2–4h = **30–70h developer time**
- Plus ~1 day Phase 0 audit + ~0.5 day Phase 4 wrap-up
- With waves of 3–5 in parallel: **5–10 days wall-clock**

---

## Decisions Awaiting Owner

| ID | Question | Status |
|---|---|---|
| OQ-1 | Office worker classification (UploadFinalization / ProfileSummary / IndexingWorker — Tier 1 or Tier 2?) | Pending |
| OQ-2 | Scope: all Tier 1 jobs, or Phase 1 quick-wins only? | Pending |
| OQ-3 | Service Bus + schedule hybrid jobs — migrate the scheduled half, or skip entirely? | Pending |
| OQ-4 | Acceptance bar: byte-equivalent behavior OR good-enough + observability? | Pending |
| OQ-5 | EmbeddingMigrationService disposition — migrate, delete, or leave? | Pending |
| OQ-6 | Framework cron expression coverage audit | Pending |
| OQ-7 | Naming convention — `XJob` vs `XScheduledJob` | Pending (default: `XJob` per R3 precedent) |
| OQ-8 | JobId namespacing — flat or namespaced | Pending |

Full text in [`design.md` § Open Questions for Owner](design.md#open-questions-for-owner).

---

*This file is the primary source of truth for active work state. Keep it updated.*
