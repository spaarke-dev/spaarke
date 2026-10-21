# Task 004 notes — mode-column reconcile, dormant columns, seeder #1318 fix (FR-05 a/b/c)

> Executed 2026-10-21 on branch `task/fm-004-005`. Commit: `27944b0727`. Task 005 follows in the same branch; the publish-size
> measurement was taken once, after both tasks (shared with `task-005-notes.md`).

## Rigor

FULL (POML `<rigor>`; confirmed against the tree: `.cs` in Sprk.Bff.Api-adjacent shared libs and the provisioning seeder, tags
`bff-api`, `provisioning`). Model tier sonnet at effort high. Step mode directional. Constraints loaded: api.md, data.md,
testing.md, bff-extensions.md, provisioning.md (seeder), ADR-038, ADR-002, project CLAUDE.md, spec FR-05(a)-(d).

## Deployed mode-column names (Dataverse `describe`, spaarkedev1, 2026-10-21, before any edit)

| Table | Deployed column | Options |
|---|---|---|
| `sprk_fieldmappingprofile` | `sprk_capabilitymode` | Strict 100000000, Resolve 100000001 |
| `sprk_fieldmappingrule` | `sprk_compatibilitymode` | Strict 0, Resolve 1 |

They are **two different columns on two different tables**, not two spellings of one. Code (`$select` lists, mapper) and the
solution (`Entity.xml` of each table) already used exactly the deployed name on each table. **No rename, no `Entity.xml`
edit, no schema change on any environment.** The "discrepancy" in the planning notes was the profile-versus-rule column
difference. I could verify dev only; the escalation trigger (names differ between environments) was not testable beyond dev and
did not fire.

Found in passing: `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` line 24 listed `sprk_compatibilitymode` on the **profile** table (wrong; the
profile has `sprk_capabilitymode`). Fixed in this task. The profile table has neither `sprk_syncmode` nor `sprk_mappingdirection`
(the planning note "the profile entity already reads SyncMode" is inexact: `MapToFieldMappingProfileEntity` hard-codes
`SyncMode = 0`).

## Files

