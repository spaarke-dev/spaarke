# Phase 1 Field IDs — Project #2 Custom Fields

> **Captured**: 2026-06-23 (post task 002)
> **Consumed by**: task 010 (`/devops-portfolio-setup` skill) + all `/devops-*` skills that mutate field values
> **Project #2 ID**: `PVT_kwHODW0Pv84BEgWu`

## Type field (extended in task 001)

- **Type field ID**: `PVTSSF_lAHODW0Pv84BEgWuzg2HOQw`
- Options (7 total, post task 001):

| Option name | Option ID |
|---|---|
| Idea | `2a98fa3c` |
| Epic | `47842682` |
| Story | `d0ee9af3` |
| Task | `82398e01` |
| Bug | `ca2544b4` |
| Spike | `eea0ded6` |
| **Project** | **`2708f496`** |

## Custom fields added in task 002

### Project Type (SINGLE_SELECT)
- **Field ID**: `PVTSSF_lAHODW0Pv84BEgWuzhWPlKQ`
- Options (8 per FR-02 + D-15):

| Option name | Color | Description |
|---|---|---|
| Module | BLUE | Backend/.NET/AI service module |
| UI | PURPLE | UI surface (PCF, Code Page, Add-in) |
| Infrastructure | GRAY | Azure/CI/CD/infra |
| Cleanup | GRAY | Refactor/retirement/hygiene |
| Data | GREEN | Dataverse schema/migration/data |
| Process | YELLOW | Workflow/procedure/governance |
| AI | ORANGE | AI Platform/Playbooks/Foundry |
| Mixed | GRAY | Cross-cutting / multi-category |

> Option IDs are NOT captured here — query at use time via field-list. Option IDs persist as long as no `updateProjectV2Field` mutation runs against this field.

### Worktree Path (TEXT)
- **Field ID**: `PVTF_lAHODW0Pv84BEgWuzhWPlKU`

### Project Folder (TEXT)
- **Field ID**: `PVTF_lAHODW0Pv84BEgWuzhWPlLQ`

### Task Count (NUMBER)
- **Field ID**: `PVTF_lAHODW0Pv84BEgWuzhWPlLU`

### Tasks Completed (NUMBER)
- **Field ID**: `PVTF_lAHODW0Pv84BEgWuzhWPlLY`

### Closed Date (DATE) — added 2026-06-25 as follow-up enhancement

- **Field ID**: `PVTF_lAHODW0Pv84BEgWuzhWYfL4`
- Set by `/devops-project-archive` to the PR merge date (or today if no PR).
- Pairs with the pre-existing `Start Date` (auto-populated by `/devops-project-register` from folder-creation date) and `Target Date` (operator-set at end of `/project-pipeline`) to enable drift tracking: `drift = Closed Date − Target Date`.

### Project Status (SINGLE_SELECT)
- **Field ID**: `PVTSSF_lAHODW0Pv84BEgWuzhWPlLc`
- Options (5 per FR-02 + D-16):

| Option name | Color | Description |
|---|---|---|
| Planned | GRAY | Project Issue exists; no implementation yet |
| In Progress | YELLOW | Active worktree + commits in last 30d |
| On Hold | ORANGE | Worktree exists but stalled |
| Completed | GREEN | All tasks complete + PR merged |
| Cancelled | RED | Abandoned (folds in former `Abandoned` per D-16) |

## Usage in `/devops-*` skills

When a skill needs to set a field value on a Project Issue, the mutation is:

```graphql
mutation {
  updateProjectV2ItemFieldValue(input: {
    projectId: "PVT_kwHODW0Pv84BEgWu"
    itemId: "<item-node-id>"
    fieldId: "<field-id-from-above>"
    value: { singleSelectOptionId: "<option-id>" }  # for SINGLE_SELECT
    # OR: value: { text: "..." }                      # for TEXT
    # OR: value: { number: 42 }                       # for NUMBER
  }) { projectV2Item { id } }
}
```

Option IDs for `Project Type` and `Project Status` must be queried fresh per session — they survive across normal operations but are recreated by `updateProjectV2Field` mutations.

## Pre-existing fields (NOT modified by Phase 1)

All 20 existing fields preserved:
Title, Assignees, Status, Labels, Linked pull requests, Milestone, Repository, Reviewers, Parent issue, Sub-issues progress, Type (extended), Priority, Sprint, Start Date, Target Date, Area, Release, Created, Updated, Closed.
