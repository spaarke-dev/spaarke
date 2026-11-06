#!/usr/bin/env pwsh
<#
.SYNOPSIS
    field-mapping-server-write-path-r1 task 044: field-secures the core record pair of a Signal and a Decision Record
    (sprk_corerecordtype + sprk_corerecordid on sprk_signal and sprk_decisionrecord), so that the pair is written only by
    the server: every user who reads these rows still reads both columns, and only the BFF application users, the
    ontology writer application user and System Administrator can create or update them. Dry run by default; -Apply
    writes; -Verify checks.

.DESCRIPTION
    WHY (owner decision 3 of 2026-11-04, ontology D-204, UAC-r2 decisions P1 and P2 of 2026-11-06). The general create
    refuses both tables (RecordTableRule.CreatedOnlyByItsWriter) and the BFF update paths refuse the pair after create
    (RecordTableRule.FixedAfterCreateColumns). Column security is what keeps every path outside the server from setting
    the pair. The writers (SignalWriter, DecisionRecordWriter) run as the ontology writer application user.

    THE FOUR CELLS: {sprk_signal, sprk_decisionrecord} x {sprk_corerecordtype, sprk_corerecordid}.

    PROFILES:
      "Spaarke BFF-Managed Field Readers" (unified-access-control-r2 task 133) - REUSED: read=4 on each cell. It is on
          EVERY business unit's default team, so every user keeps reading the pair. This script never changes its
          membership: a default team without it is a FAIL and nothing is written.
      "Spaarke BFF-Managed Field Writers" (task 133) - REUSED: read=4 create=4 update=4 on each cell. Members: the BFF
          application users ONLY (-BffApplicationIds). Never changed here: any other member is a FAIL.
      "Spaarke Ontology Writer Core Pair Writers" - created here when absent, with a fixed id (the id SpaarkeMaster's
          source carries), in -SolutionUniqueName. Its ONLY member is the application user of
          -OntologyWriterApplicationId; it holds read=4 create=4 update=4 on the four cells and nothing else. The writer
          reads the pair too (SignalSuppressionService, DecisionCommitService, the decision action executors), so Read
          stays 4. It is NOT the D-136 profile "Spaarke Ontology Writer Field Writers" (ontology-platform-r1's, kept to
          sprk_createdbyperson by scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify).

    PRECONDITIONS (every mode; -Apply refuses unless all pass):
      (p0) each of the four columns exists and can be secured for read, create and update;
      (p1) the reader and the BFF writer profile exist, exactly once each;
      (p2) the reader profile is on EVERY business unit's default team (the teams are queried, never counted in advance);
      (p3) every -BffApplicationIds application user exists and is a member of the BFF writer profile, and that profile
           has no other member and no team;
      (p4) the ontology writer's application user exists;
      (p5) informational: how many rows of each table hold both halves, the type only, the id only.

    STEPS (-Apply), in this order:
      (a) create the core pair writer profile when absent (in -SolutionUniqueName) and add the ontology writer to it; any
          OTHER member, team or column grant on it, or another id, is a FAIL in every mode (reported, never removed), and
          -Apply stops there, before any column is secured;
      (b) per cell: secure the column, then IMMEDIATELY grant the reader (read=4), the BFF writer and the core pair
          writer (read=4 create=4 update=4), retried on 0x8004f508 while the platform propagates the secured state. If a
          grant fails, that column is REVERTED to not secured, both tables are published and the run stops; if the
          revert fails too it prints "RECOVERY REQUIRED NOW". Re-running is the resume path: on a column already
          secured it grants only what is missing;
      (c) any OTHER field security profile with a grant (read, create or update) on a cell is a FAIL. The platform's
          own "System Administrator" profile is exempt; its holders are listed;
      (d) the core pair writer profile is in -SolutionUniqueName;
      (e) publish sprk_signal and sprk_decisionrecord, only when this run wrote something.
      (r) READERS (every mode, read-only): each BFF application user and the ontology writer still reads the pair.
          (r1) for each identity and cell: it holds System Administrator, or is a member of a profile (directly, or
               through a team) whose permission on the cell is read=4;
          (r2) live: one Signal and one Decision Record that hold both halves (read as the operator) are read again
               AS each identity (MSCRMCallerID impersonation; the operator holds System Administrator). Both halves
               must come back. A control read as a made-up caller id must be refused first, so a header the server
               ignored cannot pass the check. With no row holding both halves, (r2) is INFO and (r1) stands.

    A second -Apply reports every item OK and writes nothing (no publish either).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users, the members of the BFF writer profile. Dev:
    5967251e-171c-46fe-a6c2-ef843c90309d and 1e40baad-e065-4aea-a8d4-4b7ab273458c. Per-customer input, never hard-coded.

