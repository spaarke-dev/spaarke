---
description: Concise portfolio dashboard (active Epics → Projects → rollup) to terminal; optional --snapshot writes stakeholder-readable markdown to docs/portfolio/. Per FR-13 + D-10 of spaarke-devops-project-tracking-r1.
tags: [devops, portfolio-status, dashboard, reporting, gh-cli]
techStack: [gh-cli, graphql, python]
appliesTo: ["/devops-portfolio-status", "portfolio dashboard", "what's running"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-portfolio-status

> **Category**: DevOps / Portfolio
> **Tier**: Component (user-invocable via `/devops-portfolio-status`)
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` has run
- At least 1 Epic Issue with Type=Epic exists on Project #2

## Purpose

Two output modes:

- **Terminal mode (default)**: concise dashboard prints active Epics → their Projects → rollup metrics in <30 seconds. Use for engineering owner's daily check.
- **Snapshot mode (`--snapshot`)**: writes `docs/portfolio/snapshot-{YYYY-MM-DD}.md` — a stakeholder-readable narrative per D-10 (Epic-by-Epic story, NOT raw field dump). Use for sharing with non-implementers.

Both modes satisfy spec Success Criterion 7: "a non-implementer can answer in <30 seconds: 'What Epics are active? How many projects in each? Rough portfolio status?'"

## Workflow

### Step 0: Query Project #2 items

```graphql
query {
  node(id: "<project-#2-id>") {
    ... on ProjectV2 {
      items(first: 100) {
        nodes {
          id
          content { ... on Issue { number title url } }
          fieldValues(first: 30) {
            nodes {
              # Extract: Type, Project Type, Status, Parent issue, Task Count, Tasks Completed
            }
          }
        }
      }
    }
  }
}
```

If > 100 items: implement pagination via `endCursor` (becomes load-bearing as portfolio grows).

### Step 1: Group + roll up

```python
epics = [i for i in items if i.type == "Epic"]
projects = [i for i in items if i.type == "Project"]

epic_to_projects = group_by_parent_issue(projects, epics)

per_epic_rollup = {
    epic.number: {
        "title": epic.title,
        "total_projects": len(epic_to_projects[epic.number]),
        "in_progress": count_status(epic_to_projects[epic.number], "In Progress"),
        "planned": count_status(epic_to_projects[epic.number], "Planned"),
        "on_hold": count_status(epic_to_projects[epic.number], "On Hold"),
        "completed": count_status(epic_to_projects[epic.number], "Completed"),
        "cancelled": count_status(epic_to_projects[epic.number], "Cancelled"),
    } for epic in epics
}
```

### Step 2 (terminal mode): Print concise dashboard

```
Spaarke Portfolio — 2026-06-23

Epic                              | Total | In Prog | Planned | Hold | Done | Cancel
----------------------------------|-------|---------|---------|------|------|-------
AI Platform & Chat            #421 |    8  |    3    |    2    |  1   |  2   |   0
Insights Engine               #422 |    4  |    2    |    1    |  0   |  1   |   0
Smart Todo                    #423 |    3  |    1    |    0    |  0   |  2   |   0
...

Totals: 30 projects (12 in progress, 8 planned, 3 on hold, 7 done, 0 cancelled)
```

### Step 3 (snapshot mode): Write stakeholder markdown

Render Epic-by-Epic narrative (NOT raw field dump per D-10):

```markdown
# Spaarke Portfolio Snapshot — 2026-06-23

This snapshot summarizes active engineering work across Spaarke. Each Epic represents a portfolio theme; projects roll up to one Epic via GitHub's parent-issue field.

## AI Platform & Chat (Epic #421)

**Status**: 3 projects in progress, 2 in planning, 2 complete this cycle.

Active work:
- [Chat routing redesign](url) — 60% complete (12/20 tasks), in progress
- [Capability router](url) — 40% complete (8/20 tasks), in progress
- [Foundry grounding](url) — 20% complete, in progress

In planning:
- [Conversation persistence](url) — design phase
- ...

## Insights Engine (Epic #422)

...
```

Save to `docs/portfolio/snapshot-{YYYY-MM-DD}.md`. Print "Snapshot written to {path}. Commit to share with stakeholders."

### Step 4: Cleanup

If `docs/portfolio/` doesn't exist, create it (no `.gitkeep` needed; first snapshot file is the seed).

## Outputs

- Terminal: concise table (~20 lines)
- `--snapshot`: `docs/portfolio/snapshot-{YYYY-MM-DD}.md` (markdown narrative)

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| > 100 items on Project #2 | Portfolio growth | Implement pagination (`endCursor`) |
| No Epics on Project #2 | `/devops-portfolio-setup` not run | Run `/devops-portfolio-setup` first; create Epics via `/devops-epic-create` |
| Raw field IDs leak into snapshot | Bug — would violate D-10 | Smoke-test: grep snapshot for `PVT*` IDs; should return 0 hits |
| Issue with Type=Project but no Parent issue | Orphan project | Snapshot includes "Unparented Projects" section + flag for remediation |

## Related Skills

- `/devops-portfolio-setup` — prerequisite
- `/devops-project-sync` — keeps field values current (sync first if snapshot is stale)
- `/devops-project-archive` — moves Completed projects out of active rollup

## Reference

- Spec: FR-13 + D-10 (audience: engineering owner + engineers + stakeholders/leadership; NOT customer-facing)
- Success Criterion 7: <30 second answer time UX target
