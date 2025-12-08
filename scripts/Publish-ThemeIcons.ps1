# Publish all theme-related web resources

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Publishing Theme Web Resources"
Write-Host "====================================="

$iconNames = @(
    "sprk_ThemeMenu.js",
    "sprk_ThemeMenu16.svg",
    "sprk_ThemeMenu32.svg",
    "sprk_ThemeAuto16.svg",
    "sprk_ThemeLight16.svg",
    "sprk_ThemeDark16.svg"
)

$webResourceIds = @()

foreach ($iconName in $iconNames) {
    Write-Host "Looking up: $iconName"
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$iconName'&`$select=webresourceid,name"
    $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($response.value.Count -gt 0) {
        $id = $response.value[0].webresourceid
        Write-Host "  Found: $id"
        $webResourceIds += $id
    } else {
        Write-Host "  NOT FOUND" -ForegroundColor Yellow
    }
}

if ($webResourceIds.Count -eq 0) {
    Write-Host "No web resources found to publish" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Publishing $($webResourceIds.Count) web resources..."

# Build publish XML
$webResourcesXml = ""
foreach ($id in $webResourceIds) {
    $webResourcesXml += "<webresource>{$id}</webresource>"
}

$publishXml = "<importexportxml><webresources>$webResourcesXml</webresources></importexportxml>"

$publishUrl = "$apiUrl/PublishXml"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json

try {
    Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
    Write-Host "Published successfully!" -ForegroundColor Green
} catch {
    Write-Host "Error publishing: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "====================================="
Write-Host "Now publishing all ribbon customizations..."
Write-Host "====================================="

# Also publish ribbon customizations
$publishAllUrl = "$apiUrl/PublishAllXml"
try {
    Invoke-RestMethod -Uri $publishAllUrl -Headers $headers -Method Post | Out-Null
    Write-Host "All customizations published!" -ForegroundColor Green
} catch {
    Write-Host "Error publishing all: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "Done! Please hard-refresh the browser (Ctrl+F5) to see the icons."
