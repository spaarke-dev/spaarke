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

FILL-IN

## Q8 (update side) — stopped, does not fit cleanly

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
- K: names longer than 100 characters are cut (`InvoiceFamilyCoreRecordGuard.RegardingRecordNameMaxLength`).

## Found in passing

FILL-IN

rework: FILL-IN
