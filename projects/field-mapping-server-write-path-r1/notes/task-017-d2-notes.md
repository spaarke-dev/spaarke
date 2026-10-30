# Task 017-d2: chat platform tables registered (task, contact, account)

Branch `task/fm-017-d2`, from `origin/task/fm-017` (14b801a53f). This is a separate file so it does not conflict with 017's
notes; the main session folds it in.

Owner decision 2026-10-30, option b (`decisions.md`, last entry): the chat `dataverse.create_record` keeps creating
`task`, `contact` and `account` once it is on `RecordCreationService`. It keeps today's owner, the resolver's team. Every
other unregistered table stays `record_creation.table_unsupported`.

## What today's path does, and the entry for each table

Today all three tables take `OwnedChildWrite.PathFor` → `Owned`. Each is user-owned, none is in `PerUserTables`, and none
is a communication or a thread. So `OwnedChildWrite.CreateAsync` creates them.

| Registry field | Today's behaviour (all three) | Entry |
|---|---|---|
| Kind | Not a root. `RecordCreatorPerson` stamps roots only, and no Assigned* materialisation runs. | `Child` |
| Parents | `OwnershipOf` → `RecordOwnershipContext.ForRow` over every lookup the item binds. The task's `regardingobjectid` (the polymorphic activity regarding) is read like any other lookup. The service's step 7 uses the same builder over `OwnedChildWrite.LookupsOf`. Contact and account bind no parent. | (no field; same builder) |
| Server-owned columns | Only `CommonServerOwnedColumns` and the creator column, which every entry refuses. | none (default) |
| Mapping-protected, autonumber, optional references, pre-write rules | none | defaults |
| Field mapping | The chat handler applies none. | `MappingExclusion = ChatPlatformTableIsNotMapped` (a named rule with a rationale). The default (`MappingApplies`) would declare a policy today's path does not have. |
| Search-indexed | no | `false` (default) |
| For-person, server defaults | none (`ForPersonColumns` has no entry, so no copied default) | none (defaults) |
| Ownership exception | none: the resolver's team owns the row. A task regarding a matter gets the matter's team; an unfiled row gets the caller's business-unit team. | `null` (default) |
| Under a secure owner | Refused with `SecureFilingRefused`, because none is a reparentable child. | The service's step 7 (`OwnerDecided`) applies the same rule: OwnerUnresolved / `NoOwnerSource`. |

Choices of inert value:
- `Child` is inert: only `Root` drives behaviour (`ParentLineage.SecureRootColumns`, `AssignedAccessMaterializer`, step 7's secure flag).
- `MappingExclusion` is the explicit form of "not mapped". The service maps only when a request asks (`request.Mapping`). Repair skips mapping for these tables as well.
- Behaviour the service adds that the old core lacked: the after-create I-1 restamp, the I-16 refresh and the sharee-mirror steps. These are best-effort and no-ops for these tables. Not asserted here.

No service change was needed.

## Tests

- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.ChatPlatformTables.cs` (new partial,
  same harness as 017b item 9):
  - `AChatPlatformTable_ThroughTheService_GetsTheOwnerAndRowTheOwnedCreateGives`, 4 rows: a task regarding a matter, a
    task regarding nothing, a contact, an account. Each row asserts that `PathFor` is Owned today, that both creates succeed,
    the same owner (`ChildTeam`), and that every written column is byte-identical.
  - `ATaskRegardingASecureMatter_IsRefusedOnBothPaths_AsTheSecureFilingRefusal_AndNothingIsCreated`: the old core gives
    `SecureFilingRefused`; the service gives OwnerUnresolved / `NoOwnerSource`, with the Secure owner decision. Nothing is
    created.
  - `AnUnregisteredChatTable_IsStillRefusedByTheService_AsTableUnsupported` (`phonecall`): a must-not-widen control.
- `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs`:
  - new `ChatPlatformTables_DeclareOnlyWhatTheOwnedCreateAppliesToday` (3 rows);
  - `MappingApplies_EveryOtherTable_IsTrue` now lists the three tables. This is a deliberate registry change.
- `tests/Spaarke.ArchTests/RecordTableRulesRegistryGuardTests.cs`: `CreateTables()` now includes the owner's three
  chat tables (`ChatOfferedPlatformTables`). The chat tool has no compiled accept-list, so the guard names them literally.
- Fixture: `ScriptedUserClient.EntitySets` gained `account` (additive).
- The two existing task tests (`CreateRecord_ATableOutsideTheOwnershipSetFiledTo{ASecure,AnOrdinary}Matter_…`) are
  unchanged and pass.

**Must-fire** (with the `task` entry removed and rebuilt):
- 5 tests fail: both task rows, the secure-matter test, the domain `task` row and `MappingApplies_EveryOtherTable`.
- In ArchTests, `EveryCreateTable_HasARegistryEntry` and `Guard_MustFire_…` fail.
- The contact, account and must-not-widen tests and the two existing task tests still pass.

After the entry was restored, everything passes.
