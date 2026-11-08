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
| catalog read fails (403, 404, other status) | left as sent; step 5 answers as before (uniform 404 in the tested shapes) |
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

## Open

- The system principal is skipped by step 4½ (no caller to read the catalog as; no production system creator).
- A non-throwing failure of step 4½'s catalog read alone (step 5's own read then succeeding) would still store a type-only
  pair; in the tested shapes step 5's read of the same row fails too.
- On a fill, display columns the caller sent are kept as sent (045c owns display columns).
