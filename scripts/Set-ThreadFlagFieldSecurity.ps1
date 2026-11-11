#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks the five columns that decide who may read a message with field-level security, so ONLY the BFF writes them while
    EVERY user still reads their true values (field-mapping-server-write-path-r1 task 054b):
      sprk_communicationthread : sprk_threadtype, sprk_isdefaultthread
      sprk_communication       : sprk_sentby, sprk_directrecipient, sprk_communicationthread
    Dry run by default; -Apply writes; -Verify checks (exit 0 = pass, 1 = a gap).

.DESCRIPTION
    The BFF already refuses all five on every generic write path, for every identity
    (InheritedAccessPermission.BffOwnedCommunicationColumns; CommunicationBffOwnedColumnRefusalTests reads THIS script's
    target lines and keeps them equal to that set). This script closes the paths outside the BFF: a user's own Dataverse
    write (a form, the Web API, an import, a flow running as the user).

    It REUSES task 133's two profiles; it never creates them or edits their membership:
      "Spaarke BFF-Managed Field Readers"  Read; on EVERY business unit's default team (every user keeps reading).
      "Spaarke BFF-Managed Field Writers"  Read + Create + Update; the BFF application user(s) ONLY.
    Both, and their members, are maintained by scripts/Set-RecordCreatorPersonSchema.ps1. Securing a column before every
    reader holds Read masks it: Dataverse then returns it EMPTY, with no error, and a Direct message's participants and
    thread would read as unset.

    PRECONDITIONS (checked in every mode; -Apply refuses unless all pass):
      (p1) both profiles exist and are in -SolutionUniqueName;
      (p2) the reader profile is on EVERY business unit's default team;
      (p3) the writer profile's members are exactly the -BffApplicationIds application users (no human, no team);
      (p4) no system view (savedquery) or personal view (userquery) FILTERS on a target column of its table;
      (p5) no form other than a main form shows a target column; every control on a main form is LISTED (-Apply removes them);
      (p6) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets a target column name (each is
           configuration that writes a column outside the BFF's typed writers).
    INFORMATIONAL: rows whose target column is empty (never a gap: a thread's flags and a message's participants are blank on
    many rows by design).

    STEPS (-Apply), in this order:
      (a) remove every listed control from the main forms (the whole cell), and publish those tables;
      (b) per column: secure it (IsSecured = true), then IMMEDIATELY grant the reader profile read=4 and the writer profile
          read=4 create=4 update=4. If a grant fails after (a), the column is reverted to NOT secured (and published) before
          the run stops; if the revert fails too, "RECOVERY REQUIRED NOW" names the two remedies. Re-running -Apply is the
          resume path: on a column already secured it grants only the missing permissions;
      (c) report any OTHER profile that can create or update a column (FAIL). The System Administrator profile has full
          access by platform rule and cannot be narrowed; its role holders are LISTED (informational);
      (d) publish both tables.

    BINDING RULE (agreed with unified-access-control-r2, 2026-11-11): the BFF settings DirectMessage:BusinessUnitName and
    DirectMessage:OwnerTeamName stay UNSET on an environment until THIS script's -Verify exits 0 there. While they are unset,
    DirectMessageOwnerTeam refuses as NotConfigured, so no message is owned by the Direct team and nothing grants from
    sprk_sentby / sprk_directrecipient. -Verify prints whether the precondition is met.

    LIVE ORDER: dev BFF deployed from master -> THIS SCRIPT (-Apply, then -Verify) -> Direct setup (UAC-r2: the
    DirectMessage:* settings) -> 054c Stamp -> piece 3 -> 054c Move.

    MANUAL CHECKS after -Verify passes (they need a non-administrator sign-in, so the script does not run them):
      (m1) As a non-administrator, PATCH a communication's sprk_sentby, sprk_directrecipient or sprk_communicationthread
           through the Web API: refused (field security). Repeat on a message the user can write through a record team.
      (m2) As a non-administrator, PATCH a thread's sprk_threadtype or sprk_isdefaultthread: refused.
      (m3) As a non-administrator, read a Direct message and its thread: the five columns show their true values, not empty.
      (m4) The BFF still creates a per-user master thread and a Direct thread (send a Direct message; open the Assistant's
           master thread), and files the message into it (the thread lookup and both participants are set).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: the SDAP-BFF-SPE-API registration and the
    mi-bff-api-dev managed identity). Required for every mode: they are what (p3) checks. The per-customer BFF
    registration is an environment-setup input, never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-ThreadFlagFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds <id1>,<id2>
    Dry run: every precondition and step's state; zero writes.

