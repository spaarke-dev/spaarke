# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (checkpoint: Phase 0 code complete; waiting on owner gates)
> **Phase**: Phase 0. All code tasks merged and verified. Remaining Phase 0 items are owner-gated. Phase 1+ is BLOCKED on r1 task 042: r1 PR #3161 is merged, and the mcp-r1 session will message when 042 merges.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | None running. Waiting on the owner's answers below, and on r1 task 042 for Phase 1. |
| **Next** | (1) On the owner's 008 decision, apply it, run 008 verifier pass 2, then mark it done. (2) On the owner's go, run the 005 and 012 dev scripts, verify, then mark both done. (3) 019 deploys when E1 is confirmed and the owner gives a per-SHA go through uac-r2. (4) When r1 042 merges: merge master (merge, never rebase), then start Phase 1 (020–026) through task-execute. |
| **Uncommitted** | None. The branch is pushed. |
| **Branch state** | `work/spaarkeai-mcp-bff-api-r2`, master merged in via agent branches (includes r1 #3161). BFF build 0 warnings / 0 errors. ArchTests 2832/2832 at 89f3d68107. Status drift check clean. |
| **Execution model** | Each task runs as a sub-agent in an isolated worktree (`C:\code_files\spaarke\.claude\worktrees\agent-*`). The main session merges its branch, runs the verifier passes, applies `.claude/` edits and files issues. At most 4 concurrent sub-agents, counting their own reviewers. |

## Phase 0 status (TASK-INDEX is authoritative)
- ✅ **Done, all verifier passes run and fixes merged:** 001, 002, 003, 004, 006, 007, 009, 010, 011.
- 🔄 **005:** code complete. Needs the owner's go to run `scripts/dataverse/Add-PlaybookRunColumns.ps1` (9 columns, dev).
- 🔄 **012:** the column (`sprk_recordsummary`) and the registry row already exist. Needs the owner's go to run `scripts/dataverse/Set-MatterSummaryTopicRegistryKey.ps1` (one PATCH).
- 🔄 **008:** code complete; pass 1 raised an ADR-028 A10 conflict (`notes/008-decisions.md`). Pass 2 follows the owner's choice.
- 🔲 **019 (deploy):** waits on 005's dev schema, runbook E1 and a per-SHA go.

## Carried into later tasks (already written into their POMLs)
- **030:** the run must not keep using the MCP request's `HttpContext` after the tool returns (cross-user OBO risk). Other items:
  - cap the input count and size before `AuthorizePlaybookRunAsync`;
  - pass the authorized inputs to the run;
  - set `RunUserId`/`StartedBy=Person`;
  - truncate `RejectedParameterKey`;
  - A10: `RunUserUnresolved` is not the no-systemuser outcome.
- **040:** the idempotency key is bound to the payload, reserved atomically, and not consumed by a 4xx refusal. Status stays an integer 400–599. The widget shows the picker error. The callTool timeout goes in 021.
- **022:** escape the `search.in` delimiter (from 009).
- **034:** check the Daily Briefing widget bundle for module side effects (`@spaarke/auth` and the ui-components barrel).

## Open owner questions
1. **005:** go to run the run-columns script on dev? Also, accept criterion 3 as "identical except the documented differences" (`notes/005-schema.md` §4)?
2. **012:** go to run the registry-key PATCH on dev?
3. **008:** 🔔 ADR-028 A10 conflict. Path A (a spec exception; FR-11 parity wins) is recommended over Path C.
4. **#3181 (DEF-006-01, security):** choose fix (a) or (b) before tasks 025/036.
5. **019:** is runbook E1 done? Also add the bot's Key Vault access-policy removal to E2 (#3186).
6. `design.md` lacks `<hot-path-declaration>`; the block is in `spec.md`.
7. Consumer `tsc` for DailyBriefing/LegalWorkspace is unverified locally (no `node_modules`). A draft PR would run it in CI.

## Issues filed
- #3173: T257 plumbing
- #3178: orphaned Running runs
- #3179: run retention
- #3181: DEF-006-01, security
- #3182: EventStatusDeprecationTests
- #3185: stale Dataverse MCP tool names
- #3186: BYOK orphan bot and the E2 Key Vault policy

## Decided (owner)
- 2026-11-25 (pipeline): the matter summary completes the `matter-summary` topic, not matter-health.
- 2026-11-25 (spec interview):
  - reversible writes are model-callable; send email is app-only;
  - runs persist to `sprk_playbookrun`;
  - email covers matter email plus matter documents, archived;
  - the matter summary comes from the Insights cache, otherwise a run starts;
  - the briefing is the AI Daily Briefing (seam in `Spaarke.DailyBriefing.Components`).
- 2026-11-25: Q1–Q8 decided (design v0.3):
  - one connector;
  - a 30 s limit;
  - widgets reuse Console components;
  - Phase 0 runs in parallel with r1;
  - Dataverse MCP is dev/admin only.
- 2026-11-25: remove the Spaarke AI agent now; tenant steps E1/E2 are the owner's.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions are the owner's.
- Commits: run `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"` first, and end each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- In `TASK-INDEX`, `[blocked]` means blocked-and-shipped (the drift script counts it as done). Owner-gated work that hasn't shipped stays 🔄 `[wip]`.
