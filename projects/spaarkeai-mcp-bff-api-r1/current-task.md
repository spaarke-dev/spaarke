# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-13 ~02:55Z (mcp-r1, checkpoint at 85% context)
> **Recovery**: Read "Quick Recovery" first

---

## Quick Recovery (READ THIS FIRST)

**Done today:** 020 ✅, 021 ✅ (board 11/29). 021 commits `6b8f74a346`, `871b94d53e`, fix round `d9169b43b6`/`d372711ffa`/`a0f360e06d`, closure `7f5db98aed`; two adversarial passes + uac-r2 PASS (incl. fix-round re-check). Branch `work/spaarkeai-mcp-bff-api-r1` pushed, clean (tip `1124fbdd60`).

**PR #2757 (#2749 inbound backup poll) — merge held, awaiting owner:** migrated to an ADR-036 `IScheduledJob` (ADR-052 Path C), three adversarial reviews' findings fixed; tip `74e055f141` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-a39ef1abcda4cc091`, branch `fix/2749-inbound-poll-backlog`). Owner said "merge when green". CI: required `Router` + `Client lint` pass, 40 pass; two non-required jobs cancelled at their timeouts twice (re-run once): Markdown Link Validator (times out repo-wide, master too) and Office-scope server tests (api-office) — CI hit the 20-min job limit (master 9m39s, prior commit `d3f36f0322` 10m53s); locally on `74e055f141` with `--blame-hang` 569/569 passed in 12m15s, no hang. Held for the owner's call: merge now, or investigate the CI slowdown first.

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
