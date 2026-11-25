# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (checkpoint: Phase 0 execution, waves 1–2 mostly done)
> **Phase**: Phase 0 executing. Phase 1+ BLOCKED on r1 task 042 (r1 PR #3161 merged; mcp-r1 session will message when 042 merges).

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` Step 5, Phase 0. Each task runs as a sub-agent in an isolated agent worktree (`C:\code_files\spaarke\.claude\worktrees\agent-*`, branch `worktree-agent-<id>`), commits locally; main session merges the branch into `work/spaarkeai-mcp-bff-api-r2` (merge, never rebase), updates TASK-INDEX, files issues, applies `.claude/` edits. Session cap = 4 concurrent sub-agents (task agents' own reviewers count) |
| **Running now** | 007 (playbook auth shared method; in final regression run), 010 (Daily Briefing seam), 004 (Dataverse MCP research + guide paragraph), 009 fix implementer (cache-key/allow-list order, request bounds, comments, test rows) |
| **Next** | On each completion: merge branch, mark TASK-INDEX. Then verifier passes still owed: 002 (1 pass), 011 (1), 010 (1), 007 (2, auth), 009 (pass 2 after its fixes merge), 004 per its rigor. 008 pass 2 waits for the owner's A10 decision. After all Phase 0 code: build + full ArchTests, push, then 019 (deploy) waits for owner gates below |
| **Uncommitted** | none (last commit 6cab51a45f; pushed up to aec9f383e6 — push again) |
| **Branch state** | master merged in via agent branches (includes r1 #3161); BFF build 0/0; ArchTests 2831/2832 at 3a35b5d3cf, the one ADR-052 doc-drift failure passes at HEAD (117/117) |

## Phase 0 status (TASK-INDEX is authoritative)
- ✅ 001 (2 verifier passes + fixes 0aca5dbbc9, 3a35b5d3cf), 002 (verifier pass owed), 003 (main session, aec9f383e6), 006 (2 passes in-agent).
- 🟡 005 code complete — owner go for `scripts/dataverse/Add-PlaybookRunColumns.ps1` (9 columns, dev).
- 🟡 012 premise stale: row/column exist (`sprk_recordsummary`); owner go for `scripts/dataverse/Set-MatterSummaryTopicRegistryKey.ps1` (one PATCH `sprk_playbookname` chat-summarize → matter-summary).
- 🔄 008 done in code; pass 1: ADR-028 A10 conflict (notes/008-decisions.md) → owner picks Path A or C; pass 2 after.
- 🔄 009 proof clean; fixes running; pass 2 after.
- 🔄 011 merged (447281dfe5); verifier pass owed. 🔄 007, 010, 004 running. 019 waits.

## Open owner questions (put to the owner in the report)
1. 005: go to run the run-columns script on dev; accept criterion 3 as "identical except documented differences" (notes/005-schema.md §4).
2. 012: go to run the registry-key PATCH on dev.
3. 008: 🔔 ADR-028 A10 conflict — Path A (spec exception, FR-11 parity wins) recommended vs Path C.
4. #3181 (DEF-006-01 security): pick fix (a) read name/number only after a Read check, or (b) require Read|AppendTo; must land before tasks 025/036.
5. 019: runbook E1 done? (dev Azure Bot still points at the removed route; 404 after deploy).
6. design.md lacks `<hot-path-declaration>` (block is in spec.md) — owner may add it.

## Issues filed this session
#3173 (T257 plumbing, customer-provisioning), #3178 (orphaned Running runs → 030/FR-14), #3179 (run-record retention), #3181 (DEF-006-01 security), #3182 (EventStatusDeprecationTests failing, demo-data scripts).

## Decided (owner)
- 2026-11-25 (pipeline): matter summary = complete the `matter-summary` topic (new column + registry row), not matter-health.
- 2026-11-25 (spec interview): reversible writes model-callable, send email app-only; persist runs to `sprk_playbookrun`; email = matter email + matter documents, archived; matter summary = Insights cache, else start; briefing = AI Daily Briefing (seam in `Spaarke.DailyBriefing.Components`).
- 2026-11-25: Q1–Q8 decided (design v0.3): one connector; 30 s limit; widgets reuse Console components; Phase 0 parallel with r1; Dataverse MCP dev/admin only.
- 2026-11-25: remove the Spaarke AI agent NOW; tenant E1/E2 are the owner's.


## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions are the owner's.
- Commits: `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"`; end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Sub-agent worktrees: the lint-staged pre-commit hook uses git stash internally (not us); agents' Bash `export PATH` may be refused — commit hook still runs.
