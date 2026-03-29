# Deploy SmartTodo Web Resource to Dataverse
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl

Write-Host '====================================='
Write-Host 'Smart To Do Web Resource Deployment'
Write-Host '====================================='

# Get access token
Write-Host '[1/5] Getting access token...'
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host 'Error: Failed to get access token' -ForegroundColor Red
    exit 1
}
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization' = "Bearer $accessToken"
    'Content-Type' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
    'Accept' = 'application/json'
}

# Build the project
Write-Host '[2/5] Building SmartTodo...'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$projectDir = Join-Path $repoRoot 'src\solutions\SmartTodo'

Push-Location $projectDir
try {
    npm run build
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Error: npm run build failed' -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}
Write-Host '      Build complete' -ForegroundColor Green

# Read and base64-encode dist/index.html
$filePath = Join-Path $projectDir 'dist\index.html'
if (-not (Test-Path $filePath)) {
    Write-Host "Build file not found: $filePath" -ForegroundColor Red
    exit 1
}

$fileBytes = [System.IO.File]::ReadAllBytes($filePath)
$fileContent = [Convert]::ToBase64String($fileBytes)

# Find existing web resource
Write-Host '[3/5] Finding sprk_smarttodo web resource...'
$wrName = 'sprk_smarttodo'
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
$existingResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($existingResponse.value.Count -gt 0) {
    # Update existing web resource
    $webResourceId = $existingResponse.value[0].webresourceid
    Write-Host "      Found: $webResourceId" -ForegroundColor Green

    Write-Host '[4/5] Updating web resource...'
    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $fileContent } | ConvertTo-Json
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
    Write-Host '      Updated' -ForegroundColor Green
} else {
    # Create new web resource
    Write-Host '      Not found, creating new web resource...' -ForegroundColor Yellow

    Write-Host '[4/5] Creating web resource...'
    $createBody = @{
        name = $wrName
        displayname = 'Smart To Do'
        webresourcetype = 1  # Webpage (HTML)
        content = $fileContent
    } | ConvertTo-Json
    $createResponse = Invoke-RestMethod -Uri "$apiUrl/webresourceset" -Headers $headers -Method Post -Body $createBody
    $webResourceId = $createResponse.webresourceid
    Write-Host "      Created: $webResourceId" -ForegroundColor Green
}

# Publish
Write-Host '[5/5] Publishing customizations...'
$publishUrl = "$apiUrl/PublishXml"
$publishXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host '      Published' -ForegroundColor Green

Write-Host '====================================='
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host '====================================='
$fileSize = [math]::Round((Get-Item $filePath).Length / 1KB)
Write-Host "Bundle Size: $fileSize KB"
