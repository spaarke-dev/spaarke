# Task 037 notes: Work Assignment Assigned To defaults to its own Assigned To Internal (FR-36, #2354)

## Rigor and scope

FULL, from the POML. Executed on opus at high effort, on branch `task/fm-037` cut from `1bf631c84b`. One adversarial-reviewer
pass was launched by the implementing agent (security-sensitive, NFR-01); the main session forwards the diff to UAC-r2.

## Live metadata (read-only, spaarkedev1, 2026-10-27)

| Column on `sprk_workassignment` | Type | Targets | IsSecured | IsValidForCreate |
|---|---|---|---|---|
| `sprk_assignedto` (`sprk_AssignedTo`) | Lookup | contact | false | true |
| `sprk_assignedtointernal` (`sprk_AssignedToInternal`) | Lookup | contact | false | true |

Both are contact lookups, so escalation trigger 1 does not fire: the EntityReference is copied as-is, with no
contact/user conversion and no name or email match.

## Ordering (escalation trigger 2)

Both paths run `AssignedAccessMaterializer.RunAfterWriteAsync` AFTER the create (`writtenColumns: null`, so it reads the
created row), and after the isolated-root completion (task 142 ordering). The default is applied before the caller checks
and is written in the create body, so the materialiser reads it as it reads a supplied value. No separate grant path.
Trigger 2 does not fire.

## What changed

| File | Change |
|---|---|
| `RecordTableRules.cs` | The ONE declaration: the `sprk_workassignment` rule's `ServerDefault("sprk_assignedto", …) { CopiedFrom = "sprk_assignedtointernal" }`. New `ServerDefault.CopiedFrom`, `CopiedDefaultFill` and the pure `RecordTableRules.CopiedDefaultsFor(rule, item)`: one fill per copied default whose target the row leaves UNSET (not in `item.Columns`, so no value, no bind and no clear), with the bound source lookup or `null`. |
| `OwnedChildWrite.cs` | `CreateAsync` merges the copied defaults (mapped as the caller, `MapAsync` + `Merge`, the `KeepPairInStepAsync` idiom) after WhoAmI and BEFORE `CheckCallerMayCreateAsync`, so the value is checked as the caller (AppendTo, field security), planned, written in the create and read by the materialiser. A copy of a server-set source is server-set too (see fix round). Missing source: `copied_default_source_unset` warning, column blank. Unbindable default: `copied_default_not_bound` warning, create goes on. Caller's metadata failure: returned, nothing written. New optional `ILogger? logger` parameter. The ForPersonColumns remark (:178-183) updated. |
| `ChildRecordEndpoints.cs` | Passes its logger to `OwnedChildWrite.CreateAsync` (one argument). |
| `DataverseCreateRecordHandler.cs` (fix round) | Passes `_logger` to `OwnedChildWrite.CreateAsync`; the tool result's `columnsSet` / `columnCount` / summary report `Outcome.WrittenColumns` (the row as written). |
| `RecordCreationService.cs` / `.Mapping.cs` | Step 3½: `WithCopiedDefaults` adds the copied defaults, read off the row AS MAPPED, to the post-mapping defaults (a request default for the same column is kept). They then take the existing default path: `DefaultsToFill`, `FittingDefaultsAsync`, `MapMergedAsync`, the step-5 checks (dropped with `DefaultValueDroppedCode` on failure), the create, the materialiser. Caller principal only. |

Behaviour, before → after:

- Child-records create of a work assignment with `sprk_AssignedToInternal@odata.bind` = X and no Assigned To: before,
  Assigned To blank; after, Assigned To = X in the create, materialised as a supplied X.
- RecordCreationService create where mapping fills Assigned To Internal = X: before, blank; after, X.
- Supplied Assigned To Y, or an explicit null (`sprk_AssignedTo@odata.bind: null` or plain `sprk_assignedto: null`): unchanged on both paths.
- Chat `dataverse.create_record` of a work assignment (same `OwnedChildWrite` core): before, Assigned To blank; after, the
  caller's contact (the server-set "for" person) is copied into Assigned To. The test that pinned "not defaulted"
  (`CreateRecord_AWorkAssignmentFiledToAnOrdinaryMatter_…`) now pins the copy. This follows from applying the rule in
  `OwnedChildWrite.CreateAsync` as the POML directs; flagged for UAC-r2.

