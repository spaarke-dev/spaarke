# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-13 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership via RecordOwnershipResolver) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-133). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`. Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. Child-record contract tasks 140–146 (D-131..D-133) are planned; 140 is live on dev. |
| **Blocker** | Owner answers Q23, Q24, Q25, Q26, Q27 (below). |
| **Next Action** | 2026-10-14: merged #1834 (143) `96fec5d7b` and #1841 (F-nn → DI-nn rename) `aeaaed68f`. #1844 (050) is conflict-resolved at `2c44154d7` and merges on green; its live apply waits for the owner's dev-data approval. Running after the session restart (resumed): 058 (`C:/wts-058`), the 143 K1/K2 follow-up (`C:/wts-143k`), 148 (`C:/wts-148`). Next: 144 once uac-r2's C4/C7 PR lands; 042 after the K1/K2 follow-up. Merge with `scratchpad/merge-when-green3.sh`. |

## Lanes (worktrees `C:\wts-NNN`; all branch from `origin/docs/ontology-platform-design`)

| Lane | PR | State | Next |
|---|---|---|---|
| **050** worklist grid row | #1844 `2c44154d7` | merging on green | live apply after owner OK |
| **058** decision wizard | — | running (resumed) | PR → review → merge |
| **143 K1/K2** follow-up | — | running (resumed) | PR → merge |
| **148** assignee reconciler | — | running (resumed; opus, 2 passes) | PR → uac-r2 → merge |

Merge: `scratchpad/merge-when-green2.sh <pr> <full sha> docs/ontology-platform-design` (`gh pr ready` first). The Office-scope CI job hits its 20-min cap (DI-49, #1718); merge on log evidence with a PR comment.

## Child-record contract (tasks 140–146)

- **UAC 182** (lineage + census) carries the one-line `DecisionRecordWriter.cs` `LineageColumnByCoreEntity` typed-core filter (option b: conditions are the exact filter, the existing test unchanged, a new regression test, and a note in the PR body). It runs `merge-tree` against `85f430366` and sends the PR URL and sha. Its census exempts `sprk_budgetrevision` creator-person per **#1777**.
- The DR lineage row is inert until 142 writes the subject lookups. **142 must ship the two-wall refusal in the same PR** that first writes them (#1778; this is a constraint in the 142 POML).
- Sequence: 141 → 142/143 → 144 → 145 → 146. 146 waits for the 6 h metadata cache key.

## D-134/D-135 work

- **#1665 MERGED** `aeb9e0973` (task 043 ✅; D-134 included).
- **Task 148** (reconciler) is written. uac-r2 provides `SecureChildReconciler.ReownOrdinaryRowAsync` in its own PR (owner sign-off; we review it).
- **Task 147** (budget revision creator-person) starts once #1780 merges.

## Waiting / owner

- **D-137 (owner, 2026-10-13):** Q25 approved (#1747 merging); Q26 done (Basic User on the Dev 1/Test 1 default teams; #1776 closed); Q23 confirmed via D-135; Q24 Reviewed only; Q22 event individual = `sprk_assignedto` (038/059 POMLs updated); Q11 + Q15 recommended options.
- **Q7: DONE 2026-10-13** (passed at `4356397a2`; zz-037 = 0). Re-run recipe: env `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc`. Filter `FullyQualifiedName~SignalWriterSeamTests.WriteAsync_EventAndTodoUnderAMatter_GroupUnderTheMatter_CreateAndReconcile_RefreshDueDate&DisplayName~sprk_todo`. Afterwards, zz-037 rows must be 0.
- **Q8:** #1111 to master after 035 + 055.
- Owner decisions page https://claude.ai/artifact/ADCv42eKukX2BQx7d62Mop (Q1–Q19 answered).

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. Their K2 is #1730 (keep 403 `caller_unresolved`). Issues today: #1757, #1761, #1766–#1778.
