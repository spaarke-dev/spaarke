# Repo + GitHub Process (Project Work)

This procedure exists to prevent “lost work”, task/index drift, and unclear “what’s locked in”. It applies to any work under `projects/*/`.

## Goals

- Always have a recoverable remote state (no local-only work)
- Keep `master` stable and reviewable (PR-based changes)
- Prevent “task placeholders” / `TASK-INDEX.md` ↔ task file divergence

## Branching Model

- `master`: protected, always green, PR-only
- `work/<project>-<topic>`: active work branch
- `snapshot/<project>-<timestamp>`: safety snapshot branches (optional but recommended before risky automation)

## Commit + Push Cadence (Non-Negotiable)

- Commit small, coherent slices (scripts, task regen, status triage, etc.)
- Push at least:
  - after any automation that rewrites many files (task regen/upgrade)
  - after status triage updates (`TASK-INDEX.md` changes)
  - before switching context/branch

Rule of thumb: if you’d be upset losing it, commit+push it.

## Standard Workflow (Day-to-Day)

1. Create or switch to a work branch
   - Example: `work/ai-di-r1-next`
2. Make changes (pipeline/templates/tasks/code)
3. Run local audits/tests relevant to what changed
   - Tasks: run `projects/<project>/scripts/audit-tasks.ps1 -CheckCompletedCompliance -ShowIds`
4. Commit + push
5. Open PR → `master`
6. Ensure CI is green
7. Merge PR

## “Risky Operation” Workflow (Task Regen / Bulk Rewrite)

Use this when generating/upgrading tasks, renaming many files, or large-scale refactors.

1. Create a snapshot branch from current `master` (or current known-good)
2. Push the snapshot branch immediately
3. Create a work branch from the snapshot
4. Run the bulk operation
5. Run task audit
6. Commit + push
7. PR work branch → `master`

## One-Writer Rule (Avoid Conflicts + Drift)

When a branch is in “task regen / status triage” mode:
- Only one person/agent should write under `projects/<project>/tasks/` at a time.
- Avoid parallel edits to `TASK-INDEX.md` while scripts are regenerating task files.

## CI Enforcement

PRs that touch tasks should run a task audit gate.

- Workflow: `.github/workflows/tasks-audit.yml`
- Script(s): `projects/*/scripts/audit-tasks.ps1`

If the audit fails:
- Fix the tasks/index in the PR branch
- Re-run audit locally
- Push updates until CI passes