.EXAMPLE
    .\Set-ThreadFlagFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds <id1>,<id2> -Apply

.EXAMPLE
    .\Set-ThreadFlagFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds <id1>,<id2> -Verify

.NOTES
    field-mapping-server-write-path-r1 task 054b. Modelled on scripts/Set-SecureFlagFieldSecurity.ps1 (same profiles, same
    secure-then-grant-with-revert shape). Notes: projects/field-mapping-server-write-path-r1/notes/task-054-notes.md, "054b".
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
# `pwsh -File` passes "a,b" as ONE string; accept both shapes.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the target lines are read by CommunicationBffOwnedColumnRefusalTests; keep one target per line) ─────
$Targets = @(
    @{ Table = 'sprk_communicationthread'; Columns = @('sprk_threadtype', 'sprk_isdefaultthread') }
    @{ Table = 'sprk_communication'; Columns = @('sprk_sentby', 'sprk_directrecipient', 'sprk_communicationthread') }
)
$Lookups = @('sprk_sentby', 'sprk_directrecipient', 'sprk_communicationthread')
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$MainFormType = 2

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
    Prefer             = 'odata.maxpagesize=500'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvGetAll([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $page = Invoke-DvGet $Path
    while ($true) {
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
        if (-not $next) { break }
        $page = Invoke-RestMethod -Uri $next -Headers $headers -Method Get
    }
    , $rows.ToArray()
}
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Publish-Tables([string[]]$Tables) {
    $entities = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
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
Write-Host ("Columns     : {0}" -f (($Targets | ForEach-Object { $t = $_.Table; $_.Columns | ForEach-Object { "$t.$_" } }) -join ', '))

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
function Get-Profile([string]$Name) {
    @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'" +
        '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
}
$readers = Get-Profile $ReaderProfileName
$writers = Get-Profile $WriterProfileName
$reader = $null; $writer = $null
foreach ($pair in @(@{ Rows = $readers; Name = $ReaderProfileName }, @{ Rows = $writers; Name = $WriterProfileName })) {
    if ($pair.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($pair.Name)' exists" }
    else { Report 'FAIL' "(p1) $($pair.Rows.Count) profile(s) named '$($pair.Name)'; exactly one is required. Create it with scripts/Set-RecordCreatorPersonSchema.ps1 -Apply (this script never creates it)" }
}
if ($readers.Count -eq 1) { $reader = $readers[0] }
if ($writers.Count -eq 1) { $writer = $writers[0] }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    # Membership through the ONE shared helper (scripts/common/DataverseSolutionMembership.ps1). A field security profile is
    # a root component, so no TableMetadataId is passed.
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @($reader, $writer) | Where-Object { $_ }) {
        if (Test-DvInSolution -Membership $membership -ComponentId $p.fieldsecurityprofileid) { Report 'OK' "(p1) '$($p.name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "(p1) '$($p.name)' is not in $SolutionUniqueName; Set-RecordCreatorPersonSchema.ps1 -Apply adds it" }
    }
}

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read; the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p2) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile; its users would read the columns EMPTY. Run Set-RecordCreatorPersonSchema.ps1 -Apply" }
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
        else { Report 'FAIL' "(p3) BFF app user '$($u.fullname)' is NOT on the writer profile; the BFF could not set the columns wherever it lacks System Administrator. Run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p3) writer profile member '$($m.fullname)' is $kind; it could set the columns"
    }
    foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "(p3) writer profile has TEAM '$($tm.name)'; every member could set the columns" }
}