- `src/server/shared/Spaarke.Dataverse/FieldMappingColumns.cs` (new): `ProfileSelect` and `RuleSelect`, the two column lists
  that were duplicated across five inline `$select` strings in `DataverseWebApiService.cs`.
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`: the five sites use the constants.
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/AppConfigSeed/DataverseWebApiFieldMappingSeeder.cs`: rule
  existence filter is `_sprk_fieldmappingprofile_value eq {profileId}` (was the bare lookup name, a Dataverse 400, #1318).
  The sibling profile-by-name query (`sprk_name eq '...'`) is valid; unchanged.
- `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`: "Columns read but unused (dormant)" section with grep evidence; the
  enum table gained the profile mode column; the profile-table description corrected.
- Tests: `tests/unit/domain/Dataverse/FieldMapping/FieldMappingSelectColumnsTests.cs` (new, 3 tests) and
  `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/DataverseWebApiFieldMappingSeederTests.cs` (+1 test, T3b).

## Decisions and deviations

1. **Shared constants instead of leaving five inline strings** (component justification, root §11). Existing: five duplicated
   literals in `DataverseWebApiService.cs` (lines ~892, ~955, ~1042, ~2262, ~2269 before the edit). Extension: constants in place
   were the extension; the only new surface is one `internal static class` in an existing project. Cost of doing nothing: a
   wrong column is a Dataverse 400 on every read and nothing could observe it without an HTTP double (`Mock<HttpMessageHandler>`
   is banned, ADR-038 B1), and task 005 must add `sprk_syncmode` to "both" lists consistently.
2. **Test seam**: `internal` + `InternalsVisibleTo` (ADR-038 Amendment A2: a mapping/filter string that no public surface can
   observe). The existing `Spaarke.Dataverse.csproj` already grants it to `Sprk.Bff.Api.Tests`.
3. **Test location** (main-session correction): the select test is under `tests/unit/domain/Dataverse/FieldMapping/`
   (KEEP path, globbed into Sprk.Bff.Api.Tests, namespace `Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping`), not under
   `tests/unit/Sprk.Bff.Api.Tests/` as the POML says. The seeder test extends the **existing** file in
   `Sprk.Provisioning.ControlPlane.Tests` (the POML says to extend that harness; no KEEP-path project references
   Provisioning.Core, so a KEEP-path copy would need a new project reference. Recorded as K-limit, see Quality gates).
4. The select test checks each selected column against the solution's `Entity.xml` `<LogicalName>` list rather than a hard-coded
   allow-list, so code and solution cannot drift apart silently. It also pins the profile/rule mode columns so they cannot be
   swapped.
5. **Seeder verification run**: against the test double only. A live seed would write rows on dev; the main session withheld live
   writes. The new test asserts every rule-lookup GET carries `_sprk_fieldmappingprofile_value eq <guid> and` and none carries the
   bare `$filter=sprk_fieldmappingprofile eq`. No 400 is possible from that filter shape; a live seed on dev remains
   to be run by the owner if wanted.

## Dormant columns: evidence (grep 2026-10-21)

Nothing branches on any of the four (escalation trigger did not fire):

- `sprk_mappingdirection`: not in any `$select`; only `Models.cs` (profile `MappingDirection`, hard-coded 0) and the constants class.
- `sprk_isrequired`: read into `FieldMappingRuleEntity.IsRequired`; only reader is `FieldMappingEndpoints.cs:378` (copies to DTO);
  `FieldMappingService.ts:274` passes it through.
- `sprk_iscascadingsource`: read into `IsCascadingSource`; no reader.
- `sprk_compatibilitymode`: read into `CompatibilityMode`; `FieldMappingEndpoints.cs:379` copies to DTO; `FieldMappingService.ts:275`
  passes it through; no coercion code branches on it.

## Tests (fail before, pass after)

| Test | Before | After |
|---|---|---|
| `FieldMappingSelectColumnsTests` (3), with `ProfileSelect` mutated to the rule's mode column | 2 failed, 1 passed | 3 passed |
| `DataverseWebApiFieldMappingSeederTests.SeedAsync_RuleExistenceLookup_...` (T3b), with the seeder reverted to the bare name | 1 failed (7 passed) | 8 passed |

Suites run after the change: Sprk.Provisioning.ControlPlane.Tests 2854 passed, 1 skipped, 0 failed; Sprk.Bff.Api.Tests filtered to
FieldMapping and Office 950 passed, 7 skipped, 0 failed (unchanged tests, unchanged results); ArchTests 1211 passed.

## Build, publish, CVE

- `dotnet build Spaarke.sln`: 0 errors, 0 warnings (includes Sprk.Provisioning.ControlPlane.Core/Api/Worker/Tests).
  `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Publish size: see "Publish-size measurement" below (shared with task 005).
- `dotnet list package --vulnerable --include-transitive`: Sprk.Bff.Api and Sprk.Provisioning.ControlPlane.Core, no vulnerable
  packages. No package added.

### Publish-size measurement

Measured once, after both tasks, with `dotnet publish src/server/api/Sprk.Bff.Api -c Release -o <dir>` from short-path
worktrees (removed afterwards with `scripts/worktree/Remove-WorktreeSafe.ps1`) and `Compress-Archive -CompressionLevel Optimal`
(PDBs included). Master was built fresh at `44063b3473` (origin/master after `git fetch`, 2026-10-21).

| Build | Zip | Files | Raw |
|---|---|---|---|
| origin/master `44063b3473` | 39,314,273 B (37.49 MB) | 192 | 125.13 MB |
| work-branch tip `036bc69bab` (before 004/005) | 39,327,472 B (37.51 MB) | 192 | 125.18 MB |
| `task/fm-004-005` `4f31121481` (after 004 and 005) | 39,327,779 B (37.51 MB) | 192 | 125.18 MB |

Delta against fresh master: **+13,506 B (+0.013 MB)**, file count unchanged (192). The whole of that is the project branch's earlier
tasks; these two tasks add **+307 B** over the work-branch tip. Far under the +5 MB, 55 MB and 60 MB thresholds.

## Placement Justification (bff-extensions.md)

No new endpoint, service, DI registration, background work or package. The change is a column-list constant and a mapper-seam
visibility change inside `Spaarke.Dataverse` (already the BFF's Dataverse read library) and a one-line OData filter fix in the
provisioning seeder. ADRs: ADR-002 (no plugin; the read path stays server-side), ADR-038 (tests).

## /conflict-check (2026-10-21, 30 open PRs)

- #2177 `work/spaarkeai-word-add-in-r1` touches `Spaarke.Dataverse/Models.cs`: 004 does not edit Models.cs; 005 adds one
  property to `FieldMappingRuleEntity` (different region; additive).
- #2169 `uac/189-own-narrowing` touches `scripts/README.md`: 005 adds one section and one change-log line.
- ebilling-r1's `UpdateExistingRecordFieldsAsync` overload on `IFieldMappingDataverseService` is not touched; this change is additive
  and does not edit that interface.
- Task 013's files (Services/RecordCreation/*, Office services, CreateTimeFieldMapping.cs, RecordOwnerAssignmentCensusTests.cs) untouched.

## Quality gates (Step 9.5; one pass over both tasks' diffs)

code-review and adr-check run over the combined diff of `27944b0727` and `4f31121481`. Triage:

- F-class: none open in this task's code.
- Found in passing, outside this task's scope (reported to the operator, not fixed): see "Reported, not fixed" in `task-005-notes.md`
  (seeded rules default to One-time; `FieldMappingHandler.ts` selects `sprk_syncmode` from the profile table, which has no such column).
- K1-K4: (K2) `sprk_capabilitymode` is selected on every profile query and mapped nowhere: harmless, documented as dormant;
  (K2) the seeder test lives in `Sprk.Provisioning.ControlPlane.Tests`, not a KEEP-path category: it extends an existing file as the POML
  directs and no KEEP-path project references the seeder.
- ADR check: ADR-002 compliant (no plugin, server-side read); ADR-010 compliant (no DI change); ADR-038 compliant (A2 internal seam,
  no `Mock<HttpMessageHandler>`, KEEP path for the new unit test); ADR-044 compliant (GUIDs are Dataverse-returned, bare lowercase).
  No violation, so no §6.5 path was needed.

## Acceptance criteria

1. Notes record the `describe` result; code and `Entity.xml` use the deployed name per table and the non-deployed spelling is not
   selected on either table: **met**. (`sprk_compatibilitymode` is not on the profile table and `sprk_capabilitymode` is not on the rule
   table; a test pins this.)
2. Seeder rule filter uses `_sprk_fieldmappingprofile_value`; a test asserts it; a seed run reports no 400: **met** against the test
   double (no live seed, by instruction).
3. Framework doc lists the four dormant columns with evidence: **met**.
4. Negative: no deployed column renamed or removed; solution diff empty: **met** (no `Entity.xml` change).
5. Builds 0 errors, existing tests pass, publish delta and CVE recorded: **met**.

Escalations: none.

rework: none task 004
