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

    REMOVALS (-Remove)
      A separate, explicit block: config "removals" lists role, privilege and the depth it is expected to be held at now. The add
      path above never removes anything: it validates that block (a stale entry fails closed) but never acts on it. -Remove never adds or
      raises. -Remove acts on exactly the
      listed privileges (RemovePrivilegeRole on the ROOT role, one call per privilege), nothing else: after each run the root role
      is compared with its state before and any other difference fails the run. A privilege held at a depth other than the expected
      one is not removed (exit 1, nothing changed). The role XML must not hold a listed privilege (checked before connecting).
      -Remove (dry run), -Remove -Apply and -Remove -Verify work like the modes below, with "absent on the root role and on every
      business-unit copy" as the target. Plain -Verify checks the adds AND the removals.

    ROLLBACK OF THE REMOVALS (-Restore)
      Puts every privilege of the removals block back on its ROOT role at the block's expectedDepth (AddPrivilegesRole), the depth
      -Remove required it to be held at before it removed it. -Remove refuses a privilege held at any other depth, so after a -Remove
      -Apply whose dry run listed every entry as pending, -Restore -Apply restores exactly what was removed; the -Remove dry run says so,
      or names the entries that were already absent. -Restore (dry run), -Restore -Apply and -Restore -Verify work like the modes below,
      with "held at expectedDepth on the root role and every business-unit copy" as the target. A privilege held at another depth stops
      the run (exit 1, nothing changed). After the write, nothing else on the root role may change. Never removes, never touches the adds.
      -Gap limits -Remove and -Restore to the removals listed under the named gaps (for example -Gap R3). When an entry of the block is
      already absent on an environment (an earlier removal applied there), the -Remove dry run prints the rollback with the -Gap that
      makes it exact, so -Restore never puts back a privilege this run did not remove.

    MODES
      (default)  Dry run. Prints every change as role, privilege, old depth -> new depth, and the number of
                 business-unit copies that take it. Zero writes.
      -Apply     Makes the changes on each ROOT role (AddPrivilegesRole), then reads back the root role and every
                 business-unit copy and confirms each change. Each read-back re-reads (up to 8 tries, 4 seconds apart)
                 until every declared privilege shows, before it records a failure. A failed AddPrivilegesRole, or a read-back
                 that cannot be completed or does not match, prints the changes applied in this run and the ones not applied,
                 and exits non-zero; re-run -Apply to converge. Names anything the platform added that was not asked for.
                 When nothing is pending but a live depth is above the declared one or a business-unit copy differs, -Apply
                 exits 1 with the report -Verify prints (it never lowers a depth or edits a copy). The same holds after a
                 successful read-back: the written roles are re-read, and a depth above the declared one, a copy that differs
                 from its root role, a held-at-another-depth removal or a copy still holding a removed privilege is printed in
                 the same report and exits 1 (a run that leaves the roles off the declared set is not a success).
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

.PARAMETER Remove
    Works on the config's "removals" block instead of its "changes": dry run by default, -Remove -Apply removes, -Remove -Verify
    checks. Never adds or raises.

.PARAMETER Restore
    The rollback of the removals: puts each privilege of the config's "removals" block back at its expectedDepth. Dry run by default,
    -Restore -Apply restores, -Restore -Verify checks. Cannot be combined with -Remove.

.PARAMETER Gap
    With -Remove or -Restore only: works on the removals listed under these gaps (the entries' 'gap') and no others.

.EXAMPLE
    .\Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Remove
    Dry run of the removals; it also prints the one-command rollback (-Restore -Apply, with -Gap when that makes it exact).

.EXAMPLE
    .\Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Restore -Gap R3
    Dry run of the rollback of the R3 removals only.

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

    [switch]$Remove,

    [switch]$Restore,

    [string[]]$Gap,

    [string]$ConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'feature-role-privileges.json'),

    [string]$RolesXmlDir = (Join-Path $PSScriptRoot '..' 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'Roles')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
