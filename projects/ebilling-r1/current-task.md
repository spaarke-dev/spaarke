# Current Task State

> **Last Updated:** 2026-11-16. Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-16a.md` (and `-2026-11-15a.md`, `-2026-11-13a.md`).

## Owner mode

- "Continue with open work items that are available"; keep `notes/owner-testing-checklist.md` current.

## NOW — read this first

### Active: task 130e-1 (DEF-060 writer, server side)

- **Branch `task/ebilling-130e-1`**, base work 4e26b3000f. The build agent `ebill-130e1` (Opus implementer) works in `C:\code_files\spaarke\.claude\worktrees\agent-a26a8f842e71ef60b`. Never cd there. Remove it after merge with `git worktree unlock` then `Remove-WorktreeSafe.ps1`.
- POML: `tasks/130e-firm-description-writer.poml`. Design: `notes/def-060-firm-description-design.md` (§11 C1-C4, O1-O6, EB-D-213 unsecured). Implementer notes: `notes/130e-firm-description-writer.md` on the branch.
- **History:**
  - Built at 89ddc86188. Opus adversarial review, then fix round 1 → 99e380f997.
  - Sonnet re-check: PASS. ontology-r1 O6: PASS, for 99e380f997 only (a later push touching `Services/Signals/**` needs its re-glance).
  - uac-r2 (access and disclosure): PASS-WITH-FIXES.
- **Fix round 2 (sent to `ebill-130e1`, RUNNING):**
  - F1: the record-rule copy passes the base rule set name (`ProcedureAssignmentService.cs:821`).
  - F3: the C4 name check normalises with NFKC and collapses whitespace.
  - F3b: the guard entry for `sprk_rejectionreason` must say it is not yet in SpaarkeMaster (ISS-024).
  - F4: `.ProducesProblem(422)` on the RuleSetEndpoints routes.
  - Notes K1/K2 (uac-r2) and K2/K4 (ontology-r1).
- **Next when the agent reports:**
  1. Check its diff.
  2. Send the fix SHA to uac-r2 for a fix-diff re-check. If `Services/Signals/**` changed, ontology-r1 gets a re-glance.
  3. On PASS, merge `task/ebilling-130e-1` into work. The dev column is in place, so the merge is safe. Run the script's `-Verify` on dev as a merge-step check.
  4. Add K2/K4 to the notes if the agent didn't, mark TASK-INDEX 130e-1 ✅, and remove the agent worktree.
- **Then 130e-2:** the Rule Review editor field for `firmDescription`.
  - Render as plain text.
  - Show the "firms see this" notice.
  - Explain that a re-sent invalid published text is dropped, and that short names can over-refuse.
  - The contract is in the 130e notes.
- **Deploy order:** every `sprk_policyversion` read selects `sprk_firmdescription`. Any environment needs the column before a BFF carrying 130e-1. `PolicyVersionColumnsGuardTests` keeps `sprk_firmdescription` and `sprk_rejectionreason` on its pending list until task 079 syncs both into SpaarkeMaster `Entity.xml` (ontology-r1 agreed: 079, not sooner).

### Done 2026-11-16 (pushed)

- **ISS-022 / #2844 DONE.**
  - The fix is live on dev. The dev invoice index got task 152's fields (`Deploy-AllIndexes.ps1`, in place; `-VerifyOnly` passes).
  - The re-index sweep (`scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1`) indexed all 12 invoices; uac-r2 confirmed 12 handler traces. The search dependency telemetry under-counts; the handler traces are authoritative.
  - uac-r2's post-deploy gate now runs `-VerifyOnly`.
  - ISS-023 / #2849 proposes the same check in `Deploy-BffApi.ps1`.
  - Dead letters: #2848 (job platform; don't replay InvoiceIndexing ones).
- **ISS-024 / #2858 DONE on dev.** The dev BFF selected `sprk_policyversion.sprk_rejectionreason` (task 077), which had never been created on dev.
- **Both columns live on dev** (owner: "Yes, add both"; uac-r2 and ontology-r1 notified before and after):
  - `sprk_rejectionreason` (077 script) and `sprk_firmdescription` (130e-1 script @99e380f997, `-SolutionUniqueName SpaarkeMaster`, `-Verify` PASS).
  - Both are Memo 2000, not required, not secured. Each publish touched `sprk_policyversion` only. Reads naming them return 200.
  - Both are in SpaarkeMaster and OntologyPlatformSolution (each includes the table with subcomponents).
- Earlier, 2026-11-15: 113 + 113b, 084h-1, 093, #2846, #2839. See the archive.

### Open owner questions

1. **Ten design answers** (asked 2026-11-15; "as recommended" offered for all):
   - 084h:
     1. Pin-to-Follow → two records.
     2. Accept the §8 classes and tiers.
     3. DELETE note in a JSON body.
     4. No control-character rule on the existing `reason`.
     5. Ship the NotRecorded warning to the UI.
   - 095f part 2:
     6. With no note, the column takes the required reason.
     7. A separate internal `sprk_decisionnote` column.
     8. Adjustments get Decision Records (option A).
     9. Correct/confirm get a note on the correction row (option B).
     10. Create Invoice: server-only for R1 (option C).
2. **Second `word-add-in-r1` session.** A session ON THIS MACHINE now claims the name `word-add-in-r1`. The confirmed one is remote (bridge). I did not confirm the local one. Ask the owner whether they started it; until then, message only the remote one.

**Premise correction to log with EB-D-239+:**
- EB-D-237 assumed the Create Invoice wizard has a Correction path; it has none.
- from-document's client is the shared `emailInvoiceSubmission.ts`, used by OUR CommunicationActions PCF (task 118) and the EmailWorkspace, not the add-in.
- So whether from-document offers Correction is ebilling's call; fold it into 095f part 2.

### Waiting on peers

- **word-add-in-r1 (remote):** the 096 gate is NOT met. The add-in still offers Invoice as a Save target and quick-create. Removal is slice C (#2829), which waits for the owner's merge go in its session. Keep 096's server legs.
- **spa-r4:** the SPA deploy confirmation for checklist E. It accepts the optional `correction` prop (095f §13). It builds R4 021 against 093.

## Next actions (in order)

1. Finish 130e-1 (above), then 130e-2.
2. When the owner answers the ten:
   1. Log EB-D-239+ and the premise correction.
   2. Write the POMLs for 095f-2a…2e and 084h-2/3/4.
   3. Build 095f-2a with 084h-2 + 084h-3 (max 4 agents). 084h-2 enforces the 2000-character note cap at the service (H-3); it never deploys without 084h-3.
3. Task 112 (deploy; owner go here plus uac-r2 notices):
   - the 084f role apply;
   - the first create of `sprk_recordprocedures` plus the SpaarkeMaster component;
   - the Billing tab;
   - the Invoice Review page's first deploy (with 113b).
   - Before 095f goes beyond dev: the correction-note column script.
4. Task 079 must sync `sprk_firmdescription` and `sprk_rejectionreason` into SpaarkeMaster and empty the guard's pending list.

## Standing gates

- **Live writes:** a live Dataverse, Azure or index write, or a role change, needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (and ontology-r1 for `sprk_policyversion` / Signals). Merging to master needs the owner's go.
- **Agents:**
  - At most 4; name the model for each.
  - Launch from the worktree root. `isolation: worktree` starts on MASTER, so each brief names its base sha.
  - Remove a worktree only via unlock + `Remove-WorktreeSafe.ps1`.
- **Hard rules:** NEVER run python. Never `--no-verify` or a bare `git stash`.
- **Public repo:** security findings by mechanism only, and only privately to uac-r2. No identity, role or privilege names in notes, commits, PRs or issues.
- **Ebilling Pester:** run each file on its declared version (5.7.1 or 3.4.0). Plain Invoke-Pester loads 6.2 and fails.
- **Leftover folders a human must delete:** `C:\tmp\wt-iss021`, `C:\tmp\wt-ciw`, `...\.claude\worktrees\agent-a7122a26394c93f4f`.
