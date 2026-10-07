#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Moves every secure project, matter and work assignment off the Secure Record business unit's DEFAULT owner team
    onto its NAMED, non-default, memberless owner team (unified-access-control-r2 task 144, GitHub #967).
    Dry run by default. One-time; idempotent.

.DESCRIPTION
    Before task 144, provisioning owned secure records by the Secure Record business unit's DEFAULT owner team.
    Dataverse maintains a default team's membership from each user's business unit and it cannot be curated, so any
    user moved into that business unit silently became a member of the team owning every secure record. Provisioning
    now owns them by a named team (SecureRecord:OwnerTeamName, default 'Secure Record Owners') that it proves
    memberless, in a business unit it proves user-free. This script moves the rows provisioned before that change.

    It reassigns ONLY rows that are already inside the Secure Record business unit (owned there by a team other than
    the named one). Everything else is REPORTED, never touched:
      - rows already owned by the named team            -> nothing to do (a second run changes nothing)
      - secure rows owned OUTSIDE the Secure Record BU  -> NOT ISOLATED. Not a migration: they were never provisioned,
                                                           or were reassigned out. Provision them through
                                                           POST /api/v1/external-access/provision-project, or take an
                                                           owner decision; the report gives creator, container and
                                                           content so that decision can be made.
      - non-secure rows owned inside the Secure Record BU -> anomaly, reported.

    MODES
      (default)  DRY RUN. Every check below, the full plan, zero writes.
      -Apply     Writes the plan, one row at a time: count the row's shares, assign the owner, READ THE OWNER BACK,
                 count the shares again. Stops at the first row whose owner did not land or whose share count fell.
      -Verify    Read-only gate for the live cutover. Exit 0 only when: every secure row is owned by the named team (none
                 by another team in the Secure Record BU, none outside it), the named team has no members, the BU holds
                 no users, and the 'Secure Record Owner' role is held by the named team alone (not the default team).
                 Exit 1 names each failure.

    STOPS (each refuses -Apply; the dry run reports it)
      - organization.sharetopreviousowneronassign is true: every assign would share the row back to its PREVIOUS owner,
        the default team, i.e. to every user ever placed in the business unit.
      - the Secure Record BU, its default team or the named team is missing or ambiguous.
      - the named team has any member, the default team has any member, or any systemuser (enabled or disabled, human
        or application) sits in the Secure Record BU. Nobody is moved by this script: relocating users is an owner
        decision.
      - the named team does not hold the 'Secure Record Owner' role (Dataverse would refuse the assignment).
      - a relationship from sprk_project / sprk_matter / sprk_workassignment cascades ASSIGN to a child table that
        -AcceptedAssignCascade does not list: the reassignment would re-own those children as a side effect, which is
        C10 part 2's decision.
      - a NOT ISOLATED secure matter or work assignment has children or documents: provisioning it would not retract
        content already in shared storage (reported for an owner decision; does not block migrating other rows).

    AUTH
      The operator's own az CLI identity, a token minted for the EXPLICIT environment URL (setup guide section 2).
      Never pac's active profile, never a client secret (ADR-028 A4).

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only gate with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER BusinessUnitName
    The Secure Record business unit (SecureRecord:BusinessUnitName). Default 'Secure Record'.

.PARAMETER OwnerTeamName
    The named owner team (SecureRecord:OwnerTeamName). Default 'Secure Record Owners'.

.PARAMETER AcceptedAssignCascade
    Child tables (logical names) whose Assign cascade from a root the owner has reviewed and accepted, e.g.
    'team','sharepointdocumentlocation','sharepointdocument' (platform-managed relationships). Any other cascading
    child stops -Apply.

.PARAMETER ReportPath
    Optional path for a JSON report of the census and plan (ids in full).

.EXAMPLE
    .\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: checks, census and plan; zero writes.

.EXAMPLE
    .\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument
    Moves the planned rows, verifying each by read-back and share count.

.EXAMPLE
    .\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = the cutover is complete and the invariants hold; exit 1 = it is not (each gap named).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string]$BusinessUnitName = 'Secure Record',

    [string]$OwnerTeamName = 'Secure Record Owners',

    [string[]]$AcceptedAssignCascade = @(),

    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$Api = "$EnvironmentUrl/api/data/v9.2"

