# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (checkpoint: Phase 1 wave 5 running)
> **Phase**: Phase 0 code complete; its owner-gated items are listed below. Phase 1 is running: the r1 gate was released by D-r1-gate, a joint mcp-r1/mcp-r2 determination made under the owner's delegation.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 1. Tasks run as sub-agents in isolated worktrees (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`). The main session merges each branch (never rebase), runs the verifier passes, applies `.claude/` edits and files issues. At most 4 concurrent sub-agents. |
| **Running now** | 023 (document tools), 027 (search-auth extraction, opus, new), 024 (list_playbooks), 026 (portfolio) |
| **Next** | On each completion: merge the branch, apply pending `.claude/` edits (`notes/capability-index-pending.md`), then run the verifier passes: two for 027 (auth and tenant-isolation), one each for 023, 024 and 026. Then 022 (needs 027) and 025 (tenant-isolation, two passes). After Phase 1 code: build, full ArchTests, push. 029 (deploy) waits on the owner gates. |
| **Uncommitted** | None. The last commit is pushed. |
| **Branch state** | origin/master merged in at b62ac045cc and later through agent branches. BFF build: 0 warnings, 0 errors. ArchTests: 2832/2832 at 89f3d68107, and 020 reported 2846/2846. |

## Status (TASK-INDEX is authoritative)
- ✅ **Phase 0 done:** 001, 002, 003, 004, 006, 007, 009, 010, 011.
- ✅ **Phase 1 done:** 020, 021 (verifier fixes merged).
- 🔄 **Owner-gated:**
  - 005: held by D-run-record-hold;
  - 008: A10 conflict, Path A or Path C;
  - 012: needs a go for the registry PATCH.
- 🔄 **Running:** 023, 024, 026, 027.
- 🔲 **Waiting:** 022 (after 027), 025.
- **030** is held by its POML constraints. It needs an ADR-039 check on the node engine and the owner's run-record decision first. It must also fix #3198 and add the run-link seam.

## Peer coordination
- **mcp-r1**
  - Owns the ISS-010 files and the Decision wizard heading change (PR #3199, ontology reviewing). It will notify before merging.
  - r2 owns `Api/Mcp/**`, the ledger, the conformance host, and the decisions widget's transport and build files. The widget UI, including DecideLane.tsx, stays with r1.
  - I proposed that r1 add the D-227 billing-row change to #3199 and am waiting for an answer. If r1 insists r2 does it, take it as a separate small PR off master.
- **specialist-legal-agents-r1** (the starting session; same id as the brief sender)
  - Its run-path review asks to hold 005's columns until the owner picks a run-record design, and to run an ADR-039 check before 030. Both are recorded (D-run-record-hold).
  - It has been told about the D-r1-gate release.

## Open owner questions
1. **005:** pick the run-record design, with specialist-legal-agents-r1. Options:
   - a new capability-run table;
   - relax `sprk_playbookid` and add an action lookup;
   - use the ledger plus the Decision Record.

   Then give the go to run the schema script on dev.
2. **012:** go to run the registry-key PATCH on dev.
3. **008:** 🔔 ADR-028 A10 conflict. Path A (recommended) or Path C.
4. **#3181** (DEF-006-01, security): choose fix (a) or (b) before 025/036.
5. **019:** is runbook E1 done? Also add the removal of the bot's Key Vault access policy to E2 (#3186).
6. **design.md** lacks the `<hot-path-declaration>` block (it is in spec.md).
7. **Consumer tsc** for DailyBriefing and LegalWorkspace is unverified locally. A draft PR would run it.

## Issues filed
- #3173, #3178, #3179, #3181, #3182, #3185, #3186
- #3196: CI filters for Worklist/DecisionWizard
- #3198: HandoffUrlBuilder dev fallback; 030 fixes it

## Decided
- **D-r1-gate:** Phase 1 released. The owner's words, relayed verbatim by mcp-r1: "I can't judge. You have to coordinate and determine with mcp-r2 the correct approach and path".
- **D-run-record-hold:** see open owner question 1.
- **Task 027 added (2026-11-25):** 022 escalated because the search access checks live in filters and endpoint statics. 027 extracts them following 007's pattern, and 022 now depends on it.
- **Earlier owner decisions:**
  - the matter-summary topic;
  - reversible writes are model-callable, sending email is app-only;
  - runs are persisted;
  - the 30 s limit;
  - remove the Spaarke AI agent now.

## Standing directives
- Never `git stash`, never `python3` in Bash, and every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message.
- Dev BFF deploys go through uac-r2 with the owner's go for each SHA. Tenant and admin actions are the owner's.
- Commits: run `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"` first, and end the message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- `[blocked]` in TASK-INDEX means blocked-shipped. Owner-gated work that has not shipped stays 🔄 `[wip]`.
- Wave agents don't edit `.claude/`, TASK-INDEX or current-task.md, and don't launch reviewers. The main session runs the verifier passes.
