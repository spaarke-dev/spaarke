#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Raises Read to Global (Organization) depth on the Spaarke product configuration tables in
    config/product-configuration-read-role.json, for 'Spaarke Core User' and 'Spaarke Basic User'.
    Dry run by default. Raises Read only; never touches any other privilege.

.DESCRIPTION
    Spaarke product data (playbooks and their building blocks, the AI type catalogs, grid configuration) is owned in the
    root business unit (owner round 136). People work in a customer business unit below the root. A Read at Deep reaches
    only the user's own unit and its children, so a user outside the root reads none of the root's product rows. The owner
    decision of 2026-10-29 (BU1 move plan Q1) gives both roles Read at Global on these tables only.

    The SOURCE OF TRUTH is the solution role XML (src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml and
    Spaarke Basic User.xml): a SpaarkeMaster import ships the change to every environment. This script brings ONE
    environment to the declared list ahead of that import, and verifies it afterwards. It refuses to run while either role
    file does not already hold each listed Read at Global, so the environment never runs ahead of the source.

    PLAYBOOKS: READ ONLY FOR END USERS
      Read at Global on sprk_analysisplaybook lets every user read every playbook row. Creating, cloning and sharing
      playbooks is therefore for playbook administrators only: in the role XML only 'Spaarke AI Analysis Admin' holds
      Create and Share on sprk_analysisplaybook, and the BFF routes POST /api/ai/playbooks, /{id}/clone, /{id}/share and
      /{id}/unshare require the caller's own Create (create, clone) or Share (share, unshare) privilege on that table
      before they read or write anything. Core and Basic User also hold no Create on sprk_gridconfiguration.
      Platform service configuration (sprk_externalserviceconfig, sprk_dataverseenvironment, sprk_aimodeldeployment) is
      never on this list. This script refuses a config that lists one, in every mode (dry run, -Apply and -Verify),
      before it connects. The list is $PlatformServiceTables in this script; the build-time guard reads it from there.

    BUSINESS-UNIT COPIES
      Dataverse keeps one copy of a role per business unit. The copies are not edited separately: each one takes its
      privileges from the ROOT role (its _parentrootroleid). The script changes the root role only, then reads EVERY copy
      back. The dry run prints each copy (role id, business unit, number of users and teams holding it) with the depth it
      reads today and the depth it would read after -Apply.

    MODES
      (default)  Dry run. Prints every change as role, privilege, old depth -> Global, per role copy. Zero writes.
      -Apply     Raises each listed Read below Global to Global on the root role (AddPrivilegesRole), then reads the root
                 role and every copy back and confirms each one. Names any privilege the platform added unasked.
                 Lowers nothing and removes nothing.
      -Verify    Read-only. Exits 1 when any copy of either role reads a listed table below Global; 0 otherwise.

    WHAT IT CHECKS BEFORE WRITING
      - The environment is the one named (the org name is printed first).
      - The config asks for Read at Global for exactly the two roles, and no listed table is a hard-deny table
        (config/restricted-content-owner-role.json), a customer configuration table
        (config/customer-configuration-owner-role.json) or a table the Secure Record census guards
        (config/secure-record-owner-role.json) or platform service configuration ($PlatformServiceTables). The
        build-time guard checks the BFF's own sets too.
      - Both role XML files hold each listed Read at Global.
      - Each role name resolves to exactly one ROOT role.
      - Each table's Read privilege comes from entity METADATA and its name must equal the config's privilegeName.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only pass/fail check. Cannot be combined with -Apply.

.PARAMETER ConfigPath
    The declared list. Defaults to config/product-configuration-read-role.json at the repository root.

.PARAMETER RolesXmlDir
    The solution role XML folder. Defaults to src/dataverse/solutions/SpaarkeMaster/Roles.

.EXAMPLE
    .\Set-ProductConfigurationReadDepth.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the exact change per role copy, with no writes.

.EXAMPLE
    .\Set-ProductConfigurationReadDepth.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
    Raises the listed Reads to Global on both root roles and reads every copy back.

.EXAMPLE
    .\Set-ProductConfigurationReadDepth.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = every copy of both roles reads every listed table at Global; exit 1 = a gap (each is named).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string]$ConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'product-configuration-read-role.json'),

    [string]$RolesXmlDir = (Join-Path $PSScriptRoot '..' 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'Roles')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$Api = "$EnvironmentUrl/api/data/v9.2"
$ExpectedRoles = @('Spaarke Core User', 'Spaarke Basic User')
$DepthOrder = @('None', 'Basic', 'Local', 'Deep', 'Global')
function Get-Rank([string]$Depth) {
    $i = [array]::IndexOf($DepthOrder, $Depth)
    if ($i -lt 0) { throw "Unknown depth '$Depth'." }
    $i
}

