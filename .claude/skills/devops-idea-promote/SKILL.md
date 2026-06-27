---
description: Promote one or more Idea Issues into a Project Issue. Path A flips a single Idea to Type=Project; Path B packages multiple Ideas as sub-issues of a new Project. Per FR-09 + D-12 of spaarke-devops-project-tracking-r1.
tags: [devops, github-issues, promote, idea, project, gh-cli]
techStack: [gh-cli, graphql]
appliesTo: ["/devops-idea-promote", "promote idea", "package ideas", "idea to project"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-idea-promote

> **Category**: DevOps / Portfolio
> **Tier**: Component (user-invocable via `/devops-idea-promote`)
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` has run
- At least one Idea Issue (Type=Idea, label `backlog`) exists for Path A
- One or more Idea Issues exist for Path B
- Parent Epic Issue exists and is known (`--epic <#E>` required)

## Purpose

Promote raw Ideas into formal Project Issues. Two distinct paths per FR-09 + D-12:

- **Path A** (`--to-project --epic #E`): flip an Idea's `Type` from `Idea` to `Project`, set `Parent issue` to Epic #E, populate `Project Type`, swap label `backlog` → `project`. Issue number is preserved.
- **Path B** (`--package #X #Y #Z --epic #E`): create a NEW Project Issue with `Type=Project`, set `Parent issue` to Epic #E, attach Idea Issues #X #Y #Z as **sub-issues of the new Project Issue (kept open per D-20)**.

**Neither path creates a local worktree.** That's `/devops-project-start`'s job.

## What this skill does NOT do

- ❌ Create a local folder
- ❌ Create a git worktree
- ❌ Draft a `design.md`
- ❌ Close Ideas on Path B (per D-20 — Ideas stay open as sub-issues)

## Workflow

### Step 0: Validate flags

Mutex required:
- `--to-project <#N>` (Path A): single Idea Issue number
- `--package <#X> <#Y> ...` (Path B): multiple Idea Issue numbers

Always required:
- `--epic <#E>` (per D-12: every Project must have an Epic parent)

Optional:
- `--project-type <Module|UI|Infrastructure|Cleanup|Data|Process|AI|Mixed>` — prompted if missing

Error if both Path A and Path B flags provided, or if neither.

### Step 1: Validate input Issues

For each Idea Issue referenced:
- Verify it exists (`gh issue view <#N> --json number,state,labels,title`)
- Verify it has Type=Idea on Project #2 (or `--force` flag to bypass)
- Verify it is open (closed Ideas cannot be promoted)
- Verify Epic #E exists and has Type=Epic

If any validation fails, STOP with a clear error message.

### Step 2 (Path A): Flip Idea → Project

```bash
# 1. Set Type=Project field value (use updateProjectV2ItemFieldValue)
# 2. Set Parent issue to Epic #E
# 3. Set Project Type field (from --project-type)
# 4. Swap labels:
gh issue edit <#N> --remove-label backlog --add-label project
```

Capture before-after snapshot for audit log.

### Step 2 (Path B): Package Ideas into new Project

1. Compose new Project Issue body:
   ```markdown
   ### Summary
   {derived from primary Idea title or --title flag}

   ### Source Ideas
   - #X: {title of Idea X}
   - #Y: {title of Idea Y}
   - #Z: {title of Idea Z}

   ### Notes
   Packaged from {N} Ideas via /devops-idea-promote on {date}.
   ```

2. Create the Project Issue:
   ```bash
   gh issue create --title "[Project]: ${title}" --body-file ... --label project
   ```

3. Add to Project #2, set Type=Project, Parent issue=Epic #E, Project Type=<...>.

4. For each Idea #X #Y #Z: set its `Parent issue` field to the new Project Issue. **Do NOT close the Ideas** (per D-20).

5. The new Project Issue's `Sub-issues progress` widget will show count.

### Step 3: Verify

- Path A: `gh issue view <#N> --json labels` shows `project`, no `backlog`
- Path B: new Project Issue exists; `gh api repos/<owner>/<repo>/issues/<new-issue-number>/sub_issues` returns the N Idea numbers

### Step 4: Report + next-step pointer

Path A: `Promoted Idea #N to Project. Next: /devops-project-start --from-issue #N --epic #E`
Path B: `Packaged {N} Ideas into Project #M. Next: /devops-project-start --from-issue #M --epic #E --absorbs #X #Y #Z`

## Outputs

- Path A: 1 Issue mutated (type, parent, labels) — Issue # preserved
- Path B: 1 new Project Issue with N sub-issues + 0 closed Ideas
- Single confirmation line + next-step pointer

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Both `--to-project` and `--package` provided | Mutex violation | Pick one path; re-run |
| `--epic` missing | D-12 enforcement | Provide `--epic <#E>`; re-run |
| Idea already promoted (Type=Project on Path A) | Re-run after success | Idempotent — skill detects and reports "already promoted" |
| Sub-issue API rate-limit on Path B with many Ideas | Many Ideas in one package | Use NFR-05 batch-of-20 + exponential backoff |
| Path B accidentally closes Ideas | Bug — would violate D-20 | This skill explicitly does NOT close Ideas; regression test asserts they stay open |

## Related Skills

- `/devops-portfolio-setup` — prerequisite
- `/devops-idea-create` — creates the Ideas this skill promotes
- `/devops-project-start` — natural next step (creates local worktree from the promoted Project Issue)
- `/devops-epic-create` — creates the parent Epic required by D-12

## Reference

- Spec: FR-09 + D-12 + D-20
- Path B sub-issue behavior tested empirically against Project #2 via `notes/spikes/`
