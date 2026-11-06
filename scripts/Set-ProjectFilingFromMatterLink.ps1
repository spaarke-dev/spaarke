#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Files each project that is linked to exactly one matter (the Project <-> Matter N:N) and has no regarding pair under
    that matter, through the BFF. Report-only by default; -Apply writes.

.DESCRIPTION
    unified-access-control-r2 task 195 (#2352). A project's filing root is its ADR-024 regarding pair
    (sprk_regardingrecordid + sprk_regardingrecordtype); the Project <-> Matter N:N (sprk_Project_Matter_nn, intersect
    sprk_project_sprk_matter) is an association and confers nothing. Before task 195 the Create wizards wrote only the
    N:N, so a project linked to a matter that way is not filed under it. This script finds those projects and files them.

    It reads (as the operator's own az CLI identity, read-only):
      * every N:N row (sprk_project_sprk_matter);
      * every project that has one, with its pair;
      * the matter record type (sprk_recordtype_ref where sprk_recordlogicalname = 'sprk_matter').

    Each linked project is decided:
      FILE        exactly one N:N matter and no pair (neither half set)       -> filed under that matter (-Apply)
      SKIP-MANY   more than one N:N matter                                     -> reported; the owner decides by hand
      SKIP-PAIR   a pair is already set (either half)                          -> reported, with whether it names the N:N matter

    -Apply files each FILE row through the BFF route the Create wizards use — PATCH /api/v1/child-records/sprk_project/{id}
    with only the pair, never with ?move=true (a project the BFF finds filed elsewhere is left there) — so the BFF is
    the setter: it checks the caller's access, runs the secure gate, writes the pair as
    the application and secures the project when the matter is secure. (Repair-RegardingPairFiling.ps1 -Verify then
    reads the pair as set by the BFF.) Ownership then follows within minutes through the BFF's root-owner and
    ordinary-moves passes; this script changes no owner itself. Each FILE row ends as one of:
      FILED            the BFF answered 2xx and the pair reads back naming the matter
      SKIP-PAIR        the pair was re-read just before the PATCH and had been set since the report, or the BFF found the
                       project already filed under another record (409 project_filing.already_filed) -> not filed
      NOT-CHECKED      that re-read failed -> not filed (never a blind write)
      REFUSED          the BFF refused the PATCH (its words are shown)
      NOT-READ         the BFF answered 2xx but the pair reads back as something else
      FILED-UNVERIFIED the BFF answered 2xx but the read-back itself failed -> check by hand (Repair-RegardingPairFiling.ps1 -Verify)

    Exit code: 1 when -Apply could not file a FILE row (NOT-CHECKED, REFUSED, NOT-READ); 0 otherwise. SKIP-PAIR and
    FILED-UNVERIFIED are reported but do not fail the run. A dry run always exits 0.

.PARAMETER EnvironmentUrl
    The Dataverse environment. Default: dev (https://spaarkedev1.crm.dynamics.com).

.PARAMETER BffBaseUrl
    The BFF that files the projects (-Apply only). Default: dev (https://spaarke-bff-dev.azurewebsites.net). A trailing
    '/' and a trailing '/api' are removed (the script adds '/api/v1/...').

.PARAMETER ApiScope
    The scope of a token for the BFF API, minted with `az account get-access-token --scope` (-Apply only). The az login
    is the person the BFF checks: they need AppendTo on each matter and Write + Append on each project. Default: the dev
    BFF (api://1e40baad-e065-4aea-a8d4-4b7ab273458c/.default).

.PARAMETER Apply
    Write mode. Without it the script never writes and never calls the BFF.

.EXAMPLE
    .\Set-ProjectFilingFromMatterLink.ps1                       # dev, report only

.EXAMPLE
    .\Set-ProjectFilingFromMatterLink.ps1 -Apply                # dev, files the FILE rows (owner's go required)

.NOTES
    Order: deploy the BFF carrying task 195's filing route first; run report-only; -Apply after the owner's go. In dev on
    2026-10-31 all 7 linked projects were FILE rows and 3 of them change business unit through the root-owner step.
    Auth: az CLI tokens only (Dataverse and the BFF). No secrets, no Key Vault.
#>

[CmdletBinding()]
param(
    [string] $EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string] $BffBaseUrl = 'https://spaarke-bff-dev.azurewebsites.net',
    [string] $ApiScope = 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/.default',
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
# The route is appended as /api/v1/...; accept the base with or without a trailing '/' or '/api'.
$BffBaseUrl = ($BffBaseUrl.TrimEnd('/') -replace '/api$', '').TrimEnd('/')
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the same names as SecureRootInheritance: PairIdColumn, PairTypeColumn) ─────────────────────────────
$PairId = 'sprk_regardingrecordid'
$PairTypeValue = '_sprk_regardingrecordtype_value'
$Intersect = 'sprk_project_sprk_matter'

# ── Dataverse (read-only, the operator's own identity) ────────────────────────────────────────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}
function Invoke-DvGet([string] $Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvGetAll([string] $Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=500'
    $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    , $rows
}
function ConvertTo-Id($Value) { if ($null -eq $Value -or "$Value" -eq '') { return $null }; "$Value".Trim('{', '}').ToLowerInvariant() }

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Apply) { "APPLY (files through $BffBaseUrl)" } else { 'REPORT ONLY (no writes)' }))

$matterType = @((Invoke-DvGet "sprk_recordtype_refs?`$select=sprk_recordtype_refid&`$filter=sprk_recordlogicalname eq 'sprk_matter' and statecode eq 0").value)
if ($matterType.Count -ne 1) { throw "Expected exactly one active sprk_recordtype_ref for sprk_matter, found $($matterType.Count)." }
$matterTypeId = ConvertTo-Id $matterType[0].sprk_recordtype_refid

function Get-PrimaryName([string] $Table) { (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=PrimaryNameAttribute").PrimaryNameAttribute }
$projectName = Get-PrimaryName 'sprk_project'
$matterName = Get-PrimaryName 'sprk_matter'
$projectLabelColumns = @(@('sprk_projectnumber', $projectName) | Select-Object -Unique)
$intersectSet = (Invoke-DvGet "EntityDefinitions(LogicalName='$Intersect')?`$select=EntitySetName").EntitySetName

# ── Read: the N:N rows, grouped by project ────────────────────────────────────────────────────────────────────────
$links = @{}
foreach ($row in (Invoke-DvGetAll "$intersectSet`?`$select=sprk_projectid,sprk_matterid")) {
    $p = ConvertTo-Id $row.sprk_projectid; $m = ConvertTo-Id $row.sprk_matterid
    if (-not $p -or -not $m) { continue }
    if (-not $links.ContainsKey($p)) { $links[$p] = [System.Collections.Generic.List[string]]::new() }
    if (-not $links[$p].Contains($m)) { $links[$p].Add($m) }
}
Write-Host "N:N rows    : $(@($links.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) across $($links.Count) project(s)"

$matterLabels = @{}
function Get-MatterLabel([string] $Id) {
    if (-not $matterLabels.ContainsKey($Id)) {
        try {
            $cols = @('sprk_matternumber', $matterName) | Select-Object -Unique
            $m = Invoke-DvGet "sprk_matters($Id)?`$select=$($cols -join ',')"
            $matterLabels[$Id] = (@($cols | ForEach-Object { $m.$_ } | Where-Object { $_ }) -join ' ')
        } catch { $matterLabels[$Id] = '(unreadable)' }
    }
    $matterLabels[$Id]
}

# ── Decide ────────────────────────────────────────────────────────────────────────────────────────────────────────
$decisions = [System.Collections.Generic.List[object]]::new()
foreach ($projectId in ($links.Keys | Sort-Object)) {
    $project = Invoke-DvGet "sprk_projects($projectId)?`$select=$(($projectLabelColumns + $PairId + $PairTypeValue) -join ',')"
    $matters = @($links[$projectId])
    # Not $pairId / $pairType: PowerShell names are case-insensitive, and those would overwrite $PairId / $PairTypeValue.
    $setId = ConvertTo-Id $project.$PairId
    $setType = ConvertTo-Id $project.$PairTypeValue
    $label = @($projectLabelColumns | ForEach-Object { $project.$_ } | Where-Object { $_ }) -join ' '

    $decision = if ($setId -or $setType) { 'SKIP-PAIR' } elseif ($matters.Count -gt 1) { 'SKIP-MANY' } else { 'FILE' }
    $note = switch ($decision) {
        'FILE' { "-> $(Get-MatterLabel $matters[0]) ($($matters[0]))" }
        'SKIP-MANY' { "$($matters.Count) matters: " + (($matters | ForEach-Object { Get-MatterLabel $_ }) -join '; ') }
        'SKIP-PAIR' {
            $namesLinked = $setId -and $setType -eq $matterTypeId -and $matters -contains $setId
            "pair names $(if ($setId) { $setId } else { '(no id)' }) of type $(if ($setType) { $setType } else { '(no type)' }); " +
                $(if ($namesLinked) { 'it is the linked matter' } else { 'NOT the linked matter' })
        }
    }
    $decisions.Add([pscustomobject]@{ ProjectId = $projectId; Project = $label; Decision = $decision; MatterId = $(if ($decision -eq 'FILE') { $matters[0] } else { $null }); Note = $note })
}

Write-Host ''
foreach ($d in $decisions) {
    $colour = switch ($d.Decision) { 'FILE' { 'Cyan' } default { 'Yellow' } }
    Write-Host ("  {0,-10} {1}  {2}  {3}" -f $d.Decision, $d.ProjectId, $d.Project, $d.Note) -ForegroundColor $colour
}
$toFile = @($decisions | Where-Object Decision -eq 'FILE')
Write-Host ''
Write-Host ("Summary     : FILE {0}, SKIP-MANY {1}, SKIP-PAIR {2}" -f $toFile.Count,
    @($decisions | Where-Object Decision -eq 'SKIP-MANY').Count, @($decisions | Where-Object Decision -eq 'SKIP-PAIR').Count)

if (-not $Apply) {
    Write-Host 'Report only: nothing was written. Re-run with -Apply (after the owner''s go) to file the FILE rows.'
    exit 0
}

# ── Apply: file through the BFF, then read back ───────────────────────────────────────────────────────────────────
$bffToken = az account get-access-token --scope $ApiScope --query accessToken -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $bffToken) { throw "Could not get a token for $ApiScope. Run 'az login' and retry." }
$bffHeaders = @{ Authorization = "Bearer $bffToken"; 'Content-Type' = 'application/json; charset=utf-8'; Accept = 'application/json' }

$failed = 0; $filed = 0; $unverified = 0; $skipped = 0
foreach ($d in $toFile) {
    # Re-read the pair just before writing: a pair set since the report (by a person or another run) is left alone.
    try {
        $now = Invoke-DvGet "sprk_projects($($d.ProjectId))?`$select=$PairId,$PairTypeValue"
    } catch {
        $failed++
        Write-Host "  NOT-CHECKED $($d.ProjectId)  $($d.Project)  the pair could not be re-read, so it was not filed: $($_.Exception.Message)" -ForegroundColor Red
        continue
    }
    if ((ConvertTo-Id $now.$PairId) -or (ConvertTo-Id $now.$PairTypeValue)) {
        $skipped++
        Write-Host "  SKIP-PAIR  $($d.ProjectId)  $($d.Project)  a pair was set since the report ('$($now.$PairId)' / '$($now.$PairTypeValue)'); not filed" -ForegroundColor Yellow
        continue
    }

    $body = @{ $PairId = $d.MatterId; 'sprk_regardingrecordtype@odata.bind' = "/sprk_recordtype_refs($matterTypeId)" } | ConvertTo-Json -Compress
    try {
        Invoke-RestMethod -Uri "$BffBaseUrl/api/v1/child-records/sprk_project/$($d.ProjectId)" -Headers $bffHeaders -Method Patch -Body $body | Out-Null
    } catch {
        $patchError = $_
        $detail = $patchError.ErrorDetails.Message
        $status = $null
        if ($patchError.Exception.Response) { $status = [int] $patchError.Exception.Response.StatusCode }
        $code = $null
        if ($detail) { try { $code = ($detail | ConvertFrom-Json -ErrorAction Stop).reasonCode } catch { $code = $null } }
        if ($status -eq 409 -and $code -eq 'project_filing.already_filed') {
            # No move is ever asked: the BFF left a project it found filed under another record where it is.
            $skipped++
            Write-Host "  SKIP-PAIR  $($d.ProjectId)  $($d.Project)  the BFF found it already filed under another record; not filed" -ForegroundColor Yellow
            continue
        }
        $failed++
        if (-not $detail) { $detail = $patchError.Exception.Message }
        Write-Host "  REFUSED    $($d.ProjectId)  $($d.Project)  $detail" -ForegroundColor Red
        continue
    }

    # The BFF answered 2xx: the write happened. A failed read-back is not a refusal.
    try {
        $after = Invoke-DvGet "sprk_projects($($d.ProjectId))?`$select=$PairId,$PairTypeValue"
    } catch {
        $unverified++
        Write-Host "  FILED-UNVERIFIED $($d.ProjectId)  $($d.Project)  the BFF answered 2xx; the read-back failed: $($_.Exception.Message)" -ForegroundColor Yellow
        continue
    }
    if ((ConvertTo-Id $after.$PairId) -eq $d.MatterId -and (ConvertTo-Id $after.$PairTypeValue) -eq $matterTypeId) {
        $filed++
        Write-Host "  FILED      $($d.ProjectId)  $($d.Project)" -ForegroundColor Green
    } else {
        $failed++
        Write-Host "  NOT-READ   $($d.ProjectId)  the BFF answered 2xx but the pair reads back as '$($after.$PairId)' / '$($after.$PairTypeValue)'" -ForegroundColor Red
    }
}

Write-Host ''
Write-Host ("Applied     : of {0}: filed {1}, filed but unverified {2}, skipped (filed elsewhere since the report) {3}, failed {4}." -f $toFile.Count, $filed, $unverified, $skipped, $failed)
Write-Host ("Owners follow through the BFF's root-owner pass (about 5 minutes) and children through ordinary moves (about 2 minutes).")
if ($failed -gt 0) { exit 1 }
exit 0
