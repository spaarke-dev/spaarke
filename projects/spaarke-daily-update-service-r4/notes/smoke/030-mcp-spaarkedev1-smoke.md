# Task 030 — MCP smoke against spaarkedev1 (carry-forward from task 001 AC-3)

> **Date**: 2026-06-26
> **Owner**: task 030
> **Closes**: task 001 AC-3 deferral per `notes/debug/001-mcp-smoke-deferred.md`

## Test

```
mcp__dataverse__read_query
querytext:
SELECT TOP 1 sprk_analysisactionid, sprk_name, sprk_executoractiontype
FROM sprk_analysisaction
WHERE sprk_executoractiontype = 52
```

## Result — successful

```json
[
  {
    "sprk_analysisactionid": "ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6",
    "sprk_name": "Lookup User Membership",
    "sprk_executoractiontype": 52
  }
]
```

## Interpretation

1. **Dataverse MCP connectivity to spaarkedev1 is live** in this worktree's session. Confirms task 001 AC-3 (Dataverse-MCP smoke against spaarkedev1).
2. **SYS-LOOKUP-MEMBERSHIP Action row (ActionType 52)** deployed in task 005 is present in spaarkedev1 — name "Lookup User Membership" matches the planned `sprk_name` from `005-sys-lookup-membership-deploy.md`. Cross-purpose smoke (task 005 closeout confirmation) also succeeds.

## Sign-off

Task 001 AC-3 deferral closed. Task 030 step 6 complete.
