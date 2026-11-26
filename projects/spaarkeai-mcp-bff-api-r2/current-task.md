# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-26 (checkpoint: Phase 1 wave 5 closing; 025 under review, 027 pass-2 fixes pending)
> **Phase**: Phase 0 code complete; its owner-gated items are listed below. Phase 1 is running under D-r1-gate.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 1. Sub-agents run in isolated worktrees (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`). The main session merges each branch (never rebase), runs the verifiers, applies `.claude/` edits and files issues. At most 4 sub-agents at once. |
| **Running now** | The two 025 verifiers: pass 1 (tenant isolation) and pass 2 (parity and shape). A background rerun of a sample of failed Office/Search tests (see below). |
| **Next** | (1) Launch an implementer for the 027 pass-2 follow-ups (list below), merge it, then mark 027 done. (2) Launch 022 (search tools; deps 009, 020, 027). Its brief must say: fix #3212 (search 500s echo ex.Message) with a regression test; bound `limit`; catch shared-method exceptions; never expose TotalResults or count until #1533 is fixed. (3) Fix the 025 verifier findings, then mark 025 done. (4) After Phase 1 code: full build, full ArchTests, push, devops-project-sync, append to the shared notes status file. (5) 029 deploy waits on the owner gates. |
| **Uncommitted** | None. HEAD c25c884ea5 is pushed. |
| **Branch state** | origin/master merged at 3b5c87fbe1 (includes r1 #3199). The BFF builds. ArchTests 2848–2853 green on agent runs. The decisions widget passes tsc and jest (392). |

## 027 pass-2 follow-ups (no F1; to fix in scope)
- **ArchTest gaps** in `tests/Spaarke.ArchTests/SearchAuthorizationPhaseCallerGuardTests.cs:73-75`: the regex must allow `global using`, add a `global using static` must-fire control, and seal both filter classes (or flag derivation).
- **Golden tests:** add one theory pinning the REST status, title, detail, code and errorCode for every Semantic/Record refusal and for the count 403. No test pins titles today.
- **D-027-2 regression test:** file a GitHub issue and move or rename the test to `tests/integration/regression/Issue{N}_*`.
- **Notes correction:** the entity total does leak, under the already-filed #1533; correct notes/027-decisions.md.
- **Pre-existing, check and file if new:** `useRecordSearch.ts:182` `hasMore` can never be true.
- Leftover export folder `C:\wt027r`: a guard blocked its deletion; remove it by hand or leave it.

## Wider test run after the 027 merge
About 20 failures, mostly Office/DataMutation tests that 027 doesn't touch. Each took 3–4 minutes, so suspected timeouts under machine load. Rerunning a sample on its own (background). If they pass alone, record the failures as load; if they fail, investigate.

## Status (TASK-INDEX is authoritative)
- ✅ **Phase 0:** 001, 002, 003, 004, 006, 007, 009, 010, 011.
- ✅ **Phase 1:** 020, 021, 023, 024, 026.
- 🔄 **Merged, under review:** 025 (two passes running) and 027 (pass-2 follow-ups pending).
- 🔲 **Waiting:** 022 (after 027).
- 🔄 **Owner-gated:**
  - 005: code and widened script merged; the dev schema go is not given;
  - 008: A10 Path A or C;
  - 012: registry PATCH go.
- **030:** keeps its ADR-039 check. The run-record decision is made (D-run-record-decided), and the one-writer rule holds: `PlaybookRunRecordStore` is the only writer.

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

## Issues filed this wave
#3205 #3206 #3207 #3208 #3209 #3212 #3215 (plus earlier: #3173 #3178 #3179 #3181 #3182 #3185 #3186 #3196 #3198).

## Peer coordination
- **mcp-r1:** #3199 merged and brought into this branch. #2575 (r1 ISS-001, the legacy Copilot agent code) was handed to us 2026-11-26: task 001 (8a92da8751) removes it, so the merge PR body must say "Closes #2575". r1 owns the ISS-010 files and the Decision wizard heading files; message r1 before editing them.
- **specialist-legal-agents-r1:**
  - Run record decided. One shared writer (`PlaybookRunRecordStore`) is agreed; their Phase 1 adds the profile-version and gate columns to it and may later need a "required" write mode.
  - Docs PR #3213 is checked and matches 005.
  - When #3213 merges, fold our CAPABILITY-INDEX row 35 into theirs and renumber ours from 36.

## Decided
- D-r1-gate.
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
