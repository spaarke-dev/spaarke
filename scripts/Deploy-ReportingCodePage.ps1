# Deploy Reporting Code Page as a Dataverse Web Resource
# Builds the Vite single-file bundle and uploads sprk_reporting to Dataverse.
# The Reporting Code Page uses vite-plugin-singlefile, so dist/index.html is
# already fully self-contained (no external JS or CSS references).
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$WhatIf
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl.TrimEnd('/')
$webResourceName = 'sprk_reporting'
$webResourceDisplayName = 'Spaarke Reporting'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$reportingDir = Join-Path $repoRoot 'src\solutions\Reporting'
$distPath = Join-Path $reportingDir 'dist\index.html'

Write-Host '=====================================' -ForegroundColor Cyan
Write-Host 'Reporting Code Page Deployment' -ForegroundColor Cyan
Write-Host '=====================================' -ForegroundColor Cyan

if ($WhatIf) {
    Write-Host '[WhatIf] Would build src/solutions/Reporting/ and upload sprk_reporting web resource.' -ForegroundColor Yellow
    Write-Host "[WhatIf] Target org: $orgUrl" -ForegroundColor Yellow
    exit 0
}

# Step 1: npm install
Write-Host '[1/6] Installing npm dependencies...' -ForegroundColor Yellow
Push-Location $reportingDir
try {
    npm install --prefer-offline 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Error: npm install failed.' -ForegroundColor Red
        exit 1
    }
    Write-Host '      Dependencies installed' -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 2: npm run build
Write-Host '[2/6] Building Reporting Code Page (vite build)...' -ForegroundColor Yellow
Push-Location $reportingDir
try {
    npm run build 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Error: npm run build failed.' -ForegroundColor Red
        exit 1
    }
    Write-Host '      Build complete' -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 3: verify dist/index.html exists and is single-file
Write-Host '[3/6] Verifying dist/index.html...' -ForegroundColor Yellow
if (-not (Test-Path $distPath)) {
    Write-Host "Error: dist/index.html not found at $distPath" -ForegroundColor Red
    Write-Host '       Run: cd src/solutions/Reporting && npm run build' -ForegroundColor Red
    exit 1
}

$htmlContent = [System.IO.File]::ReadAllText($distPath, [System.Text.Encoding]::UTF8)

# Sanity check: vite-plugin-singlefile should have inlined everything.
# Warn if there are external src= references pointing outside the HTML.
if ($htmlContent -match '<script[^>]+src="(?!data:)[^"]*"[^>]*>' -or
    $htmlContent -match '<link[^>]+href="(?!data:)[^"]*\.css"[^>]*>') {
    Write-Host '      WARNING: dist/index.html may contain external asset references.' -ForegroundColor Yellow
    Write-Host '               Ensure vite-plugin-singlefile is configured correctly.' -ForegroundColor Yellow
} else {
    Write-Host '      Verified: single-file bundle (no external asset references)' -ForegroundColor Green
}

$fileBytes = [System.Text.Encoding]::UTF8.GetBytes($htmlContent)
$fileContent = [Convert]::ToBase64String($fileBytes)
$fileSizeKb = [math]::Round($fileBytes.Length / 1KB)
Write-Host "      Bundle size: $fileSizeKb KB" -ForegroundColor Green

# Step 4: get access token
Write-Host '[4/6] Getting access token...' -ForegroundColor Yellow
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host 'Error: Failed to get access token. Run: az login' -ForegroundColor Red
    exit 1
}
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

# Step 5: create or update web resource
Write-Host "[5/6] Checking for existing '$webResourceName'..." -ForegroundColor Yellow
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$webResourceName'&`$select=webresourceid,name"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -gt 0) {
    # UPDATE existing
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found: $webResourceId - updating..." -ForegroundColor Green

    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $fileContent } | ConvertTo-Json -Depth 2
    $updateBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($updateBody)
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBodyBytes | Out-Null
    Write-Host '      Updated' -ForegroundColor Green
} else {
    # CREATE new
    Write-Host '      Not found - creating new web resource...' -ForegroundColor Yellow

    $createBody = @{
        name                     = $webResourceName
        displayname              = $webResourceDisplayName
        description              = 'Spaarke Reporting Code Page - React 19 SPA (Vite + vite-plugin-singlefile + Fluent UI v9)'
        webresourcetype          = 1       # 1 = Webpage (HTML)
        content                  = $fileContent
        languagecode             = 1033    # English
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

# Step 6: publish
Write-Host '[6/6] Publishing web resource...' -ForegroundColor Yellow
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
Write-Host 'Open the Reporting Code Page via a model-driven app or the URL above.' -ForegroundColor Gray
