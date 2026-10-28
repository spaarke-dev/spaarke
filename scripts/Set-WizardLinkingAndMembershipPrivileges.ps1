#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies issue #2382's role changes to one environment: Spaarke Core User can link wizard lookups at its Read depth,
    and contact-organization membership writes move to the Spaarke Access Administrator role.
    Dry run by default.

.DESCRIPTION
    Reads two declared sets and brings the LIVE roles to them:

      config/core-user-wizard-linking-role.json
          For each bind target: AppendTo on the Core User role is raised to the role's own live Read depth on that
          table. For each created table: Append is raised to the role's live Create depth. Adds or raises only; never
          touches Read, Create, Write, Delete, Share or Assign.

      config/contact-organization-membership-role.json
          The writer role gets the listed privileges at Global (adds or raises only). Each read-only role loses the
          listed write privileges on the table; its Read (and Share) stay.

    MODES
      (default)  Dry run. Prints every change as role, privilege, old depth -> new depth. Zero writes.
      -Apply     Makes the changes (AddPrivilegesRole, RemovePrivilegeRole), then reads every role back and confirms
                 each change. Names anything the platform added that was not asked for.
      -Verify    Read-only. Exits 1 when any change is still pending (drift), 0 when the live roles match both sets.

    ALSO REPORTED (never changed here)
      - Each role is resolved by name to exactly one ROOT role (its business-unit copies take their privileges from
        it); the number of copies is printed.
      - Other live root roles that hold Create or Write on the membership table.
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
foreach ($t in $linking.bindTargets) {
    $read = Get-Privilege $t.logicalName $t.readPrivilege
    $appendTo = Get-Privilege $t.logicalName $t.appendToPrivilege
    $readDepth = Get-HeldDepth $core $read
    if ($readDepth -ne 'None') { Add-Raise $core $appendTo $readDepth "Read is $readDepth" }
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

# Other root roles that can still write memberships (reported, never changed here).
try {
    $writeIds = @(Get-TablePrivileges $membership.table | Where-Object { $_.Type -in 'Create', 'Write' })
    $holders = foreach ($w in $writeIds) {
        foreach ($row in @((Invoke-DvGet "roleprivilegescollection?`$select=roleid,privilegedepthmask&`$filter=privilegeid eq $($w.Id)").value)) {
            $ro = Invoke-DvGet "roles($($row.roleid))?`$select=name,_parentrootroleid_value"
            if ($ro._parentrootroleid_value -eq $row.roleid) { [pscustomobject]@{ Role = $ro.name; Privilege = $w.Name } }
        }
    }
    $planned = @($plan | Where-Object Action -eq 'Remove' | ForEach-Object { "$($_.Role.Name)|$($_.Privilege.Name)" })
    $others = @($holders | Where-Object { $_.Role -ne $membership.writerRole.roleName -and $planned -notcontains "$($_.Role)|$($_.Privilege)" } |
        Sort-Object Role, Privilege -Unique)
    Write-Host ''
    Write-Host "Other root roles that keep Create or Write on $($membership.table) ($($others.Count)):"
    foreach ($o in $others) { Write-Host ("  {0,-44} {1}" -f $o.Role, $o.Privilege) -ForegroundColor DarkYellow }
}
catch { Write-Host "Could not list the other membership writers: $($_.Exception.Message)" -ForegroundColor Yellow }

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
    if ($plan.Count -eq 0) { Write-Host "`nVERIFY PASS: the live roles match both declared sets." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: $($plan.Count) change(s) pending (listed above)." -ForegroundColor Red
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
        Invoke-DvPost "roles($($role.Id))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{ PrivilegeId = $r.Privilege.Id }
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
