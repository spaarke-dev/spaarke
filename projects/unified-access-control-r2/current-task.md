# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-14 ~07:30Z (checkpoint #140, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Deploy IN PROGRESS** | Owner said "You can proceed with deploy when ready" (= go for the client parts). Base: **master `bd43110169`** (after spa-r4 #2818). **(a) Client parts: background agent running** in `C:\wtm99` (detached at bd43110169): `sprk_spaarkeai`, `sprk_eventdetailsidepane.html`, `sprk_creatematterwizard`, RegardingResolver PCF, CommunicationConnections PCF → dev, with read-back hashes. If compacted, wait for its notification; do not relaunch. **(b) BFF:** `C:\wtdep7` holds local merge **`0173e1486e`** = bd43110169 + spike `d7369fecf1` (ledger 152→153, never pushed); Release build clean, route ArchTests 94/0. **Waiting on the owner's "go BFF deploy bd43110169"** (asked; not yet answered). Rollback = the current dev build b100effac3 (rollback snapshot dir from the last deploy: `C:\wtdep7\deploy\rollback\wwwroot-20261113T194751Z`; Deploy-BffApi makes a new one). Before the swap: ping **mcp-r1** (owner may be running Phase 0 batch D tests; give them a moment) and **spa-r4**; after the gate send "BFF live" to spa-r4 (it holds its SPA deploy, rollback b083d62183) and notices to mcp-r1 and fm. Deploy command: project CLAUDE.md §5. 30-min gate script pattern: scratchpad `gate-b100.ps1` (App Insights `spe-insights-dev-67e2xz`). |
| **Client parts DONE** | All 5 deployed to dev from bd43110169; served hashes match the builds (RegardingResolver 1.6.3, CommunicationConnections 1.7.3). The p4a re-scan is clean. Master version sync: **PR #2828** (branch `chore/pcf-version-sync-rr163-cc173`, from `C:\wtm99`; CI pending). Merge it once Router AND Client lint are green, then remove `C:\wtm99`. |
| **Not in this BFF** | #2799 (PR #2827), ebilling #2822 (approved by uac-r2, waits on ontology-r1 + owner merge). Both ride the NEXT deploy. |
| **#2799** | **PR #2827 open** (branch `fix/2799-invoice-family-field-lock`, worktree `C:\wt2799`, head bfb7b721dd; rebased on master cfa97d718b; merge-tree clean vs bd43110169). Done: helper + C1–C7, lock script (`Set-InvoiceFamilyFieldSecurity.ps1`, `-Target InvoiceFamilyLinks`), table-scoped detector, ArchTests, docs I-24 + deployment guide, 2 adversarial reviews + all fixes (R2-1: person must be enabled with accessmode 0/4). Gates: ArchTests 2554/0, integration 212/0, Spe 353/0, Core 64/0, unit 48,499 + 493 load-timeouts all passing on rerun; publish +27.8 KB; no vulnerable packages. Follow-up filed: **#2826** (access mode in all G5 checks). **Next:** CI green (Router AND Client lint) → squash merge `--match-head-commit` (the app's PR monitor; no gh polling) → next dev BFF deploy (owner SHA go) → p4a re-scan (only `sprk_creatematterwizard` was flagged; the client deploy above replaces it) → `-Apply -BffWritesInvoiceFamilyLinksAsApp` (owner go) → `-Verify`. Backup branch `backup/2799-pre-rebase` (delete after merge). |
| **Next action** | Wait for the client-deploy agent's report → verify its read-backs → if the owner has said "go BFF deploy bd43110169": ping mcp-r1 + spa-r4, deploy BFF from `C:\wtdep7`, read-back, 30-min gate, "BFF live" to spa-r4. Meanwhile watch PR #2827. |

## Peer state
- **ebilling-r1:** #2822 (084/084c–g, 084d-2a/2b/5a/5b, 095e, 095f part 1) **APPROVED by uac-r2** at head 293d87de72 (CI 47 pass / 0 fail). Includes my F4 fix (Set firm = Write | Append). 095g design approved with R1 (Append on record), R2 (add notice) and the Q2/Q3 conditions (inactive record: no promise of access, theory flag-only; Law Firm type by code, 503 if missing); build started, PR comes to me. `sprk_createinvoicewizard` redeployed on dev from cfa97d718b (scan clear). Insights slot-2 meaning filed **#2816**.
- **field-mapping-r1:** #2790 (4f9e77e9c0) and #2819 / 045g (b76276a314) on master. Runs no deploys; uac-r2 owns the five client parts (045f order: client parts before or with the BFF carrying #2790). 052b repair pass stays off until `RecordRepair:Pass:GoLive` (needs #2771).
- **spa-r4:** #2818 merged (bd43110169); holding SPA deploy for my "BFF live"; owner wants the E2E retest.
- **mcp-r1:** spike still `d7369fecf1`; never deploy `work/spaarkeai-mcp-bff-api-r1`.
- **ontology-r1:** second gate on #2822.

## Waiting on the owner
- **"go BFF deploy bd43110169"** (asked 2026-11-14).
- D-2 step 3 (Direct setup + `DirectMessage:*` settings); #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope; #2799 `-Apply` (after its BFF is deployed).
- Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Open threads (unchanged, detail in handoff history)
- 191i/191j remaining steps; #2761 fixed on dev; #2764 decision before 191c writes.

## Scratch
- Worktrees: `C:\wtdep7` (BFF deploy merge; keep until after this deploy), `C:\wtm99` (client builds; remove with `scripts/worktree/Remove-WorktreeSafe.ps1` after the deploy), `C:\wt2799` (#2799; remove after merge), `C:\wtdep4`, `C:\wt197a`, `C:\wt197b`, `C:\wt198s`.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
