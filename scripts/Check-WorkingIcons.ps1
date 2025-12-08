# Check what icon formats are used by working ribbon buttons

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Checking Icon Web Resources by Type"
Write-Host "====================================="

# Get all sprk_ web resources with image types
$searchUrl = "$apiUrl/webresourceset?`$filter=startswith(name,'sprk_') and (webresourcetype eq 5 or webresourcetype eq 6 or webresourcetype eq 7 or webresourcetype eq 10 or webresourcetype eq 11)&`$select=name,webresourcetype&`$orderby=name"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

$typeNames = @{
    5 = "PNG"; 6 = "JPG"; 7 = "GIF"; 10 = "ICO"; 11 = "SVG"
}

Write-Host ""
Write-Host "Found $($response.value.Count) image web resources:"
Write-Host ""

foreach ($wr in $response.value) {
    $typeName = $typeNames[$wr.webresourcetype]
    Write-Host "  $($wr.name) [$typeName]"
}

Write-Host ""
Write-Host "====================================="
Write-Host "Checking if SVG icons are accessible via URL"
Write-Host "====================================="

# Try to fetch one of the icons directly
$testIconUrl = "$orgUrl/WebResources/sprk_ThemeMenu16.svg"
Write-Host "Testing: $testIconUrl"
try {
    $testHeaders = @{ "Authorization" = "Bearer $accessToken" }
    $testResponse = Invoke-WebRequest -Uri $testIconUrl -Headers $testHeaders -Method Get
    Write-Host "Status: $($testResponse.StatusCode)"
    Write-Host "Content-Type: $($testResponse.Headers['Content-Type'])"
    Write-Host "Content length: $($testResponse.Content.Length) bytes"
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
