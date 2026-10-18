# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-18
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 44/79 complete (075 and 076 newly). Running agents (3/4): the 074 fix round, the 077 follow-up (D-113 reject reason), 072 (opus, on the critical path 072 → 073 → 049 → 100/130). Waiting on reviews: ontology-r1 (015 ad338f34a; 071 r2 e2288a659..229643893); uac-r2 (#2081 r2 eec0f3512..7b267873e; 051 combined b21be61be..d94a04130) |
| **Step** | Branch HEAD ee84cd75e (local, not pushed; the owner has not asked for a push). Landed this stretch: 076 r3, #2081 follow-up and its r2, 077, 051 r2, 075 retry fix (resolved a harness conflict by keeping both helpers). Verified: build 0/0; focused 5890; RuleSet and drafting 856; ArchTests 1175; SPA vitest 1055/1055 (serially; a run under load fails spuriously); RuleReview jest 49 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent or review reports: cherry-pick (cp.sh; on a conflict, resolve, commit with the original message and its trailer rewritten, then continue the range), verify, send the range on. Mark ✅: 015 when ontology-r1 is clean; 071 when its re-check is clean; 051 and #2081 when uac-r2 is clean; 074 after its fix round and ontology-r1; 077 after the reject-reason follow-up. Next tasks: 073 after 072; 049 after 073 |

### Queued briefs (not yet launched)
- **051 round 3 (uac-r2 accepted the combined range; resume the 051 agent when a slot frees):**
  - (1) Deleted record gives a retryable 503. The filter's DenyUnlinkedFirmAsync refuses early only on NotLinked and leaves Unreadable to the service. In SubmitAsync, run the link check AFTER DeriveResolverFieldsAsync (still before the owner check and any write), so a missing record gets 422 core_record_unavailable again and 503 means only a real fault. Test: a record that cannot be found gives 422, not 503. Send uac-r2 just that range.
  - (2) The comment at ExternalParticipationService.cs ~1441-1443 and the wire-test header: change "column security omits a column" to "a property absent from the response". No re-check needed.
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
  - D-113: rejecting a draft records a reason (admin-only column; the 077 follow-up builds it, 079 runs the script). D-114: Rule Review entry points are the sitemap item and the rule set form command (079).
  - 072/073/077/079/130-135 constraints were added this session (withheld template; template by subject; displayed version on accept; the GetFirmClauseAsync caller contract).
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
