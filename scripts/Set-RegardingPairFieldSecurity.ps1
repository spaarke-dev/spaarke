#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks a project's ADR-024 regarding pair (sprk_regardingrecordid + sprk_regardingrecordtype on sprk_project) with
    field-level security, so ONLY the BFF can set or change what a project is filed under, while EVERY user still reads
    it (#1616). Dry run by default; -Apply writes; -Verify checks.

.DESCRIPTION
    WHY (#1616). For sprk_project the pair is the ONLY filing column: no typed lookup names the matter or project it is
    filed under, so Dataverse checks no AppendTo when the pair is written. Any user with Write on a project could set it
    through the Web API (or the playbook's impersonated update, UpdateRecordActionCore) and the access readers
    (SecureRootInheritanceJob, RecordContainerResolver) would then isolate the project under a SECURE parent it was never
    allowed to be filed under — share it to that parent's sharees, move its storage, and reveal that the parent is secure.
    Securing the two columns makes every such write a native 403; the BFF writes them (chat create, the update tool,
    Office quick-create) only after its own AppendTo check AS THE CALLER (OwnedChildWrite).

    A work assignment's pair is NOT locked: the Create Work Assignment wizard writes it as the user through Xrm.WebApi.
    A work assignment has typed filing lookups (sprk_regardingmatter, sprk_regardingproject) that Dataverse checks
    natively, and the readers honour its pair only when one of them repeats it (SecureRootInheritance.PairNamesAParentOnItsOwn).

    It REUSES task 133's two profiles — it never creates or edits their membership:
      "Spaarke BFF-Managed Field Readers"  Read; on EVERY business unit's default team (every user keeps reading).
      "Spaarke BFF-Managed Field Writers"  Read + Create + Update; the BFF application user(s) ONLY.
    Both, and their members, are created and maintained by scripts/Set-RecordCreatorPersonSchema.ps1.

    PRECONDITIONS (checked in every mode; -Apply refuses unless all pass):
      (p1) both profiles exist and are in -SolutionUniqueName;
      (p2) the reader profile is on EVERY business unit's default team;
      (p3) the writer profile's members are exactly the -BffApplicationIds application users (no human, no team);
      (p4) informational: how many projects carry a pair now (scripts/Repair-RegardingPairFiling.ps1 decides whether
           each one was filed by someone allowed to; run its -Apply AFTER this script's — securing a column does not
           clear what was written before, so only a repair after the lock catches a pair PATCHed up to the lock — and
           its -Verify last, as the gate);
      (p5) -BffWritesProjectPair is passed: the BFF with #1616 is deployed. Before it, the chat create refuses a
           project with a pair (the column reads as field-secured) and the update tool's own PATCH of it is refused —
           both fail closed, but they fail;
      (p6) no sprk_fieldmappingrule (whose profile targets sprk_project), sprk_aitopicregistry or sprk_emailupdatefield
           row targets a pair column: maker-authored configuration that writes the column as a user would be refused.

    STEPS (-Apply), per column, in this order:
      (a) secure the column (IsSecured = true);
      (b) IMMEDIATELY grant the reader profile read=4, then the writer profile read=4 create=4 update=4 (12 retries on
          0x8004f508, as Set-SecureFlagFieldSecurity.ps1). If a grant fails after (a), the column is REVERTED to not
          secured and published before the script stops; if the revert fails too it prints "RECOVERY REQUIRED NOW".
          Re-running -Apply is the resume path: on a column already secured it grants only the missing permissions.
          The masked window (secured, no reader grant yet) is measured and printed; during it a non-administrator reads
          the pair EMPTY — the BFF (System Administrator + writer) is unaffected, and an empty pair files nothing.
      (c) report any OTHER profile that can create or update the column (FAIL); list System Administrator holders.
      (d) publish sprk_project.

    LIVE ORDER: deploy the BFF (#1616) → THIS SCRIPT -Apply (the LOCK) → THIS SCRIPT -Verify →
    Repair-RegardingPairFiling.ps1 -Apply → Repair-RegardingPairFiling.ps1 -Verify (the gate: nothing written before the
    lock survives it) → import of SpaarkeMaster carrying the same IsSecured and field permissions
    (src/dataverse/solutions/SpaarkeMaster).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: 5967251e-171c-46fe-a6c2-ef843c90309d and
    1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for every mode: (p3) checks them.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles. Default SpaarkeCore.

.PARAMETER BffWritesProjectPair
    The operator's confirmation for (p5). Required with -Apply.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-RegardingPairFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every precondition and step's state; zero writes.

.EXAMPLE
    .\Set-RegardingPairFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -BffWritesProjectPair -Apply

.NOTES
    #1616 (unified-access-control-r2). Pinned against the BFF and the SpaarkeMaster solution XML by
    RegardingPairFieldSecurityScriptAgreementTests (table, columns, both profile names). Auth: the operator's own az
    CLI identity (System Administrator in the environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$BffWritesProjectPair,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by RegardingPairFieldSecurityScriptAgreementTests) ─────────────────────────────────────
$Table = 'sprk_project'
$Columns = @('sprk_regardingrecordid', 'sprk_regardingrecordtype')
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
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

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
function Get-Profile([string]$Name) {
    @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'" +
        '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
}
$readers = Get-Profile $ReaderProfileName
$writers = Get-Profile $WriterProfileName
$reader = $null; $writer = $null
foreach ($p in @(@{ Rows = $readers; Name = $ReaderProfileName }, @{ Rows = $writers; Name = $WriterProfileName })) {
    if ($p.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($p.Name)' exists" }
    else { Report 'FAIL' "(p1) $($p.Rows.Count) profile(s) named '$($p.Name)' — exactly one is required; create it with scripts/Set-RecordCreatorPersonSchema.ps1 -Apply (this script never creates it)" }
}
if ($readers.Count -eq 1) { $reader = $readers[0] }
if ($writers.Count -eq 1) { $writer = $writers[0] }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    # A field security profile is a ROOT component: only its own solutioncomponents row ('Direct') counts.
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @($reader, $writer) | Where-Object { $_ }) {
        if (Test-DvInSolution -Membership $membership -ComponentId $p.fieldsecurityprofileid) { Report 'OK' "(p1) '$($p.name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "(p1) '$($p.name)' is not in $SolutionUniqueName — Set-RecordCreatorPersonSchema.ps1 -Apply adds it" }
    }
}

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read — the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p2) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile — its users would read a project's pair EMPTY; run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p3) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p3) BFF app user '$($u.fullname)' is a writer (explicitly)" }
        else { Report 'FAIL' "(p3) BFF app user '$($u.fullname)' is NOT on the writer profile — the BFF could not write a project's pair wherever it lacks System Administrator; run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p3) writer profile member '$($m.fullname)' is $kind — it could re-file any project"
    }
    foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "(p3) writer profile has TEAM '$($tm.name)' — every member could re-file any project" }
}

$tableSet = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName").EntitySetName
$withPair = @((Invoke-DvGet "$tableSet`?`$select=$($Table)id&`$filter=$($Columns[0]) ne null").value).Count
Report 'INFO' "(p4) $withPair $Table row(s) carry a pair — scripts/Repair-RegardingPairFiling.ps1 decides each (run its -Apply AFTER this lock, then its -Verify)"

if ($BffWritesProjectPair) { Report 'OK' '(p5) operator confirms the BFF with #1616 (app-only pair write after the caller check) is deployed' }
elseif ($Apply) { Report 'FAIL' "(p5) -BffWritesProjectPair not passed: before that BFF, every chat create or update of a project's pair is refused" }
else { Report 'INFO' '(p5) -BffWritesProjectPair is required for -Apply' }

# (p6) maker-authored configuration that writes a pair column of the project as a user would.
$configuredWriters = 0
$typeTables = @{}
function Get-RecordTypeTable([string]$Id) {
    if (-not $Id) { return $null }
    if (-not $typeTables.ContainsKey($Id)) { $typeTables[$Id] = (Invoke-DvGet "sprk_recordtype_refs($Id)?`$select=sprk_recordlogicalname").sprk_recordlogicalname }
    $typeTables[$Id]
}
$columnFilter = ($Columns | ForEach-Object { "sprk_targetfield eq '$_'" }) -join ' or '
foreach ($rule in @((Invoke-DvGet "sprk_fieldmappingrules?`$select=sprk_fieldmappingruleid,sprk_targetfield&`$filter=$columnFilter&`$expand=sprk_FieldMappingProfile(`$select=_sprk_targetrecordtype_value)").value)) {
    $target = Get-RecordTypeTable $rule.sprk_FieldMappingProfile._sprk_targetrecordtype_value
    if ($target -eq $Table -or -not $target) {
        $configuredWriters++
        Report 'FAIL' "(p6) sprk_fieldmappingrule $($rule.sprk_fieldmappingruleid) targets $($rule.sprk_targetfield) on $(if ($target) { $target } else { 'an unresolved table' }) — remove or retarget it"
    }
}
foreach ($ch in @(@{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    $filter = ($Columns | ForEach-Object { "$($ch.C) eq '$_'" }) -join ' or '
    foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$filter").value)) {
        $configuredWriters++
        Report 'FAIL' "(p6) $($ch.T) row $($row."$($ch.T)id") targets a regarding pair column — remove or retarget it"
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p6) no field-mapping rule onto $Table, AI topic-registry row or email update field targets the pair" }

if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a)+(b) SECURE, then grant at once ──────────────────────────────────────────────────────────────────────
Write-Host "`n(a)+(b) Field-level security"
function Get-Permission([string]$ProfileId, [string]$Column) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [string]$Column, [int]$Create) {
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
function Set-ColumnSecured([string]$Column, [bool]$Value) {
    # GET-then-PUT of the typed attribute definition — proven live on string and lookup columns (tasks 141/133).
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
function Grant-MissingPermissions([string]$Column, $Specs) {
    foreach ($s in $Specs) {
        if (-not (Get-Permission $s.P.fieldsecurityprofileid $Column)) { Grant-Permission $s.P.fieldsecurityprofileid $Column $s.Create }
    }
}
function Write-MaskedRecovery([string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW — $Table.$Column may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    Write-Host "  Every non-administrator then reads it EMPTY (a project's filing looks like none)." -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script with -Apply: on an already-secured column it grants only the missing' -ForegroundColor Red
    Write-Host '        profile permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}
foreach ($c in $Columns) {
    $attr = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')?`$select=IsSecured"
    $specs = @(@{ P = $reader; Name = $ReaderProfileName; Create = 0 }, @{ P = $writer; Name = $WriterProfileName; Create = 4 })

    if ($attr.IsSecured -eq $Secured) {
        Report 'OK' "$Table.$c is field-secured"
        if ($Apply) {
            try { Grant-MissingPermissions $c $specs }
            catch { Write-MaskedRecovery $c "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
        }
    }
    elseif ($Verify) { Report 'MISSING' "$Table.$c is NOT field-secured — any user with Write can change what a project is filed under" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$c, then at once grant the reader (read) and writer (read/create/update) profiles" }
    else {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        Set-ColumnSecured $c $Secured
        try {
            Grant-MissingPermissions $c $specs
        } catch {
            $grantError = $_
            Write-Host "  FAIL     granting the profiles on $Table.$c failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
            Write-Host "           reverting $Table.$c to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $c (-not $Secured)
                Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
                $back = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')?`$select=IsSecured").IsSecured
                if ($back -eq $Secured) { throw "the revert was accepted but $Table.$c still reads IsSecured=true" }
                Write-Host "  REVERTED $Table.$c is NOT field-secured again. Fix the cause, then re-run -Apply." -ForegroundColor Yellow
            } catch {
                Write-MaskedRecovery $c "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
            }
            throw $grantError
        }
        $clock.Stop()
        Report 'DONE' ("secured $Table.$c and granted both profiles — masked window {0:N1}s (secure → reader grant)" -f $clock.Elapsed.TotalSeconds)
    }

    foreach ($s in $specs) {
        if (-not $s.P) { continue }
        $perm = Get-Permission $s.P.fieldsecurityprofileid $c
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) on $Table.$c (read=4 create=$($s.Create) update=$($s.Create))" }
            else { Report 'FAIL' "$($s.Name) on $Table.$c is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
        } elseif ($Verify) { Report 'MISSING' "$($s.Name) on $Table.$c" }
        elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $Table.$c" }
        else { Report 'FAIL' "$($s.Name) on $Table.$c is still missing after -Apply granted it" }
    }

    # (c) every OTHER writer
    $otherWriters = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$c' and (canupdate eq 4 or cancreate eq 4)").value |
        Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
    foreach ($o in $otherWriters) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $Table.$c — only the BFF may" }
}

Write-Host "`nInformational: System Administrator role holders (full field access by platform rule)"
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("user '{0}'{1}" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "TEAM '$($tm.name)' — every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: a project's regarding pair is locked, every business unit's default team reads it, and only the BFF (and System Administrator) can write it." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