$Roots = @(
    [pscustomobject]@{ Type = 'project';        Logical = 'sprk_project';        Set = 'sprk_projects';        Id = 'sprk_projectid';        Name = 'sprk_projectname' }
    [pscustomobject]@{ Type = 'matter';         Logical = 'sprk_matter';         Set = 'sprk_matters';         Id = 'sprk_matterid';         Name = 'sprk_mattername' }
    [pscustomobject]@{ Type = 'workassignment'; Logical = 'sprk_workassignment'; Set = 'sprk_workassignments'; Id = 'sprk_workassignmentid'; Name = 'sprk_name' }
)

# ── Auth: the operator's own identity, pinned to the explicit URL ─────────────────────────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}

# Every page of a GET. Callers wrap the call in @(...) so an empty or single-row answer is still an array, and Count
# means rows (returning the array with a unary comma would make @(...).Count report 1 for every answer).
function Get-All([string]$RelativePath) {
    $rows = New-Object System.Collections.Generic.List[object]
    $next = "$Api/$RelativePath"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $headers -Method Get
        foreach ($item in @($page.value)) { $rows.Add($item) }
        $next = $page.'@odata.nextLink'
    }
    return $rows.ToArray()
}
function Esc([string]$s) { $s.Replace("'", "''") }
function Short($g) { if ($g) { "$g".Substring(0, 8) } else { '-' } }

$stops = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

$orgRow = (Invoke-RestMethod -Uri "$Api/organizations?`$select=name,sharetopreviousowneronassign" -Headers $headers).value[0]
Write-Host "Environment : $EnvironmentUrl (org '$($orgRow.name)')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Secure BU   : '$BusinessUnitName'   named owner team: '$OwnerTeamName'"
Write-Host ''

# ── 1. sharetopreviousowneronassign ──────────────────────────────────────────────────────────────────────────
Write-Host "sharetopreviousowneronassign = $($orgRow.sharetopreviousowneronassign)"
if ($orgRow.sharetopreviousowneronassign -eq $true) {
    $stops.Add('organization.sharetopreviousowneronassign is TRUE: every assign would share the row back to the default team (every user ever placed in the BU). Turn it off, or decide how to strip the resulting shares, before migrating.')
}

# ── 2. Topology: BU, default team, named team, members, BU users, role holders ───────────────────────────────
$bus = @(Get-All "businessunits?`$select=businessunitid,name&`$filter=name eq '$(Esc $BusinessUnitName)'")
if ($bus.Count -ne 1) { throw "Expected exactly one business unit named '$BusinessUnitName'; found $($bus.Count). Nothing can be checked." }
$buId = $bus[0].businessunitid

$defaultTeams = @(Get-All "teams?`$select=teamid,name&`$filter=_businessunitid_value eq $buId and isdefault eq true and teamtype eq 0")
$namedTeams = @(Get-All "teams?`$select=teamid,name&`$filter=_businessunitid_value eq $buId and name eq '$(Esc $OwnerTeamName)' and teamtype eq 0 and isdefault eq false")
$defaultTeamId = if ($defaultTeams.Count -eq 1) { $defaultTeams[0].teamid } else { $null }
$namedTeamId = if ($namedTeams.Count -eq 1) { $namedTeams[0].teamid } else { $null }

Write-Host "Secure BU            : $buId"
Write-Host "Default team         : $(if ($defaultTeamId) { "$($defaultTeams[0].name) ($defaultTeamId)" } else { "NOT RESOLVED ($($defaultTeams.Count) match)" })"
Write-Host "Named owner team     : $(if ($namedTeamId) { "$($namedTeams[0].name) ($namedTeamId)" } else { "NOT RESOLVED ($($namedTeams.Count) match)" })"
if (-not $defaultTeamId) { $stops.Add("The Secure Record BU's default team did not resolve to exactly one ($($defaultTeams.Count)).") }
if (-not $namedTeamId) { $stops.Add("No single non-default Owner team named '$OwnerTeamName' exists in the Secure Record BU ($($namedTeams.Count) match). Create it per docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md section 4 first.") }

