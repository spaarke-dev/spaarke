---
description: THE BLESSED HANDOFF (D-13). Reads a Project Issue and scaffolds folder + worktree + design.md skeleton + writes back fields + portfolio pointer in README. Per FR-10 of spaarke-devops-project-tracking-r1.
tags: [devops, project-start, blessed-handoff, worktree, scaffolding, gh-cli, git]
techStack: [gh-cli, graphql, git, python]
appliesTo: ["/devops-project-start", "start project from issue", "blessed handoff"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-project-start

> **Category**: DevOps / Portfolio
> **Tier**: Component, but **LOAD-BEARING** — the one canonical bridge from a Project Issue to a local worktree (D-13)
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated
- Git installed; current working directory in main repo (not in a worktree)
- `/devops-portfolio-setup` has run; Project #2 schema established
- Project Issue exists with Type=Project + Parent issue Epic set

## Purpose

**The one blessed bridge from a Project Issue to a fully-scaffolded local development environment.** Per D-13 this is the ONLY path that creates a worktree from a portfolio Issue — no parallel paths permitted.

Round-trip:
1. Read Project Issue #N body + fields (slug, summary, etc.)
2. Derive `projects/{slug}-r1/` folder name (per F9: kebab-cased Issue title + `-r1` suffix; auto-bumps `-r2`+ if folder exists)
3. Create the folder
4. Create git worktree at `c:/code_files/spaarke-wt-{slug}-r1` from master with feature branch `work/{slug}-r1`
5. Draft `design.md` skeleton populated from Issue body
6. Write back `Worktree Path` + `Project Folder` field values to the Issue (round-trip)
7. Write portfolio pointer block to the new `README.md`
8. Optionally `--absorbs #X #Y #Z`: include "Source Ideas" section in `design.md` preserving Path B Idea framings
9. Optionally `--open-editor`: launch VS Code (default OFF per D-21)

## Workflow

### Step 0: Validate inputs

Required:
- `--from-issue <#N>`: Project Issue number

Optional:
- `--slug <slug>`: override auto-derived slug
- `--absorbs <#X> <#Y> ...`: include Source Ideas section (used after Path B promotion via `/devops-idea-promote`)
- `--open-editor`: launch VS Code after scaffold (default OFF per D-21)
- `--branch-from <ref>`: base branch for worktree (default: `master`)

Idempotency: if `projects/{slug}-r1/` already exists AND `Worktree Path` field is set on the Issue, report no-op + current state.

### Step 1: Read Project Issue + fields

```bash
gh issue view <#N> --json title,body,labels,number,url
gh project item-list 2 --owner spaarke-dev --format json | jq '... select(item by Issue #)'
```

Extract from Issue:
- Title (used for slug derivation per F9)
- Body fields (Project Type, Parent Epic, Summary, Start Date, Target Date)

Validate: Issue.Type must be `Project`. If `Idea`, suggest `/devops-idea-promote` first. If `Epic`, error.

### Step 2: Derive slug per F9

```python
def derive_slug(title, override=None):
    if override:
        return override
    # Strip "[Project]: " prefix
    base = title.replace("[Project]: ", "").strip()
    # kebab-case
    slug = re.sub(r'[^\w\s-]', '', base).strip().lower()
    slug = re.sub(r'[\s_]+', '-', slug)
    slug = re.sub(r'-+', '-', slug)
    return slug

# Suffix logic
candidate = f"{slug}-r1"
n = 1
while Path(f"projects/{candidate}").exists():
    n += 1
    candidate = f"{slug}-r{n}"
folder_name = candidate  # e.g. "feature-x-r1", "feature-x-r2"
```

### Step 3: Create folder + worktree

```bash
mkdir -p projects/${folder_name}
git worktree add c:/code_files/spaarke-wt-${folder_name} -b work/${folder_name}
```

Verify: `git worktree list` shows new entry.

### Step 4: Draft design.md skeleton

Render from Issue body:

```markdown
# {Project Title}

> **Status**: Draft (created by /devops-project-start from Issue #N)
> **Parent Epic**: #E ({Epic title})
> **Project Type**: {project-type}
> **Portfolio Issue**: #N ({url})

## Summary
{from Issue body}

## Scope
TBD — draft via /design-to-spec next

## Source Ideas (if --absorbs)
- #X: {title} — {first 2 lines of body}
- #Y: ...
- #Z: ...

## Next Step
Run `/design-to-spec projects/{folder_name}` to formalize this skeleton into a complete spec.md.
```

### Step 5: Round-trip Issue field updates

```graphql
mutation {
  updateProjectV2ItemFieldValue(input: {
    projectId: "<id>"
    itemId: "<item-id>"
    fieldId: "<Worktree Path field id>"
    value: { text: "c:/code_files/spaarke-wt-<folder_name>" }
  }) { projectV2Item { id } }
}
```

Same for `Project Folder` field with value `projects/<folder_name>`.

If `Project Status` is `Planned`, change to `In Progress` (since worktree now exists).

### Step 6: Write portfolio pointer block in local README.md

Render:

```markdown
> **Portfolio**: GitHub Issue [#N](url) · Epic: [#E](url) · Project Status: In Progress · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2)

# {Project Title}

(skeleton — will be filled by /project-pipeline / design-to-spec)
```

If `--absorbs`, additionally add a Source Ideas section in the README.

### Step 7: Optional `--open-editor`

If flag set: `code projects/${folder_name}/` to open in VS Code.

### Step 8: Report

```
Project scaffolded:
  Folder:    projects/{folder_name}/
  Worktree:  c:/code_files/spaarke-wt-{folder_name}
  Branch:    work/{folder_name}
  Issue:     #N (Project Status: In Progress; fields populated)

Next step: cd projects/{folder_name} && /design-to-spec
```

## Outputs

- `projects/<folder_name>/` (new folder, README + design.md)
- `c:/code_files/spaarke-wt-<folder_name>/` (new git worktree on branch `work/<folder_name>`)
- Project Issue #N: Worktree Path + Project Folder + Project Status fields populated
- `notes/spikes/project-start-{date}.md` log entry (for replay/debug)

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Issue Type != Project | Idea or Epic referenced | Run `/devops-idea-promote` first; or use the correct Issue # |
| `projects/{slug}-r1/` already exists | Slug collision | Auto-bump to `-r2+` (Step 2) or use `--slug <new>` to override |
| `git worktree add` fails (branch exists) | Re-running on existing worktree | Skill detects + reports no-op + worktree path |
| Field write fails (rate limit) | Many simultaneous starts | Per NFR-05; backoff + retry; reconcile field values on next sync |
| `--absorbs` with non-Idea Issues | Promotion violation | Skill validates Source Ideas have label `backlog` or Type=Idea originally |
| User in worktree when invoking | Skill creates a worktree-in-worktree | Skill detects via `git rev-parse --git-common-dir` and warns + STOP |

## Related Skills

- `/devops-portfolio-setup` — prerequisite
- `/devops-idea-promote` — Path B `--absorbs` source
- `/devops-project-sync` — refreshes fields after this skill runs
- `/design-to-spec` — natural next step (formalize design.md → spec.md)
- `worktree-setup` — lower-level worktree primitive (this skill calls it conceptually)
- `project-pipeline` — full project-scaffold pipeline (runs after this skill)

## Reference

- Spec: FR-10 + D-13 (BLESSED HANDOFF) + D-21 (--open-editor default OFF) + F9 (slug derivation)
- Sub-Agent Write Boundary (root CLAUDE.md §3): this skill writes to `.github/`, `projects/`, AND `c:/code_files/` — main session only
