# Deploy Convention — master-sync-first (FR-E1 / D-6)

> **Status**: Binding project convention, adopted 2026-07-08 by task 040.
> **Source**: `notes/inbound-from-r7/05-deploy-safety-governance.md` (R7 W12 incident — a concurrent worktree's
> auto-deploy briefly overwrote R7's Daily Briefing widget bundle in spaarkedev1 because two worktrees deployed to
> the same shared BFF + widget environment without cross-team awareness).
> **Rejected alternative**: a reserved-deploy-window flag file (e.g. `deploy/.reserved-{env}-{project}`) was
> considered in note 05 "Additional guardrails" §2 and **explicitly rejected by the operator**. This task introduces
> **no new deploy mechanism** — no flag file, no new GitHub Actions workflow, no deploy-namespace scheme, no new
> script. It is convention (this doc) plus a one-line warning snippet.

## 1. The binding rule

**ALWAYS sync `origin/master` into the worktree BEFORE building and deploying locally**, so a local deploy from this
project's worktree never overwrites in-flight work that another team already merged to master.

This applies to every deploy this project performs to a shared environment (spaarkedev1) — BFF API and Daily
Briefing code page alike.

## 2. Standard deploy sequence

```powershell
# 1. Fetch latest from origin
git fetch origin

# 2. Fast-forward if possible (or rebase if diverged); NEVER force
git merge --ff-only origin/master
# OR if diverged:
git rebase origin/master

# 3. Sanity check what you're actually about to deploy
git log origin/master..HEAD --oneline

# 4. Build with master's changes incorporated
dotnet build src/server/api/Sprk.Bff.Api/
# and/or, for the Daily Briefing code page:
npm run build:prod   # (code-page-deploy convention; see docs/guides — NOT plain `npm run build`)

# 5. Deploy
.\scripts\Deploy-BffApi.ps1
# and/or
.\scripts\Deploy-DailyBriefing.ps1
```

Why this is enough (per note 05):
- If someone else's PR already merged to master, step 2 pulls their changes into the worktree — this project's
  deploy bundle then contains their code + this project's code.
- If step 2 shows divergence (rebase needed), it's a signal to check whether this project's changes conflict with
  the incoming code before proceeding.
- Step 3 makes intent visible ("here's what this deploy adds on top of master").
- Steps 4-5 deploy a bundle that reflects the union of master + local changes, never a bundle that's missing
  someone else's merged work.

## 3. The branch-behind warning snippet (reusable, PowerShell)

A minimal, **non-blocking** check to add near the start of any `scripts/Deploy-*.ps1` script this project touches.
It fetches, counts how many commits the local branch is behind `origin/master`, and emits a single `Write-Warning`
line if behind. **It never blocks, prompts, or aborts** — it is advisory only, so it cannot turn into a second
"reserved window" mechanism by another name.

```powershell
# --- FR-E1 master-sync-first advisory check (non-blocking) ---
git fetch origin --quiet
$behind = git rev-list --count HEAD..origin/master
if ($behind -gt 0) {
    Write-Warning "Branch is $behind commit(s) behind origin/master — sync master before deploying to avoid overwriting in-flight work."
}
# --- end FR-E1 check ---
```

Placement guidance: insert this block once, early in the script — after `$ErrorActionPreference = "Stop"` and any
initial parameter/config setup, before the first build or deploy action. It must run exactly once per invocation
(idempotent) and must not alter the script's existing success-path behavior.

## 4. Disposition for THIS task (040)

As of task 040 (Wave 1, executed before any of this project's deploy tasks have run), **this project has not yet
executed a deploy**. The deploy-touching tasks are 017 (Phase A: BFF + code page → spaarkedev1, via the
`bff-deploy` skill which canonically drives `scripts/Deploy-BffApi.ps1`), 038 (Phase B: BFF → spaarkedev1, same
script), and 024 (Phase D: Daily Briefing code page → spaarkedev1, via the `code-page-deploy` skill; the underlying
Dataverse web-resource update for this project's Daily Briefing bundle is `scripts/Deploy-DailyBriefing.ps1`). None
of 017/024/038 have executed yet, so **no deploy script has been edited by this task**.

Per operator direction, task 040 does not reach ahead and edit `Deploy-BffApi.ps1` / `Deploy-DailyBriefing.ps1` on
their behalf now. Instead:

- **This doc is the source of truth** for the snippet in §3.
- **Tasks 017 and 038** MUST add the §3 snippet to `scripts/Deploy-BffApi.ps1` (if not already present when they
  run) as part of their master-sync-first step, citing this note.
- **Task 024** MUST add the §3 snippet to `scripts/Deploy-DailyBriefing.ps1` (if not already present when it runs)
  as part of its master-sync-first step, citing this note.
- `scripts/Deploy-SpaarkeAi.ps1` is NOT touched by this project's task list (no R5 task deploys the SpaarkeAi
  bundle) — do not add the snippet there under this project.
- No other `Deploy-*.ps1` script should be modified under this convention; the snippet is scoped to scripts this
  project actually deploys through.

## 5. PR-description obligation

Every PR from this project that touches a deploy step (any of tasks 017, 024, 038, or any future deploy-touching
task) MUST state in its description that the master-sync-first convention (this doc, FR-E1 / D-6) was followed —
i.e., that `git fetch origin` + `merge --ff-only` (or `rebase`) against `origin/master` happened before the build
and deploy, and, if the branch-behind warning fired, how it was resolved (synced, or proceeded with stated
rationale).

## 6. Explicitly out of scope

- No reserved-deploy-window flag file (`deploy/.reserved-{env}-{project}` or similar) — rejected by the operator.
- No new GitHub Actions workflow.
- No per-project deploy-namespace scheme (e.g. `spaarkedev1-r5`).
- No new deploy script — the snippet is added to the existing scripts this project already deploys through, per
  the disposition in §4.
