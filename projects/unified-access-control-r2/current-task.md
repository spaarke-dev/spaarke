# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-07 ~04:20Z (checkpoint #100: 046c applied and the watch is running; the deploy batch is planned; the MCP spike rides along; #2292 merged; 164a, 044 r3 and 049b approved).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Running** | The 046c watch: background bash `watch046c_uac.sh` (scratchpad) checks every 30 min until **05:40Z**. When it ends, read its output, send the result to fm, then put the deploy batch to the owner. |
| **Dev state** | BFF = **20669df6a9** (since 2026-11-06 16:44:51Z) + the `sprk_spaarkeai` bundle at that SHA. G-8 ON. 226 settings. **046c applied** (by fm, with the owner's go in fm's session): G17 at 03:33:12Z (3 Core User AppendTo raises), R3 at 03:36:46Z (80 Basic User removals; Basic = Read only), both -Verify PASS. Baseline before: 0×403, 0×5xx, 0 privilege faults, census 4. Rollback: `Set-FeatureRolePrivileges.ps1 -Restore -Gap R3 -Apply` (fm runs it, after telling me). A background identity that faults gets its own role, not a restore. |
| **Next action** | 1. Watch result at 05:40Z. 2. Ask the owner for the batch deploy go (plan: scratchpad `deploy-batch-plan.md`). 3. Deploy: pre-checks, rollback snapshot, then BFF (master + MCP spike commit), then `sprk_recordfamily`, then `sprk_fieldmapping_push` v1.3.0. Then the gate, the spike gate, notices, one membership reconciliation back-fill, and a junction row count. fm runs the live UI checks (a) to (d). |

## The deploy batch (owner 2026-11-07: batch #2292 with 044)
- Master: #2292 (`78d9c87864`, membership junction miss fixed; issue closed), #2610 (`fb5738b60d`: 030a Office To Do via RecordCreationService; 046c role source), **#2617** (040+075+044; open with auto-merge, waiting for its SHA from fm).
- **MCP spike rides along** (owner: no time limit): every dev deploy = master + `5c9e301572`. mcp-bff-api merges the batch master SHA into `spike/spaarkeai-mcp-phase0` (draft PR #2615, never merged) and sends the SHA; I check the diff is spike files only. Spike gate: anonymous `/mcp/legal-operations` and `/mcp/spike-area-b` → 401, signed-in → 2xx. Send them the deployed SHA and read-back. Plain master after they say Phase 0 is done.
- #2617 order: BFF first; then the `sprk_recordfamily` page (NEW; `Deploy-WebResourceInline.ps1`, add to `spaarke_core`, read back 1 row); then `sprk_fieldmapping_push` v1.3.0. Rollback: v1.2.x. Not in the batch: `Set-FieldMappingRuleSyncMode -Apply` (owner review), 049b (next window).
- Pre-checks as before: 174/175 absent, no ebilling 095a code (the 095a gate: `sprk_sourceemaildocument` IsSecured must be read true by me first), `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exit 0, `Ebilling__StoredFileReconcileEnabled` false, census.
- Gate method: #2158 means telemetry drops whole requests, so counts are lower bounds; pair them with peer logs and a Dataverse read-back.

## Decisions this session (owner, here)
- 2026-11-06: W3 = named exceptions plus guards; 046c all five add-on holders get Core User (done); root team Console User removed (done; Get Started left as is); EB-D-200 option D.
- Partners (SPA) SEE reviewer-added lines and reasons while In Review (supersedes EB-D-192's clause; ebilling recorded it as EB-D-202; reasons via a typed invoice-module projection in task 130, never partner Read on the DR table).
- #2292: fix it (done, merged). Deploy: wait and batch with 044.
- MCP spike on dev: approach (a), then no time limit (dev/test).

## Reviews state
- **fm (`-69`):** 044 r1-r3 APPROVED (`37946fc15c`); squashed into the work branch (847e6215d9) → #2617. 040 + 075 APPROVED (re-check clean; Report Card SyncMode 1 is correct). **049b + fix1 + fix2 `82f82c0668` APPROVED**: my ruling (b), an inline fallback with the grantor only when nothing was queued; it merges after #2617, then gets its own PR. 032: I review the design before code (fill-only Matter mapping in InvoiceSubmissionPersister). The live-check gate for fm's 2026-11-06 windows was PASS (33 creates read back).
- **ebilling (`-c7`):** **164a `e7123d3fb7` APPROVED** (no F; Q1 accepted with the condition that system closes never carry a human decider; Q2, approved > billed, is a business question for the owner via ebilling; K1-K5 accepted). 165a lands; then **165c**: the submission link counts only if it equals `InvoiceIdFor(id)` (dev count 0 mismatches). 096b scope accepted (CreateTables minus invoice and billingevent, plus the ribbon and ArchTest; app-only proof before the role -WhatIf). 095d-2b-1, 095d-2a-2 and #2591 approved earlier. My flip of `Ebilling__StoredFileReconcileEnabled` needs the owner's go HERE.
- **ontology (`-38`):** 044 co-approved; the BFF holding Ontology Service is intended (reads; the writer client does the writes). Writer identity for customer environments: provisioning S23 (BFF role) plus the writer-identity task (new UAMI per stamp, owner). 174/175 held until ebilling 112.
- **mcp-bff-api (`-68`):** spike as above. A10 and B-4 auth diffs are still to come; production task 021 (CIAM/A9 refusal) is reviewed against FR-02.

## Owner items outstanding
- Report card: should its 8 `sprk_assigned*` lookups confer access (registry FR-24)? Today no (it follows the parent).
- Dev app users with prod-like names hold System Administrator (`# spaarke-bff-api-prod`, `# JobsServiceProd`).
- G-8 for customer environments via provisioning; #2590 rule (refuse vs route); `Set-FieldMappingRuleSyncMode -Apply` review (fm).
- Older: column auditing on writer-only lookups (verify ontology flags), AP diagnostics, VS Code agent-dir cleanup, word-add-in Office saves, click-test 195, #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Private items (never in issues/PRs/commits)
- Open gaps stay in peer messages only: the ebilling hold-column item (165a), the line-origin item (164a), the submission-link readers (165c). The line: identifiers are fine in public; "X can still write Y today" for an open gap is not.
- Earlier: core pair, lineage and update paths (fixed); 049 reasoning; the push runs as the caller.

## My own backlog
#2585, #2576, #2524, gate null-column hardening, SharePoint census, #2543, K-P1 for task 191; parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · mcp-bff-api `spaarke-wt-spaarkeai-mcp-bff-api-r1-68` · website `spaarke-website-cb` · SPA R4 `spaarke-wt-spa-external-access-platform-ad`.
