# Task 005 notes — read sprk_syncmode on rules + migration script (FR-05d)

> Executed 2026-10-21 on branch `task/fm-004-005`, after task 004. Commit: `4f31121481`. Prescriptive step sequence followed in
> order; no deviation from the sequence. The live `-Apply` step is **pending the owner's explicit OK** (escalation trigger 1,
> by instruction: no Dataverse write of any kind was made).

## Rigor

FULL (POML `<rigor>`; `.cs` in Spaarke.Dataverse, tags `bff-api`, `data-migration`; adds a data-changing script). Model tier
sonnet at effort high. Step mode prescriptive. Constraints loaded as for task 004 (see `task-004-notes.md`).

## Files

- `src/server/shared/Spaarke.Dataverse/Models.cs`: `FieldMappingRuleEntity.SyncMode` (int; 0 One-time, 1 Manual Refresh).
- `src/server/shared/Spaarke.Dataverse/FieldMappingColumns.cs`: `sprk_syncmode` added to `RuleSelect`, the single list behind both
  rule reads (by-profile at `GetFieldMappingRulesAsync` and the profile-with-rules `$expand`).
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`: `MapToFieldMappingRuleEntity` reads `sprk_syncmode` (null or absent
  maps to 0); the mapper is now `internal static` (it uses no instance state) so the unit test calls it directly.
- `scripts/Set-FieldMappingRuleSyncMode.ps1` (new) and its `scripts/README.md` entry plus change-log line (script-aware).
- `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`: rule column list and enum table gained `sprk_syncmode`.
- `tests/unit/domain/Dataverse/FieldMapping/FieldMappingRuleSyncModeMappingTests.cs` (new, 5 test cases).

## Decisions and deviations

1. **Test location** (main-session correction): the POML names `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/`, which
   testing.md:132 forbids for a new file. The test is in `tests/unit/domain/Dataverse/FieldMapping/` (KEEP path, `tests/unit/domain/**`),
   namespace `Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping`. `Spaarke.Dataverse` types resolve there through the existing
   project reference and `InternalsVisibleTo`.
2. **The script defaults to dry-run**; `-WhatIf` forces a dry-run even with `-Apply`; `-Verify` is read-only. Pattern copied from
   `Backfill-RecordOwnership.ps1` (param block, az-CLI token, `Invoke-DvGet` paging, read-back after write, `-SelfTest`).
3. **An empty `sprk_syncmode` counts as not migrated.** Measured on spaarkedev1 (below): 20 of 25 Copy rules are empty, not 0.
   A filter of `sprk_syncmode ne 1` would drop empty rows in OData three-valued logic; the script reads every rule and classifies
   client-side instead.
4. **Inactive Copy rules** not at 1 are reported (`InactiveNotMigrated`), never written (Dataverse refuses to update an inactive row;
   reactivating is the rule owner's call), and they fail `-Verify`. Dev has none.
5. **Refuses to guess**: a mapping type outside 0-3 or a sync mode outside {empty, 0, 1} stops the run before any write (exit 2),
   which is the POML's second escalation trigger turned into behaviour. Dev has none.
6. **Nothing in the BFF writes `sprk_syncmode`**: grep of `src/server` shows only the new read; no create path sets it, so new rules default
   to One-time (0). The DTO is unchanged (the new value is on the entity only; task 040 consumes it).
7. Lint: the PostToolUse hook reported `PSAvoidUsingWriteHost` advisories; `Backfill-RecordOwnership.ps1` (the cited pattern) uses
   `Write-Host` throughout for its operator output, so the script follows the precedent.

## Script verification

`-SelfTest` (offline, 17 checks): PASS. Covers every outcome (Migrate, Aligned, InactiveNotMigrated, NotCopy, Unknown), the unknown
sync mode on a non-Copy rule, an in-memory table applied twice (first pass migrates 2, second pass 0; only the active Copy rows
below 1 change; Default/Concat/Template and the inactive rule are byte-for-byte unchanged), and `-Verify` failing then passing.

### Read-only dry run and `-Verify` baseline on spaarkedev1 (2026-10-21)

Invocation: `pwsh scripts/Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify`
(no `-Apply`; the run issued only GETs). Exit code 1 (baseline not met). Output, with the per-rule lines abbreviated to their
pattern:

```
Set-FieldMappingRuleSyncMode on https://spaarkedev1.crm.dynamics.com (DRY RUN - nothing is written)
  Migrate  3113c3d9-...  [sprk_assignedattorney1 -> sprk_assignedattorney1 (Matter-Event)]  current=One-time
  Migrate  8de027b4-...  [sprk_assignedattorney1 -> sprk_assignedattorney1 (Matter-Report Card)]  current=empty
  ... (19 more Migrate lines, all current=empty)
Totals: 25 rule(s) read. ToMigrate: 21 (empty: 20, One-time: 1); Aligned: 4; InactiveNotMigrated: 0; NotCopy (never touched): 0; Unknown: 0
VERIFY: NOT MET (Copy rules short of Manual Refresh: 21 active, 0 inactive)
```

Cross-check with the Dataverse MCP (SQL group-by over sprk_fieldmappingrule): 25 rules, all Copy (0) and Active; sprk_syncmode
empty 20, One-time (0) 1, Manual Refresh (1) 4. The two sources agree.

**Rules that would change, by current mode: 21 (20 empty, 1 One-time). Already Manual Refresh: 4. Default/Concat/Template: 0.**
Profiles involved: Matter-Event (24dc0ed2-...), Matter-Report Card (b2915dad-...), Matter-Invoice (25dc0ed2-...), Matter-Work
Assignment (7b147316-...).

### Live `-Apply`: PENDING (not run)

Needs the owner's explicit OK for the environment (project CLAUDE.md section 5). No `-Apply` was run on dev or anywhere. After the OK,
the run is `... -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -Verify`, expected: 21 migrated, 0 failed, then
`VERIFY: PASS`; a second `-Apply` must report `ToMigrate: 0`. Record the OK and the output here when it is done.

## Tests (fail before, pass after)

`FieldMappingRuleSyncModeMappingTests` (5 cases: 1 maps to 1; 0 maps to 0; null and absent map to 0; `RuleSelect` contains
`sprk_syncmode`). With the mapper reverted to ignore the column and the column removed from `RuleSelect`: 2 failed, 3 passed. With
the change: all pass. The filtered FieldMapping run in Sprk.Bff.Api.Tests: 309 passed, 0 failed (includes tasks 001-004's tests and the
unchanged FieldMapping tests). Office tests were run once, after task 004, and 005 does not touch that code (950 passed, 7 skipped,
0 failed across FieldMapping and Office). Provisioning 2854 passed; ArchTests 1211 passed (see task 004 notes).

## Build, publish, CVE

- `dotnet build Spaarke.sln`: 0 errors, 0 warnings, after both tasks; `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Publish size: **+13,506 B (+0.013 MB) over fresh master `44063b3473`; +307 B over the work-branch tip; 192 files in all three builds.**
  Method and table: `task-004-notes.md` "Publish-size measurement".
- `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api, Sprk.Provisioning.ControlPlane.Core): none. No package added.

## Placement Justification (bff-extensions.md)

Read-path addition in `Spaarke.Dataverse` (the BFF's Dataverse library) and a PowerShell script outside the BFF; no endpoint,
service, DI registration, background work or package. Component justification for the new script (root §11): Existing: no script or
rule read of the rule column existed (grep `sprk_syncmode` in `scripts/` and `src/server`); `Backfill-RecordOwnership.ps1` is the only
dry-run/`-Apply`/`-Verify` precedent and it migrates ownership. Extension: the pattern is copied, the read path extends
`FieldMappingRuleEntity` and the shared column list. Cost of doing nothing: task 040 selects `/push` rules by Manual Refresh; without a
scripted, verifiable migration every existing Copy rule stays One-time and `/push` applies nothing.

## /conflict-check

As recorded in `task-004-notes.md`: soft overlap with #2177 (`Models.cs`, different region, additive) and #2169 (`scripts/README.md`).
ebilling-r1's `UpdateExistingRecordFieldsAsync` overload on `IFieldMappingDataverseService` is untouched; this change adds one entity
property and one constant, so it is additive and cannot conflict with an interface method. Task 013's files untouched.

## Quality gates

code-review and adr-check ran once over both tasks' diffs (see `task-004-notes.md`). No F-class finding in this task's code, so no fix
round. K-limit: lint advisories `PSAvoidUsingWriteHost` (precedent script does the same).

### Reported, not fixed (found in passing; outside this task's scope)

1. **Seeded rules default to One-time.** `DataverseWebApiFieldMappingSeeder.FindOrCreateRuleAsync` POSTs Copy rules without `sprk_syncmode`
   (and none of the BFF writers set it). A newly provisioned customer environment therefore starts with every seeded Copy rule at
   One-time, so task 040's `/push` would select none of them until someone runs `Set-FieldMappingRuleSyncMode.ps1 -Apply` there. Fix
   options for the owner: seed `sprk_syncmode = 1` for the seeded Copy rules (and update seeder test T2), or add the script to the
   provisioning runbook. Not fixed here: 004's scope is the filter and 005 says new rules default to One-time; changing the seeded
   default is a decision. To be filed by the main session via `/project-defer-issue-tracking`.
2. **`src/client/shared/Spaarke.UI.Components/src/services/FieldMappingHandler.ts:175` selects `sprk_syncmode` from
   `sprk_fieldmappingprofiles`**, a table with no such column (Dataverse describe), so that query is a 400 if reached. Pre-existing; the
   client mapping engine is scheduled for deletion in this project, so it should be covered there.
3. The profile model's `SyncMode` and `MappingDirection` are hard-coded 0 (no deployed profile columns); documented in the framework doc.

## Acceptance criteria

1. Every rule read returns `SyncMode` from `sprk_syncmode`, null maps to 0, a unit test proves both: **met**.
2. Default run writes nothing; `-Apply` sets only Copy rules not at 1 to 1; a second `-Apply` reports zero: **met in code and
   `-SelfTest`** (in-memory twice-applied table). The live two-pass proof on dev is **pending the owner's OK**.
3. `-Verify` exits 0 and reports every Copy rule = Manual Refresh after `-Apply`, exits non-zero while any is not 1: **non-zero case
   met live** (exit 1 on dev now); **exit-0 case met in `-SelfTest`**, and live after the owner-approved `-Apply` (pending).
4. Negative: Default/Concat/Template never modified, no deletion, live `-Apply` only on dev with the OK recorded or marked pending:
   **met** (NotCopy outcome never writes; PATCH body is exactly `{ "sprk_syncmode": 1 }` with `If-Match: *`; live apply marked pending).
5. New rules default to One-time and nothing in the BFF writes `sprk_syncmode`; builds 0 errors; existing tests pass; publish delta
   and CVE recorded: **met**.

Escalations: the live `-Apply` needs the owner's explicit OK (by instruction, not requested from the environment). None other.

rework: none task 005
