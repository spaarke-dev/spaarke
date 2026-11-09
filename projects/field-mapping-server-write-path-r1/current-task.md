# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-09 (main session, after 045b / guard / pair-nav merges). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075. Latest: #2629 (57748ebf6d).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed at 0684bac564. Merged since #2629, not yet on master: 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b (+ C1), 045b (fix round 2, re-check no F), guard-1 fix (8988c2499e), pair-type bind spelling fix + guard (8e0b48eadb), docs, POMLs 054 and 022b. Builds 0 warnings. |
| **Next PR to master** | That bundle + 022 (rounds 1–4). Before the PR: merge 022; `scripts/verify-suites.sh`; `scripts/publish-delta.ps1` against a fresh master; send UAC-r2 the combined diff; open the PR from a snapshot branch with auto-merge. PR text: ADR-008 path A; 032b-1's push side effect (invoice display copies never pushed); Office Assigned To targeting; 022's push gate; the one-line ontology-owned change in `DecisionActionExecutors.cs` (ontology-r1 told). |
| **Open defect (fix before PR)** | None open. Guard 1 fixed (8988c2499e, guard only). The Signal pair-type bind spelling is fixed; it never failed live (the create core matches bind keys ignoring case). |
| **Running agents** | (1) 022 fix round 4, the push gate, on `task/fm-022-4` (from `task/fm-022-3` 3f7505c4da + the work branch): per child `MappingSourceCopyGate.DecideAsync`, skip non-isolated children with a stable code in the result and preview, a fault skips the child; plus a sweep of other server copy paths. UAC-r2 asked for this as fix-now. |
| **Just finished, next action** | 022 fix round 3 done on `task/fm-022-3` (b8b0e5dc72, 946c55b68c, 7d042655f1, 3f7505c4da): strip-branch seam tests, copied-default keyed on step-3 mapped keys, `record_creation.mapping_source_secure_filing` 400, secure-source warning wording, no-Share wizard text. Next: when round 4 lands, run the SECOND adversarial pass (security, Opus) over `task/fm-022-4` vs the work branch, send UAC-r2 the diff, then merge it. Then start 022b. |
| **Owner decisions 2026-11-08/09** | 032 option B; O1 inline; U7 accepted, then corrected: invoice Assigned To targets people without access (032b-1b). O2 gate move. Repair pass: only contacts get an owner rule (`ExternalAccess:CustomerBusinessUnitId` default team, fail closed), also for the chat contact create. Inbound mapping: gap (DI-022). NFR-02 verdict in UAT. Private item: owner chose (a), now task 054 (POML committed, UAC-r2 approved). Participants keep their shares when a Direct message is filed (except on secure/Restricted targets; UAC-r2's rule). |
| **Coordination** | uac-r2 is now the desktop session `uac-r2`; also `provisioning-r1`, `word-add-in-r1` (030b read OK), `ontology-r1`. Access defect details go to uac-r2 by message only. UAC-r2 task 198 = the share side of 054 (merges 6c1ad9cb16). The Direct team's role tables = exactly the rows 054a assigns: communication, attachment, participant, email review log, document, gate decision record. |
| **Gotchas** | Shell calls time out under agent load: use Grep/Read, timeouts ≥300 s, background runs. `timeout 400 git commit`. lint-staged backs up into the shared stash and drops it itself; never pop or drop. `gh --jq`, not `jq`. Never print a token. |

## Task state (detail in TASK-INDEX.md)

- **054:** 054a piece 1 (lookup + settings) FINAL on `task/fm-054a-2`: 6c1ad9cb16 → 78d2c4076c → 7b5e914784 → ac6f1743b3 (ids only, Identify refuses Secure, Secure-not-found fails closed); re-check no F; UAC-r2 merged it into 198 (92adbf7ee6). Consumer rule (`IsIdentified` first) is owned by 198. 045b is now merged; the rest of 054a waits for UAC-r2's 198 Part C. 054b (thread-flag lock) and 054c (move script) need the owner's go for the live runs.
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
