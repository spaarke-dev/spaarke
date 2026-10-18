# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-18
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 42/79 complete. Running agents (4/4): 076 fix round 2, 051 round 2 (D-110 move to service plus uac's firm-records items), 074 fix round (ontology F4-1/F3-1/Ks), 071 fix round 2 (ontology review at 4257e7f35, plus D-111 and D-112). Queued: the 075 retry fix and the #2081 follow-up (briefs below) |
| **Step** | Branch HEAD 6bcd4d029 (local, not pushed). The 015 follow-up is cherry-picked (7965ef68e..009317530); uac-r2 re-check PASS; ontology-r1 is re-checking. ADR-010 ceiling 166; route ledger 137 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent reports: review it, cherry-pick it (`RANGES="a..b" bash $TEMP/claude/c--code-files-spaarke-wt-ebilling-r1/e7feb8db-bd91-431a-995c-acf68e28cb28/scratchpad/cp.sh`, in the background), build and run targeted tests, then send the range on. 071 r2 → ontology-r1. 074 fix → ontology-r1. 051 combined b21be61be..r2 tip → uac-r2. 076 r2 → re-check the fix diff. Then copy the GetFirmClauseAsync caller rule into the 130/134/135 POMLs. Mark 015 ✅ once ontology-r1 passes. Launch the queued agents as slots free. Next tasks: 072 (needs 071 ✅), 077 (needs 076 ✅), 049 |

### Queued briefs (not yet launched)
- **075 retry fix (open F-class defect).** A run that dies midway and is retried asks the model again, and its output is not deterministic, so both runs' drafts remain. Fix by compensation: on a rerun with no completion marker, first delete the earlier run's untouched drafts, then draft again. A draft counts as untouched only when all of these hold: created by the job identity, created for this rule set, still Proposed and Off, modifiedon = createdon, no current version. Must-fire: a rerun after a partial run leaves exactly one set. Must-not-fire: a draft an admin edited or accepted is never deleted.
- **#2081 follow-up (uac-r2 split).** For sprk_invoice and sprk_billingevent targets, the guard reads the stored row and refuses not-found, so an upsert cannot create an orphan. Also:
  - DecisionRouteCores.cs:97 passes appOnlyEntities;
  - add a D-96 end-to-end test through the three generic paths;
  - add a test for UpdateRecordActionCore with no IGenericEntityService;
  - add a code-safe hint for detach → move → re-attach;
  - ActionSeam.cs:163 prints the code twice.
- **053 deploy order (from 075):** Deploy-ActionMirrors.ps1 and Seed-PlaybookConsumers.ps1 (the BILLING-RULE-DRAFT Binding) run before or with the BFF; then check /healthz.

### Critical Context
- **Decisions:** D-96 to D-112 (decisions-log). New this session:
  - D-111: a closed core record is judged by its close date.
  - D-112: a cross-wall Duplicate flags without the tracking number or line detail. The loader still covers both partitions.
  - 072 POML carries the D-112, SubmissionId and non-USD skip constraints.
- **015 deploy sequence.** Done: steps 1–3 (R3 BFF+SPA live; grid 3ff4102c switched 2026-10-18 03:42Z). Remaining:
  4. 015 reaches master and R3 merges it.
  5. A BFF with 015 is deployed.
  6. -Delete with both attestations, -WhatIf first.
  7. Remove the transitional allow-list entries.
  After the delete, every dev BFF must contain 015 (checked by content).
- **Next master merge** (after the fix rounds are in):
  - master a82578cdc+ carries R3 #2086;
  - keep 015's marker line in ExternalAccessModule;
  - take the union of the route ledger (master 132);
  - then run merge-tree against #2078 and #2061. Expected conflicts: ExternalUserContextResponse, bff-client.ts, the ledger, ExternalRecordDocumentRoutesTests and config/secure-record-owner-role.json.
- **ontology-r1:**
  - no seeding of dev invoices until their owner answers about REAL-2026-123456.01;
  - 049 (D-104 Pending Spend) goes to them for review.
- **Standing rules:**
  - no line upload or storage route before #2047;
  - 026 must not touch Roles/Spaarke Ontology Service.xml;
  - remove worktrees with Remove-WorktreeSafe.ps1;
  - agents run filtered tests only;
  - the dev BFF and SPA deploy only through SPA-r3;
  - only `scripts/Import-SolutionScoped.ps1` imports.
- **Owner actions:**
  - add the 7 new tables to the Matter Management and Spaarke Administration apps;
  - grant Write on sprk_ruleset to the billing admin role before 079.
- **Open:** G-18 (shared lint-staged stash, not filed); 075 ADR-017 job-status (Proposed); DEF-015; ISS-009; #2010, #2011, #2045, #2077, #2087.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 071, 074, 076 (fix rounds); 051 (round 2); 015 (final re-check by ontology-r1) |

## Next Action

See Quick Recovery.
