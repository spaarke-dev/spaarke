# Current Task

**Active task**: VHVU-010 — Add shared `bootstrapWizardPage()` factory + adopt in Event page (Phase A1)
**Status**: not-started
**Phase**: A1
**Next action**: Begin task 010 (A0 complete; VHVU-004 is optional/owner-gated, off critical path)

### Completed A0 (2026-07-10)
- **VHVU-001 ✅** — declared `@spaarke/auth` on ui-components; extended `ensure-dist-fresh.js` for sibling dists. Deterministic green build.
- **VHVU-002 ✅** — removed 2 `.tgz` artifacts + `files:["dist"]` allow-list (npm-pack validated); removed committed `storybook-static/` (92 files) + gitignored.
- **VHVU-003 ✅** — bumped v1.4.35 (5 locations); build green; v1.4.35 + `trim().toLowerCase()` both confirmed in bundle.
- **VHVU-004 ⏸ optional** — dev deploy decoupled (owner call); 010 now depends on 003.
- Merged origin/master (0 behind); ADR-044 folded in.

## Progress
- [x] design.md, spec.md authored + reviewed
- [x] A0 groundwork committed (ui-components repointed to directory dep; `VisualHostRoot.tsx:505` implicit-any fixed) — commit `1c319c66e`
- [x] Green build recipe confirmed (cleanGuid in bundle) — see spec FR-01/FR-02
- [x] Project pipeline: plan/README/CLAUDE/tasks generated
- [ ] Task 001 onward

## Notes
- Worktree 3 commits behind origin/master (sync when convenient).
- Coordinate shared-surface edits with PR #508 (Events/SmartTodo package boundary).
- A0 is independently shippable — deploy to dev + UAT before Phase A1.
