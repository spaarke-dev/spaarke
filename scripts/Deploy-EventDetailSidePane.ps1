# Deploy EventDetailSidePane Web Resource to Dataverse
$ErrorActionPreference = 'Stop'
$orgUrl = 'https://spaarkedev1.crm.dynamics.com'

Write-Host '====================================='
Write-Host 'EventDetailSidePane Deployment'
Write-Host '====================================='

# Get access token
Write-Host '[1/4] Getting access token...'
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

# Find sidepane web resources
Write-Host '[2/4] Finding sidepane web resources...'
$searchUrl = "$apiUrl/webresourceset?`$filter=contains(name,'sidepane')&`$select=name,webresourceid"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($response.value.Count -eq 0) {
    Write-Host 'No sidepane web resources found' -ForegroundColor Yellow

    # Try looking for EventDetail
    $searchUrl2 = "$apiUrl/webresourceset?`$filter=contains(name,'eventdetail')&`$select=name,webresourceid"
    $response = Invoke-RestMethod -Uri $searchUrl2 -Headers $headers -Method Get
}

if ($response.value.Count -eq 0) {
    Write-Host 'No EventDetail web resources found either' -ForegroundColor Red
    exit 1
}

foreach ($wr in $response.value) {
    Write-Host "      Found: $($wr.name)" -ForegroundColor Cyan
}

# Use the first one or find the correct one
$webResourceId = $null
$webResourceName = $null
foreach ($wr in $response.value) {
    if ($wr.name -match 'sprk_eventdetailsidepane' -or $wr.name -match 'sidepane\.html') {
        $webResourceId = $wr.webresourceid
        $webResourceName = $wr.name
        break
    }
}

if (-not $webResourceId -and $response.value.Count -gt 0) {
    $webResourceId = $response.value[0].webresourceid
    $webResourceName = $response.value[0].name
}

if (-not $webResourceId) {
    Write-Host 'Could not find EventDetailSidePane web resource' -ForegroundColor Red
    exit 1
}

Write-Host "      Using: $webResourceName ($webResourceId)" -ForegroundColor Green

# Update
Write-Host '[3/4] Updating web resource...'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$filePath = Join-Path $repoRoot 'src\solutions\EventDetailSidePane\dist\index.html'

if (-not (Test-Path $filePath)) {
    Write-Host "Build file not found: $filePath" -ForegroundColor Red
    exit 1
}

$fileBytes = [System.IO.File]::ReadAllBytes($filePath)
$fileContent = [Convert]::ToBase64String($fileBytes)

$updateUrl = "$apiUrl/webresourceset($webResourceId)"
$updateBody = @{ content = $fileContent } | ConvertTo-Json
Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
Write-Host '      Updated' -ForegroundColor Green

# Publish
Write-Host '[4/4] Publishing...'
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