## Tests (KEEP paths)

| File | Tests |
|---|---|
| `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs` | Declared exactly once (AC1); `CopiedDefaultsFor`: unset fills from the bound source, supplied → nothing, explicit null → nothing, source unset/cleared → fill with no source, other tables → nothing. |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.WorkAssignmentAssignedToDefault.cs` (new) | Child route: must-fire; supplied Y kept; explicit null (bind and plain) stays null (UNSET vs explicit null, for task 038's picker); source unset → blank + structured warning; materialisation theory (supplied vs defaulted × ordinary / Secure / Restricted) over the REAL materializer (uac-r2 harness) reading the created row; chat copy of the server-set "for" person costs no AppendTo, a chosen contact still does; chat update never sets Assigned To. |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.PostMappingDefaults.cs` | RecordCreationService: must-fire after mapping; supplied kept; explicit null (bind and plain) stays null; source unset → blank + warning; materialisation theory (supplied vs defaulted × ordinary / Secure). |
| `tests/integration/data-mutation/RecordOwnership/RecordRepairModeTests.cs` | Repair never sets Assigned To on a work assignment holding only Assigned To Internal. |
| `tests/Spaarke.ArchTests/RecordTableCopiedDefaultGuardTests.cs` (new) | Source scan: `CopiedFrom =` once, in `RecordTableRules.cs`; `.CopiedDefaultsFor` used only by `OwnedChildWrite.cs` and `RecordCreationService.Mapping.cs` (so update, Repair and push cannot reach it). Must-fire and must-not-fire controls. |
| Fixture edits | `SecureChildOwnershipAiToolTests.cs`: an `_onAppCreate` hook + the chat expectation above. `RecordCreationServiceMappingSeamTests.cs`: optional `_scopes`, `_logger`, `_onCreate`. |

Test-scope justifications (beyond the AC list): the chat-create AppendTo test pins the fix-round F1 below; the ArchTest
is AC1's "arch check" and AC5's structural half for push (no push test harness touches this rule).

## Red / green evidence

- Pre-change create paths (base `OwnedChildWrite.cs`, `RecordCreationService*.cs`, `ChildRecordEndpoints.cs`; new
  registry kept so the tests compile): **10 red / 23 green** of the 33 new or changed behaviour tests (every must-fire red,
  every must-not-fire green). After: 33/33 green.
- Perturbation 1, declaration removed: **13 red** (Sprk.Bff.Api.Tests) **+ 1 red** (ArchTest).
- Perturbation 2, the default not on the row the create writes (so it can only reach the record after the materialiser
  ran): **5 red** — every defaulted materialisation case on both paths; the 5 supplied twins stay green.

## Suites, publish, CVE

- `verify-suites.sh` on `a2eeec771d`: solution build 0 warnings / 0 errors; ArchTests 1580/1580 passed; filtered
  Sprk.Bff.Api.Tests 5689 passed, 7 skipped, 0 failed (5696).
- Fix round 1 (`1f4f8ceb90`), re-checks scoped to the fix diff and its direct callers/callees: targeted filters
  (WorkAssignmentAssignedToDefault, RecordTableRulesTests, RecordCreation*, SecureChildOwnershipAiToolTests,
  RecordRepairModeTests, DataverseCreateRecordHandler, ChildRecordEndpoints, OwnedChildWrite, SecureRootCreateRouteTests,
  RecordCreatorPersonStampTests, AssignedAccessWriterTriggerTests) **889/889 passed**; ArchTests (rebuilt)
  RecordTable* guard **6/6 passed**. Test project build 0 warnings / 0 errors.
