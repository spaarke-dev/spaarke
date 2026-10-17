#!/usr/bin/env pwsh
<#
.SYNOPSIS
    eBilling R1 task 026 (owner decision D-95(2)) - field-secures sprk_signal.sprk_firmreason, the reason text a firm will
    see: every user who reads Signals still reads it, and ONLY the Ontology Service writer (and System Administrator) can
    create or update it. -WhatIf is a dry run; a plain run writes; -Verify checks.

.DESCRIPTION
    WHY (D-95(2), task 026 review). sprk_firmreason is projected to law firms (task 130). Unsecured, any user with Write on
    a Signal could change what a firm is told. Securing it makes every such write a native 403; the Ontology Service
    writer (mi-ontology-writer-dev) is the one identity that writes it.

    PROFILES:
      "Spaarke BFF-Managed Field Readers" (unified-access-control-r2 task 133) - REUSED, read only. It is already on EVERY
          business unit's default team, so every user keeps reading the column. This script never adds or removes a
          member of it: when a default team is missing, (p2) FAILs and nothing is written (a team change is the owner's).
      "Spaarke Ontology Writer Firm Reason Writers" - created here when absent, with a fixed id (the id SpaarkeMaster's
          source carries), added to SpaarkeMaster. Its ONLY member is the application user named by
          -OntologyWriterApplicationId; it holds read=4 create=4 update=4 on sprk_signal.sprk_firmreason and nothing else.
          It is NOT the D-136 profile "Spaarke Ontology Writer Field Writers": that profile is ontology-platform-r1's, and
          its -Verify (scripts/Set-ChildRecordCreatorPersonSchema.ps1) FAILs on any row outside sprk_createdbyperson.

    PRECONDITIONS (every mode; a write run refuses unless all pass):
      (p1) the reader profile exists, exactly once;
      (p2) the reader profile is on EVERY business unit's default team;
      (p3) the ontology writer's application user exists;
      (p4) informational: how many Signals carry a firm reason now.

    STEPS (a plain run), in this order:
      (a) create the writer profile when absent (in -SolutionUniqueName) and add the writer application user to it; any
          OTHER member (a human, another application user, a team) is FAIL in every mode - reported, never removed;
      (b) secure the column, then IMMEDIATELY grant the reader profile read=4 and the writer profile read=4 create=4
          update=4 (12 retries on 0x8004f508). If a grant fails after the column was secured, the column is REVERTED to
          not secured and published before the script stops; if the revert fails too it prints "RECOVERY REQUIRED NOW".
          Re-running is the resume path: on a column already secured it grants only the missing permissions;
      (c) any OTHER profile that can create or update the column is FAIL; System Administrator holders are listed;
      (d) the writer profile is in -SolutionUniqueName;
      (e) publish sprk_signal - only when this run wrote something.

    A second run reports every item OK and writes nothing (no publish either).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER OntologyWriterApplicationId
    Application (client) id of the Ontology Service writer's Dataverse application user, the ONLY member of the writer
    profile. Dev: 69040982-612e-469e-a85f-26d5172367c5 (# mi-ontology-writer-dev).

.PARAMETER SolutionUniqueName
    The unmanaged solution the writer profile travels in. Default SpaarkeMaster (where sprk_signal and the D-136 profile
    travel).

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -WhatIf.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1 -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5 -WhatIf
    pwsh scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1 -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5
    pwsh scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1 -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5 -Verify

.NOTES
    Project: ebilling-r1, task 026 fix-round-1 item 2. Pattern: scripts/Set-RegardingPairFieldSecurity.ps1 (#1616) and the
    D-136 profile in scripts/Set-ChildRecordCreatorPersonSchema.ps1. Pinned against the SpaarkeMaster solution XML by
    FirmReasonFieldSecurityAgreementTests. Run AFTER Deploy-EbillingSignalLineSubjectSchema.ps1 (it creates the column).
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
    Exit codes: 0 = pass / done, 1 = a gap or an error.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [Parameter(Mandatory)][string]$OntologyWriterApplicationId,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = [bool]$WhatIfPreference
if ($IsDryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
$OntologyWriterApplicationId = $OntologyWriterApplicationId.Trim()
if (-not [Guid]::TryParse($OntologyWriterApplicationId, [ref][Guid]::Empty)) { throw "-OntologyWriterApplicationId: '$OntologyWriterApplicationId' is not a GUID." }
$IsWrite = -not $IsDryRun -and -not $Verify
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by FirmReasonFieldSecurityAgreementTests) ─────────────────────────────────────────────
$Table = 'sprk_signal'
$Column = 'sprk_firmreason'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke Ontology Writer Firm Reason Writers'
$WriterProfileFixedId = 'ec323a05-f95c-4308-aac5-6d899032691e'
$WriterProfileDescription = 'Read/Create/Update on sprk_signal.sprk_firmreason ONLY (the reason text a firm sees). Members: the Ontology Service writer application user ONLY. ebilling-r1 task 026, owner decision D-95(2).'
$Secured = $true

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
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (-WhatIf, no writes)' } else { 'WRITE' }))

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$attr = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=IsSecured,CanBeSecuredForCreate,CanBeSecuredForRead,CanBeSecuredForUpdate"
if (-not ($attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForRead -and $attr.CanBeSecuredForUpdate)) {
    Report 'FAIL' "(p0) $Table.$Column cannot be secured for read, create and update"
}

$readers = @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$ReaderProfileName'" +
    '&$expand=teamprofiles_association($select=teamid,name)')).value)
