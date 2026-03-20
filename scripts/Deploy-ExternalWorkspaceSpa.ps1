# Deploy External Workspace SPA as a Dataverse Web Resource
# Follows the same pattern as Deploy-CorporateWorkspace.ps1
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl
$webResourceName = 'sprk_externalworkspace'
$webResourceDisplayName = 'Spaarke External Workspace SPA'

Write-Host '=====================================' -ForegroundColor Cyan
Write-Host 'External Workspace SPA Deployment' -ForegroundColor Cyan
Write-Host '=====================================' -ForegroundColor Cyan

# Get access token
Write-Host '[1/5] Getting access token...' -ForegroundColor Yellow
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host 'Error: Failed to get access token. Run: az login' -ForegroundColor Red
    exit 1
}
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization'  = "Bearer $accessToken"
    'Content-Type'   = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'  = '4.0'
    'Accept'         = 'application/json'
}

# Read dist/index.html
Write-Host '[2/5] Reading dist/index.html...' -ForegroundColor Yellow
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$distPath = Join-Path $repoRoot 'src\client\external-spa\dist\index.html'
if (-not (Test-Path $distPath)) {
    Write-Host "dist/index.html not found. Run: cd src/client/external-spa && npm run build" -ForegroundColor Red
    exit 1
}
$fileBytes = [System.IO.File]::ReadAllBytes($distPath)
$fileContent = [Convert]::ToBase64String($fileBytes)
$fileSizeKb = [math]::Round($fileBytes.Length / 1KB)
Write-Host "      Read $fileSizeKb KB" -ForegroundColor Green

# Check if web resource exists
Write-Host "[3/5] Checking for existing '$webResourceName'..." -ForegroundColor Yellow
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$webResourceName'&`$select=webresourceid,name"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -gt 0) {
    # UPDATE existing
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found: $webResourceId - updating..." -ForegroundColor Green

    Write-Host '[4/5] Updating web resource...' -ForegroundColor Yellow
    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $fileContent } | ConvertTo-Json -Depth 2
    $updateBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($updateBody)
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBodyBytes | Out-Null
    Write-Host '      Updated' -ForegroundColor Green
} else {
    # CREATE new
    Write-Host '      Not found - creating new web resource...' -ForegroundColor Yellow

    Write-Host '[4/5] Creating web resource...' -ForegroundColor Yellow
    $createBody = @{
        name            = $webResourceName
        displayname     = $webResourceDisplayName
        description     = 'Spaarke Secure Project External Workspace — React 18 SPA (Vite + Fluent UI v9)'
        webresourcetype = 1       # 1 = Webpage (HTML)
        content         = $fileContent
        languagecode    = 1033    # English
        isenabledformobileclient = $false
    } | ConvertTo-Json -Depth 2

    $createHeaders = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'
    $createUrl = "$apiUrl/webresourceset"
    $createBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($createBody)
    $created = Invoke-RestMethod -Uri $createUrl -Headers $createHeaders -Method Post -Body $createBodyBytes
    $webResourceId = $created.webresourceid
    Write-Host "      Created: $webResourceId" -ForegroundColor Green
}

# Publish
Write-Host '[5/5] Publishing web resource...' -ForegroundColor Yellow
$publishUrl = "$apiUrl/PublishXml"
$publishXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host '      Published' -ForegroundColor Green

Write-Host ''
Write-Host '=====================================' -ForegroundColor Cyan
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host '=====================================' -ForegroundColor Cyan
Write-Host "Web Resource : $webResourceName"
Write-Host "ID           : $webResourceId"
Write-Host "Bundle Size  : $fileSizeKb KB"
Write-Host "URL          : $orgUrl/WebResources/$webResourceName"
Write-Host ''
Write-Host 'Access the SPA via Dynamics model-driven app or direct URL above.' -ForegroundColor Gray
