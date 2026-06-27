<#
.SYNOPSIS
    Sync the Dataverse `sprk_aisearchindex` catalog to the canonical 8-index inventory.

.DESCRIPTION
    Phase G `DataverseAllowedIndexesProvider` queries this Dataverse entity at runtime
    to build the BFF's allow-list. If the catalog is out-of-sync with the canonical
    `AI-SEARCH-INDEX-CATALOG.md` §4 inventory, frontends targeting canonical indexes
    will be rejected with `400 INDEX_NOT_ALLOWED`.

    This script idempotently:
      - Deactivates legacy entries (statecode=1) carrying retired index names
      - Creates missing canonical entries (one per index in §4)
      - Sets `spaarke-files-index` as the default (sprk_isdefault=true)
      - Clears default flag on all other entries
#>
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$Token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $Token) { throw "Failed to get Dataverse token" }

$Read = @{ Authorization = "Bearer $Token"; 'OData-Version' = '4.0' }
$Write = @{
    Authorization    = "Bearer $Token"
    'Content-Type'   = 'application/json'
    'OData-Version'  = '4.0'
    'If-Match'       = '*'
    Prefer           = 'return=representation'
}

# Canonical 8-index catalog (per AI-SEARCH-INDEX-CATALOG.md §4)
$Canonical = @(
    [PSCustomObject]@{ Name='spaarke-files-index';        Display='SPE Files (Knowledge Tier)';      IsDefault=$true  }
    [PSCustomObject]@{ Name='spaarke-discovery-index';    Display='SPE Files (Discovery Tier)';      IsDefault=$false }
    [PSCustomObject]@{ Name='spaarke-records-index';      Display='Dataverse Records';                IsDefault=$false }
    [PSCustomObject]@{ Name='spaarke-rag-references';     Display='Golden Reference Docs (RAG)';     IsDefault=$false }
    [PSCustomObject]@{ Name='spaarke-insights-index';     Display='Derived Intelligence (Insights)'; IsDefault=$false }
    [PSCustomObject]@{ Name='spaarke-session-files';      Display='Chat Session Uploads';            IsDefault=$false }
    [PSCustomObject]@{ Name='spaarke-invoices-index';     Display='Invoice Semantic Search';         IsDefault=$false }
    [PSCustomObject]@{ Name='spaarke-playbook-embeddings'; Display='Playbook Dispatch Vectors';      IsDefault=$false }
)

# Retired names to deactivate (statecode=1) if found as active
$Retired = @('spaarke-file-index', 'spaarke-knowledge-index-v2', 'spaarke-knowledge-shared', 'discovery-index', 'spaarke-invoices-dev', 'playbook-embeddings')