if ($namedTeamId) {
    $namedMembers = @(Get-All "teammemberships?`$select=systemuserid&`$filter=teamid eq $namedTeamId")
    Write-Host "Named team members   : $($namedMembers.Count)"
    if ($namedMembers.Count -gt 0) { $stops.Add("The named owner team has $($namedMembers.Count) member(s): $(($namedMembers | ForEach-Object { $_.systemuserid }) -join ', '). It must have none. Nobody is removed by this script.") }
}
if ($defaultTeamId) {
    $defaultMembers = @(Get-All "teammemberships?`$select=systemuserid&`$filter=teamid eq $defaultTeamId")
    Write-Host "Default team members : $($defaultMembers.Count)"
    if ($defaultMembers.Count -gt 0) { $stops.Add("The default team has $($defaultMembers.Count) member(s) - users sit in the Secure Record BU. Owner decision required; this script moves nobody.") }
}

$buUsers = @(Get-All "systemusers?`$select=systemuserid,fullname,isdisabled,applicationid&`$filter=_businessunitid_value eq $buId")
Write-Host "Users in Secure BU   : $($buUsers.Count)"
if ($buUsers.Count -gt 0) { $stops.Add("$($buUsers.Count) systemuser(s) sit in the Secure Record BU: $(($buUsers | ForEach-Object { "$($_.fullname) (disabled=$($_.isdisabled), app=$([bool]$_.applicationid))" }) -join '; '). Owner decision required; this script moves nobody.") }

$ownerRoles = @(Get-All "roles?`$select=roleid,name&`$filter=name eq 'Secure Record Owner' and _businessunitid_value eq $buId&`$expand=teamroles_association(`$select=teamid,name,isdefault),systemuserroles_association(`$select=systemuserid,fullname)")
$roleHolders = @()
foreach ($r in $ownerRoles) {
    $roleHolders += @($r.teamroles_association | ForEach-Object { [pscustomobject]@{ Kind = 'team'; Id = $_.teamid; Name = $_.name; IsDefault = [bool]$_.isdefault } })
    $roleHolders += @($r.systemuserroles_association | ForEach-Object { [pscustomobject]@{ Kind = 'user'; Id = $_.systemuserid; Name = $_.fullname; IsDefault = $false } })
}
Write-Host "Owner role holders   : $(($roleHolders | ForEach-Object { "$($_.Kind) '$($_.Name)'$(if ($_.IsDefault) { ' (DEFAULT team)' })" }) -join ', ')"
$namedHoldsRole = $namedTeamId -and ($roleHolders | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $namedTeamId })
if ($namedTeamId -and -not $namedHoldsRole) { $stops.Add("The named owner team does not hold the 'Secure Record Owner' role, so Dataverse would refuse every assignment. Assign it first (guide section 4).") }

