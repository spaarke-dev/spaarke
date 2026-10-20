# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 51/79 complete; 100 and 105 🟡 (built, verified, pushed at 0d5808db8; Signals stay open after a human decision until the close lands; they do not go to dev or master before it). 049 ✅. D-133 live on dev. D-134 (owner 2026-10-20): confirm-from-attachment moves onto the submission path: an existing-document entry in 044, the invoice number required, secure re-file in the new path (uac-r2 reviews), the firm-link rule; 050 splits into 050a (Opus, FULL, adversarial review) and 050b (pure deletion). IN FLIGHT: agent mergemaster in C:\code_files\spaarke\.claude\worktreesgent-abe0b928f7d30cd9f is merging origin/master (48 commits) into the branch for #1911. All 6 conflicts are resolved per the owner-team rules (ontology A/B, uac C/D); it was told to commit a checkpoint merge (not pushed). Gates: build 0/0, filtered 8,790/0; full ArchTests 1,234 pass and 2 FAIL |
| **Step** | Merge blockers (both from master guards seeing our code): (1) task 186 workforce ceiling: our 6 /api/v1/external/.../invoice-submissions routes (ExternalInvoiceSubmissionEndpoints.cs) declare no ceiling; uac-r2 must choose the route-map sets (WorkforceRoleCeilingGate) or allow .WorkforceCeilingInHandler. (2) task 146 child-create census: #2084 made sprk_policy/sprk_policyversion secure-family, so the RuleSetService.cs:1060/1075 creates (OUR task 076 code) are UNLISTED; Routed needs the creator-person column on both tables (schema) and Waived needs uac/ontology agreement; plus a real gap: RuleSetService.cs:1284 edit-path version create passes no parent to the owner resolver. Diffs for review: the writer vs master in scratchpad\writer-vs-master.diff (+713/-0, all ours); the role JSON is a union (our 4 child Reads + master sprk_policy, sprk_policyversion, sprk_externalrecordaccess). Push only when the full ArchTests are green |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) Get the merge sha from mergemaster; ff-merge it into work/ebilling-r1 (git merge --ff-only worktree-agent-abe0b928f7d30cd9f); do NOT push yet. (2) Ask uac-r2 (blocker 1 sets; blocker 2 Routed vs Waived; the :1284 owner gap) and ontology-r1 (the writer diff check, census). (3) Fix the blockers in follow-up commits; full ArchTests green; push. (4) Then launch: the human-decision Signal close (ontology binding shape: extend CloseActedByAutomatedDecisionAsync with an optional decidedByUserId → sprk_resolvedby; no impersonation; route authz AppendTo core + Write subject; all outcomes Acted; D-122 set; a failed close only warns) wired into 100 and 105; and 050a. (5) Owner questions open: run Deploy-EbillingOverridePrivilegeSchema.ps1 on dev? Which role holds the eBilling Override privilege (prvWritesprk_ebillingoverride)? |

### Ranges out for review
- ontology-r1 PASS: D-130 closed (review round ae5197e25 + re-check fixes 77690aa4f). PASS 2026-10-19: 452acb6c0 PreflightRefusal, 07a2f67b3 D-170 sweep; 650109d97 close method approved once the F3-1..3 tests land. Earlier PASS: 072 (3ce07e0e3), 049 D-104 (295aa332a), D-125 (183c2cf47). Part D review FINDINGS addressed in pieces; core fixes PASS 5a04a41fb.

