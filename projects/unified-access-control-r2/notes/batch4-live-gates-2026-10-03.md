# Batch 4: live gates on dev (2026-10-03)

**Authority:** owner round 11 approved every dev live step needed to integrate batch 4 (`notes/session27-owner-decisions-and-research.md`). Each step runs as a dry run, then apply, then verify.

**Operator:** az CLI `ralph.schroeder@spaarke.com`.

**Target:** `https://spaarkedev1.crm.dynamics.com`.

Steps that only ADD schema are run ahead of the integrated deploy. They do not change the behaviour of the BFF already deployed (`818840ac6`), which reads none of them. Steps that change current behaviour wait for the deploy that needs them. Example: the FLS lock on `sprk_issecure` waits, because today's wizard still writes that flag from the client.

## 133 schema: `sprk_createdbyperson`. APPLIED; one verify defect found

Script `scripts/Set-RecordCreatorPersonSchema.ps1` from a read-only worktree at the verified `task/uac-r2-133-b2-r2` (`C:\wtg133`). Run it inside PowerShell: `pwsh -File` passes the comma list as one string, and the dry run then fails on `applicationid eq a,b`.

- **Dry run:** 3 lookup columns, 2 field-security profiles, writers = the two BFF app users, readers = the 6 BU default teams, FLS on the 3 columns. As expected.
- **`-Apply`:** created `sprk_project`, `sprk_matter` and `sprk_workassignment` `.sprk_createdbyperson` (each a systemuser lookup, no Assign or Share cascade). Profiles "Spaarke BFF-Managed Field Readers" (`42e542c8…`) and "… Writers" (`917b64c5…`) are created, with members as above. The 3 columns are secured and both profiles granted, after the field-permission service caught up (retries). Published.
- **Informational:** 2 app-created `sprk_matter` rows have no person recorded. A resume of either refuses (guide §7a recovery).
- **`-Verify`:** everything OK EXCEPT 6 "MISSING in SpaarkeCore" lines (the 3 columns and 3 relationships). **This is a false negative.** The three tables are in SpaarkeCore with `rootcomponentbehavior = 0` ("include subcomponents"), so their new columns and relationships are included implicitly and have no rows of their own; `AddSolutionComponent` is then a no-op. `contact` is included with behaviour 2, which is why task 141's identical check passed.
  - **Fix (at integration):** in the verify step, treat a component as in the solution when its parent table is in the solution with `rootcomponentbehavior = 0`. Then re-run `-Verify`, which must PASS.

## 143 G-1: `sprk_noaccessentry.sprk_subjectsystemuser`. APPLIED

Script `scripts/Set-NoAccessSystemUserSubjectSchema.ps1` from the verified `task/uac-r2-143-r2` (`C:\wtg143`).

- **Dry run:** 1 lookup column (to systemuser), relationship `sprk_systemuser_sprk_noaccessentry_subjectsystemuser`.
- **`-Apply`:** the column is created, added to SpaarkeCore (a no-op, see below) and published. Its "FAIL … relationship was not found" line, printed right after the create, was metadata propagation: the verify a minute later reads the relationship with the right cascade. **A data query selecting `_sprk_subjectsystemuser_value` answers**, so the 143 BFF's RowSelect will not 400. That is the precondition G-1 protects.
- **`-Verify`:** OK for the column, its cascade and the data query. "MISSING in SpaarkeCore" appears for the column and its relationship: the SAME false negative as 133, because `sprk_noaccessentry` is in SpaarkeCore with `rootcomponentbehavior = 0`.

**Cross-script defect:** the schema scripts' solution-membership verify ignores `rootcomponentbehavior = 0`. Fix every schema script that does this check, at integration and in one change, then re-run each `-Verify`.

**O2 (only an access-administrator role reads `sprk_noaccessentry`) is NOT applied yet.** It changes current behaviour, so it runs with 143's deploy.

## 150 G-0: NULL `sprk_issecure` repaired. PASS

