# Task 052g — cross-unit re-own and the isolated-row guard

POML: `tasks/052g-repair-cross-unit-reown.poml`. Rigor: FULL (security tag). Design: `notes/task-052e-design.md` §3.3, §3.4
(owner 2026-11-18; UAC-r2's guard shape, binding). Nothing here enables the pass on any environment: `RecordRepair:Pass:GoLive`
stays unset, no live Dataverse call was made, nothing was deployed. Line numbers are as of commit `4e0c8d256d`; re-grep before use.

## What changed

**Before.** An owner answer in another business unit was not written on an ownership-parent table
(`record_repair.owner_moves_business_unit`, task 016's fail-closed default). A reparentable child under a secure record
(document, event, to-do) whose row already sat in the Secure Record unit was re-owned by Repair to the Secure Record owner team.

**After.** Repair leaves isolated rows to the secure pass; every other answer, in any ordinary unit, is written and a move across
units is reported.

| Rule | Where (`Services/RecordCreation/`) |
|---|---|
| The guard is asked on the one path that writes an owner: after `OwnerDecided` succeeds (`RecordCreationService.cs:1693`), before the business-unit decision. A kept row adds `Skipped(Owner, "ownerid", code)` and returns `new OwnerRepair(null, mappingBlocked)`: the F1-b block (`:1684`) is returned unchanged | `RecordCreationService.cs:1702-1707` |
| Guard (a): the answer is the Secure Record owner team (`RecordOwnerResolution.IsSecureOwner`) → `owner_left_to_secure_pass` | `RecordCreationService.Repair.cs:368` |
| Guard (b): `IConfiguration` and `SecureChildReconciler` resolved from a scope of the existing `_scopes`; either missing → `owner_isolation_unknown` | `Repair.cs:371-375` |
| Guard (c): the Secure Record unit, `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_entities, configuration, ct, _logger)` (the configured `SecureRecord:BusinessUnitName`); a fault or `Ambiguous` → unknown; `(null, false)`: no Secure Record unit in this environment | `Repair.cs:377-389` |
| Guard (d): the row's `owningbusinessunit` unread → unknown; equal to the Secure Record unit → left | `Repair.cs:391-394` |
| Guard (e): ONE app-only read of the answer team (`businessunitid`, `name`); a fault or no unit → unknown; its unit is the Secure Record unit (any team there) → left | `Repair.cs:396-411` |
| Guard (f): the restricted-content owner team: `InPlaceOfDefaultTeamId` set, or the team's name equals `RestrictedContentOwnerTeam.OwnerTeamName(configuration)` (trimmed, ordinal, ignore case) → left | `Repair.cs:413-418` |
| Guard (g): the current owner through the shared read, `await (await RefileIsolationRead.BeforeWriteAsync(children, table, row.Id, ct))()`; true → left; a fault (after the shared read's own second try) → unknown. Only the caller's own cancellation propagates | `Repair.cs:420-429` |
| Each kept row logs table, id, the code and which condition; never a team name | `Repair.cs:351-366` |
| New codes: `OwnerLeftToSecurePass` (an answer: not retried), `OwnerIsolationUnknown` (a fault: in `TransientReasons`, retried, abandoned after the pass's attempt limit) | `Repair.cs:1913`, `:1919`, `TransientReasons` `:1799` |
| Cross-unit write: the skip and its constant are gone; an answer in another unit joins the same patch (same If-Match write, eviction). `OwnerRepair.MovedBusinessUnit` carries (from unit, to unit, family root) | `RecordCreationService.cs:1709-1718`, `OwnerRepair` `:1547` |
| Family root: the first `CoreAncestorResolver.CoreAncestorLookups` stamp column the row holds, else the first the stamp plan adds; `null` when none | `FamilyRootOf`, `Repair.cs:442` |
| After a CONFIRMED write with `ownerid` still in the patch (052f's cascade check may remove it), one Information line `"[RECORD-REPAIR] {Table} {Id} re-owned across business units from {FromUnit} to {ToUnit} (family root {RootTable} {RootId})"` (ids only) and `RecordRepairOutcome.MovedBusinessUnit` set; otherwise null | `Repair.cs:290-312`, `RecordRepairOutcome.MovedBusinessUnit` `:1766`, `RecordRepairBusinessUnitMove` `:1700` |
| 412 on an owner write: the row's `ownerid` and `owningbusinessunit` re-read ONCE app-only and logged ("another writer settled the owner" when it is now a team); never written again, no loop; a re-read fault is logged and ignored; the outcome stays `RowChanged`. No re-read when the patch had no owner | `Repair.cs:257-267`, `LogOwnerAfterConflictAsync` `:467` |
| Counts: `ownerMovedBusinessUnit` (`Wrote && MovedBusinessUnit != null`), `ownerLeftToSecurePass` (either new code), `ownerIsolationUnknown` (the unknown code), in the heartbeat and `ResultJson` | `RecordRepairPassJob.cs` `Count` :870-886, heartbeat :988-1002, result :1035-1037, `RunCounts` :1105-1111 |

**Kept unchanged.** Forward-only (`RowOutsideThePass`), `OwnerRootSecure`, `OwnerTeamOwned`, 052f's cascade check, the
invoice-family guard, the mapping gate (F1-b), the single owner write `UpdateIfRowVersionMatchesAsync` (`Repair.cs:255`) and the
owner-write census (`RepairOwnerAsync`, 1 Routed write). No call into `ReconcileOrdinaryParentMoveAsync` or any other mover.

**Removed.** `RecordCreationService.TeamBusinessUnitAsync`: its only caller was the Repair step it replaced (the task brief
described it as shared with Create; Grep finds no other caller). The guard's single team read (`businessunitid`, `name`) gives
the unit, so keeping the method would be a second read of the same row or dead code.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

- **Placement:** inside the existing `RecordCreationService` (its Repair partial and `RepairOwnerAsync`) and the existing
  `RecordRepairPassJob` counts. No new endpoint, service, DI registration, job, store, package, route or role. The guard resolves
  two services the host already registers (`IConfiguration`; `SecureChildReconciler`, scoped, `ExternalAccessModule.cs`) through
  the `IServiceScopeFactory` Repair already holds, as it already does for `MembershipOptions` and the cache invalidator.
- **Publish-size delta / CVE check:** main session's (no package change here).

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `record_repair.owner_left_to_secure_pass` | `owner_moves_business_unit` (the retired skip), `OwnerRootSecure`, `OwnerTeamOwned` | None of them names "isolated; the secure pass owns it"; reusing the retired code would report a different rule under a name that says "business unit" | Removing the skip without a guard lets Repair write into or out of isolation; without a code the kept rows are not counted or reported (§3.6) |
| `record_repair.owner_isolation_unknown` | `OwnerAssignCascadeUnknown`, `MappingFilingUnavailable` (other faults) | Each existing fault code names its own read; folding the guard's faults into one of them would mislead the 052d report and the retry counts | A faulted isolation read would have to be read as "not isolated" (writes into isolation) or as "left" (never retried) |
| `RecordRepairOutcome.MovedBusinessUnit` / `RecordRepairBusinessUnitMove` | `RacedChildren` (052f's carry of an after-write fact) | Same idiom: an init property on the existing outcome record; the pass counts it | FR-25 / design §3.3.2 requires the move to be counted (`ownerMovedBusinessUnit`) and logged with both units and the root; the pass sees only the outcome |
| `OwnerRepair.MovedBusinessUnit` | `OwnerRepair` (refusal, mapping block) | One optional member on the existing private record | The owner step is the only place that knows both units; without it `RepairAsync` would read the team again |
| Pass counts `ownerMovedBusinessUnit`, `ownerLeftToSecurePass`, `ownerIsolationUnknown` | `ownerAssignCascadeRaced` etc. (052f) | Same `Count` / heartbeat / `ResultJson` / `RunCounts` path | AC1/AC3 require them counted; without them a run's report shows no isolated rows and no moves |
| `IsolatedRowGuardAsync`, `FamilyRootOf`, `LogOwnerAfterConflictAsync` (private members) | `RefileIsolationRead`, `SecureRecordOwnerTeam`, `RestrictedContentOwnerTeam` (called, not copied) | Private steps of the existing method; every isolation answer comes from the shared reads | §3.3.1 / §3.4 require the guard and the single re-read at this write site |

## Must-fire evidence (each rule switched off, build, run, restore)

Each round edits the source in place from a backup copy, builds `tests/unit/Sprk.Bff.Api.Tests`, runs
`FullyQualifiedName~RecordRepair&FullyQualifiedName!~Api.Office.`, and restores the copy. Never `git stash`. The final restore run
passed 446/446, and `git status --porcelain src` was empty.

| Round | Rule switched off | Failed | Example failing tests |
|---|---|---|---|
| M1 | The guard's answer is ignored (a kept owner is written anyway) | 36 / 445 | every AC3 case, AC4, AC4a ×8, AC4b (configured unit, configured team name), AC4d ×2, the F1-b block, the four 042b to-do tests, pass AC3 |
| M2a | (a) A secure answer is left to the secure pass | 3 / 445 | the three 042b to-do tests that assert the log condition "the answer is the Secure Record owner team". See the note below the table. |
| M2b | (b) The isolation read is not resolvable, so the owner is kept | 2 / 445 | AC4a `ConfigurationNotResolvable`, `ReconcilerNotResolvable` |
| M2c | (c) The Secure Record unit is ambiguous, so the owner is kept | 1 / 445 | AC4a `SecureUnitAmbiguous` |
| M2c' | (c) The Secure Record unit read faults, so the owner is kept | 1 / 445 | AC4a `SecureUnitReadFaults` |
| M2d | (d) The row's unit is unreadable, so the owner is kept | 1 / 445 | AC4a `RowUnitUnreadable` |
| M2d' | (d) A row already in the Secure unit is left alone | 1 / 445 | `AC4_AUserOwnedRowInTheSecureUnit_WithAnOrdinaryAnswer_IsLeftToTheSecurePass` |
| M2e | (e) An answer team in the Secure unit is left alone | 4 / 445 | AC3 `OtherTeamInTheSecureUnit` ×3, `AC4b_TheSecureRecordUnit_IsTheConfiguredOne(true)` |
| M2e' | (e) The answer-team read faults, so the owner is kept | 1 / 445 | AC4a `AnswerTeamReadFaults` |
| M2e'' | (e) An answer team with no unit, so the owner is kept | 1 / 445 | AC4a `AnswerTeamHasNoUnit` |
| M2f | (f) A restricted-content team substituted in place of the default is left alone | 3 / 445 | AC3 `RestrictedContentTeamSubstituted` ×3 |
| M2f' | (f) The restricted-content team, matched by its configured name, is left alone | 5 / 445 | AC3 `RestrictedContentTeamByName` ×3, `AC4b_TheRestrictedContentOwnerTeam_IsTheConfiguredName` ×2 |
| M2g | (g) A row the shared isolation read calls isolated is left alone | 1 / 445 | `AC4_ARowTheSharedIsolationReadCallsIsolated_IsLeftToTheSecurePass` |
| M2g' | (g) The shared isolation read faults, so the owner is kept | 3 / 445 | AC4a `IsolationReadFaultsTwice`, AC4d `(isolationReadFaults: true)`, pass AC3 |
| M2h | `owner_isolation_unknown` is asked again (transient) | 10 / 446 | AC4a ×8, AC7, pass AC3 |
| M3a | A confirmed cross-unit write reports its move | 8 / 445 | AC1 event, AC1 document, AC1 to-do (plan root), AC2, AC4a once-faulting read, AC4a no Secure unit, AC4b `(false)`, pass AC1 |
| M3b | A move is reported only when the confirmed write held the owner | 1 / 446 | `AC1_AMoveWhoseOwnerTheCascadeCheckTookOut_WhileOtherColumnsAreWritten_IsNotReported` |
| M3c | The move carries its family root | 2 / 445 | AC1 event (the row's stamp), AC1 to-do (the root the stamp plan adds) |
| M3d | The pass counts `ownerMovedBusinessUnit` | 1 / 445 | pass `AC1_TheRunCountsACrossUnitReOwn_AndTheHeartbeatNamesIt` |
| M3e | The pass counts `ownerLeftToSecurePass` | 1 / 445 | pass AC3 |
| M3f | The pass counts `ownerIsolationUnknown` | 1 / 445 | pass AC3 |
| M4a | A 412 on an owner write re-reads the owner | 2 / 446 | `AC4c_A412OnAnOwnerWrite_ReadsTheOwnerOnce_WritesOnce_AndIsRowChanged`, `AC4c_A412WhoseReReadFaults_IsLoggedAndIgnored` |
| M4b | The re-read happens exactly once | 2 / 445 | the same two AC4c tests |
| M4c | Only a patch that held the owner is re-read | 1 / 446 | `AC4c_A412OnAPatchWithoutAnOwner_IsNotReRead` |

**First-run gaps, closed.**
- M3b first survived. Every existing test that took the owner out of the patch also emptied the patch, so the write never ran. I added `AC1_AMoveWhoseOwnerTheCascadeCheckTookOut_WhileOtherColumnsAreWritten_IsNotReported`, and the round now fails it.
- M2h, M4a and M4c first did not apply: their multi-line mutation text was LF, but the file is CRLF. All three were re-run with CRLF-safe regex mutations, which is the "/446" rows above.

**M2a is a second line of defence.** In a consistent directory the Secure Record owner team sits in the Secure unit, so check (e) also keeps a secure answer. With (a) off, the owner is still never written; only the log condition changes. Check (a) stays first:
- it needs no read;
- it covers a directory where the named team was moved out of the Secure unit.

## Known limits

- **K1 — files under a moved non-root row.** The files of documents filed directly under a non-root row Repair moved across units
  (for example an event) stay in their container. That is UAC-r2's, issue 2903 (U1); it does not block. The move is logged
  (`re-owned across business units`, with the family root) and counted.
- **K2 — a team-owned current owner is kept earlier.** A row whose current owner is the Secure Record owner team or a
  restricted-content owner team is kept as `OwnerTeamOwned` (Repair re-owns only a systemuser-owned row), not reported as
  `owner_left_to_secure_pass`. The If-Match write also refuses a row re-owned between the read and the write.
- **K3 — the shared read answers only for child tables.** `SecureChildReconciler.IsSecureTeamOwnedAsync` answers for
  `SecureChildLineage.Children` tables and is `false` for the rest (roots, contacts) without a read; for those, the unit checks
  (d, e) and the restricted-content check (f) carry the guard. The read also answers `false` when the Secure Record owner team is
  absent or ambiguous (it reads the row's `owningteam`, which a person-owned row does not have).
- **K4 — per-row reads, not cached per pass.** For each row that reaches the guard: one read of the Secure Record unit (by
  name), one read of the answer team, and, for a child table, the isolation read (one query of the row's `owningteam`; a second
  try after a fault; two more reads, the Secure Record unit and its owner team, only when the row has an owning team). So 3 reads
  for a person-owned child row (2 for a root or a contact), against the 1 team read before. They are not cached across rows, so a
  configuration or team change is seen on the next row.
- **K5 — log condition names, not team names.** The kept-row log names the condition (for example "the answer is the
  restricted-content owner team"), the table and the id; the team's name and id are not logged.
- **K6 — a `NothingToRepair` row can still need a retry.** A row whose only part is the owner and whose guard read faults ends
  `NothingToRepair` with `owner_isolation_unknown` in its fields; `NeedsRetry` is true (as for 052f's unknown code).

## For UAC-r2's review

Access-path files **edited: none.** (`SecureRootInheritance*.cs`, `DirectThreadAccessService.cs`, `DirectMessageParticipantShares.cs`,
`SecureChildShareReconciliationJob.cs`, `SecureChildReconciler.cs`, `SecureChildShareSynchronizer.cs`, `RefileIsolationRead.cs`,
`OwnedChildWrite.cs`, `RecordOwnershipResolver.cs`, `RestrictedContentOwnerTeam.cs`, `SecureRecordOwnerTeam.cs` are unchanged.)

Access-path code **called** by the guard (read-only):

| File | Member | Called from |
|---|---|---|
| `Services/Access/RefileIsolationRead.cs` | `BeforeWriteAsync` | `RecordCreationService.Repair.cs:422` |
| `Services/Access/SecureChildReconciler.cs` | `IsSecureTeamOwnedAsync` (through `BeforeWriteAsync`; the reconciler is resolved from the scope, `:373`) | — |
| `Services/Access/SecureChildShareSynchronizer.cs` | `IsSecureTeamOwnedAsync`, `IsSecureOwnerTeamAsync` (through the reconciler) | — |
| `Infrastructure/Dataverse/SecureRecordOwnerTeam.cs` | `ResolveSecureBusinessUnitAsync` | `Repair.cs:380` |
| `Infrastructure/Dataverse/RestrictedContentOwnerTeam.cs` | `OwnerTeamName` | `Repair.cs:414` |
| `Services/Dataverse/RecordOwnershipResolver.cs` | `RecordOwnerResolution.IsSecureOwner`, `.InPlaceOfDefaultTeamId` (the answer's fields, read) | `Repair.cs:368`, `:413` |

Guard conditions (UAC-r2's shape, design §3.3.1), all in `RecordCreationService.Repair.cs` `IsolatedRowGuardAsync` (`:349`),
called once from `RecordCreationService.cs:1702`:

| # | Condition | Answer | Line |
|---|---|---|---|
| a | the answer is the Secure Record owner team | left | :368 |
| b | configuration or reconciler not resolvable | unknown | :374 |
| c | Secure Record unit read faults / ambiguous | unknown | :383, :387 |
| d | row unit unread / row in the Secure Record unit | unknown / left | :391, :393 |
| e | answer team read faults / has no unit / is in the Secure Record unit | unknown / unknown / left | :404, :408, :410 |
| f | answer is the restricted-content owner team (substituted, or by configured name) | left | :413-418 |
| g | row owned by the Secure Record owner team (shared read) / shared read faults | left / unknown | :423, :428 |

The note-move path (owner Q2, AC4d): the guard runs inside the owner step, before 052f's cascade check; a kept row has no
`ownerid` in the patch, so the cascade check (and its note read) is never asked, no Assign is sent, and no note moves
(`AC4d_AnEventWithANoteUnderTheSecureMatter_IsSkippedWhole_ItsNoteUntouched`, both the isolated and the faulted read).

## Tests

New: `tests/integration/data-mutation/RecordOwnership/RecordRepairCrossUnitTests.cs` (partials of `RecordRepairModeTests` and
`RecordRepairPassJobTests`, and the `RepairWorld` seams: the guard's configuration, the REAL `SecureChildReconciler` over the
world's directory, its isolation read logged and faulted on demand, the guard's unit and team reads faulted on demand, the 412
re-read). Updated: `RecordRepairModeTests.cs` (the world registers the configuration and the reconciler in its scope; the team
read is logged and can fault; the two escalation-trigger-2 tests are replaced by the AC1 tests; the four 042b to-do tests under
a secure matter now expect `owner_left_to_secure_pass`, each with a comment naming 052g and design §3.3), and
`tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/OwnershipDirectory.cs` (a team row carries its `name`, as Dataverse returns it).

| AC | Test(s) |
|---|---|
| AC1 | `AC1_APersonOwnedEventUnderAMatterInAnotherUnit_IsReOwnedToThatUnitsTeam_AndTheMoveIsReported`, `AC1_APersonOwnedDocumentUnderAMatterInAnotherUnit_*`, `AC1_AToDoMissingItsStamp_ReportsTheFamilyRootTheStampPlanAdds`, `AC1_ASameUnitReOwn_IsWritten_WithNoMoveReported`, `AC1_AMoveWhoseOwnerTheCascadeCheckTookOut_IsNeitherWrittenNorReported`, `AC1_AMoveWhoseOwnerTheCascadeCheckTookOut_WhileOtherColumnsAreWritten_IsNotReported`, `AC1_AMoveWhoseWriteFaultsAndDidNotLand_IsNotReported`, `AC1_AWriteThatFaultedButLanded_IsStillWriteFailed_AndTheMoveIsReported` (review round 1); pass: `AC1_TheRunCountsACrossUnitReOwn_AndTheHeartbeatNamesIt` |
| AC2 | `AC2_APersonOwnedOrdinaryWorkAssignmentUnderAMatterInAnotherUnit_GetsExactlyOneUpdateAndOneReadBack_AndNoOtherWrite` (one conditional update, applied, version advanced; the service made no other write call; the reconciler's double saw no call but reads; no resolver re-own) |
| AC3 | `AC3_ARowUnderTheSecureMatterWhoseAnswerIsIsolated_IsLeftToTheSecurePass_NeverWritten` (document, event, to-do × Secure named team, another team in the Secure unit, restricted-content team substituted, restricted-content team by name), `AC3_ARowUnderTheSecureMatterAlreadyInTheSecureUnit_IsLeftToTheSecurePass` (×3); pass: `AC3_TheRunCountsRowsLeftToTheSecurePass_AndTheUndecidedOnesAreAlsoCountedAndAskedAgain` |
| AC4 | `AC4_AUserOwnedRowInTheSecureUnit_WithAnOrdinaryAnswer_IsLeftToTheSecurePass`, `AC4_ARowTheSharedIsolationReadCallsIsolated_IsLeftToTheSecurePass` (the fixture's row reads systemuser-owned while its `owningteam` is the Secure Record owner team: only the shared read, which reads `owningteam`, can tell); the restricted-content team as current owner: K2 |
| AC4a | `AC4a_WhenIsolationCannotBeTold_TheOwnerIsKept_NeverWritten_AndAskedAgain` (Secure unit read faults, Secure unit ambiguous, answer team read faults, answer team has no unit, row unit unreadable, isolation read faults twice, reconciler not resolvable, configuration not resolvable); twins: `AC4a_AnIsolationReadThatFaultsOnce_IsReadAgainByTheSharedRead_AndTheMoveIsWritten`, `AC4a_AnEnvironmentWithNoSecureRecordUnit_DecidesOnTheRestOfTheGuard` |
| AC4b | shared read asked: `AC1_APersonOwnedEvent…` (`IsolationReads` once), `AC4_ARowTheSharedIsolationReadCallsIsolated…` (the reconciler's own `owningteam` query); configuration: `AC4b_TheSecureRecordUnit_IsTheConfiguredOne` (both), `AC4b_TheRestrictedContentOwnerTeam_IsTheConfiguredName` (both); F1-b: `AC4b_ASecureFiledRow_KeepsItsF1bMappingBlock_WhenLeftToTheSecurePass`; must-not-fire twin: the AC1 tests |
| AC4c | `AC4c_A412OnAnOwnerWrite_ReadsTheOwnerOnce_WritesOnce_AndIsRowChanged`, `AC4c_A412WhoseReReadFaults_IsLoggedAndIgnored`, `AC4c_A412OnAPatchWithoutAnOwner_IsNotReRead` |
| AC4d | `AC4d_AnEventWithANoteUnderTheSecureMatter_IsSkippedWhole_ItsNoteUntouched` (isolated answer; faulted isolation read); twin: 052f AC3 |
| AC5 | `AC5_ASecureFlaggedRootUnderAMatterInAnotherUnit_IsNeverReOwned` (flag true, flag empty) |
| AC6 | `AC6_APreGoLiveRowInACrossUnitPosition_IsNeverWritten` |
| AC7 | `AC7_NoReasonCodeNamesTheRetiredCrossUnitSkip`; the constant is deleted (compile); `Grep owner_moves_business_unit` over `src/` and `docs/`: no match (project notes and POMLs before 052g keep it as history) |
| AC8 | must-fire table above; gates below |

## Results

Run on the task branch, with the code commit plus the M3b test.

| Check | Result |
|---|---|
| `verify-suites.sh`: solution build | 0 warnings, 0 errors |
| ArchTests, built fresh and run with `--no-build` | 2714 / 2714 passed |
| Census `RecordOwnerAssignmentCensusTests` | `tests/Spaarke.ArchTests/` has no diff. `RepairOwnerAsync` still makes 1 Routed owner write. |
| Filtered suites (`verify-suites.sh`) | 9600 passed, 0 failed, 7 skipped (the existing `OfficeEndpointsContractTests` skips), 27 m 46 s |
| `RecordRepair` filter | 446 / 446 passed |
| `dotnet format whitespace --verify-no-changes` | clean on `Services/RecordCreation/`, `tests/integration/data-mutation/RecordOwnership/` and `OwnershipDirectory.cs` |
| AC7 grep | `owner_moves_business_unit` and `OwnerMovesBusinessUnit` have no match in `src/` or `docs/`. The only remaining matches are project history (notes and POMLs), these notes, and the AC7 test. |

## Review round 1

Fix round on the review findings. Each fix has a test that failed with the fix switched off (the fix edited out in place, built,
run with the `RecordRepair` filter, restored from git; never `git stash`).

| # | Finding | Fix (`Services/RecordCreation/`) | Test (fails with the fix off) |
|---|---|---|---|
| 1a | Design §3.3.2 / §3.4 say "If-Match, eviction and a read-back"; a confirmed owner write was reported from the plan, never read back | `RepairAsync`: after a confirmed write that planned a move, one app-only read of `ownerid` and `owningbusinessunit` (`ReadOwnerAsync`, shared with `LogOwnerAfterConflictAsync`). The planned team as owner reports the move with the unit the read showed; another owner logs "another writer changed it" and reports no move; a read fault reports the planned move and the line says "not read back" | `AC1_AConfirmedMove_IsReportedWithTheUnitTheReadBackShowed`, `AC1_AMoveWhoseReadBackShowsAnotherOwner_IsNotReported_AndTheDifferenceIsLogged`, `AC1_AMoveWhoseReadBackFaults_IsReportedAsPlanned_AndTheLineSaysItWasNotReadBack`; the one-read assertions in `AC1_APersonOwnedEvent…` and AC2 (4 + 1 failed) |
| 1b | A write fault is ambiguous (it may have landed) but its move was never looked for | The generic write-failure catch (not the 412 path): when a move was planned, one read; the planned team as owner logs the move line ("the write landed despite the fault") and sets `MovedBusinessUnit` on the `WriteFailed` outcome (status unchanged, no write, no loop); a read fault is logged and ignored | `AC1_AWriteThatFaultedButLanded_IsStillWriteFailed_AndTheMoveIsReported`, `AC1_AMoveWhoseWriteFaultsAndDidNotLand_IsNotReported`, `AC1_AWriteFaultWhoseReadBackAlsoFaults_IsLoggedAndIgnored_StillWriteFailed` (3 failed) |
| 1c | The pass counted a move only for `Wrote` outcomes | `RecordRepairPassJob.Count` counts `ownerMovedBusinessUnit` from `MovedBusinessUnit`, whatever the status | pass `AC1_AWriteThatFaultedButLanded_IsCountedAsFailed_AndAsAMove` (1 failed). `AC1_AMoveWhoseWriteFails_IsNotReported` is split into the did-not-land and landed tests above |
| 1d | A same-unit owner write must not gain a read | The read-back runs only when `OwnerRepair.MovedBusinessUnit` is set | `AC1_ASameUnitReOwn_IsWritten_WithNoMoveReported` asserts no read (a must-not-fire twin: passes with and without the fix). AC2 (cross-unit) is now "exactly one update and one read-back, no other write" |
| 2 | `FamilyRootOf` returned the held stamp over the plan's replacement | `FamilyRootOf` (now `internal`): per `CoreAncestorLookups` column, the plan's value when the plan names the column (a planned clear gives no root, the next column is asked), else the held value; the precedence of `AsTheRowEnds` | `AC1_AToDoHoldingAStaleStamp_ReportsTheRootThePlanReplacesItWith`, `FamilyRootOf_TakesThePlansValueWhenThePlanNamesTheColumn_AndTheHeldValueOtherwise` (2 failed) |
| 3 | Resolving the shared read in the guard (scope, `IConfiguration`, `SecureChildReconciler`) could throw out of `RepairAsync` | Scope creation and both resolutions inside a try with the guard's filter; a fault is `owner_isolation_unknown` ("the shared isolation read could not be built") | `AC4a_WhenIsolationCannotBeTold…(ReconcilerFactoryThrows)` (the world's reconciler factory throws; 1 failed) |
| 4 | An abandoned row's log named no reason when it sat on a field | `ReasonOf`: the outcome's code, else the owner refusal's, else the first field reason in `RecordRepairOutcome.TransientReasons` (so `owner_isolation_unknown`, `owner_assign_cascade_unknown`) | `AC3_ARowAbandonedWhileIsolationCouldNotBeTold_IsLoggedWithThatReason` (1 failed) |
| 5 | Nothing pinned the guard's team read columns, and the double ignores them | Test only | `AC4b_TheRestrictedContentOwnerTeam_IsTheConfiguredName` asserts every guard team read asked for `businessunitid` and `name` (2 failed with `TeamNameColumn` dropped from the read) |
| 6 | AC4d did not record that no note is written | Test only, production unchanged | `AC4d_…` also asserts no write targets `annotation` and no Assign was sent (the existing `ownerid` assertion stays; the I-1 stamp write is allowed) |

Seams added to the test world (`RepairWorld`): `AfterWrite` (a change after our write), `WriteFaultAfterLanding` (the update lands,
then throws), `ReconcilerFactoryFault` (building the reconciler throws), and an optional held stamp on the to-do builder.

### Known limits added by this round

- **K7 — check (g) cannot fire for a systemuser-owned row.** The shared isolation read answers from the row's `owningteam`, and a
  person-owned row has none, so for the rows Repair reaches the guard rests on the unit checks (d, e) and the restricted-content
  check (f). Check (g) is covered only by the unit tests that give a person-owned row an owning team (the `AC4_ARowTheSharedIsolationReadCallsIsolated…` fixture).
- **K8 — an unresolvable `SecureRecord:BusinessUnitName` reads as "no Secure unit".** `ResolveSecureBusinessUnitAsync` answers
  `(null, false)`, so no unit is isolated by the unit tests. A pre-enable check for it is added to design §6 by the main session.
- **K9 — what "skipped whole" means for AC4d.** The owner and the notes are untouched; the I-1 stamp write for the same row is
  still made (the guard keeps only the owner).
- **K10 — the guard is a local composition of shared reads.** It composes `SecureRecordOwnerTeam`, `RestrictedContentOwnerTeam` and
  `RefileIsolationRead` in the shape UAC-r2 set; it is not a shared predicate, so a change to their isolation rules needs this
  method reviewed too.
- **Read-back is one read, never a loop.** A fault there reports the planned move (the safe side for a follow-up log line); a
  different owner reports none. The move count is a log and a pass count, not an access decision.
