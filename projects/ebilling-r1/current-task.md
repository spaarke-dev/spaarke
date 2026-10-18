# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-18
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 42/79 complete. Running agents (4/4): 051 round 2, the 074 fix round, the 075 retry fix, 076 fix round 3 (re-check F3 x2 plus the take-back race). Queued: #2081 follow-up |
| **Step** | Branch HEAD 66a2e3adb (local, not pushed). On the branch: 015 follow-up plus its ontology re-check fixes (ad338f34a), 071 fix round 2 (e2288a659..229643893), 076 fix round 2 (05b8c75aa..d7e5c5a83, notes hashes fixed in cd0cdffb5). Verified: build 0/0; Billing 871; RuleSet/BillingAdmin/Finance 1381; BulkUpdate 12; ArchTests 1175 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent reports: review, cherry-pick (cp.sh in the background), build and targeted tests, send the range on. Waiting on: ontology-r1 (015 ad338f34a; 071 r2 range) and the 076 r3 agent. 074 fix → ontology-r1. 051 combined b21be61be..r2 tip → uac-r2. Mark 015 ✅ when ontology-r1 is clean; 071 ✅ when its re-check is clean; 076 ✅ after r3 plus its re-check. Then start 072 (its POML carries D-112 root sets, uac-r2 calling conditions, currency and withheld-template constraints) and 077 |

### Queued briefs (not yet launched)
- **#2081 fix round 2 (uac-r2 re-check of 79bd38196..16f0f40b6). Resume the #2081 agent when a slot frees up:**
  - (1) **F, check-then-act.** UpdateRecordFieldsAsync (DataverseWebApiService.cs ~1948, SendPatchAsJsonAsync ~264) sends a PATCH with no If-Match, so a row deleted after the guard's read (InvoiceFamilyCoreRecordGuard.cs ~365) is re-created as an orphan. Fix: add an update-only (`If-Match: *`) overload that keeps impersonation, and use it for the invoice-family tables at UpdateRecordActionCore.cs:136, DataverseUpdateHandler.cs:136 (including its concurrency path) and FieldMappingEndpoints.cs:1078. A 404 maps to invoice_family_record_not_found, with nothing created.
  - (2) **Existence oracle.** On the impersonated playbook-update and output-orchestrator paths, not-found also returns invoice_family_read_failed. The push is safe because its ids come from a caller-read query. Needs a test.
  - (3) **Hint wording:** "To move an invoice that has lines, or a line of one".
- **075 retry fix (open F-class defect).** A run that dies midway and is retried asks the model again, and its output is not deterministic, so both runs' drafts remain. Fix by compensation: on a rerun with no completion marker, first delete the earlier run's untouched drafts, then draft again. A draft counts as untouched only when all of these hold: created by the job identity, created for this rule set, still Proposed and Off, modifiedon = createdon, no current version. Must-fire: a rerun after a partial run leaves exactly one set. Must-not-fire: a draft an admin edited or accepted is never deleted.
- **(done) #2081 follow-up, superseded by the round 2 brief above; the original items:** For sprk_invoice and sprk_billingevent targets, the guard reads the stored row and refuses not-found, so an upsert cannot create an orphan. Also:
  - DecisionRouteCores.cs:97 passes appOnlyEntities;
  - add a D-96 end-to-end test through the three generic paths;
  - add a test for UpdateRecordActionCore with no IGenericEntityService;
  - add a code-safe hint for detach → move → re-attach;
  - ActionSeam.cs:163 prints the code twice.
- **Pre-deploy live probe (076 re-check K2):** before relying on 076's conditional publish, make one probe on dev to confirm that IfRowVersionMatches inside ExecuteTransactionRequest is honoured per inner request and that the mismatch fault carries 0x80060882. It is a live write, so it needs the owner's OK.
- **053 deploy order (from 075):** Deploy-ActionMirrors.ps1 and Seed-PlaybookConsumers.ps1 (the BILLING-RULE-DRAFT Binding) run before or with the BFF; then check /healthz.

### Critical Context
- **Decisions:** D-96 to D-112 (decisions-log). New this session:
  - D-111: a closed core record is judged by its close date.
  - D-112: a cross-wall Duplicate flags without the tracking number or line detail. Refined to secure-root sets with a subset rule that fails closed. The loader still covers both partitions.
  - 072/073/077/130-135 constraints were added this session (withheld template; template by subject; displayed version on accept; the GetFirmClauseAsync caller contract).
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
