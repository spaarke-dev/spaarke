# List all indexes in Azure AI Search

$SearchServiceName = "spaarke-search-dev"
$ResourceGroup = "spe-infrastructure-westus2"

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

$indexListUrl = "https://$SearchServiceName.search.windows.net/indexes?api-version=2024-07-01"
$indexes = Invoke-RestMethod -Uri $indexListUrl -Headers $headers

Write-Host "Indexes in $SearchServiceName :" -ForegroundColor Cyan
Write-Host ""

foreach ($index in $indexes.value) {
    # Get document count for each index
    $countUrl = "https://$SearchServiceName.search.windows.net/indexes/$($index.name)/docs/`$count?api-version=2024-07-01"
    try {
        $count = Invoke-RestMethod -Uri $countUrl -Headers $headers -Method GET
        Write-Host "  $($index.name): $count documents" -ForegroundColor $(if ($count -gt 0) { "Green" } else { "Yellow" })
    } catch {
        Write-Host "  $($index.name): ERROR getting count" -ForegroundColor Red
    }
}
