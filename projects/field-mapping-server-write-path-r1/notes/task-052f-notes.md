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

### Fix round (one commit on top of `b695480cf9`)

| Item | Rule | Where (`RecordCreationService.Repair.cs` unless named) |
|---|---|---|
| A | `sprk_event->annotation` and `sprk_analysis->annotation` stay on the allow-list, reason: "Assign carries every note on the row; Repair re-owns the row only when every note on it is the row owner's." They are accepted with a per-row check: one app-only `top 1` read on `annotation` where `objectid` is the row and the note's owner is not the row's current owner (the owner Repair read for the row, not the target). Owner id and type are both compared: for a user owner, `owninguser ne X or owninguser null`; for a team owner, the same on `owningteam`. A hit → `owner_assign_cascades` (counted per table; the log line names the relationship and "note owned by another principal"), no write. A fault, refusal, no answer, or an owner that is not a readable user or team → `owner_assign_cascade_unknown`. Contact notes stay never-accepted (any note keeps a contact). | `AcceptedOnlyWithTheOwnersNotes` :359, `AssignCascadeChildReadKind` (records), `ChildReadsOf` :427, `ReadAssignCascadeChildAsync` :761 |
| A (race) | After the owner write, the note read is asked again for notes on the row whose `createdon` is at or after the check's start and whose `createdby` is not the previous owner → `owner_assign_cascade_raced` (table and id), counted. | `AssignCascadeRaceAsync` :691, `ReadAssignCascadeChildAsync` :761 |
| B | Read depth, a tri-state kept for the pass (`RecordRepairReadDepth`: Confirmed / NotConfirmed / Faulted). Asked only when a row needs a per-row child read: one app-only read of the app identity (`GetCurrentIdentityIdAsync`) in `systemuserroles` joined to `role` on role template id `627090ff-40a3-4053-8790-584edc5be201` (System Administrator, matched by template, never by name), through `RetrieveMultipleAsync`. A row → Confirmed. None → NotConfirmed. The identity or the roles cannot be read, or no answer → Faulted. Only a row confirms. NotConfirmed and Faulted hold every row that needs a per-row read (contact, analysis, event, any table with an unaccepted relationship) as `owner_assign_cascade_unknown` with no child read; rows needing no per-row read are unaffected. One error line per pass states the rule ("per-row child checks need organization-level Read; held"); for Faulted it carries the exception. | :528, `ReadDepthAsync` :628, `ReadOrganizationReadDepthAsync` :638; `RecordRepairPassContext.ReadDepthAsync` :133, `RecordRepairReadDepth` :145 |
| C | A child read that faults is recorded and the next read is still made. After the loop: any child or other-owner note found → `owner_assign_cascades`; otherwise any fault, or any relationship that cannot be read → `owner_assign_cascade_unknown`. | `AssignCascadeCheckAsync` :536–618 |
| D | The race re-read asks for up to `RaceReadTopCount` = 50 children per read (the check stays `top 1`). Each raced child is logged with its table and id; each read that found any logs the count and a `truncated` flag (a full page or `MoreRecords`). | :414, :743 |

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

First round (test names as of `b695480cf9`):

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

Fix round: script `mustfire2.sh` (same method; it also refuses a round whose mutation did not apply).

