#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies the owner's role rulings on task 046's gaps (config/feature-role-privileges.json) to one environment.
    Dry run by default.

.DESCRIPTION
    Reads one declared set and brings the LIVE roles to it:

      config/feature-role-privileges.json
          Each change names a role, a table, a privilege and the depth the role holds it at. The privilege is added at
          that depth, or raised to it. Never lowered, never removed, and nothing outside the declared set is touched.
          The role files under src/dataverse/solutions/SpaarkeMaster/Roles hold each change at exactly its depth
          (tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs checks it); the script checks it again before it
          connects, and stops when they disagree.

    MODES
      (default)  Dry run. Prints every change as role, privilege, old depth -> new depth, and the number of
                 business-unit copies that take it. Zero writes.
      -Apply     Makes the changes on each ROOT role (AddPrivilegesRole), then reads back the root role and every
                 business-unit copy and confirms each change. Each read-back re-reads (up to 8 tries, 4 seconds apart)
                 until every declared privilege shows, before it records a failure. A failed AddPrivilegesRole, or a read-back
                 that cannot be completed or does not match, prints the changes applied in this run and the ones not applied,
                 and exits non-zero; re-run -Apply to converge. Names anything the platform added that was not asked for.
                 When nothing is pending but a live depth is above the declared one or a business-unit copy differs, -Apply
                 exits 1 with the report -Verify prints (it never lowers a depth or edits a copy).
      -Verify    Read-only. Exits 1 when any change is still pending, a live depth is above the declared one, or a
                 business-unit copy differs from its root role on a declared privilege; 0 when the live roles match.

    FAIL CLOSED
      Every read (token, organization, business units, roles, every copy's privileges, table metadata, and each declared
      privilege's allowed depths) happens before any write. A failed read stops the script with exit 1 and nothing is
      changed. So does a role that is not a root role in the root business unit, and a declared depth the privilege
      cannot be held at.

    ALSO REPORTED (never changed here)
      - Each role is resolved by name to exactly one ROOT role; the number of business-unit copies is printed.
      - Depth above declared: a live depth deeper than the declared one (never lowered here).
      - Copy drift: a business-unit copy whose depth on a declared privilege differs from its root role's.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only pass/fail check. Cannot be combined with -Apply.

.EXAMPLE
    .\Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the exact diff, with no writes.

.EXAMPLE
    .\Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = the live roles hold the declared set; exit 1 = a change is still pending (each is named).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string]$ConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'feature-role-privileges.json'),

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

# ── The declared set, checked against the role XML before anything is read live ─────────────────────────────
if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "Config not found: $ConfigPath" }
$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.schemaVersion -ne 1) { throw 'Unsupported schemaVersion in the config.' }
$changes = @($config.changes)
if ($changes.Count -eq 0) { throw 'The config declares no change.' }
$seen = @{}
$xmlByRole = @{}
foreach ($c in $changes) {
    foreach ($field in 'gap', 'role', 'table', 'privilege', 'depth', 'from') {
        if (-not $c.$field) { throw "A change has no '$field'. Fix the config." }
    }
    if ($c.depth -eq 'None') { throw "$($c.role) $($c.privilege): 'depth' None would remove it; this script only adds or raises." }
    if ((Get-Rank $c.from) -ge (Get-Rank $c.depth)) { throw "$($c.role) $($c.privilege): 'from' $($c.from) is not below 'depth' $($c.depth)." }
    $key = "$($c.role)|$($c.privilege)".ToLowerInvariant()
    if ($seen.ContainsKey($key)) { throw "$($c.role) $($c.privilege) is declared more than once." }
    $seen[$key] = $true
    if (-not $xmlByRole.ContainsKey($c.role)) {
        $file = Join-Path $RolesXmlDir "$($c.role).xml"
        if (-not (Test-Path -LiteralPath $file)) { throw "No role XML for '$($c.role)': $file" }
        $levels = @{}
        foreach ($e in ([xml](Get-Content -LiteralPath $file -Raw -Encoding UTF8)).SelectNodes('//RolePrivilege')) { $levels[$e.name.ToLowerInvariant()] = [string]$e.level }
        $xmlByRole[$c.role] = $levels
    }
    $xmlLevel = $xmlByRole[$c.role][$c.privilege.ToLowerInvariant()]
    if ($xmlLevel -ne $c.depth) {
        throw "$($c.role) $($c.privilege): the role XML holds '$xmlLevel', the config declares '$($c.depth)'. The two must agree (FeatureRolePrivilegeGuardTests)."
    }
}

Write-Host "Environment : $EnvironmentUrl"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