`scripts/Repair-SecureFlagNulls.ps1` from the verified `task/uac-r2-150-r2` (`C:\wtg150`).

- **Dry run:** 42 NULL rows (project 9, matter 18, work assignment 11, and invoice 4, discovered beyond the three roots). The default is already No on every table.
- **`-Apply`:** all 42 set to No (PATCH, `If-Match: *`).
- **`-Verify`:** **PASS**: no NULL row on any of the 4 tables, and the default is No.
- **Script defect:** apply's immediate after-check printed "1 row(s) still hold NULL" per table, but the read-only verify seconds later found 0. The after-check looks like a counting slip. Fix it at integration.
- Reports: `C:\wtg150\secure-flag-null-backfill-20261003-*.json` (dry run, apply, verify).
- **The FLS lock on `sprk_issecure` (G-1/G-3/G-4/G-8) is NOT applied yet.** The deployed wizard still writes the flag from the client, so the lock goes on with the 150 client and BFF deploy.

## 146 G146-1: Secure Record Owner role, 9 → 26 tables. PASS

Script `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` and the config `config/secure-record-owner-role.json` (26 tables), from the verified `task/uac-r2-146-b2-r2` (`C:\wtg146`).

1. **Negative control (task 145's procedure):** one probe create per new table, owned by "Secure Record Owners" (`6eabc7f9-13be-f111-a05b-0022482913fc`). **All 17 refused**, and nothing was created. The verbatim refusal for each table goes into its config `evidence` at integration, replacing "VERBATIM REFUSAL PENDING":

   - `sprk_analysis`: 2026-10-03T16:41:21Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_analysis privilege (Id=2a83258f-91c9-4566-b3ee-83cfcad4feb2) on OTC=10795 for entity 'sprk_analysis' (LocalizedName='Analysis'). context.Caller=1d02`
   - `sprk_analysisoutput`: 2026-10-03T16:41:22Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_analysisoutput privilege (Id=17d29b02-8cb9-4e77-9c90-b0fa77b88229) on OTC=10632 for entity 'sprk_analysisoutput' (LocalizedName='Analysis Output'). `
   - `sprk_communicationthread`: 2026-10-03T16:41:23Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_CommunicationThread privilege (Id=eabda19b-74a6-4e9f-a690-fa9b6e551b92) on OTC=10907 for entity 'sprk_communicationthread' (LocalizedName='Communica`
   - `sprk_communicationattachment`: 2026-10-03T16:41:23Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_CommunicationAttachment privilege (Id=1f0f2efc-5dab-434b-889d-e34c84593165) on OTC=10884 for entity 'sprk_communicationattachment' (LocalizedName='C`
   - `sprk_communicationparticipant`: 2026-10-03T16:41:24Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_CommunicationParticipant privilege (Id=028fdee0-97d0-40e6-82b4-5d8cb88674f1) on OTC=10964 for entity 'sprk_communicationparticipant' (LocalizedName=`
   - `sprk_emailreviewlog`: 2026-10-03T16:41:25Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_EmailReviewLog privilege (Id=9a9066a5-650c-49f4-a5c6-311ab1882635) on OTC=10971 for entity 'sprk_emailreviewlog' (LocalizedName='Email Review Log').`
   - `sprk_spendsignal`: 2026-10-03T16:41:25Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_SpendSignal privilege (Id=7451f64e-2b5f-4c13-a037-47b8fdb02b35) on OTC=10828 for entity 'sprk_spendsignal' (LocalizedName='Spend Signal'). context.C`
   - `sprk_spendsnapshot`: 2026-10-03T16:41:26Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_SpendSnapshot privilege (Id=6541a556-58e2-40bc-b458-9326824f389d) on OTC=10837 for entity 'sprk_spendsnapshot' (LocalizedName='Spend Snapshot'). con`
   - `sprk_fileversion`: 2026-10-03T16:41:27Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_FileVersion privilege (Id=74e01ad3-ed5a-441b-9cfa-74a381631f84) on OTC=10794 for entity 'sprk_fileversion' (LocalizedName='File Version'). context.C`
   - `sprk_emailartifact`: 2026-10-03T16:41:27Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_EmailArtifact privilege (Id=a3a6a921-b859-442f-847c-e40eb1ec72a7) on OTC=10520 for entity 'sprk_emailartifact' (LocalizedName='Email Artifact'). con`
   - `sprk_attachmentartifact`: 2026-10-03T16:41:28Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_AttachmentArtifact privilege (Id=a08f56fd-e0cf-463d-93f1-1413223abb7d) on OTC=10638 for entity 'sprk_attachmentartifact' (LocalizedName='Attachment `
   - `sprk_eventlog`: 2026-10-03T16:41:29Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_EventLog privilege (Id=5298941e-8a95-4173-8560-94734e056443) on OTC=10706 for entity 'sprk_eventlog' (LocalizedName='Event Log'). context.Caller=1d0`
   - `sprk_agreement`: 2026-10-03T16:41:29Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_Agreement privilege (Id=f8b403be-6fc8-4bc9-b506-be52d01eace2) on OTC=10702 for entity 'sprk_agreement' (LocalizedName='Agreement'). context.Caller=1`
   - `sprk_billingevent`: 2026-10-03T16:41:30Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_BillingEvent privilege (Id=cde3e1b3-7411-47f0-9f36-316611e482cd) on OTC=10833 for entity 'sprk_billingevent' (LocalizedName='Billing Event'). contex`
   - `sprk_budget`: 2026-10-03T16:41:30Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_Budget privilege (Id=8cf76905-35f2-4441-b3a2-1e3312ad9b6b) on OTC=10704 for entity 'sprk_budget' (LocalizedName='Budget'). context.Caller=1d02f31c-1`
   - `sprk_kpiassessment`: 2026-10-03T16:41:31Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_KPIAssessment privilege (Id=e8380160-7464-4096-b35d-06cee2341356) on OTC=10681 for entity 'sprk_kpiassessment' (LocalizedName='KPI Assessment'). con`
   - `sprk_reportcard`: 2026-10-03T16:41:32Z, HTTP 403, `0x80040299 Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=9, MetadataCachePrivilegesCount=7304, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_ReportCard privilege (Id=2a9a818c-f493-49b8-a459-3ecc3bab5f8c) on OTC=10705 for entity 'sprk_reportcard' (LocalizedName='Report Card'). context.Call`

2. **Dry run:** would add 17 at Basic. **`-Apply`:** added and read back, giving 30 privileges, because the platform injected the SharePoint four at Global (guide §5.4).
3. **§5.4 strip:** `RemovePrivilegeRole` × 4 (`prvReadSharePointData`, `prvWriteSharePointData`, `prvCreateSharePointData`, `prvReadSharePointDocument`).
4. **`-Verify`:** **PASS**: 26 privileges, Read at Basic on all 26 tables, nothing outside the file.
5. **Positive probes, 3 polls about 25 s apart:** poll 1 2026-10-03T16:45:00Z: 17/17 created, owned by the team, deleted (204) · poll 2 2026-10-03T16:45:45Z: 17/17 created, owned by the team, deleted (204) · poll 3 2026-10-03T16:46:29Z: 17/17 created, owned by the team, deleted (204). On every poll, the control table `sprk_gridconfiguration` (not in the file) is still **refused**, with `privilegeCount=26`.

## 146 G146-2: BU default teams can read review logs. DONE

`AddPrivilegesRole` on the ROOT "Spaarke Basic User" (`11f93c04…`, BU Spaarke): `prvReadsprk_EmailReviewLog` (`9a9066a5-650c-49f4-a5c6-311ab1882635`) at **Basic** (HTTP 204). The BU copy "Spaarke Business Unit 1" (`dc44312f…`, `parentrootroleid` = the root role) inherits it. Read back on both copies: Basic.