| Round | Rule switched off | Failed (of 176) | Tests that fired (examples) |
|---|---|---|---|
| A1 | Accepted note relationships skipped (no per-row note read) | 16 | `AC3_AnEventWithANoteOwnedByAnotherPrincipal_*` (user, team), `AC3_AnAnalysisWithANoteOwnedByAnotherPrincipal_*` (user, team), `AC3_ANoteReadThatFails_*` (×4), `AC3_AnAcceptedNoteRelationship_*` (×2), `AC3_AnEventWhoseNotesAreAllTheOwners_*`, `AC3_AnAnalysisWhoseNotesAreAllTheOwners_*`, `AC9_ANoteAddedByAnotherPrincipal*`, `B_NotConfirmedOrFaulted_*` (×3: the event no longer needs a read) |
| A2 | The note owner check without its `null` branch (a team's note not seen as another owner's) | 3 | `AC3_AnEventWithANoteOwnedByAnotherPrincipal_*(a team)`, `AC3_AnAnalysisWithANoteOwnedByAnotherPrincipal_*(a team)`, `AC3_AnEventWhoseNotesAreAllTheOwners_*` (filter shape) |
| A3 | The note race without its creator filter | 1 | `AC9_ANoteAddedByAnotherPrincipalAfterTheCheck_IsRaced_ANoteByThePreviousOwnerIsNot` |
| A4 | The note race without its `createdon` filter | 2 | `AC9_ANoteAddedByAnotherPrincipal*`, `AC3_AnEventWhoseNotesAreAllTheOwners_*` (no raced notes) |
| B1 | No depth guard | 7 | `B_NotConfirmedOrFaulted_*` (×3), `B_Faulted_*` (×2), `B_Confirmed_*`, `B_TheDepthIsReadOncePerPass_*` |
| B2 | A faulted depth read returns Confirmed | 4 | `B_NotConfirmedOrFaulted_*` (roles, identity), `B_Faulted_*` (roles, identity) |
| B3 | Depth asked per row, not kept for the pass | 7 | `B_TheDepthIsReadOncePerPass_*`, `B_Confirmed_*`, `B_Faulted_*` (×2), `B_NotConfirmedOrFaulted_*` (×3) (the first attempt's mutation did not compile and was rerun) |
| B4 | The role matched without its template id | 31 | `B_Confirmed_*`, every per-row read test (the world confirms only the template read) |
| C1 | A fault or unreadable relationship wins over a child found | 3 | `AC6_AContactWithAnEmail_IsAnAnswer_EvenWhenAnotherReadFaults` (×2), `AC6_AChildFoundAfterAReadThatFaulted_IsAnAnswer` |
| D1 | The race re-read at `top 1` | 4 | `AC9_TwoChildrenAddedAfterTheCheck_*`, `AC9_ARaceReReadThatFillsItsPage_*`, `AC9_ANoteAddedByAnotherPrincipal*`, `AC2_AContactWithNoUnacceptedChild_*` |
| D2 | No truncation flag | 1 | `AC9_ARaceReReadThatFillsItsPage_IsLoggedAsTruncated` |
| M0 | Restored | 0 | 176 passed |

## Known limits

- **K1 — the race is not undone.** A child added along an unaccepted relationship between the check (before the mapping reads)
  and the owner write moves with the Assign. The re-read after the write logs it with its table and id and counts it
  (`ownerAssignCascadeRaced`); nothing moves it back. UAC-r2 U2: the log and count are enough; a raced child goes to the
  parent's new owner, which the family rule gives anyway, and a parent in an isolation state is never written (052g's guard).
- **K2 — skipped rows are not revisited by the window.** A row kept as `owner_assign_cascades` is an answer, not a fault: it is
  not in the retry set, and the `createdon` window has passed it, so removing its child later does not bring it back. The 052c
  on-demand run over its window retries it.
- **K3 — depth (enforced by fix-round item B).** The per-row check requires organization-level Read on each table it reads; a
  pass without it holds those rows. The pass confirms it once, as the app identity holding the System Administrator role
  directly (matched by role template id); NotConfirmed and Faulted hold every row that needs a per-row read as
  `owner_assign_cascade_unknown`. A read Dataverse refuses for a missing privilege is unknown (tested). Design §6 item 5 (the
  pre-enable check of organization-level Read on each table read) still applies.
- **K4 — a refused per-row table blocks its rows.** If an environment refuses the read of any table the check reads for a
  contact (for example `mailboxtrackingfolder` or `syncerror`), every contact there is unknown, retried, and abandoned after 12
  attempts (listed in the 052d report). Fail closed; part of the §6 item 5 check.
