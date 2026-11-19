# Task 052f — Assign-cascade allow-list and per-row child check

POML: `tasks/052f-repair-assign-cascade-allow-list.poml`. Rigor: FULL. Design: `notes/task-052e-design.md` §3.1, §3.2 (owner
2026-11-18: Q1–Q8 yes, Q2 yes). Nothing here enables the pass on any environment: `RecordRepair:Pass:GoLive` stays unset, no
live Dataverse call was made, nothing was deployed. Line numbers are as of this commit; re-grep before use.

## What changed

**Before.** One relationship that cascades on Assign to a child not on `AcceptedAssignCascades` (empty) blocked the owner write
of every row of that table: no matter, project, event, document, invoice, analysis or contact was re-owned.

**After.** Repair re-owns a row unless it actually has a child along a relationship the allow-list does not accept.

| Rule | Where (`Services/RecordCreation/RecordCreationService.Repair.cs` unless named) |
|---|---|
| Allow-list: exactly the design §3.1 "moves nothing" and "follows the record" entries (20), each with its reason as its value | `AcceptedAssignCascades` :326 |
| Never-accepted lists, enforced at runtime: every activity table, actioncard, socialprofile, mailboxtrackingfolder, syncerror, `sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision` (any parent); `contact->contact`, `contact->annotation` | `ActivityTables` :360, `NeverAcceptedAssignCascadeChildren` :373, `NeverAcceptedAssignCascades` :386, `IsAcceptedAssignCascade` :391 (a never-accepted relationship is refused even if added to the allow-list) |
| Per-row reads: one `top 1` read per unaccepted (child table, referencing column); every activity relationship on `regardingobjectid` folded into one `activitypointer` read; accepted relationships never read | `ChildReadsOf` :406, `ReadAssignCascadeChildAsync` :602 |
| Child present → owner kept, `record_repair.owner_assign_cascades`, one log line per row naming the relationship schema names | `AssignCascadeCheckAsync` :448 |
| Fail closed → `record_repair.owner_assign_cascade_unknown` (transient, retried): metadata read fault (once per table per pass, as before), a child read that throws (fault or refusal), a read with no answer, a relationship whose metadata does not state its child table or referencing column | `AssignCascadeCheckAsync` :448–540 |
| Unstated Assign value counts as cascading: `AssignCascadesOf` lists it, Repair never filters on the Assign value | `DataverseServiceClientImpl.AssignCascadesOf`, `ChildReadsOf` |
| The check runs before the mapping reads; the reads it made are kept | :182–198 (check), :205 (mapping), :255 (write) |
| Race: after a confirmed owner write, each kept read is asked again; a child found is logged (`record_repair.owner_assign_cascade_raced`, child table — the activity type for `activitypointer` — and id, relationship names) and returned on the outcome; a re-read that fails sets `RaceUnchecked` (logged as an error). Nothing is undone (K1) | :287–291, `AssignCascadeRaceAsync` :548 |
| Counts: `ownerAssignCascadesByTable`, `ownerAssignCascadeRaced`, `ownerAssignCascadeRaceUnchecked` in the run result and the heartbeat | `RecordRepairPassJob.cs` `Count` :860–868, heartbeat :969–982, result :1012–1014, `RunCounts` :1073 |
| The metadata record carries the referencing column from the same metadata read | `Spaarke.Dataverse/Models.cs` `AssignCascadeRelationship.ReferencingAttribute` (optional 4th member, default `null`); `DataverseServiceClientImpl.AssignCascadesOf` |
| Removed: the once-per-pass table-level refusal log (`RecordRepairPassContext.FirstAssignCascadeRefusal`): the refusal is per row now and is logged per row | `RecordRepairPassContext.cs` |

**Not changed.** The cross-unit skip `owner_moves_business_unit` (052g). The single owner write site (`UpdateIfRowVersionMatchesAsync`, :255) and the owner-write census. No UAC-r2 file. The pass tables and exclusions.

## The query seam (why the extension)

