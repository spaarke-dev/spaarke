# Task 045b — the update planner on every generic update path + guard 2

Rulings applied: decisions.md "2026-11-07 — Owner approves the 045 recommendations":
- Q1 (a): the 18 pair tables.
- Q2 (a): fill from agreement columns only, never from stamps or mapped lookups.
- Q4 (b): the pair follows a moved typed lookup.
- Q5 (a): a log event, the response unchanged.
- Q7: re-derive the display on a move, clear it otherwise.

Also the UAC-r2 condition: no AppendTo check on the playbook and output-mapping paths. Design: `task-045-regarding-propagation-design.md` §2.2.

## What changed

- **`OwnedChildWrite`** (`src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs`): the update planner.
  - `PlanRegardingPairAsync` is pure over the readers a path passes in. Its payload views are `PairChangeOf` (mapped item, or Web API dictionary), and its reads come from `RegardingPairReaders`: `CallerReaders` (the caller's client; the catalog via `CallerWriteAuthorization.PairTypeTableAsync`) or `AppOnlyReaders` (the reader the path already has).
  - Two wrappers apply the plan:
    - `KeepRegardingPairWholeAsync` (caller paths) maps the planned columns as the caller, over the item.
    - `KeepRegardingPairWholeAppOnlyAsync` (app-only paths) returns a copy of the payload, and writes the type as `sprk_RegardingRecordType@odata.bind`.
  - `KeepPairInStepAsync` keeps the project's "completes" rule and hands every other table to the planner. The work assignment's #1616 "follows" code is now the planner's rule 4, widened to all 18 tables.
- **Call sites.** Each runs the planner before its checks and its write:
  - `ChildRecordEndpoints.UpdateAsync` — the child-records PATCH, plus the event and communication filing routes, which call it;
  - `DataverseUpdateRecordHandler` (through `KeepPairInStepAsync`, now logged);
  - `UpdateRecordActionCore.UpdateAsync` — the playbook Update Record node and confirmed proposals;
  - `DataverseUpdateHandler.UpdateAsync` — the output mapping;
  - `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` — the push (`lookupsAreMapped: true`).
- **Invoice-family ordering (main-session decision, second session).** On the three app-only paths the invoice-family guard
  (`InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync`) now runs BEFORE the planner, so a family row whose reads fail keeps
  the guard's own `invoice_family_read_failed` (the existing `ActionSeamTests` contract, unchanged). This is safe because the
  planner writes only the pair and its display columns, never a lookup the guard decides on, so the guard's decision is the
  same either way. The secure-root filing gate (work assignment, project; never a family table) stays after the planner,
  because the pair is a filing column there. It stays fail-closed: if either read fails, nothing is written (pinned by
  `ActionSeamTests.UpdateRecordAsync_AnInvoiceWhosePairPlannerReadFails_AfterTheFamilyGuardPassed_WritesNothing` and
  `RefileWriterBindShapeTests.AppOnlyPath_ThePlannersRowReadFaults_WritesNothing`).
- **Display clears are not candidate parent clears.** `KeepRegardingPairWholeAsync` writes the display columns it clears as
  nulls, but leaves them out of `MappedItem.ClearedColumns`. `RefileAsync` reads every listed clear as a candidate parent
  clear, so listing them would turn an ordinary pair move into a re-file. A display clear the caller sent itself stays listed,
  as before.
- **Refusals add no new kind (NFR-07):**
  - caller paths: the caller's own failure, or the re-file core's uniform not-found;
  - app-only paths: `RecordOwnerUnresolvedException(ParentUndetermined)`, which each path already maps.
- **Guard 2:** `tests/Spaarke.ArchTests/GenericUpdatePathsPreparePairGuardTests.cs`.

## Planner rules

The planner reads the row once, and only when the update writes a pair column or sets or clears an agreement lookup.

| Update | Result |
|---|---|
| clears the id, sends no type | type and display columns cleared (those not sent) |
| a type with no id after the write, exactly one agreeing record | id filled, display derived |
| a type with no id, none or several agreeing records; only a stamp column while an intermediate is bound; only a mapped lookup (push); catalog read 403 | id, type and display cleared; warning log event 45201. On a caller path, AppendTo on the sent type is asked first (045a F4): no AppendTo gives the uniform 404 |
| a type with no id, catalog read 404 | left as sent (the write's own uniform not-found) |
| a type with no id, catalog read any other failure | refused, nothing written |
| moves the id, without sending all three display columns | display re-derived from the type; cleared when the type cannot be read |
| moves or clears an agreement lookup the pair repeated (same id, type naming that table), with no pair column sent and no other lookup still repeating it | pair moves (display re-derived) or is cleared; a failed catalog read refuses (#1616) |
| a whole pair, an empty pair, an id-only pair, a pair not in step, the project, a non-pair table, an update touching neither | untouched; no read when the update touches neither |

The log names the path, table, row and outcome, never the record the pair names.

## Component justification (§11)

| Member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `PlanRegardingPairAsync` + `RegardingPair*` records | `KeepPairInStepAsync` (project completion, work-assignment follow); `RecordCreationService` step 4½ (create-only, private, item-shaped) | `KeepPairInStepAsync` is extended and now delegates. Step 4½ cannot serve updates: it is private to create, and it reads no row | On 17 tables an update leaves a type-only half pair, or a pair naming a record the typed lookup no longer names (the secure-root readers file by the pair) |
| `KeepRegardingPairWholeAsync` / `…AppOnlyAsync` | the same | one planner, two payload shapes (mapped item vs Web API dictionary); each path keeps its own reader (design §2.2 item 4) | the app-only paths (playbook, output mapping, push) keep writing half or stale pairs |
| `RegardingPairTables` | none (grep: no 18-table set) | — | the planner cannot tell a pair table from the matter's lone type column |
| `RegardingPairKeptWholeEvent` (45201) | none | — | Q5 (a): a clear is otherwise silent |
| `lookupsAreMapped` parameter | 045a's mapped-candidate exclusion (create) | parameter on the shared planner | the push would fill a pair from a mapped value (against Q2) |
| guard 2 | guard 1 (create side) | different signal (generic update paths) | a new generic update path skips the planner silently |

## Placement

This is all inside the BFF write core (`Services/Dataverse/OwnedChildWrite.cs`), the existing single owner of the update path's pair rules (I-3). It adds no endpoint, DI registration or package, and no new background work. `UpdateRecordActionCore` resolves the `IGenericEntityService` it already resolves for the invoice-family guard, from a scope, as that guard does.

## Evidence

**The 16 failures inherited from the WIP (9c2b33b7bc), and how each was resolved.** Assertions were not weakened, with
one stated exception (the push's call budget, below).
- **11 push tests** (`RefileWriterBindShapeTests`). The harness passed the push no `appOnlyEntities`, so the planner
  failed closed with `record_owner_parent_undetermined`. The production endpoint passes its `entityService`
  (`FieldMappingEndpoints.cs:756-769`). Fix: the harness passes the world's `IGenericEntityService`, as production does.
  The 500-child budget moved from 5 to 6 calls per child, and the test now pins exactly one planner row read per child.
  This is an intent change under Q4 (b), which puts the push in scope; the "no descendant pass" assertion is unchanged.
- **4 `DataverseUpdateRecordHandlerTests` "Task 156" tests.** The caller client now scripts the planner's row read (the
  communication) and the event's `ManyToOneRelationships`. The mapper reads those relationships to write the display clears
  that Q7 requires on an untyped pair move.
  - The event tests also exposed a planner side effect: its display clears were listed as `ClearedColumns`, which
    `RefileAsync` reads as candidate parent clears, so an ordinary pair move went down the re-file path. Fixed in
    `KeepRegardingPairWholeAsync` and pinned by
    `RegardingPairUpdatePlannerTests.CallerPath_ADisplayClearThePlannerAdds_IsWrittenAsNull_ButIsNoClearedColumn`.
- **1 `ActionSeamTests` invoice-family test.** Fixed by the ordering change above. The test is unchanged and passes.
- **`CommunicationFiling_MovingThePairWithoutItsNumber_…`.** The production code was right; the harness was wrong. The
  scripted caller metadata for `sprk_communication` lacked the live `sprk_regardingrecordtype` lookup (SpaarkeMaster
  `sprk_Communication/Entity.xml:2786`). The mapper therefore refused the type bind with 400 before the planner ran. The
  harness gained the lookup, and the test passes unchanged. Added
  `CommunicationFiling_ReTypingThePairOverTheSameId_ReDerivesItsNumberAndDisplay`: a re-type over the same id re-derives
  the number from the new type's number column, plus the name and url.

**Added per-path tests (AC3).** `RefileWriterBindShapeTests.AppOnlyPath_MovingTheLookupThePairRepeated_MovesThePairWithIt`
and `AppOnlyPath_ThePlannersRowReadFaults_WritesNothing`, each run on the node, the output mapping and the push.

**Fail-first.** I restored the five call sites from `origin/work/field-mapping-server-write-path-r1` and removed
`KeepPairInStepAsync`'s delegation to the planner (the chat path). Result: **15 failed / 24 passed** over 39 tests.
- Every new path test failed: 7 caller-route tests, the 6 app-only per-path cases and the ActionSeam fail-closed test.
- The 24 that passed are the must-not-fire `ChildRefile_AnUpdateTouchingNoPair_IsUnchanged`, the unchanged ActionSeam
  invoice-family contract, and pre-existing `CommunicationFiling_*` tests the filter also caught.

The sources were then restored, with no diff. The planner unit tests (`RegardingPairUpdatePlannerTests`) call
`OwnedChildWrite` directly, so they do not compile against pre-045b sources; their fail-first is the WIP's.

**Guard 2 seeding.** I renamed `OwnedChildWrite.KeepRegardingPairWholeAppOnlyAsync(` in `DataverseUpdateHandler.cs` to a
non-planner name, in the source the guard scans, and ran with `--no-build`. Both rules fired:
- "line 162: a generic update ('ParentChangesWithClearsIn(') with no call of the regarding pair planner";
- "UpdateAsync does not call the regarding pair planner in its own member".

Restored: guards 1 and 2 pass, 6/6.

**Gates.**
- Filtered suites (FieldMapping, DataverseUpdateRecordHandler, ActionSeam, ChildRecord, OwnedChildWrite, UpdateRecord,
  SecureChildOwnership, RegardingPair, Push, DataverseUpdateHandler, RefileWriterBindShape, InvoiceFamily): **2297/2297**
  pass. Before this session: 16 failed of 1965 on the narrower filter.
- ArchTests guards 1 and 2: 6/6.
- BFF build (`--no-incremental`): 0 warnings, 0 errors.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Publish-size delta: not measured in this session. There is no package, asset or project change (code only); the main
  session measures it at merge.

## Q8 (update side) — out of scope for 045b; moves to 045d

Main-session decision (2026-11-08): 045b does not implement Q8's update side. Task 045d carries both halves (the create
side 045c's notes describe and this update side), re-deriving inside the guard's own decision. The analysis below is the
input for 045d.

`InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync` (`InvoiceFamilyCoreRecordGuard.cs:731`) needs two things:
- an app-only `IGenericEntityService`;
- a catalog read **by table name** (`RecordTypeLookup`, `ICommunicationDataverseService.QueryRecordTypeRefAsync`).

It doesn't fit the update paths for three reasons:
- The chat tool and the child-record routes have neither reader. Adding them breaks design §2.2 item 4 ("no path gains a reader") and puts an app-only read on a caller path.
- The guard that decides the family's core record runs inside `RefileAsync` (`InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync`), after the planner, so the planner cannot know the decided record without running the guard twice.
- On the app-only paths the guard runs after the planner too (`UpdateRecordActionCore.cs` `:284`).

What is covered today: when the family row's core lookup is an agreement column the pair repeated, rule 4 moves the pair with it and re-derives its display.

What is not covered: a core-table switch (for example matter → project) on a family row clears the pair rather than re-deriving it.

Recommendation: 045d carries both halves, re-deriving inside the guard's own decision (the `PreWriteResult` shape in the 045c notes).

## Known limits

- K: the app-only display read writes a name read app-only. This is accepted (design §2.2.4, UAC-r2), as are the existing app-only guards on those paths.
- K: the planner adds one row read (as the path's reader) whenever an update touches the pair or an agreement lookup, plus one catalog read and one record read on a fill or move.
  Measured on the push: the 500-child no-op push went from 5 to 6 Dataverse calls per child. The test's bound moved from
  5 to 6 and now pins exactly one planner row read per child; the "no descendant pass" assertion is unchanged (Q4 (b)
  puts the push in scope, so the read is required, not incidental).
- K: names longer than 100 characters are cut (`InvoiceFamilyCoreRecordGuard.RegardingRecordNameMaxLength`).
- K2: on an app-only path, rule 4's catalog read answering 404 (the row's stored type names a deleted catalog row) leaves the
  payload as sent: the lookup moves and the dangling pair stays. On a caller path the same case refuses. Rare (a deleted
  catalog row); the readers cannot resolve a type that names nothing.

## Found in passing

- **Pre-existing, `OwnedChildWrite.RefileAsync`** (`OwnedChildWrite.cs:1376`, "A cleared column may be a cleared lookup").
  Every `ClearedColumns` entry of a child update becomes a candidate parent clear. The chat mapper
  (`DataverseWriteItemMapper.MapCoreAsync`, `:169` and `:269`) lists every null, lookup or not, even though it already
  knows which nulls are lookups (`clearBinds`). So a chat-tool update that clears a plain text column runs the re-file path:
  WhoAmI, a row read and the reparent, where an ordinary PATCH would do. The resolver treats it as no parent change, so the
  outcome is correct and only the cost is extra. The browser mapper (`MapWebApiPayloadAsync`) lists lookup clears only.
  Not fixed here (outside the planner's scope); 045b's own display clears are kept out of the list.
- **Harness gap, fixed in scope.** `SecureChildOwnershipAiToolTests.ScriptedUserClient` was missing the communication's live
  `sprk_regardingrecordtype` lookup.
- **Harness note.** `RefileWriterBindShapeTests.Handler()` (default) and several other output-mapping harnesses pass a
  loose `Mock<IGenericEntityService>`, whose `RetrieveAsync` answers null. The planner reads that as "row not found" and
  leaves the payload as sent, so those tests do not exercise the planner (the per-path tests above do). Not a defect.
- **Known: lint-staged commit hook.** Each commit pushes a backup stash on the shared stack ("Backed up original state in
  git stash", this session: a1d0320413, a67117d63d, 337473d4fd).

rework: fix-round-1 task 045b