$reader = $null
if ($readers.Count -eq 1) { $reader = $readers[0]; Report 'OK' "(p1) profile '$ReaderProfileName' exists" }
else { Report 'FAIL' "(p1) $($readers.Count) profile(s) named '$ReaderProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read - the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p2) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile - its users would read the firm reason EMPTY. Adding it is a team change (the owner's); this script never makes it" }
    }
}

$ontologyWriter = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $OntologyWriterApplicationId").value) | Select-Object -First 1
if ($ontologyWriter) { Report 'OK' "(p3) ontology writer application user '$($ontologyWriter.fullname)'" }
else { Report 'FAIL' "(p3) no application user for the ontology writer appId $OntologyWriterApplicationId in this environment" }

$set = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName").EntitySetName
$withReason = @((Invoke-DvGet "$set`?`$select=$($Table)id&`$filter=$Column ne null").value).Count
Report 'INFO' "(p4) $withReason $Table row(s) carry a firm reason now (securing a column does not change what was written)"

if ($IsWrite -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: a write run needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) THE WRITER PROFILE AND ITS ONE MEMBER ───────────────────────────────────────────────────────────────
Write-Host "`n(a) $WriterProfileName"
$writer = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$WriterProfileName'").value)
$writerId = $null
if ($writer.Count -gt 1) { Report 'FAIL' "$($writer.Count) profiles named '$WriterProfileName' - exactly one is allowed" }
elseif ($writer.Count -eq 1) {
    $writerId = $writer[0].fieldsecurityprofileid
    if ($writerId -eq $WriterProfileFixedId) { Report 'OK' "profile '$WriterProfileName' ($writerId)" }
    else { Report 'FAIL' "profile '$WriterProfileName' has id $writerId, not the id SpaarkeMaster's source carries ($WriterProfileFixedId)" }
}
elseif ($Verify) { Report 'MISSING' "profile '$WriterProfileName'" }
elseif ($IsDryRun) { Report 'WOULD' "create profile '$WriterProfileName' ($WriterProfileFixedId) in $SolutionUniqueName" }
else {
    Invoke-DvWrite POST 'fieldsecurityprofiles' @{ fieldsecurityprofileid = $WriterProfileFixedId; name = $WriterProfileName; description = $WriterProfileDescription } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    $writerId = $WriterProfileFixedId
    Report 'DONE' "created profile '$WriterProfileName' ($writerId) in $SolutionUniqueName"
}

if ($ontologyWriter) {
    $label = "app user '$($ontologyWriter.fullname)'"
    $members = if ($writerId) { @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid").value) } else { @() }
    if ($members | Where-Object { $_.systemuserid -eq $ontologyWriter.systemuserid }) { Report 'OK' "member: $label" }
    elseif ($Verify) { Report 'MISSING' "member: $label" }
    elseif ($IsDryRun -or -not $writerId) { Report 'WOULD' "add $label to $WriterProfileName" }
    else {
        Invoke-DvWrite POST "fieldsecurityprofiles($writerId)/systemuserprofiles_association/`$ref" @{ '@odata.id' = "$Api/systemusers($($ontologyWriter.systemuserid))" } | Out-Null
        Report 'DONE' "added $label to $WriterProfileName"
    }
}

# The membership IS the lock: anyone else on the profile could change what a firm is told. Reported, never removed.
if ($writerId) {
    $allowed = if ($ontologyWriter) { $ontologyWriter.systemuserid.ToString().ToLowerInvariant() } else { '' }
    $users = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    $strangers = @($users | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -ne $allowed })
    foreach ($m in $strangers) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) other than the ontology writer" } else { 'a HUMAN user' }
        Report 'FAIL' "$WriterProfileName member '$($m.fullname)' is $kind - only the Ontology Service writer may write $Table.$Column"
    }
    $teams = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $teams) { Report 'FAIL' "$WriterProfileName has TEAM '$($tm.name)' - every member could write $Table.$Column" }
    if ($strangers.Count -eq 0 -and $teams.Count -eq 0) { Report 'OK' "$WriterProfileName has no member besides the ontology writer" }

    $stray = @((Invoke-DvGet "fieldpermissions?`$select=entityname,attributelogicalname&`$filter=_fieldsecurityprofileid_value eq $writerId").value |
        Where-Object { -not ($_.entityname -eq $Table -and $_.attributelogicalname -eq $Column) })
    foreach ($r in $stray) { Report 'FAIL' "$WriterProfileName also grants $($r.entityname).$($r.attributelogicalname) - it may hold $Table.$Column only" }
}

