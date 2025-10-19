# Deploy PCF Web Resources to Dataverse

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "PCF Web Resources Deployment" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Hardcoded org URL
$orgUrl = "https://spaarkedev1.crm.dynamics.com"
Write-Host "[1/5] Using Dataverse environment..."
Write-Host "      $orgUrl" -ForegroundColor Green
Write-Host ""

# Get access token
Write-Host "[2/5] Getting access token..."
$accessToken = (& pac auth token).Trim()
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
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

# Deploy bundle.js
Write-Host "[3/5] Deploying bundle.js..."
$bundlePath = "C:\code_files\spaarke\src\controls\UniversalQuickCreate\out\controls\UniversalQuickCreate\bundle.js"
$bundleBytes = [System.IO.File]::ReadAllBytes($bundlePath)
$bundleContent = [Convert]::ToBase64String($bundleBytes)
$bundleName = "sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js"

# Check if exists
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$bundleName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -gt 0) {
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Updating existing bundle.js ($webResourceId)..." -ForegroundColor Yellow

    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $bundleContent } | ConvertTo-Json
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
    Write-Host "      ✓ bundle.js updated" -ForegroundColor Green
} else {
    Write-Host "      Creating new bundle.js..." -ForegroundColor Yellow

    $createUrl = "$apiUrl/webresourceset"
    $createBody = @{
        name = $bundleName
        displayname = "Universal Document Upload - Bundle"
        webresourcetype = 3
        content = $bundleContent
    } | ConvertTo-Json

    $createResponse = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $createBody
    $webResourceId = $createResponse.webresourceid
    Write-Host "      ✓ bundle.js created ($webResourceId)" -ForegroundColor Green
}

$bundleId = $webResourceId
Write-Host ""

# Deploy CSS
Write-Host "[4/5] Deploying CSS..."
$cssPath = "C:\code_files\spaarke\src\controls\UniversalQuickCreate\out\controls\UniversalQuickCreate\css\UniversalQuickCreate.css"
$cssBytes = [System.IO.File]::ReadAllBytes($cssPath)
$cssContent = [Convert]::ToBase64String($cssBytes)
$cssName = "sprk_Spaarke.Controls.UniversalDocumentUpload/css/UniversalQuickCreate.css"

$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$cssName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -gt 0) {
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Updating existing CSS ($webResourceId)..." -ForegroundColor Yellow

    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $cssContent } | ConvertTo-Json
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
    Write-Host "      ✓ CSS updated" -ForegroundColor Green
} else {
    Write-Host "      Creating new CSS..." -ForegroundColor Yellow

    $createUrl = "$apiUrl/webresourceset"
    $createBody = @{
        name = $cssName
        displayname = "Universal Document Upload - CSS"
        webresourcetype = 2
        content = $cssContent
    } | ConvertTo-Json

    $createResponse = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $createBody
    $webResourceId = $createResponse.webresourceid
    Write-Host "      ✓ CSS created ($webResourceId)" -ForegroundColor Green
}

$cssId = $webResourceId
Write-Host ""

# Publish
Write-Host "[5/5] Publishing web resources..."
$publishUrl = "$apiUrl/PublishXml"
$publishXml = "<importexportxml><webresources><webresource>{$bundleId}</webresource><webresource>{$cssId}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json

Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host "      ✓ Published" -ForegroundColor Green
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "✓ Deployment Complete" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Web Resources Deployed:"
Write-Host "  - sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js"
Write-Host "  - sprk_Spaarke.Controls.UniversalDocumentUpload/css/UniversalQuickCreate.css"
Write-Host ""
