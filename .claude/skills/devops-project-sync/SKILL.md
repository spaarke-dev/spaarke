---
description: Re-read local state (TASK-INDEX, current-task, worktree) and idempotently update the GitHub Project Issue's custom fields. Workhorse skill called by 5 hook tasks. Per FR-12 + NFR-04 + F7 of spaarke-devops-project-tracking-r1.
tags: [devops, project-sync, idempotent, partial-success, gh-cli]
techStack: [gh-cli, graphql, python]
appliesTo: ["/devops-project-sync", "sync portfolio", "update project fields"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-project-sync

> **Category**: DevOps / Portfolio
> **Tier**: Workhorse — called by FR-16, FR-17, FR-20, FR-22, FR-24 hooks. Contract is load-bearing.
> **Last Reviewed**: 2026-06-23

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` has run
- Project has a Project Issue (created earlier via `/devops-project-start` or `/devops-project-register`)
- Local `projects/{name}/README.md` has the `> **Portfolio**:` pointer block (so Issue # is discoverable)

## Purpose

Re-read local state and update the GitHub Project Issue's custom fields. Idempotent per NFR-04 (zero API mutations on second run against unchanged state). Partial-success tolerant per F7 (failures collected and reported, sync continues).

This is the workhorse — most automation hooks call this skill at end-of-host-skill so the portfolio board never drifts more than 3 task steps stale.

## Workflow

### Step 0: Validate inputs / discover Issue

- Read `projects/{name}/README.md` portfolio pointer block to extract Issue #N
- If pointer block missing: STOP and suggest `/devops-project-register`
- If `--dry-run` flag: skip mutations; print proposed changes only

### Step 1: Read local state

- `tasks/*.poml` count → Task Count
- `tasks/TASK-INDEX.md` rows with `✅` → Tasks Completed
- `current-task.md` "Task" field → for in-progress detection
- Worktree existence + last commit date → Status heuristic
- Open PR for branch → also influences Status

### Step 2: Query current Issue field values

```graphql
query {
  node(id: "<project-item-id>") {
    ... on ProjectV2Item {
      fieldValues(first: 30) {
        nodes {
          # extract all 6 portfolio fields' current values
        }
      }
    }
  }
}
```

### Step 3: Compute diff

For each of the 6 portfolio fields:
- If computed value == current value → NO-OP for this field (idempotency contract)
- If computed value != current value → ADD to mutation list

### Step 4: Dry-run mode (if `--dry-run`)

Print diff summary; exit 0. No mutations applied.

### Step 5: Apply mutations (with partial-success)

For each field in the mutation list:

```python
results = []
for field in mutations:
    try:
        execute(updateProjectV2ItemFieldValue, field)
        results.append((field, "ok"))
    except Exception as e:
        results.append((field, f"failed: {e}"))
```

Do NOT abort on any single field's failure. Per F7 + NFR-03.

### Step 6: Report

- Success-only (all fields synced cleanly): single line `✅ Portfolio synced: #N (Task Count=X, Tasks Completed=Y)`
- Partial success: ⚠️ line listing failed-fields + success count
- No-op (no changes needed): single line `✅ Portfolio in sync: #N`

### Step 7: Update local current-task.md "Last portfolio sync" timestamp (optional)

For audit trail. Not required by spec but helps debugging.

## Outputs

- Up to 6 GitHub Project field mutations (or zero if no-op)
- Single confirmation line (per NFR-03 when called by hook; verbose by default when user-invoked)

## Behavior contracts (binding)

| Contract | Enforcement |
|---|---|
| Idempotent (NFR-04) | Second run on unchanged state = zero API mutations. Verified via `--dry-run` reporting zero proposed changes. |
| Partial-success tolerant (F7) | Single field failure does NOT abort sync. Other fields proceed. Failures reported at end. |
| Silent on success when hook-driven (NFR-03) | Single ✅ line; no verbose dump unless `--verbose` flag. |
| Degrade-to-warn on failure (NFR-03) | Hook-side callers MUST NOT abort the host skill on sync failure. |

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| README portfolio pointer block missing | Project never registered | Run `/devops-project-register --from-folder`; or `/devops-project-start --from-issue` |
| Issue closed | Manually closed | Skill warns + skips; user re-opens Issue if intended |
| Rate limit during multi-field update | Many concurrent syncs | NFR-05 backoff per field; idempotency means next sync heals |
| Single field fails repeatedly | Bad field ID or schema drift | Per-field error visible in output; user investigates that one field |
| `current-task.md` malformed | User hand-edited and broke schema | Skill falls back to defaults; logs warning |

## Related Skills

- `/devops-portfolio-setup` — prerequisite
- `/devops-project-register` — first-run analog (creates Issue + populates fields once)
- Hooks (FR-16, FR-17, FR-18, FR-19, FR-20, FR-22, FR-24): call this skill at end of host skill

## Reference

- Spec: FR-12 + NFR-03 + NFR-04 + F7
- Idempotency design pattern: snapshot → diff → mutate only-changed-fields
