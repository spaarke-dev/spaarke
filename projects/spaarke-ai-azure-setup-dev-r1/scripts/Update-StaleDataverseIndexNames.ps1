<#
.SYNOPSIS
    spaarke-ai-azure-setup-dev-r1 — Update stale sprk_searchindexname values in Dataverse to canonical names.

.DESCRIPTION
    Surfaces in scope:
      - sprk_matter, sprk_project, sprk_invoice records
      - businessunit records (cascade source for matter/project/invoice resolver)

    Rewrites:
      'spaarke-file-index' (singular)     → 'spaarke-files-index'
      'spaarke-knowledge-index-v2'        → 'spaarke-files-index'
      'discovery-index'                   → 'spaarke-discovery-index'  (per FR-14 reframe)
      'spaarke-knowledge-shared'          → 'spaarke-files-index'      (per FR-13/FR-14)

.PARAMETER DataverseUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER DryRun
    Preview only — no PATCH requests sent.
#>
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

Write-Host "Resolving Dataverse access token..."
$Token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $Token) { throw "Failed to get Dataverse token. Run 'az login' first." }

$ReadHeaders = @{ Authorization = "Bearer $Token" }
$WriteHeaders = @{
    Authorization    = "Bearer $Token"
    'Content-Type'   = 'application/json'
    'OData-Version'  = '4.0'
    'If-Match'       = '*'
}

# Rewrite rules: stale → canonical (per AI-SEARCH-INDEX-CATALOG.md §4 + §5 + FR-14 reframe)
$Rewrites = @{
    'spaarke-file-index'         = 'spaarke-files-index'
    'spaarke-knowledge-index-v2' = 'spaarke-files-index'
    'discovery-index'            = 'spaarke-discovery-index'
    'spaarke-knowledge-shared'   = 'spaarke-files-index'
}

$Targets = @(
    @{ EntitySet = 'sprk_matters';   IdField = 'sprk_matterid' }
    @{ EntitySet = 'sprk_projects';  IdField = 'sprk_projectid' }
    @{ EntitySet = 'sprk_invoices';  IdField = 'sprk_invoiceid' }
    @{ EntitySet = 'businessunits';  IdField = 'businessunitid' }
)

$Summary = [System.Collections.Generic.List[object]]::new()

foreach ($target in $Targets) {
    foreach ($stale in $Rewrites.Keys) {
        $newValue = $Rewrites[$stale]
        $filter = "sprk_searchindexname eq '$stale'"
        $queryUrl = "$DataverseUrl/api/data/v9.2/$($target.EntitySet)?`$select=$($target.IdField)&`$filter=$([uri]::EscapeDataString($filter))"

        try {
            $resp = Invoke-RestMethod -Uri $queryUrl -Headers $ReadHeaders
        } catch {
            Write-Host "  ERR query $($target.EntitySet) for '$stale': $($_.Exception.Message)" -ForegroundColor Red
            continue
        }

        $count = $resp.value.Count
        if ($count -eq 0) { continue }

        Write-Host ""
        Write-Host "$($target.EntitySet) where sprk_searchindexname='$stale' → '$newValue' ($count records)"

        if ($DryRun) {
            Write-Host "  [DRY-RUN] would PATCH $count records" -ForegroundColor Yellow
            $Summary.Add([PSCustomObject]@{ EntitySet = $target.EntitySet; Stale = $stale; New = $newValue; Updated = 0; Total = $count; Mode = 'dry-run' }) | Out-Null
            continue
        }

        $updated = 0; $failed = 0
        foreach ($r in $resp.value) {
            $id = $r.$($target.IdField)
            $patchUrl = "$DataverseUrl/api/data/v9.2/$($target.EntitySet)($id)"
            $body = @{ sprk_searchindexname = $newValue } | ConvertTo-Json -Compress
            try {
                Invoke-RestMethod -Uri $patchUrl -Method PATCH -Headers $WriteHeaders -Body $body | Out-Null
                $updated++
            } catch {
                $failed++
                Write-Host "  FAIL $id : $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        Write-Host "  Updated: $updated / $count (failed: $failed)" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
        $Summary.Add([PSCustomObject]@{ EntitySet = $target.EntitySet; Stale = $stale; New = $newValue; Updated = $updated; Total = $count; Mode = 'live' }) | Out-Null
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
if ($Summary.Count -eq 0) {
    Write-Host "No stale records found across all 4 entity sets × 4 rewrite rules." -ForegroundColor Green
} else {
    $Summary | Format-Table -AutoSize
    $totalUpdated = ($Summary | Measure-Object -Property Updated -Sum).Sum
    $totalRecords = ($Summary | Measure-Object -Property Total -Sum).Sum
    Write-Host ""
    Write-Host "TOTAL: $totalUpdated of $totalRecords records updated to canonical index names." -ForegroundColor Cyan
}
