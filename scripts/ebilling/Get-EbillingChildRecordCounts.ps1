<#
.SYNOPSIS
    READ-ONLY census for ebilling-r1 082c C19 (fix round 1, K3): how many core records sit DIRECTLY under each matter and project, against the
    bound the rule-change close reads (ProcedureAssignmentService.MaxChildRecords = 100).

.DESCRIPTION
    A changed rule's close (ProcedureAssignmentService.SupersedeBaseFindingsAsync) reads the records directly under the record the change is on
    (CoreRecordChain.ReadChildrenAsync). Past MaxChildRecords candidates the read stops and the close of the records under that record is
    dropped (the record's own invoices still close; the answer says the rest stay open). This script counts, for every matter and project, the
    candidates that read would count, so a parent near the bound is known before a change is made on it.

    The candidates are the ones ReadChildrenAsync collects, BEFORE its forward-chain verification (the bound is checked on them, so they are
    what the bound compares):
      * sprk_workassignment rows whose typed filing lookup names the parent (sprk_regardingmatter for a matter, sprk_regardingproject for a
        project), INCLUDING inactive rows (the read has no state filter);
      * sprk_project rows whose ADR-024 pair text (sprk_regardingrecordid) holds the first 8 hex digits of the parent's id (the LIKE '%frag%'
        technique; Dataverse compares text without case), the parent itself excluded.
    No second hop: a child's own children are not counted for the parent.

    Output is counts only: no record id or name. -ListFlagged additionally prints "table id count" for each flagged parent (default off).
    It makes GET requests only (one function, Invoke-DvGet, paging by @odata.nextLink); it writes nothing.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required.

.PARAMETER Bound
    The bound to compare with. Default 100 (ProcedureAssignmentService.MaxChildRecords).

.PARAMETER FlagPercent
    A parent at or over this percentage of the bound is flagged. Default 80.

.PARAMETER ListFlagged
    Also print the table, id and candidate count of each flagged parent.

.PARAMETER AsJson
    Print the census as one JSON object instead of a table.

.EXAMPLE
    pwsh scripts/ebilling/Get-EbillingChildRecordCounts.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [int]$Bound = 100,
    [int]$FlagPercent = 80,
    [switch]$ListFlagged,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"
$FlagAt = [math]::Ceiling($Bound * $FlagPercent / 100.0)

# ---- Dataverse access: GET only ----------------------------------------------------------------------------------------------------
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw "Could not get a token for $DataverseUrl (az login?)." }
$headers = @{
    Authorization      = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = 'odata.maxpagesize=5000'
}

function Invoke-DvGet([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $uri = "$ApiBase/$Path"
    while ($uri) {
        try { $page = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers }
        catch {
            $detail = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
            throw "GET $uri failed: $detail"
        }
        foreach ($row in $page.value) { $rows.Add($row) }
        $uri = $page.'@odata.nextLink'
    }
    return , $rows
}

# ---- Read (no state filter: inactive rows count, as ReadChildrenAsync counts them) --------------------------------------------------
$matters = Invoke-DvGet 'sprk_matters?$select=sprk_matterid'
$projects = Invoke-DvGet 'sprk_projects?$select=sprk_projectid,sprk_regardingrecordid'
$workAssignments = Invoke-DvGet 'sprk_workassignments?$select=sprk_workassignmentid,_sprk_regardingmatter_value,_sprk_regardingproject_value&$filter=_sprk_regardingmatter_value ne null or _sprk_regardingproject_value ne null'

# Candidate children per parent, keyed "table|id" with the id in lower-case D format.
$counts = @{}
function Add-Child([string]$Table, [string]$Id) {
    $key = "$Table|$($Id.ToLowerInvariant())"
    $counts[$key] = 1 + [int]$counts[$key]
}

foreach ($w in $workAssignments) {
    if ($w._sprk_regardingmatter_value) { Add-Child 'sprk_matter' $w._sprk_regardingmatter_value }
    if ($w._sprk_regardingproject_value) { Add-Child 'sprk_project' $w._sprk_regardingproject_value }
}

# Pair text: a project is a candidate of every parent whose first-8-hex fragment its text holds (LIKE '%frag%', case-insensitive).
$fragments = @{}   # fragment -> list of "table|id" of the parents that own it
foreach ($m in $matters) { $id = $m.sprk_matterid.ToLowerInvariant(); $f = $id.Replace('-', '').Substring(0, 8); if (-not $fragments.ContainsKey($f)) { $fragments[$f] = [System.Collections.Generic.List[string]]::new() }; $fragments[$f].Add("sprk_matter|$id") }
foreach ($p in $projects) { $id = $p.sprk_projectid.ToLowerInvariant(); $f = $id.Replace('-', '').Substring(0, 8); if (-not $fragments.ContainsKey($f)) { $fragments[$f] = [System.Collections.Generic.List[string]]::new() }; $fragments[$f].Add("sprk_project|$id") }

$pairProjects = 0
foreach ($p in $projects) {
    $text = $p.sprk_regardingrecordid
    if (-not $text) { continue }
    $pairProjects++
    $self = "sprk_project|$($p.sprk_projectid.ToLowerInvariant())"
    $lower = $text.ToLowerInvariant()
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    for ($i = 0; $i + 8 -le $lower.Length; $i++) {
        $window = $lower.Substring($i, 8)
        if (-not $fragments.ContainsKey($window)) { continue }
        foreach ($parent in $fragments[$window]) {
            if ($parent -ne $self -and $seen.Add($parent)) { $counts[$parent] = 1 + [int]$counts[$parent] }
        }
    }
}

# ---- Report -----------------------------------------------------------------------------------------------------------------------
$parentCounts = @($counts.GetEnumerator() | ForEach-Object { [pscustomobject]@{ Key = $_.Key; Count = [int]$_.Value } })
$max = if ($parentCounts.Count -gt 0) { ($parentCounts | Measure-Object -Property Count -Maximum).Maximum } else { 0 }
$flagged = @($parentCounts | Where-Object { $_.Count -ge $FlagAt })
$over = @($parentCounts | Where-Object { $_.Count -gt $Bound })

$census = [ordered]@{
    environment             = $DataverseUrl
    bound                   = $Bound
    flagAtOrOver            = $FlagAt
    matters                 = $matters.Count
    projects                = $projects.Count
    workAssignmentsFiled    = $workAssignments.Count
    projectsWithPairText    = $pairProjects
    parentsWithChildren     = $parentCounts.Count
    maxChildCandidates      = $max
    parentsAtOrOverFlag     = $flagged.Count
    parentsOverTheBound     = $over.Count
}

if ($AsJson) { $census | ConvertTo-Json } else { $census.GetEnumerator() | ForEach-Object { '{0,-24} {1}' -f $_.Key, $_.Value } }

if ($ListFlagged -and $flagged.Count -gt 0) {
    ''
    'Flagged parents (table id candidates):'
    $flagged | Sort-Object -Property Count -Descending | ForEach-Object { '{0} {1}' -f ($_.Key -replace '\|', ' '), $_.Count }
}
