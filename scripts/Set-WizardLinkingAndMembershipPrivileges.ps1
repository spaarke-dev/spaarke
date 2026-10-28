#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies issue #2382's role changes to one environment: Spaarke Core User can link wizard lookups at its Read depth,
    and contact-organization membership writes move to the Spaarke Access Administrator role.
    Dry run by default.

.DESCRIPTION
    Reads two declared sets and brings the LIVE roles to them:

      config/core-user-wizard-linking-role.json
          For each bind target: AppendTo on the Core User role is raised to the target's DECLARED depth
          ('appendToDepth'), never above it, and never above the role's live Read depth on that table unless the target
          declares 'appendToAboveRead'. For each created table: Append is raised to the role's live Create depth. Adds
          or raises only; never touches Read, Create, Write, Delete, Share or Assign.
          Depth drift: the live Read differing from the declared depth (the exception's 'readDepth' where declared), or
          the live AppendTo above the declared depth, is reported and fails -Verify. Neither is changed here.

      config/contact-organization-membership-role.json
          The writer role gets the listed privileges at Global (adds or raises only). Each read-only role loses the
          listed privileges on the table; its Read and Share stay.
          Other writers: every live root role holding Create, Write, Delete, Assign or Append on the table (all pages)
          must be in 'allowedWriterRoles' (each with its reason) or be one of the planned removals; any other fails
          -Verify. An allowlist entry marked 'platformRole' (Service Writer, Service Deleter) passes only while no
          non-application user and no team holds that role (any business-unit copy); a person or team holder fails
          -Verify. A failed read of either listing stops the script with exit 1 before any write (fail closed).

    MODES
      (default)  Dry run. Prints every change as role, privilege, old depth -> new depth. Zero writes.
      -Apply     Makes the changes (AddPrivilegesRole, RemovePrivilegeRole), then reads every role back and confirms
                 each change. Names anything the platform added that was not asked for.
      -Verify    Read-only. Exits 1 when any change is still pending, on depth drift, or on a membership writer not in
                 the allowlist; 0 when the live roles match both sets.

    ALSO REPORTED (never changed here)
      - Each role is resolved by name to exactly one ROOT role (its business-unit copies take their privileges from
        it); the number of copies is printed.
      - XML/live drift: for each role with a file under src/dataverse/solutions/SpaarkeMaster/Roles, every in-scope
        privilege whose level in the file differs from the live level after the planned changes.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only pass/fail check. Cannot be combined with -Apply.

.PARAMETER DriftRoles
    Extra roles (beyond the ones this script changes) whose in-scope privileges are compared with their role XML.

.EXAMPLE
    .\Set-WizardLinkingAndMembershipPrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the exact diff, with no writes.

.EXAMPLE
    .\Set-WizardLinkingAndMembershipPrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = the live roles match both declared sets; exit 1 = a change is still pending (each is named).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string[]]$DriftRoles = @('Spaarke Office Add In User'),

    [string]$LinkingConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'core-user-wizard-linking-role.json'),

    [string]$MembershipConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'contact-organization-membership-role.json'),

    [string]$RolesXmlDir = (Join-Path $PSScriptRoot '..' 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'Roles')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$Api = "$EnvironmentUrl/api/data/v9.2"
$DepthOrder = @('None', 'Basic', 'Local', 'Deep', 'Global')
function Get-Rank([string]$Depth) {
    $i = [array]::IndexOf($DepthOrder, $Depth)
    if ($i -lt 0) { throw "Unknown depth '$Depth'." }
    $i
}

