<#
.SYNOPSIS
    Refuses a stale or partial ribbon export: every unmanaged ribbon customisation the environment holds for a table
    must be in that table's exported RibbonDiff.xml. Read-only.

.DESCRIPTION
    An unmanaged ribbon import REPLACES a table's whole unmanaged ribbon customisation with the RibbonDiff it carries.
    An export taken before someone else's ribbon import (another task's rules, task 180's sprk.CreatePrivilege.* rules,
    the Access group, the secure-child New), or a partial export, therefore deletes what it lacks, silently.

    For each Entities/<table>/RibbonDiff.xml under -UnpackedDir this reads, from -EnvironmentUrl, the table's unmanaged
      - ribbon commands      (ribboncommands.command)   -> must be a CommandDefinition Id,
      - ribbon rules         (ribbonrules.ruleid)       -> must be a DisplayRule / EnableRule Id,
      - custom actions, hide actions and labels (ribbondiffs.diffid) -> must be an Id or HideActionId,
    and returns one line per id the export lacks. Empty result = the export is current.

    Checked against spaarkedev1 on 2026-10-09: the checked-in SpaarkeMaster exports of sprk_matter, sprk_document,
    sprk_analysis, sprk_event and email (exported 2026-10-08) miss none of the live ids.

    Used by CreatePrivilegeRibbons/Set-CreatePrivilegeRibbon.ps1 -Apply and scripts/Deploy-SecureChildNewCommands.ps1
    (unified-access-control-r2 task 180, verifier items K1/K2).

.PARAMETER EnvironmentUrl
    https://<org>.crm.dynamics.com

.PARAMETER Token
    A Dataverse bearer token for -EnvironmentUrl.

.PARAMETER UnpackedDir
    The unpacked solution (the folder holding Entities/).

.OUTPUTS
    [string] one per missing id: "<table>: <kind> <id>".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EnvironmentUrl,
    [Parameter(Mandatory)] [string] $Token,
    [Parameter(Mandatory)] [string] $UnpackedDir
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$headers = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0' }

function Get-UnmanagedIds([string] $set, [string] $column, [string] $table) {
    $filter = [uri]::EscapeDataString("entity eq '$table' and ismanaged eq false")
    $uri = "$EnvironmentUrl/api/data/v9.2/${set}?`$select=$column&`$filter=$filter"
    $ids = @()
    while ($uri) {
        $page = Invoke-RestMethod -Uri $uri -Method Get -Headers ($headers + @{ Prefer = 'odata.maxpagesize=5000' })
        $ids += @($page.value | ForEach-Object { $_.$column })
        $uri = $page.'@odata.nextLink'
    }
    return @($ids | Where-Object { $_ } | Sort-Object -Unique)
}

$entitiesDir = Join-Path $UnpackedDir 'Entities'
if (-not (Test-Path -LiteralPath $entitiesDir)) { return }
foreach ($ribbonDiff in @(Get-ChildItem -LiteralPath $entitiesDir -Recurse -Filter 'RibbonDiff.xml')) {
    $table = $ribbonDiff.Directory.Name.ToLowerInvariant()
    $text = Get-Content -Raw -LiteralPath $ribbonDiff.FullName
    $exported = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($text, '\b(?:Id|HideActionId)="([^"]+)"')) { [void] $exported.Add($m.Groups[1].Value) }
    foreach ($check in @(
            @{ Set = 'ribboncommands'; Column = 'command'; Kind = 'command' }
            @{ Set = 'ribbonrules'; Column = 'ruleid'; Kind = 'rule' }
            @{ Set = 'ribbondiffs'; Column = 'diffid'; Kind = 'custom/hide action or label' })) {
        foreach ($id in (Get-UnmanagedIds $check.Set $check.Column $table)) {
            if (-not $exported.Contains($id)) { "${table}: $($check.Kind) $id" }
        }
    }
}
