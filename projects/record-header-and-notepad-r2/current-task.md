# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-21 (worktree scaffolded; design re-scoped)
> **Recovery**: Read "Quick Recovery" first, then [`CLAUDE.md`](CLAUDE.md) — especially its "Read this before anything else" section.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — project not started |
| **Phase** | 0 — design complete, pre-spec |
| **Status** | not started |
| **Next Action** | Run the discovery pass in [`notes/discovery-checklist.md`](notes/discovery-checklist.md), fold results into `design.md` §9, then `/design-to-spec` |
| **Blocked by** | Nothing. Discovery can start immediately. |

### What just happened (2026-08-21)

The R2 design was **re-scoped** from "four cloned per-entity PCFs" to "ONE configuration-driven `Spaarke.Records.RecordHeader` control." Owner decisions in the same session:

- Project + Work Assignment ship first; **Invoice is explicitly required** (it forces the currency + date renderer work)
- Config = **JSON on a manifest property**, not a Dataverse config table — few instances ever, unlike VisualHost
- DEF-06 (`exports` migration) and DEF-08 (memo-repo promotion) both **dropped** from R2 scope

### Files changed on this branch (uncommitted at scaffold time → committed in the setup commit)

- `projects/record-header-and-notepad-r2/design.md` — full rewrite
- `.claude/patterns/ui/record-header-composition.md` — corrected; it previously instructed agents to author a new per-entity PCF
- `projects/record-header-and-notepad-r2/README.md`, `CLAUDE.md`, `current-task.md`, `notes/discovery-checklist.md` — new

### Not yet generated

`spec.md` (`/design-to-spec`) · `plan.md` (`/project-pipeline`) · `tasks/TASK-INDEX.md` (`task-create`)

---

## Decisions Log

| Date | Decision |
|------|----------|
| 2026-08-21 | One configurable control replaces four cloned PCFs |
| 2026-08-21 | JSON-on-manifest config; `sprk_headerconfiguration` table explicitly rejected |
| 2026-08-21 | Rollout: Project + Work Assignment → Invoice + Event → Matter last |
| 2026-08-21 | DEF-06 + DEF-08 dropped from R2 |
| 2026-08-21 | New control identity `Spaarke.Records.RecordHeader`; Matter form re-bound once |

---

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| OQ-1 | Do the four entities' field lists in design.md §9 match real schema? All are `TBD-CONFIRM`. | discovery | before `/design-to-spec` |
| OQ-2 | Does a populated summary field exist per entity? R1 shipped a silently-empty sparkle for weeks on Matter — assume nothing. | discovery | before `/design-to-spec` |
| OQ-3 | Are `sprk_mattertype_ref`'s primary id/name attributes `sprk_mattertype_refid` / `sprk_mattertypename`? Determines whether `LOOKUP_META` fully disappears or an escape hatch is needed. | discovery | before `/design-to-spec` |
| OQ-4 | Register on Project #2 portfolio board? | owner | before `/project-pipeline` |

---

*Reset this file at each task transition per root CLAUDE.md §7. History lives in `tasks/TASK-INDEX.md`.*