Write-Host "=== Reading current sprk_aisearchindex catalog ==="
$existing = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes?`$select=sprk_aisearchindexid,sprk_searchindexname,sprk_displayname,sprk_isdefault,statecode" -Headers $Read
Write-Host "  Found $($existing.value.Count) records (active + inactive)"
foreach ($r in $existing.value) {
    $state = if ($r.statecode -eq 0) { 'active' } else { 'inactive' }
    Write-Host "    [$state] $($r.sprk_searchindexname) (default=$($r.sprk_isdefault))"
}

# ---------------------------------------------------------------------------
# Step 1: Deactivate retired entries
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 1: Deactivate retired entries ==="
foreach ($r in $existing.value) {
    if ($r.statecode -eq 0 -and $Retired -contains $r.sprk_searchindexname) {
        $action = "Deactivate '$($r.sprk_searchindexname)' (id=$($r.sprk_aisearchindexid))"
        if ($DryRun) { Write-Host "  [DRY-RUN] $action" -ForegroundColor Yellow; continue }
        try {
            Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes($($r.sprk_aisearchindexid))" -Method PATCH -Headers $Write -Body (@{
                statecode  = 1
                statuscode = 2  # Inactive
                sprk_isdefault = $false
            } | ConvertTo-Json -Compress) | Out-Null
            Write-Host "  ✓ Deactivated $($r.sprk_searchindexname)"
        } catch { Write-Host "  ✗ FAIL $($r.sprk_searchindexname): $($_.Exception.Message)" -ForegroundColor Red }
    }
}

# ---------------------------------------------------------------------------
# Step 2: Clear default flag on all active rows (will set canonical default next)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 2: Clear isdefault flag on all active rows ==="
foreach ($r in $existing.value) {
    if ($r.statecode -eq 0 -and $r.sprk_isdefault -eq $true) {
        if ($DryRun) { Write-Host "  [DRY-RUN] Clear default on '$($r.sprk_searchindexname)'" -ForegroundColor Yellow; continue }
        try {
            Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes($($r.sprk_aisearchindexid))" -Method PATCH -Headers $Write -Body (@{ sprk_isdefault = $false } | ConvertTo-Json -Compress) | Out-Null
            Write-Host "  ✓ Cleared default on $($r.sprk_searchindexname)"
        } catch { Write-Host "  ✗ FAIL: $($_.Exception.Message)" -ForegroundColor Red }
    }
}

# ---------------------------------------------------------------------------
# Step 3: Create or update canonical entries
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 3: Create or update canonical entries ==="
# Reload current state since we just deactivated some
$existing = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes?`$select=sprk_aisearchindexid,sprk_searchindexname,sprk_displayname,sprk_isdefault,statecode" -Headers $Read

foreach ($c in $Canonical) {
    $match = $existing.value | Where-Object { $_.sprk_searchindexname -eq $c.Name -and $_.statecode -eq 0 } | Select-Object -First 1
    if ($match) {
        # Existing active row — update display name + default flag if drift
        if ($match.sprk_displayname -ne $c.Display -or $match.sprk_isdefault -ne $c.IsDefault) {
            if ($DryRun) { Write-Host "  [DRY-RUN] Update '$($c.Name)' (display→'$($c.Display)', default→$($c.IsDefault))" -ForegroundColor Yellow; continue }
            try {
                Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes($($match.sprk_aisearchindexid))" -Method PATCH -Headers $Write -Body (@{
                    sprk_displayname = $c.Display
                    sprk_isdefault   = $c.IsDefault
                } | ConvertTo-Json -Compress) | Out-Null
                Write-Host "  ✓ Updated $($c.Name) (default=$($c.IsDefault))"
            } catch { Write-Host "  ✗ FAIL: $($_.Exception.Message)" -ForegroundColor Red }
        } else {
            Write-Host "  = OK $($c.Name) already canonical"
        }
    } else {
        # Missing — create
        if ($DryRun) { Write-Host "  [DRY-RUN] CREATE '$($c.Name)' (display='$($c.Display)', default=$($c.IsDefault))" -ForegroundColor Yellow; continue }
        try {
            $newRow = @{
                sprk_searchindexname = $c.Name
                sprk_displayname     = $c.Display
                sprk_isdefault       = $c.IsDefault
            } | ConvertTo-Json -Compress
            $created = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes" -Method POST -Headers $Write -Body $newRow
            Write-Host "  + Created $($c.Name) (id=$($created.sprk_aisearchindexid))"
        } catch { Write-Host "  ✗ FAIL creating $($c.Name): $($_.Exception.Message)" -ForegroundColor Red }
    }
}

Write-Host ""
Write-Host "=== Final state ==="
$final = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aisearchindexes?`$select=sprk_searchindexname,sprk_isdefault,statecode&`$orderby=sprk_searchindexname" -Headers $Read
foreach ($r in $final.value) {
    $state = if ($r.statecode -eq 0) { 'active  ' } else { 'inactive' }
    $def = if ($r.sprk_isdefault) { '[DEFAULT]' } else { '         ' }
    Write-Host "  [$state] $def $($r.sprk_searchindexname)"
}