if ($Remove -and $Restore) { throw '-Remove and -Restore are opposite modes; pass one.' }
if ($Gap -and -not ($Remove -or $Restore)) { throw '-Gap limits -Remove or -Restore to the removals of the gaps named; pass one of them.' }
$Gap = @($Gap | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$gapFlag = if ($Gap.Count) { "-Gap $($Gap -join ',') " } else { '' }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
# The adds are worked on unless -Remove or -Restore; the removals are worked on with -Remove, and checked by every -Verify but
# -Restore -Verify; -Restore works on the removals block as its rollback.
$checkAdds = -not ($Remove.IsPresent -or $Restore.IsPresent)
$checkRemovals = ($Remove.IsPresent -or $Verify.IsPresent) -and -not $Restore.IsPresent
$checkRestores = $Restore.IsPresent
$modeFlag = $(if ($Remove) { '-Remove ' } elseif ($Restore) { '-Restore ' } else { '' }) + $gapFlag
$Api = "$EnvironmentUrl/api/data/v9.2"
$DepthOrder = @('None', 'Basic', 'Local', 'Deep', 'Global')
function Get-Rank([string]$Depth) {
    $i = [array]::IndexOf($DepthOrder, $Depth)
    if ($i -lt 0) { throw "Unknown depth '$Depth'." }
    $i
}

# The privileges a role file holds, by lower-cased name (the role XML is what the ArchTests guard pins).
function Get-XmlLevels([string]$Role) {
    $file = Join-Path $RolesXmlDir "$Role.xml"
    if (-not (Test-Path -LiteralPath $file)) { throw "No role XML for '$Role': $file" }
    $levels = @{}
    foreach ($e in ([xml](Get-Content -LiteralPath $file -Raw -Encoding UTF8)).SelectNodes('//RolePrivilege')) { $levels[$e.name.ToLowerInvariant()] = [string]$e.level }
    $levels
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
    if (-not $xmlByRole.ContainsKey($c.role)) { $xmlByRole[$c.role] = Get-XmlLevels $c.role }
    $xmlLevel = $xmlByRole[$c.role][$c.privilege.ToLowerInvariant()]
    if ($xmlLevel -ne $c.depth) {
        throw "$($c.role) $($c.privilege): the role XML holds '$xmlLevel', the config declares '$($c.depth)'. The two must agree (FeatureRolePrivilegeGuardTests)."
    }
}

# The removals block (separate from the adds): each entry names a role, a privilege and the depth it is expected at now.
$removals = if ($config.PSObject.Properties['removals']) { @($config.removals | Where-Object { $_ }) } else { @() }
$seenRemoval = @{}
foreach ($r in $removals) {
    foreach ($field in 'gap', 'role', 'table', 'privilege', 'expectedDepth') {
        if (-not $r.$field) { throw "A removal has no '$field'. Fix the config." }
    }
    if ((Get-Rank $r.expectedDepth) -lt 1) { throw "$($r.role) $($r.privilege): 'expectedDepth' must be Basic, Local, Deep or Global." }
    $key = "$($r.role)|$($r.privilege)".ToLowerInvariant()
    if ($seenRemoval.ContainsKey($key)) { throw "$($r.role) $($r.privilege) is listed for removal more than once." }
    if ($seen.ContainsKey($key)) { throw "$($r.role) $($r.privilege) is both added and removed in the config." }
    $seenRemoval[$key] = $true
    if (-not $xmlByRole.ContainsKey($r.role)) { $xmlByRole[$r.role] = Get-XmlLevels $r.role }
    if ($xmlByRole[$r.role].ContainsKey($r.privilege.ToLowerInvariant())) {
        throw "$($r.role) $($r.privilege): the role XML still holds it, the config removes it. The two must agree (FeatureRolePrivilegeGuardTests)."
    }
}
if ($Gap.Count) {
    $unknownGaps = @($Gap | Where-Object { $g = $_; -not @($removals | Where-Object { $_.gap -eq $g }).Count })
    if ($unknownGaps.Count) { throw "-Gap: the removals block lists no entry under $($unknownGaps -join ', ')." }
    $removals = @($removals | Where-Object { $Gap -contains $_.gap })
}
if (($Remove -or $Restore) -and $removals.Count -eq 0) { throw 'The config declares no removal.' }

Write-Host "Environment : $EnvironmentUrl"
Write-Host ("Mode        : {0}{1}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }), $(if ($Remove) { ' - REMOVALS' } elseif ($Restore) { ' - RESTORE (rollback of the removals)' } else { '' }))

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
    $scope = @(); if ($checkAdds) { $scope += $changes }; if ($checkRemovals -or $checkRestores) { $scope += $removals }
    foreach ($name in @($scope.role | Sort-Object -Unique)) {
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
    foreach ($c in $scope) {
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
    # The adds at their 'depth'; with -Restore, the removals at their 'expectedDepth' (the depth the rollback writes).
    $toWrite = @($(if ($checkAdds) { $changes | ForEach-Object { [pscustomobject]@{ Entry = $_; Depth = [string]$_.depth } } }))
    if ($checkRestores) { $toWrite += @($removals | ForEach-Object { [pscustomobject]@{ Entry = $_; Depth = [string]$_.expectedDepth } }) }
    foreach ($w in $toWrite) {
        $c = $w.Entry
        $id = $c.PrivilegeRef.Id
        if (-not $allowedCache.ContainsKey($id)) {
            $allowedCache[$id] = Invoke-DvGet "privileges($id)?`$select=name,canbebasic,canbelocal,canbedeep,canbeglobal"
        }
        $flag = $depthFlag[$w.Depth]
        if (-not $flag) { throw "$($c.PrivilegeRef.Name): depth '$($w.Depth)' is not one of Basic, Local, Deep, Global." }
        if ($allowedCache[$id].$flag -ne $true) {
            throw "Privilege '$($c.PrivilegeRef.Name)' ($($c.role)) cannot be held at depth '$($w.Depth)' on this environment ($flag is not true). Fix the config."
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

# ── The state ───────────────────────────────────────────────────────────────────────────────────────────────
# What the live roles (as held in $roleCache) hold against the declared set: the plan, and everything that keeps them from matching it. Built
# once after the reads, and again after a successful read-back (so -Apply can report what it cannot fix).
function Get-State {
    $plan = [System.Collections.Generic.List[object]]::new()
    $aboveDeclared = [System.Collections.Generic.List[string]]::new()
    $copyDrift = [System.Collections.Generic.List[string]]::new()
    $fromDrift = [System.Collections.Generic.List[string]]::new()
    foreach ($c in @($(if ($checkAdds) { $changes }))) {
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

    # The removal plan: only the privileges the removals block lists. A removal is pending while the privilege is held on the root role
    # (at the expected depth) or on any business-unit copy. A root role that holds it at another depth than expected is a mismatch: the
    # run stops (nothing is removed) and -Verify reports it.
    $removalPlan = [System.Collections.Generic.List[object]]::new()
    $removalMismatch = [System.Collections.Generic.List[string]]::new()
    $removalCopies = [System.Collections.Generic.List[string]]::new()
    if ($checkRemovals) {
        foreach ($r in $removals) {
            $role = $roleCache[$r.role]
            $live = Get-Depth $role.Held $r.PrivilegeRef
            if ($live -eq 'None') {
                foreach ($copy in $role.Copies) {
                    $copyDepth = Get-Depth $copy.Held $r.PrivilegeRef
                    if ($copyDepth -ne 'None') { $removalCopies.Add(("{0,-36} {1,-34} copy {2} still holds {3} (the root role does not)" -f $role.Name, $r.PrivilegeRef.Name, $copy.Id, $copyDepth)) }
                }
            }
            elseif ($live -ne [string]$r.expectedDepth) {
                $removalMismatch.Add(("{0,-36} {1,-34} held at {2}, the config expects {3} (not removed)" -f $role.Name, $r.PrivilegeRef.Name, $live, $r.expectedDepth))
            }
            else {
                $removalPlan.Add([pscustomobject]@{ Role = $role; Privilege = $r.PrivilegeRef; Old = $live; New = 'None'; Gap = [string]$r.gap })
            }
        }
    }

    # The rollback plan (-Restore): each listed privilege the root role does not hold is put back at its expectedDepth. A root role
    # that holds it at another depth is a mismatch (the run stops, nothing changed); a copy that differs from a restored root is drift.
    $restorePlan = [System.Collections.Generic.List[object]]::new()
    $restoreMismatch = [System.Collections.Generic.List[string]]::new()
    $restoreCopies = [System.Collections.Generic.List[string]]::new()
    if ($checkRestores) {
        foreach ($r in $removals) {
            $role = $roleCache[$r.role]
            $live = Get-Depth $role.Held $r.PrivilegeRef
            if ($live -eq 'None') {
                $restorePlan.Add([pscustomobject]@{ Role = $role; Privilege = $r.PrivilegeRef; Old = $live; New = [string]$r.expectedDepth; Gap = [string]$r.gap })
            }
            elseif ($live -ne [string]$r.expectedDepth) {
                $restoreMismatch.Add(("{0,-36} {1,-34} held at {2}, the rollback restores {3} (not changed)" -f $role.Name, $r.PrivilegeRef.Name, $live, $r.expectedDepth))
            }
            else {
                foreach ($copy in $role.Copies) {
                    $copyDepth = Get-Depth $copy.Held $r.PrivilegeRef
                    if ($copyDepth -ne $live) { $restoreCopies.Add(("{0,-36} {1,-34} copy {2} holds {3,-6} root holds {4}" -f $role.Name, $r.PrivilegeRef.Name, $copy.Id, $copyDepth, $live)) }
                }
            }
        }
    }

    $chosen = $plan
    if ($Remove) { $chosen = $removalPlan }
    if ($Restore) { $chosen = $restorePlan }
    [pscustomobject]@{
        Plan = $chosen; AddPlan = $plan
        AboveDeclared = $aboveDeclared; CopyDrift = $copyDrift; FromDrift = $fromDrift
        RemovalPlan = $removalPlan; RemovalMismatch = $removalMismatch; RemovalCopies = $removalCopies
        RestorePlan = $restorePlan; RestoreMismatch = $restoreMismatch; RestoreCopies = $restoreCopies
    }
}

# What keeps the live roles from matching the declared set: a pending change, a depth above the declared one, a copy that differs.
function Get-Reasons($State) {
    $reasons = @()
    if ($checkAdds) {
        if ($State.AddPlan.Count -gt 0) { $reasons += "$($State.AddPlan.Count) change(s) pending" }
        if ($State.AboveDeclared.Count -gt 0) { $reasons += "$($State.AboveDeclared.Count) live depth(s) above the declared one" }
        if ($State.CopyDrift.Count -gt 0) { $reasons += "$($State.CopyDrift.Count) business-unit copy difference(s)" }
    }
    if ($checkRemovals) {
        if ($State.RemovalPlan.Count -gt 0) { $reasons += "$($State.RemovalPlan.Count) removal(s) pending" }
        if ($State.RemovalMismatch.Count -gt 0) { $reasons += "$($State.RemovalMismatch.Count) privilege(s) held at a depth other than the config expects" }
        if ($State.RemovalCopies.Count -gt 0) { $reasons += "$($State.RemovalCopies.Count) business-unit copy still holding a removed privilege" }
    }
    if ($checkRestores) {
        if ($State.RestorePlan.Count -gt 0) { $reasons += "$($State.RestorePlan.Count) restore(s) pending" }
        if ($State.RestoreMismatch.Count -gt 0) { $reasons += "$($State.RestoreMismatch.Count) privilege(s) held at a depth other than the rollback restores" }
        if ($State.RestoreCopies.Count -gt 0) { $reasons += "$($State.RestoreCopies.Count) business-unit copy difference(s) on a restored privilege" }
    }
    , $reasons
}

# ── Report ──────────────────────────────────────────────────────────────────────────────────────────────────
function Write-Report($State) {
    Write-Host ''
    Write-Host 'Roles:'
    foreach ($r in $roleCache.Values | Sort-Object Name) {
        Write-Host ("  {0,-36} root role in the root BU; {1} business-unit copies take its privileges" -f $r.Name, $r.Copies.Count)
    }
    Write-Host ''
    if ($checkAdds) {
        if ($State.AddPlan.Count -eq 0) { Write-Host 'Changes     : none pending.' -ForegroundColor Green }
        else {
            Write-Host "Changes     : $($State.AddPlan.Count)"
            foreach ($p in $State.AddPlan | Sort-Object { $_.Role.Name }, { $_.Privilege.Name }) {
                Write-Host ("  {0,-36} {1,-34} {2,-6} -> {3,-6} ({4}; {5} business-unit copies)" -f $p.Role.Name, $p.Privilege.Name, $p.Old, $p.New, $p.Gap, $p.Role.Copies.Count) -ForegroundColor Cyan
            }
        }
        Write-Host ''
        Write-Host 'Live depth above the declared one (never lowered here):'
        if ($State.AboveDeclared.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.AboveDeclared) { Write-Host "  $d" -ForegroundColor DarkYellow }
        Write-Host "Live depth differs from the config's 'from' (informational: drift since the config or the last dry run; the plan uses the live depth):"
        if ($State.FromDrift.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.FromDrift) { Write-Host "  $d" -ForegroundColor DarkYellow }
        Write-Host 'Business-unit copies that differ from their root role on a declared privilege:'
        if ($State.CopyDrift.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.CopyDrift) { Write-Host "  $d" -ForegroundColor DarkYellow }
    }
    if ($checkRemovals) {
        if ($checkAdds) { Write-Host '' }
        if ($State.RemovalPlan.Count -eq 0) { Write-Host 'Removals    : none pending.' -ForegroundColor Green }
        else {
            Write-Host "Removals    : $($State.RemovalPlan.Count)"
            foreach ($p in $State.RemovalPlan | Sort-Object { $_.Role.Name }, { $_.Privilege.Name }) {
                Write-Host ("  {0,-36} {1,-34} {2,-6} -> removed ({3}; root role, then every business-unit copy)" -f $p.Role.Name, $p.Privilege.Name, $p.Old, $p.Gap) -ForegroundColor Cyan
            }
        }
        Write-Host 'Held at a depth other than the config expects (never removed):'
        if ($State.RemovalMismatch.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.RemovalMismatch) { Write-Host "  $d" -ForegroundColor DarkYellow }
        Write-Host 'Business-unit copies that still hold a privilege the root role no longer holds (never edited here):'
        if ($State.RemovalCopies.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.RemovalCopies) { Write-Host "  $d" -ForegroundColor DarkYellow }
        if ($Remove -and -not $Verify) { Write-RollbackLine $State }
    }
    if ($checkRestores) {
        if ($State.RestorePlan.Count -eq 0) { Write-Host 'Restores    : none pending.' -ForegroundColor Green }
        else {
            Write-Host "Restores    : $($State.RestorePlan.Count)"
            foreach ($p in $State.RestorePlan | Sort-Object { $_.Role.Name }, { $_.Privilege.Name }) {
                Write-Host ("  {0,-36} {1,-34} None   -> {2,-6} ({3}; root role, then every business-unit copy)" -f $p.Role.Name, $p.Privilege.Name, $p.New, $p.Gap) -ForegroundColor Cyan
            }
        }
        Write-Host 'Held at a depth other than the rollback restores (never changed):'
        if ($State.RestoreMismatch.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.RestoreMismatch) { Write-Host "  $d" -ForegroundColor DarkYellow }
        Write-Host 'Business-unit copies that differ from their root role on a restored privilege (never edited here):'
        if ($State.RestoreCopies.Count -eq 0) { Write-Host '  none' }
        foreach ($d in $State.RestoreCopies) { Write-Host "  $d" -ForegroundColor DarkYellow }
    }
}

# The rollback of a -Remove run, printed by its dry run and its apply: one command that puts back exactly the removals planned here.
# Exact when every listed removal is pending. When some are already absent on the root role (an earlier removal applied here), the
# command is limited with -Gap to the gaps of the planned removals, which is exact when no absent entry is under those gaps; otherwise
# -Restore would add the absent entries as well, so they are named.
$script:RollbackCommand = $null
function Write-RollbackLine($State) {
    $isPlanned = { param($r) @($State.RemovalPlan | Where-Object { $_.Role.Name -eq $r.role -and $_.Privilege.Id -eq $r.PrivilegeRef.Id }).Count -gt 0 }
    $absent = @($removals | Where-Object { -not (& $isPlanned $_) })
    $flag = $gapFlag
    if ($absent.Count -gt 0 -and $State.RemovalPlan.Count -gt 0) {
        $plannedGaps = @($State.RemovalPlan | ForEach-Object { $_.Gap } | Sort-Object -Unique)
        if (-not @($absent | Where-Object { $plannedGaps -contains $_.gap }).Count) {
            $flag = "-Gap $($plannedGaps -join ',') "
            $absent = @()
        }
    }
    $script:RollbackCommand = "pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl $EnvironmentUrl -Restore $($flag)-Apply"
    Write-Host ''
    Write-Host "Rollback    : $script:RollbackCommand" -ForegroundColor Cyan
    if ($absent.Count -eq 0) {
        Write-Host "              restores exactly the $($State.RemovalPlan.Count) removal(s) above, each at the depth listed (dry run first: -Restore$(if ($flag) { " $($flag.TrimEnd())" }))."
    }
    else {
        Write-Host "              NOT exact: $($absent.Count) listed removal(s) are not pending here, and -Restore would add them as well:" -ForegroundColor DarkYellow
        foreach ($a in $absent) { Write-Host ("                {0,-36} {1}" -f $a.role, $a.privilege) -ForegroundColor DarkYellow }
    }
}

$state = Get-State
$plan = $state.Plan
Write-Report $state

# ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────────
$reasons = Get-Reasons $state
if ($Verify) {
    if ($reasons.Count -eq 0) {
        if ($checkRestores) { Write-Host "`nVERIFY PASS: the live roles hold every listed removal again, at its expectedDepth (the rollback is complete)." -ForegroundColor Green; exit 0 }
        Write-Host "`nVERIFY PASS: the live roles hold the declared set$(if ($checkRemovals) { ' and none of the listed removals' })." -ForegroundColor Green; exit 0
    }
    Write-Host "`nVERIFY FAIL: $($reasons -join '; ') (listed above)." -ForegroundColor Red
    exit 1
}

# A privilege held at a depth the config does not expect is not removed: the run stops, in every mode, with nothing changed.
if ($Remove -and $state.RemovalMismatch.Count -gt 0) {
    Write-Host "`nFAIL: $($state.RemovalMismatch.Count) privilege(s) held at a depth other than the config expects (listed above). Nothing was changed; fix the config or the role and re-run." -ForegroundColor Red
    exit 1
}
if ($Restore -and $state.RestoreMismatch.Count -gt 0) {
    Write-Host "`nFAIL: $($state.RestoreMismatch.Count) privilege(s) held at a depth other than the rollback restores (listed above). Nothing was changed; fix the role and re-run." -ForegroundColor Red
    exit 1
}
$noun = if ($Remove) { 'removal' } elseif ($Restore) { 'restore' } else { 'change' }
if ($plan.Count -eq 0) {
    # Nothing to add or raise (or remove, or restore). -Apply does not report success over a state it cannot fix: it exits 1 with the report -Verify prints.
    if (-not $IsDryRun -and $reasons.Count -gt 0) {
        Write-Host "`nAPPLY: nothing to $(if ($Remove) { 'remove' } elseif ($Restore) { 'restore' } else { 'add or raise' }), but the live roles do not match the declared set: $($reasons -join '; ') (listed above). This script never lowers a depth or edits a copy; fix it in the environment and re-run -Verify." -ForegroundColor Red
        exit 1
    }
    Write-Host "`nNothing to change." -ForegroundColor Green; exit 0
}
if ($IsDryRun) { Write-Host "`nDRY RUN: no writes. Re-run with $($modeFlag)-Apply to make the $($plan.Count) $noun(s) above." -ForegroundColor Cyan; exit 0 }

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
$phase = if ($Remove) { 'RemovePrivilegeRole' } else { 'AddPrivilegesRole' }
$failed = @()
$failedRole = $null
# The writes AND the read-back share one try: whichever fails, the catch prints what was applied, what was not, and what to do
# next (a read-back that cannot be completed used to end in the raw error alone).
try {
    foreach ($group in $plan | Group-Object { $_.Role.Id }) {
        $role = $group.Group[0].Role
        $failedRole = $role.Name
        if ($Remove) {
            # One call per listed privilege, on the ROOT role; the body names that one privilege and nothing else.
            foreach ($p in $group.Group) {
                $json = @{ Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $p.Privilege.Id } } | ConvertTo-Json -Depth 4
                Invoke-RestMethod -Method Post -Uri "$Api/roles($($role.Id))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
                $applied.Add("$($role.Name) $($p.Privilege.Name) $($p.Old) -> $($p.New)")
            }
            continue
        }
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
            if ($t.Label -eq 'root' -and ($Remove -or $Restore)) {
                # Exactly the listed privileges went (or, with -Restore, came back): nothing else on the root role changed depth, went
                # missing or appeared.
                $removedIds = @($group.Group.Privilege.Id)
                $collateral = @($role.Held.Keys | Where-Object { $removedIds -notcontains $_ -and (-not $after.ContainsKey($_) -or [string]$after[$_].Depth -ne [string]$role.Held[$_].Depth) })
                $appeared = @($after.Keys | Where-Object { -not $role.Held.ContainsKey($_) -and $removedIds -notcontains $_ })
                # Each reported privilege names its depth before and after, so the report says what the platform did.
                foreach ($id in $collateral) {
                    $afterDepth = if ($after.ContainsKey($id)) { [string]$after[$id].Depth } else { 'None' }
                    $failed += "$($role.Name) (root): $($role.Held[$id].PrivilegeName) is not in the $(if ($Restore) { 'rollback' } else { 'removals block' }) but changed or went missing (before $([string]$role.Held[$id].Depth), after $afterDepth)"
                }
                foreach ($id in $appeared) { $failed += "$($role.Name) (root): $($after[$id].PrivilegeName) appeared during the $(if ($Restore) { 'rollback' } else { 'removal' }) (before None, after $([string]$after[$id].Depth))" }
            }
            if ($t.Label -eq 'root' -and -not ($Remove -or $Restore)) {
                $unrequested = @($after.Keys | Where-Object { -not $role.Held.ContainsKey($_) -and -not ($group.Group.Privilege.Id -contains $_) })
                if ($unrequested.Count -gt 0) {
                    Write-Host "WARNING: $($role.Name) also gained $($unrequested.Count) privilege(s) that were not requested (before None): $(($unrequested | ForEach-Object { "$($after[$_].PrivilegeName) after $([string]$after[$_].Depth)" }) -join ', '). Not removed here." -ForegroundColor Yellow
                }
            }
        }
    }
    if ($failed.Count -gt 0) { throw "Read-back did not match after 8 tries of 4 seconds:`n  $($failed -join "`n  ")" }

    # Every write is confirmed. Re-read the roles that were written and rebuild the state: what still keeps the live roles from the declared
    # set (a depth above the declared one, a copy that differs, a removal's copy) is reported below and fails the run, as -Verify does.
    # The phase changes first, so a failure in this re-read says the writes were confirmed and the re-read failed.
    $phase = 're-read'
    foreach ($group in $plan | Group-Object { $_.Role.Id }) {
        $role = $group.Group[0].Role
        $failedRole = $role.Name
        $role.Held = Get-HeldMap $role.Id
        foreach ($copy in $role.Copies) { $copy.Held = Get-HeldMap $copy.Id }
    }
    $finalState = Get-State
    $finalReasons = Get-Reasons $finalState
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
    elseif ($phase -eq 're-read') { Write-Host 'The writes were confirmed by the read-back; the final re-read of the written roles failed. Re-run -Verify to see what the live roles hold.' -ForegroundColor Red }
    elseif ($Restore) { Write-Host 'Re-run -Restore -Apply to converge (the rollback only adds back the privileges the removals block lists). Re-run -Restore -Verify to see what is still pending.' -ForegroundColor Red }
    elseif ($Remove) { Write-Host 'Re-run -Remove -Apply to converge (the script only removes the privileges the removals block lists). Re-run -Remove -Verify to see what is still pending.' -ForegroundColor Red }
    else { Write-Host 'Re-run -Apply to converge (the script only adds or raises). Re-run -Verify to see what is still pending.' -ForegroundColor Red }
    throw
}
if ($Remove) {
    Write-Host "`nREMOVED and read back absent on the root role and every business-unit copy: $($plan.Count) removal(s)." -ForegroundColor Green
    Write-Host "Rollback    : $script:RollbackCommand (puts back the $($plan.Count) removal(s) above)" -ForegroundColor Cyan
}
elseif ($Restore) { Write-Host "`nRESTORED and read back on the root role and every business-unit copy: $($plan.Count) restore(s)." -ForegroundColor Green }
else { Write-Host "`nAPPLIED and read back on every business-unit copy: $($plan.Count) change(s)." -ForegroundColor Green }
if ($finalReasons.Count -gt 0) {
    # The writes held, but the roles still differ from the declared set in a way this script never fixes: not a success.
    Write-Report $finalState
    Write-Host "`nAPPLY: the changes were applied and read back, but the live roles do not match the declared set: $($finalReasons -join '; ') (listed above). This script never lowers a depth or edits a copy; fix it in the environment and re-run -Verify." -ForegroundColor Red
    exit 1
}
Write-Host 'Dataverse caches principal privileges; a probe straight after this can show the old result. Re-check until stable.'
exit 0