.PARAMETER OntologyWriterApplicationId
    Application (client) id of the ontology writer's Dataverse application user, the ONLY member of the core pair writer
    profile. Dev: 69040982-612e-469e-a85f-26d5172367c5 (# mi-ontology-writer-dev).

.PARAMETER SolutionUniqueName
    The unmanaged solution the core pair writer profile travels in. Default SpaarkeMaster.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    pwsh scripts/Set-CorePairFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c `
        -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5
    Dry run: every precondition and step's state; zero writes.

.EXAMPLE
    (the same arguments) -Apply      then      (the same arguments) -Verify

.NOTES
    Project: field-mapping-server-write-path-r1, task 044. Modelled on scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1
    and scripts/Set-DocumentPointerFieldSecurity.ps1. Pinned against the SpaarkeMaster solution XML and the BFF code by
    tests/integration/data-mutation/Signals/CorePairFieldSecurityAgreementTests.cs; run whole against a test double by
    tests/scripts/Set-CorePairFieldSecurity.Tests.ps1. Auth: the operator's own az CLI identity (System Administrator in
    the environment). No secrets. Exit codes: 0 = pass / done / dry run, 1 = a gap or an error.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [Parameter(Mandatory)][string]$OntologyWriterApplicationId,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds: at least one application id is required.' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$OntologyWriterApplicationId = $OntologyWriterApplicationId.Trim()
if (-not [Guid]::TryParse($OntologyWriterApplicationId, [ref][Guid]::Empty)) { throw "-OntologyWriterApplicationId: '$OntologyWriterApplicationId' is not a GUID." }
$IsDryRun = -not $Apply.IsPresent -and -not $Verify.IsPresent
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by CorePairFieldSecurityAgreementTests) ───────────────────────────────────────────────
$Tables = @('sprk_signal', 'sprk_decisionrecord')
$Columns = @('sprk_corerecordtype', 'sprk_corerecordid')
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$BffWriterProfileName = 'Spaarke BFF-Managed Field Writers'
$WriterProfileName = 'Spaarke Ontology Writer Core Pair Writers'
$WriterProfileFixedId = '169feee6-7a77-4d00-9e27-ffc27bee6262'
$WriterProfileDescription = 'Read/Create/Update on sprk_corerecordtype and sprk_corerecordid of sprk_signal and sprk_decisionrecord ONLY (the core record pair). Members: the ontology writer application user ONLY. field-mapping-server-write-path-r1 task 044.'
$Secured = $true
$PlatformProfileName = 'System Administrator'
$TypeColumn = 'sprk_corerecordtype'

$Cells = @(foreach ($t in $Tables) { foreach ($c in $Columns) { [pscustomobject]@{ Table = $t; Column = $c; Name = "$t.$c" } } })

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $uri = if ($Path -match '^https?://') { $Path } else { "$Api/$Path" }
    Invoke-RestMethod -Uri $uri -Headers $h -Method Get
}
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Get-AllRows([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = $Path
    while ($next) {
        $page = Invoke-DvGet $next @{ Prefer = 'odata.maxpagesize=5000' }
        foreach ($row in @($page.value)) { $rows.Add($row) }
        $next = $page.'@odata.nextLink'
    }
    , $rows.ToArray()
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
    if ($State -eq 'DONE') { $script:Wrote = $true }
}
function Get-Id($Value) { if ($null -eq $Value) { '' } else { $Value.ToString().ToLowerInvariant() } }

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host "Cells       : $(($Cells | ForEach-Object Name) -join ', ')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$attrs = @{}
foreach ($cell in $Cells) {
    $a = try { Invoke-DvGet "EntityDefinitions(LogicalName='$($cell.Table)')/Attributes(LogicalName='$($cell.Column)')?`$select=IsSecured,CanBeSecuredForCreate,CanBeSecuredForRead,CanBeSecuredForUpdate" } catch { $null }
    if (-not $a) { Report 'FAIL' "(p0) $($cell.Name) does not exist in this environment"; continue }
    $attrs[$cell.Name] = $a
    if ($a.CanBeSecuredForCreate -and $a.CanBeSecuredForRead -and $a.CanBeSecuredForUpdate) { Report 'OK' "(p0) $($cell.Name) exists and can be secured" }
    else { Report 'FAIL' "(p0) $($cell.Name) cannot be secured for read, create and update" }
}

