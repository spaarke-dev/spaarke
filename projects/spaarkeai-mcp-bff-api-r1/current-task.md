# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-13 ~02:55Z (mcp-r1, checkpoint at 85% context)
> **Recovery**: Read "Quick Recovery" first

---

## Quick Recovery (READ THIS FIRST)

**Done today:** 020 ✅, 021 ✅ (board 11/29). 021 commits `6b8f74a346`, `871b94d53e`, fix round `d9169b43b6`/`d372711ffa`/`a0f360e06d`, closure `7f5db98aed`; two adversarial passes + uac-r2 PASS (incl. fix-round re-check). Branch `work/spaarkeai-mcp-bff-api-r1` pushed, clean (tip `1124fbdd60`).

**In flight (background agent):** PR #2757 (#2749 inbound backup poll) — owner chose ADR-052 **Path C** 2026-11-13: migrate `InboundPollingBackupService` to an ADR-036 `IScheduledJob` (`AddScheduledJob`, cron every 5 min, re-read the persisted watermark each run, remove from `ExistingTimerServices`, update PR body, comment on #976). Worktree `C:\code_files\spaarke\.claude\worktrees\agent-a39ef1abcda4cc091`, branch `fix/2749-inbound-poll-backlog`, prior tip `1f3eb409ba` (two adversarial reviews' findings already fixed: keyset paging, persisted watermark, failed-cache-read never persists, stall test, no address in Graph error logs). **Next:** when the implementer reports → review the migration diff (one adversarial pass; approach changed) → report to owner for merge go. Merge only on the owner's go.

**Open owner question (021):** amend FR-02 to the plain Bearer challenge instead of RFC 9728 protected-resource metadata (needs an anonymous `/.well-known` route; recommended amend, uac-r2 agrees). Same edit fixes FR-02 / ADR-028 A10 reason-code wording (`sdap.access.deny.caller_unresolved`, not `UserContextRequired`). ADR text is under `.claude/` → main session only.

**Next code tasks: 022/023/024** (tools). Tool shape: `McpToolDefinition` subclass tagged `[McpArea]`, `Create(IServiceProvider)` with `Services = services`, handler = an unqualified static method of the tool class, `[Authorize]` (or `[Authorize(Policy = "SystemAdmin")]`) on the class, explicit ReadOnly + title, entry in the `McpTools` ledger (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`); no `Metadata =`, no `partial`. With `Mcp:Enabled=true` a bad tool stops startup.

**Gotchas:** 020's module and the spike both map `/mcp/legal-operations` → never in one build. Tool-written working copies can be LF → `rm` + `git checkout --` before `check-changed-cs-format.sh`. Never `python3`/`python -` in Bash. The Stop-hook quality gate fails while a background implementer is mid-edit in this worktree — that is in-progress work, not a branch break.

## Task 003 (Phase 0 live tests) — open, waiting on the Copilot outage

| Field | Value |
|-------|-------|
| **Task** | 003 Run and record Phase 0 tests, STANDARD, prescriptive. |
| **State** | `notes/phase0-results.md`: T5 ✅, T0 ✅, T1 Chat ✅, E3 ✅, T11 observed, Size 1 MB. 043 cancelled (T1 gate). Checklist `notes/phase0-test-checklist.md`: batch A runnable; B needs the owner to add connector "Spaarke MCP B" on `/mcp/spike-area-b` (T13); C (Size 3/6 MB, T3, T4, T7, T12, T2, T14) now runnable. |
| **Spike** | `spike/spaarkeai-mcp-phase0` tip `d7369fecf1` (never merged; worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike`). |
| **Next** | When Copilot is back: run batches A–C with the owner + licensed non-admin test user; record results; acclerator-r1 sends WP-3/T1-Word results. |

## Active Lanes
- **Dev BFF live `1.0.0+9411f604bb`** (uac-r2, read-back 2026-11-13 02:49:36Z): master `e9208abc5e` + spike `d7369fecf1`; healthz/dataverse/ping 200, anon POST /mcp/legal-operations 401; uac-r2's 30-min gate running. Rule: every uac-r2 dev deploy includes the spike tip until Phase 0 is done; never this branch.
- **Side PRs merged 2026-11-13:** #2759 (#2756 rate-limit log, `0d676cc66a`), #2763 (#2762 CopilotAuth failure log, `2da422bb9c`, uac-r2 gate PASS). ISS-005/006 Done in `notes/defer-issues.md`.
- Filed today: #2758 (JobSubmissionService sender per job); comments on #2134 (processor logs Subject/From; IP in two logs).

## Open Owner Questions
- FR-02 plain Bearer challenge (above).
- #2745 / #2746 / #2750: fix them too? (asked, no answer)
- #2731 builder + issue wording (uac-r2 chose option A).
- #1717 close? · #2747 site-3 sign-off · #2748 merge go · area connector names · Connect consent question (T5) · one Copilot seat can be freed · PR #2622 review.

## Owed
- Connector secret rotation runbook (052); ADR-028 A10 follow-up note; user-setup docs state the Copilot licence requirement.

## Peer obligations
- **ontology-r1**: ping when PR #2587 merges; owner's answer on area connector names.
- **uac-r2**: orchestrates dev deploys (owner per-SHA go there); reviewer for auth diffs (021 done); task 041 deploy that turns `Mcp:Enabled` on goes through them.
