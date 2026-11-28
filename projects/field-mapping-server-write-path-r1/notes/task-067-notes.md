# Task 067 — the filing parent's mapped columns copy whatever the caller can read on them

> Owner decisions 2026-11-27 (`notes/decisions.md`, two entries). Access-control review conditions C1–C7 in the POML.
> FULL rigor, opus/high. No live test (owner).

## 1. Rule as built

From the row's own **filing parent** (create: the caller's filing record, table and id; send: the analysis that is the email's primary association, table and id; push: a listed Filing edge, or family mode with a listed edge; Repair: the row's filing parent), a mapped source column is read app-only and copied whether or not the caller can read it. Repair copies field-secured filing-parent columns too.

Kept (owner kept all three on 2026-11-27):
- **C1** The caller's record-level Read on the source is probed first (`MappingSourceReadProbe.ReadAsync`; push: `AuthorizePushSourceAsync`, then the caller's own read must return the row). Without it nothing is read app-only and nothing is copied; the answer equals an unknown id.
- **C2** Any other source keeps `MappingSourceCallerMask.ApplyAsync` and the `FieldSecuredSourceRule.Split` drop, unchanged.
- **C3** `AccessModelSourceColumns` (closed list, every table): 23 exact names (contact identity/invitation/external-access columns; `sprk_issecure`, `sprk_accessinheritance`; systemuser home tenant/object and `sprk_primarycontact`; invoice `sprk_vendororg`, `sprk_firmhold`; document `sprk_restrictedbysubmission`, `sprk_graphdriveid`, `sprk_graphitemid`, `sprk_graphitemidbound`) plus the `sprk_identitycollision*` prefix. Such a source keeps the caller's view (create/send masked, push as-caller, Repair not copied), with a warning naming the target. Invoice totals and `sprk_reviewdetail` copy (the caller already reads both; the invoice is never written).
- **C4** The push's app-only read is one row, `$select` of exactly the mapped non-access-model columns. Create/send keep their existing single-row read (it also selects gate columns, never copied).
- **C7** One Information line per copy past the mask: surface, rule id, source and target column names, never the value. Push logs only after a child write carried that rule's target.

## 2. Where

| Surface | File:line (at 9331c486da) |
|---|---|
| Mask exception | `Services/Dataverse/FieldMapping/MappingSourceCallerMask.cs` `ApplyOrLiftForFilingParentAsync`, `LogCopiesPastTheMask` |
| List | `Services/Dataverse/FieldMapping/AccessModelSourceColumns.cs` |
| Create | `Services/RecordCreation/RecordCreationService.Mapping.cs` (decision `sourceIsFilingRecord`, lift call, C3 warning, C7 log) |
| Send | `Services/Dataverse/FieldMapping/SendTimeFieldMapping.cs` (`SourceIsTheCommunicationsFilingParent`) |
| Push | `Api/FieldMappings/FieldMappingEndpoints.cs` (Filing-edge decision before any read, `RetrieveFilingParentValuesAppOnlyAsync`, preview from `callerValues`, log after the write loop) |
| Repair | `Services/RecordCreation/RecordCreationService.Repair.cs` (`SourceColumnsRepairMayCopyAsync`, `RepairSourceColumns`) |
| Guard | `tests/Spaarke.ArchTests/AccessModelSourceColumnsGuardTests.cs` (two-pass per table over every `Entity.xml`; contact, systemuser, tables with `sprk_issecure`, `sprk_document`, `sprk_communication`; fails on zero Entity.xml for contact/systemuser; pinned business list of 23) |

## 3. Branches and commits

