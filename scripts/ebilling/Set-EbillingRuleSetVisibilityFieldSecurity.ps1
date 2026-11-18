#!/usr/bin/env pwsh
<#
.SYNOPSIS
    eBilling R1 ISS-040 (#2901) - field-secures sprk_ruleset.sprk_visibletofirms so only the BFF writes it and every user still
    reads it: "Spaarke BFF-Managed Field Writers" gets read, create and update; "Spaarke BFF-Managed Field Readers" gets read only.
    -WhatIf is a dry run; a plain run writes; -Verify checks; -Revert undoes a run (with -WhatIf: a dry run of it).

.DESCRIPTION
    WHY. Every firm-visibility reader trusts the flag: it decides whether a rule set's clause quotes and section references are
    shown to firms (D-66, D-79). Only the BFF paths that own it change it (RuleSetService create and visibility update,
    ProcedureService type change, ProcedureAssignmentService record-set create); the BFF writes through its application user,
    which is in the writers profile. Secured, a native write by anyone else is refused, and a rule set created natively by
    someone else keeps the column default, No. The form and the "Visible to Firms" view column keep showing the value to every
    user, through the readers profile.

    PROFILES (REUSED, never created or re-membered here):
      "Spaarke BFF-Managed Field Writers" - read=4 create=4 update=4 on the column.
      "Spaarke BFF-Managed Field Readers" (id 42e542c8-47bf-f111-aaaf-0022482913fc) - read=4 create=0 update=0. It is on every
          business unit's default team, so every user keeps reading the column. This script never adds or removes a member of it.

    PRECONDITIONS (every mode; a write run refuses unless all pass):
      (p0) the column exists and can be secured for read, create and update;
      (p1) the writer profile exists, exactly once, and is in -SolutionUniqueName; its members are exactly the -BffApplicationIds
           application users: each is a member, and there is no other user (human or application) and no team (a team would make
           every member a writer). A missing BFF member refuses the run before the column is secured, with nothing changed;
      (p2) the readers profile exists, exactly once, and is in -SolutionUniqueName;
      (p3) the readers profile is on EVERY business unit's default team (the ontology writer unit's default team excepted, as the
           other scripts do);
      (p4) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets the column (maker configuration
           writing outside the BFF, refused once secured);
      (p5) the column is in -SolutionUniqueName (its own row, or its table with rootcomponentbehavior 0);
      (p6) informational: how many rule sets are visible to firms now (securing a column does not change what was written).

    STEPS (a plain run), in this order:
      (a) secure the column, then IMMEDIATELY grant the readers profile (read) and the writers profile (read, create, update),
          12 retries on 0x8004f508. Until the grants land, a user outside System Administrator reads the column as empty. If a
          grant fails, the column is REVERTED to not secured and published before the script stops; if the revert fails too it
          prints "RECOVERY REQUIRED NOW". Re-running is the resume path: on a column already secured it grants only what is
          missing;
      (b) the permissions are read back: writers read=4 create=4 update=4, readers read=4 create=0 update=0;
      (c) the readers profile description names the column (the current text is read first and appended to, once);
      (d) any OTHER profile that can create or update the column is FAIL; the platform's own "System Administrator" profile is
          matched by its fixed id (572329c1-a042-4e22-be47-367c6374ea45), never by name;
      (e) publish sprk_ruleset - only when this run wrote something.

    A second run reports every item OK and writes nothing (no publish either).

    -Confirm: every write point goes through ShouldProcess. The automatic revert after a failed grant is a recovery and is never
    asked.

    -REVERT, in this order: (r1) un-secure the column, (r2) delete this script's two permission rows if left, (r3) publish. The
    preconditions are not required. The SpaarkeMaster source (Entity.xml IsSecured and the two FieldSecurityProfiles.xml rows) is
    reverted by a commit, not by this script. The readers profile description is left as it is.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users, the ONLY members of the writer profile (dev:
    5967251e-171c-46fe-a6c2-ef843c90309d and, while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c).
    Per-customer input (D-13), never hard-coded. Mandatory in every mode, -Revert included.

.PARAMETER SolutionUniqueName
    The unmanaged solution the profiles and sprk_ruleset travel in. Default SpaarkeMaster.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -WhatIf or -Revert.

.PARAMETER Revert
    Undo a run: un-secure the column, remove this script's permission rows, publish. Combine with -WhatIf for a dry run.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
    pwsh scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    pwsh scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify
    pwsh scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Revert -WhatIf