function Get-Profile([string]$Name) {
    @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'" +
        '&$expand=teamprofiles_association($select=teamid,name),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
}
$readers = @(Get-Profile $ReaderProfileName)       # @(): a function returning one row unrolls it
$bffWriters = @(Get-Profile $BffWriterProfileName)
$reader = $null; $bffWriter = $null
foreach ($pair in @(@{ Rows = $readers; Name = $ReaderProfileName }, @{ Rows = $bffWriters; Name = $BffWriterProfileName })) {
    if ($pair.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($pair.Name)' exists" }
    else { Report 'FAIL' "(p1) $($pair.Rows.Count) profile(s) named '$($pair.Name)' - exactly one is required; scripts/Set-RecordCreatorPersonSchema.ps1 creates it (this script never does)" }
}
if ($readers.Count -eq 1) { $reader = $readers[0] }
if ($bffWriters.Count -eq 1) { $bffWriter = $bffWriters[0] }

$readerTeamIds = @()
if ($reader) {
    $readerTeamIds = @($reader.teamprofiles_association | ForEach-Object { Get-Id $_.teamid })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read - the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($readerTeamIds -contains (Get-Id $t.teamid)) { Report 'OK' "(p2) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p2) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile - its users would read the core pair EMPTY. Adding it is provisioning's step (Set-RecordCreatorPersonSchema.ps1 -Apply); this script never makes it" }
    }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname,applicationid&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p3) no application user for the BFF appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($bffWriter) {
    $members = @($bffWriter.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { Get-Id $_.systemuserid })
    $allowed = @($bffUsers | ForEach-Object { Get-Id $_.systemuserid })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains (Get-Id $u.systemuserid)) { Report 'OK' "(p3) BFF app user '$($u.fullname)' is on '$BffWriterProfileName'" }
        else { Report 'FAIL' "(p3) BFF app user '$($u.fullname)' is NOT on '$BffWriterProfileName' - membership is unified-access-control-r2's (Set-RecordCreatorPersonSchema.ps1 -Apply)" }
    }
    foreach ($m in $members | Where-Object { (Get-Id $_.systemuserid) -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p3) '$BffWriterProfileName' member '$($m.fullname)' is $kind - it could write the core pair"
    }
    foreach ($tm in @($bffWriter.teamprofiles_association)) { Report 'FAIL' "(p3) '$BffWriterProfileName' has TEAM '$($tm.name)' - every member could write the core pair" }
}

$ontologyWriter = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname,applicationid&`$filter=applicationid eq $OntologyWriterApplicationId").value) | Select-Object -First 1
if ($ontologyWriter) { Report 'OK' "(p4) ontology writer application user '$($ontologyWriter.fullname)'" }
else { Report 'FAIL' "(p4) no application user for the ontology writer appId $OntologyWriterApplicationId in this environment" }

$setOf = @{}
$sampleOf = @{}
foreach ($table in $Tables) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$table')?`$select=EntitySetName").EntitySetName
    $setOf[$table] = $set
    # Get-AllRows returns ONE array (unary comma); never wrap it in @() - that would make a one-element array of the array.
    $both = Get-AllRows "$set`?`$select=$($table)id&`$filter=_$($TypeColumn)_value ne null and sprk_corerecordid ne null"
    $typeOnly = (Get-AllRows "$set`?`$select=$($table)id&`$filter=_$($TypeColumn)_value ne null and sprk_corerecordid eq null").Count
    $idOnly = (Get-AllRows "$set`?`$select=$($table)id&`$filter=_$($TypeColumn)_value eq null and sprk_corerecordid ne null").Count
    if ($both.Count -gt 0) { $sampleOf[$table] = $both[0]."$($table)id" }
    Report 'INFO' "(p5) $table : $($both.Count) row(s) hold both halves, $typeOnly the type only, $idOnly the id only (securing a column does not change what was written)"
}

