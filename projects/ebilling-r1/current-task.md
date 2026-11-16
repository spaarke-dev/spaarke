# Current Task State

> **Last Updated:** 2026-11-15. Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-15a.md` (and `-2026-11-13a.md`).

## Owner mode

- "Continue with open work items that are available"; keep `notes/owner-testing-checklist.md` current.
- 2026-11-15, owner: "Proceed with 093; but also other work tasks that can run in parallel".

## NOW — read this first

### In flight

- **Dev BFF deployed 2026-11-15 18:32Z: aa7d6345f3** (master c1eb6e4a8e = 095g #2830 + #2839 + R4 021 #2840, plus mcp's spike). uac-r2's 30-min gate passed: 42 requests, 0 5xx, and the external invoice file routes returned 200. word-add-in-r1 was told 095g is live (queued; it is offline). The SPA deploy is spa-r4's, and confirmation is pending. Checklist section E becomes testable once spa-r4 confirms.
- **#2844 / ISS-022 (found by the gate):** the invoice index job selected the removed `sprk_invoice.sprk_document`, so every invoice re-index has failed since task 094 (2026-10-27).
  - Fix d7f36ddd6d on work, with regression test `Issue2844_*` and ArchTest `InvoiceIndexColumnsGuardTests`.
  - Sonnet review: no defects. BFF build has 0 warnings. ArchTests 2633/0. Master merged in.
  - **#2846 MERGED to master at c88ed7105a** (2026-11-15). 43 checks passed; the only failure was the known Office-scope 20-min timeout. uac-r2 has the SHA. Next: uac-r2's BFF deploy (owner go in its session). The sweep has NO existing operator path; it needs a small script that queues the normal InvoiceIndexing job per invoice (owner go before running it). uac-r2 has the number. Then a BFF deploy (owner go in uac-r2's session).
  - **Then the re-index sweep:** every sprk_invoice with modifiedon ≥ 2026-10-27, through the normal indexing job. It writes to the dev search index, so it needs **the owner's go HERE**. Report the count and the result to uac-r2.
- **Re-index sweep script ready** (fb16a2d0cc on work): `scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1`.
  - A dry run by default counts only. `-Apply` queues the BFF's own InvoiceIndexing job for each invoice on `spaarke-servicebus-dev/sdap-jobs`.
  - Pester 5.7.1: 11/11. The script consistency guard passes.
  - **Dev dry run 2026-11-15: 12 invoices.** sdap-jobs exists and has no duplicate detection, so a re-run re-queues, which is harmless.
  - **Run `-Apply` only after uac-r2 deploys c88ed7105a, and on the owner's go HERE.** It needs the "Azure Service Bus Data Sender" role on the namespace.
  - Then report the count and the result to uac-r2.
  - FYI: sdap-jobs holds 4085 dead letters (pre-existing, not triaged).
- **113b ✅ on work (owner go 2026-11-15):** the Invoice Review page now shows 113's Procedure-change banner (`ProcedureChangeNotice.tsx`). One Sonnet adversarial pass found no F-class defects; its unknown-state point is closed. It ships with task 112, the page's first deploy.
- **Reminder for 084h-2 (ontology-r1):** the writer caps only `reason` at 2000 characters, not `note`. 084h-2 must enforce or trim the note at the service (design §9), so an over-long note never reaches the create and fails as a retryable CreateFailed.

### Done today, on work (pushed)

- **113 Procedure-change banner:** ✅, merged 84e708682c. One adversarial pass plus fix round. ontology-r1 PASSED the judged-dates helper extract (`Services/Signals/Billing/BillingJudgedDates.cs`); keep the Fee/Expense filter at the row→request boundary.
- **084h-1 writer entry** (`WriteRecordProcedureDecisionAsync`, `RecordProcedureActions`): merged df7dc16ac9. One adversarial pass plus fix round 1 (shared FindKeyedRecordsAsync / AlreadyRecordedOrRefuse helpers, JSON blank note). Billing tests untouched and green (549/549).
  - Fix round 2 (ontology-r1's F3-1, F3-2 and item 3) merged at e841d90118; ontology-r1 PASSED it. TASK-INDEX row ✅.
- **093 firm file routes:** ✅, merged a95f7e7297; uac-r2 PASSED it. TASK-INDEX row ✅.
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

1. Open the master PR (see In flight), ask ontology-r1 to sign off once CI is green, and get the owner's merge go.
2. When the owner answers, update decisions-log (EB-D-239+), then:
   - write POMLs for 095f-2a … 2e and 084h-2/3/4;
   - build 095f-2a and 084h-2 + 084h-3 in parallel (max 4 agents).
3. Task 112 (deploy, owner go here plus uac-r2 notices):
   - the 084f role apply;
   - first create of `sprk_recordprocedures` plus the SpaarkeMaster AddSolutionComponent;
   - Billing-tab form placement.
   - `Deploy-EbillingInvoiceCorrectionNoteColumn.ps1` must run on any environment beyond dev before 095f goes there.
4. Still owed to ontology-r1: the DEF-059 role note, the DEF-060 Signals diff, the 176 fixture change.

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