# ── (b) SECURE, then grant at once ──────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Field-level security"
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
    # GET-then-PUT of the typed attribute definition (scripts/Set-RegardingPairFieldSecurity.ps1).
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
$specs = @(@{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4 })
function Grant-MissingPermissions {
    foreach ($s in $specs) {
        if (-not (Get-Permission $s.Id)) { Grant-Permission $s.Id $s.Create; Report 'DONE' "granted $($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))" }
    }
}
function Write-MaskedRecovery([string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    Write-Host '  Every non-administrator then reads the firm reason EMPTY.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script: on an already-secured column it grants only the missing profile' -ForegroundColor Red
    Write-Host '        permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

if ($attr.IsSecured -eq $Secured) {
    Report 'OK' "$Table.$Column is field-secured"
    if ($IsWrite) {
        try { Grant-MissingPermissions }
        catch { Write-MaskedRecovery "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
    }
}
elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured - any user with Write on a Signal can change what a firm is told" }
elseif ($IsDryRun -or -not $writerId) { Report 'WOULD' "secure $Table.$Column, then at once grant the reader (read) and writer (read/create/update) profiles" }
else {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Set-ColumnSecured $Secured
    Report 'DONE' "secured $Table.$Column"
    try {
        Grant-MissingPermissions
    } catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $Table.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
        Write-Host "           reverting $Table.$Column to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
        try {
            Set-ColumnSecured (-not $Secured)
            Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
            $back = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=IsSecured").IsSecured
            if ($back -eq $Secured) { throw "the revert was accepted but $Table.$Column still reads IsSecured=true" }
            Write-Host "  REVERTED $Table.$Column is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
        } catch {
            Write-MaskedRecovery "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
        }
        throw $grantError
    }
    $clock.Stop()
    Report 'INFO' ("masked window {0:N1}s (secure -> reader grant)" -f $clock.Elapsed.TotalSeconds)
}

foreach ($s in $specs) {
    if (-not $s.Id) { continue }
    $perm = Get-Permission $s.Id
    if ($perm) {
        if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))" }
        else { Report 'FAIL' "$($s.Name) on $Table.$Column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
    } elseif ($Verify) { Report 'MISSING' "$($s.Name) on $Table.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $Table.$Column" }
    else { Report 'FAIL' "$($s.Name) on $Table.$Column is still missing after the run granted it" }
}

# ── (c) every OTHER writer ──────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Other writers"
$otherWriters = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value |
    Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
foreach ($o in $otherWriters) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $Table.$Column - only the Ontology Service writer may" }
if ($otherWriters.Count -eq 0) { Report 'OK' "no profile besides '$WriterProfileName' and System Administrator can write $Table.$Column" }
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled -and $_.applicationid })) {
        Report 'INFO' "System Administrator application user '$($u.fullname)' (full field access by platform rule)"
    }
}

# ── (d) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(d) Solution"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
elseif ($writerId) {
    # A field security profile is a ROOT component: only its own solutioncomponents row ('Direct') counts.
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    if (Test-DvInSolution -Membership $membership -ComponentId $writerId) { Report 'OK' "'$WriterProfileName' is in $SolutionUniqueName" }
    elseif ($Verify) { Report 'MISSING' "'$WriterProfileName' in $SolutionUniqueName" }
    elseif ($IsDryRun) { Report 'WOULD' "add '$WriterProfileName' to $SolutionUniqueName" }
    else {
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $writerId; ComponentType = 70; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added '$WriterProfileName' to $SolutionUniqueName"
    }
}
elseif ($IsDryRun) { Report 'WOULD' "'$WriterProfileName' is created in $SolutionUniqueName" }

# ── (e) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($IsWrite -and $script:Wrote) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}
elseif ($IsWrite) { Write-Host "`nNothing written; no publish." -ForegroundColor Green }

if ($IsDryRun) { Write-Host "`nDRY RUN complete - nothing was written." -ForegroundColor Cyan; exit 0 }
if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Table.$Column is field-secured, every business unit's default team reads it, and only the Ontology Service writer (and System Administrator) can write it." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