# (p4) No view filters on a target column of its own table. A <condition attribute="col"> counts when its entity (its own
# entityname, else the nearest <entity> or <link-entity> above it) is the column's table.
function Get-FilteredColumns([string]$FetchXml, [string]$Table, [string[]]$Columns) {
    if ([string]::IsNullOrWhiteSpace($FetchXml)) { return @() }
    try { [xml]$doc = $FetchXml } catch { return @("(unparsable fetchxml)") }
    $hits = @()
    foreach ($c in @($doc.SelectNodes('//condition'))) {
        $attr = $c.GetAttribute('attribute')
        if ($Columns -notcontains $attr) { continue }
        $owner = $c.GetAttribute('entityname')
        if (-not $owner) {
            $n = $c.ParentNode
            while ($n -and $n.LocalName -notin 'entity', 'link-entity') { $n = $n.ParentNode }
            $owner = if ($n) { $n.GetAttribute('name') } else { '' }
        }
        if ($owner -eq $Table -or ($owner -and $doc.SelectSingleNode("//*[@alias='$owner' and @name='$Table']"))) { $hits += $attr }
    }
    , @($hits | Select-Object -Unique)
}
$viewFilters = 0
foreach ($target in $Targets) {
    foreach ($col in $target.Columns) {
        foreach ($kind in @(@{ Set = 'savedqueries'; Id = 'savedqueryid'; Label = 'system view' }, @{ Set = 'userqueries'; Id = 'userqueryid'; Label = 'personal view' })) {
            $rows = Invoke-DvGetAll "$($kind.Set)?`$select=$($kind.Id),name,fetchxml&`$filter=contains(fetchxml,'$col')"
            foreach ($v in $rows) {
                if ((Get-FilteredColumns $v.fetchxml $target.Table @($col)).Count -gt 0) {
                    $viewFilters++
                    Report 'FAIL' "(p4) $($kind.Label) '$($v.name)' ($($v."$($kind.Id)")) filters on $($target.Table).$col; remove the condition first"
                }
            }
        }
    }
}
if ($viewFilters -eq 0) { Report 'OK' '(p4) no system or personal view filters on a target column' }

# (p5) Forms. Only a main form may show a target column; each control on it is listed, and -Apply removes it.
$formControls = [System.Collections.Generic.List[object]]::new()
foreach ($target in $Targets) {
    $forms = Invoke-DvGetAll "systemforms?`$select=formid,name,type,formxml&`$filter=objecttypecode eq '$($target.Table)'"
    foreach ($f in $forms) {
        if ([string]::IsNullOrWhiteSpace($f.formxml)) { continue }
        [xml]$doc = $f.formxml
        foreach ($col in $target.Columns) {
            foreach ($ctl in @($doc.SelectNodes("//control[@datafieldname='$col']"))) {
                $where = "form '$($f.name)' ($($f.formid), type $($f.type)) shows $($target.Table).$col (control '$($ctl.GetAttribute('id'))')"
                if ($f.type -eq $MainFormType) {
                    $formControls.Add([pscustomobject]@{ Table = $target.Table; FormId = $f.formid; Column = $col; Text = $where })
                    Report 'INFO' "(p5) main $where; -Apply removes it"
                } else {
                    Report 'FAIL' "(p5) non-main $where; remove it in the form designer first (this script edits main forms only)"
                }
            }
        }
    }
}
if ($formControls.Count -eq 0) { Report 'OK' '(p5) no main form shows a target column' }

# (p6) No configured writer targets a column name.
$configuredWriters = 0
$allColumns = @($Targets | ForEach-Object { $_.Columns }) | Select-Object -Unique
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($col in $allColumns) {
        foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$col'").value)) {
            $configuredWriters++
            Report 'FAIL' "(p6) $($ch.T) row $($row."$($ch.T)id") targets $col; remove or retarget it (only the BFF's typed writers set it)"
        }
    }
}
if ($configuredWriters -eq 0) { Report 'OK' '(p6) no field-mapping rule, AI topic-registry row or email update field targets a target column' }

