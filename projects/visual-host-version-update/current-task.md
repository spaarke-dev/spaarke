# Current Task

**Active task**: VHVU-002 — Shared-lib packaging hygiene (.tgz + files allow-list + storybook-static)
**Status**: not-started
**Phase**: A0
**Next action**: Begin Step 1 of task 002

### Last completed: VHVU-001 ✅ (2026-07-10)
- Declared `@spaarke/auth` on `@spaarke/ui-components`; extended `ensure-dist-fresh.js` to freshen sibling dists (sdap-client, auth).
- Verified deterministic clean-chain green build; cleanGuid confirmed via `trim().toLowerCase()` grep.
- Modified: `Spaarke.UI.Components/package.json`, `Spaarke.UI.Components/scripts/ensure-dist-fresh.js` (+ lockfile).

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
