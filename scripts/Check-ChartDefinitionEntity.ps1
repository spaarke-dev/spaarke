# Check sprk_chartdefinition entity attributes
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$token = az account get-access-token --resource $DataverseUrl --query "accessToken" -o tsv
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
}

$apiBase = "$DataverseUrl/api/data/v9.2"

Write-Host "Checking sprk_chartdefinition entity attributes..." -ForegroundColor Cyan

try {
    $uri = "$apiBase/EntityDefinitions(LogicalName='sprk_chartdefinition')/Attributes"
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    Write-Host "Found $($result.value.Count) attributes:" -ForegroundColor Green
    $result.value | ForEach-Object { Write-Host "  - $($_.LogicalName)" }
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Also check global option sets
Write-Host ""
Write-Host "Checking global option sets..." -ForegroundColor Cyan
try {
    $uri = "$apiBase/GlobalOptionSetDefinitions(Name='sprk_visualtype')"
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    Write-Host "  sprk_visualtype exists - MetadataId: $($result.MetadataId)" -ForegroundColor Green
} catch {
    Write-Host "  sprk_visualtype not found" -ForegroundColor Yellow
}

try {
    $uri = "$apiBase/GlobalOptionSetDefinitions(Name='sprk_aggregationtype')"
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    Write-Host "  sprk_aggregationtype exists - MetadataId: $($result.MetadataId)" -ForegroundColor Green
} catch {
    Write-Host "  sprk_aggregationtype not found" -ForegroundColor Yellow
}
