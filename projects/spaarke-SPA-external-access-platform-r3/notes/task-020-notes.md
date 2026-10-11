# Task 020 — R3 schema: sprk_createdbycontact + intake columns

**Status (2026-10-11):** the repo source and the import script are done. The **dev import ran on 2026-10-11** with owner approval ("you can proceed with the import"). `-Verify` passes. See [Live run](#live-run-dev-2026-10-11).

## What changed in source
- **`sprk_CreatedByContact`** (lookup → contact, field-secured), on:
  - `sprk_document`, `sprk_todo`, `sprk_event`, `sprk_communication`.
  - It sits just before `sprk_CreatedByPerson`.
- **Not on `sprk_servicerequest`.** Service requests use the existing `sprk_requestedby` (`notes/decisions.md`; `spec.md` §New Components corrected 2026-10-10).
- **`sprk_servicerequest`** gains:
  - `sprk_RequestType`: a local choice, **not** field-secured. Values:

    | Value | Label |
    |---|---|
    | 100000000 | NDA Assessment |
    | 100000001 | Invention Submission |
    | 100000002 | Policy Question |
    | 100000003 | Trademark Search |

  - `sprk_IntakeData`: memo, max 1,048,576, format text, **field-secured** (`IsSecured` 1). Owner decision 2026-10-11 (`notes/decisions.md`): it follows the `sprk_createdbyperson` settings. Before that decision it was unsecured (like `sprk_recordsummary`).
- **Relationships:** `sprk_contact_<table>_createdbycontact` (`Other/Relationships.xml`, `Other/Relationships/Contact.xml`).
  - Delete and archive cascade: RemoveLink.
  - Merge cascade: **Cascade**. Dataverse forces it on every relationship to `contact`, which supports merge. A NoCascade request is stored as Cascade; all 81 custom contact relationships in dev have it (read 2026-10-11). The script expects and creates Cascade.
  - Every other cascade: NoCascade.
- **Field security** (`Other/FieldSecurityProfiles.xml`) — five columns in both profiles (the four `sprk_createdbycontact` columns and `sprk_servicerequest.sprk_intakedata`):
  - "BFF-Managed Field Readers": read-only (`CanRead` 4, create and update 0).
  - "BFF-Managed Field Writers": read, create and update (all 4). This is the same split as `sprk_createdbyperson`.
- **Docs:** `docs/data-model/sprk_createdbycontact.md` (new) and `docs/data-model/sprk_servicerequest.md` (the `sprk_intakedata` row now says field-secured).
- **Script:** `scripts/Set-ChildRecordCreatorContactSchema.ps1` (new, 2026-10-11) creates all six columns, the field-security entries and the solution membership, and carries `-Verify` and `-Rollback`.
- **XML checks:** all 8 XML files parse; BOM and CRLF are preserved; the diff is additive (the one non-additive line is `<IsSecured>0</IsSecured>` → `1` on `sprk_IntakeData`).
- **Not changed:** the two profile `description` attributes in `FieldSecurityProfiles.xml` still list only `sprk_createdbycontact` (not `sprk_intakedata`). Editing them would put source out of step with the live profile text until someone updates it there; the next re-export will show it.

## Pre-check already done (read-only, dev, 2026-10-10)
Both BFF application users are enabled members of the Writers profile:
- `# mi-bff-api-dev` (5967251e…);
- `SDAP-BFF-SPE-API` (1e40baad…).

## Consequence of field-securing `sprk_intakedata`
A user who is **not** in "Spaarke BFF-Managed Field Readers" (directly or through a team) will not see the intake answers in the model-driven app: Dataverse hides a secured column from anyone without Read. The record, its other columns and the BFF are unaffected (the BFF reads and writes it through the Writers profile).

How the Readers profile is populated today (from the repo; no per-user grant exists in any script or doc):
- Every `Set-*Schema.ps1` that touches the profile (`Set-ChildRecordCreatorPersonSchema.ps1`, `Set-RecordCreatorPersonSchema.ps1`, `Set-ContactIdentityBindingSchema.ps1`, `Deploy-ExternalRecordAccessContactGrantor.ps1`, and the new contact twin) associates **every business unit's default team** (`teams` where `isdefault eq true`, via `teamprofiles_association`) with the Readers profile. Nothing adds an individual user to Readers.
- Customer provisioning does the same: `.claude/constraints/provisioning.md` (H7b adds every business unit's default team to "Spaarke BFF-Managed Field Readers"); `docs/data-model/sprk_createdbycontact.md`; `projects/unified-access-control-r2/notes/task-133-provision-creator-lockout.md`.
- So who loses the column: any user not covered by a default team in the Readers profile. That is most likely a business unit created after the last `-Apply` (re-run `-Apply` to add its default team), or a user whose business unit's default team never reached the profile.
- The repo does not state that a business unit's default team automatically contains all of that unit's users; that is Dataverse platform behaviour, not shown here. Step 1c lists the Readers teams against the default teams so the owner can see the actual coverage in dev before the change.
- To give one user (for example a support analyst) the column without a team: add them to the Readers profile in the Dataverse admin centre (Settings > Security > Field security profiles), or `POST fieldsecurityprofiles(<readerId>)/systemuserprofiles_association/$ref`. No script in the repo does this; the Writers profile must never get a human (the script and `-Verify` fail on that).

## Live run (dev, 2026-10-11)

Run by the main session against SPAARKE DEV 1 (org `spaarkedev1`) as `ralph.schroeder@spaarke.com`.

| Step | Result |
|---|---|
| 1 Pre-check | All six columns were absent. `sprk_recordsummary` Format is Text, and the relationships expose `Archive`. Writers held the two BFF application users and no team. Readers held all 6 default teams. pac was on SPAARKE DEV 1 |
| 2 Dry run | exit 0 |
| 3 Apply, first run | Exit 1. It created all six columns, secured `sprk_intakedata` and granted it to both profiles, and published the five tables. It reported DRIFT on the four new contact relationships (`Merge=Cascade`, expected NoCascade), so it did **not** secure the four `sprk_createdbycontact` lookups. The cause was a wrong expectation: Dataverse forces Merge=Cascade on contact relationships (81/81 in dev). The script's expected and created Merge was changed to Cascade |
| 3 Apply, second run | exit 0. It secured the four lookups and granted each to Readers (read) and Writers (read/create/update). Everything else was already OK |
| 4 Verify | `VERIFY PASS`, exit 0 |
| 5 Re-export and diff | Exported to `C:\tmp\smx-r3-020` and compared by content. Every task-020 item matches the source: the four `sprk_CreatedByContact` lookups (secured), `sprk_RequestType` (four values, unsecured), `sprk_IntakeData` (secured, Text, 1,048,576), the ten field permissions and `Contact.xml` (no difference). The source was **not** overwritten (5d not run). Other differences are other work in dev or formatting: extra `sprk_project`/`sprk_workassignment` permissions and the profile description text |
| 6 Test rows | A service request accepted `sprk_requesttype` 100000000 and `sprk_intakedata` and read them back. A todo accepted `sprk_CreatedByContact` and read it back. Both rows were deleted; 0 `TEST task020` rows are left |

The four lookups existed without field security for the few minutes between the two apply runs. Nothing writes or reads them yet: the R3 BFF is not deployed.

The deploy-order rule in [Open](#open) is now satisfied in dev.

## Live steps (as run; kept for other environments)

Everything runs from the worktree root in **PowerShell 7**. The script is invoked with `&` and an id array, never `pwsh -File` (which passes the comma list as one string; the script also splits it). Auth is your own `az login` identity (System Administrator in dev). The access token is never printed. Times are estimates, not measurements.

Set up once per terminal:

```powershell
Set-Location C:\code_files\spaarke-wt-SPA-external-access-platform-r3
$dv  = 'https://spaarkedev1.crm.dynamics.com'
$bff = '5967251e-171c-46fe-a6c2-ef843c90309d','1e40baad-e065-4aea-a8d4-4b7ab273458c'
az account show --query "{user:user.name, tenant:tenantId}" -o json      # confirm who you are; does not print a token
```

### 1. Read-only pre-check
**Changes:** nothing. **Time:** about 2 minutes. **Undo:** none needed.

```powershell
$token = az account get-access-token --resource $dv --query accessToken -o tsv      # held in a variable; never echo it
$h = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
function Get-Dv([string]$p) { Invoke-RestMethod -Uri "$dv/api/data/v9.2/$p" -Headers $h }

# 1a. None of the six columns exists yet (expect 0 on every line).
foreach ($x in 'sprk_document:sprk_createdbycontact','sprk_todo:sprk_createdbycontact','sprk_event:sprk_createdbycontact','sprk_communication:sprk_createdbycontact','sprk_servicerequest:sprk_requesttype','sprk_servicerequest:sprk_intakedata') {
    $t, $c = $x -split ':'
    '{0}.{1} -> {2}' -f $t, $c, @((Get-Dv "EntityDefinitions(LogicalName='$t')/Attributes?`$select=LogicalName&`$filter=LogicalName eq '$c'").value).Count
}

# 1b. Two facts the script depends on: the existing memo's Format (expect Text) and whether relationships expose an Archive cascade.
Get-Dv "EntityDefinitions(LogicalName='sprk_servicerequest')/Attributes(LogicalName='sprk_recordsummary')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=Format,MaxLength,IsSecured" | Select-Object Format, MaxLength, IsSecured
(Get-Dv "RelationshipDefinitions(SchemaName='sprk_systemuser_sprk_document_createdbyperson')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=CascadeConfiguration").CascadeConfiguration | Format-List

# 1c. Profile membership: Writers = the two BFF application users and no team; Readers = which teams?
$w = (Get-Dv "fieldsecurityprofiles?`$select=fieldsecurityprofileid&`$filter=name eq 'Spaarke BFF-Managed Field Writers'").value[0]
$r = (Get-Dv "fieldsecurityprofiles?`$select=fieldsecurityprofileid&`$filter=name eq 'Spaarke BFF-Managed Field Readers'").value[0]
'-- Writers: users';  (Get-Dv "fieldsecurityprofiles($($w.fieldsecurityprofileid))/systemuserprofiles_association?`$select=fullname,applicationid,isdisabled").value | Format-Table fullname, applicationid, isdisabled
'-- Writers: teams (expect none)'; (Get-Dv "fieldsecurityprofiles($($w.fieldsecurityprofileid))/teamprofiles_association?`$select=name").value | Format-Table name
'-- Readers: teams';  (Get-Dv "fieldsecurityprofiles($($r.fieldsecurityprofileid))/teamprofiles_association?`$select=teamid,name").value | Format-Table name
'-- Default teams (every one should be in the Readers list above)'; (Get-Dv 'teams?$select=teamid,name&$filter=isdefault eq true').value | Format-Table name

# 1d. pac is authenticated to dev for step 5 (local only).
pac auth list
```

Expect: 1a all `0`; 1b `Format` = `Text` and `Archive` present in `CascadeConfiguration`; 1c Writers hold the two BFF users and no team, and every default team is in Readers; 1d lists a profile for `spaarkedev1`.
- If 1a is not all `0`, stop: a column exists; step 2 will say whether it matches or drifted.
- If 1b `Format` is not `Text`, stop and tell the owner: the script's memo check would call a matching column drift.
- If 1b has no `Archive`, the script skips that cascade and says so (INFO); step 5 then shows `CascadeArchive`.
- If a default team is missing from Readers, step 3 adds it (that is the intended fix), but that team's users would currently not read the other secured columns either.

### 2. Dry run
**Changes:** nothing (reads only; no `-Apply`). **Time:** 1 to 3 minutes. **Undo:** none needed.

```powershell
& .\scripts\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl $dv -BffApplicationIds $bff
$LASTEXITCODE
```

Expect: `Mode : DRY RUN (no writes)`, then `WOULD create` for the six columns (the four lookups name their relationship), `WOULD secure` and `WOULD grant` for the five secured columns, no `FAIL`, final line "DRY RUN complete — nothing was written", exit `0`. Any `FAIL` (drift, an unexpected Writers member, solution not found) exits `1`: stop and read it. The script changes nothing for a drifted column even in `-Apply`.

### 3. Apply
**Changes** (dev Dataverse, solution `SpaarkeCore`):
- 4 one-to-many relationships `sprk_contact_<table>_createdbycontact` and their lookup columns;
- `sprk_servicerequest.sprk_requesttype` (local choice) and `.sprk_intakedata` (memo);
- members added to the two profiles if missing (BFF users to Writers, default teams to Readers);
- `IsSecured` set on the 5 secured columns; 10 field-permission rows (Readers read-only, Writers read/create/update);
- solution components added where the table is not already in `SpaarkeCore`;
- a publish of the five tables.

**Time:** about 10 to 25 minutes (metadata propagation waits; each relationship is polled for up to 2 minutes). **Undo:** step 7. A failure part-way is safe to re-run: every create is get-before-create.

```powershell
& .\scripts\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl $dv -BffApplicationIds $bff -Apply
$LASTEXITCODE
```

Expect `DONE` lines, no `FAIL`, "Published sprk_document, sprk_todo, sprk_event, sprk_communication, sprk_servicerequest.", exit `0`. This is the moment internal users outside the Readers profile lose sight of `sprk_intakedata` (see the consequence above), although no row holds data yet.

### 4. Verify
**Changes:** nothing (read-only; cannot be combined with `-Apply`). **Time:** 1 to 3 minutes. **Undo:** none needed.

```powershell
& .\scripts\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl $dv -BffApplicationIds $bff -Verify
$LASTEXITCODE
```

Expect `VERIFY PASS: the six columns are in place; sprk_createdbycontact and sprk_intakedata are secured and only the BFF can write them.` and exit `0`. Exit `1` lists each gap. If it fails, fix with a second `-Apply` (idempotent) or roll back (step 7); do not continue to step 5.

### 5. Re-export and diff (repo convention: create in dev by script, then re-export)
`Export-SpaarkeMasterSource.ps1` exports `SpaarkeMaster` (unmanaged and managed) from dev and unpacks it over `-OutputFolder`, **replacing** the source tree. So export to a scratch folder first and diff, and only overwrite the tracked tree once the diff is understood. Keep any scratch folder path short: the script refuses an output folder longer than 140 characters.

```powershell
# 5a. Print the commands; exports nothing, writes nothing.
& .\scripts\solution-authoring\Export-SpaarkeMasterSource.ps1 -WhatIf

# 5b. Real export, into a scratch folder (reads dev; writes only under C:\tmp and %TEMP%).
& .\scripts\solution-authoring\Export-SpaarkeMasterSource.ps1 -OutputFolder C:\tmp\smx-r3-020

# 5c. Diff the export against the hand-written source (line endings ignored).
git diff --no-index --ignore-cr-at-eol --stat src\dataverse\solutions\SpaarkeMaster C:\tmp\smx-r3-020
git diff --no-index --ignore-cr-at-eol -- src\dataverse\solutions\SpaarkeMaster\Entities\sprk_ServiceRequest\Entity.xml C:\tmp\smx-r3-020\Entities\sprk_ServiceRequest\Entity.xml
git diff --no-index --ignore-cr-at-eol -- src\dataverse\solutions\SpaarkeMaster\Other\FieldSecurityProfiles.xml C:\tmp\smx-r3-020\Other\FieldSecurityProfiles.xml
git diff --no-index --ignore-cr-at-eol -- src\dataverse\solutions\SpaarkeMaster\Other\Relationships\Contact.xml C:\tmp\smx-r3-020\Other\Relationships\Contact.xml
```

- **Changes:** 5a nothing. 5b/5c only local scratch files; dev is read. **Time:** 5b 3 to 10 minutes (two exports and an unpack); 5a and 5c seconds. **Undo:** `Remove-Item -Recurse -Force C:\tmp\smx-r3-020` and the `spaarkemaster-export-*` folder the script names under `%TEMP%`.
- **What must match** (the hand-written XML is right when these agree): `sprk_IntakeData` `IsSecured` 1, `Format` `text`, `MaxLength` 1048576; `sprk_RequestType` option set `sprk_servicerequest_sprk_requesttype` with the four values; five `sprk_createdbycontact`/`sprk_intakedata` `FieldPermission` entries in each profile; the four relationships with `CascadeDelete` and `CascadeArchive` RemoveLink.
- The export is the whole dev solution, so unrelated hunks from other work in dev can appear. Keep only the task-020 hunks; do not adopt the rest in this task.
- **5d. Keeping the export:** only if the diff shows nothing but harmless formatting differences, or the owner wants the exported form. Stage the hand-written tree first so it stays recoverable, then export over it:

```powershell
git add -A src\dataverse\solutions\SpaarkeMaster          # staging only; owner commits
& .\scripts\solution-authoring\Export-SpaarkeMasterSource.ps1
git diff -- src\dataverse\solutions\SpaarkeMaster         # export vs the staged hand-written version
git restore -- src\dataverse\solutions\SpaarkeMaster      # UNDO: back to the staged hand-written version
git clean -fd -- src\dataverse\solutions\SpaarkeMaster   # UNDO, second half: removes files the export ADDED (restore does not touch untracked files)
```

Fallback (old step 3): `pac solution pack` plus `pac solution import` of the whole solution. Use it only after 5c shows no drift; it is not part of this sequence.

### 6. Owner-approved test row create and delete
**Changes:** creates, then deletes, one `sprk_servicerequest` row and one `sprk_todo` row in dev. **Time:** under 2 minutes. **Undo:** the delete is part of the step; if a delete is skipped, delete the row by id (below).

The caller is **you** (System Administrator, whose "System Administrator" field-security profile is expected to cover secured columns; the script treats that profile as an allowed writer). It proves the columns accept the values and read back; it is **not** the BFF identity. The BFF identity cannot be impersonated from here without its credential, so the as-BFF proof is the first BFF-written row in tasks 030 to 034. If your create is refused on a secured column, that is a finding to report, not a reason to change the profile by hand.

```powershell
$h2 = $h.Clone(); $h2['Content-Type'] = 'application/json; charset=utf-8'; $h2['Prefer'] = 'return=representation'
$ctr = (Get-Dv "contacts?`$select=contactid,fullname&`$top=1").value[0]            # any existing contact; read only

# 6a. Service request: request type + intake data.
$sr = (Get-Dv "EntityDefinitions(LogicalName='sprk_servicerequest')?`$select=EntitySetName").EntitySetName
$srRow = Invoke-RestMethod -Method Post -Uri "$dv/api/data/v9.2/$sr" -Headers $h2 -Body (@{ sprk_name = 'TEST task020 - delete me'; sprk_requesttype = 100000000; sprk_intakedata = '{"test":true}' } | ConvertTo-Json)
$srRow | Select-Object sprk_servicerequestid, sprk_requesttype, sprk_intakedata
Invoke-RestMethod -Method Delete -Uri "$dv/api/data/v9.2/$sr($($srRow.sprk_servicerequestid))" -Headers $h        # delete the test row

# 6b. A child with sprk_createdbycontact set (sprk_todo; primary-name column read from metadata, not assumed).
$td = Get-Dv "EntityDefinitions(LogicalName='sprk_todo')?`$select=EntitySetName,PrimaryNameAttribute,PrimaryIdAttribute"
$tdRow = Invoke-RestMethod -Method Post -Uri "$dv/api/data/v9.2/$($td.EntitySetName)" -Headers $h2 -Body (@{ $td.PrimaryNameAttribute = 'TEST task020 - delete me'; 'sprk_CreatedByContact@odata.bind' = "/contacts($($ctr.contactid))" } | ConvertTo-Json)
$tdRow._sprk_createdbycontact_value -eq $ctr.contactid                                                              # expect True
Invoke-RestMethod -Method Delete -Uri "$dv/api/data/v9.2/$($td.EntitySetName)($($tdRow.($td.PrimaryIdAttribute)))" -Headers $h   # delete the test row
```

If 6b is refused for a required column other than the ones set, stop and report the column; do not guess values. Then confirm no `TEST task020` row is left: `(Get-Dv "$sr?`$select=sprk_name&`$filter=startswith(sprk_name,'TEST task020')").value`.

Optional UI check of the consequence: open a service request as an internal user who is not covered by the Readers profile; the Intake Data field is hidden or blank. Not required for the schema acceptance.

### 7. Rollback
**Changes:** deletes the six columns from dev. The data in them is lost. Dataverse is expected to drop the four relationships, the local option set and the field-permission rows with the columns (the earlier notes' assumption, not yet seen live); the script also removes any relationship or field-permission row that survives, then publishes. The two profiles (shared with tasks 133/146), their members and the XML in git are **not** touched. Nothing reads these columns until tasks 030, 032 and 034 ship. **Time:** about 3 to 10 minutes. **Undo of the rollback:** run steps 3 and 4 again (data is not restored).

```powershell
# 7a. Read-only listing: what exists, how many rows hold a value. Deletes NOTHING. The header line prints
#     "Environment : <url> (org '<name>')" - copy that organisation name.
& .\scripts\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl $dv -Rollback
$org = '<the organisation name printed in 7a>'

# 7b. Delete. -ConfirmRollback takes the organisation name (exactly as printed, case-sensitive); anything else - a typo,
#     another environment's name, or a bare -Confirm (which PowerShell reads as an abbreviation of -ConfirmRollback and
#     which then needs a value) - refuses and deletes nothing. It also refuses any column that holds data (or whose row
#     count cannot be read) unless -AcceptDataLoss is also given.
& .\scripts\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl $dv -Rollback -ConfirmRollback $org
# 7c. Only if 7b refused for data you are willing to lose:
& .\scripts\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl $dv -Rollback -ConfirmRollback $org -AcceptDataLoss
```

What the rollback will and will not do (all of it is visible in the 7a/7b output):
- **Only what the script created.** A column with the same name but a different type, shape or target (drift) is reported `FAIL refusing to delete ...` and left alone: no column delete, no relationship delete, no field-permission delete. The run exits 1 when anything is refused, so check the exit code (`$LASTEXITCODE`) as well as the screen.
- **"Row count could not be read".** Any column can show this instead of a number when the count query itself fails (a timeout, a throttle, or a filter the server rejects for that column type). The script treats an unreadable count as "may hold data": it refuses without `-AcceptDataLoss`. That is the safe direction - it can block a rollback of an empty column, never delete a full one by mistake. Re-run 7a to retry the count; use `-AcceptDataLoss` only when you accept losing whatever is there.
- **A refused lookup delete.** If Dataverse refuses to delete a lookup column, the script deletes its relationship by id (which drops the lookup), re-reads the column and says what happened; it is a `FAIL` only if the column is still there.
- **Stale reads.** A relationship or field-permission row that a 404 says is already gone is reported `OK ... already gone`; the rest of the rollback and the publish still run.

Then revert the source with git (the XML files from this task: `Entities/sprk_Document`, `sprk_Todo`, `sprk_Event`, `sprk_Communication`, `sprk_ServiceRequest/Entity.xml`, `Other/FieldSecurityProfiles.xml`, `Other/Relationships.xml`, `Other/Relationships/Contact.xml`) so source and dev agree again.

Manual equivalent if the script cannot run (each is the same call the script makes; run per column, `$h` as in step 1):

```powershell
foreach ($x in 'sprk_document:sprk_createdbycontact','sprk_todo:sprk_createdbycontact','sprk_event:sprk_createdbycontact','sprk_communication:sprk_createdbycontact','sprk_servicerequest:sprk_requesttype','sprk_servicerequest:sprk_intakedata') {
    $t, $c = $x -split ':'
    Invoke-RestMethod -Method Delete -Uri "$dv/api/data/v9.2/EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$c')" -Headers $h
}
# a relationship that survives: Invoke-RestMethod -Method Delete -Uri "$dv/api/data/v9.2/RelationshipDefinitions(SchemaName='sprk_contact_<table>_createdbycontact')" -Headers $h
```

## Found while doing the task (not fixed here)
- `scripts/Deploy-ExternalRecordAccessContactGrantor.ps1:314-315` (`Ensure-Profile`) has older profile descriptions. Re-running it would overwrite the updated text. It is cosmetic.
- `FieldSecurityProfiles.xml` lists both `sprk_grantedbycontact` and `sprk_grantedbycontactid` for `sprk_externalrecordaccess`. This is name drift and outside this task's scope.
- Differences between the notes' earlier plan and `Set-ChildRecordCreatorPersonSchema.ps1` (the template), and how the twin handles them:
  - The notes said the twin covers only the lookups and the service-request columns "go through the Web API pattern". The twin now creates all six columns.
  - The person script creates no Archive cascade. The source XML and the notes say `CascadeArchive` is RemoveLink, so the twin probes an existing relationship for an `Archive` cascade property and sets and checks it only when the environment exposes it (otherwise it prints INFO).
  - The person script reports drift (target mismatch) as FAIL but still secures the column. The twin changes nothing for a drifted column (no securing, no grant), as the task asked.
  - The person script's `Try-DvGet` treats every error (including 401 and 5xx) as "absent". The twin returns `$null` only for a 404 and throws otherwise, so an expired token cannot read as "column missing". A Dataverse answer other than 404 for a missing item would stop the script (safe); say so if it happens.
  - The person script's informational "rows with no person recorded" block is omitted: there is nothing to backfill for a new column.
  - The person script publishes the child tables only; the twin publishes the five tables (the four children and `sprk_servicerequest`), not `contact`.
  - `-SolutionUniqueName` defaults to `SpaarkeCore` as in the template. Re-export reads `SpaarkeMaster`, which includes the five tables as root components with `behavior="0"` (include subcomponents), so the new columns are exported either way. Whether `sprk_servicerequest` is in `SpaarkeCore` is not shown in the repo; step 2's section (d) will say, and step 3 adds what is missing.
  - The memo `Format` is created as `Text` to match the XML and `sprk_recordsummary`; step 1b confirms `sprk_recordsummary` is `Text` before anything is created.

## Open
- ~~Owner approval of the columns, the field-security posture and the rollback (acceptance criterion 6).~~ Approved 2026-10-11: the field-security posture ("follow sprk_createdbyperson settings") and the import ("you can proceed with the import").
- **DEPLOY ORDER (satisfied in dev 2026-10-11; applies to every other environment):** task 030's code in this branch already writes `sprk_CreatedByContact@odata.bind` on external creates (`ExternalDataService.cs:~528,~570`), so a BFF built from this branch must **not** be deployed to dev before step 3 (`-Apply`) has run and `-Verify` exits 0; until then those creates fail.
- Live criteria 1–3 wait for the import.