Child reads go through the existing `IGenericEntityService.RetrieveMultipleAsync(QueryExpression)`, app-only, `TopCount = 1`,
`NoLock = false`, `ColumnSet(false)` (`activitypointer`: `activitytypecode`, to name a raced activity's type). No new method on
the interface. The one extension is a member on the existing record `AssignCascadeRelationship`: `ReferencingAttribute`,
projected from the `RetrieveEntityRequest` the metadata read already makes. Why: the per-row read needs the child's lookup
column, and the metadata already returns it; reading it again or hard-coding it per relationship would duplicate the source
of truth. A relationship whose metadata does not state it is unknown, never skipped.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

- **Placement:** inside the existing `RecordCreationService` Repair partial and the existing `RecordRepairPassJob` counts. No
  new endpoint, service, DI registration, job, store, package, route or role. The read runs in the existing ADR-036 job, under
  its lease and claims; on-demand (052c) runs the same code.
- **Publish-size delta / CVE check:** main session's (no package change here).

## Component justification (CLAUDE.md §11)

1. **Existing** — `AcceptedAssignCascades` and the table-level `AssignCascadeRefusalAsync` in Repair; `GetAssignCascadesAsync`
   (052b); `AssignCascadeChildOwners` (UAC-r2's snapshot/restore for secure roots, which reads the three root cascades on
   `_regardingobjectid_value` through the Web API client).
2. **Extension** — the allow-list is filled and the refusal narrowed to the row in the same method; the child-presence read is
   the existing app-only `RetrieveMultipleAsync`; the record gains one member from the read it already makes. Not
   `AssignCascadeChildOwners`: it is a snapshot of owners for three root tables over the Web API, refuses other tables by design,
   and is UAC-r2's.
3. **Cost of doing nothing** — every row of seven pass tables keeps its creator as owner, so the repair pass cannot give a
   natively created matter, project, event, document, invoice, analysis or contact its standard owner (FR-25 acceptance).

## Must-fire evidence (each rule switched off, build, run, restore)

Script: scratchpad `mustfire.sh` (switch-off by `sed`/`perl` on a copy, the files restored from the copy after each round;
never `git stash`). Filter: `RecordRepairPassJobTests|AssignCascadeProjection`.

| Round | Rule switched off | Failed (of 153) | Tests that fired (examples) |
|---|---|---|---|
| M1 | Allow-list: `IsAcceptedAssignCascade` accepts nothing | 9 | `AC2_ARowWithOnlyAcceptedCascades_*` (×3), `AC3_AnEventWithANote_*`, `AC3_AnAnalysisWithANote_*`, `AC2_AContactWithNoUnacceptedChild_*`, `AC1_TheChildReads_*`, `AC1_AC10_*`, `AC2_AnInvoice*` |
| M2 | Never-accepted guard off and `contact->annotation` added to the allow-list | 12 | `AC1_TheAllowList_HoldsExactly*`, `AC1_AC10_NoNeverAccepted*`, `AC10_ANeverAcceptedRelationship_IsNotAccepted(contact, annotation)`, `AC3a_AContactWithANote_*`, `AC6_*` (×4), per-table count |
| M3 | A child present does not keep the owner | 12 | `AC4_*`, `AC5_*`, `AC3a_*`, `AC8_*`, `AC10_ARowWithANeverAcceptedChild_*` (×5), `AContactWithAnActivityChild_*`, `ARowWithAnUnacceptedChild_*`, per-table count |
| M4 | A child read that throws is read as "no children" | 4 | `AC6_AChildReadThatFails_*` (timeout, privilege refused, HTTP refused), `AC7_*` |
| M4b | A read with no answer is read as "no children" | 1 | `AC6_AChildReadThatFails_*(no answer)` |
| M5 | A relationship without its referencing column is skipped | 1 | `AC6_ARelationshipWithoutItsReferencingColumn_*` |
| M6 | No `activitypointer` fold | 9 | `AC1_TheChildReads_*`, `AC4_*`, `AC9_AChildAddedAfterTheCheck_*`, `AC10_*(task)`, `AContactWithAnActivityChild_*`, `AC7_*` |
| M7 | Relationships with an unstated Assign value dropped | 2 | `AC8_*`, `ARowWithAnUnacceptedChild_StillGetsItsMapping_*` |
| M8 | No re-read after the owner write | 7 | `AC9_*` (×3), `AC2_AContactWithNoUnacceptedChild_*`, `AC2_TheMetadataIsReadOnce*` |
| M9 | No per-table count | 1 | `TheRunCountsRowsKeptForAnUnacceptedChild_PerTable` |
| M0 | Restored | 0 | 153 passed |

## Known limits

- **K1 — the race is not undone.** A child added along an unaccepted relationship between the check (before the mapping reads)
  and the owner write moves with the Assign. The re-read after the write logs it with its table and id and counts it
  (`ownerAssignCascadeRaced`); nothing moves it back. UAC-r2 U2: the log and count are enough; a raced child goes to the
  parent's new owner, which the family rule gives anyway, and a parent in an isolation state is never written (052g's guard).
