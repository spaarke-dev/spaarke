# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-26 (checkpoint at 85% context: Wave 7 merged; 031 and 034 waiting only on a merged-tree full ArchTests + publish delta)
> **Phase**: Phase 0 and Phase 1 code complete. Phase 2 Wave 7: 031 and 034 merged with verifier fixes; 030 stopped (owner Q14); 033 and 036 blocked on owner decisions.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 2 Wave 7. Sub-agents run in isolated worktrees (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`). The main session merges each branch (never rebase), runs the verifiers (two adversarial passes on opus per FULL task; scoped re-check of the fix diff), applies `.claude/` edits and files issues. At most 4 sub-agents at once. |
| **State** | 031 (analysis tools) and 034 (briefing tool and widget) are merged with both review passes' fixes. Merges: 034 3a93364b9d, fixes 6895d4525b; 031 ef89428a89, fixes 6a566dafcd. Scoped re-checks done. CAPABILITY-INDEX rows 45–48 are applied. Issues filed: #3226–#3233. |
| **Next (exact)** | (1) When 035 reports: merge, run two opus review passes, fix, re-check, mark done. (2) Then the owner questions decide the rest: Q14 (030 run model, blocks 030 and 032), 008 (blocks 033), #3181 (blocks 036), Q16 (FR-09 preferences). Deploys 019/029/039 wait on owner gates. |
| **Uncommitted** | None. 031 and 034 marked done; pushed. |
| **Branch state** | Agent worktrees brought in newer master PRs. The agent gates were green: 031 branch full ArchTests 2859; 034 fixes, filtered ArchTests 117, widget jest 130. The merged tree has not had a full ArchTests run yet. |

## Status (TASK-INDEX is authoritative)
- ✅ **Phase 0:** 001, 002, 003, 004, 006, 007, 009, 010, 011.
- ✅ **Phase 1 code:** 020, 021, 022, 023, 024, 025, 026, 027. 029 (deploy) waits on 019 and the owner's go.
- 🔄 **Owner-gated:**
  - 005: code and widened script merged; the dev schema go is not given;
  - 008: A10 Path A or C;
  - 012: registry PATCH go.
- ✅ **Phase 2 done:** 031, 034 (merged-tree gates at 1895213b48: ArchTests 2859, unit tests 1367; publish about 42 MB). Wave 8: 035 running.
- 🛑 **030:** stopped before code (owner Q14). 032 waits on it.
- 🔲 **Blocked on owner:** 033 (008), 036 (#3181). 035 is ready (Wave 8).

## Open owner questions
1. **005:** the go to run the widened `scripts/dataverse/Add-PlaybookRunColumns.ps1` on dev. Also the earlier §4 acceptance-criterion-3 question in notes/005-schema.md.
2. **012:** the go for the registry-key PATCH on dev.
3. **008:** the ADR-028 A10 conflict, Path A (recommended) or Path C.
4. **#3181:** fix (a) or (b), before 036 (not 025).
5. **019:** is runbook E1 done? Plus #3186.
6. **design.md** lacks `<hot-path-declaration>`.
7. **Consumer tsc** for DailyBriefing and LegalWorkspace, via a draft PR.
8. **REST changes made or proposed:**
   - #3205: empty portfolio cached;
   - #3208: playbook lists answer 500 with AI disabled, should be 503;
   - #3215: document names never filled, affecting 3 outputs;
   - D-027-2: an empty record page past the end now reports count 0 (done);
   - 024: a client abort on the playbook lists now returns 499 (done).
9. **D-023-preview:** the preview link is withheld from model-visible output (fail-closed; spec FR-04 lists it). The owner may reverse it.
10. **NFR-05:** do logs carrying the oid (007/024 shared methods) count as "claims"?
11. **uac-r2:** the document list checks Read on the matter, not on each document (023 D1).
12. **Stray processes:** four python.exe processes from other sessions have been spinning for days (PIDs 41460, 43772, 84288, 93720). The user decides.
13. **Cowork review skill pairing (recorded 2026-11-26, for accelerator-r1):** `notes/cross-project-cowork-review-skill.md`. A Spaarke Cowork skill (for example `nda-review`) sits on top of this project's tools. Gaps for accelerator-r1 to decide: getting the document file into the sandbox, and matter email through `spaarke_compose_email`, not Outlook. No r2 action.
14. **030 run-detach (🔔 ADR conflict, blocks 030 start and 032):** a run must outlive the stateless MCP request. Options 1-4 in `notes/030-decisions.md`; recommended Option 4 (run inside the request up to ~25 s, else hand off to the Console), plus ship `spaarke_get_playbook_run` now. Options 1-3 need an ADR-052, ADR-039 or ADR-028 A10 exception.
15. **MCP AI-tool rate limit (034 review K3, project-wide):** MCP tools sit under the `mcp` limiter (60 per minute per user), while the REST AI routes they wrap use `ai-batch` (20 per minute, queue 5). A model can therefore trigger about three times more LLM renders per user than the Console can (briefing, analysis, run). Add a per-tool limiter, or accept it?
16. **FR-09 saved preferences (034, #3232):** the briefing tool now uses the Console's default windows (5 days, 168 hours), but not a user's saved Daily Digest preferences: no BFF reader of `sprk_userpreference` exists. Either narrow FR-09's "equals the Console" to default preferences, or approve a new caller-scoped preference reader (§11 surface).

## Issues filed this wave
#3205 #3206 #3207 #3208 #3209 #3212 #3215 #3218 #3219 #3220 #3221 #3224 #3226 #3227 #3228 #3229 #3230 (plus earlier: #3173 #3178 #3179 #3181 #3182 #3185 #3186 #3196 #3198).

## Peer coordination
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