if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) THE CORE PAIR WRITER PROFILE AND ITS ONE MEMBER ─────────────────────────────────────────────────────
Write-Host "`n(a) $WriterProfileName"
$writer = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$WriterProfileName'").value)
$writerId = $null
if ($writer.Count -gt 1) { Report 'FAIL' "$($writer.Count) profiles named '$WriterProfileName' - exactly one is allowed" }
elseif ($writer.Count -eq 1) {
    $writerId = Get-Id $writer[0].fieldsecurityprofileid
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
    if ($members | Where-Object { (Get-Id $_.systemuserid) -eq (Get-Id $ontologyWriter.systemuserid) }) { Report 'OK' "member: $label" }
    elseif ($Verify) { Report 'MISSING' "member: $label" }
    elseif ($IsDryRun -or -not $writerId) { Report 'WOULD' "add $label to $WriterProfileName" }
    else {
        Invoke-DvWrite POST "fieldsecurityprofiles($writerId)/systemuserprofiles_association/`$ref" @{ '@odata.id' = "$Api/systemusers($($ontologyWriter.systemuserid))" } | Out-Null
        Report 'DONE' "added $label to $WriterProfileName"
    }
}

# The membership IS the lock. Reported, never removed.
if ($writerId) {
    $allowedWriter = if ($ontologyWriter) { Get-Id $ontologyWriter.systemuserid } else { '' }
    $users = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    $strangers = @($users | Where-Object { (Get-Id $_.systemuserid) -ne $allowedWriter })
    foreach ($m in $strangers) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) other than the ontology writer" } else { 'a HUMAN user' }
        Report 'FAIL' "$WriterProfileName member '$($m.fullname)' is $kind - only the ontology writer may be on it"
    }
    $teams = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $teams) { Report 'FAIL' "$WriterProfileName has TEAM '$($tm.name)' - every member could write the core pair" }
    if ($strangers.Count -eq 0 -and $teams.Count -eq 0) { Report 'OK' "$WriterProfileName has no member besides the ontology writer" }

    $stray = @((Invoke-DvGet "fieldpermissions?`$select=entityname,attributelogicalname&`$filter=_fieldsecurityprofileid_value eq $writerId").value |
        Where-Object { "$($_.entityname).$($_.attributelogicalname)" -notin @($Cells | ForEach-Object Name) })
    foreach ($r in $stray) { Report 'FAIL' "$WriterProfileName also grants $($r.entityname).$($r.attributelogicalname) - it may hold the four core pair cells only" }
}

