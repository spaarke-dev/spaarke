---
description: Capture an idea as a GitHub Issue (Type=Idea, label backlog). NO local folder or worktree created. Per FR-08 of spaarke-devops-project-tracking-r1.
tags: [devops, github-issues, idea, capture, portfolio, gh-cli]
techStack: [gh-cli, graphql]
appliesTo: ["/devops-idea-create", "capture idea", "add to backlog"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-idea-create

> **Category**: DevOps / Portfolio
> **Tier**: Component (user-invocable via `/devops-idea-create`)
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` has run (label `backlog` exists; Type field has `Idea` option)

## Purpose

Friction-free idea capture: one summary line → one Issue on the backlog. **DOES NOT create a local folder or worktree.** Promotion to a Project happens later via `/devops-idea-promote`.

This is the cheap-capture surface. Ideas live in the backlog until they're worth scaffolding into a real Project.

## What this skill does NOT do

- ❌ Create a local `projects/{name}/` folder
- ❌ Create a git worktree
- ❌ Draft a `design.md`
- ❌ Set a Parent Epic as required (Epic is optional at capture time)

For all of those, the appropriate downstream skills are `/devops-idea-promote` + `/devops-project-start`.

## Workflow

### Step 0: Validate inputs

Required flag:
- `--summary "<one-line summary>"`

Optional flags:
- `--why-it-matters "..."` — Why the idea is worth pursuing
- `--epic <#N>` — Tentative parent Epic (used only as a hint; not enforced)
- `--originating-source "..."` — Free-text — where the idea came from

If `--summary` missing, error out with usage.

### Step 1: Compose body

Use the `idea.yml` template field structure:

```markdown
### Summary
{summary}

### Why It Matters
{why-it-matters or "(not specified)"}

### Tentative Epic
{epic or "(not assigned)"}

### Originating Source
{originating-source or "(unspecified)"}

### Notes
Created by /devops-idea-create on {date}.
```

### Step 2: Create Issue

```bash
gh issue create \
  --title "[Idea]: ${SUMMARY}" \
  --body-file <(echo "$BODY") \
  --label backlog
```

### Step 3: Add to Project #2 + set Type=Idea

```bash
gh project item-add 2 --owner spaarke-dev --url "$ISSUE_URL" --format json
# Set Type=Idea via updateProjectV2ItemFieldValue with Idea option ID
```

If `--epic <#N>` was provided, ALSO set the `Parent issue` field to that Epic. This is best-effort — Idea is captured even if Parent-issue set fails (degrade to ⚠️ warn).

### Step 4: Verify NO side-effects on filesystem

Confirm: `projects/` was not modified; no new worktree was created. Add this confirmation to the skill's user output.

### Step 5: Report

Print: `Idea captured: #N: <summary> -> <url>`. Append: "When ready to promote, run /devops-idea-promote --to-project --epic #E."

## Outputs

- 1 new GitHub Issue with Type=Idea, label backlog
- Single confirmation line

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Type=Idea option ID not found | `/devops-portfolio-setup` not run | Run `/devops-portfolio-setup` first |
| `--epic <#N>` not found | Wrong Issue number | Issue is still created; Parent issue field set is best-effort |
| Skill accidentally creates a folder | Bug — would violate spec FR-08 | Immediate regression; this skill's contract guarantees ZERO filesystem side-effects |

## Related Skills

- `/devops-portfolio-setup` — prerequisite
- `/devops-idea-promote` — natural next step (when an idea matures into a project)
- `/devops-epic-create` — for capturing Epics (not Ideas)

## Reference

- Spec: `projects/spaarke-devops-project-tracking-r1/spec.md` FR-08
- D-12 (every Project must have an Epic): does NOT apply to Ideas — Ideas can float without an Epic until promotion