# ── 3. Assign cascade census ─────────────────────────────────────────────────────────────────────────────────
Write-Host ''
$cascading = @()
foreach ($root in $Roots) {
    $rels = (Invoke-RestMethod -Uri "$Api/EntityDefinitions(LogicalName='$($root.Logical)')/OneToManyRelationships?`$select=SchemaName,ReferencingEntity,ReferencingAttribute,CascadeConfiguration" -Headers $headers).value
    foreach ($rel in ($rels | Where-Object { $_.CascadeConfiguration.Assign -ne 'NoCascade' })) {
        $cascading += [pscustomobject]@{ Root = $root.Logical; Child = $rel.ReferencingEntity; Attribute = $rel.ReferencingAttribute; Assign = $rel.CascadeConfiguration.Assign; Schema = $rel.SchemaName }
    }
}
Write-Host "Assign-cascading relationships from the roots: $($cascading.Count)"
foreach ($c in $cascading) {
    $accepted = $AcceptedAssignCascade -contains $c.Child
    Write-Host ("   {0,-22} -> {1}.{2}  Assign={3}  {4}" -f $c.Root, $c.Child, $c.Attribute, $c.Assign, $(if ($accepted) { 'ACCEPTED' } else { 'NOT ACCEPTED' }))
}
$unaccepted = @($cascading | Where-Object { $AcceptedAssignCascade -notcontains $_.Child } | ForEach-Object { $_.Child } | Sort-Object -Unique)
if ($unaccepted.Count -gt 0) {
    $stops.Add("Assign cascades from a root to: $($unaccepted -join ', '). Reassigning a root re-owns those children as a side effect (C10 part 2's decision). Record the owner's answer and pass it as -AcceptedAssignCascade.")
}

# ── 4. Census of secure rows ─────────────────────────────────────────────────────────────────────────────────
$entitySetCache = @{}
function Get-EntitySet([string]$logical) {
    if (-not $entitySetCache.ContainsKey($logical)) {
        $entitySetCache[$logical] = (Invoke-RestMethod -Uri "$Api/EntityDefinitions(LogicalName='$logical')?`$select=EntitySetName" -Headers $headers).EntitySetName
    }
    return $entitySetCache[$logical]
}
function Get-ShareCount([guid]$id) {
    @((Get-All "principalobjectaccessset?`$select=principalid,accessrightsmask&`$filter=objectid eq $id") | Where-Object { $_.accessrightsmask -gt 0 }).Count
}
$buContainers = @((Get-All "businessunits?`$select=sprk_containerid&`$filter=sprk_containerid ne null") | ForEach-Object { $_.sprk_containerid })

$plan = @()
$census = @()
foreach ($root in $Roots) {
    $rows = @(Get-All "$($root.Set)?`$select=$($root.Id),$($root.Name),sprk_issecure,sprk_containerid,_owningteam_value,_owninguser_value,_owningbusinessunit_value,_createdby_value&`$filter=sprk_issecure eq true or _owningbusinessunit_value eq $buId")
    $childRels = (Invoke-RestMethod -Uri "$Api/EntityDefinitions(LogicalName='$($root.Logical)')/OneToManyRelationships?`$select=ReferencingEntity,ReferencingAttribute" -Headers $headers).value |
        Where-Object { $_.ReferencingEntity -like 'sprk_*' }

    foreach ($row in $rows) {
        $id = $row.($root.Id)
        $inSecureBu = $row._owningbusinessunit_value -eq $buId
        $category =
            if ($row.sprk_issecure -ne $true) { 'ANOMALY-NOT-SECURE-IN-SECURE-BU' }
            elseif ($inSecureBu -and $namedTeamId -and $row._owningteam_value -eq $namedTeamId) { 'DONE' }
            elseif ($inSecureBu) { 'MIGRATE' }
            else { 'NOT-ISOLATED' }

        $entry = [ordered]@{
            Type = $root.Type; Id = $id; Name = $row.($root.Name); Category = $category
            OwningTeam = $row._owningteam_value; OwningUser = $row._owninguser_value; OwningBu = $row._owningbusinessunit_value
            OwnedByDefaultTeam = ($defaultTeamId -and $row._owningteam_value -eq $defaultTeamId)
            Container = $row.sprk_containerid; Shares = (Get-ShareCount $id)
        }

        if ($category -eq 'NOT-ISOLATED') {
            $creator = if ($row._createdby_value) { Invoke-RestMethod -Uri "$Api/systemusers($($row._createdby_value))?`$select=fullname,applicationid,isdisabled" -Headers $headers } else { $null }
            $entry.CreatedBy = if ($creator) { "$($creator.fullname) (app=$([bool]$creator.applicationid), disabled=$($creator.isdisabled))" } else { 'unknown' }
            $sameContainerRoots = 0
            if ($row.sprk_containerid) {
                foreach ($r2 in $Roots) { $sameContainerRoots += @(Get-All "$($r2.Set)?`$select=createdon&`$filter=sprk_containerid eq '$(Esc $row.sprk_containerid)'").Count }
            }
            $entry.ContainerKind =
                if (-not $row.sprk_containerid) { 'none' }
                elseif ($buContainers -contains $row.sprk_containerid -or $sameContainerRoots -gt 1) { 'SHARED' }
                else { 'own' }
            $content = [ordered]@{}
            foreach ($rel in $childRels) {
                $set = Get-EntitySet $rel.ReferencingEntity
                $n = @(Get-All "$set`?`$select=createdon&`$filter=_$($rel.ReferencingAttribute)_value eq $id").Count
                if ($n -gt 0) { $content["$($rel.ReferencingEntity).$($rel.ReferencingAttribute)"] = $n }
            }
            $entry.Content = $content
            if ($root.Type -ne 'project' -and $content.Count -gt 0) {
                $warnings.Add("STOP (escalation): secure $($root.Type) $id is NOT ISOLATED and has content ($(($content.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')). Provisioning it does not retract content already in shared storage - owner decision before provisioning it.")
            }
        }

        if ($category -eq 'MIGRATE' -and $cascading.Count -gt 0) {
            $children = [ordered]@{}
            foreach ($c in ($cascading | Where-Object Root -eq $root.Logical)) {
                $set = Get-EntitySet $c.Child
                $children["$($c.Child).$($c.Attribute)"] = @(Get-All "$set`?`$select=createdon&`$filter=_$($c.Attribute)_value eq $id").Count
            }
            $entry.CascadeChildren = $children
        }

        $census += [pscustomobject]$entry
        if ($category -eq 'MIGRATE') { $plan += [pscustomobject]$entry }
    }
}

