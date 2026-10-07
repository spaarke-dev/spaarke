#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Gives the 'Secure Record Owner' role Read, at User (Basic) depth, on every table in
    config/secure-record-owner-role.json, so Dataverse accepts the Secure Record team as the owner of those rows.
    Dry run by default. Adds only; never removes.

.DESCRIPTION
    Dataverse refuses to make a team the OWNER of a row unless one of the team's roles holds Read on that row's table:

        Read Privilege Check For Owner failed ... Principal team (...) is missing prvReadsprk_Todo privilege

    Write-path invariant I-6 (RecordOwnershipResolver, spaarkeai-word-add-in-r1 task 080) owns a child of a secure
    record by the Secure Record team, so the role must cover each child table as well as the three root tables that
    carry sprk_issecure. The list lives in ONE file, config/secure-record-owner-role.json. The setup guide
    (docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) and unified-access-control-r2's NFR-05 census read the same
    file.

    MODES
      (default)  Dry run. Prints what is present, what is missing, and any privilege the role holds that the file
                 does not list. Zero writes.
      -Apply     Adds each missing Read privilege at Basic depth (AddPrivilegesRole), then reads the role back and
                 confirms each one. Removes NOTHING, including privileges outside the file: removing a privilege
                 from this role is an owner decision (SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4 has the
                 procedure).
      -Verify    Read-only check. Exits 1 and names each table whose Read is missing or held at the wrong depth.
                 Exits 0 when the role covers the whole file. Privileges outside the file are reported but do not
                 fail it.

    WHAT IT CHECKS BEFORE WRITING
      - The environment is the one named (the org name is printed first).
      - Exactly one business unit has the configured name, and exactly one role of the configured name lives in it.
      - Each table's Read privilege is taken from entity METADATA, and its name must equal the file's privilegeName.
        A mismatch stops the run: the file is wrong, or the table was renamed.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only coverage check with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER ConfigPath
    The codified set. Defaults to config/secure-record-owner-role.json at the repository root.

.EXAMPLE
    .\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: present / missing / outside-the-file, with no writes.

.EXAMPLE
    .\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
    Adds the missing Read privileges and reads them back.

.EXAMPLE
    .\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = the role covers every table in the file; exit 1 = it does not (each gap is named).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string]$ConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'secure-record-owner-role.json')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── The codified set ────────────────────────────────────────────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "Config not found: $ConfigPath" }
