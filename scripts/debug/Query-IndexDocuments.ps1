<#
.SYNOPSIS
    Diagnostic script to query Azure AI Search index for semantic search debugging.

.DESCRIPTION
    Queries the RAG index to understand:
    - What tenantId values exist
    - Whether documentId is populated
    - Whether documentVector3072 is populated
    - Sample documents for testing

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER IndexName
    Name of the index. Defaults to 'spaarke-knowledge-shared'.

.PARAMETER ResourceGroup
    Azure resource group name.

.EXAMPLE
    .\Query-IndexDocuments.ps1
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-knowledge-index-v2",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "spe-infrastructure-westus2"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Index Diagnostic Query" -ForegroundColor Cyan
Write-Host " Service: $SearchServiceName" -ForegroundColor Cyan
Write-Host " Index: $IndexName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get admin key
Write-Host "Retrieving admin key..."
$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key"
    exit 1
}

$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

# Get document count
$countUrl = "$searchEndpoint/indexes/$IndexName/docs/`$count?api-version=2024-07-01"
try {
    $count = Invoke-RestMethod -Uri $countUrl -Headers $headers -Method GET
    Write-Host "Total documents in index: $count" -ForegroundColor Green
} catch {
    Write-Host "Failed to get document count: $_" -ForegroundColor Red
    Write-Host "Index '$IndexName' may not exist." -ForegroundColor Yellow
    exit 1
}

if ($count -eq 0) {
    Write-Host "Index is empty - no documents to query" -ForegroundColor Yellow
    exit 0
}

Write-Host ""

# Query for unique tenantId values
Write-Host "--- TenantId Distribution ---" -ForegroundColor Yellow
$facetBody = @{
    search = "*"
    facets = @("tenantId,count:100")
    top = 0
} | ConvertTo-Json

$searchUrl = "$searchEndpoint/indexes/$IndexName/docs/search?api-version=2024-07-01"
$facetResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $facetBody

if ($facetResult.'@search.facets'.tenantId) {
    foreach ($facet in $facetResult.'@search.facets'.tenantId) {
        Write-Host "  TenantId: $($facet.value) (count: $($facet.count))" -ForegroundColor White
    }
} else {
    Write-Host "  No tenantId facets returned" -ForegroundColor Yellow
}

Write-Host ""

# Query sample documents with vector info
Write-Host "--- Sample Documents ---" -ForegroundColor Yellow
$sampleBody = @{
    search = "*"
    select = "id,documentId,speFileId,fileName,tenantId,fileType"
    top = 10
} | ConvertTo-Json

$sampleResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $sampleBody

foreach ($doc in $sampleResult.value) {
    $hasDocId = if ($doc.documentId) { "Yes" } else { "No" }
    $hasSpeId = if ($doc.speFileId) { "Yes" } else { "No" }
    Write-Host "  File: $($doc.fileName)" -ForegroundColor White
    Write-Host "    TenantId: $($doc.tenantId)" -ForegroundColor Gray
    Write-Host "    DocumentId: $($doc.documentId) (HasValue: $hasDocId)" -ForegroundColor Gray
    Write-Host "    SpeFileId: $($doc.speFileId) (HasValue: $hasSpeId)" -ForegroundColor Gray
    Write-Host ""
}

# Check for documents with vector
Write-Host "--- Vector Field Check ---" -ForegroundColor Yellow
$vectorCheckBody = @{
    search = "*"
    select = "id,documentId,fileName,documentVector3072"
    top = 1
} | ConvertTo-Json

$vectorResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $vectorCheckBody

if ($vectorResult.value.Count -gt 0) {
    $doc = $vectorResult.value[0]
    $vectorLength = if ($doc.documentVector3072) { $doc.documentVector3072.Count } else { 0 }
    Write-Host "  Sample document: $($doc.fileName)" -ForegroundColor White
    Write-Host "  DocumentVector3072 length: $vectorLength" -ForegroundColor $(if ($vectorLength -eq 3072) { "Green" } elseif ($vectorLength -gt 0) { "Yellow" } else { "Red" })

    if ($vectorLength -eq 0) {
        Write-Host "  WARNING: documentVector3072 is EMPTY - semantic search will not work!" -ForegroundColor Red
    } elseif ($vectorLength -ne 3072) {
        Write-Host "  WARNING: Expected 3072 dimensions, got $vectorLength" -ForegroundColor Yellow
    }
} else {
    Write-Host "  No documents found" -ForegroundColor Red
}

Write-Host ""
Write-Host "--- Summary ---" -ForegroundColor Yellow
Write-Host "Use one of the tenantId values above when configuring the PCF control." -ForegroundColor Cyan
Write-Host "The tenantId must match exactly for semantic search to work." -ForegroundColor Cyan
Write-Host ""