# INFORMATIONAL: empty values (never a gap).
foreach ($target in $Targets) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($target.Table)')?`$select=EntitySetName").EntitySetName
    foreach ($col in $target.Columns) {
        $field = if ($Lookups -contains $col) { "_$($col)_value" } else { $col }
        $n = @((Invoke-DvGet "$set`?`$select=$($target.Table)id&`$filter=$field eq null&`$top=1").value).Count
        Report 'INFO' ("{0}.{1}: {2}" -f $target.Table, $col, $(if ($n -gt 0) { 'some rows are empty (expected; not a gap)' } else { 'no empty rows' }))
    }
}

if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) REMOVE the listed main-form controls ────────────────────────────────────────────────────────────────
if ($formControls.Count -gt 0) {
    Write-Host "`n(a) Main-form controls"
    foreach ($group in $formControls | Group-Object FormId) {
        $first = $group.Group[0]
        if ($IsDryRun -or $Verify) { $group.Group | ForEach-Object { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "remove: $($_.Text)" }; continue }
        $form = Invoke-DvGet "systemforms($($first.FormId))?`$select=formxml"
        [xml]$doc = $form.formxml
        foreach ($c in $group.Group) {
            foreach ($ctl in @($doc.SelectNodes("//control[@datafieldname='$($c.Column)']"))) {
                $cell = $ctl.ParentNode
                while ($cell -and $cell.LocalName -ne 'cell') { $cell = $cell.ParentNode }
                if ($cell) { [void]$cell.ParentNode.RemoveChild($cell) } else { [void]$ctl.ParentNode.RemoveChild($ctl) }
            }
        }
        Invoke-DvWrite PATCH "systemforms($($first.FormId))" @{ formxml = $doc.OuterXml } | Out-Null
        $group.Group | ForEach-Object { Report 'DONE' "removed: $($_.Text)" }
    }
    if ($Apply) { Publish-Tables @($formControls | ForEach-Object { $_.Table } | Select-Object -Unique) }
}