$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.schemaVersion -ne 1) { throw "Unsupported schemaVersion '$($config.schemaVersion)' in $ConfigPath" }
if ($config.access -ne 'Read' -or $config.depth -ne 'Basic') {
    # The role's safety argument rests on Read at User depth only (setup guide section 5.1). Refuse anything else
    # rather than grant it.
    throw "Config asks for access '$($config.access)' at depth '$($config.depth)'. Only Read at Basic is supported, by design."
}
$tables = @($config.tables)
if ($tables.Count -eq 0) { throw "Config lists no tables: $ConfigPath" }
$dupes = $tables | Group-Object logicalName | Where-Object Count -gt 1
if ($dupes) { throw "Config lists a table twice: $(($dupes | ForEach-Object Name) -join ', ')" }
foreach ($t in $tables) {
    # Every entry must say why it is there and quote the refusal that forced it (the file's howToExtend rule).
    # An entry without them is how "privilege without a purpose" creeps back in.
    foreach ($field in 'logicalName', 'privilegeName', 'reason', 'evidence') {
        if ([string]::IsNullOrWhiteSpace($t.$field)) { throw "Config entry '$($t.logicalName)' has no '$field'. Every table needs a reason and its recorded evidence." }
    }
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

$orgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$orgName')"
Write-Host "Config      : $ConfigPath ($($tables.Count) tables)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY (adds only)' }))

# ── Business unit and role, each resolved to exactly one ────────────────────────────────────────────────────
$buName = $config.businessUnitName -replace "'", "''"
$bus = @((Invoke-DvGet "businessunits?`$select=businessunitid,name&`$filter=name eq '$buName'").value)
if ($bus.Count -ne 1) { throw "Expected exactly one business unit named '$($config.businessUnitName)'; found $($bus.Count)." }
$buId = $bus[0].businessunitid

$roleName = $config.roleName -replace "'", "''"
$roles = @((Invoke-DvGet "roles?`$select=roleid,name,_parentrootroleid_value&`$filter=name eq '$roleName' and _businessunitid_value eq $buId").value)
if ($roles.Count -ne 1) {
    throw "Expected exactly one role named '$($config.roleName)' in business unit '$($config.businessUnitName)'; found $($roles.Count). The setup guide creates it there (section 5.2)."
}
$roleId = $roles[0].roleid
if ($roles[0]._parentrootroleid_value -and ($roles[0]._parentrootroleid_value -ne $roleId)) {
    # A role created in an ANCESTOR business unit is replicated into every child unit, and its privileges live on
    # the root copy only. What matched here is such a replica, so it is assignable in every unit, which is not the
    # contained role the guide prescribes. Refuse rather than edit or grade the wrong object.
    throw "Role '$($config.roleName)' in '$($config.businessUnitName)' is a replica of root role $($roles[0]._parentrootroleid_value), so it was created in an ancestor business unit. Setup guide section 5.2 creates it IN the secure business unit. Fix the role before running this."
}
Write-Host "Role        : $($config.roleName) ($roleId) in BU $($config.businessUnitName) ($buId)"

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

# ── What the role holds now ─────────────────────────────────────────────────────────────────────────────────
function Get-RolePrivileges {
    @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
}
$held = Get-RolePrivileges
$heldById = @{}
foreach ($h in $held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }

$present = @(); $missing = @(); $wrongDepth = @()
foreach ($w in $wanted) {
    $h = $heldById[$w.Id]
    if ($null -eq $h) { $missing += $w }
    elseif ($h.Depth -ne 'Basic') { $wrongDepth += [pscustomobject]@{ Table = $w.Table; Name = $w.Name; Depth = $h.Depth } }
    else { $present += $w }
}
$wantedIds = @($wanted | ForEach-Object Id)
$outside = @($held | Where-Object { $wantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() } |
    Sort-Object PrivilegeName)

Write-Host ''
Write-Host "Held now    : $($held.Count) privileges"
Write-Host "Present     : $($present.Count) of $($wanted.Count)  $(($present | ForEach-Object Table) -join ', ')"
foreach ($m in $missing) { Write-Host "MISSING     : $($m.Table)  ($($m.Name))" -ForegroundColor Yellow }
foreach ($d in $wrongDepth) { Write-Host "WRONG DEPTH : $($d.Table)  ($($d.Name)) at $($d.Depth), expected Basic" -ForegroundColor Red }
if ($outside.Count -gt 0) {
    Write-Host "Outside the file ($($outside.Count)) — reported, never removed by this script:" -ForegroundColor DarkYellow
    foreach ($o in $outside) { Write-Host ("  {0,-40} {1}" -f $o.PrivilegeName, $o.Depth) -ForegroundColor DarkYellow }
}

# ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────────
if ($Verify) {
    if ($missing.Count -eq 0 -and $wrongDepth.Count -eq 0) {
        Write-Host "`nVERIFY PASS: the role holds Read at Basic on all $($wanted.Count) tables." -ForegroundColor Green
        exit 0
    }
    $gaps = @($missing | ForEach-Object Table) + @($wrongDepth | ForEach-Object Table)
    Write-Host "`nVERIFY FAIL: the Secure Record team cannot own rows of: $($gaps -join ', ')" -ForegroundColor Red
    exit 1
}

# ── Wrong depth is never "fixed" here: widening is forbidden and narrowing changes an existing grant ─────────
if ($wrongDepth.Count -gt 0) {
    throw "A listed privilege is held at a depth other than Basic. This script does not change existing grants; that is an owner decision (setup guide section 5.1)."
}

if ($missing.Count -eq 0) { Write-Host "`nNothing to add." -ForegroundColor Green; exit 0 }
if ($IsDryRun) {
    Write-Host "`nDRY RUN: would add $($missing.Count) privilege(s) at Basic: $(($missing | ForEach-Object Name) -join ', '). Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}

# ── Apply: add only, then read back ─────────────────────────────────────────────────────────────────────────
$body = @{
    Privileges = @($missing | ForEach-Object {
            @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = 'Basic'; PrivilegeId = $_.Id; PrivilegeName = $_.Name; BusinessUnitId = $buId }
        })
} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri "$Api/roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Headers $headers `
    -Body ([Text.Encoding]::UTF8.GetBytes($body)) | Out-Null

$after = Get-RolePrivileges
$afterById = @{}
foreach ($a in $after) { $afterById[$a.PrivilegeId.ToString().ToLowerInvariant()] = $a }
$failed = @($missing | Where-Object { $null -eq $afterById[$_.Id] -or $afterById[$_.Id].Depth -ne 'Basic' })
if ($failed.Count -gt 0) { throw "Read-back did not show: $(($failed | ForEach-Object Name) -join ', '). The role was not changed as expected." }

# AddPrivilegesRole is known to re-inject platform privileges (setup guide section 5.4). Name anything that
# appeared without being asked for, so it is seen rather than silently kept.
$beforeIds = @($held | ForEach-Object { $_.PrivilegeId.ToString().ToLowerInvariant() })
$unrequested = @($after | Where-Object {
        $id = $_.PrivilegeId.ToString().ToLowerInvariant()
        ($beforeIds -notcontains $id) -and ($wantedIds -notcontains $id)
    })
Write-Host "`nADDED at Basic and read back: $(($missing | ForEach-Object Name) -join ', ')" -ForegroundColor Green
Write-Host "Held now    : $($after.Count) privileges (was $($held.Count))"
if ($unrequested.Count -gt 0) {
    Write-Host "WARNING: the platform also added $($unrequested.Count) privilege(s) that were not requested: $(($unrequested | ForEach-Object PrivilegeName) -join ', '). Not removed here; see setup guide section 5.4." -ForegroundColor Yellow
}
Write-Host 'Dataverse caches principal privileges. A probe run straight after this can show the OLD result; re-probe until it is stable across 3 polls (setup guide section 7).'
exit 0
