# Publish all theme-related web resources

param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$orgUrl = $DataverseUrl
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
. (Join-Path $PSScriptRoot "lib" "Publish-SolutionComponents.ps1")

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

# Publish exactly these web resources (shared scoped-publish module).
try {
    Invoke-PublishXml -Context @{ Api = $apiUrl; Headers = $headers } -ParameterXml (New-PublishParameterXml -WebResources $webResourceIds)
    Write-Host "Published successfully!" -ForegroundColor Green
} catch {
    Write-Host "Error publishing: $_" -ForegroundColor Red
    exit 1
}

# Task 130 (D-83): only the web resources above are published. The theme menu ribbon needs no publish of its own here.

Write-Host ""
Write-Host "Done! Please hard-refresh the browser (Ctrl+F5) to see the icons."