- **K5 — invoices.** In this build every invoice-family write is held by the invoice-family guard (`InvoiceFamilyUpdateRefusalAsync`,
  design §3.6), so an invoice's owner is not written whatever the cascade check says. The check refuses nothing and reads
  nothing for an invoice (only `->team`, accepted).
- **K6 — the read depth is confirmed by a directly held role.** The pass confirms organization-level Read when the app identity
  holds the System Administrator role directly (`systemuserroles`); otherwise the rows that need a per-row read are held (fail
  closed).
- **K7 — the note race counts by creation.** The race counts a note whose `createdon` is at or after the check start and whose
  `createdby` is not the previous owner (N2).
- **N1 — the note check compares against the owner read with the row.** The note check compares each note's owner with the
  owner Repair read with the row. An owner change between that read and the check is the same detection-only class as K1.
- **N2 — the note race is detection only.** The race counts a note created at or after the check start by a principal other
  than the previous owner; when the previous owner is a team, every new note counts (log only). Clock skew near the boundary is
  accepted (detection only).

Pre-enable item (§6.5): the BFF app user holds the System Administrator role directly; confirmed read-only on dev 2026-11-18.
- **K8 — `ActivityTables` is a hand-kept list.** An activity type not on it is not folded into the `activitypointer` read: it is
  read per row on its own table along its own referencing column, like any child the allow-list does not hold (a child keeps
  the row; a fault is unknown). It still fails closed.
- **K9 — onward cascades.** An accepted child may itself cascade an Assign to its own children. That is the pre-enable check of
  design §6 item 4; Repair does not read onward.
- **K10 — an empty page with more records.** A child read whose page is empty but marked `MoreRecords = true` reads as none.
  Dataverse does not return that shape for a `top` read; noted for completeness.

## Open point — resolved

