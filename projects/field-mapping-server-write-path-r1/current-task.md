# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-09 (main session checkpoint at 85% context). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075. Latest: #2629 (57748ebf6d).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed at 6aca0f50a1. Merged since #2629, not yet on master: 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b (+ C1 comments), docs, POMLs 054 and 022b. Builds 0 warnings. |
| **Next PR to master** | That bundle + 045b + 022 (rounds 1–2). UAC-r2 has approved 032b-1, 032b-1b (C1 done). Before the PR: merge 045b and 022; fix the guard-1 failure below; `scripts/verify-suites.sh`; `scripts/publish-delta.ps1` against a fresh master; send UAC-r2 the combined diff; open the PR from a snapshot branch with auto-merge. The PR text must cite ADR-008 path A and state 032b-1's push side effect (the invoice display copies are never pushed) and the Office Assigned To targeting. |
| **Open defect (fix before PR)** | 045a guard 1 (`RegardingPairTypeImpliesIdGuardTests`) fails at `CommunicationService.cs:3023` (`CopyRegardingFromSourceAsync`, from 045c's reply copy) per the 022-2 agent. Check it on the work branch and fix (likely the reply copy needs the guard's allowed shape, or the guard an exception with a reason). |
| **Running agents** | (1) 045b fix round on `task/fm-045b-2`: 4 review findings (invoice lower-case nav `sprk_regardingrecordtype`; the push reads display values AS THE CALLER, denied → clear, fault → refuse that child, assert no app-only read; a failed read on a move refuses, not blanks; the `bindsAnIntermediate` test over `after`). Then a scoped re-check and merge. (2) Scoped Opus review of 054a lookup commit 6c1ad9cb16 (`task/fm-054a`); send the result to uac-r2, who merges that exact sha into uac-r2 task 198. |
| **Just finished, next action** | 022 round 2 on `task/fm-022-2` (f75e10e198 server rule, 4802e65456 wizard Secure lock, bd9694ecf7 notes). Next: TWO adversarial passes (security), then merge `task/fm-022-2` (it contains round 1) and send UAC-r2 the diff. Then start 022b (POML committed). Its choices to check: if a stripped mapped value is the row's filing value, the create is refused (no test); after a strip, the table's pre-write rules run again. |
| **Owner decisions 2026-11-08/09** | 032 option B; O1 inline; U7 accepted, then corrected: invoice Assigned To targets people without access (032b-1b). O2 gate move. Repair pass: only contacts get an owner rule (`ExternalAccess:CustomerBusinessUnitId` default team, fail closed), also for the chat contact create. Inbound mapping: gap (DI-022). NFR-02 verdict in UAT. Private item: owner chose (a), now task 054 (POML committed, UAC-r2 approved). Participants keep their shares when a Direct message is filed (except on secure/Restricted targets; UAC-r2's rule). |
| **Coordination** | uac-r2 is now the desktop session `uac-r2`; also `provisioning-r1`, `word-add-in-r1` (030b read OK), `ontology-r1`. Access defect details go to uac-r2 by message only. UAC-r2 task 198 = the share side of 054 (merges 6c1ad9cb16). The Direct team's role tables = exactly the rows 054a assigns: communication, attachment, participant, email review log, document, gate decision record. |
| **Gotchas** | Shell calls time out under agent load: use Grep/Read, timeouts ≥300 s, background runs. `timeout 400 git commit`. lint-staged backs up into the shared stash and drops it itself; never pop or drop. `gh --jq`, not `jq`. Never print a token. |

## Task state (detail in TASK-INDEX.md)

- **054:** 054a piece 1 (lookup + settings) done at 6c1ad9cb16. The rest of 054a waits for 045b's merge and UAC-r2's 198. 054b (thread-flag lock) and 054c (move script) need the owner's go for the live runs.
- **022b:** Restricted mapping source; document access permission set in the create. Starts after 022 round 2 merges; UAC-r2 reviews.
- **045d:** Q8 (invoice family re-derive) create and update halves; proposal in `notes/task-045c-notes.md`.
- **052b:** waits on 032b-1 (merged): scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; 032b-2 gets two adversarial passes; the diff goes to ebilling-r1. Repair display-copy never-map is in.
- **021:** deploy by UAC-r2 with the owner's go after master; live checks in its notes.
- **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Standing rules

- The repo is PUBLIC: access defects go to UAC-r2 by message only; repo text states rules.
- Live Dataverse writes need the owner's go in THIS session. No deploys from here.
- Never run Sprk.Bff.Api.Tests unfiltered: use `scripts/verify-suites.sh`.
- Every Agent call names a model; at most 4 agents at once.
- Every defect is fixed in scope, or filed and reported.
