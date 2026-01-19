# Query documents by tenant ID
param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,

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

$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

$searchBody = @{
    search = "*"
    filter = "tenantId eq '$TenantId'"
    select = "id,documentId,fileName,tenantId,fileType"
    top = 20
} | ConvertTo-Json

$searchUrl = "https://$SearchServiceName.search.windows.net/indexes/$IndexName/docs/search?api-version=2024-07-01"
$result = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $searchBody

Write-Host ""
Write-Host "Documents for TenantId: $TenantId" -ForegroundColor Cyan
Write-Host "Found: $($result.value.Count) documents" -ForegroundColor Green
Write-Host ""

foreach ($doc in $result.value) {
    Write-Host "  $($doc.fileName)" -ForegroundColor White
    Write-Host "    DocumentId: $($doc.documentId)" -ForegroundColor Gray
    Write-Host "    FileType: $($doc.fileType)" -ForegroundColor Gray
    Write-Host ""
}
