# Current Task State

> **Last Updated:** 2026-11-14, about 03:00Z. Session `ebilling-r1`, Claude Desktop, the only writer to this worktree.
> Older detail is in `notes/handoff-history/current-task-2026-11-13a.md`.

## Quick recovery

- **Branch:** `work/ebilling-r1`.
- **Local HEAD:** `bb28870b76`, plus uncommitted notes (listed below).
- **Not pushed:** everything since `e22ac1f1aa`, including these merges:
  - `8f5ff019fa` (084d-5a)
  - `c6bf88d98a` (EB-D-232)
  - `f86871820a` (084d-5b)
  - `7953d052d9` (095e)
  - `bb28870b76` (095f part 1)
- **Owner mode:** "continue with open work items that are available", plus a testing checklist (`notes/owner-testing-checklist.md`, uncommitted).

### Uncommitted, to commit when the gates are green

- `tasks/TASK-INDEX.md`: 084d-5a, 084d-5b and 084d-5 ✅; 095e ✅; 095f 🔄.
- `notes/084d-dry-run-design.md`: the K6 lines.
- `notes/084-procedure-review-ui.md`: "084d-5a close" and "084d-5b close".
- `notes/095g-assign-firm.md`: the design for uac-r2, and uac-r2's approval with R1 and R2.
- `notes/owner-testing-checklist.md` (new).
- This file.

## Gates

- The first full run on `c6bf88d98a` had 415 failures. All the visible ones are 3-5 minute timeouts in Office, Insights, Memory and Documents tests, from machine load. A sample of 143 re-ran green on its own.
- Re-run: Bash task `bkrmcr45w`. It covers the full `Sprk.Bff.Api.Tests` with a trx file, plus ArchTests, on `7953d052d9`. The trx goes to `scratchpad/trx/full-7953d0.trx`.
  - If anything fails, re-run exactly those tests on their own.
  - Then run the 095f suites on `bb28870b76`: the agent got 4639/0 and ArchTests 2570/0.
  - Then commit the notes and push.
- uac-r2's 084d-5a merge condition is "full gates green".

## Next actions (in order)

1. **Gates green**: commit the notes, then push work.
2. **Merge origin/master into work.** It brings:
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
5. **PR work to master** (095e, 095f, 084d), then send the master SHA to word-add-in-r1 and spa-r4.
6. **095g build** once the owner answers Q2, Q3, Q8, Q9 and Q10.
   - uac-r2 APPROVED the design with R1 (Append on the record for an add) and R2 (the pane states the access effect on every add).
   - The build needs two adversarial passes; uac-r2 reviews the PR.
7. **095f part 2** (the Invoice Review steps) once the owner answers:
   - Should Approve get a note?
   - Do line-level reasons count?
   - Should a from-document Correction (097b) need a note?
   - The builder's suggested split is 2a (lifecycle extension + reject/return), 2b (adjustment) and 2c (override + blocked return). Overwrite with the latest reason; Decision Records keep the history.
8. DONE 2026-11-14 03:45Z: `sprk_createinvoicewizard` redeployed to dev from master `cfa97d718b`. The owner's go was typed. uac-r2 got notices before and after. Recorded in `notes/053-wp2-deploy.md`.
9. Then task 112 (deploy). The 095f hold is RELEASED: word-add-in-r1's pane, which sends `correctionNote`, is live on dev at 942b2889bf (#2815 759dc4b1f6, #2820). `Deploy-EbillingInvoiceCorrectionNoteColumn.ps1` must run on every environment beyond dev before 095f goes there.

**Master now:** `cfa97d718b`. It includes 045g (#2819, merged at b76276a314). Close #2794 once checked (field-mapping-r1).

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
