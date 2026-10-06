---
description: Manually refresh the AI Spend (est.) / AI Calls / AI Spend As Of fields on the portfolio board for one, several, or all projects
tags: [devops, portfolio, cost, gh-cli]
techStack: [gh-cli, python]
appliesTo: ["/project-spend-update", "update project spend", "refresh AI cost", "sync project cost"]
alwaysApply: false
last-reviewed: 2026-10-04
---

# project-spend-update

> **Category**: DevOps / Portfolio
> **Tier**: On-demand utility — deliberately NOT wired into `task-execute` or any other hook.
> **Last Reviewed**: 2026-10-04

## Why this is a separate, manually-triggered skill

An earlier version of this wired the cost refresh into `devops-project-sync` Step 1, which fires automatically at `task-execute` Step 9.6 (once per completed task). That under-covers the actual spend pattern: `unified-access-control-r2`'s heaviest cost came from Workflow-tool-driven batches whose executor agents follow their own embedded instructions rather than the full `task-execute` protocol, so a task-completion-tied refresh would silently miss exactly the sessions most worth tracking. Owner direction (2026-10-04): decouple it, run it on demand, keep it simple (no attempt to distinguish metered-API from subscription/Max-plan usage — reconcile against the actual invoice manually at the portfolio level).

## Prerequisites

- `gh` CLI authenticated with project read/write scope
- Python 3 available on PATH
- The target project(s) already exist as `[Project]:` Type=Project items on the board (run `/devops-project-register` first if not — see `projects/INDEX.md` for the active registry)

## Purpose

Recompute estimated AI spend from local Claude Code transcripts and push it to the board, for whichever projects you name — not automatically, not on a schedule, only when invoked.

## When to Use

| User says | Behavior |
|---|---|
| `/project-spend-update` (no args) | Refresh every `Type=Project` item on the board |
| `/project-spend-update {slug} [{slug} ...]` | Refresh just the named project(s) |
| "update project spend" / "refresh AI cost" / "what's the current spend on {project}" | Same, scoped to what was asked |

Not auto-triggered by anything — including `task-execute`, `devops-project-sync`, or project completion. If you want it current, run it.

## Workflow

### Step 1: Run the script

```bash
python scripts/ai-cost/update-board-spend.py                      # all projects
python scripts/ai-cost/update-board-spend.py unified-access-control-r2   # one project
```

This does, per project:
1. Match the project slug to a local Claude Code transcript folder (`~/.claude/projects/c--code-files-spaarke-wt-{slug}`, with fallback fuzzy matching)
2. If no folder matches → skip (leave existing field values untouched, do not write zero)
3. If a folder matches but has zero logged API calls → skip, same reason
4. Otherwise: rescan **all** transcripts for that folder from scratch (full recompute, not incremental — cheap, since it's local file reads only) and push `AI Spend (est.)`, `AI Calls`, `AI Spend As Of` = today

### Step 2: Report

Print the script's own summary line (`N updated, M skipped (no local data), K failed`) plus the per-project detail lines it already emits. Don't re-narrate what the script already printed — relay it.

### Step 3: Note staleness, don't silently claim freshness

If reporting a dashboard or total that includes this field elsewhere (e.g. via `/devops-portfolio-status`), check each project's `AI Spend As Of` date first. A project not refreshed by this skill since its last heavy work session will under-report — say so rather than presenting the total as current.

## Outputs

- Up to 3 GitHub Project field mutations per project touched
- Terminal summary: updated / skipped / failed counts + per-project cost and call count

## What this does NOT do

- Does not distinguish metered-API spend from subscription/Max-plan usage (by design — see Why above)
- Does not reconcile against the actual Anthropic invoice (that's a manual, portfolio-level check)
- Does not run automatically on any schedule, hook, or task completion
- Does not recover cost history for projects whose local transcript folder no longer exists (old worktrees cleaned up, or work done before this tracking existed) — those stay blank, not zero

## Related Skills

- `/devops-project-sync` — syncs Task Count / Tasks Completed / Status fields (NOT cost — see its SKILL.md for why this was split out)
- `/devops-portfolio-status` — reads the `AI Spend (est.)` field for the dashboard rollup; does not refresh it
- `/devops-project-register` — must have run first for a project to have a board item this skill can update

## Reference

- Script: `scripts/ai-cost/get-project-cost.py` (per-project cost computation) + `scripts/ai-cost/update-board-spend.py` (board iteration + field writes)
- Board field IDs documented inline in `update-board-spend.py` (re-derive via `gh project field-list 2 --owner spaarke-dev --format json` if the board is ever rebuilt)
