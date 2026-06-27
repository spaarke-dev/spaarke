---
description: Inverse of /devops-project-start. For an existing local worktree/folder without a Project Issue, create the Project Issue and populate fields from local state. Used in active-project backfill. Per FR-11 of spaarke-devops-project-tracking-r1.
tags: [devops, project-register, backfill, gh-cli, git]
techStack: [gh-cli, graphql, git, python]
appliesTo: ["/devops-project-register", "register existing project", "backfill project on portfolio"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-project-register

> **Category**: DevOps / Portfolio
> **Tier**: Component (user-invocable via `/devops-project-register`)
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` has run
- A local `projects/{name}/` folder exists (and ideally a worktree at `c:/code_files/spaarke-wt-{name}`)
- Parent Epic Issue exists (`--epic <#E>` REQUIRED per D-12)

## Purpose

Inverse direction of `/devops-project-start`. For an *existing* worktree/folder without a Project Issue, create the Project Issue and populate fields from local state (Worktree Path, Project Folder, Task Count, Tasks Completed, Project Status).

Phase 3 backfill of `spaarke-devops-project-tracking-r1` calls this skill for each active worktree (~20–30 projects).

## Workflow

### Step 0: Validate inputs

Required:
- `--from-folder <projects/{name}>`: folder to register
- `--epic <#E>`: Parent Epic (per D-12)

Optional:
- `--project-type <Module|UI|Infrastructure|Cleanup|Data|Process|AI|Mixed>` — prompted if missing
- `--dry-run`: print proposed Issue + field mutations without applying

### Step 1: Read local state

- `projects/{name}/README.md` (project title, summary)
- `projects/{name}/spec.md` (if present)
- `projects/{name}/tasks/TASK-INDEX.md` (compute Task Count + Tasks Completed)
- Worktree state via `git worktree list` (path + branch)
- Last commit date: `git log -1 --format=%cd --date=iso work/{name}` if the branch exists

### Step 2: Idempotency check

Query: is there already a Project Issue for this folder? Check via:
- `Worktree Path` field text match
- `Project Folder` field text match
- Open Issue body grep for the folder path

If found, report no-op + return Issue URL.

### Step 3: Compute Project Status heuristic

```
IF tasks_completed == task_count AND task_count > 0:
  status = "Completed candidate"
ELIF open PR exists referencing branch:
  status = "In Progress"
ELIF last commit < 30 days ago AND active_task_in_current_task_md:
  status = "In Progress"
ELIF last commit < 30 days ago:
  status = "In Progress"
ELIF worktree exists but no commits in 30 days:
  status = "On Hold"
ELSE:
  status = "Planned"
```

This matches the F6 active/in-flight definition + plays well with `/devops-project-sync` later.

### Step 4: Compose Project Issue body

Use `project.yml` template field structure:

```markdown
### Project Folder Slug
{slug}

### Worktree Slug
{slug}

### Proposed Project Type
{project-type}

### Parent Epic Reference
#{epic}

### Project Summary
{first 2-3 lines from spec.md or README.md if available}

### Projected Start Date
(unknown — backfilled from existing worktree)

### Projected Target Date
(unknown — backfilled from existing worktree)

### Notes / Context
Registered by /devops-project-register on {date}. Source: existing local worktree at {worktree_path}.
```

### Step 5: Create Issue + add to Project + populate fields

```bash
gh issue create --title "[Project]: ${slug}" --body-file ... --label project
# Capture Issue # and URL
gh project item-add 2 --owner spaarke-dev --url <url> --format json
# Capture item_id

# Set all 6 new fields via updateProjectV2ItemFieldValue mutations:
# - Type = Project
# - Project Type = <project-type>
# - Worktree Path = <worktree_path>
# - Project Folder = projects/<name>
# - Task Count = <count>
# - Tasks Completed = <completed>
# - Project Status = <heuristic from Step 3>

# Set Parent issue = Epic #E
```

### Step 6: Write/update README pointer block

If `projects/{name}/README.md` lacks the `> **Portfolio**:` pointer block, insert it at top.

If the block exists but Issue # is wrong (e.g., a prior failed register), update it.

### Step 7: Report

```
Project registered: #N at <url>
  Worktree Path: <path>
  Task Count: <count>
  Tasks Completed: <completed>
  Project Status: <status>
  Parent Epic: #E
```

## Outputs

- 1 new GitHub Issue with all 6 portfolio fields populated
- Local `README.md` portfolio pointer block (created or updated)
- Confirmation line

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Folder doesn't exist | Wrong path | Provide correct `--from-folder` |
| `--epic <#E>` missing | D-12 enforcement | Provide `--epic`; re-run |
| Folder already registered | Re-run after success | Idempotent — reports current state, returns Issue URL |
| Cannot compute Project Status | Worktree missing, no PR, no commits | Falls back to `Planned`; user can manually adjust |
| `spec.md` absent | Brand-new folder | Skill registers with placeholder summary; user runs `/design-to-spec` later |
| Task Count != ls *.poml | Stale POMLs in tasks/ | Skill counts files; user can re-sync via `/devops-project-sync` |

## Related Skills

- `/devops-portfolio-setup` — prerequisite
- `/devops-project-start` — opposite direction (Issue → worktree)
- `/devops-project-sync` — keeps fields current after this skill runs
- `/repo-cleanup` — may invoke this skill for orphan-worktree remediation

## Reference

- Spec: FR-11 + F4 (Phase 3 ordering)
- Sub-Agent Write Boundary: writes to `projects/<name>/README.md` (project subfolder, not `.claude/`) — sub-agent OK in principle, but typically run from main session
