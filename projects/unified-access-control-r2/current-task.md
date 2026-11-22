# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-22 ~21:00Z (desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs EVERY dev deploy. Live actions need the owner's go IN THIS SESSION; a BFF deploy needs the owner to name the exact SHA here ("BFF deploy <sha>"); a go relayed by another session is not a go. ≤ 2 agents at once. |
| **Dev BFF** | **`c8ab2cba9e` LIVE since 20:43Z 2026-11-20** (`C:\wtdep16`, ledger 158, 30-min gate PASS). Rollback `5ae2804884` (`C:\wtdep15`), then `326781c50f` (`C:\wtdep14`). `RecordRepair:Pass:GoLive` UNSET. Direct-message settings set 2026-11-20; D-2 Direct chain complete on dev. |
| **Master** | `6a907a5cd4` (2026-11-22; includes #2941 `9461dc0bb3` and spa-r4 #2971). |
| **Agents running** | None. |
| **Next action** | **Next dev BFF deploy** = master (after fm 055 lands on master, or earlier if spa-r4 is ready first) + mcp spike-v2 `973776a5a6`. Build in a fresh short-path worktree; run the five CI shards + ArchTests, **including `CommunicationWorkspaceReadSeamTests` by name** (fm's 055 param-count pin fix comes as its own commit before their master PR); ping mcp-r1 + spa-r4; ask the owner for the exact SHA; deploy via `/bff-deploy`, then fm's web resources `sprk_communicationconversationpage` + `sprk_spaarkeai`; 30-min gate; live notice to mcp-r1, spa-r4, fm-r1. **Must contain #2941** (spa-r4's record Documents grid; the SPA on master shows "This list could not be loaded" until it is live). After the live notice spa-r4 writes the grid-config row, PATCHes, deploys the SPA (its own owner gos). |

## Reviews done 2026-11-22
- **fm 055** (PR #2943): PASS at `434958a0ca`; merged into fm work branch as `46a0e1c236` (same tree, verified). Waiting for fm's work branch → master (owner).
- **PR #2957** (#2745/#2746 Direct share pass): APPROVE at `4ff2260901`. **PR #2958** (#2750 held-window drain): APPROVE at `b351e8ffb2` (its #2456 office-addins flake explained). Owner gives each merge go.
- **#2959** (Direct capped watermark, no overlap; can also delay a revoke): owned by uac-r2; in `notes/defer-issues.md` (D-2959, commit `58ca067f58`). Fix proposal: WatermarkAfter rule from #2958 (min(horizon, startedAt − overlap)), key only older than that. Asked the #1821 session to correct the issue's "fails closed" wording.
- **ebilling 124** (retire Finance:ProcedureLibraryContainerId, ExternalVisibility step 4, #2151 declared home): APPROVED at `decb4de05f`. They merge to work/ebilling-r1, close #1967, tell provisioning-r1.
- **ebilling 086** (Stored File as secure child): SIGNED OFF (live in c8ab2cba9e). Open, not mine: owner criterion 6 (Verify stands in for missing Apply record); `Ebilling:StoredFileReconcileEnabled` stays false until F4 + DEF-044 (their rule) AND the owner's go (app-setting restart). The 4 NFR-05 census findings = known root-BU dev artifact (task-144 clause 1).
- **fm 056a** (private messages in a record's context): design APPROVED at `51419e7768` with C1–C6 + owner answers; three wording fixes due with the first 056b commit (sender-side refusal incl. unknown id; sender checks before recipient; guard Unverifiable → 503). 056b adds the `MembershipFieldDiscoveryService.DiscoverLookupsTargetingAsync` exclusion via `PrivateContextColumns.All`. **056b access review comes to me (branch + SHA after their two passes); its schema script + field security need the owner's go per environment.**

## Open work (mine)
- **#2845 latency:** not met (notifications p50 ≈ 2.7 s). Recheck at the next gate with external SPA traffic. Scratchpad `lat-now.ps1`, `lat-seq.ps1`.
- **#2959** (above), **#2860** (lenient QueryAsync), **#2903** (re-file gap), **#2912** (load flake), **#2918** (no Direct-message UI; fm 055 adds it).
- **Gate gotchas:** gate t0 = new process start; KQL: no `first`/`last` aliases. Use the five CI shards (`ci-tier2-advisory.yml`; scratchpad `shards.sh`).

## Peer state
- **field-mapping-r1:** 055 on its work branch; 056b in build. Owner asked (in fm's session) where the exact-SHA go belongs; my position: the owner names it in BOTH sessions, I deploy only on the go here.
- **spa-r4:** building R4 task 024 (SPA only); will say when ready for the SPA deploy.
- **ebilling-r1:** 124 approved; 096 BFF change is a future deploy candidate.
- **mcp-r1:** dev deploys = master + spike-v2 `973776a5a6`; never deploy `work/spaarkeai-mcp-bff-api-r1`; ping before a swap, live notice after.
- **provisioning-r1:** told 1.3.2.0 published; ACME catch-up runs through them.
- **ontology-r1:** owner items (185 rechecks, budget-inquiry + 176 cleanup choice, lockedRecord fix routing, UAT 105 then 090).

## Waiting on the owner
- Exact SHA for the next BFF deploy (above). Merge gos for #2957 and #2958.
- SpaarkeMaster 1.3.2.0 imports (published, not imported anywhere).
- Dev hand checks: `notes/owner-test-checklist-2026-11-13.md`.
- Quality alerts #1906, #2067, #2866: mention only.

## Scratch
- Keep: `C:\wtdep16` (live), `C:\wtdep15`, `C:\wtdep14` (rollbacks). Can go (only via `scripts/worktree/Remove-WorktreeSafe.ps1`): `C:\wtd2`, `C:\wtdep8`–`C:\wtdep13`, `C:\wtcew`, `C:\wt2845b`, `C:\wt2845`, `C:\wtm16`, `C:\wt2859`, `C:\wt2869`, `C:\wt2852`, `C:\wt130e`, `C:\wt2833`.
- Gate script: scratchpad `gate-now.ps1 <t0>`; AI Search `-VerifyOnly` only if `AiSearch__ManagedIdentity__Enabled` is true.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs`: leave it alone.
