# Current Task State

> **Last Updated:** 2026-11-14, about 06:00Z. Session `ebilling-r1`, Claude Desktop, the only writer to this worktree.
> Older detail is in `notes/handoff-history/current-task-2026-11-13a.md`.

## Quick recovery

- **Branch:** `work/ebilling-r1`.
- **Local HEAD:** `f6f8a16f3a` plus this notes commit; pushed. **PR #2822** (work → master) is open, not bound; ontology-r1 and uac-r2 were asked to review it (both approvals gate the merge).
- **Owner mode:** "continue with open work items that are available", plus a testing checklist (`notes/owner-testing-checklist.md`, committed).

## Gates

- Green. The full-suite failures were TaskCanceled timeouts from machine load. Every failing class re-ran green on its own: 8 sequential batches, 872 tests, 0 failures. ArchTests are 2570/0. This meets uac-r2's 084d-5a condition ("full gates green").

## Next actions (in order)

1. DONE: gates green, notes committed, work pushed.
2. DONE (cdde3d69b4 + follow-ups merged; 084d ✅; provisioning-r1 Pester 81/81). **Merge origin/master into work.** It brings:
   - #2798 (ISS-021, `3ae2a63362`);
   - #2790 (fm 052b and 045d-f, `4f9e77e9c0`);
   - later #2819 (fm 045g).

   Then:
   - add the ISS-021 dry-run/write parity case in the 084d rig (asked for by ontology-r1);
   - swap the planner's literal `"statecode", 0` for StateActive;
   - add a `ProcedureEntity.StateActive` alias (ontology-r1 comment);
   - tell provisioning-r1 to re-run the `Set-CustomerOwnerTeams` Pester tests.
3. **Once 084d's parity case is in**, mark 084d ✅.
4. **When 045g is on work**:
   - reword `InvoiceApprovalLineOriginTests.LinesDetachedMovedToANewCoreAndReattached_Approve` to state the rule only. Name NO route; the repo is public.
   - Add a runbook line: a legacy invoice with no core, or with two, cannot be filed through the BFF (EB-D-231).
5. **PR #2822 open.** ontology-r1 PASSED it at 565f82624b, with K1–K4 and no F findings. uac-r2's review is in progress.
   - Do NOT push to the work branch while uac-r2 reviews; commit locally only.
   - Asked ontology-r1 which test was its 1/2990 failure (it needed a file that isn't in the repo); fix or file that.
   - K4: should a per-record Procedure override file a Decision Record? This goes to the owner.
   - Merging needs uac-r2's pass plus the owner's go. After the merge, send the master SHA to word-add-in-r1 and spa-r4.
6. **095g build.** The owner answered every question (EB-D-233..235; 095g notes, "Owner answers").
   - Q2 changed to ALLOW an inactive record. uac-r2 was asked whether its approval still holds; wait for that answer before building.
   - Q3: organization type "Law Firm", matched by `sprk_organizationtypecode`, which is empty on dev and has to be seeded (a live write). Two firms assigned on dev have no type.
   - The build includes R1 and R2, needs two adversarial passes, and uac-r2 reviews the PR.
7. **095f part 2.** The owner answered (EB-D-236, 237):
   - every decision allows an optional short note, line decisions included;
   - every Correction needs a note, the from-document route and the Create Invoice wizard too.
   - Split: 2a (lifecycle extension + reject/return), 2b (adjustment), 2c (override + blocked return), 2d (line-decision notes), 2e (from-document Correction note, 094 route + CreateInvoiceWizard). Overwrite with the latest reason; Decision Records keep the history.
8. DONE 2026-11-14 03:45Z: `sprk_createinvoicewizard` redeployed to dev from master `cfa97d718b`. The owner's go was typed. uac-r2 got notices before and after. Recorded in `notes/053-wp2-deploy.md`.
9. Then task 112 (deploy). The 095f hold is RELEASED: word-add-in-r1's pane, which sends `correctionNote`, is live on dev at 942b2889bf (#2815 759dc4b1f6, #2820). `Deploy-EbillingInvoiceCorrectionNoteColumn.ps1` must run on every environment beyond dev before 095f goes there.

**Master now:** `cfa97d718b`. It includes 045g (#2819, merged at b76276a314). #2794 (ISS-020) is CLOSED.

**Gates re-run:** Bash `b8vixvoty` re-runs the 177 classes that failed in the full run on `bb28870b76`. The trx is `scratchpad/trx/rerun-bb2887.trx`. The second full run had 438 failures: 435 TaskCanceled (57-475 s), plus 1 TimeoutException, 1 IOException and 1 Compose fidelity failure (`alternate-content-duplicate-paraid.docx`, not ours).

## Decisions this session (all in `notes/decisions-log.md`)

- EB-D-223..232. EB-D-218 is refined for the 084d-5a candidates.
- Owner, via field-mapping-r1: an approval or correction is never without an invoice (045g round 4, reviewed PASS).

## Peers

- **uac-r2:**
  - 095g design APPROVED with R1 and R2. The pending-revoke limit is its D-10. The slot 2 Insights label is #2816.
  - The dev `createinvoicewizard` is stale (confirmed); the redeploy waits on the owner's go.
- **ontology-r1:** EB-D-232 PASS. Still owed: the DEF-059 role note, the DEF-060 Signals diff and the 176 fixture change.
- **field-mapping-r1:**
  - 045g round 4 PASS.
  - #2790 is merged to master; #2819 (045g) is open.
- **spa-r4:** task 010 review PASS-with-changes; its fix 9b10699efe re-checked PASS. Owes us its master merge SHA.
- **word-add-in-r1:**
  - It has the 095e and 095f contracts; the pane follows, using a top-level `firmName`.
  - Owed: the master SHA.
- **provisioning-r1:** re-run Pester after our master merge.

## Filed this session

- ISS-021 #2797 (fixed by #2798 plus EB-D-232).
- ISS-020 #2794.
- ISS-018 #2777.
- ISS-019 #2782.

## Known limits recorded

- 084d-5a and 084d-5b: in `notes/084-procedure-review-ui.md`.
- 095e: contract §13.
- 095f:
  - The persister's no-note Warning also fires for a from-document Correction (097b), which carries no note, until the owner decides that route.
  - The column script must run before any deploy beyond dev.

## Standing gates

- A live Dataverse write or role change needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (also ontology-r1 for `sprk_policyversion`).
- The access-invariant check lives only in session memory. Run it after every solution import; record pass or fail only.
- **Agents:**
  - At most 4, and name the model for each.
  - Launch only from the worktree root.
  - `isolation: worktree` starts on MASTER, so every brief names its base sha.
  - Remove an agent's worktree with `Remove-WorktreeSafe.ps1` after `git worktree unlock`.
- Leftover folders that could not be deleted (permission denied; git registration removed): `C:\tmp\wt-iss021`, `C:\tmp\wt-ciw` (partial), `...\.claude\worktrees\agent-a7122a26394c93f4f`. A human can delete them.
- NEVER run python. Never use `--no-verify` or a bare `git stash`.
- The repo is public: describe security and timing findings by mechanism only, and only privately to uac-r2.