# Fail closed: never grant the writer profile Create/Update while it has the wrong id, a stranger, a team or a stray grant.
if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: '$WriterProfileName' is not as SpaarkeMaster declares it ($($gaps.Count) gap(s)); no column was secured." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (b) SECURE, then grant at once ──────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Field-level security"
function Get-Permission([string]$ProfileId, $Cell) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$($Cell.Table)' and attributelogicalname eq '$($Cell.Column)'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, $Cell, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Cell.Table; attributelogicalname = $Cell.Column
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $($Cell.Name) not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured($Cell, [bool]$Value) {
    # GET-then-PUT of the typed attribute definition (scripts/Set-RegardingPairFieldSecurity.ps1).
    $attrPath = "EntityDefinitions(LogicalName='$($Cell.Table)')/Attributes(LogicalName='$($Cell.Column)')"
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
function Publish-Tables {
    $entities = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
}
# The reader first: the window in which a secured column has no reader permission is the shortest it can be.
$specs = @(
    @{ Id = (Get-Id $reader.fieldsecurityprofileid); Name = $ReaderProfileName; Create = 0 },
    @{ Id = (Get-Id $bffWriter.fieldsecurityprofileid); Name = $BffWriterProfileName; Create = 4 },
    @{ Id = $writerId; Name = $WriterProfileName; Create = 4 })
function Grant-MissingPermissions($Cell) {
    foreach ($s in $specs) {
        if (-not (Get-Permission $s.Id $Cell)) { Grant-Permission $s.Id $Cell $s.Create; Report 'DONE' "granted $($s.Name) on $($Cell.Name) (read=4 create=$($s.Create) update=$($s.Create))" }
    }
}
function Write-MaskedRecovery($Cell, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $($Cell.Name) may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    Write-Host '  Every non-administrator then reads it EMPTY.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now: (1) fix the cause and re-run -Apply (on an already secured column it grants only what is' -ForegroundColor Red
    Write-Host "  missing), then -Verify; (2) or clear 'Enable column security' on $($Cell.Name) and publish $($Cell.Table)." -ForegroundColor Red
}

foreach ($cell in $Cells) {
    $attr = $attrs[$cell.Name]
    if (-not $attr) { continue }   # (p0) reported it; -Apply was refused above
    if ($attr.IsSecured -eq $Secured) {
        Report 'OK' "$($cell.Name) is field-secured"
        if ($Apply) {
            try { Grant-MissingPermissions $cell }
            catch { Write-MaskedRecovery $cell "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
        }
    }
    elseif ($Verify) { Report 'MISSING' "$($cell.Name) is NOT field-secured - a path outside the server could set the core pair" }
    elseif ($IsDryRun -or -not $writerId) { Report 'WOULD' "secure $($cell.Name), then at once grant the reader (read) and both writer profiles (read/create/update)" }
    else {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        Set-ColumnSecured $cell $Secured
        Report 'DONE' "secured $($cell.Name)"
        try {
            Grant-MissingPermissions $cell
        } catch {
            $grantError = $_
            Write-Host "  FAIL     granting the profiles on $($cell.Name) failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
            Write-Host "           reverting $($cell.Name) to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $cell (-not $Secured)
                Publish-Tables
                $back = (Invoke-DvGet "EntityDefinitions(LogicalName='$($cell.Table)')/Attributes(LogicalName='$($cell.Column)')?`$select=IsSecured").IsSecured
                if ($back -eq $Secured) { throw "the revert was accepted but $($cell.Name) still reads IsSecured=true" }
                Write-Host "  REVERTED $($cell.Name) is NOT field-secured again. Fix the cause, then re-run -Apply." -ForegroundColor Yellow
            } catch {
                Write-MaskedRecovery $cell "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
            }
            throw $grantError
        }
        $clock.Stop()
        Report 'INFO' ("$($cell.Name): masked window {0:N1}s (secure -> reader grant)" -f $clock.Elapsed.TotalSeconds)
    }

    foreach ($s in $specs) {
        if (-not $s.Id) {
            if ($IsDryRun -and $s.Name -eq $WriterProfileName) { Report 'WOULD' "grant $($s.Name) on $($cell.Name) (read=4 create=4 update=4)" }
            continue
        }
        $perm = Get-Permission $s.Id $cell
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) on $($cell.Name) (read=4 create=$($s.Create) update=$($s.Create))" }
            else { Report 'FAIL' "$($s.Name) on $($cell.Name) is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
        }
        elseif ($Verify) { Report 'MISSING' "$($s.Name) on $($cell.Name)" }
        elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $($cell.Name)" }
        else { Report 'FAIL' "$($s.Name) on $($cell.Name) is still missing after -Apply granted it" }
    }
}

# ── (c) every OTHER profile ─────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Other profiles"
$known = @($ReaderProfileName, $BffWriterProfileName, $WriterProfileName, $PlatformProfileName)
$others = 0
foreach ($cell in $Cells) {
    $rows = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$($cell.Table)' and attributelogicalname eq '$($cell.Column)'").value |
        Where-Object { $_.fieldsecurityprofileid.name -notin $known -and ($_.canread -eq 4 -or $_.cancreate -eq 4 -or $_.canupdate -eq 4) })
    foreach ($o in $rows) {
        $others++
        Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' also grants $($cell.Name) (read=$($o.canread) create=$($o.cancreate) update=$($o.canupdate)) - only the three named profiles may"
    }
}
if ($others -eq 0) { Report 'OK' "no profile besides the reader, the two writer profiles and System Administrator grants any core pair cell" }
$sysAdminIds = @()
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=systemuserid,fullname,applicationid,isdisabled").value)) {
        $sysAdminIds += Get-Id $u.systemuserid
        if (-not $u.isdisabled -and $u.applicationid) { Report 'INFO' "System Administrator application user '$($u.fullname)' (full field access by platform rule)" }
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
if ($Apply -and $script:Wrote) {
    Publish-Tables
    Write-Host "`nPublished $($Tables -join ' and ')." -ForegroundColor Green
}
elseif ($Apply) { Write-Host "`nNothing written; no publish." -ForegroundColor Green }

