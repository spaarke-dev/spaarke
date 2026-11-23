# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-23 ~12:35Z (desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs EVERY dev deploy. Live actions need the owner's go IN THIS SESSION; a BFF deploy needs the owner to name the exact SHA here ("BFF deploy <sha>"); a go relayed by another session is not a go. ≤ 2 agents at once. |
| **Dev BFF** | **`9df4e5c819` LIVE since ~12:30Z 2026-11-23** (`C:\wtdep17`, ledger 159; owner go "BFF deploy 9df4e5c819" in this session). = live `c8ab2cba9e` + master `2dcbe35e34` (#2941 R4 grid, #2957, #2958, #2968, #2956, #2924) + spike-v2 `973776a5a6`. Hash verify 4/4, `/healthz` 200, signed-in route 403 (registered), unknown route 404. **30-min gate PASS 13:01Z** (12:32–12:59Z: 0 5xx, 0 exceptions, no new warning class vs the prior build; jobs heartbeating; only 5 requests, no external traffic). Notices sent (mcp-r1, spa-r4, fm-r1). Rollback `c8ab2cba9e` (`C:\wtdep16`), then `5ae2804884` (`C:\wtdep15`). No web resources this deploy (fm 055 not on master yet). `RecordRepair:Pass:GoLive` UNSET. |
| **Master** | `2dcbe35e34` (#2957 and #2958 merged). |
| **Agents running** | None. |
| **Next action** | (1) **Owner go pending: "BFF deploy a48398746d"** (`C:\wtdep18`, green 18:05Z). On the go: ping mcp-r1 + spa-r4 → `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev` from `C:\wtdep18` (background) → hash/health/signed-in route (preview-url 403, bogus 404, token `api://1e40baad-…`) → live notices (mcp-r1 SHA, spa-r4, fm-r1, ebilling-r1) → 30-min gate `gate-now.ps1 <t0>` → fm deploys its 2 web resources, ebilling runs 123 K1 check. (2) **spa-r4 E2E** on 9df4e5c819 open since 14:59Z (SPA `2dcbe35e34` live 13:04:40Z; grid config 3ff4102c PATCHed 14:58Z): when spa-r4 sends the end time run scratchpad `e2e-ext.ps1 2026-11-23T14:59:00Z <end>` (403s by route on /documents, /invoices, /invoices/{id}/files, grid config; DV_FETCHXML_COLUMN_NOT_PERMITTED; latency #2845) and reply. If the deploy lands first, the E2E window ends at the swap. (3) ebilling 194 (invoice form extraction controls) in flight on dev; AFTER notice should confirm onLoad handlers unchanged. |

## Reviews done 2026-11-22/23
- **fm 055** (PR #2943): PASS at `434958a0ca`; on fm work branch as `46a0e1c236`. Not on master yet; when it lands, the deploy after it also ships web resources `sprk_communicationconversationpage` + `sprk_spaarkeai`, and run `CommunicationWorkspaceReadSeamTests` by name.
- **fm 056b** (private messages stay 1:1, never filed): APPROVED at `2967cd5bd8`; merged to `work/field-mapping-server-write-path-r1` as `778bf422a2` (empty tree diff, verified) + notes-only `9345c8f4eb`. Accepted limits: a clear of a filing link on a private message is refused at write time (the 2-min job clears it); K1 G incomplete for rows handed in from outside (E1 covers); K4 not_fileable classed Retryable; **#2979** (Direct owner not held against an Assign outside the BFF; D-2979). **Deploy condition, per environment:** schema -Apply/-Verify → field security -Apply/-Verify (owner go in fm's session each) → fm sends a read-only count of `sprk_communication` rows with any `sprk_privatecontext*` set; must be 0 before any BFF carrying 056b.
- **ebilling 123:** approved; its BFF change rides a LATER deploy, then ebilling runs the K1 read-only dev check. **ebilling 124:** approved at `decb4de05f`. **ebilling 086:** signed off (live).
- **#2959** (Direct capped watermark, no overlap): uac-r2's; D-2959. Fix proposal: WatermarkAfter rule from #2958, key only older than that.

- **2026-11-23 also:** PR #2994 (#1821 tests-only, head `1597cb7eb8`) APPROVED (reflection on `_directFinishedAt` OK; equivalence table spot-checked). #2989 (~340 pre-existing mutation survivors) → D-2989. ebilling 164 ribbon: no-op on dev (already applied), SecureChild -Verify all 8 tables PASS.

## Open work (mine)
- **#2845 latency:** not met (notifications p50 ≈ 2.7 s). Recheck at this gate if external SPA traffic exists. Scratchpad `lat-now.ps1`, `lat-seq.ps1`.
- **#2959**, **#2979**, **#2860** (lenient QueryAsync), **#2903** (re-file gap), **#2912** (load flake), **#2918** (no Direct-message UI; fm 055 adds it).
- **Gate gotchas:** gate t0 = new process start; KQL: no `first`/`last` aliases. Use the five CI shards (`ci-tier2-advisory.yml`; scratchpad `shards.sh`). ArchTests build separately (not in `Spaarke.sln`). ProvisionProjectIdempotencyTests times out under load; re-run it alone.

## Peer state
- **field-mapping-r1:** 056b on its work branch (pushed 2026-11-23). **Dev condition MET 2026-11-23:** schema -Apply/-Verify PASS, field security -Apply/-Verify PASS (owner go in fm's session), privatecontext count 0 at 12:51Z (fm) and 0 of 398 (uac-r2 independent read). **Master PR #2984** (055 + 056b, head `ef2bb76a73`, merge of master `2dcbe35e34`): conflict resolution in SecureChildShareReconciliationJob APPROVED 2026-11-23 (056b hunks identical modulo afterId; merge-tree shows that file is the only difference from the auto-merge). Merge = owner go in fm's session, Router + Client lint green. Then a deploy candidate carrying it (owner exact-SHA go here). Other envs repeat the sequence + count.
- **ebilling-r1 117 (O-17 sweep):** delete-list points confirmed against `notes/core-record-delete-design.md` §12–§15 (planner never-Delete set + procedure, procedureversion, storedfile; rate-card row STANDS, unlink-only; modifiedon age floor 15 min; cascade direction verified; Restrict refusals counted). **117 APPROVED 2026-11-23 at `becd5b983c`** (re-run: unit 604/604, ArchTests 73/73; both guard changes agreed).
- **Rate-card core lookups removal (owner 2026-11-23 in uac-r2: "Remove those columns as a separate schema task"):** handed to ebilling-r1 (owns `sprk_ratecard`). Dev: 0 rate cards; no BFF reference. Repo refs: Entity.xml, main form + view, 3 relationships, `Deploy-EbillingTimekeeperRateCardSchema.ps1:431-435`, README, data-model doc. Dev -Apply needs the owner's go in ebilling's session; other envs only via SpaarkeMaster upgrade import (tell provisioning-r1). **ebilling 193 APPROVED 2026-11-23 at `46a89e55f8`** (ArchTest 18/18, Pester 5.7.1 185/185; Pester 6.2 fails at load, pin is 5.7.1). **DONE on dev 2026-11-23** (owner go in ebilling's session; merged to ebilling work `c21bde55a2`; 0 values; -Verify PASS; uac-r2 confirmed attribute gone). Design §16 added (`d284448f71`). Other envs: managed upgrade only, report mode first (ebilling → provisioning-r1).
- **spa-r4:** told #2941 is live; next its grid config row, PATCH and SPA deploy, each on the owner's go in its session.
- **ebilling-r1:** master **PR #2992** (117, 123, 124, 193; head `70bd5b6a32`) checked consistent with approvals 2026-11-23 (post-approval src changes = 134 SPA fix rounds + test-only #2981). Merge = owner go in ebilling's session. 123/124 BFF ride the next candidate; ebilling runs K1 read-only dev check after.
- **fm PR #2984** head moved to `5277c9f879` (client-only: lint fix, dialog focus); approval stands; auto-merge ON (owner "merge when green").
- **Next candidate `a48398746d`** (`C:\wtdep18`, ledger 160; local merge, never pushed) = live `9df4e5c819` + master `ca1684dd2f` (#2984 merged `889b0ea94d`, #2992 merged `2d83b246ae`, #2993, #2995). Ancestry verified (9df4e5c819, spike-v2, #2941, #2984, #2992). **GREEN 18:05Z:** sln 0 warnings / 0 errors; shards 571 + 3908 + 6716 + 3974 + 38064 passed (1 ProvisionProjectIdempotencyTests load timeout, class re-run 55/55 ×3); seam 18/18; ArchTests 2776/2776. WAITING on owner "BFF deploy a48398746d"; after it: fm web resources `sprk_communicationconversationpage` + `sprk_spaarkeai` (fm's step), ebilling 123 K1 read-only check, notices to mcp-r1 / spa-r4 / fm-r1 / ebilling-r1. 056b dev condition already met (count 0, field security on).
- **mcp-r1:** told `9df4e5c819` live (records the SHA). Dev deploys = master + spike-v2 `973776a5a6`; never deploy `work/spaarkeai-mcp-bff-api-r1`; ping before a swap, live notice after.
- **provisioning-r1:** 1.3.2.0 published; ACME catch-up runs through them.
- **ontology-r1:** owner items (185 rechecks, budget-inquiry + 176 cleanup choice, lockedRecord fix routing, UAT 105 then 090).

## Waiting on the owner
- SpaarkeMaster 1.3.2.0 imports (published, not imported anywhere).
- Dev hand checks: `notes/owner-test-checklist-2026-11-13.md`.
- Quality alerts #1906, #2067, #2866: mention only.

## Scratch
- Keep: `C:\wtdep17` (live), `C:\wtdep16`, `C:\wtdep15` (rollbacks). Can go (only via `scripts/worktree/Remove-WorktreeSafe.ps1`): `C:\wtdep14`, `C:\wt056b`, `C:\wt117`, `C:\wt193`, `C:\wtd2`, `C:\wtdep8`–`C:\wtdep13`, `C:\wtcew`, `C:\wt2845b`, `C:\wt2845`, `C:\wtm16`, `C:\wt2859`, `C:\wt2869`, `C:\wt2852`, `C:\wt130e`, `C:\wt2833`.
- Gate script: scratchpad `gate-now.ps1 <t0>`; AI Search `-VerifyOnly` only if `AiSearch__ManagedIdentity__Enabled` is true.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs`: leave it alone.