### Owner decisions this session
D-111 to D-120 are in the decisions log.
- D-117: an expense rule rejects only on the firm-billed code.
- D-118: client workforce users may use the partner portal.
- D-119: a bare "$" is USD.
- D-121: credit amounts may be negative (lower sprk_amount min). D-122: an automatic rejection resolves every open Signal on the invoice. D-123: D-91 / coding findings become Signals under a system billing policy (needs ontology-r1). D-124: new sprk_invoice tracking-number column.
- D-129: credit memo arithmetic. D-130: rules on uncoded; spend = approved total; submitter stamped; Processing until after-steps.
- Owner 2026-10-19 (to ontology-r1): when a billing policy is turned off or retired, its OPEN Signals close (PolicyRetired-style); closed ones untouched. ontology-r1 asked to own the sweep change.
- D-131: the invoice records the external submitter contact in the new sprk_submittedby (Submitted By, contact lookup, created live on dev); a submitter with no linked systemuser still gets an invoice.
- D-125: SubmissionIntegrity rule type for the system policy; an admin may disable it. D-126: a read but uncoded submission still creates its invoice, held. D-127: negative invoice totals (credit memos). D-128: a reviewer may disallow a credit line.
- D-120: core-record delete. Finance history blocks the delete. A rate card scoped 1:1 to the record is deleted with it; a shared card is kept. Budgets are kept, unlinked, and never block. uac-r2 adopted it.

### Queued owner actions (live writes need the owner's OK)
- Re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1; 073 prompt change).
- The 074 live seam test.
- The IfRowVersionMatches probe (076).
- Run the schema scripts on dev, with the owner's OK and notices before and after to ontology-r1 and uac-r2, BEFORE a BFF that reads them: rejection reason; D-104 sprk_pendingspend; D-121 Deploy-EbillingBillingEventAmountRange.ps1; D-124 Deploy-EbillingInvoiceTrackingNumberSchema.ps1; D-125 rule-type option and Seed-BillingSystemPolicy.ps1 (fix #2107, the owning team, first); D-127/D-129 Deploy-EbillingInvoiceTotalsRange.ps1 (seven columns, including sprk_billingexport total). The pending-spend query shapes are already verified read-only on dev.
- D-131: export sprk_invoice.sprk_submittedby (created live on dev 2026-10-19) into the SpaarkeMaster solution source.
- Add the 7 new tables to the apps.
- Grant Write on sprk_ruleset before 079.

### Critical Context
- **049 constraints** (in its POML):
  - ReceivedOn = submission createdon (DateOnly, UTC);
  - Signal episode n+1 and supersede on a fingerprint change;
  - HeaderConfidence["Currency"];
  - CoreRecord comes from the same submission read that files the invoice (re-evaluate on change);
  - pass the tracking number;
  - build the DR snapshot from the scrubbed mapping;
  - the BillingDecisionRecorder caller obligations;
  - D-104 Pending Spend (ontology-r1 reviews).
- **100:** render billing reasons from codes. SignalReadService strips non-witness evidence text; ontology-r1 will not exempt it.
- **015 deploy sequence:**
  - R3 deployed dev BFF 44f802a1d on 2026-10-19 11:37Z; it still reads sprk_invoicestatus, so it is not an attestation.
  - Remaining: 015 reaches master → a BFF with 015 → -Delete with both attestations (-WhatIf first) → remove the transitional allow-list entries.
- **ontology-r1:**
  - no seeding of dev invoices until REAL-2026-123456.01 is answered;
  - after 074 reaches master they add the immutability ArchTest entry, the 5 comments and the ExistingPolicyCodesAsync comment.
- **uac-r2:** #2100 (K-i: any Secure-BU team, with task 178). Secure Record Owner role: dev holds our 4 Reads (submission, approval, external identifier, correction; 013, D-86, 35ce457f2) that master's config/secure-record-owner-role.json lacks until #1911 merges. uac-r2 will not run the §5.4 "outside master's file" strip until then; no separate PR (agreed 2026-10-19).
- **Standing rules:**
  - agents work only in their own worktree, never in the main worktree;
  - run filtered tests; then the broad filter after multi-range picks;
  - remove worktrees with Remove-WorktreeSafe.ps1;
  - only Import-SolutionScoped.ps1 imports;
  - no line upload route before #2047;
  - the dev BFF and SPA deploy only through SPA-r3;
  - never use bare git stash or --no-verify.
- **Open:** G-18 (lint-staged shared stash, not filed); DEF-015; ISS-009; #2010, #2011, #2045, #2077, #2087, #2090, #2100, #2106, #2107, #2108, #2118 (DEF-026, billing Duplicate detail after a later Secure transition; ontology DI-120).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | #1911 master merge; then the human-decision close + 050a |

## Next Action

See Quick Recovery.