- Publish delta on `1f4f8ceb90` vs master `078f62bcad`: **+3,679 bytes** (39,729,309 → 39,732,988; 192 files both);
  total 37.89 MB. Under every threshold.
- CVE: `dotnet list package --vulnerable --include-transitive` — Sprk.Bff.Api has no vulnerable packages.

## Review (one full adversarial pass) and fix round 1

rework: fix-round-1

| # | Finding | Class | Resolution |
|---|---|---|---|
| 1 | Chat create: the copy of the server-set "for" person (the caller's own contact) cost AppendTo on that contact — a regression for callers without it | F1 | Fixed in `OwnedChildWrite.CreateAsync`: a copy of a server-set source is server-set. Test `CreateRecord_AWorkAssignment_TheCopyOfTheServerSetForPerson_CostsNoAppendTo_AChosenContactStillDoes` (red before, green after). |
| 2 | Path parity: `RecordCreationService` checked the copy of a server-set source like any default | F2 | Fixed: `WithCopiedDefaults` returns the (column, source) pairs it added; `DecideAsync` extends `request.ServerSetLookupColumns` with the copies of server-set sources (an adapter's own request default is never made server-set). Seam test `CopiedDefault_TheCopyOfAServerSetAssignedToInternal_CostsNoAppendTo_AndIsWritten` (red before, green after). Supersedes the former K2 limit. |
| 3 | Chat tool result reported the pre-default columns, telling the model Assigned To was not set | F3 | Fixed: `OwnedChildWrite.Outcome.WrittenColumns` (set at both create returns); `OwnedCreateResult` reports it in `columnsSet`, `columnCount` and the summary. Test `CreateRecord_AWorkAssignment_TheToolResultReportsTheCopiedAssignedToAsSet` (red before, green after). |
| 4 | Chat create did not pass its logger, so its copied-default warnings were lost | F3 | Fixed: `_logger` passed to `OwnedChildWrite.CreateAsync`. No dedicated test (logging plumbing; the warning itself is pinned on the child route). |
| 5 | `RecordTableRules` Path text said "every server create", but the service skips a system principal | F4 | Fixed: Path names user-initiated creates (child-records route, chat create, the record-creation service with a calling user). Text only. |
| 6 | Two extra metadata GETs per work-assignment create (the copied default's mapping) | K-limit | Accepted, below. |
| 7 | The negative-control tests pass before and after the change | K-limit | Accepted, below. |

Red proof for the fix round: the four pre-fix sources (`OwnedChildWrite.cs`, `RecordCreationService.cs`,
`RecordCreationService.Mapping.cs`, `DataverseCreateRecordHandler.cs` at `a2eeec771d`) with the new tests: **3 red / 0 green**;
fixed sources restored: 3/3 green.

## Fix round 2 (UAC-r2's review of `3638ec0dd8`, plus the main session's scoped re-check of `1f4f8ceb90`)

rework: fix-round-2 task 037

Tests and the declaration guard only; no production code changed, so the publish delta and CVE result above stand
(`verify-suites.sh` not re-run, per the coordinator's rule).

| # | Finding | Class | Resolution |
|---|---|---|---|
| UAC 1 | AC6 untested: no test ran the isolated (Secure-parent) create with a copied default; UAC's mutant (the isolated branch writes the pre-default item) gave 0 failures | F3 | `SecureRootInheritanceWriterTests.ChildRecordRoute_AWorkAssignmentUnderASecureMatter_IsCreatedIntoIsolationWithTheCopiedAssignedTo_AndItIsOnlySuggested`: the REAL gate isolates; the isolated create body carries `sprk_issecure = true`, the Secure team owner and `sprk_AssignedTo@odata.bind` = the Assigned To Internal contact; the REAL materializer's row is PendingConfirmation, nothing granted or shared. **Perturbation (isolated branch fed the pre-default item): 1 red / 974.** |
| UAC 2 | No declaration guard against a copy onto a FILING lookup (server-set by copy, while RecordCreationService's server-set-filing refusal runs before step 3½ and OwnedChildWrite has none) | K3 taken | `RecordTableRulesTests.CopiedDefault_NoDeclaredCopyTargetsAFilingLookup` (filing = `RecordCreationService.IsFilingLookup(table, column)` or `RecordOwnershipResolver.IsOwnershipParent(RecordReparent.FilingParentOf(table, column))`); must-fire `CopiedDefault_FilingTargetGuard_MustFire_OnASyntheticCopyOntoAFilingLookup` (WA `sprk_regardingmatter`, to-do `sprk_regardingmatter`); must-not-fire `CopiedDefault_FilingTargetGuard_MustNotFire_OnTheRealAssignedToRule`. |
| UAC 3 / re-check gap | The chat "chosen contact" twin is refused on the caller-chosen source before the copy is classified | K1 | Renamed `CreateRecord_AWorkAssignment_TheCopyOfTheServerSetForPerson_CostsNoAppendTo_AChosenSourceIsStillChecked`, with a remark saying it proves only that a chosen source is checked. |
| Re-check | Nothing pinned "an adapter's own default never becomes server-set" (the `&& merged.TryAdd(...)` condition) — fail-open on an access-conferring column if lost | F3 | `RecordCreationServiceMappingSeamTests.CopiedDefault_AnAdaptersOwnAssignedToDefault_IsNeverMadeServerSet_ItIsCheckedAndDropped`: server-set Assigned To Internal = X (no AppendTo on X), adapter default Assigned To = X → created, `DefaultValueDroppedCode` on `sprk_assignedto`, no Assigned To bind. **Perturbation (`copied.Add` unconditional): 1 red / 974.** |

Suites after fix round 2: targeted filters (fix round 1's set plus `SecureRootInheritanceWriterTests`) **974/974 passed**;
ArchTests (rebuilt) RecordTable* **6/6 passed**; test project build 0 warnings / 0 errors.

## Known limits

- K #6: a work-assignment create makes up to two extra metadata reads (the copied default mapped as the caller). One create,
  not a loop; accepted by the coordinator without a fix.
- K #7: the negative controls (supplied value kept, explicit null kept, update/Repair never back-fill) pass both before and
  after the change by design: they pin that the change does not over-reach. The must-fire tests carry the red proof.
- K2 (UAC fix round 2): on RecordCreationService the copy reads the row as mapped, BEFORE the adapter's
  `PostMappingDefaults` merge, so an Office-style "for" person supplied as an adapter default is not copied (warning,
  Assigned To blank). Task 017 decides this (the main session adds it to 017's POML).
- K4 (UAC fix round 2): on RecordCreationService one cause produces two warnings; a test-only path today.
- K4 (re-check): the rebuilt server-set set is `OrdinalIgnoreCase` even when the adapter passed an ordinal set; no
  production caller sets it today.
- Gap (re-check): no test pins that the chat path passes its logger to `OwnedChildWrite.CreateAsync` (logging plumbing only;
  the warning itself is pinned on the child route).
- K2: a system principal's `RecordCreationService` create takes no copied default (post-mapping defaults are caller-only,
  task 018a). No system creator of a work assignment exists.
- K4: field security on `sprk_assignedto`: on the child route a secured column would refuse the create (a supplied value
  would too); on RecordCreationService it would drop the default with a warning. The column is not secured on dev.

## Placement Justification (CLAUDE.md §10, bff-extensions)

Modification inside the BFF's existing create path; no new endpoint, DI registration, package, or background work. The
rule lives in the existing per-table registry (`RecordTableRules`, task 012) and is applied by the two existing create
cores (`OwnedChildWrite.CreateAsync`, `RecordCreationService` step 3½). Component justification (§11): Existing —
`AssignedToDefaults` (to-do/event only, a different precedence), `ServerDefaults` (descriptive only), and the 018a
post-mapping default path; Extension — `ServerDefault` gains `CopiedFrom` and RecordCreationService reuses its default
path, rather than adding a second mechanism; Cost of doing nothing — a work assignment created from a matter keeps a blank
Assigned To, against the owner decision of 2026-10-27 (#2354).