- **K2 — skipped rows are not revisited by the window.** A row kept as `owner_assign_cascades` is an answer, not a fault: it is
  not in the retry set, and the `createdon` window has passed it, so removing its child later does not bring it back. The 052c
  on-demand run over its window retries it.
- **K3 — depth.** An app-only read sees only what the BFF app user's Read depth reaches; a read limited by depth returns fewer
  rows without an error, and the code cannot tell that from "no children". Held by design §6 item 5 (organization-level Read on
  `activitypointer`, `annotation`, `contact`, and each per-row table, checked before enable). A read Dataverse refuses for a
  missing privilege is unknown (tested). See "Open point" below.
- **K4 — a refused per-row table blocks its rows.** If an environment refuses the read of any table the check reads for a
  contact (for example `mailboxtrackingfolder` or `syncerror`), every contact there is unknown, retried, and abandoned after 12
  attempts (listed in the 052d report). Fail closed; part of the §6 item 5 check.
- **K5 — invoices.** In this build every invoice-family write is held by the invoice-family guard (`InvoiceFamilyUpdateRefusalAsync`,
  design §3.6), so an invoice's owner is not written whatever the cascade check says. The check refuses nothing and reads
  nothing for an invoice (only `->team`, accepted).

## Open point (needs a decision; not designed around)

AC6's "depth-limited read" cannot be detected from the read's result. Two options: (a) keep design §6 item 5 as the control
(the pre-enable check that the app user holds organization-level Read on each table read); (b) add an app-only privilege-depth
read per table per pass (a new `IGenericEntityService` read: the table's Read privilege from its metadata, and the identity's
depth for it), failing closed to unknown when it is not organization-level. (b) is a design addition with platform behaviour to
confirm (roles held through teams, administrator roles), so it is not in this task.

## Tests

New: `tests/integration/data-mutation/RecordOwnership/RecordRepairAssignCascadeTests.cs` (a partial of
`RecordRepairPassJobTests`, the existing harness idiom, plus the `RepairWorld` cascade-children seam). Updated:
`RecordRepairPassJobTests.cs` (the 052b cascade tests now per row; the "list starts empty" test replaced by the exact-list
test), `RecordRepairModeTests.cs` (the world answers child reads), `tests/unit/domain/Dataverse/AssignCascadeProjectionTests.cs`
(the referencing column is carried).