.NOTES
    Project: ebilling-r1, ISS-040 (#2901). Shape: scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 (modes,
    revert-on-failed-grant, publish only on a write) with the reader grant of scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1.
    The SpaarkeMaster source carries IsSecured=1 and both permission rows; the guard is RuleSetVisibilityFieldSecuredGuardTests.
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets. A live run follows the
    notices rule (notices to unified-access-control-r2 before and after) and the owner's typed OK.
    Exit codes: 0 = pass / done, 1 = a gap or an error.
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
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds: at least one application id is required.' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$IsDryRun = [bool]$WhatIfPreference
if ($IsDryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Revert -and $Verify) { throw '-Revert and -Verify are separate modes; run -Revert, then check IsSecured.' }
$IsWrite = -not $IsDryRun -and -not $Verify
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$Table = 'sprk_ruleset'
$Column = 'sprk_visibletofirms'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$ReaderProfileFixedId = '42e542c8-47bf-f111-aaaf-0022482913fc'
$ReaderDescriptionAppend = "Also reads $Table.$Column (ebilling-r1 ISS-040)."
# The platform's own System Administrator field security profile: a fixed id in every environment, and Dataverse gives it a row on
# every secured column. Matched by id, never by name.
$SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    # Strongly consistent reads: attribute metadata (IsSecured) is cached per server, and a plain read soon after a change can
    # come back stale (a secured column reported as unsecured). The header is ignored on data requests.
    Consistency        = 'Strong'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}

. (Join-Path $PSScriptRoot '..' 'common' 'DataverseSolutionMembership.ps1')
. (Join-Path $PSScriptRoot '..' 'common' 'OntologyWriterUnit.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'UPDATED' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
    # UPDATED is a profile description edit: it needs no publish of the table.
    if ($State -eq 'DONE') { $script:Wrote = $true }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host "Column      : $Table.$Column (every user reads it; only the BFF writers profile changes it)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Revert -and $IsDryRun) { 'REVERT DRY RUN (-WhatIf, no writes)' } elseif ($Revert) { 'REVERT' } elseif ($IsDryRun) { 'DRY RUN (-WhatIf, no writes)' } else { 'WRITE' }))
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
$publishBody = @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }

# ── -REVERT: un-secure first, then remove this script's rows, then publish ───────────────────────────────────
if ($Revert) {
    Write-Host "`nRevert"
    $secured = (Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured
    $rows = @((Invoke-DvGet ("fieldpermissions?`$select=fieldpermissionid&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column'" +
        "&`$expand=fieldsecurityprofileid(`$select=name)")).value | Where-Object { $_.fieldsecurityprofileid.name -in $WriterProfileName, $ReaderProfileName })
    $wrote = $false
    if (-not $secured) { Write-Host "  OK       $Table.$Column is not field-secured" -ForegroundColor Green }
    elseif ($IsDryRun) { Write-Host "  WOULD    (r1) un-secure $Table.$Column" -ForegroundColor Cyan }
    elseif ($PSCmdlet.ShouldProcess("$Table.$Column", 'Revert: un-secure the column')) {
        $typed = Invoke-DvGet $attrPath; $typed.IsSecured = $false
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $wrote = $true; Write-Host "  DONE     (r1) un-secured $Table.$Column" -ForegroundColor Green
    }
    if ($rows.Count -eq 0) { Write-Host "  OK       no $WriterProfileName or $ReaderProfileName row on $Table.$Column" -ForegroundColor Green }
    foreach ($row in $rows) {
        if ($IsDryRun) { Write-Host "  WOULD    (r2) delete the '$($row.fieldsecurityprofileid.name)' row $($row.fieldpermissionid) on $Table.$Column" -ForegroundColor Cyan }
        elseif ($PSCmdlet.ShouldProcess("fieldpermission $($row.fieldpermissionid)", "Revert: delete the '$($row.fieldsecurityprofileid.name)' row on $Table.$Column")) {
            try { Invoke-DvWrite DELETE "fieldpermissions($($row.fieldpermissionid))" $null | Out-Null; Write-Host "  DONE     (r2) deleted the '$($row.fieldsecurityprofileid.name)' row $($row.fieldpermissionid)" -ForegroundColor Green }
            catch { if ("$($_.Exception.Message)" -notmatch '404') { throw }; Write-Host '  OK       (r2) the row went with the un-secure' -ForegroundColor Green }
            $wrote = $true
        }
    }
    if ($IsDryRun) { Write-Host "  WOULD    (r3) publish $Table (only if r1 or r2 wrote)" -ForegroundColor Cyan; Write-Host "`nREVERT DRY RUN complete - nothing was written." -ForegroundColor Cyan; exit 0 }
    if ($wrote -and $PSCmdlet.ShouldProcess($Table, 'Revert: publish')) { Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null; Write-Host "`nPublished $Table." -ForegroundColor Green }
    if ((Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured) { Write-Host "`nFAIL: $Table.$Column is still field-secured." -ForegroundColor Red; exit 1 }
    Write-Host "`nREVERTED: $Table.$Column is not field-secured. Revert the SpaarkeMaster source (Entity.xml IsSecured, the two FieldSecurityProfiles.xml rows) by a commit." -ForegroundColor Green
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
    '&$expand=teamprofiles_association($select=teamid,name),systemuserprofiles_association($select=systemuserid,fullname,applicationid,isdisabled)')).value)
