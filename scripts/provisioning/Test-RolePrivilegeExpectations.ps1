#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Read-only post-upgrade check: do a stamp's security roles hold exactly what a SpaarkeMaster release says they should?

.DESCRIPTION
    Reads an expectations file (scripts/provisioning/role-privilege-expectations/*.json, one per release) listing, per
    role, the privileges that must be ABSENT at every depth and the privileges that must be PRESENT at an exact depth.
    For each role it reads the ROOT copy (no parent role; inherited copies report the root's set) with
    RetrieveRolePrivilegesRole and reports every mismatch. Writes nothing to Dataverse.

    Why it exists: privileges removed from a managed role leave a stamp only on a managed stage-and-upgrade (H6 uses
    StageAndUpgradeAsync for an older managed SpaarkeMaster). A plain update import, or a privilege added by hand after
    the last import, leaves them behind silently — and a leftover Signal Write / Decision Record Create lets callers
    bypass the BFF commit route. Run it after every stamp upgrade, with the file for the release just imported.

    Auth: the operator's own az CLI identity (az account get-access-token). No secrets.

.PARAMETER EnvironmentUrl
    The stamp's Dataverse URL, e.g. https://spaarke-acme.crm.dynamics.com.

.PARAMETER ExpectationsPath
    The release's expectations file.

.EXAMPLE
    pwsh -File scripts/provisioning/Test-RolePrivilegeExpectations.ps1 -EnvironmentUrl https://spaarke-acme.crm.dynamics.com `
        -ExpectationsPath scripts/provisioning/role-privilege-expectations/spaarkemaster-1.2.4.0.json
    Exit 0 = every role matches; 1 = a mismatch (listed); 2 = the check could not run (role missing, read failed).

.NOTES
    customer-provisioning-orchestration-r1. Expectations for 1.2.4.0 come from spaarke-ontology-platform-r1 (2026-10-29).
#>
#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$EnvironmentUrl,
    [Parameter(Mandatory)] [string]$ExpectationsPath
)
$ErrorActionPreference = 'Stop'

$base = $EnvironmentUrl.TrimEnd('/')
$api = "$base/api/data/v9.2"
$spec = Get-Content -LiteralPath $ExpectationsPath -Raw | ConvertFrom-Json -AsHashtable
$depthNames = @{ '1' = 'Basic'; '2' = 'Local'; '4' = 'Deep'; '8' = 'Global' }

try {
    $token = az account get-access-token --resource $base --query accessToken -o tsv 2>$null
    if (-not $token) { throw "No Dataverse token for $base. Run 'az login' and retry." }
    $headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
    function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$api/$Path" -Headers $headers -Method Get }

    $version = @((Invoke-DvGet "solutions?`$select=version,ismanaged&`$filter=uniquename eq 'SpaarkeMaster'").value)
    Write-Host "Environment : $base"
    Write-Host "Expectations: $($spec.release) ($ExpectationsPath)"
    Write-Host ("SpaarkeMaster installed: {0}" -f $(if ($version.Count) { "$($version[0].version) ($(if ($version[0].ismanaged) { 'managed' } else { 'unmanaged' }))" } else { 'NOT INSTALLED' }))

    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($roleName in $spec.roles.Keys) {
        $n = $roleName -replace "'", "''"
        $roots = @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq '$n' and _parentroleid_value eq null").value)
        if ($roots.Count -ne 1) { throw "Role '$roleName': expected exactly one root copy, found $($roots.Count)." }
        $held = @{}
        foreach ($p in (Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$($roots[0].roleid)").RolePrivileges) {
            $d = "$($p.Depth)"; if ($depthNames.ContainsKey($d)) { $d = $depthNames[$d] }
            $held[$p.PrivilegeName.ToLowerInvariant()] = $d
        }
        $exp = $spec.roles[$roleName]
        $roleFailures = @()
        foreach ($a in $exp.absent) {
            if ($held.ContainsKey($a.ToLowerInvariant())) { $roleFailures += "LEFTOVER $a ($($held[$a.ToLowerInvariant()])) — must be absent" }
        }
        foreach ($k in $exp.present.Keys) {
            $want = $exp.present[$k]
            if (-not $held.ContainsKey($k.ToLowerInvariant())) { $roleFailures += "MISSING $k — must be held at $want" }
            elseif ($held[$k.ToLowerInvariant()] -ne $want) { $roleFailures += "DEPTH $k is $($held[$k.ToLowerInvariant()]) — must be $want" }
        }
        $checked = @($exp.absent).Count + @($exp.present.Keys).Count
        if ($roleFailures.Count -eq 0) { Write-Host ("  OK   {0,-32} {1} checks" -f $roleName, $checked) -ForegroundColor Green }
        else {
            Write-Host ("  FAIL {0,-32} {1} of {2} checks" -f $roleName, $roleFailures.Count, $checked) -ForegroundColor Red
            foreach ($f in $roleFailures) { Write-Host "         $f" -ForegroundColor Red; $failures.Add("$roleName`: $f") }
        }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) mismatch(es). A LEFTOVER after a managed stage-and-upgrade is drift (added by hand); after a plain update import it means the wrong import method." -ForegroundColor Red
    exit 1
}
Write-Host 'All roles match the release expectations.' -ForegroundColor Green
exit 0
