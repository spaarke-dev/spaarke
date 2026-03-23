# Deploy SPE Admin App Web Resource to Dataverse
# Builds the Vite app (single universal build) and uploads sprk_speadmin.
# Creates the web resource if it does not exist, updates it if it does.
#
# The BFF URL is resolved at RUNTIME via Dataverse environment detection
# (ENVIRONMENT_BFF_MAP in bffConfig.ts), so a single build works in dev + prod.
#
# SYNOPSIS
#   Deploy-SpeAdminApp.ps1 [-Environment <dev|prod>]
# PARAMETERS
#   -Environment  Target Dataverse org: 'dev' (default) or 'prod'
# DEPENDENCIES
#   Azure CLI (az login), Node.js/npm
# USAGE
#   .\scripts\Deploy-SpeAdminApp.ps1           # dev (default)
#   .\scripts\Deploy-SpeAdminApp.ps1 -Environment dev
#   .\scripts\Deploy-SpeAdminApp.ps1 -Environment prod
param(
    [ValidateSet('dev', 'prod')]
    [string]$Environment = 'dev',
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

$ErrorActionPreference = 'Stop'

# -- Environment config -------------------------------------------------------
if ($DataverseUrl) {
    $orgUrl = $DataverseUrl
} elseif ($Environment -eq 'prod') {
    $orgUrl = 'https://spaarke-demo.crm.dynamics.com'
} else {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

Write-Host '====================================='
Write-Host 'SPE Admin App Web Resource Deployment'
Write-Host "Environment : $Environment  ($orgUrl)"
Write-Host '====================================='

# -- Step 1: Build ------------------------------------------------------------
$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot    = Split-Path -Parent $scriptDir
$appDir      = Join-Path $repoRoot 'src\solutions\SpeAdminApp'
$filePath    = Join-Path $appDir 'dist\speadmin.html'

Write-Host '[1/5] Building SPE Admin App...'
Write-Host "      Universal build - BFF URL resolved at runtime via Dataverse env detection"
Push-Location $appDir
try {
    npx vite build
    if ($LASTEXITCODE -ne 0) { throw "vite build failed (exit $LASTEXITCODE)" }
    # Rename dist/index.html -> dist/speadmin.html
    if (Test-Path 'dist\index.html') {
        if (Test-Path 'dist\speadmin.html') { Remove-Item 'dist\speadmin.html' -Force }
        Rename-Item 'dist\index.html' 'speadmin.html'
    }
} finally {
    Pop-Location
}
Write-Host '      Build complete' -ForegroundColor Green

# -- Step 2: Verify artifact --------------------------------------------------
Write-Host '[2/5] Verifying build artifact...'
if (-not (Test-Path $filePath)) {
    Write-Host "Error: Build artifact not found: $filePath" -ForegroundColor Red
    exit 1
}
$fileBytes  = [System.IO.File]::ReadAllBytes($filePath)
$fileSizeKb = [math]::Round($fileBytes.Length / 1KB)
Write-Host "      Found: speadmin.html ($($fileSizeKb) KB)" -ForegroundColor Green

# -- Step 3: Auth -------------------------------------------------------------
Write-Host '[3/5] Getting access token...'
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host 'Error: Failed to get access token' -ForegroundColor Red
    exit 1
}
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl  = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}
$fileContent = [Convert]::ToBase64String($fileBytes)

# -- Step 4: Create or update web resource -----------------------------------
$wrName     = 'sprk_speadmin'
Write-Host '[4/5] Checking for existing sprk_speadmin web resource...'
$searchUrl      = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -eq 0) {
    Write-Host '      Not found - creating new web resource...' -ForegroundColor Yellow
    $createBody = @{
        name            = $wrName
        displayname     = 'SPE Admin App'
        description     = 'SharePoint Embedded administration application.'
        webresourcetype = 1
        content         = $fileContent
    } | ConvertTo-Json -Depth 5

    $createHeaders         = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'
    $createResponse        = Invoke-WebRequest -Uri "$apiUrl/webresourceset" -Headers $createHeaders -Method Post -Body $createBody -UseBasicParsing
    $webResourceId         = ($createResponse.Content | ConvertFrom-Json).webresourceid
    Write-Host "      Created: $webResourceId" -ForegroundColor Green
} else {
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found existing: $webResourceId" -ForegroundColor Green

    Write-Host '[4/5] Updating web resource content...'
    Invoke-RestMethod -Uri "$apiUrl/webresourceset($webResourceId)" -Headers $headers -Method Patch `
        -Body (@{ content = $fileContent } | ConvertTo-Json -Depth 5) | Out-Null
    Write-Host '      Updated' -ForegroundColor Green
}

# -- Step 5: Publish ----------------------------------------------------------
Write-Host '[5/5] Publishing customizations...'
$publishXml  = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post `
    -Body (@{ ParameterXml = $publishXml } | ConvertTo-Json) | Out-Null
Write-Host '      Published' -ForegroundColor Green

Write-Host '====================================='
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host '====================================='
Write-Host "Environment   : $Environment"
Write-Host "Org URL       : $orgUrl"
Write-Host "Web Resource  : $wrName"
Write-Host "Web Resource  ID: $webResourceId"
Write-Host "Bundle Size   : $fileSizeKb KB"
Write-Host ''
Write-Host 'Access URL (relative to Dataverse org):'
Write-Host "  /WebResources/$wrName" -ForegroundColor Cyan
