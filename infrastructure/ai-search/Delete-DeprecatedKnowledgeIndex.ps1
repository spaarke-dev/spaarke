<#
.SYNOPSIS
    Deletes the deprecated spaarke-knowledge-index (1536-dim, replaced by spaarke-knowledge-index-v2).

.DESCRIPTION
    This script permanently deletes the deprecated Azure AI Search index 'spaarke-knowledge-index'.
    The v2 index (spaarke-knowledge-index-v2, 3072-dim) is the active replacement.

    Safety checks:
    1. Verifies the v2 index exists and is operational before deleting the deprecated index
    2. Requires explicit confirmation before deletion
    3. Verifies deletion was successful

.PARAMETER ResourceGroup
    Resource group containing the Azure AI Search service.

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER DeprecatedIndexName
    Name of the deprecated index to delete. Defaults to 'spaarke-knowledge-index'.

.PARAMETER ActiveIndexName
    Name of the active v2 index to verify. Defaults to 'spaarke-knowledge-index-v2'.

.PARAMETER Force
    Skip confirmation prompt (for CI/CD use).

.EXAMPLE
    .\Delete-DeprecatedKnowledgeIndex.ps1 -ResourceGroup "spe-infrastructure-westus2" -SearchServiceName "spaarke-search-dev"

.NOTES
    Task PPI-036: Delete Deprecated AI Search Index (C8)
    Phase 4: Code Quality, Logging & Workspace
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup = "spe-infrastructure-westus2",

    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$DeprecatedIndexName = "spaarke-knowledge-index",

    [Parameter(Mandatory = $false)]
    [string]$ActiveIndexName = "spaarke-knowledge-index-v2",

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host "=== Delete Deprecated AI Search Index ===" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Search Service: $SearchServiceName"
Write-Host "Deprecated Index: $DeprecatedIndexName"
Write-Host "Active Index: $ActiveIndexName"
Write-Host ""

# Get search service admin key
Write-Host "Retrieving search service admin key..."
$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key. Ensure you have access to the search service."
    exit 1
}

$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

# Step 1: Verify v2 index exists and is operational
Write-Host "Step 1: Verifying active index '$ActiveIndexName' is operational..."
$activeIndexUrl = "$searchEndpoint/indexes/$ActiveIndexName`?api-version=2024-07-01"

try {
    $activeIndex = Invoke-RestMethod -Uri $activeIndexUrl -Method Get -Headers $headers
    $docCountUrl = "$searchEndpoint/indexes/$ActiveIndexName/docs/`$count?api-version=2024-07-01"
    $docCount = Invoke-RestMethod -Uri $docCountUrl -Method Get -Headers $headers
    Write-Host "  Active index '$ActiveIndexName' exists with $docCount documents." -ForegroundColor Green
}
catch {
    Write-Error "Active index '$ActiveIndexName' is NOT accessible. Aborting deletion to prevent data loss."
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Step 2: Check if deprecated index exists
Write-Host ""
Write-Host "Step 2: Checking if deprecated index '$DeprecatedIndexName' exists..."
$deprecatedIndexUrl = "$searchEndpoint/indexes/$DeprecatedIndexName`?api-version=2024-07-01"

try {
    $deprecatedIndex = Invoke-RestMethod -Uri $deprecatedIndexUrl -Method Get -Headers $headers
    $deprecatedDocCountUrl = "$searchEndpoint/indexes/$DeprecatedIndexName/docs/`$count?api-version=2024-07-01"
    $deprecatedDocCount = Invoke-RestMethod -Uri $deprecatedDocCountUrl -Method Get -Headers $headers
    Write-Host "  Deprecated index '$DeprecatedIndexName' exists with $deprecatedDocCount documents." -ForegroundColor Yellow
}
catch {
    Write-Host "  Deprecated index '$DeprecatedIndexName' does not exist. Nothing to delete." -ForegroundColor Green
    Write-Host ""
    Write-Host "=== Complete ===" -ForegroundColor Cyan
    exit 0
}

# Step 3: Confirm deletion
Write-Host ""
if (-not $Force) {
    Write-Host "WARNING: This will permanently delete index '$DeprecatedIndexName' with $deprecatedDocCount documents." -ForegroundColor Red
    Write-Host "The active index '$ActiveIndexName' ($docCount documents) will NOT be affected." -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host "Type 'DELETE' to confirm deletion"
    if ($confirm -ne "DELETE") {
        Write-Host "Deletion cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# Step 4: Delete the deprecated index
Write-Host ""
Write-Host "Step 3: Deleting deprecated index '$DeprecatedIndexName'..."

try {
    Invoke-RestMethod -Uri $deprecatedIndexUrl -Method Delete -Headers $headers
    Write-Host "  Deprecated index '$DeprecatedIndexName' deleted successfully!" -ForegroundColor Green
}
catch {
    Write-Error "Failed to delete deprecated index: $_"
    exit 1
}

# Step 5: Verify v2 index is still operational after deletion
Write-Host ""
Write-Host "Step 4: Verifying active index '$ActiveIndexName' is still operational..."

try {
    $verifyIndex = Invoke-RestMethod -Uri $activeIndexUrl -Method Get -Headers $headers
    $verifyDocCount = Invoke-RestMethod -Uri $docCountUrl -Method Get -Headers $headers
    Write-Host "  Active index '$ActiveIndexName' is operational with $verifyDocCount documents." -ForegroundColor Green
}
catch {
    Write-Error "WARNING: Active index verification failed after deletion! Check Azure portal immediately."
    exit 1
}

# Step 6: Verify deprecated index is gone
Write-Host ""
Write-Host "Step 5: Confirming deprecated index is removed..."

try {
    $checkDeleted = Invoke-RestMethod -Uri $deprecatedIndexUrl -Method Get -Headers $headers
    Write-Host "  WARNING: Deprecated index still appears to exist. Check Azure portal." -ForegroundColor Yellow
}
catch {
    Write-Host "  Confirmed: Deprecated index '$DeprecatedIndexName' no longer exists." -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Deletion Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor White
Write-Host "  Deleted: $DeprecatedIndexName (1536-dim, $deprecatedDocCount documents)" -ForegroundColor Red
Write-Host "  Active:  $ActiveIndexName ($verifyDocCount documents, operational)" -ForegroundColor Green
Write-Host ""
Write-Host "Storage savings: Index storage for $deprecatedDocCount documents reclaimed."
Write-Host ""
