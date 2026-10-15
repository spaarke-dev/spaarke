# Ontology tables against the secure-family procedure (audit, 2026-10-12)

Owner request (round 94): Decision Records and every related ontology component must be part of the secure-record schema and fit every stage of the procedure.

**What was audited.** The procedure on master `91cbac7b8`, applied to the ontology integration branch `origin/docs/ontology-platform-design` at `142b90ff3` (PR #1111). The open PRs #1665 (043), #1746 (`35ce786`) and #1747 (`5c51f86`) were also checked. Master holds only the schema and UAC's lineage rows for `sprk_signal` and `sprk_decisionrecord`; the writers live on the ontology branch.

## Tables

| Table | Ownership | BFF writers |
|---|---|---|
| `sprk_signal` (matter, decisionrecord, policy/version, nine `sprk_regarding*` subject lookups; core id stored as text) | User | `SignalWriter`, `SignalSuppressionService`, `DecisionCommitService` |
| `sprk_decisionrecord` (matter, project, workassignment, policyversion, corerecordtype, confirmedby) | User | `DecisionRecordWriter` (create only) |
| `sprk_budgetrevision` (matter, budget) | User | `ReviseBudgetExecutor` (043) |
| `sprk_policy` / `sprk_policyversion` | User | none (admin-maintained) |
| `sprk_servicerequest` (Inquiry) | User | `BudgetInquiryExecutor`, `InquiryDispositionService` |

Suppression, plans, steps and evidence are columns or JSON on Signal and Decision Record, not separate tables. To Do, Event, Communication and Work Assignment writes go through UAC's own cores.

## Matrix

| Stage | signal | decisionrecord | budgetrevision | policy | servicerequest |
|---|---|---|---|---|---|
| 1 Lineage | OK (no typed project/WA core, F5) | core types OK; no-core and subject unreachable (F1, F5) | GAP on master, OK on ontology branch (F8) | N-A (F9) | N-A by design |
| 2 Resolver | secure OK; ordinary and no-core paths not resolver-owned (F2, F3) | secure OK; core-only parents (F1); ordinary keeps the writer (F2); no-core owned by a user (F3) | OK | N-A | secure refused, OK |
| 3 Reconciler + 2-min job | OK | OK | ontology branch only | N-A | N-A |
| 4 Share sync | OK | OK | ontology branch only | N-A | N-A |
| 5 Access permission | N-A | N-A | N-A | N-A | N-A |
| 6 Core stamps | N-A; core frozen on re-file (F5) | core frozen; no subject lookup (F5) | N-A | N-A | core table |
| 7 External | N-A by design | N-A | N-A | N-A | N-A |
| 8 Files | N-A | N-A | N-A | N-A | N-A |
| 9 Read paths | OBO OK; suppression crosses the wall (F6, #1746) | app-only counts only, OK | N-A | readable by every Console user (F9) | OK |
| 10 Guards | partial (F4) | partial (F4) | F4 | none | censused |

## Findings

| # | Severity | Finding | Owner |
|---|---|---|---|
| F1 | High (leak) | A Decision Record's owner ignores the secure ownership of the Signals it resolves. The resolver is given the core only (`DecisionRecordWriter.cs:353-359`, 043), and Also-resolve checks only the same core (`DecisionCommitService.cs:1079`). A Signal that is secure-owned through its subject, but has an ordinary core, produces an ordinary-owned Decision Record that the core's BU can read. Fix now: pass every resolved Signal's subject as a parent, or refuse. Durable fix: subject lookups on the DR, or `sprk_securefamily`. | Ontology (043 rework); schema decision is the owner's |
| F2 | Medium | D-130 is not implemented on the ordinary paths: the Signal is owned by the writer MI with `owningbusinessunit` set directly, and the DR keeps the writer's ownership. Blocker: BU default teams lack Read (0x80040299). | Ontology; the role change is an owner decision (UAC/provisioning) |
| F3 | Medium | No-core Signals and DRs are owned by a user. The `ownerid == caller` read rule breaks once owners are teams. The 043 rework probably misses `SignalWriter.BuildEntity` and `ReassignNoCoreOwnerAsync`. | Ontology |
| F4 | Medium | Guards: the census `ChildTables` (`RecordOwnerAssignmentCensusTests.cs:42-50`) omits signal, decisionrecord and budgetrevision. Direct `owningbusinessunit` writes are not detected. Nothing ties the solution's entities to the codified set and the lineage. | **UAC** |
| F5 | Low–medium | The core is frozen when the subject is re-filed. DRs have no subject lookup, so a subject moving from ordinary into secure leaves its DRs readable by the old BU. Also limits #1747 (dedupe key collides). | Ontology; owner decision (D-34) |
| F6 | Medium | Suppression crosses the secure wall. #1746 splits only secure-owned versus not; Signals under two different secure roots that share an ordinary core still cross-stamp. Partition by the set of secure roots. | Ontology (#1746) |
| F7 | Low | Console User holds Create on `sprk_decisionrecord` and Write on `sprk_signal` (Deep). Creates outside the BFF skip the resolver (corrected within about 2 minutes), and this breaks append-only. | Owner decision (ontology roles) |
| F8 | Transitional | The UAC-owned lineage and role entries for budget revision are on the ontology branch only. | UAC reviews them at merge |
| F9 | Low | A matter-scoped `sprk_policy` row exposes a secure matter's name and rule to every Console user. | Owner decision |

The secure-family design (round 94: `sprk_securefamily` plus an access team per family) would resolve F1 and F5 generically: a DR or Signal joins its subject's family by field.
