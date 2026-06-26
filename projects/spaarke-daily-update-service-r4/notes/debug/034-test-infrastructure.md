# Task 034 — npm build status note (pre-existing peer-dep gaps)

> **Captured**: 2026-06-26 during task 034 execution.
> **Scope**: `@spaarke/daily-briefing-components` package build (`tsc --noEmit`).

## Status

- **Jest tests for task 034 (`ActivityNotesSection.fallback.test.tsx`)**: ✅ PASS (5/5).
- **All `ActivityNotesSection`-scoped tests**: ✅ PASS (8/8 — fallback + sub-list).
- **`npm run build`**: 5 errors **— all pre-existing**, identical before and after task 034 changes.

## The 5 pre-existing errors

```
src/components/NarrativeBullet.tsx(36,35):       error TS2307: Cannot find module '@spaarke/ui-components'
src/components/SubRowTodo.tsx(45,35):            error TS2307: Cannot find module '@spaarke/ui-components'
src/hooks/useInlineTodoCreate.ts(51,8):          error TS2307: Cannot find module '@spaarke/ui-components/services'
src/services/briefingService.ts(31,36):          error TS2307: Cannot find module '@spaarke/auth'
src/widgets/dailyBriefing.registration.ts(49,87): error TS2307: Cannot find module '@spaarke/ui-components'
```

All five are sibling-package resolution errors (`@spaarke/ui-components`, `@spaarke/auth`). The repo has no top-level `npm workspaces` config (`package.json` does not declare a `workspaces` array), so unresolved peer dependencies are expected when building one shared package in isolation without first building/installing the siblings.

## Verification

- `git stash` → `npm run build` → 5 errors (same files / lines).
- `git stash pop` → `npm run build` → 5 errors (same files / lines).
- `npm run build 2>&1 | grep ActivityNotesSection` → **0 matches** — task 034's modified file is TypeScript-clean.

## Why this does not block task 034

- The task's load-bearing acceptance signal is "Jest passes for the modified component" (FR-16 AC-16). It does.
- The pre-existing peer-dep gap is the same condition that allowed task 033 (parallel) and prior tasks 029/030 to land — it is a build-infrastructure debt item outside task 034's scope.
- Task POML constraint explicitly permits this path: "If npm test infrastructure is broken (pre-existing peer-deps), document at `notes/debug/034-test-infrastructure.md` and complete the task — build success is load-bearing."

## Suggested follow-up (NOT in scope for task 034)

A separate task should (a) decide whether to introduce a workspace root with `npm workspaces`, or (b) document the per-package install order so `tsc --noEmit` runs clean at the `@spaarke/daily-briefing-components` boundary. This is orthogonal to the FR-16 fallback behavior.
