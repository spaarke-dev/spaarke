# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-27 (036 done: #3181 fix + task write tools; master merged at 60933c82be; #3317 filed)
> **Phase**: Phase 0 code complete except 012 live check. Phase 1 code complete. Phase 2: 030, 031, 033, 034, 035, 037, 038, 036 done; 032 escalated.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 2. Sub-agents run in isolated worktrees (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`). The main session merges each branch (never rebase), runs the verifiers (opus adversarial passes: two for auth/security tasks, one otherwise; a scoped re-check of the fix diff), applies `.claude/` edits and files issues. At most 4 sub-agents at once. |
| **State** | Done: 005, 008, 030, 033, 037, 038 (board 26 of 38). **037** (owner D-037-results option 1a + D-037-engine option B): the Copilot start creates the caller's sprk_analysis (`IAnalysisStartAi`, the create core moved out of AnalysisEndpoints); the engine forwards a server-set `PlaybookRunRequest.AnalysisId` and saves Deliver Output through `AnalysisResultPersistence.SaveNodeRunAsync` (the engine edit is owner-approved; it also fixes #3285 for these runs); the run's regarding columns link to the analysis; the app-only `spaarke_get_playbook_run_results` reads it as the caller; widget `ui://spaarke/playbook-run.html`. Parts a/b/c, two opus passes, one fix round, a Sonnet re-check, and two K fixes by the main session. CAPABILITY-INDEX: rows 47 and 50 edited, rows 54-56 added. Filed #3302, #3303. **037d** (D-037-cleanup, owner "Yes delete"): every non-Started exit after the analysis create abandons the run, then deletes that analysis as the caller (IDataverseUserClient, 10 s own budget; a failed delete is logged, answer unchanged). One opus pass: no F; K1 flaky test fixed, K2/K3 documented (037-decisions §9). WriterOnlyTablesDeleteGuardTests allowlist entry added. PR #3259 client lint: one new no-unnecessary-condition in DailyBriefingApp (task 010) fixed in b3a18c79a5. **mcp-r1 #3242** (Origin/Host filter) approved from r2 for the LogOnly merge; r1 does K1/K4/K6/K7 in a follow-up before Enforce. Publish zip 41.62 MB (−0.07 MB vs master e140b01761). File-probe spike: **`spike/spe-file-probe-on-specialist` 9e8f0ca8e0** (on dev's SHA d307d67e36). Worktree C:\tmp\wtprobe (remove with Remove-WorktreeSafe.ps1 after the test). Draft PR #3259 (consumer typecheck CI, not for merge). |
| **Next (exact)** | (1) uac-r2 approved 036 (no F). Close #3181 when the branch reaches master ("Closes #3181" in the merge PR). (1b) #3317 is uac-r2's (it takes the D-3181 extension to the owner); no r2 action. (1c) ADR-010 ratchet: branch ceiling 174 (IPlaybookRunAi, IEventsCapability documented; master is fine at 171/172). IEventsCapability seam decided at 090 (#3320). (2) Owner: deploy go for file-probe 9e8f0ca8e0 through uac-r2 (tell uac-r2 it layers on d307d67e36). (3) Still open: E1, NFR-05 wording, 032 option, the process-guard card. (4) Read the PR #3259 typecheck result. (5) Remaining runnable Phase 2/3 work after owner answers: 032, then 039 deploy. |
| **Uncommitted** | None. |
| **Branch state** | Master merged through 60933c82be (includes #3242 7f48825d90). Known red: EventStatusDeprecationTests (#3246, master). 036 targeted suites 1,356 pass; ArchTests 2,898 pass. 037 affected filters: 2,068 .NET + 119 widget jest at the fix round; 157 after the main session's K fixes; ArchTests 2,886 at the fix round. |

## Status (TASK-INDEX is authoritative)
- ✅ **Phase 0:** 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011. 012: the registry PATCH is applied; the live check is still owed.
- ✅ **Phase 1 code:** 020, 021, 022, 023, 024, 025, 026, 027. 029 (deploy) waits on 019 and the owner's go.
- ✅ **Phase 2 done:** 030, 031, 033, 034, 035, 037, 038.
- ✅ 036 (two opus passes, F1 fixed: a faulted create is remembered as outcome unknown). 🔲 **Blocked on owner:** 032 (option). 040 waits on 039.

## Open owner questions
Answered 2026-11-26: see notes/decisions.md D-owner-answers-2026-11-26 (1, 2, 3, 6, 7, 8, 9, 11, 12, 13, 15, 16, 17, 18 and the amendment sign-off). Still open:
1. **019 / runbook E1:** not done. The owner does E1 (both "Spaarke AI" Registry rows: uninstall, then delete), then gives a go for the SHA through uac-r2. #3186 is a code follow-up.
2. **NFR-05 and oid in logs:** recommendation: an oid is an opaque id, not a claim. Narrow NFR-05 to "no tokens, names, emails, UPNs, roles or other claim values; oid/tid allowed as audit ids".
3. **032 matter summary (escalation, notes/032-decisions.md):** the matter-summary Binding is not a playbook (SUM-CHAT@v1, chat-session dispatch), so 030's run path cannot start it. Option 2 (recommended now): read-only v1, which needs an FR-10 amendment to drop "starts a run". Option 1 (later): author a matter-subject JPS playbook. Option 3 rejected.
4. **Stray-process prevention:** a watchdog plus a Bash pre-check (proposal sent to the owner).

## Issues filed this wave
#3317 #3302 #3303 #3285 #3268 #3281 #3234 #3235 #3205 #3206 #3207 #3208 #3209 #3212 #3215 #3218 #3219 #3220 #3221 #3224 #3226 #3227 #3228 #3229 #3230 (plus earlier: #3173 #3178 #3179 #3181 #3182 #3185 #3186 #3196 #3198).

## Peer coordination
- **mcp-r1 (2026-11-26):** r1 builds ISS-007 #2835 (Origin/Host filter on /mcp/legal-operations, own file + one wiring line, from master) and DEF-001 #2832 (closedToday on spaarke_show_decisions); r2 reviews both and is messaged before each PR opens and merges.
- **mcp-r1:** #3199 merged and brought into this branch. #2575 (r1 ISS-001, the legacy Copilot agent code) was handed to us 2026-11-26: task 001 (8a92da8751) removes it, so the merge PR body must say "Closes #2575". r1 owns the ISS-010 files and the Decision wizard heading files; message r1 before editing them.
- **specialist-legal-agents-r1:**
  - Run record decided. One shared writer (`PlaybookRunRecordStore`) is agreed; their Phase 1 adds the profile-version and gate columns to it and may later need a "required" write mode.
  - Docs PR #3213 is checked and matches 005.
  - When #3213 merges, fold our CAPABILITY-INDEX row 35 into theirs and renumber ours from 36.

## Decided
- D-r1-gate.
- D-3268-user-focus (2026-11-26): the owner approved one prompt-builder edit (ADR-039 Path C); see notes/decisions.md.
- D-030-adr039 (2026-11-26): 030 passes the ADR-039 check as Path C; see notes/decisions.md.
- D-run-record-decided, which replaces D-run-record-hold.
- D-024-extract.
- Task 027 added.
- D-023-preview, D-023-names and the neutral empty-list text.
- Earlier owner decisions: see notes/decisions.md.

## Standing directives
- Never `git stash`. Never `python`/`python3` in Bash. Every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message.
- Dev BFF deploys go through uac-r2 with the owner's go per SHA. Tenant and admin actions are the owner's.
- Commits: run `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"` first; end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. `--no-verify` only on merge commits.
- `[blocked]` in TASK-INDEX means blocked-shipped; owner-gated work stays 🔄 `[wip]`.
- Wave agents don't edit `.claude/`, TASK-INDEX or current-task.md, and don't launch reviewers.
- New tools add their rows to the two tool-set pins:
  - `IsExactlyThePinnedTools` in McpHostContractTests;
  - `TheStaticSchemaTools` in McpCallerPathTests (strict host order).
