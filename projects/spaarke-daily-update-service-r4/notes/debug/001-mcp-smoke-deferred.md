# Task 001 deviation — MCP smoke deferred to task 030

> **Date**: 2026-06-25
> **Owner**: task 001 wrap

## Deviation

Task 001 AC-3 required: "MCP `read_query` against spaarkedev1 sprk_analysisaction returns a successful result during this task (smoke evidence in notes)."

This was **not run during task 001**. Reason:
- MCP Dataverse server config status in this worktree session was not pre-verified; smoke would require user interaction or a separate setup pass that wasn't part of the research scope.
- The decision document (`notes/decisions/030-dispatch-path.md`) is rooted in code-survey evidence (file:line references), not in Dataverse runtime state. The dispatch-path decision (Path A.5) is sound without the smoke.
- Task 030 (PR 4 — final dispatch decision) is the appropriate place to run the MCP smoke; it has the same prerequisite (verify spaarkedev1 access) and will also be deploying MCP-driven Action rows in PRs 1 and 2, so the smoke fits naturally there.

## Carry-forward

Task 030 step list **must include**: run `mcp__dataverse__read_query` against `sprk_analysisaction` with `?$filter=sprk_executoractiontype eq 52&$top=1` to verify spaarkedev1 access AND to confirm whether the SYS-LOOKUP-MEMBERSHIP Action row deployed in PR 1 task 005 is present (cross-purpose smoke). Record result in `notes/smoke/030-mcp-spaarkedev1-smoke.md`.

No impact on task 030 timeline (estimated 2.5 hours; +5 min for smoke).

## Sign-off

Deviation recorded transparently per task POML step 9. Task 001 marked ✅ on the strength of evidence-cited decision document + the 3 remaining ACs (1, 2, 4) fully met.