# ── The declared sets ───────────────────────────────────────────────────────────────────────────────────────
foreach ($p in $LinkingConfigPath, $MembershipConfigPath) { if (-not (Test-Path -LiteralPath $p)) { throw "Config not found: $p" } }
$linking = Get-Content -LiteralPath $LinkingConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$membership = Get-Content -LiteralPath $MembershipConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($linking.schemaVersion -ne 1 -or $membership.schemaVersion -ne 1) { throw 'Unsupported schemaVersion in a config file.' }
if ($membership.writerRole.depth -ne 'Global') { throw 'The membership writer role is granted at Global only, by design.' }
foreach ($removed in $membership.removedFromReadOnlyRoles) {
    if ($removed -match '^prv(Read|Share)') { throw "Config would remove '$removed' from the read-only roles; Read and Share stay, by design." }
}
foreach ($t in $linking.bindTargets) {
    if (-not $t.appendToDepth) { throw "Bind target '$($t.logicalName)' declares no appendToDepth. Fix the config." }
    $null = Get-Rank ([string]$t.appendToDepth)
    if ($t.appendToAboveRead) {
        $null = Get-Rank ([string]$t.appendToAboveRead.readDepth)
        if (-not $t.appendToAboveRead.reason -or -not $t.appendToAboveRead.since) { throw "Bind target '$($t.logicalName)': appendToAboveRead needs a reason and a commit ('since')." }
    }
}
$allowedWriters = @{}
$platformRoles = @()
foreach ($a in @($membership.allowedWriterRoles)) {
    if (-not $a.roleName -or -not $a.reason) { throw 'Each allowedWriterRoles entry needs a roleName and a reason.' }
    $allowedWriters[[string]$a.roleName] = [string]$a.reason
    if ($a.platformRole -eq $true) { $platformRoles += [string]$a.roleName }
}
if (-not $allowedWriters.ContainsKey([string]$membership.writerRole.roleName)) { throw 'allowedWriterRoles must list the writer role.' }
if ($platformRoles -contains [string]$membership.writerRole.roleName) { throw 'The writer role cannot be marked platformRole.' }
foreach ($ro in $membership.readOnlyRoles) { if ($allowedWriters.ContainsKey([string]$ro)) { throw "Read-only role '$ro' cannot also be an allowed writer." } }

# ── Auth: the operator's own az CLI identity. No secrets in this script or the repo. ────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$RelativePath) { Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers $headers -Method Get }
# Every row of a collection query: follows @odata.nextLink to the last page.
function Invoke-DvGetAll([string]$RelativePath) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $uri = "$Api/$RelativePath"
    while ($uri) {
        $page = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        foreach ($v in @($page.value)) { $rows.Add($v) }
        $uri = $page.'@odata.nextLink'
    }
    , $rows
}
function Invoke-DvPost([string]$RelativePath, $Body) {
    $json = $Body | ConvertTo-Json -Depth 6
    Invoke-RestMethod -Method Post -Uri "$Api/$RelativePath" -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
}

$orgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$orgName')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

