#!/usr/bin/env pwsh
<#
.SYNOPSIS
    eBilling R1 task 122 (#2235, D-176) - field-secures sprk_invoicesubmission.sprk_findingsjson so ONLY the BFF reads and
    writes it: the BFF application user(s) through "Spaarke BFF-Managed Field Writers" (read, create, update), and NO reader
    profile. -WhatIf is a dry run; a plain run writes; -Verify checks.

.DESCRIPTION
    WHY (#2235, owner decision 2026-10-22). sprk_findingsjson holds a submission's stored findings, Duplicate detail included:
    the earlier invoice's tracking number, its id and the duplicated line's values. D-176 lets a user see that detail only when
    they can read the earlier invoice, and the BFF applies that check on /review and /blocked (DuplicateDetailGate). Unsecured,
    any user with Read on a submission reads the raw column on the form or through the Web API, past the check. Secured with no
    reader profile, every such read returns null; the BFF reads it app-only (FindingsJsonAppOnlyReadGuardTests). It also stops a
    user writing junk findings into the column (DEF-029), which broke Override and Return.

    PROFILE (REUSED, never created or re-membered here):
      "Spaarke BFF-Managed Field Writers" (unified-access-control-r2 task 133; members: the BFF application user(s) ONLY). It
          gets read=4 create=4 update=4 on the column.
      "Spaarke BFF-Managed Field Readers" is deliberately NOT granted: it is on every business unit's default team, so a grant
          would leave the column readable to everyone. A row for it on this column is FAIL in every mode (never removed here).

    PRECONDITIONS (every mode; a write run refuses unless all pass):
      (p0) the column exists and can be secured for read, create and update;
      (p1) the writer profile exists, exactly once, and is in -SolutionUniqueName;
      (p2) its members are exactly the -BffApplicationIds application users: each is a member, and there is no other user and no
           team (a team would make every member a reader);
      (p3) no business unit's default team holds the writer profile;
      (p4) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets the column (maker configuration
           writing outside the BFF, refused once secured);
      (p5) the column is in -SolutionUniqueName (its own row, or its table with rootcomponentbehavior 0), so the secured state
           travels with the solution;
      (p6) informational: how many submissions carry findings now (securing a column does not change what was written), the
           forms on this environment that still bind the column (shown masked to users outside the profile), and whether each
           BFF application user holds System Administrator (full field access by platform rule; then there is no masked window
           for the BFF between (a) and its grant).

    STEPS (a plain run), in this order:
      (a) secure the column, then IMMEDIATELY grant the writer profile read=4 create=4 update=4 (12 retries on 0x8004f508). Until
          that grant lands, a BFF application user without System Administrator reads the column as null. If the grant fails, the
          column is REVERTED to not secured and published before the script stops; if the revert fails too it prints
          "RECOVERY REQUIRED NOW". Re-running is the resume path: on a column already secured it grants only what is missing;
      (b) the writer profile's permission is read=4 create=4 update=4; the reader profile has NO row on the column;
      (c) any OTHER profile that can read, create or update the column is FAIL (System Administrator excepted); System
          Administrator holders are listed;
      (d) publish sprk_invoicesubmission - only when this run wrote something.

    A second run reports every item OK and writes nothing (no publish either).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users, the ONLY members of the writer profile (dev:
    5967251e-171c-46fe-a6c2-ef843c90309d and, while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c).
    Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution the writer profile and sprk_invoicesubmission travel in. Default SpaarkeMaster.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -WhatIf.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
    pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    Project: ebilling-r1, task 122 (#2235; closes DEF-029). Shape: scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1
    (modes, revert-on-failed-grant, publish only on a write) and scripts/Set-DocumentPointerFieldSecurity.ps1 (the reused BFF
    writer profile, the -BffApplicationIds membership check, the maker-configuration check). The server-only shape (writer, no
    reader) is the F3 columns' in scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1. SpaarkeMaster source carries
    IsSecured=1 and the writer row. Auth: the operator's own az CLI identity (System Administrator in the environment). No
    secrets. Live runs follow the notices rule (ontology-r1 review of the script and its -WhatIf; notices to ontology-r1 and
    uac-r2 before and after). Exit codes: 0 = pass / done, 1 = a gap or an error.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = [bool]$WhatIfPreference
if ($IsDryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds: at least one application id is required.' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$IsWrite = -not $IsDryRun -and -not $Verify
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$Table = 'sprk_invoicesubmission'
$Column = 'sprk_findingsjson'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'

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
Write-Host "Column      : $Table.$Column (server-only: BFF writer profile, no reader profile)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (-WhatIf, no writes)' } else { 'WRITE' }))

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
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

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
$membership = $null
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    if ($writer) {
        if (Test-DvInSolution -Membership $membership -ComponentId $writer.fieldsecurityprofileid) { Report 'OK' "(p1) '$WriterProfileName' is in $SolutionUniqueName" }
        else { Report 'FAIL' "(p1) '$WriterProfileName' is not in $SolutionUniqueName - its permission on $Column would not travel" }
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
        else { Report 'FAIL' "(p2) BFF app user '$($u.fullname)' is NOT a member of $WriterProfileName - it would read $Column as null once secured (unless System Administrator). Membership is maintained by Set-RecordCreatorPersonSchema.ps1, never here" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p2) $WriterProfileName member '$($m.fullname)' is $kind - it would read every submission's findings"
    }
    $teams = @($writer.teamprofiles_association)
    foreach ($tm in $teams) {
        $what = if ($tm.isdefault) { "(p3) $WriterProfileName is on business-unit DEFAULT team '$($tm.name)'" } else { "(p2) $WriterProfileName has TEAM '$($tm.name)'" }
        Report 'FAIL' "$what - every member would read the findings"
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

    $withFindings = @((Invoke-DvGet "$($tableMeta.EntitySetName)?`$select=$($Table)id&`$filter=$Column ne null").value).Count
    Report 'INFO' "(p6) $withFindings $Table row(s) carry findings now (securing a column does not change what was written)"
    $forms = @((Invoke-DvGet "systemforms?`$select=name,type,formxml&`$filter=objecttypecode eq '$Table'").value |
        Where-Object { $_.formxml -match "datafieldname=`"$Column`"" })
    foreach ($f in $forms) { Report 'INFO' "(p6) form '$($f.name)' (type $($f.type)) still binds $Column - users outside the profile see it masked; the SpaarkeMaster source drops it (task 122)" }
}
foreach ($u in $bffUsers) {
    $isAdmin = @((Invoke-DvGet "systemusers($($u.systemuserid))/systemuserroles_association?`$select=name&`$filter=name eq 'System Administrator'").value).Count -gt 0
    $adminNote = if ($isAdmin) { 'holds System Administrator (full field access; no masked window for it)' } else { 'does NOT hold System Administrator - it reads the column as null between (a) securing and the writer grant (seconds)' }
    Report 'INFO' "(p6) BFF app user '$($u.fullname)'$(if ($u.isdisabled) { ' (DISABLED)' }) $adminNote"
}

if ($IsWrite -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: a write run needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) SECURE, then grant the writer at once ───────────────────────────────────────────────────────────────
Write-Host "`n(a) Field-level security"
function Get-Permission([string]$ProfileId) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-WriterPermission([string]$ProfileId) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = 4; canupdate = 4
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
    # GET-then-PUT of the typed attribute definition (scripts/Set-RegardingPairFieldSecurity.ps1).
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
function Grant-MissingWriter {
    if (-not (Get-Permission $writer.fieldsecurityprofileid)) {
        Grant-WriterPermission $writer.fieldsecurityprofileid
        Report 'DONE' "granted $WriterProfileName on $Table.$Column (read=4 create=4 update=4)"
    }
}
function Write-MaskedRecovery([string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT the BFF writer permission !!!" -ForegroundColor Red
    Write-Host '  A BFF application user without System Administrator then reads every submission''s findings as null:' -ForegroundColor Red
    Write-Host '  /review and /blocked show none, Override and Return refuse, and the submission job cannot finish.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script: on an already-secured column it grants only the missing writer' -ForegroundColor Red
    Write-Host '        permission (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

if (-not $attr) { Report 'FAIL' "$Table.$Column cannot be secured: it does not exist" }
elseif ($attr.IsSecured) {
    Report 'OK' "$Table.$Column is field-secured"
    if ($IsWrite) {
        try { Grant-MissingWriter }
        catch { Write-MaskedRecovery "granting the writer permission on the already-secured column failed: $($_.Exception.Message)"; throw }
    }
}
elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured - any user with Read on a submission reads its findings, Duplicate detail included" }
elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$Column, then at once grant $WriterProfileName read/create/update (no reader profile)" }
else {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Set-ColumnSecured $true
    Report 'DONE' "secured $Table.$Column"
    try {
        Grant-MissingWriter
    } catch {
        $grantError = $_
        Write-Host "  FAIL     granting $WriterProfileName on $Table.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
        Write-Host "           reverting $Table.$Column to NOT field-secured, so the BFF is not left reading null..." -ForegroundColor Yellow
        try {
            Set-ColumnSecured $false
            Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
            $back = (Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured
            if ($back) { throw "the revert was accepted but $Table.$Column still reads IsSecured=true" }
            Write-Host "  REVERTED $Table.$Column is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
        } catch {
            Write-MaskedRecovery "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
        }
        throw $grantError
    }
    $clock.Stop()
    Report 'INFO' ("masked window {0:N1}s (secure -> writer grant)" -f $clock.Elapsed.TotalSeconds)
}

# ── (b) THE PERMISSIONS ─────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Permissions on $Table.$Column"
if ($writer) {
    $perm = Get-Permission $writer.fieldsecurityprofileid
    if ($perm) {
        if ($perm.canread -eq 4 -and $perm.cancreate -eq 4 -and $perm.canupdate -eq 4) { Report 'OK' "$WriterProfileName (read=4 create=4 update=4)" }
        else { Report 'FAIL' "$WriterProfileName is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=4 update=4" }
    }
    elseif ($Verify) { Report 'MISSING' "$WriterProfileName on $Table.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "grant $WriterProfileName on $Table.$Column (read=4 create=4 update=4)" }
    else { Report 'FAIL' "$WriterProfileName on $Table.$Column is still missing after the run granted it" }
}
$readerProfile = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid&`$filter=name eq '$ReaderProfileName'").value) | Select-Object -First 1
if ($readerProfile -and ($r = Get-Permission $readerProfile.fieldsecurityprofileid)) {
    Report 'FAIL' "$ReaderProfileName has a row on $Table.$Column (read=$($r.canread)) - it is on every default team, so everyone would read the findings. Not removed by this script"
}
else { Report 'OK' "$ReaderProfileName has no row on $Table.$Column (server-only)" }

# ── (c) every OTHER reader or writer ────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Other profiles"
$others = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canread eq 4 or canupdate eq 4 or cancreate eq 4)").value |
    Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, $ReaderProfileName, 'System Administrator' })
foreach ($o in $others) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can read or write $Table.$Column (read=$($o.canread) create=$($o.cancreate) update=$($o.canupdate)) - only the BFF writer profile may" }
if ($others.Count -eq 0) { Report 'OK' "no profile besides '$WriterProfileName' and System Administrator can read or write $Table.$Column" }
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("System Administrator '{0}'{1} (full field access by platform rule)" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "System Administrator TEAM '$($tm.name)' - every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($IsWrite -and $script:Wrote) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
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
if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Table.$Column is field-secured; only the BFF application user(s) (and System Administrator) read or write it." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