# ── The declared list ───────────────────────────────────────────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "Config not found: $ConfigPath" }
$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.schemaVersion -ne 1) { throw "Unsupported schemaVersion '$($config.schemaVersion)' in $ConfigPath" }
if ($config.access -ne 'Read' -or $config.depth -ne 'Global') {
    # The owner decision is Read at Global and nothing else. Refuse anything wider or different rather than grant it.
    throw "Config asks for access '$($config.access)' at depth '$($config.depth)'. Only Read at Global is supported, by design."
}
$roleNames = @($config.roles)
if ((@($roleNames | Sort-Object) -join '|') -ne (@($ExpectedRoles | Sort-Object) -join '|')) {
    throw "Config names roles '$($roleNames -join "', '")'. Only '$($ExpectedRoles -join "' and '")' are in scope, by design."
}
$tables = @($config.tables)
if ($tables.Count -eq 0) { throw "Config lists no tables: $ConfigPath" }
$dupes = $tables | Group-Object logicalName | Where-Object Count -gt 1
if ($dupes) { throw "Config lists a table twice: $(($dupes | ForEach-Object Name) -join ', ')" }
foreach ($t in $tables) {
    foreach ($field in 'logicalName', 'privilegeName', 'reason', 'evidence') {
        if ([string]::IsNullOrWhiteSpace($t.$field)) { throw "Config entry '$($t.logicalName)' has no '$field'. Every table needs a reason and its recorded evidence." }
    }
}

# Customer data never reaches Global through this script: refuse any table in the declared customer or restricted sets.
$forbidden = @{}
foreach ($pair in @(
        @('restricted-content-owner-role.json', 'a hard-deny restricted-content table'),
        @('customer-configuration-owner-role.json', 'a customer configuration table'),
        @('secure-record-owner-role.json', 'a table the Secure Record census guards'))) {
    $path = Join-Path (Split-Path -Parent $ConfigPath) $pair[0]
    if (-not (Test-Path -LiteralPath $path)) { throw "Exclusion source not found: $path. Refusing to run without it." }
    foreach ($x in @((Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json).tables)) {
        $forbidden[([string]$x.logicalName).ToLowerInvariant()] = "$($pair[1]) ($($pair[0]))"
    }
}
$bad = @($tables | Where-Object { $forbidden.ContainsKey(([string]$_.logicalName).ToLowerInvariant()) } |
    ForEach-Object { "$($_.logicalName): $($forbidden[([string]$_.logicalName).ToLowerInvariant()])" })
if ($bad.Count -gt 0) { throw "Config lists customer or restricted tables, which never get Global Read:`n  $($bad -join "`n  ")" }

# Platform service configuration is never product configuration. This line is THE one list: the build guard
# (ProductConfigurationReadRoleGuardTests.PlatformServiceTables) reads it from here, so the two cannot disagree.
$PlatformServiceTables = @('sprk_externalserviceconfig', 'sprk_dataverseenvironment', 'sprk_aimodeldeployment')
function Get-PlatformServiceViolations([object[]]$Tables, [string[]]$Platform) {
    @($Tables | Where-Object { $Platform -contains ([string]$_.logicalName).Trim().ToLowerInvariant() } |
        ForEach-Object { "$($_.logicalName): platform service configuration" })
}
$platformBad = @(Get-PlatformServiceViolations $tables $PlatformServiceTables)
if ($platformBad.Count -gt 0) {
    throw "Config lists platform service configuration, which never gets Global Read through this list (refused in every mode):`n  $($platformBad -join "`n  ")"
}

