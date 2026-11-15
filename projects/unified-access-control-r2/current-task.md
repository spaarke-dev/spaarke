# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-15 (checkpoint #142, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev BFF** | **`5a9b687a0b`** = master `2747977f47` + spike `d7369fecf1` (local merge in `C:\wtdep8`, ledger 153→154, never pushed). Live 15:20Z 2026-11-14 on the owner's "go BFF deploy 2747977f47". Read-back OK; 30-min gate PASSED (53 req, 0 5xx). The 15 BadImageFormatException in the 15:20:00–33Z swap window are a known direct-deploy artifact (evidence on **#2549**). Rollback = `0173e1486e` (bd43110169 + spike). Client parts: the Console already carries #2825 via its pipeline; `sprk_recordprocedures` was left to ebilling task 112. |
| **#2799** | DONE: merged `2747977f47`, deployed, **lock APPLIED on dev** (owner's go; 24 columns, 6 tables published, masked window ≤ 47 s), **-Verify PASS**. The verify's stale metadata reads were fixed by **PR #2834, MERGED `8726dbe95a`** (engine sends `Consistency: Strong`). The 32 other scripts are **#2833**: ebilling did its `scripts/ebilling` slice (e36a1e54c4 on work/ebilling-r1, lands with its next PR); the uac-r2 scripts and the source-scan guard are still open (mine, no owner decision). Follow-up **#2826** (access mode in G5 checks) is still open. |
| **In review (peer)** | **ebilling task 093** (firm file routes, external plane). My ruling **B** (2026-11-15): a systemuser caller is also asked Read on the invoice ROW via CallerRecordAccessProbe, intersected with the root decision; None → uniform 404, unknown → 503, no inherit fallback; contacts unchanged. Fixed head **a95f7e7297** on work/ebilling-r1 (fix round 56bc29d9a4..b3e8de5ac6). Review: the gate diff + direct callers in `ExternalInvoiceSubmissionAuthorizationFilter.DecideInvoiceFilesAsync`; confirm `GetPrincipalRightsAppOnlyOrUnknownAsync` (app-only, binder systemuserid) is the right variant, since OBO is barred on /api/v1/external by ExternalPlaneNoOboGuardTests; check the 188 firm-file section shape; StoredFileService.ReadFirmVisibleRowsForInvoiceAsync filter. Reply PASS/findings to ebilling-r1 (private; not committed anywhere). |
| **093 review DONE** | PASS sent to ebilling-r1 at a95f7e7297 (K1 404 timing, K2 header compare optional). **HOLD (spa-r4, 2026-11-15): do NOT swap a BFF containing 093 until spa-r4 confirms its 021 SPA is merged and ready**; then BFF swap first, 021 SPA straight after. |
| **Next action** | Worktree clean-up running (wt2799, wt2799c, wtdep7, backup branch). Then the uac-r2 slice of #2833 (add `Consistency: Strong` to the remaining uac-r2 scripts + a source-scan guard) in a fresh worktree. |

## Peer state
- **ebilling-r1:** #2822 and #2830 (095g) are on master (`acc96e1927`), but 095g is NOT on dev yet. The Law Firm type code is seeded on dev (owner's go in ebilling). Task 112 (`sprk_recordprocedures` + forms) needs the owner's go in ebilling. Deploy order it asked for: spa-r4's 021 built first, then my BFF swap, then the SPA. #2615 (the spike) overlaps `RouteAuthorizationGuardTests.Ledger.cs`: re-resolve the ledger count on the next deploy merge.
- **word-add-in-r1:** #2829 (095g Outlook pane) PASSED my review at bebc8e047c; held until 095g is on dev + the owner's go. It asked the owner to re-run "To Do create" on dev.
- **spa-r4:** the SPA is on dev, built from 2747977f47 (13:19Z); the owner E2E retest runs on the current BFF. SPA rollback 2ef4a17915. Before any BFF restart during its E2E: ask for its "clear".
- **mcp-r1:** spike still `d7369fecf1` (never deploy `work/spaarkeai-mcp-bff-api-r1`). Task 010 PASSED and closed. Ping it before every swap and send the live notice (SHA, healthz, anonymous /mcp 401).
- **ontology-r1:** #2825 on dev. Owner check: "Send budget inquiry" opens with "Budget inquiry: {number} {name}" + the approved message. #2824/#2831 (wizards) are on master but not deployed; ask ontology about the client parts before the next deploy.
- **field-mapping-r1:** #2790/#2819 on dev; 052b repair pass off (`RecordRepair:Pass:GoLive` unset). The local session was unreachable on the last notice (queued).

## Waiting on the owner
- **Next dev BFF SHA go** (master is 24+ commits ahead of dev: 095g #2830, ontology #2824/#2831; + spike).
- Owner checks on dev: SPA E2E retest; To Do create (Office); Send budget inquiry text.
- D-2 step 3 (Direct setup + `DirectMessage:*` settings); #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope.
- Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Scratch
- Worktrees: `C:\wtdep8` (current dev BFF merge; keep as the rollback source until the next deploy), `C:\wtdep7` (previous; can go), `C:\wt2799`, `C:\wt2799c` (both merged; can go), `C:\wtdep4`, `C:\wt197a`, `C:\wt197b`, `C:\wt198s`.
- 30-min gate script: scratchpad `gate-0173.ps1 <t0 ISO>` (App Insights `spe-insights-dev-67e2xz`).
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
