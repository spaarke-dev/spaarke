# List all theme-related web resources in Dataverse

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Theme-Related Web Resources"
Write-Host "====================================="

# Search for theme-related web resources
$searchUrl = "$apiUrl/webresourceset?`$filter=contains(name,'Theme') or contains(name,'theme')&`$select=name,displayname,webresourcetype&`$orderby=name"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

$typeNames = @{
    1 = "HTML"; 2 = "CSS"; 3 = "JS"; 4 = "XML"; 5 = "PNG"
    6 = "JPG"; 7 = "GIF"; 8 = "XAP"; 9 = "XSL"; 10 = "ICO"
    11 = "SVG"; 12 = "RESX"
}

foreach ($wr in $response.value) {
    $typeName = $typeNames[$wr.webresourcetype]
    if (-not $typeName) { $typeName = "Unknown" }
    Write-Host "$($wr.name) [$typeName]"
}

Write-Host ""
Write-Host "Total: $($response.value.Count) web resources"
