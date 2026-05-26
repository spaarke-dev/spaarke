<#
.SYNOPSIS
    One-time backfill of sprk_hasfile=true on Dataverse sprk_document records
    that have valid SPE pointers (sprk_graphdriveid + sprk_graphitemid) but were
    left with sprk_hasfile=false due to a client-side oversight in
    DocumentRecordService.ts / EntityCreationService.ts.

.DESCRIPTION
    The bug (fixed 2026-05-14 in @spaarke/ui-components): client-side document
    creation payloads never included `sprk_hasfile: true`, so every document
    uploaded via the UI ended up with the flag stuck at the Dataverse default
    (false). Server-side filters that gate on `sprk_hasfile eq true` (RAG
    indexing, scheduled indexing service, ribbon button visibility) silently
    skipped these documents.

    This script finds records where the flag is false but the SPE pointers are
    valid (Drive starts with "b!" + length >= 20, Item length >= 20) and flips
    the flag to true. Idempotent: re-running finds zero records.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER WhatIf
    Show what would change without doing it.

.PARAMETER MaxRecords
    Safety cap. Default 1000.

.EXAMPLE
    .\Backfill-DocumentHasFile.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -WhatIf
    .\Backfill-DocumentHasFile.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"
    .\Backfill-DocumentHasFile.ps1 -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com"
#>
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$WhatIf,

    [int]$MaxRecords = 1000
)

$ErrorActionPreference = 'Stop'

# Strip trailing slash
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

Write-Host ""
Write-Host "=== sprk_hasfile Backfill ===" -ForegroundColor Cyan
Write-Host "  Environment: $EnvironmentUrl"
Write-Host "  Mode:        $(if ($WhatIf) { 'WHATIF (no writes)' } else { 'EXECUTE (will write)' })"
Write-Host "  Max records: $MaxRecords"
Write-Host ""

# Acquire token for Dataverse Web API
Write-Host "Acquiring Dataverse access token..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to acquire token for $EnvironmentUrl. Try 'az login' or check 'az account show'."
}

$headers = @{
    Authorization      = "Bearer $token"
    Accept             = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "If-Match"         = "*"
    Prefer             = 'return=minimal'
}

# Query candidates
Write-Host "Querying candidates (sprk_hasfile=false AND drive/item populated)..." -ForegroundColor Yellow
$filter = "sprk_hasfile eq false and sprk_graphdriveid ne null and sprk_graphitemid ne null"
$select = "sprk_documentid,sprk_documentname,sprk_graphdriveid,sprk_graphitemid"
$queryUrl = "$EnvironmentUrl/api/data/v9.2/sprk_documents?`$filter=$filter&`$select=$select&`$top=$MaxRecords"

$queryHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}

$response = Invoke-RestMethod -Uri $queryUrl -Headers $queryHeaders -Method GET
$records = $response.value

Write-Host "  Found $($records.Count) candidate records" -ForegroundColor Green

if ($records.Count -eq 0) {
    Write-Host ""
    Write-Host "Nothing to backfill. Exiting." -ForegroundColor Green
    exit 0
}

# Apply validation: DriveId must start with "b!" and be >20 chars, ItemId >20 chars
$valid = $records | Where-Object {
    $_.sprk_graphdriveid -like "b!*" -and
    $_.sprk_graphdriveid.Length -gt 20 -and
    $_.sprk_graphitemid.Length -gt 20
}

$invalid = @($records) | Where-Object {
    -not ($_.sprk_graphdriveid -like "b!*" -and
          $_.sprk_graphdriveid.Length -gt 20 -and
          $_.sprk_graphitemid.Length -gt 20)
}

Write-Host "  Valid (will update):     $($valid.Count)" -ForegroundColor Green
if ($invalid.Count -gt 0) {
    Write-Host "  Invalid (will SKIP):     $($invalid.Count)" -ForegroundColor Yellow
    $invalid | ForEach-Object {
        Write-Host "    SKIP: $($_.sprk_documentid) - $($_.sprk_documentname) - drive='$($_.sprk_graphdriveid)' item='$($_.sprk_graphitemid)'" -ForegroundColor Gray
    }
}

if ($WhatIf) {
    Write-Host ""
    Write-Host "WhatIf: would update $($valid.Count) records. Re-run without -WhatIf to execute." -ForegroundColor Cyan
    exit 0
}

# Execute updates
Write-Host ""
Write-Host "Updating $($valid.Count) records..." -ForegroundColor Yellow
$updated = 0
$errors = 0
$total = $valid.Count

$body = '{"sprk_hasfile":true}'

for ($i = 0; $i -lt $total; $i++) {
    $r = $valid[$i]
    $patchUrl = "$EnvironmentUrl/api/data/v9.2/sprk_documents($($r.sprk_documentid))"
    try {
        Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method PATCH -Body $body -ContentType "application/json" | Out-Null
        $updated++
        if (($updated % 25) -eq 0 -or $updated -eq $total) {
            Write-Host "  Progress: $updated/$total" -ForegroundColor Gray
        }
    } catch {
        $errors++
        Write-Host "  ERROR on $($r.sprk_documentid) ($($r.sprk_documentname)): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "  Updated: $updated" -ForegroundColor Green
Write-Host "  Errors:  $errors $(if ($errors -gt 0) { '(see lines above)' })" -ForegroundColor $(if ($errors -gt 0) { 'Red' } else { 'Gray' })

if ($errors -gt 0) { exit 1 }
exit 0