$rootBus = @((Invoke-DvGet 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null').value)
if ($rootBus.Count -ne 1) { throw "Expected one root business unit; found $($rootBus.Count)." }
$rootBuId = $rootBus[0].businessunitid

# ── Roles: each name resolves to exactly one ROOT role ──────────────────────────────────────────────────────
$roleCache = @{}
function Get-Role([string]$Name) {
    if ($roleCache.ContainsKey($Name)) { return $roleCache[$Name] }
    $escaped = $Name -replace "'", "''"
    $all = @((Invoke-DvGet "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$escaped'").value)
    $roots = @($all | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
    if ($roots.Count -ne 1) { throw "Expected exactly one root role named '$Name'; found $($roots.Count) (of $($all.Count) rows)." }
    $r = $roots[0]
    $copies = @($all | Where-Object { $_._parentrootroleid_value -eq $r.roleid -and $_.roleid -ne $r.roleid }).Count
    $held = @{}
    foreach ($h in @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$($r.roleid)").RolePrivileges)) {
        $held[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h
    }
    $role = [pscustomobject]@{
        Name = $Name; Id = $r.roleid; BusinessUnitId = $r._businessunitid_value; InRootBu = ($r._businessunitid_value -eq $rootBuId)
        Copies = $copies; Held = $held
    }
    $roleCache[$Name] = $role
    $role
}
function Get-HeldDepth($Role, $Privilege) {
    $h = $Role.Held[$Privilege.Id]
    if ($null -eq $h) { 'None' } else { [string]$h.Depth }
}

# ── Privileges by name, from table METADATA (never guessed) ─────────────────────────────────────────────────
$privCache = @{}
function Get-TablePrivileges([string]$Table) {
    if (-not $privCache.ContainsKey($Table)) {
        $privCache[$Table] = @((Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Privileges").value | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Id = $_.PrivilegeId.ToString().ToLowerInvariant(); Type = $_.PrivilegeType }
            })
    }
    $privCache[$Table]
}
function Get-Privilege([string]$Table, [string]$Name) {
    $match = @(Get-TablePrivileges $Table | Where-Object { $_.Name -ieq $Name })
    if ($match.Count -ne 1) { throw "Table '$Table' has no privilege named '$Name' in metadata. Fix the config." }
    $match[0]
}

# ── The plan ────────────────────────────────────────────────────────────────────────────────────────────────
$plan = [System.Collections.Generic.List[object]]::new()
function Add-Raise($Role, $Privilege, [string]$To, [string]$Why) {
    $old = Get-HeldDepth $Role $Privilege
    if ((Get-Rank $old) -lt (Get-Rank $To)) {
        $plan.Add([pscustomobject]@{ Role = $Role; Privilege = $Privilege; Old = $old; New = $To; Action = 'Raise'; Why = $Why })
    }
}

$core = Get-Role $linking.roleName
$depthDrift = [System.Collections.Generic.List[string]]::new()
foreach ($t in $linking.bindTargets) {
    $read = Get-Privilege $t.logicalName $t.readPrivilege
    $appendTo = Get-Privilege $t.logicalName $t.appendToPrivilege
    $declared = [string]$t.appendToDepth
    $declaredRead = if ($t.appendToAboveRead) { [string]$t.appendToAboveRead.readDepth } else { $declared }
    $readDepth = Get-HeldDepth $core $read
    $appendToDepth = Get-HeldDepth $core $appendTo
    if ($readDepth -ne $declaredRead) {
        $depthDrift.Add(("{0,-44} {1,-40} live {2,-6} declared {3}" -f $core.Name, $read.Name, $readDepth, $declaredRead))
    }
    if ((Get-Rank $appendToDepth) -gt (Get-Rank $declared)) {
        $depthDrift.Add(("{0,-44} {1,-40} live {2,-6} declared {3} (above it; not lowered here)" -f $core.Name, $appendTo.Name, $appendToDepth, $declared))
    }
    # The target is the declared depth. Without a declared exception it is also capped at the live Read, so a Read
    # narrower than declared (reported above as drift) never leaves AppendTo above it.
    $target = $declared
    if (-not $t.appendToAboveRead -and (Get-Rank $readDepth) -lt (Get-Rank $target)) { $target = $readDepth }
    if ($target -ne 'None') { Add-Raise $core $appendTo $target "declared $declared; Read is $readDepth" }
}
foreach ($c in $linking.createdTables) {
    $create = Get-Privilege $c.logicalName $c.createPrivilege
    $append = Get-Privilege $c.logicalName $c.appendPrivilege
    $createDepth = Get-HeldDepth $core $create
    if ($createDepth -ne 'None') { Add-Raise $core $append $createDepth "Create is $createDepth" }
}

$writer = Get-Role $membership.writerRole.roleName
foreach ($name in $membership.writerRole.privileges) {
    Add-Raise $writer (Get-Privilege $membership.table $name) 'Global' 'membership writer'
}
foreach ($roleName in $membership.readOnlyRoles) {
    $role = Get-Role $roleName
    foreach ($name in $membership.removedFromReadOnlyRoles) {
        $p = Get-Privilege $membership.table $name
        $old = Get-HeldDepth $role $p
        if ($old -ne 'None') {
            $plan.Add([pscustomobject]@{ Role = $role; Privilege = $p; Old = $old; New = 'None'; Action = 'Remove'; Why = 'membership writes move to the writer role' })
        }
    }
}

# ── Report ──────────────────────────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host 'Roles:'
foreach ($r in $roleCache.Values | Sort-Object Name) {
    Write-Host ("  {0,-44} root role in {1}; {2} business-unit copies take its privileges" -f $r.Name, $(if ($r.InRootBu) { 'the root BU' } else { 'a child BU' }), $r.Copies)
}
Write-Host ''
if ($plan.Count -eq 0) { Write-Host 'Changes     : none pending.' -ForegroundColor Green }
else {
    Write-Host "Changes     : $($plan.Count)"
    foreach ($c in $plan | Sort-Object { $_.Role.Name }, { $_.Privilege.Name }) {
        Write-Host ("  {0,-44} {1,-40} {2,-6} -> {3,-6} ({4})" -f $c.Role.Name, $c.Privilege.Name, $c.Old, $c.New, $c.Why) -ForegroundColor Cyan
    }
}

Write-Host ''
Write-Host 'Depth drift (live Core User Read or AppendTo off the declared depth; never changed here):'
if ($depthDrift.Count -eq 0) { Write-Host '  none' }
foreach ($d in $depthDrift) { Write-Host "  $d" -ForegroundColor DarkYellow }

# Every live root role that can write memberships, checked against the allowlist (never changed here). Fail closed: a
# failed read stops the script before any write.
$writerTypes = @('Create', 'Write', 'Delete', 'Assign', 'Append')
try {
    $writeIds = @(Get-TablePrivileges $membership.table | Where-Object { [string]$_.Type -in $writerTypes })
    if ($writeIds.Count -ne $writerTypes.Count) {
        throw "Expected $($writerTypes.Count) write privileges ($($writerTypes -join ', ')) on $($membership.table) in metadata; found $($writeIds.Count)."
    }
    $roleById = @{}
    $holders = foreach ($w in $writeIds) {
        foreach ($row in (Invoke-DvGetAll "roleprivilegescollection?`$select=roleid&`$filter=privilegeid eq $($w.Id)")) {
            $rid = ([string]$row.roleid).ToLowerInvariant()
            if (-not $roleById.ContainsKey($rid)) { $roleById[$rid] = Invoke-DvGet "roles($rid)?`$select=name,_parentrootroleid_value" }
            $ro = $roleById[$rid]
            if (([string]$ro._parentrootroleid_value).ToLowerInvariant() -eq $rid) { [pscustomobject]@{ Role = [string]$ro.name; Privilege = $w.Name } }
        }
    }
}
catch {
    Write-Host "`nFAIL: could not list the roles that write $($membership.table): $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Nothing was changed. Fix the read and re-run.' -ForegroundColor Red
    exit 1
}
$planned = @($plan | Where-Object Action -eq 'Remove' | ForEach-Object { "$($_.Role.Name)|$($_.Privilege.Name)" })
$holders = @($holders | Sort-Object Role, Privilege -Unique)
$allowedHolders = @($holders | Where-Object { $allowedWriters.ContainsKey($_.Role) })
$unexpectedWriters = @($holders | Where-Object { -not $allowedWriters.ContainsKey($_.Role) -and $planned -notcontains "$($_.Role)|$($_.Privilege)" })
Write-Host ''
Write-Host "Root roles that write $($membership.table), in the allowlist ($(@($allowedHolders.Role | Sort-Object -Unique).Count) roles):"
foreach ($g in $allowedHolders | Group-Object Role) {
    Write-Host ("  {0,-44} {1} ({2})" -f $g.Name, (($g.Group.Privilege | ForEach-Object { $_ -replace "(?i)sprk_ContactOrganization$", '' }) -join ', '), $allowedWriters[$g.Name])
}
Write-Host "Root roles that write $($membership.table), NOT in the allowlist and not planned for removal ($($unexpectedWriters.Count)):"
if ($unexpectedWriters.Count -eq 0) { Write-Host '  none' }
foreach ($o in $unexpectedWriters) { Write-Host ("  {0,-44} {1}" -f $o.Role, $o.Privilege) -ForegroundColor Red }

# The allowlist's platform roles pass only while no person and no team holds them (any business-unit copy). Read-only;
# fail closed: a failed read stops the script before any write. Holders are named by id only.
$humanHolders = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($prn in $platformRoles) {
        $escaped = $prn -replace "'", "''"
        $copies = Invoke-DvGetAll "roles?`$select=roleid&`$filter=name eq '$escaped'"
        if ($copies.Count -eq 0) { throw "Platform role '$prn' was not found." }
        $users = 0; $apps = 0
        $userCache = @{}
        foreach ($c in $copies) {
            $rid = ([string]$c.roleid).ToLowerInvariant()
            foreach ($ur in (Invoke-DvGetAll "systemuserrolescollection?`$select=systemuserid&`$filter=roleid eq $rid")) {
                $uid = ([string]$ur.systemuserid).ToLowerInvariant()
                if (-not $userCache.ContainsKey($uid)) { $userCache[$uid] = Invoke-DvGet "systemusers($uid)?`$select=applicationid" }
                $users++
                if ($userCache[$uid].applicationid) { $apps++ } else { $humanHolders.Add("$prn : user $uid is not an application user") }
            }
            foreach ($tr in (Invoke-DvGetAll "teamrolescollection?`$select=teamid&`$filter=roleid eq $rid")) {
                $humanHolders.Add("$prn : team $(([string]$tr.teamid).ToLowerInvariant()) holds it")
            }
        }
        Write-Host ("Platform role {0,-30} {1} role row(s); {2} user holder(s), {3} of them application users" -f $prn, $copies.Count, $users, $apps)
    }
}
catch {
    Write-Host "`nFAIL: could not read the holders of the platform roles: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Nothing was changed. Fix the read and re-run.' -ForegroundColor Red
    exit 1
}
Write-Host "Platform-role holders that are a person or a team ($($humanHolders.Count)):"
if ($humanHolders.Count -eq 0) { Write-Host '  none' }
foreach ($hh in $humanHolders) { Write-Host "  $hh" -ForegroundColor Red }

# XML/live drift on the in-scope privileges, after the planned changes (reported, never changed here).
$scope = @()
foreach ($t in $linking.bindTargets) { $scope += @{ T = $t.logicalName; N = $t.readPrivilege }, @{ T = $t.logicalName; N = $t.appendToPrivilege } }
foreach ($c in $linking.createdTables) { $scope += @{ T = $c.logicalName; N = $c.createPrivilege }, @{ T = $c.logicalName; N = $c.appendPrivilege } }
foreach ($p in Get-TablePrivileges $membership.table) { $scope += @{ T = $membership.table; N = $p.Name } }
$driftRoleNames = @($roleCache.Keys) + @($DriftRoles) | Sort-Object -Unique
Write-Host ''
Write-Host 'XML/live drift on the in-scope privileges (after the planned changes):'
$driftCount = 0
foreach ($rn in $driftRoleNames) {
    $file = Join-Path $RolesXmlDir "$rn.xml"
    if (-not (Test-Path -LiteralPath $file)) { Write-Host "  $rn : no role XML in the repo (live-only role)" -ForegroundColor DarkYellow; continue }
    $xmlLevels = @{}
    foreach ($e in ([xml](Get-Content -LiteralPath $file -Raw -Encoding UTF8)).SelectNodes('//RolePrivilege')) { $xmlLevels[$e.name.ToLowerInvariant()] = $e.level }
    $role = Get-Role $rn
    foreach ($s in $scope) {
        $p = Get-Privilege $s.T $s.N
        $live = Get-HeldDepth $role $p
        $pending = $plan | Where-Object { $_.Role.Id -eq $role.Id -and $_.Privilege.Id -eq $p.Id } | Select-Object -First 1
        if ($pending) { $live = $pending.New }
        $xml = if ($xmlLevels.ContainsKey($p.Name.ToLowerInvariant())) { $xmlLevels[$p.Name.ToLowerInvariant()] } else { 'None' }
        if ($xml -ne $live) { $driftCount++; Write-Host ("  {0,-44} {1,-40} XML {2,-6} live {3}" -f $rn, $p.Name, $xml, $live) -ForegroundColor DarkYellow }
    }
}
if ($driftCount -eq 0) { Write-Host '  none' }

# ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────────
if ($Verify) {
    $reasons = @()
    if ($plan.Count -gt 0) { $reasons += "$($plan.Count) change(s) pending" }
    if ($depthDrift.Count -gt 0) { $reasons += "$($depthDrift.Count) depth drift finding(s)" }
    if ($unexpectedWriters.Count -gt 0) { $reasons += "$($unexpectedWriters.Count) membership writer(s) not in the allowlist" }
    if ($humanHolders.Count -gt 0) { $reasons += "$($humanHolders.Count) person or team holder(s) of an allowlisted platform role" }
    if ($reasons.Count -eq 0) { Write-Host "`nVERIFY PASS: the live roles match both declared sets." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: $($reasons -join '; ') (listed above)." -ForegroundColor Red
    exit 1
}
if ($plan.Count -eq 0) { Write-Host "`nNothing to change." -ForegroundColor Green; exit 0 }
if ($IsDryRun) { Write-Host "`nDRY RUN: no writes. Re-run with -Apply to make the $($plan.Count) change(s) above." -ForegroundColor Cyan; exit 0 }

# ── Apply, then read every role back ────────────────────────────────────────────────────────────────────────
foreach ($group in $plan | Group-Object { $_.Role.Id }) {
    $role = $group.Group[0].Role
    $raises = @($group.Group | Where-Object Action -eq 'Raise')
    if ($raises.Count -gt 0) {
        Invoke-DvPost "roles($($role.Id))/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{
            Privileges = @($raises | ForEach-Object {
                    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = $_.New; PrivilegeId = $_.Privilege.Id; PrivilegeName = $_.Privilege.Name; BusinessUnitId = $role.BusinessUnitId }
                })
        }
    }
    foreach ($r in @($group.Group | Where-Object Action -eq 'Remove')) {
        # The bound action's one parameter is 'Privilege', an entity reference (Web API $metadata), not 'PrivilegeId'.
        Invoke-DvPost "roles($($role.Id))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
            Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $r.Privilege.Id }
        }
    }
}