$writer = $null
if ($writers.Count -eq 1) { $writer = $writers[0]; Report 'OK' "(p1) profile '$WriterProfileName' exists ($($writer.fieldsecurityprofileid))" }
else { Report 'FAIL' "(p1) $($writers.Count) profile(s) named '$WriterProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

$readers = @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name,description&`$filter=name eq '$ReaderProfileName'" +
    '&$expand=teamprofiles_association($select=teamid,name)')).value)
$reader = $null
if ($readers.Count -eq 1) {
    $reader = $readers[0]
    if ($reader.fieldsecurityprofileid.ToString().ToLowerInvariant() -eq $ReaderProfileFixedId) { Report 'OK' "(p2) profile '$ReaderProfileName' exists ($($reader.fieldsecurityprofileid))" }
    else { Report 'FAIL' "(p2) profile '$ReaderProfileName' has id $($reader.fieldsecurityprofileid), not the id the SpaarkeMaster source carries ($ReaderProfileFixedId)" }
}
else { Report 'FAIL' "(p2) $($readers.Count) profile(s) named '$ReaderProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
$membership = $null
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @(@{ Id = 'p1'; Profile = $writer; Name = $WriterProfileName }, @{ Id = 'p2'; Profile = $reader; Name = $ReaderProfileName })) {
        if (-not $p.Profile) { continue }
        # A field security profile is a ROOT component: only its own solutioncomponents row ('Direct') counts.
        if (Test-DvInSolution -Membership $membership -ComponentId $p.Profile.fieldsecurityprofileid) { Report 'OK' "($($p.Id)) '$($p.Name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "($($p.Id)) '$($p.Name)' is not in $SolutionUniqueName - its permission on $Column would not travel" }
    }
}

# (p1) the writer profile's members are exactly the -BffApplicationIds application users. This runs in the preconditions, before the
# column is secured: a missing BFF member refuses the run with nothing changed.
$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname,isdisabled&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p1) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p1) BFF app user '$($u.fullname)' is a member of $WriterProfileName" }
        else { Report 'FAIL' "(p1) BFF app user '$($u.fullname)' is NOT a member of $WriterProfileName - it would be refused when it writes $Column once secured (unless System Administrator). Membership is maintained by Set-RecordCreatorPersonSchema.ps1, never here" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p1) $WriterProfileName member '$($m.fullname)' is $kind - it could change $Column"
    }
    foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "(p1) $WriterProfileName has TEAM '$($tm.name)' - every member could change $Column" }
}

# The ontology writer's business unit's default team (scripts/common/OntologyWriterUnit.ps1) is left out of the set that must hold the
# readers profile, and is a FAIL when it does hold it (reported, never removed).
function Test-ReaderOnDefaultTeam([string[]]$OnReader, $ReaderProfileId) {
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p3) no business-unit default team was read - the query is wrong' }
    $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams $defaultTeams
    $left = @($split.WriterUnitTeams | ForEach-Object { "'$($_.name)' ($($_.teamid))" })
    Report 'INFO' "(p3) ontology writer business unit: default team left out of the readers profile: $(if ($left.Count) { $left -join ', ' } else { 'none' })"
    foreach ($w in @(Get-DvOntologyWriterUnitTeamsOnProfile -Api $Api -Headers $headers -ProfileId $ReaderProfileId -WriterUnitTeams $split.WriterUnitTeams)) {
        Report 'FAIL' "(p3) the ontology writer business unit's default team '$($w.name)' holds the readers profile - it must hold no field-security profile. Removing it is a team change (the owner's); this script never removes a member"
    }
    foreach ($t in $split.ReaderTeams) {
        if ($OnReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p3) readers profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p3) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the readers profile - its users would read the visibility flag EMPTY. Adding it is a team change (the owner's); this script never makes it" }
    }
}
if ($reader) {
    Test-ReaderOnDefaultTeam @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() }) $reader.fieldsecurityprofileid
}

$configuredWriters = 0
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $chSet = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($row in @((Invoke-DvGet "$chSet`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$Column'").value)) {
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

    $visible = @((Invoke-DvGet "$($tableMeta.EntitySetName)?`$select=$($Table)id&`$filter=$Column eq true").value).Count
    Report 'INFO' "(p6) $visible $Table row(s) are visible to firms now (securing a column does not change what was written)"
}

