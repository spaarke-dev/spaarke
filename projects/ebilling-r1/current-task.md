# Current Task State

> **Last Updated**: 2026-11-13 ~14:30Z (session `ebilling-r1`, Claude Desktop; the only writer to this worktree).
> The previous version, with history through 2026-11-13 04:30Z, is `notes/handoff-history/current-task-2026-11-13a.md`. Read it for older detail: the 116 rounds, the master-sync history, and the owed lists for word-add-in, provisioning and DEF-060.

## Quick recovery

- **Branch:** `work/ebilling-r1`, pushed; nothing uncommitted.
- **Owner mode:** "keep progressing on open tasks so we can complete the project".
- **Active task:** 084d, the before/after Impact dry run and Assign Procedure. It is split in `tasks/TASK-INDEX.md`, rows 084d to 084d-4:
  - 084d-1 (planning without writes): ✅ merged at `d24681ed38`.
  - 084d-2a (access, read, three POST routes) and 084d-2b (impact: `closes`, `affectedOpenInvoices`): ✅ merged at `c313c6c211`.
  - Tip gates: filtered BFF tests 8341/0/4, ArchTests 2570/0, build 0 warnings.
  - **084d-3** 🔲 NEXT: the parity matrix, mutation proofs, no-write and concurrency tests.
  - **084d-4** 🔲: the client. It adds the Impact step to both wizards, labelled "if saved now", plus the Assign Procedure wizard.
- **Design and review conditions:**
  - `notes/084d-dry-run-design.md`, including its review sections for uac-r2 and ontology-r1, which are binding.
  - In `notes/084-procedure-review-ui.md`: the "## 084d-1", "## 084d-2a" and "## 084d-2b" sections, and "### 084d-1 / 2a / 2b close". The close section lists the known limits that 084d-3 and 084d-4 must take: O-K1a, O-K1b and O-K3.

## Next action (exact)

1. **Launch 084d-3** with a fresh `implementer` (opus, high effort, `isolation: worktree`). The brief:
   - Base it on the current work tip, checked with `merge-base --is-ancestor`.
   - Parity matrix, with ontology-r1's conditions:
     - (a) a seed larger than one 5000-row page, or a smaller page size;
     - (b) the parent policy in a chunk that is not the last;
     - (c) a rig clock later than every seeded `createdon`.
   - Mutation proofs: dropping `createdon`, the chunk check, or the policy switch-on must each fail a test.
   - No-write tests and concurrent dry-run tests.
   - O-K3: extend the write-side token guard (`ProcedureAssignmentServiceTests` ~2246) to the `sprk_invoice`, `sprk_billingevent` and core-table reads.
   - O-K1a: today an unreadable comparison row gives a 503 for the whole preview. Decide with uac-r2 whether it should null the count instead, before changing anything.
   - Use the standard prohibitions.
   - When it is done, send the diff to ontology-r1.
2. **Then 084d-4.** It consumes the 2a and 2b DTOs; their shape is in the "## 084d-2b" notes.
   - R6 wording: "this preview isn't available for this firm".
   - A null count shows "can't be counted", never 0 or blank (O-K1b).
   - Nothing presents After as "will succeed".
   - The Assign wizard is for the billing admin only, with Follow latest or Pin.
   - jest tests.
3. **Then mark the 084d parent ✅**, and go to task 112: deploy, ui-tests, the access-invariant check from session memory, and the step 3 probe. Its live changes need the owner's typed OK.

## Owner questions OPEN (asked in chat 2026-11-13; no answer yet)

1. **DONE 2026-11-13: owner said "Yes, run it".** The script was applied on dev and read back, and uac-r2 had notices before and after. Remaining: SpaarkeMaster source, and 112's apply list.
   **Pending Spend, ISS-018 / #2777.**
   - `sprk_pendingspend` is missing on dev, on both matter and project (confirmed read-only), and from the SpaarkeMaster source.
   - Every recalculate logs a 400 on the Pending Spend PATCH. The counted rollup still saves.
   - Asked: may I run `scripts/ebilling/Deploy-EbillingPendingSpendSchema.ps1` on dev?
     - It is additive: two Money columns. `-WhatIf` runs first.
     - It needs the owner's typed OK, plus a uac-r2 notice before and after.
   - Follow-ups either way: put the columns in the next SpaarkeMaster version, and add the script to 112's apply list.