$failed = @()
foreach ($group in $plan | Group-Object { $_.Role.Id }) {
    $role = $group.Group[0].Role
    $after = @{}
    foreach ($h in @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$($role.Id)").RolePrivileges)) { $after[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
    foreach ($c in $group.Group) {
        $now = if ($after.ContainsKey($c.Privilege.Id)) { [string]$after[$c.Privilege.Id].Depth } else { 'None' }
        if ($now -ne $c.New) { $failed += "$($role.Name) $($c.Privilege.Name): expected $($c.New), read back $now" }
    }
    $unrequested = @($after.Keys | Where-Object { -not $role.Held.ContainsKey($_) -and -not ($group.Group.Privilege.Id -contains $_) })
    if ($unrequested.Count -gt 0) {
        Write-Host "WARNING: $($role.Name) also gained $($unrequested.Count) privilege(s) that were not requested: $(($unrequested | ForEach-Object { $after[$_].PrivilegeName }) -join ', '). Not removed here." -ForegroundColor Yellow
    }
}
if ($failed.Count -gt 0) { throw "Read-back did not match:`n  $($failed -join "`n  ")" }
Write-Host "`nAPPLIED and read back: $($plan.Count) change(s)." -ForegroundColor Green
Write-Host 'Dataverse caches principal privileges; a probe straight after this can show the old result. Re-check until stable.'
exit 0