# The solution role XML is the source of truth: it must already hold each listed Read at Global.
$xmlGaps = @()
foreach ($rn in $roleNames) {
    $file = Join-Path $RolesXmlDir "$rn.xml"
    if (-not (Test-Path -LiteralPath $file)) { throw "Role XML not found: $file" }
    $levels = @{}
    foreach ($e in ([xml](Get-Content -LiteralPath $file -Raw -Encoding UTF8)).SelectNodes('//RolePrivilege')) { $levels[$e.name.ToLowerInvariant()] = $e.level }
    foreach ($t in $tables) {
        $lvl = $levels[([string]$t.privilegeName).ToLowerInvariant()]
        if ($lvl -ne 'Global') { $xmlGaps += "$rn.xml: $($t.privilegeName) is '$(if ($lvl) { $lvl } else { 'absent' })', not Global" }
    }
}
if ($xmlGaps.Count -gt 0) {
    throw "The solution role XML does not hold the declared list yet. Edit the XML first (it ships the change to every environment):`n  $($xmlGaps -join "`n  ")"
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

$orgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$orgName')"
Write-Host "Config      : $ConfigPath ($($tables.Count) tables, Read at Global)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY (raises Read only)' }))

$buNames = @{}
foreach ($b in (Invoke-DvGetAll 'businessunits?$select=businessunitid,name')) { $buNames[([string]$b.businessunitid).ToLowerInvariant()] = [string]$b.name }

# ── Each table's Read privilege, from METADATA ──────────────────────────────────────────────────────────────
$wanted = foreach ($t in $tables) {
    $privs = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($t.logicalName)')/Privileges").value |
        Where-Object { $_.PrivilegeType -eq 'Read' })
    if ($privs.Count -ne 1) { throw "Table '$($t.logicalName)': expected one Read privilege in metadata, found $($privs.Count)." }
    if ($privs[0].Name -cne $t.privilegeName) {
        throw "Table '$($t.logicalName)': metadata names its Read privilege '$($privs[0].Name)', the config says '$($t.privilegeName)'. Fix the config (casing follows the schema name)."
    }
    [pscustomobject]@{ Table = $t.logicalName; Name = $privs[0].Name; Id = $privs[0].PrivilegeId.ToString().ToLowerInvariant() }
}
$wantedIds = @($wanted | ForEach-Object Id)

function Get-HeldMap([string]$RoleId) {
    $held = @{}
    foreach ($h in @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$RoleId").RolePrivileges)) {
        $held[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h
    }
    $held
}
function Get-Depth($Held, [string]$PrivilegeId) { if ($Held.ContainsKey($PrivilegeId)) { [string]$Held[$PrivilegeId].Depth } else { 'None' } }

# ── Roles: each name resolves to exactly one ROOT role; every business-unit copy is listed ──────────────────
$roles = foreach ($rn in $roleNames) {
    $escaped = $rn -replace "'", "''"
    # Not wrapped in @(): Invoke-DvGetAll returns its list as ONE object, and @() would make that a one-element array.
    $all = Invoke-DvGetAll "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$escaped'"
    $roots = @($all | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
    if ($roots.Count -ne 1) { throw "Expected exactly one root role named '$rn'; found $($roots.Count) (of $($all.Count) rows)." }
    $root = $roots[0]
    $copies = foreach ($c in @($all | Where-Object { $_._parentrootroleid_value -eq $root.roleid } | Sort-Object { $_.roleid -ne $root.roleid }, { $buNames[([string]$_._businessunitid_value).ToLowerInvariant()] })) {
        $cid = ([string]$c.roleid).ToLowerInvariant()
        [pscustomobject]@{
            Id       = $cid
            IsRoot   = ($cid -eq ([string]$root.roleid).ToLowerInvariant())
            BuId     = ([string]$c._businessunitid_value).ToLowerInvariant()
            BuName   = $buNames[([string]$c._businessunitid_value).ToLowerInvariant()]
            Users    = (Invoke-DvGetAll "systemuserrolescollection?`$select=systemuserid&`$filter=roleid eq $cid").Count
            Teams    = (Invoke-DvGetAll "teamrolescollection?`$select=teamid&`$filter=roleid eq $cid").Count
            Held     = (Get-HeldMap $cid)
        }
    }
    $other = @($all | Where-Object { $_._parentrootroleid_value -ne $root.roleid })
    if ($other.Count -gt 0) { Write-Host "NOTE: $($other.Count) role row(s) named '$rn' are not copies of the root role; they are not changed here." -ForegroundColor DarkYellow }
    [pscustomobject]@{ Name = $rn; RootId = ([string]$root.roleid).ToLowerInvariant(); RootBuId = ([string]$root._businessunitid_value).ToLowerInvariant(); Copies = @($copies) }
}

# ── The plan (root role) and the per-copy view ──────────────────────────────────────────────────────────────
$plan = [System.Collections.Generic.List[object]]::new()
$gaps = [System.Collections.Generic.List[string]]::new()
Write-Host ''
foreach ($r in $roles) {
    $rootCopy = $r.Copies | Where-Object IsRoot
    foreach ($w in $wanted) {
        $old = Get-Depth $rootCopy.Held $w.Id
        if ((Get-Rank $old) -lt (Get-Rank 'Global')) { $plan.Add([pscustomobject]@{ Role = $r; Privilege = $w; Old = $old }) }
    }
    Write-Host "$($r.Name): root role $($r.RootId); $($r.Copies.Count) copies (root included)"
    foreach ($c in $r.Copies) {
        $now = @($wanted | ForEach-Object { Get-Depth $c.Held $_.Id })
        $below = @(for ($i = 0; $i -lt $wanted.Count; $i++) { if ($now[$i] -ne 'Global') { $wanted[$i] } })
        foreach ($b in $below) { $gaps.Add("$($r.Name) [$($c.BuName)] $($c.Id): $($b.Name) at $(Get-Depth $c.Held $b.Id)") }
        $summary = ($now | Group-Object | Sort-Object Name | ForEach-Object { "$($_.Count) at $($_.Name)" }) -join ', '
        $after = if ($below.Count -eq 0) { 'no change' } else { "$($below.Count) -> Global" }
        Write-Host ("  {0} {1,-36} {2,-28} users {3,-3} teams {4,-3} today: {5}; after -Apply: {6}" -f `
                $(if ($c.IsRoot) { '*' } else { ' ' }), $c.Id, $c.BuName, $c.Users, $c.Teams, $summary, $after)
    }
}
Write-Host '  (* = the root role; every other copy takes its privileges from it)'

