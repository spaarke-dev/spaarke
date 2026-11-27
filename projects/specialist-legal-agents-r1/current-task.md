# Current Task State — specialist-legal-agents-r1 (umbrella), Phase 0

> **Last Updated**: 2026-11-27 ~21:00Z (spike `af8a5547ef` live on dev ~20:57Z with all 4 spike tools, rollback `d9595dd98e`, gate PASSED 21:28Z; owner uploaded all 8; scope + load check pending)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Recovery**: read this file, then `CLAUDE.md` (binding rules, gotchas), then `tasks/TASK-INDEX.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Phase** | Phase 0 in execution. `spec.md` (2026-11-25), `plan.md`, project `CLAUDE.md`, 20 POMLs + `tasks/TASK-INDEX.md` (2026-11-26, lint PASS, no drift) |
| **Active task** | 001–015 ✅; spike live on dev at `af8a5547ef` since ~20:57Z 2026-11-27 (off ~20:19–20:57Z while master `d9595dd98e` was deployed; earlier `5cd40984b0`). **020 🔄**: 8 packages built (a0d73c0b9e). R4 rows for 7 sent; coordination recorded them PLANNED (263cfa87b8). G5 stubs `spike_review_agreement` / `spike_review_legal_hold` at spike `5cd40984b0`, **live on dev 2026-11-27 ~13:14Z** (uac-r2; `tools/list` 9 tools incl. all 4 spike tools; rollback `d307d67e36`; 30-min gate PASSED 13:45Z). G5 R4 row sent 2026-11-27 and recorded PLANNED by coordination (c0445b2cbe), so all 8 rows are recorded |
| **Stop points** | 010 DEV write (`agreement-review-spike`) needs the owner's go; 015 dev deploy needs the owner's per-SHA go via uac-r2 |
| **Uncommitted** | see `git status` |

## Next action (exact)

1. Coordination checks on 020: DONE (d1cf402ec2; test markers, run isolation, R1′ wording; reply sent).
2. DONE 2026-11-27: spike `5cd40984b0` live on dev (owner go in uac-r2), all 4 spike tools in `tools/list`, G5 R4 row sent to coordination. uac-r2's 30-minute gate PASSED at 13:45Z; rollback stays `d307d67e36`. G1 (c) runs from now on see the broadened `spike_review_nda` description (notes/decisions.md 2026-11-27).
2b. 2026-11-27: master `d9595dd98e` removed the spike tools ~20:19Z; spike `af8a5547ef` (same spike patch-id as `5cd40984b0`) **live ~20:57Z** on the owner's go in uac-r2, `tools/list` = 9, rollback `d9595dd98e`; uac-r2 gate PASSED 21:28Z. Every future dev deploy from master removes the tools: merge master into the spike branch and send uac-r2 a new tip.
3. **Owner (2026-11-27): all 8 packages UPLOADED** (screenshots: each Available; agents show the dynamic-discovery banner). Pending from the owner: confirm each Users tab lists only owner + demo@ (R7), then the load check per `phase0/g1-nda/packages/UPLOAD.md` ("List every tool" as owner and demo@; Cowork `/` list), with the answers. Coordination told of the uploads. Remove the G8 artifacts by 2026-11-30.
4. Then 021/022 runs. The latency p95 check moved into 021.

## Open items outside the task list

- **PR #3213** (playbook-architecture docs + ADR-039 amendment, branch `docs/playbook-architecture`, worktree `C:\code_files\spaarke-wt-docs-playbook-architecture`): waiting on CI and the owner's merge. After the merge, remove the worktree with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- **Owner, by 2026-11-30 (admin center):** remove G8-a agent + 2 skills, G8-p2 `802fd9cc`, G8-p2-sl `d38f8df8`, G8-p2-org-learn `0421c980`, G8-p2-dd `cb5afd1c`; then tell COPILOT-PLATFORM-COORDINATION to close the rows. This precedes task 022.
- **Owner:** Spaarke AI tenant removal (coordination runbook E1/E2); mcp-r2's removal deploy waits on E1.
- **Stale in design.md** (owner's document, not edited): §3.14/§12 D-A18 (7) assumes a required playbook on `sprk_playbookrun` (now optional, mcp-r2 task 005); §3.15 `PriorOutputReference` is documentation, not chaining.
- **design.md lacks a `<hot-path-declaration>` block** (pipeline warning; all surfaces N for Phase 0). Owner to add.
- **Still open, not Phase 0 blockers:** D-A12 framing; D-A13 Microsoft-first; D-A1 front door; D-A7 (decided at task 030); D-A5 pricing; the R1 change (with coordination). Phase 1 open point: a "required" write mode for gated side-effect runs vs recording the gate decision in the Decision Record.

## Decided (do not re-open)

- Run path = action model (`IActionRunner` + `PublicContracts` facade; `ICodedWorkflow`); node engine frozen; playbooks as a concept live (`docs/architecture/playbook-architecture.md`).
- `sprk_playbookrun` ("AI Run") is the one AI-run record, written only through `PlaybookRunRecordStore` (mcp-r2 task 005).
- OD-2: patent attorney first; NDA is a function; intake harness = `spaarke-intake-pipeline-r1` (#3128).
- Phase 0 runs in this folder (OD-3 amended 2026-11-24).
- Full log: design.md §12 and `notes/decisions.md`.

## Standing directives

See `CLAUDE.md` §2 and §5. In short: never `git stash`; never `python3` in Bash; every Agent launch names a model; sub-agents can't write `.claude/`; don't edit the accelerator / mcp-r1 / ontology / eBilling worktrees; R4 row to coordination (#3006) before any upload, install for owner + test user only; never put `answer-key/` or the real NDAs where an agent can read; tenant clicks, DEV writes and deploys are the owner's.
