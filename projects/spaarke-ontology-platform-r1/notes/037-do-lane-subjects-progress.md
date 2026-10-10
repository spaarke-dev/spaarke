# 037: Signal writer accepts Do-lane subjects: progress and completion record

> **Date**: 2026-10-10 · **Rigor**: FULL · **Branch**: `task/ontology-037-do-lane-subjects` (from `origin/docs/ontology-platform-design` @ `48742f4d2`)

## 1. Live counts (spaarkedev1, 2026-10-10, step 1)

| Subject | Under a matter | Project only (no matter) | Neither matter nor project |
|---|---|---|---|
| sprk_todo | 38 | 3 | 25 with none of the four core lookups (13 user-owned, 12 team-owned) |
| sprk_event | 42 | 9 | 32 (26 with none of the four core lookups: 23 user-owned, 3 team-owned) |
| sprk_workassignment | 22 rows, each its own core record (D-36) | | |

## 2. uac-r2 files re-read (origin/master, 2026-10-10)

| File | Last commit |
|---|---|
| `Services/Dataverse/CoreAncestorResolver.cs` | `d7fdcafc3` (#1458, task 173: inherited Access Permission; the stamp derivation is unchanged) |
| `Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` |
| `Services/Access/SecureChildLineage.cs`, `config/secure-record-owner-role.json` | `0615f9781` (ontology 039, #1390) |
| `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` | `7b89a63ab` |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `d5e1dffe7` |

No open PR touches `SignalWriter.cs`, the census or the telemetry file. The plan stands.

## 3. What changed

`SignalWriter` (ctor gains `CoreAncestorResolver`, the existing singleton):

- `VerifiedSubjects` (matter, communication, event, to-do, work assignment) replaces `VerifiedMatterDerivation`. Service request, project, invoice, document stay refused as subjects.
- Core record only from `CoreAncestorResolver.ResolveStampsAsync`: `CoreTarget` is itself; one stamp is that stamp; several stamps: the record the subject's own regarding pair (`sprk_regardingrecordid`) names, then matter over project, else refuse (`core_record_ambiguous`); `Error` refuses (`core_record_unresolved`); `NoAncestor` is the D-35 owner-only path for the Do lane (Decide lane: refuse).
- Written: `sprk_corerecordtype` (the single active `sprk_recordtype_ref` row for the core table; none or several refuses `core_record_type_not_cataloged`), `sprk_corerecordid`, and `sprk_matter` only when the core is a matter.
- Ownership: the uac-r2 resolver is called with the CORE record as target and the subject as parent (D-33/D-36/D-38, no skips). Secure answer: Secure Record Owners team in the create. Otherwise `owningbusinessunit` = the core record's BU (read from the core's table, any type). No-core: `ownerid` = the subject's owner, read back (`no_core_owner_mismatch`).
- `sprk_duedate`: event `sprk_duedate`, to-do `sprk_duedate`, work assignment `sprk_responseduedate`. Written on create, refreshed on reconcile only while Open or Acknowledged, never on Resolved.
- `BuildDedupeKey` is unchanged.
- `SignalWriteResult` gains `CoreRecordEntity`, `CoreRecordId`; `GroupingMatterId` is now `Guid.Empty` when the core is not a matter.

## 4. Deviations from the POML (recorded)

1. **No typed `sprk_project` on the Signal** (acceptance criterion 4 says "sprk_project is that project"). Task 007/039 confirmed there is no typed core lookup on `sprk_signal`; a project core is `sprk_corerecordtype` = project catalog row + `sprk_corerecordid`, with `sprk_matter` null. Same deviation 039 recorded.
2. **Team-owned no-core items.** D-35 says `ownerid = that systemuser`. 12 of the 25 no-core To Dos in dev are owned by a BU default team. The writer copies whatever the item's owner is (system user or team), so those items are not dropped (D-38). Proven live for a team (seam test `WriteAsync_TeamOwnedTodoWithNoCoreRecord_IsOwnedByThatTeam`); no 0x80040299. An owner that is neither is refused (`no_core_owner_unresolved`).
3. **Communication subjects now group under a project too** (D-34): a communication with only a project used to be refused as `matter_lookup_empty`; it now groups under the project. A Decide-lane subject with no core record is still refused.
4. **Direct filed-under (D-37)** is implemented as "the subject's regarding pair names one of the stamps", which is the `DirectRootLink` case of `ClassifyStampSource` without its carrier handling; a pair naming an intermediate is not direct, so matter-over-project applies.
5. **Acceptance criterion 15 (fake fifth core type)** is not testable without mutating `CoreAncestorResolver`'s static taxonomy. Evidence instead: the writer has no per-type branch for work assignment or service request (both proven generic by tests: core = itself, core = service request); the only type names in the grouping code are the two D-37 rules (matter over project) and the `sprk_matter` column write the POML mandates.
6. **uac-r2-owned file changed**: `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` gets one `PerUser` entry for `SignalWriter.BuildEntity` (the D-35 owner write copies the item's owner; there is no parent for the resolver to ask). This needs uac-r2's review (POML coordination constraint 3); linked from the PR.

## 5. Evidence

- Unit: `SignalWriterTests` (see PR for counts). Seam: 8 live tests in `SignalWriterSeamTests` against spaarkedev1 as the writer principal, all pass; probe rows `zz-037-*` deleted, confirmed by query (todo, event, work assignment, `ONTOLOGY-DEV-TEST%` signals all 0).
- ArchTests: 958 of 958 after the census entry (958 passed, the census was the only failure before it).