2. **How a wrongly filed invoice is corrected** (follows #2772 / ISS-017).
   - Today D-96 refuses a core change on an invoice that has lines. The only path is to detach the lines, move the invoice, move each line, and reattach.
   - Approval, submission, corrections and identifiers never move, so the family splits.
   - **Option 1 (recommended):** never move an invoice that has any related record, and drop the detach hint. Correction is return-and-resubmit, which fits EB-D-148.
   - **Option 2:** one atomic server "move invoice family" action. That is a new task, with uac-r2 review.
   - Either way, a child-core-equals-invoice rule follows for approval, submission, correction and external identifier. field-mapping-r1 is HOLDING it until this is decided.
   - On the decision: file it with `/project-defer-issue-tracking` and tell field-mapping-r1.

## Peers and reviews in flight

- **field-mapping-r1:**
  - 045d is merged in their work branch (we PASSed after round 5; uac-r2 re-PASSed).
  - 045e (#2772, refuse a disagreeing pair): we PASSed at `14a98aadb9`, and uac-r2 PASSed. Our one note: name the case "URL cleared while the id stays" in their K5d.
  - The child-core rule is held for owner question 2.
- **uac-r2:**
  - Submit Invoice step A comment done (`0a7aacf1d5`).
  - K-6 filed as ISS-019 / #2782 (NFR-01 for a named firm, assign and dry-run routes).
  - The dev BFF is `0c06f464e3` (master `92d0ca09f7` plus the mcp spike) since 2026-11-13 13:26Z. That carries no 084, so no access check was needed.
- **ontology-r1:**
  - 084d-1, 2a and 2b reviewed OK; send them 084d-3 next.
  - Told about ISS-018.
  - Still owed to them: the DEF-059 role note, the DEF-060 Signals diff, and the 176 fixture change.
- **provisioning-r1:**
  - #2780 (task 269) edits `Set-CustomerOwnerTeams.ps1` and its tests, which our unmerged `02e4d9396f` (EB-D-220) also edits. Their trial merge was clean, Pester 81/81. Re-run Pester after our next master merge.
  - Their S26 needs SpaarkeMaster 1.3.1.0 INSTALLED on each stamp.
- **spa-r4:** ping them before task 134 starts. Its trigger has fired: it becomes an `OutcomeSection` in `record/sections/`.

## Filed this session

- ISS-018 #2777 (Pending Spend)
- ISS-019 #2782 (NFR-01 named firm)
- ISS-017 #2772: decided as refuse; built in field-mapping's 045e.
- DEF-067 #2770 (other file types)

## Rulings in force (detail in `notes/decisions-log.md`)

- **EB-D-218:** the read routes read app-only, then run ONE batched caller check over every exposed row. A refusal or a fault gives 503. `Exempt` covers only the exact synthetic rows.
- **EB-D-221:** the read routes carry the caller's write permissions. **EB-D-222:** only ApprovedRate refuses Change for this record.
- **D-170:** only switching a rule Off closes findings; a plain Exclude closes nothing.
- **D-96:** an invoice core change is refused while any line names another core. Lines match their invoice, and a superseding invoice shares the superseded one's core.
- **D-70, EB-D-214, EB-D-215, EB-D-216/216a:** see the archived file.

## Remaining tasks

- 084d-3 (built `task/084d-3` 06139f6aca, gates green; review running, then ontology-r1, then merge), 084d-4.
- 095e, 095f, 095g (owner rulings EB-D-223..226, 2026-11-13). 095g inputs: `notes/095g-assign-firm.md`; design note to uac-r2 before code.
  word-add-in-r1 (their task 134) is owed: each master SHA and dev SHA for 095e (banner `firm {id,name}`), 095f contract (max length, missing-note rule), 095g contract after uac-r2 agrees.
- 112 (deploy and ui-tests).
- 079 (role expectations list the two Reads).
- 131–135 (134: ping spa-r4 first).
- 093, 096, 113, 117, 121, 123, 124.
- Deploy tasks 053, 136, 153.
- 160, 162, 163, 189, 190.
- DEF-060 build.
- The `sprk_sourcedocument` delete.
- Drift on 050a/161.
- DEF-067 design.

## Standing gates

- The creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master and L6 passes.
- No stamp gets a BFF carrying 116 until SpaarkeMaster 1.3.1.0 is INSTALLED on it. 1.3.1.0 is published.
- A live Dataverse write or role change needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (and ontology-r1 for `sprk_policyversion`).
- The access-invariant check lives ONLY in session memory (`eb-d-218-access-invariant-check.md`). Run it after every solution import; record pass/fail only.
- Agents:
  - At most 4. Name the model and launch from the worktree root.
  - `isolation: worktree` starts on MASTER, so every brief names its base sha.
  - Remove a worktree only with `scripts/worktree/Remove-WorktreeSafe.ps1`, after `git worktree unlock`.
- NEVER run python in any form, agents included. Never use `--no-verify` or a bare `git stash`.
- The repo is public: describe security findings by mechanism only. Private findings go to uac-r2 by message.
