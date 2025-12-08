# Deploy sprk_ThemeMenu.js Web Resource to Dataverse

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Theme Menu JS Web Resource Deployment" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$webResourceName = "sprk_ThemeMenu.js"
$jsFilePath = "C:\code_files\spaarke\src\client\webresources\js\sprk_ThemeMenu.js"

Write-Host "[1/4] Using Dataverse environment: $orgUrl" -ForegroundColor Green
Write-Host ""

# Get access token via Azure CLI
Write-Host "[2/4] Getting access token via Azure CLI..."
$accessToken = (& az account get-access-token --resource $orgUrl/ --query accessToken -o tsv 2>$null)
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "      Token acquired" -ForegroundColor Green
Write-Host ""

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Accept" = "application/json"
}

# Read and encode JS file
Write-Host "[3/4] Reading JS file..."
if (!(Test-Path $jsFilePath)) {
    Write-Host "Error: File not found: $jsFilePath" -ForegroundColor Red
    exit 1
}
$jsBytes = [System.IO.File]::ReadAllBytes($jsFilePath)
$jsContent = [Convert]::ToBase64String($jsBytes)
Write-Host "      File read ($($jsBytes.Length) bytes)" -ForegroundColor Green
Write-Host ""

# Find existing web resource
Write-Host "[4/4] Deploying $webResourceName..."
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$webResourceName'"
try {
    $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
} catch {
    Write-Host "      API error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($searchResponse.value.Count -gt 0) {
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found existing resource: $webResourceId" -ForegroundColor Gray
    Write-Host "      Updating..." -ForegroundColor Yellow

    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $jsContent } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
        Write-Host "      Updated successfully" -ForegroundColor Green
    } catch {
        Write-Host "      Update failed: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    # Publish
    Write-Host "      Publishing..."
    $publishUrl = "$apiUrl/PublishXml"
    $publishXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
    $publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
        Write-Host "      Published" -ForegroundColor Green
    } catch {
        Write-Host "      Publish failed: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "      Web resource not found: $webResourceName" -ForegroundColor Red
    Write-Host "      You may need to create it first in the Dataverse solution" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Deployment Complete" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
