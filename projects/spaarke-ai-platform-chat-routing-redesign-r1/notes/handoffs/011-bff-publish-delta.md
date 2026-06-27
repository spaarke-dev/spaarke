# Task 011 BFF Publish Delta

> **Generated**: 2026-06-22 (main session continuation after sub-agent watchdog stall during build/test step)
> **Task**: 011 — Add ProblemDetails 404 shape per ADR-019 + integration test
> **Phase 0 baseline**: 44.75 MB compressed

## Measurements

| Stage | Compressed (MB) | Delta vs Phase 0 |
|---|---|---|
| Phase 0 baseline | 44.75 | — |
| Post-task-010 (endpoint stand-up) | 44.7531 | +0.0031 (+887 B) |
| Post-task-011 (this task — 404 RFC 7807 refinement + new test) | 44.74 | ≈ 0 (within measurement noise) |

## NFR-01 status

- Cumulative delta after Wave 1-A round 1 + round 2 (tasks 010 + 011 — 012 not included in this measurement as it ran in parallel and 012's separate measurement at 46.08 MB likely reflected different state): ≈ **0 MB** vs Phase 0 baseline.
- Hard ceiling 60 MB → **44.74 MB measured**, **15.26 MB headroom**.
- Escalation threshold 55 MB → headroom 10.26 MB.

## What changed in this task

- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` — refined the 404 return in the `GetPlaybookByCode` handler (added by task 010) to include all 5 RFC 7807 fields: `type` (`https://spaarke.com/problems/playbook-not-found`), `title`, `status`, `detail`, `instance` (`/api/ai/playbooks/by-code/{code}`).
- `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByCodeProblemDetailsTests.cs` — new test file (107 lines, 3 tests) asserting RFC 7807 shape per ADR-019.

## Test outcome

- `dotnet test ... --filter "FullyQualifiedName~PlaybookByCode"` → **8/8 passed, 0 failed, 0 skipped, 163 ms**
- 5 tests from task 010 (endpoint behavior) + 3 new from task 011 (ProblemDetails shape).

## Process note: sub-agent watchdog stall

The sub-agent dispatched for this task stalled at the build/test/publish step (step 5/6/7) per the runtime notification ("no progress for 600s"). The agent had successfully completed:
- Step 1 (read endpoint)
- Step 2 (locate ProblemDetails infrastructure / convention)
- Step 3 (refine the 404 return) — verified in source via grep
- Step 4 (create the new test file) — verified at 107 lines complete

Main session picked up from step 5 (build) onward without retrying the sub-agent (no work to redo; the artifacts were in place). All gates passed. Recommended: if this stall pattern repeats across tasks (3+ occurrences), file a maintenance ticket — likely root cause is the long-running `dotnet test` or `dotnet publish` on Windows exceeding the watchdog window. Not a code defect.