# ── (b) SECURE, then grant at once ──────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Field-level security"
function Get-Permission([string]$ProfileId, [string]$Table, [string]$Column) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [string]$Table, [string]$Column, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            # Securing propagates asynchronously: a grant right after it may be refused 0x8004f508.
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured([string]$Table, [string]$Column, [bool]$Value) {
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    try {
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    } catch {
        # A Boolean or Picklist column carries its OptionSet as a navigation property the plain GET omits, and the PUT may
        # need it. Retry ONCE with the typed cast and the OptionSet expanded; any other type, or a second refusal, throws.
        $cast = switch ($typed.'@odata.type') {
            '#Microsoft.Dynamics.CRM.BooleanAttributeMetadata' { 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata' }
            '#Microsoft.Dynamics.CRM.PicklistAttributeMetadata' { 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata' }
            default { $null }
        }
        if (-not $cast) { throw }
        Write-Host "    PUT of $Table.$Column refused without its OptionSet ($($_.ErrorDetails.Message)); retrying once with `$expand=OptionSet..."
        $typed = Invoke-DvGet "$attrPath/$cast`?`$expand=OptionSet"
        $typed.IsSecured = $Value
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    }
}
function Grant-MissingPermissions([string]$Table, [string]$Column, $Specs) {
    foreach ($s in $Specs) {
        if (-not (Get-Permission $s.P.fieldsecurityprofileid $Table $Column)) { Grant-Permission $s.P.fieldsecurityprofileid $Table $Column $s.Create }
    }
}
function Write-MaskedRecovery([string]$Table, [string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW: $Table.$Column may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    Write-Host "  Every non-administrator then reads $Column EMPTY." -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script with -Apply: on an already-secured column it grants only the missing' -ForegroundColor Red
    Write-Host '        profile permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table; then -Verify reports it NOT secured (expected)." -ForegroundColor Red
}
foreach ($target in $Targets) {
    $t = $target.Table
    foreach ($col in $target.Columns) {
        $attr = Invoke-DvGet "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$col')?`$select=IsSecured"
        $specs = @(@{ P = $reader; Name = $ReaderProfileName; Create = 0 }, @{ P = $writer; Name = $WriterProfileName; Create = 4 })

        if ($attr.IsSecured -eq $true) {
            Report 'OK' "$t.$col is field-secured"
            if ($Apply) {
                try { Grant-MissingPermissions $t $col $specs }
                catch { Write-MaskedRecovery $t $col "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
            }
        }
        elseif ($Verify) { Report 'MISSING' "$t.$col is NOT field-secured; a user with Write can change it" }
        elseif ($IsDryRun) { Report 'WOULD' "secure $t.$col ($(if ($attr.'@odata.type') { $attr.'@odata.type' } else { 'type not annotated' })), then at once grant the reader (read) and writer (read/create/update) profiles" }
        else {
            $clock = [System.Diagnostics.Stopwatch]::StartNew()
            Set-ColumnSecured $t $col $true
            try {
                Grant-MissingPermissions $t $col $specs
            } catch {
                # The column is now secured and a grant failed: undo this run's securing so no reader is left masked.
                $grantError = $_
                Write-Host "  FAIL     granting the profiles on $t.$col failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
                Write-Host "           reverting $t.$col to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
                try {
                    Set-ColumnSecured $t $col $false
                    Publish-Tables @($t)
                    $back = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$col')?`$select=IsSecured").IsSecured
                    if ($back -eq $true) { throw "the revert was accepted but $t.$col still reads IsSecured=true" }
                    Write-Host "  REVERTED $t.$col is NOT field-secured again (as -Apply found it). Fix the cause, then re-run -Apply." -ForegroundColor Yellow
                } catch {
                    Write-MaskedRecovery $t $col "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
                }
                throw $grantError
            }
            $clock.Stop()
            Report 'DONE' ("secured $t.$col and granted both profiles; masked window {0:N1}s (secure -> reader grant)" -f $clock.Elapsed.TotalSeconds)
        }

        foreach ($s in $specs) {
            if (-not $s.P) { continue }
            $perm = Get-Permission $s.P.fieldsecurityprofileid $t $col
            if ($perm) {
                if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) on $t.$col (read=4 create=$($s.Create) update=$($s.Create))" }
                else { Report 'FAIL' "$($s.Name) on $t.$col is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
            } elseif ($Verify) { Report 'MISSING' "$($s.Name) on $t.$col" }
            elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $t.$col" }
            else { Report 'FAIL' "$($s.Name) on $t.$col is still missing after -Apply granted it" }
        }

        # (c) every OTHER writer
        $otherWriters = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$col' and (canupdate eq 4 or cancreate eq 4)").value |
            Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
        foreach ($o in $otherWriters) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $t.$col; only the BFF may" }
    }
}

# ── INFORMATIONAL: the residual writer set ───────────────────────────────────────────────────────────────────
Write-Host "`nInformational: System Administrator role holders (full field access by platform rule)"
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("user '{0}'{1}" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "TEAM '$($tm.name)'; every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    $tables = @($Targets | ForEach-Object { $_.Table })
    Publish-Tables $tables
    Write-Host "`nPublished $($tables -join ', ')." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) {
        Write-Host "`nPASS: the five columns are locked, every business unit's default team reads them, and only the BFF (and System Administrator) can write them." -ForegroundColor Green
        Write-Host "PRECONDITION MET: DirectMessage:BusinessUnitName and DirectMessage:OwnerTeamName may now be set on $EnvironmentUrl (fm task 054b binding rule)." -ForegroundColor Green
        exit 0
    }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "PRECONDITION NOT MET: leave DirectMessage:BusinessUnitName and DirectMessage:OwnerTeamName UNSET on $EnvironmentUrl (fm task 054b binding rule)." -ForegroundColor Red
    exit 1
}
Write-Host "`nDRY RUN complete; nothing was written. -Verify decides whether the DirectMessage:* settings may be set." -ForegroundColor Cyan
