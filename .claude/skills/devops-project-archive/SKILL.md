---
description: Archive a completed or cancelled project — set Status, set Closed Date, capture counts, close Issue, retain folder + .archived marker. Worktree is PRESERVED for separate cleanup (override of D-18/NFR-09 per operator workflow). Per FR-14 of spaarke-devops-project-tracking-r1.
tags: [devops, project-archive, gh-cli, git]
techStack: [gh-cli, graphql, git, python]
appliesTo: ["/devops-project-archive", "archive project", "close project"]
alwaysApply: false
last-reviewed: 2026-06-25
---

# devops-project-archive

> **Category**: DevOps / Portfolio
> **Tier**: Non-destructive — closes Issue state only. Worktree preserved.
> **Last Reviewed**: 2026-06-25 (worktree-deletion behavior REMOVED per operator workflow change)

## What this skill does (and doesn't)

**Does**: closes out the Project Issue cleanly — sets Status, captures Closed Date, captures final task counts, closes the Issue, writes a local `.archived` marker file.

**Does NOT**: delete the worktree. Spaarke operators **keep worktrees long after project archive** as long-lived dev environments. Worktree cleanup is a separate workflow (manual `git worktree remove`, or a dedicated cleanup skill if/when one exists).

**Spec deviation note**: This overrides the original D-18 / NFR-09 in `spec.md` which called for worktree deletion. The override is operator-driven (decision: 2026-06-25 walkthrough feedback).

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` + `/devops-project-sync` available
- A registered Project Issue exists for the target project
- All POML tasks complete (for `--status Completed`) OR explicit user decision to cancel (for `--status Cancelled`)

(No worktree-state prerequisites — the worktree is preserved untouched.)

## Purpose

Close out a project cleanly:
1. Set `Status` field (Completed or Cancelled)
2. Set `Closed Date` field (from PR merge date if provided, else today)
3. Capture final `Task Count` / `Tasks Completed` values (via `/devops-project-sync`)
4. Close the GitHub Issue with appropriate state (`Status=Done` or label `cancelled`)
5. **Retain** `projects/{name}/` folder + worktree (no destructive operations)
6. Write `.archived` marker file with archive date + final status + closing PR # (if any) + note that worktree is intentionally preserved

Remote branch is preserved — branch history stays on GitHub for audit.

## Workflow

### Step 0: Validate inputs

Required:
- `--status <Completed|Cancelled>`

Optional:
- `--pr-number <#M>` — closing PR # (auto-detected via `gh pr list --head <branch>` if not provided)
- `--folder <projects/{name}>` — defaults to current folder

(No `--force` flag — the skill is non-destructive, so there's nothing to force past.)

### Step 1: Discover Project Issue

- Read `projects/{name}/README.md` portfolio pointer block → Issue #N
- Note the worktree path (for inclusion in the `.archived` marker) — not modified.

### Step 2: Confirm prompt (light)

```
About to ARCHIVE project (close-out only — worktree preserved):
  Folder:    projects/{name}/        (kept)
  Worktree:  c:/code_files/spaarke-wt-{slug}  (kept — clean up separately if/when desired)
  Issue:     #N                       (will be closed)
  Status:    {Completed|Cancelled}
  PR:        #M                       (closing PR reference)

Proceed? [Y/n]
```

Default: Y (non-destructive). Operator can still N to cancel.

### Step 3: Final field sync

Invoke `/devops-project-sync` to capture latest field values (Task Count, Tasks Completed) BEFORE we close the Issue.

### Step 4: Update Issue fields + close

- Set `Status` field to `Completed` or `Cancelled` (per --status)
- **Set `Closed Date` field** (field ID: `PVTF_lAHODW0Pv84BEgWuzhWYfL4`, type DATE):
  - If `--pr-number #M` provided: use the PR's merge date (`gh pr view #M --json mergedAt --jq .mergedAt`, take the date prefix YYYY-MM-DD). This is the truest "actual end".
  - Else: use today's date.
  - Mutation: `updateProjectV2ItemFieldValue` with `value: { date: "YYYY-MM-DD" }`.
- Add comment to Issue: `Archived via /devops-project-archive on {date}. Closing PR: #M. Closed Date set to {merge-or-today}. Worktree at {worktree-path} preserved.`
- For Completed: `gh project item-edit ... --status Done` (or analogous mutation)
- For Cancelled: add label `cancelled`
- Close Issue: `gh issue close <#N> --comment "..."`

**Drift calculation note**: After this Step, both `Target Date` (set at project setup) and `Closed Date` (set here) are populated. A Project #2 view can compute drift = `Closed Date − Target Date` (positive = late, negative = early). Project status reports + `scripts/portfolio-status.py --show-drift` can surface this.

### Step 5: Write `.archived` marker (folder + worktree both preserved)

Write `projects/{name}/.archived`:

```
Archived: {YYYY-MM-DD}
Final status: {Completed|Cancelled}
Closing PR: #M (https://github.com/spaarke-dev/spaarke/pull/M)
Worktree: c:/code_files/spaarke-wt-{slug} (PRESERVED — not removed by this skill; clean up separately if desired)
Remote branch: work/{slug} (still on origin)
Issue: #N (closed)
```

The folder + `.archived` mark this project as closed. The worktree remains usable — operator can revisit, re-run tools, or clean up at their own cadence (via a separate workflow, not this skill).

### Step 6: Report

```
Archived project: projects/{name}/
  Issue #N closed (Status=Completed, Closed Date=YYYY-MM-DD)
  Folder retained with .archived marker
  Worktree c:/code_files/spaarke-wt-{slug} PRESERVED (clean up separately if desired)
  Remote branch work/{slug} preserved on origin
```

## Outputs

- `projects/{name}/.archived` marker file (worktree path noted as preserved)
- Issue #N closed with Status=Completed or Cancelled + Closed Date set
- Folder + worktree both untouched on local filesystem
- 1 line confirmation

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Issue close fails (rate limit) | Concurrent operations | Re-run skill — idempotency ensures state is healed |
| `.archived` marker write fails | Permission issue | Skill warns; user manually creates the marker |
| Skill run twice on same project | User unsure if first run completed | Idempotent — second run detects Issue already closed + `.archived` already present; reports no-op |
| Project has no portfolio pointer block | Project never registered | Run `/devops-project-register --from-folder` first |
| `--pr-number` references a non-merged PR | Wrong PR number provided | Skill warns; uses today's date as Closed Date fallback |

## Related Skills

- `/devops-project-sync` — runs before Issue close (capture final counts)
- `/devops-portfolio-status` — Completed/Cancelled projects move out of active rollup
- `/merge-to-master` — typically runs BEFORE this skill (project merged, then archived)
- `/repo-cleanup` — may prompt this skill for archive candidates (per FR-23)

## Reference

- Spec: FR-14 (close-out semantics) + F3 (explicit gate, NOT auto-archive on merge)
- **Spec override (2026-06-25)**: D-18 and NFR-09 in `spec.md` originally called for worktree deletion on archive. This skill no longer deletes the worktree — operator workflow keeps worktrees as long-lived dev environments. If a future use case requires worktree removal, build a separate `/devops-worktree-cleanup` skill rather than re-coupling here.
- Idempotency: re-running on archived project is no-op
- Sub-Agent Write Boundary: writes to `projects/<name>/.archived` — main session only (touches local filesystem)
