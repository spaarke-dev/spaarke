# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-26 (033 and 038 done; #3268 merged; 037 escalated after its spike)
> **Phase**: Phase 0 code complete except 012 live check. Phase 1 code complete. Phase 2: 030, 031, 033, 034, 035, 038 done; 037 escalated (owner option); 032 escalated; 036 waits on #3181.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 2. Sub-agents run in isolated worktrees (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`). The main session merges each branch (never rebase), runs the verifiers (opus adversarial passes: two for auth/security tasks, one otherwise; a scoped re-check of the fix diff), applies `.claude/` edits and files issues. At most 4 sub-agents at once. |
| **State** | Done: 005, 008, 030, 033, 038 (board 25 of 38). **033:** review fixes merged (c696b857ca → 5f7adf0290), CAPABILITY-INDEX row 53. **038 + #3268:** the owner approved fix (a). UserContext now reaches the prompt as a fenced "User focus" block (escaped, 2,000-character cap in `PromptContextHelper.BuildUserFocusBlock`, absent means byte-identical). Merged at cd12bb4c2f after one opus pass (no F in the code; one F2 test deleted; K1/K2/K4 hardened). The builders that still ignore the focus are filed as #3281. **037 running** (implementer, opus/high). Step 1 is a spike that STOPS with options if outputs are not caller-readable by run id. File-probe spike: **`spike/spe-file-probe-on-specialist` 9e8f0ca8e0** (on dev's SHA d307d67e36); the master-based `spike/spe-file-probe` e05c209fda is unused. Worktree C:\tmp\wtprobe (remove with Remove-WorktreeSafe.ps1 after the test). Draft PR #3259 (consumer typecheck CI, not for merge). |
| **Next (exact)** | (1) 037: the owner chose option 1a on 2026-11-27 (D-037-results). The implementer stopped before code (037-decisions §3): draining the wrapper loses the run (G1 legacy playbooks get no run, G2 run id, G3 parameters, G4 counts). The owner approved option B (D-037-engine, 2026-11-27): AnalysisId on PlaybookRunRequest, and the engine saves Deliver Output through AnalysisResultPersistence (this also fixes #3285). The work is split into three parts that run in order on one branch: 037a (engine, start path and the analysis-create service move; running), 037b (results tool, must-not-leak tests), 037c (widget, `_meta.ui`, CI, publish-size). Two opus passes over the whole of 037 after 037c. Master merged at 710302979d (via 8edc92c225). (2) Owner: deploy go for file-probe 9e8f0ca8e0 through uac-r2 (tell uac-r2 it layers on d307d67e36). (3) Still open: #3181 a/b, E1, NFR-05 wording, 032 option, the process-guard card. (4) Read the PR #3259 typecheck result. |
| **Uncommitted** | None. |
| **Branch state** | The 033 fix branch brought in master's provisioning commits (#3244, #3251). Full ArchTests on the merged tree: 2867 at 5f7adf0290. Merged-tree unit filter: 10,815 passed at cd12bb4c2f. |

## Status (TASK-INDEX is authoritative)
- ✅ **Phase 0:** 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011. 012: the registry PATCH is applied; the live check is still owed.
- ✅ **Phase 1 code:** 020, 021, 022, 023, 024, 025, 026, 027. 029 (deploy) waits on 019 and the owner's go.
- ✅ **Phase 2 done:** 030, 031, 033, 034, 035, 038.
- 🔄 **In progress:** 037 (option B approved, D-037-engine; build running).
- 🔲 **Blocked on owner:** 032 (option), 036 (#3181). 040 waits on 039.

## Open owner questions
Answered 2026-11-26: see notes/decisions.md D-owner-answers-2026-11-26 (1, 2, 3, 6, 7, 8, 9, 11, 12, 13, 15, 16, 17, 18 and the amendment sign-off). Still open:
1. **#3181:** fix (a) read the name only after a caller Read check, or (b) require Read + AppendTo. Recommendation (b). Needed before 036.
2. **019 / runbook E1:** not done. The owner does E1 (both "Spaarke AI" Registry rows: uninstall, then delete), then gives a go for the SHA through uac-r2. #3186 is a code follow-up.
3. **NFR-05 and oid in logs:** recommendation: an oid is an opaque id, not a claim. Narrow NFR-05 to "no tokens, names, emails, UPNs, roles or other claim values; oid/tid allowed as audit ids".
4. **032 matter summary (escalation, notes/032-decisions.md):** the matter-summary Binding is not a playbook (SUM-CHAT@v1, chat-session dispatch), so 030's run path cannot start it. Option 2 (recommended now): read-only v1, which needs an FR-10 amendment to drop "starts a run". Option 1 (later): author a matter-subject JPS playbook. Option 3 rejected.
5. **Stray-process prevention:** a watchdog plus a Bash pre-check (proposal sent to the owner).

## Issues filed this wave
#3268 #3281 #3234 #3235 #3205 #3206 #3207 #3208 #3209 #3212 #3215 #3218 #3219 #3220 #3221 #3224 #3226 #3227 #3228 #3229 #3230 (plus earlier: #3173 #3178 #3179 #3181 #3182 #3185 #3186 #3196 #3198).

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
