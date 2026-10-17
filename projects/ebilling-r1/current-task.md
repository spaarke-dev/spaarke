# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-17
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. Done 40/79. Running (4 agents, base 16c76e7d8): 070 fix-round-2 (ontology paired change), 026 fix-round-2 + DEF-022 (D-96 invoice-side line check, app identity; guard on three generic update paths; uac K4; 053/130 doc items), 015 (retire sprk_invoicestatus, owner D-97 incl. live delete), 051 (SPA submission UI). ADR-010 ceiling 166; route ledger 136 (after the master merge) |
| **Step** | origin/master 1df4ee12d merged as 49acd2f78. Conflicts: ADR010 166, ledger 136, SignalReevaluationJobTests fake, PolicyVersionValidator (ValidateForAuthoring passes RuleDomain.Ontology). The full unit suite is running on 49acd2f78 with TRX, results in scratchpad/full-49acd2f78. The earlier 25 failures came from a pre-#2074 master regression (#2072); clean master 1df4ee12d passes TodoAssigneeOwnerReconciliationJobTests 34/34 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent reports: cherry-pick (trailer rewrite, background, 600 s), build, targeted tests and ArchTests. 070 → send its range to ontology-r1, including the merge-time ValidateForAuthoring change; on approval mark 070 ✅, then start 071/074/075/076 (≤4 at a time). 026 fr2 → send to uac-r2 (resolver K4, OwnedChildWrite D-96, the three DEF-022 paths) and ontology-r1 (053/130 docs); on OK mark 026 ✅ and close #2056 via the PR. 015 → apply the ACT-004.json edit in the main session (.claude), send ontology-r1 the notice the agent wrote, review the live delete evidence, mark ✅. 051 → review, then mark ✅ (052 also needs 105) |

### Files Modified This Session
- All committed (last: D-97 16c76e7d8, plus this checkpoint).

### Critical Context
- Decisions: D-92 (= ontology D-153); D-93 tuning (054 done); D-94 billingevent intermediate; D-95/D-95a AppendTo (ontology #2048) + firm reason FLS (applied live; profile "Spaarke Ontology Writer Firm Reason Writers", accepted by ontology); D-96 invoice re-file refused when lines name another core (app identity); D-97 owner OK for 015 incl. delete.
- 015 code check (owner asked to cover ontology code): indirect readers via the FinanceBaseValues constants (FinanceRollupService); live-only aiskillconfig a32ae5a7 (Copilot form-fill opt-out) blocks the attribute delete; 3 live rows (ToReview, statuscode 1); stale ontology branches carry the old FinanceRollupService. All of this is in the 015 POML constraints.
- DEF-022 (#2056): FR-05 guard missing on UpdateRecordActionCore, DataverseUpdateHandler and the FieldMappingEndpoints push; uac-r2 direction is to guard each path (fix in 026 fr2).
- Standing: no line (sprk_billingevent) upload/storage route before uac-r2 #2047. The 053 deploy needs the SpaarkeMaster import (or schema) before the BFF, then the firm-reason script.
- The full Sprk.Bff.Api.Tests run takes ~45 min (24.5k tests); agents run filtered sets only.
- Owner action pending: add the 7 new tables to the Matter Management and Spaarke Administration apps (app designer).
- Open: 106 rate-history question (optional); DEF-015; ISS-009; #2010, #2011, #2045; write-path registry row for the tracking number.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 070 fr2, 026 fr2, 015, 051 (agents) |

## Next Action

See Quick Recovery.
