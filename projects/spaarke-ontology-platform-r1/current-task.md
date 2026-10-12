# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-12 late (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership via RecordOwnershipResolver) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-133). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`. Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. Child-record contract tasks 140–146 (D-131..D-133) are planned; 140 is live on dev. |
| **Blocker** | Owner answers Q23, Q24, Q25, Q26, Q27 (below). |
| **Next Action** | Background work running: #1746 fix round 2 (opus agent `a33783db5c83ec2e7`), merge scripts for #1763 (`bl281qdi7`) and #1756 (`b13jn7oxs`, head `5c020d2ac`). When #1746 round 2 reports → send the head to uac-r2 + a scoped re-check (fix diff only) → merge. When UAC 182's PR arrives (branch `uac/182-ontology-lineage-census`, targets this branch) → review → merge. Then task 141 (after #1665, #1746, #1747 and 182 are merged). |

## Lanes (worktrees `C:\wts-NNN`; all branch from `origin/docs/ontology-platform-design`)

| Lane | PR | State | Next |
|---|---|---|---|
| **043** D-129/D-130 ownership, D-35 read | #1665, head `85f430366` | Our pass + fix round done. **UAC verdict APPROVE, held only for Q23.** N1 (two-wall refusal) scoped to task 142: PR-body note + #1778. K-a identity cache ~2 min; K-b no-core pair not cross-checked (FR-51); K-c writer AppendTo on contact → task 146 live gate. **Before merge:** merge the base (with #1780), rewrite the DR Pending reason in `RecordOwnerAssignmentCensusTests.cs` ~:246 (uac-r2 C2; PR comment posted), and fix the `DecisionCommitFollowUpsTests.cs:54` / `DecisionCommitServiceTests.cs:216` constructor args if #1746 merged first. | Merge when Q23 is answered (and Q26 for dev use). |
| **025b** threshold values | #1756, head `5c020d2ac` | Re-check F3 boundary Theory added (`ThresholdRuleTests.ThresholdHolds_AppliesEachOperatorAtTheBoundary`; gt→ge and lt→le mutations each fail one row). Counted status = Reviewed only. | Merge script running. A one-line change if Q24 adds ToReview. |
| **#1661** secure suppression | #1746, head `63e7a569d` | Fix round 2 (opus) running: our F1 ×3 + F3, uac-r2 N1 (cross-wall stamp survives NeedsWork), N2 (placing hits with a null owner; the cheap fix is preferred), N3 (issue); the run result shows the skipped policy id + count. | Send the head to uac-r2 → scoped re-check → merge. |
| **#1659** filing trigger | #1747, head `cc6b10cf5` | UAC APPROVED. | Merge when Q25 is answered. |
| **140** schema delta | #1763, head `0916bddc0` | Live on dev. Review fixes done. | Merge script running. |

Merge: `scratchpad/merge-when-green2.sh <pr> <full sha> docs/ontology-platform-design` (`gh pr ready` first). The Office-scope CI job hits its 20-min cap (F-49, #1718); merge on log evidence with a PR comment.
**Merge order:** whichever of #1665/#1746 merges second fixes `DecisionCommitFollowUpsTests.cs:54` and `DecisionCommitServiceTests.cs:216` (the constructor takes `IConfiguration` + `IServiceScopeFactory`).
**Don't deploy** a BFF with SignalWriter/043 readers to dev until 141 merges: the code reads the pair type as text, but it is now a Lookup.

## Child-record contract (tasks 140–146)

- **UAC 182** (lineage + census) carries the one-line `DecisionRecordWriter.cs` `LineageColumnByCoreEntity` typed-core filter (option b: conditions are the exact filter, the existing test unchanged, a new regression test, and a note in the PR body). It runs `merge-tree` against `85f430366` and sends the PR URL and sha. Its census exempts `sprk_budgetrevision` creator-person per **#1777**.
- The DR lineage row is inert until 142 writes the subject lookups. **142 must ship the two-wall refusal in the same PR** that first writes them (#1778; this is a constraint in the 142 POML).
- Sequence: 141 → 142/143 → 144 → 145 → 146. 146 waits for the 6 h metadata cache key.

## Waiting / owner

- **Q23 (blocks #1665 merge and task 143): personal To Do Signals/DRs.** A (recommended): a new resolver outcome follows the subject's owner when the subject is user-owned and unfiled (uac-r2 design `RecordOwnershipContext.DerivedFrom(target)` at `RecordOwnershipResolver.cs:1511-1536`; UAC file → owner sign-off). B: BU team (uac-r2 advises against). C: no Signals on personal To Dos. Today the DR lands in the To Do's own BU default team.
- **Q26 (blocks #1665 on dev):** assign Spaarke Basic User (or a minimal role) to the **Dev 1 and Test 1** BU default teams only; the Secure Record team stays roleless. #1776. Facts: `C:\wts-043\...\notes\043-bu-team-read-check.md`.
- **Q27 (blocks tasks 142 and 143 flipping to Routed):** `sprk_createdbyperson` (lookup → systemuser) is missing on three tables:
  - `sprk_budgetrevision` (#1777);
  - `sprk_decisionrecord` and `sprk_signal` (#1783).
  - A (recommended): add the column to all three in one scoped import (D-83). Writers pass `RequestedBy`; job-raised Signals leave it null, with the reason recorded in the guard. Drop 182's exemption.
  - B: Signal is exempt; DR and budget revision get the column.
  - It is a schema change, so it needs owner approval. If approved, it goes in a new task 147, before 142 and 143.
- **Q24:** invoice statuses counted as spend (only ToReview/Reviewed exist; Reviewed only for now).
- **Q25:** word-add-in sign-off on `ReconciledEmailFiling.cs` for #1747 (the owner may approve on their behalf).
- **Q22:** event column for the individual: `sprk_assignedto1` or `sprk_assignedto` (all are contact lookups).
- **Q20 (parked):** no-core DRs on a later secure flip; superseded by tasks 142/144 once they land.
- **Q7 live re-run:** retry once the machine is quiet. Env `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc`. Filter `FullyQualifiedName~SignalWriterSeamTests.WriteAsync_EventAndTodoUnderAMatter_GroupUnderTheMatter_CreateAndReconcile_RefreshDueDate&DisplayName~sprk_todo`. Afterwards, zz-037 rows must be 0.
- **Q11:** F-nn → I-nn rename in one commit, after #1746, #1747 and #1756 merge.
- **Q15:** EventId 50361 alert + runbook in task 035. **Q8:** #1111 to master after 035 + 055.
- Owner decisions page https://claude.ai/artifact/ADCv42eKukX2BQx7d62Mop (Q1–Q19 answered).

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. Their K2 is #1730 (keep 403 `caller_unresolved`). Issues today: #1757, #1761, #1766–#1778.