# ── (r) THE SERVER IDENTITIES STILL READ THE PAIR ───────────────────────────────────────────────────────────
# Their app-only readers (SignalReadService, the Signal jobs, SignalSuppressionService, DecisionCommitService, ...) get a null
# pair with no error when they cannot read it, so this is checked, not assumed.
Write-Host "`n(r) The server identities read the core pair"
$identities = @($bffUsers | ForEach-Object { @{ User = $_; Kind = 'BFF app user' } })
if ($ontologyWriter) { $identities += @{ User = $ontologyWriter; Kind = 'ontology writer' } }
$writerRead = @{}
foreach ($s in $specs | Where-Object { $_.Id }) {
    $writerRead[$s.Id] = @{}
    foreach ($cell in $Cells) { $p = Get-Permission $s.Id $cell; $writerRead[$s.Id][$cell.Name] = [bool]($p -and $p.canread -eq 4) }
}
foreach ($i in $identities) {
    $uid = Get-Id $i.User.systemuserid
    $label = "$($i.Kind) '$($i.User.fullname)'"
    # (r1) structurally: System Administrator, or a profile reached directly or through a team with read=4 on every cell.
    $viaProfiles = @()
    if ($bffWriter -and (@($bffWriter.systemuserprofiles_association | ForEach-Object { Get-Id $_.systemuserid }) -contains $uid)) { $viaProfiles += $specs[1].Id }
    if ($writerId -and $ontologyWriter -and $uid -eq (Get-Id $ontologyWriter.systemuserid)) {
        $onCore = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid").value | ForEach-Object { Get-Id $_.systemuserid })
        if ($onCore -contains $uid) { $viaProfiles += $writerId }
    }
    $myTeams = @((Invoke-DvGet "systemusers($uid)/teammembership_association?`$select=teamid").value | ForEach-Object { Get-Id $_.teamid })
    if ($reader -and @($myTeams | Where-Object { $readerTeamIds -contains $_ }).Count -gt 0) { $viaProfiles += $specs[0].Id }
    if ($sysAdminIds -contains $uid) { Report 'OK' "(r1) $label holds System Administrator" }
    else {
        $unread = @($Cells | Where-Object { $c = $_; -not ($viaProfiles | Where-Object { $writerRead[$_] -and $writerRead[$_][$c.Name] }) } | ForEach-Object Name)
        if ($unread.Count -eq 0) { Report 'OK' "(r1) $label reads every cell through its field security profiles" }
        elseif ($IsDryRun) { Report 'WOULD' "(r1) $label reads $($unread -join ', ') once the grants above are made" }
        else { Report 'FAIL' "(r1) $label holds neither System Administrator nor a profile with read=4 on $($unread -join ', ') - its app-only reads of the pair come back empty" }
    }
}
# (r2) live, AS each identity. The control first: a made-up caller id must be refused, else the header is not honoured.
$control = try { Invoke-DvGet "$($setOf[$Tables[0]])?`$select=$($Tables[0])id&`$top=1" @{ MSCRMCallerID = '00000000-0000-0000-0000-0000000044ff' }; $true } catch { $false }
if ($control) { Report 'FAIL' '(r2) a read as a made-up caller id was accepted - impersonation is not honoured, so no read below proves anything' }
else {
    foreach ($table in $Tables) {
        $rowId = $sampleOf[$table]
        if (-not $rowId) { Report 'INFO' "(r2) no $table row holds both halves; nothing to read back - (r1) stands"; continue }
        foreach ($i in $identities) {
            $label = "$($i.Kind) '$($i.User.fullname)'"
            try {
                $row = Invoke-DvGet "$($setOf[$table])($rowId)?`$select=_$($TypeColumn)_value,sprk_corerecordid" @{ MSCRMCallerID = $i.User.systemuserid.ToString() }
                if ($row."_$($TypeColumn)_value" -and $row.sprk_corerecordid) { Report 'OK' "(r2) $label reads both halves of $table $rowId" }
                else { Report 'FAIL' "(r2) $label reads $table $rowId with a half missing (type '$($row."_$($TypeColumn)_value")', id '$($row.sprk_corerecordid)') - its app-only reads of the pair come back empty" }
            } catch { Report 'FAIL' "(r2) reading $table $rowId as $label failed: $($_.Exception.Message)" }
        }
    }
}

if ($IsDryRun) {
    if ($gaps.Count -gt 0) { Write-Host "`n$($gaps.Count) FAIL item(s) (a failed precondition stops -Apply before any write):" -ForegroundColor Yellow; $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow } }
    Write-Host "`nDRY RUN complete - nothing was written." -ForegroundColor Cyan; exit 0
}
if ($gaps.Count -eq 0) { Write-Host "`nPASS: the core pair is field-secured on both tables, every business unit's default team reads it, the server identities read it, and only the BFF, the ontology writer and System Administrator can write it." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