The depth-limited read (AC6) is now enforced in code (fix-round item B, owner decision "code check"); see K3 and K6.

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
| AC3 (owner's notes only) | must-not-fire: `AC3_AnEventWhoseNotesAreAllTheOwners_IsReOwned_AfterOneNoteReadForAnotherOwner`, `AC3_AnAnalysisWhoseNotesAreAllTheOwners_IsReOwned`; must-fire: `AC3_AnEventWithANoteOwnedByAnotherPrincipal_IsNotReOwned_AndIsCounted` (another user, a team), `AC3_AnAnalysisWithANoteOwnedByAnotherPrincipal_IsNotReOwned` (another user, a team); fault: `AC3_ANoteReadThatFails_IsUnknown_NeverNoOtherOwnersNote` (timeout, privilege refused, HTTP refused, no answer); pure: `AC3_AnAcceptedNoteRelationship_IsReadPerRow_ForANoteOfAnotherOwner` (event, analysis); race: `AC9_ANoteAddedByAnotherPrincipalAfterTheCheck_IsRaced_ANoteByThePreviousOwnerIsNot`. The move itself is Dataverse's; checked live before enable (design §6) |
| AC3a | `AC3a_AContactWithANote_IsNotReOwned_AndIsCounted` |
| AC4 | `AC4_AnAnalysisWithAnEmail_IsNotReOwned_WithTheRelationshipNamed` |
| AC5 | `AC5_AContactWithAChildContact_IsNotReOwned` |
| AC6 | `AC6_AChildReadThatFails_IsUnknown_NeverNoChildren` (timeout, privilege refused, HTTP refused, no answer), `AC6_ARelationshipWithoutItsReferencingColumn_IsUnknown_NeverNoChildren`, `AC6_ARelationshipThatDoesNotStateItsReferencingColumn_IsUnreadable_NeverSkipped`; a child found wins over a fault (item C): `AC6_AContactWithAnEmail_IsAnAnswer_EvenWhenAnotherReadFaults` (fault after, fault between), `AC6_AChildFoundAfterAReadThatFaulted_IsAnAnswer`; depth (item B, K3): `B_Confirmed_ThePerRowReadsAreMade_TheNoteReadIncluded`, `B_NotConfirmedOrFaulted_HoldsEveryRowThatNeedsAPerRowRead_OthersProceed` (not held, roles read faults, identity read faults), `B_Faulted_LogsTheRuleOnceWithTheFault_AndIsNeverConfirmed` (roles, identity), `B_TheDepthIsReadOncePerPass_HoweverManyRowsNeedIt` |
| AC7 | `AC7_ARowStillUnknownAfterItsAttempts_IsAbandoned_AndListedInTheReport` |
| AC8 | `AC8_ARelationshipWithAnUnstatedAssignValue_IsReadAndRefusesARowWithAChild`, `ARowWithAnUnacceptedChild_StillGetsItsMapping_WithoutTheOwner`, `AssignCascadeProjectionTests.ARelationshipWhoseAssignCascadeIsNotStated_IsListed_FailClosed` |
| AC9 | `AC9_AChildAddedAfterTheCheck_IsLoggedWithItsTableAndId_AndCounted`, `AC9_TheRaceReRead_ReturnsTheRacedChild_AndNothingWhenNoneWasAdded`, `AC9_ARaceReReadThatFails_IsReportedAsUnchecked_AndCounted`; item D: `AC9_TwoChildrenAddedAfterTheCheck_AreEachLoggedWithTheirTableAndId_AndCounted`, `AC9_ARaceReReadThatFillsItsPage_IsLoggedAsTruncated`, and the race reads' `top 50` in `AC2_AContactWithNoUnacceptedChild_*` |
| AC10 | `AC10_ARowWithANeverAcceptedChild_IsNotReOwned_AndNoWriteReachesTheChild` (task, child contact, Signal, Decision Record, Budget Revision), `AC10_ANeverAcceptedRelationship_IsNotAccepted`, `AC1_AC10_...` |
| Per-table count | `TheRunCountsRowsKeptForAnUnacceptedChild_PerTable` |

ArchTest: `AccessTableReaderScanTests.NotAccessTables` gains `activitypointer` with its reason (the scan reads every
`*Table` constant under `Services/RecordCreation`; 052d's `audit` entry is the precedent). It decides no access.

## Results

Final fix (re-check F2): `AssignCascadeCheckAsync` and `AssignCascadeCheck` are internal so the tests can ask the note check's
owner branches directly — Repair re-owns only a user-owned row, so a team owner or an unreadable owner never reaches the check
through Repair. Tests: `AC3_ATeamOwnedRow_TheNoteCheckFiltersOnOwningTeam_AUserOwnedNoteKeepsIt_TheTeamsOwnNoteLetsItThrough`
(event, analysis), `AC3_TheNoteCheck_WithNoReadableUserOrTeamOwner_IsUnknown_AndReadsNoNote` (none, empty id, not a user or a
team), `B_ARolesReadWithNoAnswer_OrAnEmptyIdentity_IsFaulted_NeverConfirmed` (both), and the two cases added to
`B_NotConfirmedOrFaulted_*`. Must-fire (`mustfire4.sh`, of 185): team branch forced to `owninguser` → 2 failed (the team-owned row test, event and
analysis); the unreadable-owner guard off → 3; a roles read with no answer returning Confirmed → 2; the empty-identity check
dropped → 1; restored → 185 passed. Repair filter 405 passed; BFF build 0 warnings; format clean.

Fix round:

- `Spaarke.sln` (verify-suites): 0 warnings, 0 errors. `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Repair and projection filter (`RecordRepair|AssignCascadeProjection`, `Api.Office` excluded): 396 passed, 0 failed. Must-fire
  filter: 176 passed after restore.
- ArchTests: 2714 passed, 0 failed (the new `systemuserroles` / `role` table constants needed no scan entry).
- `verify-suites.sh` filtered suites: 9500 passed, 0 failed, 7 skipped (of 9507). The run reported its result line, then the
  host was ended by the shell's 40-minute limit as it shut down ("Test host process crashed"); no test failed.
- `dotnet format whitespace --verify-no-changes`: clean on `Services/RecordCreation` and the changed test files.

First round:

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
