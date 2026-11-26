# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-26 (checkpoint at 85%: 038 F4 needs owner; 033 has 4 F to fix; file-probe rebuilt on dev SHA)
> **Phase**: Phase 0 code complete except 012 live check. Phase 1 code complete. Phase 2: 030, 031, 034, 035 done; 033, 038 running; 037 next; 032 escalated; 036 waits on #3181.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 2 (Wave 8 done; waiting on owner decisions). Sub-agents run in isolated worktrees (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`). The main session merges each branch (never rebase), runs the verifiers (two adversarial passes on opus per FULL task; scoped re-check of the fix diff), applies `.claude/` edits and files issues. At most 4 sub-agents at once. |
| **State** | Done: 005, 008, 030 (board 23). Merged, not yet marked done: **038** (d6074501e7) and **033** (ac0ea5fe34). **038 review: F4.** The `instructions` reach PlaybookRunRequest.UserContext, but NO prompt builder reads UserContext (GenericAnalysisHandler and PromptContextHelper ignore it), so the feature does nothing. Fixing it needs an engine edit (ADR-039, owner): (a) approve one edit appending a "User focus" block in PromptContextHelper/GenericAnalysisHandler, or (b) withdraw the argument. Also a pre-existing F: chat "rerun with additional instructions" (AnalysisExecutionHandler.cs:268-278,376-378) claims instructions it ignores; file an issue. **033 review pass 1: no F** (K1 audience test refused by JwtBearer, not A10; K2 non-GUID 400 vs REST 404). **033 pass 2: 4 F**, all to fix: F1 "under budget" when no budget (GetMatterFinanceTool.cs:245); F4 FinanceSummaryService.cs:267-270 logs CurrentSpend (pre-existing, on the tool path: remove); F2 EB-D-194 scan misses InvoiceLineDecisionService etc. and its control proves nothing (switch to an allowlist plus a seeded must-fire); F2 no test of signal severity/time. File-probe spike: `spike/spe-file-probe` e05c209fda (master-based) and **`spike/spe-file-probe-on-specialist` 9e8f0ca8e0** (on dev's current SHA d307d67e36, keeps the specialist spike tools; 277/277 MCP tests). Worktree C:\tmp\wtprobe (remove with Remove-WorktreeSafe.ps1). Draft PR #3259 (CI). |
| **Next (exact)** | (1) Launch an implementer to fix 033's 4 F findings on the work branch, then re-check. (2) Owner: 038 option (a) engine edit or (b) withdraw; file the rerun-instructions issue. (3) Owner: deploy go for file-probe 9e8f0ca8e0 through uac-r2 (tell uac-r2 it layers on d307d67e36). (4) 037 waits until 038 settles. (5) Still open: #3181 a/b, E1, NFR-05 wording, 032 option. |
| **Uncommitted** | None. |
| **Branch state** | Agent worktrees brought in newer master PRs (normal master merges). Full ArchTests on the merged tree: 2859 at 4f56fbbe54. |

## Status (TASK-INDEX is authoritative)
- ✅ **Phase 0:** 001, 002, 003, 004, 006, 007, 009, 010, 011.
- ✅ **Phase 1 code:** 020, 021, 022, 023, 024, 025, 026, 027. 029 (deploy) waits on 019 and the owner's go.
- 🔄 **Owner-gated:**
  - 005: code and widened script merged; the dev schema go is not given;
  - 008: A10 Path A or C;
  - 012: registry PATCH go.
- ✅ **Phase 2 done:** 031, 034, 035 (publish about 42 MB; 035 adds +0.23 MB).
- ✅ **030:** done 2026-11-26 (amendment awaits the owner's security sign-off).
- 🔲 **Blocked on owner:** 033 (008), 036 (#3181). 040 waits on 039.

## Open owner questions
Answered 2026-11-26: see notes/decisions.md D-owner-answers-2026-11-26 (1, 2, 3, 6, 7, 8, 9, 11, 12, 13, 15, 16, 17, 18 and the amendment sign-off). Still open:
1. **#3181:** fix (a) read the name only after a caller Read check, or (b) require Read + AppendTo. Recommendation (b). Needed before 036.
2. **019 / runbook E1:** not done. The owner does E1 (both "Spaarke AI" Registry rows: uninstall, then delete), then gives a go for the SHA through uac-r2. #3186 is a code follow-up.
3. **NFR-05 and oid in logs:** recommendation: an oid is an opaque id, not a claim. Narrow NFR-05 to "no tokens, names, emails, UPNs, roles or other claim values; oid/tid allowed as audit ids".
4. **032 matter summary (escalation, notes/032-decisions.md):** the matter-summary Binding is not a playbook (SUM-CHAT@v1, chat-session dispatch), so 030's run path cannot start it. Option 2 (recommended now): read-only v1, which needs an FR-10 amendment to drop "starts a run". Option 1 (later): author a matter-subject JPS playbook. Option 3 rejected.
5. **Stray-process prevention:** a watchdog plus a Bash pre-check (proposal sent to the owner).

## Issues filed this wave
#3234 #3235 #3205 #3206 #3207 #3208 #3209 #3212 #3215 #3218 #3219 #3220 #3221 #3224 #3226 #3227 #3228 #3229 #3230 (plus earlier: #3173 #3178 #3179 #3181 #3182 #3185 #3186 #3196 #3198).

## Peer coordination
- **mcp-r1 (2026-11-26):** r1 builds ISS-007 #2835 (Origin/Host filter on /mcp/legal-operations, own file + one wiring line, from master) and DEF-001 #2832 (closedToday on spaarke_show_decisions); r2 reviews both and is messaged before each PR opens and merges.
- **mcp-r1:** #3199 merged and brought into this branch. #2575 (r1 ISS-001, the legacy Copilot agent code) was handed to us 2026-11-26: task 001 (8a92da8751) removes it, so the merge PR body must say "Closes #2575". r1 owns the ISS-010 files and the Decision wizard heading files; message r1 before editing them.
- **specialist-legal-agents-r1:**
  - Run record decided. One shared writer (`PlaybookRunRecordStore`) is agreed; their Phase 1 adds the profile-version and gate columns to it and may later need a "required" write mode.
  - Docs PR #3213 is checked and matches 005.
  - When #3213 merges, fold our CAPABILITY-INDEX row 35 into theirs and renumber ours from 36.

## Decided
- D-r1-gate.
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
