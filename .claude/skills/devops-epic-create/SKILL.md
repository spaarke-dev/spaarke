---
description: Create a new Epic Issue on GitHub Project #2 with Type=Epic, label epic, populated description fields. Per FR-07 of spaarke-devops-project-tracking-r1.
tags: [devops, github-issues, epic, portfolio, gh-cli]
techStack: [gh-cli, graphql]
appliesTo: ["/devops-epic-create", "create epic", "new portfolio epic"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-epic-create

> **Category**: DevOps / Portfolio
> **Tier**: Component (user-invocable via `/devops-epic-create`)
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated as project owner
- `/devops-portfolio-setup` has run (label `epic` exists; Type field has `Epic` option)
- Project #2 ID + Type field ID + Epic option ID known (look up via `gh project field-list 2 --owner spaarke-dev`)

## Purpose

Single command to create one Epic Issue, add it to Project #2, set `Type=Epic`, and apply label `epic`. The Issue body uses the `.github/ISSUE_TEMPLATE/epic.yml` field structure (Objectives, Scope, Success Criteria, Projected Timeframe).

Epics group related Spaarke projects under a portfolio rollup. Every Project Issue created in the future will set its `Parent issue` to one of these Epics (per D-12).

## Workflow

### Step 0: Validate inputs

Required flags:
- `--title "<Epic title>"`

Optional flags:
- `--objectives "..."` — 2-4 sentence focus statement
- `--scope "..."` — In-scope / out-of-scope items
- `--success "..."` — Measurable success criteria
- `--timeframe "..."` — Free-text timing (e.g. "H2 2026")

If optional flags omitted, prompt interactively or pass through with placeholder text the user can refine.

### Step 1: Idempotency check

Query existing Epic Issues: `gh issue list --label epic --state open --json number,title`. If an open Issue with the same title exists, prompt:
- `skip` (return existing #N) — default
- `edit` (open Issue body in editor)
- `continue` (create duplicate — discouraged)

### Step 2: Compose body

Render the body using the `epic.yml` template field structure as a markdown document:

```markdown
### Objectives / Focus
{objectives}

### Scope
{scope}

### Success Criteria
{success}

### Projected Timeframe
{timeframe}

### Notes / Context
Created by /devops-epic-create on {date}.
```

### Step 3: Create Issue

```bash
gh issue create \
  --title "[Epic]: ${TITLE}" \
  --body-file <(echo "$BODY") \
  --label epic
```

Capture the resulting Issue URL + number.

### Step 4: Add to Project #2 + set Type=Epic

```bash
# Add to Project
gh project item-add 2 --owner spaarke-dev --url "$ISSUE_URL" --format json
# Capture item_id from response

# Set Type=Epic via GraphQL mutation
gh api graphql -f query='
mutation {
  updateProjectV2ItemFieldValue(input: {
    projectId: "${PROJECT_ID}"
    itemId: "${item_id}"
    fieldId: "${TYPE_FIELD_ID}"
    value: { singleSelectOptionId: "${EPIC_OPTION_ID}" }
  }) { projectV2Item { id } }
}'
```

### Step 5: Report

Print Issue URL + number + "ready to accept Project sub-issues".

## Outputs

- 1 new GitHub Issue
- 1 new Project #2 item with Type=Epic
- Confirmation line: `Created Epic #N: <title> -> <url>`

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Title already exists as open Epic | Re-run with same title | Use `skip` to return existing #N |
| Type=Epic option ID not found | `/devops-portfolio-setup` not run | Run `/devops-portfolio-setup` first |
| `gh project item-add` fails | Token lacks `project` scope | Verify `gh auth status` shows `project` scope |
| Body too long for `gh issue create -b "..."` | Long descriptions | Use `--body-file` with stdin (`<(echo)`) or temp file |

## Related Skills

- `/devops-portfolio-setup` — prerequisite (ensures Type=Epic option + label `epic`)
- `/devops-idea-create` — analogous skill for Idea Issues
- `/devops-project-start` — Projects attach to the Epics this skill creates
