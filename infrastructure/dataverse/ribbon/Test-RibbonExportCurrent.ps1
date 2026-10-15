<#
.SYNOPSIS
    Refuses a stale or partial ribbon export: every unmanaged ribbon customisation the environment holds for a table must
    be in that table's exported RibbonDiff.xml WITH THE SAME CONTENT (missing or changed both refuse). Read-only.

.DESCRIPTION
    An unmanaged ribbon import REPLACES a table's whole unmanaged ribbon customisation with the RibbonDiff it carries.
    An export taken before someone else's ribbon import (another task's rules, task 180's sprk.CreatePrivilege.* rules,
    the Access group, the secure-child New), or a partial export, therefore deletes what it lacks, silently.

    For each Entities/<table>/RibbonDiff.xml under -UnpackedDir this reads, from -EnvironmentUrl, the table's unmanaged
      - ribbon commands      (ribboncommands.command + commanddefinition),
      - ribbon rules         (ribbonrules.ruleid + ruledefinition),
      - custom actions, hide actions and labels (ribbondiffs.diffid + rdx),
    and, for each, looks for an element of the same name with that Id / HideActionId in the export whose content is the
    same after normalisation (attributes sorted, whitespace-only text and comments dropped, text trimmed). It returns one
    line per id the export lacks ("missing") or holds with different content ("changed": e.g. another import edited
    the command, rule or label in place after this export was taken). Empty result = the export is current.

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

function Get-UnmanagedRows([string] $set, [string] $idColumn, [string] $xmlColumn, [string] $table) {
    $filter = [uri]::EscapeDataString("entity eq '$table' and ismanaged eq false")
    $uri = "$EnvironmentUrl/api/data/v9.2/${set}?`$select=$idColumn,$xmlColumn&`$filter=$filter"
    while ($uri) {
        $page = Invoke-RestMethod -Uri $uri -Method Get -Headers ($headers + @{ Prefer = 'odata.maxpagesize=5000' })
        foreach ($row in @($page.value)) { if ($row.$idColumn) { [pscustomobject] @{ Id = $row.$idColumn; Xml = $row.$xmlColumn } } }
        $uri = $page.'@odata.nextLink'
    }
}

# A content fingerprint of an element: name, attributes sorted by name, children in order; whitespace-only text and
# comments dropped, text trimmed.
function Get-Canonical([System.Xml.XmlNode] $node) {
    switch ($node.NodeType) {
        'Element' {
            $attrs = @($node.Attributes | Where-Object { $_.Name -notlike 'xmlns*' } | Sort-Object Name |
                ForEach-Object { "$($_.LocalName)=$($_.Value)" }) -join ' '
            $children = (@($node.ChildNodes) | ForEach-Object { Get-Canonical $_ }) -join ''
            return "<$($node.LocalName) $attrs>$children</>"
        }
        { $_ -in 'Text', 'CDATA' } { $t = $node.Value.Trim(); if ($t) { return "[$t]" } else { return '' } }
        default { return '' }
    }
}

$entitiesDir = Join-Path $UnpackedDir 'Entities'
if (-not (Test-Path -LiteralPath $entitiesDir)) { return }
foreach ($ribbonDiff in @(Get-ChildItem -LiteralPath $entitiesDir -Recurse -Filter 'RibbonDiff.xml')) {
    $table = $ribbonDiff.Directory.Name.ToLowerInvariant()
    $doc = New-Object System.Xml.XmlDocument
    $doc.LoadXml((Get-Content -Raw -LiteralPath $ribbonDiff.FullName))
    # "<element name>|<Id or HideActionId>" -> the canonical forms of every exported element carrying it (a rule id also
    # appears as an empty reference inside a command; the definition is one of the candidates).
    $exported = @{}
    foreach ($e in @($doc.SelectNodes('//*[@Id or @HideActionId]'))) {
        $key = "$($e.LocalName)|$(if ($e.HasAttribute('Id')) { $e.GetAttribute('Id') } else { $e.GetAttribute('HideActionId') })"
        if (-not $exported.ContainsKey($key)) { $exported[$key] = New-Object System.Collections.Generic.List[string] }
        $exported[$key].Add((Get-Canonical $e))
    }
    foreach ($check in @(
            @{ Set = 'ribboncommands'; Id = 'command'; Xml = 'commanddefinition'; Kind = 'command' }
            @{ Set = 'ribbonrules'; Id = 'ruleid'; Xml = 'ruledefinition'; Kind = 'rule' }
            @{ Set = 'ribbondiffs'; Id = 'diffid'; Xml = 'rdx'; Kind = 'custom/hide action or label' })) {
        foreach ($row in @(Get-UnmanagedRows $check.Set $check.Id $check.Xml $table)) {
            $live = New-Object System.Xml.XmlDocument
            try { $live.LoadXml($row.Xml) } catch { "${table}: $($check.Kind) $($row.Id) - the live definition could not be parsed; compare by hand"; continue }
            $key = "$($live.DocumentElement.LocalName)|$($row.Id)"
            if (-not $exported.ContainsKey($key)) { "${table}: $($check.Kind) $($row.Id) missing"; continue }
            if (-not $exported[$key].Contains((Get-Canonical $live.DocumentElement))) {
                "${table}: $($check.Kind) $($row.Id) changed (the live content differs from the export)"
            }
        }
    }
}