if ($IsWrite -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: a write run needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) SECURE, then grant both profiles at once ────────────────────────────────────────────────────────────
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
    # GET-then-PUT of the typed attribute definition (scripts/Set-RegardingPairFieldSecurity.ps1).
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
# Readers first: they are everyone, so their grant ends the widest masked window.
$specs = @()
if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
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
    Write-Host '  Users outside System Administrator then read the visibility flag as empty, and the BFF cannot write it.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script: on an already-secured column it grants only the missing profile' -ForegroundColor Red
    Write-Host '        permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host '    (2) or run it with -Revert;' -ForegroundColor Red
    Write-Host "    (3) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
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
elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured - NOT field-secured; writes are not limited to the BFF writers profile" }
elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$Column, then at once grant $ReaderProfileName (read) and $WriterProfileName (read, create, update)" }
elseif ($specs.Count -lt 2) { Report 'FAIL' "$Table.$Column was not secured: a profile is missing" }
elseif (-not $PSCmdlet.ShouldProcess("$Table.$Column", "Field-secure the column, then at once grant $ReaderProfileName read and $WriterProfileName read/create/update")) { Report 'FAIL' "$Table.$Column was not secured (declined at the confirmation)" }
else {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Set-ColumnSecured $true
    Report 'DONE' "secured $Table.$Column"
    try {
        Grant-MissingPermissions
    } catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $Table.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
        Write-Host "           reverting $Table.$Column to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
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
    Report 'INFO' ("masked window {0:N1}s (secure -> grants)" -f $clock.Elapsed.TotalSeconds)
}

# ── (b) THE PERMISSIONS, read back ──────────────────────────────────────────────────────────────────────────
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

# ── (c) THE READERS PROFILE DESCRIPTION names the column (read first, append once, never clobber) ─────────────
Write-Host "`n(c) Readers profile description"
if ($reader) {
    $current = "$($reader.description)"
    if ($current -match [regex]::Escape("$Table.$Column")) { Report 'OK' "'$ReaderProfileName' description names $Table.$Column" }
    elseif ($Verify) { Report 'MISSING' "'$ReaderProfileName' description does not name $Table.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "append to the '$ReaderProfileName' description: $ReaderDescriptionAppend" }
    elseif ($PSCmdlet.ShouldProcess($ReaderProfileName, 'Append the column to the profile description')) {
        $next = if ($current.Trim()) { "$($current.TrimEnd()) $ReaderDescriptionAppend" } else { $ReaderDescriptionAppend }
        Invoke-DvWrite PATCH "fieldsecurityprofiles($($reader.fieldsecurityprofileid))" @{ description = $next } | Out-Null
        Report 'UPDATED' "'$ReaderProfileName' description now names $Table.$Column"
    }
}

# ── (d) every OTHER profile that can create or update ───────────────────────────────────────────────────────
Write-Host "`n(d) Other profiles"
$writerId = if ($writer) { $writer.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$others = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=fieldsecurityprofileid,name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value |
    Where-Object { $_.fieldsecurityprofileid.fieldsecurityprofileid.ToString().ToLowerInvariant() -notin $SystemAdministratorProfileId, $writerId })
foreach ($o in $others) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' ($($o.fieldsecurityprofileid.fieldsecurityprofileid)) can create or update $Table.$Column (create=$($o.cancreate) update=$($o.canupdate)) - only the BFF writers profile may" }
if ($others.Count -eq 0) { Report 'OK' "no profile besides '$WriterProfileName' and the System Administrator profile ($SystemAdministratorProfileId) can create or update $Table.$Column" }

# ── (e) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($IsWrite -and $script:Wrote -and $PSCmdlet.ShouldProcess($Table, 'Publish')) {
    Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}
elseif ($IsWrite) { Write-Host "`nNothing published." -ForegroundColor Green }

if ($IsDryRun) {
    if ($gaps.Count -gt 0) {
        Write-Host "`nDRY RUN complete - nothing was written. A write run would be REFUSED ($($gaps.Count) gap(s)):" -ForegroundColor Yellow
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }
    else { Write-Host "`nDRY RUN complete - nothing was written." -ForegroundColor Cyan }
    exit 0
}
if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Table.$Column is field-secured, every user reads it through the readers profile, and only the BFF writers profile (and System Administrator) can create or update it." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