| AC | Test(s) |
|---|---|
| AC1 | `AC1_TheAllowList_HoldsExactlyTheDesignEntries_EachWithItsReason`, `AC1_AC10_NoNeverAcceptedRelationship_IsOnTheAllowList_AndTheNeverListsHoldTheDesignEntries`, `AC1_TheChildReads_SkipEveryAcceptedRelationship_AndFoldEveryActivityIntoOneActivityPointerRead`, `AC2_ARowWithOnlyAcceptedCascades_IsReOwned_WithNoChildRead` |
| AC2 | `AC2_ARowWithOnlyAcceptedCascades_IsReOwned_WithNoChildRead` (matter, project, document), `AC2_AContactWithNoUnacceptedChild_IsReOwned_AfterOneTopOneReadPerUnacceptedRelationship`, `AC2_AnInvoiceWithOnlyAcceptedCascades_IsNotRefusedForThem` (K5), `AC2_TheMetadataIsReadOncePerTablePerPass_TheChildrenOncePerRow` |
| AC3 | `AC3_AnEventWithANote_IsReOwned_AndTheNoteRelationshipIsNeverRead`, `AC3_AnAnalysisWithANote_IsReOwned_AndTheNoteRelationshipIsNeverRead` (the move itself is Dataverse's; checked live before enable, design §6) |
| AC3a | `AC3a_AContactWithANote_IsNotReOwned_AndIsCounted` |
| AC4 | `AC4_AnAnalysisWithAnEmail_IsNotReOwned_WithTheRelationshipNamed` |
| AC5 | `AC5_AContactWithAChildContact_IsNotReOwned` |
| AC6 | `AC6_AChildReadThatFails_IsUnknown_NeverNoChildren` (timeout, privilege refused, HTTP refused, no answer), `AC6_ARelationshipWithoutItsReferencingColumn_IsUnknown_NeverNoChildren`, `AC6_ARelationshipThatDoesNotStateItsReferencingColumn_IsUnreadable_NeverSkipped`; depth: K3 / open point |
| AC7 | `AC7_ARowStillUnknownAfterItsAttempts_IsAbandoned_AndListedInTheReport` |
| AC8 | `AC8_ARelationshipWithAnUnstatedAssignValue_IsReadAndRefusesARowWithAChild`, `ARowWithAnUnacceptedChild_StillGetsItsMapping_WithoutTheOwner`, `AssignCascadeProjectionTests.ARelationshipWhoseAssignCascadeIsNotStated_IsListed_FailClosed` |
| AC9 | `AC9_AChildAddedAfterTheCheck_IsLoggedWithItsTableAndId_AndCounted`, `AC9_TheRaceReRead_ReturnsTheRacedChild_AndNothingWhenNoneWasAdded`, `AC9_ARaceReReadThatFails_IsReportedAsUnchecked_AndCounted` |
| AC10 | `AC10_ARowWithANeverAcceptedChild_IsNotReOwned_AndNoWriteReachesTheChild` (task, child contact, Signal, Decision Record, Budget Revision), `AC10_ANeverAcceptedRelationship_IsNotAccepted`, `AC1_AC10_...` |
| Per-table count | `TheRunCountsRowsKeptForAnUnacceptedChild_PerTable` |

ArchTest: `AccessTableReaderScanTests.NotAccessTables` gains `activitypointer` with its reason (the scan reads every
`*Table` constant under `Services/RecordCreation`; 052d's `audit` entry is the precedent). It decides no access.

## Results

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `Spaarke.sln` (verify-suites): 0 warnings, 0 errors.
- Task 052f tests: 52 passed (new, rewritten and the projection test). Repair and projection filter
  (`RecordRepair|AssignCascadeProjection`, `Api.Office` excluded): 373 passed, 0 failed.
- ArchTests: 2714 passed, 0 failed, the owner-write census (`RecordOwnerAssignmentCensusTests`) included and unchanged. The
  first run had 1 failure (the `activitypointer` scan above); fixed.
- `scripts/verify-suites.sh` filtered suites: 9305 passed, 28 failed, 7 skipped. All 28 failures are host-based endpoint tests
  cancelled at about 3 minutes (`TaskCanceledException` in the test HTTP client) under machine load: 15 under `Api.Office.`
  (which the task brief excludes) and 13 elsewhere. Those 13 test classes rerun on their own: 154 passed, 0 failed (including
  `RecordRepairPassTriggerAuthTests`).
- `dotnet format whitespace --verify-no-changes`: clean on `Sprk.Bff.Api` (RecordCreation), `Spaarke.Dataverse`, and the changed
  test files in `Sprk.Bff.Api.Tests` and `Spaarke.ArchTests`.