Write-Host ''
if ($plan.Count -eq 0) { Write-Host 'Changes     : none pending on the root roles.' -ForegroundColor Green }
else {
    Write-Host "Changes     : $($plan.Count) (root roles; copies follow)"
    foreach ($p in $plan | Sort-Object { $_.Role.Name }, { $_.Privilege.Table }) {
        Write-Host ("  {0,-20} {1,-36} {2,-6} -> Global" -f $p.Role.Name, $p.Privilege.Name, $p.Old) -ForegroundColor Cyan
    }
}

# ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────────
if ($Verify) {
    if ($gaps.Count -eq 0) {
        Write-Host "`nVERIFY PASS: every copy of both roles reads all $($wanted.Count) tables at Global." -ForegroundColor Green
        exit 0
    }
    Write-Host "`nVERIFY FAIL: $($gaps.Count) gap(s):" -ForegroundColor Red
    foreach ($g in $gaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}
if ($plan.Count -eq 0) {
    if ($gaps.Count -gt 0) {
        Write-Host "`nThe root roles are complete but a copy is not: $($gaps -join '; '). Not a change this script makes; re-run -Verify (Dataverse may still be propagating)." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "`nNothing to change." -ForegroundColor Green; exit 0
}
if ($IsDryRun) {
    Write-Host "`nDRY RUN: no writes. Re-run with -Apply to make the $($plan.Count) change(s) above." -ForegroundColor Cyan
    exit 0
}

# ── Apply: raise Read to Global on each root role, then read the root and every copy back ───────────────────
foreach ($group in $plan | Group-Object { $_.Role.RootId }) {
    $role = $group.Group[0].Role
    $body = @{
        Privileges = @($group.Group | ForEach-Object {
                @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = 'Global'; PrivilegeId = $_.Privilege.Id; PrivilegeName = $_.Privilege.Name; BusinessUnitId = $role.RootBuId }
            })
    } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Post -Uri "$Api/roles($($role.RootId))/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Headers $headers `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) | Out-Null
}

$failed = @(); $copyLag = @()
foreach ($r in $roles) {
    $rootBefore = ($r.Copies | Where-Object IsRoot).Held
    foreach ($c in $r.Copies) {
        $after = Get-HeldMap $c.Id
        foreach ($w in $wanted) {
            $now = Get-Depth $after $w.Id
            if ($now -ne 'Global') {
                if ($c.IsRoot) { $failed += "$($r.Name) $($w.Name): expected Global, read back $now" }
                else { $copyLag += "$($r.Name) [$($c.BuName)] $($w.Name): read back $now" }
            }
        }
        if ($c.IsRoot) {
            $unrequested = @($after.Keys | Where-Object { -not $rootBefore.ContainsKey($_) -and ($wantedIds -notcontains $_) })
            if ($unrequested.Count -gt 0) {
                Write-Host "WARNING: $($r.Name) also gained $($unrequested.Count) privilege(s) that were not requested: $(($unrequested | ForEach-Object { $after[$_].PrivilegeName }) -join ', '). Not removed here." -ForegroundColor Yellow
            }
            $changed = @($after.Keys | Where-Object { $rootBefore.ContainsKey($_) -and ($wantedIds -notcontains $_) -and ([string]$after[$_].Depth -ne [string]$rootBefore[$_].Depth) })
            if ($changed.Count -gt 0) {
                $failed += "$($r.Name): $($changed.Count) privilege(s) outside the list changed depth: $(($changed | ForEach-Object { "$($after[$_].PrivilegeName) $($rootBefore[$_].Depth)->$($after[$_].Depth)" }) -join ', ')"
            }
        }
    }
}
if ($failed.Count -gt 0) { throw "Read-back did not match:`n  $($failed -join "`n  ")" }
Write-Host "`nAPPLIED and read back on the root roles: $($plan.Count) change(s)." -ForegroundColor Green
if ($copyLag.Count -gt 0) {
    Write-Host "A business-unit copy does not show the change yet ($($copyLag.Count)): $($copyLag -join '; '). Re-run -Verify until it passes." -ForegroundColor Yellow
}
Write-Host 'Dataverse caches principal privileges; a probe straight after this can show the old result. Re-check until stable.'
exit 0