Write-Host ''
Write-Host "Secure-root census ($($census.Count) row(s) secure or owned in the Secure BU):"
foreach ($e in $census) {
    $extra = if ($e.Category -eq 'NOT-ISOLATED') { " createdBy=$($e.CreatedBy) container=$($e.ContainerKind) content=$(if ($e.Content.Count) { ($e.Content.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ',' } else { 'none' })" } else { '' }
    Write-Host ("   [{0,-31}] {1,-14} {2} '{3}' owningTeam={4} owningUser={5} owningBu={6} defaultTeam={7} shares={8}{9}" -f `
        $e.Category, $e.Type, (Short $e.Id), $e.Name, (Short $e.OwningTeam), (Short $e.OwningUser), (Short $e.OwningBu), $e.OwnedByDefaultTeam, $e.Shares, $extra)
}
foreach ($e in ($census | Where-Object Category -eq 'NOT-ISOLATED')) {
    $warnings.Add("NOT ISOLATED: secure $($e.Type) $($e.Id) '$($e.Name)' is owned outside the Secure Record BU (owningBu $(Short $e.OwningBu)) and is readable by that business unit. Not migrated by this script: provision it, unsecure it, or delete it (owner decision).")
}
foreach ($e in ($census | Where-Object Category -eq 'ANOMALY-NOT-SECURE-IN-SECURE-BU')) {
    $warnings.Add("ANOMALY: $($e.Type) $($e.Id) is owned in the Secure Record BU but sprk_issecure is not true. Not touched.")
}

Write-Host ''
Write-Host "PLAN: $($plan.Count) row(s) to move to '$OwnerTeamName'."
foreach ($p in $plan) { Write-Host "   $($p.Type) $($p.Id) '$($p.Name)' from team $($p.OwningTeam) (shares now: $($p.Shares))" }
if ($warnings.Count) { Write-Host ''; $warnings | ForEach-Object { Write-Warning $_ } }
if ($stops.Count) { Write-Host ''; $stops | ForEach-Object { Write-Host "STOP: $_" -ForegroundColor Red } }

if ($ReportPath) {
    [ordered]@{
        environment = $EnvironmentUrl; generatedUtc = (Get-Date).ToUniversalTime().ToString('o'); mode = $(if ($Verify) { 'verify' } elseif ($IsDryRun) { 'dry-run' } else { 'apply' })
        businessUnitId = $buId; defaultTeamId = $defaultTeamId; namedTeamId = $namedTeamId
        sharetopreviousowneronassign = $orgRow.sharetopreviousowneronassign
        roleHolders = $roleHolders; assignCascade = $cascading; census = $census; plan = $plan; stops = $stops; warnings = $warnings
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    Write-Host "Report written: $ReportPath"
}

# ── VERIFY ───────────────────────────────────────────────────────────────────────────────────────────────────
if ($Verify) {
    $failures = New-Object System.Collections.Generic.List[string]
    $failures.AddRange([string[]]$stops.Where({ -not $_.StartsWith('Assign cascades') }))
    foreach ($p in $plan) { $failures.Add("$($p.Type) $($p.Id) is still owned by team $($p.OwningTeam), not the named team.") }
    # Every secure row must end owned by the named team — a secure row outside the Secure Record BU is not isolated.
    foreach ($e in ($census | Where-Object Category -eq 'NOT-ISOLATED')) { $failures.Add("secure $($e.Type) $($e.Id) '$($e.Name)' is owned outside the Secure Record BU (not isolated).") }
    foreach ($h in ($roleHolders | Where-Object { -not ($_.Kind -eq 'team' -and $_.Id -eq $namedTeamId) })) { $failures.Add("'Secure Record Owner' is held by $($h.Kind) '$($h.Name)'$(if ($h.IsDefault) { ' (the DEFAULT team)' }) - the named team must hold it alone.") }
    if ($failures.Count -eq 0) { Write-Host 'VERIFY: PASS' -ForegroundColor Green; exit 0 }
    $failures | ForEach-Object { Write-Host "VERIFY FAIL: $_" -ForegroundColor Red }
    exit 1
}

if ($IsDryRun) {
    Write-Host ''
    Write-Host 'DRY RUN: nothing was written.'
    exit $(if ($stops.Count) { 2 } else { 0 })
}

# ── APPLY ────────────────────────────────────────────────────────────────────────────────────────────────────
if ($stops.Count) { throw "Refusing -Apply: $($stops.Count) stop condition(s) above. Nothing was written." }
if ($plan.Count -eq 0) { Write-Host 'Nothing to migrate: every secure row in the Secure Record BU is already owned by the named team.'; exit 0 }

$moved = 0
foreach ($p in $plan) {
    $root = $Roots | Where-Object Type -eq $p.Type
    $before = Get-ShareCount $p.Id
    $body = @{ 'ownerid@odata.bind' = "/teams($namedTeamId)" } | ConvertTo-Json
    Invoke-RestMethod -Method Patch -Uri "$Api/$($root.Set)($($p.Id))" -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($body)) | Out-Null

    # Read-back, not trust: an ignored @odata.bind is accepted with no error.
    $after = Invoke-RestMethod -Uri "$Api/$($root.Set)($($p.Id))?`$select=_owningteam_value,_owningbusinessunit_value" -Headers $headers
    if ($after._owningteam_value -ne $namedTeamId) {
        throw "Read-back FAILED for $($p.Type) $($p.Id): owning team is $($after._owningteam_value), expected $namedTeamId. Stopped after $moved row(s)."
    }
    $sharesAfter = Get-ShareCount $p.Id
    if ($sharesAfter -ne $before) {
        throw "Share count changed on $($p.Type) $($p.Id): $before before, $sharesAfter after. Stopped after $($moved + 1) row(s) - inspect the record's shares."
    }
    $moved++
    Write-Host "   moved $($p.Type) $($p.Id): owner verified, shares $before -> $sharesAfter"
}
Write-Host "APPLIED: $moved row(s) moved to '$OwnerTeamName'. Re-run without -Apply: the plan must be empty. Then remove the role from the default team (guide section 4) and run -Verify."
