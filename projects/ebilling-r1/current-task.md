# Current Task State

> **Last Updated:** 2026-11-15. Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-15a.md` (and `-2026-11-13a.md`).

## Owner mode

- "Continue with open work items that are available"; keep `notes/owner-testing-checklist.md` current.
- 2026-11-15, owner: "Proceed with 093; but also other work tasks that can run in parallel".

## NOW — read this first

### In flight

- **093 (firm file routes)**
  - Build: branch `worktree-agent-aa64df149f3786340` (dc2e5ab86c).
  - Two Opus adversarial reviews are done.
  - **Fix round 1 is running** in an Opus agent, on branch `worktree-agent-a69a2b98b7b63b95a`.
    - **A (uac-r2 ruling B):** a systemuser caller also needs Read on the INVOICE ROW through CallerRecordAccessProbe's gate variant, intersected with the root Read. None → uniform 404; fault → 503, never the inherit answer. Contacts are unchanged.
    - **B:** a firm-specific, server-side filtered Stored File read (no 50-row 503).
    - **C:** the vacuous (a′) guard check.
    - **D:** delete-own invoice-parent tests.
    - **E:** remove the dead upload-only code.
    - **F:** notes (no Content-Disposition; `.ProducesProblem(403)`).
    - **G:** stale ProcedureLibraryCiamDenyTests header.
    - **H:** known limits.
  - **On its arrival:**
    1. Check the diff.
    2. Re-check only the fix diff.
    3. Run `/conflict-check`.
    4. Send the head to uac-r2 for review (gate diff plus direct callers).
    5. Tell spa-r4 whether the DTO changed (today: value[] {id, fileName, contentType, sizeBytes, createdOn, scanStatus}).
    6. Merge into work, then open the PR work → master.
  - **Deploy order (strict):** spa-r4 has R4 task 021 built and merged first; then uac-r2 swaps in the BFF; then spa-r4 deploys the SPA immediately after.
- Nothing else is running.

### Done today, on work (pushed)

- **113 Procedure-change banner:** ✅, merged 84e708682c. One adversarial pass plus fix round. ontology-r1 PASSED the judged-dates helper extract (`Services/Signals/Billing/BillingJudgedDates.cs`); keep the Fee/Expense filter at the row→request boundary.
- **084h-1 writer entry** (`WriteRecordProcedureDecisionAsync`, `RecordProcedureActions`): merged df7dc16ac9. One adversarial pass plus fix round 1 (shared FindKeyedRecordsAsync / AlreadyRecordedOrRefuse helpers, JSON blank note). Billing tests untouched and green (549/549).
  - **Next:** send it to ontology-r1 for the path-B review, citing the amended `DecisionRecordContextImmutabilityTests`.
  - Mark the TASK-INDEX 084h-1 row ✅ after ontology-r1 PASSES.
  - Hand-offs H-1/H-2 to 084h-2/3 are in `notes/084h-1-writer-entry.md`.
- **084h and 095f part 2 designs:** ontology-r1 PASSED both after round 1 (73ce24172c).
  - 084h-2 never deploys without 084h-3 (the repair job, ADR-004 IJobHandler).
  - 084h-2 to 084h-5 are not started; no POMLs yet.
- **#2830 (095g) merged** to master acc96e1927. It is not on the dev BFF yet; it rides uac-r2's next deploy, which needs the owner's go in uac-r2's session.
- **#2833 ebilling slice:** `Consistency: Strong` added in scripts/ebilling (b80da28e20). It ships with the next PR.

### Open owner questions (asked 2026-11-15, no answer yet)

The owner was offered "as recommended" for all ten.

**084h:**
1. Pin-to-Follow → two records.
2. Accept the §8 classes and tiers.
3. DELETE note in a JSON body.
4. No control-character rule on the existing `reason`.
5. Ship the NotRecorded warning to the UI.

**095f part 2:**
6. With no note, the column takes the required reason.
7. A separate internal `sprk_decisionnote` column, not `sprk_correctionnote`.
8. Adjustments get Decision Records (option A).
9. Correct/confirm get a note on the correction row (option B).
10. Create Invoice wizard: server-only for R1 (option C).

**Premise correction to log:** EB-D-237 assumed the Create Invoice wizard has a Correction path. It has none and does not use from-document. The only from-document client is word-add-in-r1's emailInvoiceSubmission.ts (New only).

### Waiting on peers

- **word-add-in-r1** (offline; messages queued):
  - 096 gate: has the add-in removed the Invoice save target and invoice quick-create, plus one release since?
  - Will from-document offer Correction?
  - Slice C (the firm-assign pane) waits until 095g is on the dev BFF.
- **spa-r4:**
  - Accepts an optional `correction` prop on InvoiceSubmissionWizard, with conditions (095f design §13).
  - Builds R4 021 against the 093 contract once its task 013 is done.

## Next actions (in order)

1. When the 093 fix round reports, follow the "On its arrival" list above.
2. Send 084h-1 to ontology-r1 for the path-B review.
3. When the owner answers, update decisions-log (EB-D-239+), then:
   - write POMLs for 095f-2a … 2e and 084h-2/3/4;
   - build 095f-2a and 084h-2 + 084h-3 in parallel (max 4 agents).
4. Task 112 (deploy, owner go here plus uac-r2 notices):
   - the 084f role apply;
   - first create of `sprk_recordprocedures` plus the SpaarkeMaster AddSolutionComponent;
   - Billing-tab form placement.
   - `Deploy-EbillingInvoiceCorrectionNoteColumn.ps1` must run on any environment beyond dev before 095f goes there.
5. Still owed to ontology-r1: the DEF-059 role note, the DEF-060 Signals diff, the 176 fixture change.

## Standing gates

- **Live writes:** a live Dataverse write or role change needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (also ontology-r1 for `sprk_policyversion`). Merging a PR needs the owner's go.
- **Agents:**
  - At most 4; name the model for each.
  - Launch from the worktree root. `isolation: worktree` starts on MASTER, so each brief names its base sha.
  - Remove a worktree with `git worktree unlock` then `scripts/worktree/Remove-WorktreeSafe.ps1 -Path` (single-quoted Windows path).
- **Hard rules:** NEVER run python. Never `--no-verify` or a bare `git stash`.
- **Public repo:** describe security findings by mechanism only, and only privately to uac-r2. Never put access-gap mechanisms, identity names, role memberships or privilege maps in notes, commits, PRs or issues.
- **Ebilling Pester:** run on each file's declared version (5.7.1 / 3.4.0). Plain Invoke-Pester loads 6.2 and fails.
- **Leftover folders a human must delete:** `C:\tmp\wt-iss021`, `C:\tmp\wt-ciw`, `...\.claude\worktrees\agent-a7122a26394c93f4f`.
