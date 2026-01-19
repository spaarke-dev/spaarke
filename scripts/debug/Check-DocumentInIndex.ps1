# Check if a specific document is in the AI Search index
param(
    [Parameter(Mandatory = $true)]
    [string]$DocumentId,

    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-knowledge-index-v2",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "spe-infrastructure-westus2"
)

$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key"
    exit 1
}

$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

# Search for the specific document by documentId
$searchBody = @{
    search = "*"
    filter = "documentId eq '$DocumentId'"
    select = "id,documentId,fileName,tenantId,documentVector3072"
    top = 5
} | ConvertTo-Json

$searchUrl = "https://$SearchServiceName.search.windows.net/indexes/$IndexName/docs/search?api-version=2024-07-01"
$result = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $searchBody

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Document Index Check" -ForegroundColor Cyan
Write-Host " DocumentId: $DocumentId" -ForegroundColor Cyan
Write-Host " Index: $IndexName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($result.value.Count -gt 0) {
    Write-Host "STATUS: FOUND in AI Search index" -ForegroundColor Green
    foreach ($doc in $result.value) {
        $vectorLength = if ($doc.documentVector3072) { $doc.documentVector3072.Count } else { 0 }
        Write-Host "  FileName: $($doc.fileName)"
        Write-Host "  TenantId: $($doc.tenantId)"
        Write-Host "  VectorLength: $vectorLength" -ForegroundColor $(if ($vectorLength -eq 3072) { "Green" } else { "Red" })
    }
} else {
    Write-Host "STATUS: NOT FOUND in AI Search index" -ForegroundColor Red
    Write-Host ""
    Write-Host "This document needs to be indexed for semantic search to work." -ForegroundColor Yellow
    Write-Host "Run the RAG indexing job to index this document." -ForegroundColor Yellow
}