- `task/fm-067` (6 commits, 42828b51a9..e8f51b0d49) → work `5b91d95cfd`; master merged `7efc4ea8d2` (includes mcp-r1 D1 #3316).
- Review fixes `task/fm-067b` (6 commits) → `8f8d96bfb3`.
- Closed access-model list, invoice columns copy, firm/hold/contact link kept `task/fm-067c` (3 commits) → `9331c486da`.

## 4. Reviews

- Opus and Fable adversarial passes on `64b8b9081e..e8f51b0d49`. Both: **F1** the push preview returned the lifted value (`PushPreviewChange.To`); **F2** family-mode push read app-only before the Filing-edge check (false C7 line). Also F3 (no family/preview tests; the Office "caller read fails" leg vacuous), F2-2 (guard scoped per Entity.xml element). All fixed with must-fire tests.
- Sonnet scoped re-check of `7efc4ea8d2..8f8d96bfb3`: no F-class. K notes: a null table cannot widen the list (now moot: the list is table-independent); preview understates the apply for lifted columns (by design, documented).
- Access-control review: approach approved; conditions C1–C3 unchanged; three columns kept on the list (they decide access); totals and review detail approved off it.

## 5. Mutation evidence

| Mutation | Tests failing |
|---|---|
| Skip the caller Read probe | 24 |
| Push app read without a caller row | 2 |
| Create lifts every source / by id only | 16 / 3 |
| Send ignores the table / takes any association | 3 / 2 |
| Push lifts on every lookup | 2 |
| Access-model list never matches | 10 (all four surfaces) |
| Push selects an extra column / lift keeps unmapped columns | 3 / 1 |
| Repair drops field-secured columns | 1 |
| No lift at all | 15 |
| Preview from lifted values + edge check after the reads | 3 |
| Push log at lift time | 3 |
| Failed Read probe treated as allowed (Office legs) | 3 |
| One-pass guard scan, no document/communication scope | 7 |
| Re-add `sprk_totalamount` to the list | 4 |
| Send read check always passes | 7 |

## 6. Gates (work branch at aed446f7b0)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- ArchTests (built, full): 2913/2913.
- Filtered `Sprk.Bff.Api.Tests` (verify-suites filter): 13,869 passed, 228 failed in one run — all `TaskCanceledException` after 2.5–5 min in Office/Document classes under machine load. Rerun of the failed classes single-process: 886/900; the 14 still failing all passed run alone (72/72 and 27/27). The solution build in that run hit MSB3021 file locks only.
- `dotnet list package --vulnerable --include-transitive`: none.
- Publish delta: see §7.

## 7. Publish delta

`scripts/publish-delta.ps1`, fresh builds in throwaway worktrees: master merge-base `8b38e97667` → 43,735,250 bytes, 194 files; work `aed446f7b0` → 43,743,728 bytes, 194 files. **Delta +8,478 bytes** (41.72 MB total; well under the +5 MB / 55 MB thresholds). No vulnerable packages.

Placement: all new code sits in the existing field-mapping and record-creation services and the push endpoint (`Services/Dataverse/FieldMapping/`, `Services/RecordCreation/`, `Api/FieldMappings/`). One new file, `AccessModelSourceColumns.cs` (a static closed list; no DI registration, no package, no endpoint). Existing: `MappingSourceCallerMask` / `FieldSecuredSourceRule`. Extension: the lift is a named method on the mask, the list a static it consults. Cost of doing nothing: the owner's rule (a mapping rule maps regardless of the caller's privileges) is not met on any of the four surfaces.

## 7a. Known limits (K-class, no fix)

- The push preview understates what the apply copies for a lifted column (the preview is the caller's own view; no lifted value appears in any response).
- A push `FieldResult` reads Mapped or Skipped for a lifted, non-access-model column. That says whether the filing parent's column holds a value, never the value. It follows from the owner's decision to copy past the column view, and the copied value is then in the child's column anyway. The access-control review listed this as K-class on 2026-11-28.

The access-control review approved #3328 on 2026-11-28 after checking the code. It holds two conditions: 067 enters a dev candidate only with #3329 (#3326) merged beside it, and it gets both merge SHAs.

## 8. Follow-ups

- #3326 (DI-053): the push refuses typed-writer target columns (same predicate as create and Repair). `task/fm-068`, approved by the access-control review; its own PR right after 067; deployed to dev together with 067.
- #3327 (DI-054): the record-family panel's skipped-rules line covers server-managed columns (shared library).
- Two ArchTests ask for a note under `projects/ebilling-r1/notes` about `AccessModelSourceColumns` naming invoice columns; that folder belongs to ebilling.