# ── Every read happens here, before any write. Fail closed: a failed read exits 1 and nothing is changed. ─────
try {
    # Auth: the operator's own az CLI identity, held in memory only. No secrets in this script or the repo.
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
    function Get-HeldMap([string]$RoleId) {
        $held = @{}
        foreach ($h in @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$RoleId").RolePrivileges)) {
            $held[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h
        }
        $held
    }

    $orgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
    Write-Host "Organization: $orgName"
    $rootBus = @((Invoke-DvGet 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null').value)
    if ($rootBus.Count -ne 1) { throw "Expected one root business unit; found $($rootBus.Count)." }
    $rootBuId = $rootBus[0].businessunitid

    # Roles: each name resolves to exactly one ROOT role; every business-unit copy is read too.
    $roleCache = @{}
    foreach ($name in @($changes.role | Sort-Object -Unique)) {
        # The quotes are doubled first, then the whole value is URL-encoded (a name with &, #, + or % must not alter the query).
        $escaped = [Uri]::EscapeDataString(($name -replace "'", "''"))
        $all = Invoke-DvGetAll "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$escaped'"
        $roots = @($all | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
        if ($roots.Count -ne 1) { throw "Expected exactly one root role named '$name'; found $($roots.Count) (of $($all.Count) rows)." }
        $r = $roots[0]
        # Copied from scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1: a role outside the root business unit is never acted on.
        if ($r._businessunitid_value -ne $rootBuId) { throw "Root role '$name' is not in the root business unit. Nothing was changed." }
        $copies = @($all | Where-Object { $_._parentrootroleid_value -eq $r.roleid -and $_.roleid -ne $r.roleid })
        $strays = @($all | Where-Object { $_._parentrootroleid_value -ne $r.roleid })
        if ($strays.Count -gt 0) { throw "'$name': $($strays.Count) role row(s) with that name belong to another root role." }
        $roleCache[$name] = [pscustomobject]@{
            Name = $name; Id = $r.roleid; BusinessUnitId = $r._businessunitid_value
            Held = (Get-HeldMap $r.roleid)
            Copies = @($copies | ForEach-Object { [pscustomobject]@{ Id = $_.roleid; BusinessUnitId = $_._businessunitid_value; Held = (Get-HeldMap $_.roleid) } })
        }
    }

    # Privileges by name, from table METADATA (never guessed).
    $privCache = @{}
    foreach ($c in $changes) {
        if (-not $privCache.ContainsKey($c.table)) {
            $privCache[$c.table] = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($c.table)')/Privileges").value | ForEach-Object {
                    [pscustomobject]@{ Name = $_.Name; Id = $_.PrivilegeId.ToString().ToLowerInvariant() }
                })
        }
        $match = @($privCache[$c.table] | Where-Object { $_.Name -ieq $c.privilege })
        if ($match.Count -ne 1) { throw "Table '$($c.table)' has no privilege named '$($c.privilege)' in metadata. Fix the config." }
        $c | Add-Member -NotePropertyName PrivilegeRef -NotePropertyValue $match[0] -Force
    }

    # Depth pre-check: the privilege must be allowed at the declared depth (privilege.canbebasic / canbelocal / canbedeep / canbeglobal),
    # decided here, before any write, so AddPrivilegesRole is never sent a depth the platform refuses half way through a run.
    $depthFlag = @{ Basic = 'canbebasic'; Local = 'canbelocal'; Deep = 'canbedeep'; Global = 'canbeglobal' }
    $allowedCache = @{}
    foreach ($c in $changes) {
        $id = $c.PrivilegeRef.Id
        if (-not $allowedCache.ContainsKey($id)) {
            $allowedCache[$id] = Invoke-DvGet "privileges($id)?`$select=name,canbebasic,canbelocal,canbedeep,canbeglobal"
        }
        $flag = $depthFlag[[string]$c.depth]
        if (-not $flag) { throw "$($c.PrivilegeRef.Name): depth '$($c.depth)' is not one of Basic, Local, Deep, Global." }
        if ($allowedCache[$id].$flag -ne $true) {
            throw "Privilege '$($c.PrivilegeRef.Name)' ($($c.role)) cannot be held at depth '$($c.depth)' on this environment ($flag is not true). Fix the config."
        }
    }
}
catch {
    Write-Host "`nFAIL: a read failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Nothing was changed. Fix the read and re-run.' -ForegroundColor Red
    exit 1
}

function Get-Depth($Held, $Privilege) {
    $h = $Held[$Privilege.Id]
    if ($null -eq $h) { 'None' } else { [string]$h.Depth }
}

# ── The plan ────────────────────────────────────────────────────────────────────────────────────────────────
$plan = [System.Collections.Generic.List[object]]::new()
$aboveDeclared = [System.Collections.Generic.List[string]]::new()
$copyDrift = [System.Collections.Generic.List[string]]::new()
$fromDrift = [System.Collections.Generic.List[string]]::new()
foreach ($c in $changes) {
    $role = $roleCache[$c.role]
    $live = Get-Depth $role.Held $c.PrivilegeRef
    # Informational: the plan always uses the live depth; a live depth other than the config's 'from' means the role
    # changed since the config was written (or since the last dry run).
    if ($live -ne [string]$c.from) {
        $fromDrift.Add(("{0,-36} {1,-34} config from {2,-6} live {3}" -f $role.Name, $c.PrivilegeRef.Name, $c.from, $live))
    }
    if ((Get-Rank $live) -lt (Get-Rank $c.depth)) {
        $plan.Add([pscustomobject]@{ Role = $role; Privilege = $c.PrivilegeRef; Old = $live; New = [string]$c.depth; Gap = [string]$c.gap })
    }
    elseif ((Get-Rank $live) -gt (Get-Rank $c.depth)) {
        $aboveDeclared.Add(("{0,-36} {1,-34} live {2,-6} declared {3} (not lowered here)" -f $role.Name, $c.PrivilegeRef.Name, $live, $c.depth))
    }
    foreach ($copy in $role.Copies) {
        $copyDepth = Get-Depth $copy.Held $c.PrivilegeRef
        if ($copyDepth -ne $live) {
            $copyDrift.Add(("{0,-36} {1,-34} copy {2} holds {3,-6} root holds {4}" -f $role.Name, $c.PrivilegeRef.Name, $copy.Id, $copyDepth, $live))
        }
    }
}

# ── Report ──────────────────────────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host 'Roles:'
foreach ($r in $roleCache.Values | Sort-Object Name) {
    Write-Host ("  {0,-36} root role in the root BU; {1} business-unit copies take its privileges" -f $r.Name, $r.Copies.Count)
}
Write-Host ''
if ($plan.Count -eq 0) { Write-Host 'Changes     : none pending.' -ForegroundColor Green }
else {
    Write-Host "Changes     : $($plan.Count)"
    foreach ($p in $plan | Sort-Object { $_.Role.Name }, { $_.Privilege.Name }) {
        Write-Host ("  {0,-36} {1,-34} {2,-6} -> {3,-6} ({4}; {5} business-unit copies)" -f $p.Role.Name, $p.Privilege.Name, $p.Old, $p.New, $p.Gap, $p.Role.Copies.Count) -ForegroundColor Cyan
    }
}
Write-Host ''
Write-Host 'Live depth above the declared one (never lowered here):'
if ($aboveDeclared.Count -eq 0) { Write-Host '  none' }
foreach ($d in $aboveDeclared) { Write-Host "  $d" -ForegroundColor DarkYellow }
Write-Host "Live depth differs from the config's 'from' (informational: drift since the config or the last dry run; the plan uses the live depth):"
if ($fromDrift.Count -eq 0) { Write-Host '  none' }
foreach ($d in $fromDrift) { Write-Host "  $d" -ForegroundColor DarkYellow }
Write-Host 'Business-unit copies that differ from their root role on a declared privilege:'
if ($copyDrift.Count -eq 0) { Write-Host '  none' }
foreach ($d in $copyDrift) { Write-Host "  $d" -ForegroundColor DarkYellow }

# ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────────
# What keeps the live roles from matching the declared set: a pending change, a depth above the declared one, a copy that differs.
$reasons = @()
if ($plan.Count -gt 0) { $reasons += "$($plan.Count) change(s) pending" }
if ($aboveDeclared.Count -gt 0) { $reasons += "$($aboveDeclared.Count) live depth(s) above the declared one" }
if ($copyDrift.Count -gt 0) { $reasons += "$($copyDrift.Count) business-unit copy difference(s)" }
if ($Verify) {
    if ($reasons.Count -eq 0) { Write-Host "`nVERIFY PASS: the live roles hold the declared set." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: $($reasons -join '; ') (listed above)." -ForegroundColor Red
    exit 1
}
if ($plan.Count -eq 0) {
    # Nothing to add or raise. -Apply does not report success over a state it cannot fix: it exits 1 with the report -Verify prints.
    if (-not $IsDryRun -and $reasons.Count -gt 0) {
        Write-Host "`nAPPLY: nothing to add or raise, but the live roles do not match the declared set: $($reasons -join '; ') (listed above). This script never lowers a depth or edits a copy; fix it in the environment and re-run -Verify." -ForegroundColor Red
        exit 1
    }
    Write-Host "`nNothing to change." -ForegroundColor Green; exit 0
}
if ($IsDryRun) { Write-Host "`nDRY RUN: no writes. Re-run with -Apply to make the $($plan.Count) change(s) above." -ForegroundColor Cyan; exit 0 }

# ── Apply on each root role, then read back the root role and every business-unit copy ───────────────────────
# Re-read until the test passes (role edits settle after a few seconds), at most $Tries times
# (the settle approach of scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1).
function Wait-Settled([scriptblock]$Read, [scriptblock]$Test, [int]$Tries = 8, [int]$Seconds = 4) {
    $v = $null
    for ($i = 0; $i -lt $Tries; $i++) {
        $v = & $Read
        if (& $Test $v) { return $v }
        Start-Sleep -Seconds $Seconds
    }
    return $v
}

$applied = [System.Collections.Generic.List[string]]::new()
$phase = 'AddPrivilegesRole'
$failed = @()
$failedRole = $null
# The writes AND the read-back share one try: whichever fails, the catch prints what was applied, what was not, and what to do
# next (a read-back that cannot be completed used to end in the raw error alone).
try {
    foreach ($group in $plan | Group-Object { $_.Role.Id }) {
        $role = $group.Group[0].Role
        $failedRole = $role.Name
        $body = @{
            Privileges = @($group.Group | ForEach-Object {
                    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = $_.New; PrivilegeId = $_.Privilege.Id; PrivilegeName = $_.Privilege.Name; BusinessUnitId = $role.BusinessUnitId }
                })
        }
        $json = $body | ConvertTo-Json -Depth 6
        Invoke-RestMethod -Method Post -Uri "$Api/roles($($role.Id))/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
        foreach ($p in $group.Group) { $applied.Add("$($role.Name) $($p.Privilege.Name) $($p.Old) -> $($p.New)") }
    }

    # Read back the root role and every business-unit copy.
    $phase = 'read-back'
    foreach ($group in $plan | Group-Object { $_.Role.Id }) {
        $role = $group.Group[0].Role
        $failedRole = $role.Name
        $declared = @($group.Group)
        $targets = @([pscustomobject]@{ Label = 'root'; Id = $role.Id }) + @($role.Copies | ForEach-Object { [pscustomobject]@{ Label = "copy $($_.Id)"; Id = $_.Id } })
        foreach ($t in $targets) {
            $tid = $t.Id
            $read = { Get-HeldMap $tid }   # dynamic scope: $tid is read when Wait-Settled runs it
            $after = Wait-Settled $read { param($m) @($declared | Where-Object { (Get-Depth $m $_.Privilege) -ne $_.New }).Count -eq 0 }
            foreach ($p in $declared) {
                $now = Get-Depth $after $p.Privilege
                if ($now -ne $p.New) { $failed += "$($role.Name) ($($t.Label)) $($p.Privilege.Name): expected $($p.New), read back $now" }
            }
            if ($t.Label -eq 'root') {
                $unrequested = @($after.Keys | Where-Object { -not $role.Held.ContainsKey($_) -and -not ($group.Group.Privilege.Id -contains $_) })
                if ($unrequested.Count -gt 0) {
                    Write-Host "WARNING: $($role.Name) also gained $($unrequested.Count) privilege(s) that were not requested: $(($unrequested | ForEach-Object { $after[$_].PrivilegeName }) -join ', '). Not removed here." -ForegroundColor Yellow
                }
            }
        }
    }
    if ($failed.Count -gt 0) { throw "Read-back did not match after 8 tries of 4 seconds:`n  $($failed -join "`n  ")" }
}
catch {
    Write-Host "`nFAIL: $phase on '$failedRole' failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Applied in this run before the failure ($($applied.Count)):" -ForegroundColor Red
    if ($applied.Count -eq 0) { Write-Host '  none' -ForegroundColor Red }
    foreach ($a in $applied) { Write-Host "  $a" -ForegroundColor Red }
    $notApplied = @($plan | Where-Object { $applied -notcontains "$($_.Role.Name) $($_.Privilege.Name) $($_.Old) -> $($_.New)" })
    Write-Host "Not applied ($($notApplied.Count)):" -ForegroundColor Red
    if ($notApplied.Count -eq 0) { Write-Host '  none' -ForegroundColor Red }
    foreach ($n in $notApplied) { Write-Host "  $($n.Role.Name) $($n.Privilege.Name) $($n.Old) -> $($n.New)" -ForegroundColor Red }
    if ($phase -eq 'read-back') { Write-Host 'Every write was sent; the read-back did not confirm all of them. Re-run -Verify to see what the live roles hold.' -ForegroundColor Red }
    else { Write-Host 'Re-run -Apply to converge (the script only adds or raises). Re-run -Verify to see what is still pending.' -ForegroundColor Red }
    throw
}
Write-Host "`nAPPLIED and read back on every business-unit copy: $($plan.Count) change(s)." -ForegroundColor Green
Write-Host 'Dataverse caches principal privileges; a probe straight after this can show the old result. Re-check until stable.'
exit 0
