#!/usr/bin/env pwsh
<#
.SYNOPSIS
    eBilling R1 (task 097 second review F2; owner decision 2026-10-30) - field-secures sprk_invoicesubmission.sprk_resubmissionof
    so ONLY the server may set it: the BFF application user(s) through "Spaarke BFF-Managed Field Writers" (read, create,
    update), and every other user keeps READING it through "Spaarke BFF-Managed Field Readers" (read only). -WhatIf is a dry
    run; a plain run writes; -Verify checks; -Revert undoes a run (with -WhatIf: a dry run of it).

.DESCRIPTION
    WHY (097 second review F2). sprk_resubmissionof names the earlier submission a Correction replaces. The BFF derives the
    target on the server (the intake's CheckCorrectionAsync) and writes it with the submission's create; the commit, the
    after-steps and the Signals close later trust the stored value. Unsecured, any user with Write on a submission can point it
    at another submission with the platform's own form or Web API, past the intake's checks. Secured, a write by anyone but the
    BFF application user is refused natively. The column is shown on the submission and read by the 097b-d code, so it stays
    READABLE: unlike sprk_findingsjson (task 122), the Readers profile IS granted.

    PROFILES (REUSED, never created or re-membered here):
      "Spaarke BFF-Managed Field Writers" (unified-access-control-r2 task 133; members: the BFF application user(s) ONLY). It
          gets read=4 create=4 update=4 on the column.
      "Spaarke BFF-Managed Field Readers" (same task) is on EVERY business unit's default team, so every user keeps reading
          the column. It gets read=4 create=0 update=0 on the column. This script never adds or removes a member of it: when
          a default team is missing, (p8) FAILs and nothing is written (a team change is the owner's).
      Any other profile that can create or update the column is FAIL in every mode (never removed here), except the platform's
      own "System Administrator" profile, matched by its fixed id.

    PRECONDITIONS (every mode; a write run refuses unless all pass):
      (p0) the column exists and can be secured for read, create and update;
      (p1) the writer profile exists, exactly once, and is in -SolutionUniqueName;
      (p2) its members are exactly the -BffApplicationIds application users: each is a member, and there is no other user and no
           team (a team would let every member write the column);
      (p3) no business unit's default team holds the writer profile;
      (p4) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets the column (maker configuration
           writing outside the BFF, refused once secured);
      (p5) the column is in -SolutionUniqueName (its own row, or its table with rootcomponentbehavior 0), so the secured state
           travels with the solution;
      (p6) informational: how many submissions name an earlier submission now (securing a column does not change what was
           written), the forms that bind the column (still shown, read-only to users outside the writer profile), and whether
           each BFF application user holds System Administrator (full field access by platform rule; then there is no masked
           window for the BFF between (a) and its grant);
      (p7) the reader profile exists, exactly once, and is in -SolutionUniqueName;
      (p8) the reader profile is on EVERY business unit's default team.

    STEPS (a plain run), in this order:
      (a) secure the column, then IMMEDIATELY grant the writer profile read=4 create=4 update=4 and the reader profile read=4
          create=0 update=0 (12 retries each on 0x8004f508). Until the grants land a user (and a BFF application user without
          System Administrator) reads the column as empty. If a grant fails, the column is REVERTED to not secured and
          published before the script stops; if the revert fails too it prints "RECOVERY REQUIRED NOW". Re-running is the
          resume path: on a column already secured it grants only what is missing;
      (b) the permissions are exactly: writer read=4 create=4 update=4; reader read=4 create=0 update=0;
      (c) any OTHER profile that can create or update the column is FAIL. The one exception is the platform's own
          "System Administrator" field security profile, matched by its fixed id (572329c1-a042-4e22-be47-367c6374ea45; Dataverse
          gives it a row on every secured column), never by name. Other profiles that only read it are listed (INFO). System
          Administrator role holders are listed;
      (d) publish sprk_invoicesubmission - only when this run wrote something.

    A second run reports every item OK and writes nothing (no publish either).

    -Confirm: every write point (secure + grants, a resume grant, the publish, and each -Revert write) goes through
    ShouldProcess, so -Confirm asks before each. The automatic revert after a failed grant is a recovery and is never asked.

    -VERIFY passes only when the column IS secured, the writer profile holds read=4 create=4 update=4, the reader profile holds
    read=4 create=0 update=0, and NO other profile can create or update it (the System Administrator profile above excepted).

    -REVERT (rollback after a successful run), in this order: (r1) un-secure the column (so no user is ever left masked), (r2)
    delete the writer and reader profiles' permission rows on it if any are left, (r3) publish sprk_invoicesubmission. The
    preconditions are not required. With -WhatIf it only reports what it would do. The SpaarkeMaster source (Entity.xml
    IsSecured, the FieldSecurityProfiles.xml rows) is reverted by a commit, not by this script.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users, the ONLY members of the writer profile (dev:
    5967251e-171c-46fe-a6c2-ef843c90309d and, while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c).
    Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution the profiles and sprk_invoicesubmission travel in. Default SpaarkeMaster.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -WhatIf or -Revert.

.PARAMETER Revert
    Undo a run: un-secure the column, remove the writer and reader profiles' rows on it, publish. Combine with -WhatIf for a dry run.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
    pwsh scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    pwsh scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify
    pwsh scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Revert -WhatIf

.NOTES
    Project: ebilling-r1, task 097 second review F2. Shape: scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 (task 122:
    preconditions, modes, revert-on-failed-grant, publish only on a write, the reused writer profile) and
    scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1 (the reader grant, read only). The one difference from 122: the
    Readers profile is granted read. SpaarkeMaster source carries IsSecured=1 and both rows. Pinned against that source by
    ResubmissionOfFieldSecurityAgreementTests; every BFF write of the column is pinned app-only by
    ResubmissionOfAppOnlyWriteGuardTests. Auth: the operator's own az CLI identity (System Administrator in the environment).
    No secrets. Exit codes: 0 = pass / done, 1 = a gap or an error.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify,
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = [bool]$WhatIfPreference
if ($IsDryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Revert -and $Verify) { throw '-Revert and -Verify are separate modes; run -Revert, then check IsSecured.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds: at least one application id is required.' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$IsWrite = -not $IsDryRun -and -not $Verify
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by ResubmissionOfFieldSecurityAgreementTests) ─────────────────────────────────────────
$Table = 'sprk_invoicesubmission'
$Column = 'sprk_resubmissionof'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
# The platform's own System Administrator field security profile: a fixed id in every environment, and Dataverse gives it a row on
# every secured column. The one profile besides the writer and the reader allowed a row; matched by id, never by name.
$SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}

. (Join-Path $PSScriptRoot '..' 'common' 'DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
    if ($State -eq 'DONE') { $script:Wrote = $true }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host "Column      : $Table.$Column (only the server sets it: BFF writer profile writes, reader profile reads)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Revert -and $IsDryRun) { 'REVERT DRY RUN (-WhatIf, no writes)' } elseif ($Revert) { 'REVERT' } elseif ($IsDryRun) { 'DRY RUN (-WhatIf, no writes)' } else { 'WRITE' }))
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
$publishBody = @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }

# ── -REVERT: un-secure first, then remove the profile rows, then publish ─────────────────────────────────────
if ($Revert) {
    Write-Host "`nRevert"
    $secured = (Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured
    $ourRows = @((Invoke-DvGet ("fieldpermissions?`$select=fieldpermissionid&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column'" +
        "&`$expand=fieldsecurityprofileid(`$select=name)")).value | Where-Object { $_.fieldsecurityprofileid.name -in $WriterProfileName, $ReaderProfileName })
    $wrote = $false
    if (-not $secured) { Write-Host "  OK       $Table.$Column is not field-secured" -ForegroundColor Green }
    elseif ($IsDryRun) { Write-Host "  WOULD    (r1) un-secure $Table.$Column" -ForegroundColor Cyan }
    elseif ($PSCmdlet.ShouldProcess("$Table.$Column", 'Revert: un-secure the column')) {
        $typed = Invoke-DvGet $attrPath; $typed.IsSecured = $false
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $wrote = $true; Write-Host "  DONE     (r1) un-secured $Table.$Column" -ForegroundColor Green
    }
    if ($ourRows.Count -eq 0) { Write-Host "  OK       no $WriterProfileName or $ReaderProfileName row on $Table.$Column" -ForegroundColor Green }
    foreach ($row in $ourRows) {
        if ($IsDryRun) { Write-Host "  WOULD    (r2) delete $($row.fieldsecurityprofileid.name) row $($row.fieldpermissionid) on $Table.$Column" -ForegroundColor Cyan }
        elseif ($PSCmdlet.ShouldProcess("fieldpermission $($row.fieldpermissionid)", "Revert: delete the $($row.fieldsecurityprofileid.name) row on $Table.$Column")) {
            try { Invoke-DvWrite DELETE "fieldpermissions($($row.fieldpermissionid))" $null | Out-Null; Write-Host "  DONE     (r2) deleted $($row.fieldsecurityprofileid.name) row $($row.fieldpermissionid)" -ForegroundColor Green }
            catch { if ("$($_.Exception.Message)" -notmatch '404') { throw }; Write-Host "  OK       (r2) the row went with the un-secure" -ForegroundColor Green }
            $wrote = $true
        }
    }
    if ($IsDryRun) { Write-Host "  WOULD    (r3) publish $Table (only if r1 or r2 wrote)" -ForegroundColor Cyan; Write-Host "`nREVERT DRY RUN complete - nothing was written." -ForegroundColor Cyan; exit 0 }
    if ($wrote -and $PSCmdlet.ShouldProcess($Table, 'Revert: publish')) { Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null; Write-Host "`nPublished $Table." -ForegroundColor Green }
    if ((Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured) { Write-Host "`nFAIL: $Table.$Column is still field-secured." -ForegroundColor Red; exit 1 }
    Write-Host "`nREVERTED: $Table.$Column is not field-secured. Revert the SpaarkeMaster source (Entity.xml IsSecured, the FieldSecurityProfiles.xml rows) by a commit." -ForegroundColor Green
    exit 0
}

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$attr = try { Invoke-DvGet "$attrPath`?`$select=MetadataId,IsSecured,CanBeSecuredForCreate,CanBeSecuredForRead,CanBeSecuredForUpdate" } catch { $null }
if (-not $attr) { Report 'FAIL' "(p0) $Table.$Column does not exist in this environment" }
elseif (-not ($attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForRead -and $attr.CanBeSecuredForUpdate)) {
    Report 'FAIL' "(p0) $Table.$Column cannot be secured for read, create and update"
}
else { Report 'OK' "(p0) $Table.$Column exists and can be secured for read, create and update" }

$writers = @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$WriterProfileName'" +
    '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
$writer = $null
if ($writers.Count -eq 1) { $writer = $writers[0]; Report 'OK' "(p1) profile '$WriterProfileName' exists ($($writer.fieldsecurityprofileid))" }
else { Report 'FAIL' "(p1) $($writers.Count) profile(s) named '$WriterProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

$readers = @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$ReaderProfileName'" +
    '&$expand=teamprofiles_association($select=teamid,name)')).value)
$reader = $null
if ($readers.Count -eq 1) { $reader = $readers[0]; Report 'OK' "(p7) profile '$ReaderProfileName' exists ($($reader.fieldsecurityprofileid))" }
else { Report 'FAIL' "(p7) $($readers.Count) profile(s) named '$ReaderProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
$membership = $null
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @(@{ Id = 'p1'; Profile = $writer; Name = $WriterProfileName }, @{ Id = 'p7'; Profile = $reader; Name = $ReaderProfileName })) {
        if (-not $p.Profile) { continue }
        # A field security profile is a ROOT component: only its own solutioncomponents row ('Direct') counts.
        if (Test-DvInSolution -Membership $membership -ComponentId $p.Profile.fieldsecurityprofileid) { Report 'OK' "($($p.Id)) '$($p.Name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "($($p.Id)) '$($p.Name)' is not in $SolutionUniqueName - its permission on $Column would not travel" }
    }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname,isdisabled&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p2) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) BFF app user '$($u.fullname)' is a member of $WriterProfileName" }
        else { Report 'FAIL' "(p2) BFF app user '$($u.fullname)' is NOT a member of $WriterProfileName - it could not write $Column once secured (unless System Administrator). Membership is maintained by Set-RecordCreatorPersonSchema.ps1, never here" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p2) $WriterProfileName member '$($m.fullname)' is $kind - it could set every submission's Correction target"
    }
    $teams = @($writer.teamprofiles_association)
    foreach ($tm in $teams) {
        $what = if ($tm.isdefault) { "(p3) $WriterProfileName is on business-unit DEFAULT team '$($tm.name)'" } else { "(p2) $WriterProfileName has TEAM '$($tm.name)'" }
        Report 'FAIL' "$what - every member could set the Correction target"
    }
    if (@($teams | Where-Object { $_.isdefault }).Count -eq 0) { Report 'OK' "(p3) no business-unit default team holds $WriterProfileName" }
}

