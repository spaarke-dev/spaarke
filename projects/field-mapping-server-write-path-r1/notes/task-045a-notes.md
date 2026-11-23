# Task 045a — type-only regarding pair completed or cleared at create (step 4½) + guard 1

Rulings applied: decisions.md "2026-11-07 — Owner approves the 045 recommendations" (Q2 (a): fill from exactly one
agreeing lookup, else clear; Q3 (a): a warning on clear only). Design: `task-045-regarding-propagation-design.md` §2.1.

## What changed

- `RecordCreationService` — step 4½ (`CompleteTypeOnlyRegardingPairAsync`), run in the caller branch right before step 5,
  so step 5 checks the pair as written. Helpers `RegardingPairFillRecords` (pure candidate set) and
  `RewriteRegardingPairAsync` (rebuilds an item through `MergedItem` + `DataverseWriteItemMapper.MapAsync`). Both the
  merged item and the caller's own item are rewritten, because step 5 may rebuild the row from the caller's item when a
  mapped value is dropped.
- `RecordCreationWarning.RegardingTypeClearedCode` (`record_creation.regarding_type_cleared`), column
  `sprk_regardingrecordtype`; the detail names no record.
- `CallerWriteAuthorization.PairTypeTableAsync` — the catalog read extracted from `PairTargetAsync` (behaviour unchanged),
  returning the response and the table separately so step 4½ can tell a failed read from a type naming no table.
- Guard 1: `tests/Spaarke.ArchTests/RegardingPairTypeImpliesIdGuardTests.cs` (member-scoped; no baseline, no violations).

## Behaviour

| Sent | Result |
|---|---|
| type, no id (absent/null/""/whitespace), one agreeing lookup of the type's table (caller-sent or server-set) | id filled, no warning |
| same record in two agreeing columns | one candidate: filled |
| no candidate, two different candidates, only a mapped candidate, only an I-1 stamp column while an intermediate is bound, type naming no table | type, id and display columns cleared; one warning |
| catalog read 403 / 404 | left as sent; step 5 answers as before (uniform 404) |
| catalog read other failure (fix round 1) | upstream refusal |
| a clear without AppendTo on the type row (fix round 1) | step 5's uniform 404 |
| system principal, type only (fix round 1) | cleared without a read, same warning |
| whole pair, no pair, id only | untouched, no extra read |

Cost: a type-only pair costs one extra catalog read (as the caller) and, when filled, step 5's usual AppendTo on the
pair's record.

## Evidence

Fail-first (base sources + the warning constant only; `RecordCreationServiceMappingSeamTests`, 200 tests): 12 failed /
188 passed. The 4 tests that pass on both (catalog read 403/404, upstream failure, must-not-fire) pin unchanged behaviour.
After: 200/200.

Mutants (each run, then restored):

| Mutant | Killed by |
|---|---|
| M1 never fill | 4 fill tests |
| M2 drop the stamp-beside-intermediate exclusion | stamp test |
| M3 drop the mapped exclusion | mapped test |
| M4 two candidates fill | two-candidate test |
| M5 a failed read treated as table-less (clears) | 403, 404 and upstream tests |
| M6 rewrite the merged item only | dropped-mapped-value rebuild test |

Guard seeding proof: commenting out `payload["sprk_regardingrecordid"] = …` in
`DataverseWebApiService.AddRegardingWriteSet` made the guard fire on that member (line 676); restored.

Harness: `SecureChildOwnershipAiToolTests.ScriptedUserClient` gained the analysis's `sprk_regardingdocument` and
`sprk_regardingrecordtype` lookups (live: the analysis is a polymorphic pair table, `RecordContainerResolver.ChildAncestorLinks`).

## For 045b

- `CallerWriteAuthorization.PairTypeTableAsync` is shared and internal: use it for the update path's catalog read.
- `RegardingPairFillRecords` / `RewriteRegardingPairAsync` are private to `RecordCreationService` (create-only, item-shaped).
  The update path keeps the row's existing lookups in play (`OwnedChildWrite.KeepPairInStepAsync`), so it needs its own
  candidate read.
- The warning code lives on `RecordCreationWarning`; the update path reports through log events (Q5).

## Fix round 1 (adversarial review of 818c61d623)

| Item | Change | Fail on 818c61d623 → pass |
|---|---|---|
| F4 | Before a clear, step 4½ asks `CheckCallerMayAppendToAsync` on the type lookup and refuses through `CallerCheckRefused` (step 5's answer) | seam F4 theory (2), `ChildCreate_…WithoutItsAppendToPrivilege…`, `ChildCreate_…RecordTypeRowThatDoesNotExist…` |
| F3 | `ChildCreate_AToDoBindingTheRecordTypeCatalog_IsCreated…` registers the matter type's table (`CatalogKnowsTheMatterType`) | passes both (fill path) |
| F2 | A catalog read other than 403/404 refuses with `Upstream` | seam F2 (only the first read fails) |
| K4 | `TaskActionCore.RegardingFieldByEntity` loses its `sprk_recordtype_ref` entry (the pair's TYPE column, written with no id); such a task is created unfiled. Guard rule 2 flags a map entry whose value is the type column | `ServerWriterAncestorStampingTests` K4 test; guard fired on `TaskActionCore.cs:107` |
| K2 | The system principal's type-only pair is cleared without a read (`WithoutRegardingPair`), same warning | seam K2 |
| — | `RateCardImportServiceTests` asserted `CreateDocumentRequest` has no `*Lookup` property; 030b added `ParentDocumentLookup`. Test fixed (assert the values are null); the import sets none, so the code is right | was failing before this round |

The first-round test "a catalog read that fails upstream leaves the row as sent" is replaced by the F2 test (that outcome
was the defect).

## Open

- On a fill, display columns the caller sent are kept as sent (045c owns display columns).

## Guard 1 false positive (fixed)

`RegardingPairTypeImpliesIdGuardTests` flagged `CommunicationService.CopyRegardingFromSourceAsync` (~:3023, added by 045c's reply
copy): it writes the type (only when the row's id equals the source's id) but writes the id through the helper
`CopyStringIfPresent(source, communication, "sprk_regardingrecordid")`, which the source scan did not read as an id write. The code
was safe; the guard was blind to the helper.

Fix (guard only, no BFF change): the scan counts `CopyStringIfPresent(<name>, <name>, <ID key>)` as an id write, recognised only
for that helper with the ID key (literal, shared constant or same-file const) as its THIRD argument. No file exception, no general
loosening: a call that copies another column through the helper is no id write.

Controls: must fire - a member writing the type and copying a different column (name, number, url) through the helper; the id
copied in ANOTHER member. Must not fire - `CopyRegardingFromSourceAsync`'s shape, and the helper with the `RegardingRecordIdColumn`
constant. Before the fix the guard failed with the one finding above; after, 3 of 3 pass (this guard's two tests and
`GenericUpdatePathsPreparePairGuardTests`).
