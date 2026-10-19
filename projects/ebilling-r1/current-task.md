# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-18
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. Fix rounds for 072 (parts L and E landed; part W running), 073 (fix round landed), 074 (fix round and follow-up landed). Also running: the #2095 refile fix. On the critical path: 072 → 073 → 049 → 100/130 |
| **Step** | Branch HEAD db4eee636+ (local; origin/work/ebilling-r1 is 33723dbdc). origin/master 1473b34c6 was merged (11d3b7103, then bf5ca1305); uac-r2 passed the FieldMappingEndpoints 1081-1195 resolution. Full unit and ArchTests verification of the tip is in progress (scratchpad fulltest.txt). Load-related seam timeouts are spurious; one real failure was fixed: c3d8616e2, the deleted-policy existence read stays domain-unfiltered (ontology-r1 agreed) |
| **Status** | in-progress (parallel mode) |
| **Next Action** | When an agent reports: cherry-pick with cp.sh (on a conflict, resolve, commit with the original message and its trailer rewritten, continue with RANGES="<conflicted>..<end>"); rewrite note hashes and verify they resolve; build and run filtered tests; send the range on. Then: (1) part W → its single adversarial pass → ontology-r1 checks the D-116 text and the gate downgrade → mark 071 and 072 ✅; (2) mark 073 ✅ after ontology-r1 re-checks 3473da85b..3540fd90d; (3) mark 074 ✅ after ontology-r1 re-checks 2a28e54f0..38dc7ab9e; (4) #2095 → send to uac-r2; (5) start 049 (opus) once 072 and 073 are ✅ |

### Ranges out for review
- **ontology-r1:**
  - 074 follow-up 2a28e54f0..38dc7ab9e;
  - 073 fix round 3473da85b..3540fd90d;
  - 072 part E 46c45e4bc..2b7c6a228.
  - Their 074 fix-round re-check was clean, and they agreed ADR-013 path C.
- **uac-r2:** 072 part L c9f4b64c3..b31430d12, checking known limit (i) against per-family Secure teams.

### Queued
- **077 follow-up (small).** The 073 F1-b note on a drafted rule whose consequence was lowered to Flag is stored in sprk_proposedaction, but the Rule Review page doesn't show it. Add it to the 077 read model and the page.
- **Owner OKs needed (live writes):**
  - re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1, 073 prompt change);
  - the 074 live seam test;
  - the IfRowVersionMatches probe (076).
- **053 deploy order:** Deploy-ActionMirrors.ps1 and Seed-PlaybookConsumers.ps1 run before or with the BFF; then /healthz. The D-116 columns must reach SpaarkeMaster.

### Critical Context
- **Decisions this session:** D-111 to D-119.
  - D-116: sprk_billingstatus on core records.
  - D-117: an expense rule rejects only on the firm-billed code.
  - D-118: client workforce users (WorkforceHome) may use the partner portal; the submission routes are unchanged.
  - D-119: a bare "$" is USD.
- **Constraints added to tasks:**
  - 049: ReceivedOn; episode n+1 and supersede on a fingerprint change; the currency key; CoreRecord from the filing read; the tracking number.
  - 049, 100, 103, 104, 105: BillingDecisionRecorder caller obligations (194ad556f).
- **015 deploy sequence.** Remaining:
  4. 015 reaches master and R3 merges it.
  5. A BFF with 015 is deployed.
  6. -Delete with both attestations, -WhatIf first.
  7. Remove the transitional allow-list entries.
- **ontology-r1:**
  - no seeding of dev invoices until their owner answers about REAL-2026-123456.01;
  - 049 (D-104 Pending Spend) goes to them for review;
  - after 074 reaches master they add the immutability ArchTest entry, the 5 comments, and the ExistingPolicyCodesAsync comment.
- **Standing rules:**
  - no line upload or storage route before #2047;
  - 026 must not touch Roles/Spaarke Ontology Service.xml;
  - remove worktrees with Remove-WorktreeSafe.ps1;
  - agents run filtered tests only, in their own worktree, never in the main worktree;
  - the dev BFF and SPA deploy only through SPA-r3;
  - only `scripts/Import-SolutionScoped.ps1` imports;
  - ask the owner before any push.
- **Owner actions:**
  - add the 7 new tables to the apps;
  - grant Write on sprk_ruleset before 079;
  - run the schema scripts (rejection reason; D-104 Pending Spend) on dev before a BFF that reads them.
- **Open:** G-18 (lint-staged shared stash, not filed); DEF-015; ISS-009; #2010, #2011, #2045, #2077, #2087, #2090.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 072 (part W), 073, 074 (awaiting re-checks), #2095 |

## Next Action

See Quick Recovery.
