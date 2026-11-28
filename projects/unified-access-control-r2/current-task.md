# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-28 ~02:55Z (desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative: it reviews peers as an access gate and runs EVERY dev deploy. Live actions need the owner's go IN THIS SESSION; a deploy needs the owner to name the exact SHA here. A go relayed by another session is not a go. ≤ 2 agents at once. |
| **Active work** | None in flight. #3317 fix MERGED (PR #3324, squash `bc22f35481`, owner go "Yes merge" 2026-11-28; office-scope job cancelled at its 20-min cap = known stall); decision recorded in `notes/decisions.md`. Not deployed. |
| **Dev BFF (CURRENT)** | `af8a5547ef` (= d9595dd98e + spike tools), live ~20:57Z 2026-11-27 from `C:/wtdep33`, DLL SHA-256 CE2229C1… = local, 30-min gate PASS 21:28Z. Rollback `d9595dd98e` (`C:/wtdep32`). |
| **Dev SPA** | c438119bca. Invoice Review page: sha256 00fb8d16…ba1d from c55b9812d8 (21:44Z). |
| **Agents running** | None. |

## Next dev BFF candidate (needs the owner's exact-SHA go here)
- Cleared server inputs on master, not on dev: **7f48825d90** (#3242 MCP Host/Origin filter, LogOnly default — still CHECK its src/server diff and that LogOnly is the default when the setting is absent), **8b38e97667** (#3316 ISS-010 D1 owner-lookup cache, approved; merged patch-id 179cfb35ec86 = reviewed), **bf7d6a969f** (#3308 ClosesTodo fail-closed, approved), **4f9e371445** (#3311 spa-r4 044+047, 047 approved). Plus **bc22f35481** (#3324, #3317 Read|AppendTo on comm create-task) and **a7712744d4** (intake #3253 merge; all items approved incl. late d83e269b5d = stage bookkeeping only). #3322 (spa-r4 045, approved) if merged in time.
- Live is a spike tip: before a candidate, ask specialist-legal-agents-r1 to re-merge master into `spike/specialist-agents-phase0`; verify the spike patch-id equals the reviewed spike (48c5630a3f21).
- Before: `issecured.ps1` (064/065 condition). After: smoke (`after30.ps1`), `livecheck.ps1`, 30-min gate (`gate-now.ps1 <t0>`, `gatesdap.ps1`), BEFORE/LIVE/GATE notices to the 8 (CLAUDE-session summary lists pipes). ontology-r1 runs its zz- re-check of 237 only AFTER my GATE notice (agreed).

## Open peer reviews / decisions given (uac-r2 as access gate)
- **intake-pipeline-r1:** PR #3253 MERGED at a7712744d4 (2026-11-28 02:41Z); access diff fully approved (items 1-7 + d83e269b5d). Timing difference unknown-vs-unreadable reply parent = accepted K-class limit. They want ONLY the LIVE notice (no BEFORE/GATE) when a candidate with a7712744d4 is on dev; their 3 follow-ups (AgreementEditPlan Binding seed, engine image digest, completion-queue setting with 014) wait on it, each on the owner's OK in their session.
- **mcp-r2 task 036** (5e38b387ef): APPROVED (D-3181 event fix + spaarke_create_task / spaarke_complete_task). Until it merges, #3181 is live on master.
- **word-add-in-r1 task 144** (email filing alignment design, 63e036b5dd / §5b 9639d8364c): I first recommended B (derive). After fm-r1's A with the mirror INSIDE RecordOwnershipResolver.ReparentAsync under its lock before AfterRefileAsync, I said A is acceptable from the access side with conditions: all-or-nothing (failed mirror = unfinished re-file), lock order C→documents through the document re-file core, copy at create + safety-net pass, contact-plane veto (Restricted C hides its documents from M's contacts; stamp sprk_relatedcommunication on every child; must-fire test), R1 derived-slot Read checks. Owner decides; I gate the code. D-218 superseded under either option.
- **field-mapping-r1 task 067** (work/field-mapping-server-write-path-r1 @8f8d96bfb3 + task/fm-067c coming): gate C1–C7 binding (64b8b9081e). 2026-11-28 agreed: the 067 list governs SOURCE reads only (targets stay refused via IsCopyInvariantColumn → RefuseAppOnlyWrite on create/push/Repair; must-fire on all 3 surfaces). Source list = C3 + 4 sprk_document cols + identitycollision prefix + sprk_invoice.sprk_vendororg, sprk_invoice.sprk_firmhold, systemuser.sprk_primarycontact (must-fire on vendororg + primarycontact as sources). Totals + reviewdetail OFF the source list: approved (caller already reads them). Send lift: probe as caller first; no Read → send proceeds without mapped content, no app read; packet must assert value absent from message + response. ParentLineage.cs:1181-1183 stale comment fixed in 067c. C1/C2/C3 unchanged unless owner answers fm-r1 + I review. Await packet. Pre-existing gap found by fm-r1: the PUSH (writes as caller) does not refuse ParentLineage typed-writer TARGET columns (create/Repair do). I chose (a): its own task/PR after 067, same predicate, skipped+reported, must-fire even with Update+FLS (firmhold, vendororg, totalamount), must-not-fire ordinary column; fm-r1 files the issue and cites it in the 067 packet.
- **mcp-r2 ADR-052 amendment:** PROPOSED until the owner's security sign-off.

## Waiting on the owner
- **#3323**: D-3181 scope for other AppendTo-only name copies (S2 thread re-file name copy in ThreadResolver; S3 send ancestor stamp; S4 platform lookup display names, not verified live; plus the playbook CreateTask node note). Owner ruling needed before any fix.
- Next dev BFF deploy: exact-SHA go here (see candidate above).
- ADR-052 amendment security sign-off (mcp-r2).
- Dev hand checks: `notes/owner-test-checklist-2026-11-13.md`; ebilling W/X checklist; word-add-in 136-138.
- Quality alerts #1906, #2067, #2866: mention only.

## Scratch
- Keep: `C:\wtdep31`, `C:\wtdep32` (rollback), `C:\wtdep33` (live), `C:\wtirp34`. Older `C:\wtdep8..30`, `C:\wtirp*`, `C:\wt3120`, `C:\wt3317` can go, only via `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Scratchpad (session dir): `livecheck.ps1 -LocalDll <deploy/api-publish/Sprk.Bff.Api.dll>`, `after30.ps1`, `gate-now.ps1 <t0>`, `gate5xx.ps1 <t0>`, `gatesdap.ps1 <t0>`, `issecured.ps1`, `wrhash.ps1 -Name <wr> -LocalFile <f>`, `fm065*.ps1`.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs`: leave it alone.
- Peer pipes: ebilling `cc-msg-c8f5bbc4…`, mcp-r1 `cc-msg-6ad27070…`, word-add-in `cc-msg-6723e836…`, field-mapping `cc-msg-833ddee7…`, ontology `cc-msg-c1a1d83c…`, intake `cc-msg-db5f17bc…`, mcp-r2 `cc-msg-fb73a77b…`, search `cc-msg-a716f66a…`; spa-r4 = session `local_32df59f7-3d57-49c9-9808-75fb87513600`; specialist-legal-agents-r1 by name.
