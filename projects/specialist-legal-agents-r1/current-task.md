# Current Task State — specialist-legal-agents-r1 (umbrella), Phase 0

> **Last Updated**: 2026-11-28 (checkpoint: tasks 021 and 025 prepared and waiting on the owner; accelerator findings sweep running)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Recovery**: read this file, then `CLAUDE.md` (binding rules, §4 coordination, §6 gotchas), then `tasks/TASK-INDEX.md`. History is in git log and `notes/decisions.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Phase** | Phase 0 in execution. 001–015 ✅; 020, 021, 025 🔄; 022–024, 026, 030, 031, 090 🔲 |
| **Dev BFF** | `b84fa5f061` = spike tip `af8a5547ef` + mcp-r2 file probe (uac-r2, since 2026-11-28 ~04:15Z; gate PASSED; rollback `af8a5547ef`). tools/list = 10, all 4 spike tools. Spike branch `spike/specialist-agents-phase0` tip `af8a5547ef` (worktree `C:\wt-sla-spike`). Any dev deploy from master removes the spike tools: merge master into the spike branch, check `git patch-id` equals the reviewed spike (48c5630a3f…), send uac-r2 the tip; the owner gives the per-SHA go in uac-r2's session |
| **Uncommitted** | none at checkpoint (last commit `458ad23e23`) |
| **In flight** | researcher agent (sonnet) sweeping the accelerator branch notes + coordination CLAUDE.md gotchas for findings that change our test prep; result → apply to the affected READMEs, add to CLAUDE.md §6, tell the owner |

## Next action (exact)

1. **Accelerator findings (owner asked 2026-11-28: "coordinate with Accelerator so have benefit of those findings")**: when the researcher returns, apply each relevant finding to the affected kit (`phase0/g1-nda/README.md` / `packages/UPLOAD.md`, `phase0/g5`, `phase0/g3`, `phase0/g6`, `phase0/patents`), add dated gotchas to `CLAUDE.md` §6, commit, and report. Standing request sent to acclerator-r1 (session `local_64a7776f-8f89-4df6-81b8-9bd990288b0d`) to send us findings as recorded; we send them ours.
2. **Task 020 🔄** — owner load check AS DEMO: sign in per agent (G1c showed "Sign in to Spaarke MCP G1c"); per agent one new chat: `Call the Spaarke tool spaarke_show_decisions`, then `List every tool and source you can use in this conversation`; then Cowork as demo with Spaarke MCP on. Owner results all PASS (`phase0/g1-nda/packages/UPLOAD.md` "Load check results"). Then send the demo result to coordination and close 020.
3. **Task 025 🔄** — plugin `spaarke-legal-pat-cowork-test` (app id `55352aad-2e90-41b5-a571-1d0e783ed9fc`, v1.0.0) built under `phase0/patents/package/` (zip gitignored); R4 row sent to coordination 2026-11-28, waiting for its PLANNED record. Then the owner uploads via admin center Tools > Registry (never Cowork first) and runs SP-1-A (owner), SP-2-B (demo), controls C1–C3 (`phase0/patents/README.md` "Spike package").
4. **Task 021 🔄** — owner runs the G3 run sheet (`phase0/g3/README.md` "Run sheet", rows G3-1..G3-8) and sends times + profileVersion per row; this session then queries App Insights (`traces | where message startswith 'MCP spike tool'`) and fills Headers seen / Answers / p95.
5. **Task 022** waits on the owner removing the G8 artifacts (due 2026-11-30) and on 020.

## Open items outside the task list

- **Owner, by 2026-11-30 (admin center):** remove G8-a agent + 2 skills, G8-p2 `802fd9cc`, G8-p2-sl `d38f8df8`, G8-p2-org-learn `0421c980`, G8-p2-dd `cb5afd1c`; then tell COPILOT-PLATFORM-COORDINATION to close the rows. Precedes task 022.
- **PR #3213** (playbook-architecture docs + ADR-039 amendment, branch `docs/playbook-architecture`, worktree `C:\code_files\spaarke-wt-docs-playbook-architecture`): waiting on CI and the owner's merge; afterwards remove the worktree with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- **Owner:** Spaarke AI tenant removal (coordination runbook E1/E2).
- **design.md** (owner's document): lacks a `<hot-path-declaration>` block; §3.14/§12 D-A18 (7) and §3.15 stale.
- **Still open, not Phase 0 blockers:** D-A12, D-A13 (Microsoft-first), D-A1, D-A7 (at task 030), D-A5, the R1 change.

## Decided (do not re-open)

- Run path = action model; node engine frozen; playbooks as a concept live. `sprk_playbookrun` is the one AI-run record (`PlaybookRunRecordStore`).
- OD-2: patent attorney first; NDA is a function. Phase 0 runs in this folder.
- Full log: design.md §12 and `notes/decisions.md`.
