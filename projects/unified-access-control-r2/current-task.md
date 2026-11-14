# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-14 ~12:50Z (checkpoint #141, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev deploy DONE (2026-11-14)** | Client parts (5/5, hashes match) then the BFF **`0173e1486e`** = master bd43110169 + spike d7369fecf1, live ~12:44Z on the owner's "go BFF deploy bd43110169". Deploy script: 4 critical files SHA-256 verified; healthz/dataverse/ping 200; anon /mcp GET+POST, /api/me, /api/finance/firms 401. **30-min gate PASSED** (12:43–13:14Z: 41 req, 0 5xx, 0 exceptions, no OWNERSHIP-REFUSED). "BFF live" sent to spa-r4 (it deploys its SPA next). mcp-r1 and fm were already told it is live. Rollback = b100effac3 (Deploy-BffApi snapshot under `C:\wtdep7\deploy\rollback\`). |
| **Master version sync** | PR #2828 MERGED (`2626b691cd`). `C:\wtm99` removed. Issue #2799 closed by the merge. |
| **Next BFF deploy (needs a new owner SHA go)** | master now has #2799 (`2747977f47`) and ebilling #2822 (`7da54c00a3`). ebilling #2830 (095g, head d825808b7d) is approved by uac-r2, awaiting ontology-r1 + owner merge. word-add-in #2829 (095g pane) passed uac-r2 review at bebc8e047c; it is held until 095g is on dev. The type code was seeded on dev by ebilling-r1 (owner go there; LAWFIRM + Morrison Foerster typed). |
| **#2799** | **MERGED** as `2747977f47` (PR #2827, squash, head bfb7b721dd). Before merging, the merge with master 7da54c00a3 (incl. #2822) built clean and ArchTests passed 2619/0. Follow-up filed: **#2826**. **Next:** next dev BFF deploy (owner SHA go) → p4a re-scan → `Set-InvoiceFamilyFieldSecurity.ps1 -BffWritesInvoiceFamilyLinksAsApp -Apply` (owner go) → `-Verify`. Then remove `C:\wt2799` and delete the backup branch `backup/2799-pre-rebase`. |
| **Next action** | Ask the owner for the next BFF SHA go (master with #2799 + #2822, + spike), then the #2799 lock `-Apply` go. Watch for #2830 (095g) merging. |

## Peer state
- **ebilling-r1:** #2822 (084/084c–g, 084d-2a/2b/5a/5b, 095e, 095f part 1) **APPROVED by uac-r2** at head 293d87de72 (CI 47 pass / 0 fail). Includes my F4 fix (Set firm = Write | Append). 095g design approved with R1 (Append on record), R2 (add notice) and the Q2/Q3 conditions (inactive record: no promise of access, theory flag-only; Law Firm type by code, 503 if missing); build started, PR comes to me. `sprk_createinvoicewizard` redeployed on dev from cfa97d718b (scan clear). Insights slot-2 meaning filed **#2816**.
- **field-mapping-r1:** #2790 (4f9e77e9c0) and #2819 / 045g (b76276a314) on master. Runs no deploys; uac-r2 owns the five client parts (045f order: client parts before or with the BFF carrying #2790). 052b repair pass stays off until `RecordRepair:Pass:GoLive` (needs #2771).
- **spa-r4:** #2818 merged (bd43110169); holding SPA deploy for my "BFF live"; owner wants the E2E retest.
- **mcp-r1:** spike still `d7369fecf1`; never deploy `work/spaarkeai-mcp-bff-api-r1`.
- **ontology-r1:** second gate on #2822.

## Waiting on the owner
- **The next BFF SHA go** (master with #2799 + #2822, + the spike), then the #2799 lock `-Apply` go.
- D-2 step 3 (Direct setup + `DirectMessage:*` settings); #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope; #2799 `-Apply` (after its BFF is deployed).
- Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Open threads (unchanged, detail in handoff history)
- 191i/191j remaining steps; #2761 fixed on dev; #2764 decision before 191c writes.

## Scratch
- Worktrees: `C:\wtdep7` (BFF deploy merge; keep until after this deploy), `C:\wtm99` (client builds; remove with `scripts/worktree/Remove-WorktreeSafe.ps1` after the deploy), `C:\wt2799` (#2799; remove after merge), `C:\wtdep4`, `C:\wt197a`, `C:\wt197b`, `C:\wt198s`.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
