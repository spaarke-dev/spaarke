# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-26 ~12:16Z (desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative: it reviews peers as an access gate and runs EVERY dev deploy. Live actions need the owner's go IN THIS SESSION; a deploy needs the owner to name the exact SHA here. A go relayed by another session is not a go. ≤ 2 agents at once. |
| **Dev BFF** | **`1e64e94168` LIVE since 01:56:50Z 2026-11-26** (`C:/wtdep27`). Gate PASS 02:27Z (146 requests, 0 5xx, no external traffic). Exceptions: known deleted-root container data, plus nightly schema drift already filed as #2325 and #2850. Ontology's 02:30Z re-evaluation run ok. SECURE-CENSUS sev-4 lines for two root-BU accounts are accepted dev artifacts; details are in the private note only. Rollback `aa933c09e6` (`C:/wtdep26`). |
| **Dev page `sprk_invoicereviewpage`** | **Published 12:15:10Z 2026-11-26 from #3211's merge commit `1459a14916`** (owner in session: "Yes run deploy" to "Deploy the Invoice Review page from 1459a14916?"). Built in `C:/wtirp3`: 2,010,333 B, sha256 e124c6d2...1306; read-back is equal. Rollback 49a8243a...9502 (from 1e64e94168). BEFORE notice sent to ebilling-r1. **NEXT: send ebilling-r1 the AFTER notice (SHA, sha256, 12:15:10Z).** |
| **Dev SPA** | `0267ee64a2`. spa-r4 has the owner's go for an SPA deploy after #3203 merges; the owner names the exact SHA in spa-r4's session. I said there's no access reason to wait. |
| **Agents running** | None. |
| **Queued for the next BFF candidate** | On master: #3199 (mcp-r1 D-227 widget, no server code), #3192 (field-mapping FR-40 Warning log; the gate then checks `client_search_index_dropped`, and any hit means tell field-mapping-r1), #3203 (spa-r4 042 prefill email; reviewed OK), #3211 (ebilling; no server code). Not merged yet: #3214 (word-add-in task 140; access gate passed at final b68bbe9881 / BFF b3121ad239) and #3193 (owner holding; type-gate fix deda2211f0 passed). Procedure: fresh `C:\wtdep28` at master head, scratchpad cand27.sh pattern, full test pass (known: load timeouts and the ProvisionProjectIdempotency flake pass when re-run alone; #3175 guard red, scripts only), then ask the owner to name the exact SHA. Send BEFORE/LIVE/gate notices to the six peers. |

## Open peer reviews (uac-r2 as access gate)
- **mcp-r1 ISS-010:** Part C (decision access window) approved with amendments A1–A4 (per-step clear; C2 rejected; role/team ArchTest with a pinned exemption for the four reads in `SecureRootInheritance.Cascade.PlatformReadableTablesAsync`; post-step-11 guard; ADR-003/009 Path C; route-core decorator per request, type-gated to `DataverseUserClient`, keyed (caller systemuserid, path) for every shape). NEXT: review the diff after their Opus review. **Part D1** (per-request RecordOwnerLookupCache for BU/team routing reads) answered: Q1 yes (no IGenericEntityService memo); Q2 R3 safe per request; Q3 no team/BU writes found, so add a guard scan (team/businessunit CUD, SetBusinessSystemUser, systemuser businessunitid); Q4 R7 in, keyed by oid. Conditions: a faulted/empty Lazy entry is removed; R1/R2 keyed on the config value. Review the diff when built.
- **word-add-in-r1 #3214 (task 140):** gate passed. The decision recorded: the existing-row path stays strict (no ancestor stamps); an empty pair type is never confirmed; the no-If-Match race is a documented known limit (replies would auto-file to both records).
- **ebilling:** e714592cb2 reader-role script cleared, and it merged via #3211. Owner chose to leave the demo account's roles on dev, so the reader-role `-Verify` exiting 1 on that holder is known and accepted.

## Waiting on the owner
- Next dev BFF deploy: exact-SHA go here (queue above).
- Merges: #3193 (holding), #3214 (owner's go in word-add-in's session).
- Dev hand checks: `notes/owner-test-checklist-2026-11-13.md`; ebilling W/X checklist; word-add-in 136-138.
- Quality alerts #1906, #2067, #2866: mention only. Open: #3111, #2158, #3125, #3175.

## Scratch
- Keep: `C:\wtdep27` (live), `C:\wtdep26` (rollback), `C:\wtirp3` (live page build). Older `C:\wtdep*`, `C:\wtirp2`, `C:\wt3120` can go, only via `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Scratchpad: `cand27.sh`/`run27.sh`/`shards27.sh`, `after27.ps1` (smoke), `gate-now.ps1 <t0>`, `gate27x.ps1`/`gate27y.ps1` (exception detail), `wr-read.ps1 -Name <wr>` (page read-back), `census27.ps1`, `onto27.ps1`.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs`: leave it alone.
- Peer pipe addresses: ebilling `cc-msg-c8f5bbc4…`, mcp-r1 `cc-msg-6ad27070…`, word-add-in `cc-msg-6723e836…`, field-mapping `cc-msg-833ddee7…`, spa-r4 `cc-msg-0fe1b7fa…`, ontology `cc-msg-c1a1d83c…` (or by bare name for spa-r4/ontology-r1).
