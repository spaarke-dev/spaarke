# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (checkpoint at 85% context, during Wave 0-1)
> **Phase**: Phase 0 executing. Wave 0-1 running: sub-agents on 002, 003, 005; main session on 007.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | **Wave 0-1.** Three implementer agents (Sonnet) run task-execute on 002 (Bicep), 003 (CI image workflow) and 005 (cleanup BFF fixes). Each commits only its own files and does not push. 002 was also messaged two consistency additions: Data Sender on `intake-engine-requests` (for retry copies), and `APPLICATIONINSIGHTS_CONNECTION_STRING` taken from the stamp's App Insights, the same source as the BFF |
| **Task 007** | A2-only commit `b3027fd2d0` is on branch `work/adr-004-a2-queue-consumers`, as PR #3191. ArchTests 120/120 pass. **Waiting for the owner's merge go-ahead** (do not merge without it). After the merge: mark 007 ✅ in TASK-INDEX + its POML, and add the PR link to notes/decisions.md |
| **Next action** | When the three agents report: check each JSON result, run `dotnet build src/server/api/Sprk.Bff.Api/` (the 005 .cs changes), update the TASK-INDEX status cells for 002/003/005 and run `pwsh scripts/check-task-status-drift.ps1`. Commit the uncommitted planning edits listed below, push, and report Wave 0-1. Next wave: 0-2 = 004 + 006 (004 also needs #3163) |
| **Uncommitted (main session)** | tasks/001 (Azure SDK wrapper + consistency constraints: BFF DLQ reason codes, PriorDeliveryCount retry copies, message properties, azure-monitor-opentelemetry, sidecar Dockerfile header format), tasks/014 (idempotency key `IntakeEngine:{runId}:{dispatchAttempt}`), tasks/023 (DeadLetterQueueService gains an optional allow-listed queue name, so the engine queue's DLQ is covered) |
| **Unresolved (spec)** | (1) Clean copy with prior markup → task 008 spike with the accelerator; (2) ADR-049 scope → `/adr-check` (090); (3) ontology columns on sprk_servicerequest → before 010 |
| **External gates** | PR #3163 (engines on master) → 001, 004; eBilling UAT (ebilling-r1 task 163) → 050; ontology coordination → 010 |
| **Issues/PRs** | #3191 (A2), #3162, #1920 (task 042), portfolio #3128 |

## Decided 2026-11-25 (owner: "take the correct technical approach"; "ensure consistent with other components")
- Engine wrapper uses hash-pinned azure-identity / azure-servicebus / azure-storage-blob (+ azure-monitor-opentelemetry). The engines stay stdlib + lxml.
- design.md §8.5 has the hot-path declaration (skill-directives = Y) + Placement Justification.
- Engine job matches the BFF conventions: the same DLQ reason codes, retry semantics (PriorDeliveryCount, RetryDelay, MaxDeliveryCount 5), message properties, OTel → App Insights, and the sidecar Dockerfile header format.
- Full log: notes/decisions.md.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message (accelerator session: "acclerator-r1").
- Never put accelerator `answer-key/` or real NDAs where an agent can read them.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Don't copy the accelerator engines; build from `src/accelerators` on master; Python 3.11; the engines use stdlib + lxml, and only the wrapper layer adds hash-pinned Azure SDKs.
- Phase 0 cleanup is one PR with the real fixes (005 + 006); no hygiene-only PRs.
- New tests go under KEEP paths (`tests/CLAUDE.md`).
- Sub-agents in this worktree commit only their own files by path; the main session owns TASK-INDEX.md and current-task.md.
