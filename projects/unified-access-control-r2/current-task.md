# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-15 19:10Z (checkpoint #143, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev BFF** | **`aa7d6345f3`** = master `c1eb6e4a8e` + spike `d7369fecf1` (local merge in `C:\wtdep9`, ledger 154→155, never pushed). Live 18:32Z 2026-11-15 on the owner's "go BFF c1eb6e4a8e". Carries 095g (#2830) and #2839 (093/113/084h-1). Read-back: 4 critical files SHA-256 match, healthz 200, CORS OK, anonymous /mcp and /api/mcp/spike 401. **30-min gate PASSED** (42 req, 0 5xx; external 29/29 OK incl. invoice file content 200). |
| **Dev SPA** | spa-r4's R4 021 deployed after the BFF (run 43747335450, from `c1eb6e4a8e`). **Rollback is PAIRED: BFF `5a9b687a0b` (`C:\wtdep8`) together with SPA `04dbd8df05`, never one alone.** |
| **#2833** | CLOSED. uac-r2 slice merged in **#2838** (`a682d37850`): 19 scripts send `Consistency: Strong`; ArchTest `ScriptMetadataStrongConsistencyGuardTests` (follows `using module`/`Import-Module` to a sibling module; baseline EMPTY). ebilling slice merged with #2839. |
| **Filed from the gate** | **#2844** (ebilling's): `InvoiceIndexingJobHandler` faults — `InvoiceIndexDocument.cs:38/52/132` still selects `sprk_invoice.sprk_document`, removed by task 094; invoice re-index fails on every invoice change. Pre-existing (not this deploy). Sent to ebilling-r1. |
| **Next action** | Nothing running. Wait for: ebilling's #2844 fix; spa-r4's owner E2E retest result; owner checks below. Follow-ups still open: **#2826** (access mode in G5 checks), **#2549** (deploy-window exceptions). |

## Peer state
- **ebilling-r1:** 095g + 093/113/084h-1 are on dev. Owes the #2844 fix. Task 112 (`sprk_recordprocedures` + forms) needs the owner's go in ebilling.
- **word-add-in-r1:** #2829 (095g Outlook pane) PASSED my review at bebc8e047c; it was held until 095g is on dev, which is now true. Merge still needs the owner's go. It asked the owner to re-run "To Do create" on dev.
- **spa-r4:** 021 SPA on dev; its owner is retesting. Before any BFF restart during its E2E: ask for its "clear".
- **mcp-r1:** spike still `d7369fecf1` (never deploy `work/spaarkeai-mcp-bff-api-r1`). Live notice sent for aa7d6345f3. Ping it before every swap. It is merging master (ontology 191 into the MCP complete tool) locally on its branch.
- **ontology-r1:** #2825 on dev; #2824/#2831 (wizards) are client-only. Owner check: "Send budget inquiry" opens with "Budget inquiry: {number} {name}".
- **field-mapping-r1:** #2790/#2819 on dev; 052b repair pass off (`RecordRepair:Pass:GoLive` unset).

## Waiting on the owner
- Owner checks on dev: SPA E2E retest (spa-r4); To Do create (Office); Send budget inquiry text.
- word-add-in #2829 merge go; ebilling task 112 go (in ebilling's session).
- D-2 step 3 (Direct setup + `DirectMessage:*` settings); #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope.
- Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Scratch
- Worktrees: `C:\wtdep9` (current dev BFF merge; keep), `C:\wtdep8` (paired rollback source; keep until the next deploy), `C:\wt2833` (merged; can go), `C:\wtdep4`, `C:\wt197a`, `C:\wt197b`, `C:\wt198s`.
- 30-min gate script: scratchpad `gate-aa7d.ps1 <t0 ISO>` (App Insights `spe-insights-dev-67e2xz`).
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