$configuredWriters = 0
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$Column'").value)) {
        $configuredWriters++
        Report 'FAIL' "(p4) $($ch.T) row $($row."$($ch.T)id") targets $Column - only the BFF writes it; remove or retarget the row"
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p4) no field-mapping rule, AI topic-registry row or email update field targets $Column" }

if ($membership -and $attr) {
    $tableMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId,EntitySetName"
    $how = Test-DvInSolution -Membership $membership -ComponentId $attr.MetadataId -TableMetadataId $tableMeta.MetadataId
    if ($how) { Report 'OK' "(p5) $Table.$Column is in $SolutionUniqueName ($how)" }
    else { Report 'FAIL' "(p5) $Table.$Column is not in $SolutionUniqueName - the secured state would not travel" }

    $withTarget = @((Invoke-DvGet "$($tableMeta.EntitySetName)?`$select=$($Table)id&`$filter=_$($Column)_value ne null").value).Count
    Report 'INFO' "(p6) $withTarget $Table row(s) name an earlier submission now (securing a column does not change what was written)"
    $forms = @((Invoke-DvGet "systemforms?`$select=name,type,formxml&`$filter=objecttypecode eq '$Table'").value |
        Where-Object { $_.formxml -match "datafieldname=`"$Column`"" })
    foreach ($f in $forms) { Report 'INFO' "(p6) form '$($f.name)' (type $($f.type)) binds $Column - it stays on the form, shown read-only to users outside $WriterProfileName" }
}
foreach ($u in $bffUsers) {
    $isAdmin = @((Invoke-DvGet "systemusers($($u.systemuserid))/systemuserroles_association?`$select=name&`$filter=name eq 'System Administrator'").value).Count -gt 0
    $adminNote = if ($isAdmin) { 'holds System Administrator (full field access; no masked window for it)' } else { 'does NOT hold System Administrator - it reads the column as null between (a) securing and the writer grant (seconds)' }
    Report 'INFO' "(p6) BFF app user '$($u.fullname)'$(if ($u.isdisabled) { ' (DISABLED)' }) $adminNote"
}

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p8) no business-unit default team was read - the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p8) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p8) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile - its users would read the Correction target EMPTY. Adding it is a team change (the owner's); this script never makes it" }
    }
}

if ($IsWrite -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: a write run needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) SECURE, then grant the profiles at once ─────────────────────────────────────────────────────────────
Write-Host "`n(a) Field-level security"
function Get-Permission([string]$ProfileId) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured([bool]$Value) {
    # GET-then-PUT of the typed attribute definition (scripts/Set-RegardingPairFieldSecurity.ps1: proven live on string and lookup columns).
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
# The writer first (the BFF's own read and write), then the reader (every other user's read). Create = the value of create AND update.
$specs = @()
if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
function Grant-MissingPermissions {
    foreach ($s in $specs) {
        if (-not (Get-Permission $s.Id) -and $PSCmdlet.ShouldProcess("$Table.$Column", "Grant $($s.Name) read=4 create=$($s.Create) update=$($s.Create)")) {
            Grant-Permission $s.Id $s.Create
            Report 'DONE' "granted $($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))"
        }
    }
}
function Write-MaskedRecovery([string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT its profile permissions !!!" -ForegroundColor Red
    Write-Host '  A user, and a BFF application user without System Administrator, then reads the Correction target as empty/null:' -ForegroundColor Red
    Write-Host '  a Correction submission cannot be committed, and the BFF cannot create one.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script: on an already-secured column it grants only the missing profile' -ForegroundColor Red
    Write-Host '        permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

if (-not $attr) { Report 'FAIL' "$Table.$Column cannot be secured: it does not exist" }
elseif ($attr.IsSecured) {
    Report 'OK' "$Table.$Column is field-secured"
    if ($IsWrite) {
        try { Grant-MissingPermissions }
        catch { Write-MaskedRecovery "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
    }
}
elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured - any user with Write on a submission can point it at another submission" }
elseif ($IsDryRun -or $specs.Count -lt 2) { Report 'WOULD' "secure $Table.$Column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read only" }
elseif (-not $PSCmdlet.ShouldProcess("$Table.$Column", "Field-secure the column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read")) { Report 'FAIL' "$Table.$Column was not secured (declined at the confirmation)" }
else {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Set-ColumnSecured $true
    Report 'DONE' "secured $Table.$Column"
    try {
        Grant-MissingPermissions
    } catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $Table.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
        Write-Host "           reverting $Table.$Column to NOT field-secured, so no reader or BFF user is left masked..." -ForegroundColor Yellow
        try {
            Set-ColumnSecured $false
            Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
            $back = (Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured
            if ($back) { throw "the revert was accepted but $Table.$Column still reads IsSecured=true" }
            Write-Host "  REVERTED $Table.$Column is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
        } catch {
            Write-MaskedRecovery "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
        }
        throw $grantError
    }
    $clock.Stop()
    Report 'INFO' ("masked window {0:N1}s (secure -> both grants)" -f $clock.Elapsed.TotalSeconds)
}

# ── (b) THE PERMISSIONS ─────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Permissions on $Table.$Column"
foreach ($s in $specs) {
    $perm = Get-Permission $s.Id
    if ($perm) {
        if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) (read=4 create=$($s.Create) update=$($s.Create))" }
        else { Report 'FAIL' "$($s.Name) is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
    }
    elseif ($Verify) { Report 'MISSING' "$($s.Name) on $Table.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))" }
    else { Report 'FAIL' "$($s.Name) on $Table.$Column is still missing after the run granted it" }
}

# ── (c) every OTHER profile that can create or update ───────────────────────────────────────────────────────
Write-Host "`n(c) Other profiles"
# Create or update by any profile but the writer, the reader (both checked in (b), so never counted twice) and the platform's System
# Administrator profile (all by id). A profile that only reads the column is allowed and listed.
$writerId = if ($writer) { $writer.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$readerId = if ($reader) { $reader.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$rows = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=fieldsecurityprofileid,name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canread eq 4 or canupdate eq 4 or cancreate eq 4)").value |
    Where-Object { $_.fieldsecurityprofileid.fieldsecurityprofileid.ToString().ToLowerInvariant() -notin $SystemAdministratorProfileId, $writerId, $readerId })
$others = @($rows | Where-Object { $_.cancreate -eq 4 -or $_.canupdate -eq 4 })
foreach ($o in $others) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' ($($o.fieldsecurityprofileid.fieldsecurityprofileid)) can write $Table.$Column (read=$($o.canread) create=$($o.cancreate) update=$($o.canupdate)) - only the BFF writer profile may" }
foreach ($o in @($rows | Where-Object { $_.cancreate -ne 4 -and $_.canupdate -ne 4 })) { Report 'INFO' "profile '$($o.fieldsecurityprofileid.name)' only reads $Table.$Column (read=$($o.canread))" }
if ($others.Count -eq 0) { Report 'OK' "no profile besides '$WriterProfileName' and the System Administrator profile ($SystemAdministratorProfileId) can create or update $Table.$Column ('$ReaderProfileName' is checked in (b): read only)" }
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("System Administrator '{0}'{1} (full field access by platform rule)" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "System Administrator TEAM '$($tm.name)' - every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($IsWrite -and $script:Wrote -and $PSCmdlet.ShouldProcess($Table, 'Publish')) {
    Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}
elseif ($IsWrite) { Write-Host "`nNothing written; no publish." -ForegroundColor Green }

if ($IsDryRun) {
    if ($gaps.Count -gt 0) {
        Write-Host "`nDRY RUN complete - nothing was written. A write run would be REFUSED ($($gaps.Count) gap(s)):" -ForegroundColor Yellow
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }
    else { Write-Host "`nDRY RUN complete - nothing was written." -ForegroundColor Cyan }
    exit 0
}
if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Table.$Column is field-secured, the BFF writer profile holds read/create/update, the reader profile holds read, and no other profile (the System Administrator profile excepted) can create or update it." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
