---
description: Archive a completed or cancelled project — set Status, set Closed Date, capture counts, close Issue, retain folder + .archived marker. Worktree is PRESERVED for separate cleanup (override of D-18/NFR-09 per operator workflow). Per FR-14 of spaarke-devops-project-tracking-r1.
tags: [devops, project-archive, gh-cli, git]
techStack: [gh-cli, graphql, git, python]
appliesTo: ["/devops-project-archive", "archive project", "close project"]
alwaysApply: false
last-reviewed: 2026-10-12
---

# devops-project-archive

> **Category**: DevOps / Portfolio
> **Tier**: Non-destructive — closes Issue state only. Worktree preserved.
> **Last Reviewed**: 2026-10-12 (final AI spend captured at close-out, Step 3); 2026-06-25 (worktree-deletion behavior REMOVED per operator workflow change)

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
5. **Retain** `projects/{name}/` folder + worktree (no destructive operations). **Never rename or move the folder** — no `x-` prefix, no `projects/_archive/`. A rename breaks every link to the project (the Issue's Quick Links, the board's Project Folder field, links from other projects and skills) and makes history tools see a new project on archive day. "Archived" is the `.archived` marker plus board Status, not the folder name. The 37 older `x-` folders keep their names (renaming them back would break the links that now point at them). Enforced by: the pre-commit hook and the `projects-layout-guard` workflow (both run `scripts/quality/check-project-archive-prefix.sh` on added or renamed paths, so the PR that renames is the one that fails), and `tests/Spaarke.ArchTests/ProjectFolderArchivePrefixGuardTests.cs` over the whole tree when Arch Tests run. A `projects/`-only PR counts as docs-only and skips Arch Tests, which is why the first two exist.
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

**Then capture the final AI spend** (added 2026-10-11 — costs are computed from local transcripts, so close-out is the last moment they are complete for this project):

```bash
python scripts/ai-cost/spend-report.py --project {slug} --since {project start date} --archive   # folds every project's days in that range into the durable archive (--project only filters the printed report)
python scripts/ai-cost/update-board-spend.py {slug}                                              # final AI Spend / AI Calls / As Of (never lowers a value)
```

`{project start date}` is `YYYY-MM-DD` (the project README's created date). Over a long range expect one `WARN archive: ...` line per already-pruned day — the archived rows are kept, which is the point. Note the resulting `AI Spend (est.)` and `AI Calls` for the Step 4 Issue comment. Do **not** write cost figures into `projects/{name}/` or any other committed file — the repo keeps costs out of version control (`scripts/ai-cost/README.md`); the board and the Issue are where they live. If the project has no local transcript history on this machine, say so in the comment instead of a number.

### Step 4: Update Issue fields + close

- Set `Status` field to `Completed` or `Cancelled` (per --status)
- **Set `Closed Date` field** (field ID: `PVTF_lAHODW0Pv84BEgWuzhWYfL4`, type DATE):
  - If `--pr-number #M` provided: use the PR's merge date (`gh pr view #M --json mergedAt --jq .mergedAt`, take the date prefix YYYY-MM-DD). This is the truest "actual end".
  - Else: use today's date.
  - Mutation: `updateProjectV2ItemFieldValue` with `value: { date: "YYYY-MM-DD" }`.
- Add comment to Issue: `Archived via /devops-project-archive on {date}. Closing PR: #M. Closed Date set to {merge-or-today}. Final AI spend (est., list price, this machine): ${spend} over {calls} calls. Worktree at {worktree-path} preserved.`
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
| Skill run twice on same project | User unsure if first run completed | Idempotent — second run detects Issue already closed + `.archived` already present; reports no-op. The 37 older `x-` folders got their `.archived` marker on 2026-10-14 without any board change: for those, the marker alone does not mean the board was updated, so check the Issue and Status and run the board steps if they are still open |
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
